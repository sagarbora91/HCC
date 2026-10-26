-- HCC migration 3950 · stable Tally voucher ids (audit A03b-05). Until now the (simulated) Tally XML carried no voucher id, so importing
-- a range again made Tally create every voucher a second time. Every voucher now carries REMOTEID = GUID = "HCC-<identity>-<source key>"
-- (Hcc.Domain.Accounting.VoucherExport.RemoteId): the source key is unique per property and never changes, and the identity below is one
-- random GUID per property, created once (by this migration for the properties that exist, by the first Tally export for a later one)
-- and never changed, so a voucher has the same id in every export and a reinstalled HCC with a new database never reuses another's ids.
-- acc.tally_export.remote_ids tells the exports made before this change (0: Tally may hold those days without ids) from the ones after (1).
CREATE TABLE acc.tally_identity(
  property_id INT NOT NULL CONSTRAINT PK_tally_identity PRIMARY KEY CONSTRAINT FK_tally_identity_property REFERENCES inv.property,
  tally_guid UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_tally_identity_guid DEFAULT NEWID() CONSTRAINT UQ_tally_identity_guid UNIQUE,
  created_utc DATETIME2(3) NOT NULL);
GO
INSERT acc.tally_identity(property_id, created_utc) SELECT property_id, SYSUTCDATETIME() FROM inv.property;
GO
CREATE TRIGGER acc.TR_tally_identity_fixed ON acc.tally_identity AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51108, 'acc.tally_identity never changes: every exported REMOTEID depends on it.', 1;
END
GO
-- Exports recorded before this migration carried no REMOTEID (0); every later export writes 1.
ALTER TABLE acc.tally_export ADD remote_ids BIT NOT NULL CONSTRAINT DF_tally_export_remote_ids DEFAULT 0;
GO
