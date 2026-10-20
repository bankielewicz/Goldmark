using System.Text;

namespace Goldmark.Api.Security;

/// <summary>
/// CSV for the "Export CSV" downloads (screens 16 and 17), RFC 4180 with CRLF line ends. A cell that a
/// spreadsheet would read as a formula (starting with =, +, -, @, a tab or a carriage return) is
/// prefixed with an apostrophe, so an exported value can never run as one (CSV injection).
/// </summary>
internal static class Csv
{
    public static StringBuilder AppendRow(this StringBuilder csv, IEnumerable<string> cells) =>
        csv.AppendJoin(',', cells.Select(Cell)).Append("\r\n");

    public static string Cell(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 || value.StartsWith('\'') ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }
}
