-- HCC migration 3501 · Company credit limit permission (DEC-076, audit A05-01; Wave B item B5). Setting or changing a company account's
-- credit limit is Owner only; the Manager (and Reception, who holds group.manage) may only REQUEST a change, which the Owner approves or
-- rejects (grp.company_limit_request, never the requester: PERM-SOD-001). Until now the limit was decided under PERM-GRP-003 (seed 2201),
-- where the Manager holds an amount limit of Rs 10,000 and could therefore set a limit up to Rs 10,000 directly. The company limit now has
-- its own permission, so 2201 is left as it is: PERM-GRP-003 keeps attrition, credit terms of a group and master refunds (the role matrix
-- owner's cells). Cells of this migration: PERM-GRP-006 × OWN = A, MGR = R, REC = R; no other role holds it.
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-GRP-006', 'company.credit.limit (set a company credit limit, decide a limit request)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-GRP-006', 'A', 'P', NULL, NULL),
  ('MGR', 'PERM-GRP-006', 'R', 'P', NULL, NULL),
  ('REC', 'PERM-GRP-006', 'R', 'P', NULL, NULL);
