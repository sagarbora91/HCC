-- HCC migration 4230 · ACC-2 purchases on the expense record (02 Planning/agents/ACC2-POSTING-CONTRACT.md §3; rulebook C1, C5, C8, C9, C10;
-- DEC-143, DEC-145, DEC-146, DEC-147). Block 4230–4237 (acc2-purchase). Every new column is optional: rows before ACC-2 stay as they are.
--   gst_kind            'inter' = IGST on the bill (vendor outside Maharashtra) → ITC to 1330; NULL / 'intra' = CGST + SGST (1310 / 1320)
--   rcm, rcm_bps        reverse charge (category flag stamped at approval): the bill has no vendor GST; HCC posts the RCM GST to 2140
--   capital, asset_no   fixed-asset purchase (category flag): the category's ledger is a 15x0 block; asset_no = the acc.fixed_asset row
--   tds_paise, tds_section  TDS held back on the bill (DEC-146; the vendor is credited net, Cr 2150)
--   cash_deduction_lost B10 cash guard tripped (cash above ₹10,000 to one person in a day; acc2-flows sets it)
--   return_of           a goods return / vendor credit note (C5): a negative bill linked to the bill it returns (Owner approves)
-- Constraints widened: a return is negative while not a reversal (CK_expense_sign); the Owner's own return posts at once (CK_expense_sod);
-- ITC on a reverse-charge bill is the RCM GST, so it may be allowed without vendor GST (CK_expense_itc).
ALTER TABLE ops.expense ADD
  gst_kind VARCHAR(5) NULL CONSTRAINT CK_expense_gst_kind CHECK (gst_kind IN ('intra','inter')),
  rcm BIT NOT NULL CONSTRAINT DF_expense_rcm DEFAULT 0,
  rcm_bps INT NULL CONSTRAINT CK_expense_rcm_bps CHECK (rcm_bps BETWEEN 0 AND 2800),
  capital BIT NOT NULL CONSTRAINT DF_expense_capital DEFAULT 0,
  asset_no VARCHAR(20) NULL,
  tds_paise BIGINT NULL CONSTRAINT CK_expense_tds CHECK (tds_paise <> 0 AND tds_paise BETWEEN -10000000000 AND 10000000000),
  tds_section VARCHAR(20) NULL,
  cash_deduction_lost BIT NOT NULL CONSTRAINT DF_expense_cash_lost DEFAULT 0,
  return_of BIGINT NULL CONSTRAINT FK_expense_return_of REFERENCES ops.expense;
GO
ALTER TABLE ops.expense DROP CONSTRAINT CK_expense_sign;
ALTER TABLE ops.expense DROP CONSTRAINT CK_expense_sod;
ALTER TABLE ops.expense DROP CONSTRAINT CK_expense_itc;
GO
ALTER TABLE ops.expense ADD CONSTRAINT CK_expense_sign CHECK ((reversal_of IS NULL AND return_of IS NULL AND amount_paise > 0)
  OR (reversal_of IS NOT NULL AND amount_paise < 0 AND status = 'Posted')
  OR (return_of IS NOT NULL AND reversal_of IS NULL AND amount_paise < 0));
ALTER TABLE ops.expense ADD CONSTRAINT CK_expense_sod CHECK (reversal_of IS NOT NULL OR return_of IS NOT NULL OR decided_by IS NULL OR decided_by <> submitted_by);
ALTER TABLE ops.expense ADD CONSTRAINT CK_expense_itc CHECK (itc_allowed = 0 OR gst_paise IS NOT NULL OR rcm = 1);
ALTER TABLE ops.expense ADD CONSTRAINT CK_expense_rcm CHECK ((rcm = 0 AND rcm_bps IS NULL) OR (rcm = 1 AND rcm_bps IS NOT NULL AND gst_paise IS NULL));
CREATE INDEX IX_expense_return_of ON ops.expense(return_of) INCLUDE (status, amount_paise, gst_paise) WHERE return_of IS NOT NULL;
CREATE INDEX IX_expense_vendor ON ops.expense(property_id, vendor) INCLUDE (status, method, amount_paise, business_date, reversal_of, return_of, tds_paise);
GO
