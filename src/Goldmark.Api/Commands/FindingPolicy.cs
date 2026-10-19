using System.Text.RegularExpressions;

namespace Goldmark.Api.Commands;

/// <summary>
/// The operator's accepted audit findings (<c>Goldmark:Bootstrap:AcceptedFindings</c>,
/// <c>GOLDMARK_DB_ACCEPTED_FINDINGS</c>): finding IDs separated by commas or white space, matched
/// exactly (case-sensitive: names may differ only by case on some servers). Only
/// findings about the shared instance can be accepted (<see cref="PrincipalAudit.IsAcceptableKind"/>);
/// an accepted finding is still reported, and an accepted ID that no longer occurs is a warning.
/// </summary>
internal sealed partial class FindingPolicy
{
    public const string Setting = "Goldmark:Bootstrap:AcceptedFindings";
    public const string Variable = "GOLDMARK_DB_ACCEPTED_FINDINGS";

    private readonly HashSet<string> _accepted;
    // Ordinal: names in IDs may differ only by case on a case-sensitive server (Codex on #17).
    private readonly HashSet<string> _matched = new(StringComparer.Ordinal);

    private FindingPolicy(HashSet<string> accepted) => _accepted = accepted;

    [GeneratedRegex(@"^([a-z][a-z0-9-]*)(?::[A-Za-z0-9_.\-$#@\\/\[\]+%]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    private static string[] Split(string? setting) =>
        (setting ?? "").Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Configuration errors (exit 2): malformed IDs, and IDs of findings that must be fixed instead.</summary>
    public static List<string> Errors(string? setting)
    {
        var errors = new List<string>();
        var entries = Split(setting);
        for (var i = 0; i < entries.Length; i++)
        {
            var match = IdPattern().Match(entries[i]);
            if (!match.Success)
            {
                // Not echoed: a value pasted into the wrong setting may be a secret.
                errors.Add($"{Setting}: entry {i + 1} of {entries.Length} is not a finding ID (kind:part[:part...]).");
                continue;
            }

            var kind = match.Groups[1].Value;
            if (!PrincipalAudit.IsAcceptableKind(kind))
            {
                errors.Add($"{Setting}: findings of kind '{kind}' cannot be accepted; fix them instead. " +
                    $"Acceptable kinds: {string.Join(", ", PrincipalAudit.AcceptableKindNames.Order(StringComparer.Ordinal))}.");
            }
        }

        return errors;
    }

    public static FindingPolicy Parse(string? setting) => new(new HashSet<string>(Split(setting), StringComparer.Ordinal));

    /// <summary>
    /// Reports accepted findings and returns those that refuse the run (each once, by ID).
    /// </summary>
    public List<AuditFinding> Refused(IEnumerable<AuditFinding> findings, TextWriter output, string command)
    {
        var refused = new List<AuditFinding>();
        foreach (var finding in findings.DistinctBy(f => f.Id, StringComparer.Ordinal))
        {
            if (finding.Acceptable && _accepted.Contains(finding.Id))
            {
                if (_matched.Add(finding.Id))
                {
                    output.WriteLine($"{command}: accepted finding [{finding.Id}] ({Variable}): {finding.Description}");
                }
            }
            else
            {
                refused.Add(finding);
            }
        }

        return refused;
    }

    /// <summary>Warns about accepted IDs that matched no finding in this run.</summary>
    public void ReportUnmatched(TextWriter output, string command)
    {
        foreach (var id in _accepted.Where(id => !_matched.Contains(id)).Order(StringComparer.Ordinal))
        {
            output.WriteLine($"{command}: warning: accepted finding [{id}] did not occur; remove it from {Variable}.");
        }
    }

    /// <summary>The refusal text: every finding, with its ID, what is wrong and how to fix it.</summary>
    public static string Describe(IReadOnlyCollection<AuditFinding> refused) =>
        $"the principal audit found {refused.Count} problem(s):" + string.Concat(refused.Select(f =>
            $"{Environment.NewLine}  - [{f.Id}] {f.Description}" +
            (f.Acceptable ? $" On a shared server you may instead accept it: add {f.Id} to {Variable}." : "")));
}
