-- HCC migration 4244 · ACC-2 TDS remittances (contract §5 ACC_TDS_REMIT; rulebook E5; DEC-146; lane acc2-tds).
-- One row per TDS deposit the CA made for a row of the TDS table and the month the TDS was held back in (due by the 7th, 30 April for
-- March): amount, tender (a bank method), challan, payment date. The voucher is the manual template 'tds-remittance' (Dr 2150 TDS payable ·
-- Cr 1030/<bank>) of the manual-voucher service (acc2-manual, migrations 4238–4241); manual_voucher_id links it once that service posts it
-- (NULL until the integration wires it — ACC2-TDS-DONE.md). The deductee report allocates the remitted amounts to the month's bills.
-- PERM-ACC-013 (Owner, Accountant). Append-only; a wrong entry is reversed through the manual voucher's void.
CREATE TABLE acc.tds_remit(
  remit_id BIGINT IDENTITY CONSTRAINT PK_acc_tds_remit PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_acc_tds_remit_property REFERENCES inv.property,
  month CHAR(7) NOT NULL CONSTRAINT CK_acc_tds_remit_month CHECK (month LIKE '[2][0-9][0-9][0-9]-[01][0-9]'),
  section VARCHAR(20) NOT NULL CONSTRAINT CK_acc_tds_remit_section CHECK (section IN ('rent','contractor','professional','commission')),
  amount_paise BIGINT NOT NULL CONSTRAINT CK_acc_tds_remit_amount CHECK (amount_paise > 0 AND amount_paise <= 10000000000),
  tender VARCHAR(16) NOT NULL CONSTRAINT CK_acc_tds_remit_tender CHECK (tender IN ('Bank transfer','UPI')),
  challan NVARCHAR(40) NOT NULL CONSTRAINT CK_acc_tds_remit_challan CHECK (LEN(challan) >= 3),
  business_date DATE NOT NULL,
  manual_voucher_id BIGINT NULL,
  recorded_by INT NOT NULL,
  recorded_utc DATETIME2(3) NOT NULL);
CREATE INDEX IX_acc_tds_remit_month ON acc.tds_remit(property_id, section, month) INCLUDE (amount_paise);
GO
CREATE TRIGGER acc.TR_tds_remit_append_only ON acc.tds_remit AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted) THROW 52401, 'acc.tds_remit is append-only.', 1;
  IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.remit_id = i.remit_id
             WHERE i.amount_paise <> d.amount_paise OR i.section <> d.section OR i.month <> d.month OR i.challan <> d.challan
                OR (d.manual_voucher_id IS NOT NULL AND (i.manual_voucher_id IS NULL OR i.manual_voucher_id <> d.manual_voucher_id)))
    THROW 52402, 'acc.tds_remit is append-only (only the manual voucher link may be set once).', 1;
END;
GO
