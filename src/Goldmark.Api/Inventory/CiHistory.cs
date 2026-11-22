using System.Text.Json;
using System.Text.Json.Nodes;

namespace Goldmark.Api.Inventory;

/// <summary>
/// Who made a change (US-43: "who or what"). An account has <c>Id</c>, and <c>DisplayName</c> while it exists;
/// anything else <c>ChangedBy</c> holds (a source, from slice 10) is given as <c>Name</c>.
/// </summary>
internal sealed record HistoryActor(Guid? Id, string? DisplayName, string? Name);

/// <summary>
/// One difference between two versions: <c>Field</c> is <c>name</c>, <c>lifecycleState</c>, <c>attributes.{name}</c>
/// or <c>redundancyGroup</c>; <c>Change</c> is <c>added</c>, <c>removed</c> or <c>changed</c>; the values are as the
/// API sends a CI's (a number as a decimal string, a link as the CI's ID). <c>Kind</c> is the attribute's kind
/// when its definition is known. <c>AttributeDeleted</c> says the value went because its attribute was deleted
/// at that moment, not because someone cleared it.
/// </summary>
internal sealed record HistoryChange(string Field, string Label, string? Kind, string Change, JsonNode? Before, JsonNode? After,
    bool AttributeDeleted = false);

/// <summary>The relationship an entry is about, seen from the CI: its type, which way it points, and the CI at the other end.</summary>
internal sealed record HistoryRelationship(Guid Id, string Type, string Direction, Guid Ci, string? RedundancyGroup);

/// <summary>
/// One entry of a CI's history (US-43). <c>At</c> is when it took effect, in the form <c>asOf</c> takes, so the CI
/// can be read as it was from that moment on. <c>By</c> is null when nobody is on record. <c>Kind</c> is one of
/// <see cref="HistoryKinds"/>.
/// </summary>
internal sealed record HistoryEntry(string At, string Kind, HistoryActor? By, IReadOnlyList<HistoryChange> Changes, HistoryRelationship? Relationship);

/// <summary>
/// One page of a CI's history, newest first. <c>Total</c> comes with the first page only; <c>Next</c> is the cursor
/// of the page after. <c>References</c> holds the CIs the entries name (the other ends, and linked CIs in values) as
/// they are now.
/// </summary>
internal sealed record HistoryPage(IReadOnlyList<HistoryEntry> Items, int? Total, string? Next, IReadOnlyDictionary<string, CiReference> References);

internal static class HistoryKinds
{
    public const string Created = "created";
    public const string Changed = "changed";
    public const string Retired = "retired";
    public const string Restored = "restored";
    public const string RelationshipAdded = "relationshipAdded";
    public const string RelationshipChanged = "relationshipChanged";
    public const string RelationshipEnded = "relationshipEnded";
}

/// <summary>A version of a CI as its history table and current row hold it.</summary>
internal sealed record CiVersion(string Name, string LifecycleState, string Attributes, string? ChangedBy, DateTime ValidFrom, DateTime ValidTo);

/// <summary>A version of one of the CI type's attribute definitions (deleted ones included).</summary>
internal sealed record AttributeVersion(string Name, string Label, string DataType, bool IsBuiltIn, int SortOrder, int Id, DateTime ValidFrom, DateTime ValidTo);

/// <summary>A version of a relationship with the CI at one end.</summary>
internal sealed record RelationshipVersion(Guid Id, string Type, Guid FromCiId, Guid ToCiId, string? RedundancyGroup, string? ChangedBy,
    DateTime ValidFrom, DateTime ValidTo);

/// <summary>An entry before paging: its moment, its place among entries of the same moment, and the entry.</summary>
internal sealed record HistoryEvent(DateTime At, string Tie, HistoryEntry Entry);

