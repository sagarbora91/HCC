# ACC3-TESTS-DONE — lane acc3-tests (Sonnet), branch claude/acc3-tests

Status: **written against the contract; UNVERIFIED against the report lanes.** At hand-off none of ACC3-PL-DONE.md, ACC3-AGEING-DONE.md, ACC3-TALLY-DONE.md existed, so nothing was merged and the engine tests have not run against real selectors.

## Files
- `03 Prototype/src/fixtures/ext/accounting-rulebook.ts`: appended `ACC_RB_REPORTS` (expectations only, paise, hand-computed from rulebook 8.1-8.6).
- `03 Prototype/tests/stream-accounting-golden3.mjs`: 15 tests `[ACC3-TESTS]`.
- `08 Production/tests/Hcc.Accounting.Tests/Golden/rulebook-reports.json` (same figures) and `RulebookReportsTests.cs`.

## Test counts
- Prototype `npm test`: 2095 (before) -> 2096 pass / 2110 total. 1 self-check passes; 14 fail on purpose with "waits for lane acc3-pl / acc3-ageing / acc3-tally" (selectors not exported yet): accPl, accBalanceSheet, accCashRatio, accAgeing, accGstSummary, accTallyMasters, accTallyVouchers. The failing run is expected until the lanes are merged.
- Server: `RulebookReportsTests.cs` is wrapped in `#if ACC3_REPORTS`, so it compiles to nothing and cannot break the build. Not run. After merging the lanes: add `<DefineConstants>ACC3_REPORTS</DefineConstants>` to Hcc.Accounting.Tests.csproj and point the `Adapter` class at the real PlQueries / AgeingQueries / GstSummaryQueries signatures (guessed names there). Server Tally golden assertions (byte equality with tally-april-2026.*.xml) are NOT written; the prototype Tally tests cover counts, sum Dr = sum Cr per voucher, GUID = REMOTEID, uniqueness, re-export byte equality, 31-day refusal.

## Findings the integrator must read
1. **Brief/contract vs rulebook (stop-and-note):** the briefs say "Deccan Agro receivable 60,000 + GST in 0-30". Rulebook 8.3 has 1110/Deccan = 0 at 30 April (T7b pays the 63,000 on 30 April). Expectations therefore: receivable ageing at 30 April = empty; at 28 April Deccan 63,000 (6 April bill, 22 days) + Goibibo 21,000 (17 April, 11 days), both 0-30. Payables at 30 April match the brief (Morning 24,000 = 5 April bill, 25 days; Kaveri 42,000, 18 days). Extra bucket checks at 31 May / 30 Jun / 15 Jul.
2. Prototype pay run is locked on 1 May, so April reports are read to 2026-05-01 (the existing convention); server events date it 30 April.
3. Assumed argument shapes (change `call()` only): accPl(s,from,to), accBalanceSheet(s,asOf), accCashRatio(s,fy '2026-27'), accAgeing(s,asOf,side), accGstSummary(s,month), accTallyMasters(s,fy), accTallyVouchers(s,from,to). GST summary: rate5/b2b/b2cs expected as taxable values (5,80,000 / 60,000 / 5,20,000; number or {taxable}); setoff.challan 28,900, tcsCredit and cashLedgerBalance 100.
4. Cash-ratio classification is mine, to be confirmed: receipts = Receipt vouchers (T3, T5c, T6, T7a, T7b, T19 = 6,70,500; cash 1,83,500 = 27.37 %, flagged); payments = Payment vouchers (T10, T11, T12, T13a, T13c, T16, T17, T18 = 4,37,090; cash 13,000 = 2.97 %). Percent or bps accepted.
5. Balance sheet block membership follows contract 2 (TDS 1360 and GST cash 1340 under current assets; duties and advance deposits in current liabilities): owner's funds 57,95,400 / secured loan 19,60,000 / current 1,72,500; assets 73,80,000 + 5,47,900.

## Integration needed
Merge order pl -> ageing -> tally -> tests; then re-run `npm test` and the C# project; fix `call()` / `Adapter` names; enable ACC3_REPORTS. A defect found by these tests goes to the owning lane with the expected figure above.

## HANDOFF bullet
ACC-3 golden reports: expectations ACC_RB_REPORTS + stream-accounting-golden3.mjs (15 tests, 14 waiting for the report lanes) + RulebookReportsTests (behind ACC3_REPORTS); Deccan receivable is nil at 30 April (open 63,000 only until 29 April).
