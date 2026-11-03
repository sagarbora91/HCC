-- HCC migration 4284 · ACC-4 year end permissions (ACC4-CONTRACT §4). PERM-ACC-014 = year preview / carry-forward draft: Owner, Accountant.
-- PERM-ACC-015 = year lock / reopen / relock: Owner only (money, DEC-058). Other roles hold neither (DEC-061).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-ACC-014', 'accounting.year.preview / carry-forward draft', 'ACTION', 1),
  ('PERM-ACC-015', 'accounting.year.lock / reopen / relock', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-ACC-014', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-ACC-014', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-ACC-015', 'A', 'P', NULL, NULL);
