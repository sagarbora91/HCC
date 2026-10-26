<#
.SYNOPSIS
  Creates the guest-ID encryption key (DEC-068) and installs it into the server's appsettings.Production.json. Run once, before the first check-in.
  On a replacement PC (or after Windows is reinstalled) -ImportOfflineCopy installs the SAME key again from its offline copy (A07-03 / A09-07).
.DESCRIPTION
  Guest ID numbers (Aadhaar, passport, ...) are stored encrypted (AES-256-GCM). Without a key the server refuses to store a full ID number
  ("ID storage is not set up on the server"); names, phones and the last 4 digits still work. This script:
    1. makes a new random 32-byte key (or, with -ImportOfflineCopy, reads it from the offline copy made by an earlier run),
    2. wraps it with Windows DPAPI for THIS PC (scheme dpapi-machine: only programs on this PC can unwrap it; the key never appears in git, SQL or backups),
    3. writes it to Hcc:GuestIdKeys in appsettings.Production.json (that file is then readable only by Administrators, SYSTEM and the HCC service),
       with Required=true (the server then refuses to start if the key ever disappears) and, for -Scope Machine, AllowMachineScope=true:
       in Production the server accepts a machine-wide key ONLY with that flag AND only while the settings file keeps that ACL (it checks
       the file at every start, F-M05); plain keys and AllowPlainKeys are refused in Production,
    4. (new key only) writes the raw key to an OFFLINE COPY file you give with -OfflineCopyPath (a USB stick, not this PC). Copy it to the
       second keys USB (DEC-087: hotel safe + off-site with the owner). A DPAPI blob is useless on another PC or after Windows is reinstalled:
       without the offline copy every stored ID number is lost (the last 4 digits stay). Database backups never contain the key.
  Restart the service afterwards: Restart-Service HccServer.
  Refuses to run if a key is already configured (replacing it would make stored numbers unreadable); use -Rotate to add a new key under a new id
  (the old key stays in the file until the Owner has re-encrypted: see src\Hcc.Application\GuestIds\README.md, "Key rotation").
  -ImportOfflineCopy <file>: the key id comes from the file. The first imported key becomes the current key; importing a further offline copy
  (an older key after a rotation) adds it next to the current one (-MakeCurrent makes it the current key). It refuses to replace a key id that is
  already in the settings file unless -Force (for example an entry copied from the old PC, which cannot be opened here); after writing it checks
  that the key opens on this PC. Then check the database with: Hcc.Api.exe verify-guest-id (INSTALL.md section 6b).
  -WhatIf prints the plan and writes nothing. The raw key is never printed.
  Scope: -Scope Machine (default, works when an administrator runs this) or -Scope User (only if you run this script AS the account that runs the
  service; it then only opens for that account).
.EXAMPLE
  .\New-HccGuestIdKey.ps1 -OfflineCopyPath "E:\HCC-guest-id-key-2026-10.txt"
  .\New-HccGuestIdKey.ps1 -ImportOfflineCopy "E:\HCC-guest-id-key-2026-10.txt"      # replacement PC, before the service starts
