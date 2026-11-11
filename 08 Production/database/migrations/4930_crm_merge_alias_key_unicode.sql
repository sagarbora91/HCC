-- HCC migration 4930 · CU-NVARCHAR (wave CU): crm merge / alias customer keys VARCHAR(80) -> NVARCHAR(80).
-- Why: a customer key is built from the normalised name when there is no usable mobile ('n:' + name). A Devanagari name was stored in a
-- VARCHAR column and collapsed to question marks, so different customers shared tags, notes, aliases and pending merges.
-- Keys already stored stay as they are (a question mark that was written cannot be recovered); new writes keep the full text.
-- Re-runnable: every step checks the current column type / object first, so a second run changes nothing. Safe on a database with rows.
-- Dependent objects dropped and recreated with the same names and definitions as migration 2300:
--   crm.customer_merge: UX_crm_merge_pending (filtered unique), CK_crm_merge_distinct;
--   crm.customer_alias: PK_crm_alias, CK_crm_alias_distinct, IX_crm_alias_target.
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('crm.customer_merge') AND name IN ('source_key','target_key') AND system_type_id = 167)
BEGIN
  IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_crm_merge_pending' AND object_id = OBJECT_ID('crm.customer_merge')) DROP INDEX UX_crm_merge_pending ON crm.customer_merge;
  IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_crm_merge_distinct' AND parent_object_id = OBJECT_ID('crm.customer_merge')) ALTER TABLE crm.customer_merge DROP CONSTRAINT CK_crm_merge_distinct;
  ALTER TABLE crm.customer_merge ALTER COLUMN source_key NVARCHAR(80) NOT NULL;
  ALTER TABLE crm.customer_merge ALTER COLUMN target_key NVARCHAR(80) NOT NULL;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_crm_merge_distinct' AND parent_object_id = OBJECT_ID('crm.customer_merge'))
  ALTER TABLE crm.customer_merge ADD CONSTRAINT CK_crm_merge_distinct CHECK (source_key <> target_key);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_crm_merge_pending' AND object_id = OBJECT_ID('crm.customer_merge'))
  CREATE UNIQUE INDEX UX_crm_merge_pending ON crm.customer_merge(property_id, source_key) WHERE status = 'Pending';
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('crm.customer_alias') AND name IN ('alias_key','target_key') AND system_type_id = 167)
BEGIN
  IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_crm_alias_target' AND object_id = OBJECT_ID('crm.customer_alias')) DROP INDEX IX_crm_alias_target ON crm.customer_alias;
  IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_crm_alias_distinct' AND parent_object_id = OBJECT_ID('crm.customer_alias')) ALTER TABLE crm.customer_alias DROP CONSTRAINT CK_crm_alias_distinct;
  IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_crm_alias' AND parent_object_id = OBJECT_ID('crm.customer_alias')) ALTER TABLE crm.customer_alias DROP CONSTRAINT PK_crm_alias;
  ALTER TABLE crm.customer_alias ALTER COLUMN alias_key NVARCHAR(80) NOT NULL;
  ALTER TABLE crm.customer_alias ALTER COLUMN target_key NVARCHAR(80) NOT NULL;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_crm_alias' AND parent_object_id = OBJECT_ID('crm.customer_alias'))
  ALTER TABLE crm.customer_alias ADD CONSTRAINT PK_crm_alias PRIMARY KEY (property_id, alias_key);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_crm_alias_distinct' AND parent_object_id = OBJECT_ID('crm.customer_alias'))
  ALTER TABLE crm.customer_alias ADD CONSTRAINT CK_crm_alias_distinct CHECK (alias_key <> target_key);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_crm_alias_target' AND object_id = OBJECT_ID('crm.customer_alias'))
  CREATE INDEX IX_crm_alias_target ON crm.customer_alias(property_id, target_key);
