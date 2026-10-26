-- A09-18 probe: Query Store top statements (by average logical reads) and row counts of the seeded HccTest_PERF_* database.
-- Same query as A09_perf_run.ps1. Run with: sqlcmd -S .\SQLEXPRESS -E -C -y 0 -v DB=<name> -i A09_roomboard_querystore.sql
SET NOCOUNT ON;
USE [$(DB)];
SELECT TOP 30 SUM(rs.count_executions) AS execs,
  CAST(SUM(rs.avg_duration*rs.count_executions)/NULLIF(SUM(rs.count_executions),0)/1000.0 AS decimal(10,2)) AS avg_ms,
  CAST(MAX(rs.max_duration)/1000.0 AS decimal(10,2)) AS max_ms,
  CAST(SUM(rs.avg_logical_io_reads*rs.count_executions)/NULLIF(SUM(rs.count_executions),0) AS bigint) AS avg_reads,
  CAST(MAX(rs.max_logical_io_reads) AS bigint) AS max_reads,
  REPLACE(REPLACE(LEFT(qt.query_sql_text,160),CHAR(10),' '),CHAR(13),' ') AS stmt
FROM sys.query_store_query q JOIN sys.query_store_query_text qt ON qt.query_text_id=q.query_text_id
JOIN sys.query_store_plan p ON p.query_id=q.query_id JOIN sys.query_store_runtime_stats rs ON rs.plan_id=p.plan_id
GROUP BY qt.query_sql_text ORDER BY avg_reads DESC;
SELECT s.name + '.' + t.name AS tbl, SUM(p.rows) AS row_count FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1) GROUP BY s.name,t.name HAVING SUM(p.rows) > 0 ORDER BY 2 DESC;
SELECT CAST(SUM(size)*8/1024.0 AS decimal(10,1)) AS size_mb FROM sys.database_files;
SELECT business_date FROM inv.property;
