-- HCC A3 spike · draft production schema SUBSET (inventory, folio, receipts, platform tables).
-- Original HCC design; no donor migrations. Disposable database only. Money = BIGINT paise. Dates: DATE for calendar/business
-- dates, DATETIME2(3) UTC for event timestamps. Full table list: 02 Planning/HCC_Production_Contracts_and_Schema_v1.md §4.
SET XACT_ABORT ON;
GO
CREATE SCHEMA inv;
GO
CREATE SCHEMA bill;
GO
CREATE SCHEMA pay;
GO
CREATE SCHEMA plat;
GO
CREATE TABLE inv.property(
  property_id INT IDENTITY PRIMARY KEY,
  code VARCHAR(20) NOT NULL UNIQUE,
  name NVARCHAR(200) NOT NULL,
  time_zone VARCHAR(64) NOT NULL,          -- validated by the application (calendar-date adapter H04)
  business_date DATE NOT NULL,             -- advanced only by an authorized day close (DEC-042)
  row_ver ROWVERSION);
CREATE TABLE inv.room_type(
  room_type_id INT IDENTITY PRIMARY KEY,
  property_id INT NOT NULL REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  max_occupancy TINYINT NOT NULL CHECK (max_occupancy BETWEEN 1 AND 10),
  CONSTRAINT UQ_room_type UNIQUE (property_id, code));
CREATE TABLE inv.room(
  room_id INT IDENTITY PRIMARY KEY,
  property_id INT NOT NULL REFERENCES inv.property,
  room_no VARCHAR(10) NOT NULL,
  room_type_id INT NOT NULL REFERENCES inv.room_type,
  hk_status VARCHAR(12) NOT NULL DEFAULT 'Ready' CHECK (hk_status IN ('Ready','Dirty','Cleaning','Inspection')),
  row_ver ROWVERSION,
  CONSTRAINT UQ_room UNIQUE (property_id, room_no));
CREATE TABLE inv.reservation(
  reservation_id BIGINT IDENTITY PRIMARY KEY,
  property_id INT NOT NULL REFERENCES inv.property,
  public_no VARCHAR(20) NOT NULL,
  guest_name NVARCHAR(200) NOT NULL,
  arrival DATE NOT NULL,
  departure DATE NOT NULL,
  status VARCHAR(12) NOT NULL CHECK (status IN ('Held','Expired','Confirmed','InHouse','Departed','Cancelled','NoShow')),
  hold_until_utc DATETIME2(3) NULL,
  row_ver ROWVERSION,
  created_utc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
  CONSTRAINT CK_res_dates CHECK (departure > arrival),
  CONSTRAINT UQ_res_no UNIQUE (property_id, public_no));
-- Exclusive inventory: one row per room per night. The primary key makes an overlapping stay impossible in any
-- interleaving; a unique (room, start, end) index would NOT (plan instructions §7 rule 4).
CREATE TABLE inv.room_night(
  room_id INT NOT NULL REFERENCES inv.room,
  night DATE NOT NULL,
  reservation_id BIGINT NOT NULL REFERENCES inv.reservation,
  CONSTRAINT PK_room_night PRIMARY KEY (room_id, night));
CREATE INDEX IX_room_night_res ON inv.room_night(reservation_id);
CREATE TABLE bill.folio(
  folio_id BIGINT IDENTITY PRIMARY KEY,
  reservation_id BIGINT NOT NULL UNIQUE REFERENCES inv.reservation,
  row_ver ROWVERSION);
CREATE TABLE bill.charge(
  charge_id BIGINT IDENTITY PRIMARY KEY,
  folio_id BIGINT NOT NULL REFERENCES bill.folio,
  kind VARCHAR(12) NOT NULL CHECK (kind IN ('room','service','discount','correction')),
  service_date DATE NOT NULL,
  label NVARCHAR(200) NOT NULL,
  amount_paise BIGINT NOT NULL,
  tax_bps INT NOT NULL CHECK (tax_bps BETWEEN 0 AND 10000),   -- snapshot at posting
  tax_paise BIGINT NOT NULL,
  reverses_charge_id BIGINT NULL REFERENCES bill.charge,
  posted_utc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME());
