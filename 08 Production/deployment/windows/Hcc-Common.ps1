# Shared helpers for the HCC deployment scripts. Dot-source it: . "$PSScriptRoot\Hcc-Common.ps1"
# Works on Windows PowerShell 5.1 (the hotel PC) and PowerShell 7. No secrets are stored or logged.
# ETP-REUSE: these helpers are original HCC stand-ins; replace/align with ETP's backup/restore script library when V:\ETP is available.

Set-StrictMode -Version 2.0

# --- safety -----------------------------------------------------------------------------------------------------------
# Only HCC databases may be backed up or restored here. ETP databases (EtpReporting, EtpScratch_*) and system databases never.
function Assert-HccDatabaseName {
    param([Parameter(Mandatory)][string]$Name)
    if ($Name -notmatch '^Hcc[A-Za-z0-9_]{0,60}$') { throw "Refusing database '$Name': only databases named Hcc, Hcc_..., HccTest_... or HccDrill_... are handled by the HCC scripts." }
    return $Name
}

# Restore drills may only create and drop scratch databases with these prefixes.
function Assert-HccScratchName {
    param([Parameter(Mandatory)][string]$Name)
    if ($Name -notmatch '^(HccDrill|HccTest)_[A-Za-z0-9_]{1,60}$') { throw "Refusing scratch database '$Name': it must start with HccDrill_ or HccTest_." }
    return $Name
}

function ConvertTo-HccSqlLiteral { param([string]$Value) return "N'" + $Value.Replace("'", "''") + "'" }
function ConvertTo-HccSqlName { param([string]$Value) return '[' + $Value.Replace(']', ']]') + ']' }

# --- configuration ------------------------------------------------------------------------------------------------------
# hcc-ops.json (next to the scripts, created by the installer from hcc-ops.sample.json) supplies defaults; parameters win.
function Get-HccOpsConfig {
    param([string]$Path)
    if (-not $Path) { $Path = Join-Path $PSScriptRoot 'hcc-ops.json' }
    $cfg = [ordered]@{ SqlInstance = '.\SQLEXPRESS'; Database = 'Hcc'; BackupDirectory = ''; LogDirectory = ''; RetentionDays = 30; KeepMinFulls = 3; PreMigrateRetentionDays = 90; ScratchDirectory = ''; TrustServerCertificate = $false
        EncryptBackups = $true; BackupKeyFile = ''; BackupAccount = '' }
    if (Test-Path -LiteralPath $Path) {
        $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        foreach ($p in $json.PSObject.Properties) { if ($cfg.Contains($p.Name)) { $cfg[$p.Name] = $p.Value } }
    }
    return $cfg
}

