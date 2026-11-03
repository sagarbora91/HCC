-- HCC migration 4380 · F4-T1 tariff card (02 Planning/agents/F4-CONTRACT.md §1.2). A versioned view + snapshot of the published prices: publish
-- writes the existing masters (inv.room_type_tariff, inv.occupancy_charge_rate, bill.meal_plan_price, bill.extra_item_price) dated effective_from;
-- nothing reads these tables for a price (DEC-MA-102). Money BIGINT paise; GST in basis points snapshot at effective_from (DEC-069).
-- Published / superseded versions and their lines are append-only: only status → superseded and superseded_by may change (trigger below).
IF SCHEMA_ID('mst') IS NULL EXEC('CREATE SCHEMA mst');
GO
CREATE TABLE mst.tariff_card_setting(
  property_id INT NOT NULL CONSTRAINT PK_tariff_card_setting PRIMARY KEY CONSTRAINT FK_tariff_card_setting_property REFERENCES inv.property,
  weekend_days VARCHAR(13) NOT NULL CONSTRAINT DF_tariff_card_setting_weekend DEFAULT '5,6,0');
CREATE TABLE mst.tariff_card_version(
  version_id BIGINT IDENTITY CONSTRAINT PK_tariff_card_version PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_tariff_card_version_property REFERENCES inv.property,
  label NVARCHAR(60) NOT NULL,
  effective_from DATE NOT NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_tariff_card_version_status CHECK (status IN ('draft','proposed','published','superseded','rejected')),
  weekend_days VARCHAR(13) NOT NULL CONSTRAINT DF_tariff_card_version_weekend DEFAULT '5,6,0',
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  approval_id BIGINT NULL,
  proposed_by INT NULL,
  published_by INT NULL,
  published_utc DATETIME2(3) NULL,
  superseded_by BIGINT NULL CONSTRAINT FK_tariff_card_version_superseded REFERENCES mst.tariff_card_version,
  notes NVARCHAR(500) NULL,
  row_version ROWVERSION);
CREATE INDEX IX_tariff_card_version_property ON mst.tariff_card_version(property_id, status, effective_from);
CREATE TABLE mst.tariff_card_line(
  version_id BIGINT NOT NULL CONSTRAINT FK_tariff_card_line_version REFERENCES mst.tariff_card_version,
  line_no INT NOT NULL,
  kind VARCHAR(10) NOT NULL CONSTRAINT CK_tariff_card_line_kind CHECK (kind IN ('room','occupancy','meal','extra','hall','day-use')),
  room_type_id INT NULL CONSTRAINT FK_tariff_card_line_type REFERENCES inv.room_type,
  room_type_code VARCHAR(20) NULL,
  [plan] VARCHAR(20) NULL,
  occ_kind VARCHAR(5) NULL CONSTRAINT CK_tariff_card_line_occ CHECK (occ_kind IS NULL OR occ_kind IN ('adult','child','bed')),
  meal_plan_id INT NULL,   -- bill.meal_plan (no FK: mst tables reference only masters, MastersMigrationUpgradeTests)
  meal_plan_code VARCHAR(20) NULL,
  extra_id INT NULL,       -- bill.extra_item (no FK, same rule)
  extra_code VARCHAR(20) NULL,
  hall_id VARCHAR(20) NULL,
  day_set VARCHAR(7) NOT NULL CONSTRAINT CK_tariff_card_line_dayset CHECK (day_set IN ('all','weekday','weekend')),
  rate_excl_paise BIGINT NULL CONSTRAINT CK_tariff_card_line_rate CHECK (rate_excl_paise IS NULL OR rate_excl_paise BETWEEN 0 AND 100000000),
  gst_bps INT NOT NULL,
  display_incl_paise BIGINT NULL,
  entered_incl_paise BIGINT NULL,
  min_guests TINYINT NULL, max_guests TINYINT NULL, age_from TINYINT NULL, age_to TINYINT NULL,
  note NVARCHAR(200) NULL,
  CONSTRAINT PK_tariff_card_line PRIMARY KEY (version_id, line_no));
GO
CREATE TRIGGER mst.TR_tariff_card_version_published ON mst.tariff_card_version AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted d WHERE d.status IN ('published','superseded')) AND NOT EXISTS (SELECT 1 FROM inserted)
    THROW 51380, 'A published tariff card is never deleted.', 1;
  IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.version_id = d.version_id WHERE d.status IN ('published','superseded')
      AND (i.label <> d.label OR i.effective_from <> d.effective_from OR i.weekend_days <> d.weekend_days OR i.published_by <> d.published_by
        OR NOT (i.status = 'superseded' OR i.status = d.status)))
    THROW 51381, 'A published tariff card is read-only; only status superseded and superseded_by may change.', 1;
END;
GO
CREATE TRIGGER mst.TR_tariff_card_line_published ON mst.tariff_card_line AFTER INSERT, UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted d JOIN mst.tariff_card_version v ON v.version_id = d.version_id WHERE v.status IN ('published','superseded'))
    OR EXISTS (SELECT 1 FROM inserted i JOIN mst.tariff_card_version v ON v.version_id = i.version_id WHERE v.status IN ('published','superseded'))
    THROW 51382, 'The lines of a published tariff card are append-only.', 1;
END;
