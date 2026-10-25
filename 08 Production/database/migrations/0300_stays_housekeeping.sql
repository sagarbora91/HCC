-- HCC migration 0300 · P3 stays & housekeeping (block 0300–0399). Contracts package §3 (RegisterGuest, CheckIn, CheckoutStay,
-- TransitionHousekeepingTask), §4 schemas gst/ops, rules 6–8 (audit/outbox, XACT_ABORT, locked check-then-insert).
-- Original HCC design; no donor code. Uses the 0002 tables as they are (inv.property, inv.room, inv.reservation,
-- inv.room_night, bill.folio); adds no column to tables owned by other modules.
-- Parked in database/modules/ (not database/migrations/) until the lead allows non-contiguous module blocks in the frozen
-- EmbeddedMigrationSource.Validate — see the P3 report, "Requested changes".

-- ── Guests and registration (gst) ──
-- ID documents: type + last 4 characters only, never the full number (prototype rule REQ-GUEST-006). Masked on read
-- unless the reader holds PERM-GST-003; never written to audit details or logs.
CREATE TABLE gst.guest(
  guest_id BIGINT IDENTITY CONSTRAINT PK_guest PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_guest_property REFERENCES inv.property,
  full_name NVARCHAR(200) NOT NULL,
  phone VARCHAR(20) NOT NULL,
  email NVARCHAR(200) NULL,
  address NVARCHAR(400) NULL,
  nationality NVARCHAR(60) NOT NULL CONSTRAINT DF_guest_nationality DEFAULT N'Indian',
  id_type NVARCHAR(40) NULL,
  id_last4 VARCHAR(4) NULL CONSTRAINT CK_guest_id_last4 CHECK (id_last4 IS NULL OR (LEN(id_last4) = 4 AND id_last4 NOT LIKE '%[^A-Z0-9]%')),
  company NVARCHAR(200) NULL,
  gstin VARCHAR(15) NULL,
  preferences NVARCHAR(500) NULL,
  notes NVARCHAR(1000) NULL,
  verified BIT NOT NULL CONSTRAINT DF_guest_verified DEFAULT 0,
  origin VARCHAR(10) NOT NULL CONSTRAINT CK_guest_origin CHECK (origin IN ('manual','auto')),
  created_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION);
CREATE INDEX IX_guest_phone ON gst.guest(property_id, phone);
CREATE TABLE gst.guest_party(
  guest_id BIGINT NOT NULL CONSTRAINT FK_guest_party_guest REFERENCES gst.guest,
  seq TINYINT NOT NULL CONSTRAINT CK_guest_party_seq CHECK (seq BETWEEN 1 AND 6),
  name NVARCHAR(200) NOT NULL,
  age_band VARCHAR(10) NOT NULL CONSTRAINT CK_guest_party_age CHECK (age_band IN ('Adult','Child','Infant','Senior')),
  relation NVARCHAR(60) NULL,
  CONSTRAINT PK_guest_party PRIMARY KEY (guest_id, seq));
