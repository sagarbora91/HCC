-- Lodge ID document patch - Access / Jet / ACE equivalent of scripts 01 and 03 (DESIGN DRAFT, NOT EXECUTED ANYWHERE)
-- Date written: 2 Nov 2026
-- WHY THIS FILE EXISTS: the live Hotel Parth install runs in Access mode (DatabaseType.dll first line = "acc"), not SQL Server.
-- The SQL Server scripts only apply if the site first moves to SQL Server mode (Lodge's code already has that branch).
-- How to run: create an empty LodgeIdDocs.mdb (Access 2000 format, with a database password held by the owner), open it with
-- ADODB (Provider=Microsoft.Jet.OLEDB.4.0) and db.Execute each statement below separately. Jet SQL allows one statement per Execute.
-- Limits to remember: an .mdb is capped at 2 GB; this file holds only metadata (no images), so it stays tiny.
-- Jet has no triggers, no stored procedures, no filtered unique indexes: the rules those give in SQL Server are enforced in modIdDocs.bas
-- (soft delete only, one current row per slot, audit rows always written in the same ADO transaction).

CREATE TABLE IdDocType (DocTypeCode TEXT(12) NOT NULL PRIMARY KEY, DocTypeName TEXT(60) NOT NULL, LegacyIdentityCode LONG, HccDocKind TEXT(10) NOT NULL, IsPhoto YESNO, IsActive YESNO, SortOrder LONG);

CREATE TABLE IdDocSetting (SettingKey TEXT(40) NOT NULL PRIMARY KEY, SettingValue TEXT(255) NOT NULL, Note TEXT(200), UpdatedAt DATETIME, UpdatedBy TEXT(60));

CREATE TABLE IdDocument (
  IdDocumentId COUNTER PRIMARY KEY,
  GuestID LONG NOT NULL, FYCode TEXT(7), BookingID LONG, TransactionID LONG,
  DocTypeCode TEXT(12) NOT NULL, SrNo SHORT NOT NULL, SideNo BYTE NOT NULL,
  FolderRel TEXT(200) NOT NULL, FileName TEXT(200) NOT NULL, OriginalName TEXT(255), FileExt TEXT(8) NOT NULL,
  SizeBytes LONG NOT NULL, Sha256Hex TEXT(64), CapturedVia TEXT(10) NOT NULL, CapturedAt DATETIME NOT NULL, CapturedBy TEXT(60) NOT NULL, Workstation TEXT(60),
  IsCurrent YESNO, IsDeleted YESNO, DeletedAt DATETIME, DeletedBy TEXT(60), DeleteReason TEXT(200), QualityFlag TEXT(12), MigratedFrom TEXT(255));

CREATE INDEX IX_IdDocument_Guest ON IdDocument (GuestID, IsDeleted, IsCurrent);
CREATE UNIQUE INDEX UX_IdDocument_File ON IdDocument (FolderRel, FileName);
CREATE INDEX IX_IdDocument_Hash ON IdDocument (Sha256Hex);

CREATE TABLE IdDocumentAudit (
  AuditId COUNTER PRIMARY KEY, IdDocumentId LONG, GuestID LONG, Action TEXT(12) NOT NULL, ActionAt DATETIME NOT NULL, ActionBy TEXT(60), Workstation TEXT(60), Detail TEXT(255));
CREATE INDEX IX_IdDocumentAudit_Doc ON IdDocumentAudit (IdDocumentId, ActionAt);

-- seed rows (same codes as the SQL Server script 01)
INSERT INTO IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, IsActive, SortOrder) VALUES ('AADHAAR', 'Aadhaar card', 'aadhaar', 0, 1, 10);
INSERT INTO IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, IsActive, SortOrder) VALUES ('DL', 'Driving licence', 'dl', 0, 1, 20);
INSERT INTO IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, IsActive, SortOrder) VALUES ('VOTER', 'Voter ID (election card)', 'voter', 0, 1, 30);
INSERT INTO IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, IsActive, SortOrder) VALUES ('PAN', 'PAN card', 'pan', 0, 1, 40);
INSERT INTO IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, IsActive, SortOrder) VALUES ('PASSPORT', 'Passport', 'passport', 0, 1, 50);
INSERT INTO IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, IsActive, SortOrder) VALUES ('VISA', 'Visa / FRRO paper', 'other', 0, 1, 55);
INSERT INTO IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, IsActive, SortOrder) VALUES ('OFFICE-ID', 'Office ID card', 'other', 0, 1, 60);
INSERT INTO IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, IsActive, SortOrder) VALUES ('OTHER', 'Other ID', 'other', 0, 1, 70);
INSERT INTO IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, IsActive, SortOrder) VALUES ('LEGACY', 'Old file, type unknown', 'legacy', 0, 1, 90);
INSERT INTO IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, IsActive, SortOrder) VALUES ('PHOTO', 'Guest photo', 'other', 1, 1, 95);
INSERT INTO IdDocSetting (SettingKey, SettingValue, Note) VALUES ('RootPath', '\\SERVER\LodgeIdStore', 'REPLACE with the real UNC path');
INSERT INTO IdDocSetting (SettingKey, SettingValue, Note) VALUES ('MaxBytes', '8388608', '8 MB');
INSERT INTO IdDocSetting (SettingKey, SettingValue, Note) VALUES ('AllowedExt', 'jpg,jpeg,png,bmp,gif,pdf', '');
INSERT INTO IdDocSetting (SettingKey, SettingValue, Note) VALUES ('FolderFan', '1000', 'sub-folder = GuestID / 1000');
