-- HCC migration 1500 · W2-9 guest services (Wave 2 block 1500–1599). Module 18 rules ported from the prototype
-- (03 Prototype/src/domain/streams/services.ts): in-house service requests with SLAs, complaints with root cause / recovery /
-- follow-up, lost & found with retention. Original HCC design; no donor code.
-- Money: a recovery discount is NEVER posted here. It is requested through Billing's RequestAdjustment (discount, DEC-041 binding,
-- DEC-063 limits); this block only keeps the link (bill.approval / bill.charge ids) and the amount for reporting.
-- Privacy (DEC-068): claimants keep a name, a phone and an ID-check note holding only the last characters; full ID numbers are
-- refused by the application and by CK_lost_item_id_note. Event times DATETIME2(3) UTC from the application's TimeProvider.
-- No new PERM ids or grants: the module reuses PERM-GST-002, PERM-HK-002/003, PERM-ROOM-001, PERM-APR-001, PERM-RPT-001 (see the W2-9 report).

CREATE TABLE ops.guest_service_setting(
  property_id INT NOT NULL CONSTRAINT PK_guest_service_setting PRIMARY KEY CONSTRAINT FK_guest_service_setting_property REFERENCES inv.property,
  sla_json NVARCHAR(600) NOT NULL CONSTRAINT CK_guest_service_setting_sla CHECK (ISJSON(sla_json) = 1),
  retention_days SMALLINT NOT NULL CONSTRAINT CK_guest_service_setting_ret CHECK (retention_days BETWEEN 30 AND 365),
  valuable_retention_days SMALLINT NOT NULL CONSTRAINT CK_guest_service_setting_vret CHECK (valuable_retention_days BETWEEN 30 AND 730),
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_guest_service_setting_order CHECK (valuable_retention_days >= retention_days));

-- Service requests for in-house guests (prototype GSVRequest). due_utc = created + SLA (urgent halves it, min 5); a wake-up call is
-- due its SLA after scheduled_utc.
CREATE TABLE ops.guest_request(
  request_id BIGINT IDENTITY CONSTRAINT PK_guest_request PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_guest_request_property REFERENCES inv.property,
  stay_id BIGINT NOT NULL CONSTRAINT FK_guest_request_stay REFERENCES gst.stay,
  room_id INT NOT NULL CONSTRAINT FK_guest_request_room REFERENCES inv.room,
  guest_name NVARCHAR(200) NOT NULL,
  request_type VARCHAR(30) NOT NULL CONSTRAINT CK_guest_request_type CHECK (request_type IN
    ('Extra towels','Drinking water','Iron & board','Wake-up call','Taxi','Toiletries & amenities','Minor repair','Other')),
  detail NVARCHAR(160) NOT NULL CONSTRAINT DF_guest_request_detail DEFAULT N'',
  dept VARCHAR(12) NOT NULL CONSTRAINT CK_guest_request_dept CHECK (dept IN ('Housekeeping','Maintenance','Front desk')),
  assignee_id INT NULL,
  priority VARCHAR(6) NOT NULL CONSTRAINT CK_guest_request_priority CHECK (priority IN ('Normal','Urgent')),
  status VARCHAR(12) NOT NULL CONSTRAINT CK_guest_request_status CHECK (status IN ('Open','Assigned','InProgress','Done','Cancelled')),
  created_utc DATETIME2(3) NOT NULL,
  due_utc DATETIME2(3) NOT NULL,
  scheduled_utc DATETIME2(3) NULL,
  started_utc DATETIME2(3) NULL,
  done_utc DATETIME2(3) NULL,
  guest_note NVARCHAR(200) NULL,          -- guest-facing completion note (shown to the guest; simulated message)
  cancel_reason NVARCHAR(200) NULL,
  created_by INT NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_guest_request_done CHECK (status <> 'Done' OR (done_utc IS NOT NULL AND guest_note IS NOT NULL)),
  CONSTRAINT CK_guest_request_cancel CHECK (status <> 'Cancelled' OR cancel_reason IS NOT NULL));
CREATE INDEX IX_guest_request_open ON ops.guest_request(property_id, status, due_utc);
CREATE INDEX IX_guest_request_assignee ON ops.guest_request(assignee_id, status) WHERE assignee_id IS NOT NULL;

