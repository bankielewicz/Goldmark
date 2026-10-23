<#
.SYNOPSIS
The Windows service smoke test (DEC-043 item 8): installs Goldmark from its zip on this computer against a local
SQL Server and checks the "Done when" items that only Windows can show. Run elevated, in Windows PowerShell 5.1.

.DESCRIPTION
1. Windows authentication as NT SERVICE\Goldmark: the install, readiness and the sign-in page, owner-only
   folders, the log file, nothing secret in the Application log, the encrypted key ring, the Windows login in
   goldmark_runtime and no migration login. 1b: an upgrade finds the service's data folder, keeps the settings it
   doesn't manage, clears an optional value given empty, and replaces the program folder whole.
2. Refusals: a right that reaches the service only through a Windows group stops it at start, and so does an
   invalid setting; each is written to the Application log, and Windows doesn't restart it. A group's user in
   another database stops it too, until that finding is accepted.
3. Uninstall keeps the data.
4. Data folders the installer can't trust are refused (a link, settings others may change, inside the program
   folder). A SQL login: the service encrypts its credential file to itself; rotation; uninstall with the data.
Everything it creates (three GoldmarkSmoke* databases, their logins, a local group and a SQL administrator login
for step 4, the folders under -Root) is removed at the end. The SQL Server instance must already present a
certificate Goldmark can pin (tests/windows/Set-SqlTestCertificate.ps1).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Zip,
    [Parameter(Mandatory)][string]$SqlCertificateFile,
    [string]$SqlHost = 'localhost',
    [string]$Root = (Join-Path $env:ProgramData 'Goldmark-test'),
    # Principal-audit findings this instance has beyond SQL Server 2025's defaults (for example a Developer
    # edition with Machine Learning Services); none on CI's fresh Express.
    [string]$AcceptedFindings = '',
    [int]$HttpsPort = 18444
)

Set-StrictMode -Version 3
$ErrorActionPreference = 'Stop'
$script:failures = 0
$suffix = Get-Random -Minimum 10000 -Maximum 99999
$databases = @("GoldmarkSmokeWin$suffix", "GoldmarkSmokeSql$suffix", "GoldmarkSmokeOther$suffix")
$group = "GoldmarkSmoke$suffix"
$adminLogin = "goldmark_smoke_admin_$suffix"
$adminPassword = 'Sm0ke-' + [Guid]::NewGuid().ToString('N')
$account = 'NT SERVICE\Goldmark'
$unpacked = Join-Path $Root "zip-$suffix"
$program = Join-Path $Root 'program'
$readyUrl = "https://localhost:$HttpsPort/health/ready"

function Check([string]$Name, [scriptblock]$Test) {
    try {
        $result = & $Test
        if ($result -eq $false) { throw 'returned false' }
        Write-Host "ok    $Name"
    }
    catch {
        $script:failures++
        Write-Host "FAIL  $Name : $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Sql([string]$Text, [string]$Database = 'master') {
    $connection = New-Object System.Data.SqlClient.SqlConnection "Server=$SqlHost;Database=$Database;Integrated Security=true"
    $connection.Open()
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Text
        $command.CommandTimeout = 60
        return $command.ExecuteScalar()
    }
    finally { $connection.Close() }
}

function Install([string]$Data, [hashtable]$Extra) {
    $arguments = @{
        NonInteractive = $true; SqlHost = $SqlHost; SqlCertificatePath = $SqlCertificateFile; ServiceAccount = $account
        HttpsPort = $HttpsPort; Hostnames = 'localhost'; InstallDirectory = $program; DataDirectory = $Data; AcceptedFindings = $AcceptedFindings
    }
    foreach ($key in $Extra.Keys) { $arguments[$key] = $Extra[$key] }
    $null = & (Join-Path $unpacked 'Install-Goldmark.ps1') @arguments
    return $LASTEXITCODE
}

function Ready([string]$Data) {
    $certificate = Join-Path $Data 'tls\goldmark.crt'
    for ($i = 0; $i -lt 60; $i++) {
        if ((Test-Path $certificate) -and [GoldmarkSmoke.Http]::Status($readyUrl, $certificate) -eq 200) { return $true }
        Start-Sleep -Seconds 2
    }
    return $false
}

