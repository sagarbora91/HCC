-- A08-01 after-fix probe: the same data as A08_outbox_poll.sql ($(Rows) events, 30 types, 5 consumers, last 20 per consumer pending),
-- plus migration 2900 (IX_outbox_type, plat.outbox_consumer / outbox_subscription / consumer_failure), polled with the statement
-- OutboxConsumer.ConsumeOnceAsync now sends (sp_executesql, types as @t0..@tN). Events are backdated one day so the settle window
-- lets the watermark advance. Runs in a throwaway HccTest_OUTBOX_* database (created and dropped by the caller).
SET NOCOUNT ON; SET XACT_ABORT ON;
GO
CREATE SCHEMA plat;
GO
CREATE TABLE plat.outbox(
  outbox_id BIGINT IDENTITY CONSTRAINT PK_outbox PRIMARY KEY,
  event_type VARCHAR(60) NOT NULL,
  aggregate_id BIGINT NOT NULL,
  payload NVARCHAR(MAX) NOT NULL CONSTRAINT CK_outbox_payload CHECK (ISJSON(payload) = 1),
  property_id INT NULL,
  principal_id INT NULL,
  created_utc DATETIME2(3) NOT NULL CONSTRAINT DF_outbox_created DEFAULT SYSUTCDATETIME(),
  published_utc DATETIME2(3) NULL,
  attempts INT NOT NULL CONSTRAINT DF_outbox_attempts DEFAULT 0,
  last_error NVARCHAR(1000) NULL);
CREATE INDEX IX_outbox_unpublished ON plat.outbox(outbox_id) WHERE published_utc IS NULL;
CREATE TABLE plat.consumer_dedup(
  consumer VARCHAR(40) NOT NULL,
  outbox_id BIGINT NOT NULL CONSTRAINT FK_consumer_dedup_outbox REFERENCES plat.outbox,
  processed_utc DATETIME2(3) NOT NULL CONSTRAINT DF_consumer_dedup_processed DEFAULT SYSUTCDATETIME(),
  CONSTRAINT PK_consumer_dedup PRIMARY KEY (consumer, outbox_id));
GO
-- migration 2900 (without the acc.source index: no accounting schema here)
CREATE INDEX IX_outbox_type ON plat.outbox(event_type, outbox_id) INCLUDE (property_id);
CREATE TABLE plat.outbox_consumer(
  consumer VARCHAR(40) NOT NULL CONSTRAINT PK_outbox_consumer PRIMARY KEY,
  low_outbox_id BIGINT NOT NULL CONSTRAINT DF_outbox_consumer_low DEFAULT 0,
  updated_utc DATETIME2(3) NOT NULL CONSTRAINT DF_outbox_consumer_updated DEFAULT SYSUTCDATETIME());
CREATE TABLE plat.consumer_failure(
  consumer VARCHAR(40) NOT NULL,
  outbox_id BIGINT NOT NULL CONSTRAINT FK_consumer_failure_outbox REFERENCES plat.outbox,
  attempts INT NOT NULL,
  last_error NVARCHAR(1000) NOT NULL,
  first_failed_utc DATETIME2(3) NOT NULL,
  last_failed_utc DATETIME2(3) NOT NULL,
  parked_utc DATETIME2(3) NULL,
  CONSTRAINT PK_consumer_failure PRIMARY KEY (consumer, outbox_id));
GO
DECLARE @n INT = $(Rows);
;WITH a AS (SELECT 1 x FROM (VALUES(1),(1),(1),(1),(1),(1),(1),(1),(1),(1)) v(x)),
      n AS (SELECT TOP (@n) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) i FROM a a1, a a2, a a3, a a4, a a5, a a6, a a7)
INSERT plat.outbox(event_type, aggregate_id, payload, property_id, principal_id, created_utc, published_utc, attempts)
SELECT CONCAT('Event', i % 30), i, CONCAT(N'{"folioId":', i, N',"chargeId":', i, N',"kind":"room","serviceDate":"2026-10-24","amountPaise":200000,"taxPaise":10000,"note":"', REPLICATE(N'x', 180), N'"}'),
       1, 1, DATEADD(DAY, -1, SYSUTCDATETIME()), SYSUTCDATETIME(), 1
