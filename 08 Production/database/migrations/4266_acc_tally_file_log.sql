-- HCC migration 4266 · ACC-3 acc3-tally (02 Planning/agents/ACC3-REPORTS-CONTRACT.md §4, §10): the log of the two Tally XML files
-- (masters per financial year, vouchers per range of at most 31 days). acc.tally_export / acc.tally_export_day (migrations 3601, 3950) stay the
-- DEC-077 per-date log that drives re-export flags; this table records every FILE: who made it, when, the range, how many masters / vouchers,
-- the SHA-256 of the exact bytes handed out and whether it was provisional (opening not yet approved, or a day still open / reopened / with
-- accounting events pending). Append-only; times written by the application; original HCC design.
CREATE TABLE acc.tally_file(
  file_id BIGINT IDENTITY CONSTRAINT PK_tally_file PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_tally_file_property REFERENCES inv.property,
  kind VARCHAR(8) NOT NULL CONSTRAINT CK_tally_file_kind CHECK (kind IN ('masters', 'vouchers')),
  from_date DATE NOT NULL,        -- masters: the financial year's first day; vouchers: the range start
  to_date DATE NOT NULL,          -- masters: its last day; vouchers: the range end
  item_count INT NOT NULL CONSTRAINT CK_tally_file_count CHECK (item_count >= 0),   -- masters: groups + ledgers; vouchers: vouchers written
  byte_count INT NOT NULL CONSTRAINT CK_tally_file_bytes CHECK (byte_count > 0),
  sha256 CHAR(64) NOT NULL CONSTRAINT CK_tally_file_sha CHECK (sha256 NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_BIN),
  provisional BIT NOT NULL,
  exported_by INT NOT NULL,
  exported_utc DATETIME2(3) NOT NULL,
  CONSTRAINT CK_tally_file_range CHECK (from_date <= to_date));
CREATE INDEX IX_tally_file_property ON acc.tally_file(property_id, kind, from_date, file_id);
GO
CREATE TRIGGER acc.TR_tally_file_append_only ON acc.tally_file AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51121, 'acc.tally_file is append-only.', 1;
END
GO
