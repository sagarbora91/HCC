-- HCC migration 2500 · W3-1 OTA channels (module 21; prototype src/domain/streams/channels.ts). Original HCC design.
-- Owns schema ota. SIMULATED channel manager: nothing leaves the server. Reservations are created only through the existing
-- Reservations service (never inserted from here); inventory / tariffs are read through the Inventory service.
-- Money BIGINT paise; commission / markup in basis points; dates DATE; times DATETIME2(3) UTC from the application clock.
-- Permissions: see 2501_ota_rbac_seed.sql.
IF SCHEMA_ID('ota') IS NULL EXEC('CREATE SCHEMA ota');
GO
CREATE TABLE ota.channel(
  channel_id INT IDENTITY CONSTRAINT PK_ota_channel PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_ota_channel_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(80) NOT NULL,
  status VARCHAR(12) NOT NULL CONSTRAINT DF_ota_channel_status DEFAULT 'Disconnected'
    CONSTRAINT CK_ota_channel_status CHECK (status IN ('Connected','Paused','Error','Disconnected')),
  commission_bps INT NOT NULL CONSTRAINT CK_ota_channel_commission CHECK (commission_bps BETWEEN 0 AND 3000),
  markup_bps INT NOT NULL CONSTRAINT CK_ota_channel_markup CHECK (markup_bps BETWEEN 0 AND 2500),
  last_sync_utc DATETIME2(3) NULL,
  status_note NVARCHAR(200) NOT NULL CONSTRAINT DF_ota_channel_note DEFAULT N'',
  ari_dirty_utc DATETIME2(3) NULL,            -- set by the outbox dispatcher when inventory or tariffs changed after the last push
  row_ver ROWVERSION,
  CONSTRAINT UQ_ota_channel_code UNIQUE (property_id, code));

-- Hotel room type -> the channel's room code. No row = not sold on this channel. Stop-sell is per channel and room type.
CREATE TABLE ota.channel_room_map(
  channel_id INT NOT NULL CONSTRAINT FK_ota_map_channel REFERENCES ota.channel ON DELETE CASCADE,
  room_type_id INT NOT NULL CONSTRAINT FK_ota_map_type REFERENCES inv.room_type,
  channel_code VARCHAR(20) NOT NULL CONSTRAINT CK_ota_map_code CHECK (LEN(channel_code) >= 2),
  stop_sell BIT NOT NULL CONSTRAINT DF_ota_map_stop DEFAULT 0,
  CONSTRAINT PK_ota_map PRIMARY KEY (channel_id, room_type_id),
  CONSTRAINT UQ_ota_map_code UNIQUE (channel_id, channel_code));

-- Inbound channel messages. Queued = scripted simulation not yet delivered; Pending = in the inbox; Accepted = a New turned into a
-- reservation; Applied = a Modify/Cancel processed; Rejected = refused with a reason. (channel, external_id) makes a re-delivered
-- message idempotent; one New per (channel, channel_ref) is enforced by the filtered unique index below.
CREATE TABLE ota.message(
  message_id BIGINT IDENTITY CONSTRAINT PK_ota_message PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_ota_message_property REFERENCES inv.property,
  channel_id INT NOT NULL CONSTRAINT FK_ota_message_channel REFERENCES ota.channel,
  external_id VARCHAR(40) NOT NULL,
  kind VARCHAR(6) NOT NULL CONSTRAINT CK_ota_message_kind CHECK (kind IN ('New','Modify','Cancel')),
  channel_ref VARCHAR(30) NOT NULL,
  guest_name NVARCHAR(200) NOT NULL,
  phone VARCHAR(30) NULL,
  email NVARCHAR(200) NULL,
  channel_room_code VARCHAR(20) NOT NULL,
  arrival DATE NOT NULL, departure DATE NOT NULL,
  adults TINYINT NOT NULL, children TINYINT NOT NULL CONSTRAINT DF_ota_message_children DEFAULT 0,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_ota_message_amount CHECK (amount_paise BETWEEN 0 AND 10000000000),  -- as received from the channel, reference only
  received_utc DATETIME2(3) NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_ota_message_status CHECK (status IN ('Queued','Pending','Importing','Accepted','Applied','Rejected')),
  reservation_id BIGINT NULL CONSTRAINT FK_ota_message_reservation REFERENCES inv.reservation,
  room_id INT NULL CONSTRAINT FK_ota_message_room REFERENCES inv.room,
  reason NVARCHAR(200) NULL,
  decided_by INT NULL CONSTRAINT FK_ota_message_user REFERENCES plat.app_user,
  decided_utc DATETIME2(3) NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_ota_message_external UNIQUE (channel_id, external_id),
  CONSTRAINT CK_ota_message_dates CHECK (departure > arrival),
  CONSTRAINT CK_ota_message_party CHECK (adults BETWEEN 1 AND 10 AND children BETWEEN 0 AND 10));
