-- HCC migration 2200 · Groups billing (W3-4, block 2200–2299). Module 14 (group & corporate) on the production server:
-- group master (organiser, billing contact, optional GSTIN), one master folio per group, routing rules (room / tax / extras →
-- master or guest, per-member overrides), pickup billing (posted member lines get a route snapshot), re-routing as an
-- append-only transfer (never an edit of bill.charge), attrition for unpicked block rooms after the cut-off (Manager approval),
-- group settlement (deposits / payments / refunds on the master folio) and the group-statement read model.
-- Room holds stay in Inventory (inv.group_block); this module only links to a block. Charges stay on the guest folios where
-- Billing posted them (revenue and GST once); the master folio holds the routed shares, the attrition lines and its receipts.
-- Original HCC design; no donor code. Event times are written by the application from its TimeProvider.

CREATE SCHEMA grp;
GO
-- Group master (one master folio per group: the grp.master_entry rows of this group).
CREATE SEQUENCE grp.seq_group_no AS BIGINT START WITH 1001 INCREMENT BY 1;
GO
CREATE TABLE grp.group_account(
  group_id BIGINT IDENTITY CONSTRAINT PK_group_account PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_group_account_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(200) NOT NULL,
  group_block_id BIGINT NULL CONSTRAINT FK_group_account_block REFERENCES inv.group_block,
  organiser NVARCHAR(120) NOT NULL,
  billing_name NVARCHAR(200) NOT NULL,          -- legal name on the group invoice (W3-6)
  billing_contact NVARCHAR(120) NOT NULL,
  billing_phone VARCHAR(20) NULL,
  billing_email NVARCHAR(120) NULL,
  billing_address NVARCHAR(400) NULL,
  gstin CHAR(15) NULL,
  attrition_allowance_bps INT NOT NULL CONSTRAINT CK_group_allowance CHECK (attrition_allowance_bps BETWEEN 0 AND 10000),
  attrition_rate_bps INT NOT NULL CONSTRAINT CK_group_attrition_rate CHECK (attrition_rate_bps BETWEEN 0 AND 10000),
  status VARCHAR(10) NOT NULL CONSTRAINT CK_group_status CHECK (status IN ('Open','Settled')),
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  settled_by INT NULL,
  settled_utc DATETIME2(3) NULL,
  change_seq INT NOT NULL CONSTRAINT DF_group_change_seq DEFAULT 0,
  row_ver ROWVERSION,
  CONSTRAINT UQ_group_code UNIQUE (property_id, code),
  CONSTRAINT CK_group_gstin CHECK (gstin IS NULL OR LEN(gstin) = 15),
  CONSTRAINT CK_group_settled CHECK (status <> 'Settled' OR (settled_by IS NOT NULL AND settled_utc IS NOT NULL)));
CREATE UNIQUE INDEX UX_group_block ON grp.group_account(group_block_id) WHERE group_block_id IS NOT NULL;

