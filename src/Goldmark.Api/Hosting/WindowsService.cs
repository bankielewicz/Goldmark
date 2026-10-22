using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Goldmark.Api.Hosting;

/// <summary>
/// The Windows service distribution's host (DEC-043). The installer creates the service <c>Goldmark</c> and
/// the Application log source of the same name.
/// </summary>
internal static class WindowsService
{
    public const string ServiceName = "Goldmark";

    /// <summary>Application log events: 2 = the configuration is invalid, 3 = the start was refused.</summary>
    public const int InvalidConfigurationEvent = 2;
    public const int RefusedEvent = 3;

    /// <summary>ERROR_INVALID_DATA, which Windows shows as "The data is invalid."; the Application log says what.</summary>
    public const int RefusedExitCode = 13;

    public static bool IsRunning => OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService();

    /// <summary>
    /// A start refused before the host runs. The reason goes to the Application log (setting names or a
    /// reason only, never a value: the log is readable by anyone signed in on the server, DEC-043 item 4).
    /// Then the service reports to Windows that it stopped with an error. Measured on 2026-10-22: a process
    /// that just exits looks like a crash, which restart-on-failure actions repeat without end, or leaves the
    /// start hanging 30 s; a reported stop is not restarted.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static void RefuseStart(string reason, int eventId)
    {
        WriteEvent(reason, eventId);
        try
        {
            ServiceBase.Run(new RefusedStart());
        }
        catch (InvalidOperationException)
        {
            // OnStart's refusal, rethrown by ServiceBase.Run after it reported the stop.
        }
    }

    /// <summary>
    /// An error in the Application log under the source <c>Goldmark</c>, which the installer registers. Only a
    /// refusal's reason goes there, never a secret: anyone signed in on the server can read that log.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static void WriteEvent(string reason, int eventId)
    {
        try
        {
            EventLog.WriteEntry(ServiceName, reason, EventLogEntryType.Error, eventId);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            // The source is missing (it takes an administrator to create): the service's state still says it failed.
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed class RefusedStart : ServiceBase
    {
        public RefusedStart()
        {
            ServiceName = WindowsService.ServiceName;
            AutoLog = false;
        }

        protected override void OnStart(string[] args)
        {
            // A start that throws is reported as stopped, with this exit code.
            ExitCode = RefusedExitCode;
            throw new InvalidOperationException("Goldmark refused to start.");
        }
    }
}
