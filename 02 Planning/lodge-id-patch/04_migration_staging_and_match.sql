/*
  Lodge ID document patch - script 04 of 5 (one-off migration of the existing IDProof and Photo folders)
  Date written : 2 Nov 2026         Status: DESIGN DRAFT. NOT EXECUTED ANYWHERE.
  Target       : SQL Server 2008 R2.
  Flow         : (a) tools\scan_idproof_folder.ps1 -Mode Scan   reads the live IDProof and Photo folders, hashes every file, writes scan.csv
                 (b) export from each yearly Lodge file:  guests-snapshot.csv with ONLY  FYCode, GuestID, IDProofPath, Photpath, IDCode, deleteflag
                     (no names, no phone numbers, no ID numbers - they are not needed to match)
                 (c) this script loads both, classifies every file name, matches to GuestID, writes the plan and the exception reports
                 (d) tools\scan_idproof_folder.ps1 -Mode Copy -Plan plan.csv   COPIES (never moves) files to the new layout and re-hashes
                 (e) section 5 of this script inserts the rows into dbo.IdDocument as CapturedVia = 'MIGRATED'
  The old IDProof and Photo folders are left untouched until the owner signs the reconciliation (05) and the retention period of the
  old folder (suggested 90 days, read-only) has passed.
  File name rules observed in the VB6 code (frmCustomerMaster.SaveRecord, frmRoomBooking.cmdSave_Click, frmMultipalePhotoAndIDProff.cmdAddIDProff_Click,
  frmRoomBooking.StoreMultipalePhoto):
     <GuestID>.jpg                      one ID or photo per guest (overwritten on every re-upload; no history exists)
     <GuestID>-<srno>.jpg               extra documents of one guest (srno 1,2,3...)
     <srno>.jpg                         extra document saved before the guest had an id  -> AMBIGUOUS, reported, never auto-matched
     <TransactionNo>-<GuestID>[-<srno>].jpg   copy made for one stay (StoreMultipalePhoto)  -> ambiguous with the line above, resolved by lookup
     CustomerIDProof.jpg, CustomerPhoto.jpg, CustomerMultipaleIDProof.jpg, CustomerMultipalePhoto.jpg   per-PC work files, no guest
*/
USE LodgeIdDocs;
GO

/* ---------- 1. staging tables (dropped by 99_rollback) ---------- */
IF OBJECT_ID(N'dbo.IdMigScanFile') IS NULL
CREATE TABLE dbo.IdMigScanFile
(
    ScanId       INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    SourceFolder VARCHAR(10)   NOT NULL,              -- 'IDProof' or 'Photo'
    FileName     NVARCHAR(260) NOT NULL,
    SizeBytes    BIGINT        NOT NULL,
    ModifiedAt   DATETIME      NULL,
    Sha256Hex    CHAR(64)      NULL,
    -- filled by section 3
    NameClass    VARCHAR(16)   NULL,                  -- GUEST | GUEST-SR | TRANS-GUEST | SR-ONLY | WORKFILE | UNPARSED
    N1 INT NULL, N2 INT NULL, N3 INT NULL,
    Ext          VARCHAR(8)    NULL,
    QualityFlag  VARCHAR(12)   NULL,                  -- EMPTY | PLACEHOLDER
    MatchStatus  VARCHAR(20)   NULL,                  -- MATCHED | AMBIGUOUS | NO-GUEST | NOT-IN-DB | WORKFILE | EMPTY | PLACEHOLDER | DUPLICATE
    GuestID      INT           NULL,
    SrNo         SMALLINT      NULL,
    DocTypeCode  VARCHAR(12)   NULL,
    Note         NVARCHAR(200) NULL,
    NewFolderRel NVARCHAR(200) NULL,
    NewFileName  NVARCHAR(200) NULL
);
IF OBJECT_ID(N'dbo.IdMigGuest') IS NULL
CREATE TABLE dbo.IdMigGuest
(
    FYCode      VARCHAR(7)  NOT NULL,                 -- '2017-18' ...
    GuestID     INT         NOT NULL,
    IDProofPath NVARCHAR(60) NULL,                    -- bare file name or '-'
    Photpath    NVARCHAR(60) NULL,
    IDCode      INT         NULL,                     -- Lodge IdentityType code
    DeleteFlag  CHAR(1)     NULL,
    CONSTRAINT PK_IdMigGuest PRIMARY KEY (FYCode, GuestID)
);
GO

