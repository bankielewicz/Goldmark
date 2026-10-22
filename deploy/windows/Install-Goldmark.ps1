<#
.SYNOPSIS
Installs or upgrades Goldmark as a Windows service (DEC-043). Run it again to upgrade.

.DESCRIPTION
Run from the unpacked zip, in an elevated Windows PowerShell 5.1 or PowerShell 7 window. Every value can be
given as a parameter; a value that is missing is asked for, unless -NonInteractive is given (CI), and on an
upgrade the values in the existing settings file are the defaults. The steps:

  1. The service account: a gMSA (CORP\svc-goldmark$), a domain user (CORP\svc-goldmark, its password typed
     once and handed to Windows, never stored by Goldmark), or NT SERVICE\Goldmark when SQL Server is on this
     server. Never LocalSystem or NetworkService.
  2. The program in -InstallDirectory; the data folders under -DataDirectory, each readable by SYSTEM,
     Administrators and the service account only (settings, keys, tls, credentials, logs).
  3. The settings file (settings\goldmark.json), which the service reads through Goldmark__SettingsFile.
  4. The database: bootstrap-db and migrate, as you (Windows authentication) or as the SQL administrator login
     you type; no administrator or migration password is stored.
  5. The service, with restart-on-crash, the Application log source "Goldmark", a firewall rule; started and
     checked (/health/ready).

SQL Server must present a TLS certificate Goldmark can validate: one issued by a CA this server trusts, or the
certificate file given with -SqlCertificatePath (pinned). Goldmark never turns certificate validation off.
See docs/ops/windows-service.md.
#>
[CmdletBinding()]
param(
    [string]$SqlHost,
    [Nullable[int]]$SqlPort,
    [string]$Database,
    [ValidateSet('', 'Windows', 'Sql')][string]$Authentication = '',
    [string]$ServiceAccount,
    [securestring]$ServiceAccountPassword,
    [ValidateSet('', 'Windows', 'Sql')][string]$AdminAuthentication = '',
    [string]$SqlAdminUser,
    [securestring]$SqlAdminPassword,
    [string]$SqlCertificatePath,
    [string]$SqlHostNameInCertificate,
    [Nullable[int]]$HttpsPort,
    [string]$Hostnames,
    [string]$CertificatePath,
    [string]$KeyPath,
    [string]$AcceptedFindings,
    [Nullable[int]]$AuditRetentionDays,
    [string]$InstallDirectory = (Join-Path $env:ProgramFiles 'Goldmark'),
    [string]$DataDirectory = (Join-Path $env:ProgramData 'Goldmark'),
    [switch]$NoStart,
    [switch]$NonInteractive
)

Set-StrictMode -Version 3
$ErrorActionPreference = 'Stop'
$ServiceName = 'Goldmark'
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProgramSource = Join-Path $Here 'Goldmark'
$Exe = Join-Path $InstallDirectory 'Goldmark.Api.exe'
$SettingsFile = Join-Path $DataDirectory 'settings\goldmark.json'

function Fail([string]$Message) {
    Write-Host "Install-Goldmark: $Message" -ForegroundColor Red
    exit 1
}

function Step([string]$Message) { Write-Host "-- $Message" -ForegroundColor Cyan }

# Stops the service and waits for its process to exit: the process outlives the reported stop and keeps the
# program's files open until it has. Its ID comes from Windows; another account's process doesn't show its path.
function Stop-GoldmarkService {
    $processId = (Get-CimInstance Win32_Service -Filter "Name='Goldmark'").ProcessId
    Stop-Service -Name 'Goldmark' -Force -ErrorAction SilentlyContinue
    if ($processId) { Wait-Process -Id $processId -Timeout 120 -ErrorAction SilentlyContinue }
}


# A value from the parameter, else the existing settings, else a prompt (or a failure with -NonInteractive).
function Value([string]$Name, $Given, $Existing, [string]$Prompt, $Default = $null) {
    if ($null -ne $Given -and "$Given" -ne '') { return $Given }
    if ($null -ne $Existing -and "$Existing" -ne '') { return $Existing }
    if ($NonInteractive) {
        if ($null -ne $Default) { return $Default }
        Fail "-$Name is required with -NonInteractive."
    }
    $suffix = if ($null -ne $Default) { " [$Default]" } else { '' }
    $answer = Read-Host "$Prompt$suffix"
    if ($answer -eq '' -and $null -ne $Default) { return $Default }
    if ($answer -eq '') { Fail "-$Name is required." }
    return $answer
}

