/*
  Lodge ID document patch - script 99 (rollback)
  Date written : 2 Nov 2026         Status: DESIGN DRAFT. NOT EXECUTED ANYWHERE.   Target: SQL Server 2008 R2.
  What rollback means here: the Lodge EXE goes back to the old build (old modIdDocs-free Hotel.vbp) which only reads
  App.Path\IDProof and App.Path\Photo. Because the migration COPIES and never moves, those folders still hold every old file, and
  CustomerInfo.IDProofPath / Photpath were never changed by the patch. So the data rollback is safe at any time.
  Files that were uploaded AFTER go-live exist only in the new store. Script 99a exports them first so nothing is lost.
*/

/* ---------- 99a: list the post-go-live documents that exist only in the new store (give this CSV to the vendor / keep it) ---------- */
USE LodgeIdDocs;
GO
SELECT IdDocumentId, GuestID, DocTypeCode, SrNo, FolderRel, FileName, SizeBytes, Sha256Hex, CapturedAt, CapturedBy
  FROM dbo.IdDocument
 WHERE CapturedVia <> 'MIGRATED' AND IsDeleted = 0 AND IsCurrent = 1
 ORDER BY CapturedAt;
-- then copy those files back to the old layout (GuestID.jpg in IDProof / Photo) with  tools\scan_idproof_folder.ps1 -Mode Export -Plan <this csv>
GO

/* ---------- 99b: remove the objects. Refuses to run if post-go-live rows exist and @Force = 0 ---------- */
DECLARE @Force BIT; SET @Force = 0;          -- set to 1 only after 99a was exported and checked
IF @Force = 0 AND EXISTS (SELECT 1 FROM dbo.IdDocument WHERE CapturedVia <> 'MIGRATED')
BEGIN
    RAISERROR (N'Rows captured after go-live exist. Export them (99a) and set @Force = 1.', 16, 1);
    RETURN;
END
IF OBJECT_ID(N'dbo.IdMigCopyResult')     IS NOT NULL DROP TABLE dbo.IdMigCopyResult;
IF OBJECT_ID(N'dbo.IdMigScanFile_Load')  IS NOT NULL DROP TABLE dbo.IdMigScanFile_Load;
IF OBJECT_ID(N'dbo.IdMigGuest_Load')     IS NOT NULL DROP TABLE dbo.IdMigGuest_Load;
IF OBJECT_ID(N'dbo.IdMigScanFile')       IS NOT NULL DROP TABLE dbo.IdMigScanFile;
IF OBJECT_ID(N'dbo.IdMigGuest')          IS NOT NULL DROP TABLE dbo.IdMigGuest;
IF OBJECT_ID(N'dbo.usp_IdDocument_Add')       IS NOT NULL DROP PROCEDURE dbo.usp_IdDocument_Add;
IF OBJECT_ID(N'dbo.usp_IdDocument_List')      IS NOT NULL DROP PROCEDURE dbo.usp_IdDocument_List;
IF OBJECT_ID(N'dbo.usp_IdDocument_Open')      IS NOT NULL DROP PROCEDURE dbo.usp_IdDocument_Open;
IF OBJECT_ID(N'dbo.usp_IdDocument_SoftDelete') IS NOT NULL DROP PROCEDURE dbo.usp_IdDocument_SoftDelete;
IF OBJECT_ID(N'dbo.usp_IdDocument_Restore')   IS NOT NULL DROP PROCEDURE dbo.usp_IdDocument_Restore;
IF OBJECT_ID(N'dbo.usp_IdDocument_LogPrint')  IS NOT NULL DROP PROCEDURE dbo.usp_IdDocument_LogPrint;
IF OBJECT_ID(N'dbo.trg_IdDocument_NoDelete')  IS NOT NULL DROP TRIGGER dbo.trg_IdDocument_NoDelete;
IF OBJECT_ID(N'dbo.trg_IdDocument_Guard')     IS NOT NULL DROP TRIGGER dbo.trg_IdDocument_Guard;
IF OBJECT_ID(N'dbo.trg_IdDocumentAudit_AppendOnly') IS NOT NULL DROP TRIGGER dbo.trg_IdDocumentAudit_AppendOnly;
-- The audit and register tables are the evidence of who saw what. They are NOT dropped here. Keep the database (or a final .bak) for the retention period.
-- To remove them as well (owner decision, after a .bak is stored offline):
--   DROP TABLE dbo.IdDocumentAudit; DROP TABLE dbo.IdDocument; DROP TABLE dbo.IdDocSetting; DROP TABLE dbo.IdDocType;
--   then, from master:  ALTER DATABASE LodgeIdDocs SET SINGLE_USER WITH ROLLBACK IMMEDIATE;  DROP DATABASE LodgeIdDocs;
PRINT 'Rollback 99b finished: procedures, triggers and staging removed; register and audit tables kept.';
GO
