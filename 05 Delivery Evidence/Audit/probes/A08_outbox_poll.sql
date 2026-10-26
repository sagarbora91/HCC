-- A08 bounded probe: cost of the projector poll query (OutboxConsumer.cs:51-58) on a year-sized plat.outbox.
-- Runs in a throwaway HccTest_A08_outbox database (created and dropped by A08_outbox_poll.ps1). DDL copied from 0001_platform.sql:31-48.
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
-- $(Rows) events over 30 event types (~300-char payloads); 5 consumers, each subscribed to a share of the types.
DECLARE @n INT = $(Rows);
;WITH a AS (SELECT 1 x FROM (VALUES(1),(1),(1),(1),(1),(1),(1),(1),(1),(1)) v(x)),
      n AS (SELECT TOP (@n) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) i FROM a a1, a a2, a a3, a a4, a a5, a a6, a a7)
INSERT plat.outbox(event_type, aggregate_id, payload, property_id, principal_id, published_utc, attempts)
SELECT CONCAT('Event', i % 30), i, CONCAT(N'{"folioId":', i, N',"chargeId":', i, N',"kind":"room","serviceDate":"2026-10-24","amountPaise":200000,"taxPaise":10000,"note":"', REPLICATE(N'x', 180), N'"}'),
       1, 1, SYSUTCDATETIME(), 1
FROM n;
-- consumers: accounting = types 0-11, housekeeping = 12, stayover = 13, messaging = 14-19, ota = 20-25; everything processed except the last 20 per consumer
INSERT plat.consumer_dedup(consumer, outbox_id)
SELECT c.consumer, o.outbox_id FROM plat.outbox o
JOIN (VALUES ('accounting', 0, 11), ('housekeeping', 12, 12), ('stayover', 13, 13), ('messaging', 14, 19), ('ota', 20, 25)) c(consumer, lo, hi)
  ON CAST(SUBSTRING(o.event_type, 6, 2) AS INT) BETWEEN c.lo AND c.hi
WHERE o.outbox_id <= @n - 20;
GO
SELECT 'rows' AS what, (SELECT COUNT(*) FROM plat.outbox) AS outbox_rows, (SELECT COUNT(*) FROM plat.consumer_dedup) AS dedup_rows,
       (SELECT SUM(used_page_count) * 8 / 1024 FROM sys.dm_db_partition_stats WHERE object_id = OBJECT_ID('plat.outbox')) AS outbox_mb;
GO
SET STATISTICS TIME ON; SET STATISTICS IO ON;
GO
-- first run: the accounting poll exactly as OutboxConsumer.ConsumeOnceAsync issues it (TOP 100, types as OPENJSON)
DECLARE @types NVARCHAR(MAX) = N'["Event0","Event1","Event2","Event3","Event4","Event5","Event6","Event7","Event8","Event9","Event10","Event11"]';
SELECT TOP (100) o.outbox_id, o.event_type, o.aggregate_id, o.payload, o.created_utc
FROM plat.outbox o
WHERE o.event_type IN (SELECT value FROM OPENJSON(@types))
  AND NOT EXISTS (SELECT 1 FROM plat.consumer_dedup d WHERE d.consumer = 'accounting' AND d.outbox_id = o.outbox_id)
ORDER BY o.outbox_id;
GO
-- warm: the same poll again, then the housekeeping poll (one type)
DECLARE @types NVARCHAR(MAX) = N'["Event0","Event1","Event2","Event3","Event4","Event5","Event6","Event7","Event8","Event9","Event10","Event11"]';
SELECT TOP (100) o.outbox_id FROM plat.outbox o
WHERE o.event_type IN (SELECT value FROM OPENJSON(@types))
  AND NOT EXISTS (SELECT 1 FROM plat.consumer_dedup d WHERE d.consumer = 'accounting' AND d.outbox_id = o.outbox_id)
ORDER BY o.outbox_id;
GO
DECLARE @types NVARCHAR(MAX) = N'["Event12"]';
SELECT TOP (100) o.outbox_id FROM plat.outbox o
WHERE o.event_type IN (SELECT value FROM OPENJSON(@types))
  AND NOT EXISTS (SELECT 1 FROM plat.consumer_dedup d WHERE d.consumer = 'housekeeping' AND d.outbox_id = o.outbox_id)
ORDER BY o.outbox_id;
GO
-- variant (fix direction): the types materialised once into a keyed table variable
DECLARE @types NVARCHAR(MAX) = N'["Event0","Event1","Event2","Event3","Event4","Event5","Event6","Event7","Event8","Event9","Event10","Event11"]';
DECLARE @t TABLE(v VARCHAR(60) PRIMARY KEY); INSERT @t SELECT value FROM OPENJSON(@types);
SELECT TOP (100) o.outbox_id FROM plat.outbox o JOIN @t t ON t.v = o.event_type
WHERE NOT EXISTS (SELECT 1 FROM plat.consumer_dedup d WHERE d.consumer = 'accounting' AND d.outbox_id = o.outbox_id)
ORDER BY o.outbox_id;
GO
SET STATISTICS TIME OFF; SET STATISTICS IO OFF;
GO