function Write-HccLog {
    param([string]$Message, [string]$LogFile, [ValidateSet('INFO', 'WARN', 'ERROR')][string]$Level = 'INFO')
    $line = '{0} {1} {2}' -f (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss\Z'), $Level, $Message
    if ($Level -eq 'ERROR') { Write-Host $line -ForegroundColor Red } elseif ($Level -eq 'WARN') { Write-Host $line -ForegroundColor Yellow } else { Write-Host $line }
    if ($LogFile) {
        $dir = Split-Path -Parent $LogFile
        if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8
    }
}

# --- SQL via sqlcmd (Windows login of whoever runs the script; -b makes SQL errors a non-zero exit) -------------------------
function Invoke-HccSql {
    param([Parameter(Mandatory)][string]$Instance, [Parameter(Mandatory)][string]$Query, [switch]$TrustServerCertificate, [switch]$Scalar, [switch]$Table)
    $sqlcmd = (Get-Command sqlcmd.exe -ErrorAction SilentlyContinue)
    if (-not $sqlcmd) { throw 'sqlcmd.exe was not found. Install "Microsoft Command Line Utilities for SQL Server" (see INSTALL.md).' }
    $a = @('-S', $Instance, '-E', '-b', '-l', '30', '-Q', $Query)
    if ($TrustServerCertificate) { $a += '-C' }
    if ($Scalar -or $Table) { $a += @('-h', '-1', '-W') }
    if ($Table) { $a += @('-s', '|') }
    $out = & $sqlcmd.Source @a 2>&1
    if ($LASTEXITCODE -ne 0) { throw ("sqlcmd failed (exit $LASTEXITCODE): " + (($out | Out-String).Trim())) }
    $text = ($out | Out-String).Trim()
    if ($Scalar) { return (($text -split "`r?`n" | Where-Object { $_ -and $_ -notmatch '^\(\d+ rows? affected\)$' }) | Select-Object -First 1) }
    return $text
}

# --- backup file naming, retention, restore chain (pure functions; unit-tested without SQL Server) ---------------------------
# <Database>_FULL_<yyyyMMddTHHmmssZ>.bak, <Database>_DIFF_<...>.bak, <Database>_PRE_<...>_<tag>.bak (COPY_ONLY before a migration);
# each may carry the .hccenc suffix once encrypted (F-H03): Hcc_FULL_20261025T120000Z.bak.hccenc
function New-HccBackupFileName {
    param([Parameter(Mandatory)][string]$Database, [Parameter(Mandatory)][ValidateSet('Full', 'Differential')][string]$Type, [Parameter(Mandatory)][datetime]$UtcNow, [string]$Tag)
    $stamp = $UtcNow.ToString('yyyyMMdd\THHmmss\Z', [Globalization.CultureInfo]::InvariantCulture)
    if ($Tag) {
        if ($Type -ne 'Full') { throw 'A tagged (pre-migration) backup is always a full backup.' }
        if ($Tag -notmatch '^[A-Za-z0-9_]{1,60}$') { throw "Backup tag '$Tag' may contain only letters, digits and underscore." }
        return "{0}_PRE_{1}_{2}.bak" -f $Database, $stamp, $Tag
    }
    $kind = if ($Type -eq 'Full') { 'FULL' } else { 'DIFF' }
    return "{0}_{1}_{2}.bak" -f $Database, $kind, $stamp
}

function ConvertFrom-HccBackupFileName {
    param([Parameter(Mandatory)][string]$Name)
    $m = [regex]::Match($Name, '^(?<db>[A-Za-z0-9_]+?)_(?<type>FULL|DIFF|PRE)_(?<ts>\d{8}T\d{6}Z)(?:_(?<tag>[A-Za-z0-9_]+))?\.bak(?<enc>\.hccenc)?$')
    if (-not $m.Success) { return $null }
    $t = [datetime]::ParseExact($m.Groups['ts'].Value, "yyyyMMdd'T'HHmmss'Z'", [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal)
    return [pscustomobject]@{ Name = $Name; Database = $m.Groups['db'].Value; Type = $m.Groups['type'].Value; TimeUtc = $t; Tag = $m.Groups['tag'].Value; Encrypted = $m.Groups['enc'].Success }
}

# Which backup file names to delete. Keeps at least $KeepMinFulls newest FULL files regardless of age; a FULL older than
# $RetentionDays (and not among the newest $KeepMinFulls) is deleted; a DIFF is deleted when no kept FULL precedes it or it is
# older than the oldest kept FULL; PRE files are deleted after $PreMigrateRetentionDays. Unrecognised names are never touched.
function Select-HccBackupsToDelete {
    param([string[]]$Names, [Parameter(Mandatory)][datetime]$UtcNow, [int]$RetentionDays = 30, [int]$KeepMinFulls = 3, [int]$PreMigrateRetentionDays = 90)
    $items = @($Names | ForEach-Object { ConvertFrom-HccBackupFileName $_ } | Where-Object { $_ })
    $fulls = @($items | Where-Object Type -eq 'FULL' | Sort-Object TimeUtc -Descending)
    $keep = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $fulls.Count; $i++) {
        if ($i -lt $KeepMinFulls -or $fulls[$i].TimeUtc -ge $UtcNow.AddDays(-$RetentionDays)) { $keep.Add($fulls[$i]) }
    }
    $delete = New-Object System.Collections.Generic.List[string]
    foreach ($f in $fulls) { if (-not ($keep | Where-Object Name -eq $f.Name)) { $delete.Add($f.Name) } }
    $oldestKept = if ($keep.Count) { ($keep | Sort-Object TimeUtc | Select-Object -First 1).TimeUtc } else { $null }
    foreach ($d in ($items | Where-Object Type -eq 'DIFF')) { if ($null -eq $oldestKept -or $d.TimeUtc -lt $oldestKept) { $delete.Add($d.Name) } }
    foreach ($p in ($items | Where-Object Type -eq 'PRE')) { if ($p.TimeUtc -lt $UtcNow.AddDays(-$PreMigrateRetentionDays)) { $delete.Add($p.Name) } }
    return ,@($delete)
}

# The restore chain for the newest recoverable point: the newest FULL (not PRE) plus the newest DIFF taken after it.
function Select-HccRestoreChain {
    param([string[]]$Names, [string]$Database)
    $items = @($Names | ForEach-Object { ConvertFrom-HccBackupFileName $_ } | Where-Object { $_ -and (-not $Database -or $_.Database -eq $Database) })
    $full = $items | Where-Object Type -eq 'FULL' | Sort-Object TimeUtc -Descending | Select-Object -First 1
    if (-not $full) { return $null }
    $diff = $items | Where-Object { $_.Type -eq 'DIFF' -and $_.TimeUtc -gt $full.TimeUtc } | Sort-Object TimeUtc -Descending | Select-Object -First 1
    return [pscustomobject]@{ Full = $full.Name; Differential = $(if ($diff) { $diff.Name } else { $null }) }
}

# Row-count comparison for the restore drill. $Restored/$Live are hashtables of "schema.table" -> row count.
# Fails when a live table is missing from the restore, or when the restore has no rows at all while live has some.
# Counts may legitimately differ (the live database keeps changing after the backup); differences are reported, not failed.
function Compare-HccRowCounts {
    param([hashtable]$Restored, [hashtable]$Live)
    $missing = @($Live.Keys | Where-Object { -not $Restored.ContainsKey($_) } | Sort-Object)
    $rs = 0; foreach ($v in $Restored.Values) { $rs += [long]$v }
    $ls = 0; foreach ($v in $Live.Values) { $ls += [long]$v }
    $diff = @($Live.Keys | Where-Object { $Restored.ContainsKey($_) -and [long]$Restored[$_] -ne [long]$Live[$_] } | Sort-Object)
    $ok = ($missing.Count -eq 0) -and -not ($rs -eq 0 -and $ls -gt 0)
    return [pscustomobject]@{ Ok = $ok; MissingTables = $missing; DifferingTables = $diff; RestoredRows = $rs; LiveRows = $ls; Tables = $Restored.Count }
}

# --- folder protection (F-H03, F-M06) ------------------------------------------------------------------------------------------
# Well-known SIDs (language independent): SYSTEM, BUILTIN\Administrators, NT SERVICE\TrustedInstaller.
$script:HccSidSystem = 'S-1-5-18'
$script:HccSidAdministrators = 'S-1-5-32-544'
$script:HccSidTrustedInstaller = 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464'

# SQL Server service account of an instance (BACKUP/RESTORE files are read and written by the SQL Server process, not by the script).
function Get-HccSqlServiceAccount {
    param([string]$Instance = '.\SQLEXPRESS')
    $name = if ($Instance -match '\\(.+)$') { $Matches[1] } else { 'MSSQLSERVER' }
    if ($name -eq 'MSSQLSERVER') { return 'NT SERVICE\MSSQLSERVER' }
    return 'NT SERVICE\MSSQL$' + $name
}

function ConvertTo-HccSid {
    param([Parameter(Mandatory)][string]$Account)
    if ($Account -match '^\*?(S-1-[0-9-]+)$') { return $Matches[1] }
    if ($Account -eq 'SYSTEM' -or $Account -eq 'NT AUTHORITY\SYSTEM') { return $script:HccSidSystem }
    return (New-Object Security.Principal.NTAccount($Account)).Translate([Security.Principal.SecurityIdentifier]).Value
}

function Get-HccAccountName {
    param([string]$Sid)
    try { return (New-Object Security.Principal.SecurityIdentifier($Sid)).Translate([Security.Principal.NTAccount]).Value } catch { return $Sid }
}

# icacls arguments for a protected folder: inheritance removed; SYSTEM and Administrators full control; each -ModifyAccounts entry
# Modify; each -ReadAccounts entry read. Pure (unit-tested). Accounts may be names or *S-1-... SIDs.
function Get-HccProtectedDirectoryArgs {
    param([Parameter(Mandatory)][string]$Path, [string[]]$ModifyAccounts = @(), [string[]]$ReadAccounts = @())
    $a = @($Path, '/inheritance:r', '/grant:r', "*${script:HccSidSystem}:(OI)(CI)F", "*${script:HccSidAdministrators}:(OI)(CI)F")
    foreach ($m in @($ModifyAccounts | Where-Object { $_ } | Select-Object -Unique)) { $a += ('{0}:(OI)(CI)M' -f $m) }
    foreach ($r in @($ReadAccounts | Where-Object { $_ } | Select-Object -Unique)) { $a += ('{0}:(OI)(CI)RX' -f $r) }
    return , $a
}

# Creates the folder if needed and applies the protected ACL (propagated to existing files). Throws if icacls fails.
function Set-HccProtectedDirectory {
    param([Parameter(Mandatory)][string]$Path, [string[]]$ModifyAccounts = @(), [string[]]$ReadAccounts = @())
    if (-not (Test-Path -LiteralPath $Path)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
    $a = Get-HccProtectedDirectoryArgs -Path $Path -ModifyAccounts $ModifyAccounts -ReadAccounts $ReadAccounts
    $out = & icacls.exe @a 2>&1
    if ($LASTEXITCODE -ne 0) { throw "icacls could not protect ${Path}: $(($out | Out-String).Trim())" }
}

# Problems with a folder that must be private to SYSTEM, Administrators and -AllowedAccounts: inheritance still on, or any other
# account allowed in. Empty list = protected. Inherit-only entries (CREATOR OWNER templates) are ignored.
function Test-HccProtectedDirectory {
    param([Parameter(Mandatory)][string]$Path, [string[]]$AllowedAccounts = @())
    if (-not (Test-Path -LiteralPath $Path)) { return "$Path does not exist" }
    $problems = @()
    $allowed = @($script:HccSidSystem, $script:HccSidAdministrators)
    foreach ($a in @($AllowedAccounts | Where-Object { $_ })) { try { $allowed += (ConvertTo-HccSid $a) } catch { $problems += "account '$a' does not exist on this PC" } }
    $acl = Get-Acl -LiteralPath $Path
    if (-not $acl.AreAccessRulesProtected) { $problems += "$Path still inherits permissions from its parent folder" }
    foreach ($r in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($r.AccessControlType -ne 'Allow') { continue }
        if (($r.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -ne 0) { continue }
        if ($allowed -notcontains $r.IdentityReference.Value) { $problems += ('{0} has access to {1}' -f (Get-HccAccountName $r.IdentityReference.Value), $Path) }
    }
    return $problems
}

# F-M06: a script folder run by a scheduled task as SYSTEM must be writable only by SYSTEM, Administrators and TrustedInstaller.
# Checks the folder itself and every .ps1/.psm1/.psd1/.json in it. Returns the problems (empty = safe).
function Test-HccAdminOnlyWritable {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return "$Path does not exist" }
    # write/append data, write (extended) attributes, change permissions, take ownership, GENERIC_ALL, GENERIC_WRITE
    $writeMask = 0x2 -bor 0x4 -bor 0x10 -bor 0x100 -bor 0x40000 -bor 0x80000 -bor 0x10000000 -bor 0x40000000
    $ok = @($script:HccSidSystem, $script:HccSidAdministrators, $script:HccSidTrustedInstaller)
    $items = @(Get-Item -LiteralPath $Path) + @(Get-ChildItem -LiteralPath $Path -File | Where-Object { @('.ps1', '.psm1', '.psd1', '.json') -contains $_.Extension.ToLowerInvariant() })
    $problems = @()
    foreach ($i in $items) {
        $acl = Get-Acl -LiteralPath $i.FullName
        foreach ($r in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
            if ($r.AccessControlType -ne 'Allow') { continue }
            if (($r.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -ne 0) { continue }
            if (([int64][int]$r.FileSystemRights -band [int64]$writeMask) -eq 0) { continue }
            if ($ok -contains $r.IdentityReference.Value) { continue }
            $problems += ('{0} can change {1}' -f (Get-HccAccountName $r.IdentityReference.Value), $i.FullName)
        }
    }
    return $problems
}

# --- backup encryption (F-H03) ---------------------------------------------------------------------------------------------------
# SQL Server Express cannot encrypt backups, so each verified .bak is encrypted by the script into <name>.bak.hccenc and the plain
# file is deleted. Format (version 1): "HCCBAK1"+0x00 | 8-byte key fingerprint | 16-byte IV | AES-256-CBC (PKCS7) ciphertext |
# HMAC-SHA256 tag over everything before it (encrypt-then-MAC). The sub-keys are HMAC-SHA256(master, label). Built only on .NET
# classes that ship with Windows (Windows PowerShell 5.1 and PowerShell 7): no 7-Zip or other software.
# The 32-byte master key is wrapped with DPAPI for this PC (LocalMachine scope, entropy "HCC backup key v1") in a key file that only
# SYSTEM and Administrators can read (New-HccBackupKey.ps1); the raw key also goes to an offline copy in the safe, so backups can be
# opened on a replacement PC. A backup copied off the PC (USB disk, NAS) is useless without the key.
$script:HccBackupMagic = [byte[]](0x48, 0x43, 0x43, 0x42, 0x41, 0x4B, 0x31, 0x00)
$script:HccBackupEntropy = [Text.Encoding]::ASCII.GetBytes('HCC backup key v1')
$script:HccEncryptedSuffix = '.hccenc'

function Get-HccDefaultBackupKeyFile { return (Join-Path $env:ProgramData 'HCC\Keys\hcc-backup-key.txt') }

function New-HccRandomBytes {
    param([int]$Count = 32)
    $k = New-Object byte[] $Count
    $r = [Security.Cryptography.RandomNumberGenerator]::Create(); try { $r.GetBytes($k) } finally { $r.Dispose() }
    return , $k
}

function Get-HccBackupSubKeys {
    param([Parameter(Mandatory)][byte[]]$Key)
    if ($Key.Length -ne 32) { throw 'The backup key must be 32 bytes.' }
    $h = New-Object Security.Cryptography.HMACSHA256 (, $Key)
    try {
        $enc = $h.ComputeHash([Text.Encoding]::ASCII.GetBytes('HCC backup encryption v1'))
        $mac = $h.ComputeHash([Text.Encoding]::ASCII.GetBytes('HCC backup authentication v1'))
        $id = $h.ComputeHash([Text.Encoding]::ASCII.GetBytes('HCC backup key id v1'))
    }
    finally { $h.Dispose() }
    $fp = New-Object byte[] 8; [Array]::Copy($id, $fp, 8)
    return [pscustomobject]@{ Enc = $enc; Mac = $mac; Fingerprint = $fp; FingerprintHex = ([BitConverter]::ToString($fp) -replace '-', '').ToLowerInvariant() }
}

function Read-HccStreamExactly {
    param($Stream, [byte[]]$Buffer, [int]$Count)
    $read = 0
    while ($read -lt $Count) { $n = $Stream.Read($Buffer, $read, $Count - $read); if ($n -le 0) { throw 'Unexpected end of file.' }; $read += $n }
}

function New-HccAes {
    param([byte[]]$Key, [byte[]]$IV)
    $aes = [Security.Cryptography.Aes]::Create()
    $aes.KeySize = 256; $aes.Mode = [Security.Cryptography.CipherMode]::CBC; $aes.Padding = [Security.Cryptography.PaddingMode]::PKCS7
    $aes.Key = $Key; $aes.IV = $IV
    return $aes
}

# Encrypts $Source into $Destination (must not exist). Streams in 1 MB blocks; works for multi-GB files.
function Protect-HccBackupFile {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination, [Parameter(Mandatory)][byte[]]$Key)
    $k = Get-HccBackupSubKeys -Key $Key
    $iv = New-HccRandomBytes 16
    $header = New-Object byte[] 32
    [Array]::Copy($script:HccBackupMagic, 0, $header, 0, 8); [Array]::Copy($k.Fingerprint, 0, $header, 8, 8); [Array]::Copy($iv, 0, $header, 16, 16)
    $aes = New-HccAes -Key $k.Enc -IV $iv
    $hmac = New-Object Security.Cryptography.HMACSHA256 (, $k.Mac)
    $in = [IO.File]::OpenRead($Source)
    $out = $null
    try {
        $out = New-Object IO.FileStream($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $enc = $aes.CreateEncryptor()
        $out.Write($header, 0, 32); [void]$hmac.TransformBlock($header, 0, 32, $null, 0)
        $size = 1048576; $buf = New-Object byte[] $size; $cipher = New-Object byte[] ($size + 16)
        $remaining = $in.Length
        while ($remaining -gt $size) {
            Read-HccStreamExactly $in $buf $size
            $n = $enc.TransformBlock($buf, 0, $size, $cipher, 0)
            $out.Write($cipher, 0, $n); [void]$hmac.TransformBlock($cipher, 0, $n, $null, 0)
            $remaining -= $size
        }
        if ($remaining -gt 0) { Read-HccStreamExactly $in $buf ([int]$remaining) }
        $last = $enc.TransformFinalBlock($buf, 0, [int]$remaining)
        $out.Write($last, 0, $last.Length); [void]$hmac.TransformBlock($last, 0, $last.Length, $null, 0)
        [void]$hmac.TransformFinalBlock((New-Object byte[] 0), 0, 0)
        $out.Write($hmac.Hash, 0, 32)
        $enc.Dispose()
    }
    catch {
        if ($out) { $out.Dispose(); $out = $null; Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue }
        throw
    }
    finally { $in.Dispose(); if ($out) { $out.Dispose() }; $aes.Dispose(); $hmac.Dispose() }
}

# Checks the tag over the whole file before writing a single byte, then decrypts $Source into $Destination (must not exist).
# Throws "different backup key" or "damaged or altered" without writing anything.
function Unprotect-HccBackupFile {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination, [Parameter(Mandatory)][byte[]]$Key)
    $k = Get-HccBackupSubKeys -Key $Key
    $in = [IO.File]::OpenRead($Source)
    try {
        $len = $in.Length
        if ($len -lt 80 -or (($len - 64) % 16) -ne 0) { throw "$Source is not an HCC encrypted backup (wrong size)." }
        $header = New-Object byte[] 32; Read-HccStreamExactly $in $header 32
        for ($i = 0; $i -lt 8; $i++) { if ($header[$i] -ne $script:HccBackupMagic[$i]) { throw "$Source is not an HCC encrypted backup." } }
        $fp = ([BitConverter]::ToString($header, 8, 8) -replace '-', '').ToLowerInvariant()
        if ($fp -ne $k.FingerprintHex) { throw "$Source was encrypted with a different backup key (key fingerprint $fp; this PC's key is $($k.FingerprintHex)). Import the matching offline copy with New-HccBackupKey.ps1 -ImportOfflineCopy." }
        $iv = New-Object byte[] 16; [Array]::Copy($header, 16, $iv, 0, 16)
        $size = 1048576; $buf = New-Object byte[] $size
        $hmac = New-Object Security.Cryptography.HMACSHA256 (, $k.Mac)
        try {
            [void]$hmac.TransformBlock($header, 0, 32, $null, 0)
            $remaining = $len - 64
            while ($remaining -gt 0) { $n = [int][Math]::Min($size, $remaining); Read-HccStreamExactly $in $buf $n; [void]$hmac.TransformBlock($buf, 0, $n, $null, 0); $remaining -= $n }
            [void]$hmac.TransformFinalBlock((New-Object byte[] 0), 0, 0)
            $tag = New-Object byte[] 32; Read-HccStreamExactly $in $tag 32
            $diff = 0; for ($i = 0; $i -lt 32; $i++) { $diff = $diff -bor ($tag[$i] -bxor $hmac.Hash[$i]) }
            if ($diff -ne 0) { throw "$Source is damaged or was altered (integrity check failed); nothing was restored." }
        }
        finally { $hmac.Dispose() }

        $in.Position = 32
        $aes = New-HccAes -Key $k.Enc -IV $iv
        $out = $null
        try {
            $out = New-Object IO.FileStream($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            $dec = $aes.CreateDecryptor()
            $plain = New-Object byte[] ($size + 16)
            $remaining = $len - 64
            while ($remaining -gt 0) {
                $n = [int][Math]::Min($size, $remaining); Read-HccStreamExactly $in $buf $n; $remaining -= $n
                if ($remaining -gt 0) { $m = $dec.TransformBlock($buf, 0, $n, $plain, 0); $out.Write($plain, 0, $m) }
                else { $last = $dec.TransformFinalBlock($buf, 0, $n); $out.Write($last, 0, $last.Length) }
            }
            $dec.Dispose()
        }
        catch {
            if ($out) { $out.Dispose(); $out = $null; Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue }
            throw
        }
        finally { if ($out) { $out.Dispose() }; $aes.Dispose() }
    }
    finally { $in.Dispose() }
}

# Key file text: a comment, the fingerprint and "dpapi-machine:<base64>" (DPAPI for this PC). Readable only by SYSTEM, Administrators
# and -ReaderAccounts (the tests pass the current user). Refuses to overwrite. Returns the key fingerprint.
function Save-HccBackupKey {
    param([Parameter(Mandatory)][byte[]]$Key, [Parameter(Mandatory)][string]$Path, [string[]]$ReaderAccounts = @())
    if (Test-Path -LiteralPath $Path) { throw "$Path already exists; a backup key is never overwritten (older backups need it)." }
    Add-Type -AssemblyName System.Security
    $k = Get-HccBackupSubKeys -Key $Key
    $blob = [Security.Cryptography.ProtectedData]::Protect($Key, $script:HccBackupEntropy, [Security.Cryptography.DataProtectionScope]::LocalMachine)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $text = "HCC backup encryption key (DPAPI, this PC only). Do not copy it; the offline copy is in the safe.`r`nfingerprint: $($k.FingerprintHex)`r`ndpapi-machine:$([Convert]::ToBase64String($blob))`r`n"
    [IO.File]::WriteAllText($Path, $text, (New-Object Text.UTF8Encoding($false)))
    $a = @($Path, '/inheritance:r', '/grant:r', "*${script:HccSidSystem}:(F)", "*${script:HccSidAdministrators}:(F)") + @($ReaderAccounts | Where-Object { $_ } | ForEach-Object { "${_}:(R)" })
    $out = & icacls.exe @a 2>&1
    if ($LASTEXITCODE -ne 0) { throw "icacls could not protect ${Path}: $(($out | Out-String).Trim())" }
    return $k.FingerprintHex
}

function Read-HccBackupKey {
    param([string]$Path)
    if (-not $Path) { $Path = Get-HccDefaultBackupKeyFile }
    if (-not (Test-Path -LiteralPath $Path)) { throw "No backup encryption key at $Path. Run New-HccBackupKey.ps1 once (INSTALL.md section 7), or set EncryptBackups to false in hcc-ops.json (not recommended)." }
    Add-Type -AssemblyName System.Security
    $line = @(Get-Content -LiteralPath $Path | Where-Object { $_ -like 'dpapi-machine:*' }) | Select-Object -First 1
    if (-not $line) { throw "$Path is not an HCC backup key file." }
    $key = [Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($line.Substring('dpapi-machine:'.Length)), $script:HccBackupEntropy, [Security.Cryptography.DataProtectionScope]::LocalMachine)
    if ($key.Length -ne 32) { throw "$Path does not hold a 32-byte key." }
    return , $key
}

# --- SQL permissions (F-L08) -----------------------------------------------------------------------------------------------------
# The HCC service never changes the schema: migrations run only from "Hcc.Api.exe migrate", started by the installing administrator
# (sysadmin) under their own Windows login. So the runtime login needs only data access: read, write (tables and sequences) and
# EXECUTE (procedures, functions, table types). No db_owner, no ALTER: it cannot disable the append-only audit trigger or change
# tables. Returns the statements for database user $User (already quoted with ConvertTo-HccSqlName).
function Get-HccRuntimeGrantSql {
    param([Parameter(Mandatory)][string]$User)
    return "ALTER ROLE db_datareader ADD MEMBER $User; ALTER ROLE db_datawriter ADD MEMBER $User; GRANT EXECUTE TO $User;"
}

# --- masters import (A02b-01, Import-HccMasters.ps1) ----------------------------------------------------------------------------
# Pure helpers (unit-tested without a server): which CSV files to send and in which order, the request bodies, and the report text.
# The order is the server's dependency order (deployment\import-templates\README.md "The order to import"): a file may only refer
# to masters imported before it (rooms need room types, opening balances need ledgers, the menu needs outlets).
$script:HccImportOrder = @('room-types', 'rooms', 'tariffs', 'seasons', 'staff', 'ledgers', 'opening-balances', 'till-cash', 'guests', 'pos-outlets', 'pos-menu',
    'departments', 'companies', 'suppliers', 'stock-items')
$script:HccImportNeeds = @{ 'rooms' = 'room-types'; 'tariffs' = 'room-types'; 'seasons' = 'room-types'; 'companies' = 'room-types'; 'opening-balances' = 'ledgers'; 'pos-menu' = 'pos-outlets' }

function Get-HccImportOrder { return , @($script:HccImportOrder) }

# The server address: -ServerUrl, else "ServerUrl" in hcc-ops.json, else https://localhost:5443 (the server PC itself after setup).
function Get-HccImportServerUrl {
    param([string]$ServerUrl, [string]$ConfigPath)
    if ($ServerUrl) { return $ServerUrl }
    if (-not $ConfigPath) { $ConfigPath = Join-Path $PSScriptRoot 'hcc-ops.json' }
    if (Test-Path -LiteralPath $ConfigPath) {
        $json = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
        if ($json.PSObject.Properties['ServerUrl'] -and $json.ServerUrl) { return [string]$json.ServerUrl }
    }
    return 'https://localhost:5443'
}

# https://<server>:5443 -> https://<server>:5443/api/v1. A password is never sent over plain HTTP to another PC: http:// is accepted
# only for this PC itself (127.0.0.1 / localhost, used by the tests).
function Get-HccImportApiBase {
    param([Parameter(Mandatory)][string]$ServerUrl)
    $u = $null
    if (-not [Uri]::TryCreate($ServerUrl.Trim(), [UriKind]::Absolute, [ref]$u) -or ($u.Scheme -ne 'https' -and $u.Scheme -ne 'http')) {
        throw "'$ServerUrl' is not a server address. Use https://localhost:5443 on the server PC, or https://<server name>:5443."
    }
    if ($u.Scheme -eq 'http' -and -not $u.IsLoopback) { throw "Refusing plain http://$($u.Authority): the sign-in password is sent only over HTTPS. Use https://$($u.Host):5443." }
    return $u.GetLeftPart([UriPartial]::Authority) + '/api/v1'
}

# The import files found among $FileNames (paths), in import order; -Only limits the masters. Unknown = .csv files that are not
# named like a template (warned about and skipped). Returns { Files = @({Master, Path, FileName}); Unknown = @(names) }.
function Select-HccImportFiles {
    param([string[]]$FileNames = @(), [string[]]$Only = @())
    $only = @($Only | Where-Object { $_ } | ForEach-Object { $_.Trim().ToLowerInvariant() })
    foreach ($o in $only) { if ($script:HccImportOrder -notcontains $o) { throw "-Only '$o' is not an import file. Use: $($script:HccImportOrder -join ', ')." } }
    $byName = @{}
    foreach ($f in @($FileNames | Where-Object { $_ })) { $byName[[IO.Path]::GetFileName($f).ToLowerInvariant()] = $f }
    $files = @()
    foreach ($m in $script:HccImportOrder) {
        $key = "$m.csv"
        if ($byName.ContainsKey($key) -and ($only.Count -eq 0 -or $only -contains $m)) {
            $files += [pscustomobject]@{ Master = $m; Path = $byName[$key]; FileName = [IO.Path]::GetFileName($byName[$key]) }
        }
    }
    $known = @($script:HccImportOrder | ForEach-Object { "$_.csv" })
    $unknown = @($byName.Keys | Where-Object { $_.EndsWith('.csv') -and $known -notcontains $_ } | Sort-Object | ForEach-Object { [IO.Path]::GetFileName($byName[$_]) })
    return [pscustomobject]@{ Files = $files; Unknown = $unknown }
}

# Reads a CSV as Excel saves it: "CSV UTF-8" (with or without BOM) or plain "CSV (Comma delimited)" (Windows code page 1252).
function Read-HccImportCsv {
    param([Parameter(Mandatory)][string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    try { $text = (New-Object Text.UTF8Encoding($false, $true)).GetString($bytes) }
    catch {
        $ansi = try { [Text.Encoding]::GetEncoding(1252) } catch { [Text.Encoding]::GetEncoding(28591) }
        $text = $ansi.GetString($bytes)
    }
    if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }
    return $text
}

function New-HccIdempotencyKey { param([string]$Prefix = 'import') return $Prefix + '-' + [guid]::NewGuid().ToString('N') }

# Body of POST /commands/UploadImportBatch (contract UploadImportBatchRequest); asOfDate only for the opening balances.
function New-HccImportUploadBody {
    param([Parameter(Mandatory)][string]$Master, [Parameter(Mandatory)][string]$FileName, [Parameter(Mandatory)][AllowEmptyString()][string]$CsvText, [string]$AsOfDate)
    $b = [ordered]@{ master = $Master; fileName = $FileName; csvText = $CsvText }
    if ($Master -eq 'opening-balances' -and $AsOfDate) { $b.asOfDate = $AsOfDate }
    return $b
}

# Body of POST /commands/CommitImportBatch (contract CommitImportBatchRequest); dryRun = run everything and roll it back.
function New-HccImportCommitBody {
    param([Parameter(Mandatory)][long]$BatchId, [switch]$DryRun)
    return [ordered]@{ batchId = $BatchId; dryRun = [bool]$DryRun }
}

# Splits a report's rejected rows. In a dry run, a row that only says "... is not in HCC yet" / "Import the ... file first" for a
# master that an EARLIER file of the same run creates is expected (that file is not committed in a dry run), not a real rejection.
function Split-HccImportErrors {
    param([Parameter(Mandatory)][string]$Master, [object[]]$Errors = @(), [switch]$DryRun, [string[]]$EarlierInRun = @())
    $blocking = @(); $expected = @()
    $needs = $script:HccImportNeeds[$Master]
    foreach ($e in @($Errors | Where-Object { $null -ne $_ })) {
        if ($DryRun -and $needs -and $EarlierInRun -contains $needs -and [string]$e.message -match 'is not in HCC yet|Import the .+ file first') { $expected += $e }
        else { $blocking += $e }
    }
    return [pscustomobject]@{ Blocking = $blocking; Expected = $expected; Needs = $needs }
}

# Paise -> "Rs 1,25,000.50" (Indian grouping; whole rupees without decimals). ASCII only: the hotel PC console may lack the rupee sign.
function Format-HccRupees {
    param([long]$Paise)
    $neg = $Paise -lt 0
    $p = [Math]::Abs($Paise)
    $whole = [string][Math]::Floor($p / 100)
    $frac = $p % 100
    if ($whole.Length -gt 3) {
        $head = $whole.Substring(0, $whole.Length - 3); $tail = $whole.Substring($whole.Length - 3)
        $groups = @(); while ($head.Length -gt 2) { $groups = @($head.Substring($head.Length - 2)) + $groups; $head = $head.Substring(0, $head.Length - 2) }
        $whole = (@($head) + $groups + @($tail)) -join ','
    }
    $text = 'Rs ' + $whole + $(if ($frac -ne 0) { '.' + $frac.ToString('00', [Globalization.CultureInfo]::InvariantCulture) } else { '' })
    if ($neg) { return '-' + $text }
    return $text
}

# A property of a JSON reply, or $null when the reply has no such property (the scripts run with Set-StrictMode).
function Get-HccField {
    param($Object, [string]$Name)
    if ($null -eq $Object -or -not $Object.PSObject.Properties[$Name]) { return $null }
    return $Object.$Name
}

function Format-HccImportRow {
    param($Row)
    if ([int](Get-HccField $Row 'row') -eq 0) { $where = 'file' } else { $where = 'row ' + (Get-HccField $Row 'row') }
    if (Get-HccField $Row 'column') { $where += ' / ' + $Row.column }
    return $where + ': ' + (Get-HccField $Row 'message')
}

# The text printed for one uploaded file (contract ImportBatchReport): summary, rejected rows (with row, column and what to fix),
# rows expected in a dry run, warnings, then the planned changes (at most -MaxChanges lines; "unchanged" rows are only counted).
function Format-HccImportReport {
    param([Parameter(Mandatory)]$Report, [object[]]$Blocking, [object[]]$Expected = @(), [int]$MaxChanges = 50)
    if ($null -eq $Blocking) { $Blocking = @(Get-HccField $Report 'errors') }
    $c = $Report.counts
    $lines = @()
    $summary = '{0} ({1}): {2} row(s) - {3} new, {4} changed, {5} unchanged' -f $Report.fileName, $Report.master, $Report.rows, $c.create, $c.update, $c.unchanged
    if ([int]$c.remove -gt 0) { $summary += ", $($c.remove) removed" }
    $summary += ' - ' + @($Blocking).Count + ' rejected'
    $lines += $summary
    if ($null -ne (Get-HccField $Report 'debitPaise')) { $lines += '  Total debits {0} = total credits {1}' -f (Format-HccRupees $Report.debitPaise), (Format-HccRupees $Report.creditPaise) }
    foreach ($e in @($Blocking)) { $lines += '  REJECTED ' + (Format-HccImportRow $e) }
    foreach ($e in @($Expected)) { $lines += '  expected in a dry run (an earlier file is not committed yet): ' + (Format-HccImportRow $e) }
    foreach ($w in @(Get-HccField $Report 'warnings')) { if ($null -ne $w) { $lines += '  warning  ' + (Format-HccImportRow $w) } }
    $changes = @(@(Get-HccField $Report 'plan') | Where-Object { $null -ne $_ -and $_.action -ne 'Unchanged' })
    $label = @{ Create = 'new'; Update = 'change'; Remove = 'remove' }
    foreach ($p in ($changes | Select-Object -First $MaxChanges)) {
        $a = if ($label.ContainsKey([string]$p.action)) { $label[[string]$p.action] } else { [string]$p.action }
        $lines += '  {0,-7}{1}: {2}' -f $a, $p.key, $p.detail
    }
    if ($changes.Count -gt $MaxChanges) { $lines += '  ... and {0} more change(s)' -f ($changes.Count - $MaxChanges) }
    return , $lines
}

# One line after CommitImportBatch (contract ImportCommitResult), plus the users who still need a first password or PIN.
function Format-HccImportCommit {
    param([Parameter(Mandatory)]$Result)
    $c = $Result.counts
    if (Get-HccField $Result 'dryRun') { $lines = @('  -> dry run passed: {0} new, {1} changed, {2} removed would be saved; nothing was kept' -f $c.create, $c.update, $c.remove) }
    else { $lines = @('  -> committed: {0} new, {1} changed, {2} removed, {3} unchanged' -f $c.create, $c.update, $c.remove, $c.unchanged) }
    foreach ($u in @(@(Get-HccField $Result 'usersNeedingCredential') | Where-Object { $null -ne $_ })) {
        $kind = if ($u.credentialKind -eq 'PASSWORD') { 'password' } else { 'PIN' }
        $lines += "     $($u.loginName) ($($u.displayName)) cannot sign in yet: set the first $kind in Access control > Set credential."
    }
    return , $lines
}

# Text of a refused API call: the server's {code, message, details.errors} JSON when there is one, else the transport message with a
# hint for certificate problems (the hotel CA must be trusted on this PC, INSTALL.md section 8).
function ConvertFrom-HccApiError {
    param([string]$Body, [string]$Fallback)
    if ($Body) {
        try {
            $j = $Body | ConvertFrom-Json
            if ($j.PSObject.Properties['message']) {
                $text = "$($j.code): $($j.message)"
                if ($j.PSObject.Properties['details'] -and $j.details -and $j.details.PSObject.Properties['errors']) {
                    foreach ($e in @($j.details.errors)) { $text += [Environment]::NewLine + '  REJECTED ' + (Format-HccImportRow $e) }
                }
                return $text
            }
        }
        catch { }
    }
    if ($Fallback -match 'SSL|TLS|trust relationship|certificate') {
        return "$Fallback (HTTPS certificate problem: install HCC-Hotel-CA.cer into Local Machine > Trusted Root Certification Authorities on this PC, INSTALL.md section 8, and use a name that is in the certificate, for example https://localhost:5443.)"
    }
    return $Fallback
}
