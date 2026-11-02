-- HCC migration 4289 · ACC-4 acc4-bank · reconciliation helper view (ACC4-CONTRACT §2.6). Live matched book lines with their cleared date; the
-- report (BankReconRules.Recon) takes books ± unmatched lines ± unmatched rows = statement, difference must be 0.
GO
CREATE VIEW acc.v_bank_cleared_line AS
  SELECT m.property_id, m.bank_ledger, l.source_key, l.entry_no, l.cleared_date, m.match_id, m.rule_kind
  FROM acc.bank_match m JOIN acc.bank_match_line l ON l.match_id = m.match_id
  WHERE m.undone_utc IS NULL;
GO
