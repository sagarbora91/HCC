-- HCC migration 4234 · ACC-2 vendor payments permission (posting contract §9). PERM-EXP-004 = expenses.vendor.pay: record a vendor payment /
-- advance (RecordVendorPayment) and a goods return / vendor credit note (RecordExpenseReturn). Owner, Manager, Accountant. Voiding a vendor
-- payment and approving a return are the Owner's (PERM-MST-002 in code, as the expense month lock). Reading payments / ageing / the asset
-- register: PERM-EXP-004 or PERM-ACC-001 (accounting view).
-- tests/Hcc.Access.Tests RbacV2MatrixTests pins every cell (row to be added at the ACC-2 merge — ACC2-PURCHASE-DONE.md).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-EXP-004', 'expenses.vendor.pay (vendor payments, advances, returns)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-EXP-004', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-EXP-004', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-EXP-004', 'A', 'P', NULL, NULL);
