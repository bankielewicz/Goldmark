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
# The parameters given, even empty: on an upgrade an empty optional value clears the setting (Codex on #39).
$BoundNames = @($PSBoundParameters.Keys)
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


# A value from the parameter, else the existing settings, else a prompt (or a failure with -NonInteractive). An
# optional value (default '') given as '' is cleared.
function Value([string]$Name, $Given, $Existing, [string]$Prompt, $Default = $null) {
    if ($null -ne $Given -and "$Given" -ne '') { return $Given }
    if ($BoundNames -contains $Name -and $null -ne $Default -and "$Default" -eq '') { return '' }
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
        if ($null -eq $node -or $null -eq $node.PSObject.Properties[$key]) { return $null }
        $node = $node.$key
    }
    return $node
}

# Sets a value in the settings tree, creating the sections on the way; $null removes it. Everything else in an
# existing file stays, such as Https:Hsts or Logging:RetainedFiles (Codex on #39).
function Set-Setting($Node, [string[]]$Path, $Value) {
    for ($i = 0; $i -lt $Path.Count - 1; $i++) {
        $key = $Path[$i]
        if ($null -eq $Node.PSObject.Properties[$key] -or $null -eq $Node.$key) {
            $Node | Add-Member -NotePropertyName $key -NotePropertyValue ([pscustomobject]@{}) -Force
        }
        $Node = $Node.$key
    }
    $leaf = $Path[$Path.Count - 1]
    if ($null -eq $Value) { $Node.PSObject.Properties.Remove($leaf) }
    else { $Node | Add-Member -NotePropertyName $leaf -NotePropertyValue $Value -Force }
}

