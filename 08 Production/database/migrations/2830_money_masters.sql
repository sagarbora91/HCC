-- HCC migration 2830 · MA-3 money masters (block 2830–2839). Plan: 02 Planning/HCC_Customisation_Features_Plan_v1.md Part 2 §8 M15–M18,
-- §9 (common framework). Prototype rules: 03 Prototype/src/domain/streams/money-master.ts (+ money-master-specs.ts).
--   mst.bank_account     M16 bank accounts and tills ("settles to" targets; only the last four digits of an account number are kept)
--   mst.payment_method   M15 one list for the folio, the POS, expenses, the cash book and closing (kind, settles to, needs reference, usable at)
--   ops.expense_category M17 expense categories with the accounting ledger, "needs a bill" and a Manager approval-limit override
--   ops.expense_vendor   M18 expense vendors (DEC-MA-6: separate from mnt.vendor, which is untouched)
-- Row rules as every master (2801): code ^[A-Z0-9-]{1,20}$ unique per property (retired rows keep their code) and immutable once used, name
-- 1–60, retire (active = 0 + retired_on) instead of deleting a used row. All four are money lists: every change needs PERM-MST-002 (Owner,
-- DEC-058, granted in 2802). Records keep storing the method / category / vendor TEXT they always stored (pay.receipt.method, pos.bill.tender,
-- ops.expense.method / category / vendor); a value resolves to its row by name, code or an earlier name (mst.master_history field "name").
-- Seeds = the old fixed lists (Cash, UPI, Card, Bank transfer, Vendor credit, Company credit; Supplies, Laundry, Maintenance, Utilities,
-- Petty cash; the till and one bank account) and the distinct free-text vendors of ops.expense, through mst.usp_seed_money_masters: run
-- below for every existing property and again (idempotently) by the money-master reads for a property created later (first-time setup).
CREATE TABLE mst.bank_account(
  bank_account_id INT IDENTITY CONSTRAINT PK_mst_bank_account PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_mst_bank_account_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_mst_bank_account_active DEFAULT 1,
  retired_on DATE NULL,
  kind VARCHAR(4) NOT NULL CONSTRAINT CK_mst_bank_account_kind CHECK (kind IN ('till','bank')),
  bank NVARCHAR(60) NULL,
  account_last4 CHAR(4) NULL CONSTRAINT CK_mst_bank_account_last4 CHECK (account_last4 NOT LIKE '%[^0-9]%'),
  ifsc VARCHAR(11) NULL CONSTRAINT CK_mst_bank_account_ifsc CHECK (ifsc LIKE '[A-Z][A-Z][A-Z][A-Z]0[A-Z0-9][A-Z0-9][A-Z0-9][A-Z0-9][A-Z0-9][A-Z0-9]' COLLATE Latin1_General_BIN),
  opening_balance_paise BIGINT NOT NULL CONSTRAINT DF_mst_bank_account_opening DEFAULT 0 CONSTRAINT CK_mst_bank_account_opening CHECK (opening_balance_paise BETWEEN 0 AND 100000000000),
  default_for VARCHAR(40) NOT NULL CONSTRAINT DF_mst_bank_account_default DEFAULT '',
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_mst_bank_account_code UNIQUE (property_id, code),
  CONSTRAINT CK_mst_bank_account_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_mst_bank_account_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_mst_bank_account_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_mst_bank_account_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)),
  CONSTRAINT CK_mst_bank_account_till CHECK (kind = 'bank' OR (bank IS NULL AND account_last4 IS NULL AND ifsc IS NULL AND default_for = '')),
  CONSTRAINT CK_mst_bank_account_bank CHECK (kind = 'till' OR bank IS NOT NULL));
CREATE INDEX IX_mst_bank_account_order ON mst.bank_account(property_id, sort_order);

