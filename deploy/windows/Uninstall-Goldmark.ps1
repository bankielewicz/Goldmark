<#
.SYNOPSIS
Removes the Goldmark Windows service (DEC-043). Keeps the data unless -RemoveData is given.

.DESCRIPTION
Stops and deletes the service, its firewall rule and its Application log source, and removes the program folder.
The data folder (settings, sign-in keys, TLS, credentials, logs) and the database stay, so installing again
picks them up; with -RemoveData the data folder is deleted too. The database is never dropped: a DBA does that.
Run elevated.
#>
[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:ProgramFiles 'Goldmark'),
    [string]$DataDirectory = (Join-Path $env:ProgramData 'Goldmark'),
    [switch]$RemoveData
)

Set-StrictMode -Version 3
$ErrorActionPreference = 'Stop'
$ServiceName = 'Goldmark'

# Stops the service and waits for its process to exit: the process outlives the reported stop and keeps the
# program's files open until it has. Its ID comes from Windows; another account's process doesn't show its path.
function Stop-GoldmarkService {
    $processId = (Get-CimInstance Win32_Service -Filter "Name='Goldmark'").ProcessId
    Stop-Service -Name 'Goldmark' -Force -ErrorAction SilentlyContinue
    if ($processId) { Wait-Process -Id $processId -Timeout 120 -ErrorAction SilentlyContinue }
}

# Retries for a short while: a scanner can still hold a file the process just closed.
function Remove-Folder([string]$Folder) {
    for ($i = 1; ; $i++) {
        try { Remove-Item -Recurse -Force $Folder; return }
        catch { if ($i -ge 10) { throw } ; Start-Sleep -Seconds 3 }
    }
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Uninstall-Goldmark: run this from an elevated (Run as administrator) PowerShell window.' -ForegroundColor Red
    exit 1
}

# The installed service's own folders, unless given (Codex on #39), read before the service is deleted: its program
# path and its settings file's data folder.
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

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "-- Stopping and deleting the service $ServiceName"
    Stop-GoldmarkService
    & sc.exe delete $ServiceName | Out-Null
}
Remove-NetFirewallRule -Name 'Goldmark-HTTPS' -ErrorAction SilentlyContinue
if ([Diagnostics.EventLog]::SourceExists($ServiceName)) { [Diagnostics.EventLog]::DeleteEventSource($ServiceName) }
if (Test-Path $InstallDirectory) {
    Write-Host "-- Removing $InstallDirectory"
    Remove-Folder $InstallDirectory
}
if ($RemoveData -and (Test-Path $DataDirectory)) {
    Write-Host "-- Removing $DataDirectory (settings, sign-in keys, TLS, credentials, logs)"
    Remove-Folder $DataDirectory
}
elseif (Test-Path $DataDirectory) {
    Write-Host "-- Kept $DataDirectory and the database: installing again picks them up (give -DataDirectory $DataDirectory if it isn't the default)."
}
exit 0
