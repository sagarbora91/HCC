-- A09-18 probe: the room board statement's plans in Query Store during the seed (one row per plan, then the last ten 1-minute
-- intervals), so the "before" cost is also shown as the API saw it while the seed ran, independent of later statistics updates.
-- Run with: sqlcmd -S .\SQLEXPRESS -E -C -I -W -s "|" -v DB=<name> -i A09_roomboard_qs_plans.sql
SET NOCOUNT ON;
USE [$(DB)];
SELECT p.plan_id, SUM(rs.count_executions) AS execs,
  CAST(SUM(rs.avg_logical_io_reads*rs.count_executions)/NULLIF(SUM(rs.count_executions),0) AS bigint) AS avg_reads,
  CAST(MAX(rs.max_logical_io_reads) AS bigint) AS max_reads,
  CAST(SUM(rs.avg_duration*rs.count_executions)/NULLIF(SUM(rs.count_executions),0)/1000.0 AS decimal(10,2)) AS avg_ms,
  CAST(MAX(rs.max_duration)/1000.0 AS decimal(10,2)) AS max_ms,
  CAST(SUM(rs.avg_cpu_time*rs.count_executions)/NULLIF(SUM(rs.count_executions),0)/1000.0 AS decimal(10,2)) AS avg_cpu_ms,
  MIN(rs.first_execution_time) AS first_exec, MAX(rs.last_execution_time) AS last_exec
FROM sys.query_store_query q JOIN sys.query_store_query_text qt ON qt.query_text_id = q.query_text_id
JOIN sys.query_store_plan p ON p.query_id = q.query_id JOIN sys.query_store_runtime_stats rs ON rs.plan_id = p.plan_id
WHERE qt.query_sql_text LIKE '%cur.guest_name, cur.arrival, cur.departure%' AND qt.query_sql_text LIKE '%OUTER APPLY (SELECT TOP (1) res.guest_name, res.arrival%' AND qt.query_sql_text NOT LIKE '%query_store%'
GROUP BY p.plan_id ORDER BY MIN(rs.first_execution_time);
SELECT TOP 10 i.start_time, SUM(rs.count_executions) AS execs,
  CAST(SUM(rs.avg_logical_io_reads*rs.count_executions)/NULLIF(SUM(rs.count_executions),0) AS bigint) AS avg_reads,
  CAST(MAX(rs.max_logical_io_reads) AS bigint) AS max_reads,
  CAST(SUM(rs.avg_duration*rs.count_executions)/NULLIF(SUM(rs.count_executions),0)/1000.0 AS decimal(10,2)) AS avg_ms
FROM sys.query_store_query q JOIN sys.query_store_query_text qt ON qt.query_text_id = q.query_text_id
JOIN sys.query_store_plan p ON p.query_id = q.query_id JOIN sys.query_store_runtime_stats rs ON rs.plan_id = p.plan_id
JOIN sys.query_store_runtime_stats_interval i ON i.runtime_stats_interval_id = rs.runtime_stats_interval_id
WHERE qt.query_sql_text LIKE '%cur.guest_name, cur.arrival, cur.departure%' AND qt.query_sql_text LIKE '%OUTER APPLY (SELECT TOP (1) res.guest_name, res.arrival%' AND qt.query_sql_text NOT LIKE '%query_store%'
GROUP BY i.start_time ORDER BY i.start_time DESC;
