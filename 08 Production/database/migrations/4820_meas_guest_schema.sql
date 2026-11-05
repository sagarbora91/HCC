-- HCC migration 4820 · MEAS guest captures (MEAS-CONTRACT §2, lane meas-cap-guest): meas.survey, meas.review_month, meas.root_cause (+ meas.root_cause_set).
-- The `meas` schema is shared by every MEAS lane; each lane creates it only when missing. property_id + client_id make every capture idempotent.
-- meas.survey has NO column for a name, phone, ID number or photo (DEC-068, DoD "a survey cannot store ID data"): consent is NOT NULL and must be 1.
-- Nothing here touches money. Rows of meas.survey are append-only (trigger below); a review month is replaced by its natural key.
IF SCHEMA_ID('meas') IS NULL EXEC('CREATE SCHEMA meas');
GO
CREATE TABLE meas.survey(
  survey_id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_meas_survey PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_meas_survey_property REFERENCES inv.property,
  client_id VARCHAR(60) NOT NULL,
  at_utc DATETIME2(3) NOT NULL,
  via VARCHAR(5) NOT NULL CONSTRAINT CK_meas_survey_via CHECK (via IN ('qr', 'desk', 'link')),
  reservation_id BIGINT NULL,
  room_type VARCHAR(40) NULL,
  channel VARCHAR(40) NULL,
  overall TINYINT NOT NULL CONSTRAINT CK_meas_survey_overall CHECK (overall BETWEEN 1 AND 5),
  cleanliness TINYINT NOT NULL CONSTRAINT CK_meas_survey_clean CHECK (cleanliness BETWEEN 1 AND 5),
  staff TINYINT NOT NULL CONSTRAINT CK_meas_survey_staff CHECK (staff BETWEEN 1 AND 5),
  value_score TINYINT NOT NULL CONSTRAINT CK_meas_survey_value CHECK (value_score BETWEEN 1 AND 5),
  breakfast TINYINT NULL CONSTRAINT CK_meas_survey_breakfast CHECK (breakfast IS NULL OR breakfast BETWEEN 1 AND 5),
  recommend TINYINT NOT NULL CONSTRAINT CK_meas_survey_recommend CHECK (recommend BETWEEN 0 AND 10),
  comment NVARCHAR(500) NULL,
  consent BIT NOT NULL CONSTRAINT CK_meas_survey_consent CHECK (consent = 1),
  entered_by INT NULL,
  created_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_survey_created DEFAULT SYSUTCDATETIME(),
  CONSTRAINT UQ_meas_survey_client UNIQUE (property_id, client_id));
CREATE INDEX IX_meas_survey_at ON meas.survey (property_id, at_utc) INCLUDE (via, overall, recommend);
GO
CREATE TRIGGER meas.TR_survey_append_only ON meas.survey INSTEAD OF UPDATE, DELETE AS
BEGIN
  THROW 51820, 'meas.survey is append-only.', 1;
END;
GO
CREATE TABLE meas.review_month(
  property_id INT NOT NULL CONSTRAINT FK_meas_review_property REFERENCES inv.property,
  month CHAR(7) NOT NULL CONSTRAINT CK_meas_review_month CHECK (month LIKE '[0-9][0-9][0-9][0-9]-[0-1][0-9]'),
  platform VARCHAR(12) NOT NULL CONSTRAINT CK_meas_review_platform CHECK (platform IN ('google', 'agoda', 'makemytrip', 'booking', 'other')),
  review_count INT NOT NULL CONSTRAINT CK_meas_review_count CHECK (review_count BETWEEN 0 AND 100000),
  avg_score DECIMAL(3,2) NOT NULL CONSTRAINT CK_meas_review_avg CHECK (avg_score BETWEEN 0 AND 5),
  score_12m DECIMAL(3,2) NULL CONSTRAINT CK_meas_review_12m CHECK (score_12m IS NULL OR score_12m BETWEEN 0 AND 5),
  responded INT NOT NULL,
  themes NVARCHAR(200) NULL,
  updated_by INT NULL,
  updated_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_review_updated DEFAULT SYSUTCDATETIME(),
  CONSTRAINT PK_meas_review_month PRIMARY KEY (property_id, month, platform),
  CONSTRAINT CK_meas_review_responded CHECK (responded >= 0 AND responded <= review_count));
GO
CREATE TABLE meas.root_cause(
  property_id INT NOT NULL CONSTRAINT FK_meas_root_cause_property REFERENCES inv.property,
  code VARCHAR(12) NOT NULL CONSTRAINT CK_meas_root_cause_code CHECK (code NOT LIKE '%[^A-Z0-9_-]%'),
  label NVARCHAR(40) NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_meas_root_cause_active DEFAULT 1,
  CONSTRAINT PK_meas_root_cause PRIMARY KEY (property_id, code));
-- The cause chosen for a complaint (side table keyed by the complaint id; the complaint row itself is never changed).
CREATE TABLE meas.root_cause_set(
  property_id INT NOT NULL CONSTRAINT FK_meas_rcs_property REFERENCES inv.property,
  complaint_id VARCHAR(40) NOT NULL,
  code VARCHAR(12) NOT NULL,
  set_by INT NOT NULL,
  set_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_rcs_set DEFAULT SYSUTCDATETIME(),
  CONSTRAINT PK_meas_rcs PRIMARY KEY (property_id, complaint_id),
  CONSTRAINT FK_meas_rcs_cause FOREIGN KEY (property_id, code) REFERENCES meas.root_cause (property_id, code));
GO
