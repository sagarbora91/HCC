-- HCC migration 4684 · LF B7 GSTR-1 Table 13 legacy series (LF-CONTRACT §5): lgc.bill is read by (property_id, bill_date) for a period.
-- The LG migration 4600 indexed (property_id, customer_key, bill_date) only, so the date index is added here.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_lgc_bill_date' AND object_id = OBJECT_ID('lgc.bill'))
  CREATE INDEX IX_lgc_bill_date ON lgc.bill(property_id, bill_date) INCLUDE (legacy_bill_no);
