/*
  Lodge ID document patch - script 03 of 5 (stored procedures)
  Date written : 2 Nov 2026         Status: DESIGN DRAFT. NOT EXECUTED ANYWHERE.
  Target       : SQL Server 2008 R2 (TRY/CATCH and RAISERROR only; no THROW).
  VB6 calls    : dbDocs.Execute / ADODB.Command with these procedures only (see vb6\modIdDocs.bas).
  The procedures do not write files. VB6 writes the file first, checks size and hash, THEN calls usp_IdDocument_Add.
  If the call fails VB6 deletes the file it just wrote, so no file exists without a row. The nightly check in 05 finds the opposite
  case (row without file) and any stray file.
*/
USE LodgeIdDocs;
GO

/* ---------- add (or replace) a document ---------- */
IF OBJECT_ID(N'dbo.usp_IdDocument_Add') IS NOT NULL DROP PROCEDURE dbo.usp_IdDocument_Add;
GO
CREATE PROCEDURE dbo.usp_IdDocument_Add
    @GuestID       INT,
    @FYCode        VARCHAR(7)    = NULL,
    @BookingID     INT           = NULL,
    @TransactionID INT           = NULL,
    @DocTypeCode   VARCHAR(12),
    @SrNo          SMALLINT      = 1,
    @SideNo        TINYINT       = 1,
    @FolderRel     NVARCHAR(200),
    @FileName      NVARCHAR(200),
    @OriginalName  NVARCHAR(260) = NULL,
    @FileExt       VARCHAR(8),
    @SizeBytes     INT,
    @Sha256Hex     CHAR(64)      = NULL,
    @CapturedVia   VARCHAR(10),
    @User          NVARCHAR(60),
    @Workstation   NVARCHAR(60)  = NULL,
    @NewId         INT           OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @maxBytes INT, @allowed NVARCHAR(400), @action VARCHAR(12), @oldId INT;

    SELECT @maxBytes = CONVERT(INT, SettingValue) FROM dbo.IdDocSetting WHERE SettingKey = 'MaxBytes';
    SELECT @allowed  = N',' + SettingValue + N','  FROM dbo.IdDocSetting WHERE SettingKey = 'AllowedExt';

    IF @GuestID IS NULL OR @GuestID <= 0                       BEGIN RAISERROR (N'GuestID is required.', 16, 1); RETURN 1; END
    IF NOT EXISTS (SELECT 1 FROM dbo.IdDocType WHERE DocTypeCode = @DocTypeCode AND IsActive = 1)
                                                               BEGIN RAISERROR (N'Unknown or inactive document type.', 16, 1); RETURN 1; END
    IF @SizeBytes < 1 OR @SizeBytes > ISNULL(@maxBytes, 8388608)
                                                               BEGIN RAISERROR (N'File is empty or larger than the allowed size.', 16, 1); RETURN 1; END
    IF CHARINDEX(N',' + LOWER(@FileExt) + N',', LOWER(ISNULL(@allowed, N''))) = 0
                                                               BEGIN RAISERROR (N'File type not allowed.', 16, 1); RETURN 1; END
    IF @FolderRel LIKE N'%..%' OR @FolderRel LIKE N':%' OR @FolderRel LIKE N'\\%' OR @FileName LIKE N'%[\/:*?"<>|]%'
                                                               BEGIN RAISERROR (N'Folder must be relative and the file name must be a plain name.', 16, 1); RETURN 1; END

    BEGIN TRY
        BEGIN TRANSACTION;

        SELECT @oldId = IdDocumentId FROM dbo.IdDocument
         WHERE GuestID = @GuestID AND DocTypeCode = @DocTypeCode AND SrNo = @SrNo AND SideNo = @SideNo AND IsCurrent = 1 AND IsDeleted = 0;

        IF @oldId IS NOT NULL
        BEGIN
            UPDATE dbo.IdDocument SET IsCurrent = 0 WHERE IdDocumentId = @oldId;   -- old file stays on disk, history kept
            SET @action = 'REPLACE';
        END
        ELSE SET @action = 'ADD';

        INSERT INTO dbo.IdDocument
            (GuestID, FYCode, BookingID, TransactionID, DocTypeCode, SrNo, SideNo, FolderRel, FileName, OriginalName, FileExt,
             SizeBytes, Sha256Hex, CapturedVia, CapturedBy, Workstation)
        VALUES
            (@GuestID, @FYCode, @BookingID, @TransactionID, @DocTypeCode, @SrNo, @SideNo, @FolderRel, @FileName, @OriginalName, LOWER(@FileExt),
             @SizeBytes, @Sha256Hex, @CapturedVia, @User, @Workstation);
        SET @NewId = SCOPE_IDENTITY();

        INSERT INTO dbo.IdDocumentAudit (IdDocumentId, GuestID, Action, ActionBy, Workstation, Detail)
        VALUES (@NewId, @GuestID, @action, @User, @Workstation,
                LEFT(N'type=' + @DocTypeCode + N' sr=' + CONVERT(NVARCHAR(10), @SrNo) + N' via=' + @CapturedVia + CASE WHEN @oldId IS NULL THEN N'' ELSE N' replaced=' + CONVERT(NVARCHAR(12), @oldId) END, 400));

        COMMIT TRANSACTION;
        RETURN 0;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        DECLARE @msg NVARCHAR(400); SET @msg = LEFT(ERROR_MESSAGE(), 400);
        INSERT INTO dbo.IdDocumentAudit (GuestID, Action, ActionBy, Workstation, Detail) VALUES (@GuestID, 'ERROR', @User, @Workstation, @msg);
        RAISERROR (@msg, 16, 1);
        RETURN 2;
    END CATCH
