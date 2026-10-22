<#
.SYNOPSIS
CI only: installs SQL Server 2025 Express on a GitHub-hosted Windows runner for the Windows service smoke test.

.DESCRIPTION
GitHub's Windows runners can't run Linux containers, so the integration suite's SQL Server container isn't
available there (DEC-043 item 8). This downloads Microsoft's Express bootstrapper, fetches the core media,
installs the database engine as the default instance (TCP 1433, mixed mode so the smoke can test a SQL login,
the runner's Administrators as sysadmin), checks it is SQL Server 2025 (major version 17), and gives it a test
certificate (Set-SqlTestCertificate.ps1), whose public part goes to -CertificateFile for Goldmark to pin.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CertificateFile,
    [string]$Work = (Join-Path $env:RUNNER_TEMP 'sqlexpress')
)

Set-StrictMode -Version 3
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Run([string]$File, [string[]]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -Wait -PassThru -NoNewWindow
    if ($process.ExitCode -notin 0, 3010) { throw "$File exited with $($process.ExitCode)." }
}

New-Item -ItemType Directory -Force -Path $Work | Out-Null
$bootstrapper = Join-Path $Work 'SQL2025-SSEI-Expr.exe'
# The "Download now" link for SQL Server 2025 Express on https://www.microsoft.com/en-us/sql-server/sql-server-downloads
Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?linkid=2216019' -OutFile $bootstrapper -UseBasicParsing
$media = Join-Path $Work 'media'
Run $bootstrapper @('/ACTION=Download', "/MEDIAPATH=$media", '/MEDIATYPE=Core', '/QUIET')
$package = Get-ChildItem -Path $media -Filter 'SQLEXPR*.exe' | Select-Object -First 1
if (-not $package) { throw "The bootstrapper downloaded no Express package into $media." }
$extracted = Join-Path $Work 'setup'
Run $package.FullName @('/q', "/x:$extracted")

$sa = 'Ci-' + [Guid]::NewGuid().ToString('N') # never used: Goldmark refuses sa as its administrator
Run (Join-Path $extracted 'SETUP.EXE') @('/Q', '/ACTION=Install', '/FEATURES=SQLEngine', '/INSTANCENAME=MSSQLSERVER',
    '/SQLSYSADMINACCOUNTS="BUILTIN\Administrators"', '/SECURITYMODE=SQL', "/SAPWD=$sa", '/TCPENABLED=1',
    '/UPDATEENABLED=0', '/IACCEPTSQLSERVERLICENSETERMS')

$connection = New-Object System.Data.SqlClient.SqlConnection 'Server=localhost;Database=master;Integrated Security=true'
$connection.Open()
$command = $connection.CreateCommand()
$command.CommandText = "SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int)"
$major = [int]$command.ExecuteScalar()
$command.CommandText = "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(32)) + N' ' + CAST(SERVERPROPERTY('Edition') AS nvarchar(128))"
$version = [string]$command.ExecuteScalar()
$connection.Close()
if ($major -ne 17) { throw "Expected SQL Server 2025 (major version 17), got $major." }
Write-Host "SQL Server 2025 is installed: $version."

& (Join-Path $PSScriptRoot 'Set-SqlTestCertificate.ps1') -Instance MSSQLSERVER -StateFile (Join-Path $Work 'certificate.json') -CertificateFile $CertificateFile
exit 0
