-- HCC migration 4201 · ACC-1 ledgers and chart history (posting contract §2; rulebook §3.2, §3.4; lane acc1-chart).
-- acc.ledger          the built-in (system) ledgers of the contract §2 table as catalogue rows (property_id NULL; code, name, Tally group,
--                     control flag, GST flag, note) + each property's Owner rows: an OVERRIDE of a built-in (is_system 1: name, group within
--                     the same Tally nature, GST flag, Tally name, note, active — code and control never change) or an OWNER-ADDED ledger
--                     (is_system 0, an unused 4-digit code, never a control). The effective chart = built-ins overlaid with the property's rows
--                     (Hcc.Domain.Accounting.ChartOfAccounts.Merge); a built-in edited back to its defaults has no property row.
--                     Retiring (active 0) is refused in code while a mapping key points at the ledger or it or one of its parties
--                     (<code>/<party>) has a balance; 9999 Suspense and 1400 Opening-balance difference never retire.
-- acc.ledger_change   one row per changed field of a ledger (kind 'ledger', code) or custom group (kind 'group', group id); never updated.
-- The catalogue rows mirror ChartOfAccounts.All (tests/Hcc.Accounting.Tests Sql/SqlChartStoreTests pins them).
CREATE TABLE acc.ledger(
  ledger_id INT IDENTITY CONSTRAINT PK_acc_ledger PRIMARY KEY,
  property_id INT NULL CONSTRAINT FK_acc_ledger_property REFERENCES inv.property,
  code VARCHAR(4) NOT NULL CONSTRAINT CK_acc_ledger_code CHECK (code LIKE '[0-9][0-9][0-9][0-9]'),
  name NVARCHAR(60) NOT NULL CONSTRAINT CK_acc_ledger_name CHECK (LEN(name) >= 1),
  group_id VARCHAR(48) NOT NULL,
  is_system BIT NOT NULL,
  is_control BIT NOT NULL CONSTRAINT DF_acc_ledger_control DEFAULT 0,
  gst VARCHAR(6) NOT NULL CONSTRAINT DF_acc_ledger_gst DEFAULT 'none' CONSTRAINT CK_acc_ledger_gst CHECK (gst IN ('none', 'input', 'output')),
  tally_name NVARCHAR(60) NULL,
  active BIT NOT NULL CONSTRAINT DF_acc_ledger_active DEFAULT 1,
  note NVARCHAR(200) NULL,
  changed_by INT NULL,
  changed_utc DATETIME2(3) NULL,
  CONSTRAINT CK_acc_ledger_catalogue CHECK (property_id IS NOT NULL OR (is_system = 1 AND active = 1 AND tally_name IS NULL)),
  CONSTRAINT CK_acc_ledger_owner_no_control CHECK (is_system = 1 OR is_control = 0));
GO
CREATE UNIQUE INDEX UX_acc_ledger_builtin ON acc.ledger(code) WHERE property_id IS NULL;
CREATE UNIQUE INDEX UX_acc_ledger_property ON acc.ledger(property_id, code) WHERE property_id IS NOT NULL;
GO
CREATE TABLE acc.ledger_change(
  change_id BIGINT IDENTITY CONSTRAINT PK_acc_ledger_change PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_acc_ledger_change_property REFERENCES inv.property,
  kind VARCHAR(6) NOT NULL CONSTRAINT CK_acc_ledger_change_kind CHECK (kind IN ('ledger', 'group')),
  code VARCHAR(48) NOT NULL,
  field VARCHAR(20) NOT NULL,
  before_value NVARCHAR(300) NOT NULL,
  after_value NVARCHAR(300) NOT NULL,
  actor INT NOT NULL,
  at_utc DATETIME2(3) NOT NULL);
