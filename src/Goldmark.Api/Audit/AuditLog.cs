using System.Globalization;
using System.Text;
using System.Text.Json;
using Goldmark.Api.Data;
using Goldmark.Api.Security;

namespace Goldmark.Api.Audit;

/// <summary>
/// Reads <c>cmdb.AuditLog</c> for screen 16 and its CSV export (US-07). Read-only: the runtime login
/// holds no UPDATE or DELETE on the log (0002). Names are resolved when read, so an entry about an
/// account that was removed since (revoking an unused invitation removes it) shows the user name the
/// entry recorded, if any.
/// </summary>
internal sealed class AuditLog(DbSession db)
{
    /// <summary>The largest export (screen 16: "Exports are limited to 100,000").</summary>
    public const int ExportLimit = 100_000;

    private const string Columns = """
        a.Id, a.At, a.ActorType, a.ActorId, a.Action, a.TargetType, a.TargetId, a.Ip,
        CAST(a.Details AS nvarchar(max)) AS Details,
        actor.UserName AS ActorUserName, actor.DisplayName AS ActorDisplayName,
        target.UserName AS TargetUserName, target.DisplayName AS TargetDisplayName
        """;

    private const string Joins = """
        FROM cmdb.AuditLog AS a
        LEFT JOIN cmdb.UserAccount AS actor ON a.ActorType = 'User' AND actor.Id = TRY_CAST(a.ActorId AS uniqueidentifier)
        LEFT JOIN cmdb.UserAccount AS target ON a.TargetType = 'User' AND target.Id = TRY_CAST(a.TargetId AS uniqueidentifier)
        """;

    // Every filter is a parameter (NFR-SEC-3); an unset one is NULL. Times arrive as UTC
    // datetimeoffset values and are compared as datetime2, so the index on At can be used.
    private const string Filter = """
        WHERE (@from IS NULL OR a.At >= CAST(@from AS datetime2(7)))
            AND (@to IS NULL OR a.At < CAST(@to AS datetime2(7)))
            AND (@actorType IS NULL OR a.ActorType = @actorType)
            AND (@actorId IS NULL OR TRY_CAST(a.ActorId AS uniqueidentifier) = @actorId)
            AND (@targetId IS NULL OR (a.TargetType = 'User' AND TRY_CAST(a.TargetId AS uniqueidentifier) = @targetId))
            AND (@actions IS NULL OR a.Action IN (SELECT [value] FROM OPENJSON(@actions)))
            AND (@ip IS NULL OR a.Ip LIKE @ip + N'%')
        """;

    private const string PageSql = $"""
        SELECT TOP (@take) {Columns}
        {Joins}
        {Filter}
            AND (@cursor IS NULL OR (@oldest = 0 AND a.Id < @cursor) OR (@oldest = 1 AND a.Id > @cursor))
        ORDER BY CASE WHEN @oldest = 1 THEN a.Id END, a.Id DESC
        OPTION (RECOMPILE);
        """;

    private const string CountSql = $"""
        SELECT COUNT_BIG(*) FROM cmdb.AuditLog AS a
        {Filter}
        OPTION (RECOMPILE);
        """;

    // The people an Auditor can already see in the log's entries, for the Actor and Person-affected
    // filters; not the whole user directory, which only an Admin sees (screen 17).
    private const string PeopleSql = """
        SELECT u.Id, u.UserName, u.DisplayName
        FROM cmdb.UserAccount AS u
        WHERE u.Id IN (SELECT TRY_CAST(ActorId AS uniqueidentifier) FROM cmdb.AuditLog WHERE ActorType = 'User'
            UNION SELECT TRY_CAST(TargetId AS uniqueidentifier) FROM cmdb.AuditLog WHERE TargetType = 'User')
        ORDER BY u.DisplayName, u.UserName;
        """;

    public async Task<long> CountAsync(AuditQuery query) =>
        await db.ExecuteScalarAsync<long>(CountSql, Parameters(query, take: 0, cursor: null));

    /// <summary>One page, newest (or oldest) first, and the cursor of the next page if there is one.</summary>
    public async Task<(IReadOnlyList<AuditEntry> Entries, long? Next)> PageAsync(AuditQuery query, int limit, long? cursor)
    {
        var rows = await db.QueryAsync<Row>(PageSql, Parameters(query, limit + 1, cursor));
        var entries = rows.Take(limit).Select(Entry).ToList();
        return (entries, rows.Count > limit ? entries[^1].Id : null);
    }

