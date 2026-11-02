-- HCC migration 4678 · LF B1 permission (LF-CONTRACT §3, §11): PERM-GST-010 guest.agreedRate set / clear — Owner only (DEC-058).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-GST-010', 'guest.agreedRate (set / clear a guest''s agreed rate)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-GST-010', 'A', 'P', NULL, NULL);
