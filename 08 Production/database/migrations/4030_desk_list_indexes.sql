-- HCC migration 4030 · Bookings list and cashier folio list indexes (Wave D5, the list-speed leftover of A09-18 / Wave C4).
-- Both reads used to scan every booking the hotel ever had, so they slowed down a little every day:
--   * the bookings list the app loads (GET /reservations?view=range, departure after the business date - 180 days) counted and
--     paged by full clustered scans of inv.reservation. IX_reservation_departure lets SqlReservationStore.ListSql seek the window
--     (count and page) and carries what the page needs to sort and to join the room and room type;
--   * the cashier's folio list (GET /cashier/folios) was one OR over the whole of inv.reservation joined to the whole of gst.stay.
--     SqlBillingStore.DeskFoliosSql now finds its three groups separately: in-house bookings (IX_reservation_inhouse, filtered:
--     only the guests in house), today's confirmed arrivals (IX_reservation_confirmed, 3930) and today's departures
--     (IX_stay_checked_out on the business date of check-out).
-- Index only; no table or column changes.
CREATE INDEX IX_reservation_departure ON inv.reservation(property_id, departure) INCLUDE (arrival, status, hold_until_utc, room_id, room_type_id);
CREATE INDEX IX_reservation_inhouse ON inv.reservation(property_id) WHERE status = 'InHouse';
CREATE INDEX IX_stay_checked_out ON gst.stay(checked_out_date) INCLUDE (reservation_id);
