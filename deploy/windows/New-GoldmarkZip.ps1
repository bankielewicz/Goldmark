<#
.SYNOPSIS
Builds the Windows service zip (DEC-043): a self-contained win-x64 Goldmark with the web app, and the scripts.

.DESCRIPTION
Run from the repository root after the web app is built (web/dist). The zip holds Goldmark\ (the program,
with wwwroot), Install-Goldmark.ps1, Uninstall-Goldmark.ps1 and Rotate-GoldmarkCredentials.ps1. Restore is in
locked mode, as everywhere else.
#>
[CmdletBinding()]
param(
    [string]$Output = 'out',
    [string]$WebDist = 'web/dist'
)

Set-StrictMode -Version 3
$ErrorActionPreference = 'Stop'

if (-not (Test-Path (Join-Path $WebDist 'index.html'))) { throw "$WebDist has no index.html: build the web app first (pnpm run build in web)." }
$stage = Join-Path $Output 'goldmark-win-x64'
if (Test-Path $stage) { throw "$stage exists already: use a new -Output folder." }
$program = Join-Path $stage 'Goldmark'

dotnet publish src/Goldmark.Api/Goldmark.Api.csproj -c Release -r win-x64 --self-contained -p:RestoreLockedMode=true -o $program
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
New-Item -ItemType Directory -Force -Path (Join-Path $program 'wwwroot') | Out-Null
Copy-Item -Path (Join-Path $WebDist '*') -Destination (Join-Path $program 'wwwroot') -Recurse
foreach ($script in 'Install-Goldmark.ps1', 'Uninstall-Goldmark.ps1', 'Rotate-GoldmarkCredentials.ps1') {
    Copy-Item -Path (Join-Path 'deploy/windows' $script) -Destination $stage
}
$zip = Join-Path $Output 'goldmark-win-x64.zip'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "Built $zip"
exit 0
