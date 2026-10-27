-- HCC migration 4181 · DEC-133 (owner, 27 Oct 2026; APR-015 — SCC decisions wave): the hotel in Latur, Maharashtra also needs its FSSAI licence
-- and its lodging house licence renewed. The firm profile gains the optional licence numbers (free text) and expiry dates (the Owner sets them);
-- each expiry date puts its renewal card (Hcc.Domain LicenceCards: FSSAI Licence – Renewal / Lodging House Licence – Renewal, risk High) on the
-- FY page of the expiry month, due on that date. NULL = not set (no card). Existing profiles are untouched.
ALTER TABLE cmp.profile ADD
  fssai_no NVARCHAR(60) NULL,
  fssai_expiry DATE NULL,
  lodging_no NVARCHAR(60) NULL,
  lodging_expiry DATE NULL;
