-- HCC migration 1101 · compliance permissions (W2-5). The RBAC matrix v1 has no compliance row; module PERM ids and grants live in
-- the module's *_rbac_seed.sql migration (one rule for every Wave 2 module).
-- ── Permissions (DEC-055: Owner and Accountant only; managing = Owner) ──
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-CMP-001', 'compliance.use (view, mark filed / not applicable, CA pack)', 'ACTION', 0),
  ('PERM-CMP-002', 'compliance.manage (reopen, obligations, settings)', 'ACTION', 0);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-CMP-001', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-CMP-001', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-CMP-002', 'A', 'P', NULL, NULL);
