-- HCC migration 4248 · ACC-2 acc2-gst · the firm profile's "GST on advances" switch (02 Planning/agents/ACC2-POSTING-CONTRACT.md §4;
-- rulebook §4.6, A6 / A6a / A6b; DEC-142: the Owner ruled NO GST on booking advances (against advice, CA to confirm), so the switch is OFF by
-- default and the lawful entry can be turned on without a rebuild). On: a folio deposit received also books Dr 1350 GST paid on advances ·
-- Cr 2110 · Cr 2120 (cum-tax at the booked room's rate); the deposit applied or returned books the reversal. Owner only (PERM-CFG-003, money
-- settings, DEC-058); SetGstOnAdvances writes these columns only (SaveComplianceProfile leaves them).
ALTER TABLE cmp.profile ADD gst_on_advances BIT NOT NULL CONSTRAINT DF_cmp_profile_gst_on_advances DEFAULT 0,
  gst_on_advances_by INT NULL,
  gst_on_advances_utc DATETIME2(3) NULL;
