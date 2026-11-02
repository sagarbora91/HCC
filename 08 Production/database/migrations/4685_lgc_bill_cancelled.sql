-- HCC migration 4685 · LF B7 legacy cancel flag (LF-CONTRACT §5): lgc.bill.cancelled = the legacy bill was cancelled in the old software, counted
-- in the GSTR-1 Table 13 'LGC' row's cancelled column. The LG extractor sets it from the old Bill table's cancel marker (open point for the LG
-- mapping: until then every imported bill reads 0 = issued). History stays read-only otherwise (DEC-199).
ALTER TABLE lgc.bill ADD cancelled BIT NOT NULL CONSTRAINT DF_lgc_bill_cancelled DEFAULT 0;
