namespace Goldmark.Api.Audit;

/// <summary>
/// Audit-log settings (section <c>Goldmark:Audit</c>; NFR-LOG-3, DEC-041). <c>migrate</c> reads them and
/// writes the retention period to <c>cmdb.AuditRetention</c>, which the runtime login can read but not
/// change; the application takes the period from there, so the setting belongs to <c>db-init</c>.
/// </summary>
public sealed class AuditOptions
{
    public const string SectionName = "Goldmark:Audit";

    /// <summary>The floor of NFR-LOG-3 ("retained for at least one year"), owner's choice in DEC-041.</summary>
    public const int MinimumRetentionDays = 365;

    /// <summary>A hundred years; the table's CHECK constraint has the same bounds.</summary>
    public const int MaximumRetentionDays = 36500;

    /// <summary>Days an entry is kept: 365 to 36500, or 0 to keep entries indefinitely.</summary>
    public int RetentionDays { get; set; } = MinimumRetentionDays;

    /// <summary>The configuration errors, naming the setting but never its value.</summary>
    public static List<string> Errors(AuditOptions audit) =>
        audit.RetentionDays is 0 or (>= MinimumRetentionDays and <= MaximumRetentionDays)
            ? []
            : [$"{SectionName}:RetentionDays must be 0 (keep entries indefinitely) or between {MinimumRetentionDays} and {MaximumRetentionDays}."];
}
