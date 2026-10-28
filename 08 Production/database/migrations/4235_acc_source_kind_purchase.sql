-- HCC migration 4235 · ACC-2 source kinds (posting contract §1): acc.source.source_kind widened to SourceKinds.All — line, receipt, pos,
-- expense, closing, payroll + manual, purchase, vendor, tax, ota — so the purchase lane's vendor payments ('vendor') and reverse-charge GST
-- ('tax', sub 'rcm') can be stored. acc2-manual's 4238 widens the same constraint to the same list (drop + add by name; order-independent).
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_acc_source_kind' AND parent_object_id = OBJECT_ID('acc.source'))
  ALTER TABLE acc.source DROP CONSTRAINT CK_acc_source_kind;
GO
ALTER TABLE acc.source ADD CONSTRAINT CK_acc_source_kind CHECK (source_kind IN ('line','receipt','pos','expense','closing','payroll','manual','purchase','vendor','tax','ota'));
GO
