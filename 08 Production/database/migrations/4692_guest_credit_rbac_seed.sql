-- HCC migration 4692 · LF B2 permission (LF-CONTRACT §7, §11): PERM-GST-011 guest.creditAccount — Owner only (money field, DEC-058).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-GST-011', 'guest.creditAccount (open / change a guest''s credit account)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-GST-011', 'A', 'P', NULL, NULL);
