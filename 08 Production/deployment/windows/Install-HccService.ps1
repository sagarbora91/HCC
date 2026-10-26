<#
.SYNOPSIS
  Registers the HCC API as a Windows service ("HccServer"), opens the LAN HTTPS port in the firewall (Private/Domain networks
  only), and sets automatic restart on failure. Run from an elevated PowerShell. -WhatIf prints the plan and changes nothing.
.DESCRIPTION
  The service runs Hcc.Api.exe from -InstallDirectory under -ServiceAccount (a dedicated low-privilege Windows account whose SQL
  login Initialize-HccDatabase.ps1 created; with -ServiceAccount omitted it runs as "NT SERVICE\HccServer", a virtual account,
  which then is the login name to give Initialize-HccDatabase.ps1). It starts automatically at boot. Configuration comes from
  appsettings.Production.json in the install folder; the service refuses to start if it is incomplete (see the Windows
  Event Log / service log). -Uninstall stops and removes the service and the firewall rule (data and backups are never touched).
# ETP-REUSE: replace/align with ETP's service install step (service name, recovery actions, logging) when V:\ETP is available.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallDirectory = 'C:\Program Files\HCC\Server',
    [string]$ServiceName = 'HccServer',
    [string]$ServiceAccount,
    [securestring]$ServiceAccountPassword,
    [int]$HttpsPort = 5443,
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $InstallDirectory 'Hcc.Api.exe'
$ruleName = "HCC HTTPS $HttpsPort (LAN)"

if ($Uninstall) {
    if ($PSCmdlet.ShouldProcess($ServiceName, 'Stop and delete service; remove firewall rule')) {
        if (Get-Service $ServiceName -ErrorAction SilentlyContinue) { Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue; & sc.exe delete $ServiceName | Out-Null }
        Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    }
    return
}

$account = if ($ServiceAccount) { $ServiceAccount } else { "NT SERVICE\$ServiceName" }
if (-not $PSCmdlet.ShouldProcess($ServiceName, "Create service: $exe as $account, auto start, restart on failure; firewall allow TCP $HttpsPort (Private,Domain)")) {
    Write-Host "DRY RUN: New-Service $ServiceName -BinaryPathName '$exe' -StartupType Automatic (account $account)"
    Write-Host "DRY RUN: sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/30000/restart/60000"
    Write-Host "DRY RUN: grant $account Modify on $InstallDirectory\logs and ReadAndExecute on $InstallDirectory"
    Write-Host "DRY RUN: icacls $InstallDirectory\appsettings.Production.json /inheritance:r /grant:r SYSTEM:F Administrators:F ${account}:R (F-M05: only they may read the settings and the guest ID key)"
    Write-Host "DRY RUN: New-NetFirewallRule '$ruleName' -Direction Inbound -Protocol TCP -LocalPort $HttpsPort -Profile Private,Domain -Action Allow"
    return
}
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Run this script from an elevated (Administrator) PowerShell.' }
if (-not (Test-Path -LiteralPath $exe)) { throw "Hcc.Api.exe not found in $InstallDirectory. Publish or copy the server files first." }
if (-not (Test-Path (Join-Path $InstallDirectory 'appsettings.Production.json'))) { throw 'appsettings.Production.json is missing in the install folder (copy appsettings.Production.sample.json and fill it in).' }

if (Get-Service $ServiceName -ErrorAction SilentlyContinue) { throw "Service $ServiceName already exists. Use -Uninstall first (your data is not affected)." }
$params = @{ Name = $ServiceName; BinaryPathName = ('"{0}"' -f $exe); DisplayName = 'Hotel Control Centre server'; Description = 'HCC API for the hotel LAN (web app and Android app).'; StartupType = 'Automatic' }
if ($ServiceAccount) {
    if (-not $ServiceAccountPassword) { $ServiceAccountPassword = Read-Host "Password for $ServiceAccount" -AsSecureString }
    $params.Credential = New-Object System.Management.Automation.PSCredential($ServiceAccount, $ServiceAccountPassword)
}
New-Service @params | Out-Null
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/30000/restart/60000 | Out-Null
& sc.exe config $ServiceName start= delayed-auto | Out-Null
$logs = Join-Path $InstallDirectory 'logs'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
& icacls.exe $InstallDirectory /grant ("{0}:(OI)(CI)RX" -f $account) | Out-Null
& icacls.exe $logs /grant ("{0}:(OI)(CI)M" -f $account) | Out-Null
# F-M05: the settings file (guest ID key, connection details) is readable only by SYSTEM, Administrators and the service account.
$settings = Join-Path $InstallDirectory 'appsettings.Production.json'
& icacls.exe $settings /inheritance:r /grant:r '*S-1-5-18:(F)' '*S-1-5-32-544:(F)' ("{0}:(R)" -f $account) | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Warning "Could not restrict $settings; only SYSTEM, Administrators and $account may read it (the server refuses to start with a guest ID key in a file others can read)." }
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $HttpsPort -Profile Private, Domain -Action Allow | Out-Null
}
Write-Host "Service $ServiceName installed (not started). Next: certificate (New-HccCertificate.ps1), migrate, then Start-Service $ServiceName."
