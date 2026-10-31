-- HCC migration 4247 · ACC-2 acc2-gst · the month-end GST set-off (02 Planning/agents/ACC2-POSTING-CONTRACT.md §6; rulebook §4.12, E1, §8.6).
-- PostGstSetoff (Owner / Accountant, PERM-ACC-013) computes the month's output by head, the ITC available (brought forward + the month's) and
-- the GST electronic cash ledger 1340, sets off in the s.49 / 49A / 49B order (Hcc.Domain.Accounting.GstSetOff) and books the journal as an
-- acc.source (kind 'tax', sub 'gst-setoff', key 'gst-setoff:<month>@<posted on>') on the business date it is posted (after the month ended).
-- One current row per month: a later set-off of a month not locked supersedes the row (superseded_utc) and books the reversal of the old
-- journal; acc.source stays append-only. The inputs are stored (output, credit, cash available); the set-off is recomputed from them.
-- Also widens acc.source.source_kind to the ACC-2 kinds (contract §1) unless an earlier ACC-2 migration (4238, acc2-manual) already did.
-- Original HCC design; no donor code. Money BIGINT paise; times DATETIME2(3) UTC written by the application.
CREATE TABLE acc.gst_setoff(
  setoff_id BIGINT IDENTITY CONSTRAINT PK_acc_gst_setoff PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_acc_gst_setoff_property REFERENCES inv.property,
  setoff_month CHAR(7) NOT NULL CONSTRAINT CK_acc_gst_setoff_month CHECK (setoff_month LIKE '[0-9][0-9][0-9][0-9]-[01][0-9]'),
  posted_on DATE NOT NULL,
  source_key VARCHAR(80) NOT NULL,
  output_igst_paise BIGINT NOT NULL CONSTRAINT CK_acc_gst_setoff_oi CHECK (output_igst_paise >= 0),
  output_cgst_paise BIGINT NOT NULL CONSTRAINT CK_acc_gst_setoff_oc CHECK (output_cgst_paise >= 0),
  output_sgst_paise BIGINT NOT NULL CONSTRAINT CK_acc_gst_setoff_os CHECK (output_sgst_paise >= 0),
  credit_igst_paise BIGINT NOT NULL CONSTRAINT CK_acc_gst_setoff_ci CHECK (credit_igst_paise >= 0),
  credit_cgst_paise BIGINT NOT NULL CONSTRAINT CK_acc_gst_setoff_cc CHECK (credit_cgst_paise >= 0),
  credit_sgst_paise BIGINT NOT NULL CONSTRAINT CK_acc_gst_setoff_cs CHECK (credit_sgst_paise >= 0),
  cash_available_paise BIGINT NOT NULL CONSTRAINT CK_acc_gst_setoff_cash CHECK (cash_available_paise >= 0),
  payable_paise BIGINT NOT NULL,
  challan_paise BIGINT NOT NULL,
  posted_by INT NOT NULL,
  posted_utc DATETIME2(3) NOT NULL,
  superseded_utc DATETIME2(3) NULL,
  CONSTRAINT CK_acc_gst_setoff_challan CHECK (challan_paise BETWEEN 0 AND payable_paise));
CREATE UNIQUE INDEX UX_acc_gst_setoff_current ON acc.gst_setoff(property_id, setoff_month) WHERE superseded_utc IS NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_acc_source_kind' AND parent_object_id = OBJECT_ID('acc.source') AND definition LIKE '%''tax''%')
BEGIN
  ALTER TABLE acc.source DROP CONSTRAINT CK_acc_source_kind;
  ALTER TABLE acc.source ADD CONSTRAINT CK_acc_source_kind
    CHECK (source_kind IN ('line','receipt','pos','expense','closing','payroll','manual','purchase','vendor','tax','ota'));
END
