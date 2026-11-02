-- HCC migration 4395 · F4-T3: PERM-FBK-001 record guest feedback (Owner, Manager, Reception, Cashier), 002 follow-up calls + feedback address (Owner), 003 printer test (Owner, Tech admin).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-FBK-001', 'feedback.record (guest feedback at check-out)', 'ACTION', 0),
  ('PERM-FBK-002', 'feedback.followup (Owner follow-up calls, feedback address)', 'ACTION', 0),
  ('PERM-FBK-003', 'printer.test (print a test page, printer status)', 'ACTION', 0);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-FBK-001', 'A', 'P', NULL, NULL), ('MGR', 'PERM-FBK-001', 'A', 'P', NULL, NULL), ('REC', 'PERM-FBK-001', 'A', 'P', NULL, NULL), ('CSH', 'PERM-FBK-001', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-FBK-002', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-FBK-003', 'A', 'P', NULL, NULL), ('TAD', 'PERM-FBK-003', 'A', 'P', NULL, NULL);
