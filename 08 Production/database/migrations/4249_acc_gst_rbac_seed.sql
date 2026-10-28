-- HCC migration 4249 · ACC-2 acc2-gst permissions (02 Planning/agents/ACC2-POSTING-CONTRACT.md §9; rulebook §6; DEC-058, DEC-131).
-- PERM-ACC-012 = accounting.month.lock / unlock: Owner ('A'); the Accountant holds it request-only ('R') and asks with RequestAccountingMonthLock.
-- PERM-ACC-013 = accounting.tax (GST set-off, GST / TDS / statutory remittance entries, TDS report): Owner, Accountant. acc2-tds seeds it in its
-- own block; the inserts below are guarded so whichever migration runs first creates it and the other adds nothing.
-- Money permissions: the Cashier, housekeeping, maintenance, restaurant and Tech admin roles hold neither (DEC-061, DEC-063 G11).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-ACC-012', 'accounting.month.lock / unlock (Accountant: request)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-ACC-012', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-ACC-012', 'R', 'P', NULL, NULL);
IF NOT EXISTS (SELECT 1 FROM plat.permission WHERE permission_id = 'PERM-ACC-013')
  INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
    ('PERM-ACC-013', 'accounting.tax (GST set-off, GST / TDS / statutory remittances, TDS report)', 'ACTION', 1);
IF NOT EXISTS (SELECT 1 FROM plat.role_grant WHERE role_code = 'OWN' AND permission_id = 'PERM-ACC-013')
  INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES ('OWN', 'PERM-ACC-013', 'A', 'P', NULL, NULL);
IF NOT EXISTS (SELECT 1 FROM plat.role_grant WHERE role_code = 'ACC' AND permission_id = 'PERM-ACC-013')
  INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES ('ACC', 'PERM-ACC-013', 'A', 'P', NULL, NULL);
