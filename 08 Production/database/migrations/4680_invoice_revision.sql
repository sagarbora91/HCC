-- HCC migration 4680 · LF A2 invoice revisions (LF-CONTRACT §4). A corrected bill is a NEW tax invoice (next number) for the same source
-- with revision = old + 1; the original row stays immutable. One invoice per (source, revision) replaces one per source.
ALTER TABLE ivc.invoice ADD revision INT NOT NULL CONSTRAINT DF_ivc_invoice_revision DEFAULT 1 CONSTRAINT CK_ivc_invoice_revision CHECK (revision >= 1);
GO
DROP INDEX UX_ivc_invoice_source ON ivc.invoice;
CREATE UNIQUE INDEX UX_ivc_invoice_source_revision ON ivc.invoice(property_id, source_kind, source_id, revision);