-- Complaints (prototype GSVComplaint). Recovery columns link to the Billing adjustment; recovery_state: NULL none,
-- 'Requesting' (claimed, Billing call in flight), 'Recorded' (approval and/or charge ids stored).
CREATE TABLE ops.guest_complaint(
  complaint_id BIGINT IDENTITY CONSTRAINT PK_guest_complaint PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_guest_complaint_property REFERENCES inv.property,
  stay_id BIGINT NULL CONSTRAINT FK_guest_complaint_stay REFERENCES gst.stay,
  room_id INT NULL CONSTRAINT FK_guest_complaint_room REFERENCES inv.room,
  guest_name NVARCHAR(200) NOT NULL,
  category VARCHAR(20) NOT NULL CONSTRAINT CK_guest_complaint_category CHECK (category IN
    ('Room cleanliness','Maintenance','Noise','Staff behaviour','Food & beverage','Billing','Amenities','Other')),
  severity VARCHAR(8) NOT NULL CONSTRAINT CK_guest_complaint_severity CHECK (severity IN ('Low','Medium','High','Critical')),
  description NVARCHAR(400) NOT NULL,
  owner_id INT NOT NULL,
  status VARCHAR(13) NOT NULL CONSTRAINT CK_guest_complaint_status CHECK (status IN ('Open','Investigating','Resolved','Closed')),
  root_cause NVARCHAR(300) NULL,
  recovery_action NVARCHAR(300) NULL,
  follow_up_on DATE NULL,
  follow_up_outcome NVARCHAR(300) NULL,
  follow_up_satisfied BIT NULL,
  follow_up_utc DATETIME2(3) NULL,
  follow_up_by INT NULL,
  created_utc DATETIME2(3) NOT NULL,
  resolved_utc DATETIME2(3) NULL,
  closed_utc DATETIME2(3) NULL,
  created_by INT NOT NULL,
  recovery_state VARCHAR(10) NULL CONSTRAINT CK_guest_complaint_rstate CHECK (recovery_state IN ('Requesting','Recorded')),
  recovery_amount_paise BIGINT NULL CONSTRAINT CK_guest_complaint_ramount CHECK (recovery_amount_paise BETWEEN 1 AND 10000000000),
  recovery_reason NVARCHAR(200) NULL,
  recovery_folio_id BIGINT NULL CONSTRAINT FK_guest_complaint_folio REFERENCES bill.folio,
  recovery_approval_id BIGINT NULL CONSTRAINT FK_guest_complaint_approval REFERENCES bill.approval,
  recovery_charge_id BIGINT NULL CONSTRAINT FK_guest_complaint_charge REFERENCES bill.charge,
  recovery_utc DATETIME2(3) NULL,
  recovery_by INT NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_guest_complaint_closed CHECK (status <> 'Closed' OR closed_utc IS NOT NULL),
  CONSTRAINT CK_guest_complaint_recovery CHECK (recovery_state IS NULL OR (recovery_amount_paise IS NOT NULL AND recovery_folio_id IS NOT NULL)));
CREATE INDEX IX_guest_complaint_open ON ops.guest_complaint(property_id, status);

-- Lost & found (prototype GSVItem). Valuables are held in the front office safe; claimed by name + phone + ID-check note only.
CREATE TABLE ops.lost_item(
  item_id BIGINT IDENTITY CONSTRAINT PK_lost_item PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_lost_item_property REFERENCES inv.property,
  description NVARCHAR(160) NOT NULL,
  category VARCHAR(16) NOT NULL CONSTRAINT CK_lost_item_category CHECK (category IN
    ('Jewellery','Electronics','Cash / wallet','Documents','Clothing','Personal care','Other')),
  valuable BIT NOT NULL,
  found_where NVARCHAR(120) NOT NULL,
  room_id INT NULL CONSTRAINT FK_lost_item_room REFERENCES inv.room,
  stay_id BIGINT NULL CONSTRAINT FK_lost_item_stay REFERENCES gst.stay,   -- last stay in that room, for front-office follow-up only
  found_utc DATETIME2(3) NOT NULL,
  found_date DATE NOT NULL,                                                -- property business date; retention counts from here
  found_by INT NOT NULL,
  storage VARCHAR(22) NOT NULL CONSTRAINT CK_lost_item_storage CHECK (storage IN ('Front office safe','Housekeeping store','Back office cupboard')),
  status VARCHAR(10) NOT NULL CONSTRAINT CK_lost_item_status CHECK (status IN ('Stored','Claimed','HandedOver','Disposed')),
  claim_name NVARCHAR(100) NULL,
  claim_phone VARCHAR(15) NULL,
  claim_id_type VARCHAR(30) NULL,
  claim_id_note NVARCHAR(60) NULL,
  claim_match_note NVARCHAR(200) NULL,
  claim_utc DATETIME2(3) NULL,
  claim_by INT NULL,
  handover_to NVARCHAR(100) NULL,
  handover_note NVARCHAR(200) NULL,
  handover_utc DATETIME2(3) NULL,
  handover_by INT NULL,
  disposal_method VARCHAR(16) NULL CONSTRAINT CK_lost_item_disposal CHECK (disposal_method IN ('Donated','Destroyed','Handed to police')),
  disposal_note NVARCHAR(200) NULL,
  disposal_utc DATETIME2(3) NULL,
  disposal_by INT NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_lost_item_valuable_safe CHECK (valuable = 0 OR storage = 'Front office safe'),
  CONSTRAINT CK_lost_item_valuable_not_destroyed CHECK (valuable = 0 OR disposal_method IS NULL OR disposal_method <> 'Destroyed'),
  -- Never a full ID number: no run of 6 or more digits in the note (the application also ignores spaces and dashes).
  CONSTRAINT CK_lost_item_id_note CHECK (claim_id_note IS NULL OR claim_id_note NOT LIKE '%[0-9][0-9][0-9][0-9][0-9][0-9]%'),
  CONSTRAINT CK_lost_item_claimed CHECK (status NOT IN ('Claimed','HandedOver') OR (claim_name IS NOT NULL AND claim_utc IS NOT NULL)),
  CONSTRAINT CK_lost_item_disposed CHECK (status <> 'Disposed' OR (disposal_method IS NOT NULL AND disposal_utc IS NOT NULL)));
CREATE INDEX IX_lost_item_status ON ops.lost_item(property_id, status, found_date);

-- One history trail for all three registers (prototype log[]): who did what, when. No phone numbers or ID notes in here.
CREATE TABLE ops.guest_service_log(
  log_id BIGINT IDENTITY CONSTRAINT PK_guest_service_log PRIMARY KEY,
  kind VARCHAR(10) NOT NULL CONSTRAINT CK_guest_service_log_kind CHECK (kind IN ('request','complaint','item')),
  record_id BIGINT NOT NULL,
  at_utc DATETIME2(3) NOT NULL,
  by_principal INT NOT NULL,
  what NVARCHAR(300) NOT NULL);
CREATE INDEX IX_guest_service_log_record ON ops.guest_service_log(kind, record_id, log_id);