END
GO

/* ---------- list the documents of a guest (current + optionally history) ---------- */
IF OBJECT_ID(N'dbo.usp_IdDocument_List') IS NOT NULL DROP PROCEDURE dbo.usp_IdDocument_List;
GO
CREATE PROCEDURE dbo.usp_IdDocument_List
    @GuestID INT,
    @IncludeHistory BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT d.IdDocumentId, d.GuestID, d.FYCode, d.BookingID, d.DocTypeCode, t.DocTypeName, d.SrNo, d.SideNo,
           d.FolderRel, d.FileName, d.FileExt, d.SizeBytes, d.Sha256Hex, d.CapturedVia, d.CapturedAt, d.CapturedBy,
           d.IsCurrent, d.IsDeleted, d.QualityFlag
      FROM dbo.IdDocument d JOIN dbo.IdDocType t ON t.DocTypeCode = d.DocTypeCode
     WHERE d.GuestID = @GuestID
       AND d.IsDeleted = 0
       AND (d.IsCurrent = 1 OR @IncludeHistory = 1)
     ORDER BY t.SortOrder, d.SrNo, d.SideNo, d.IdDocumentId DESC;
END
GO

/* ---------- one document, for opening it. Writes a VIEW audit row every time. ---------- */
IF OBJECT_ID(N'dbo.usp_IdDocument_Open') IS NOT NULL DROP PROCEDURE dbo.usp_IdDocument_Open;
GO
CREATE PROCEDURE dbo.usp_IdDocument_Open
    @IdDocumentId INT,
    @User         NVARCHAR(60),
    @Workstation  NVARCHAR(60) = NULL,
    @Purpose      NVARCHAR(100) = NULL      -- 'CHECKIN', 'BILL PRINT', 'SEARCH' ... shown in the audit
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @g INT;
    SELECT @g = GuestID FROM dbo.IdDocument WHERE IdDocumentId = @IdDocumentId AND IsDeleted = 0;
    IF @g IS NULL
    BEGIN
        INSERT INTO dbo.IdDocumentAudit (IdDocumentId, Action, ActionBy, Workstation, Detail) VALUES (@IdDocumentId, 'DENIED', @User, @Workstation, N'no such live document');
        RAISERROR (N'Document not found.', 16, 1);
        RETURN 1;
    END
    INSERT INTO dbo.IdDocumentAudit (IdDocumentId, GuestID, Action, ActionBy, Workstation, Detail) VALUES (@IdDocumentId, @g, 'VIEW', @User, @Workstation, @Purpose);
    SELECT IdDocumentId, GuestID, FolderRel, FileName, FileExt, SizeBytes, Sha256Hex FROM dbo.IdDocument WHERE IdDocumentId = @IdDocumentId;
