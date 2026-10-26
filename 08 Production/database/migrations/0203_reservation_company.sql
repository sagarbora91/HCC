-- HCC migration 0203 · W4-G5 company on the reservation (Inventory block 0200-0299). Original HCC design; never edits 0002/0200.
-- A booking made for a company carries the company name and (optionally) its GSTIN, so company credit at checkout and the B2B tax
-- invoice work in production without parsing the free-text notes. Both columns are optional; existing rows stay NULL.
ALTER TABLE inv.reservation ADD
  company_name NVARCHAR(200) NULL,
  company_gstin VARCHAR(15) NULL;
GO
