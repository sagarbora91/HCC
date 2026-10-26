-- HCC migration 2701 · Import permissions (W4-13). The RBAC matrix v1 has no import rows; module PERM ids and grants live here.
-- DEC-057/058: the Tech admin may upload, validate, preview and dry-run every master but commits nothing; the Owner commits.
-- PERM-IMP-002 = commit the non-money masters (rooms, guest profiles) — Owner today; the Owner may later grant it to the Tech admin (DEC-058).
-- PERM-IMP-003 = commit the Owner-only masters (room types with rack rates, tariffs, seasons, staff roles, ledgers, opening balances, till cash).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-IMP-001', 'import.upload / validate / preview / dry-run', 'ACTION', 0),
  ('PERM-IMP-002', 'import.commit (rooms, guest profiles)', 'ACTION', 0),
  ('PERM-IMP-003', 'import.commit (money, rates, roles, accounts, opening balances)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-IMP-001', 'A', 'P', NULL, NULL),
  ('TAD', 'PERM-IMP-001', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-IMP-002', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-IMP-003', 'A', 'P', NULL, NULL);