END
GO

/* ---------- soft delete and restore ---------- */
IF OBJECT_ID(N'dbo.usp_IdDocument_SoftDelete') IS NOT NULL DROP PROCEDURE dbo.usp_IdDocument_SoftDelete;
GO
CREATE PROCEDURE dbo.usp_IdDocument_SoftDelete
    @IdDocumentId INT, @User NVARCHAR(60), @Reason NVARCHAR(200), @Workstation NVARCHAR(60) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Reason IS NULL OR LEN(LTRIM(RTRIM(@Reason))) < 5 BEGIN RAISERROR (N'A reason of at least 5 characters is required.', 16, 1); RETURN 1; END
    UPDATE dbo.IdDocument SET IsDeleted = 1, DeletedAt = GETDATE(), DeletedBy = @User, DeleteReason = @Reason
     WHERE IdDocumentId = @IdDocumentId AND IsDeleted = 0;
    IF @@ROWCOUNT = 0 BEGIN RAISERROR (N'Nothing to delete.', 16, 1); RETURN 1; END
    INSERT INTO dbo.IdDocumentAudit (IdDocumentId, GuestID, Action, ActionBy, Workstation, Detail)
    SELECT IdDocumentId, GuestID, 'DELETE', @User, @Workstation, @Reason FROM dbo.IdDocument WHERE IdDocumentId = @IdDocumentId;
END
GO
IF OBJECT_ID(N'dbo.usp_IdDocument_Restore') IS NOT NULL DROP PROCEDURE dbo.usp_IdDocument_Restore;
GO
CREATE PROCEDURE dbo.usp_IdDocument_Restore
    @IdDocumentId INT, @User NVARCHAR(60), @Workstation NVARCHAR(60) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.IdDocument SET IsDeleted = 0, DeletedAt = NULL, DeletedBy = NULL, DeleteReason = NULL WHERE IdDocumentId = @IdDocumentId AND IsDeleted = 1;
    IF @@ROWCOUNT = 0 BEGIN RAISERROR (N'Nothing to restore (or a newer current document now holds that slot).', 16, 1); RETURN 1; END
    INSERT INTO dbo.IdDocumentAudit (IdDocumentId, GuestID, Action, ActionBy, Workstation)
    SELECT IdDocumentId, GuestID, 'RESTORE', @User, @Workstation FROM dbo.IdDocument WHERE IdDocumentId = @IdDocumentId;
END
GO

/* ---------- print event (bill print and registration card print read the file into Crystal) ---------- */
IF OBJECT_ID(N'dbo.usp_IdDocument_LogPrint') IS NOT NULL DROP PROCEDURE dbo.usp_IdDocument_LogPrint;
GO
CREATE PROCEDURE dbo.usp_IdDocument_LogPrint
    @IdDocumentId INT, @User NVARCHAR(60), @Workstation NVARCHAR(60) = NULL, @ReportName NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.IdDocumentAudit (IdDocumentId, GuestID, Action, ActionBy, Workstation, Detail)
    SELECT IdDocumentId, GuestID, 'PRINT', @User, @Workstation, @ReportName FROM dbo.IdDocument WHERE IdDocumentId = @IdDocumentId;
END
GO

/* ---------- permissions: procedures only ---------- */
GRANT EXECUTE ON dbo.usp_IdDocument_List      TO LodgeIdViewer, LodgeIdEditor, LodgeIdAdmin;
GRANT EXECUTE ON dbo.usp_IdDocument_Open      TO LodgeIdViewer, LodgeIdEditor, LodgeIdAdmin;
GRANT EXECUTE ON dbo.usp_IdDocument_LogPrint  TO LodgeIdViewer, LodgeIdEditor, LodgeIdAdmin;
GRANT EXECUTE ON dbo.usp_IdDocument_Add       TO LodgeIdEditor, LodgeIdAdmin;
GRANT EXECUTE ON dbo.usp_IdDocument_SoftDelete TO LodgeIdAdmin;       -- delete is an owner action
GRANT EXECUTE ON dbo.usp_IdDocument_Restore   TO LodgeIdAdmin;
GO
PRINT 'Script 03 done (procedures).';
GO
