-- HCC migration 4243 · ACC-2 TDS-rate table overrides and the firm's TDS switches (contract §4, §5; rulebook C9; DEC-146, DEC-058; lane acc2-tds).
--   acc.tds_rate     Owner overrides of the dated TDS table (Hcc.Domain.Accounting.TdsRates: keys tds.<row>.<field>, seed in code, every row
--                    "CA to confirm"): one row per change, effective from a business date (never backdated); the latest version on or before a
--                    date wins (TdsRates.ValueOn). Rates in basis points (0–3000), thresholds in paise (0–₹10,00,000). Withdrawn rows stay.
--   acc.tds_setting  the two firm-profile switches of contract §4 (prototype CMPProfile.hotelDeductsTds / tdsDeductorType): the hotel deducts TDS
--                    (DEC-146, default on) and the rate column (individual / other). No row = the defaults (on, other).
-- Owner only (PERM-CFG-003, money configuration). Original HCC design; no donor code. Times DATETIME2(3) UTC written by the application.
CREATE TABLE acc.tds_rate(
  rate_id BIGINT IDENTITY CONSTRAINT PK_acc_tds_rate PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_acc_tds_rate_property REFERENCES inv.property,
  rate_key VARCHAR(40) NOT NULL CONSTRAINT CK_acc_tds_rate_key CHECK (rate_key LIKE 'tds.%'),
  effective_from DATE NOT NULL,
  value BIGINT NOT NULL CONSTRAINT CK_acc_tds_rate_value CHECK (value BETWEEN 0 AND 100000000),
  reason NVARCHAR(200) NOT NULL CONSTRAINT CK_acc_tds_rate_reason CHECK (LEN(reason) >= 3),
  set_by INT NOT NULL,
  set_utc DATETIME2(3) NOT NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT DF_acc_tds_rate_status DEFAULT 'active' CONSTRAINT CK_acc_tds_rate_status CHECK (status IN ('active','withdrawn')));
CREATE INDEX IX_acc_tds_rate_key ON acc.tds_rate(property_id, rate_key, effective_from) INCLUDE (value, status);
CREATE TABLE acc.tds_setting(
  property_id INT NOT NULL CONSTRAINT PK_acc_tds_setting PRIMARY KEY CONSTRAINT FK_acc_tds_setting_property REFERENCES inv.property,
  hotel_deducts_tds BIT NOT NULL,
  deductor_type VARCHAR(10) NOT NULL CONSTRAINT CK_acc_tds_setting_type CHECK (deductor_type IN ('individual','other')),
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION);
GO
