<#
.SYNOPSIS
  Restore drill: proves the newest backups can really be restored. Restores into a SCRATCH database, runs DBCC CHECKDB and a
  row-count check, logs the result, and drops the scratch database. The live database is never written to.
.DESCRIPTION
  1. Picks the newest FULL backup (+ the newest DIFFERENTIAL after it) from the backup folder.
  2. RESTORE FILELISTONLY, then RESTORE ... WITH MOVE into the scratch folder as database HccDrill_<UTC time>
     (names must start with HccDrill_ or HccTest_; anything else is refused; EtpReporting / EtpScratch_* can never match).
  3. DBCC CHECKDB on the scratch copy must be clean.
  4. Row counts per table (scratch vs live): a table missing from the restore, or an empty restore of a non-empty live
     database, fails the drill. Small differences are normal (the live database moved on) and are only listed.
  5. Drops the scratch database (unless -KeepScratch) and appends a JSON line to drill-history.jsonl next to the log.
  Exit code 0 = passed, 1 = failed. -WhatIf prints the plan only (no SQL connection, no files).
# ETP-REUSE: replace/align with ETP's restore-drill script (report format, e-mail of the result, scheduling) when V:\ETP is available.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SqlInstance, [string]$Database, [string]$BackupDirectory, [string]$LogDirectory, [string]$ScratchDirectory,
    [string]$ScratchName, [switch]$KeepScratch, [switch]$TrustServerCertificate, [string]$ConfigPath
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Hcc-Common.ps1"
$cfg = Get-HccOpsConfig -Path $ConfigPath
if (-not $SqlInstance) { $SqlInstance = $cfg.SqlInstance }
if (-not $Database) { $Database = $cfg.Database }
if (-not $BackupDirectory) { $BackupDirectory = $cfg.BackupDirectory }
if (-not $LogDirectory) { $LogDirectory = if ($cfg.LogDirectory) { $cfg.LogDirectory } else { Join-Path $BackupDirectory 'Logs' } }
if (-not $ScratchDirectory) { $ScratchDirectory = if ($cfg.ScratchDirectory) { $cfg.ScratchDirectory } else { Join-Path $BackupDirectory 'DrillScratch' } }
$trust = $TrustServerCertificate -or [bool]$cfg.TrustServerCertificate
Assert-HccDatabaseName $Database | Out-Null
if (-not $BackupDirectory) { throw 'No backup folder: set BackupDirectory in hcc-ops.json or pass -BackupDirectory.' }
$started = (Get-Date).ToUniversalTime()
if (-not $ScratchName) { $ScratchName = 'HccDrill_' + $started.ToString('yyyyMMddHHmmss') }
Assert-HccScratchName $ScratchName | Out-Null
if ($ScratchName -eq $Database) { throw 'The scratch database must differ from the live database.' }
$log = Join-Path $LogDirectory ('restore-drill_{0:yyyyMMdd_HHmmss}.log' -f $started)

if (-not $PSCmdlet.ShouldProcess("$SqlInstance / $ScratchName", "Restore newest backup of [$Database] from $BackupDirectory, DBCC CHECKDB, row-count check, drop scratch")) {
    Write-HccLog "DRY RUN: would pick the newest FULL (+ later DIFF) backup of [$Database] in $BackupDirectory" -LogFile $null
    Write-HccLog "DRY RUN: would RESTORE as [$ScratchName] with MOVE into $ScratchDirectory on $SqlInstance (live database untouched)" -LogFile $null
    Write-HccLog ('DRY RUN: would run DBCC CHECKDB, compare per-table row counts with the live database, drop the scratch database and log to ' + $log) -LogFile $null
    return
}

