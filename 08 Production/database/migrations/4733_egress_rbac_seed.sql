-- HCC migration 4733 · F5-E1 internet permissions (EGRESS-CONTRACT §1.1, owner default A28: Owner only).
-- PERM-NET-001 net.change = change the Internet switches (Owner); the change itself goes through Master configuration (group "internet" is a
-- money-flagged group, so only the Owner may change it, reason required). PERM-NET-002 net.read = System health › Egress block and the
-- Internet group read (Owner, Manager). Not money: no amounts.
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-NET-001', 'net.change (internet master switch, per-integration switches, window)', 'ACTION', 0),
  ('PERM-NET-002', 'net.read (internet switches, egress state, held counts, firewall rule state)', 'VIEW', 0);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-NET-001', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-NET-002', 'A', 'P', NULL, NULL), ('MGR', 'PERM-NET-002', 'A', 'P', NULL, NULL);
