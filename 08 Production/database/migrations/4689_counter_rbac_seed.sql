-- HCC migration 4689 · LF A7 permissions (LF-CONTRACT §6, §11): PERM-CTR-001 counter.bill.issue and PERM-CTR-002 counter.bill.void.request —
-- Reception, Cashier, Manager, Owner (the Restaurant role bills through POS). The void's credit note is approved by the Owner (DEC-063 G3).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-CTR-001', 'counter.bill.issue (counter / direct bill)', 'ACTION', 1),
  ('PERM-CTR-002', 'counter.bill.void.request (credit note for a counter bill)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-CTR-001', 'A', 'P', NULL, NULL), ('MGR', 'PERM-CTR-001', 'A', 'P', NULL, NULL), ('REC', 'PERM-CTR-001', 'A', 'P', NULL, NULL), ('CSH', 'PERM-CTR-001', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-CTR-002', 'A', 'P', NULL, NULL), ('MGR', 'PERM-CTR-002', 'R', 'P', NULL, NULL), ('REC', 'PERM-CTR-002', 'R', 'P', NULL, NULL), ('CSH', 'PERM-CTR-002', 'R', 'P', NULL, NULL);
