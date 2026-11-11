-- HCC migration 4931 · CU-NVARCHAR (wave CU): crm tag / note / preference customer_key VARCHAR(80) -> NVARCHAR(80).
-- See 4930 for the reason. Re-runnable (checks the column type first); safe on a database with rows.
-- Dependent objects dropped and recreated with the 2300 names: PK_crm_tag (property_id, customer_key, tag), IX_crm_note_customer, PK_crm_pref.
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('crm.customer_tag') AND name = 'customer_key' AND system_type_id = 167)
BEGIN
  IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_crm_tag' AND parent_object_id = OBJECT_ID('crm.customer_tag')) ALTER TABLE crm.customer_tag DROP CONSTRAINT PK_crm_tag;
  ALTER TABLE crm.customer_tag ALTER COLUMN customer_key NVARCHAR(80) NOT NULL;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_crm_tag' AND parent_object_id = OBJECT_ID('crm.customer_tag'))
  ALTER TABLE crm.customer_tag ADD CONSTRAINT PK_crm_tag PRIMARY KEY (property_id, customer_key, tag);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('crm.customer_note') AND name = 'customer_key' AND system_type_id = 167)
BEGIN
  IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_crm_note_customer' AND object_id = OBJECT_ID('crm.customer_note')) DROP INDEX IX_crm_note_customer ON crm.customer_note;
  ALTER TABLE crm.customer_note ALTER COLUMN customer_key NVARCHAR(80) NOT NULL;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_crm_note_customer' AND object_id = OBJECT_ID('crm.customer_note'))
  CREATE INDEX IX_crm_note_customer ON crm.customer_note(property_id, customer_key);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('crm.customer_pref') AND name = 'customer_key' AND system_type_id = 167)
BEGIN
  IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_crm_pref' AND parent_object_id = OBJECT_ID('crm.customer_pref')) ALTER TABLE crm.customer_pref DROP CONSTRAINT PK_crm_pref;
  ALTER TABLE crm.customer_pref ALTER COLUMN customer_key NVARCHAR(80) NOT NULL;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_crm_pref' AND parent_object_id = OBJECT_ID('crm.customer_pref'))
  ALTER TABLE crm.customer_pref ADD CONSTRAINT PK_crm_pref PRIMARY KEY (property_id, customer_key);
