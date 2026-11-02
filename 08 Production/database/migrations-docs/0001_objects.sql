-- HccDocuments 0001 · LF-docdb (02 Planning/agents/LF-CONTRACT.md §1.3; DEC-200): the documents database. Content only; metadata stays in Hcc.
CREATE SCHEMA dob;
GO
CREATE TABLE dob.object(
  object_id BIGINT IDENTITY CONSTRAINT PK_dob_object PRIMARY KEY,
  object_ref UNIQUEIDENTIFIER NOT NULL CONSTRAINT UQ_dob_object_ref UNIQUE,
  property_id INT NOT NULL,
  kind VARCHAR(20) NOT NULL CONSTRAINT CK_dob_object_kind CHECK (kind IN ('id-image','guest-photo','expense-bill','banking-slip','compliance-evidence','counter-attachment','legacy-image')),
  owner_table VARCHAR(40) NOT NULL,
  owner_id BIGINT NOT NULL,
  content_type VARCHAR(30) NOT NULL,
  size_bytes INT NOT NULL CONSTRAINT CK_dob_object_size CHECK (size_bytes BETWEEN 1 AND 8388608),
  sealed BIT NOT NULL,
  sha256 BINARY(32) NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  created_by INT NOT NULL,
  deleted_utc DATETIME2(3) NULL);
CREATE INDEX IX_dob_object_owner ON dob.object(property_id, owner_table, owner_id);
CREATE INDEX IX_dob_object_sweep ON dob.object(created_utc) WHERE deleted_utc IS NULL;
GO
CREATE TABLE dob.object_content(
  object_id BIGINT NOT NULL CONSTRAINT PK_dob_object_content PRIMARY KEY CONSTRAINT FK_dob_object_content REFERENCES dob.object ON DELETE CASCADE,
  content VARBINARY(MAX) NULL,
  sealed_text VARCHAR(MAX) NULL,
  CONSTRAINT CK_dob_object_content_one CHECK ((content IS NULL AND sealed_text IS NOT NULL) OR (content IS NOT NULL AND sealed_text IS NULL)));
