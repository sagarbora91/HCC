-- HCC migration 4601 · W1a LG legacy importer permission (02 Planning/agents/LG-HISTORY-CONTRACT.md §3, §10; DEC-161…164).
-- PERM-LGC-001 = run the legacy importer (legacy.import) and list its runs (GET /legacy/runs): Owner only. Money permission: the
-- importer writes opening-balance drafts for receivables, customer openings and advances (DEC-148, DEC-058). Reading legacy history
-- in Customers 360 uses the existing CRM permissions and masking (PERM-CRM-001 / 002 / 004).
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-LGC-001', 'legacy.import (and list runs)', 'ACTION', 1);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-LGC-001', 'A', 'P', NULL, NULL);
