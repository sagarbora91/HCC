-- HCC migration 4910 · PER-01 (owner approved the rewrite, Q7): customer list / search / profile reads, set-based and index-backed.
-- CustomerQueries now runs a narrow key pass (SqlCustomerReadStore.HeadsAsync) over every guest and booking of the property, then loads the full
-- rows (room, folio lines) only for the customers on the page or the one profile (LoadSomeAsync, id sets in table variables).
--   * IX_guest_crm_head: the key pass reads guest id, name and mobile in id order without touching the wide guest rows;
--   * IX_reservation_crm_head: the key pass reads booking id, number, name, arrival and status in id order (covering; no sort);
--   * IX_charge_folio: the folio lines of the page's bookings are seeks by folio (bill.charge had no index leading on folio_id
--     except the filtered unique room-night index).
-- Index only; no table or column changes.
CREATE INDEX IX_guest_crm_head ON gst.guest(property_id, guest_id) INCLUDE (full_name, phone);
CREATE INDEX IX_reservation_crm_head ON inv.reservation(property_id, reservation_id) INCLUDE (public_no, guest_name, arrival, status);
CREATE INDEX IX_charge_folio ON bill.charge(folio_id) INCLUDE (kind, amount_paise, tax_bps, reverses_charge_id);