    /// <summary>Up to <see cref="ExportLimit"/> entries, in the query's order.</summary>
    public async Task<IReadOnlyList<AuditEntry>> ExportAsync(AuditQuery query) =>
        [.. (await db.QueryAsync<Row>(PageSql, Parameters(query, ExportLimit, cursor: null))).Select(Entry)];

    public Task<IReadOnlyList<AuditPerson>> PeopleAsync() => db.QueryAsync<AuditPerson>(PeopleSql);

    /// <summary>Screen 16's export: the columns the export dialog names, formula cells neutralized.</summary>
    public static string ToCsv(IEnumerable<AuditEntry> entries)
    {
        var csv = new StringBuilder("Time (UTC),Actor type,Actor,Action,Action code,Person affected,IP address,Outcome,Details\r\n");
        foreach (var entry in entries)
        {
            csv.AppendRow(
            [
                entry.At.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                entry.Actor.Type switch
                {
                    AuditActorTypes.User => "Person",
                    AuditActorTypes.Anonymous => "Not signed in",
                    _ => entry.Actor.Type,
                },
                Name(entry.Actor),
                entry.Label,
                entry.Action,
                entry.Target is { } target ? Name(target) : "",
                entry.Ip ?? "",
                entry.Outcome,
                entry.Details is { } details ? details.GetRawText() : "",
            ]);
        }

        return csv.ToString();
    }

    /// <summary>A person's name; empty for System and an anonymous client, whose type column says it all.</summary>
    private static string Name(AuditParty party) => party switch
    {
        { DisplayName: { } name, UserName: { } user } => $"{name} ({user})",
        { Removed: true, UserName: { } user } => $"{user} (account removed)",
        { Removed: true } => "Removed account",
        _ => "",
    };

    private static object Parameters(AuditQuery query, int take, long? cursor) => new
    {
        take,
        cursor,
        oldest = query.Oldest,
        from = query.From,
        to = query.To,
        actorType = query.ActorType,
        actorId = query.ActorId,
        targetId = query.TargetId,
        actions = query.ActionsJson(),
        ip = query.IpPrefix,
    };

    private static AuditEntry Entry(Row row)
    {
        JsonElement? details = null;
        if (row.Details is { } json)
        {
            using var document = JsonDocument.Parse(json);
            details = document.RootElement.Clone();
        }

        var action = AuditCatalog.Describe(row.Action);
        var actor = row.ActorType == AuditActorTypes.User
            ? Person(row.ActorId, row.ActorUserName, row.ActorDisplayName, recordedUserName: null)
            : new AuditParty(row.ActorType, null, null, null, Removed: false);
        var target = row.TargetType == AuditActorTypes.User && row.TargetId is not null
            ? Person(row.TargetId, row.TargetUserName, row.TargetDisplayName, RecordedUserName(details))
            : null;
        return new AuditEntry(row.Id, row.At, actor, row.Action, action.Label, action.Outcome, target, row.Ip, details);
    }

    private static AuditParty Person(string? id, string? userName, string? displayName, string? recordedUserName) =>
        userName is not null
            ? new AuditParty(AuditActorTypes.User, Guid.TryParse(id, out var guid) ? guid : null, userName, displayName, Removed: false)
            : new AuditParty(AuditActorTypes.User, Guid.TryParse(id, out var removed) ? removed : null, recordedUserName, null, Removed: true);

    /// <summary>The user name an administrator's action recorded for the person affected (slice 3 writes it).</summary>
    private static string? RecordedUserName(JsonElement? details) =>
        details is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty("userName", out var name) && name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;

    private sealed record Row(long Id, DateTime At, string ActorType, string? ActorId, string Action, string? TargetType, string? TargetId,
        string? Ip, string? Details, string? ActorUserName, string? ActorDisplayName, string? TargetUserName, string? TargetDisplayName);
}

internal sealed record AuditEntry(long Id, DateTime At, AuditParty Actor, string Action, string Label, string Outcome, AuditParty? Target,
    string? Ip, JsonElement? Details);

/// <summary>Who acted, or whom it was about. A person whose account no longer exists is <c>Removed</c>.</summary>
internal sealed record AuditParty(string Type, Guid? Id, string? UserName, string? DisplayName, bool Removed);

internal sealed record AuditPerson(Guid Id, string UserName, string DisplayName);
