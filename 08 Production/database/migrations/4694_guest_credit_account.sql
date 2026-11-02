-- HCC migration 4694 · LF B2 individual credit guests (LF-CONTRACT §7). A guest may take HCC credit up to `limit_paise`; customer_key = the
-- Udhaar party id (CustomerRules.CreditKey: phone last 10 digits, else the guest id) so the party ledger is 1120/UD-<customer_key> — the
-- same key the cut-over opening entry uses (DEC-148 / DEC-164). Owner only (PERM-GST-011).
CREATE TABLE gst.guest_credit_account(
  guest_id BIGINT NOT NULL CONSTRAINT PK_guest_credit_account PRIMARY KEY CONSTRAINT FK_guest_credit_account_guest REFERENCES gst.guest,
  property_id INT NOT NULL CONSTRAINT FK_guest_credit_account_property REFERENCES inv.property,
  limit_paise BIGINT NOT NULL CONSTRAINT CK_guest_credit_account_limit CHECK (limit_paise BETWEEN 0 AND 10000000000),
  active BIT NOT NULL,
  customer_key VARCHAR(40) NOT NULL,
  note NVARCHAR(200) NULL,
  set_by INT NOT NULL,
  set_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_guest_credit_account_key UNIQUE (property_id, customer_key));