function Secret([string]$Name, [securestring]$Given, [string]$Prompt) {
    if ($null -ne $Given) { return $Given }
    if ($NonInteractive) { Fail "-$Name is required with -NonInteractive." }
    return Read-Host $Prompt -AsSecureString
}

function Plain([securestring]$Secure) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

function Get-Setting($Settings, [string[]]$Path) {
    $node = $Settings
    foreach ($key in $Path) {
        if ($null -eq $node -or -not ($node.PSObject.Properties.Name -contains $key)) { return $null }
        $node = $node.$key
    }
    return $node
}

function Test-LocalHost([string]$Name) {
    $local = @('localhost', '127.0.0.1', '::1', '.', $env:COMPUTERNAME)
    try { $local += [Net.Dns]::GetHostEntry($env:COMPUTERNAME).HostName } catch { }
    return $local -contains $Name
}

# Owner-only folder: SYSTEM, Administrators and the service account, inheritance off (DEC-043 item 5). The
# service gets only what it needs: read for the settings, which it never writes; modify for the others.
function New-PrivateFolder([string]$Path, [Security.Principal.SecurityIdentifier]$Account, [string]$AccountRights) {
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    $inherit = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    $none = [Security.AccessControl.PropagationFlags]::None
    foreach ($sid in @(
            (New-Object Security.Principal.SecurityIdentifier 'S-1-5-18'),
            (New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-544'))) {
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', $inherit, $none, 'Allow')))
    }
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($Account, $AccountRights, $inherit, $none, 'Allow')))
    $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-544'))
    Set-Acl -Path $Path -AclObject $acl
}

# "Log on as a service" for a domain account or gMSA (a virtual account has it already). sc.exe doesn't grant it.
function Grant-ServiceLogonRight([Security.Principal.SecurityIdentifier]$Account) {
    if (-not ('Goldmark.Lsa' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace Goldmark {
public static class Lsa {
    [StructLayout(LayoutKind.Sequential)] struct LSA_UNICODE_STRING { public ushort Length, MaximumLength; public IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)] struct LSA_OBJECT_ATTRIBUTES { public int Length; public IntPtr RootDirectory, ObjectName; public int Attributes; public IntPtr SecurityDescriptor, SecurityQualityOfService; }
    [DllImport("advapi32.dll")] static extern uint LsaOpenPolicy(IntPtr system, ref LSA_OBJECT_ATTRIBUTES attributes, int access, out IntPtr handle);
    [DllImport("advapi32.dll")] static extern uint LsaAddAccountRights(IntPtr handle, byte[] sid, LSA_UNICODE_STRING[] rights, int count);
    [DllImport("advapi32.dll")] static extern uint LsaClose(IntPtr handle);
    [DllImport("advapi32.dll")] static extern int LsaNtStatusToWinError(uint status);
    public static int Grant(byte[] sid, string right) {
        var attributes = new LSA_OBJECT_ATTRIBUTES();
        IntPtr policy;
        uint status = LsaOpenPolicy(IntPtr.Zero, ref attributes, 0x00000800 | 0x00000010, out policy);
        if (status != 0) return LsaNtStatusToWinError(status);
        try {
            var name = new LSA_UNICODE_STRING { Buffer = Marshal.StringToHGlobalUni(right), Length = (ushort)(right.Length * 2), MaximumLength = (ushort)((right.Length + 1) * 2) };
            try { return LsaNtStatusToWinError(LsaAddAccountRights(policy, sid, new[] { name }, 1)); }
            finally { Marshal.FreeHGlobal(name.Buffer); }
        } finally { LsaClose(policy); }
    }
}
}
'@
    }
    $bytes = New-Object byte[] $Account.BinaryLength
    $Account.GetBinaryForm($bytes, 0)
    $code = [Goldmark.Lsa]::Grant($bytes, 'SeServiceLogonRight')
    if ($code -ne 0) { Fail "could not grant 'Log on as a service' to the service account (Windows error $code)." }
}

# Runs a Goldmark command with the settings file, and the SQL administrator's password in its environment only.
function Invoke-Goldmark([string[]]$Commands, [string]$AdminPassword) {
    $env:Goldmark__SettingsFile = $SettingsFile
    if ($AdminPassword) { $env:Goldmark__Bootstrap__AdminPassword = $AdminPassword }
    # Its output goes to the host, so a transcript keeps it; under 'Stop', Windows PowerShell would turn its first
    # line on stderr into an exception.
    $preference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Exe @Commands 2>&1 | ForEach-Object { Write-Host "   $_" }
        return $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $preference
        Remove-Item Env:Goldmark__Bootstrap__AdminPassword -ErrorAction SilentlyContinue
        Remove-Item Env:Goldmark__SettingsFile -ErrorAction SilentlyContinue
    }
}

