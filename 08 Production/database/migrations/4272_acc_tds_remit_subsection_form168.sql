-- HCC migration 4272 · ACC-3 acc3-carry C6 / C7 (02 Planning/agents/ACC3-REPORTS-CONTRACT.md §7). C6: a TDS remittance is kept per
-- sub-section — the vendor master's codes rent:plant (old 194-I(a)) and professional:technical (old 194J(a)) beside rent (194-I(b)) and
-- professional (194J(b)). C7: the Form 168 figures the CA keys in (JSON [{companyId, amountPaise}]) are stored on the remittance (the
-- contract names it acc.tds_remittance; the table is acc.tds_remit, migration 4244). The append-only trigger of 4244 leaves form168_json
-- updatable (SetTdsForm168).
ALTER TABLE acc.tds_remit DROP CONSTRAINT CK_acc_tds_remit_section;
ALTER TABLE acc.tds_remit ADD CONSTRAINT CK_acc_tds_remit_section
  CHECK (section IN ('rent','rent:plant','contractor','professional','professional:technical','commission'));
ALTER TABLE acc.tds_remit ADD form168_json NVARCHAR(MAX) NULL CONSTRAINT CK_acc_tds_remit_form168 CHECK (form168_json IS NULL OR ISJSON(form168_json) = 1);
GO
