<#
.SYNOPSIS
  Publishes the server and compiles Hcc.iss with Inno Setup, if Inno Setup is installed. If it is not, says so and exits 0.
  -DryRun prints the steps without publishing or compiling.
# ETP-REUSE: replace/align with ETP's installer build script when V:\ETP is available.
#>
[CmdletBinding()]
param([string]$Version = '0.1.0', [string]$OutputDirectory, [switch]$DryRun)
$ErrorActionPreference = 'Stop'
$ops = Split-Path -Parent $PSScriptRoot
$prod = Resolve-Path (Join-Path $ops '..\..')
$publish = Join-Path $prod 'publish'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $prod 'installer-out' }
$candidates = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")
$iscc = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
$publishCmd = "dotnet publish `"$prod\src\Hcc.Api\Hcc.Api.csproj`" -c Release -r win-x64 --self-contained false -o `"$publish`""
$isccCmd = "ISCC.exe /DAppVersion=$Version /DPublishDir=`"$publish`" /DOpsDir=`"$ops`" /O`"$OutputDirectory`" Hcc.iss"
if ($DryRun) {
    Write-Host "DRY RUN: $publishCmd"
    Write-Host "DRY RUN: $isccCmd"
    Write-Host ("DRY RUN: Inno Setup " + $(if ($iscc) { "found at $iscc" } else { 'not installed (the real run would skip compiling)' }))
    return
}
if (-not $iscc) { Write-Host 'Inno Setup 6 is not installed: nothing compiled. Install it from https://jrsoftware.org/isinfo.php and run this again.'; return }
$env:HCC_RELEASE = 'true'
& dotnet publish "$prod\src\Hcc.Api\Hcc.Api.csproj" -c Release -r win-x64 --self-contained false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
& $iscc "/DAppVersion=$Version" "/DPublishDir=$publish" "/DOpsDir=$ops" "/O$OutputDirectory" (Join-Path $PSScriptRoot 'Hcc.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compile failed.' }
Write-Host "Installer written to $OutputDirectory"
