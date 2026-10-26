using System.Globalization;
using System.Text.Json;
using Goldmark.Api.Data;

namespace Goldmark.Api.Inventory;

/// <summary>A CI on screen 6. <c>Grouped</c>: reached only as a member of a group, so the graph draws its group instead.</summary>
internal sealed record GraphNode(Guid Id, string Name, string Type, string LifecycleState, int Hops, bool Grouped);

/// <summary>A relationship on screen 6: its hops are its farther end's; <c>Group</c> names the group it leads into, if any.</summary>
internal sealed record GraphRelationship(Guid Id, string Type, Guid From, Guid To, string? RedundancyGroup, int Hops, string? Group);

/// <summary>
/// More than <see cref="GraphQuery.GroupAbove"/> neighbours of one CI (<c>Hub</c>) sharing the walk's direction, the
/// relationship type and the CI type, drawn as one counted node (DEC-046 item 5); the walk doesn't go past them.
/// </summary>
internal sealed record GraphGroup(string Id, Guid Hub, string Direction, string RelationshipType, string CiType, int Hops, int Count,
    IReadOnlyList<Guid> Members);

/// <summary>
/// Screen 6's graph (US-40; DEC-046 items 4 and 5): every CI and relationship the table view lists, the groups,
/// how many things the graph draws at each hop (a CI drawn on its own, or a group, counts one; index 0 is the focus
/// CI), and whether the walk stopped at <see cref="GraphStore.MaxRelationships"/> before the depth asked for.
/// </summary>
internal sealed record GraphResult(Guid Focus, int Depth, string Direction, IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphRelationship> Relationships, IReadOnlyList<GraphGroup> Groups, IReadOnlyList<int> CountsByHop, bool Truncated);

/// <summary>
/// The graph read's parameters (screen 6's address, <c>docs/integration/deep-links.md</c>): the focus CI, the depth
/// (1 to 6, 3 by default; item 10), the direction, the CI types and relationship types shown (all when none is
/// given), retired CIs (left out by default) and grouping (on by default; item 5's switch).
/// </summary>
internal sealed record GraphQuery(Guid Ci, int Depth, string Direction, IReadOnlyList<string> Types, IReadOnlyList<string> RelationshipTypes,
    bool Retired, bool Group)
{
    public const int DefaultDepth = 3;
    public const int MaxDepth = 6;

    /// <summary>More neighbours than this, of one CI with one direction, relationship type and CI type, become a group.</summary>
    public const int GroupAbove = 10;

    public const string Both = "both";

    public static readonly IReadOnlyList<string> Directions = [Both, RelationshipDirections.Dependencies, RelationshipDirections.Dependents];

    public bool WalksDependencies => Direction != RelationshipDirections.Dependents;

    public bool WalksDependents => Direction != RelationshipDirections.Dependencies;

    /// <summary>The query from the request, or the field errors (each parameter named once).</summary>
    public static (GraphQuery? Query, Dictionary<string, string[]> Errors) Parse(IQueryCollection query, IReadOnlyList<CiTypeInfo> types,
        IReadOnlyList<RelationshipTypeInfo> relationshipTypes)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var ci = Guid.Empty;
        if (query["ci"].ToString() is not { Length: > 0 } c)
        {
            errors["ci"] = ["Required"];
        }
        else if (!Guid.TryParseExact(c, "D", out ci))
        {
            errors["ci"] = ["InvalidId"];
        }

        var depth = DefaultDepth;
        if (query["depth"].ToString() is { Length: > 0 } d
            && (!int.TryParse(d, NumberStyles.None, CultureInfo.InvariantCulture, out depth) || depth is < 1 or > MaxDepth))
        {
            errors["depth"] = ["InvalidDepth"];
        }

        var direction = query["direction"].ToString() is { Length: > 0 } dir ? dir : Both;
        if (!Directions.Contains(direction, StringComparer.Ordinal))
        {
            errors["direction"] = ["InvalidDirection"];
        }

        var typeNames = new List<string>();
        foreach (var name in query["type"])
        {
            if (CiTypeCatalog.Find(types, name) is { } type)
            {
                typeNames.Add(type.Name);
            }
            else
            {
                errors["type"] = ["UnknownType"];
            }
        }

        var relationshipTypeNames = new List<string>();
        foreach (var name in query["relationshipType"])
        {
            if (relationshipTypes.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) is { } relationshipType)
            {
                relationshipTypeNames.Add(relationshipType.Name);
            }
            else
            {
                errors["relationshipType"] = ["UnknownRelationshipType"];
            }
        }

        var retired = Boolean(query, "retired", false, errors);
        var group = Boolean(query, "group", true, errors);
        return errors.Count > 0
            ? (null, errors)
            : (new GraphQuery(ci, depth, direction, [.. typeNames.Distinct(StringComparer.Ordinal)],
                [.. relationshipTypeNames.Distinct(StringComparer.Ordinal)], retired, group), errors);
    }

    /// <summary>The query as an export's audit entry records it (DEC-046 item 7).</summary>
    public object Describe() => new { depth = Depth, direction = Direction, types = Types, relationshipTypes = RelationshipTypes, retired = Retired, group = Group };

    private static bool Boolean(IQueryCollection query, string name, bool fallback, Dictionary<string, string[]> errors)
    {
        switch (query[name].ToString())
        {
            case "":
                return fallback;
            case "true":
                return true;
            case "false":
                return false;
            default:
                errors[name] = ["InvalidBoolean"];
                return fallback;
        }
    }
}

