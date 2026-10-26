-- HCC migration 3920 · System health: backup results (Wave C3, audit A09-05). Every scheduled full / differential backup, pre-migration
-- backup and restore drill records its outcome here at the end of the run (deployment\windows\Backup-HccDatabase.ps1, Restore-HccDrill.ps1,
-- through sqlcmd), so the Owner and the Tech admin see the last backup result in Admin & Setup › System health (GET /admin/system-health).
-- The scripts run as the backup account (default NT AUTHORITY\SYSTEM: db_backupoperator + db_datareader, Initialize-HccDatabase.ps1),
-- which may not write tables: role hcc_backup_reporter may INSERT here and nothing else. Every member of db_backupoperator at migrate time
-- joins it; Initialize-HccDatabase.ps1 adds the backup account again whenever it is re-run. A failed or missing write never fails a backup
-- (the script logs a warning). No guest data: kind, times (UTC), file name, size and the failure reason (script text or SQL error numbers).
CREATE TABLE plat.backup_run(
  run_id BIGINT IDENTITY CONSTRAINT PK_backup_run PRIMARY KEY,
  kind VARCHAR(20) NOT NULL CONSTRAINT CK_backup_run_kind CHECK (kind IN ('Full', 'Differential', 'PreMigration', 'RestoreDrill')),
  succeeded BIT NOT NULL,
  started_utc DATETIME2(0) NOT NULL,
  finished_utc DATETIME2(0) NOT NULL,
  file_name NVARCHAR(260) NULL,
  size_bytes BIGINT NULL CONSTRAINT CK_backup_run_size CHECK (size_bytes IS NULL OR size_bytes >= 0),
  reason NVARCHAR(400) NULL,
  host_name NVARCHAR(64) NULL,
  recorded_utc DATETIME2(3) NOT NULL CONSTRAINT DF_backup_run_recorded DEFAULT SYSUTCDATETIME(),
  CONSTRAINT CK_backup_run_times CHECK (finished_utc >= started_utc));
CREATE INDEX IX_backup_run_kind ON plat.backup_run(kind, finished_utc DESC, run_id DESC) INCLUDE (succeeded);
GO
CREATE ROLE hcc_backup_reporter;
GO
GRANT INSERT ON plat.backup_run TO hcc_backup_reporter;
GO
-- The backup account(s) already created by Initialize-HccDatabase.ps1 (members of db_backupoperator) may now record their results.
DECLARE @member SYSNAME, @sql NVARCHAR(400);
DECLARE members CURSOR LOCAL FAST_FORWARD FOR
  SELECT u.name FROM sys.database_role_members m
  JOIN sys.database_principals r ON r.principal_id = m.role_principal_id
  JOIN sys.database_principals u ON u.principal_id = m.member_principal_id
  WHERE r.name = N'db_backupoperator' AND u.type IN ('U', 'G', 'S');
OPEN members;
FETCH NEXT FROM members INTO @member;
WHILE @@FETCH_STATUS = 0
BEGIN
  SET @sql = N'ALTER ROLE hcc_backup_reporter ADD MEMBER ' + QUOTENAME(@member) + N';';
  EXEC sys.sp_executesql @sql;
  FETCH NEXT FROM members INTO @member;
END;
CLOSE members;
DEALLOCATE members;
