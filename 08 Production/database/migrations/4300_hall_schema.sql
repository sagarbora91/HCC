-- HCC migration 4300 · F6 hall booking (module 27, F6-CONTRACT §6, lane H1): schema hall — venues and their slots, enquiries, quotations
-- (one header per number) with versions and lines, events (accepted quotations) with their lines, and calendar holds.
-- Calendar holds are the double-booking backstop: a hard hold writes one row per physical hall × date × part-of-day (MORNING / AFTERNOON /
-- EVENING; FULL_DAY writes all three; a combined venue writes rows for each of its halls), so the unique filtered index refuses any overlap
-- even under a race (HallRules.Clash is the friendly check before it). Soft holds (enquiry, Manager, up to 3 days) never block.
-- Money is integer paise; GST is taken at the invoice date from the dated master by gst_class (never a stored rate on a quotation line).
-- Nothing is deleted: cancel / supersede / lose, never delete.
IF SCHEMA_ID('hall') IS NULL EXEC('CREATE SCHEMA hall');
GO
CREATE TABLE hall.venue(
  venue_id INT IDENTITY CONSTRAINT PK_hall_venue PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_hall_venue_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  kind VARCHAR(8) NOT NULL CONSTRAINT CK_hall_venue_kind CHECK (kind IN ('HALL','LAWN','TERRACE')),
  cap_theatre INT NULL, cap_round INT NULL, cap_ushape INT NULL, cap_floating INT NULL, cap_classroom INT NULL,
  min_plates INT NULL,
  gst_class VARCHAR(16) NOT NULL CONSTRAINT DF_hall_venue_gst DEFAULT 'HALL_RENT',
  combined_of VARCHAR(200) NULL,          -- comma-separated venue codes of a combined booking (NULL = a single hall)
  active BIT NOT NULL CONSTRAINT DF_hall_venue_active DEFAULT 1,
  version INT NOT NULL CONSTRAINT DF_hall_venue_version DEFAULT 1,
  updated_by INT NULL,
  updated_utc DATETIME2(3) NOT NULL CONSTRAINT DF_hall_venue_updated DEFAULT SYSUTCDATETIME(),
  CONSTRAINT UX_hall_venue_code UNIQUE (property_id, code),
  CONSTRAINT UX_hall_venue_name UNIQUE (property_id, name));
CREATE TABLE hall.venue_slot(
  venue_id INT NOT NULL CONSTRAINT FK_hall_venue_slot_venue REFERENCES hall.venue,
  slot VARCHAR(10) NOT NULL CONSTRAINT CK_hall_venue_slot_slot CHECK (slot IN ('MORNING','AFTERNOON','EVENING','FULL_DAY')),
  time_from CHAR(5) NOT NULL,
  time_to CHAR(5) NOT NULL,
  rate_paise BIGINT NOT NULL CONSTRAINT CK_hall_venue_slot_rate CHECK (rate_paise >= 0),
  min_plates INT NULL,
  CONSTRAINT PK_hall_venue_slot PRIMARY KEY (venue_id, slot),
  CONSTRAINT CK_hall_venue_slot_times CHECK (time_from < time_to));
CREATE TABLE hall.enquiry(
  enquiry_id BIGINT IDENTITY CONSTRAINT PK_hall_enquiry PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_hall_enquiry_property REFERENCES inv.property,
  guest_id BIGINT NULL,
  company_id BIGINT NULL,
  name NVARCHAR(80) NOT NULL,
  phone VARCHAR(16) NOT NULL,
  event_type VARCHAR(12) NOT NULL CONSTRAINT CK_hall_enquiry_type CHECK (event_type IN ('WEDDING','RECEPTION','BIRTHDAY','ENGAGEMENT','CORPORATE','CONFERENCE','RELIGIOUS','OTHER')),
  dates VARCHAR(100) NOT NULL,            -- yyyy-MM-dd, comma-separated, sorted (1–7)
  slot VARCHAR(10) NOT NULL CONSTRAINT CK_hall_enquiry_slot CHECK (slot IN ('MORNING','AFTERNOON','EVENING','FULL_DAY')),
  pax INT NOT NULL CONSTRAINT CK_hall_enquiry_pax CHECK (pax BETWEEN 1 AND 5000),
  venue_id INT NULL CONSTRAINT FK_hall_enquiry_venue REFERENCES hall.venue,
  source VARCHAR(10) NOT NULL CONSTRAINT CK_hall_enquiry_source CHECK (source IN ('WALKIN','PHONE','REFERENCE','WEBSITE','REPEAT')),
  follow_up DATE NOT NULL,
  note NVARCHAR(500) NULL,
  status VARCHAR(8) NOT NULL CONSTRAINT CK_hall_enquiry_status CHECK (status IN ('OPEN','QUOTED','WON','LOST')),
  lost_reason_id VARCHAR(20) NULL,
  lost_note NVARCHAR(300) NULL,
  soft_venue_id INT NULL CONSTRAINT FK_hall_enquiry_soft_venue REFERENCES hall.venue,
  soft_until_utc DATETIME2(3) NULL,
  soft_by INT NULL,
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  version INT NOT NULL CONSTRAINT DF_hall_enquiry_version DEFAULT 1,
  CONSTRAINT CK_hall_enquiry_lost CHECK (status <> 'LOST' OR lost_reason_id IS NOT NULL),
  CONSTRAINT CK_hall_enquiry_soft CHECK ((soft_venue_id IS NULL AND soft_until_utc IS NULL) OR (soft_venue_id IS NOT NULL AND soft_until_utc IS NOT NULL)));
