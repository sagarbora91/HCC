-- HCC migration 4346 · F6 hall booking read indexes (lane H3): quotations of an enquiry, versions awaiting expiry, events by customer / invoice, receipts by date.
CREATE INDEX IX_hall_quotation_enquiry ON hall.quotation(enquiry_id);
-- F6-int: 4332 (H2) created this index without the INCLUDE columns; widen it in place.
CREATE INDEX IX_hall_quotation_version_status ON hall.quotation_version(status, valid_until) INCLUDE (quotation_id, v) WITH (DROP_EXISTING = ON);
CREATE INDEX IX_hall_event_quotation ON hall.event(quotation_id) INCLUDE (status, event_date);
CREATE INDEX IX_hall_event_receipt_date ON hall.event_receipt(business_date, kind) INCLUDE (event_id, amount_paise);
