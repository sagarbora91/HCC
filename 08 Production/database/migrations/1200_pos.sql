-- HCC migration 1200 · Restaurant POS & room service (W2-6, Wave 2 block 1200–1299). Prototype stream pos (module 23 spec §3–§5).
-- Outlets, menu, checks (orders), lines, KOTs, bills, outlet receipts, void requests. Cash settles into the SHARED drawer through the
-- billing/payments ICashDrawerPort (DEC-052: pay.cash_movement kind 'pos-sale' / 'pos-void'); room posting is one bill.charge service line
-- through Billing's folio port. No PERM ids are added: POS reuses PERM-FOL-002 (operate/post to room), PERM-PAY-001 (settle/void a bill) and
-- PERM-APR-001 (decide a void above the limit) — the same roles as the prototype's billing / payments / approve (RBAC matrix, DEC-063).
-- Original HCC design; no donor code. Event times are written by the application from its TimeProvider.
CREATE SCHEMA pos;
GO
-- Outlets: 'table' outlets have named tables, 'room' outlets (room service) target an in-house room.
CREATE TABLE pos.outlet(
  outlet_id INT IDENTITY CONSTRAINT PK_pos_outlet PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_pos_outlet_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(100) NOT NULL,
  kind VARCHAR(5) NOT NULL CONSTRAINT CK_pos_outlet_kind CHECK (kind IN ('table','room')),
  created_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_pos_outlet UNIQUE (property_id, code));
CREATE TABLE pos.outlet_table(
  outlet_id INT NOT NULL CONSTRAINT FK_pos_outlet_table_outlet REFERENCES pos.outlet,
  table_no VARCHAR(10) NOT NULL,
  CONSTRAINT PK_pos_outlet_table PRIMARY KEY (outlet_id, table_no));
CREATE TABLE pos.menu_item(
  item_id INT IDENTITY CONSTRAINT PK_pos_menu_item PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_pos_menu_item_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(100) NOT NULL,
  category NVARCHAR(60) NOT NULL,
  price_paise BIGINT NOT NULL CONSTRAINT CK_pos_menu_price CHECK (price_paise BETWEEN 0 AND 10000000000),
  veg BIT NOT NULL,
  available BIT NOT NULL CONSTRAINT DF_pos_menu_available DEFAULT 1,   -- 0 = "86" (unavailable)
  row_ver ROWVERSION,
  CONSTRAINT UQ_pos_menu_item UNIQUE (property_id, code));
CREATE TABLE pos.menu_item_outlet(
  item_id INT NOT NULL CONSTRAINT FK_pos_menu_item_outlet_item REFERENCES pos.menu_item,
  outlet_id INT NOT NULL CONSTRAINT FK_pos_menu_item_outlet_outlet REFERENCES pos.outlet,
  CONSTRAINT PK_pos_menu_item_outlet PRIMARY KEY (item_id, outlet_id));
-- Bill number sequence per property (POS/YYMM/NNNN, never reused; read under UPDLOCK in the settling transaction).
CREATE TABLE pos.counter(
  property_id INT NOT NULL CONSTRAINT PK_pos_counter PRIMARY KEY CONSTRAINT FK_pos_counter_property REFERENCES inv.property,
  bill_seq INT NOT NULL CONSTRAINT DF_pos_counter_bill DEFAULT 0);
GO
-- Checks. Exactly one of table_no / room_id. rev moves on every change (void requests bind to it, DEC-041 style); row_ver is the wire version.
CREATE TABLE pos.check_order(
  order_id BIGINT IDENTITY CONSTRAINT PK_pos_check_order PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_pos_check_property REFERENCES inv.property,
  outlet_id INT NOT NULL CONSTRAINT FK_pos_check_outlet REFERENCES pos.outlet,
  table_no VARCHAR(10) NULL,
  room_id INT NULL CONSTRAINT FK_pos_check_room REFERENCES inv.room,
  stay_id BIGINT NULL CONSTRAINT FK_pos_check_stay REFERENCES gst.stay,
  guest NVARCHAR(60) NULL,
  covers TINYINT NOT NULL CONSTRAINT CK_pos_check_covers CHECK (covers BETWEEN 1 AND 20),
  business_date DATE NOT NULL,               -- business date the check was opened
  opened_utc DATETIME2(3) NOT NULL,
  opened_by INT NOT NULL,
  status VARCHAR(7) NOT NULL CONSTRAINT CK_pos_check_status CHECK (status IN ('Open','Settled','Void')),
  pending_void_id BIGINT NULL,               -- FK added below; the check is locked until the request is decided
  void_reason NVARCHAR(200) NULL,
  voided_by INT NULL,
  voided_utc DATETIME2(3) NULL,
  rev INT NOT NULL CONSTRAINT DF_pos_check_rev DEFAULT 1,
  row_ver ROWVERSION,
  CONSTRAINT CK_pos_check_spot CHECK ((table_no IS NOT NULL AND room_id IS NULL) OR (table_no IS NULL AND room_id IS NOT NULL)),
  CONSTRAINT CK_pos_check_void CHECK (status <> 'Void' OR (void_reason IS NOT NULL AND voided_by IS NOT NULL AND voided_utc IS NOT NULL)));
