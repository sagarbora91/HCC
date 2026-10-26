-- HCC migration 3601 · Tally export log and re-export flags (DEC-077). Until now the (simulated) Tally XML export kept no record, so nothing
-- could tell which business dates the accountant had already taken into Tally. Every export now writes one log row plus one row per
-- business date it carried. When the Owner reopens a closed day that had been exported (Closing, migration 3600), the date is flagged
-- changed here; every later export carries a flagged date again (even outside the chosen range) with a RE-EXPORT note in the XML, and
-- the flag is cleared by the first export made after the day was closed again. Append-only history; times written by the application.
CREATE TABLE acc.tally_export(
  export_id BIGINT IDENTITY CONSTRAINT PK_tally_export PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_tally_export_property REFERENCES inv.property,
  from_date DATE NOT NULL,
  to_date DATE NOT NULL,
  voucher_count INT NOT NULL,
  debit_paise BIGINT NOT NULL,
  credit_paise BIGINT NOT NULL,
  exported_by INT NOT NULL,
  exported_utc DATETIME2(3) NOT NULL,
  CONSTRAINT CK_tally_export_range CHECK (from_date <= to_date));
CREATE TABLE acc.tally_export_day(
  export_id BIGINT NOT NULL CONSTRAINT FK_tally_export_day_export REFERENCES acc.tally_export,
  property_id INT NOT NULL,
  business_date DATE NOT NULL,
  voucher_count INT NOT NULL,
  reexport BIT NOT NULL,       -- carried because it was flagged changed after an earlier export
  provisional BIT NOT NULL,    -- the day was still reopened when exported (it may change again; the flag stays)
  CONSTRAINT PK_tally_export_day PRIMARY KEY (export_id, business_date));
CREATE INDEX IX_tally_export_day_date ON acc.tally_export_day(property_id, business_date, export_id);
-- A business date whose vouchers changed after they were exported; open while cleared_export_id IS NULL (one open flag per date).
CREATE TABLE acc.tally_reexport(
  reexport_id BIGINT IDENTITY CONSTRAINT PK_tally_reexport PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_tally_reexport_property REFERENCES inv.property,
  business_date DATE NOT NULL,
  reopen_id BIGINT NULL,                 -- cls.day_reopen that changed it
  reason NVARCHAR(200) NOT NULL,
  marked_utc DATETIME2(3) NOT NULL,
  cleared_export_id BIGINT NULL CONSTRAINT FK_tally_reexport_export REFERENCES acc.tally_export,
  cleared_utc DATETIME2(3) NULL,
  CONSTRAINT CK_tally_reexport_cleared CHECK ((cleared_export_id IS NULL AND cleared_utc IS NULL) OR (cleared_export_id IS NOT NULL AND cleared_utc IS NOT NULL)));
CREATE UNIQUE INDEX UX_tally_reexport_open ON acc.tally_reexport(property_id, business_date) WHERE cleared_export_id IS NULL;
GO
CREATE TRIGGER acc.TR_tally_export_append_only ON acc.tally_export AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51105, 'acc.tally_export is append-only.', 1;
END
GO
CREATE TRIGGER acc.TR_tally_export_day_append_only ON acc.tally_export_day AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51106, 'acc.tally_export_day is append-only.', 1;
END
GO
CREATE TRIGGER acc.TR_tally_reexport_guard ON acc.tally_reexport AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF NOT EXISTS (SELECT 1 FROM deleted) RETURN;
  -- A flag is only ever cleared, once, by an export.
  IF NOT EXISTS (SELECT 1 FROM inserted)
     OR UPDATE(property_id) OR UPDATE(business_date) OR UPDATE(reopen_id) OR UPDATE(reason) OR UPDATE(marked_utc)
     OR EXISTS (SELECT 1 FROM deleted WHERE cleared_export_id IS NOT NULL)
    THROW 51107, 'acc.tally_reexport is append-only: a flag is only cleared by an export.', 1;
END
