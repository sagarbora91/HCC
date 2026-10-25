-- TEST-ONLY spike procedures + fixture, applied on top of production migrations 0001-0002 in a disposable database.
-- Copied unchanged from 07 Integration Lab/sql-spike/01-schema.sql (lines 114-end; original HCC work, A3 spike) so the spike
-- reruns through Microsoft.Data.SqlClient against the production schema. Never part of database/migrations.
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