#>
[CmdletBinding(SupportsShouldProcess, DefaultParameterSetName = 'New')]
param(
    [string]$SettingsFile = 'C:\Program Files\HCC\Server\appsettings.Production.json',
    [Parameter(Mandatory, ParameterSetName = 'New')][string]$OfflineCopyPath,
    [Parameter(Mandatory, ParameterSetName = 'Import')][string]$ImportOfflineCopy,
    [string]$KeyId,
    [ValidateSet('Machine', 'User')][string]$Scope = 'Machine',
    [Parameter(ParameterSetName = 'New')][switch]$Rotate,
    [Parameter(ParameterSetName = 'Import')][switch]$Force,
    [Parameter(ParameterSetName = 'Import')][switch]$MakeCurrent,
    [string]$ServiceAccount = 'NT SERVICE\HccServer',
    [switch]$SkipAcl
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# Pure helpers. Same wrapping as DpapiGuestIdKeyRing: DPAPI with entropy "HCC guest ID key v1".
function New-HccGuestIdKeyBytes { $k = New-Object byte[] 32; $r = [Security.Cryptography.RandomNumberGenerator]::Create(); try { $r.GetBytes($k) } finally { $r.Dispose() }; return , $k }
function ConvertTo-HccWrappedGuestIdKey {
    param([Parameter(Mandatory)][byte[]]$Key, [ValidateSet('Machine', 'User')][string]$Scope = 'Machine')
    Add-Type -AssemblyName System.Security
    $e = [Text.Encoding]::ASCII.GetBytes('HCC guest ID key v1')
    $ds = if ($Scope -eq 'Machine') { [Security.Cryptography.DataProtectionScope]::LocalMachine } else { [Security.Cryptography.DataProtectionScope]::CurrentUser }
    $prefix = if ($Scope -eq 'Machine') { 'dpapi-machine:' } else { 'dpapi-user:' }
    return $prefix + [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect($Key, $e, $ds))
}
# The 32-byte key inside a settings value (dpapi-machine:/dpapi-user:), or $null when it cannot be opened here (another PC or account).
function ConvertFrom-HccWrappedGuestIdKey {
    param([string]$Wrapped)
    if (-not $Wrapped) { return $null }
    Add-Type -AssemblyName System.Security
    $e = [Text.Encoding]::ASCII.GetBytes('HCC guest ID key v1')
    try {
        if ($Wrapped.StartsWith('dpapi-machine:')) { $k = [Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($Wrapped.Substring(14)), $e, [Security.Cryptography.DataProtectionScope]::LocalMachine) }
        elseif ($Wrapped.StartsWith('dpapi-user:')) { $k = [Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($Wrapped.Substring(11)), $e, [Security.Cryptography.DataProtectionScope]::CurrentUser) }
        else { return $null }
    }
    catch { return $null }
    if ($k.Length -ne 32) { return $null }
    return , $k
}
function Test-HccSameBytes { param([byte[]]$A, [byte[]]$B) if ($null -eq $A -or $null -eq $B -or $A.Length -ne $B.Length) { return $false }; $d = 0; for ($i = 0; $i -lt $A.Length; $i++) { $d = $d -bor ($A[$i] -bxor $B[$i]) }; return $d -eq 0 }
function Assert-HccGuestIdKeyId { param([string]$Id) if ($Id -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$') { throw "Key id '$Id' must be 1-32 letters, digits, '.', '_' or '-' (for example 2026-10)." } return $Id }
# Reads an offline copy written by this script: @{ KeyId = '2026-10' (or $null); Key = 32 bytes }. Refuses any other file (the backup key's copy too).
function Read-HccGuestIdOfflineCopy {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Offline copy not found: $Path (is the keys USB stick plugged in?)." }
    $lines = @(Get-Content -LiteralPath $Path)
    if ($lines.Count -eq 0 -or $lines[0] -notlike 'HCC guest ID key*') { throw "$Path is not an HCC guest ID key offline copy (the first line must start with 'HCC guest ID key'; the backup key's copy is imported with New-HccBackupKey.ps1)." }
    $idLine = @($lines | Where-Object { $_ -like 'Key id:*' }) | Select-Object -First 1
    $rawLine = @($lines | Where-Object { $_ -like 'Raw key*' }) | Select-Object -First 1
    if (-not $rawLine) { throw "$Path has no 'Raw key' line: it is not a complete guest ID key offline copy." }
    try { $k = [Convert]::FromBase64String(($rawLine -split ': ', 2)[1].Trim()) } catch { throw "$Path does not hold a readable key (the 'Raw key' line is damaged)." }
    if ($k.Length -ne 32) { [Array]::Clear($k, 0, $k.Length); throw "$Path does not hold a 32-byte key." }
    $id = if ($idLine) { ($idLine -split ':', 2)[1].Trim() } else { $null }
    return @{ KeyId = $id; Key = $k }
}
if ($MyInvocation.InvocationName -eq '.') { return }   # dot-sourced: functions only

$import = $PSCmdlet.ParameterSetName -eq 'Import'
if (-not (Test-Path -LiteralPath $SettingsFile)) { throw "Settings file not found: $SettingsFile. Install the server first (INSTALL.md step 3)." }
$json = Get-Content -LiteralPath $SettingsFile -Raw | ConvertFrom-Json
$existing = $null
if ($json.PSObject.Properties['Hcc'] -and $json.Hcc.PSObject.Properties['GuestIdKeys']) { $existing = $json.Hcc.GuestIdKeys }
$hasCurrent = $existing -and $existing.PSObject.Properties['Current'] -and $existing.Current
$existingKeys = if ($existing -and $existing.PSObject.Properties['Keys'] -and $existing.Keys) { $existing.Keys } else { $null }

if ($import) {
    # --- A07-03 / A09-07: install the key from its offline copy (replacement PC, Windows reinstalled, settings file lost) ---------------
    $copy = Read-HccGuestIdOfflineCopy -Path $ImportOfflineCopy
    $key = $copy.Key
    try {
        if ($KeyId -and $copy.KeyId -and $KeyId -ne $copy.KeyId) { throw "The offline copy is key '$($copy.KeyId)', not '$KeyId'. Stored ID numbers carry the id they were sealed with: import it under its own id (leave out -KeyId)." }
        if (-not $copy.KeyId -and -not $KeyId) { throw "$ImportOfflineCopy has no 'Key id' line: give the id it was made under with -KeyId (for example 2026-10)." }
        $id = if ($copy.KeyId) { $copy.KeyId } else { $KeyId }
        Assert-HccGuestIdKeyId $id | Out-Null
        $replacing = $false
        if ($existingKeys -and $existingKeys.PSObject.Properties[$id]) {
            $old = ConvertFrom-HccWrappedGuestIdKey -Wrapped ([string]$existingKeys.$id)
            $same = Test-HccSameBytes $old $key
            if ($old) { [Array]::Clear($old, 0, $old.Length) }
            if ($same) {
                $becomesCurrent = $MakeCurrent -and -not ($hasCurrent -and $existing.Current -eq $id)
                if (-not $becomesCurrent) { Write-Host "Guest ID key '$id' is already installed in $SettingsFile and opens on this PC: nothing to do."; return }
            }
            elseif (-not $Force) {
                if ($null -eq $old) { throw "Guest ID key '$id' is already in $SettingsFile but cannot be opened on this PC (it was probably copied from the old PC, or wrapped for another Windows account). Run again with -Force to replace that entry with the offline copy." }
                throw "Guest ID key '$id' in $SettingsFile is a DIFFERENT key from the offline copy. Replacing it would make every ID number sealed with the installed key unreadable. Check that this is the right offline copy; only if you are sure, run again with -Force."
            }
            else { $replacing = $true }
        }
        $setsCurrent = (-not $hasCurrent) -or $MakeCurrent -or ($hasCurrent -and $existing.Current -eq $id)
        $what = "Import guest ID key '$id' from $ImportOfflineCopy (DPAPI $Scope) into $SettingsFile" + $(if ($setsCurrent) { ' as the current key' } else { " next to the current key '$($existing.Current)'" })
        if (-not $PSCmdlet.ShouldProcess($SettingsFile, $what)) {
            Write-Host "DRY RUN: $what"
            if ($replacing) { Write-Host "DRY RUN: WARNING: -Force replaces the existing entry for '$id'; ID numbers sealed with a different key under that id would become unreadable" }
            Write-Host "DRY RUN: Hcc:GuestIdKeys gets Required=true$(if ($Scope -eq 'Machine') { ' and AllowMachineScope=true' }); safety copy $SettingsFile.before-guestid-key"
            Write-Host "DRY RUN: settings file then readable only by Administrators, SYSTEM and '$ServiceAccount' (icacls); the key is checked to open on this PC"
            Write-Host 'DRY RUN: then start (or restart) HccServer and run Hcc.Api.exe verify-guest-id. The raw key is never printed.'
            return
        }
        if ($replacing) { Write-Warning "Replacing guest ID key '$id' in $SettingsFile with the offline copy (-Force). If the replaced entry was a different key, ID numbers sealed with it can no longer be opened. The previous file is kept as $SettingsFile.before-guestid-key until you delete it." }
        $wrapped = ConvertTo-HccWrappedGuestIdKey -Key $key -Scope $Scope
        $check = ConvertFrom-HccWrappedGuestIdKey -Wrapped $wrapped
        $opens = Test-HccSameBytes $check $key
        if ($check) { [Array]::Clear($check, 0, $check.Length) }
        if (-not $opens) { throw "DPAPI ($Scope) could not round-trip the key on this PC; nothing was written." }
    }
    finally { [Array]::Clear($key, 0, $key.Length) }
    $KeyId = $id
}
else {
    if (-not $KeyId) { $KeyId = Get-Date -Format 'yyyy-MM' }
    Assert-HccGuestIdKeyId $KeyId | Out-Null
    $full = [IO.Path]::GetFullPath($OfflineCopyPath)
    $serverDir = [IO.Path]::GetFullPath((Split-Path -Parent $SettingsFile))
    if (("$full" + [IO.Path]::DirectorySeparatorChar).StartsWith(($serverDir.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) { throw 'The offline copy must NOT be inside the server folder: put it on a USB stick or another PC.' }
    if (Test-Path -LiteralPath $full) { throw "$full already exists; give a new file name (never overwrite an earlier offline copy)." }
    if ($hasCurrent) {
        if (-not $Rotate) { throw "A guest ID key ('$($existing.Current)') is already configured. Replacing it would make stored ID numbers unreadable. Use -Rotate -KeyId <new id> to add a new key and keep the old one (on a replacement PC: -ImportOfflineCopy <file>)." }
        if ($existingKeys -and $existingKeys.PSObject.Properties[$KeyId]) { throw "Key id '$KeyId' already exists; pick a new id." }
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
        $text = "HCC guest ID key (OFFLINE COPY - keep in the safe, never e-mail, never on the server PC)`r`nKey id: $KeyId`r`nCreated (UTC): $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm'))`r`nRaw key (base64, 32 bytes): $([Convert]::ToBase64String($key))`r`nRestore on a new PC: New-HccGuestIdKey.ps1 -ImportOfflineCopy <this file> (INSTALL.md section 6b).`r`n"
        [IO.File]::WriteAllText($full, $text, (New-Object Text.UTF8Encoding($false)))
    }
    finally { [Array]::Clear($key, 0, $key.Length) }
    $setsCurrent = $true
}

if (-not $json.PSObject.Properties['Hcc']) { $json | Add-Member -NotePropertyName Hcc -NotePropertyValue ([pscustomobject]@{}) }
if (-not $json.Hcc.PSObject.Properties['GuestIdKeys']) { $json.Hcc | Add-Member -NotePropertyName GuestIdKeys -NotePropertyValue ([pscustomobject]@{ Current = $KeyId; Keys = [pscustomobject]@{} }) }
$g = $json.Hcc.GuestIdKeys
if ($setsCurrent) { $g | Add-Member -NotePropertyName Current -NotePropertyValue $KeyId -Force }
$g | Add-Member -NotePropertyName Required -NotePropertyValue $true -Force
if ($Scope -eq 'Machine') { $g | Add-Member -NotePropertyName AllowMachineScope -NotePropertyValue $true -Force }
if (-not $g.PSObject.Properties['Keys'] -or -not $g.Keys) { $g | Add-Member -NotePropertyName Keys -NotePropertyValue ([pscustomobject]@{}) -Force }
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
if ($import) {
    Write-Host "Guest ID key '$KeyId' imported from the offline copy ($Scope DPAPI) into $SettingsFile$(if ($setsCurrent) { ' as the current key' } else { " next to the current key '$($g.Current)'" }); it opens on this PC."
    Write-Host "Next: start (or restart) HccServer, then in the server folder run: .\Hcc.Api.exe verify-guest-id   (it must say 'Guest ID check: OK'). Delete ${backup} once the server starts."
}
else {
    Write-Host "Guest ID key '$KeyId' installed ($Scope DPAPI) in $SettingsFile."
    Write-Host "Offline copy written to ${full}: copy it to the second keys USB stick (DEC-087), put the sticks in the safe / off site now, and delete ${backup} once you have checked the server starts."
    Write-Host 'Next: Restart-Service HccServer, then check in a guest with an ID number.'
}
