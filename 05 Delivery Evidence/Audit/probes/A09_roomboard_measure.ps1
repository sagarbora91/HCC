# A09-18 probe: measures the room board statement (before = the statement at 70c2713, after = the set-based statement of Wave C4)
# and the other hot list reads on a seeded HccTest_PERF_* database, with SET STATISTICS IO/TIME (1 warm-up + 5 measured runs, rows
# discarded) and SET STATISTICS XML (actual plan, saved as .sqlplan plus an operator summary). It also checks that the old and the
# new board statements return the same rows, column for column, in the same order.
# The statements are copied from the C# code: keep them in step with SqlHousekeepingStore.cs (BoardSql, OpenTasksAsync),
# SqlReservationStore.ListAsync and SqlBillingStore.ListDeskFoliosAsync.
# Usage: pwsh A09_roomboard_measure.ps1 -Db HccTest_PERF_<utc> -Label before|after|stress -Out <file> -PlanDir <folder>
param([string]$Db, [string]$Label, [string]$Out, [string]$PlanDir, [int]$Runs = 5)
$ErrorActionPreference = 'Stop'
if ($Db -notmatch '^HccTest_PERF_\d{14}$') { throw "Refusing database name '$Db'." }
New-Item -ItemType Directory -Force $PlanDir | Out-Null

$boardOld = @'
SELECT r.room_id, r.room_no, t.code, r.hk_status, r.row_ver,
       b.block_id, b.room_id, b.from_date, b.to_date, b.reason, b.source, b.row_ver,
       cur.guest_name, cur.arrival, cur.departure,
       nxt.guest_name, nxt.arrival, nxt.departure
FROM inv.room r
JOIN inv.room_type t ON t.room_type_id = r.room_type_id
LEFT JOIN ops.room_block b ON b.room_id = r.room_id AND b.released_utc IS NULL
OUTER APPLY (SELECT TOP (1) res.guest_name, s.arrival, s.departure
             FROM gst.stay_segment seg JOIN gst.stay s ON s.stay_id = seg.stay_id JOIN inv.reservation res ON res.reservation_id = s.reservation_id
             WHERE seg.room_id = r.room_id AND seg.to_date IS NULL) cur
OUTER APPLY (SELECT TOP (1) res.guest_name, res.arrival, res.departure
             FROM inv.reservation res
             WHERE res.status = 'Confirmed' AND EXISTS (SELECT 1 FROM inv.room_night n WHERE n.reservation_id = res.reservation_id AND n.room_id = r.room_id)
             ORDER BY res.arrival, res.reservation_id) nxt
