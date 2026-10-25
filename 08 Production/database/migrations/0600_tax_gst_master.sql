-- HCC migration 0600 · GST master (Wave 1 agent C, block 0600–0699). DEC-069 (dated GST rates master, Owner-only, history),
-- DEC-058 (money/tax configuration is Owner-only; Tech admin read-only). Replaces the DEC-039 synthetic 10 % tax for new postings;
-- posted bill.charge rows keep their tax_bps / tax_paise snapshot. Rates are seeded from public sources (25 Oct 2026) and are
-- ILLUSTRATIVE — CA TO CONFIRM. Original HCC design (port of 03 Prototype/src/domain/gst.ts); no donor code.
-- Values: percent = basis points (500 = 5 %), money = paise, boolean = 0/1. Event times are written by the application.
CREATE SCHEMA tax;
GO
-- Catalogue of master settings (one row per key, same keys as the prototype's gst.* settings).
CREATE TABLE tax.gst_setting(
  setting_key VARCHAR(40) NOT NULL CONSTRAINT PK_gst_setting PRIMARY KEY,
  label NVARCHAR(120) NOT NULL,
  value_kind VARCHAR(8) NOT NULL CONSTRAINT CK_gst_setting_kind CHECK (value_kind IN ('percent','money','boolean')),
  note NVARCHAR(60) NOT NULL CONSTRAINT DF_gst_setting_note DEFAULT N'Illustrative — CA to confirm');
-- Dated versions. property_id NULL = statutory seed for every property; Owner changes are per property. Append-only: a change is a
-- new row (history); a not-yet-effective change can be withdrawn (status), never edited or deleted (trigger below).
CREATE TABLE tax.gst_setting_version(
  version_id BIGINT IDENTITY CONSTRAINT PK_gst_setting_version PRIMARY KEY,
  property_id INT NULL CONSTRAINT FK_gst_version_property REFERENCES inv.property,
  setting_key VARCHAR(40) NOT NULL CONSTRAINT FK_gst_version_setting REFERENCES tax.gst_setting,
  effective_from DATE NOT NULL,
  value BIGINT NOT NULL,
  source VARCHAR(6) NOT NULL CONSTRAINT CK_gst_version_source CHECK (source IN ('seed','owner')),
  status VARCHAR(10) NOT NULL CONSTRAINT DF_gst_version_status DEFAULT 'active' CONSTRAINT CK_gst_version_status CHECK (status IN ('active','withdrawn')),
  reason NVARCHAR(200) NULL,
  changed_by INT NULL,
  changed_utc DATETIME2(3) NOT NULL CONSTRAINT DF_gst_version_changed DEFAULT SYSUTCDATETIME(),
  withdrawn_by INT NULL,
  withdrawn_utc DATETIME2(3) NULL,
  CONSTRAINT CK_gst_version_origin CHECK ((source = 'seed' AND property_id IS NULL AND changed_by IS NULL)
                                       OR (source = 'owner' AND property_id IS NOT NULL AND changed_by IS NOT NULL AND reason IS NOT NULL)),
  CONSTRAINT CK_gst_version_withdrawn CHECK ((status = 'active' AND withdrawn_by IS NULL) OR (status = 'withdrawn' AND source = 'owner' AND withdrawn_by IS NOT NULL)));
CREATE INDEX IX_gst_version_lookup ON tax.gst_setting_version(setting_key, effective_from) INCLUDE (property_id, value, status);
GO
CREATE TRIGGER tax.TR_gst_setting_version_append_only ON tax.gst_setting_version AFTER UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
    THROW 51600, 'GST master history is append-only: versions cannot be deleted.', 1;
  IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.version_id = i.version_id
             WHERE i.setting_key <> d.setting_key OR i.effective_from <> d.effective_from OR i.value <> d.value OR i.source <> d.source
                OR ISNULL(i.property_id, -1) <> ISNULL(d.property_id, -1) OR ISNULL(i.changed_by, -1) <> ISNULL(d.changed_by, -1)
                OR i.changed_utc <> d.changed_utc OR d.status <> 'active')
    THROW 51601, 'GST master history is append-only: only an active change can be withdrawn.', 1;
