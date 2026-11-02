-- HCC migration 4681 · LF A2 supersession (LF-CONTRACT §4): the old invoice is marked superseded here (the invoice row itself never changes).
-- Print: "Supersedes <old>" on the new one, "Superseded by <new> (credit note <CRN>)" on the old one.
CREATE TABLE ivc.invoice_supersession(
  invoice_id BIGINT NOT NULL CONSTRAINT PK_ivc_invoice_supersession PRIMARY KEY CONSTRAINT FK_ivc_supersession_old REFERENCES ivc.invoice,
  superseded_by_invoice_id BIGINT NOT NULL CONSTRAINT UQ_ivc_supersession_new UNIQUE CONSTRAINT FK_ivc_supersession_new REFERENCES ivc.invoice,
  correction_id BIGINT NOT NULL,
  utc DATETIME2(3) NOT NULL,
  CONSTRAINT CK_ivc_supersession_distinct CHECK (invoice_id <> superseded_by_invoice_id));
