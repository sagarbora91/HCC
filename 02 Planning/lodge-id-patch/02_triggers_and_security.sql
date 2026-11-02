/*
  Lodge ID document patch - script 02 of 5 (triggers, roles, permissions)
  Date written : 2 Nov 2026         Status: DESIGN DRAFT. NOT EXECUTED ANYWHERE.
  Target       : SQL Server 2008 R2.  Run after 01 and BEFORE 03 (procedures grant EXEC to the roles made here).
  Idea         : nobody touches the tables directly. The Lodge login gets two roles that can only run stored procedures.
                 Hard DELETE is blocked by a trigger even for owners of the table, so a mistake or a bug cannot erase the register.
*/
USE LodgeIdDocs;
GO

/* ---------- no hard delete, no tampering with what was stored ---------- */
IF OBJECT_ID(N'dbo.trg_IdDocument_NoDelete') IS NOT NULL DROP TRIGGER dbo.trg_IdDocument_NoDelete;
GO
CREATE TRIGGER dbo.trg_IdDocument_NoDelete ON dbo.IdDocument INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    RAISERROR (N'IdDocument rows cannot be deleted. Use dbo.usp_IdDocument_SoftDelete (sets IsDeleted and writes the audit).', 16, 1);
    -- INSTEAD OF: nothing is deleted
END
GO
IF OBJECT_ID(N'dbo.trg_IdDocument_Guard') IS NOT NULL DROP TRIGGER dbo.trg_IdDocument_Guard;
GO
CREATE TRIGGER dbo.trg_IdDocument_Guard ON dbo.IdDocument AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    -- identity of the stored file may never change after insert
    IF UPDATE(GuestID) OR UPDATE(FolderRel) OR UPDATE(FileName) OR UPDATE(Sha256Hex) OR UPDATE(SizeBytes) OR UPDATE(CapturedAt) OR UPDATE(CapturedBy)
    BEGIN
        -- allow the one-time hash back-fill on migrated rows: old hash NULL -> new value
        IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.IdDocumentId = i.IdDocumentId
                   WHERE i.GuestID <> d.GuestID OR i.FolderRel <> d.FolderRel OR i.FileName <> d.FileName
                      OR i.CapturedAt <> d.CapturedAt OR i.CapturedBy <> d.CapturedBy
                      OR (d.Sha256Hex IS NOT NULL AND (i.Sha256Hex IS NULL OR i.Sha256Hex <> d.Sha256Hex))   -- once a hash exists it is fixed
                      OR (d.Sha256Hex IS NOT NULL AND i.SizeBytes <> d.SizeBytes))                           -- size may be corrected only while no hash exists
        BEGIN
            RAISERROR (N'IdDocument identity columns (guest, folder, file name, hash, size, captured at/by) are write-once.', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END
    END
END
GO
IF OBJECT_ID(N'dbo.trg_IdDocumentAudit_AppendOnly') IS NOT NULL DROP TRIGGER dbo.trg_IdDocumentAudit_AppendOnly;
GO
CREATE TRIGGER dbo.trg_IdDocumentAudit_AppendOnly ON dbo.IdDocumentAudit INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    RAISERROR (N'IdDocumentAudit is append-only.', 16, 1);
END
GO

/* ---------- roles ---------- */
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'LodgeIdViewer' AND type = 'R') CREATE ROLE LodgeIdViewer;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'LodgeIdEditor' AND type = 'R') CREATE ROLE LodgeIdEditor;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'LodgeIdAdmin'  AND type = 'R') CREATE ROLE LodgeIdAdmin;   -- owner only: restore, export, migrate
GO
-- no direct table access for anyone but db_owner
DENY SELECT, INSERT, UPDATE, DELETE ON dbo.IdDocument      TO LodgeIdViewer, LodgeIdEditor, LodgeIdAdmin;
DENY SELECT, INSERT, UPDATE, DELETE ON dbo.IdDocumentAudit TO LodgeIdViewer, LodgeIdEditor, LodgeIdAdmin;
GRANT SELECT ON dbo.IdDocType    TO LodgeIdViewer, LodgeIdEditor, LodgeIdAdmin;
GRANT SELECT ON dbo.IdDocSetting TO LodgeIdViewer, LodgeIdEditor, LodgeIdAdmin;
GO
/*
  Logins are created by the DBA, NOT in this script (no passwords in scripts):
     CREATE LOGIN [LodgeIdApp] WITH PASSWORD = '<set by the DBA>';           -- the one login the Lodge exe uses for this database
     CREATE USER  [LodgeIdApp] FOR LOGIN [LodgeIdApp];
     EXEC sp_addrolemember N'LodgeIdEditor', N'LodgeIdApp';
  Lodge today connects with the shared 'sa' login in SQL mode (DatabaseType.dll line 3). Do NOT reuse it for this database.
  If the site stays on Access, the equivalent is the folder ACL + the separate LodgeIdDocs.mdb password (see design doc, section 8).
*/
PRINT 'Script 02 done (triggers + roles).';
GO
