-- HCC migration 4687 · LF A7 counter bill (LF-CONTRACT §6): a counter bill is a tax invoice of the INV series with source_kind 'counter'
-- (source_id = ctr.bill.bill_id); its accounting source kind is 'counter' (SourceRules.Counter / CounterCredit).
ALTER TABLE ivc.invoice DROP CONSTRAINT CK_ivc_invoice_source;
GO
ALTER TABLE ivc.invoice ADD CONSTRAINT CK_ivc_invoice_source CHECK (source_kind IN ('folio','group','pos','counter'));
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_acc_source_kind' AND parent_object_id = OBJECT_ID('acc.source'))
  ALTER TABLE acc.source DROP CONSTRAINT CK_acc_source_kind;
GO
ALTER TABLE acc.source ADD CONSTRAINT CK_acc_source_kind
  CHECK (source_kind IN ('line','receipt','pos','expense','closing','payroll','manual','purchase','vendor','tax','ota','counter'));
