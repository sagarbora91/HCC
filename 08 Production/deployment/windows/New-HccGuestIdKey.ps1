<#
.SYNOPSIS
  Creates the guest-ID encryption key (DEC-068) and installs it into the server's appsettings.Production.json. Run once, before the first check-in.
.DESCRIPTION
  Guest ID numbers (Aadhaar, passport, ...) are stored encrypted (AES-256-GCM). Without a key the server refuses to store a full ID number
  ("ID storage is not set up on the server"); names, phones and the last 4 digits still work. This script:
    1. makes a new random 32-byte key,
    2. wraps it with Windows DPAPI for THIS PC (scheme dpapi-machine: only programs on this PC can unwrap it; the key never appears in git, SQL or backups),
    3. writes it to Hcc:GuestIdKeys in appsettings.Production.json (that file is then readable only by Administrators, SYSTEM and the HCC service),
       with Required=true (the server then refuses to start if the key ever disappears) and, for -Scope Machine, AllowMachineScope=true:
       in Production the server accepts a machine-wide key ONLY with that flag AND only while the settings file keeps that ACL (it checks
       the file at every start, F-M05); plain keys and AllowPlainKeys are refused in Production,
    4. writes the raw key to an OFFLINE COPY file you give with -OfflineCopyPath (a USB stick, not this PC). Put that stick in the safe.
       A DPAPI blob is useless on another PC or after Windows is reinstalled: without the offline copy every stored ID number is lost
       (the last 4 digits stay). Database backups never contain the key.
  Restart the service afterwards: Restart-Service HccServer.
  Refuses to run if a key is already configured (replacing it would make stored numbers unreadable); use -Rotate to add a new key under a new id
  (the old key stays in the file until the Owner has re-encrypted: see src\Hcc.Application\GuestIds\README.md, "Key rotation").
  -WhatIf prints the plan and writes nothing. The raw key is never printed.
  Scope: -Scope Machine (default, works when an administrator runs this) or -Scope User (only if you run this script AS the account that runs the
  service; it then only opens for that account).
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SettingsFile = 'C:\Program Files\HCC\Server\appsettings.Production.json',
    [Parameter(Mandatory)][string]$OfflineCopyPath,
    [string]$KeyId = (Get-Date -Format 'yyyy-MM'),
    [ValidateSet('Machine', 'User')][string]$Scope = 'Machine',
    [switch]$Rotate,
    [string]$ServiceAccount = 'NT SERVICE\HccServer',
    [switch]$SkipAcl
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# Pure helpers (unit-tested). Same wrapping as DpapiGuestIdKeyRing: DPAPI with entropy "HCC guest ID key v1".
function New-HccGuestIdKeyBytes { $k = New-Object byte[] 32; $r = [Security.Cryptography.RandomNumberGenerator]::Create(); try { $r.GetBytes($k) } finally { $r.Dispose() }; return , $k }
function ConvertTo-HccWrappedGuestIdKey {
    param([Parameter(Mandatory)][byte[]]$Key, [ValidateSet('Machine', 'User')][string]$Scope = 'Machine')
    Add-Type -AssemblyName System.Security
    $e = [Text.Encoding]::ASCII.GetBytes('HCC guest ID key v1')
    $ds = if ($Scope -eq 'Machine') { [Security.Cryptography.DataProtectionScope]::LocalMachine } else { [Security.Cryptography.DataProtectionScope]::CurrentUser }
    $prefix = if ($Scope -eq 'Machine') { 'dpapi-machine:' } else { 'dpapi-user:' }
    return $prefix + [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect($Key, $e, $ds))
}
function Assert-HccGuestIdKeyId { param([string]$Id) if ($Id -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$') { throw "Key id '$Id' must be 1-32 letters, digits, '.', '_' or '-' (for example 2026-10)." } return $Id }
if ($MyInvocation.InvocationName -eq '.') { return }   # dot-sourced by the tests: functions only

Assert-HccGuestIdKeyId $KeyId | Out-Null
if (-not (Test-Path -LiteralPath $SettingsFile)) { throw "Settings file not found: $SettingsFile. Install the server first (INSTALL.md step 3)." }
$full = [IO.Path]::GetFullPath($OfflineCopyPath)
$serverDir = [IO.Path]::GetFullPath((Split-Path -Parent $SettingsFile))
if ($full.StartsWith($serverDir, [StringComparison]::OrdinalIgnoreCase)) { throw 'The offline copy must NOT be inside the server folder: put it on a USB stick or another PC.' }
if (Test-Path -LiteralPath $full) { throw "$full already exists; give a new file name (never overwrite an earlier offline copy)." }

$json = Get-Content -LiteralPath $SettingsFile -Raw | ConvertFrom-Json
$existing = $null
if ($json.PSObject.Properties['Hcc'] -and $json.Hcc.PSObject.Properties['GuestIdKeys']) { $existing = $json.Hcc.GuestIdKeys }
$hasCurrent = $existing -and $existing.PSObject.Properties['Current'] -and $existing.Current
if ($hasCurrent) {
    if (-not $Rotate) { throw "A guest ID key ('$($existing.Current)') is already configured. Replacing it would make stored ID numbers unreadable. Use -Rotate -KeyId <new id> to add a new key and keep the old one." }
    if ($existing.PSObject.Properties['Keys'] -and $existing.Keys.PSObject.Properties[$KeyId]) { throw "Key id '$KeyId' already exists; pick a new id." }
}
elseif ($Rotate) { throw 'Nothing to rotate: no guest ID key is configured yet. Run without -Rotate.' }

$what = "Create guest ID key '$KeyId' (DPAPI $Scope) in $SettingsFile, offline copy -> $full"
if (-not $PSCmdlet.ShouldProcess($SettingsFile, $what)) {
    Write-Host "DRY RUN: $what"
    Write-Host "DRY RUN: Hcc:GuestIdKeys gets Required=true$(if ($Scope -eq 'Machine') { ' and AllowMachineScope=true' }); the server checks the file permissions at every start"
    Write-Host "DRY RUN: settings file then readable only by Administrators, SYSTEM and '$ServiceAccount' (icacls)"
    Write-Host 'DRY RUN: then Restart-Service HccServer. The raw key is never printed.'
    return
}

$key = New-HccGuestIdKeyBytes
try {
    $wrapped = ConvertTo-HccWrappedGuestIdKey -Key $key -Scope $Scope
    # Offline copy first: if this fails nothing is configured yet.
    $dir = Split-Path -Parent $full
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { throw "Folder for the offline copy not found: $dir (is the USB stick plugged in?)." }
    $text = "HCC guest ID key (OFFLINE COPY - keep in the safe, never e-mail, never on the server PC)`r`nKey id: $KeyId`r`nCreated (UTC): $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm'))`r`nRaw key (base64, 32 bytes): $([Convert]::ToBase64String($key))`r`nRestore: see INSTALL.md section 'Guest ID key'.`r`n"
    [IO.File]::WriteAllText($full, $text, (New-Object Text.UTF8Encoding($false)))
}
finally { [Array]::Clear($key, 0, $key.Length) }

if (-not $json.PSObject.Properties['Hcc']) { $json | Add-Member -NotePropertyName Hcc -NotePropertyValue ([pscustomobject]@{}) }
if (-not $json.Hcc.PSObject.Properties['GuestIdKeys']) { $json.Hcc | Add-Member -NotePropertyName GuestIdKeys -NotePropertyValue ([pscustomobject]@{ Current = $KeyId; Keys = [pscustomobject]@{} }) }
$g = $json.Hcc.GuestIdKeys
$g | Add-Member -NotePropertyName Current -NotePropertyValue $KeyId -Force
$g | Add-Member -NotePropertyName Required -NotePropertyValue $true -Force
if ($Scope -eq 'Machine') { $g | Add-Member -NotePropertyName AllowMachineScope -NotePropertyValue $true -Force }
if (-not $g.PSObject.Properties['Keys']) { $g | Add-Member -NotePropertyName Keys -NotePropertyValue ([pscustomobject]@{}) }
$g.Keys | Add-Member -NotePropertyName $KeyId -NotePropertyValue $wrapped -Force
$backup = "$SettingsFile.before-guestid-key"
Copy-Item -LiteralPath $SettingsFile -Destination $backup -Force
[IO.File]::WriteAllText($SettingsFile, ($json | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))

if (-not $SkipAcl) {
    try {
        & icacls.exe $SettingsFile /inheritance:r /grant:r '*S-1-5-18:(F)' '*S-1-5-32-544:(F)' "${ServiceAccount}:(R)" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "icacls exit $LASTEXITCODE" }
        & icacls.exe $backup /inheritance:r /grant:r '*S-1-5-18:(F)' '*S-1-5-32-544:(F)' | Out-Null
    }
    catch { Write-Warning "Could not restrict access to $SettingsFile ($($_.Exception.Message)). Do it by hand: only Administrators, SYSTEM and the HCC service account may read it." }
}
Write-Host "Guest ID key '$KeyId' installed ($Scope DPAPI) in $SettingsFile."
Write-Host "Offline copy written to ${full}: put that USB stick in the safe now, and delete ${backup} once you have checked the server starts."
Write-Host 'Next: Restart-Service HccServer, then check in a guest with an ID number.'
