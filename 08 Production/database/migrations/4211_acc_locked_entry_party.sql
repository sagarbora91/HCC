-- HCC migration 4211 · ACC-1 party sub-ledgers (lane acc1-parties): a locked snapshot keeps the party ledger id each entry posted to
-- ("<control code>/<party id>", at most 4 + 1 + 40 characters), so acc.locked_entry.account_code widens from VARCHAR(4) to VARCHAR(45).
-- Existing snapshots keep their 4-digit codes unchanged (locked snapshots are never re-pointed). Not part of a key or an index.
ALTER TABLE acc.locked_entry ALTER COLUMN account_code VARCHAR(45) NOT NULL;
GO