-- One registration per reservation (the prototype's booking.registered flag + guestId link).
CREATE TABLE gst.registration(
  reservation_id BIGINT NOT NULL CONSTRAINT PK_registration PRIMARY KEY CONSTRAINT FK_registration_reservation REFERENCES inv.reservation,
  guest_id BIGINT NOT NULL CONSTRAINT FK_registration_guest REFERENCES gst.guest,
  registered_utc DATETIME2(3) NOT NULL,
  registered_by INT NOT NULL,
  row_ver ROWVERSION);
CREATE INDEX IX_registration_guest ON gst.registration(guest_id);

-- ── Stays and room segments (gst) ──
CREATE TABLE gst.stay(
  stay_id BIGINT IDENTITY CONSTRAINT PK_stay PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_stay_property REFERENCES inv.property,
  reservation_id BIGINT NOT NULL CONSTRAINT UQ_stay_reservation UNIQUE CONSTRAINT FK_stay_reservation REFERENCES inv.reservation,
  guest_id BIGINT NOT NULL CONSTRAINT FK_stay_guest REFERENCES gst.guest,
  folio_id BIGINT NOT NULL CONSTRAINT FK_stay_folio REFERENCES bill.folio,
  status VARCHAR(12) NOT NULL CONSTRAINT CK_stay_status CHECK (status IN ('InHouse','CheckedOut')),
  arrival DATE NOT NULL,
  departure DATE NOT NULL,
  checked_in_date DATE NOT NULL,               -- business date of check-in
  checked_in_utc DATETIME2(3) NOT NULL,
  checked_in_by INT NOT NULL,
  checked_out_date DATE NULL,                  -- business date of departure
  checked_out_utc DATETIME2(3) NULL,
  checked_out_by INT NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_stay_dates CHECK (departure > arrival),
  CONSTRAINT CK_stay_checkout CHECK ((status = 'CheckedOut' AND checked_out_utc IS NOT NULL) OR (status = 'InHouse' AND checked_out_utc IS NULL)));
-- Room segments [from_date, to_date); to_date NULL = the open (current) segment.
CREATE TABLE gst.stay_segment(
  stay_id BIGINT NOT NULL CONSTRAINT FK_stay_segment_stay REFERENCES gst.stay,
  seq TINYINT NOT NULL,
  room_id INT NOT NULL CONSTRAINT FK_stay_segment_room REFERENCES inv.room,
  from_date DATE NOT NULL,
  to_date DATE NULL,
  CONSTRAINT PK_stay_segment PRIMARY KEY (stay_id, seq),
  CONSTRAINT CK_stay_segment_dates CHECK (to_date IS NULL OR to_date >= from_date));
-- One occupant per room: at most one open segment per room (backstop; handlers check under the room's row lock first).
CREATE UNIQUE INDEX UX_stay_segment_open_room ON gst.stay_segment(room_id) WHERE to_date IS NULL;

-- ── Housekeeping tasks (ops) ──
CREATE TABLE ops.hk_task(
  task_id BIGINT IDENTITY CONSTRAINT PK_hk_task PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_hk_task_property REFERENCES inv.property,
  room_id INT NOT NULL CONSTRAINT FK_hk_task_room REFERENCES inv.room,
  kind VARCHAR(10) NOT NULL CONSTRAINT CK_hk_task_kind CHECK (kind IN ('checkout','stayover','repair','move')),
  status VARCHAR(12) NOT NULL CONSTRAINT CK_hk_task_status CHECK (status IN ('ToDo','InProgress','Inspection','Done')),
  assignee_id INT NULL,                        -- principal (P1 plat.app_user); NULL = not yet assigned
  stay_id BIGINT NULL CONSTRAINT FK_hk_task_stay REFERENCES gst.stay,
  causal_key VARCHAR(80) NULL,                 -- e.g. hk:checkout:<stayId> — exactly one task per cause
  checklist_mask TINYINT NOT NULL CONSTRAINT DF_hk_task_checklist DEFAULT 0 CONSTRAINT CK_hk_task_checklist CHECK (checklist_mask BETWEEN 0 AND 7),
  note NVARCHAR(500) NULL,
  reject_reason NVARCHAR(500) NULL,
  business_date DATE NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  started_utc DATETIME2(3) NULL,
  completed_utc DATETIME2(3) NULL,
  inspected_utc DATETIME2(3) NULL,
  inspected_by INT NULL,
  row_ver ROWVERSION);
CREATE UNIQUE INDEX UX_hk_task_causal ON ops.hk_task(causal_key) WHERE causal_key IS NOT NULL;
CREATE UNIQUE INDEX UX_hk_task_open_repair ON ops.hk_task(room_id) WHERE kind = 'repair' AND status <> 'Done';
CREATE INDEX IX_hk_task_room ON ops.hk_task(room_id, status);
CREATE INDEX IX_hk_task_assignee ON ops.hk_task(assignee_id, status) WHERE assignee_id IS NOT NULL;

-- ── Room blocks / out of order (ops) ──
-- A block covers nights [from_date, to_date); from_date NULL = no lower bound, to_date NULL = open-ended (prototype
-- blockedDuring). At most one active (unreleased) block per room. Inventory (P2) must treat a room as unavailable for any
-- night an active block covers: read ops.v_room_block_active or call IRoomBlockReader.
CREATE TABLE ops.room_block(
  block_id BIGINT IDENTITY CONSTRAINT PK_room_block PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_room_block_property REFERENCES inv.property,
  room_id INT NOT NULL CONSTRAINT FK_room_block_room REFERENCES inv.room,
  from_date DATE NULL,
  to_date DATE NULL,
  reason NVARCHAR(200) NOT NULL,
  source VARCHAR(12) NOT NULL CONSTRAINT CK_room_block_source CHECK (source IN ('manual','maintenance')),
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  released_utc DATETIME2(3) NULL,
  released_by INT NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_room_block_dates CHECK (to_date IS NULL OR from_date IS NULL OR to_date > from_date),
  CONSTRAINT CK_room_block_release CHECK ((released_utc IS NULL AND released_by IS NULL) OR (released_utc IS NOT NULL AND released_by IS NOT NULL)));
CREATE UNIQUE INDEX UX_room_block_active ON ops.room_block(room_id) WHERE released_utc IS NULL;
GO
CREATE VIEW ops.v_room_block_active AS
  SELECT block_id, property_id, room_id, from_date, to_date, reason, source FROM ops.room_block WHERE released_utc IS NULL;
