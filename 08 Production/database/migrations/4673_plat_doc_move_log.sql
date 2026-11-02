-- HCC migration 4673 · LF-docdb (contract §1.4, §1.6, §1.7): journal of the idempotent move job and of the orphan sweep.
-- status: 'Move' (migrate), 'Sweep' (daily / on demand), 'Check' (check-documents). No guest data.
CREATE TABLE plat.doc_move_run(
  run_id BIGINT IDENTITY CONSTRAINT PK_doc_move_run PRIMARY KEY,
  started_utc DATETIME2(3) NOT NULL,
  finished_utc DATETIME2(3) NULL,
  moved_rows INT NOT NULL CONSTRAINT DF_doc_move_run_moved DEFAULT 0,
  cleared_rows INT NOT NULL CONSTRAINT DF_doc_move_run_cleared DEFAULT 0,
  remaining_rows INT NOT NULL CONSTRAINT DF_doc_move_run_remaining DEFAULT 0,
  status VARCHAR(12) NOT NULL CONSTRAINT CK_doc_move_run_status CHECK (status IN ('Move', 'Sweep', 'Check', 'Failed')),
  error NVARCHAR(400) NULL);
CREATE INDEX IX_doc_move_run_started ON plat.doc_move_run(status, started_utc DESC);