CREATE TABLE mst.payment_method(
  payment_method_id INT IDENTITY CONSTRAINT PK_mst_payment_method PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_mst_payment_method_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_mst_payment_method_active DEFAULT 1,
  retired_on DATE NULL,
  kind VARCHAR(6) NOT NULL CONSTRAINT CK_mst_payment_method_kind CHECK (kind IN ('cash','upi','card','bank','wallet','credit')),
  settles_to_id INT NULL CONSTRAINT FK_mst_payment_method_settles_to REFERENCES mst.bank_account,
  needs_reference BIT NOT NULL CONSTRAINT DF_mst_payment_method_ref DEFAULT 0,
  usable_frontdesk BIT NOT NULL CONSTRAINT DF_mst_payment_method_fd DEFAULT 0,
  usable_pos BIT NOT NULL CONSTRAINT DF_mst_payment_method_pos DEFAULT 0,
  usable_expenses BIT NOT NULL CONSTRAINT DF_mst_payment_method_exp DEFAULT 0,
  surcharge_bps INT NOT NULL CONSTRAINT DF_mst_payment_method_surcharge DEFAULT 0 CONSTRAINT CK_mst_payment_method_surcharge CHECK (surcharge_bps BETWEEN 0 AND 1000),
  ledger VARCHAR(4) NULL CONSTRAINT CK_mst_payment_method_ledger CHECK (ledger NOT LIKE '%[^0-9]%' AND LEN(ledger) = 4 AND ledger <> '9999'),
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_mst_payment_method_code UNIQUE (property_id, code),
  CONSTRAINT CK_mst_payment_method_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_mst_payment_method_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_mst_payment_method_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_mst_payment_method_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)),
  -- Credit (vendor credit, company credit) settles to no account and is never a guest payment at the desk or the outlet.
  CONSTRAINT CK_mst_payment_method_credit CHECK ((kind = 'credit' AND settles_to_id IS NULL AND usable_frontdesk = 0 AND usable_pos = 0) OR (kind <> 'credit' AND settles_to_id IS NOT NULL)));
CREATE INDEX IX_mst_payment_method_order ON mst.payment_method(property_id, sort_order);
CREATE INDEX IX_mst_payment_method_settles ON mst.payment_method(settles_to_id) WHERE settles_to_id IS NOT NULL;

CREATE TABLE ops.expense_category(
  expense_category_id INT IDENTITY CONSTRAINT PK_ops_expense_category PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_ops_expense_category_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_ops_expense_category_active DEFAULT 1,
  retired_on DATE NULL,
  ledger VARCHAR(4) NOT NULL CONSTRAINT CK_ops_expense_category_ledger CHECK (ledger NOT LIKE '%[^0-9]%' AND LEN(ledger) = 4 AND ledger <> '9999'),
  needs_bill BIT NOT NULL CONSTRAINT DF_ops_expense_category_bill DEFAULT 0,
  approval_limit_paise BIGINT NULL CONSTRAINT CK_ops_expense_category_limit CHECK (approval_limit_paise BETWEEN 0 AND 10000000000),
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_ops_expense_category_code UNIQUE (property_id, code),
  CONSTRAINT CK_ops_expense_category_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_ops_expense_category_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_ops_expense_category_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_ops_expense_category_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)));
CREATE INDEX IX_ops_expense_category_order ON ops.expense_category(property_id, sort_order);

CREATE TABLE ops.expense_vendor(
  expense_vendor_id INT IDENTITY CONSTRAINT PK_ops_expense_vendor PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_ops_expense_vendor_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_ops_expense_vendor_active DEFAULT 1,
  retired_on DATE NULL,
  vendor_type VARCHAR(8) NOT NULL CONSTRAINT CK_ops_expense_vendor_type CHECK (vendor_type IN ('supplier','utility','laundry','other')),
  gstin VARCHAR(15) NULL CONSTRAINT CK_ops_expense_vendor_gstin CHECK (LEN(gstin) = 15 AND gstin NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN),
  pan VARCHAR(10) NULL CONSTRAINT CK_ops_expense_vendor_pan CHECK (pan LIKE '[A-Z][A-Z][A-Z][A-Z][A-Z][0-9][0-9][0-9][0-9][A-Z]' COLLATE Latin1_General_BIN),
  tds BIT NOT NULL CONSTRAINT DF_ops_expense_vendor_tds DEFAULT 0,
  contact NVARCHAR(100) NULL,
  terms NVARCHAR(60) NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_ops_expense_vendor_code UNIQUE (property_id, code),
  CONSTRAINT CK_ops_expense_vendor_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_ops_expense_vendor_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_ops_expense_vendor_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_ops_expense_vendor_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)),
  CONSTRAINT CK_ops_expense_vendor_tds CHECK (tds = 0 OR pan IS NOT NULL));
