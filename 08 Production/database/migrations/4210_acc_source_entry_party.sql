-- HCC migration 4210 · ACC-1 party sub-ledgers (02 Planning/agents/ACC1-POSTING-CONTRACT.md §3; rulebook §3.3; lane acc1-parties, block 4210–4219).
-- acc.source_entry.party_id = the party the source names for that side (company CMP-<id>, staff STF-<id>, vendor / bank account / till code,
-- OTA channel code, Udhaar customer UD-<mobile> or UD-<slug of name>). The mapping resolves map_key to a ledger code; when that code is a
-- control ledger (1000, 1030, 1110, 1115, 1120, 1200, 2200, 2400) the voucher entry posts to "<code>/<party_id>" (Hcc.Domain PartyLedgers).
-- NULL = no party named (every row written before ACC-1, and sides that are not control ledgers): a control entry then stays on the control
-- and the day shows a "no-party" warning (never blocks a lock). Sources stay append-only (TR_source_entry_append_only): nothing is back-filled.
ALTER TABLE acc.source_entry ADD party_id VARCHAR(40) NULL
  CONSTRAINT CK_acc_source_entry_party CHECK (party_id IS NULL OR (LEN(party_id) BETWEEN 1 AND 40 AND party_id NOT LIKE '%/%'));
GO
