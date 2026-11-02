-- HCC migration 4370 · F4-T2 rpt.money.revenue-by-category-tender (F4-CONTRACT §2): supporting indexes for the report's three reads.
-- Lines by service date with their folio and kind; stay segments by folio's stay and date. PERM-RPT-002 / PERM-RPT-001 already exist (0101): the report needs both,
-- so it is Owner, Manager and Accountant (the Cashier holds PERM-RPT-002 for the own shift only and has no PERM-RPT-001). Nothing about the data changes.
CREATE INDEX IX_charge_service_date ON bill.charge(service_date) INCLUDE (folio_id, kind, amount_paise, tax_bps, tax_paise);
CREATE INDEX IX_stay_segment_dates ON gst.stay_segment(from_date, to_date) INCLUDE (room_id);
