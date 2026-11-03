-- HCC migration 4293 · ACC-4 bank permissions (ACC4-CONTRACT §4). PERM-ACC-016 = bank import / match / settlement post: Owner, Accountant.
-- PERM-ACC-017 = bank statement layout master: Owner. Reports read with PERM-ACC-001 (DEC-203); the Manager reads the clearing view only.
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-ACC-016', 'accounting.bank.import / match / settlement post', 'ACTION', 1),
  ('PERM-ACC-017', 'accounting.bank.layout master', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-ACC-016', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-ACC-016', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-ACC-017', 'A', 'P', NULL, NULL);
