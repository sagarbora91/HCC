-- HCC migration 4682 · LF A2 bill correction after check-out (LF-CONTRACT §4). One request (approval action 'bill-correction', Owner decides)
-- that on approval issues in one transaction: a credit note for the whole original invoice, the folio changes, a new tax invoice (revision + 1).
CREATE TABLE ivc.bill_correction(
  correction_id BIGINT IDENTITY CONSTRAINT PK_ivc_bill_correction PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_ivc_bill_correction_property REFERENCES inv.property,
  folio_id BIGINT NOT NULL CONSTRAINT FK_ivc_bill_correction_folio REFERENCES bill.folio,
  old_invoice_id BIGINT NOT NULL CONSTRAINT FK_ivc_bill_correction_old REFERENCES ivc.invoice,
  credit_note_no VARCHAR(16) NULL,
  new_invoice_id BIGINT NULL CONSTRAINT FK_ivc_bill_correction_new REFERENCES ivc.invoice,
  approval_id BIGINT NOT NULL CONSTRAINT FK_ivc_bill_correction_approval REFERENCES bill.approval,
  reason NVARCHAR(200) NOT NULL,
  requested_by INT NOT NULL,
  requested_utc DATETIME2(3) NOT NULL,
  approved_by INT NULL,
  decided_utc DATETIME2(3) NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_ivc_bill_correction_status CHECK (status IN ('Pending','Issued','Rejected','Expired')),
  old_total_paise BIGINT NOT NULL,
  new_total_paise BIGINT NOT NULL,
  delta_paise BIGINT NOT NULL,
  settlement VARCHAR(12) NULL CONSTRAINT CK_ivc_bill_correction_settlement CHECK (settlement IS NULL OR settlement IN ('refund-due','balance-due','none')),
  row_ver ROWVERSION,
  CONSTRAINT CK_ivc_bill_correction_delta CHECK (delta_paise = new_total_paise - old_total_paise),
  CONSTRAINT CK_ivc_bill_correction_issued CHECK (status <> 'Issued' OR (new_invoice_id IS NOT NULL AND credit_note_no IS NOT NULL AND approved_by IS NOT NULL AND settlement IS NOT NULL)));
CREATE INDEX IX_ivc_bill_correction_folio ON ivc.bill_correction(folio_id, status);
CREATE TABLE ivc.bill_correction_change(
  correction_id BIGINT NOT NULL CONSTRAINT FK_ivc_bcc_correction REFERENCES ivc.bill_correction,
  seq INT NOT NULL,
  kind VARCHAR(12) NOT NULL CONSTRAINT CK_ivc_bcc_kind CHECK (kind IN ('room-rate','remove-line','add-line')),
  night DATE NULL,
  old_rate_paise BIGINT NULL,
  new_rate_paise BIGINT NULL CONSTRAINT CK_ivc_bcc_new_rate CHECK (new_rate_paise IS NULL OR new_rate_paise >= 0),
  charge_id BIGINT NULL,
  label NVARCHAR(120) NULL,
  amount_paise BIGINT NULL CONSTRAINT CK_ivc_bcc_amount CHECK (amount_paise IS NULL OR amount_paise > 0),
  gst_category VARCHAR(10) NULL,
  service_date DATE NULL,
  extra_id VARCHAR(40) NULL,
  CONSTRAINT PK_ivc_bill_correction_change PRIMARY KEY (correction_id, seq),
  CONSTRAINT CK_ivc_bcc_shape CHECK ((kind = 'room-rate' AND night IS NOT NULL AND old_rate_paise IS NOT NULL AND new_rate_paise IS NOT NULL)
                                  OR (kind = 'remove-line' AND charge_id IS NOT NULL)
                                  OR (kind = 'add-line' AND label IS NOT NULL AND amount_paise IS NOT NULL AND gst_category IS NOT NULL AND service_date IS NOT NULL)));
