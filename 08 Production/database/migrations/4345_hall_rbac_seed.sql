-- HCC migration 4345 · F6 hall booking permissions (F6-CONTRACT §4, spec §16; lane H3). Housekeeping, maintenance, Tech admin and the Restaurant role hold none:
-- they read the calendar and the function sheet through the money-free endpoints only (DEC-061).
-- 001 enquiries · 002 quotations create / revise / send (Accountant views) · 003 accept + advances (Cashier: advances only, the command refuses the accept)
-- 004 event bill, cancel, refund (Reception cancels as a request; Cashier settles) · 005 T&C templates + placeholder override (Owner) · 006 halls master, menus.
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-HAL-001', 'hall.enquiry (add, follow up, mark lost, soft hold)', 'ACTION', 0),
  ('PERM-HAL-002', 'hall.quotation (create, revise, send; view)', 'ACTION', 1),
  ('PERM-HAL-003', 'hall.accept (accept a quotation, take a hall advance)', 'ACTION', 1),
  ('PERM-HAL-004', 'hall.event.bill (event-day bill, cancel, refund)', 'ACTION', 1),
  ('PERM-HAL-005', 'hall.tnc (terms and conditions templates, placeholder override)', 'ACTION', 0),
  ('PERM-HAL-006', 'hall.master (halls master, menu packages)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-HAL-001', 'A', 'P', NULL, NULL), ('MGR', 'PERM-HAL-001', 'A', 'P', NULL, NULL), ('REC', 'PERM-HAL-001', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-HAL-002', 'A', 'P', NULL, NULL), ('MGR', 'PERM-HAL-002', 'A', 'P', NULL, NULL), ('REC', 'PERM-HAL-002', 'A', 'P', NULL, NULL), ('ACC', 'PERM-HAL-002', 'V', 'P', NULL, NULL),
  ('OWN', 'PERM-HAL-003', 'A', 'P', NULL, NULL), ('MGR', 'PERM-HAL-003', 'A', 'P', NULL, NULL), ('REC', 'PERM-HAL-003', 'A', 'P', NULL, NULL), ('CSH', 'PERM-HAL-003', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-HAL-004', 'A', 'P', NULL, NULL), ('MGR', 'PERM-HAL-004', 'A', 'P', NULL, NULL), ('REC', 'PERM-HAL-004', 'A', 'P', NULL, NULL), ('CSH', 'PERM-HAL-004', 'A', 'P', NULL, NULL), ('ACC', 'PERM-HAL-004', 'V', 'P', NULL, NULL),
  ('OWN', 'PERM-HAL-005', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-HAL-006', 'A', 'P', NULL, NULL), ('MGR', 'PERM-HAL-006', 'A', 'P', NULL, NULL);