/// <summary>
/// Walks the relationships around a CI for screen 6 (US-40; DEC-046 items 4 and 5), hop by hop in batched queries
/// (FR-IM-3's alternative to one recursive CTE): the dependencies walk follows <c>UQ_Relationship (FromCiId, …)</c>
/// away from the focus CI, the dependents walk <c>IX_Relationship_ToCi</c>, and neither turns round. Each walk keeps its
/// own visited set, so a CI both walks reach is expanded by each in its direction; its hops are the fewer. At each hop
/// a CI's new neighbours of one relationship type and CI type become a group when there are more than
/// <see cref="GraphQuery.GroupAbove"/> of them, before the next hop, so the walk never expands them. After the
/// dependents walk, the other members of each redundancy group it reached (same dependent, same text under
/// <see cref="RedundancyGroups"/>) come in at their true hop, one past their dependent's, and are not expanded.
/// Retired CIs (unless asked for) and CIs or relationships of types not shown are left out, and the walk doesn't pass
/// through them; the focus CI is always shown. The walk stops before a hop that would take it past
/// <see cref="MaxRelationships"/>, so every hop it returns is complete.
/// </summary>
internal sealed class GraphStore(DbSession db)
{
    /// <summary>
    /// The most relationships one read returns (and an export holds), so a hub never makes the walk unbounded: the design
    /// target's CI count (NFR-PERF-1), which a CI's relationships of one type can't pass there (owner, 2026-10-25). An
    /// estate past it needs a server-paged table view, not a larger number.
    /// </summary>
    public const int MaxRelationships = 25_000;

    private const string HopColumns = """
        SELECT TOP (@take) r.Id, r.FromCiId, r.ToCiId, r.RelationshipTypeId, r.RedundancyGroup, c.CiTypeId AS FarTypeId
        FROM cmdb.Relationship AS r
        """;

    private const string HopFilters = """

            AND (@relationshipTypes IS NULL OR r.RelationshipTypeId IN (SELECT CAST(value AS int) FROM OPENJSON(@relationshipTypes)))
            AND (@types IS NULL OR c.CiTypeId IN (SELECT CAST(value AS int) FROM OPENJSON(@types)))
            AND (@retired = 1 OR c.LifecycleState <> 'Retired')
        """;

    private const string DependenciesHopSql = HopColumns + """

        JOIN cmdb.Ci AS c ON c.Id = r.ToCiId
        WHERE r.TenantId = @tenant AND r.FromCiId IN (SELECT CAST(value AS uniqueidentifier) FROM OPENJSON(@ids))
        """ + HopFilters + ";";