/// <summary>
/// Builds a CI's history from its versions (US-43; DEC-047), with no database: every version of the CI compared with
/// the one before it, and every relationship at either end as added, changed (its redundancy group) and ended.
/// <para>
/// A value is read under the definition its attribute had when the version was written; a value that goes because
/// its attribute was deleted is read under the definition in force until just before, since the delete and the
/// removal are one transaction and the definition no longer exists at the new version's time. Who changed a CI or added
/// or changed a relationship is the version's <c>ChangedBy</c>. Who ended a relationship is not in its versions
/// (DEC-046 item 1: the delete leaves the last writer's): it is the audit entry's actor, given here by
/// relationship ID, and nobody when that entry is gone. The last writer is never shown as the one who ended it.
/// </para>
/// </summary>
internal static class CiHistoryBuilder
{
    private static readonly DateTime Open = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    public static List<HistoryEvent> Build(Guid ci, IReadOnlyList<CiVersion> versions, IReadOnlyList<AttributeVersion> definitions,
        IReadOnlyList<RelationshipVersion> relationships, IReadOnlyDictionary<Guid, HistoryActor> endedBy, Func<string?, HistoryActor?> actor)
    {
        var events = new List<HistoryEvent>();
        CiVersion? previous = null;
        foreach (var version in versions.OrderBy(v => v.ValidFrom))
        {
            events.Add(new HistoryEvent(version.ValidFrom, "0", new HistoryEntry(AsOf.Text(version.ValidFrom), KindOf(previous, version),
                actor(version.ChangedBy), Differences(previous, version, definitions), null)));
            previous = version;
        }

        foreach (var group in relationships.GroupBy(r => r.Id))
        {
            RelationshipVersion? before = null;
            foreach (var version in group.OrderBy(v => v.ValidFrom))
            {
                var about = About(ci, version);
                events.Add(before is null
                    ? Event(version.ValidFrom, "1", HistoryKinds.RelationshipAdded, actor(version.ChangedBy), [], about)
                    : Event(version.ValidFrom, "2", HistoryKinds.RelationshipChanged, actor(version.ChangedBy),
                        Difference("redundancyGroup", "Redundancy group", null, Text(before.RedundancyGroup), Text(version.RedundancyGroup)) is { } change ? [change] : [],
                        about));
                before = version;
            }

            if (before is not null && before.ValidTo < Open)
            {
                events.Add(Event(before.ValidTo, "3", HistoryKinds.RelationshipEnded, endedBy.GetValueOrDefault(before.Id), [], About(ci, before)));
            }
        }

        // Newest first; entries of one moment in a fixed order, so a cursor always finds its place.
        events.Sort((a, b) => b.At != a.At ? b.At.CompareTo(a.At) : string.CompareOrdinal(a.Tie, b.Tie));
        return events;
    }

    /// <summary>The entries after <paramref name="after"/> (a cursor's moment and tie), at most <paramref name="take"/> + 1 of them.</summary>
    public static List<HistoryEvent> Page(List<HistoryEvent> events, (DateTime At, string Tie)? after, int take)
    {
        var start = after is { } a
            ? events.FindIndex(e => e.At < a.At || (e.At == a.At && string.CompareOrdinal(e.Tie, a.Tie) > 0))
            : 0;
        return start < 0 ? [] : events.GetRange(start, Math.Min(take + 1, events.Count - start));
    }

    /// <summary>The CIs the entries name: the other end of each relationship, and the CIs their link values hold.</summary>
    public static IEnumerable<Guid> Referenced(IEnumerable<HistoryEvent> events) =>
        events.SelectMany(e => (e.Entry.Relationship is { } r ? [r.Ci] : Array.Empty<Guid>())
            .Concat(e.Entry.Changes.Where(c => c.Kind == "ciRef").SelectMany(c => new[] { c.Before, c.After })
                .Select(v => v is JsonValue value && value.TryGetValue<string>(out var text) && Guid.TryParse(text, out var id) ? id : (Guid?)null)
                .OfType<Guid>()));

    private static HistoryEvent Event(DateTime at, string rank, string kind, HistoryActor? by, IReadOnlyList<HistoryChange> changes, HistoryRelationship about) =>
        new(at, $"{rank}:{about.Id:D}", new HistoryEntry(AsOf.Text(at), kind, by, changes, about));

    private static HistoryRelationship About(Guid ci, RelationshipVersion version) => version.FromCiId == ci
        ? new HistoryRelationship(version.Id, version.Type, RelationshipDirections.Dependencies, version.ToCiId, version.RedundancyGroup)
        : new HistoryRelationship(version.Id, version.Type, RelationshipDirections.Dependents, version.FromCiId, version.RedundancyGroup);

