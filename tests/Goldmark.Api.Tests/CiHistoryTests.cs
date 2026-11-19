using Goldmark.Api.Inventory;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Goldmark.Api.Tests;

/// <summary>
/// A CI's history as <see cref="CiHistoryBuilder"/> builds it from versions (US-43; DEC-047), and the <c>asOf</c>
/// parameter's reading (US-42). The reads themselves are in the integration tests (<c>HistoryApiTests</c>).
/// </summary>
public sealed class CiHistoryTests
{
    private static readonly Guid Ci = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Host = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Vm = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid Ann = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid Bob = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly DateTime Open = new(9999, 12, 31, 23, 59, 59, DateTimeKind.Utc);

    private static DateTime T(int minute) => new(2026, 11, 1, 10, minute, 0, DateTimeKind.Utc);

    private static HistoryActor? Actor(string? changedBy) => changedBy switch
    {
        null => null,
        _ when Guid.TryParse(changedBy, out var id) => new HistoryActor(id, id == Ann ? "Ann" : "Bob", null),
        _ => new HistoryActor(null, null, changedBy),
    };

    private static AttributeVersion Definition(string name, string label, string dataType, int order, DateTime from, DateTime? to = null) =>
        new(name, label, dataType, false, order, order, from, to ?? Open);

    [Fact]
    public void Each_version_is_compared_with_the_one_before_under_the_definitions_of_its_time()
    {
        CiVersion[] versions =
        [
            new("srv-01", "Active", """{"rack":"R1","cores":8,"big":9007199254740993}""", Ann.ToString("D"), T(1), T(2)),
            new("srv-01b", "Active", """{"rack":"R2","cores":8,"big":9007199254740993,"owner":"10000000-0000-0000-0000-000000000002"}""", Bob.ToString("D"), T(2), T(3)),
            new("srv-01b", "Retired", """{"rack":"R2","cores":8,"big":9007199254740993,"owner":"10000000-0000-0000-0000-000000000002"}""", Bob.ToString("D"), T(3), T(4)),
            new("srv-01b", "Active", """{"rack":"R2","cores":8,"big":9007199254740993,"owner":"10000000-0000-0000-0000-000000000002"}""", Ann.ToString("D"), T(4), T(6)),
            // The attribute "rack" was deleted at T(6): its definition ends then, in the transaction that wrote this version.
            new("srv-01b", "Active", """{"cores":8,"big":9007199254740993,"owner":"10000000-0000-0000-0000-000000000002"}""", "collector:vc01", T(6), Open),
        ];
        AttributeVersion[] definitions =
        [
            Definition("rack", "Rack", "String", 1, T(0), T(5)),
            Definition("rack", "Rack position", "String", 1, T(5), T(6)), // renamed, then deleted
            Definition("cores", "Cores", "Number", 2, T(0)),
            Definition("big", "Big", "Number", 3, T(0)),
            Definition("owner", "Owner", "CiRef", 4, T(0)),
        ];

        var events = CiHistoryBuilder.Build(Ci, versions, definitions, [], new Dictionary<Guid, HistoryActor>(), Actor);

        Assert.Equal(["changed", "restored", "retired", "changed", "created"], events.Select(e => e.Entry.Kind));
        Assert.Equal(["2026-11-01T10:06:00Z", "2026-11-01T10:04:00Z", "2026-11-01T10:03:00Z", "2026-11-01T10:02:00Z", "2026-11-01T10:01:00Z"],
            events.Select(e => e.Entry.At));

        // Created: everything it started with, numbers as decimal strings with every digit.
        var created = events[4].Entry;
        Assert.Equal(new HistoryActor(Ann, "Ann", null), created.By);
        Assert.Equal(["name:Name:added::\"srv-01\"", "attributes.rack:Rack:added::\"R1\"", "attributes.cores:Cores:added::\"8\"",
            "attributes.big:Big:added::\"9007199254740993\""], created.Changes.Select(Line));

        // Changed: only what differs, in the type's order; a link as the CI's ID.
        Assert.Equal(["name:Name:changed:\"srv-01\":\"srv-01b\"", "attributes.rack:Rack:changed:\"R1\":\"R2\"",
            "attributes.owner:Owner:added::\"10000000-0000-0000-0000-000000000002\""], events[3].Entry.Changes.Select(Line));
        Assert.Equal("ciRef", events[3].Entry.Changes[2].Kind);
        Assert.Equal([Host], CiHistoryBuilder.Referenced(events));

        // Retire and restore are kinds, with no difference of their own.
        Assert.Empty(events[2].Entry.Changes);
        Assert.Empty(events[1].Entry.Changes);
        Assert.Equal(Bob, events[2].Entry.By!.Id);

        // The value that went with its deleted attribute reads under the label the version before knew, and a
        // ChangedBy that is no account is given as it is.
        var removed = events[0].Entry;
        Assert.Equal(["attributes.rack:Rack position:removed:\"R2\":"], removed.Changes.Select(Line));
        Assert.Equal(new HistoryActor(null, null, "collector:vc01"), removed.By);
    }

