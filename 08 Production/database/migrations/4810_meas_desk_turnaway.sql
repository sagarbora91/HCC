-- HCC migration 4810 · F12 MEAS cap-desk: schema meas, turn-away log (res.turnaway in the contract) and walked-guest log.
-- Append-only captures: one row per client id (idempotent), property scoped. Money in paise. No guest identity beyond the booking id.
IF SCHEMA_ID('meas') IS NULL EXEC('CREATE SCHEMA meas');
GO
CREATE TABLE meas.turnaway(
  turnaway_id BIGINT IDENTITY CONSTRAINT PK_meas_turnaway PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_meas_turnaway_property REFERENCES inv.property,
  client_id NVARCHAR(64) NOT NULL,
  reason VARCHAR(12) NOT NULL CONSTRAINT CK_meas_turnaway_reason CHECK (reason IN ('full','price','dates','room-type','policy','other')),
  arrival DATE NOT NULL,
  nights TINYINT NOT NULL CONSTRAINT CK_meas_turnaway_nights CHECK (nights BETWEEN 1 AND 30),
  room_type NVARCHAR(40) NOT NULL,
  rooms TINYINT NOT NULL CONSTRAINT DF_meas_turnaway_rooms DEFAULT 1 CONSTRAINT CK_meas_turnaway_rooms CHECK (rooms BETWEEN 1 AND 20),
  quoted_rate_paise BIGINT NULL CONSTRAINT CK_meas_turnaway_rate CHECK (quoted_rate_paise IS NULL OR quoted_rate_paise >= 0),
  channel NVARCHAR(30) NOT NULL,
  lead_days SMALLINT NOT NULL CONSTRAINT CK_meas_turnaway_lead CHECK (lead_days >= 0),
  note NVARCHAR(120) NULL,
  business_date DATE NOT NULL,
  recorded_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_turnaway_utc DEFAULT SYSUTCDATETIME(),
  recorded_by INT NOT NULL,
  CONSTRAINT UQ_meas_turnaway_client UNIQUE (property_id, client_id));
CREATE INDEX IX_meas_turnaway_arrival ON meas.turnaway(property_id, arrival);
GO
CREATE TABLE meas.walked(
  walked_id BIGINT IDENTITY CONSTRAINT PK_meas_walked PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_meas_walked_property REFERENCES inv.property,
  client_id NVARCHAR(64) NOT NULL,
  reservation_id BIGINT NOT NULL,
  night DATE NOT NULL,
  reason NVARCHAR(200) NOT NULL,
  cost_paise BIGINT NULL CONSTRAINT CK_meas_walked_cost CHECK (cost_paise IS NULL OR cost_paise >= 0),
  approved_by INT NOT NULL,
  recorded_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_walked_utc DEFAULT SYSUTCDATETIME(),
  recorded_by INT NOT NULL,
  CONSTRAINT UQ_meas_walked_client UNIQUE (property_id, client_id));
CREATE INDEX IX_meas_walked_night ON meas.walked(property_id, night);
