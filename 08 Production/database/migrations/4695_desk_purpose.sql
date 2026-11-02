-- HCC migration 4695 · LF A3 purpose of visit (LF-CONTRACT §8.1). The master list (mst.purpose_of_visit: code + name, Owner / Tech admin, PERM-MST-001) and
-- the booking's purpose code. Required at check-in when the Owner setting frontdesk.purposeRequired is on (stays settings provider); optional at booking.
-- Defaults (Business, Tourism, Family function, Medical, Education, Official, Transit, Other) are the application's built-ins until the first change is saved,
-- like every MA-5 list. The legacy names come through tools/legacy/extract.py --purposes into deployment/import-templates/purposes.csv.
CREATE TABLE mst.purpose_of_visit(
  row_id INT IDENTITY CONSTRAINT PK_mst_purpose_of_visit PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_mst_purpose_of_visit_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_mst_purpose_of_visit_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_mst_purpose_of_visit_code UNIQUE (property_id, code),
  CONSTRAINT CK_mst_purpose_of_visit_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_mst_purpose_of_visit_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_mst_purpose_of_visit_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_mst_purpose_of_visit_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)));
CREATE INDEX IX_mst_purpose_of_visit_order ON mst.purpose_of_visit(property_id, sort_order);
ALTER TABLE inv.reservation ADD purpose_code VARCHAR(20) NULL;