    [Fact]
    public void A_relationship_is_added_changed_and_ended_and_only_the_audit_entry_says_who_ended_it()
    {
        var kept = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var ended = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var forgotten = Guid.Parse("20000000-0000-0000-0000-000000000003");
        CiVersion[] versions = [new("srv-01", "Active", "{}", Ann.ToString("D"), T(1), Open)];
        RelationshipVersion[] relationships =
        [
            // The CI depends on the host: added without a group, then given one.
            new(kept, "runs_on", Ci, Host, null, Ann.ToString("D"), T(2), T(4)),
            new(kept, "runs_on", Ci, Host, "A", Bob.ToString("D"), T(4), Open),
            // A VM depended on the CI; Bob wrote it last, Ann ended it (the audit entry says so).
            new(ended, "depends_on", Vm, Ci, "g", Bob.ToString("D"), T(2), T(5)),
            // Ended too, and its audit entry is gone: nobody, never the last writer.
            new(forgotten, "depends_on", Vm, Ci, null, Bob.ToString("D"), T(3), T(5)),
        ];
        var endedBy = new Dictionary<Guid, HistoryActor> { [ended] = new(Ann, "Ann", null) };

        var events = CiHistoryBuilder.Build(Ci, versions, [], relationships, endedBy, Actor);

        Assert.Equal(
        [
            $"10:05 relationshipEnded {ended:D} dependents {Vm:D} by Ann",
            $"10:05 relationshipEnded {forgotten:D} dependents {Vm:D} by nobody",
            $"10:04 relationshipChanged {kept:D} dependencies {Host:D} by Bob",
            $"10:03 relationshipAdded {forgotten:D} dependents {Vm:D} by Bob",
            $"10:02 relationshipAdded {kept:D} dependencies {Host:D} by Ann",
            $"10:02 relationshipAdded {ended:D} dependents {Vm:D} by Bob",
            "10:01 created",
        ], events.Select(e => e.Entry.Relationship is { } r
            ? $"{e.At:HH:mm} {e.Entry.Kind} {r.Id:D} {r.Direction} {r.Ci:D} by {e.Entry.By?.DisplayName ?? "nobody"}"
            : $"{e.At:HH:mm} {e.Entry.Kind}"));
        Assert.Equal(["redundancyGroup:Redundancy group:added::\"A\""], events[2].Entry.Changes.Select(Line));
        Assert.Equal([Vm, Host], CiHistoryBuilder.Referenced(events).Distinct());

        // Paging: entries of one moment keep their order, so a cursor between them loses and repeats nothing.
        var seen = new List<HistoryEvent>();
        (DateTime At, string Tie)? after = null;
        for (var pages = 0; pages < 10; pages++)
        {
            var page = CiHistoryBuilder.Page(events, after, 2);
            seen.AddRange(page.Take(2));
            if (page.Count <= 2)
            {
                break;
            }

            after = (page[1].At, page[1].Tie);
        }

        Assert.Equal(events, seen);
        Assert.Empty(CiHistoryBuilder.Page(events, (T(0), "0"), 2));
    }

