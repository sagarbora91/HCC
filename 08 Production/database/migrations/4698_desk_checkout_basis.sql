-- HCC migration 4698 · LF A6 check-out basis (LF-CONTRACT §8.4). 'fixed' = the check-out time on the departure date (HCC today), '24h' = 24 hours per night from the
-- check-in time. The Owner settings (Master configuration › Front desk) frontdesk.defaultCheckoutBasis, lateCheckoutGraceHours, earlyCheckinGraceHours and
-- frontdesk.halfDayPct (money, Owner only) are registered by the stays SettingsProvider and stored in the settings tables — no table here.
-- A night stays the inventory unit: a 24h stay holds no room for its departure date (no change to inv.room_night). gst.stay.checked_in_utc (0300) is the check-in time.
ALTER TABLE inv.reservation ADD checkout_basis CHAR(5) NOT NULL CONSTRAINT DF_reservation_checkout_basis DEFAULT 'fixed' CONSTRAINT CK_reservation_checkout_basis CHECK (checkout_basis IN ('fixed','24h'));
