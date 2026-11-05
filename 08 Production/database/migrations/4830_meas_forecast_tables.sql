-- HCC migration 4830 · F12 MEAS lane meas-forecast (MEAS-CONTRACT §2, plan §4 items 2.1 / 2.3 / 2.6 / 2.7): the revenue-management captures.
-- meas.event (local events master, Manager / Owner), meas.otb_snapshot (weekly on-the-books snapshot, Monday 06:00 schedule run + back-fill),
-- meas.competitor + meas.rate_shop (manual rate-shop entry), meas.restriction_counter (MIN_STAY / CTA refusals and re-shapes per night).
-- Columns follow the TypeScript record fields (src/domain/streams/measure-types.ts) in snake_case. Money = BIGINT paise. Every row carries
-- property_id and the client id (idempotency); captures are audited through the platform audit log by the command layer.
-- Schema meas is created here when the settings lane (4802 / 4803) has not created it yet (lanes merge in any order).
IF SCHEMA_ID('meas') IS NULL EXEC('CREATE SCHEMA meas');
CREATE TABLE meas.event(
  property_id INT NOT NULL CONSTRAINT FK_meas_event_property REFERENCES inv.property,
  event_id VARCHAR(40) NOT NULL,
  name NVARCHAR(80) NOT NULL,
  from_date DATE NOT NULL,
  to_date DATE NOT NULL,
  kind VARCHAR(16) NOT NULL CONSTRAINT CK_meas_event_kind CHECK (kind IN ('wedding-season', 'exam', 'fair', 'corporate-visit', 'holiday', 'other')),
  impact_pct INT NOT NULL CONSTRAINT CK_meas_event_impact CHECK (impact_pct BETWEEN -50 AND 100),
  note NVARCHAR(200) NULL,
  deleted BIT NOT NULL CONSTRAINT DF_meas_event_deleted DEFAULT 0,
  updated_by INT NULL,
  updated_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_event_updated DEFAULT SYSUTCDATETIME(),
  row_ver ROWVERSION,
  CONSTRAINT PK_meas_event PRIMARY KEY (property_id, event_id),
  CONSTRAINT CK_meas_event_dates CHECK (to_date >= from_date));
CREATE TABLE meas.otb_snapshot(
  property_id INT NOT NULL CONSTRAINT FK_meas_otb_property REFERENCES inv.property,
  taken_on DATE NOT NULL,
  night DATE NOT NULL,
  rooms INT NOT NULL CONSTRAINT CK_meas_otb_rooms CHECK (rooms >= 0),
  revenue_paise BIGINT NOT NULL CONSTRAINT CK_meas_otb_revenue CHECK (revenue_paise >= 0),
  created_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_otb_created DEFAULT SYSUTCDATETIME(),
  CONSTRAINT PK_meas_otb_snapshot PRIMARY KEY (property_id, taken_on, night),
  CONSTRAINT CK_meas_otb_night CHECK (night >= taken_on));
CREATE TABLE meas.competitor(
  property_id INT NOT NULL CONSTRAINT FK_meas_competitor_property REFERENCES inv.property,
  competitor_id VARCHAR(40) NOT NULL,
  name NVARCHAR(80) NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_meas_competitor_active DEFAULT 1,
  updated_by INT NULL,
  updated_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_competitor_updated DEFAULT SYSUTCDATETIME(),
  CONSTRAINT PK_meas_competitor PRIMARY KEY (property_id, competitor_id),
  CONSTRAINT UQ_meas_competitor_name UNIQUE (property_id, name));
CREATE TABLE meas.rate_shop(
  property_id INT NOT NULL CONSTRAINT FK_meas_rate_shop_property REFERENCES inv.property,
  client_id VARCHAR(60) NOT NULL,
  shop_date DATE NOT NULL,
  stay_date DATE NOT NULL,
  competitor_id VARCHAR(40) NOT NULL,
  room_type_id INT NOT NULL,
  rate_paise BIGINT NOT NULL CONSTRAINT CK_meas_rate_shop_rate CHECK (rate_paise > 0 AND rate_paise <= 10000000),
  with_breakfast BIT NOT NULL,
  source VARCHAR(40) NOT NULL,
  entered_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_rate_shop_created DEFAULT SYSUTCDATETIME(),
  CONSTRAINT PK_meas_rate_shop PRIMARY KEY (property_id, client_id),
  CONSTRAINT FK_meas_rate_shop_competitor FOREIGN KEY (property_id, competitor_id) REFERENCES meas.competitor (property_id, competitor_id));
CREATE INDEX IX_meas_rate_shop_stay ON meas.rate_shop (property_id, stay_date, room_type_id);
CREATE TABLE meas.restriction_counter(
  property_id INT NOT NULL CONSTRAINT FK_meas_restriction_counter_property REFERENCES inv.property,
  night DATE NOT NULL,
  kind VARCHAR(8) NOT NULL CONSTRAINT CK_meas_restriction_counter_kind CHECK (kind IN ('MIN_STAY', 'CTA')),
  refused INT NOT NULL CONSTRAINT DF_meas_restriction_counter_refused DEFAULT 0 CONSTRAINT CK_meas_restriction_counter_refused CHECK (refused >= 0),
  reshaped INT NOT NULL CONSTRAINT DF_meas_restriction_counter_reshaped DEFAULT 0 CONSTRAINT CK_meas_restriction_counter_reshaped CHECK (reshaped >= 0),
  updated_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_restriction_counter_updated DEFAULT SYSUTCDATETIME(),
  CONSTRAINT PK_meas_restriction_counter PRIMARY KEY (property_id, night, kind));
