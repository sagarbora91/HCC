-- HCC migration 4932 · CU-NVARCHAR (wave CU): legacy-history customer_key VARCHAR(80) -> NVARCHAR(80) in lgc.guest_map / stay / bill / receipt.
-- lgc keys are the same CustomerRules key as crm.* (LG contract section 1) and are joined to it in lgc.v_customer_stay and Customers 360, so they
-- must compare equal for a Devanagari name. See 4930 for the reason. Re-runnable (checks the column type first); safe on a database with rows.
-- Indexes dropped and recreated with the 4600 names: IX_lgc_guest_map_key, IX_lgc_stay_customer, IX_lgc_bill_customer, IX_lgc_receipt_customer.
-- lgc.v_customer_stay (not schema-bound) is refreshed at the end.
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('lgc.guest_map') AND name = 'customer_key' AND system_type_id = 167)
BEGIN
  IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_lgc_guest_map_key' AND object_id = OBJECT_ID('lgc.guest_map')) DROP INDEX IX_lgc_guest_map_key ON lgc.guest_map;
  ALTER TABLE lgc.guest_map ALTER COLUMN customer_key NVARCHAR(80) NOT NULL;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_lgc_guest_map_key' AND object_id = OBJECT_ID('lgc.guest_map'))
  CREATE INDEX IX_lgc_guest_map_key ON lgc.guest_map(property_id, customer_key);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('lgc.stay') AND name = 'customer_key' AND system_type_id = 167)
BEGIN
  IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_lgc_stay_customer' AND object_id = OBJECT_ID('lgc.stay')) DROP INDEX IX_lgc_stay_customer ON lgc.stay;
  ALTER TABLE lgc.stay ALTER COLUMN customer_key NVARCHAR(80) NOT NULL;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_lgc_stay_customer' AND object_id = OBJECT_ID('lgc.stay'))
  CREATE INDEX IX_lgc_stay_customer ON lgc.stay(property_id, customer_key, arrival_date DESC);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('lgc.bill') AND name = 'customer_key' AND system_type_id = 167)
BEGIN
  IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_lgc_bill_customer' AND object_id = OBJECT_ID('lgc.bill')) DROP INDEX IX_lgc_bill_customer ON lgc.bill;
  ALTER TABLE lgc.bill ALTER COLUMN customer_key NVARCHAR(80) NULL;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_lgc_bill_customer' AND object_id = OBJECT_ID('lgc.bill'))
  CREATE INDEX IX_lgc_bill_customer ON lgc.bill(property_id, customer_key, bill_date DESC);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('lgc.receipt') AND name = 'customer_key' AND system_type_id = 167)
BEGIN
  IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_lgc_receipt_customer' AND object_id = OBJECT_ID('lgc.receipt')) DROP INDEX IX_lgc_receipt_customer ON lgc.receipt;
  ALTER TABLE lgc.receipt ALTER COLUMN customer_key NVARCHAR(80) NULL;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_lgc_receipt_customer' AND object_id = OBJECT_ID('lgc.receipt'))
  CREATE INDEX IX_lgc_receipt_customer ON lgc.receipt(property_id, customer_key, received_date DESC);
GO
EXEC sys.sp_refreshview N'lgc.v_customer_stay';