# Stopped on its own within the time given, and still stopped after Windows's restart delay (60 s) has passed.
function Stays-Stopped([int]$Within) {
    for ($i = 0; $i -lt $Within -and (Get-Service Goldmark).Status -ne 'Stopped'; $i++) { Start-Sleep -Seconds 1 }
    if ((Get-Service Goldmark).Status -ne 'Stopped') { throw "still $((Get-Service Goldmark).Status) after $Within s" }
    $exit = (Get-CimInstance Win32_Service -Filter "Name='Goldmark'").ExitCode
    $script:lastExit = $exit
    Start-Sleep -Seconds 75
    if ((Get-Service Goldmark).Status -ne 'Stopped') { throw "restarted (exit code was $exit): a refused start must not loop" }
    Write-Host "      (stopped; Windows recorded exit code $exit)"
    return $true
}

function Goldmark-Event([int]$Id, [datetime]$Since) {
    # Filtering by ProviderName fails with "The parameter is incorrect" for a classic event source.
    return Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $Since } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -eq 'Goldmark' -and $_.Id -eq $Id }
}

function Private-Folder([string]$Path) {
    $acl = Get-Acl -Path $Path
    if (-not $acl.AreAccessRulesProtected) { throw "$Path inherits its permissions" }
    $allowed = @('S-1-5-18', 'S-1-5-32-544', (New-Object Security.Principal.NTAccount $account).Translate([Security.Principal.SecurityIdentifier]).Value)
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq 'Allow' -and $allowed -notcontains $rule.IdentityReference.Value) { throw "$Path grants $($rule.IdentityReference)" }
    }
    return $true
}

Add-Type -ReferencedAssemblies System.Net.Http -TypeDefinition @'
using System;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
namespace GoldmarkSmoke {
public static class Http {
    static HttpClient Client(string certificateFile) {
        string expected = new X509Certificate2(certificateFile).GetCertHashString();
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
            string.Equals(certificate.GetCertHashString(), expected, StringComparison.OrdinalIgnoreCase);
        return new HttpClient(handler);
    }
    public static int Status(string url, string certificateFile) {
        using (var client = Client(certificateFile)) {
            try { return (int)client.GetAsync(url).GetAwaiter().GetResult().StatusCode; } catch (HttpRequestException) { return 0; }
        }
    }
    public static string Text(string url, string certificateFile) {
        using (var client = Client(certificateFile)) { return client.GetStringAsync(url).GetAwaiter().GetResult(); }
    }
}
}
'@

