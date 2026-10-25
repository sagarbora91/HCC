-- HCC migration 2000 · W3-2 booking website / web reservations (block 2000–2099). Spec: modules/HCC_Module_21_22_Channels_Booking_Site_Spec.md §3.2
-- (R10–R13); prototype rules: domain/streams/channels.ts (chnWebQuote, chnWebSearchError), handlers/channels.ts (CHN_WEB_*).
-- Tables live in schema web. Guest contact data (name, phone, e-mail) is held here only for the review queue; no identity-document
-- number is ever collected on the public path (DEC-068). The web request does NOT hold inventory: confirming it creates the
-- reservation through the Reservations service (QuoteStay + CreateReservation), so availability / restrictions / day-closed rules are
-- the same as a manual booking. A simulated deposit link follows a confirmed request; "paid" posts a deposit receipt on the folio.
-- Adds no column to a table owned by another module (reservation_id is a plain FK to inv.reservation).
CREATE SCHEMA web;
GO
CREATE SEQUENCE web.seq_request_no AS INT START WITH 1 INCREMENT BY 1;
GO
CREATE TABLE web.request(
  request_id BIGINT IDENTITY CONSTRAINT PK_web_request PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_web_request_property REFERENCES inv.property,
  public_no VARCHAR(20) NOT NULL,
  room_type_id INT NOT NULL CONSTRAINT FK_web_request_room_type REFERENCES inv.room_type,
  arrival DATE NOT NULL,
  departure DATE NOT NULL,
  adults TINYINT NOT NULL,
  children TINYINT NOT NULL CONSTRAINT DF_web_request_children DEFAULT 0,
  guest_name NVARCHAR(200) NOT NULL,
  phone VARCHAR(20) NOT NULL,
  email NVARCHAR(200) NULL,
  notes NVARCHAR(200) NULL,
  net_paise BIGINT NOT NULL,
  tax_paise BIGINT NOT NULL,
  total_paise BIGINT NOT NULL,
  quote_nights NVARCHAR(MAX) NOT NULL CONSTRAINT CK_web_request_nights CHECK (ISJSON(quote_nights) = 1),   -- the nightly quote the guest saw
  status VARCHAR(10) NOT NULL CONSTRAINT DF_web_request_status DEFAULT 'Pending',
  reason NVARCHAR(200) NULL,
  claimed_by INT NULL,                -- staff confirming right now (status Confirming)
  decided_by INT NULL,
  decided_utc DATETIME2(3) NULL,
  reservation_id BIGINT NULL CONSTRAINT FK_web_request_reservation REFERENCES inv.reservation,
  created_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_web_request_no UNIQUE (property_id, public_no),
  CONSTRAINT CK_web_request_status CHECK (status IN ('Pending','Confirming','Confirmed','Declined')),
  CONSTRAINT CK_web_request_dates CHECK (departure > arrival),
  CONSTRAINT CK_web_request_party CHECK (adults BETWEEN 1 AND 4 AND children BETWEEN 0 AND 3),
  CONSTRAINT CK_web_request_money CHECK (net_paise >= 0 AND tax_paise >= 0 AND total_paise = net_paise + tax_paise),
  CONSTRAINT CK_web_request_confirmed CHECK ((status = 'Confirmed' AND reservation_id IS NOT NULL) OR (status <> 'Confirmed' AND reservation_id IS NULL)));
CREATE INDEX IX_web_request_queue ON web.request(property_id, status, created_utc);
CREATE INDEX IX_web_request_phone ON web.request(property_id, phone) WHERE status IN ('Pending','Confirming');
GO
-- Simulated deposit link: the token is shown once at creation; only its SHA-256 is stored.
CREATE TABLE web.deposit_link(
  link_id BIGINT IDENTITY CONSTRAINT PK_web_deposit_link PRIMARY KEY,
  request_id BIGINT NOT NULL CONSTRAINT FK_web_deposit_link_request REFERENCES web.request,
  token_hash BINARY(32) NOT NULL CONSTRAINT UQ_web_deposit_link_token UNIQUE,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_web_deposit_link_amount CHECK (amount_paise > 0 AND amount_paise <= 10000000000),
  status VARCHAR(10) NOT NULL CONSTRAINT DF_web_deposit_link_status DEFAULT 'Open',
  expires_utc DATETIME2(3) NOT NULL,
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  paid_utc DATETIME2(3) NULL,
  paid_by INT NULL,
  paid_ref VARCHAR(80) NULL,
  receipt_id BIGINT NULL,             -- bill receipt posted when paid (plain id, billing owns the table)
  row_ver ROWVERSION,
  CONSTRAINT CK_web_deposit_link_status CHECK (status IN ('Open','Paid','Cancelled')),
  CONSTRAINT CK_web_deposit_link_paid CHECK ((status = 'Paid' AND paid_utc IS NOT NULL AND receipt_id IS NOT NULL) OR (status <> 'Paid' AND paid_utc IS NULL)));
CREATE UNIQUE INDEX UX_web_deposit_link_open ON web.deposit_link(request_id) WHERE status = 'Open';
GO
