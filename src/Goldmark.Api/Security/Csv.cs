using System.Text;

namespace Goldmark.Api.Security;

/// <summary>
/// CSV for the "Export CSV" downloads (screens 16 and 17), RFC 4180 with CRLF line ends. A cell that a
/// spreadsheet would read as a formula (starting with =, +, -, @, a tab or a carriage return) is
/// prefixed with an apostrophe, so an exported value can never run as one (CSV injection).
/// </summary>
internal static class Csv
{
    /// <summary>
    /// The file's bytes: UTF-8 with its byte-order mark, so Excel on Windows reads non-ASCII names
    /// correctly instead of in the legacy code page (owner, PR #32 round 2).
    /// </summary>
    public static byte[] Utf8WithBom(string csv) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv)];

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
