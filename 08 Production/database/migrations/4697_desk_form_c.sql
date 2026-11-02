-- HCC migration 4697 · LF A5 Form C for foreign guests (LF-CONTRACT §8.3). One row per STAY (a guest arrives many times). The passport NUMBER is the guest's
-- ID number (doc.id_capture, DEC-068) and is never copied here. visa_no is personal data: stored in clear behind PERM-DOC-004 (OQ-3 asks whether to seal it
-- too), returned masked (last 4) to everyone without that right, never written to audit text, exports or messages.
-- Required at check-in when the registered guest's nationality is not Indian and the Owner setting frontdesk.formCRequired is on (default on).
CREATE TABLE gst.stay_form_c(
  stay_id BIGINT NOT NULL CONSTRAINT PK_stay_form_c PRIMARY KEY CONSTRAINT FK_stay_form_c_stay REFERENCES gst.stay,
  property_id INT NOT NULL CONSTRAINT FK_stay_form_c_property REFERENCES inv.property,
  passport_place NVARCHAR(60) NOT NULL,
  passport_issued DATE NOT NULL,
  passport_expiry DATE NOT NULL,
  visa_no NVARCHAR(30) NOT NULL,
  visa_type NVARCHAR(30) NOT NULL,
  visa_place NVARCHAR(60) NOT NULL,
  visa_issued DATE NOT NULL,
  visa_expiry DATE NOT NULL,
  arrived_from NVARCHAR(60) NOT NULL,
  arrival_in_india DATE NOT NULL,
  intended_stay_days INT NOT NULL,
  next_destination NVARCHAR(60) NOT NULL,
  employed_in_india BIT NOT NULL,
  employer_or_business NVARCHAR(80) NULL,
  address_abroad NVARCHAR(160) NOT NULL,
  contact_abroad NVARCHAR(80) NULL,
  sex CHAR(1) NOT NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_stay_form_c_sex CHECK (sex IN ('M','F','X')),
  CONSTRAINT CK_stay_form_c_days CHECK (intended_stay_days BETWEEN 1 AND 3650),
  CONSTRAINT CK_stay_form_c_passport CHECK (passport_expiry > passport_issued),
  CONSTRAINT CK_stay_form_c_visa CHECK (visa_expiry > visa_issued),
  CONSTRAINT CK_stay_form_c_employer CHECK (employed_in_india = 0 OR employer_or_business IS NOT NULL));
CREATE INDEX IX_stay_form_c_property ON gst.stay_form_c(property_id, arrival_in_india);
