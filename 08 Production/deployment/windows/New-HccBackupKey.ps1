<#
.SYNOPSIS
  Creates the backup encryption key (F-H03) once, or imports it from its offline copy on a replacement PC.
.DESCRIPTION
  SQL Server Express cannot encrypt backups, so Backup-HccDatabase.ps1 encrypts every verified .bak (AES-256 + HMAC-SHA256, Hcc-Common.ps1)
  with this key and deletes the plain file; Restore-HccDrill.ps1 checks and decrypts it. This script:
    1. makes a random 32-byte key (or reads it from -ImportOfflineCopy),
    2. stores it wrapped with Windows DPAPI for THIS PC in -KeyFile (default C:\ProgramData\HCC\Keys\hcc-backup-key.txt), a file only
       SYSTEM and Administrators can read (the scheduled backup tasks run as SYSTEM or as -ReaderAccount),
    3. writes the raw key to -OfflineCopyPath (a USB stick, not this PC). Keep that stick in the safe, NOT with the backup USB disk.
       Without it the backups cannot be opened on another PC or after Windows is reinstalled.
  Refuses to replace an existing key (older backups need it). The raw key is never printed. -WhatIf prints the plan and writes nothing.
.EXAMPLE
  .\New-HccBackupKey.ps1 -OfflineCopyPath "E:\HCC-backup-key.txt"
  .\New-HccBackupKey.ps1 -ImportOfflineCopy "E:\HCC-backup-key.txt"      # on a replacement PC, before the first restore
#>
[CmdletBinding(SupportsShouldProcess, DefaultParameterSetName = 'New')]
param(
    [Parameter(Mandatory, ParameterSetName = 'New')][string]$OfflineCopyPath,
    [Parameter(Mandatory, ParameterSetName = 'Import')][string]$ImportOfflineCopy,
    [string]$KeyFile,
    [string[]]$ReaderAccount = @()
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Hcc-Common.ps1"
if (-not $KeyFile) { $KeyFile = Get-HccDefaultBackupKeyFile }
if (Test-Path -LiteralPath $KeyFile) { throw "A backup key already exists at $KeyFile. It is never replaced: older backups need it." }
$keyDir = Split-Path -Parent $KeyFile

if ($PSCmdlet.ParameterSetName -eq 'New') {
    $full = [IO.Path]::GetFullPath($OfflineCopyPath)
    if ($full.StartsWith([IO.Path]::GetFullPath($keyDir), [StringComparison]::OrdinalIgnoreCase)) { throw 'The offline copy must not be next to the key: put it on a USB stick for the safe.' }
    if (Test-Path -LiteralPath $full) { throw "$full already exists; give a new file name." }
    $what = "Create the backup key in $KeyFile (DPAPI, this PC; SYSTEM + Administrators only), offline copy -> $full"
}
else {
    if (-not (Test-Path -LiteralPath $ImportOfflineCopy)) { throw "Offline copy not found: $ImportOfflineCopy" }
    $what = "Import the backup key from $ImportOfflineCopy into $KeyFile (DPAPI, this PC; SYSTEM + Administrators only)"
}
if (-not $PSCmdlet.ShouldProcess($KeyFile, $what)) {
    Write-Host "DRY RUN: $what"
    Write-Host "DRY RUN: folder ${keyDir}: inheritance removed, SYSTEM and Administrators only. The raw key is never printed."
    return
}

if (-not (Test-Path -LiteralPath $keyDir)) { Set-HccProtectedDirectory -Path $keyDir -ReadAccounts $ReaderAccount }
if ($PSCmdlet.ParameterSetName -eq 'New') {
    $key = New-HccRandomBytes 32
    try {
        $fp = (Get-HccBackupSubKeys -Key $key).FingerprintHex
        $dir = Split-Path -Parent $full
        if ($dir -and -not (Test-Path -LiteralPath $dir)) { throw "Folder for the offline copy not found: $dir (is the USB stick plugged in?)." }
        $text = "HCC BACKUP key (OFFLINE COPY - keep in the safe, apart from the backup disk; never e-mail it)`r`nKey fingerprint: $fp`r`nCreated (UTC): $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm'))`r`nRaw key (base64, 32 bytes): $([Convert]::ToBase64String($key))`r`nRestore on a new PC: New-HccBackupKey.ps1 -ImportOfflineCopy <this file> (INSTALL.md section 7).`r`n"
        [IO.File]::WriteAllText($full, $text, (New-Object Text.UTF8Encoding($false)))
        Save-HccBackupKey -Key $key -Path $KeyFile -ReaderAccounts $ReaderAccount | Out-Null
    }
    finally { [Array]::Clear($key, 0, $key.Length) }
    Write-Host "Backup key $fp created in $KeyFile. Offline copy: $full - put that USB stick in the safe now (not with the backup disk)."
}
else {
    $line = @(Get-Content -LiteralPath $ImportOfflineCopy | Where-Object { $_ -like 'Raw key*' }) | Select-Object -First 1
    if (-not $line) { throw "$ImportOfflineCopy is not an HCC backup key offline copy." }
    $key = [Convert]::FromBase64String(($line -split ': ', 2)[1].Trim())
    try {
        if ($key.Length -ne 32) { throw 'The offline copy does not hold a 32-byte key.' }
        $fp = Save-HccBackupKey -Key $key -Path $KeyFile -ReaderAccounts $ReaderAccount
    }
    finally { [Array]::Clear($key, 0, $key.Length) }
    Write-Host "Backup key $fp imported into $KeyFile. Backups made with it can now be restored on this PC."
}
