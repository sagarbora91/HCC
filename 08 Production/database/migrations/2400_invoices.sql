-- HCC migration 2400 · W3-6 tax invoices and credit notes (block 2400–2499). Original HCC design (port of the prototype stream C sample
-- invoice / credit note: 03 Prototype/src/services/mock/handlers/folio.ts CHECKOUT, CREDIT_NOTE, APPROVAL; no donor code).
-- Numbering: one gap-free series per property, financial year (Apr–Mar) and document type, allocated in the issuing transaction under
-- a row lock (a rolled-back issue releases its number). Invoice numbers are at most 16 characters (CGST Rules r.46(b)).
-- Issued documents (invoice, invoice lines, credit note, credit-note lines) are immutable: triggers refuse UPDATE and DELETE.
-- Posted folio lines keep their tax snapshot (DEC-069): invoice lines copy bill.charge tax_bps / tax_paise and tax.charge_gst CGST/SGST.
-- Money = BIGINT paise; dates DATE; event times DATETIME2(3) UTC written by the application.
CREATE SCHEMA ivc;
GO
-- Series per (property, FY, type). doc_type INV tax invoice · BOS bill of supply · CRN credit note. fy_code '2627' = FY 2026-27.
-- The prefix is frozen when the FY's series is created (Master configuration invoicePrefix for INV).
CREATE TABLE ivc.series(
  property_id INT NOT NULL CONSTRAINT FK_ivc_series_property REFERENCES inv.property,
  fy_code CHAR(4) NOT NULL CONSTRAINT CK_ivc_series_fy CHECK (fy_code NOT LIKE '%[^0-9]%'),
  doc_type CHAR(3) NOT NULL CONSTRAINT CK_ivc_series_type CHECK (doc_type IN ('INV','BOS','CRN')),
  prefix VARCHAR(6) NOT NULL CONSTRAINT CK_ivc_series_prefix CHECK (LEN(prefix) BETWEEN 1 AND 6 AND prefix NOT LIKE '%[^A-Z0-9-]%'),
  last_seq INT NOT NULL CONSTRAINT CK_ivc_series_seq CHECK (last_seq >= 0),
  created_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_ivc_series PRIMARY KEY (property_id, fy_code, doc_type));
GO
CREATE TABLE ivc.invoice(
  invoice_id BIGINT IDENTITY CONSTRAINT PK_ivc_invoice PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_ivc_invoice_property REFERENCES inv.property,
  doc_type CHAR(3) NOT NULL CONSTRAINT CK_ivc_invoice_type CHECK (doc_type IN ('INV','BOS')),
  invoice_no VARCHAR(16) NOT NULL,
  fy_code CHAR(4) NOT NULL,
  seq INT NOT NULL CONSTRAINT CK_ivc_invoice_seq CHECK (seq > 0),
  invoice_date DATE NOT NULL,
  source_kind VARCHAR(10) NOT NULL CONSTRAINT CK_ivc_invoice_source CHECK (source_kind IN ('folio','group')),
  source_id BIGINT NOT NULL,
  source_ref NVARCHAR(40) NOT NULL,
  period_from DATE NULL,
  period_to DATE NULL,
  supply_type VARCHAR(3) NOT NULL CONSTRAINT CK_ivc_invoice_supply CHECK (supply_type IN ('B2C','B2B')),
  buyer_name NVARCHAR(120) NULL,
  buyer_gstin CHAR(15) NULL,
  buyer_address NVARCHAR(200) NULL,
  place_of_supply CHAR(2) NOT NULL,
  supplier_name NVARCHAR(120) NOT NULL,
  supplier_gstin CHAR(15) NOT NULL,
  supplier_state CHAR(2) NOT NULL,
  supplier_address NVARCHAR(200) NULL,
  inter_state BIT NOT NULL,
  taxable_paise BIGINT NOT NULL,
  cgst_paise BIGINT NOT NULL,
  sgst_paise BIGINT NOT NULL,
  igst_paise BIGINT NOT NULL,
  total_paise BIGINT NOT NULL,
  amount_words NVARCHAR(300) NOT NULL,
  issued_by INT NOT NULL,
  issued_utc DATETIME2(3) NOT NULL,
  CONSTRAINT UQ_ivc_invoice_no UNIQUE (property_id, invoice_no),
  CONSTRAINT UQ_ivc_invoice_seq UNIQUE (property_id, fy_code, doc_type, seq),
  CONSTRAINT FK_ivc_invoice_series FOREIGN KEY (property_id, fy_code, doc_type) REFERENCES ivc.series(property_id, fy_code, doc_type),
  CONSTRAINT CK_ivc_invoice_b2b CHECK ((supply_type = 'B2B' AND buyer_gstin IS NOT NULL AND buyer_name IS NOT NULL) OR (supply_type = 'B2C' AND buyer_gstin IS NULL)),
  CONSTRAINT CK_ivc_invoice_tax CHECK ((inter_state = 1 AND cgst_paise = 0 AND sgst_paise = 0) OR (inter_state = 0 AND igst_paise = 0)),
  CONSTRAINT CK_ivc_invoice_total CHECK (total_paise = taxable_paise + cgst_paise + sgst_paise + igst_paise));