CREATE INDEX IX_acc_ledger_change ON acc.ledger_change(property_id, change_id);
GO
INSERT acc.ledger(code, name, group_id, is_system, is_control, gst, note) SELECT c, n, g, 1, k, t, x FROM (VALUES
  ('1000', N'Cash in hand', 'cash', 1, 'none', N'Front-desk drawer; one party per till (1000/<till code>)'),
  ('1005', N'Petty cash float', 'cash', 0, 'none', N'SCC petty-cash float (expenses wave)'),
  ('1010', N'UPI clearing', 'bank', 0, 'none', N'Settles to bank (simulated)'),
  ('1020', N'Card clearing', 'bank', 0, 'none', N'Settles to bank net of MDR (simulated)'),
  ('1030', N'Bank accounts', 'bank', 1, 'none', N'One party per row of the Bank accounts master (kind bank)'),
  ('1100', N'Guest ledger (in-house folios)', 'debtors', 0, 'none', N'Open folio balances; zero after check-out'),
  ('1110', N'Company receivables (city ledger)', 'debtors', 1, 'none', N'Authorised company credit; one party per company account'),
  ('1115', N'OTA receivables', 'debtors', 1, 'none', N'One party per OTA channel (ACC-2 posts it)'),
  ('1120', N'Udhaar receivables (customer credit)', 'debtors', 1, 'none', N'DEC-130 / DEC-144: one party per Udhaar customer'),
  ('1200', N'Staff advances', 'loans-advances', 1, 'none', N'Salary advances recovered through payroll; one party per staff member'),
  ('1210', N'Recoverable from leavers (F&F)', 'loans-advances', 0, 'none', NULL),
  ('1300', N'GST input credit (legacy, locked snapshots only)', 'duties-taxes', 0, 'input', N'DEC-135 one-ledger ITC before ACC-1; new ITC goes to 1310 / 1320'),
  ('1310', N'Input CGST', 'duties-taxes', 0, 'input', N'ITC marked allowed, CGST half'),
  ('1320', N'Input SGST', 'duties-taxes', 0, 'input', N'ITC marked allowed, SGST half'),
  ('1330', N'Input IGST', 'duties-taxes', 0, 'input', N'Bills from vendors outside Maharashtra (ACC-2)'),
  ('1340', N'GST electronic cash ledger', 'duties-taxes', 0, 'none', N'Challan deposits with the government'),
  ('1350', N'GST paid on advances (adjustable)', 'duties-taxes', 0, 'none', NULL),
  ('1360', N'TDS receivable', 'loans-advances', 0, 'none', N'TDS deducted by companies / OTAs'),
  ('1370', N'Prepaid expenses', 'loans-advances', 0, 'none', NULL),
  ('1400', N'Opening-balance difference', 'suspense', 0, 'none', N'DEC-139: parks an unbalanced opening entry; must be zero before the first period lock'),
  ('1500', N'Buildings', 'fixed-assets', 0, 'none', NULL),
  ('1510', N'Furniture, fittings & electrical fittings', 'fixed-assets', 0, 'none', NULL),
  ('1520', N'Plant & machinery', 'fixed-assets', 0, 'none', NULL),
  ('1530', N'Computers, software & POS hardware', 'fixed-assets', 0, 'none', NULL),
  ('1540', N'Vehicles', 'fixed-assets', 0, 'none', NULL),
  ('2000', N'Advance deposits (guest and group)', 'current-liabilities', 0, 'none', N'Refundable deposits held; never income'),
  ('2100', N'Synthetic tax payable (legacy)', 'duties-taxes', 0, 'output', N'Before DEC-069 only — kept for locked snapshots'),
  ('2110', N'Output CGST', 'duties-taxes', 0, 'output', N'GST master, intra-state half (illustrative — CA to confirm)'),
  ('2120', N'Output SGST', 'duties-taxes', 0, 'output', N'GST master, intra-state half (illustrative — CA to confirm)'),
  ('2130', N'Output IGST', 'duties-taxes', 0, 'output', N'Normally zero (DEC-136 refuses inter-state supply)'),
  ('2140', N'GST payable under reverse charge', 'duties-taxes', 0, 'output', NULL),
  ('2150', N'TDS payable', 'duties-taxes', 0, 'none', NULL),
  ('2200', N'Vendor payables', 'creditors', 1, 'none', N'Vendor-credit expenses; one party per expense vendor'),
  ('2300', N'Salaries payable (net)', 'current-liabilities', 0, 'none', N'Net pay due to staff'),
  ('2310', N'PF payable', 'duties-taxes', 0, 'none', N'Employee + employer PF (illustrative — CA to confirm)'),
  ('2320', N'ESIC payable', 'duties-taxes', 0, 'none', N'Employee + employer ESIC (illustrative — CA to confirm)'),
  ('2330', N'Professional tax payable', 'duties-taxes', 0, 'none', NULL),
  ('2340', N'Other payroll deductions', 'current-liabilities', 0, 'none', NULL),
  ('2400', N'Bank loans (secured)', 'secured-loans', 1, 'none', N'One party per loan (Owner-added loan rows, ACC-2)'),
  ('2410', N'Unsecured loans', 'unsecured-loans', 0, 'none', NULL),
  ('3000', N'Owner''s drawings', 'capital', 0, 'none', N'Capital account (contra): cash the Owner takes from the drawer (DEC-073) — CA to confirm'),
  ('3100', N'Owner''s capital', 'capital', 0, 'none', N'Opening capital + profit − drawings'),
  ('4000', N'Room revenue', 'sales', 0, 'none', NULL),
  ('4100', N'Other services revenue', 'sales', 0, 'none', N'Laundry, extras and other services'),
  ('4200', N'Food & beverage revenue', 'sales', 0, 'none', N'Outlet (POS) bills'),
  ('4300', N'Cancellation, no-show & attrition charges', 'sales', 0, 'none', NULL),
  ('4400', N'Banquet & events revenue', 'sales', 0, 'none', NULL),
  ('4900', N'Discounts allowed', 'sales', 0, 'none', N'Contra revenue'),
  ('4910', N'Refunds, reversals & credit notes', 'sales', 0, 'none', N'Contra revenue (folio adjustments)'),
  ('4950', N'Cash over', 'indirect-income', 0, 'none', N'Accepted drawer overs (shorts go to 5900)'),
  ('4960', N'Interest & other income', 'indirect-income', 0, 'none', NULL),
  ('5000', N'Housekeeping & guest supplies', 'direct-expenses', 0, 'none', NULL),
  ('5010', N'Linen & laundry', 'direct-expenses', 0, 'none', NULL),
  ('5015', N'Kitchen provisions & beverages', 'direct-expenses', 0, 'none', NULL),
  ('5020', N'Repairs & maintenance', 'indirect-expenses', 0, 'none', NULL),
  ('5030', N'Electricity, water & fuel', 'direct-expenses', 0, 'none', NULL),
  ('5040', N'Petty cash & sundry', 'indirect-expenses', 0, 'none', NULL),
  ('5050', N'General & administrative expenses', 'indirect-expenses', 0, 'none', NULL),
  ('5060', N'Rent & licence fees', 'indirect-expenses', 0, 'none', NULL),
  ('5070', N'OTA commission', 'indirect-expenses', 0, 'none', NULL),
  ('5080', N'Bank & card charges', 'indirect-expenses', 0, 'none', NULL),
  ('5090', N'Interest on loans', 'indirect-expenses', 0, 'none', NULL),
  ('5100', N'Salaries & wages', 'indirect-expenses', 0, 'none', NULL),
  ('5110', N'Employer PF & ESIC', 'indirect-expenses', 0, 'none', NULL),
  ('5120', N'Staff welfare', 'indirect-expenses', 0, 'none', NULL),
  ('5200', N'Depreciation', 'indirect-expenses', 0, 'none', NULL),
  ('5300', N'Bad debts written off', 'indirect-expenses', 0, 'none', NULL),
  ('5400', N'GST / TDS interest & late fees', 'indirect-expenses', 0, 'none', NULL),
  ('5900', N'Cash short', 'indirect-expenses', 0, 'none', N'Accepted drawer shorts (overs go to 4950)'),
  ('9999', N'Suspense (unmapped)', 'suspense', 0, 'none', N'Never a mapping target')
) v(c, n, g, k, t, x);
GO
