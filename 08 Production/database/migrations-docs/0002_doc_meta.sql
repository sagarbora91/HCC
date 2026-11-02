-- HccDocuments 0002 · LF-docdb (contract §1.3, §1.9): plat.doc_meta('main_database', 'name') is written by the move job so a restore of a
-- wrong pair of databases is detected by check-documents.
CREATE SCHEMA plat;
GO
CREATE TABLE plat.doc_meta(
  meta_key VARCHAR(40) NOT NULL CONSTRAINT PK_doc_meta PRIMARY KEY,
  meta_value NVARCHAR(200) NOT NULL,
  updated_utc DATETIME2(3) NOT NULL CONSTRAINT DF_doc_meta_updated DEFAULT SYSUTCDATETIME());