CREATE UNIQUE INDEX UX_ota_message_new_ref ON ota.message(channel_id, channel_ref) WHERE kind = 'New';
CREATE INDEX IX_ota_message_status ON ota.message(property_id, status, message_id);
CREATE INDEX IX_ota_message_reservation ON ota.message(reservation_id) WHERE reservation_id IS NOT NULL;

-- Append-only commission ledger per imported booking (anchored on the New message): Accrual on import, Adjustment when the channel
-- modifies the stay, Reversal when it cancels. commission_bps is the channel's rate at accrual time and stays with the booking.
CREATE TABLE ota.commission_entry(
  entry_id BIGINT IDENTITY CONSTRAINT PK_ota_commission PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_ota_commission_property REFERENCES inv.property,
  channel_id INT NOT NULL CONSTRAINT FK_ota_commission_channel REFERENCES ota.channel,
  message_id BIGINT NOT NULL CONSTRAINT FK_ota_commission_message REFERENCES ota.message,
  reservation_id BIGINT NOT NULL CONSTRAINT FK_ota_commission_reservation REFERENCES inv.reservation,
  kind VARCHAR(10) NOT NULL CONSTRAINT CK_ota_commission_kind CHECK (kind IN ('Accrual','Adjustment','Reversal')),
  base_paise BIGINT NOT NULL,                  -- signed change of the commissionable room revenue (net of GST)
  commission_bps INT NOT NULL CONSTRAINT CK_ota_commission_bps CHECK (commission_bps BETWEEN 0 AND 3000),
  commission_paise BIGINT NOT NULL,            -- signed change of the commission (half away from zero on the booking total)
  note NVARCHAR(200) NOT NULL,
  created_utc DATETIME2(3) NOT NULL, created_by INT NOT NULL CONSTRAINT FK_ota_commission_user REFERENCES plat.app_user);
CREATE INDEX IX_ota_commission_message ON ota.commission_entry(message_id, entry_id);
GO
CREATE TRIGGER ota.TR_commission_entry_immutable ON ota.commission_entry INSTEAD OF UPDATE, DELETE AS
BEGIN
  THROW 52501, 'The commission ledger is append-only.', 1;
END;
GO
-- Rate / availability pushes to the (simulated) channel manager. Queued by the command, Delivered by the outbox dispatcher.
CREATE TABLE ota.push(
  push_id BIGINT IDENTITY CONSTRAINT PK_ota_push PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_ota_push_property REFERENCES inv.property,
  channel_id INT NOT NULL CONSTRAINT FK_ota_push_channel REFERENCES ota.channel,
  requested_utc DATETIME2(3) NOT NULL, requested_by INT NOT NULL CONSTRAINT FK_ota_push_user REFERENCES plat.app_user,
  row_count INT NOT NULL, closed_count INT NOT NULL,
  summary NVARCHAR(300) NOT NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_ota_push_status CHECK (status IN ('Queued','Delivered')),
  delivered_utc DATETIME2(3) NULL, ack_ref VARCHAR(30) NULL);
CREATE INDEX IX_ota_push_channel ON ota.push(channel_id, push_id DESC);
CREATE TABLE ota.push_row(
  push_id BIGINT NOT NULL CONSTRAINT FK_ota_push_row_push REFERENCES ota.push,
  room_type_id INT NOT NULL CONSTRAINT FK_ota_push_row_type REFERENCES inv.room_type,
  night DATE NOT NULL,
  channel_code VARCHAR(20) NOT NULL,
  avail INT NOT NULL,
  rate_paise BIGINT NOT NULL,
  closed BIT NOT NULL,
  why VARCHAR(12) NULL,
  CONSTRAINT PK_ota_push_row PRIMARY KEY (push_id, room_type_id, night));
GO
