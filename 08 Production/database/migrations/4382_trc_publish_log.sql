-- HCC migration 4382 · F4-T1 publish log (F4-CONTRACT §1.2): one row per master row a tariff card publish wrote (append-only). A later card on the
-- same key and date may replace only rows logged here; a hand-entered row on that date is refused (TRC_CONFLICT).
CREATE TABLE mst.tariff_card_publish_log(
  log_id BIGINT IDENTITY CONSTRAINT PK_tariff_card_publish_log PRIMARY KEY,
  version_id BIGINT NOT NULL CONSTRAINT FK_tariff_card_publish_log_version REFERENCES mst.tariff_card_version,
  master VARCHAR(20) NOT NULL CONSTRAINT CK_tariff_card_publish_log_master CHECK (master IN ('tariffs','occupancyCharges','mealPlans','extras')),
  row_ref VARCHAR(80) NOT NULL,
  label NVARCHAR(120) NOT NULL,
  before_paise BIGINT NULL,
  after_paise BIGINT NOT NULL,
  at_utc DATETIME2(3) NOT NULL CONSTRAINT DF_tariff_card_publish_log_at DEFAULT SYSUTCDATETIME(),
  by_user INT NOT NULL);
CREATE INDEX IX_tariff_card_publish_log_ref ON mst.tariff_card_publish_log(master, row_ref);
GO
CREATE TRIGGER mst.TR_tariff_card_publish_log_append_only ON mst.tariff_card_publish_log AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  THROW 51383, 'mst.tariff_card_publish_log is append-only.', 1;
END;
