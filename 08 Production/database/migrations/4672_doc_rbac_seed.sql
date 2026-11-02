-- HCC migration 4672 · LF-docdb (contract §1.4, §11): PERM-DOC-006 documents.objects.admin (orphan sweep, move job, documents database
-- health). Not a money permission (DEC-058): Owner and Tech admin.
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-DOC-006', 'documents.objects.admin (orphan sweep, move job, documents database health)', 'ACTION', 0);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-DOC-006', 'A', 'P', NULL, NULL),
  ('TAD', 'PERM-DOC-006', 'A', 'P', NULL, NULL);