# /health/ready over HTTPS, trusting only the certificate Goldmark serves from its folder (no validation turned
# off). C#, because Windows PowerShell can't run a script block on the thread that checks the certificate.
function Test-Ready([string]$Url, [string]$CertificateFile) {
    if (-not ('Goldmark.Ready' -as [type])) {
        Add-Type -ReferencedAssemblies System.Net.Http -TypeDefinition @'
using System;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
namespace Goldmark {
public static class Ready {
    public static int Status(string url, string certificateFile) {
        string expected = new X509Certificate2(certificateFile).GetCertHashString();
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
            string.Equals(certificate.GetCertHashString(), expected, StringComparison.OrdinalIgnoreCase);
        using (var client = new HttpClient(handler)) {
            try { return (int)client.GetAsync(url).GetAwaiter().GetResult().StatusCode; }
            catch (HttpRequestException) { return 0; }
        }
    }
}
}
'@
    }
    return [Goldmark.Ready]::Status($Url, $CertificateFile)
}

# --- 0. Preconditions --------------------------------------------------------------------------------------
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Fail 'run this from an elevated (Run as administrator) PowerShell window.'
}
if (-not (Test-Path (Join-Path $ProgramSource 'Goldmark.Api.exe'))) {
    Fail "the program folder '$ProgramSource' is missing: run this script from the unpacked Goldmark zip."
}

$existing = $null
if (Test-Path $SettingsFile) {
    Step "Upgrading: the values in $SettingsFile are the defaults"
    $existing = Get-Content -Raw $SettingsFile | ConvertFrom-Json
}

# --- 1. Values -----------------------------------------------------------------------------------------
$SqlHost = Value 'SqlHost' $SqlHost (Get-Setting $existing 'Goldmark', 'Database', 'Host') 'SQL Server host name'
$SqlPort = [int](Value 'SqlPort' $SqlPort (Get-Setting $existing 'Goldmark', 'Database', 'Port') 'SQL Server TCP port' 1433)
$Database = Value 'Database' $Database (Get-Setting $existing 'Goldmark', 'Database', 'Name') 'Database name' 'Goldmark'
$Authentication = Value 'Authentication' $Authentication (Get-Setting $existing 'Goldmark', 'Database', 'Authentication') `
    'How the service signs in to SQL Server: Windows (recommended) or Sql' 'Windows'
if ($Authentication -notin 'Windows', 'Sql') { Fail '-Authentication must be Windows or Sql.' }
$local = Test-LocalHost $SqlHost
$defaultAccount = if ($local) { 'NT SERVICE\Goldmark' } else { $null }
$ServiceAccount = Value 'ServiceAccount' $ServiceAccount (Get-Setting $existing 'Goldmark', 'Service', 'Account') `
    'Service account (a gMSA such as CORP\svc-goldmark$, or a domain user)' $defaultAccount
if ($ServiceAccount -match '^(NT AUTHORITY\\|LocalSystem$|.*\\SYSTEM$)' -or $ServiceAccount -in 'LocalSystem', 'NetworkService', 'LocalService') {
    Fail 'the service account must not be LocalSystem, NetworkService or LocalService (DEC-043 item 1).'
}
$virtual = $ServiceAccount -like 'NT SERVICE\*'
if ($virtual -and $ServiceAccount -ne 'NT SERVICE\Goldmark') { Fail 'the only virtual account Goldmark uses is NT SERVICE\Goldmark.' }
if ($virtual -and -not $local) {
    Fail 'NT SERVICE\Goldmark reaches a remote SQL Server as this computer''s account; use a gMSA or a domain user (DEC-043 item 1).'
}
$managed = $ServiceAccount.EndsWith('$')
$AdminAuthentication = Value 'AdminAuthentication' $AdminAuthentication (Get-Setting $existing 'Goldmark', 'Bootstrap', 'AdminAuthentication') `
    'How you sign in to SQL Server to set up the database: Windows (as you) or Sql (an administrator login)' 'Windows'
$adminPassword = $null
if ($AdminAuthentication -eq 'Sql') {
    $SqlAdminUser = Value 'SqlAdminUser' $SqlAdminUser (Get-Setting $existing 'Goldmark', 'Bootstrap', 'AdminUser') 'SQL administrator login (CONTROL SERVER)'
    $adminPassword = Plain (Secret 'SqlAdminPassword' $SqlAdminPassword "Password of $SqlAdminUser")
}
$SqlCertificatePath = Value 'SqlCertificatePath' $SqlCertificatePath (Get-Setting $existing 'Goldmark', 'Database', 'ServerCertificatePath') `
    'SQL Server certificate file to pin (empty: its CA must be trusted here)' ''
