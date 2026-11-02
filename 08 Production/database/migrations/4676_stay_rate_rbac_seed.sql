-- HCC migration 4676 · LF A1 permission (LF-CONTRACT §2, §11). PERM-STY-010 stay.rateChange.request: Owner, Manager, Reception request;
-- the Owner and a Manager whose new rate is within the PERM-RATE-001 limit below the tariff (Manager 15 %, 0101 seed) apply it directly.
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-STY-010', 'stay.rateChange.request (rate / guest-count change during a stay)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-STY-010', 'A', 'P', NULL, NULL), ('MGR', 'PERM-STY-010', 'A', 'P', NULL, NULL), ('REC', 'PERM-STY-010', 'R', 'P', NULL, NULL);
