-- HCC migration 4241 · ACC-2 manual voucher permissions (02 Planning/agents/ACC2-POSTING-CONTRACT.md §9; rulebook §6; DEC-058, DEC-063).
-- PERM-ACC-010 = enter a manual voucher: Owner (approved at once) and Accountant (pending). The Manager holds no manual vouchers (contract §9).
-- PERM-ACC-011 = approve / reject and void a manual voucher: Owner only (money, maker-checker).
-- Money permissions: the Cashier, housekeeping, maintenance, restaurant and Tech admin roles hold neither (DEC-061, DEC-063 G11).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-ACC-010', 'accounting.manual.enter', 'ACTION', 1),
  ('PERM-ACC-011', 'accounting.manual.approve (and void)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-ACC-010', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-ACC-010', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-ACC-011', 'A', 'P', NULL, NULL);
