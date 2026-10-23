namespace Goldmark.Api.Configuration;

/// <summary>
/// The Windows service distribution's settings (section <c>Goldmark:Service</c>; DEC-043). The installer
/// writes them to the settings file; Docker leaves them unset.
/// </summary>
public sealed class ServiceOptions
{
    public const string SectionName = "Goldmark:Service";

    /// <summary>
    /// The Windows account the service runs as (<c>CORP\svc-goldmark$</c>, a domain user, or
    /// <c>NT SERVICE\Goldmark</c>), or its SID. Owner-only folders allow it besides SYSTEM and
    /// Administrators, and an operator's command encrypts the runtime login's password to it. Ignored
    /// outside Windows.
    /// </summary>
    public string? Account { get; set; }
}