END;
GO
-- Per-charge GST snapshot beside bill.charge (category, ITC, CGST / SGST split per line). Written in the posting transaction.
CREATE TABLE tax.charge_gst(
  charge_id BIGINT NOT NULL CONSTRAINT PK_charge_gst PRIMARY KEY CONSTRAINT FK_charge_gst_charge REFERENCES bill.charge,
  category VARCHAR(8) NOT NULL CONSTRAINT CK_charge_gst_category CHECK (category IN ('room','food','laundry','banquet','other')),
  tax_bps INT NOT NULL CONSTRAINT CK_charge_gst_bps CHECK (tax_bps BETWEEN 0 AND 10000),
  itc BIT NOT NULL,
  tax_paise BIGINT NOT NULL,
  cgst_paise BIGINT NOT NULL,
  sgst_paise BIGINT NOT NULL,
  rule_text NVARCHAR(80) NOT NULL,
  CONSTRAINT CK_charge_gst_split CHECK (cgst_paise + sgst_paise = tax_paise AND ABS(cgst_paise - sgst_paise) <= 1));
GO
INSERT tax.gst_setting(setting_key, label, value_kind) VALUES
  ('gst.room.threshold', N'GST · room slab threshold (per unit per night)', 'money'),
  ('gst.room.lowBps', N'GST · room night at or below the threshold', 'percent'),
  ('gst.room.lowItc', N'GST · input tax credit on the lower room slab', 'boolean'),
  ('gst.room.highBps', N'GST · room night above the threshold', 'percent'),
  ('gst.food.bps', N'GST · restaurant and room service (standard)', 'percent'),
  ('gst.food.specifiedBps', N'GST · restaurant and room service (specified premises)', 'percent'),
  ('gst.food.optIn', N'GST · declared specified premises (opt-in)', 'boolean'),
  ('gst.svc.laundryBps', N'GST · laundry and dry cleaning', 'percent'),
  ('gst.svc.banquetBps', N'GST · banquet, hall and conference', 'percent'),
  ('gst.svc.otherBps', N'GST · other hotel services', 'percent');
-- Statutory seed versions (Illustrative — CA to confirm). The first version of a key applies to every earlier date.
INSERT tax.gst_setting_version(property_id, setting_key, effective_from, value, source, reason) VALUES
  (NULL, 'gst.room.threshold', '2017-07-01', 750000, 'seed', N'Seed · ₹7,500 per unit per night · Illustrative — CA to confirm'),
  (NULL, 'gst.room.lowBps', '2017-07-01', 1200, 'seed', N'Seed · 12 % with ITC before 22 Sep 2025 · Illustrative — CA to confirm'),
  (NULL, 'gst.room.lowBps', '2025-09-22', 500, 'seed', N'Seed · 5 % without ITC from 22 Sep 2025 · Illustrative — CA to confirm'),
  (NULL, 'gst.room.lowItc', '2017-07-01', 1, 'seed', N'Seed · ITC available before 22 Sep 2025 · Illustrative — CA to confirm'),
  (NULL, 'gst.room.lowItc', '2025-09-22', 0, 'seed', N'Seed · no ITC from 22 Sep 2025 · Illustrative — CA to confirm'),
  (NULL, 'gst.room.highBps', '2017-07-01', 1800, 'seed', N'Seed · 18 % with ITC · Illustrative — CA to confirm'),
  (NULL, 'gst.food.bps', '2017-07-01', 500, 'seed', N'Seed · 5 % without ITC · Illustrative — CA to confirm'),
  (NULL, 'gst.food.specifiedBps', '2017-07-01', 1800, 'seed', N'Seed · 18 % with ITC, specified premises (from 1 Apr 2025) · Illustrative — CA to confirm'),
  (NULL, 'gst.food.optIn', '2017-07-01', 0, 'seed', N'Seed · not declared · Illustrative — CA to confirm'),
  (NULL, 'gst.svc.laundryBps', '2017-07-01', 1800, 'seed', N'Seed · 18 % · Illustrative — CA to confirm'),
  (NULL, 'gst.svc.banquetBps', '2017-07-01', 1800, 'seed', N'Seed · 18 % · Illustrative — CA to confirm'),
  (NULL, 'gst.svc.otherBps', '2017-07-01', 1800, 'seed', N'Seed · 18 % · Illustrative — CA to confirm');
