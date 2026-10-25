-- HCC migration 0001 · platform schemas and tables (contracts package §4: plat = audit, idempotency, outbox, consumer dedup).
-- Original HCC design (07 Integration Lab/sql-spike/01-schema.sql, A3); no donor migrations. Applied by the HCC migration
-- runner inside one transaction; batches are separated by GO lines. Money = BIGINT paise; event times DATETIME2(3) UTC.
-- Users, roles, grants, sessions and approvals are P1 (access) migrations.
CREATE SCHEMA inv;
GO
CREATE SCHEMA gst;
GO
CREATE SCHEMA bill;
GO
CREATE SCHEMA pay;
GO
CREATE SCHEMA ops;
GO
CREATE SCHEMA cls;
GO
CREATE SCHEMA plat;
GO
-- Idempotency record per (property, principal, operation, key) with SHA-256 of the canonical payload (spike T08).
CREATE TABLE plat.idempotency(
  property_id INT NOT NULL,
  principal_id INT NOT NULL,
  operation VARCHAR(40) NOT NULL,
  idem_key VARCHAR(80) NOT NULL,
  payload_sha256 BINARY(32) NOT NULL,
  result_json NVARCHAR(MAX) NULL,
  created_utc DATETIME2(3) NOT NULL CONSTRAINT DF_idempotency_created DEFAULT SYSUTCDATETIME(),
  CONSTRAINT PK_idempotency PRIMARY KEY (property_id, principal_id, operation, idem_key),
  CONSTRAINT CK_idempotency_result CHECK (result_json IS NULL OR ISJSON(result_json) = 1));
-- Transactional outbox: written in the same transaction as the business change (spike T09).
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
-- A consumer records (consumer, outbox_id) in the same transaction as its effect: redelivery cannot apply twice.
CREATE TABLE plat.consumer_dedup(
  consumer VARCHAR(40) NOT NULL,
  outbox_id BIGINT NOT NULL CONSTRAINT FK_consumer_dedup_outbox REFERENCES plat.outbox,
  processed_utc DATETIME2(3) NOT NULL CONSTRAINT DF_consumer_dedup_processed DEFAULT SYSUTCDATETIME(),
  CONSTRAINT PK_consumer_dedup PRIMARY KEY (consumer, outbox_id));
-- Append-only audit trail; every command transaction writes at least one row.
CREATE TABLE plat.audit_event(
  audit_id BIGINT IDENTITY CONSTRAINT PK_audit_event PRIMARY KEY,
  property_id INT NOT NULL,
  principal_id INT NOT NULL,
  action VARCHAR(60) NOT NULL,
  target VARCHAR(60) NOT NULL,
  detail NVARCHAR(1000) NOT NULL,
  operation VARCHAR(40) NULL,
  idem_key VARCHAR(80) NULL,
  at_utc DATETIME2(3) NOT NULL CONSTRAINT DF_audit_event_at DEFAULT SYSUTCDATETIME());
CREATE INDEX IX_audit_event_target ON plat.audit_event(target, at_utc);
GO
-- Audit rows are never updated or deleted by the application.
CREATE TRIGGER plat.TR_audit_event_append_only ON plat.audit_event AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51000, 'plat.audit_event is append-only.', 1;
END;
