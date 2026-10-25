-- HCC migration 1800 · W2-12 yield & tariffs (module 13; prototype src/domain/streams/yield.ts). Original HCC design.
-- Owns schema yld. Published nightly rates are written through IRatePublicationPort into P2's inv.room_type_tariff (effective-dated BAR)
-- and house restrictions into inv.stay_restriction (the tables P2 already reads); this migration never alters inv.* tables.
-- Money BIGINT paise; adjustments in basis points; dates DATE; event times DATETIME2(3) UTC from the application clock.
-- Authorization: no new PERM ids (view PERM-RPT-001 / propose PERM-APR-001 / manage + decide PERM-ADM-003, DEC-063).
IF SCHEMA_ID('yld') IS NULL EXEC('CREATE SCHEMA yld');
GO
-- One row per property: day-of-week adjustments (index 0 = Sunday), guardrails, rounding unit. Also the per-property lock row for proposals.
CREATE TABLE yld.settings(
  property_id INT NOT NULL CONSTRAINT PK_yld_settings PRIMARY KEY CONSTRAINT FK_yld_settings_property REFERENCES inv.property,
  dow0_bps INT NOT NULL CONSTRAINT DF_yld_dow0 DEFAULT 0, dow1_bps INT NOT NULL CONSTRAINT DF_yld_dow1 DEFAULT 0,
  dow2_bps INT NOT NULL CONSTRAINT DF_yld_dow2 DEFAULT 0, dow3_bps INT NOT NULL CONSTRAINT DF_yld_dow3 DEFAULT 0,
  dow4_bps INT NOT NULL CONSTRAINT DF_yld_dow4 DEFAULT 0, dow5_bps INT NOT NULL CONSTRAINT DF_yld_dow5 DEFAULT 0,
  dow6_bps INT NOT NULL CONSTRAINT DF_yld_dow6 DEFAULT 0,
  floor_bps INT NOT NULL CONSTRAINT DF_yld_floor DEFAULT -2000,
  ceil_bps INT NOT NULL CONSTRAINT DF_yld_ceil DEFAULT 4000,
  round_to_paise INT NOT NULL CONSTRAINT DF_yld_round DEFAULT 5000,
  row_ver ROWVERSION,
  CONSTRAINT CK_yld_dow CHECK (dow0_bps BETWEEN -3000 AND 5000 AND dow1_bps BETWEEN -3000 AND 5000 AND dow2_bps BETWEEN -3000 AND 5000
    AND dow3_bps BETWEEN -3000 AND 5000 AND dow4_bps BETWEEN -3000 AND 5000 AND dow5_bps BETWEEN -3000 AND 5000 AND dow6_bps BETWEEN -3000 AND 5000),
  CONSTRAINT CK_yld_guard CHECK (floor_bps BETWEEN -5000 AND 0 AND ceil_bps BETWEEN 0 AND 10000 AND round_to_paise IN (100, 1000, 5000, 10000)));

-- Rate plans derive from the night's suggested BAR (BAR itself = 0 bps, never changes).
CREATE TABLE yld.rate_plan(
  property_id INT NOT NULL CONSTRAINT FK_yld_plan_property REFERENCES inv.property,
  plan_code VARCHAR(12) NOT NULL,
  name NVARCHAR(80) NOT NULL,
  adj_bps INT NOT NULL CONSTRAINT CK_yld_plan_adj CHECK (adj_bps BETWEEN -9000 AND 5000),
  min_nights INT NOT NULL CONSTRAINT CK_yld_plan_min CHECK (min_nights BETWEEN 0 AND 30),
  sell_dow VARCHAR(7) NULL,                    -- digits 0-6 (0 = Sunday) the plan is sold on; NULL = every night
  dayuse_from VARCHAR(5) NULL, dayuse_to VARCHAR(5) NULL,
  dayuse_close_occ INT NULL CONSTRAINT CK_yld_plan_close CHECK (dayuse_close_occ IS NULL OR dayuse_close_occ BETWEEN 50 AND 101),
  active BIT NOT NULL CONSTRAINT DF_yld_plan_active DEFAULT 1,
  note NVARCHAR(200) NOT NULL CONSTRAINT DF_yld_plan_note DEFAULT N'',
  row_ver ROWVERSION,
  CONSTRAINT PK_yld_plan PRIMARY KEY (property_id, plan_code));