CREATE INDEX IX_hall_enquiry_follow ON hall.enquiry(property_id, status, follow_up);
CREATE TABLE hall.quotation(
  quotation_id BIGINT IDENTITY CONSTRAINT PK_hall_quotation PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_hall_quotation_property REFERENCES inv.property,
  number VARCHAR(20) NOT NULL,            -- QT/2627/0001 (H2: QUO number series, restarts every 1 April, no gaps)
  fy CHAR(4) NOT NULL,
  enquiry_id BIGINT NOT NULL CONSTRAINT FK_hall_quotation_enquiry REFERENCES hall.enquiry,
  current_version INT NOT NULL CONSTRAINT DF_hall_quotation_current DEFAULT 1,
  customer_name NVARCHAR(80) NOT NULL,
  customer_phone VARCHAR(16) NOT NULL,
  company_id BIGINT NULL,
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  CONSTRAINT UX_hall_quotation_number UNIQUE (property_id, number));
CREATE TABLE hall.quotation_version(
  version_id BIGINT IDENTITY CONSTRAINT PK_hall_quotation_version PRIMARY KEY,
  quotation_id BIGINT NOT NULL CONSTRAINT FK_hall_quotation_version_q REFERENCES hall.quotation,
  v INT NOT NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_hall_quotation_version_status CHECK (status IN ('DRAFT','SENT','SUPERSEDED','EXPIRED','ACCEPTED','LOST','CANCELLED')),
  event_type VARCHAR(12) NOT NULL,
  dates VARCHAR(100) NOT NULL,
  slot VARCHAR(10) NOT NULL CONSTRAINT CK_hall_quotation_version_slot CHECK (slot IN ('MORNING','AFTERNOON','EVENING','FULL_DAY')),
  venue_id INT NOT NULL CONSTRAINT FK_hall_quotation_version_venue REFERENCES hall.venue,
  setup VARCHAR(10) NOT NULL CONSTRAINT CK_hall_quotation_version_setup CHECK (setup IN ('THEATRE','ROUND','USHAPE','FLOATING','CLASSROOM')),
  pax INT NOT NULL,
  guaranteed_pax INT NOT NULL,
  valid_until DATE NOT NULL,
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  sent_utc DATETIME2(3) NULL,
  override_by INT NULL, override_reason NVARCHAR(200) NULL, override_utc DATETIME2(3) NULL,
  approval_id BIGINT NULL,
  CONSTRAINT UX_hall_quotation_version UNIQUE (quotation_id, v));
CREATE TABLE hall.quotation_line(
  version_id BIGINT NOT NULL CONSTRAINT FK_hall_quotation_line_version REFERENCES hall.quotation_version,
  seq INT NOT NULL,
  kind VARCHAR(6) NOT NULL CONSTRAINT CK_hall_quotation_line_kind CHECK (kind IN ('HALL','MENU','EXTRA','ROOM','OTHER')),
  ref_id VARCHAR(40) NULL,
  descr NVARCHAR(120) NOT NULL,
  qty INT NOT NULL CONSTRAINT CK_hall_quotation_line_qty CHECK (qty > 0),
  unit VARCHAR(12) NOT NULL,
  rate_paise BIGINT NOT NULL CONSTRAINT CK_hall_quotation_line_rate CHECK (rate_paise >= 0),
  list_rate_paise BIGINT NOT NULL CONSTRAINT CK_hall_quotation_line_list CHECK (list_rate_paise >= 0),
  discount_paise BIGINT NOT NULL CONSTRAINT DF_hall_quotation_line_disc DEFAULT 0,
  gst_class VARCHAR(16) NOT NULL CONSTRAINT CK_hall_quotation_line_class CHECK (gst_class IN ('HALL_RENT','BANQUET_FOOD','SERVICE','ROOM')),
  CONSTRAINT PK_hall_quotation_line PRIMARY KEY (version_id, seq));
