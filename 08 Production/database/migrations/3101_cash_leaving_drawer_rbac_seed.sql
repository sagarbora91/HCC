-- HCC migration 3101 · permissions for cash leaving the drawer (Wave B1; DEC-073). Module PERM ids outside the RBAC matrix v1 (like
-- 2101 documents). Own-shift grants (scope S) are checked against the shift: own = the person who opened it (DEC-078,
-- SqlShiftScopeResolver). Bank deposit: Cashier or Manager records it from their own open shift; a DIFFERENT Manager or Owner confirms
-- it (the "different" part is enforced in code and by CK_approval_sod_decide / CK_cash_transfer_sod). Slip photo: Owner, Manager,
-- Accountant (read-only). Till transfer: whoever holds a cash shift gives from and accepts on their own open shift. Owner drawing: Owner.
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-DRW-001', 'cash.banking.record (bank deposit from your own open shift: slip number + slip photo)', 'ACTION', 1),
  ('PERM-DRW-002', 'cash.banking.confirm / reject (not your own; also returns a till transfer)', 'ACTION', 1),
  ('PERM-DRW-003', 'cash.banking.slip.view (deposit-slip photo, read-only)', 'VIEW', 1),
  ('PERM-DRW-004', 'cash.till.transfer / accept (your own open shift)', 'ACTION', 1),
  ('PERM-DRW-005', 'cash.owner.drawing (Owner only)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('MGR', 'PERM-DRW-001', 'A', 'S', NULL, NULL),
  ('CSH', 'PERM-DRW-001', 'A', 'S', NULL, NULL),
  ('OWN', 'PERM-DRW-002', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-DRW-002', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-DRW-003', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-DRW-003', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-DRW-003', 'V', 'P', NULL, NULL),
  ('OWN', 'PERM-DRW-004', 'A', 'S', NULL, NULL),
  ('MGR', 'PERM-DRW-004', 'A', 'S', NULL, NULL),
  ('REC', 'PERM-DRW-004', 'A', 'S', NULL, NULL),
  ('CSH', 'PERM-DRW-004', 'A', 'S', NULL, NULL),
  ('OWN', 'PERM-DRW-005', 'A', 'P', NULL, NULL);
