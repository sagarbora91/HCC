-- HCC migration 4131 · SCC tax port (DEC-116): the W05-C compliance calendar (obligation × period, cmp.filing) is carried onto SCC's cards so
-- nothing recorded is lost (gap report §7 step 2; the same mapping as Hcc.Domain ComplianceRules.LegacyCard and the prototype's cmpFromLegacy):
--   • a property that used the W05-C calendar gets its SCC firm profile — regular dealer, monthly filer, has employees (what W05-C tracked:
--     GSTR-1 / GSTR-3B monthly, EPF, ESIC, PT) — for the Owner to confirm, and the FY of its business date (2026–27 … 2032–33);
--   • the FIRST obligation of each kind maps onto SCC's built-in cards: a filing for period P lands on the card of page month(P + 1) in the FY
--     that holds that page (TDS → ap1 … mr1, GSTR-1 → ap2 … mr5, GSTR-3B → ap3 … mr6, EPF → ap5 … mr8, ESIC → ap6 … mr9, PT → ap7 … mr10);
--     GSTR-9 for FY y → dc5 in FY y + 1;
--   • any further obligation of a kind becomes an SCC custom obligation (its latest due rule — SCC custom edits apply to every month;
--     "last day of the next month" → due day 0 on every page, "day d of the next month" → due day d, annual → the rule's month);
--   • Filed → done (filed on = the IST date it was recorded, evidence ref = the W05-C reference); Not applicable → NA (reason → notes);
--   • a switched-off obligation → its SCC archive keys (EPF / ESIC: both keys, "… Contribution Deposit" and "… Contribution"); a switched-off
--     custom one → its id; reopened filings → the reopened log.
-- The W05-C rows are left in place (history). Only properties with W05-C obligations are touched.

DECLARE @cards TABLE(kind VARCHAR(10) NOT NULL, page_month TINYINT NOT NULL, item_id VARCHAR(8) NOT NULL, PRIMARY KEY (kind, page_month));
INSERT @cards(kind, page_month, item_id) VALUES
  ('TDS', 4, 'ap1'), ('TDS', 5, 'my1'), ('TDS', 6, 'jn1'), ('TDS', 7, 'jl1'), ('TDS', 8, 'ag1'), ('TDS', 9, 'sp1'),
  ('TDS', 10, 'oc1'), ('TDS', 11, 'nv1'), ('TDS', 12, 'dc1'), ('TDS', 1, 'jn_1'), ('TDS', 2, 'fb1'), ('TDS', 3, 'mr1'),
  ('GSTR-1', 4, 'ap2'), ('GSTR-1', 5, 'my3'), ('GSTR-1', 6, 'jn5'), ('GSTR-1', 7, 'jl4'), ('GSTR-1', 8, 'ag3'), ('GSTR-1', 9, 'sp4'),
  ('GSTR-1', 10, 'oc4'), ('GSTR-1', 11, 'nv3'), ('GSTR-1', 12, 'dc3'), ('GSTR-1', 1, 'jn_3'), ('GSTR-1', 2, 'fb4'), ('GSTR-1', 3, 'mr5'),
  ('GSTR-3B', 4, 'ap3'), ('GSTR-3B', 5, 'my4'), ('GSTR-3B', 6, 'jn6'), ('GSTR-3B', 7, 'jl6'), ('GSTR-3B', 8, 'ag4'), ('GSTR-3B', 9, 'sp5'),
  ('GSTR-3B', 10, 'oc5'), ('GSTR-3B', 11, 'nv4'), ('GSTR-3B', 12, 'dc4'), ('GSTR-3B', 1, 'jn_4'), ('GSTR-3B', 2, 'fb5'), ('GSTR-3B', 3, 'mr6'),
  ('EPF', 4, 'ap5'), ('EPF', 5, 'my5'), ('EPF', 6, 'jn8'), ('EPF', 7, 'jl9'), ('EPF', 8, 'ag5'), ('EPF', 9, 'sp6'),
  ('EPF', 10, 'oc9'), ('EPF', 11, 'nv5'), ('EPF', 12, 'dc7'), ('EPF', 1, 'jn_8'), ('EPF', 2, 'fb6'), ('EPF', 3, 'mr8'),
  ('ESIC', 4, 'ap6'), ('ESIC', 5, 'my6'), ('ESIC', 6, 'jn9'), ('ESIC', 7, 'jl10'), ('ESIC', 8, 'ag6'), ('ESIC', 9, 'sp7'),
  ('ESIC', 10, 'oc10'), ('ESIC', 11, 'nv6'), ('ESIC', 12, 'dc8'), ('ESIC', 1, 'jn_9'), ('ESIC', 2, 'fb7'), ('ESIC', 3, 'mr9'),
  ('PT', 4, 'ap7'), ('PT', 5, 'my8'), ('PT', 6, 'jn10'), ('PT', 7, 'jl12'), ('PT', 8, 'ag7'), ('PT', 9, 'sp8'),
  ('PT', 10, 'oc11'), ('PT', 11, 'nv8'), ('PT', 12, 'dc9'), ('PT', 1, 'jn_12'), ('PT', 2, 'fb8'), ('PT', 3, 'mr10');

DECLARE @keys TABLE(kind VARCHAR(10) NOT NULL, archive_key NVARCHAR(200) NOT NULL);
INSERT @keys(kind, archive_key) VALUES
  ('TDS', N'TDS Deposit'), ('GSTR-1', N'GSTR-1'), ('GSTR-3B', N'GSTR-3B'), ('GSTR-9', N'GSTR-9'),
  ('EPF', N'EPF Contribution Deposit'), ('EPF', N'EPF Contribution'), ('ESIC', N'ESIC Contribution Deposit'), ('ESIC', N'ESIC Contribution'), ('PT', N'Professional Tax');

DECLARE @months TABLE(m TINYINT PRIMARY KEY, abbr CHAR(3) NOT NULL, name VARCHAR(9) NOT NULL);
INSERT @months(m, abbr, name) VALUES (1, 'Jan', 'January'), (2, 'Feb', 'February'), (3, 'Mar', 'March'), (4, 'Apr', 'April'), (5, 'May', 'May'),
  (6, 'Jun', 'June'), (7, 'Jul', 'July'), (8, 'Aug', 'August'), (9, 'Sep', 'September'), (10, 'Oct', 'October'), (11, 'Nov', 'November'), (12, 'Dec', 'December');

-- 1) The SCC firm profile and FY of each property that used the W05-C calendar.
INSERT cmp.profile(property_id, firm_name, entity, gst_type, has_employees, qrmp, notes)
SELECT p.property_id, LEFT(p.name, 120), 'proprietorship', 'regular', 1, 0,
  N'Set from the HCC calendar before the SCC port (it tracked GSTR-1 / GSTR-3B monthly, EPF, ESIC and PT): regular dealer, monthly filer, has employees. Owner to confirm.'
FROM inv.property p
WHERE EXISTS (SELECT 1 FROM cmp.obligation o WHERE o.property_id = p.property_id)
  AND NOT EXISTS (SELECT 1 FROM cmp.profile x WHERE x.property_id = p.property_id);

INSERT cmp.setting(property_id, ca_name, ca_email, due_soon_days)
SELECT p.property_id, N'Rao & Associates (fictional)', N'ca.desk@example.test', 7
FROM inv.property p
WHERE EXISTS (SELECT 1 FROM cmp.obligation o WHERE o.property_id = p.property_id)
  AND NOT EXISTS (SELECT 1 FROM cmp.setting s WHERE s.property_id = p.property_id);

UPDATE s SET fy_start = CASE WHEN f.fy < 2026 THEN 2026 WHEN f.fy > 2032 THEN 2032 ELSE f.fy END
FROM cmp.setting s
JOIN (SELECT property_id, CASE WHEN MONTH(business_date) >= 4 THEN YEAR(business_date) ELSE YEAR(business_date) - 1 END AS fy FROM inv.property) f
  ON f.property_id = s.property_id
WHERE EXISTS (SELECT 1 FROM cmp.obligation o WHERE o.property_id = s.property_id);

-- 2) The first obligation of each kind = SCC's built-in cards; any other one = a custom obligation (latest rule).
DECLARE @ob TABLE(obligation_id BIGINT PRIMARY KEY, property_id INT NOT NULL, kind VARCHAR(10) NOT NULL, frequency VARCHAR(7) NOT NULL, active BIT NOT NULL,
  rn INT NOT NULL, rule_type VARCHAR(9) NULL, due_day TINYINT NULL, due_month TINYINT NULL, custom_id BIGINT NULL);
