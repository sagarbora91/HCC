-- HCC migration 4802 · F12 MEAS measurement permissions (MEAS-CONTRACT §1.1, §2; lane meas-settings).
-- PERM-MEAS-001 measurement settings: Owner changes (A); Manager and Technical administrator read the group (V). DEC-058: Owner-only settings.
-- PERM-MEAS-002 enter meter readings / a survey on behalf of a guest: Owner, Manager, Reception (surveys), Maintenance (readings).
-- Housekeeping and Maintenance see no money (DEC-061): PERM-MEAS-002 is not a money permission and grants no report.
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-MEAS-001', 'meas.settings (measurement packs, items, targets; Owner changes)', 'ACTION', 0),
  ('PERM-MEAS-002', 'meas.capture.behalf (meter readings, desk survey entry)', 'ACTION', 0);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-MEAS-001', 'A', 'P', NULL, NULL), ('MGR', 'PERM-MEAS-001', 'V', 'P', NULL, NULL), ('TAD', 'PERM-MEAS-001', 'V', 'P', NULL, NULL),
  ('OWN', 'PERM-MEAS-002', 'A', 'P', NULL, NULL), ('MGR', 'PERM-MEAS-002', 'A', 'P', NULL, NULL), ('REC', 'PERM-MEAS-002', 'A', 'P', NULL, NULL),
  ('MNT', 'PERM-MEAS-002', 'A', 'P', NULL, NULL);