    [Theory]
    [InlineData("2026-11-01T14:30Z", "2026-11-01T14:30:00.0000000")]
    [InlineData("2026-11-01T14:30:05Z", "2026-11-01T14:30:05.0000000")]
    [InlineData("2026-11-01T14:30:05.1234567Z", "2026-11-01T14:30:05.1234567")]
    [InlineData("2026-11-01T14:30:05.5Z", "2026-11-01T14:30:05.5000000")]
    public void AsOf_reads_a_UTC_moment_and_writes_it_back_as_it_came(string text, string stamp)
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));

        Assert.True(AsOf.TryParse(Query(text), clock, out var moment, out var error));

        Assert.Null(error);
        Assert.Equal(DateTimeKind.Utc, moment!.Value.Kind);
        Assert.Equal(stamp, AsOf.Stamp(moment));
        Assert.True(AsOf.TryParse(Query(AsOf.Text(moment.Value)), clock, out var again, out _));
        Assert.Equal(moment, again);
    }

    [Theory]
    [InlineData("2026-11-01T14:30:00", "InvalidTime")] // no zone: each client would read it differently
    [InlineData("2026-11-01T14:30:00+02:00", "InvalidTime")]
    [InlineData("2026-11-01", "InvalidTime")]
    [InlineData("yesterday", "InvalidTime")]
    [InlineData("", "InvalidTime")]
    [InlineData("2026-11-18T00:00:01Z", "FutureTime")]
    public void AsOf_refuses_what_is_not_a_past_UTC_moment(string text, string expected)
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));

        Assert.False(AsOf.TryParse(Query(text), clock, out var moment, out var error));

        Assert.Null(moment);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void AsOf_absent_is_now_and_given_twice_is_refused()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));

        Assert.True(AsOf.TryParse(new QueryCollection(), clock, out var moment, out _));
        Assert.Null(moment);
        Assert.False(AsOf.TryParse(new QueryCollection(new Dictionary<string, StringValues>
        {
            ["asOf"] = new StringValues(["2026-11-01T14:30Z", "2026-11-02T14:30Z"]),
        }), clock, out _, out var error));
        Assert.Equal("InvalidTime", error);
    }

    [Fact]
    public void A_cursor_belongs_to_its_moment()
    {
        var id = Guid.NewGuid();
        var then = T(1);
        var now = new CiCursor("name", "srv", null, null, id).Encode();
        var past = new CiCursor("name", "srv", null, null, id, AsOf.Stamp(then)).Encode();

        Assert.NotNull(CiCursor.Decode(now, "name"));
        Assert.NotNull(CiCursor.Decode(past, "name", then));
        Assert.Null(CiCursor.Decode(past, "name"));
        Assert.Null(CiCursor.Decode(now, "name", then));
        Assert.Null(CiCursor.Decode(past, "name", T(2)));

        var section = new RelationshipCursor("dependencies", "runs_on", "esx", id, AsOf.Stamp(then)).Encode();
        Assert.NotNull(RelationshipCursor.Decode(section, "dependencies", then));
        Assert.Null(RelationshipCursor.Decode(section, "dependencies"));
        Assert.Null(RelationshipCursor.Decode(new RelationshipCursor("dependencies", "runs_on", "esx", id).Encode(), "dependencies", then));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static QueryCollection Query(string asOf) => new(new Dictionary<string, StringValues> { ["asOf"] = asOf });

    private static string Line(HistoryChange change) =>
        $"{change.Field}:{change.Label}:{change.Change}:{change.Before?.ToJsonString()}:{change.After?.ToJsonString()}";
}
