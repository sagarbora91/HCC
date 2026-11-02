-- HCC migration 4383 · F4-T1 tariff card permissions (F4-CONTRACT §1.5): 001 view / history / print = Owner, Manager, Accountant, Reception, Cashier
-- (never housekeeping / maintenance / restaurant, DEC-061); 002 draft + propose = Owner, Manager; 003 publish / reject / weekend days / import =
-- Owner only (DEC-058).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-TRF-001', 'tariffcard.view (tariff card, history, rate sheet print)', 'VIEW', 1),
  ('PERM-TRF-002', 'tariffcard.propose (draft and propose a tariff card)', 'ACTION', 1),
  ('PERM-TRF-003', 'tariffcard.publish (publish / reject a tariff card, weekend nights, rate-card import)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-TRF-001', 'A', 'P', NULL, NULL), ('MGR', 'PERM-TRF-001', 'A', 'P', NULL, NULL), ('ACC', 'PERM-TRF-001', 'A', 'P', NULL, NULL),
  ('REC', 'PERM-TRF-001', 'A', 'P', NULL, NULL), ('CSH', 'PERM-TRF-001', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-TRF-002', 'A', 'P', NULL, NULL), ('MGR', 'PERM-TRF-002', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-TRF-003', 'A', 'P', NULL, NULL);
