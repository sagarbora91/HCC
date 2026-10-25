-- HCC migration 2001 · web booking permissions (W3-2). Module PERM ids and grants live in the module's own rbac seed (one rule for
-- every Wave 2/3 module). Review = see the web-request queue, confirm (creates the reservation), decline, make a deposit link.
-- Recording the simulated payment additionally needs the existing deposit permission (PERM-PAY-002), which posts the receipt.
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-WEB-001', 'webbooking.review (web request queue, confirm / decline, deposit link)', 'ACTION', 0);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-WEB-001', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-WEB-001', 'A', 'P', NULL, NULL),
  ('REC', 'PERM-WEB-001', 'A', 'P', NULL, NULL);