INSERT @ob(obligation_id, property_id, kind, frequency, active, rn, rule_type, due_day, due_month)
SELECT o.obligation_id, o.property_id, o.kind, o.frequency, o.active,
  ROW_NUMBER() OVER (PARTITION BY o.property_id, o.kind ORDER BY o.obligation_id), r.rule_type, r.due_day, r.due_month
FROM cmp.obligation o
OUTER APPLY (SELECT TOP (1) x.rule_type, x.due_day, x.due_month FROM cmp.obligation_rule x WHERE x.obligation_id = o.obligation_id ORDER BY x.effective_from DESC) r;

INSERT cmp.custom_obligation(property_id, cat, name, freq, due_day, months_json, form, what, responsible, legacy, updated_by, updated_utc)
SELECT o.property_id,
  CASE ob.kind WHEN 'TDS' THEN 'it' WHEN 'EPF' THEN 'lab' WHEN 'ESIC' THEN 'lab' WHEN 'PT' THEN 'lab' ELSE 'gst' END,
  o.name,
  CASE WHEN ob.frequency = 'Annual' OR ob.rule_type = 'fixed' THEN 'Annual' ELSE 'Monthly' END,
  CASE WHEN ob.rule_type = 'month-end' THEN 0 ELSE ISNULL(ob.due_day, 0) END,
  CASE WHEN (ob.frequency = 'Annual' OR ob.rule_type = 'fixed') AND mo.name IS NOT NULL THEN N'["' + mo.name + N'"]'
       ELSE N'["April","May","June","July","August","September","October","November","December","January","February","March"]' END,
  o.form, ISNULL(o.note, N''), o.owner_role, 'obligation:' + CAST(o.obligation_id AS VARCHAR(20)), o.updated_by, o.updated_utc