-- One tax invoice (or bill of supply) per source document (prototype: number allocated once per stay).
CREATE UNIQUE INDEX UX_ivc_invoice_source ON ivc.invoice(property_id, source_kind, source_id);
CREATE INDEX IX_ivc_invoice_date ON ivc.invoice(property_id, invoice_date) INCLUDE (invoice_no, doc_type, supply_type, total_paise);
GO
CREATE TABLE ivc.invoice_line(
  invoice_id BIGINT NOT NULL CONSTRAINT FK_ivc_line_invoice REFERENCES ivc.invoice,
  line_no INT NOT NULL CONSTRAINT CK_ivc_line_no CHECK (line_no > 0),
  source_line_id BIGINT NULL,           -- bill.charge id for a folio invoice
  kind VARCHAR(12) NOT NULL,
  service_date DATE NOT NULL,
  description NVARCHAR(200) NOT NULL,
  category VARCHAR(8) NOT NULL CONSTRAINT CK_ivc_line_category CHECK (category IN ('room','food','laundry','banquet','other')),
  sac CHAR(6) NOT NULL,
  taxable_paise BIGINT NOT NULL,
  tax_bps INT NOT NULL CONSTRAINT CK_ivc_line_bps CHECK (tax_bps BETWEEN 0 AND 10000),
  tax_paise BIGINT NOT NULL,
  cgst_paise BIGINT NOT NULL,
  sgst_paise BIGINT NOT NULL,
  igst_paise BIGINT NOT NULL,
  CONSTRAINT PK_ivc_invoice_line PRIMARY KEY (invoice_id, line_no),
  CONSTRAINT CK_ivc_line_split CHECK (cgst_paise + sgst_paise + igst_paise = tax_paise));
GO
-- Credit-note requests (DEC-063 G3: Owner approves; the requester never decides; 24-hour expiry as every folio approval).
-- direct = 1: issued by an Owner without a request (RBAC v2 D7: the Owner holds the approval permission and needs no request).
CREATE TABLE ivc.credit_note_request(
  request_id BIGINT IDENTITY CONSTRAINT PK_ivc_cn_request PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_ivc_cn_request_property REFERENCES inv.property,
  invoice_id BIGINT NOT NULL CONSTRAINT FK_ivc_cn_request_invoice REFERENCES ivc.invoice,
  mode VARCHAR(6) NOT NULL CONSTRAINT CK_ivc_cn_request_mode CHECK (mode IN ('full','amount','lines')),
  amount_paise BIGINT NULL,             -- taxable amount for mode = amount
  lines_json NVARCHAR(2000) NULL CONSTRAINT CK_ivc_cn_request_lines CHECK (lines_json IS NULL OR ISJSON(lines_json) = 1),
  reason NVARCHAR(200) NOT NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_ivc_cn_request_status CHECK (status IN ('Pending','Issued','Rejected','Expired')),
  direct BIT NOT NULL,
  requested_by INT NOT NULL,
  requested_utc DATETIME2(3) NOT NULL,
  expires_utc DATETIME2(3) NOT NULL,
  decided_by INT NULL,
  decided_utc DATETIME2(3) NULL,
  decision_reason NVARCHAR(200) NULL,
  credit_note_id BIGINT NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_ivc_cn_request_sod CHECK (decided_by IS NULL OR decided_by <> requested_by OR direct = 1),
  CONSTRAINT CK_ivc_cn_request_amount CHECK ((mode = 'amount' AND amount_paise > 0) OR (mode <> 'amount' AND amount_paise IS NULL)));
