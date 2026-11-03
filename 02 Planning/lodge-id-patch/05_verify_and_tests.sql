/*
  Lodge ID document patch - script 05 of 5 (checks and tests)
  Date written : 2 Nov 2026         Status: DESIGN DRAFT. NOT EXECUTED ANYWHERE.   Target: SQL Server 2008 R2.
  Run on a COPY of the database first (test plan T-DB-*). Every test prints PASS or FAIL.
*/
USE LodgeIdDocs;
GO
SET NOCOUNT ON;
DECLARE @id1 INT, @id2 INT, @n INT, @err NVARCHAR(400);

/* T-DB-01 add */
EXEC dbo.usp_IdDocument_Add @GuestID = 999001, @DocTypeCode = 'AADHAAR', @FolderRel = N'999\999001', @FileName = N'999001-AADHAAR-1-1.jpg',
     @FileExt = 'jpg', @SizeBytes = 1000, @Sha256Hex = '0000000000000000000000000000000000000000000000000000000000000001',
     @CapturedVia = 'FILE', @User = N'TEST', @NewId = @id1 OUTPUT;
SELECT CASE WHEN @id1 IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.IdDocumentAudit WHERE IdDocumentId = @id1 AND Action = 'ADD') THEN 'PASS' ELSE 'FAIL' END AS [T-DB-01 add + audit];

/* T-DB-02 replace keeps history: second upload for the same slot */
EXEC dbo.usp_IdDocument_Add @GuestID = 999001, @DocTypeCode = 'AADHAAR', @FolderRel = N'999\999001', @FileName = N'999001-AADHAAR-1-2.jpg',
     @FileExt = 'jpg', @SizeBytes = 2000, @Sha256Hex = '0000000000000000000000000000000000000000000000000000000000000002',
     @CapturedVia = 'WEBCAM', @User = N'TEST', @NewId = @id2 OUTPUT;
SELECT CASE WHEN (SELECT IsCurrent FROM dbo.IdDocument WHERE IdDocumentId = @id1) = 0 AND (SELECT IsCurrent FROM dbo.IdDocument WHERE IdDocumentId = @id2) = 1
            AND EXISTS (SELECT 1 FROM dbo.IdDocumentAudit WHERE IdDocumentId = @id2 AND Action = 'REPLACE') THEN 'PASS' ELSE 'FAIL' END AS [T-DB-02 replace];

/* T-DB-03 hard delete is refused */
BEGIN TRY
    DELETE FROM dbo.IdDocument WHERE IdDocumentId = @id1;
    SELECT 'FAIL' AS [T-DB-03 delete blocked];
END TRY
BEGIN CATCH
    SELECT CASE WHEN ERROR_MESSAGE() LIKE '%cannot be deleted%' THEN 'PASS' ELSE 'FAIL' END AS [T-DB-03 delete blocked];
END CATCH

/* T-DB-04 write-once columns */
BEGIN TRY
    UPDATE dbo.IdDocument SET FileName = N'hacked.jpg' WHERE IdDocumentId = @id2;
    SELECT 'FAIL' AS [T-DB-04 write-once];
END TRY
BEGIN CATCH
    SELECT 'PASS' AS [T-DB-04 write-once];
END CATCH

