# A09 follow-up (Wave C4): measures SqlGstMasterStore.MaxUnitTariffAsync (the previous financial year's highest room tariff, used for
# the restaurant "specified premises" GST rule) on a seeded HccTest_PERF_* database: SET STATISTICS IO/TIME, 1 warm-up + 5 runs,
# and the actual plan's operators. The statement is read from the code, so the probe measures what ships.
# Usage: pwsh A09_tariff_measure.ps1 -Db HccTest_PERF_<utc> -From 2025-04-01 -To 2026-03-31 -Out <file>
param([string]$Db, [string]$From, [string]$To, [string]$Out, [int]$Runs = 5)
$ErrorActionPreference = 'Stop'
if ($Db -notmatch '^HccTest_PERF_\d{14}$') { throw "Refusing database name '$Db'." }
$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$src = Get-Content -Raw (Join-Path $repo '08 Production\src\Hcc.Infrastructure.SqlServer\Tax\SqlGstMasterStore.cs')
$m = [regex]::Match($src, 'MaxUnitTariffAsync\(.*?WithCommandAsync\(tx, """\r?\n(.*?)\r?\n\s*""", ', [System.Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $m.Success) { throw 'MaxUnitTariffAsync statement not found' }
$sql = $m.Groups[1].Value
$conn = New-Object System.Data.SqlClient.SqlConnection("Data Source=.\SQLEXPRESS;Initial Catalog=$Db;Integrated Security=True;TrustServerCertificate=True;Application Name=HCC-A09-tariff-measure")
$script:msgs = New-Object System.Collections.Generic.List[string]
$conn.add_InfoMessage({ param($s, $e) foreach ($er in $e.Errors) { $script:msgs.Add($er.Message) } })
$conn.Open()
function Run([switch]$Plan) {
    $c = $conn.CreateCommand(); $c.CommandText = $sql; $c.CommandTimeout = 300
    ($c.Parameters.Add('@p', [System.Data.SqlDbType]::Int)).Value = 1
    ($c.Parameters.Add('@from', [System.Data.SqlDbType]::Date)).Value = [datetime]$From
    ($c.Parameters.Add('@to', [System.Data.SqlDbType]::Date)).Value = [datetime]$To
    $rd = $c.ExecuteReader(); $val = $null; $xml = @()
    try { do { $isPlan = $rd.FieldCount -eq 1 -and $rd.GetName(0) -like '*Showplan*'; while ($rd.Read()) { if ($isPlan) { $xml += $rd.GetString(0) } else { $val = $rd.GetValue(0) } } } while ($rd.NextResult()) } finally { $rd.Close() }
    return @{ Value = $val; Plans = $xml }
}
function Exec([string]$t) { $c = $conn.CreateCommand(); $c.CommandText = $t; [void]$c.ExecuteNonQuery() }
$report = New-Object System.Collections.Generic.List[string]
$report.Add("MaxUnitTariffAsync on $Db · @from $From · @to $To · $((Get-Date).ToString('o'))")
$report.Add('bill.charge rows: ' + (& { $c = $conn.CreateCommand(); $c.CommandText = 'SELECT COUNT(*) FROM bill.charge;'; $c.ExecuteScalar() }))
$v = (Run).Value
$report.Add("result (paise): $v")
Exec 'SET STATISTICS IO, TIME ON;'
$reads = @(); $cpu = @(); $ela = @(); $first = $null
for ($i = 0; $i -lt $Runs; $i++) {
    $script:msgs.Clear(); [void](Run); $txt = $script:msgs -join "`n"; if (-not $first) { $first = $txt }
    $reads += (([regex]::Matches($txt, 'logical reads (\d+)') | ForEach-Object { [long]$_.Groups[1].Value }) | Measure-Object -Sum).Sum
    $ex = [regex]::Matches($txt, 'SQL Server Execution Times:\s*CPU time = (\d+) ms,\s*elapsed time = (\d+) ms')
    $cpu += (($ex | ForEach-Object { [long]$_.Groups[1].Value }) | Measure-Object -Sum).Sum
    $ela += (($ex | ForEach-Object { [long]$_.Groups[2].Value }) | Measure-Object -Sum).Sum
}
Exec 'SET STATISTICS IO, TIME OFF;'
$report.Add("runs: reads=[$($reads -join ', ')] cpu_ms=[$($cpu -join ', ')] elapsed_ms=[$($ela -join ', ')]")
foreach ($l in ($first -split "`n")) { if ($l -match 'logical reads|Execution Times|CPU time') { $report.Add('  ' + (($l -replace ', physical reads.*$', '') -replace '\s+', ' ').Trim()) } }
Exec 'SET STATISTICS XML ON;'; $plans = (Run -Plan).Plans; Exec 'SET STATISTICS XML OFF;'
$ns = 'http://schemas.microsoft.com/sqlserver/2004/07/showplan'
function Walk($op, [int]$depth, $mgr) {
    $obj = $op.SelectSingleNode('./*/sp:Object', $mgr)
    $o = if ($obj) { ' ' + (($obj.Schema, $obj.Table, $obj.Index) -join '.').Replace('[', '').Replace(']', '') } else { '' }
    $rows = 0; $execs = 0; $rr = 0
    foreach ($t in $op.SelectNodes('./sp:RunTimeInformation/sp:RunTimeCountersPerThread', $mgr)) { $rows += [long]$t.ActualRows; $execs += [long]$t.ActualExecutions; if ($t.ActualLogicalReads) { $rr += [long]$t.ActualLogicalReads } }
    if ($op.PhysicalOp -ne 'Compute Scalar') { $report.Add((' ' * $depth) + "$($op.PhysicalOp) ($($op.LogicalOp))$o rows=$rows execs=$execs" + $(if ($rr) { " reads=$rr" } else { '' })) }
    foreach ($ch in $op.SelectNodes('./*/sp:RelOp', $mgr)) { Walk $ch ($depth + 2) $mgr }
}
foreach ($pl in $plans) { $d = [xml]$pl; $mgr = New-Object System.Xml.XmlNamespaceManager($d.NameTable); $mgr.AddNamespace('sp', $ns); foreach ($st in $d.SelectNodes('//sp:StmtSimple', $mgr)) { $r = $st.SelectSingleNode('./sp:QueryPlan/sp:RelOp', $mgr); if ($r) { Walk $r 2 $mgr } } }
$conn.Close()
[IO.File]::WriteAllLines($Out, $report)
$report | Select-Object -First 4
"median reads $(($reads | Sort-Object)[[int][Math]::Floor($reads.Count / 2)]) · cpu $(($cpu | Sort-Object)[[int][Math]::Floor($cpu.Count / 2)]) ms · elapsed $(($ela | Sort-Object)[[int][Math]::Floor($ela.Count / 2)]) ms"