FROM n ORDER BY i;
INSERT plat.consumer_dedup(consumer, outbox_id)
SELECT c.consumer, o.outbox_id FROM plat.outbox o
JOIN (VALUES ('accounting', 0, 11), ('housekeeping', 12, 12), ('stayover', 13, 13), ('messaging', 14, 19), ('ota', 20, 25)) c(consumer, lo, hi)
  ON CAST(SUBSTRING(o.event_type, 6, 2) AS INT) BETWEEN c.lo AND c.hi
WHERE o.outbox_id <= @n - 20;
INSERT plat.outbox_consumer(consumer) VALUES ('accounting'), ('housekeeping');
GO
SELECT 'rows' AS what, (SELECT COUNT(*) FROM plat.outbox) AS outbox_rows, (SELECT COUNT(*) FROM plat.consumer_dedup) AS dedup_rows,
       (SELECT SUM(used_page_count) * 8 / 1024 FROM sys.dm_db_partition_stats WHERE object_id = OBJECT_ID('plat.outbox')) AS outbox_mb;
GO
-- The poll exactly as OutboxConsumer.ConsumeOnceAsync sends it.
CREATE TABLE #poll(stmt NVARCHAR(MAX));
INSERT #poll VALUES (N'
DECLARE @low BIGINT = ISNULL((SELECT low_outbox_id FROM plat.outbox_consumer WHERE consumer = @c), 0);
DECLARE @p TABLE(outbox_id BIGINT PRIMARY KEY);
INSERT @p(outbox_id)
SELECT TOP (@n) x.outbox_id
FROM (VALUES (@t0), (@t1), (@t2), (@t3), (@t4), (@t5), (@t6), (@t7), (@t8), (@t9), (@t10), (@t11)) t(event_type)
CROSS APPLY (SELECT TOP (@n) o.outbox_id
             FROM plat.outbox o WITH (READCOMMITTEDLOCK, INDEX(IX_outbox_type))
             WHERE o.event_type = t.event_type AND o.outbox_id > @low
               AND NOT EXISTS (SELECT 1 FROM plat.consumer_dedup d WHERE d.consumer = @c AND d.outbox_id = o.outbox_id)
               AND NOT EXISTS (SELECT 1 FROM plat.consumer_failure f WHERE f.consumer = @c AND f.outbox_id = o.outbox_id AND f.parked_utc IS NOT NULL)
             ORDER BY o.outbox_id) x
ORDER BY x.outbox_id;
DECLARE @settled BIGINT = ISNULL((SELECT TOP (1) outbox_id FROM plat.outbox WITH (READPAST)
                                  WHERE created_utc < DATEADD(SECOND, -@settle, SYSUTCDATETIME()) ORDER BY outbox_id DESC), 0);
DECLARE @first BIGINT = (SELECT MIN(outbox_id) FROM @p);
DECLARE @new BIGINT = CASE WHEN @first IS NOT NULL AND @first - 1 < @settled THEN @first - 1 ELSE @settled END;
IF @new > @low
  UPDATE plat.outbox_consumer SET low_outbox_id = @new, updated_utc = SYSUTCDATETIME() WHERE consumer = @c AND low_outbox_id < @new;