/* T-DB-05 bad input is refused: path trick, empty file, wrong extension */
SET @n = 0;
BEGIN TRY EXEC dbo.usp_IdDocument_Add @GuestID = 999002, @DocTypeCode = 'AADHAAR', @FolderRel = N'..\x', @FileName = N'a.jpg', @FileExt = 'jpg', @SizeBytes = 10, @CapturedVia = 'FILE', @User = N'TEST', @NewId = @id1 OUTPUT; END TRY BEGIN CATCH SET @n = @n + 1; END CATCH
BEGIN TRY EXEC dbo.usp_IdDocument_Add @GuestID = 999002, @DocTypeCode = 'AADHAAR', @FolderRel = N'1\2', @FileName = N'a.jpg', @FileExt = 'jpg', @SizeBytes = 0, @CapturedVia = 'FILE', @User = N'TEST', @NewId = @id1 OUTPUT; END TRY BEGIN CATCH SET @n = @n + 1; END CATCH
BEGIN TRY EXEC dbo.usp_IdDocument_Add @GuestID = 999002, @DocTypeCode = 'AADHAAR', @FolderRel = N'1\2', @FileName = N'a.exe', @FileExt = 'exe', @SizeBytes = 10, @CapturedVia = 'FILE', @User = N'TEST', @NewId = @id1 OUTPUT; END TRY BEGIN CATCH SET @n = @n + 1; END CATCH
SELECT CASE WHEN @n = 3 THEN 'PASS' ELSE 'FAIL' END AS [T-DB-05 validation];

/* T-DB-06 open writes a VIEW audit row; list shows only current + undeleted */
EXEC dbo.usp_IdDocument_Open @IdDocumentId = @id2, @User = N'TEST', @Purpose = N'T-DB-06';
SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.IdDocumentAudit WHERE IdDocumentId = @id2 AND Action = 'VIEW') THEN 'PASS' ELSE 'FAIL' END AS [T-DB-06 view audit];

/* T-DB-07 soft delete + restore */
EXEC dbo.usp_IdDocument_SoftDelete @IdDocumentId = @id2, @User = N'TEST', @Reason = N'test delete';
SELECT CASE WHEN (SELECT IsDeleted FROM dbo.IdDocument WHERE IdDocumentId = @id2) = 1 THEN 'PASS' ELSE 'FAIL' END AS [T-DB-07a soft delete];
EXEC dbo.usp_IdDocument_Restore @IdDocumentId = @id2, @User = N'TEST';
SELECT CASE WHEN (SELECT IsDeleted FROM dbo.IdDocument WHERE IdDocumentId = @id2) = 0 THEN 'PASS' ELSE 'FAIL' END AS [T-DB-07b restore];

/* clean the test rows: tests run on a COPY only; db_owner cannot delete either, so use the copy and drop it afterwards. */
PRINT 'Tests finished. Drop the test copy of the database. Do NOT run these tests on the live LodgeIdDocs (they leave GuestID 999001 rows).';
GO

/* ============ reconciliation queries used after migration and every month (run read-only) ============ */
-- R1 rows whose file is missing is checked by tools\scan_idproof_folder.ps1 -Mode Verify (T-SQL cannot see the share).
-- R2 duplicates of the same bytes under different guests
SELECT Sha256Hex, COUNT(DISTINCT GuestID) AS Guests, COUNT(*) AS Rows FROM dbo.IdDocument WHERE Sha256Hex IS NOT NULL AND IsDeleted = 0 GROUP BY Sha256Hex HAVING COUNT(DISTINCT GuestID) > 1;
-- R3 counts by origin
SELECT CapturedVia, COUNT(*) AS Rows, SUM(CONVERT(BIGINT, SizeBytes)) AS Bytes FROM dbo.IdDocument GROUP BY CapturedVia;
-- R4 who viewed / printed what in the last 30 days (owner's monthly look)
SELECT ActionBy, Action, COUNT(*) AS Events FROM dbo.IdDocumentAudit WHERE ActionAt >= DATEADD(DAY, -30, GETDATE()) GROUP BY ActionBy, Action ORDER BY ActionBy, Action;
-- R5 users with unusual view counts (more than 100 views in a day)
SELECT ActionBy, CONVERT(VARCHAR(10), ActionAt, 120) AS Day, COUNT(*) AS Views FROM dbo.IdDocumentAudit WHERE Action = 'VIEW' GROUP BY ActionBy, CONVERT(VARCHAR(10), ActionAt, 120) HAVING COUNT(*) > 100;
GO
