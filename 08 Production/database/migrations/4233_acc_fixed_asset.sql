-- HCC migration 4233 · ACC-2 fixed-asset register (contract §3.4; rulebook C10, §F F1; DEC-138 / DEC-140: HCC keeps the register, the CA
-- passes depreciation in Tally — no depreciation journal here). A row is created when a capital purchase is approved: asset_no
-- FA-<yyyy>-<nnn> (one series per calendar year of the purchase), block ledger 15x0, cost = bill − ITC taken. A reversed purchase marks the
-- row disposed at 0 (never deleted). put_to_use = the date the asset was first used (half-rate rule, F2).
CREATE TABLE acc.fixed_asset(
  fixed_asset_id BIGINT IDENTITY CONSTRAINT PK_acc_fixed_asset PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_acc_fixed_asset_property REFERENCES inv.property,
  asset_no VARCHAR(20) NOT NULL,
  ledger VARCHAR(4) NOT NULL CONSTRAINT CK_acc_fixed_asset_ledger CHECK (ledger LIKE '15[0-9]0'),
  name NVARCHAR(300) NOT NULL,
  cost_paise BIGINT NOT NULL CONSTRAINT CK_acc_fixed_asset_cost CHECK (cost_paise BETWEEN 0 AND 10000000000),
  itc_paise BIGINT NOT NULL CONSTRAINT DF_acc_fixed_asset_itc DEFAULT 0,
  purchase_date DATE NOT NULL,
  vendor NVARCHAR(120) NOT NULL,
  expense_id BIGINT NOT NULL CONSTRAINT FK_acc_fixed_asset_expense REFERENCES ops.expense,
  put_to_use DATE NULL,
  disposed_date DATE NULL,
  disposed_amount_paise BIGINT NULL,
  disposed_voucher VARCHAR(60) NULL,
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  CONSTRAINT UQ_acc_fixed_asset_no UNIQUE (property_id, asset_no),
  CONSTRAINT UQ_acc_fixed_asset_expense UNIQUE (expense_id),
  CONSTRAINT CK_acc_fixed_asset_no CHECK (asset_no LIKE 'FA-[0-9][0-9][0-9][0-9]-[0-9][0-9][0-9]%'),
  CONSTRAINT CK_acc_fixed_asset_disposed CHECK ((disposed_date IS NULL AND disposed_amount_paise IS NULL) OR (disposed_date IS NOT NULL AND disposed_amount_paise IS NOT NULL)));
CREATE INDEX IX_acc_fixed_asset_ledger ON acc.fixed_asset(property_id, ledger);
GO
