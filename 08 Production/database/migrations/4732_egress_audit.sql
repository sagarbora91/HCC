-- HCC migration 4732 · F5-E1 egress decision audit (EGRESS-CONTRACT §1): one row per allowed outbound request and per refusal (refusals
-- rate-limited to one per integration per minute by the gate). host = the target host name only (never a path, query or payload).
CREATE TABLE ops.egress_audit(
  audit_id BIGINT IDENTITY CONSTRAINT PK_egress_audit PRIMARY KEY,
  at_utc DATETIME2(3) NOT NULL,
  integration VARCHAR(12) NOT NULL,
  decision VARCHAR(7) NOT NULL CONSTRAINT CK_egress_audit_decision CHECK (decision IN ('allowed', 'refused')),
  reason VARCHAR(30) NOT NULL,
  user_id INT NULL,
  host NVARCHAR(255) NULL);
CREATE INDEX IX_egress_audit_integration ON ops.egress_audit(integration, decision, at_utc DESC);
