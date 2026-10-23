<#
.SYNOPSIS
Gives Goldmark's generated SQL logins new passwords, then restarts the service (DEC-043 item 7).

.DESCRIPTION
Runs rotate-db-credentials with the service's settings file, as you (Windows authentication) or as the SQL
administrator login whose password you type (it isn't stored), then restarts the service so it reads its new
password, which it encrypts to itself at that start. Under Windows authentication there is nothing to rotate.
Run elevated.
#>
[CmdletBinding()]
param(
    [securestring]$SqlAdminPassword,
    [string]$DataDirectory = (Join-Path $env:ProgramData 'Goldmark'),
    [string]$InstallDirectory = (Join-Path $env:ProgramFiles 'Goldmark'),
    [switch]$NonInteractive
)

Set-StrictMode -Version 3
$ErrorActionPreference = 'Stop'
$ServiceName = 'Goldmark'

function Fail([string]$Message) {
    Write-Host "Rotate-GoldmarkCredentials: $Message" -ForegroundColor Red
    exit 1
}

# The installed service's own folders, unless given (Codex on #39): its program path and settings file.
$service = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
if ($service) {
    if (-not $PSBoundParameters.ContainsKey('InstallDirectory') -and $service.PathName) {
        $InstallDirectory = Split-Path -Parent $service.PathName.Trim('"')
    }
    $environment = Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name Environment -ErrorAction SilentlyContinue
    $registered = if ($environment) { @($environment.Environment) | Where-Object { $_ -like 'Goldmark__SettingsFile=*' } | Select-Object -First 1 } else { $null }
    if (-not $PSBoundParameters.ContainsKey('DataDirectory') -and $registered) {
        $DataDirectory = Split-Path -Parent (Split-Path -Parent $registered.Substring('Goldmark__SettingsFile='.Length))
    }
}
$SettingsFile = Join-Path $DataDirectory 'settings\goldmark.json'

# /health/ready over HTTPS, trusting only the certificate Goldmark serves, 5 s per poll (as the installer does).
function Test-Ready([string]$Url, [string]$CertificateFile) {
    if (-not ('Goldmark.RotateReady' -as [type])) {
        Add-Type -ReferencedAssemblies System.Net.Http -TypeDefinition @'
using System;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
namespace Goldmark {
public static class RotateReady {
    public static int Status(string url, string certificateFile) {
        string expected = new X509Certificate2(certificateFile).GetCertHashString();
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
            string.Equals(certificate.GetCertHashString(), expected, StringComparison.OrdinalIgnoreCase);
        using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) }) {
            try { return (int)client.GetAsync(url).GetAwaiter().GetResult().StatusCode; }
            catch (HttpRequestException) { return 0; }
            catch (System.Threading.Tasks.TaskCanceledException) { return 0; }
        }
    }
}
}
'@
    }
    return [Goldmark.RotateReady]::Status($Url, $CertificateFile)
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { Fail 'run this from an elevated PowerShell window.' }
if (-not (Test-Path $SettingsFile)) { Fail "no settings file at ${SettingsFile}: is Goldmark installed?" }
$settings = Get-Content -Raw $SettingsFile | ConvertFrom-Json

$env:Goldmark__SettingsFile = $SettingsFile
try {
    if ($settings.Goldmark.Bootstrap.AdminAuthentication -eq 'Sql') {
        if ($null -eq $SqlAdminPassword) {
            if ($NonInteractive) { Fail '-SqlAdminPassword is required with -NonInteractive.' }
            $SqlAdminPassword = Read-Host "Password of $($settings.Goldmark.Bootstrap.AdminUser)" -AsSecureString
        }
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SqlAdminPassword)
        try { $env:Goldmark__Bootstrap__AdminPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    }
    # Its output goes to the host (kept by a transcript); 'Continue', or stderr would throw in Windows PowerShell.
    $ErrorActionPreference = 'Continue'
    & (Join-Path $InstallDirectory 'Goldmark.Api.exe') rotate-db-credentials 2>&1 | ForEach-Object { Write-Host "   $_" }
    $code = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = 'Stop'
    Remove-Item Env:Goldmark__Bootstrap__AdminPassword -ErrorAction SilentlyContinue
    Remove-Item Env:Goldmark__SettingsFile -ErrorAction SilentlyContinue
}
if ($code -ne 0) { Fail "rotate-db-credentials stopped (exit $code); see what it printed above." }

Write-Host '-- Restarting the service, so it reads its new password'
$restarting = Get-Date
Restart-Service -Name $ServiceName
# Ready means it signed in with the new password and passed its rights check (Codex on #39): a running process
# alone doesn't.
$https = $settings.Goldmark.Https
$generated = if ($https.PSObject.Properties['GeneratedCertificateDirectory']) { $https.GeneratedCertificateDirectory } else { Join-Path $DataDirectory 'tls' }
$certificate = if ($https.PSObject.Properties['CertificatePath'] -and $https.CertificatePath) { $https.CertificatePath } else { Join-Path $generated 'goldmark.crt' }
$status = 0
for ($i = 0; $i -lt 90 -and $status -ne 200; $i++) {
    Start-Sleep -Seconds 2
    if ((Get-Service -Name $ServiceName).Status -eq 'Stopped') { break }
    if (Test-Path $certificate) { $status = Test-Ready "https://localhost:$($https.Port)/health/ready" $certificate }
}
if ($status -ne 200) {
    # Why: a refusal in the Application log, and the end of the log file without the two logged secrets.
    $refusal = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $restarting } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -eq $ServiceName -and $_.Id -in 2, 3 } | Select-Object -First 1
    if ($refusal) { Write-Host "Application log, source Goldmark (event $($refusal.Id)):"; Write-Host $refusal.Message }
    $log = Get-ChildItem (Join-Path $DataDirectory 'logs') -Filter 'goldmark-*.json' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
    if ($log) {
        Write-Host "The end of $($log.FullName):"
        Get-Content $log.FullName -Tail 20 | Where-Object { $_ -notmatch '"Id":300[14]\b' } | ForEach-Object { Write-Host "   $_" }
    }
    Fail "the passwords were changed, but the service didn't become ready (state $((Get-Service -Name $ServiceName).Status)); see above."
}
Write-Host '-- Ready with its new password'
exit 0
