namespace Goldmark.Api.Configuration;

/// <summary>
/// Default folders for what Goldmark keeps (rule 8: no Linux-only default). Linux and the container:
/// <c>/var/lib/goldmark/&lt;name&gt;</c>. Windows: <c>%ProgramData%\Goldmark\&lt;name&gt;</c>, where the
/// installer puts them (DEC-043). Settings override every one.
/// </summary>
internal static class DefaultPaths
{
    public static string Data(string name) => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Goldmark", name)
        : $"/var/lib/goldmark/{name}";
}