-- One canonical room-night charge per folio and service date (repeat-safe night posting).
CREATE UNIQUE INDEX UX_charge_room_night ON bill.charge(folio_id, service_date) WHERE kind = 'room' AND reverses_charge_id IS NULL;
CREATE TABLE pay.receipt(
  receipt_id BIGINT IDENTITY PRIMARY KEY,
  folio_id BIGINT NOT NULL REFERENCES bill.folio,
  kind VARCHAR(16) NOT NULL CHECK (kind IN ('payment','deposit','deposit-return','refund','company-credit')),
  method VARCHAR(16) NOT NULL,
  amount_paise BIGINT NOT NULL CHECK (amount_paise > 0 AND amount_paise <= 10000000000),
  refund_of BIGINT NULL REFERENCES pay.receipt,
  business_date DATE NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_refund_link CHECK ((kind = 'refund' AND refund_of IS NOT NULL) OR (kind <> 'refund' AND refund_of IS NULL)));
CREATE INDEX IX_receipt_refund_of ON pay.receipt(refund_of) WHERE refund_of IS NOT NULL;
CREATE TABLE plat.idempotency(
  property_id INT NOT NULL,
  principal_id INT NOT NULL,
  operation VARCHAR(40) NOT NULL,
  idem_key VARCHAR(80) NOT NULL,
  payload_sha256 BINARY(32) NOT NULL,
  result_json NVARCHAR(MAX) NULL,
  created_utc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
  CONSTRAINT PK_idempotency PRIMARY KEY (property_id, principal_id, operation, idem_key));
CREATE TABLE plat.outbox(
  outbox_id BIGINT IDENTITY PRIMARY KEY,
  event_type VARCHAR(60) NOT NULL,
  aggregate_id BIGINT NOT NULL,
  payload NVARCHAR(MAX) NOT NULL CHECK (ISJSON(payload) = 1),
  created_utc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
  published_utc DATETIME2(3) NULL);
CREATE TABLE plat.consumer_dedup(
  consumer VARCHAR(40) NOT NULL,
  outbox_id BIGINT NOT NULL REFERENCES plat.outbox,
  processed_utc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
  CONSTRAINT PK_consumer_dedup PRIMARY KEY (consumer, outbox_id));
CREATE TABLE plat.audit_event(
  audit_id BIGINT IDENTITY PRIMARY KEY,
  property_id INT NOT NULL,
  principal_id INT NOT NULL,
  action VARCHAR(60) NOT NULL,
  target VARCHAR(60) NOT NULL,
  detail NVARCHAR(1000) NOT NULL,
  at_utc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME());
GO
-- Book a stay: reservation + every room night + folio + outbox + audit in ONE transaction. Nights are inserted in ascending
-- order (consistent lock order). @delay keeps the transaction open to force interleaving in the concurrency test.
CREATE PROCEDURE inv.usp_book
  @property_id INT, @room_id INT, @arrival DATE, @departure DATE, @guest NVARCHAR(200), @public_no VARCHAR(20),
  @principal_id INT = 1, @delay CHAR(12) = '00:00:00.000'
AS
BEGIN
  SET NOCOUNT ON; SET XACT_ABORT ON;
  IF @departure <= @arrival THROW 50001, 'Departure must follow arrival.', 1;
  BEGIN TRY
  BEGIN TRAN;
    INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status)
      VALUES (@property_id, @public_no, @guest, @arrival, @departure, 'Confirmed');
    DECLARE @rid BIGINT = SCOPE_IDENTITY();
    INSERT inv.room_night(room_id, night, reservation_id)
      SELECT @room_id, DATEADD(day, n, @arrival), @rid
      FROM (SELECT TOP (DATEDIFF(day, @arrival, @departure)) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS n FROM sys.all_objects) AS x
      ORDER BY n;
    INSERT bill.folio(reservation_id) VALUES (@rid);
    INSERT plat.outbox(event_type, aggregate_id, payload)
      VALUES ('ReservationCreated', @rid, (SELECT @rid AS reservationId, @room_id AS roomId, @arrival AS arrival, @departure AS departure FOR JSON PATH, WITHOUT_ARRAY_WRAPPER));
    INSERT plat.audit_event(property_id, principal_id, action, target, detail) VALUES (@property_id, @principal_id, 'BOOK', @public_no, @guest);
    IF @delay <> '00:00:00.000' WAITFOR DELAY @delay;
  COMMIT;
  END TRY
  -- A caller's TRY/CATCH would otherwise see a doomed but not yet rolled-back transaction (spike finding, T02).
  BEGIN CATCH IF @@TRANCOUNT > 0 ROLLBACK; THROW; END CATCH
  SELECT @rid AS reservation_id;