$data1 = Join-Path $Root 'data-windows'
$data2 = Join-Path $Root 'data-sql'
try {
    # A run that stopped before its cleanup may have left its folder.
    if (Test-Path $Root) { Remove-Item -Recurse -Force $Root }
    New-Item -ItemType Directory -Force -Path $Root | Out-Null
    Expand-Archive -Path $Zip -DestinationPath $unpacked
    Write-Host "== 1. Windows authentication as $account"
    $started = Get-Date
    if ((Install $data1 @{ Database = $databases[0]; Authentication = 'Windows'; AdminAuthentication = 'Windows' }) -ne 0) {
        $script:failures++
        throw 'the installer failed (its output is above); the other checks depend on it'
    }
    Write-Host 'ok    the installer finishes (exit 0)'
    Check "the service runs as $account" { (Get-CimInstance Win32_Service -Filter "Name='Goldmark'").StartName -eq $account }
    Check '/health/ready answers 200' { Ready $data1 }
    Check 'the sign-in page is served (SPA shell with its style nonce)' {
        [GoldmarkSmoke.Http]::Text("https://localhost:$HttpsPort/", (Join-Path $data1 'tls\goldmark.crt')) -match 'property="csp-nonce"'
    }
    foreach ($folder in 'settings', 'keys', 'tls', 'logs') {
        Check "$folder folder: SYSTEM, Administrators and the service account only, inheritance off" { Private-Folder (Join-Path $data1 $folder) }
    }
    $log = Get-ChildItem (Join-Path $data1 'logs') -Filter 'goldmark-*.json' | Select-Object -First 1
    Check 'the log file exists and records the rights check (1004)' { $null -ne $log -and (Select-String -Path $log.FullName -Pattern '"Id":1004' -Quiet) }
    $tokenLine = if ($log) { Select-String -Path $log.FullName -Pattern '"Id":3001' | Select-Object -First 1 } else { $null }
    Check 'the setup token is in the log file (3001)' { $null -ne $tokenLine }
    Check 'nothing in the Application log carries the setup token or the recovery path' {
        if ($null -eq $tokenLine) { throw 'no setup token in the log file' }
        $token = ($tokenLine.Line | ConvertFrom-Json).SetupToken
        if (-not $token) { throw 'no SetupToken property' }
        $events = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $started } -ErrorAction SilentlyContinue
        -not ($events | Where-Object { $_.Message -like "*$token*" -or $_.Message -like '*password-reset#t=*' })
    }
    Check 'the sign-in keys are encrypted (DPAPI)' {
        $keys = @(Get-ChildItem (Join-Path $data1 'keys') -Filter 'key-*.xml')
        $keys.Count -gt 0 -and -not ($keys | Where-Object { -not (Select-String -Path $_.FullName -Pattern 'encryptedSecret' -Quiet) })
    }
    Check 'the service account has a Windows login, in goldmark_runtime' {
        (Sql "SELECT type FROM sys.server_principals WHERE name = N'$account';") -eq 'U' -and
        (Sql "SELECT IS_ROLEMEMBER(N'goldmark_runtime', N'$account');" $databases[0]) -eq 1
    }
    Check 'no migration login exists (migrations ran as the installing administrator)' {
        (Sql "SELECT COUNT(*) FROM sys.extended_properties WHERE name = N'goldmark:managed' AND CAST(value AS nvarchar(64)) = N'migration-login';" $databases[0]) -eq 0
    }
    Check 'Windows restarts the service after a crash' { (& sc.exe qfailure Goldmark | Out-String) -match 'RESTART' }
    Check 'only SYSTEM and Administrators may change the program folder (owned by Administrators, inheritance off)' {
        $acl = Get-Acl $program
        $writers = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) | Where-Object {
                $_.AccessControlType -eq 'Allow' -and ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]'WriteData, AppendData, Delete, ChangePermissions, TakeOwnership') })
        $acl.AreAccessRulesProtected -and $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -eq 'S-1-5-32-544' -and
        -not ($writers | Where-Object { $_.IdentityReference.Value -notin 'S-1-5-18', 'S-1-5-32-544' })
    }
    Check 'the service can read its settings but not change them' {
        $sid = (New-Object Security.Principal.NTAccount $account).Translate([Security.Principal.SecurityIdentifier])
        $rules = @((Get-Acl (Join-Path $data1 'settings')).GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) | Where-Object { $_.IdentityReference -eq $sid })
        $rules.Count -gt 0 -and -not ($rules | Where-Object { $_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::WriteData })
    }

    Write-Host '== 1b. Upgrade: the installer again, with the same data'
    $keysBefore = @(Get-ChildItem (Join-Path $data1 'keys') -Filter 'key-*.xml' | ForEach-Object Name) -join ','
    # Settings the installer doesn't manage, an accepted finding to clear, and a file the new release doesn't have.
    $settingsFile = Join-Path $data1 'settings\goldmark.json'
    $edited = Get-Content -Raw $settingsFile | ConvertFrom-Json
    $edited.Goldmark.Logging | Add-Member -NotePropertyName RetainedFiles -NotePropertyValue 40 -Force
    $edited.Goldmark.Https | Add-Member -NotePropertyName Hsts -NotePropertyValue ([pscustomobject]@{ MaxAgeDays = 100 }) -Force
    $edited.Goldmark.Bootstrap.AcceptedFindings = (@($AcceptedFindings, 'guest-connect:GoldmarkSmokeNone') | Where-Object { $_ }) -join ','
    $edited | ConvertTo-Json -Depth 10 | Set-Content -Path $settingsFile -Encoding UTF8
    Set-Content -Path (Join-Path $program 'wwwroot\stale-asset.js') -Value '// from an older release'
    # No -DataDirectory: the service's own is used. -AcceptedFindings as given, even empty, replaces the old value.
    $null = & (Join-Path $unpacked 'Install-Goldmark.ps1') -NonInteractive -SqlCertificatePath $SqlCertificateFile -InstallDirectory $program -AcceptedFindings $AcceptedFindings
    Check 'an upgrade without -DataDirectory finishes (exit 0), from the existing settings' { $LASTEXITCODE -eq 0 }
    Check 'after the upgrade it is ready, with the same database and sign-in keys' {
        (Ready $data1) -and ((Get-Content -Raw $settingsFile | ConvertFrom-Json).Goldmark.Database.Name -eq $databases[0]) -and
        ((@(Get-ChildItem (Join-Path $data1 'keys') -Filter 'key-*.xml' | ForEach-Object Name) -join ',') -eq $keysBefore)
    }
    $upgraded = Get-Content -Raw $settingsFile | ConvertFrom-Json
    Check 'the upgrade kept the settings it does not manage (Logging:RetainedFiles, Https:Hsts)' {
        $upgraded.Goldmark.Logging.RetainedFiles -eq 40 -and $upgraded.Goldmark.Https.Hsts.MaxAgeDays -eq 100
    }
    Check 'a given -AcceptedFindings replaced the old value, also when empty' { $upgraded.Goldmark.Bootstrap.AcceptedFindings -eq $AcceptedFindings }
    Check 'changing -Authentication on an upgrade is refused before anything changes (#41)' {
        $null = & (Join-Path $unpacked 'Install-Goldmark.ps1') -NonInteractive -SqlCertificatePath $SqlCertificateFile -InstallDirectory $program -Authentication Sql
        $LASTEXITCODE -ne 0 -and (Get-Service Goldmark).Status -eq 'Running' -and
        (Get-Content -Raw $settingsFile | ConvertFrom-Json).Goldmark.Database.Authentication -eq 'Windows'
    }
    Check 'the program folder was replaced, not added to' { -not (Test-Path (Join-Path $program 'wwwroot\stale-asset.js')) }
    Check 'the settings file is owned by Administrators and grants only what its folder does' {
        $acl = Get-Acl $settingsFile
        $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -eq 'S-1-5-32-544' -and
        -not ($acl.GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier]).Count)
    }

    Write-Host '== 2. Refusals'
    $since = Get-Date
    & net.exe localgroup $group /add | Out-Null
    & net.exe localgroup $group $account /add | Out-Null
    Sql "CREATE LOGIN [$env:COMPUTERNAME\$group] FROM WINDOWS; GRANT VIEW SERVER STATE TO [$env:COMPUTERNAME\$group];" | Out-Null
    Restart-Service Goldmark
    Check 'a right from a Windows group stops the service at start, without a restart loop' { Stays-Stopped 60 }
    Check 'Windows records that stop as a failure (exit code 13)' { $script:lastExit -eq 13 }
    Check 'the refusal is in the Application log (Goldmark, event 3), naming the right' {
        (Goldmark-Event 3 $since | Select-Object -First 1).Message -match 'VIEW SERVER STATE'
    }
    # A member's implicit rights on its own group's login are not a finding (SQL Server Express's BUILTIN\Users).
    Check 'the refusal names no right on the group login itself' {
        (Goldmark-Event 3 $since | Select-Object -First 1).Message -notmatch [regex]::Escape("LOGIN::[$env:COMPUTERNAME\$group]")
    }
    Sql "DROP LOGIN [$env:COMPUTERNAME\$group];" | Out-Null
    & net.exe localgroup $group /delete | Out-Null
    Start-Service Goldmark
    Check 'with the right removed, it is ready again' { Ready $data1 }

    # Owner, 2026-10-22 (DEC-043 item 11): a user for one of the service's groups in another database stops it,
    # and an operator may accept that finding by its ID.
    & net.exe localgroup $group /add | Out-Null
    & net.exe localgroup $group $account /add | Out-Null
    Sql "CREATE LOGIN [$env:COMPUTERNAME\$group] FROM WINDOWS; CREATE DATABASE [$($databases[2])];" | Out-Null
    Sql "CREATE USER [$env:COMPUTERNAME\$group] FOR LOGIN [$env:COMPUTERNAME\$group];" $databases[2] | Out-Null
    $since = Get-Date
    Restart-Service Goldmark -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt 60 -and (Get-Service Goldmark).Status -ne 'Stopped'; $i++) { Start-Sleep -Seconds 1 }
    $refusal = (Goldmark-Event 3 $since | Select-Object -First 1)
    $accept = if ($refusal -and $refusal.Message -match '\[(group-database-access:[^\]]+)\]') { $Matches[1] } else { '' }
    Check "a Windows group's user in another database stops the start, as group-database-access" {
        (Get-Service Goldmark).Status -eq 'Stopped' -and $accept -like "group-database-access:$($databases[2]):*"
    }
    $saved = Get-Content -Raw $settingsFile
    $accepting = $saved | ConvertFrom-Json
    $accepting.Goldmark.Bootstrap.AcceptedFindings = (@($AcceptedFindings, $accept) | Where-Object { $_ }) -join ','
    $accepting | ConvertTo-Json -Depth 10 | Set-Content -Path $settingsFile -Encoding UTF8
    Start-Service Goldmark
    Check 'accepted by its ID, it starts and is ready' { Ready $data1 }
    Set-Content -Path $settingsFile -Value $saved -Encoding UTF8
    Sql "ALTER DATABASE [$($databases[2])] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$($databases[2])]; DROP LOGIN [$env:COMPUTERNAME\$group];" | Out-Null
    & net.exe localgroup $group /delete | Out-Null
    Restart-Service Goldmark
    Check 'with the user removed and nothing accepted, it is ready again' { Ready $data1 }

    $saved = Get-Content -Raw $settingsFile
    $broken = $saved | ConvertFrom-Json
    $broken.Goldmark.Https.Port = 0
    $broken | ConvertTo-Json -Depth 5 | Set-Content -Path $settingsFile -Encoding UTF8
    $since = Get-Date
    Restart-Service Goldmark -ErrorAction SilentlyContinue
    Check 'an invalid setting stops the start, without a restart loop' { Stays-Stopped 30 }
    Check 'Windows records that stop as a failure (exit code 13)' { $script:lastExit -eq 13 }
    Check 'the setting is named in the Application log (Goldmark, event 2)' {
        (Goldmark-Event 2 $since | Select-Object -First 1).Message -match 'Goldmark:Https:Port'
    }
    Set-Content -Path $settingsFile -Value $saved -Encoding UTF8
    Start-Service Goldmark
    Check 'with the setting fixed, it is ready again' { Ready $data1 }

    Write-Host '== 3. Uninstall keeps the data'
    $processId = (Get-CimInstance Win32_Service -Filter "Name='Goldmark'").ProcessId
    $stopping = Get-Date
    # No folders given: the uninstaller reads the service's own (Codex on #39).
    $null = & (Join-Path $unpacked 'Uninstall-Goldmark.ps1')
    Write-Host "      (the process exited $([int]((Get-Date) - $stopping).TotalSeconds) s after the stop began; it was $processId)"
    Check 'the service and its program folder are gone, found without parameters, and the data stays' {
        -not (Get-Service Goldmark -ErrorAction SilentlyContinue) -and -not (Test-Path $program) -and (Test-Path (Join-Path $data1 'keys'))
    }

    Check 'no unhandled .NET exception in Goldmark.Api.exe since the install (Application log)' {
        -not (Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $started } -ErrorAction SilentlyContinue |
            Where-Object { $_.ProviderName -in '.NET Runtime', 'Application Error' -and $_.Message -like '*Goldmark.Api*' })
    }

    Write-Host '== 4. A data folder the installer can''t trust, then a SQL login, and rotation'
    # Codex on #39: settings in a folder someone else made could send the administrator's password elsewhere.
    $planted = Join-Path $Root 'planted'
    New-Item -ItemType Directory -Force -Path (Join-Path $planted 'settings') | Out-Null
    '{"Goldmark": {"Database": {"Host": "attacker.invalid"}}}' | Set-Content -Path (Join-Path $planted 'settings\goldmark.json')
    $linked = Join-Path $Root 'data-linked'
    New-Item -ItemType Junction -Path $linked -Target $planted | Out-Null
    Check 'a data folder that is a link is refused, before anything changes' {
        (Install $linked @{ Database = 'GoldmarkSmokeNever'; Authentication = 'Windows'; AdminAuthentication = 'Windows' }) -ne 0 -and
        -not (Get-Service Goldmark -ErrorAction SilentlyContinue)
    }
    [IO.Directory]::Delete($linked)
    # An administrator's folder whose settings others may change (Codex on #39, round 2).
    $wide = Join-Path $Root 'data-wide'
    New-Item -ItemType Directory -Force -Path (Join-Path $wide 'settings') | Out-Null
    '{"Goldmark": {"Database": {"Host": "attacker.invalid"}}}' | Set-Content -Path (Join-Path $wide 'settings\goldmark.json')
    & icacls.exe (Join-Path $wide 'settings') /grant '*S-1-5-32-545:(OI)(CI)M' | Out-Null
    Check 'a data folder whose settings ordinary users may change is refused, before anything changes' {
        (Install $wide @{ Database = 'GoldmarkSmokeNever'; Authentication = 'Windows'; AdminAuthentication = 'Windows' }) -ne 0 -and
        -not (Get-Service Goldmark -ErrorAction SilentlyContinue)
    }
    Check 'a data folder inside the program folder is refused, before anything changes' {
        (Install (Join-Path $program 'data') @{ Database = 'GoldmarkSmokeNever'; Authentication = 'Windows'; AdminAuthentication = 'Windows' }) -ne 0 -and
        -not (Get-Service Goldmark -ErrorAction SilentlyContinue) -and -not (Test-Path $program)
    }

    Sql "CREATE LOGIN [$adminLogin] WITH PASSWORD = N'$adminPassword', CHECK_POLICY = OFF; ALTER SERVER ROLE sysadmin ADD MEMBER [$adminLogin];" | Out-Null
    $secure = ConvertTo-SecureString $adminPassword -AsPlainText -Force
    if ((Install $data2 @{ Database = $databases[1]; Authentication = 'Sql'; AdminAuthentication = 'Sql'; SqlAdminUser = $adminLogin; SqlAdminPassword = $secure }) -ne 0) {
        $script:failures++
        throw 'the installer failed with a SQL login (its output is above)'
    }
    Write-Host 'ok    the installer finishes with a SQL login (exit 0)'
    $runtimeFile = Join-Path $data2 'credentials\runtime.json'
    Check 'the service encrypted its credential file to itself at start' {
        (Get-Content -Raw $runtimeFile | ConvertFrom-Json).password.protection -in 'dpapi', 'dpapi-ng'
    }
    Check 'the credentials folder is owner-only' { Private-Folder (Join-Path $data2 'credentials') }
    $before = (Get-Content -Raw $runtimeFile | ConvertFrom-Json).password.encrypted
    Check 'rotation finishes and restarts the service' {
        # No folders given: the service's own are used; the script waits for readiness itself.
        $null = & (Join-Path $unpacked 'Rotate-GoldmarkCredentials.ps1') -NonInteractive -SqlAdminPassword $secure
        $LASTEXITCODE -eq 0 -and (Ready $data2)
    }
    Check 'the new password is in the file, encrypted again, and the rotation is audited' {
        $after = Get-Content -Raw $runtimeFile | ConvertFrom-Json
        $after.password.protection -in 'dpapi', 'dpapi-ng' -and $after.password.encrypted -ne $before -and
        (Sql "SELECT COUNT(*) FROM cmdb.AuditLog WHERE Action = N'database.credentials.rotated' AND ActorType = 'System';" $databases[1]) -eq 1
    }
    $null = & (Join-Path $unpacked 'Uninstall-Goldmark.ps1') -InstallDirectory $program -DataDirectory $data2 -RemoveData
    Check 'uninstall -RemoveData removes the service and the data' { -not (Get-Service Goldmark -ErrorAction SilentlyContinue) -and -not (Test-Path $data2) }
}
finally {
    # Every step, whatever the one before did.
    $ErrorActionPreference = 'Continue'
    if ($script:failures -gt 0) {
        Write-Host '== Diagnostics (before cleanup)'
        $service = Get-CimInstance Win32_Service -Filter "Name='Goldmark'" -ErrorAction SilentlyContinue
        if ($service) { Write-Host "service: $($service.State), exit code $($service.ExitCode), as $($service.StartName)" }
        Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $started } -ErrorAction SilentlyContinue |
            Where-Object { $_.ProviderName -in 'Goldmark', '.NET Runtime', 'Application Error' } | Select-Object -First 5 |
            ForEach-Object { Write-Host "Application log, $($_.ProviderName) $($_.Id): $($_.Message)" }
        foreach ($data in $data1, $data2) {
            Get-ChildItem (Join-Path $data 'logs') -Filter 'goldmark-*.json' -ErrorAction SilentlyContinue | ForEach-Object {
                Write-Host "-- $($_.FullName) (without events 3001 and 3004)"
                Get-Content $_.FullName -Tail 25 | Where-Object { $_ -notmatch '"Id":300[14]\b' } | ForEach-Object { Write-Host "   $_" }
            }
        }
    }
    Write-Host '== Cleanup'
    if (Get-Service Goldmark -ErrorAction SilentlyContinue) {
        $processId = (Get-CimInstance Win32_Service -Filter "Name='Goldmark'").ProcessId
        Stop-Service Goldmark -Force -ErrorAction SilentlyContinue
        if ($processId) { Wait-Process -Id $processId -Timeout 120 -ErrorAction SilentlyContinue }
        & sc.exe delete Goldmark | Out-Null
    }
    $logins = @($account, "$env:COMPUTERNAME\$group", $adminLogin)
    foreach ($name in $databases) {
        try {
            # The logins of the users Goldmark marked in this test database (its runtime login), then the database.
            $mapped = Sql "IF DB_ID(N'$name') IS NOT NULL SELECT STRING_AGG(SUSER_SNAME(u.sid), N'|') FROM [$name].sys.database_principals AS u JOIN [$name].sys.extended_properties AS e ON e.class = 4 AND e.major_id = u.principal_id AND e.name = N'goldmark:managed';"
            if ($mapped -and $mapped -isnot [DBNull]) { $logins += ([string]$mapped).Split('|') }
            Sql "IF DB_ID(N'$name') IS NOT NULL BEGIN ALTER DATABASE [$name] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$name]; END;" | Out-Null
        }
        catch { Write-Host "drop $name : $($_.Exception.Message)" }
    }
    foreach ($login in ($logins | Where-Object { $_ } | Select-Object -Unique)) {
        try { Sql "IF SUSER_ID(N'$($login.Replace("'", "''"))') IS NOT NULL DROP LOGIN [$($login.Replace(']', ']]'))];" | Out-Null } catch { Write-Host "drop login $login : $($_.Exception.Message)" }
    }
    & net.exe localgroup $group /delete *> $null
    Remove-NetFirewallRule -Name 'Goldmark-HTTPS' -ErrorAction SilentlyContinue
    try { if ([Diagnostics.EventLog]::SourceExists('Goldmark')) { [Diagnostics.EventLog]::DeleteEventSource('Goldmark') } } catch { Write-Host "event source: $($_.Exception.Message)" }
    Remove-Item -Recurse -Force $Root -ErrorAction SilentlyContinue
    Write-Host "folder removed: $(-not (Test-Path $Root))"
}

if ($script:failures -gt 0) {
    Write-Host "$($script:failures) check(s) failed." -ForegroundColor Red
    exit 1
}
Write-Host 'All Windows service checks passed.' -ForegroundColor Green
exit 0
