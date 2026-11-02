-- HCC migration 4705 · LF B4 permission PERM-RMD-001 = reminders.manage (create, edit, done, dismiss desk reminders): Owner, Manager, Reception, Accountant (LF-CONTRACT §9.2, §11).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-RMD-001', 'reminders.manage (desk reminders: create, edit, done, dismiss)', 'ACTION', 0);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-RMD-001', 'A', 'P', NULL, NULL), ('MGR', 'PERM-RMD-001', 'A', 'P', NULL, NULL),
  ('REC', 'PERM-RMD-001', 'A', 'P', NULL, NULL), ('ACC', 'PERM-RMD-001', 'A', 'P', NULL, NULL);
