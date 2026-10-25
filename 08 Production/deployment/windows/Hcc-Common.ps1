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
    $cfg = [ordered]@{ SqlInstance = '.\SQLEXPRESS'; Database = 'Hcc'; BackupDirectory = ''; LogDirectory = ''; RetentionDays = 30; KeepMinFulls = 3; PreMigrateRetentionDays = 90; ScratchDirectory = ''; TrustServerCertificate = $false }
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
# <Database>_FULL_<yyyyMMddTHHmmssZ>.bak, <Database>_DIFF_<...>.bak, <Database>_PRE_<...>_<tag>.bak (COPY_ONLY before a migration)
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
    $m = [regex]::Match($Name, '^(?<db>[A-Za-z0-9_]+?)_(?<type>FULL|DIFF|PRE)_(?<ts>\d{8}T\d{6}Z)(?:_(?<tag>[A-Za-z0-9_]+))?\.bak$')
    if (-not $m.Success) { return $null }
    $t = [datetime]::ParseExact($m.Groups['ts'].Value, "yyyyMMdd'T'HHmmss'Z'", [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal)
    return [pscustomobject]@{ Name = $Name; Database = $m.Groups['db'].Value; Type = $m.Groups['type'].Value; TimeUtc = $t; Tag = $m.Groups['tag'].Value }
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
