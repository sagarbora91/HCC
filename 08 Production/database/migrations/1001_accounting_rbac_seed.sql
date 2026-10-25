-- HCC migration 1001 · accounting permissions (W2-3). The RBAC matrix v1 has no accounting rows; these follow the prototype
-- (ACC_LOCK: Owner/Manager/Accountant, ACC_UNLOCK: Owner; page needs 'reports' = Owner/Manager/Accountant/Cashier) and DEC-063:
-- G11 ledger mapping Owner only (never the Tech admin), G13 voucher CSV/Excel export refused for the Cashier, Tally tab
-- Owner + Accountant only. Housekeeping, maintenance, reception and tech admin hold none.
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-ACC-001', 'accounting.view (vouchers, trial balance, exceptions)', 'VIEW', 1),
  ('PERM-ACC-002', 'accounting.period.lock', 'ACTION', 1),
  ('PERM-ACC-003', 'accounting.period.unlock', 'ACTION', 1),
  ('PERM-ACC-004', 'accounting.mapping.edit', 'ACTION', 1),
  ('PERM-ACC-005', 'accounting.export (voucher CSV)', 'ACTION', 1),
  ('PERM-ACC-006', 'accounting.tally.export (simulated)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-ACC-001', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-ACC-001', 'A', 'P', NULL, NULL),
  ('CSH', 'PERM-ACC-001', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-ACC-001', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-ACC-002', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-ACC-002', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-ACC-002', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-ACC-003', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-ACC-004', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-ACC-005', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-ACC-005', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-ACC-005', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-ACC-006', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-ACC-006', 'A', 'P', NULL, NULL);
