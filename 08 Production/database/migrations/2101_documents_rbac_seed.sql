-- HCC migration 2101 · ID document capture permissions (W3-3). The RBAC matrix v1 has no document-capture row; module PERM ids and
-- grants live in the module's *_rbac_seed.sql migration. Viewing the stored image or the extracted number needs the existing
-- ID-view permission PERM-GST-003 (OWN, MGR, REC) and is audited; confirming a capture also needs it (it writes the full number).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-DOC-001', 'documents.capture (capture an ID image, list masked captures, confirm / reject after review)', 'ACTION', 0),
  ('PERM-DOC-002', 'documents.purge (run the ID image retention purge)', 'ACTION', 0);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-DOC-001', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-DOC-001', 'A', 'P', NULL, NULL),
  ('REC', 'PERM-DOC-001', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-DOC-002', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-DOC-002', 'A', 'P', NULL, NULL);