$SqlHostNameInCertificate = Value 'SqlHostNameInCertificate' $SqlHostNameInCertificate (Get-Setting $existing 'Goldmark', 'Database', 'HostNameInCertificate') `
    'Host name in the SQL Server certificate, if it differs' ''
$HttpsPort = [int](Value 'HttpsPort' $HttpsPort (Get-Setting $existing 'Goldmark', 'Https', 'Port') 'HTTPS port' 443)
$CertificatePath = Value 'CertificatePath' $CertificatePath (Get-Setting $existing 'Goldmark', 'Https', 'CertificatePath') `
    'Your HTTPS certificate (PEM; empty: Goldmark generates a self-signed one)' ''
if ($CertificatePath) {
    $KeyPath = Value 'KeyPath' $KeyPath (Get-Setting $existing 'Goldmark', 'Https', 'KeyPath') 'Its private key (PEM, unencrypted)'
}
$fqdn = try { [Net.Dns]::GetHostEntry($env:COMPUTERNAME).HostName } catch { $env:COMPUTERNAME }
$Hostnames = Value 'Hostnames' $Hostnames (Get-Setting $existing 'Goldmark', 'Https', 'Hostnames') `
    'Host names users type' "$fqdn,$($env:COMPUTERNAME),localhost"
$AcceptedFindings = Value 'AcceptedFindings' $AcceptedFindings (Get-Setting $existing 'Goldmark', 'Bootstrap', 'AcceptedFindings') `
    'Accepted principal-audit findings (usually none)' ''