-- One open check per table and per room (backstop; the handler also checks first).
CREATE UNIQUE INDEX UX_pos_check_open_table ON pos.check_order(outlet_id, table_no) WHERE status = 'Open' AND table_no IS NOT NULL;
CREATE UNIQUE INDEX UX_pos_check_open_room ON pos.check_order(outlet_id, room_id) WHERE status = 'Open' AND room_id IS NOT NULL;
CREATE INDEX IX_pos_check_property_status ON pos.check_order(property_id, status, business_date);
CREATE TABLE pos.kot(
  kot_id BIGINT IDENTITY CONSTRAINT PK_pos_kot PRIMARY KEY,
  order_id BIGINT NOT NULL CONSTRAINT FK_pos_kot_order REFERENCES pos.check_order,
  outlet_id INT NOT NULL CONSTRAINT FK_pos_kot_outlet REFERENCES pos.outlet,
  business_date DATE NOT NULL,
  kot_no INT NOT NULL,                       -- per outlet per business date
  status VARCHAR(9) NOT NULL CONSTRAINT CK_pos_kot_status CHECK (status IN ('Preparing','Ready','Served')),
  created_utc DATETIME2(3) NOT NULL,
  created_by INT NOT NULL,
  ready_utc DATETIME2(3) NULL,
  served_utc DATETIME2(3) NULL,
  CONSTRAINT UQ_pos_kot_no UNIQUE (outlet_id, business_date, kot_no));
CREATE INDEX IX_pos_kot_order ON pos.kot(order_id);
CREATE TABLE pos.void_request(
  void_id BIGINT IDENTITY CONSTRAINT PK_pos_void_request PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_pos_void_property REFERENCES inv.property,
  order_id BIGINT NOT NULL CONSTRAINT FK_pos_void_order REFERENCES pos.check_order,
  scope VARCHAR(4) NOT NULL CONSTRAINT CK_pos_void_scope CHECK (scope IN ('item','bill')),
  line_id BIGINT NULL,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_pos_void_amount CHECK (amount_paise > 0 AND amount_paise <= 10000000000),
  reason NVARCHAR(200) NOT NULL,
  status VARCHAR(8) NOT NULL CONSTRAINT CK_pos_void_status CHECK (status IN ('Pending','Rejected','Executed')),
  order_rev INT NOT NULL,                    -- check revision at request time; a changed check makes the approval STALE_VERSION
  requested_by INT NOT NULL,
  requested_utc DATETIME2(3) NOT NULL,
  expires_utc DATETIME2(3) NOT NULL,
  decided_by INT NULL,
  decided_utc DATETIME2(3) NULL,
  decision_reason NVARCHAR(200) NULL,
  row_ver ROWVERSION,
  -- PERM-SOD-001: nobody decides their own request.
  CONSTRAINT CK_pos_void_sod CHECK (decided_by IS NULL OR decided_by <> requested_by));
CREATE INDEX IX_pos_void_order ON pos.void_request(order_id, status);
ALTER TABLE pos.check_order ADD CONSTRAINT FK_pos_check_pending_void FOREIGN KEY (pending_void_id) REFERENCES pos.void_request;
GO
CREATE TABLE pos.order_line(
  line_id BIGINT IDENTITY CONSTRAINT PK_pos_order_line PRIMARY KEY,
  order_id BIGINT NOT NULL CONSTRAINT FK_pos_line_order REFERENCES pos.check_order,
  item_id INT NOT NULL CONSTRAINT FK_pos_line_item REFERENCES pos.menu_item,
  name NVARCHAR(100) NOT NULL,               -- snapshot of the menu name and price at the time of ordering
  price_paise BIGINT NOT NULL CONSTRAINT CK_pos_line_price CHECK (price_paise BETWEEN 0 AND 10000000000),
  qty SMALLINT NOT NULL CONSTRAINT CK_pos_line_qty CHECK (qty BETWEEN 1 AND 50),
  kot_id BIGINT NULL CONSTRAINT FK_pos_line_kot REFERENCES pos.kot,   -- set when sent to the kitchen
  voided BIT NOT NULL CONSTRAINT DF_pos_line_voided DEFAULT 0,
  void_reason NVARCHAR(200) NULL,
  void_by INT NULL,
  void_date DATE NULL,                       -- business date of the void
  void_request_id BIGINT NULL CONSTRAINT FK_pos_line_void_request REFERENCES pos.void_request,
  CONSTRAINT CK_pos_line_void CHECK (voided = 0 OR (void_reason IS NOT NULL AND void_by IS NOT NULL AND void_date IS NOT NULL)));
