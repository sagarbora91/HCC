-- HCC migration 2401 · Invoices and credit notes permissions (W3-6). Ids and cells from the role matrix v2 proposal §2.4 (invoices split
-- out of PERM-FOL-004 / FOL-005) with D7 (credit-note approval Owner only, DEC-063 G3). Module PERM ids and grants live here (one rule
-- for every Wave 2/3 module). PERM-INV-006 (e-invoice / GST portal) is not seeded: nothing is built for it yet.
-- Owner holds 003 and issues a credit note directly (no request, v2 note under §2.4); Manager requests (A), Reception/Cashier/Accountant request (R).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-INV-001', 'invoices.view (invoices, credit notes, register, GSTR-1 summary)', 'VIEW', 1),
  ('PERM-INV-002', 'invoices.issue / print / reprint', 'ACTION', 1),
  ('PERM-INV-003', 'invoices.creditNote.approve (Owner only, DEC-063 G3)', 'ACTION', 1),
  ('PERM-INV-005', 'invoices.creditNote.request', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-INV-001', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-INV-001', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-INV-001', 'V', 'P', NULL, NULL),
  ('REC', 'PERM-INV-001', 'A', 'P', NULL, NULL),
  ('CSH', 'PERM-INV-001', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-INV-002', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-INV-002', 'A', 'P', NULL, NULL),
  ('REC', 'PERM-INV-002', 'A', 'P', NULL, NULL),
  ('CSH', 'PERM-INV-002', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-INV-003', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-INV-005', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-INV-005', 'R', 'P', NULL, NULL),
  ('REC', 'PERM-INV-005', 'R', 'P', NULL, NULL),
  ('CSH', 'PERM-INV-005', 'R', 'P', NULL, NULL);
