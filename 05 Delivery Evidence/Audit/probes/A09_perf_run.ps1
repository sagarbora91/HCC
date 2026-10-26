# A09 performance probe wrapper (audit only). Needs the dotnet-test lock. Creates HccTest_perf_<time>, migrates it with the built
# Hcc.Api (copied to a scratch folder so no build output is touched), starts the API in Development on 127.0.0.1:5291, runs
# A09_perf_seed.mjs, captures the top statements from sys.dm_exec_query_stats for that database, stops the API.
# The database is NOT dropped here (the restore drill uses it); A09_cleanup.ps1 drops every HccTest_perf_*/HccTest_drill_* database.
param([string]$Start = '2025-10-01', [int]$Days = 365, [int]$SeedMinutes = 27, [string]$Scratch)
$ErrorActionPreference = 'Stop'
$repo = 'E:\Projects\HCC\Hotel Control Centre'
$ev = Join-Path $repo '05 Delivery Evidence\Audit\evidence\A09'
$db = 'HccTest_perf_' + (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
Set-Content -Path (Join-Path $ev 'perf-db-name.txt') -Value $db
$bin = Join-Path $repo '08 Production\src\Hcc.Api\bin\Debug\net10.0'
if (-not $Scratch) { $Scratch = Join-Path $env:TEMP 'a09-perf-api' }
if (Test-Path $Scratch) { Remove-Item -Recurse -Force $Scratch }
Copy-Item -Recurse $bin $Scratch
$cs = "Data Source=.\SQLEXPRESS;Initial Catalog=$db;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Application Name=HCC-A09-perf"
sqlcmd -S .\SQLEXPRESS -E -C -b -Q "CREATE DATABASE [$db];" | Out-Null
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Hcc__Sql__ConnectionString = $cs
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5291'
Push-Location $Scratch
try {
    $m = Measure-Command { & dotnet .\Hcc.Api.dll migrate *>&1 | Tee-Object -FilePath (Join-Path $ev 'perf-migrate.txt') }
    "migrate wall: $($m.TotalSeconds) s" | Add-Content (Join-Path $ev 'perf-migrate.txt')
    $api = Start-Process dotnet -ArgumentList '.\Hcc.Api.dll' -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $env:TEMP 'a09-api-out.txt') -RedirectStandardError (Join-Path $env:TEMP 'a09-api-err.txt')
    for ($i = 0; $i -lt 60; $i++) { try { Invoke-RestMethod http://127.0.0.1:5291/api/v1/system/health | Out-Null; break } catch { Start-Sleep -Milliseconds 500 } }
    $env:PATH = "E:\Tools\nodejs;$env:PATH"
    & node -v | Set-Content (Join-Path $ev 'node-version.txt')
    sqlcmd -S .\SQLEXPRESS -E -C -Q "ALTER DATABASE [$db] SET QUERY_STORE = ON (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = ALL, INTERVAL_LENGTH_MINUTES = 1);" 2>&1 | Out-File (Join-Path $ev 'perf-querystore.txt')
    & node (Join-Path $repo '05 Delivery Evidence\Audit\probes\A09_perf_seed.mjs') 'http://127.0.0.1:5291/api/v1' $ev $Start $Days $SeedMinutes
    $q = @"
SET NOCOUNT ON; USE [$db];
SELECT TOP 30 SUM(rs.count_executions) AS execs,
  CAST(SUM(rs.avg_duration*rs.count_executions)/NULLIF(SUM(rs.count_executions),0)/1000.0 AS decimal(10,2)) AS avg_ms,
  CAST(MAX(rs.max_duration)/1000.0 AS decimal(10,2)) AS max_ms,
  CAST(SUM(rs.avg_logical_io_reads*rs.count_executions)/NULLIF(SUM(rs.count_executions),0) AS bigint) AS avg_reads,
  REPLACE(REPLACE(LEFT(qt.query_sql_text,220),CHAR(10),' '),CHAR(13),' ') AS stmt
FROM sys.query_store_query q JOIN sys.query_store_query_text qt ON qt.query_text_id=q.query_text_id
JOIN sys.query_store_plan p ON p.query_id=q.query_id JOIN sys.query_store_runtime_stats rs ON rs.plan_id=p.plan_id
GROUP BY qt.query_sql_text ORDER BY avg_reads DESC;
"@
    sqlcmd -S .\SQLEXPRESS -E -C -W -s "|" -Q $q | Set-Content (Join-Path $ev 'perf-top-statements.txt')
    $c = @"
SET NOCOUNT ON; USE [$db];
SELECT s.name + '.' + t.name AS tbl, SUM(p.rows) AS rows FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1) GROUP BY s.name,t.name HAVING SUM(p.rows) > 0 ORDER BY 2 DESC;
SELECT CAST(SUM(size)*8/1024.0 AS decimal(10,1)) AS size_mb FROM sys.database_files;
"@
    sqlcmd -S .\SQLEXPRESS -E -C -W -s "|" -Q $c | Set-Content (Join-Path $ev 'perf-row-counts.txt')
}
finally {
    if ($api -and -not $api.HasExited) { Stop-Process -Id $api.Id -Force }
    Pop-Location
    Copy-Item (Join-Path $env:TEMP 'a09-api-err.txt') (Join-Path $ev 'perf-api-stderr.txt') -ErrorAction SilentlyContinue
}