CREATE INDEX IX_pos_line_order ON pos.order_line(order_id);
-- The settlement of a check (one bill per check). mode 'outlet' = Cash/UPI/Card at the outlet; 'folio' = one service line on the room folio.
CREATE TABLE pos.bill(
  bill_id BIGINT IDENTITY CONSTRAINT PK_pos_bill PRIMARY KEY,
  order_id BIGINT NOT NULL CONSTRAINT UQ_pos_bill_order UNIQUE CONSTRAINT FK_pos_bill_order REFERENCES pos.check_order,
  property_id INT NOT NULL CONSTRAINT FK_pos_bill_property REFERENCES inv.property,
  bill_no VARCHAR(20) NOT NULL,
  mode VARCHAR(6) NOT NULL CONSTRAINT CK_pos_bill_mode CHECK (mode IN ('outlet','folio')),
  tender VARCHAR(8) NULL CONSTRAINT CK_pos_bill_tender CHECK (tender IS NULL OR tender IN ('Cash','UPI','Card')),
  reference NVARCHAR(120) NULL,
  net_paise BIGINT NOT NULL CONSTRAINT CK_pos_bill_net CHECK (net_paise > 0),
  tax_bps INT NOT NULL CONSTRAINT CK_pos_bill_bps CHECK (tax_bps BETWEEN 0 AND 10000),   -- snapshot at settlement
  tax_paise BIGINT NOT NULL CONSTRAINT CK_pos_bill_tax CHECK (tax_paise >= 0),
  total_paise BIGINT NOT NULL,
  business_date DATE NOT NULL,
  settled_utc DATETIME2(3) NOT NULL,
  settled_by INT NOT NULL,
  till_id INT NULL CONSTRAINT FK_pos_bill_till REFERENCES pay.till,       -- cash bills: the shared drawer's till (DEC-052)
  shift_id BIGINT NULL CONSTRAINT FK_pos_bill_shift REFERENCES pay.shift,
  cash_movement_id BIGINT NULL CONSTRAINT FK_pos_bill_movement REFERENCES pay.cash_movement,
  stay_id BIGINT NULL CONSTRAINT FK_pos_bill_stay REFERENCES gst.stay,
  folio_id BIGINT NULL CONSTRAINT FK_pos_bill_folio REFERENCES bill.folio,
  charge_id BIGINT NULL CONSTRAINT FK_pos_bill_charge REFERENCES bill.charge,
  status VARCHAR(7) NOT NULL CONSTRAINT DF_pos_bill_status DEFAULT 'Settled' CONSTRAINT CK_pos_bill_status CHECK (status IN ('Settled','Void')),
  void_reason NVARCHAR(200) NULL,
  voided_by INT NULL,
  voided_utc DATETIME2(3) NULL,
  CONSTRAINT UQ_pos_bill_no UNIQUE (property_id, bill_no),
  CONSTRAINT CK_pos_bill_total CHECK (total_paise = net_paise + tax_paise),
  CONSTRAINT CK_pos_bill_shape CHECK ((mode = 'outlet' AND tender IS NOT NULL AND folio_id IS NULL AND charge_id IS NULL)
                                   OR (mode = 'folio' AND tender IS NULL AND folio_id IS NOT NULL AND charge_id IS NOT NULL AND till_id IS NULL)),
  CONSTRAINT CK_pos_bill_cash CHECK (tender IS NULL OR tender <> 'Cash' OR (till_id IS NOT NULL AND shift_id IS NOT NULL AND cash_movement_id IS NOT NULL)));
CREATE INDEX IX_pos_bill_date ON pos.bill(property_id, business_date);
-- Outlet receipts (prototype posX.receipts): a sale is positive, a void of a settled outlet bill adds the negative reversal. Append-only.
CREATE TABLE pos.receipt(
  receipt_id BIGINT IDENTITY CONSTRAINT PK_pos_receipt PRIMARY KEY,
  bill_id BIGINT NOT NULL CONSTRAINT FK_pos_receipt_bill REFERENCES pos.bill,
  order_id BIGINT NOT NULL CONSTRAINT FK_pos_receipt_order REFERENCES pos.check_order,
  outlet_id INT NOT NULL CONSTRAINT FK_pos_receipt_outlet REFERENCES pos.outlet,
  kind VARCHAR(4) NOT NULL CONSTRAINT CK_pos_receipt_kind CHECK (kind IN ('sale','void')),
  tender VARCHAR(8) NOT NULL CONSTRAINT CK_pos_receipt_tender CHECK (tender IN ('Cash','UPI','Card')),
  amount_paise BIGINT NOT NULL CONSTRAINT CK_pos_receipt_amount CHECK (amount_paise <> 0),
  net_paise BIGINT NOT NULL,
  tax_paise BIGINT NOT NULL,
  tax_bps INT NOT NULL,
  reference NVARCHAR(120) NOT NULL,
  business_date DATE NOT NULL,
  principal_id INT NOT NULL,
  recorded_utc DATETIME2(3) NOT NULL,
  CONSTRAINT CK_pos_receipt_sign CHECK ((kind = 'sale' AND amount_paise > 0) OR (kind = 'void' AND amount_paise < 0)));
CREATE INDEX IX_pos_receipt_date ON pos.receipt(business_date, tender) INCLUDE (amount_paise);
GO
CREATE TRIGGER pos.TR_receipt_append_only ON pos.receipt AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51201, 'pos.receipt is append-only; a void adds a reversal receipt.', 1;
END;
GO
