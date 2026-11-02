-- HCC migration 4731 · F5-E1 outbox of outbound (internet) work (EGRESS-CONTRACT §1.2). Every item an adapter must send to the internet is
-- queued here with its integration; while that integration is OFF the item is 'held' (held_reason 'internet off', not a failure, no attempt
-- consumed). The egress replay hook (EgressOutboxWorker, a poll of the existing outbox worker base) moves held items of an integration back to
-- 'pending' in original order as soon as it is ON. queue = messaging | ota-push | email | scheduled-report (System health counts per queue).
CREATE TABLE ops.egress_outbox(
  item_id BIGINT IDENTITY CONSTRAINT PK_egress_outbox PRIMARY KEY,
  queue VARCHAR(20) NOT NULL CONSTRAINT CK_egress_outbox_queue CHECK (queue IN ('messaging', 'ota-push', 'email', 'scheduled-report', 'ocr', 'payments', 'update-check')),
  integration VARCHAR(12) NOT NULL CONSTRAINT CK_egress_outbox_integration CHECK (integration IN ('messaging', 'ota', 'ocr', 'payments', 'email', 'updateCheck')),
  ref_id NVARCHAR(80) NULL,
  payload NVARCHAR(MAX) NOT NULL CONSTRAINT CK_egress_outbox_payload CHECK (ISJSON(payload) = 1),
  status VARCHAR(8) NOT NULL CONSTRAINT CK_egress_outbox_status CHECK (status IN ('pending', 'held', 'sent', 'failed')),
  held_reason NVARCHAR(60) NULL,
  held_at DATETIME2(3) NULL,
  attempts INT NOT NULL CONSTRAINT DF_egress_outbox_attempts DEFAULT 0,
  created_utc DATETIME2(3) NOT NULL CONSTRAINT DF_egress_outbox_created DEFAULT SYSUTCDATETIME(),
  updated_utc DATETIME2(3) NOT NULL CONSTRAINT DF_egress_outbox_updated DEFAULT SYSUTCDATETIME(),
  CONSTRAINT CK_egress_outbox_held CHECK ((status = 'held' AND held_reason IS NOT NULL AND held_at IS NOT NULL) OR (status <> 'held' AND held_reason IS NULL AND held_at IS NULL)));
CREATE INDEX IX_egress_outbox_integration_status ON ops.egress_outbox(integration, status, item_id);
