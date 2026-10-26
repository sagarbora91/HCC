-- HCC migration 2802 · Masters permissions (MA-0). The RBAC matrix v1 has no master rows; module PERM ids and grants live here.
-- DEC-MA-1 (owner, 26 Oct 2026): operational lists (floors, …) are changed by the Owner and the Tech admin — NOT the Manager by
-- default (the Owner may grant it later in Access control). Money lists and money fields stay Owner-only (DEC-058).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-MST-001', 'masters.manage (operational lists)', 'ACTION', 0),
  ('PERM-MST-002', 'masters.manage-money (money lists and money fields)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-MST-001', 'A', 'P', NULL, NULL),
  ('TAD', 'PERM-MST-001', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-MST-002', 'A', 'P', NULL, NULL);
