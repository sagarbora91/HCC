# A09-18 probe: saves every Query Store plan of the OLD room board statement (captured while A09_perf_seed.mjs ran) as .sqlplan and
# prints the operator tree of each (estimated rows; Query Store keeps no actual counters) with its measured averages, so the
# plan flips behind the audit's "spikes" can be read. Usage: pwsh A09_roomboard_qs_plan_shapes.ps1 -Db HccTest_PERF_<utc> -Out <file> -PlanDir <folder>
param([string]$Db, [string]$Out, [string]$PlanDir)
$ErrorActionPreference = 'Stop'
if ($Db -notmatch '^HccTest_PERF_\d{14}$') { throw "Refusing database name '$Db'." }
New-Item -ItemType Directory -Force $PlanDir | Out-Null
$conn = New-Object System.Data.SqlClient.SqlConnection("Data Source=.\SQLEXPRESS;Initial Catalog=$Db;Integrated Security=True;TrustServerCertificate=True;Application Name=HCC-A09-roomboard-qs")
$conn.Open()
$c = $conn.CreateCommand()
$c.CommandText = @'
SELECT p.plan_id, CAST(p.query_plan AS nvarchar(max)),
  SUM(rs.count_executions), CAST(SUM(rs.avg_logical_io_reads*rs.count_executions)/NULLIF(SUM(rs.count_executions),0) AS bigint),
  CAST(MAX(rs.max_logical_io_reads) AS bigint), CAST(SUM(rs.avg_duration*rs.count_executions)/NULLIF(SUM(rs.count_executions),0)/1000.0 AS decimal(10,2)),
  CAST(MAX(rs.max_duration)/1000.0 AS decimal(10,2)), CONVERT(varchar(19), MIN(rs.first_execution_time), 120), CONVERT(varchar(19), MAX(rs.last_execution_time), 120)
FROM sys.query_store_query q JOIN sys.query_store_query_text qt ON qt.query_text_id = q.query_text_id
JOIN sys.query_store_plan p ON p.query_id = q.query_id JOIN sys.query_store_runtime_stats rs ON rs.plan_id = p.plan_id
WHERE qt.query_sql_text LIKE '%cur.guest_name, cur.arrival, cur.departure%' AND qt.query_sql_text LIKE '%OUTER APPLY (SELECT TOP (1) res.guest_name, res.arrival%' AND qt.query_sql_text NOT LIKE '%query_store%'
GROUP BY p.plan_id, CAST(p.query_plan AS nvarchar(max)) ORDER BY p.plan_id;
'@
$rd = $c.ExecuteReader()
$rows = @(); while ($rd.Read()) { $rows += , @($rd.GetInt64(0), $rd.GetString(1), $rd.GetInt64(2), $rd.GetInt64(3), $rd.GetInt64(4), $rd.GetDecimal(5), $rd.GetDecimal(6), $rd.GetString(7), $rd.GetString(8)) }
$rd.Close(); $conn.Close()
$report = New-Object System.Collections.Generic.List[string]
$ns = 'http://schemas.microsoft.com/sqlserver/2004/07/showplan'
function Walk($op, [int]$depth, $mgr) {
    $obj = $op.SelectSingleNode('./*/sp:Object', $mgr)
    $o = if ($obj) { ' ' + (($obj.Schema, $obj.Table, $obj.Index) -join '.').Replace('[', '').Replace(']', '') } else { '' }
    $reb = [double]$op.EstimateRebinds + [double]$op.EstimateRewinds
    if ($op.PhysicalOp -ne 'Compute Scalar') { $report.Add((' ' * $depth) + "$($op.PhysicalOp) ($($op.LogicalOp))$o est.rows=$([Math]::Round([double]$op.EstimateRows, 1)) est.executions=$([Math]::Round($reb + 1, 0))") }
    foreach ($ch in $op.SelectNodes('./*/sp:RelOp', $mgr)) { Walk $ch ($depth + 2) $mgr }
}
foreach ($r in $rows) {
    [IO.File]::WriteAllText((Join-Path $PlanDir "roomboard-seed-querystore-plan-$($r[0]).sqlplan"), $r[1])
    $report.Add("=== plan $($r[0]): $($r[2]) executions, avg $($r[3]) reads (max $($r[4])), avg $($r[5]) ms (max $($r[6]) ms), first $($r[7]) UTC, last $($r[8]) UTC ===")
    $d = [xml]$r[1]; $mgr = New-Object System.Xml.XmlNamespaceManager($d.NameTable); $mgr.AddNamespace('sp', $ns)
    foreach ($st in $d.SelectNodes('//sp:StmtSimple', $mgr)) { $root = $st.SelectSingleNode('./sp:QueryPlan/sp:RelOp', $mgr); if ($root) { Walk $root 2 $mgr } }
    $report.Add('')
}
[IO.File]::WriteAllLines($Out, $report)
"$($rows.Count) plans written"
