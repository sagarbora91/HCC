-- HCC migration 4721 · room keys permissions (HCC_KeyAgent_Spec_v1 §5). The Manager holds every key permission (owner ruling: no separate key operator login).
-- 001 keys.read (panel, counts, key log, encoder status) · 002 keys.issue (issue, re-issue, extend, reactivate, lost card, manual-key record)
-- 003 keys.cancel (software cancel at check-out) · 004 keys.configure (lock mapping, register an encoder): Owner and Manager.
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-KEY-001', 'keys.read (key panel, counts, key log, encoder status)', 'VIEW', 0),
  ('PERM-KEY-002', 'keys.issue (issue, re-issue, extend, reactivate, lost card, manual key record)', 'ACTION', 0),
  ('PERM-KEY-003', 'keys.cancel (cancel a room key)', 'ACTION', 0),
  ('PERM-KEY-004', 'keys.configure (room-to-lock mapping, register an encoder)', 'ACTION', 0);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-KEY-001', 'A', 'P', NULL, NULL), ('MGR', 'PERM-KEY-001', 'A', 'P', NULL, NULL), ('REC', 'PERM-KEY-001', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-KEY-002', 'A', 'P', NULL, NULL), ('MGR', 'PERM-KEY-002', 'A', 'P', NULL, NULL), ('REC', 'PERM-KEY-002', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-KEY-003', 'A', 'P', NULL, NULL), ('MGR', 'PERM-KEY-003', 'A', 'P', NULL, NULL), ('REC', 'PERM-KEY-003', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-KEY-004', 'A', 'P', NULL, NULL), ('MGR', 'PERM-KEY-004', 'A', 'P', NULL, NULL);