/* ---------- 2. load (paths are examples; run from the DBA's PC, UTF-8 CSV with header row) ---------- */
-- BULK INSERT dbo.IdMigScanFile_Load FROM 'D:\IdMigration\scan.csv'            WITH (FIRSTROW = 2, FIELDTERMINATOR = ',', ROWTERMINATOR = '\n', CODEPAGE = '65001');
-- BULK INSERT dbo.IdMigGuest_Load    FROM 'D:\IdMigration\guests-snapshot.csv' WITH (FIRSTROW = 2, FIELDTERMINATOR = ',', ROWTERMINATOR = '\n', CODEPAGE = '65001');
-- (CODEPAGE 65001 is not supported by BULK INSERT on 2008 R2: save the CSVs as ANSI/1252 - file names are ASCII - or use the import wizard.)
-- Load into the *_Load heap tables below, then INSERT ... SELECT into the real staging tables.
IF OBJECT_ID(N'dbo.IdMigScanFile_Load') IS NULL
CREATE TABLE dbo.IdMigScanFile_Load (SourceFolder VARCHAR(10), FileName NVARCHAR(260), SizeBytes BIGINT, ModifiedAt VARCHAR(30), Sha256Hex CHAR(64));
IF OBJECT_ID(N'dbo.IdMigGuest_Load') IS NULL
CREATE TABLE dbo.IdMigGuest_Load (FYCode VARCHAR(7), GuestID INT, IDProofPath NVARCHAR(60), Photpath NVARCHAR(60), IDCode INT, DeleteFlag CHAR(1));
GO
-- after the BULK INSERTs:
-- INSERT INTO dbo.IdMigScanFile (SourceFolder, FileName, SizeBytes, ModifiedAt, Sha256Hex)
--   SELECT SourceFolder, FileName, SizeBytes, CONVERT(DATETIME, ModifiedAt, 120), Sha256Hex FROM dbo.IdMigScanFile_Load;
-- INSERT INTO dbo.IdMigGuest SELECT FYCode, GuestID, IDProofPath, Photpath, IDCode, DeleteFlag FROM dbo.IdMigGuest_Load;
GO

/* ---------- 3. classify the names (pure T-SQL, no CLR; works on 2008 R2) ---------- */
-- 3a extension and zero-byte
UPDATE dbo.IdMigScanFile
   SET Ext = LOWER(CASE WHEN CHARINDEX('.', FileName) > 0 THEN RIGHT(FileName, CHARINDEX('.', REVERSE(FileName)) - 1) ELSE '' END);
UPDATE dbo.IdMigScanFile SET QualityFlag = 'EMPTY', MatchStatus = 'EMPTY', Note = N'0-byte file (a failed FileCopy); nothing to migrate' WHERE SizeBytes = 0;

-- 3b work files
UPDATE dbo.IdMigScanFile SET NameClass = 'WORKFILE', MatchStatus = 'WORKFILE', Note = N'per-PC staging file, belongs to no guest'
 WHERE NameClass IS NULL AND LOWER(FileName) IN ('customeridproof.jpg','customerphoto.jpg','customermultipaleidproof.jpg','customermultipalephoto.jpg');

-- 3c numeric patterns: digits only, digits-digits, digits-digits-digits (before the extension)
UPDATE dbo.IdMigScanFile
   SET NameClass = CASE
          WHEN base NOT LIKE '%[^0-9]%' AND LEN(base) > 0                                  THEN 'GUEST'
          WHEN base LIKE '[0-9]%-[0-9]%' AND base NOT LIKE '%-%-%' AND REPLACE(base,'-','') NOT LIKE '%[^0-9]%' THEN 'GUEST-SR'
          WHEN base LIKE '[0-9]%-[0-9]%-[0-9]%' AND REPLACE(base,'-','') NOT LIKE '%[^0-9]%'  THEN 'TRANS-GUEST'
          ELSE 'UNPARSED' END
  FROM (SELECT ScanId AS id2, LEFT(FileName, LEN(FileName) - LEN(Ext) - 1) AS base FROM dbo.IdMigScanFile WHERE NameClass IS NULL AND Ext <> '') b
 WHERE ScanId = b.id2;
