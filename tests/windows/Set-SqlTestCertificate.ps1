<#
.SYNOPSIS
Gives a local SQL Server instance a self-signed TLS certificate for tests, or puts back what it had (-Restore).

.DESCRIPTION
For the Windows service tests only (DEC-043): Goldmark never skips certificate checks (TDS 8 strict, the
certificate pinned or issued by a trusted CA), and a fresh SQL Server has no certificate it can check. This
creates a certificate for localhost and this computer in LocalMachine\My (an SChannel key, which SQL Server
reads), lets the instance's service account read its key, points the instance at it, restarts the instance
and writes the public certificate to -CertificateFile for Goldmark to pin. The instance's previous setting is
saved to -StateFile; -Restore puts it back, restarts the instance and deletes the certificate. Run elevated.
#>
[CmdletBinding()]
param(
    [string]$Instance = 'MSSQLSERVER',
    [Parameter(Mandatory)][string]$StateFile,
    [string]$CertificateFile,
    [switch]$Restore
)

Set-StrictMode -Version 3
$ErrorActionPreference = 'Stop'

$instanceId = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL').$Instance
if (-not $instanceId) { throw "No SQL Server instance named $Instance." }
$netLib = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instanceId\MSSQLServer\SuperSocketNetLib"
$serviceName = if ($Instance -eq 'MSSQLSERVER') { 'MSSQLSERVER' } else { "MSSQL`$$Instance" }

function Restart-Instance {
    Restart-Service -Name $serviceName -Force
    (Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromMinutes(2))
}

if ($Restore) {
    $state = Get-Content -Raw $StateFile | ConvertFrom-Json
    Set-ItemProperty -Path $netLib -Name Certificate -Value ([string]$state.Previous)
    Restart-Instance
    $certificate = Get-Item -Path "Cert:\LocalMachine\My\$($state.Thumbprint)" -ErrorAction SilentlyContinue
    if ($certificate) { Remove-Item -Path $certificate.PSPath -DeleteKey }
    Remove-Item $StateFile
    Write-Host "Restored $Instance's certificate setting ('$($state.Previous)') and deleted the test certificate."
    return
}

if (-not $CertificateFile) { throw '-CertificateFile is required.' }
$previous = [string](Get-ItemProperty -Path $netLib -Name Certificate -ErrorAction SilentlyContinue).Certificate
$names = @('localhost', $env:COMPUTERNAME)
try { $names += [Net.Dns]::GetHostEntry($env:COMPUTERNAME).HostName } catch { }
$certificate = New-SelfSignedCertificate -DnsName ($names | Select-Object -Unique) -CertStoreLocation 'Cert:\LocalMachine\My' `
    -Provider 'Microsoft RSA SChannel Cryptographic Provider' -KeySpec KeyExchange -KeyLength 2048 -KeyExportPolicy NonExportable `
    -NotAfter (Get-Date).AddDays(30) -FriendlyName 'Goldmark test (SQL Server TLS)' -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.1')
@{ Instance = $Instance; Thumbprint = $certificate.Thumbprint; Previous = $previous } | ConvertTo-Json | Set-Content -Path $StateFile

# The instance's service account reads the private key.
$account = (Get-CimInstance Win32_Service -Filter "Name='$serviceName'").StartName
$keyName = $certificate.PrivateKey.CspKeyContainerInfo.UniqueKeyContainerName
$keyFile = Join-Path $env:ProgramData "Microsoft\Crypto\RSA\MachineKeys\$keyName"
$acl = Get-Acl -Path $keyFile
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($account, 'Read', 'Allow')))
Set-Acl -Path $keyFile -AclObject $acl

Set-ItemProperty -Path $netLib -Name Certificate -Value $certificate.Thumbprint.ToLowerInvariant()
Restart-Instance
[IO.File]::WriteAllText($CertificateFile, "-----BEGIN CERTIFICATE-----`n" +
    [Convert]::ToBase64String($certificate.RawData, 'InsertLineBreaks') + "`n-----END CERTIFICATE-----`n")
Write-Host "$Instance uses the test certificate $($certificate.Thumbprint); its public part: $CertificateFile"
