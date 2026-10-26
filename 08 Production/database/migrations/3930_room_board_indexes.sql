-- HCC migration 3930 · Room board read index (audit A09-18, Wave C4). The board's "next arrival" per room is the earliest Confirmed
-- reservation (by arrival, then id) that holds a night in the room. It used to walk every inv.room_night row the room ever had and
-- look each reservation up (about 440 page reads per room after nine months, growing every day). The board now reads the Confirmed
-- reservations once for the property (SqlHousekeepingReadStore.BoardSql) from this filtered index, which holds only bookings that
-- are still to arrive, so its size follows the future bookings and not the history. Index only; no table or column changes.
CREATE INDEX IX_reservation_confirmed ON inv.reservation(arrival, reservation_id) INCLUDE (departure, guest_name) WHERE status = 'Confirmed';