CREATE TABLE hall.event(
  event_id BIGINT IDENTITY CONSTRAINT PK_hall_event PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_hall_event_property REFERENCES inv.property,
  quotation_id BIGINT NOT NULL CONSTRAINT FK_hall_event_quotation REFERENCES hall.quotation,
  version INT NOT NULL,
  venue_id INT NOT NULL CONSTRAINT FK_hall_event_venue REFERENCES hall.venue,
  event_date DATE NOT NULL,
  dates VARCHAR(100) NOT NULL,
  slot VARCHAR(10) NOT NULL CONSTRAINT CK_hall_event_slot CHECK (slot IN ('MORNING','AFTERNOON','EVENING','FULL_DAY')),
  event_type VARCHAR(12) NOT NULL,
  guaranteed_pax INT NOT NULL,
  actual_pax INT NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_hall_event_status CHECK (status IN ('CONFIRMED','BILLED','CANCELLED')),
  customer_name NVARCHAR(80) NOT NULL,
  customer_phone VARCHAR(16) NOT NULL,
  company_id BIGINT NULL,
  block_id BIGINT NULL,
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  idem_key VARCHAR(80) NULL,
  row_ver ROWVERSION);
CREATE INDEX IX_hall_event_date ON hall.event(property_id, event_date, status);
CREATE UNIQUE INDEX UX_hall_event_quotation ON hall.event(quotation_id) WHERE status <> 'CANCELLED';
CREATE TABLE hall.event_line(
  event_id BIGINT NOT NULL CONSTRAINT FK_hall_event_line_event REFERENCES hall.event,
  seq INT NOT NULL,
  is_extra BIT NOT NULL CONSTRAINT DF_hall_event_line_extra DEFAULT 0,
  kind VARCHAR(6) NOT NULL CONSTRAINT CK_hall_event_line_kind CHECK (kind IN ('HALL','MENU','EXTRA','ROOM','OTHER')),
  ref_id VARCHAR(40) NULL,
  descr NVARCHAR(120) NOT NULL,
  qty INT NOT NULL CONSTRAINT CK_hall_event_line_qty CHECK (qty > 0),
  unit VARCHAR(12) NOT NULL,
  rate_paise BIGINT NOT NULL,
  list_rate_paise BIGINT NOT NULL,
  discount_paise BIGINT NOT NULL CONSTRAINT DF_hall_event_line_disc DEFAULT 0,
  gst_class VARCHAR(16) NOT NULL CONSTRAINT CK_hall_event_line_class CHECK (gst_class IN ('HALL_RENT','BANQUET_FOOD','SERVICE','ROOM')),
  tax_bps INT NULL,                        -- snapshot set at the invoice (posted lines keep their rate)
  CONSTRAINT PK_hall_event_line PRIMARY KEY (event_id, seq));
CREATE TABLE hall.calendar_hold(
  hold_id BIGINT IDENTITY CONSTRAINT PK_hall_calendar_hold PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_hall_calendar_hold_property REFERENCES inv.property,
  venue_id INT NOT NULL CONSTRAINT FK_hall_calendar_hold_venue REFERENCES hall.venue,     -- a physical hall (combined venues expand to their halls)
  hold_date DATE NOT NULL,
  part VARCHAR(10) NOT NULL CONSTRAINT CK_hall_calendar_hold_part CHECK (part IN ('MORNING','AFTERNOON','EVENING')),
  kind VARCHAR(4) NOT NULL CONSTRAINT CK_hall_calendar_hold_kind CHECK (kind IN ('HARD','SOFT')),
  event_id BIGINT NULL CONSTRAINT FK_hall_calendar_hold_event REFERENCES hall.event,
  enquiry_id BIGINT NULL CONSTRAINT FK_hall_calendar_hold_enquiry REFERENCES hall.enquiry,
  released_utc DATETIME2(3) NULL,
  CONSTRAINT CK_hall_calendar_hold_ref CHECK ((kind = 'HARD' AND event_id IS NOT NULL) OR (kind = 'SOFT' AND enquiry_id IS NOT NULL)));
CREATE UNIQUE INDEX UX_hall_calendar_hold_hard ON hall.calendar_hold(property_id, venue_id, hold_date, part) WHERE kind = 'HARD' AND released_utc IS NULL;
