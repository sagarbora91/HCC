-- HCC migration 4405 · F7-R1 report permissions (catalogue §1.3). PERM-RPT-001 / 002 / 003 are already seeded by 0101 exactly as §1.3
-- (001 Owner, Manager, Accountant; 002 Owner A, Manager A, Accountant A, Cashier V·S own shift; 003 Owner, Manager, Accountant) — re-asserted
-- here without duplicates. New: PERM-RPT-004 report.schedule (create / change own schedules; Owner, Manager, Accountant). Housekeeping,
-- maintenance and stock roles hold none of these (DEC-061).
IF NOT EXISTS (SELECT 1 FROM plat.permission WHERE permission_id = 'PERM-RPT-001') INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES ('PERM-RPT-001', 'report.operations', 'VIEW', 0);
IF NOT EXISTS (SELECT 1 FROM plat.permission WHERE permission_id = 'PERM-RPT-002') INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES ('PERM-RPT-002', 'report.finance', 'VIEW', 1);
IF NOT EXISTS (SELECT 1 FROM plat.permission WHERE permission_id = 'PERM-RPT-003') INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES ('PERM-RPT-003', 'report.export', 'ACTION', 0);
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES ('PERM-RPT-004', 'report.schedule (scheduled report runs)', 'ACTION', 0);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit)
SELECT v.role_code, v.permission_id, v.mode, v.scope, NULL, NULL
FROM (VALUES ('OWN', 'PERM-RPT-001', 'A', 'P'), ('MGR', 'PERM-RPT-001', 'A', 'P'), ('ACC', 'PERM-RPT-001', 'A', 'P'),
             ('OWN', 'PERM-RPT-002', 'A', 'P'), ('MGR', 'PERM-RPT-002', 'A', 'P'), ('ACC', 'PERM-RPT-002', 'A', 'P'), ('CSH', 'PERM-RPT-002', 'V', 'S'),
             ('OWN', 'PERM-RPT-003', 'A', 'P'), ('MGR', 'PERM-RPT-003', 'A', 'P'), ('ACC', 'PERM-RPT-003', 'A', 'P'),
             ('OWN', 'PERM-RPT-004', 'A', 'P'), ('MGR', 'PERM-RPT-004', 'A', 'P'), ('ACC', 'PERM-RPT-004', 'A', 'P')) v(role_code, permission_id, mode, scope)
WHERE NOT EXISTS (SELECT 1 FROM plat.role_grant g WHERE g.role_code = v.role_code AND g.permission_id = v.permission_id);