CREATE TABLE yld.season(
  season_id INT IDENTITY CONSTRAINT PK_yld_season PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_yld_season_property REFERENCES inv.property,
  name NVARCHAR(80) NOT NULL,
  from_date DATE NOT NULL, to_date DATE NOT NULL,
  adj_bps INT NOT NULL CONSTRAINT CK_yld_season_adj CHECK (adj_bps BETWEEN -5000 AND 10000 AND adj_bps <> 0),
  priority INT NOT NULL CONSTRAINT DF_yld_season_prio DEFAULT 1,
  row_ver ROWVERSION,
  CONSTRAINT CK_yld_season_dates CHECK (to_date >= from_date AND DATEDIFF(DAY, from_date, to_date) <= 365));
-- No rows = the season applies to every room type.
CREATE TABLE yld.season_type(
  season_id INT NOT NULL CONSTRAINT FK_yld_season_type_season REFERENCES yld.season ON DELETE CASCADE,
  room_type_id INT NOT NULL CONSTRAINT FK_yld_season_type_type REFERENCES inv.room_type,
  CONSTRAINT PK_yld_season_type PRIMARY KEY (season_id, room_type_id));

CREATE TABLE yld.occ_rule(
  rule_id INT IDENTITY CONSTRAINT PK_yld_occ_rule PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_yld_occ_property REFERENCES inv.property,
  op VARCHAR(2) NOT NULL CONSTRAINT CK_yld_occ_op CHECK (op IN ('>=', '<')),
  occ_pct INT NOT NULL CONSTRAINT CK_yld_occ_pct CHECK (occ_pct BETWEEN 1 AND 100),
  adj_bps INT NOT NULL CONSTRAINT CK_yld_occ_adj CHECK (adj_bps BETWEEN -5000 AND 5000 AND adj_bps <> 0),
  row_ver ROWVERSION,
  CONSTRAINT UQ_yld_occ_rule UNIQUE (property_id, op, occ_pct),
  CONSTRAINT CK_yld_occ_dir CHECK ((op = '>=' AND adj_bps > 0) OR (op = '<' AND adj_bps < 0)));

-- House restrictions managed by the Owner; mirrored into inv.stay_restriction under the same code by IRatePublicationPort.
CREATE SEQUENCE yld.seq_restriction AS INT START WITH 1 INCREMENT BY 1;
CREATE TABLE yld.restriction(
  restriction_id INT IDENTITY CONSTRAINT PK_yld_restriction PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_yld_restriction_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  room_type_id INT NULL CONSTRAINT FK_yld_restriction_type REFERENCES inv.room_type,   -- NULL = all types
  arrival_from DATE NOT NULL, arrival_to DATE NOT NULL,
  min_stay TINYINT NOT NULL CONSTRAINT CK_yld_restriction_min CHECK (min_stay BETWEEN 1 AND 14),
  closed_to_arrival BIT NOT NULL,
  note NVARCHAR(200) NOT NULL CONSTRAINT DF_yld_restriction_note DEFAULT N'',
  row_ver ROWVERSION,
  CONSTRAINT UQ_yld_restriction UNIQUE (property_id, code),
  CONSTRAINT CK_yld_restriction_dates CHECK (arrival_to >= arrival_from AND DATEDIFF(DAY, arrival_from, arrival_to) <= 90),
  CONSTRAINT CK_yld_restriction_effect CHECK (min_stay > 1 OR closed_to_arrival = 1));

-- Manual per-night override of the computed rate (not live until published).
CREATE TABLE yld.rate_override(
  property_id INT NOT NULL CONSTRAINT FK_yld_override_property REFERENCES inv.property,
  room_type_id INT NOT NULL CONSTRAINT FK_yld_override_type REFERENCES inv.room_type,
  night DATE NOT NULL,
  rate_paise BIGINT NOT NULL CONSTRAINT CK_yld_override_rate CHECK (rate_paise BETWEEN 10000 AND 10000000),
  reason NVARCHAR(200) NOT NULL,
  actor_id INT NOT NULL,
  set_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_yld_rate_override PRIMARY KEY (property_id, room_type_id, night));

-- SIMULATED forecast of rooms sold per night (fixture-style input until a pickup/forecast feed exists).
CREATE TABLE yld.forecast(
  property_id INT NOT NULL CONSTRAINT FK_yld_forecast_property REFERENCES inv.property,
  night DATE NOT NULL,
  rooms_sold INT NOT NULL CONSTRAINT CK_yld_forecast CHECK (rooms_sold BETWEEN 0 AND 1000),
  CONSTRAINT PK_yld_forecast PRIMARY KEY (property_id, night));