    private static string KindOf(CiVersion? previous, CiVersion version) => previous switch
    {
        null => HistoryKinds.Created,
        _ when previous.LifecycleState != LifecycleStates.Retired && version.LifecycleState == LifecycleStates.Retired => HistoryKinds.Retired,
        _ when previous.LifecycleState == LifecycleStates.Retired && version.LifecycleState == LifecycleStates.Active => HistoryKinds.Restored,
        _ => HistoryKinds.Changed,
    };

    private static List<HistoryChange> Differences(CiVersion? previous, CiVersion version, IReadOnlyList<AttributeVersion> definitions)
    {
        var changes = new List<HistoryChange>();
        if (Difference("name", "Name", "string", Text(previous?.Name), Text(version.Name)) is { } name)
        {
            changes.Add(name);
        }

        // Retire and restore are the entry's kind; any other lifecycle change (slice 20) is a difference like the rest.
        if (previous is not null && previous.LifecycleState != version.LifecycleState && KindOf(previous, version) == HistoryKinds.Changed
            && Difference("lifecycleState", "Lifecycle", "string", Text(previous.LifecycleState), Text(version.LifecycleState)) is { } lifecycle)
        {
            changes.Add(lifecycle);
        }

        var before = previous is null ? [] : AttributeValues.Parse(previous.Attributes);
        var after = AttributeValues.Parse(version.Attributes);
        var now = DefinitionsAt(definitions, version.ValidFrom);
        var then = DefinitionsBefore(definitions, version.ValidFrom);
        // In the type's order as of this version, then what was defined until just before it, then anything undefined.
        var names = now.Select(d => d.Name).Concat(then.Select(d => d.Name))
            .Concat(before.Select(p => p.Key).Concat(after.Select(p => p.Key)).Order(StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal);
        foreach (var attribute in names)
        {
            var current = now.FirstOrDefault(d => d.Name == attribute);
            var definition = current ?? then.FirstOrDefault(d => d.Name == attribute);
            if (Difference($"attributes.{attribute}", definition?.Label ?? attribute, definition is null ? null : AttributeDataTypes.ToApi(definition.DataType),
                Value(before[attribute], definition), Value(after[attribute], definition)) is { } change)
            {
                // Defined until just before this version and not at it: the attribute was deleted in its transaction.
                changes.Add(change with { AttributeDeleted = current is null && definition is not null && change.Change == "removed" });
            }
        }

        return changes;
    }

    private static HistoryChange? Difference(string field, string label, string? kind, JsonNode? before, JsonNode? after) =>
        JsonNode.DeepEquals(before, after)
            ? null
            : new HistoryChange(field, label, kind, before is null ? "added" : after is null ? "removed" : "changed", before, after);

    private static List<AttributeVersion> DefinitionsAt(IReadOnlyList<AttributeVersion> definitions, DateTime moment) =>
        [.. definitions.Where(d => d.ValidFrom <= moment && moment < d.ValidTo).OrderByDescending(d => d.IsBuiltIn).ThenBy(d => d.SortOrder).ThenBy(d => d.Id)];

    // The definitions in force until just before the moment: a definition deleted at that very moment is among them.
    private static List<AttributeVersion> DefinitionsBefore(IReadOnlyList<AttributeVersion> definitions, DateTime moment) =>
        [.. definitions.Where(d => d.ValidFrom < moment && moment <= d.ValidTo).OrderByDescending(d => d.IsBuiltIn).ThenBy(d => d.SortOrder).ThenBy(d => d.Id)];

    private static JsonValue? Text(string? value) => value is null ? null : JsonValue.Create(value);

    // As AttributeValues.ForResponse sends a CI's values: a number as a decimal string with every digit as stored.
    private static JsonNode? Value(JsonNode? stored, AttributeVersion? definition) =>
        definition?.DataType == AttributeDataTypes.Number && stored is JsonValue value && value.GetValueKind() == JsonValueKind.Number
            ? JsonValue.Create(stored.ToJsonString())
            : stored?.DeepClone();
}
