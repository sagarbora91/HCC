-- HCC migration 4671 · LF-docdb (contract §1.4, §1.9): every backup / drill row names its database ('Hcc' or 'HccDocuments').
ALTER TABLE plat.backup_run ADD database_name NVARCHAR(128) NOT NULL CONSTRAINT DF_backup_run_database DEFAULT N'Hcc';
GO
CREATE INDEX IX_backup_run_database ON plat.backup_run(database_name, started_utc DESC);
