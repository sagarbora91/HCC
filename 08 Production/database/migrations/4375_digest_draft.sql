-- HCC migration 4375 · F4-T2 owner digest drafts (F4-CONTRACT §3). Two drafts per closed business date: Email (money, Owner + Accountant) and WhatsApp (counts only).
-- status is always 'held': nothing reads these rows to send; the F9 messaging adapter will move held drafts into the outbox. Idempotent per (property, date, channel):
-- a re-close replaces the row. The server outbox is NOT used for these drafts (it would deliver).
IF SCHEMA_ID('digest') IS NULL EXEC('CREATE SCHEMA digest');
