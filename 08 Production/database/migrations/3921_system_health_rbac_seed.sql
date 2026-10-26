-- HCC migration 3921 · System health permission (Wave C3: audit A09-05 and the fix wave A leftovers on parked outbox events).
-- PERM-SYS-001 = read Admin & Setup › System health (parked outbox events: consumer, event type, attempts, failure times and the error
-- TYPE — never the payload or the error text, F-L07; the last backup and restore-drill results) and run RetryParkedEvent (release a parked
-- event for one more delivery attempt; audited OUTBOX_PARKED_RETRY). Owner and Tech admin only: the Administration area (DEC-063 G9).
-- Not money: the panel shows no amounts. tests/Hcc.Access.Tests RbacV2MatrixTests pins every cell (LaterPermissions).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-SYS-001', 'system.health (view parked events and backup results, retry a parked event)', 'ACTION', 0);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-SYS-001', 'A', 'P', NULL, NULL),
  ('TAD', 'PERM-SYS-001', 'A', 'P', NULL, NULL);
