-- HCC migration 2871 · MA-7 seed: the values the prototype fixture already uses, so no figure and no behaviour moves (task types: 3-item
-- checklist for cleans, departure cleans inspected, stayovers not; complaint and found-item categories: today's fixed lists). Zones are
-- not seeded (they need housekeepers; with no zone nothing changes). New installs get the same rows from first-time setup
-- (Infrastructure/OpsMasters/OpsMastersSeed.cs, kept identical to this statement). Idempotent per table and property.
DECLARE @p INT = NULL;
INSERT ops.hk_task_type(property_id, code, name, sort_order, active, applies_to, standard_minutes, needs_inspection, checklist_json, updated_by, updated_utc)
SELECT pr.property_id, v.code, v.name, v.sort_order, 1, v.applies_to, v.minutes, v.needs_inspection, v.checklist, 0, SYSUTCDATETIME()
  FROM inv.property pr CROSS JOIN (VALUES
    ('DEP',  N'Departure clean',  10, 'checkout,move', 40, 1, N'["Linen & bathroom","Surfaces & amenities","Final room check"]'),
    ('STAY', N'Stayover service', 20, 'stayover',      20, 0, N'["Linen & bathroom","Surfaces & amenities","Final room check"]'),
    ('DEEP', N'Deep clean',       30, 'deep',          90, 1, N'["Mattress, linen and curtains","Bathroom descale and grout","Furniture, vents and fittings","Final room check"]'),
    ('INSP', N'Inspection',       40, 'inspection',    10, 0, N'["Bathroom and linen","Surfaces and amenities","Fittings and lights"]')
  ) v(code, name, sort_order, applies_to, minutes, needs_inspection, checklist)
 WHERE (@p IS NULL OR pr.property_id = @p) AND NOT EXISTS (SELECT 1 FROM ops.hk_task_type t WHERE t.property_id = pr.property_id);

INSERT gsv.complaint_category(property_id, code, name, sort_order, active, department, sla_hours, updated_by, updated_utc)
SELECT pr.property_id, v.code, v.name, v.sort_order, 1, v.department, v.sla, 0, SYSUTCDATETIME()
  FROM inv.property pr CROSS JOIN (VALUES
    ('CLEAN',   N'Room cleanliness', 10, 'Housekeeping', 4),
    ('MAINT',   N'Maintenance',      20, 'Maintenance',  8),
    ('NOISE',   N'Noise',            30, 'Front desk',   2),
    ('STAFF',   N'Staff behaviour',  40, 'Front desk',   24),
    ('FNB',     N'Food & beverage',  50, 'Front desk',   8),
    ('BILLING', N'Billing',          60, 'Front desk',   24),
    ('AMEN',    N'Amenities',        70, 'Housekeeping', 8),
    ('OTHER',   N'Other',            80, 'Front desk',   24)
  ) v(code, name, sort_order, department, sla)
 WHERE (@p IS NULL OR pr.property_id = @p) AND NOT EXISTS (SELECT 1 FROM gsv.complaint_category t WHERE t.property_id = pr.property_id);

INSERT gsv.lostfound_category(property_id, code, name, sort_order, active, department, sla_hours, valuable, updated_by, updated_utc)
SELECT pr.property_id, v.code, v.name, v.sort_order, 1, v.department, v.sla, v.valuable, 0, SYSUTCDATETIME()
  FROM inv.property pr CROSS JOIN (VALUES
    ('JEWEL', N'Jewellery',     10, 'Front desk',   4,  1),
    ('ELEC',  N'Electronics',   20, 'Front desk',   4,  1),
    ('CASH',  N'Cash / wallet', 30, 'Front desk',   4,  1),
    ('DOCS',  N'Documents',     40, 'Front desk',   4,  1),
    ('CLOTH', N'Clothing',      50, 'Housekeeping', 24, 0),
    ('CARE',  N'Personal care', 60, 'Housekeeping', 24, 0),
    ('OTHER', N'Other',         70, 'Housekeeping', 48, 0)
  ) v(code, name, sort_order, department, sla, valuable)
 WHERE (@p IS NULL OR pr.property_id = @p) AND NOT EXISTS (SELECT 1 FROM gsv.lostfound_category t WHERE t.property_id = pr.property_id);

-- Records logged before this block keep their name; they get the code of the category with that name.
UPDATE c SET category_code = k.code FROM ops.guest_complaint c JOIN gsv.complaint_category k ON k.property_id = c.property_id AND k.name = c.category WHERE c.category_code IS NULL;
UPDATE i SET category_code = k.code FROM ops.lost_item i JOIN gsv.lostfound_category k ON k.property_id = i.property_id AND k.name = i.category WHERE i.category_code IS NULL;