$result = [ordered]@{ startedUtc = $started.ToString('o'); database = $Database; scratch = $ScratchName; full = $null; differential = $null; checkdb = 'not run'; rowCounts = 'not run'; passed = $false; error = $null }
$created = $false
try {
    $names = @(Get-ChildItem -LiteralPath $BackupDirectory -Filter '*.bak' -File | ForEach-Object Name)
    $chain = Select-HccRestoreChain -Names $names -Database $Database
    if (-not $chain) { throw "No FULL backup of [$Database] found in $BackupDirectory." }
    $result.full = $chain.Full; $result.differential = $chain.Differential
    $fullPath = Join-Path $BackupDirectory $chain.Full
    Write-HccLog "Drill start: restoring $($chain.Full) $(if ($chain.Differential) { '+ ' + $chain.Differential })as [$ScratchName]" -LogFile $log
    if (-not (Test-Path -LiteralPath $ScratchDirectory)) { New-Item -ItemType Directory -Path $ScratchDirectory -Force | Out-Null }

    # logical file names inside the backup
    $fl = Invoke-HccSql -Instance $SqlInstance -TrustServerCertificate:$trust -Table -Query ('RESTORE FILELISTONLY FROM DISK = {0};' -f (ConvertTo-HccSqlLiteral $fullPath))
    $moves = @()
    foreach ($f in ($fl -split "`r?`n" | Where-Object { $_ -match '\|' })) {
        $col = $f -split '\|'
        $parts = @($col[0].Trim(), $col[2].Trim())   # LogicalName | PhysicalName | Type (D data, L log)
        $ext = if ($parts[1] -eq 'L') { '.ldf' } else { '.mdf' }
        $moves += ('MOVE {0} TO {1}' -f (ConvertTo-HccSqlLiteral $parts[0]), (ConvertTo-HccSqlLiteral (Join-Path $ScratchDirectory ($ScratchName + '_' + $parts[0] + $ext))))
    }
    if ($moves.Count -eq 0) { throw 'RESTORE FILELISTONLY returned no files.' }
    $sq = ConvertTo-HccSqlName $ScratchName
    $recovery = if ($chain.Differential) { 'NORECOVERY' } else { 'RECOVERY' }
    $created = $true
    Invoke-HccSql -Instance $SqlInstance -TrustServerCertificate:$trust -Query ('RESTORE DATABASE {0} FROM DISK = {1} WITH {2}, {3}, CHECKSUM, STATS = 25;' -f $sq, (ConvertTo-HccSqlLiteral $fullPath), ($moves -join ', '), $recovery) | Out-Null
    if ($chain.Differential) {
        Invoke-HccSql -Instance $SqlInstance -TrustServerCertificate:$trust -Query ('RESTORE DATABASE {0} FROM DISK = {1} WITH RECOVERY, CHECKSUM;' -f $sq, (ConvertTo-HccSqlLiteral (Join-Path $BackupDirectory $chain.Differential))) | Out-Null
    }
    Write-HccLog 'Restore finished.' -LogFile $log

    Invoke-HccSql -Instance $SqlInstance -TrustServerCertificate:$trust -Query ("DBCC CHECKDB ({0}) WITH NO_INFOMSGS, ALL_ERRORMSGS;" -f $sq) | Out-Null
    $result.checkdb = 'clean'
    Write-HccLog 'DBCC CHECKDB: clean.' -LogFile $log

    $countSql = "SET NOCOUNT ON; SELECT s.name + '.' + t.name + '|' + CAST(SUM(p.rows) AS varchar(20)) FROM {0}.sys.tables t JOIN {0}.sys.schemas s ON s.schema_id = t.schema_id JOIN {0}.sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1) GROUP BY s.name, t.name;"
    function Get-Counts([string]$db) {
        $h = @{}
        $txt = Invoke-HccSql -Instance $SqlInstance -TrustServerCertificate:$trust -Query ($countSql -f (ConvertTo-HccSqlName $db))
        foreach ($line in ($txt -split "`r?`n")) { if ($line -match '^(?<t>[^|\s]+)\|(?<n>\d+)\s*$') { $h[$Matches['t']] = [long]$Matches['n'] } }
        return $h
    }
    $restored = Get-Counts $ScratchName
    $live = Get-Counts $Database
    $cmp = Compare-HccRowCounts -Restored $restored -Live $live
    $result.rowCounts = if ($cmp.Ok) { 'ok' } else { 'FAILED' }
    $result.tables = $cmp.Tables; $result.restoredRows = $cmp.RestoredRows; $result.liveRows = $cmp.LiveRows
    Write-HccLog ("Row counts: {0} tables, {1} rows restored vs {2} rows live now; {3} table(s) differ (normal if the hotel has worked since the backup); missing from restore: {4}." -f $cmp.Tables, $cmp.RestoredRows, $cmp.LiveRows, $cmp.DifferingTables.Count, $cmp.MissingTables.Count) -LogFile $log
    if (-not $cmp.Ok) { throw ('Row-count check failed. Missing tables: ' + ($cmp.MissingTables -join ', ')) }
    $result.passed = $true
    Write-HccLog 'DRILL PASSED.' -LogFile $log
}
catch {
    $result.error = $_.Exception.Message
    Write-HccLog ('DRILL FAILED: ' + $_.Exception.Message) -LogFile $log -Level ERROR
}
finally {
    if ($created -and -not $KeepScratch) {
        try {
            Assert-HccScratchName $ScratchName | Out-Null
            $sq = ConvertTo-HccSqlName $ScratchName
            Invoke-HccSql -Instance $SqlInstance -TrustServerCertificate:$trust -Query ("IF DB_ID({0}) IS NOT NULL BEGIN ALTER DATABASE {1} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {1}; END" -f (ConvertTo-HccSqlLiteral $ScratchName), $sq) | Out-Null
            Write-HccLog "Scratch database $ScratchName dropped." -LogFile $log
        }
        catch { Write-HccLog ("Could not drop scratch database ${ScratchName}: " + $_.Exception.Message) -LogFile $log -Level WARN }
    }
    $result.finishedUtc = (Get-Date).ToUniversalTime().ToString('o')
    try { Add-Content -LiteralPath (Join-Path $LogDirectory 'drill-history.jsonl') -Value ($result | ConvertTo-Json -Compress) -Encoding UTF8 } catch { Write-Host "Could not write drill history: $($_.Exception.Message)" }
}
if (-not $result.passed) { exit 1 }
