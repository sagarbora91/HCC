-- A09-18 probe, stress set: a year of advance bookings on top of A09_roomboard_enrich.sql, 12 more Confirmed bookings per room
-- (288 in all) at bd+60+25*j+(k%5) for 2 nights, j = 0..11. Shows how the board scales with the number of future bookings.
SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
USE [$(DB)];
DECLARE @p INT = (SELECT MIN(property_id) FROM inv.property);
DECLARE @bd DATE = (SELECT business_date FROM inv.property WHERE property_id = @p);
DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
IF EXISTS (SELECT 1 FROM inv.reservation WHERE public_no LIKE 'PRS-%') THROW 50000, 'Already stressed.', 1;
WITH rooms AS (SELECT room_id, room_type_id, ROW_NUMBER() OVER (ORDER BY room_no) - 1 AS k FROM inv.room WHERE property_id = @p),
     j AS (SELECT n FROM (VALUES (0), (1), (2), (3), (4), (5), (6), (7), (8), (9), (10), (11)) x(n))
INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, source, created_utc)
SELECT @p, CONCAT('PRS-', r.k, '-', j.n), CONCAT(N'Future guest ', r.k, '-', j.n, N' (fictional)'),
       DATEADD(day, 60 + 25 * j.n + r.k % 5, @bd), DATEADD(day, 62 + 25 * j.n + r.k % 5, @bd), 'Confirmed', r.room_id, r.room_type_id, 'Direct', @now
FROM rooms r CROSS JOIN j;
INSERT inv.room_night(room_id, night, reservation_id)
SELECT r.room_id, DATEADD(day, d.n, r.arrival), r.reservation_id
FROM inv.reservation r CROSS JOIN (VALUES (0), (1)) d(n)
WHERE r.public_no LIKE 'PRS-%';
UPDATE STATISTICS inv.reservation;
UPDATE STATISTICS inv.room_night;
SELECT (SELECT COUNT(*) FROM inv.reservation WHERE status = 'Confirmed') AS confirmed,
  (SELECT COUNT(*) FROM inv.reservation) AS reservations,
  (SELECT COUNT(*) FROM inv.room_night) AS room_nights;