FROM @ob ob
JOIN cmp.obligation o ON o.obligation_id = ob.obligation_id
LEFT JOIN @months mo ON mo.m = ob.due_month
WHERE ob.rn > 1
ORDER BY o.obligation_id;

UPDATE ob SET custom_id = c.custom_id
FROM @ob ob JOIN cmp.custom_obligation c ON c.legacy = 'obligation:' + CAST(ob.obligation_id AS VARCHAR(20));

-- 3) Where every W05-C (obligation, period) lands.
DECLARE @map TABLE(obligation_id BIGINT NOT NULL, period VARCHAR(7) NOT NULL, property_id INT NOT NULL, fy_start SMALLINT NOT NULL, item_id VARCHAR(48) NOT NULL,
  PRIMARY KEY (obligation_id, period));
INSERT @map(obligation_id, period, property_id, fy_start, item_id)
SELECT f.obligation_id, f.period, ob.property_id, c.fy, c.item
FROM (SELECT obligation_id, period FROM cmp.filing UNION SELECT obligation_id, period FROM cmp.filing_reopened) f
JOIN @ob ob ON ob.obligation_id = f.obligation_id
CROSS APPLY (SELECT TRY_CONVERT(DATE, f.period + '-01', 23) AS p0, TRY_CONVERT(INT, LEFT(f.period, 4)) AS y) a
CROSS APPLY (SELECT DATEADD(MONTH, 1, a.p0) AS page) b
CROSS APPLY (SELECT
  CASE WHEN ob.rn = 1 AND ob.kind = 'GSTR-9' THEN a.y + 1
       WHEN ob.rn > 1 AND (ob.frequency = 'Annual' OR ob.rule_type = 'fixed') THEN a.y + 1
       WHEN b.page IS NULL THEN NULL
       WHEN MONTH(b.page) >= 4 THEN YEAR(b.page) ELSE YEAR(b.page) - 1 END AS fy,
  CASE WHEN ob.rn = 1 AND ob.kind = 'GSTR-9' THEN 'dc5'
       WHEN ob.rn = 1 THEN (SELECT k.item_id FROM @cards k WHERE k.kind = ob.kind AND k.page_month = MONTH(b.page))
       WHEN ob.frequency = 'Annual' OR ob.rule_type = 'fixed' THEN 'cust-' + CAST(ob.custom_id AS VARCHAR(20)) + '-' + (SELECT m.abbr FROM @months m WHERE m.m = ob.due_month)
       ELSE 'cust-' + CAST(ob.custom_id AS VARCHAR(20)) + '-' + (SELECT m.abbr FROM @months m WHERE m.m = MONTH(b.page)) END AS item) c
