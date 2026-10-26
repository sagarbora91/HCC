-- V1 probe (A08-01): cost of OutboxConsumer's pending query once the outbox holds ~a year of events and every consumer is caught up.
-- Schema copied from database/migrations/0001_platform.sql:31-48; query copied from OutboxConsumer.cs:51-58. Runs in HccTest_V1_outbox only.
SET NOCOUNT ON;
IF DB_ID('HccTest_V1_outbox') IS NOT NULL BEGIN ALTER DATABASE HccTest_V1_outbox SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE HccTest_V1_outbox; END
CREATE DATABASE HccTest_V1_outbox;
GO
USE HccTest_V1_outbox;
GO
SET NOCOUNT ON;
EXEC('CREATE SCHEMA plat');
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
-- 250,000 events (~685/day for a year), mixed types, ~250-char JSON payloads; all published.
;WITH n AS (SELECT TOP (250000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b CROSS JOIN sys.all_objects c)
INSERT plat.outbox(event_type, aggregate_id, payload, property_id, principal_id, published_utc, attempts)
SELECT CASE i % 10 WHEN 0 THEN 'ChargePosted' WHEN 1 THEN 'ChargePosted' WHEN 2 THEN 'ChargePosted' WHEN 3 THEN 'ReceiptRecorded' WHEN 4 THEN 'StayCheckedIn'
                   WHEN 5 THEN 'StayCheckedOut' WHEN 6 THEN 'PosBillSettled' WHEN 7 THEN 'RoomStatusChanged' WHEN 8 THEN 'BookingCreated' ELSE 'BusinessDateOpened' END,
       i, CONCAT(N'{"folioId":', i, N',"chargeId":', i * 3, N',"kind":"room","serviceDate":"2026-05-01","amountPaise":350000,"taxPaise":17500,"note":"', REPLICATE(N'x', 120), N'"}'),
       1, 1, SYSUTCDATETIME(), 1
FROM n;
-- every consumer caught up on the types it consumes (worst realistic case: nothing pending)
INSERT plat.consumer_dedup(consumer, outbox_id) SELECT 'accounting-vouchers', outbox_id FROM plat.outbox WHERE event_type IN ('ChargePosted','ReceiptRecorded','PosBillSettled');
INSERT plat.consumer_dedup(consumer, outbox_id) SELECT 'housekeeping-tasks', outbox_id FROM plat.outbox WHERE event_type IN ('StayCheckedOut','RoomStatusChanged');
INSERT plat.consumer_dedup(consumer, outbox_id) SELECT 'housekeeping-stayover', outbox_id FROM plat.outbox WHERE event_type IN ('BusinessDateOpened');
INSERT plat.consumer_dedup(consumer, outbox_id) SELECT 'messaging-triggers', outbox_id FROM plat.outbox WHERE event_type IN ('BookingCreated','StayCheckedIn','StayCheckedOut');
INSERT plat.consumer_dedup(consumer, outbox_id) SELECT 'ota-channel-manager', outbox_id FROM plat.outbox WHERE event_type IN ('BookingCreated');
GO
SELECT COUNT(*) AS outbox_rows FROM plat.outbox; SELECT COUNT(*) AS dedup_rows FROM plat.consumer_dedup;
-- warm cache once, then measure
DECLARE @types NVARCHAR(MAX) = N'["ChargePosted","ReceiptRecorded","ApprovalDecided","ShiftDifferenceDecided","PosBillSettled","PosBillVoided","ExpensePosted","ExpenseReversed","PayrollPosted","PayrollPaid","PayRunUnlocked"]';
SELECT TOP (100) o.outbox_id FROM plat.outbox o WHERE o.event_type IN (SELECT value FROM OPENJSON(@types))
  AND NOT EXISTS (SELECT 1 FROM plat.consumer_dedup d WHERE d.consumer = 'accounting-vouchers' AND d.outbox_id = o.outbox_id) ORDER BY o.outbox_id;
GO
SET STATISTICS IO ON; SET STATISTICS TIME ON;
DECLARE @types NVARCHAR(MAX) = N'["ChargePosted","ReceiptRecorded","ApprovalDecided","ShiftDifferenceDecided","PosBillSettled","PosBillVoided","ExpensePosted","ExpenseReversed","PayrollPosted","PayrollPaid","PayRunUnlocked"]';
PRINT '--- accounting-vouchers pending query (0 pending)';
SELECT TOP (100) o.outbox_id, o.event_type, o.aggregate_id, o.payload, o.created_utc FROM plat.outbox o WHERE o.event_type IN (SELECT value FROM OPENJSON(@types))
  AND NOT EXISTS (SELECT 1 FROM plat.consumer_dedup d WHERE d.consumer = 'accounting-vouchers' AND d.outbox_id = o.outbox_id) ORDER BY o.outbox_id;
PRINT '--- ota-channel-manager pending query (0 pending)';
SELECT TOP (100) o.outbox_id, o.event_type, o.aggregate_id, o.payload, o.created_utc FROM plat.outbox o WHERE o.event_type IN (SELECT value FROM OPENJSON(N'["BookingCreated"]'))
  AND NOT EXISTS (SELECT 1 FROM plat.consumer_dedup d WHERE d.consumer = 'ota-channel-manager' AND d.outbox_id = o.outbox_id) ORDER BY o.outbox_id;
SET STATISTICS IO OFF; SET STATISTICS TIME OFF;
GO
USE master;
ALTER DATABASE HccTest_V1_outbox SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE HccTest_V1_outbox;
PRINT 'dropped HccTest_V1_outbox';
GO