    private const string DependentsHopSql = HopColumns + """

        JOIN cmdb.Ci AS c ON c.Id = r.FromCiId
        WHERE r.TenantId = @tenant AND r.ToCiId IN (SELECT CAST(value AS uniqueidentifier) FROM OPENJSON(@ids))
        """ + HopFilters + ";";

    /// <summary>The members read's page: the dependents' relationships in any redundancy group, after <c>@after</c>.</summary>
    private const int MembersPage = MaxRelationships;

    private const string MembersSql = HopColumns + """

        JOIN cmdb.Ci AS c ON c.Id = r.ToCiId
        WHERE r.TenantId = @tenant AND r.RedundancyGroup IS NOT NULL
            AND r.FromCiId IN (SELECT CAST(value AS uniqueidentifier) FROM OPENJSON(@ids))
            AND (@after IS NULL OR r.Id > @after)
        """ + HopFilters + """

        ORDER BY r.Id;
        """;

    private const string NodesSql = """
        SELECT c.Id, c.Name, c.CiTypeId, c.LifecycleState
        FROM cmdb.Ci AS c
        WHERE c.TenantId = @tenant AND c.Id IN (SELECT CAST(value AS uniqueidentifier) FROM OPENJSON(@ids));
        """;

    /// <summary>The graph, or null when the focus CI doesn't exist.</summary>
    public async Task<GraphResult?> WalkAsync(GraphQuery query, IReadOnlyList<CiTypeInfo> types, IReadOnlyList<RelationshipTypeInfo> relationshipTypes)
    {
        var filter = new Filter(
            query.Types.Count == 0 ? null : JsonSerializer.Serialize(query.Types.Select(n => types.First(t => t.Name == n).Id)),
            query.RelationshipTypes.Count == 0 ? null : JsonSerializer.Serialize(query.RelationshipTypes.Select(n => relationshipTypes.First(r => r.Name == n).Id)),
            query.Retired);
        var hops = new Dictionary<Guid, int> { [query.Ci] = 0 };
        var drawn = new HashSet<Guid> { query.Ci };
        var edges = new Dictionary<Guid, Edge>();
        var groups = new List<GraphGroup>();
        var walks = new List<Walk>();
        if (query.WalksDependencies)
        {
            walks.Add(new Walk(Forward: true, query.Ci));
        }

        if (query.WalksDependents)
        {
            walks.Add(new Walk(Forward: false, query.Ci));
        }

        var relationshipTypeNames = relationshipTypes.ToDictionary(r => r.Id, r => r.Name);
        var typeNames = types.ToDictionary(t => t.Id, t => t.Name);
        var truncated = false;
        for (var hop = 1; hop <= query.Depth && !truncated && walks.Any(w => w.Frontier.Count > 0); hop++)
        {
            // This hop's additions, applied only if the hop fits under the limit.
            var newEdges = new Dictionary<Guid, Edge>();
            var newGroups = new List<GraphGroup>();
            var reached = new List<(Walk Walk, HashSet<Guid> Expanded, HashSet<Guid> Grouped)>();
            foreach (var walk in walks.Where(w => w.Frontier.Count > 0))
            {
                var take = MaxRelationships - edges.Count - newEdges.Count + 1;
                var rows = await HopAsync(walk.Forward, walk.Frontier, filter, take);
                if (rows.Count >= take)
                {
                    truncated = true;
                    break;
                }

                var groupedKeys = new Dictionary<(Guid Near, int RelationshipTypeId, int FarTypeId), string>();
                if (query.Group)
                {
                    foreach (var g in rows.Where(r => !walk.Visited.Contains(walk.Far(r))).GroupBy(r => (walk.Near(r), r.RelationshipTypeId, r.FarTypeId)))
                    {
                        var members = g.Select(walk.Far).Distinct().ToList();
                        if (members.Count > GraphQuery.GroupAbove)
                        {
                            var id = $"g{groups.Count + newGroups.Count + 1}";
                            newGroups.Add(new GraphGroup(id, g.Key.Item1, walk.Forward ? RelationshipDirections.Dependencies : RelationshipDirections.Dependents,
                                relationshipTypeNames[g.Key.RelationshipTypeId], typeNames[g.Key.FarTypeId], hop, members.Count, members));
                            groupedKeys[g.Key] = id;
                        }
                    }
                }

                var expanded = new HashSet<Guid>();
                var grouped = new HashSet<Guid>();
                foreach (var row in rows)
                {
                    var far = walk.Far(row);
                    var isNew = !walk.Visited.Contains(far);
                    var group = isNew && groupedKeys.TryGetValue((walk.Near(row), row.RelationshipTypeId, row.FarTypeId), out var g) ? g : null;
                    if (!edges.ContainsKey(row.Id))
                    {
                        newEdges.TryAdd(row.Id, new Edge(row, Dependents: !walk.Forward, group));
                    }

                    if (isNew)
                    {
                        (group is null ? expanded : grouped).Add(far);
                    }
                }

                reached.Add((walk, expanded, grouped));
            }

            if (truncated || edges.Count + newEdges.Count > MaxRelationships)
            {
                truncated = true;
                break;
            }

            foreach (var (id, edge) in newEdges)
            {
                edges[id] = edge;
            }

            groups.AddRange(newGroups);
            foreach (var (walk, expanded, grouped) in reached)
            {
                foreach (var ci in expanded.Concat(grouped))
                {
                    walk.Visited.Add(ci);
                    hops[ci] = Math.Min(hops.GetValueOrDefault(ci, int.MaxValue), hop);
                }

                drawn.UnionWith(expanded);
                walk.Frontier = [.. expanded];
            }
        }

        if (!truncated && query.WalksDependents)
        {
            truncated = !await AddRedundancyMembersAsync(edges, hops, drawn, filter);
        }

        var nodes = await NodesAsync(hops.Keys);
        if (!nodes.ContainsKey(query.Ci))
        {
            return null;
        }

        // A group can be one hop past every CI it holds: both walks reach them, the other walk first (a cycle).
        var counts = new int[hops.Values.Concat(groups.Select(g => g.Hops)).Max() + 1];
        foreach (var (ci, hop) in hops)
        {
            if (drawn.Contains(ci))
            {
                counts[hop]++;
            }
        }

        foreach (var group in groups)
        {
            counts[group.Hops]++;
        }

        return new GraphResult(query.Ci, query.Depth, query.Direction,
            [.. hops.Select(h => (Node: nodes[h.Key], Hops: h.Value))
                .OrderBy(n => n.Hops).ThenBy(n => n.Node.Name, StringComparer.OrdinalIgnoreCase).ThenBy(n => n.Node.Id)
                .Select(n => new GraphNode(n.Node.Id, n.Node.Name, typeNames[n.Node.CiTypeId], n.Node.LifecycleState, n.Hops, !drawn.Contains(n.Node.Id)))],
            [.. edges.Values.Select(e => new GraphRelationship(e.Row.Id, relationshipTypeNames[e.Row.RelationshipTypeId], e.Row.FromCiId, e.Row.ToCiId,
                    e.Row.RedundancyGroup, Math.Max(hops[e.Row.FromCiId], hops[e.Row.ToCiId]), e.Group))
                .OrderBy(r => r.Hops).ThenBy(r => r.Type, StringComparer.Ordinal).ThenBy(r => r.Id)],
            groups, counts, truncated);
    }