$AuditRetentionDays = [int](Value 'AuditRetentionDays' $AuditRetentionDays (Get-Setting $existing 'Goldmark', 'Audit', 'RetentionDays') `
    'Days the audit log keeps entries (365 to 36500, or 0 for ever)' 365)
$servicePassword = $null
if (-not $virtual -and -not $managed) {
    $servicePassword = Plain (Secret 'ServiceAccountPassword' $ServiceAccountPassword "Password of $ServiceAccount (given to Windows only)")
}

# --- 2. The service, so that NT SERVICE\Goldmark exists, then the program --------------------------------
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne 'Stopped') {
    Step 'Stopping the service'
    Stop-GoldmarkService
}
Step "Program: $InstallDirectory"
New-Item -ItemType Directory -Force -Path $InstallDirectory | Out-Null
Copy-Item -Path (Join-Path $ProgramSource '*') -Destination $InstallDirectory -Recurse -Force
$binPath = "`"$Exe`""
# The account is set before anything runs as it: the service never runs as LocalSystem. Windows PowerShell drops
# an empty argument, so password= is passed only for a domain user.
$account = @('obj=', $ServiceAccount)
if ($servicePassword) { $account += @('password=', $servicePassword) }
if (-not $service) {
    Step "Service $ServiceName, as $ServiceAccount"
    & sc.exe create $ServiceName binPath= $binPath start= delayed-auto DisplayName= 'Goldmark CMDB' @account | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "sc.exe create failed ($LASTEXITCODE): check the service account and its password." }
}
else {
    & sc.exe config $ServiceName binPath= $binPath start= delayed-auto @account | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "sc.exe config failed ($LASTEXITCODE): check the service account and its password." }
}
$servicePassword = $null
try {
    $accountSid = (New-Object Security.Principal.NTAccount $ServiceAccount).Translate([Security.Principal.SecurityIdentifier])
}
catch { Fail "Windows doesn't know the account '$ServiceAccount'." }
if (-not $virtual) { Grant-ServiceLogonRight $accountSid }
& sc.exe description $ServiceName 'Goldmark configuration management database (https://github.com/bankielewicz/Goldmark)' | Out-Null
# Restart after a crash: after 1 minute, twice, then not again that day. A refused start reports a stop with an
# error, which Windows doesn't restart (DEC-043 item 11).
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000//0 | Out-Null
& sc.exe failureflag $ServiceName 0 | Out-Null
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name Environment -Type MultiString `
    -Value @("Goldmark__SettingsFile=$SettingsFile")

# --- 3. Data folders and the settings file ---------------------------------------------------------------
Step "Data: $DataDirectory (SYSTEM, Administrators and $ServiceAccount only)"
New-Item -ItemType Directory -Force -Path $DataDirectory | Out-Null
New-PrivateFolder (Join-Path $DataDirectory 'settings') $accountSid 'ReadAndExecute'
foreach ($folder in 'keys', 'tls', 'credentials', 'logs') {
    New-PrivateFolder (Join-Path $DataDirectory $folder) $accountSid 'Modify'
}

$settings = [ordered]@{
    Goldmark = [ordered]@{
        Service        = [ordered]@{ Account = $ServiceAccount }
        Database       = [ordered]@{
            Host = $SqlHost; Port = $SqlPort; Name = $Database; Authentication = $Authentication; CheckRightsAtStart = $true
        }
        Migration      = [ordered]@{ Login = 'Administrator' }
        Bootstrap      = [ordered]@{ AdminAuthentication = $AdminAuthentication; AcceptedFindings = $AcceptedFindings }
        Audit          = [ordered]@{ RetentionDays = $AuditRetentionDays }
        DataProtection = [ordered]@{ KeyDirectory = (Join-Path $DataDirectory 'keys') }
        Https          = [ordered]@{ Port = $HttpsPort; Hostnames = $Hostnames; GeneratedCertificateDirectory = (Join-Path $DataDirectory 'tls') }
        Logging        = [ordered]@{ Directory = (Join-Path $DataDirectory 'logs') }
    }
}
if ($Authentication -eq 'Sql') { $settings.Goldmark.Database.CredentialFile = (Join-Path $DataDirectory 'credentials\runtime.json') }
if ($AdminAuthentication -eq 'Sql') { $settings.Goldmark.Bootstrap.AdminUser = $SqlAdminUser }
if ($SqlCertificatePath) { $settings.Goldmark.Database.ServerCertificatePath = $SqlCertificatePath }
if ($SqlHostNameInCertificate) { $settings.Goldmark.Database.HostNameInCertificate = $SqlHostNameInCertificate }
if ($CertificatePath) { $settings.Goldmark.Https.CertificatePath = $CertificatePath; $settings.Goldmark.Https.KeyPath = $KeyPath }
# The recovery setting (DEC-042 item 6) is kept on an upgrade; set it by editing this file and restarting.
$recover = Get-Setting $existing 'Goldmark', 'Auth'
if ($recover) { $settings.Goldmark.Auth = $recover }
Step "Settings: $SettingsFile"
$settings | ConvertTo-Json -Depth 5 | Set-Content -Path $SettingsFile -Encoding UTF8

# --- 4. The database: as you, or as the SQL administrator you typed ------------------------------------
Step 'Database: bootstrap-db, then migrate'
$code = Invoke-Goldmark @('bootstrap-db', 'migrate') $adminPassword
$adminPassword = $null
if ($code -ne 0) { Fail "the database setup stopped (exit $code); nothing more was changed. Fix what it printed above and run this again." }

# --- 5. Event log source, firewall, start ----------------------------------------------------------------
if (-not [Diagnostics.EventLog]::SourceExists($ServiceName)) {
    [Diagnostics.EventLog]::CreateEventSource($ServiceName, 'Application')
}
if (-not (Get-NetFirewallRule -Name 'Goldmark-HTTPS' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -Name 'Goldmark-HTTPS' -DisplayName 'Goldmark (HTTPS)' -Direction Inbound -Protocol TCP -LocalPort $HttpsPort -Action Allow | Out-Null
}
else {
    Set-NetFirewallRule -Name 'Goldmark-HTTPS' -LocalPort $HttpsPort | Out-Null
}

if ($NoStart) {
    Step 'Not started (-NoStart).'
    exit 0
}
Step 'Starting the service'
Start-Service -Name $ServiceName
$certificate = if ($CertificatePath) { $CertificatePath } else { Join-Path $DataDirectory 'tls\goldmark.crt' }
$status = 0
for ($i = 0; $i -lt 90 -and $status -ne 200; $i++) {
    Start-Sleep -Seconds 2
    if ((Get-Service -Name $ServiceName).Status -eq 'Stopped') { break }
    if (Test-Path $certificate) { $status = Test-Ready "https://localhost:$HttpsPort/health/ready" $certificate }
}
if ($status -ne 200) {
    Fail "the service didn't become ready. Its log: $(Join-Path $DataDirectory 'logs'); refusals also go to the Application log (source Goldmark)."
}
Write-Host ''
Write-Host "Goldmark is running: https://$($fqdn):$HttpsPort/" -ForegroundColor Green
$token = Get-ChildItem (Join-Path $DataDirectory 'logs') -Filter 'goldmark-*.json' | Sort-Object LastWriteTime |
    Select-String -Pattern '"EventId":\{"Id":3001' | Select-Object -Last 1
if ($token) {
    Write-Host 'First run: create the first administrator with the setup token in the log (event 3001):'
    Write-Host "  $($token.Path)"
}

exit 0
