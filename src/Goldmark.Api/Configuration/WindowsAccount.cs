using System.Text.RegularExpressions;

namespace Goldmark.Api.Configuration;

/// <summary>
/// The name of the Windows service's account as SQL Server knows its login (DEC-043): <c>DOMAIN\name</c>,
/// <c>DOMAIN\gmsa$</c> or <c>NT SERVICE\name</c>. Letters, digits, spaces, <c>.</c>, <c>_</c> and
/// <c>-</c>, one backslash, and a trailing <c>$</c> for a managed service account; never a quote or a
/// bracket, so it can be written into an <c>EXECUTE AS LOGIN</c> statement as a literal.
/// </summary>
internal static partial class WindowsAccount
{
    public static bool IsValid(string? account) => account is not null && Name().IsMatch(account);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 ._-]{0,62}\\[A-Za-z0-9][A-Za-z0-9 ._-]{0,103}\$?$", RegexOptions.CultureInvariant)]
    private static partial Regex Name();
}
