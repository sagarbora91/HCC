-- HCC migration 4130 · SCC tax port (DEC-116, 27 Oct 2026): the compliance calendar carries SCC's tax module in full ("Compliance Operating
-- System"). SCC's catalogue (113 cards per FY, 30 obligation keys) is static data in the code (Hcc.Domain/Compliance/SccTaxCatalogue.json);
-- this migration adds what SCC stores per firm — here one firm per property, the Owner sets its profile:
--   cmp.profile            SCC firm: name, PAN, GSTIN (no format check, upper-cased), entity, GST type, has employees, QRMP (regular / both only)
--   cmp.setting.fy_start   SCC fyStartYear: FY 2026–27 … 2032–33, kept until changed
--   cmp.record             SCC compliance[firm][fy][itemId]: done (filed-on, notes, evidence ref, reviewer — all optional) and NA (no reason),
--                          independent flags; + who / when (HCC audit)
--   cmp.record_reopened    HCC safeguard: what an Owner reopen removed (SCC's undo deletes it)
--   cmp.custom_obligation  SCC custom obligations (due day 0–31 in the ticked month); soft-deleted so the audit keeps them
--   cmp.archive / cmp.archive_category   SCC archived obligation keys and whole categories
--   cmp.evidence (+ _content)            SCC evidence files per FY and card (HCC limit 5 MB a file); listings never read the content
-- The W05-C tables (cmp.obligation, obligation_rule, filing, filing_reopened) stay for history; migration 4131 carries their data over.
-- Owner + Accountant only (DEC-055, PERM-CMP-001 / 002, unchanged).

CREATE TABLE cmp.profile(
  property_id INT NOT NULL CONSTRAINT PK_cmp_profile PRIMARY KEY CONSTRAINT FK_cmp_profile_property REFERENCES inv.property,
  firm_name NVARCHAR(120) NOT NULL CONSTRAINT CK_cmp_profile_name CHECK (LEN(firm_name) > 0),
  pan NVARCHAR(20) NOT NULL CONSTRAINT DF_cmp_profile_pan DEFAULT N'',
  gstin NVARCHAR(20) NOT NULL CONSTRAINT DF_cmp_profile_gstin DEFAULT N'',
  entity VARCHAR(14) NOT NULL CONSTRAINT CK_cmp_profile_entity CHECK (entity IN ('proprietorship', 'partnership', 'llp', 'company')),
  gst_type VARCHAR(12) NOT NULL CONSTRAINT CK_cmp_profile_gst CHECK (gst_type IN ('regular', 'composition', 'both', 'unregistered')),
  has_employees BIT NOT NULL,
  qrmp BIT NOT NULL,
  notes NVARCHAR(500) NOT NULL CONSTRAINT DF_cmp_profile_notes DEFAULT N'',
  updated_by INT NULL,
  updated_utc DATETIME2(3) NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_cmp_profile_qrmp CHECK (qrmp = 0 OR gst_type IN ('regular', 'both')));

ALTER TABLE cmp.setting ADD fy_start SMALLINT NOT NULL CONSTRAINT DF_cmp_setting_fy DEFAULT 2026
  CONSTRAINT CK_cmp_setting_fy CHECK (fy_start BETWEEN 2026 AND 2032);

CREATE TABLE cmp.record(
  property_id INT NOT NULL CONSTRAINT FK_cmp_record_property REFERENCES inv.property,
  fy_start SMALLINT NOT NULL,
  item_id VARCHAR(48) NOT NULL,
  done BIT NOT NULL,
  na BIT NOT NULL,
  filed_on DATE NULL,
  notes NVARCHAR(500) NULL,
  evidence_ref NVARCHAR(200) NULL,
  reviewer_note NVARCHAR(200) NULL,
  due_date DATE NULL,
  done_by INT NULL,
  done_utc DATETIME2(3) NULL,
  na_by INT NULL,
  na_utc DATETIME2(3) NULL,
  legacy VARCHAR(40) NULL,                -- "obligation:period" of a W05-C filing carried over by 4131
  CONSTRAINT PK_cmp_record PRIMARY KEY (property_id, fy_start, item_id),
  CONSTRAINT CK_cmp_record_state CHECK (done = 1 OR na = 1));

CREATE TABLE cmp.record_reopened(
  reopen_id BIGINT IDENTITY CONSTRAINT PK_cmp_record_reopened PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_cmp_record_reopened_property REFERENCES inv.property,
  fy_start SMALLINT NOT NULL,
  item_id VARCHAR(48) NOT NULL,
  part VARCHAR(4) NOT NULL CONSTRAINT CK_cmp_record_reopened_part CHECK (part IN ('done', 'na')),
  was_done BIT NOT NULL,
  was_na BIT NOT NULL,
  filed_on DATE NULL,
  notes NVARCHAR(500) NULL,
  evidence_ref NVARCHAR(200) NULL,
  reviewer_note NVARCHAR(200) NULL,
  due_date DATE NULL,
  recorded_by INT NULL,
  recorded_utc DATETIME2(3) NULL,
  reopened_by INT NOT NULL,
  reopened_utc DATETIME2(3) NOT NULL,
  reopen_reason NVARCHAR(120) NOT NULL);
CREATE INDEX IX_cmp_record_reopened_property ON cmp.record_reopened(property_id, reopen_id DESC);

CREATE TABLE cmp.custom_obligation(
  custom_id BIGINT IDENTITY CONSTRAINT PK_cmp_custom_obligation PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_cmp_custom_property REFERENCES inv.property,
  cat VARCHAR(4) NOT NULL CONSTRAINT CK_cmp_custom_cat CHECK (cat IN ('it', 'gst', 'comp', 'lab')),
  name NVARCHAR(200) NOT NULL CONSTRAINT CK_cmp_custom_name CHECK (LEN(name) > 0),
  freq VARCHAR(10) NOT NULL CONSTRAINT CK_cmp_custom_freq CHECK (freq IN ('Monthly', 'Quarterly', 'Annual', 'One-time')),
  due_day TINYINT NOT NULL CONSTRAINT CK_cmp_custom_due CHECK (due_day BETWEEN 0 AND 31),
  months_json NVARCHAR(400) NOT NULL CONSTRAINT CK_cmp_custom_months CHECK (ISJSON(months_json) = 1),
  form NVARCHAR(200) NOT NULL CONSTRAINT DF_cmp_custom_form DEFAULT N'',
  who NVARCHAR(2000) NOT NULL CONSTRAINT DF_cmp_custom_who DEFAULT N'',
  what NVARCHAR(2000) NOT NULL CONSTRAINT DF_cmp_custom_what DEFAULT N'',
  why NVARCHAR(2000) NOT NULL CONSTRAINT DF_cmp_custom_why DEFAULT N'',
  penalty NVARCHAR(2000) NOT NULL CONSTRAINT DF_cmp_custom_penalty DEFAULT N'',
  responsible NVARCHAR(200) NOT NULL CONSTRAINT DF_cmp_custom_responsible DEFAULT N'',
  reviewer NVARCHAR(200) NOT NULL CONSTRAINT DF_cmp_custom_reviewer DEFAULT N'',
  risk VARCHAR(6) NULL CONSTRAINT CK_cmp_custom_risk CHECK (risk IN ('High', 'Medium', 'Low')),
  buffer_days TINYINT NULL CONSTRAINT CK_cmp_custom_buffer CHECK (buffer_days BETWEEN 0 AND 30),
  checklist_json NVARCHAR(MAX) NOT NULL CONSTRAINT DF_cmp_custom_checklist DEFAULT N'[]' CONSTRAINT CK_cmp_custom_checklist CHECK (ISJSON(checklist_json) = 1),
  workflow_json NVARCHAR(MAX) NOT NULL CONSTRAINT DF_cmp_custom_workflow DEFAULT N'[]' CONSTRAINT CK_cmp_custom_workflow CHECK (ISJSON(workflow_json) = 1),
  evidence_json NVARCHAR(MAX) NOT NULL CONSTRAINT DF_cmp_custom_evidence DEFAULT N'[]' CONSTRAINT CK_cmp_custom_evidence CHECK (ISJSON(evidence_json) = 1),
  legacy VARCHAR(40) NULL,                -- "obligation:<id>" of a W05-C obligation carried over by 4131
  updated_by INT NULL,
  updated_utc DATETIME2(3) NULL,
  deleted_by INT NULL,
  deleted_utc DATETIME2(3) NULL,
  row_ver ROWVERSION);
CREATE INDEX IX_cmp_custom_property ON cmp.custom_obligation(property_id, custom_id) WHERE deleted_utc IS NULL;

CREATE TABLE cmp.archive(
  property_id INT NOT NULL CONSTRAINT FK_cmp_archive_property REFERENCES inv.property,
  archive_key NVARCHAR(200) NOT NULL,
  archived_by INT NOT NULL,
  archived_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_cmp_archive PRIMARY KEY (property_id, archive_key));

CREATE TABLE cmp.archive_category(
  property_id INT NOT NULL CONSTRAINT FK_cmp_archive_category_property REFERENCES inv.property,
  cat VARCHAR(4) NOT NULL CONSTRAINT CK_cmp_archive_category CHECK (cat IN ('it', 'gst', 'comp', 'lab')),
  archived_by INT NOT NULL,
  archived_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_cmp_archive_category PRIMARY KEY (property_id, cat));

CREATE TABLE cmp.evidence(
  evidence_id BIGINT IDENTITY CONSTRAINT PK_cmp_evidence PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_cmp_evidence_property REFERENCES inv.property,
  fy_start SMALLINT NOT NULL,
  item_id VARCHAR(48) NOT NULL,
  file_name NVARCHAR(200) NOT NULL,
  content_type VARCHAR(100) NOT NULL,
  size_bytes INT NOT NULL CONSTRAINT CK_cmp_evidence_size CHECK (size_bytes BETWEEN 0 AND 5242880),
  added_by INT NOT NULL,
  added_utc DATETIME2(3) NOT NULL);
CREATE INDEX IX_cmp_evidence_item ON cmp.evidence(property_id, fy_start, item_id);

-- The file itself, in its own table so listings never read it.
CREATE TABLE cmp.evidence_content(
  evidence_id BIGINT NOT NULL CONSTRAINT PK_cmp_evidence_content PRIMARY KEY CONSTRAINT FK_cmp_evidence_content REFERENCES cmp.evidence ON DELETE CASCADE,
  content VARBINARY(MAX) NOT NULL);
