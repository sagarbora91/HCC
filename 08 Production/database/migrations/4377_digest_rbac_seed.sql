-- HCC migration 4377 · F4-T2 PERM-DGS-001 = digest.view (the owner digest and its held drafts): Owner only (F4-CONTRACT §3).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-DGS-001', 'digest.view (owner digest and held drafts)', 'VIEW', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-DGS-001', 'A', 'P', NULL, NULL);