WHERE r.property_id = @p
ORDER BY r.room_no;
'@
# The new statement is read from the code itself (SqlHousekeepingReadStore.BoardSql), so the probe always measures what ships.
$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$store = Get-Content -Raw (Join-Path $repo '08 Production\src\Hcc.Infrastructure.SqlServer\Housekeeping\SqlHousekeepingStore.cs')
$mm = [regex]::Match($store, 'const string BoardSql = """\r?\n(.*?)\r?\n\s*""";', [System.Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $mm.Success) { throw 'BoardSql not found in SqlHousekeepingStore.cs' }
$boardNew = $mm.Groups[1].Value

$tasks = @'
SELECT t.task_id, t.room_id, r.room_no, t.kind, t.status, t.assignee_id, t.checklist_mask, t.note, t.reject_reason,
       t.business_date, t.started_utc, t.completed_utc, t.row_ver
FROM ops.hk_task t JOIN inv.room r ON r.room_id = t.room_id
WHERE t.property_id = @p AND t.status <> 'Done' AND (@a IS NULL OR t.assignee_id = @a)
ORDER BY CASE t.status WHEN 'InProgress' THEN 0 WHEN 'ToDo' THEN 1 ELSE 2 END, r.room_no, t.task_id;
'@

function ReservationList([string]$where) {
    $from = "FROM inv.reservation r JOIN inv.room rm ON rm.room_id = r.room_id JOIN inv.room_type t ON t.room_type_id = r.room_type_id WHERE $where"
    return @'
DECLARE @bd DATE = (SELECT business_date FROM inv.property WHERE property_id = @p);
SELECT @bd, COUNT(*) {FROM};
CREATE TABLE #pg(ord INT IDENTITY PRIMARY KEY, reservation_id BIGINT NOT NULL);
INSERT #pg(reservation_id) SELECT r.reservation_id {FROM} ORDER BY r.arrival DESC, r.reservation_id DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
SELECT r.reservation_id, r.public_no, r.row_ver, r.status, r.guest_name, r.phone, r.room_id, rm.room_no, r.room_type_id, t.code,
  r.arrival, r.departure, r.adults, r.children, r.source, r.nightly_rate_paise, r.hold_until_utc, r.group_block_id,
  r.restriction_override, r.cancel_reason, r.notes,
  COALESCE(s.guest_id, g.guest_id), CASE WHEN g.reservation_id IS NULL THEN 0 ELSE 1 END, s.stay_id, s.row_ver, s.status, s.checked_in_date, s.checked_out_date,
  r.company_name, r.company_gstin, r.company_id
FROM #pg pg JOIN inv.reservation r ON r.reservation_id = pg.reservation_id
  JOIN inv.room rm ON rm.room_id = r.room_id JOIN inv.room_type t ON t.room_type_id = r.room_type_id
  LEFT JOIN gst.registration g ON g.reservation_id = r.reservation_id
  LEFT JOIN gst.stay s ON s.reservation_id = r.reservation_id
ORDER BY pg.ord;
SELECT s.reservation_id, rm.room_no, sg.from_date, sg.to_date
FROM #pg pg JOIN gst.stay s ON s.reservation_id = pg.reservation_id
  JOIN gst.stay_segment sg ON sg.stay_id = s.stay_id JOIN inv.room rm ON rm.room_id = sg.room_id
ORDER BY s.reservation_id, sg.seq;
DROP TABLE #pg;
'@.Replace('{FROM}', $from)
}

$desk = @'
DECLARE @bd DATE = (SELECT business_date FROM inv.property WHERE property_id = @p);
SELECT @bd;
SELECT r.reservation_id, r.public_no, r.status, r.guest_name, COALESCE(cur.room_id, r.room_id), COALESCE(cur.room_no, rm.room_no), t.code,
       r.arrival, r.departure, r.adults, r.children, r.source, r.nightly_rate_paise, s.stay_id, f.folio_id, r.company_name, r.company_gstin
FROM inv.reservation r
  LEFT JOIN inv.room rm ON rm.room_id = r.room_id
  LEFT JOIN inv.room_type t ON t.room_type_id = r.room_type_id
  LEFT JOIN gst.stay s ON s.reservation_id = r.reservation_id
  LEFT JOIN bill.folio f ON f.reservation_id = r.reservation_id
  OUTER APPLY (SELECT TOP (1) sg.room_id, sr.room_no FROM gst.stay_segment sg JOIN inv.room sr ON sr.room_id = sg.room_id
               WHERE sg.stay_id = s.stay_id AND sg.to_date IS NULL) cur
WHERE r.property_id = @p
  AND (r.status = 'InHouse'
       OR (r.status = 'Confirmed' AND r.arrival <= @bd AND r.departure > @bd)
       OR (r.status = 'Departed' AND s.checked_out_date = @bd))
ORDER BY COALESCE(cur.room_no, rm.room_no), r.reservation_id;
'@

$cs = "Data Source=.\SQLEXPRESS;Initial Catalog=$Db;Integrated Security=True;TrustServerCertificate=True;Application Name=HCC-A09-roomboard-measure"
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$script:msgs = New-Object System.Collections.Generic.List[string]
$conn.add_InfoMessage({ param($s, $e) foreach ($er in $e.Errors) { $script:msgs.Add($er.Message) } })
$conn.Open()
function Scalar([string]$sql) { $c = $conn.CreateCommand(); $c.CommandText = $sql; return $c.ExecuteScalar() }
$p = [int](Scalar 'SELECT MIN(property_id) FROM inv.property;')
$bd = [datetime](Scalar "SELECT business_date FROM inv.property WHERE property_id = $p;")
$now = [datetime]::UtcNow

function New-Cmd([string]$sql, [hashtable]$prm) {
    $c = $conn.CreateCommand(); $c.CommandText = $sql; $c.CommandTimeout = 300
    foreach ($k in $prm.Keys) {
        $v = $prm[$k]
        $t = switch ($v.GetType().Name) { 'Int32' { [System.Data.SqlDbType]::Int } 'DateTime' { if ($k -eq '@now') { [System.Data.SqlDbType]::DateTime2 } else { [System.Data.SqlDbType]::Date } } 'DBNull' { [System.Data.SqlDbType]::Int } default { [System.Data.SqlDbType]::NVarChar } }
        $x = $c.Parameters.Add($k, $t); $x.Value = $v
    }
    return $c
}
function Drain($c) {
    $plans = @(); $rd = $c.ExecuteReader()
    try {
        do {
            $isPlan = $rd.FieldCount -eq 1 -and $rd.GetName(0) -like '*XML Showplan*'
            while ($rd.Read()) { if ($isPlan) { $plans += $rd.GetString(0) } }
        } while ($rd.NextResult())
    } finally { $rd.Close() }
    return , $plans
}
function Rows($c) {
    $rows = New-Object System.Collections.Generic.List[string]; $rd = $c.ExecuteReader()
    try {
        while ($rd.Read()) {
            $cells = for ($i = 0; $i -lt $rd.FieldCount; $i++) {
                if ($rd.IsDBNull($i)) { 'NULL' } else { $v = $rd.GetValue($i); if ($v -is [byte[]]) { [Convert]::ToBase64String($v) } elseif ($v -is [datetime]) { $v.ToString('yyyy-MM-dd') } else { [string]$v } }
            }
            $rows.Add(($cells -join '|'))
        }
    } finally { $rd.Close() }
    return , $rows
}
function Exec([string]$sql) { $c = $conn.CreateCommand(); $c.CommandText = $sql; [void]$c.ExecuteNonQuery() }

$ns = @{ sp = 'http://schemas.microsoft.com/sqlserver/2004/07/showplan' }
function PlanSummary([string]$xml) {
    $doc = [xml]$xml; $m = New-Object System.Xml.XmlNamespaceManager($doc.NameTable); $m.AddNamespace('sp', $ns.sp)
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($st in $doc.SelectNodes('//sp:StmtSimple', $m)) {
        $lines.Add('  statement: ' + ($st.StatementText -replace '\s+', ' ').Substring(0, [Math]::Min(110, ($st.StatementText -replace '\s+', ' ').Length)))
        $root = $st.SelectSingleNode('./sp:QueryPlan/sp:RelOp', $m)
        if ($root) { Walk $root 2 $m $lines }
    }
    return $lines
}
function Walk($op, [int]$depth, $m, $lines) {
    $obj = $op.SelectSingleNode('./*/sp:Object', $m)
    $o = if ($obj) { ' ' + (($obj.Schema, $obj.Table, $obj.Index) -join '.').Replace('[', '').Replace(']', '') } else { '' }
    $rows = 0; $execs = 0; $reads = 0
    foreach ($t in $op.SelectNodes('./sp:RunTimeInformation/sp:RunTimeCountersPerThread', $m)) { $rows += [long]$t.ActualRows; $execs += [long]$t.ActualExecutions; if ($t.ActualLogicalReads) { $reads += [long]$t.ActualLogicalReads } }
    $r = if ($reads) { " reads=$reads" } else { '' }
    $lines.Add((' ' * $depth) + "$($op.PhysicalOp) ($($op.LogicalOp))$o rows=$rows execs=$execs$r")
    foreach ($ch in $op.SelectNodes('./*/sp:RelOp', $m)) { Walk $ch ($depth + 2) $m $lines }
}

$statements = [ordered]@{
    'board-old'          = @{ sql = $boardOld; prm = @{ '@p' = $p } }
    'board-new'          = @{ sql = $boardNew; prm = @{ '@p' = $p } }
    'board-open-tasks'   = @{ sql = $tasks; prm = @{ '@p' = $p; '@a' = [DBNull]::Value } }
    'bookings-range-180' = @{ sql = (ReservationList 'r.property_id = @p AND r.departure > @from'); prm = @{ '@p' = $p; '@now' = $now; '@skip' = 0; '@take' = 500; '@from' = $bd.AddDays(-180) } }
    'arrivals'           = @{ sql = (ReservationList "r.property_id = @p AND r.status IN ('Held', 'Confirmed') AND r.arrival <= @bd AND r.departure > @bd"); prm = @{ '@p' = $p; '@now' = $now; '@skip' = 0; '@take' = 200 } }
    'in-house'           = @{ sql = (ReservationList "r.property_id = @p AND r.status = 'InHouse'"); prm = @{ '@p' = $p; '@now' = $now; '@skip' = 0; '@take' = 200 } }
    'cashier-folio-list' = @{ sql = $desk; prm = @{ '@p' = $p } }
}

$report = New-Object System.Collections.Generic.List[string]
$report.Add("A09-18 room board probe · $Label · database $Db · property $p · business date $($bd.ToString('yyyy-MM-dd')) · $((Get-Date).ToString('o'))")
$counts = Rows (New-Cmd "SELECT (SELECT COUNT(*) FROM inv.reservation), (SELECT COUNT(*) FROM inv.reservation WHERE status = 'Confirmed'), (SELECT COUNT(*) FROM inv.room_night), (SELECT COUNT(*) FROM gst.stay), (SELECT COUNT(*) FROM ops.hk_task), (SELECT COUNT(*) FROM ops.hk_task WHERE status <> 'Done'), (SELECT COUNT(*) FROM inv.room WHERE property_id = $p);" @{})
$report.Add('rows: reservations|confirmed|room_nights|stays|hk_tasks|open_hk_tasks|rooms = ' + $counts[0])
$idx = Rows (New-Cmd "SELECT i.name FROM sys.indexes i WHERE i.object_id IN (OBJECT_ID('inv.reservation'), OBJECT_ID('ops.hk_task')) AND i.name IS NOT NULL ORDER BY i.name;" @{})
$report.Add('indexes on inv.reservation + ops.hk_task: ' + ($idx -join ', '))
$report.Add('')
$summary = New-Object System.Collections.Generic.List[string]
$summary.Add('| statement | logical reads (median of ' + $Runs + ') | CPU ms (median) | elapsed ms (median) | elapsed ms (max) |')
$summary.Add('|---|---|---|---|---|')
foreach ($name in $statements.Keys) {
    $s = $statements[$name]
    [void](Drain (New-Cmd $s.sql $s.prm))   # warm-up (plan compiled and cached, pages in memory)
    Exec 'SET STATISTICS IO, TIME ON;'
    $reads = @(); $cpu = @(); $ela = @(); $first = $null
    for ($i = 0; $i -lt $Runs; $i++) {
        $script:msgs.Clear()
        [void](Drain (New-Cmd $s.sql $s.prm))
        $txt = $script:msgs -join "`n"
        if (-not $first) { $first = $txt }
        $reads += (([regex]::Matches($txt, 'logical reads (\d+)') | ForEach-Object { [long]$_.Groups[1].Value }) | Measure-Object -Sum).Sum
        $ex = [regex]::Matches($txt, 'SQL Server Execution Times:\s*CPU time = (\d+) ms,\s*elapsed time = (\d+) ms')
        $cpu += (($ex | ForEach-Object { [long]$_.Groups[1].Value }) | Measure-Object -Sum).Sum
        $ela += (($ex | ForEach-Object { [long]$_.Groups[2].Value }) | Measure-Object -Sum).Sum
    }
    Exec 'SET STATISTICS IO, TIME OFF;'
    Exec 'SET STATISTICS XML ON;'
    $plans = Drain (New-Cmd $s.sql $s.prm)
    Exec 'SET STATISTICS XML OFF;'
    if ($name -like 'board-*') { $k = 0; foreach ($pl in $plans) { $k++; [IO.File]::WriteAllText((Join-Path $PlanDir "roomboard-$Label-$name-$k.sqlplan"), $pl) } }   # list plans: summary in the text only
    $med = { param($a) ($a | Sort-Object)[[int][Math]::Floor($a.Count / 2)] }
    $summary.Add("| $name | $(& $med $reads) | $(& $med $cpu) | $(& $med $ela) | $(($ela | Measure-Object -Maximum).Maximum) |")
    $report.Add("=== $name ===")
    $report.Add("runs: reads=[$($reads -join ', ')] cpu_ms=[$($cpu -join ', ')] elapsed_ms=[$($ela -join ', ')]")
    $report.Add('STATISTICS IO/TIME of the first measured run:')
    foreach ($l in ($first -split "`n")) { if ($l -match 'logical reads|Execution Times|CPU time') { $report.Add('  ' + ($l -replace '\s+', ' ').Trim()) } }
    $report.Add('actual plan (operator, object, actual rows, executions, logical reads):')
    foreach ($pl in $plans) { foreach ($l in (PlanSummary $pl)) { $report.Add($l) } }
    $report.Add('')
}

# Equivalence: the old and the new board statements, every column, in order.
$a = Rows (New-Cmd $boardOld @{ '@p' = $p }); $b = Rows (New-Cmd $boardNew @{ '@p' = $p })
$diff = 0; for ($i = 0; $i -lt [Math]::Max($a.Count, $b.Count); $i++) { if ($i -ge $a.Count -or $i -ge $b.Count -or $a[$i] -ne $b[$i]) { $diff++ } }
$withNext = ($a | Where-Object { ($_ -split '\|')[15] -ne 'NULL' }).Count
$withCur = ($a | Where-Object { ($_ -split '\|')[12] -ne 'NULL' }).Count
$withBlock = ($a | Where-Object { ($_ -split '\|')[5] -ne 'NULL' }).Count
$report.Add("=== equivalence (board-old vs board-new) ===")
$report.Add("rows old=$($a.Count) new=$($b.Count) differing rows=$diff · rooms with a current guest=$withCur, with a next arrival=$withNext, with an active block=$withBlock")
$report.Add('old rows:')
foreach ($r in $a) { $report.Add('  ' + $r) }
$report.Add('')
$report.Insert(4, ($summary -join "`n") + "`n")
$conn.Close()
[IO.File]::WriteAllLines($Out, $report)
$summary
"equivalence: rows old=$($a.Count) new=$($b.Count) differing=$diff"
if ($diff -ne 0) { exit 3 }
