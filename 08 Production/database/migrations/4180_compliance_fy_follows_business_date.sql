-- HCC migration 4180 · DEC-125 (owner, 27 Oct 2026 — SCC decisions wave): the compliance FY follows the business date on 1 April.
-- SCC kept the selected FY until someone changed it. Now cmp.setting also stores, with the pick, the FY the business date was in when it was
-- made (fy_chosen_in). The calendar shows the pick while the business date is still in that FY; from the next 1 April — and with no pick
-- (NULL) — it shows the business date's FY, clamped to SCC's list 2026–27 … 2032–33 (Hcc.Domain SccTaxRules.EffectiveFy; prototype
-- cmpEffectiveFy). Never the device clock.
ALTER TABLE cmp.setting ADD fy_chosen_in SMALLINT NULL
  CONSTRAINT CK_cmp_setting_fy_chosen CHECK (fy_chosen_in IS NULL OR fy_chosen_in BETWEEN 2000 AND 2100);
GO
-- Rows from before DEC-125: a pick recorded in the audit trail (SetComplianceFy → COMPLIANCE_FY) counts as made in the current business-date
-- FY, so it holds until the next 1 April — no jump today. Without a recorded pick the row stays NULL and the calendar follows the business date
-- (fy_start then still equals it: the default 2026 or migration 4131's FY of the business date). Same rule as the prototype's restore
-- (cmpStampFyChoice).
UPDATE s SET fy_chosen_in = CASE WHEN MONTH(p.business_date) >= 4 THEN YEAR(p.business_date) ELSE YEAR(p.business_date) - 1 END
FROM cmp.setting s
JOIN inv.property p ON p.property_id = s.property_id
WHERE s.fy_chosen_in IS NULL
  AND EXISTS (SELECT 1 FROM plat.audit_event a WHERE a.property_id = s.property_id AND a.action = 'COMPLIANCE_FY');