-- At most one pending request per invoice (prototype: "A credit-note request is already pending").
CREATE UNIQUE INDEX UX_ivc_cn_request_pending ON ivc.credit_note_request(invoice_id) WHERE status = 'Pending';
CREATE INDEX IX_ivc_cn_request_status ON ivc.credit_note_request(property_id, status);
GO
CREATE TABLE ivc.credit_note(
  credit_note_id BIGINT IDENTITY CONSTRAINT PK_ivc_credit_note PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_ivc_cn_property REFERENCES inv.property,
  credit_note_no VARCHAR(16) NOT NULL,
  fy_code CHAR(4) NOT NULL,
  seq INT NOT NULL CONSTRAINT CK_ivc_cn_seq CHECK (seq > 0),
  doc_type CHAR(3) NOT NULL CONSTRAINT DF_ivc_cn_type DEFAULT 'CRN' CONSTRAINT CK_ivc_cn_type CHECK (doc_type = 'CRN'),
  note_date DATE NOT NULL,
  invoice_id BIGINT NOT NULL CONSTRAINT FK_ivc_cn_invoice REFERENCES ivc.invoice,
  request_id BIGINT NOT NULL CONSTRAINT FK_ivc_cn_request REFERENCES ivc.credit_note_request,
  mode VARCHAR(6) NOT NULL,
  reason NVARCHAR(200) NOT NULL,
  taxable_paise BIGINT NOT NULL,
  cgst_paise BIGINT NOT NULL,
  sgst_paise BIGINT NOT NULL,
  igst_paise BIGINT NOT NULL,
  total_paise BIGINT NOT NULL,
  amount_words NVARCHAR(300) NOT NULL,
  issued_by INT NOT NULL,
  approved_by INT NOT NULL,
  issued_utc DATETIME2(3) NOT NULL,
  CONSTRAINT UQ_ivc_cn_no UNIQUE (property_id, credit_note_no),
  CONSTRAINT UQ_ivc_cn_seq UNIQUE (property_id, fy_code, seq),
  CONSTRAINT UQ_ivc_cn_request UNIQUE (request_id),
  CONSTRAINT FK_ivc_cn_series FOREIGN KEY (property_id, fy_code, doc_type) REFERENCES ivc.series(property_id, fy_code, doc_type),
  CONSTRAINT CK_ivc_cn_total CHECK (total_paise = taxable_paise + cgst_paise + sgst_paise + igst_paise));
CREATE INDEX IX_ivc_cn_invoice ON ivc.credit_note(invoice_id);
CREATE INDEX IX_ivc_cn_date ON ivc.credit_note(property_id, note_date);
GO
-- Credited amounts are positive (they reduce the invoice line); the matching folio line (folio_charge_id) is the negative posting.
CREATE TABLE ivc.credit_note_line(
  credit_note_id BIGINT NOT NULL CONSTRAINT FK_ivc_cn_line_note REFERENCES ivc.credit_note,
  line_no INT NOT NULL CONSTRAINT CK_ivc_cn_line_no CHECK (line_no > 0),
  invoice_id BIGINT NOT NULL,
  invoice_line_no INT NOT NULL,
  category VARCHAR(8) NOT NULL,
  sac CHAR(6) NOT NULL,
  taxable_paise BIGINT NOT NULL,
  tax_bps INT NOT NULL,
  tax_paise BIGINT NOT NULL,
  cgst_paise BIGINT NOT NULL,
  sgst_paise BIGINT NOT NULL,
  igst_paise BIGINT NOT NULL,
  folio_charge_id BIGINT NULL CONSTRAINT FK_ivc_cn_line_charge REFERENCES bill.charge,
  CONSTRAINT PK_ivc_credit_note_line PRIMARY KEY (credit_note_id, line_no),
  CONSTRAINT FK_ivc_cn_line_invoice_line FOREIGN KEY (invoice_id, invoice_line_no) REFERENCES ivc.invoice_line(invoice_id, line_no),
  CONSTRAINT CK_ivc_cn_line_split CHECK (cgst_paise + sgst_paise + igst_paise = tax_paise));
CREATE INDEX IX_ivc_cn_line_invoice ON ivc.credit_note_line(invoice_id, invoice_line_no) INCLUDE (taxable_paise, tax_paise);
GO
-- Immutability: an issued document is never edited or deleted (corrections are credit notes).
CREATE TRIGGER ivc.TR_invoice_immutable ON ivc.invoice AFTER UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 52400, 'An issued invoice is immutable: it cannot be changed or deleted. Issue a credit note.', 1;
END;
GO
CREATE TRIGGER ivc.TR_invoice_line_immutable ON ivc.invoice_line AFTER UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 52401, 'Invoice lines are immutable once issued.', 1;
END;
GO
CREATE TRIGGER ivc.TR_credit_note_immutable ON ivc.credit_note AFTER UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 52402, 'An issued credit note is immutable: it cannot be changed or deleted.', 1;
END;
GO
CREATE TRIGGER ivc.TR_credit_note_line_immutable ON ivc.credit_note_line AFTER UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 52403, 'Credit-note lines are immutable once issued.', 1;
END;
GO
-- A series only moves forward by one (gap-free) and is never deleted.
CREATE TRIGGER ivc.TR_series_forward ON ivc.series AFTER UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted) THROW 52404, 'A numbering series cannot be deleted.', 1;
  IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.property_id = i.property_id AND d.fy_code = i.fy_code AND d.doc_type = i.doc_type
             WHERE i.last_seq <> d.last_seq + 1 OR i.prefix <> d.prefix)
    THROW 52405, 'A numbering series only advances by one; its prefix is fixed for the financial year.', 1;
END;
