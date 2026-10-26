-- HCC migration 2900 · outbox consumer cursor, failure records and purge support (fix wave A, audit A08-01 and A08-02 / A03b-04).
-- Each poll now reads only the tail of plat.outbox: a per-consumer low watermark (every subscribed event at or below it is processed
-- or parked) plus an index on (event_type, outbox_id). A failing effect is recorded per (consumer, event) in its own transaction and
-- parked after 5 failures, so later events keep flowing. Subscriptions are stored so the retention purge only removes events that
-- every subscribed consumer has processed. Event times are SQL Server UTC (as published_utc in OutboxConsumer).
CREATE INDEX IX_outbox_type ON plat.outbox(event_type, outbox_id) INCLUDE (property_id);
-- Purge and the FK check on outbox deletes look accounting sources up by outbox id (sources keep their event: never purged).
CREATE INDEX IX_acc_source_outbox ON acc.source(outbox_id);
-- One row per consumer: low_outbox_id = every event of a subscribed type with outbox_id <= low is processed (consumer_dedup) or parked.
CREATE TABLE plat.outbox_consumer(
  consumer VARCHAR(40) NOT NULL CONSTRAINT PK_outbox_consumer PRIMARY KEY,
  low_outbox_id BIGINT NOT NULL CONSTRAINT DF_outbox_consumer_low DEFAULT 0 CONSTRAINT CK_outbox_consumer_low CHECK (low_outbox_id >= 0),
  updated_utc DATETIME2(3) NOT NULL CONSTRAINT DF_outbox_consumer_updated DEFAULT SYSUTCDATETIME());
-- Event types each consumer subscribes to (registered by OutboxConsumer on its first poll; seeded below for the shipped consumers).
CREATE TABLE plat.outbox_subscription(
  consumer VARCHAR(40) NOT NULL CONSTRAINT FK_outbox_subscription_consumer REFERENCES plat.outbox_consumer,
  event_type VARCHAR(60) NOT NULL,
  CONSTRAINT PK_outbox_subscription PRIMARY KEY (consumer, event_type));
CREATE INDEX IX_outbox_subscription_type ON plat.outbox_subscription(event_type, consumer);
-- Failed deliveries per consumer and event; written outside the (rolled back) effect transaction. parked_utc set = no more retries.
CREATE TABLE plat.consumer_failure(
  consumer VARCHAR(40) NOT NULL,
  outbox_id BIGINT NOT NULL CONSTRAINT FK_consumer_failure_outbox REFERENCES plat.outbox,
  attempts INT NOT NULL CONSTRAINT CK_consumer_failure_attempts CHECK (attempts >= 1),
  last_error NVARCHAR(1000) NOT NULL,
  first_failed_utc DATETIME2(3) NOT NULL,
  last_failed_utc DATETIME2(3) NOT NULL,
  parked_utc DATETIME2(3) NULL,
  CONSTRAINT PK_consumer_failure PRIMARY KEY (consumer, outbox_id));
CREATE INDEX IX_consumer_failure_parked ON plat.consumer_failure(outbox_id) WHERE parked_utc IS NOT NULL;
GO
-- The shipped consumers and their event types (AccountingEvents.Consumed, HousekeepingTaskProjector, StayoverTaskProjector,
-- MessagingTriggerProjector.EventTypes, OtaChannelManagerProjector.EventTypes). low = 0: the first poll walks the history once.
INSERT plat.outbox_consumer(consumer) VALUES ('accounting-vouchers'), ('housekeeping-tasks'), ('housekeeping-stayover'), ('messaging-triggers'), ('ota-channel-manager');
INSERT plat.outbox_subscription(consumer, event_type) VALUES
  ('accounting-vouchers', 'ChargePosted'), ('accounting-vouchers', 'ReceiptRecorded'), ('accounting-vouchers', 'ApprovalDecided'),
  ('accounting-vouchers', 'ShiftDifferenceDecided'), ('accounting-vouchers', 'PosBillSettled'), ('accounting-vouchers', 'PosBillVoided'),
  ('accounting-vouchers', 'ExpensePosted'), ('accounting-vouchers', 'ExpenseReversed'), ('accounting-vouchers', 'PayrollPosted'),
  ('accounting-vouchers', 'PayrollPaid'), ('accounting-vouchers', 'PayRunUnlocked'),
  ('housekeeping-tasks', 'StayDeparted'),
  ('housekeeping-stayover', 'BusinessDateOpened'),
  ('messaging-triggers', 'ReservationCreated'), ('messaging-triggers', 'ReservationConfirmed'), ('messaging-triggers', 'ReceiptRecorded'),
  ('messaging-triggers', 'StayDeparted'),
  ('ota-channel-manager', 'ChannelPushQueued'), ('ota-channel-manager', 'ReservationCreated'), ('ota-channel-manager', 'ReservationAmended'),
  ('ota-channel-manager', 'ReservationCancelled'), ('ota-channel-manager', 'ReservationNoShow'), ('ota-channel-manager', 'YieldTariffsPublished');
