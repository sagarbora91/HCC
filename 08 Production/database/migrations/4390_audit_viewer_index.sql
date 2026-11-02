-- HCC migration 4390 · F4-T3 qw-audit: the audit viewer filters by date and actor (newest first). 4391-4392 reserved.
CREATE INDEX IX_audit_event_at_principal ON plat.audit_event(at_utc DESC, principal_id) INCLUDE (action, target);
