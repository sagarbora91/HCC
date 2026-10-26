-- A09-18 probe: give the seeded HccTest_PERF_* database what A09_perf_seed.mjs never creates (it books walk-ins only, so no
-- reservation is ever 'Confirmed' for a later date): advance bookings, a few holds and cancellations, and room blocks. Written
-- directly (fictional), as the inventory allocator would leave them: one inv.room_night row per night in the booked room.
-- Per room k (0..23, by room number) after the business date bd: Confirmed A at bd+10+(k%7) for 2 nights (k%4<>3), Confirmed B at
-- bd+25+(k%9) for 3 nights (k%3<>2), Held at bd+40+(k%3) for 2 nights (k%5=0), Cancelled without nights (k%6=1). The seed's
-- in-house guests leave by bd+7, so nothing overlaps. Two active blocks (rooms k=5, k=17) and four released blocks.
SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
USE [$(DB)];
DECLARE @p INT = (SELECT MIN(property_id) FROM inv.property);
DECLARE @bd DATE = (SELECT business_date FROM inv.property WHERE property_id = @p);
DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
IF EXISTS (SELECT 1 FROM inv.reservation WHERE public_no LIKE 'PRB-%') THROW 50000, 'Already enriched.', 1;
WITH rooms AS (SELECT room_id, room_type_id, ROW_NUMBER() OVER (ORDER BY room_no) - 1 AS k FROM inv.room WHERE property_id = @p)
INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, hold_until_utc, room_id, room_type_id, source, created_utc)
SELECT @p, v.no, v.guest, v.a, DATEADD(day, v.los, v.a), v.status, v.hold, r.room_id, r.room_type_id, 'Direct', @now
FROM rooms r
CROSS APPLY (VALUES
  (CASE WHEN r.k % 4 <> 3 THEN 1 END, CONCAT('PRB-', r.k, '-A'), CONCAT(N'Advance guest A', r.k, N' (fictional)'), DATEADD(day, 10 + r.k % 7, @bd), 2, 'Confirmed', CAST(NULL AS DATETIME2(3))),
  (CASE WHEN r.k % 3 <> 2 THEN 1 END, CONCAT('PRB-', r.k, '-B'), CONCAT(N'Advance guest B', r.k, N' (fictional)'), DATEADD(day, 25 + r.k % 9, @bd), 3, 'Confirmed', NULL),
  (CASE WHEN r.k % 5 = 0 THEN 1 END, CONCAT('PRB-', r.k, '-H'), CONCAT(N'Held guest ', r.k, N' (fictional)'), DATEADD(day, 40 + r.k % 3, @bd), 2, 'Held', DATEADD(day, 1, @now)),
  (CASE WHEN r.k % 6 = 1 THEN 1 END, CONCAT('PRB-', r.k, '-C'), CONCAT(N'Cancelled guest ', r.k, N' (fictional)'), DATEADD(day, 12, @bd), 2, 'Cancelled', NULL)
) v(keep, no, guest, a, los, status, hold)
WHERE v.keep = 1;
INSERT inv.room_night(room_id, night, reservation_id)
SELECT r.room_id, DATEADD(day, d.n, r.arrival), r.reservation_id
FROM inv.reservation r CROSS JOIN (VALUES (0), (1), (2), (3), (4), (5), (6), (7), (8), (9)) d(n)
WHERE r.public_no LIKE 'PRB-%' AND r.status IN ('Confirmed', 'Held') AND DATEADD(day, d.n, r.arrival) < r.departure;
WITH rooms AS (SELECT room_id, ROW_NUMBER() OVER (ORDER BY room_no) - 1 AS k FROM inv.room WHERE property_id = @p)
INSERT ops.room_block(property_id, room_id, from_date, to_date, reason, source, created_by, created_utc, released_utc, released_by)
SELECT @p, r.room_id, v.f, v.t, v.reason, 'manual', 1, @now, v.rel, CASE WHEN v.rel IS NULL THEN NULL ELSE 1 END
FROM rooms r
CROSS APPLY (VALUES
  (CASE WHEN r.k IN (5, 17) THEN 1 END, DATEADD(day, 50, @bd), DATEADD(day, 52, @bd), N'Repaint (fictional)', CAST(NULL AS DATETIME2(3))),
  (CASE WHEN r.k IN (2, 9, 14, 21) THEN 1 END, DATEADD(day, -60, @bd), DATEADD(day, -58, @bd), N'Old AC repair (fictional)', DATEADD(day, -58, @now))
) v(keep, f, t, reason, rel)
WHERE v.keep = 1;
UPDATE STATISTICS inv.reservation;
UPDATE STATISTICS inv.room_night;
SELECT @bd AS business_date,
  (SELECT COUNT(*) FROM inv.reservation WHERE status = 'Confirmed') AS confirmed,
  (SELECT COUNT(*) FROM inv.reservation WHERE status = 'Held') AS held,
  (SELECT COUNT(*) FROM inv.reservation) AS reservations,
  (SELECT COUNT(*) FROM inv.room_night) AS room_nights,
  (SELECT COUNT(*) FROM ops.room_block WHERE released_utc IS NULL) AS active_blocks,
  (SELECT COUNT(*) FROM ops.hk_task) AS hk_tasks,
  (SELECT COUNT(*) FROM ops.hk_task WHERE status <> 'Done') AS open_hk_tasks;
