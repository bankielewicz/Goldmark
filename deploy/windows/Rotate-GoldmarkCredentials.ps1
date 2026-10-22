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
$SettingsFile = Join-Path $DataDirectory 'settings\goldmark.json'

function Fail([string]$Message) {
    Write-Host "Rotate-GoldmarkCredentials: $Message" -ForegroundColor Red
    exit 1
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
Restart-Service -Name $ServiceName

exit 0
