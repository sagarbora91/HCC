-- HCC migration 4740 · F6 hall booking (lane H1): ivc.invoice source_kind 'hall' (the event-day tax invoice, INV series HPE/2627/…, DEC-197) and
-- acc.source source_kind 'hall' (hall:advance / hall:invoice / hall:advance-apply / hall:balance / hall:cancel / hall:refund).
-- Numbered above the F6 range 4300–4349 on purpose: 4687 (LF counter bills) drops and re-creates both constraints, so a widening numbered below
-- it would be undone on a fresh database. Any later migration that re-creates these constraints must keep 'hall'.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ivc_invoice_source' AND parent_object_id = OBJECT_ID('ivc.invoice'))
  ALTER TABLE ivc.invoice DROP CONSTRAINT CK_ivc_invoice_source;
GO
ALTER TABLE ivc.invoice ADD CONSTRAINT CK_ivc_invoice_source CHECK (source_kind IN ('folio','group','pos','counter','hall'));
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_acc_source_kind' AND parent_object_id = OBJECT_ID('acc.source'))
  ALTER TABLE acc.source DROP CONSTRAINT CK_acc_source_kind;
GO
ALTER TABLE acc.source ADD CONSTRAINT CK_acc_source_kind
  CHECK (source_kind IN ('line','receipt','pos','expense','closing','payroll','manual','purchase','vendor','tax','ota','counter','hall'));
