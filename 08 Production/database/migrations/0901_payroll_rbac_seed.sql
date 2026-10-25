-- HCC migration 0901 · payroll and leave permissions (W2-2). RBAC matrix v1 has no payroll rows; these follow the module 17 spec §2,
-- the prototype's rules (hrCanManage = Owner/Manager, hrCanPay = Owner/Accountant, hrCanRates = Owner), DEC-058 (payroll and
-- statutory settings Owner-only, never self-approved), DEC-061 (every staff member may see their OWN pay and payslip; housekeeping and
-- maintenance never see hotel money) and DEC-066 (F&F additions above Rs 1,00,000 need the Owner = the Accountant's limit on
-- PERM-HR-008). Money permissions go only to roles that are already Owner-assigned (OWN, ACC). Self-service (PERM-HR-004) is scope O:
-- the handler checks the staff record is linked to the caller.
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-HR-001', 'hr.staff.view (master, roster, attendance, leave)', 'VIEW', 0),
  ('PERM-HR-002', 'hr.staff.manage (records, roster, attendance, exit)', 'ACTION', 0),
  ('PERM-HR-003', 'hr.leave.decide', 'ACTION', 0),
  ('PERM-HR-004', 'hr.self (own roster, check-in, leave, payslip)', 'ACTION', 0),
  ('PERM-HR-005', 'payroll.salary.view / edit (pay, bank, PT slab)', 'ACTION', 1),
  ('PERM-HR-006', 'payroll.run.prepare / lock / files / payslips', 'ACTION', 1),
  ('PERM-HR-007', 'payroll.advance.give', 'ACTION', 1),
  ('PERM-HR-008', 'payroll.fnf.prepare / confirm', 'ACTION', 1),
  ('PERM-HR-009', 'payroll.rates / leave policy / unlock / login link', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-HR-001', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-HR-001', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-HR-001', 'A', 'P', NULL, NULL),
  ('TAD', 'PERM-HR-001', 'V', 'P', NULL, NULL),
  ('OWN', 'PERM-HR-002', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-HR-002', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-HR-003', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-HR-003', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-HR-004', 'A', 'O', NULL, NULL),
  ('MGR', 'PERM-HR-004', 'A', 'O', NULL, NULL),
  ('REC', 'PERM-HR-004', 'A', 'O', NULL, NULL),
  ('CSH', 'PERM-HR-004', 'A', 'O', NULL, NULL),
  ('ACC', 'PERM-HR-004', 'A', 'O', NULL, NULL),
  ('SUP', 'PERM-HR-004', 'A', 'O', NULL, NULL),
  ('HKP', 'PERM-HR-004', 'A', 'O', NULL, NULL),
  ('MNT', 'PERM-HR-004', 'A', 'O', NULL, NULL),
  ('TAD', 'PERM-HR-004', 'A', 'O', NULL, NULL),
  ('OWN', 'PERM-HR-005', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-HR-005', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-HR-006', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-HR-006', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-HR-007', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-HR-007', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-HR-008', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-HR-008', 'L', 'P', 10000000, 'PAISE'),
  ('OWN', 'PERM-HR-009', 'A', 'P', NULL, NULL);
