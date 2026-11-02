-- HCC migration 4720 · room keys module (02 Planning/HCC_KeyAgent_Spec_v1.md §5): key_lock (room -> BIS door), key_encoder (front-desk agent),
-- key_job (every key request = the append-only log of events), key_card (card ledger). Owner rulings: room 106 has no working lock (has_lock = 0,
-- manual key); every HCC room without a BIS door has has_lock = 0 (36 HCC rooms vs 35 BIS doors; the Manager edits the mapping in settings).
-- Coordinator addition: every job and card row carries an explicit event_type (issued / reissued / extended / reactivated / cancelled / read) and a
-- reason (extension / lost / damaged / other, required for reissued, extended and reactivated); the per-booking and per-room counts are derived from this
-- append-only log (no editable counter). Rows are never deleted; only state and result columns move (triggers below).
-- Numbered 4720: the LF wave reserves 4670-4719.
CREATE TABLE ops.key_lock(
  property_id INT NOT NULL CONSTRAINT FK_key_lock_property REFERENCES inv.property,
  room_id INT NOT NULL CONSTRAINT FK_key_lock_room REFERENCES inv.room,
  lock_no INT NULL,
  door_id CHAR(6) NULL,
  door_name VARCHAR(20) NULL,
  has_lock BIT NOT NULL,
  note NVARCHAR(200) NULL,
  updated_by INT NULL,
  updated_utc DATETIME2(3) NOT NULL CONSTRAINT DF_key_lock_updated DEFAULT SYSUTCDATETIME(),
  row_ver ROWVERSION,
  CONSTRAINT PK_key_lock PRIMARY KEY (property_id, room_id),
  CONSTRAINT CK_key_lock_door CHECK (has_lock = 0 OR door_name IS NOT NULL));
CREATE TABLE ops.key_encoder(
  encoder_id VARCHAR(40) NOT NULL CONSTRAINT PK_key_encoder PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_key_encoder_property REFERENCES inv.property,
  pc_name NVARCHAR(80) NOT NULL,
  adapter VARCHAR(40) NULL,
  last_seen_utc DATETIME2(3) NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT DF_key_encoder_status DEFAULT 'offline' CONSTRAINT CK_key_encoder_status CHECK (status IN ('online', 'offline')),
  detail NVARCHAR(300) NULL,
  x86 BIT NULL,
  token_hash CHAR(64) NOT NULL,
  enabled BIT NOT NULL CONSTRAINT DF_key_encoder_enabled DEFAULT 1);
CREATE TABLE ops.key_job(
  job_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_key_job PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_key_job_property REFERENCES inv.property,
  kind VARCHAR(12) NOT NULL CONSTRAINT CK_key_job_kind CHECK (kind IN ('issue_guest', 'extend', 'cancel', 'read', 'manual')),
  event_type VARCHAR(12) NOT NULL CONSTRAINT CK_key_job_event CHECK (event_type IN ('issued', 'reissued', 'extended', 'reactivated', 'cancelled', 'read')),
  reason VARCHAR(12) NULL CONSTRAINT CK_key_job_reason CHECK (reason IS NULL OR reason IN ('extension', 'lost', 'damaged', 'other')),
  reservation_id BIGINT NULL,
  room_id INT NULL CONSTRAINT FK_key_job_room REFERENCES inv.room,
  payload_json NVARCHAR(1000) NOT NULL,
  requested_by INT NOT NULL,
  requested_utc DATETIME2(3) NOT NULL,
  encoder_id VARCHAR(40) NULL CONSTRAINT FK_key_job_encoder REFERENCES ops.key_encoder,
  state VARCHAR(12) NOT NULL CONSTRAINT CK_key_job_state CHECK (state IN ('queued', 'picked', 'waiting_card', 'done', 'failed', 'expired')),
  result_json NVARCHAR(1000) NULL,
  error_code VARCHAR(20) NULL,
  timeout_utc DATETIME2(3) NOT NULL,
  picked_utc DATETIME2(3) NULL,
  finished_utc DATETIME2(3) NULL,
  idem_key VARCHAR(80) NULL,
  warning NVARCHAR(300) NULL,
  CONSTRAINT CK_key_job_reason_required CHECK (event_type NOT IN ('reissued', 'extended', 'reactivated') OR reason IS NOT NULL));
