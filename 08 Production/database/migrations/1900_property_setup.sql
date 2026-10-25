-- HCC migration 1900 · W3-b first-time hotel setup (SetupProperty). Original HCC design.
-- The property row now carries the hotel's legal identity captured once at setup: the state (GST place of supply; default
-- Maharashtra), the GSTIN (optional) and the address. Documents print the Master configuration copies (hotel, brand.address,
-- brand.gstin) that SetupProperty seeds from the same values; these columns are the as-registered record.
-- Existing rows (and test seeds that insert only code/name/time_zone/business_date) get state Maharashtra and NULLs.
ALTER TABLE inv.property ADD
  state_name NVARCHAR(60) NOT NULL CONSTRAINT DF_property_state DEFAULT N'Maharashtra',
  gstin VARCHAR(15) NULL CONSTRAINT CK_property_gstin CHECK (gstin IS NULL OR (LEN(gstin) = 15 AND gstin NOT LIKE '%[^0-9A-Z]%')),
  address NVARCHAR(200) NULL;
