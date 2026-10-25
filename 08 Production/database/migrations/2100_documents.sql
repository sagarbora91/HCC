-- HCC migration 2100 · ID document capture / OCR (W3-3, block 2100-2199). DEC-068: full guest ID numbers are stored (owner decision against
-- advice; LEGAL RISK: UIDAI / Aadhaar Act 2016 - CA/lawyer to confirm before production). A captured ID image and the OCR extraction
-- (number, name, date of birth) are PERSONAL DATA: both are sealed by the application with the guest-ID AES-256-GCM service (keys never in
-- git or SQL); SQL holds only sealed text, plus the last 4 of the extracted number for the masked display. Confirming a capture writes the
-- number into gst.guest_id_secret (0601) through the existing guest-ID store - this module never keeps a second copy of a confirmed number.
-- Original HCC design; no donor code.
CREATE SCHEMA doc;
GO
CREATE TABLE doc.id_capture(
  capture_id BIGINT IDENTITY CONSTRAINT PK_id_capture PRIMARY KEY,
  capture_ref UNIQUEIDENTIFIER NOT NULL CONSTRAINT UQ_id_capture_ref UNIQUE,   -- binds the sealed image / OCR text (associated data)
  property_id INT NOT NULL CONSTRAINT FK_id_capture_property REFERENCES inv.property,
  guest_id BIGINT NOT NULL CONSTRAINT FK_id_capture_guest REFERENCES gst.guest,
  stay_id BIGINT NULL CONSTRAINT FK_id_capture_stay REFERENCES gst.stay,
  doc_kind VARCHAR(10) NOT NULL CONSTRAINT CK_id_capture_kind CHECK (doc_kind IN ('aadhaar','passport','dl','voter','pan','other')),
  content_type VARCHAR(20) NOT NULL CONSTRAINT CK_id_capture_type CHECK (content_type IN ('image/jpeg','image/png','image/webp')),
  size_bytes INT NOT NULL CONSTRAINT CK_id_capture_size CHECK (size_bytes BETWEEN 1 AND 8388608),
  status VARCHAR(10) NOT NULL CONSTRAINT CK_id_capture_status CHECK (status IN ('Captured','Confirmed','Rejected','Purged')),
  ocr_doc_kind VARCHAR(10) NULL,
  ocr_last4 VARCHAR(4) NULL CONSTRAINT CK_id_capture_last4 CHECK (ocr_last4 IS NULL OR (LEN(ocr_last4) BETWEEN 1 AND 4 AND ocr_last4 NOT LIKE '%[^A-Z0-9]%')),
  ocr_confidence TINYINT NULL CONSTRAINT CK_id_capture_conf CHECK (ocr_confidence IS NULL OR ocr_confidence <= 100),
  ocr_sealed VARCHAR(4000) NULL,                                                 -- sealed JSON {number,name,dob}; NULL once purged
  captured_date DATE NOT NULL,                                                   -- business date of capture (retention clock)
  captured_utc DATETIME2(3) NOT NULL,
  captured_by INT NOT NULL,
  confirmed_utc DATETIME2(3) NULL,
  confirmed_by INT NULL,
  reject_reason NVARCHAR(200) NULL,
  purged_utc DATETIME2(3) NULL,
  row_ver ROWVERSION);
CREATE INDEX IX_id_capture_guest ON doc.id_capture(property_id, guest_id, capture_id);
CREATE INDEX IX_id_capture_retention ON doc.id_capture(property_id, captured_date) WHERE status <> 'Purged';
GO
-- The sealed image lives in its own table so listings never read it; the row is deleted when the capture is purged.
CREATE TABLE doc.id_capture_image(
  capture_id BIGINT NOT NULL CONSTRAINT PK_id_capture_image PRIMARY KEY CONSTRAINT FK_id_capture_image_capture REFERENCES doc.id_capture,
  sealed VARCHAR(MAX) NOT NULL);
GO
-- Master configuration setting documents.retentionDays is registered by the module (group "Data and system"); no seed row needed.
