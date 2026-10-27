-- HCC migration 4200 · ACC-1 ledger groups (posting contract §1; rulebook §3.1; lane acc1-chart, block 4200–4209).
-- acc.ledger_group   Tally's 28 predefined groups (15 primary, 13 sub-groups) as built-in rows (property_id NULL, is_custom 0, fixed ids
--                    capital … indirect-expenses, never renamed / deleted / re-parented) + each property's Owner custom sub-groups (is_custom 1,
--                    id g-<slug>, parent = any group, nature and side inherited from the parent). nature = normal balance Dr / Cr; side = BS
--                    (balance sheet) / PL (profit & loss). The legacy group (Asset / Liability / Income / Expense / Suspense) is derived from the
--                    primary ancestor in code (Hcc.Domain.Accounting.ChartOfAccounts.LegacyOfPrimary), never stored.
-- The built-in rows mirror ChartOfAccounts.Groups (tests/Hcc.Accounting.Tests Sql/SqlChartStoreTests pins them).
CREATE TABLE acc.ledger_group(
  ledger_group_id INT IDENTITY CONSTRAINT PK_acc_ledger_group PRIMARY KEY,
  property_id INT NULL CONSTRAINT FK_acc_ledger_group_property REFERENCES inv.property,
  group_id VARCHAR(48) NOT NULL,
  name NVARCHAR(60) NOT NULL CONSTRAINT CK_acc_ledger_group_name CHECK (LEN(name) >= 1),
  parent_id VARCHAR(48) NULL,
  nature CHAR(2) NOT NULL CONSTRAINT CK_acc_ledger_group_nature CHECK (nature IN ('Dr', 'Cr')),
  side CHAR(2) NOT NULL CONSTRAINT CK_acc_ledger_group_side CHECK (side IN ('BS', 'PL')),
  is_custom BIT NOT NULL,
  sort_order INT NOT NULL CONSTRAINT DF_acc_ledger_group_sort DEFAULT 0,
  changed_by INT NULL,
  changed_utc DATETIME2(3) NULL,
  CONSTRAINT CK_acc_ledger_group_kind CHECK (
       (is_custom = 0 AND property_id IS NULL)
    OR (is_custom = 1 AND property_id IS NOT NULL AND parent_id IS NOT NULL AND group_id LIKE 'g-%')));
GO
CREATE UNIQUE INDEX UX_acc_ledger_group_builtin ON acc.ledger_group(group_id) WHERE property_id IS NULL;
CREATE UNIQUE INDEX UX_acc_ledger_group_custom ON acc.ledger_group(property_id, group_id) WHERE property_id IS NOT NULL;
GO
INSERT acc.ledger_group(group_id, name, parent_id, nature, side, is_custom, sort_order) SELECT g, n, p, na, si, 0, so FROM (VALUES
  ('capital', N'Capital Account', NULL, 'Cr', 'BS', 1),
  ('reserves', N'Reserves & Surplus', 'capital', 'Cr', 'BS', 2),
  ('current-assets', N'Current Assets', NULL, 'Dr', 'BS', 3),
  ('bank', N'Bank Accounts', 'current-assets', 'Dr', 'BS', 4),
  ('cash', N'Cash-in-Hand', 'current-assets', 'Dr', 'BS', 5),
  ('deposits-asset', N'Deposits (Asset)', 'current-assets', 'Dr', 'BS', 6),
  ('loans-advances', N'Loans & Advances (Asset)', 'current-assets', 'Dr', 'BS', 7),
  ('stock', N'Stock-in-Hand', 'current-assets', 'Dr', 'BS', 8),
  ('debtors', N'Sundry Debtors', 'current-assets', 'Dr', 'BS', 9),
  ('current-liabilities', N'Current Liabilities', NULL, 'Cr', 'BS', 10),
  ('duties-taxes', N'Duties & Taxes', 'current-liabilities', 'Cr', 'BS', 11),
  ('provisions', N'Provisions', 'current-liabilities', 'Cr', 'BS', 12),
  ('creditors', N'Sundry Creditors', 'current-liabilities', 'Cr', 'BS', 13),
  ('fixed-assets', N'Fixed Assets', NULL, 'Dr', 'BS', 14),
  ('investments', N'Investments', NULL, 'Dr', 'BS', 15),
  ('loans', N'Loans (Liability)', NULL, 'Cr', 'BS', 16),
  ('bank-od', N'Bank OD A/c', 'loans', 'Cr', 'BS', 17),
  ('secured-loans', N'Secured Loans', 'loans', 'Cr', 'BS', 18),
  ('unsecured-loans', N'Unsecured Loans', 'loans', 'Cr', 'BS', 19),
  ('suspense', N'Suspense A/c', NULL, 'Dr', 'BS', 20),
  ('misc-expenses-asset', N'Misc. Expenses (Asset)', NULL, 'Dr', 'BS', 21),
  ('branches', N'Branch / Divisions', NULL, 'Dr', 'BS', 22),
  ('sales', N'Sales Accounts', NULL, 'Cr', 'PL', 23),
  ('purchases', N'Purchase Accounts', NULL, 'Dr', 'PL', 24),
  ('direct-income', N'Direct Incomes', NULL, 'Cr', 'PL', 25),
  ('direct-expenses', N'Direct Expenses', NULL, 'Dr', 'PL', 26),
  ('indirect-income', N'Indirect Incomes', NULL, 'Cr', 'PL', 27),
  ('indirect-expenses', N'Indirect Expenses', NULL, 'Dr', 'PL', 28)
) v(g, n, p, na, si, so);
GO
