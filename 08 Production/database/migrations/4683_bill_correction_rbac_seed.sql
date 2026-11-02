-- HCC migration 4683 · LF A2 permission (LF-CONTRACT §4, §11): PERM-FOL-010 folio.billCorrection.request — the PERM-FOL-005 holders
-- (Reception, Cashier, Accountant, Manager, Owner; DEC-063 G3). The approval is the Owner's (RBAC_OWNER_DECIDES).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-FOL-010', 'folio.billCorrection.request (correct a bill after check-out)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-FOL-010', 'A', 'P', NULL, NULL), ('MGR', 'PERM-FOL-010', 'R', 'P', NULL, NULL), ('REC', 'PERM-FOL-010', 'R', 'P', NULL, NULL),
  ('CSH', 'PERM-FOL-010', 'R', 'P', NULL, NULL), ('ACC', 'PERM-FOL-010', 'R', 'P', NULL, NULL);