CREATE INDEX IX_ops_expense_vendor_order ON ops.expense_vendor(property_id, sort_order);
GO
-- Idempotent seed of one property: each list is filled only while it is empty (rows locked, so two first reads cannot both insert).
CREATE PROCEDURE mst.usp_seed_money_masters @property_id INT
AS
BEGIN
  SET NOCOUNT ON;
  SET XACT_ABORT ON;
  IF EXISTS (SELECT 1 FROM mst.bank_account WHERE property_id = @property_id)
     AND EXISTS (SELECT 1 FROM mst.payment_method WHERE property_id = @property_id)
     AND EXISTS (SELECT 1 FROM ops.expense_category WHERE property_id = @property_id)
     AND (EXISTS (SELECT 1 FROM ops.expense_vendor WHERE property_id = @property_id)
          OR NOT EXISTS (SELECT 1 FROM ops.expense WHERE property_id = @property_id AND LEN(LTRIM(RTRIM(vendor))) > 0))
    RETURN;
  DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
  BEGIN TRANSACTION;
  IF NOT EXISTS (SELECT 1 FROM mst.bank_account WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @property_id)
    INSERT mst.bank_account(property_id, code, name, sort_order, kind, bank, account_last4, ifsc, default_for, updated_by, updated_utc) VALUES
      (@property_id, 'TILL', N'Front-desk cash drawer', 10, 'till', NULL, NULL, NULL, '', 0, @now),
      (@property_id, 'BANK-1', N'Hotel current account', 20, 'bank', N'Bank (to be set by the Owner)', NULL, NULL, 'upi,card,bank', 0, @now);
  IF NOT EXISTS (SELECT 1 FROM mst.payment_method WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @property_id)
  BEGIN
    DECLARE @till INT = (SELECT TOP (1) bank_account_id FROM mst.bank_account WHERE property_id = @property_id AND kind = 'till' AND active = 1 ORDER BY sort_order);
    DECLARE @bank INT = (SELECT TOP (1) bank_account_id FROM mst.bank_account WHERE property_id = @property_id AND kind = 'bank' AND active = 1 ORDER BY sort_order);
    INSERT mst.payment_method(property_id, code, name, sort_order, kind, settles_to_id, needs_reference, usable_frontdesk, usable_pos, usable_expenses, ledger, updated_by, updated_utc) VALUES
      (@property_id, 'CASH', N'Cash', 10, 'cash', @till, 0, 1, 1, 1, '1000', 0, @now),
      (@property_id, 'UPI', N'UPI', 20, 'upi', @bank, 1, 1, 1, 1, '1010', 0, @now),
      (@property_id, 'CARD', N'Card', 30, 'card', @bank, 1, 1, 1, 0, '1020', 0, @now),
      (@property_id, 'BANK', N'Bank transfer', 40, 'bank', @bank, 1, 1, 0, 1, '1030', 0, @now),
      (@property_id, 'VCREDIT', N'Vendor credit', 50, 'credit', NULL, 0, 0, 0, 1, '2200', 0, @now),
      (@property_id, 'COMPANY', N'Company credit', 60, 'credit', NULL, 0, 0, 0, 0, NULL, 0, @now);
  END
  IF NOT EXISTS (SELECT 1 FROM ops.expense_category WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @property_id)
    INSERT ops.expense_category(property_id, code, name, sort_order, ledger, needs_bill, updated_by, updated_utc) VALUES
      (@property_id, 'SUPPLIES', N'Supplies', 10, '5000', 0, 0, @now),
      (@property_id, 'LAUNDRY', N'Laundry', 20, '5010', 0, 0, @now),
      (@property_id, 'MAINT', N'Maintenance', 30, '5020', 0, 0, @now),
      (@property_id, 'UTIL', N'Utilities', 40, '5030', 0, 0, @now),
      (@property_id, 'PETTY', N'Petty cash', 50, '5040', 0, 0, @now);
  IF NOT EXISTS (SELECT 1 FROM ops.expense_vendor WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @property_id)
    INSERT ops.expense_vendor(property_id, code, name, sort_order, vendor_type, tds, updated_by, updated_utc)
    SELECT @property_id, CONCAT('V-', RIGHT(CONCAT('0000', ROW_NUMBER() OVER (ORDER BY v.name)), 4)), LEFT(v.name, 60), CAST(ROW_NUMBER() OVER (ORDER BY v.name) * 10 AS INT),
           CASE WHEN v.name LIKE N'%linen%' OR v.name LIKE N'%laundry%' OR v.name LIKE N'%dhobi%' THEN 'laundry'
                WHEN v.name LIKE N'%power%' OR v.name LIKE N'%water%' OR v.name LIKE N'%broadband%' OR v.name LIKE N'%internet%' THEN 'utility' ELSE 'supplier' END,
           0, 0, @now
      FROM (SELECT MIN(LTRIM(RTRIM(e.vendor)) COLLATE Latin1_General_BIN) COLLATE DATABASE_DEFAULT AS name -- one row per vendor ignoring case; deterministic spelling
              FROM ops.expense e WHERE e.property_id = @property_id AND LEN(LTRIM(RTRIM(e.vendor))) > 0 GROUP BY LTRIM(RTRIM(e.vendor))) v;
  COMMIT TRANSACTION;
END
GO
DECLARE @p INT = (SELECT MIN(property_id) FROM inv.property);
WHILE @p IS NOT NULL
BEGIN
  EXEC mst.usp_seed_money_masters @p;
  SET @p = (SELECT MIN(property_id) FROM inv.property WHERE property_id > @p);
END
