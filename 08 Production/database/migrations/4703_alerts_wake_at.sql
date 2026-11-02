-- HCC migration 4703 · LF B3 wake-up call alert (LF-CONTRACT §9.1). ops.guest_request.wake_at_utc = the wake-up time (UTC, whole minutes) of a 'Wake-up call'
-- request; the SLA clock starts there (scheduled_utc, 1500, already carried the time — it stays as it is). The desk derives the alert from the synced requests
-- (wake_at_utc <= now and the request not Done / Cancelled); snooze moves wake_at_utc, scheduled_utc and due_utc together by 5 minutes.
ALTER TABLE ops.guest_request ADD wake_at_utc DATETIME2(0) NULL;
GO
UPDATE ops.guest_request SET wake_at_utc = CONVERT(DATETIME2(0), scheduled_utc) WHERE request_type = 'Wake-up call' AND scheduled_utc IS NOT NULL;
CREATE INDEX IX_guest_request_wake ON ops.guest_request(property_id, wake_at_utc) WHERE wake_at_utc IS NOT NULL AND status IN ('Open','Assigned','InProgress');
