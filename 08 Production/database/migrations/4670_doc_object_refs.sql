-- HCC migration 4670 · LF-docdb (02 Planning/agents/LF-CONTRACT.md §1.4; DEC-200): the four image / file content tables gain
-- object_ref, the key of the content in the separate HccDocuments database. The content columns become nullable so the move job
-- (Hcc.Api.exe migrate) can clear them row by row. A row with object_ref NULL still reads its old column (both paths are tested)
-- until the W9 migration 4719 drops the emptied columns. Metadata (who, when, type, size, status) stays here.
-- Deviation from the contract text: the refs sit on the content tables themselves (doc.id_capture_image, ops.expense_bill_photo,
-- pay.banking_slip, cmp.evidence_content), because that is where the content lives in this schema.
ALTER TABLE doc.id_capture_image ADD object_ref UNIQUEIDENTIFIER NULL;
GO
ALTER TABLE doc.id_capture_image ALTER COLUMN sealed VARCHAR(MAX) NULL;
GO
ALTER TABLE ops.expense_bill_photo ADD object_ref UNIQUEIDENTIFIER NULL;
GO
ALTER TABLE ops.expense_bill_photo ALTER COLUMN image VARBINARY(MAX) NULL;
GO
ALTER TABLE pay.banking_slip ADD object_ref UNIQUEIDENTIFIER NULL;
GO
ALTER TABLE pay.banking_slip ALTER COLUMN image VARBINARY(MAX) NULL;
GO
ALTER TABLE cmp.evidence_content ADD object_ref UNIQUEIDENTIFIER NULL;
GO
ALTER TABLE cmp.evidence_content ALTER COLUMN content VARBINARY(MAX) NULL;
GO
CREATE INDEX IX_id_capture_image_ref ON doc.id_capture_image(object_ref) WHERE object_ref IS NOT NULL;
CREATE INDEX IX_expense_bill_photo_ref ON ops.expense_bill_photo(object_ref) WHERE object_ref IS NOT NULL;
CREATE INDEX IX_banking_slip_ref ON pay.banking_slip(object_ref) WHERE object_ref IS NOT NULL;
CREATE INDEX IX_evidence_content_ref ON cmp.evidence_content(object_ref) WHERE object_ref IS NOT NULL;
