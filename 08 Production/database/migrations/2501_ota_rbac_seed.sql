-- HCC migration 2501 · OTA channel permissions (W3-1). The RBAC matrix v1 has no channel rows; the prototype used "reservations"
-- for the inbox / push / stop-sell and "approve" for connection and commercial terms. Module PERM ids and grants live here (one rule for every Wave 2/3 module).
-- Commission figures are money: Reception works the inbox but does not see the commission report.
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-OTA-001', 'channels.operate (inbox accept/reject, pull, push rates, stop-sell)', 'ACTION', 0),
  ('PERM-OTA-002', 'channels.manage (connect/pause, commission, markup, room mapping)', 'ACTION', 1),
  ('PERM-OTA-003', 'channels.commission.view (commission ledger, reconciliation)', 'VIEW', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-OTA-001', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-OTA-001', 'A', 'P', NULL, NULL),
  ('REC', 'PERM-OTA-001', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-OTA-002', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-OTA-002', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-OTA-003', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-OTA-003', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-OTA-003', 'A', 'P', NULL, NULL);
