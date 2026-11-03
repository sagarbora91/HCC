/*
  Lodge ID document patch - script 01 of 5 (tables)
  Date written : 2 Nov 2026         Status: DESIGN DRAFT. NOT EXECUTED ANYWHERE. Not syntax-checked on a live server.
  Target       : SQL Server 2008 R2 (compatibility level 100). Uses only 2008 R2 syntax:
                 no THROW, no IIF, no FORMAT, no CONCAT, no SEQUENCE, no OFFSET/FETCH, no DROP ... IF EXISTS, no CREATE OR ALTER.
  Why a separate database: Lodge makes a NEW database every financial year (Lodge17-18 ... Lodge26-27) and copies CustomerInfo into it;
                 GuestID is carried forward (DB-PROFILE README section 2). ID documents belong to the guest, not to the year,
                 so they live in one permanent database, LodgeIdDocs. A second ADO connection (dbDocs) is opened by modIdDocs.bas.
  Privacy      : this database holds NO images, NO names, NO ID numbers. Only file names, sizes, hashes and who/when.
                 The image files stay on disk in a protected share (see design doc, section 8).
  Run order    : 01, 02, 03, (04 only during migration), 05 for tests. Rollback = 99.
*/
USE master;
GO
IF DB_ID(N'LodgeIdDocs') IS NULL
    CREATE DATABASE LodgeIdDocs;          -- DBA: add FILENAME / sizes if the data folder is not the default
GO
ALTER DATABASE LodgeIdDocs SET RECOVERY SIMPLE;   -- change to FULL only if you run log backups (see design doc, backup)
GO
USE LodgeIdDocs;
GO

/* ---------- 1. document types (mirrors Lodge IdentityType and HCC doc.id_capture.doc_kind) ---------- */
IF OBJECT_ID(N'dbo.IdDocType') IS NULL
CREATE TABLE dbo.IdDocType
(
    DocTypeCode        VARCHAR(12)   NOT NULL CONSTRAINT PK_IdDocType PRIMARY KEY,
    DocTypeName        NVARCHAR(60)  NOT NULL,
    LegacyIdentityCode INT           NULL,      -- Lodge IdentityType.IDCode if known (filled by the owner; unknown = NULL)
    HccDocKind         VARCHAR(10)   NOT NULL,  -- value HCC will use in doc.id_capture.doc_kind when this row is imported
    IsPhoto            BIT           NOT NULL CONSTRAINT DF_IdDocType_IsPhoto DEFAULT (0),
    IsActive           BIT           NOT NULL CONSTRAINT DF_IdDocType_IsActive DEFAULT (1),
    SortOrder          INT           NOT NULL CONSTRAINT DF_IdDocType_Sort DEFAULT (100)
);
GO
IF NOT EXISTS (SELECT 1 FROM dbo.IdDocType)
BEGIN
    INSERT INTO dbo.IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, SortOrder) VALUES ('AADHAAR',  N'Aadhaar card',            'aadhaar',  0, 10);
    INSERT INTO dbo.IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, SortOrder) VALUES ('DL',       N'Driving licence',         'dl',       0, 20);
    INSERT INTO dbo.IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, SortOrder) VALUES ('VOTER',    N'Voter ID (election card)','voter',    0, 30);
    INSERT INTO dbo.IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, SortOrder) VALUES ('PAN',      N'PAN card',                'pan',      0, 40);
    INSERT INTO dbo.IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, SortOrder) VALUES ('PASSPORT', N'Passport',                'passport', 0, 50);
    INSERT INTO dbo.IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, SortOrder) VALUES ('VISA',     N'Visa / FRRO paper',       'other',    0, 55);
    INSERT INTO dbo.IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, SortOrder) VALUES ('OFFICE-ID',N'Office ID card',         'other',    0, 60);
    INSERT INTO dbo.IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, SortOrder) VALUES ('OTHER',    N'Other ID',                'other',    0, 70);
    INSERT INTO dbo.IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, SortOrder) VALUES ('LEGACY',   N'Old file, type unknown',  'legacy',   0, 90);
    INSERT INTO dbo.IdDocType (DocTypeCode, DocTypeName, HccDocKind, IsPhoto, SortOrder) VALUES ('PHOTO',    N'Guest photo',             'other',    1, 95);
