-- HCC migration 2201 · Groups billing permissions (W3-4). The RBAC matrix v1 has no group rows; the ids and holders follow the
-- role matrix v2 proposal (02 Planning/HCC_RBAC_Matrix_v2_Proposal.md, "Groups billing") so the owner's ruling only has to confirm them.
-- GRP-003 (attrition, credit terms, master refunds): Manager limited to Rs 10,000 per decision (the same limit as PERM-PAY-005
-- company credit); above it, and Reception always, request only. GRP-005 own-shift scope applies to cash, which the master folio
-- does not take (non-cash tenders only), so it is checked at any scope.
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-GRP-001', 'group.view (groups, rooming list, routing)', 'VIEW', 0),
  ('PERM-GRP-002', 'group.manage (create, routing, pickup billing, transfer, settle)', 'ACTION', 1),
  ('PERM-GRP-003', 'group.approve (attrition, credit terms, master refund)', 'ACTION', 1),
  ('PERM-GRP-004', 'group.money.view (master folio, statement, deposits)', 'VIEW', 1),
  ('PERM-GRP-005', 'group.payment.record (deposit / payment on the master folio)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-GRP-001', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-GRP-001', 'A', 'P', NULL, NULL),
  ('REC', 'PERM-GRP-001', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-GRP-001', 'V', 'P', NULL, NULL),
  ('CSH', 'PERM-GRP-001', 'V', 'P', NULL, NULL),
  ('OWN', 'PERM-GRP-002', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-GRP-002', 'A', 'P', NULL, NULL),
  ('REC', 'PERM-GRP-002', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-GRP-003', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-GRP-003', 'L', 'P', 1000000, 'PAISE'),
  ('REC', 'PERM-GRP-003', 'R', 'P', NULL, NULL),
  ('OWN', 'PERM-GRP-004', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-GRP-004', 'A', 'P', NULL, NULL),
  ('REC', 'PERM-GRP-004', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-GRP-004', 'V', 'P', NULL, NULL),
  ('CSH', 'PERM-GRP-004', 'V', 'P', NULL, NULL),
  ('OWN', 'PERM-GRP-005', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-GRP-005', 'A', 'P', NULL, NULL),
  ('REC', 'PERM-GRP-005', 'A', 'S', NULL, NULL),
  ('CSH', 'PERM-GRP-005', 'A', 'S', NULL, NULL);