-- Tariff rows written by this module (key room type + effective_from + rate); they are excluded from the base rate.
CREATE TABLE yld.owned_tariff(
  room_type_id INT NOT NULL CONSTRAINT FK_yld_owned_type REFERENCES inv.room_type,
  effective_from DATE NOT NULL,
  rate_paise BIGINT NOT NULL,
  CONSTRAINT PK_yld_owned_tariff PRIMARY KEY (room_type_id, effective_from));

-- Manager proposes -> Owner approves. Rates are frozen in yld.proposal_rate; sig_sha256 detects tariff changes since the proposal.
CREATE TABLE yld.proposal(
  proposal_id BIGINT IDENTITY CONSTRAINT PK_yld_proposal PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_yld_proposal_property REFERENCES inv.property,
  from_night DATE NOT NULL, to_night DATE NOT NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_yld_proposal_status CHECK (status IN ('Pending', 'Rejected', 'Executed')),
  proposed_by INT NOT NULL, proposed_utc DATETIME2(3) NOT NULL,
  expires_utc DATETIME2(3) NOT NULL,
  reason NVARCHAR(200) NOT NULL,
  sig_sha256 CHAR(64) NOT NULL,
  changed_nights INT NOT NULL,
  decided_by INT NULL, decided_utc DATETIME2(3) NULL, decision_note NVARCHAR(200) NULL,
  publish_id BIGINT NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_yld_proposal_window CHECK (to_night >= from_night));
CREATE TABLE yld.proposal_type(
  proposal_id BIGINT NOT NULL CONSTRAINT FK_yld_proposal_type_p REFERENCES yld.proposal,
  room_type_id INT NOT NULL CONSTRAINT FK_yld_proposal_type_t REFERENCES inv.room_type,
  CONSTRAINT PK_yld_proposal_type PRIMARY KEY (proposal_id, room_type_id));
CREATE TABLE yld.proposal_rate(
  proposal_id BIGINT NOT NULL CONSTRAINT FK_yld_proposal_rate_p REFERENCES yld.proposal,
  room_type_id INT NOT NULL CONSTRAINT FK_yld_proposal_rate_t REFERENCES inv.room_type,
  night DATE NOT NULL,
  rate_paise BIGINT NOT NULL,
  CONSTRAINT PK_yld_proposal_rate PRIMARY KEY (proposal_id, room_type_id, night));

CREATE TABLE yld.publish(
  publish_id BIGINT IDENTITY CONSTRAINT PK_yld_publish PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_yld_publish_property REFERENCES inv.property,
  from_night DATE NOT NULL, to_night DATE NOT NULL,
  actor_id INT NOT NULL, published_utc DATETIME2(3) NOT NULL,
  reason NVARCHAR(200) NOT NULL,
  proposal_id BIGINT NULL CONSTRAINT FK_yld_publish_proposal REFERENCES yld.proposal);
CREATE TABLE yld.publish_row(
  publish_id BIGINT NOT NULL CONSTRAINT FK_yld_publish_row_p REFERENCES yld.publish,
  room_type_id INT NOT NULL CONSTRAINT FK_yld_publish_row_t REFERENCES inv.room_type,
  night DATE NOT NULL,
  before_paise BIGINT NOT NULL, after_paise BIGINT NOT NULL,
  closing BIT NOT NULL CONSTRAINT DF_yld_publish_row_closing DEFAULT 0,
  CONSTRAINT PK_yld_publish_row PRIMARY KEY (publish_id, room_type_id, night));

CREATE TABLE yld.history(
  history_id BIGINT IDENTITY CONSTRAINT PK_yld_history PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_yld_history_property REFERENCES inv.property,
  at_utc DATETIME2(3) NOT NULL,
  actor_id INT NOT NULL,
  area NVARCHAR(120) NOT NULL,
  before_text NVARCHAR(400) NOT NULL,
  after_text NVARCHAR(400) NOT NULL,
  reason NVARCHAR(200) NOT NULL CONSTRAINT DF_yld_history_reason DEFAULT N'');
CREATE INDEX IX_yld_history ON yld.history(property_id, history_id DESC);
GO