END
GO

/* ---------- 2. settings (one row per key) ---------- */
IF OBJECT_ID(N'dbo.IdDocSetting') IS NULL
CREATE TABLE dbo.IdDocSetting
(
    SettingKey   VARCHAR(40)    NOT NULL CONSTRAINT PK_IdDocSetting PRIMARY KEY,
    SettingValue NVARCHAR(400)  NOT NULL,
    Note         NVARCHAR(200)  NULL,
    UpdatedAt    DATETIME       NOT NULL CONSTRAINT DF_IdDocSetting_At DEFAULT (GETDATE()),
    UpdatedBy    NVARCHAR(60)   NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM dbo.IdDocSetting)
BEGIN
    INSERT INTO dbo.IdDocSetting (SettingKey, SettingValue, Note) VALUES ('RootPath',   N'\\SERVER\LodgeIdStore', N'REPLACE: UNC path of the protected ID folder on the server PC (never a per-PC App.Path)');
    INSERT INTO dbo.IdDocSetting (SettingKey, SettingValue, Note) VALUES ('MaxBytes',   N'8388608',               N'8 MB, same cap as HCC doc.id_capture');
    INSERT INTO dbo.IdDocSetting (SettingKey, SettingValue, Note) VALUES ('AllowedExt',  N'jpg,jpeg,png,bmp,gif,pdf', N'Lodge dialog allows bmp, ico, jpg, jpeg, gif; ico dropped, png and pdf added');
    INSERT INTO dbo.IdDocSetting (SettingKey, SettingValue, Note) VALUES ('Retention',  N'KEEP',                  N'HCC DEC-071: keep indefinitely, manual purge only. Lawyer to confirm (DPDP Act 2023 erasure expectation)');
    INSERT INTO dbo.IdDocSetting (SettingKey, SettingValue, Note) VALUES ('FolderFan',  N'1000',                  N'Sub-folder = GuestID / 1000, so no folder holds more than ~1000 guests');
END
GO

