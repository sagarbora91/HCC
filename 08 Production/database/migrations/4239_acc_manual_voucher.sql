-- HCC migration 4239 · ACC-2 manual vouchers (02 Planning/agents/ACC2-POSTING-CONTRACT.md §2; rulebook §1 principle 2, §5 B5–B7, B11, A18, §6).
-- What operations cannot produce (capital, loans, bank charges, interest, bad debts, statutory / GST / TDS remittances, corrections) is
-- entered by hand: the Accountant's voucher is pending until the Owner decides; the Owner's own is approved at once (maker-checker,
-- DEC-058 / DEC-063). An approved voucher publishes ManualVoucherApproved; the accounting consumer books it as the source
-- manual:<voucher_id> (kind manual, sub = template) on voucher_date. Voiding (Owner): on an unlocked day the voucher becomes void and its
-- source no longer counts (SqlAccountingStore.SourcesAsync leaves out the source of a void voucher that has no reversal; sources stay
-- append-only); on a locked day a reversing voucher (lines swapped, void_of = the original) is created and approved, dated the business date.
-- account = a ledger code or a party ledger id <control code>/<party id> (contract §3). Money BIGINT paise; times DATETIME2(3) UTC.
CREATE TABLE acc.manual_voucher(
  voucher_id BIGINT IDENTITY CONSTRAINT PK_acc_manual_voucher PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_acc_manual_voucher_property REFERENCES inv.property,
  voucher_date DATE NOT NULL,
  voucher_type VARCHAR(8) NOT NULL CONSTRAINT CK_acc_manual_voucher_type CHECK (voucher_type IN ('Journal','Contra','Payment','Receipt')),
  narration NVARCHAR(400) NOT NULL CONSTRAINT CK_acc_manual_voucher_narration CHECK (LEN(narration) >= 3),
  reference NVARCHAR(60) NULL,
  template VARCHAR(24) NULL CONSTRAINT CK_acc_manual_voucher_template CHECK (template IS NULL OR template IN ('journal','contra','capital-introduced',
    'owner-drawing-bank','loan-received','loan-emi','bank-charges','interest-income','bad-debt','statutory-remittance','gst-payment','tds-remittance')),
  status VARCHAR(8) NOT NULL CONSTRAINT CK_acc_manual_voucher_status CHECK (status IN ('pending','approved','rejected','void')),
  entered_by INT NOT NULL,
  entered_utc DATETIME2(3) NOT NULL,
  decided_by INT NULL,
  decided_utc DATETIME2(3) NULL,
  reason NVARCHAR(200) NULL,
  void_of BIGINT NULL CONSTRAINT FK_acc_manual_voucher_void_of REFERENCES acc.manual_voucher,
  voided_by INT NULL,
  voided_utc DATETIME2(3) NULL,
  booked_on DATE NULL,
  source_key AS CAST(CONCAT('manual:', voucher_id) AS VARCHAR(80)) PERSISTED,
  row_ver ROWVERSION,
  CONSTRAINT CK_acc_manual_voucher_decided CHECK ((status = 'pending' AND decided_by IS NULL AND decided_utc IS NULL)
                                               OR (status <> 'pending' AND decided_by IS NOT NULL AND decided_utc IS NOT NULL)),
  CONSTRAINT CK_acc_manual_voucher_void CHECK ((status = 'void' AND voided_by IS NOT NULL AND voided_utc IS NOT NULL AND reason IS NOT NULL)
                                            OR (status <> 'void' AND voided_by IS NULL AND voided_utc IS NULL)),
  CONSTRAINT CK_acc_manual_voucher_rejected CHECK (status <> 'rejected' OR reason IS NOT NULL));
GO
CREATE INDEX IX_acc_manual_voucher_date ON acc.manual_voucher(property_id, voucher_date, voucher_id);
CREATE UNIQUE INDEX UX_acc_manual_voucher_source ON acc.manual_voucher(property_id, source_key);
CREATE UNIQUE INDEX UX_acc_manual_voucher_void_of ON acc.manual_voucher(void_of) WHERE void_of IS NOT NULL;
CREATE TABLE acc.manual_line(
  voucher_id BIGINT NOT NULL CONSTRAINT FK_acc_manual_line_voucher REFERENCES acc.manual_voucher,
  line_no SMALLINT NOT NULL,
  account VARCHAR(45) NOT NULL CONSTRAINT CK_acc_manual_line_account CHECK (account LIKE '[0-9][0-9][0-9][0-9]' OR account LIKE '[0-9][0-9][0-9][0-9]/_%'),
  debit_paise BIGINT NOT NULL CONSTRAINT CK_acc_manual_line_debit CHECK (debit_paise BETWEEN 0 AND 10000000000),
  credit_paise BIGINT NOT NULL CONSTRAINT CK_acc_manual_line_credit CHECK (credit_paise BETWEEN 0 AND 10000000000),
  note NVARCHAR(120) NULL,
  CONSTRAINT PK_acc_manual_line PRIMARY KEY (voucher_id, line_no),
  CONSTRAINT CK_acc_manual_line_side CHECK ((debit_paise > 0 AND credit_paise = 0) OR (credit_paise > 0 AND debit_paise = 0)));
GO
