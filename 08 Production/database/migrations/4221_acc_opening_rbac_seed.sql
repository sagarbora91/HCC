-- HCC migration 4221 · ACC-1 opening balances permissions (02 Planning/agents/ACC1-POSTING-CONTRACT.md §7; rulebook §6; DEC-148, DEC-058).
-- PERM-ACC-008 = enter / save the opening balances (draft): Owner, Manager, Accountant (DEC-148: the Manager enters them).
-- PERM-ACC-009 = approve the opening balances and reopen an approved entry: Owner only (money, DEC-058).
-- Money permissions: the Cashier, housekeeping, maintenance, restaurant and Tech admin roles hold neither (DEC-061, DEC-063 G11).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-ACC-008', 'accounting.opening.enter', 'ACTION', 1),
  ('PERM-ACC-009', 'accounting.opening.approve (and reopen)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-ACC-008', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-ACC-008', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-ACC-008', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-ACC-009', 'A', 'P', NULL, NULL);