-- Routing rules: the group default per category (member_reservation_id NULL) and per-member overrides. Categories: room (room
-- nights and discounts), extras (service lines), tax (GST of every line; 'follow' = the line's own net target).
CREATE TABLE grp.routing_rule(
  rule_id BIGINT IDENTITY CONSTRAINT PK_routing_rule PRIMARY KEY,
  group_id BIGINT NOT NULL CONSTRAINT FK_routing_rule_group REFERENCES grp.group_account,
  member_reservation_id BIGINT NULL CONSTRAINT FK_routing_rule_member REFERENCES inv.reservation,
  category VARCHAR(8) NOT NULL CONSTRAINT CK_routing_rule_category CHECK (category IN ('room','extras','tax')),
  target VARCHAR(8) NOT NULL CONSTRAINT CK_routing_rule_target CHECK (target IN ('master','guest','follow')),
  changed_by INT NOT NULL,
  changed_utc DATETIME2(3) NOT NULL,
  CONSTRAINT CK_routing_rule_follow CHECK (target <> 'follow' OR category = 'tax'));
CREATE UNIQUE INDEX UX_routing_rule_default ON grp.routing_rule(group_id, category) WHERE member_reservation_id IS NULL;
CREATE UNIQUE INDEX UX_routing_rule_member ON grp.routing_rule(group_id, member_reservation_id, category) WHERE member_reservation_id IS NOT NULL;

-- Members attached by hand (block pickups are members through inv.reservation.group_block_id). One group per reservation.
CREATE TABLE grp.member(
  group_id BIGINT NOT NULL CONSTRAINT FK_member_group REFERENCES grp.group_account,
  reservation_id BIGINT NOT NULL CONSTRAINT FK_member_reservation REFERENCES inv.reservation,
  attached_by INT NOT NULL,
  attached_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_member PRIMARY KEY (group_id, reservation_id),
  CONSTRAINT UQ_member_reservation UNIQUE (reservation_id));

-- Route snapshot of a posted member line (append-only). The current route is the highest seq; a re-route appends seq + 1 with
-- the reason (transfer with audit, the bill.charge line itself is never edited).
CREATE TABLE grp.line_route(
  route_id BIGINT IDENTITY CONSTRAINT PK_line_route PRIMARY KEY,
  group_id BIGINT NOT NULL CONSTRAINT FK_line_route_group REFERENCES grp.group_account,
  charge_id BIGINT NOT NULL CONSTRAINT FK_line_route_charge REFERENCES bill.charge,
  reservation_id BIGINT NOT NULL CONSTRAINT FK_line_route_reservation REFERENCES inv.reservation,
  seq INT NOT NULL CONSTRAINT CK_line_route_seq CHECK (seq >= 1),
  net_target VARCHAR(8) NOT NULL CONSTRAINT CK_line_route_net CHECK (net_target IN ('master','guest')),
  tax_target VARCHAR(8) NOT NULL CONSTRAINT CK_line_route_tax CHECK (tax_target IN ('master','guest')),
  source VARCHAR(8) NOT NULL CONSTRAINT CK_line_route_source CHECK (source IN ('rule','reroute')),
  reason NVARCHAR(200) NULL,
  routed_by INT NOT NULL,
  routed_utc DATETIME2(3) NOT NULL,
  CONSTRAINT UQ_line_route UNIQUE (charge_id, seq),
  CONSTRAINT CK_line_route_reason CHECK (source = 'rule' OR reason IS NOT NULL));
CREATE INDEX IX_line_route_group ON grp.line_route(group_id, charge_id, seq);

-- Attrition requests (unpicked block rooms after the cut-off): requested by group staff, decided by a different approver.
CREATE TABLE grp.attrition_request(
  attrition_id BIGINT IDENTITY CONSTRAINT PK_attrition_request PRIMARY KEY,
  group_id BIGINT NOT NULL CONSTRAINT FK_attrition_group REFERENCES grp.group_account,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_attrition_status CHECK (status IN ('Pending','Posted','Rejected')),
  net_paise BIGINT NOT NULL CONSTRAINT CK_attrition_net CHECK (net_paise > 0 AND net_paise <= 10000000000),
  tax_paise BIGINT NOT NULL CONSTRAINT CK_attrition_tax CHECK (tax_paise >= 0),
  detail NVARCHAR(MAX) NOT NULL CONSTRAINT CK_attrition_detail CHECK (ISJSON(detail) = 1),
  group_version BINARY(8) NOT NULL,             -- group rowversion at request time (decision is bound to it)
  reason NVARCHAR(200) NOT NULL,
  requested_by INT NOT NULL,
  requested_utc DATETIME2(3) NOT NULL,
  decided_by INT NULL,
  decided_utc DATETIME2(3) NULL,
  decision_reason NVARCHAR(200) NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_attrition_sod CHECK (decided_by IS NULL OR decided_by <> requested_by));
CREATE UNIQUE INDEX UX_attrition_pending ON grp.attrition_request(group_id) WHERE status = 'Pending';
CREATE UNIQUE INDEX UX_attrition_posted ON grp.attrition_request(group_id) WHERE status = 'Posted';

-- The master folio ledger (append-only): attrition charge lines (own GST snapshot, CGST + SGST per line), transfers of a
-- member's master share off the guest folio (company-credit receipt there) and the master's own receipts.
CREATE TABLE grp.master_entry(
  entry_id BIGINT IDENTITY CONSTRAINT PK_master_entry PRIMARY KEY,
  group_id BIGINT NOT NULL CONSTRAINT FK_master_entry_group REFERENCES grp.group_account,
  kind VARCHAR(10) NOT NULL CONSTRAINT CK_master_entry_kind CHECK (kind IN ('attrition','transfer','payment','deposit','refund')),
  business_date DATE NOT NULL,
  service_date DATE NULL,
  label NVARCHAR(200) NOT NULL,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_master_entry_amount CHECK (amount_paise > 0 AND amount_paise <= 10000000000),
  gst_category VARCHAR(8) NULL,
  tax_bps INT NULL CONSTRAINT CK_master_entry_bps CHECK (tax_bps IS NULL OR tax_bps BETWEEN 0 AND 10000),
  itc BIT NULL,
  tax_paise BIGINT NULL,
  cgst_paise BIGINT NULL,
  sgst_paise BIGINT NULL,
  gst_rule NVARCHAR(80) NULL,
  tender VARCHAR(16) NULL,
  reference NVARCHAR(120) NULL,
  reservation_id BIGINT NULL CONSTRAINT FK_master_entry_reservation REFERENCES inv.reservation,
  folio_id BIGINT NULL CONSTRAINT FK_master_entry_folio REFERENCES bill.folio,
  folio_receipt_id BIGINT NULL CONSTRAINT FK_master_entry_receipt REFERENCES pay.receipt,
  attrition_id BIGINT NULL CONSTRAINT FK_master_entry_attrition REFERENCES grp.attrition_request,
  refund_of BIGINT NULL CONSTRAINT FK_master_entry_refund_of REFERENCES grp.master_entry,
  principal_id INT NOT NULL,
  recorded_utc DATETIME2(3) NOT NULL,
  CONSTRAINT CK_master_entry_charge CHECK (kind <> 'attrition' OR (service_date IS NOT NULL AND tax_bps IS NOT NULL AND tax_paise IS NOT NULL
    AND cgst_paise IS NOT NULL AND sgst_paise IS NOT NULL AND cgst_paise + sgst_paise = tax_paise AND attrition_id IS NOT NULL)),
  CONSTRAINT CK_master_entry_transfer CHECK (kind <> 'transfer' OR (reservation_id IS NOT NULL AND folio_id IS NOT NULL AND folio_receipt_id IS NOT NULL)),
  CONSTRAINT CK_master_entry_receipt CHECK (kind NOT IN ('payment','deposit','refund') OR (tender IS NOT NULL AND reference IS NOT NULL)));
CREATE INDEX IX_master_entry_group ON grp.master_entry(group_id, kind);
GO
CREATE TRIGGER grp.TR_line_route_append_only ON grp.line_route AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 52201, 'grp.line_route is append-only; re-route with a new route row.', 1;
END;
GO
CREATE TRIGGER grp.TR_master_entry_append_only ON grp.master_entry AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 52202, 'grp.master_entry is append-only; post a reversing entry instead.', 1;
END;
