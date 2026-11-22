using System.Text.Json;
using Goldmark.Api.Audit;
using Goldmark.Api.Data;

namespace Goldmark.Api.Inventory;

/// <summary>
/// Where a history's next page starts: the last entry's moment and its place among entries of that moment, as an
/// opaque base64url JSON array.
/// </summary>
internal sealed record HistoryCursor(string At, string Tie)
{
    public string Encode() =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new[] { At, Tie })).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static (DateTime At, string Tie)? Decode(string value)
    {
        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - (base64.Length % 4)) % 4);
            return JsonSerializer.Deserialize<string?[]>(Convert.FromBase64String(base64)) is [{ } at, { } tie] && CiStore.TryParseStamp(at, out var moment)
                ? (moment, tie)
                : null;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Reads a CI's history (US-43; DEC-047): every version of the CI, of its type's attribute definitions and of the
/// relationships at either end of it, through <c>FOR SYSTEM_TIME ALL</c>, which leaves out versions that lasted
/// no time (two changes in one transaction; measured, <c>ChangedByTests</c>). One such version is read on purpose,
/// from the history table itself: the stamp a relationship's delete leaves, which says who ended it. Migration 0009's indexes on the
/// history tables serve the reads by CI. The entries are built in memory (<see cref="CiHistoryBuilder"/>) and paged
/// there: one CI's versions are few (slice 10's collectors write none for a re-read that changes nothing, US-21).
/// </summary>
internal sealed class CiHistoryStore(DbSession db, CiStore cis)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    private const string VersionsSql = """
        SELECT c.Name, c.LifecycleState, CAST(c.Attributes AS nvarchar(max)) AS Attributes, c.ChangedBy, c.ValidFrom, c.ValidTo
        FROM cmdb.Ci FOR SYSTEM_TIME ALL AS c
        WHERE c.Id = @id AND c.TenantId = @tenant;
        """;

    private const string DefinitionsSql = """
        SELECT a.Name, a.Label, a.DataType, a.IsBuiltIn, a.SortOrder, a.Id, a.ValidFrom, a.ValidTo
        FROM cmdb.AttributeDef FOR SYSTEM_TIME ALL AS a
        WHERE a.CiTypeId = @typeId;
        """;

    // Each half reads one index, in the current table and in the history (IX_RelationshipHistory_FromCi and _ToCi).
    // A relationship type's name never changes, so any of its versions names it, a deleted type's too.
    private const string RelationshipsSql = """
        SELECT r.Id, (SELECT TOP (1) t.Name FROM cmdb.RelationshipType FOR SYSTEM_TIME ALL AS t WHERE t.Id = r.RelationshipTypeId) AS Type,
            r.FromCiId, r.ToCiId, r.RedundancyGroup, r.ChangedBy, r.ValidFrom, r.ValidTo
        FROM cmdb.Relationship FOR SYSTEM_TIME ALL AS r
        WHERE r.TenantId = @tenant AND r.FromCiId = @id
        UNION ALL
        SELECT r.Id, (SELECT TOP (1) t.Name FROM cmdb.RelationshipType FOR SYSTEM_TIME ALL AS t WHERE t.Id = r.RelationshipTypeId) AS Type,
            r.FromCiId, r.ToCiId, r.RedundancyGroup, r.ChangedBy, r.ValidFrom, r.ValidTo
        FROM cmdb.Relationship FOR SYSTEM_TIME ALL AS r
        WHERE r.TenantId = @tenant AND r.ToCiId = @id AND r.FromCiId <> @id;
        """;

    // Who ended a relationship (DEC-047 item 9): the delete's stamp, a last version that lasted no time, which only
    // the history table itself returns. Each half reads one of 0009's indexes.
    private const string EndStampsSql = """
        SELECT h.Id, h.ChangedBy, h.ValidTo
        FROM cmdb.RelationshipHistory AS h
        WHERE h.TenantId = @tenant AND h.FromCiId = @id AND h.ValidFrom = h.ValidTo
        UNION ALL
        SELECT h.Id, h.ChangedBy, h.ValidTo
        FROM cmdb.RelationshipHistory AS h
        WHERE h.TenantId = @tenant AND h.ToCiId = @id AND h.FromCiId <> @id AND h.ValidFrom = h.ValidTo;
        """;

    // A relationship ended before the delete stamped it (slice 6): its "relationship.deleted" audit entry's actor
    // (DEC-046 item 1; IX_AuditLog_Target), while that entry is kept.
    private const string EndedBySql = """
        SELECT a.TargetId, a.ActorType, a.ActorId
        FROM cmdb.AuditLog AS a
        WHERE a.TargetType = @targetType AND a.Action = @action
            AND a.TargetId IN (SELECT CAST(value AS nvarchar(256)) FROM OPENJSON(@ids));
        """;

    private const string PeopleSql = """
        SELECT u.Id, u.DisplayName FROM cmdb.UserAccount AS u
        WHERE u.Id IN (SELECT CAST(value AS uniqueidentifier) FROM OPENJSON(@ids));
        """;

    // The API tokens that made changes (ChangedBy 'token:' and the ID; DEC-048); never deleted, so each keeps its name.
    private const string TokensSql = """
        SELECT k.Id, k.Name FROM cmdb.ApiToken AS k
        WHERE k.Id IN (SELECT CAST(value AS uniqueidentifier) FROM OPENJSON(@ids));
        """;

    /// <summary>One page of the CI's history, newest first, or null when there is no such CI.</summary>
    public async Task<HistoryPage?> PageAsync(Guid id, int take, (DateTime At, string Tie)? after)
    {
        if (await cis.TypeIdAsync(id) is not { } typeId)
        {
            return null;
        }

        var versions = await db.QueryAsync<CiVersion>(VersionsSql, new { id, tenant = Tenants.Default });
        var definitions = await db.QueryAsync<AttributeVersion>(DefinitionsSql, new { typeId });
        var relationships = await db.QueryAsync<RelationshipVersion>(RelationshipsSql, new { id, tenant = Tenants.Default });

        var current = relationships.Where(r => r.ValidTo.Year == 9999).Select(r => r.Id).ToHashSet();
        var endings = relationships.Where(r => !current.Contains(r.Id)).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.Max(r => r.ValidTo));
        // The stamp is the version of no length at the very moment the relationship ended.
        var stamps = endings.Count == 0
            ? []
            : (await db.QueryAsync<StampRow>(EndStampsSql, new { id, tenant = Tenants.Default }))
                .Where(s => endings.TryGetValue(s.Id, out var end) && s.ValidTo == end)
                .GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().ChangedBy);
        var ended = endings.Keys.Where(r => !stamps.ContainsKey(r)).ToList();
        var enders = ended.Count == 0
            ? []
            : await db.QueryAsync<EndRow>(EndedBySql, new
            {
                targetType = AuditTargetTypes.Relationship,
                action = AuditActions.RelationshipDeleted,
                ids = JsonSerializer.Serialize(ended.Select(r => r.ToString("D"))),
            });

        var changers = versions.Select(v => v.ChangedBy).Concat(relationships.Select(r => r.ChangedBy)).Concat(stamps.Values)
            .Concat(enders.Where(e => e.ActorType == AuditActorTypes.User).Select(e => e.ActorId)).ToList();
        var accounts = changers.Select(text => Guid.TryParse(text, out var account) ? account : (Guid?)null).OfType<Guid>().Distinct().ToList();
        var people = accounts.Count == 0
            ? []
            : (await db.QueryAsync<PersonRow>(PeopleSql, new { ids = JsonSerializer.Serialize(accounts.Select(a => a.ToString("D"))) }))
                .ToDictionary(p => p.Id, p => p.DisplayName);
        var tokenIds = changers.Select(ActingPrincipal.TokenOf).OfType<Guid>().Distinct().ToList();
        var tokens = tokenIds.Count == 0
            ? []
            : (await db.QueryAsync<TokenRow>(TokensSql, new { ids = JsonSerializer.Serialize(tokenIds.Select(t => t.ToString("D"))) }))
                .ToDictionary(t => t.Id, t => t.Name);
        HistoryActor? Actor(string? changedBy) => changedBy switch
        {
            null => null,
            _ when Guid.TryParse(changedBy, out var account) => new HistoryActor(account, people.GetValueOrDefault(account), null),
            _ when ActingPrincipal.TokenOf(changedBy) is { } token => new HistoryActor(token, null, tokens.GetValueOrDefault(token), ChangedByKinds.Token),
            _ => new HistoryActor(null, null, changedBy, ChangedByKinds.Other),
        };

        // The stamp first. From the audit log, only a person's entry names who ended it; anything else on record there
        // (System, Anonymous) is nobody here.
        var endedBy = enders.Where(e => e.ActorType == AuditActorTypes.User && Guid.TryParse(e.TargetId, out _) && Actor(e.ActorId) is not null)
            .GroupBy(e => Guid.Parse(e.TargetId))
            .ToDictionary(g => g.Key, g => Actor(g.First().ActorId)!);
        foreach (var (relationship, changedBy) in stamps)
        {
            if (Actor(changedBy) is { } who)
            {
                endedBy[relationship] = who;
            }
        }

        var events = CiHistoryBuilder.Build(id, versions, definitions, relationships, endedBy, Actor);
        var page = CiHistoryBuilder.Page(events, after, take);
        var items = page.Take(take).ToList();
        var next = page.Count > take && items[^1] is var last ? new HistoryCursor(CiStore.Stamp(last.At), last.Tie).Encode() : null;
        var references = await cis.LinkedAsync(CiHistoryBuilder.Referenced(items));
        return new HistoryPage([.. items.Select(e => e.Entry)], after is null ? events.Count : null, next,
            references.Values.ToDictionary(l => l.Id.ToString("D"), l => new CiReference(l.Id, l.Name, l.Type, l.LifecycleState)));
    }

    private sealed record StampRow(Guid Id, string? ChangedBy, DateTime ValidTo);

    private sealed record EndRow(string TargetId, string ActorType, string? ActorId);

    private sealed record PersonRow(Guid Id, string DisplayName);

    private sealed record TokenRow(Guid Id, string Name);
}