    /// <summary>
    /// DEC-046 item 4: for each relationship the dependents walk reached its dependent through (not into a group) that
    /// has a redundancy group, the dependent's other relationships in the same group, with their far ends at one hop
    /// past the dependent. False when they would take the graph past the limit; nothing is added then. The dependents'
    /// relationships in any group are read page by page, so those already in the graph and those in other groups never
    /// count toward the limit (the texts are matched here, not in SQL).
    /// </summary>
    private async Task<bool> AddRedundancyMembersAsync(Dictionary<Guid, Edge> edges, Dictionary<Guid, int> hops, HashSet<Guid> drawn, Filter filter)
    {
        var reachedThrough = edges.Values.Where(e => e.Dependents && e.Group is null && e.Row.RedundancyGroup is not null).ToList();
        if (reachedThrough.Count == 0)
        {
            return true;
        }

        var groupsOf = reachedThrough.ToLookup(e => e.Row.FromCiId, e => e.Row.RedundancyGroup);
        var dependents = groupsOf.Select(g => g.Key).ToList();
        var members = new List<HopRow>();
        Guid? after = null;
        while (true)
        {
            var rows = await MembersAsync(dependents, filter, after);
            members.AddRange(rows.Where(r => !edges.ContainsKey(r.Id) && groupsOf[r.FromCiId].Contains(r.RedundancyGroup, RedundancyGroups.Comparer)));
            if (edges.Count + members.Count > MaxRelationships)
            {
                return false;
            }

            if (rows.Count < MembersPage)
            {
                break;
            }

            after = rows[^1].Id;
        }

        foreach (var row in members)
        {
            edges[row.Id] = new Edge(row, Dependents: false, Group: null);
            hops[row.ToCiId] = Math.Min(hops.GetValueOrDefault(row.ToCiId, int.MaxValue), hops[row.FromCiId] + 1);
            drawn.Add(row.ToCiId);
        }

        return true;
    }