CREATE INDEX IX_key_job_queue ON ops.key_job(property_id, state, requested_utc);
CREATE INDEX IX_key_job_reservation ON ops.key_job(reservation_id, requested_utc);
CREATE INDEX IX_key_job_room ON ops.key_job(room_id, requested_utc);
CREATE UNIQUE INDEX UX_key_job_idem ON ops.key_job(property_id, idem_key) WHERE idem_key IS NOT NULL;
CREATE TABLE ops.key_card(
  card_id BIGINT IDENTITY CONSTRAINT PK_key_card PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_key_card_property REFERENCES inv.property,
  job_id UNIQUEIDENTIFIER NULL CONSTRAINT FK_key_card_job REFERENCES ops.key_job,
  event_type VARCHAR(12) NOT NULL CONSTRAINT CK_key_card_event CHECK (event_type IN ('issued', 'reissued', 'extended', 'reactivated')),
  reason VARCHAR(12) NULL,
  card_no VARCHAR(20) NULL,
  reservation_id BIGINT NULL,
  room_id INT NOT NULL CONSTRAINT FK_key_card_room REFERENCES inv.room,
  guest_sn VARCHAR(20) NULL,
  begin_at DATETIME2(0) NOT NULL,
  end_at DATETIME2(0) NOT NULL,
  issued_by INT NOT NULL,
  issued_utc DATETIME2(3) NOT NULL,
  encoder_id VARCHAR(40) NULL,
  status VARCHAR(12) NOT NULL CONSTRAINT CK_key_card_status CHECK (status IN ('active', 'superseded', 'checked_out', 'lost', 'failed', 'manual')),
  vendor_row_seen BIT NOT NULL CONSTRAINT DF_key_card_seen DEFAULT 0,
  vendor_issued_utc DATETIME2(3) NULL);
CREATE INDEX IX_key_card_reservation ON ops.key_card(reservation_id, status);
CREATE INDEX IX_key_card_room ON ops.key_card(room_id, status);
GO
CREATE TRIGGER ops.TR_key_job_append_only ON ops.key_job AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted) THROW 51010, 'ops.key_job is append-only.', 1;
  IF UPDATE(event_type) OR UPDATE(reason) OR UPDATE(kind) OR UPDATE(room_id) OR UPDATE(reservation_id) OR UPDATE(requested_by) OR UPDATE(requested_utc) OR UPDATE(payload_json)
    THROW 51011, 'A key job keeps its event, reason, room, requester and time.', 1;
END;
GO
CREATE TRIGGER ops.TR_key_card_append_only ON ops.key_card AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted) THROW 51012, 'ops.key_card is append-only.', 1;
  IF UPDATE(event_type) OR UPDATE(reason) OR UPDATE(room_id) OR UPDATE(reservation_id) OR UPDATE(issued_by) OR UPDATE(issued_utc) OR UPDATE(begin_at) OR UPDATE(end_at)
    THROW 51013, 'A key card keeps its event, reason, room, validity, issuer and time.', 1;
END;
GO
-- Seed: every existing room. The BIS doors are 101-107, 201-214 and 301-314 (door name = room number); 106 is the owner's manual-key room, any other room has no door.
INSERT ops.key_lock(property_id, room_id, door_name, has_lock, note)
SELECT r.property_id, r.room_id,
       CASE WHEN r.room_no IN ('101','102','103','104','105','107','201','202','203','204','205','206','207','208','209','210','211','212','213','214','301','302','303','304','305','306','307','308','309','310','311','312','313','314') THEN r.room_no END,
       CASE WHEN r.room_no IN ('101','102','103','104','105','107','201','202','203','204','205','206','207','208','209','210','211','212','213','214','301','302','303','304','305','306','307','308','309','310','311','312','313','314') THEN 1 ELSE 0 END,
       CASE WHEN r.room_no = '106' THEN N'Owner ruling: no working lock, manual key'
            WHEN r.room_no NOT IN ('101','102','103','104','105','107','201','202','203','204','205','206','207','208','209','210','211','212','213','214','301','302','303','304','305','306','307','308','309','310','311','312','313','314') THEN N'No BIS door known; the Manager can map it' END
  FROM inv.room r;
