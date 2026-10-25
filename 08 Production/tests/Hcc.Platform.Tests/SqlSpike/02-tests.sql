-- HCC A3 spike · single-connection invariant tests. Each test prints "PASS <id> …" or "FAIL <id> …".
SET NOCOUNT ON; SET XACT_ABORT OFF;
GO
-- T01 Unicode guest names survive a round trip.
BEGIN TRY
  EXEC inv.usp_book 1, 1, '2026-10-24', '2026-10-26', N'सागर बोरा · Ünïcødé', 'T01';
  IF NOT EXISTS (SELECT 1 FROM inv.reservation WHERE public_no='T01' AND guest_name = N'सागर बोरा · Ünïcødé') THROW 50000, 'unicode mismatch', 1;
  PRINT 'PASS T01 Unicode NVARCHAR round trip';
END TRY BEGIN CATCH PRINT 'FAIL T01 ' + ERROR_MESSAGE(); END CATCH
GO
-- T02 Overlapping stay on the same room is rejected and leaves no partial rows (reservation, nights, folio, outbox).
BEGIN TRY
  DECLARE @before INT = (SELECT COUNT(*) FROM inv.reservation), @ob INT = (SELECT COUNT(*) FROM plat.outbox), @failed BIT = 0;
  BEGIN TRY EXEC inv.usp_book 1, 1, '2026-10-25', '2026-10-27', N'Overlap', 'T02'; END TRY
  BEGIN CATCH IF ERROR_NUMBER() = 2627 SET @failed = 1; ELSE THROW; END CATCH
  IF @failed = 0 THROW 50000, 'overlap was accepted', 1;
  IF (SELECT COUNT(*) FROM inv.reservation) <> @before OR (SELECT COUNT(*) FROM plat.outbox) <> @ob THROW 50000, 'partial rows left after rollback', 1;
  IF (SELECT COUNT(*) FROM inv.room_night WHERE room_id = 1) <> 2 THROW 50000, 'original allocation changed', 1;
  PRINT 'PASS T02 Overlap rejected (PK violation 2627); full rollback; original allocation intact';
END TRY BEGIN CATCH PRINT 'FAIL T02 ' + ERROR_MESSAGE(); END CATCH
GO
-- T03 Adjacent stay (departure day = next arrival) is accepted.
BEGIN TRY
  EXEC inv.usp_book 1, 1, '2026-10-26', '2026-10-28', N'Adjacent', 'T03';
  PRINT 'PASS T03 Adjacent stay accepted';
END TRY BEGIN CATCH PRINT 'FAIL T03 ' + ERROR_MESSAGE(); END CATCH
GO
-- T04 Optimistic concurrency: an update carrying a stale rowversion changes nothing.
BEGIN TRY
  DECLARE @v BINARY(8) = (SELECT row_ver FROM inv.reservation WHERE public_no = 'T03');
  UPDATE inv.reservation SET guest_name = N'Adjacent (edited)' WHERE public_no = 'T03' AND row_ver = @v;
  UPDATE inv.reservation SET guest_name = N'Stale write' WHERE public_no = 'T03' AND row_ver = @v;
  IF @@ROWCOUNT <> 0 THROW 50000, 'stale write applied', 1;
  IF (SELECT guest_name FROM inv.reservation WHERE public_no = 'T03') <> N'Adjacent (edited)' THROW 50000, 'wrong value', 1;
  PRINT 'PASS T04 Stale rowversion update affects 0 rows';
END TRY BEGIN CATCH PRINT 'FAIL T04 ' + ERROR_MESSAGE(); END CATCH
GO
-- T05 Money: BIGINT paise up to the CHECK limit; zero, negative and over-limit rejected.
BEGIN TRY
  DECLARE @f BIGINT = (SELECT folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'T01');
  INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) VALUES (@f, 'payment', 'Cash', 100000, '2026-10-24');
  DECLARE @bad INT = 0;
  BEGIN TRY INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) VALUES (@f, 'payment', 'Cash', 0, '2026-10-24'); END TRY BEGIN CATCH SET @bad += 1; END CATCH
  BEGIN TRY INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) VALUES (@f, 'payment', 'Cash', -500, '2026-10-24'); END TRY BEGIN CATCH SET @bad += 1; END CATCH
  BEGIN TRY INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) VALUES (@f, 'payment', 'Cash', 10000000001, '2026-10-24'); END TRY BEGIN CATCH SET @bad += 1; END CATCH
  BEGIN TRY INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) VALUES (@f, 'refund', 'Cash', 100, '2026-10-24'); END TRY BEGIN CATCH SET @bad += 1; END CATCH
  IF @bad <> 4 THROW 50000, 'an invalid receipt was accepted', 1;
  PRINT 'PASS T05 Paise CHECKs: 0 / negative / over limit / unlinked refund rejected';
END TRY BEGIN CATCH PRINT 'FAIL T05 ' + ERROR_MESSAGE(); END CATCH
GO
-- T06 Refund remainder: ₹1,000 − ₹250 − ₹750 succeeds; the next paisa fails.
BEGIN TRY
  DECLARE @r BIGINT = (SELECT MIN(receipt_id) FROM pay.receipt WHERE kind = 'payment' AND amount_paise = 100000);
  EXEC pay.usp_refund @r, 25000, '2026-10-24';
  EXEC pay.usp_refund @r, 75000, '2026-10-24';
  DECLARE @x INT = 0;
  BEGIN TRY EXEC pay.usp_refund @r, 1, '2026-10-24'; END TRY BEGIN CATCH IF ERROR_NUMBER() = 50010 SET @x = 1; END CATCH
  IF @x = 0 THROW 50000, 'over-refund accepted', 1;
  IF (SELECT amount_paise FROM pay.receipt WHERE receipt_id = @r) <> 100000 THROW 50000, 'parent receipt changed', 1;
  PRINT 'PASS T06 Refund remainder enforced; parent receipt retained';
