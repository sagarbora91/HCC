-- HCC migration 4202 · ACC-1 chart of accounts permission (posting contract §7; rulebook §6 "Chart of accounts, mapping: Owner").
-- PERM-ACC-007 = accounting.chart.edit: add / edit / retire ledgers and add / rename / re-parent custom sub-groups (UpsertLedger,
-- RetireLedger, UpsertLedgerGroup). A money master (DEC-058): Owner only. Reading the chart stays PERM-ACC-001.
-- tests/Hcc.Access.Tests RbacV2MatrixTests pins every cell (LaterPermissions — row added at the ACC-1 merge).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-ACC-007', 'accounting.chart.edit (ledgers, custom sub-groups)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-ACC-007', 'A', 'P', NULL, NULL);