/* ---------- 3. the document register ---------- */
IF OBJECT_ID(N'dbo.IdDocument') IS NULL
CREATE TABLE dbo.IdDocument
(
    IdDocumentId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IdDocument PRIMARY KEY,
    GuestID        INT            NOT NULL,           -- Lodge CustomerInfo.GuestID (carried forward across all FYs)
    FYCode         VARCHAR(7)     NULL,               -- '2026-27' = FY file that held the booking; NULL for guest-level documents
    BookingID      INT            NULL,               -- Lodge RoomBooking.BookingID inside that FY file (restarts per FY, so use with FYCode)
    TransactionID  INT            NULL,               -- Lodge TransactionData.TransactionID (stay), same FY rule
    DocTypeCode    VARCHAR(12)    NOT NULL CONSTRAINT FK_IdDocument_Type REFERENCES dbo.IdDocType (DocTypeCode),
    SrNo           SMALLINT       NOT NULL CONSTRAINT DF_IdDocument_SrNo DEFAULT (1),   -- 1,2,3 = several documents of one guest (Lodge CustomerIdProofDetail.srno)
    SideNo         TINYINT        NOT NULL CONSTRAINT DF_IdDocument_Side DEFAULT (1),   -- 1 front / only side, 2 back
    FolderRel      NVARCHAR(200)  NOT NULL,           -- folder RELATIVE to RootPath, e.g. '14\14803'. Never an absolute path.
    FileName       NVARCHAR(200)  NOT NULL,           -- stored name, e.g. '000045-AADHAAR-1-1.jpg'
    OriginalName   NVARCHAR(260)  NULL,               -- name the user picked / old Lodge name (e.g. '14803.jpg') - no folder part
    FileExt        VARCHAR(8)     NOT NULL,
    SizeBytes      INT            NOT NULL CONSTRAINT CK_IdDocument_Size CHECK (SizeBytes >= 0),
    Sha256Hex      CHAR(64)       NULL,               -- hash of the stored file; NULL only for rows imported with no hash yet
    CapturedVia    VARCHAR(10)    NOT NULL CONSTRAINT CK_IdDocument_Via CHECK (CapturedVia IN ('WEBCAM','FILE','SCAN','MIGRATED','OTHER')),
    CapturedAt     DATETIME       NOT NULL CONSTRAINT DF_IdDocument_At DEFAULT (GETDATE()),
    CapturedBy     NVARCHAR(60)   NOT NULL,           -- Lodge login name (UserMaster.Username)
    Workstation    NVARCHAR(60)   NULL,
    IsCurrent      BIT            NOT NULL CONSTRAINT DF_IdDocument_Cur DEFAULT (1),    -- 0 = replaced by a newer upload (old file kept)
    IsDeleted      BIT            NOT NULL CONSTRAINT DF_IdDocument_Del DEFAULT (0),    -- soft delete only; the file is NOT removed
    DeletedAt      DATETIME       NULL,
    DeletedBy      NVARCHAR(60)   NULL,
    DeleteReason   NVARCHAR(200)  NULL,
    QualityFlag    VARCHAR(12)    NULL CONSTRAINT CK_IdDocument_Q CHECK (QualityFlag IS NULL OR QualityFlag IN ('EMPTY','PLACEHOLDER','UNREADABLE','SUSPECT')),
    MigratedFrom   NVARCHAR(260)  NULL,               -- old relative path e.g. 'IDProof\14803.jpg' (MIGRATED rows only)
    RowVer         TIMESTAMP      NOT NULL            -- 2008 R2 name for rowversion
);
GO
-- one current, undeleted document per guest + type + serial + side
CREATE UNIQUE INDEX UX_IdDocument_Current ON dbo.IdDocument (GuestID, DocTypeCode, SrNo, SideNo) WHERE IsCurrent = 1 AND IsDeleted = 0;
CREATE INDEX IX_IdDocument_Guest   ON dbo.IdDocument (GuestID, IsDeleted, IsCurrent);
CREATE INDEX IX_IdDocument_Booking ON dbo.IdDocument (FYCode, BookingID) WHERE BookingID IS NOT NULL;
CREATE UNIQUE INDEX UX_IdDocument_File ON dbo.IdDocument (FolderRel, FileName);
CREATE INDEX IX_IdDocument_Hash    ON dbo.IdDocument (Sha256Hex) WHERE Sha256Hex IS NOT NULL;
GO

/* ---------- 4. audit trail (append-only) ---------- */
IF OBJECT_ID(N'dbo.IdDocumentAudit') IS NULL
CREATE TABLE dbo.IdDocumentAudit
(
    AuditId       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IdDocumentAudit PRIMARY KEY,
    IdDocumentId  INT            NULL,                -- NULL for failed attempts / batch events
    GuestID       INT            NULL,
    Action        VARCHAR(12)    NOT NULL CONSTRAINT CK_IdDocumentAudit_Act CHECK (Action IN ('ADD','REPLACE','VIEW','PRINT','DELETE','RESTORE','MIGRATE','EXPORT','DENIED','ERROR')),
    ActionAt      DATETIME       NOT NULL CONSTRAINT DF_IdDocumentAudit_At DEFAULT (GETDATE()),
    ActionBy      NVARCHAR(60)   NULL,
    Workstation   NVARCHAR(60)   NULL,
    Detail        NVARCHAR(400)  NULL                 -- reason, source file name, error text. Never an ID number.
);
CREATE INDEX IX_IdDocumentAudit_Doc  ON dbo.IdDocumentAudit (IdDocumentId, ActionAt);
CREATE INDEX IX_IdDocumentAudit_When ON dbo.IdDocumentAudit (ActionAt);
GO
PRINT 'Script 01 done (tables created if missing).';
GO
