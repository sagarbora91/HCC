<#
.SYNOPSIS
  Writes the HCC client kit: the folder the owner copies to a USB stick and runs on each other Windows PC of the hotel.
.DESCRIPTION
  Writes into -OutDir (default: ..\ClientKit beside the Operations folder):
    HCC-Client-Setup-<version>.exe   the client setup (copied from -ClientSetupExe, else found in -OutDir or beside Operations)
    HCC-Hotel-CA.cer                 the hotel CA certificate (public part only, DER) - checked to be a name-constrained CA first
    hcc-client.json                  { "serverUrl": "https://<name>:<port>/", "hotelName": "", "caSha256": "" }
    READ-ME.txt                      what to do on each PC, with the certificate fingerprint to compare
  The client setup reads the .cer and the .json from its own folder, shows the fingerprint, refuses a mismatch.
  Safe to run twice (files are replaced). -WhatIf prints the plan and writes nothing. No secret is involved: only the public CA
  certificate is read.
.EXAMPLE
  .\New-HccClientKit.ps1 -ServerName HOTEL-PC -Port 5443 -HotelName "Hotel Parth" -CaCer C:\ProgramData\HCC\certs\HCC-Hotel-CA.cer -OutDir "C:\Program Files\HCC\ClientKit"
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$ServerName,
    [ValidateRange(1, 65535)][int]$Port = 5443,
    [Parameter(Mandatory)][string]$HotelName,
    [Parameter(Mandatory)][string]$CaCer,
    [string]$OutDir,
    [string]$ClientSetupExe
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $OutDir) { $OutDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'ClientKit' }
. (Join-Path $PSScriptRoot 'Hcc-ClientTrust.ps1')

# --- validate everything before writing anything ---------------------------------------------------------------------------------
$name = $ServerName.Trim()
$url = Test-HccServerUrl ("https://{0}:{1}/" -f $name, $Port)
if (-not $url.Ok) { throw "Server name '$ServerName' / port $Port cannot be used: $($url.Error)" }
$hotel = $HotelName.Trim()
if (-not $hotel -or $hotel.Length -gt 100 -or $hotel -match '[\x00-\x1F\x7F]') { throw 'HotelName must be 1 to 100 characters, on one line.' }
$ca = Get-HccCaFileInfo $CaCer
if (-not $ca.Ok) { throw "CaCer '$CaCer' cannot be used for the kit: $($ca.Error)" }

function Find-HccClientSetupExe {
    if ($ClientSetupExe) {
        if (-not (Test-Path -LiteralPath $ClientSetupExe -PathType Leaf)) { throw "ClientSetupExe '$ClientSetupExe' was not found." }
        return (Get-Item -LiteralPath $ClientSetupExe).FullName
    }
    $cands = @()
    foreach ($d in @($OutDir, (Join-Path (Split-Path -Parent $PSScriptRoot) 'ClientSetup'), (Join-Path $PSScriptRoot 'ClientSetup'))) {
        if (Test-Path -LiteralPath $d) { $cands += @(Get-ChildItem -LiteralPath $d -Filter 'HCC-Client-Setup-*.exe' -File -ErrorAction SilentlyContinue) }
    }
    if ($cands.Count -eq 0) { return $null }
    return ($cands | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}
$setup = Find-HccClientSetupExe
if (-not $setup -and -not $WhatIfPreference) {
    throw "The client setup (HCC-Client-Setup-<version>.exe) was not found in '$OutDir' or beside the Operations folder. Pass -ClientSetupExe <file>."
}

$cerOut = Join-Path $OutDir 'HCC-Hotel-CA.cer'
$jsonOut = Join-Path $OutDir 'hcc-client.json'
$readmeOut = Join-Path $OutDir 'READ-ME.txt'
$fp = Format-HccFingerprint $ca.Sha256
$setupNameOut = 'HCC-Client-Setup-<version>.exe'
if ($setup) { $setupNameOut = Split-Path -Leaf $setup }

$plan = "Write the HCC client kit for '$hotel' ($($url.Url)) into $OutDir"
if (-not $PSCmdlet.ShouldProcess($OutDir, $plan)) {
    Write-Host "DRY RUN plan: $plan"
    Write-Host "DRY RUN plan: certificate $($ca.Subject), SHA-256 $fp, valid until $($ca.NotAfter), name-constrained: $($ca.NameConstrained)"
    Write-Host "DRY RUN plan: files $setupNameOut, HCC-Hotel-CA.cer, hcc-client.json, READ-ME.txt"
    return
}

if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
[IO.File]::WriteAllBytes($cerOut, $ca.Cert.RawData)   # always DER, public part only
$cfg = [ordered]@{ serverUrl = $url.Url; hotelName = $hotel; caSha256 = $ca.Sha256 }
[IO.File]::WriteAllText($jsonOut, (($cfg | ConvertTo-Json) + "`r`n"), (New-Object Text.UTF8Encoding($false)))
$dest = Join-Path $OutDir $setupNameOut
if ((Get-Item -LiteralPath $setup).FullName -ne $dest) { Copy-Item -LiteralPath $setup -Destination $dest -Force }

$readme = @"
HCC client kit - $hotel
=====================================================

What this is
  A small setup for every OTHER Windows PC of the hotel (front desk, accounts, manager). It installs no program. It does two things:
  1. It trusts the hotel's own certificate, so the browser shows no warning for $($url.Url)
  2. It puts an "HCC" icon on the desktop and in the Start menu. The icon opens HCC in its own window.
  What a person may do in HCC depends on their sign-in role on the server, not on this PC.

What to do on each PC (about two minutes)
  1. Copy this whole folder to the PC (USB stick). Keep the files together.
  2. Double-click $setupNameOut
  3. Windows asks for an administrator's permission. Say yes (an administrator of that PC must do this once).
  4. The setup shows the hotel name, the server address and a certificate fingerprint. Check that the fingerprint is
     the same as the one below. Tick the box and go on.
  5. Finish. The setup says whether it could already reach the server. If the server is off or the PC is on another network, that is
     only a message: the icon works as soon as the PC can reach the server.

Certificate fingerprint (SHA-256) - this must be what the setup shows:
  $fp

Hotel server address: $($url.Url)
Certificate valid until: $($ca.NotAfter)
The certificate can only vouch for the HCC server's own names and addresses (it is name-restricted), so trusting it does not
let it vouch for other websites.

Removing: Settings > Apps > "HCC (client)" > Uninstall. It removes the icon and asks whether to remove the certificate too.
Do not copy this kit to phones or to PCs outside the hotel. Phones and tablets: see ANDROID_SERVER_ADDRESS.md on the server PC.
"@
[IO.File]::WriteAllText($readmeOut, ($readme -replace "`r?`n", "`r`n"), (New-Object Text.UTF8Encoding($true)))

Write-Host "HCC client kit written to $OutDir"
Write-Host "  setup: $setupNameOut   certificate fingerprint: $fp"
[pscustomobject]@{ OutDir = $OutDir; Setup = $dest; Cer = $cerOut; Json = $jsonOut; ReadMe = $readmeOut; Sha256 = $ca.Sha256 }