SELECT o.outbox_id, o.event_type, o.aggregate_id, o.payload, o.created_utc
FROM @p p JOIN plat.outbox o ON o.outbox_id = p.outbox_id
ORDER BY o.outbox_id;');
GO
SET STATISTICS TIME ON; SET STATISTICS IO ON;
GO
PRINT '--- after #1: accounting poll, watermark 0 (first poll after the upgrade: walks the history once)';
DECLARE @s NVARCHAR(MAX) = (SELECT stmt FROM #poll);
EXEC sp_executesql @s, N'@n INT, @c VARCHAR(40), @settle INT, @t0 VARCHAR(60), @t1 VARCHAR(60), @t2 VARCHAR(60), @t3 VARCHAR(60), @t4 VARCHAR(60), @t5 VARCHAR(60), @t6 VARCHAR(60), @t7 VARCHAR(60), @t8 VARCHAR(60), @t9 VARCHAR(60), @t10 VARCHAR(60), @t11 VARCHAR(60)',
  100, 'accounting', 60, 'Event0', 'Event1', 'Event10', 'Event11', 'Event2', 'Event3', 'Event4', 'Event5', 'Event6', 'Event7', 'Event8', 'Event9';
GO
PRINT '--- after #2: accounting poll, steady state (watermark just below the 20 pending events)';
DECLARE @s NVARCHAR(MAX) = (SELECT stmt FROM #poll);
EXEC sp_executesql @s, N'@n INT, @c VARCHAR(40), @settle INT, @t0 VARCHAR(60), @t1 VARCHAR(60), @t2 VARCHAR(60), @t3 VARCHAR(60), @t4 VARCHAR(60), @t5 VARCHAR(60), @t6 VARCHAR(60), @t7 VARCHAR(60), @t8 VARCHAR(60), @t9 VARCHAR(60), @t10 VARCHAR(60), @t11 VARCHAR(60)',
  100, 'accounting', 60, 'Event0', 'Event1', 'Event10', 'Event11', 'Event2', 'Event3', 'Event4', 'Event5', 'Event6', 'Event7', 'Event8', 'Event9';
GO
SET STATISTICS TIME OFF; SET STATISTICS IO OFF;
-- the consumer delivers the pending events (dedup rows), as DeliverOnceAsync would
INSERT plat.consumer_dedup(consumer, outbox_id)
SELECT 'accounting', o.outbox_id FROM plat.outbox o
WHERE CAST(SUBSTRING(o.event_type, 6, 2) AS INT) BETWEEN 0 AND 11
  AND NOT EXISTS (SELECT 1 FROM plat.consumer_dedup d WHERE d.consumer = 'accounting' AND d.outbox_id = o.outbox_id);
SET STATISTICS TIME ON; SET STATISTICS IO ON;
GO
PRINT '--- after #3: accounting poll, caught up (0 pending; the watermark moves to the newest settled event)';
DECLARE @s NVARCHAR(MAX) = (SELECT stmt FROM #poll);
EXEC sp_executesql @s, N'@n INT, @c VARCHAR(40), @settle INT, @t0 VARCHAR(60), @t1 VARCHAR(60), @t2 VARCHAR(60), @t3 VARCHAR(60), @t4 VARCHAR(60), @t5 VARCHAR(60), @t6 VARCHAR(60), @t7 VARCHAR(60), @t8 VARCHAR(60), @t9 VARCHAR(60), @t10 VARCHAR(60), @t11 VARCHAR(60)',
  100, 'accounting', 60, 'Event0', 'Event1', 'Event10', 'Event11', 'Event2', 'Event3', 'Event4', 'Event5', 'Event6', 'Event7', 'Event8', 'Event9';
GO
PRINT '--- after #4: accounting poll, idle (watermark at the head)';
DECLARE @s NVARCHAR(MAX) = (SELECT stmt FROM #poll);
EXEC sp_executesql @s, N'@n INT, @c VARCHAR(40), @settle INT, @t0 VARCHAR(60), @t1 VARCHAR(60), @t2 VARCHAR(60), @t3 VARCHAR(60), @t4 VARCHAR(60), @t5 VARCHAR(60), @t6 VARCHAR(60), @t7 VARCHAR(60), @t8 VARCHAR(60), @t9 VARCHAR(60), @t10 VARCHAR(60), @t11 VARCHAR(60)',
  100, 'accounting', 60, 'Event0', 'Event1', 'Event10', 'Event11', 'Event2', 'Event3', 'Event4', 'Event5', 'Event6', 'Event7', 'Event8', 'Event9';
GO
PRINT '--- after #5: housekeeping poll (one type), watermark 0 then steady state';
DECLARE @s NVARCHAR(MAX) = REPLACE((SELECT stmt FROM #poll), N'(VALUES (@t0), (@t1), (@t2), (@t3), (@t4), (@t5), (@t6), (@t7), (@t8), (@t9), (@t10), (@t11))', N'(VALUES (@t0))');
EXEC sp_executesql @s, N'@n INT, @c VARCHAR(40), @settle INT, @t0 VARCHAR(60)', 100, 'housekeeping', 60, 'Event12';
EXEC sp_executesql @s, N'@n INT, @c VARCHAR(40), @settle INT, @t0 VARCHAR(60)', 100, 'housekeeping', 60, 'Event12';
GO
SET STATISTICS TIME OFF; SET STATISTICS IO OFF;
GO
SELECT consumer, low_outbox_id FROM plat.outbox_consumer ORDER BY consumer;
GO