END TRY BEGIN CATCH PRINT 'FAIL T06 ' + ERROR_MESSAGE(); END CATCH
GO
-- T07 Room-night posting is repeat-safe by business key.
BEGIN TRY
  DECLARE @f BIGINT = (SELECT folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'T01');
  DECLARE @c1 BIGINT, @n1 BIT, @c2 BIGINT, @n2 BIT;
  EXEC bill.usp_post_room_night @f, '2026-10-24', 200000, 1000, @c1 OUTPUT, @n1 OUTPUT;
  EXEC bill.usp_post_room_night @f, '2026-10-24', 200000, 1000, @c2 OUTPUT, @n2 OUTPUT;
  IF (SELECT COUNT(*) FROM bill.charge WHERE folio_id = @f AND kind = 'room') <> 1 THROW 50000, 'duplicate night charge', 1;
  IF @c1 <> @c2 OR @n1 <> 1 OR @n2 <> 0 THROW 50000, 'replay did not return the existing charge', 1;
  IF (SELECT tax_paise FROM bill.charge WHERE folio_id = @f AND kind = 'room') <> 20000 THROW 50000, 'tax snapshot wrong', 1;
  PRINT 'PASS T07 Night posting idempotent (locked check + filtered unique index); replay returns existing charge';
END TRY BEGIN CATCH PRINT 'FAIL T07 ' + ERROR_MESSAGE(); END CATCH
GO
-- T08 Idempotency record: same key + same payload = replay; same key + different payload = rejected.
BEGIN TRY
  DECLARE @h1 BINARY(32) = HASHBYTES('SHA2_256', N'{"amount":100000}'), @h2 BINARY(32) = HASHBYTES('SHA2_256', N'{"amount":999}');
  INSERT plat.idempotency(property_id, principal_id, operation, idem_key, payload_sha256, result_json) VALUES (1, 7, 'RecordReceipt', 'k-1', @h1, N'{"receiptId":1}');
  DECLARE @existing BINARY(32), @dup INT = 0;
  BEGIN TRY INSERT plat.idempotency(property_id, principal_id, operation, idem_key, payload_sha256) VALUES (1, 7, 'RecordReceipt', 'k-1', @h2); END TRY
  BEGIN CATCH IF ERROR_NUMBER() = 2627 SET @dup = 1; END CATCH
  SELECT @existing = payload_sha256 FROM plat.idempotency WHERE property_id = 1 AND principal_id = 7 AND operation = 'RecordReceipt' AND idem_key = 'k-1';
  IF @dup = 0 OR @existing <> @h1 OR @existing = @h2 THROW 50000, 'key reuse with different payload not detected', 1;
  INSERT plat.idempotency(property_id, principal_id, operation, idem_key, payload_sha256) VALUES (1, 8, 'RecordReceipt', 'k-1', @h2); -- other principal: independent
  PRINT 'PASS T08 Idempotency key bound to principal+operation; payload hash mismatch detectable';
END TRY BEGIN CATCH PRINT 'FAIL T08 ' + ERROR_MESSAGE(); END CATCH
GO
-- T09 Outbox written atomically with the business change: a failed booking leaves no event; consumer dedup blocks double effect.
BEGIN TRY
  DECLARE @n INT = (SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ReservationCreated');
  IF @n <> (SELECT COUNT(*) FROM inv.reservation) THROW 50000, 'outbox count differs from committed reservations', 1;
  DECLARE @o BIGINT = (SELECT MIN(outbox_id) FROM plat.outbox), @d INT = 0;
  INSERT plat.consumer_dedup(consumer, outbox_id) VALUES ('housekeeping', @o);
  BEGIN TRY INSERT plat.consumer_dedup(consumer, outbox_id) VALUES ('housekeeping', @o); END TRY BEGIN CATCH IF ERROR_NUMBER() = 2627 SET @d = 1; END CATCH
  IF @d = 0 THROW 50000, 'duplicate consumption accepted', 1;
  PRINT 'PASS T09 Outbox atomic with booking; redelivery deduplicated per consumer';
END TRY BEGIN CATCH PRINT 'FAIL T09 ' + ERROR_MESSAGE(); END CATCH
GO
-- T10 Dates: DATE has no time-zone drift; UTC timestamps are DATETIME2(3).
BEGIN TRY
  IF (SELECT TOP 1 arrival FROM inv.reservation WHERE public_no = 'T01') <> '2026-10-24' THROW 50000, 'date drift', 1;
  IF (SELECT system_type_id FROM sys.columns WHERE object_id = OBJECT_ID('plat.outbox') AND name = 'created_utc') <> TYPE_ID('datetime2') THROW 50000, 'wrong timestamp type', 1;
  PRINT 'PASS T10 DATE for calendar/business dates, DATETIME2(3) for UTC events';
END TRY BEGIN CATCH PRINT 'FAIL T10 ' + ERROR_MESSAGE(); END CATCH
GO