GO
-- numeric parts N1, N2, N3 (safe: only rows that passed the digit test)
UPDATE s SET N1 = CONVERT(INT, LEFT(b.base, ISNULL(NULLIF(CHARINDEX('-', b.base), 0) - 1, LEN(b.base))))
  FROM dbo.IdMigScanFile s CROSS APPLY (SELECT LEFT(s.FileName, LEN(s.FileName) - LEN(s.Ext) - 1) AS base) b
 WHERE s.NameClass IN ('GUEST','GUEST-SR','TRANS-GUEST') AND LEN(LEFT(b.base, ISNULL(NULLIF(CHARINDEX('-', b.base), 0) - 1, LEN(b.base)))) <= 9;
UPDATE s SET N2 = CONVERT(INT, CASE WHEN CHARINDEX('-', r.rest) > 0 THEN LEFT(r.rest, CHARINDEX('-', r.rest) - 1) ELSE r.rest END)
  FROM dbo.IdMigScanFile s CROSS APPLY (SELECT LEFT(s.FileName, LEN(s.FileName) - LEN(s.Ext) - 1) AS base) b
  CROSS APPLY (SELECT SUBSTRING(b.base, CHARINDEX('-', b.base) + 1, 100) AS rest) r
 WHERE s.NameClass IN ('GUEST-SR','TRANS-GUEST') AND LEN(CASE WHEN CHARINDEX('-', r.rest) > 0 THEN LEFT(r.rest, CHARINDEX('-', r.rest) - 1) ELSE r.rest END) <= 9;
UPDATE s SET N3 = CONVERT(INT, SUBSTRING(r.rest, CHARINDEX('-', r.rest) + 1, 20))
  FROM dbo.IdMigScanFile s CROSS APPLY (SELECT LEFT(s.FileName, LEN(s.FileName) - LEN(s.Ext) - 1) AS base) b
  CROSS APPLY (SELECT SUBSTRING(b.base, CHARINDEX('-', b.base) + 1, 100) AS rest) r
 WHERE s.NameClass = 'TRANS-GUEST' AND LEN(SUBSTRING(r.rest, CHARINDEX('-', r.rest) + 1, 20)) <= 9;
GO

/* ---------- 4. match ---------- */
-- 4a placeholder: the 17186-byte stock picture that sits under 31 of 37 files in the sample IDProof folder.
--    Any hash that appears on more than 5 different guests is treated as a placeholder, not as a document.
UPDATE s SET QualityFlag = 'PLACEHOLDER', MatchStatus = 'PLACEHOLDER', Note = N'same bytes appear on many guests (stock picture)'
  FROM dbo.IdMigScanFile s
 WHERE s.MatchStatus IS NULL AND s.Sha256Hex IN (SELECT Sha256Hex FROM dbo.IdMigScanFile WHERE Sha256Hex IS NOT NULL AND SizeBytes > 0 GROUP BY Sha256Hex HAVING COUNT(*) > 5);

-- 4b GUEST  : <GuestID>.<ext>   -> guest must exist in at least one FY snapshot and the snapshot must name this file
UPDATE s SET GuestID = s.N1, SrNo = 1,
             MatchStatus = CASE WHEN EXISTS (SELECT 1 FROM dbo.IdMigGuest g WHERE g.GuestID = s.N1
                                              AND ((s.SourceFolder = 'IDProof' AND g.IDProofPath = s.FileName) OR (s.SourceFolder = 'Photo' AND g.Photpath = s.FileName)))
                                THEN 'MATCHED'
                                WHEN EXISTS (SELECT 1 FROM dbo.IdMigGuest g WHERE g.GuestID = s.N1) THEN 'NO-GUEST'     -- guest exists but no year points at this file
                                ELSE 'NOT-IN-DB' END
  FROM dbo.IdMigScanFile s WHERE s.MatchStatus IS NULL AND s.NameClass = 'GUEST';

