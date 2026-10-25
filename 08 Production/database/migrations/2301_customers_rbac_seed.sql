-- HCC migration 2301 · Customers permissions (W3-5). The RBAC matrix v1 has no merge rows; module PERM ids and grants live here.
-- Viewing/contact reveal/notes/tags/consent reuse PERM-GST-001 (view; Cashier = masked) and PERM-GST-002 (edit; contact reveal), as the Stays module does.
-- Merging duplicates is maker-checker: Owner and Manager may propose and decide, never the same person for the same merge (DEC-066 pattern).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-CRM-001', 'customers.merge.propose', 'ACTION', 0),
  ('PERM-CRM-002', 'customers.merge.decide', 'ACTION', 0);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-CRM-001', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-CRM-001', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-CRM-002', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-CRM-002', 'A', 'P', NULL, NULL);