WHERE c.fy IS NOT NULL AND c.item IS NOT NULL;

-- 4) Filings → SCC records (Filed → done; Not applicable → NA, the reason as notes).
INSERT cmp.record(property_id, fy_start, item_id, done, na, filed_on, notes, evidence_ref, reviewer_note, done_by, done_utc, na_by, na_utc, legacy)
SELECT m.property_id, m.fy_start, m.item_id,
  CASE WHEN f.status = 'Filed' THEN 1 ELSE 0 END, CASE WHEN f.status = 'Filed' THEN 0 ELSE 1 END,
  CASE WHEN f.status = 'Filed' THEN CAST(DATEADD(MINUTE, 330, f.filed_utc) AS DATE) END,   -- the IST date (UTC + 5:30)
  CASE WHEN f.status = 'Filed' THEN N'' ELSE f.reason END,
  CASE WHEN f.status = 'Filed' THEN f.reference END,
  CASE WHEN f.status = 'Filed' THEN N'' END,
  CASE WHEN f.status = 'Filed' THEN f.filed_by END, CASE WHEN f.status = 'Filed' THEN f.filed_utc END,
  CASE WHEN f.status = 'Filed' THEN NULL ELSE f.filed_by END, CASE WHEN f.status = 'Filed' THEN NULL ELSE f.filed_utc END,
  CAST(f.obligation_id AS VARCHAR(20)) + ':' + f.period
FROM cmp.filing f
JOIN @map m ON m.obligation_id = f.obligation_id AND m.period = f.period
WHERE NOT EXISTS (SELECT 1 FROM cmp.record r WHERE r.property_id = m.property_id AND r.fy_start = m.fy_start AND r.item_id = m.item_id);

-- 5) Switched-off obligations → archived keys.
INSERT cmp.archive(property_id, archive_key, archived_by, archived_utc)
SELECT ob.property_id, k.archive_key, o.updated_by, o.updated_utc
FROM @ob ob JOIN cmp.obligation o ON o.obligation_id = ob.obligation_id JOIN @keys k ON k.kind = ob.kind
WHERE ob.rn = 1 AND ob.active = 0
UNION ALL
SELECT ob.property_id, N'cust-' + CAST(ob.custom_id AS NVARCHAR(20)), o.updated_by, o.updated_utc
FROM @ob ob JOIN cmp.obligation o ON o.obligation_id = ob.obligation_id
WHERE ob.rn > 1 AND ob.active = 0 AND ob.custom_id IS NOT NULL;

-- 6) Reopened filings → the reopened log (oldest first, so the newest keeps the highest id).
INSERT cmp.record_reopened(property_id, fy_start, item_id, part, was_done, was_na, filed_on, notes, evidence_ref, reviewer_note, recorded_by, recorded_utc,
  reopened_by, reopened_utc, reopen_reason)
SELECT m.property_id, m.fy_start, m.item_id, CASE WHEN r.status = 'Filed' THEN 'done' ELSE 'na' END,
  CASE WHEN r.status = 'Filed' THEN 1 ELSE 0 END, CASE WHEN r.status = 'Filed' THEN 0 ELSE 1 END,
  CASE WHEN r.status = 'Filed' THEN CAST(DATEADD(MINUTE, 330, r.filed_utc) AS DATE) END,
  CASE WHEN r.status = 'Filed' THEN N'' ELSE r.reason END, r.reference, CASE WHEN r.status = 'Filed' THEN N'' END,
  r.filed_by, r.filed_utc, r.reopened_by, r.reopened_utc, r.reopen_reason
FROM cmp.filing_reopened r
JOIN @map m ON m.obligation_id = r.obligation_id AND m.period = r.period
ORDER BY r.reopen_id;