-- 4c GUEST-SR : <GuestID>-<srno>.<ext>  (srno small) -> matched if guest exists; the other reading is TRANS-GUEST (4d)
UPDATE s SET GuestID = s.N1, SrNo = s.N2, MatchStatus = 'MATCHED', Note = N'extra document of guest (GuestID-srno)'
  FROM dbo.IdMigScanFile s
 WHERE s.MatchStatus IS NULL AND s.NameClass = 'GUEST-SR' AND s.N2 BETWEEN 1 AND 20
   AND EXISTS (SELECT 1 FROM dbo.IdMigGuest g WHERE g.GuestID = s.N1);
-- 4d GUEST-SR read as <TransactionNo>-<GuestID>: second number is a guest and first is not a guest, or srno is too large
UPDATE s SET GuestID = s.N2, SrNo = 1, MatchStatus = 'AMBIGUOUS', Note = N'read as TransactionNo-GuestID; owner to confirm'
  FROM dbo.IdMigScanFile s
 WHERE s.MatchStatus IS NULL AND s.NameClass = 'GUEST-SR'
   AND EXISTS (SELECT 1 FROM dbo.IdMigGuest g WHERE g.GuestID = s.N2);
-- 4e TRANS-GUEST : <TransactionNo>-<GuestID>-<srno>
UPDATE s SET GuestID = s.N2, SrNo = s.N3, MatchStatus = 'MATCHED', Note = N'stay copy (TransactionNo-GuestID-srno)'
  FROM dbo.IdMigScanFile s
 WHERE s.MatchStatus IS NULL AND s.NameClass = 'TRANS-GUEST' AND EXISTS (SELECT 1 FROM dbo.IdMigGuest g WHERE g.GuestID = s.N2);
-- 4f everything else
UPDATE dbo.IdMigScanFile SET MatchStatus = 'AMBIGUOUS', Note = ISNULL(Note, N'name does not follow any Lodge rule') WHERE MatchStatus IS NULL;

-- 4g same bytes under two matched guests = probable copy/paste error at the desk: flag, do not block
UPDATE s SET Note = ISNULL(Note, N'') + N' | same bytes as another guest (<=5 guests): check'
  FROM dbo.IdMigScanFile s
 WHERE s.MatchStatus = 'MATCHED' AND s.Sha256Hex IN (SELECT Sha256Hex FROM dbo.IdMigScanFile WHERE MatchStatus = 'MATCHED' GROUP BY Sha256Hex HAVING COUNT(DISTINCT GuestID) > 1);

-- 4h document type: from the guest's IDCode in the newest FY snapshot; Photo folder = PHOTO; unknown = LEGACY
UPDATE s SET DocTypeCode = CASE WHEN s.SourceFolder = 'Photo' THEN 'PHOTO'
                                ELSE ISNULL((SELECT t.DocTypeCode FROM dbo.IdDocType t
                                              WHERE t.LegacyIdentityCode = (SELECT TOP 1 g.IDCode FROM dbo.IdMigGuest g WHERE g.GuestID = s.GuestID ORDER BY g.FYCode DESC)), 'LEGACY') END
  FROM dbo.IdMigScanFile s WHERE s.MatchStatus IN ('MATCHED','AMBIGUOUS') AND s.GuestID IS NOT NULL;

-- 4i the new place: <GuestID/1000>\<GuestID>\<GuestID>-<TYPE>-<sr>-1.<ext>   (IdDocumentId is not known yet, so it is not in the name)
UPDATE dbo.IdMigScanFile
   SET NewFolderRel = CONVERT(NVARCHAR(12), GuestID / 1000) + N'\' + CONVERT(NVARCHAR(12), GuestID),
       NewFileName  = CONVERT(NVARCHAR(12), GuestID) + N'-' + DocTypeCode + N'-' + CONVERT(NVARCHAR(4), SrNo) + N'-1.' + Ext
 WHERE MatchStatus = 'MATCHED';
GO