function Get-AccountSid([string]$Name) {
    try { return (New-Object Security.Principal.NTAccount ($Name -replace '^\.\\', "$env:COMPUTERNAME\")).Translate([Security.Principal.SecurityIdentifier]) }
    catch { return $null }
}

# Why an existing file or folder can't be trusted, or $null (Codex on #39). Every user may create folders in
# ProgramData, so a data folder someone else made could hold settings that send your SQL administrator's password to
# their server, or keys they know. This installer makes Administrators the owner of what it creates.
function Get-UntrustedReason([string]$Path, [string[]]$Owners) {
    if ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { return "$Path is a link" }
    $owner = (Get-Acl -LiteralPath $Path).GetOwner([Security.Principal.SecurityIdentifier])
    if ($Owners -contains $owner.Value) { return $null }
    $name = try { $owner.Translate([Security.Principal.NTAccount]).Value } catch { $owner.Value }
    return "$Path is owned by $name"
}

# Only what its folder grants, and owned by Administrators, so any administrator's later run trusts it.
function Set-PrivateFile([string]$Path) {
    & icacls.exe $Path /reset /Q | Out-Null
    if ($LASTEXITCODE -eq 0) { & icacls.exe $Path /setowner '*S-1-5-32-544' /Q | Out-Null }
    if ($LASTEXITCODE -ne 0) { Fail "could not set the permissions of $Path (icacls exit $LASTEXITCODE)." }
}

# An operator's certificate or key, copied into the data folder: its own path may be readable by others, or relative
# to where this ran (Codex on #39). Returns the copy's path.
function Copy-Private([string]$Name, [string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { Fail "-$Name '$Source' isn't a file." }
    $from = (Resolve-Path -LiteralPath $Source).ProviderPath
    if ($from -ne $Destination) {
        Copy-Item -LiteralPath $from -Destination $Destination -Force
        Set-PrivateFile $Destination
    }
    return $Destination
}

# The process outlives the service's stop by a moment and keeps its files open: try for a while.
function Remove-Tree([string]$Path) {
    for ($i = 0; $i -lt 10; $i++) {
        try { Remove-Item -LiteralPath $Path -Recurse -Force; return }
        catch { Start-Sleep -Seconds 2 }
    }
    Write-Host "   could not remove $Path yet (a file is still in use); remove it later." -ForegroundColor Yellow
}

function Rename-Tree([string]$Path, [string]$NewName) {
    for ($i = 0; $i -lt 10; $i++) {
        try { Rename-Item -LiteralPath $Path -NewName $NewName; return }
        catch { Start-Sleep -Seconds 2 }
    }
    Fail "could not rename $Path (a file is still in use); nothing else was changed in it, run this again."
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
        // Each poll waits 5 s at most: a server that accepts the connection but never answers is a failed poll,
        // not the client's 100 s default and an exception (Codex on #39).
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

# An upgrade uses the data folder the service was installed with (Codex on #39): the settings file named in its
# environment, unless -DataDirectory names the same folder.
$existingService = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
$serviceEnvironment = (Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name Environment -ErrorAction SilentlyContinue)
$registered = if ($serviceEnvironment) { @($serviceEnvironment.Environment) | Where-Object { $_ -like 'Goldmark__SettingsFile=*' } | Select-Object -First 1 } else { $null }
if ($existingService -and $registered) {
    $registeredData = Split-Path -Parent (Split-Path -Parent $registered.Substring('Goldmark__SettingsFile='.Length))
    if ($BoundNames -notcontains 'DataDirectory') { $DataDirectory = $registeredData }
    elseif ([IO.Path]::GetFullPath($DataDirectory).TrimEnd('\') -ne [IO.Path]::GetFullPath($registeredData).TrimEnd('\')) {
        Fail "the installed service uses the data folder $registeredData, not $DataDirectory. Run this again without -DataDirectory, or uninstall first."
    }
    $SettingsFile = Join-Path $DataDirectory 'settings\goldmark.json'
}

# What was there before, only if Administrators, SYSTEM, you or the installed service's account made it.
$owners = @('S-1-5-18', 'S-1-5-32-544', [Security.Principal.WindowsIdentity]::GetCurrent().User.Value)
if ($existingService -and (Get-AccountSid $existingService.StartName)) { $owners += (Get-AccountSid $existingService.StartName).Value }
if (Test-Path -LiteralPath $DataDirectory) {
    $reasons = @()
    foreach ($path in @($DataDirectory, $SettingsFile) + @('settings', 'keys', 'tls', 'credentials', 'logs' | ForEach-Object { Join-Path $DataDirectory $_ })) {
        if (Test-Path -LiteralPath $path) {
            $reason = Get-UntrustedReason $path $owners
            if ($reason) { $reasons += $reason }
        }
    }
    if ($reasons.Count -gt 0) {
        Fail ("$DataDirectory can't be trusted, so nothing in it was used and nothing was changed: $($reasons -join '; '). " +
            'If an administrator made it, make Administrators its owner (icacls <folder> /setowner *S-1-5-32-544 /T); otherwise remove it, or choose another -DataDirectory.')
    }
}

# The program folder is replaced whole on an upgrade, so it must be Goldmark's own or empty.
$installFull = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
$sourceFull = (Resolve-Path -LiteralPath $ProgramSource).ProviderPath.TrimEnd('\')
if ($sourceFull -eq $installFull -or $sourceFull.StartsWith("$installFull\", [StringComparison]::OrdinalIgnoreCase) -or
    $installFull.StartsWith("$sourceFull\", [StringComparison]::OrdinalIgnoreCase)) {
    Fail "unpack the zip outside $InstallDirectory, then run this again."
}
if ((Test-Path -LiteralPath $installFull) -and (Get-ChildItem -LiteralPath $installFull -Force | Select-Object -First 1) -and
    -not (Test-Path -LiteralPath (Join-Path $installFull 'Goldmark.Api.exe'))) {
    Fail "$InstallDirectory holds other files and no Goldmark.Api.exe: choose an empty folder, or Goldmark's own."
}

$existing = $null
if (Test-Path -LiteralPath $SettingsFile) {
    Step "Upgrading: the values in $SettingsFile are the defaults"
    $existing = Get-Content -Raw -LiteralPath $SettingsFile | ConvertFrom-Json
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
# An upgrade keeps the service's account, and Windows keeps a domain user's password, which is asked for only when
# given or for a new service (Codex on #39). Changing the account would leave the old one's login in goldmark_runtime,
# which bootstrap-db refuses after the service had been changed: not supported yet, so refused before anything changes.
$sameAccount = $false
if ($existingService) {
    $current = Get-AccountSid $existingService.StartName
    $wanted = Get-AccountSid $ServiceAccount
    if (-not $current -or -not $wanted -or $current.Value -ne $wanted.Value) {
        Fail "the service runs as $($existingService.StartName), and changing its account isn't supported yet (#41). Run this again with -ServiceAccount '$($existingService.StartName)'."
    }
    $sameAccount = $true
}
$servicePassword = $null
if (-not $virtual -and -not $managed -and (-not $sameAccount -or $BoundNames -contains 'ServiceAccountPassword')) {
    $servicePassword = Plain (Secret 'ServiceAccountPassword' $ServiceAccountPassword "Password of $ServiceAccount (given to Windows only)")
}

# --- 2. The service, so that NT SERVICE\Goldmark exists, then the program --------------------------------
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne 'Stopped') {
    Step 'Stopping the service'
    Stop-GoldmarkService
}
Step "Program: $InstallDirectory"
# The new release replaces the old one whole, so files it no longer has don't stay behind (Codex on #39): copied
# beside it first, then swapped in.
$staging = "$installFull.new"
$previous = "$installFull.old"
foreach ($leftover in $staging, $previous) { if (Test-Path -LiteralPath $leftover) { Remove-Tree $leftover } }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $installFull) | Out-Null
Copy-Item -LiteralPath $sourceFull -Destination $staging -Recurse
if (Test-Path -LiteralPath $installFull) { Rename-Tree $installFull (Split-Path -Leaf $previous) }
Rename-Tree $staging (Split-Path -Leaf $installFull)
if (Test-Path -LiteralPath $previous) { Remove-Tree $previous }
$binPath = "`"$Exe`""
# The account is set before anything runs as it: the service never runs as LocalSystem; an upgrade leaves it as it
# is unless a password is given. Windows PowerShell drops an empty argument, so password= is passed only for a
# domain user.
$account = @()
if (-not $sameAccount -or $servicePassword) { $account = @('obj=', $ServiceAccount) }
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
# The data folder itself: Administrators own it and nobody else may create anything in it (Codex on #39).
New-PrivateFolder $DataDirectory $accountSid 'ReadAndExecute'
New-PrivateFolder (Join-Path $DataDirectory 'settings') $accountSid 'ReadAndExecute'
foreach ($folder in 'keys', 'tls', 'credentials', 'logs') {
    New-PrivateFolder (Join-Path $DataDirectory $folder) $accountSid 'Modify'
}

# The existing settings with the installer's values set, and those it no longer needs removed; the rest stays.
$settings = if ($existing) { $existing } else { [pscustomobject]@{} }
Set-Setting $settings 'Goldmark', 'Service', 'Account' $ServiceAccount
Set-Setting $settings 'Goldmark', 'Database', 'Host' $SqlHost
Set-Setting $settings 'Goldmark', 'Database', 'Port' $SqlPort
Set-Setting $settings 'Goldmark', 'Database', 'Name' $Database
Set-Setting $settings 'Goldmark', 'Database', 'Authentication' $Authentication
Set-Setting $settings 'Goldmark', 'Database', 'CheckRightsAtStart' $true
$credentialFile = if ($Authentication -eq 'Sql') { Join-Path $DataDirectory 'credentials\runtime.json' } else { $null }
Set-Setting $settings 'Goldmark', 'Database', 'CredentialFile' $credentialFile
# A copy in the settings folder, which the service account can read; the operator's path may not be.
$pinned = if ($SqlCertificatePath) { Copy-Private 'SqlCertificatePath' $SqlCertificatePath (Join-Path $DataDirectory 'settings\sql-server.crt') } else { $null }
Set-Setting $settings 'Goldmark', 'Database', 'ServerCertificatePath' $pinned
Set-Setting $settings 'Goldmark', 'Database', 'HostNameInCertificate' $(if ($SqlHostNameInCertificate) { $SqlHostNameInCertificate } else { $null })
Set-Setting $settings 'Goldmark', 'Migration', 'Login' 'Administrator'
Set-Setting $settings 'Goldmark', 'Bootstrap', 'AdminAuthentication' $AdminAuthentication
Set-Setting $settings 'Goldmark', 'Bootstrap', 'AdminUser' $(if ($AdminAuthentication -eq 'Sql') { $SqlAdminUser } else { $null })
Set-Setting $settings 'Goldmark', 'Bootstrap', 'AcceptedFindings' $AcceptedFindings
Set-Setting $settings 'Goldmark', 'Audit', 'RetentionDays' $AuditRetentionDays
Set-Setting $settings 'Goldmark', 'DataProtection', 'KeyDirectory' (Join-Path $DataDirectory 'keys')
Set-Setting $settings 'Goldmark', 'Https', 'Port' $HttpsPort
Set-Setting $settings 'Goldmark', 'Https', 'Hostnames' $Hostnames
Set-Setting $settings 'Goldmark', 'Https', 'GeneratedCertificateDirectory' (Join-Path $DataDirectory 'tls')
# Your certificate and key, copied into the tls folder, which only SYSTEM, Administrators and the service read.
$operatorCertificate = if ($CertificatePath) { Copy-Private 'CertificatePath' $CertificatePath (Join-Path $DataDirectory 'tls\operator.crt') } else { $null }
$operatorKey = if ($CertificatePath) { Copy-Private 'KeyPath' $KeyPath (Join-Path $DataDirectory 'tls\operator.key') } else { $null }
Set-Setting $settings 'Goldmark', 'Https', 'CertificatePath' $operatorCertificate
Set-Setting $settings 'Goldmark', 'Https', 'KeyPath' $operatorKey
Set-Setting $settings 'Goldmark', 'Logging', 'Directory' (Join-Path $DataDirectory 'logs')
# The recovery setting (DEC-042 item 6), Goldmark:Auth, is kept like the rest; set it by editing this file and restarting.
Step "Settings: $SettingsFile"
$settings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $SettingsFile -Encoding UTF8
Set-PrivateFile $SettingsFile

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
$starting = Get-Date
Start-Service -Name $ServiceName
$certificate = if ($operatorCertificate) { $operatorCertificate } else { Join-Path $DataDirectory 'tls\goldmark.crt' }
$status = 0
for ($i = 0; $i -lt 90 -and $status -ne 200; $i++) {
    Start-Sleep -Seconds 2
    if ((Get-Service -Name $ServiceName).Status -eq 'Stopped') { break }
    if (Test-Path $certificate) { $status = Test-Ready "https://localhost:$HttpsPort/health/ready" $certificate }
}
if ($status -ne 200) {
    # Why: a refusal in the Application log, and the end of the log file without the two logged secrets.
    $refusal = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $starting } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -eq $ServiceName } | Select-Object -First 1
    if ($refusal) { Write-Host "Application log, source Goldmark (event $($refusal.Id)):"; Write-Host $refusal.Message }
    $log = Get-ChildItem (Join-Path $DataDirectory 'logs') -Filter 'goldmark-*.json' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
    if ($log) {
        Write-Host "The end of $($log.FullName):"
        Get-Content $log.FullName -Tail 20 | Where-Object { $_ -notmatch '"Id":300[14]\b' } | ForEach-Object { Write-Host "   $_" }
    }
    Fail "the service didn't become ready (state $((Get-Service -Name $ServiceName).Status)). Its log: $(Join-Path $DataDirectory 'logs'); refusals also go to the Application log (source Goldmark)."
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
