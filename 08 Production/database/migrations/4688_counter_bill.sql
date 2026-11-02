-- HCC migration 4688 · LF A7 counter bill (LF-CONTRACT §6; legacy "Direct Bill", F11). A walk-in / non-folio bill: item lines (extras
-- catalogue, POS menu or free text with a GST category), GST per line at the dated master (CGST + SGST half away from zero per line), an
-- optional discount within the DEC-063 role limits, one tax invoice (INV) at issue, settled paid / on Udhaar credit / on company credit.
-- A void is a credit note (approval 'counter-credit', Owner) — status 'Credited'; rows are otherwise never changed.
CREATE SCHEMA ctr;
GO
CREATE TABLE ctr.bill(
  bill_id BIGINT IDENTITY CONSTRAINT PK_ctr_bill PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_ctr_bill_property REFERENCES inv.property,
  invoice_id BIGINT NULL CONSTRAINT FK_ctr_bill_invoice REFERENCES ivc.invoice,
  invoice_no VARCHAR(16) NOT NULL,
  business_date DATE NOT NULL,
  issued_utc DATETIME2(3) NOT NULL,
  issued_by INT NOT NULL,
  party_name NVARCHAR(80) NOT NULL,
  party_phone VARCHAR(20) NULL,
  party_gstin CHAR(15) NULL,
  party_address NVARCHAR(200) NULL,
  guest_id BIGINT NULL CONSTRAINT FK_ctr_bill_guest REFERENCES gst.guest,
  company_id BIGINT NULL CONSTRAINT FK_ctr_bill_company REFERENCES grp.company_account,
  discount_paise BIGINT NOT NULL CONSTRAINT DF_ctr_bill_discount DEFAULT 0 CONSTRAINT CK_ctr_bill_discount CHECK (discount_paise >= 0),
  discount_reason NVARCHAR(200) NULL,
  net_paise BIGINT NOT NULL,
  tax_paise BIGINT NOT NULL,
  cgst_paise BIGINT NOT NULL,
  sgst_paise BIGINT NOT NULL,
  total_paise BIGINT NOT NULL CONSTRAINT CK_ctr_bill_total_positive CHECK (total_paise > 0),
  remark NVARCHAR(200) NULL,
  settlement_mode VARCHAR(8) NOT NULL CONSTRAINT CK_ctr_bill_mode CHECK (settlement_mode IN ('paid','udhaar','company')),
  method VARCHAR(40) NULL,
  reference NVARCHAR(80) NULL,
  receipt_id BIGINT NULL,
  udhaar_id BIGINT NULL,
  status VARCHAR(8) NOT NULL CONSTRAINT CK_ctr_bill_status CHECK (status IN ('Issued','Credited')),
  credit_note_no VARCHAR(16) NULL,
  credit_note_reason NVARCHAR(200) NULL,
  credit_date DATE NULL,
  void_approval_id BIGINT NULL CONSTRAINT FK_ctr_bill_void_approval REFERENCES bill.approval,
  idem_key VARCHAR(80) NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_ctr_bill_invoice_no UNIQUE (property_id, invoice_no),
  CONSTRAINT CK_ctr_bill_sum CHECK (total_paise = net_paise + tax_paise AND tax_paise = cgst_paise + sgst_paise),
  CONSTRAINT CK_ctr_bill_paid CHECK (settlement_mode <> 'paid' OR method IS NOT NULL),
  CONSTRAINT CK_ctr_bill_company CHECK ((settlement_mode = 'company' AND company_id IS NOT NULL) OR (settlement_mode <> 'company' AND company_id IS NULL)),
  CONSTRAINT CK_ctr_bill_udhaar CHECK (settlement_mode <> 'udhaar' OR party_phone IS NOT NULL OR guest_id IS NOT NULL),
  CONSTRAINT CK_ctr_bill_credited CHECK ((status = 'Credited' AND credit_note_no IS NOT NULL AND credit_date IS NOT NULL AND void_approval_id IS NOT NULL) OR (status = 'Issued' AND credit_note_no IS NULL)));
CREATE INDEX IX_ctr_bill_date ON ctr.bill(property_id, business_date) INCLUDE (invoice_no, total_paise, settlement_mode, status);
CREATE UNIQUE INDEX UX_ctr_bill_idem ON ctr.bill(property_id, idem_key) WHERE idem_key IS NOT NULL;
CREATE TABLE ctr.bill_line(
  bill_id BIGINT NOT NULL CONSTRAINT FK_ctr_bill_line_bill REFERENCES ctr.bill,
  seq INT NOT NULL CONSTRAINT CK_ctr_bill_line_seq CHECK (seq BETWEEN 1 AND 30),
  label NVARCHAR(120) NOT NULL,
  extra_id VARCHAR(40) NULL,
  menu_item_id VARCHAR(40) NULL,
  qty INT NOT NULL CONSTRAINT CK_ctr_bill_line_qty CHECK (qty BETWEEN 1 AND 99),
  unit_paise BIGINT NOT NULL CONSTRAINT CK_ctr_bill_line_unit CHECK (unit_paise > 0),
  amount_paise BIGINT NOT NULL,
  gst_category VARCHAR(10) NOT NULL CONSTRAINT CK_ctr_bill_line_cat CHECK (gst_category IN ('food','laundry','banquet','other','room')),
  tax_bps INT NOT NULL CONSTRAINT CK_ctr_bill_line_bps CHECK (tax_bps BETWEEN 0 AND 2800),
  sac CHAR(6) NOT NULL,
  CONSTRAINT PK_ctr_bill_line PRIMARY KEY (bill_id, seq),
  CONSTRAINT CK_ctr_bill_line_amount CHECK (amount_paise = unit_paise * qty));
GO
CREATE TRIGGER ctr.TR_bill_line_immutable ON ctr.bill_line AFTER UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51688, 'ctr.bill_line rows are immutable: credit the bill instead.', 1;
END;
