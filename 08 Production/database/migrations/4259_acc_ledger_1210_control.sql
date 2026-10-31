-- HCC migration 4259 · ACC-2 integration, coordinator ruling 7 (parity DONE open item 2; rulebook D4): 1210 Recoverable from leavers (F&F)
-- becomes a CONTROL ledger with one party per staff member (1210/STF-<id>): a full and final that leaves the leaver owing posts
-- payroll:fnf-recoverable → 1210/<leaver> (ChartOfAccounts.All / DefaultMapping; prototype ACC_LEDGERS / ACC_PARTY_CONTROLS).
-- The catalogue row mirrors ChartOfAccounts.All (SqlChartStoreTests pins it); a property's override row of the built-in keeps the
-- control flag of the built-in (code and control never change, 4201).
UPDATE acc.ledger SET is_control = 1,
  note = CASE WHEN property_id IS NULL THEN N'ACC-2 (ruling 7): one party per staff member — a full and final that leaves the leaver owing (D4)' ELSE note END
WHERE code = '1210' AND is_system = 1;
GO
