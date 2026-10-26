-- HCC migration 3600 · DEC-077 (audit D8, resolves DEC-028): the Owner reopens a closed business day with a reason (audited), corrections
-- post on it, and it is closed again. The barrier row in cls.business_day stays (append-only): "reopened" is a separate, dated record here.
-- Rules (Hcc.Domain.Closing.ReopenRules): Owner only (PERM-CLS-004, close.correction.approve); only a closed day before the business date;
-- never a day locked in Accounting; at most ONE reopened day per property at a time; reopening a day never reopens later days, and the
-- current day cannot be closed until the reopened day is closed again. The DEC-065 cash-book lock of the day is not touched.
-- Re-closing writes a new closing record; the record it replaces is stamped superseded (kept as history, never changed otherwise).

-- One row per reopen; open while reclosed_utc IS NULL. Writers that post on the reopened date read their row WITH (HOLDLOCK), so the
-- re-close (which locks the row exclusively) waits for in-flight corrections, and every later writer sees the day closed again.
CREATE TABLE cls.day_reopen(
  reopen_id BIGINT IDENTITY CONSTRAINT PK_day_reopen PRIMARY KEY,
  property_id INT NOT NULL,
  business_date DATE NOT NULL,
  reopened_by INT NOT NULL,
  reopened_utc DATETIME2(3) NOT NULL,
  reason NVARCHAR(200) NOT NULL CONSTRAINT CK_day_reopen_reason CHECK (LEN(reason) > 0),
  prior_snapshot_id BIGINT NOT NULL CONSTRAINT FK_day_reopen_prior REFERENCES cls.closing_snapshot,  -- the record in force when reopened
  cash_book_locked BIT NOT NULL,         -- the day's DEC-065 cash-book lock at reopen (it stays as it was)
  tally_reexport BIT NOT NULL,           -- the day had been exported to Tally: marked changed for re-export (acc.tally_reexport)
  reclosed_by INT NULL,
  reclosed_utc DATETIME2(3) NULL,
  snapshot_id BIGINT NULL CONSTRAINT FK_day_reopen_snapshot REFERENCES cls.closing_snapshot,       -- the record the re-close wrote
  row_ver ROWVERSION,
  CONSTRAINT FK_day_reopen_day FOREIGN KEY (property_id, business_date) REFERENCES cls.business_day(property_id, business_date),
  CONSTRAINT CK_day_reopen_reclose CHECK ((reclosed_utc IS NULL AND reclosed_by IS NULL AND snapshot_id IS NULL)
                                       OR (reclosed_utc IS NOT NULL AND reclosed_by IS NOT NULL AND snapshot_id IS NOT NULL)));
-- At most one reopened day per property.
CREATE UNIQUE INDEX UX_day_reopen_open ON cls.day_reopen(property_id) WHERE reclosed_utc IS NULL;
-- The writers' barrier read (reclosed_utc included, so the re-close's update takes the same key).
CREATE INDEX IX_day_reopen_day ON cls.day_reopen(property_id, business_date) INCLUDE (reclosed_utc);
GO
-- Closing records: one CURRENT record per date (filtered unique index); a re-close stamps the replaced one.
ALTER TABLE cls.closing_snapshot ADD
  superseded_utc DATETIME2(3) NULL,
  superseded_reopen_id BIGINT NULL CONSTRAINT FK_closing_snapshot_superseded REFERENCES cls.day_reopen(reopen_id),  -- the reopen whose re-close replaced it
  reopen_id BIGINT NULL CONSTRAINT FK_closing_snapshot_reopen REFERENCES cls.day_reopen(reopen_id);               -- the reopen whose re-close wrote it
GO
ALTER TABLE cls.closing_snapshot DROP CONSTRAINT UQ_closing_snapshot_day;
ALTER TABLE cls.closing_snapshot ADD CONSTRAINT CK_closing_snapshot_superseded
  CHECK ((superseded_utc IS NULL AND superseded_reopen_id IS NULL) OR (superseded_utc IS NOT NULL AND superseded_reopen_id IS NOT NULL));
CREATE UNIQUE INDEX UX_closing_snapshot_current ON cls.closing_snapshot(property_id, business_date) WHERE superseded_utc IS NULL;
CREATE INDEX IX_closing_snapshot_day ON cls.closing_snapshot(property_id, business_date, snapshot_id);
GO
-- A stored closing record never changes. The one write allowed (DEC-077) stamps a current record as superseded by the re-close of its
-- reopened day, once; every figure, the JSON and its hash stay exactly as closed.
CREATE OR ALTER TRIGGER cls.TR_closing_snapshot_append_only ON cls.closing_snapshot AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF NOT EXISTS (SELECT 1 FROM deleted) RETURN;
  IF NOT EXISTS (SELECT 1 FROM inserted)
     OR UPDATE(property_id) OR UPDATE(business_date) OR UPDATE(run_id) OR UPDATE(closed_by) OR UPDATE(closed_utc)
     OR UPDATE(net_revenue_paise) OR UPDATE(tax_paise) OR UPDATE(net_receipts_paise) OR UPDATE(cash_expected_paise) OR UPDATE(cash_counted_paise)
     OR UPDATE(occupied_room_nights) OR UPDATE(sellable_room_nights) OR UPDATE(record_json) OR UPDATE(record_sha256) OR UPDATE(reopen_id)
     OR EXISTS (SELECT 1 FROM deleted WHERE superseded_utc IS NOT NULL)
     OR EXISTS (SELECT 1 FROM inserted WHERE superseded_utc IS NULL)
    THROW 51501, 'cls.closing_snapshot is immutable; only the re-close of a reopened day marks the record it replaces (DEC-077).', 1;
END;
GO
CREATE TRIGGER cls.TR_day_reopen_guard ON cls.day_reopen AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF NOT EXISTS (SELECT 1 FROM deleted) RETURN;
  -- Append-only history: a reopen is only ever closed again, once.
  IF NOT EXISTS (SELECT 1 FROM inserted)
     OR UPDATE(property_id) OR UPDATE(business_date) OR UPDATE(reopened_by) OR UPDATE(reopened_utc) OR UPDATE(reason) OR UPDATE(prior_snapshot_id)
     OR UPDATE(cash_book_locked) OR UPDATE(tally_reexport)
     OR EXISTS (SELECT 1 FROM deleted WHERE reclosed_utc IS NOT NULL)
    THROW 51503, 'cls.day_reopen is append-only: a reopened day is only closed again.', 1;
END;