/* ---------- 5. reports (give these CSVs to the owner) ---------- */
-- 5a summary
SELECT SourceFolder, MatchStatus, COUNT(*) AS Files, SUM(SizeBytes) AS Bytes FROM dbo.IdMigScanFile GROUP BY SourceFolder, MatchStatus ORDER BY SourceFolder, MatchStatus;
-- 5b database says a file exists but the folder has no such file (the 5,053 IDProofPath values minus what was collected)
SELECT g.FYCode, g.GuestID, 'IDProof' AS SourceFolder, g.IDProofPath AS ExpectedFile
  FROM dbo.IdMigGuest g
 WHERE g.IDProofPath NOT IN ('-', '') AND g.IDProofPath IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.IdMigScanFile s WHERE s.SourceFolder = 'IDProof' AND s.FileName = g.IDProofPath AND s.SizeBytes > 0)
UNION ALL
SELECT g.FYCode, g.GuestID, 'Photo', g.Photpath
  FROM dbo.IdMigGuest g
 WHERE g.Photpath NOT IN ('-', '') AND g.Photpath IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.IdMigScanFile s WHERE s.SourceFolder = 'Photo' AND s.FileName = g.Photpath AND s.SizeBytes > 0)
 ORDER BY 1, 2;
-- 5c files that nothing points at, and files needing the owner's decision
SELECT ScanId, SourceFolder, FileName, SizeBytes, MatchStatus, Note FROM dbo.IdMigScanFile WHERE MatchStatus IN ('AMBIGUOUS','NO-GUEST','NOT-IN-DB') ORDER BY MatchStatus, SourceFolder, FileName;
GO

/* ---------- 6. after the PowerShell Copy step succeeded: register the copied files ---------- */
-- Only rows whose copy was verified (CopyOk = 1 in plan.csv, loaded into dbo.IdMigCopyResult) are registered.
IF OBJECT_ID(N'dbo.IdMigCopyResult') IS NULL
CREATE TABLE dbo.IdMigCopyResult (ScanId INT NOT NULL PRIMARY KEY, CopyOk BIT NOT NULL, NewSha256Hex CHAR(64) NULL, NewSizeBytes BIGINT NULL, Message NVARCHAR(200) NULL);
GO
-- INSERT INTO dbo.IdDocument (GuestID, DocTypeCode, SrNo, SideNo, FolderRel, FileName, OriginalName, FileExt, SizeBytes, Sha256Hex,
--                             CapturedVia, CapturedAt, CapturedBy, IsCurrent, MigratedFrom, QualityFlag)
-- SELECT s.GuestID, s.DocTypeCode, s.SrNo, 1, s.NewFolderRel, s.NewFileName, s.FileName, s.Ext, r.NewSizeBytes, r.NewSha256Hex,
--        'MIGRATED', ISNULL(s.ModifiedAt, GETDATE()), N'MIGRATION', 1, s.SourceFolder + N'\' + s.FileName, NULL
--   FROM dbo.IdMigScanFile s JOIN dbo.IdMigCopyResult r ON r.ScanId = s.ScanId AND r.CopyOk = 1
--  WHERE s.MatchStatus = 'MATCHED'
--    AND NOT EXISTS (SELECT 1 FROM dbo.IdDocument d WHERE d.FolderRel = s.NewFolderRel AND d.FileName = s.NewFileName);
-- Two old files for one guest + type + srno (e.g. IDProof\5.jpg seen in two FY rows) would break UX_IdDocument_Current:
-- the newest ModifiedAt wins (IsCurrent = 1), the other is inserted with IsCurrent = 0. Resolve with ROW_NUMBER() OVER (PARTITION BY GuestID, DocTypeCode, SrNo ORDER BY ModifiedAt DESC).
-- INSERT INTO dbo.IdDocumentAudit (GuestID, Action, ActionBy, Detail) VALUES (NULL, 'MIGRATE', N'MIGRATION', N'batch registered: see IdDocument.CapturedVia = MIGRATED');
GO
PRINT 'Script 04 loaded (staging + matching + reports). Nothing is registered until section 6 is un-commented and run by the owner.';
GO
