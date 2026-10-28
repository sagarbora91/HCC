-- HCC migration 4231 · ACC-2 purchase fields on the expense category and vendor masters (contract §3; DEC-143 ITC guard, DEC-145 RCM flag,
-- rulebook C10 capital purchases, §5 TDS section). Money masters: every change is the Owner's (DEC-058, PERM-MST-002).
--   ops.expense_category  rcm + rcm_bps (default reverse-charge rate), capital (the ledger is a fixed-asset block 15x0), itc_blocked (s.17(5))
--   ops.expense_vendor    gst_state (two-letter state code; MH / NULL = intra-state), tds_section (rent | contractor | professional | commission)
ALTER TABLE ops.expense_category ADD
  rcm BIT NOT NULL CONSTRAINT DF_ops_expense_category_rcm DEFAULT 0,
  rcm_bps INT NULL CONSTRAINT CK_ops_expense_category_rcm_bps CHECK (rcm_bps BETWEEN 1 AND 2800),
  capital BIT NOT NULL CONSTRAINT DF_ops_expense_category_capital DEFAULT 0,
  itc_blocked BIT NOT NULL CONSTRAINT DF_ops_expense_category_itc_blocked DEFAULT 0;
GO
ALTER TABLE ops.expense_category ADD CONSTRAINT CK_ops_expense_category_rcm CHECK ((rcm = 0 AND rcm_bps IS NULL) OR (rcm = 1 AND rcm_bps IS NOT NULL));
ALTER TABLE ops.expense_category ADD CONSTRAINT CK_ops_expense_category_capital CHECK (capital = 0 OR ledger LIKE '15[0-9]0');
ALTER TABLE ops.expense_vendor ADD
  gst_state CHAR(2) NULL CONSTRAINT CK_ops_expense_vendor_state CHECK (gst_state LIKE '[A-Z][A-Z]' COLLATE Latin1_General_BIN),
  tds_section VARCHAR(20) NULL CONSTRAINT CK_ops_expense_vendor_tds_section CHECK (tds_section IN ('rent','contractor','professional','commission'));
GO
ALTER TABLE ops.expense_vendor ADD CONSTRAINT CK_ops_expense_vendor_tds_pair CHECK (tds_section IS NULL OR tds = 1);
GO
