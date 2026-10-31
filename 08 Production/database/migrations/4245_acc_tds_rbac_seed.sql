-- HCC migration 4245 · ACC-2 tax permission (02 Planning/agents/ACC2-POSTING-CONTRACT.md §9; rulebook §6; DEC-146, DEC-058; lane acc2-tds).
-- PERM-ACC-013 = accounting.tax: GST set-off, GST / TDS / statutory remittance entries, the TDS deductee report and the Form 168
-- reconciliation — Owner and Accountant. The TDS table and the firm's TDS switches stay Owner-only money configuration (PERM-CFG-003).
-- Money permission: the Cashier, Reception, housekeeping, maintenance, restaurant and Tech admin roles do not hold it (DEC-061, DEC-063 G11).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-ACC-013', 'accounting.tax (GST set-off, GST / TDS / statutory remittances, TDS report)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-ACC-013', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-ACC-013', 'A', 'P', NULL, NULL);