    private async Task<IReadOnlyList<HopRow>> HopAsync(bool forward, IReadOnlyList<Guid> frontier, Filter filter, int take)
    {
        var parameters = Parameters(frontier, filter, take);
        return forward ? await db.QueryAsync<HopRow>(DependenciesHopSql, parameters) : await db.QueryAsync<HopRow>(DependentsHopSql, parameters);
    }

    private Task<IReadOnlyList<HopRow>> MembersAsync(IReadOnlyList<Guid> dependents, Filter filter, Guid? after) =>
        db.QueryAsync<HopRow>(MembersSql, new
        {
            take = MembersPage,
            tenant = Tenants.Default,
            ids = JsonSerializer.Serialize(dependents.Select(i => i.ToString("D"))),
            types = filter.TypeIds,
            relationshipTypes = filter.RelationshipTypeIds,
            retired = filter.Retired,
            after,
        });

    private async Task<Dictionary<Guid, NodeRow>> NodesAsync(IEnumerable<Guid> ids) =>
        (await db.QueryAsync<NodeRow>(NodesSql, new { tenant = Tenants.Default, ids = JsonSerializer.Serialize(ids.Select(i => i.ToString("D"))) }))
            .ToDictionary(n => n.Id);

    private static object Parameters(IReadOnlyList<Guid> ids, Filter filter, int take) => new
    {
        take,
        tenant = Tenants.Default,
        ids = JsonSerializer.Serialize(ids.Select(i => i.ToString("D"))),
        types = filter.TypeIds,
        relationshipTypes = filter.RelationshipTypeIds,
        retired = filter.Retired,
    };

    private sealed record Filter(string? TypeIds, string? RelationshipTypeIds, bool Retired);

    /// <summary>One walk's state: which way it goes, the CIs it has reached and the ones it expands at the next hop.</summary>
    private sealed record Walk(bool Forward, Guid Focus)
    {
        public HashSet<Guid> Visited { get; } = [Focus];

        public List<Guid> Frontier { get; set; } = [Focus];

        public Guid Near(HopRow row) => Forward ? row.FromCiId : row.ToCiId;

        public Guid Far(HopRow row) => Forward ? row.ToCiId : row.FromCiId;
    }

    private sealed record Edge(HopRow Row, bool Dependents, string? Group);

    private sealed record HopRow(Guid Id, Guid FromCiId, Guid ToCiId, int RelationshipTypeId, string? RedundancyGroup, int FarTypeId);

    private sealed record NodeRow(Guid Id, string Name, int CiTypeId, string LifecycleState);
}