END;
GO
-- Refund against one receipt. UPDLOCK+HOLDLOCK on the parent receipt serializes competing refunds, so the remainder check
-- cannot be passed twice concurrently (H03 scenario on real SQL).
CREATE PROCEDURE pay.usp_refund
  @receipt_id BIGINT, @amount BIGINT, @business_date DATE, @delay CHAR(12) = '00:00:00.000'
AS
BEGIN
  SET NOCOUNT ON; SET XACT_ABORT ON;
  BEGIN TRY
  BEGIN TRAN;
    DECLARE @captured BIGINT, @folio BIGINT, @method VARCHAR(16), @refunded BIGINT;
    SELECT @captured = amount_paise, @folio = folio_id, @method = method
      FROM pay.receipt WITH (UPDLOCK, HOLDLOCK) WHERE receipt_id = @receipt_id AND kind = 'payment';
    IF @captured IS NULL THROW 50011, 'Only a payment receipt can be refunded.', 1;
    SELECT @refunded = ISNULL(SUM(amount_paise), 0) FROM pay.receipt WHERE refund_of = @receipt_id;
    IF @amount > @captured - @refunded THROW 50010, 'Refund exceeds the refundable remainder.', 1;
    INSERT pay.receipt(folio_id, kind, method, amount_paise, refund_of, business_date)
      VALUES (@folio, 'refund', @method, @amount, @receipt_id, @business_date);
    IF @delay <> '00:00:00.000' WAITFOR DELAY @delay;
  COMMIT;
  END TRY
  BEGIN CATCH IF @@TRANCOUNT > 0 ROLLBACK; THROW; END CATCH
END;
GO
-- Post one room night; a repeat returns the existing charge instead of adding another (idempotent by business key).
-- Check-then-insert under UPDLOCK+HOLDLOCK (key-range lock) so two posters cannot both insert; the filtered unique index is
-- the backstop. Catching a duplicate-key error instead does not work inside a caller's transaction (spike finding, T07).
CREATE PROCEDURE bill.usp_post_room_night
  @folio_id BIGINT, @service_date DATE, @amount BIGINT, @tax_bps INT, @charge_id BIGINT OUTPUT, @created BIT OUTPUT
AS
BEGIN
  SET NOCOUNT ON; SET XACT_ABORT ON;
  BEGIN TRY
  BEGIN TRAN;
    SET @created = 0;
    SELECT @charge_id = charge_id FROM bill.charge WITH (UPDLOCK, HOLDLOCK)
      WHERE folio_id = @folio_id AND service_date = @service_date AND kind = 'room' AND reverses_charge_id IS NULL;
    IF @charge_id IS NULL
    BEGIN
      INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise)
        VALUES (@folio_id, 'room', @service_date, N'Room night ' + CONVERT(NVARCHAR(10), @service_date, 23), @amount, @tax_bps, (@amount * @tax_bps + 5000) / 10000);
      SET @charge_id = SCOPE_IDENTITY(); SET @created = 1;
    END
  COMMIT;
  END TRY
  BEGIN CATCH IF @@TRANCOUNT > 0 ROLLBACK; THROW; END CATCH
END;
GO
-- Fixture: one property, two room types, three rooms (fictional).
INSERT inv.property(code, name, time_zone, business_date) VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '2026-10-24');
INSERT inv.room_type(property_id, code, max_occupancy) VALUES (1, 'Classic', 2), (1, 'Deluxe', 3);
INSERT inv.room(property_id, room_no, room_type_id) VALUES (1, '101', 1), (1, '102', 1), (1, '201', 2);
GO
