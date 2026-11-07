# MEAS2 lane E — study / asset money (7 Nov 2026)

Branch `claude/meas2-e` (cut from `claude/meas` 79f7abe7). Opus lane.

## Sizing claims checked in code
- N1 confirmed: `StayRateCommands.cs:206` writes the re-post reversal as `ChargeKind.Correction` with `reverses_charge_id`; `BillingService.cs:411` (void) does the same. The loader summed it under "other", so room revenue = old + new rate. The old fixture wrote the reversal as kind `'room'` and hid this.
- N2 confirmed: `AssetLifeReport.cs:70-73` had no reversal filter.
- N5 confirmed: the server built the kind as `"Fixed assets " + ledger code`, and the prototype used the ledger name.
- L2 (linen) confirmed: `LinenCostReport.cs:84` divided through `double`.
- Extra finding (twin): the prototype `asset-life.ts` `roomRevenueBetween` had the same N2 double count. `stay-rate.ts:25` writes the reversal as kind `'adjustment'`, so the original and the new line were both counted. Fixed too.

## Fixes
- **N1 + M5 study side**: `08 Production/src/Hcc.Reporting/Analytics/SqlAnalyticsInputSource.cs` `LiveChargeSql` (about lines 149-168).
  - A `'correction'` line that reverses another line is summed under the kind of the line it reverses (LEFT JOIN on `reverses_charge_id`, `COALESCE(o.kind, c.kind)`).
  - A re-posted night therefore counts once in room revenue, at the new rate, and "other" no longer goes negative. Bill totals and tax are unchanged.
  - Charged nights now also need `amount_paise > 0` (owner decision M5/N1, 7 Nov).
  - Only live months change; the `lgc` legacy queries are untouched.
- **N2**: `AssetLifeReport.cs` (revenue query about line 77) gains `AND c.reverses_charge_id IS NULL AND c.reversed_by_charge_id IS NULL`. The basis text says so.
  - Prototype twin: `03 Prototype/src/domain/reports/meas-hk/asset-life.ts` `roomRevenueBetween` skips reversal lines and reversed originals.
- **N5 kind**: `AssetLifeReport.cs` (about lines 46-60). A register asset's kind is the effective ledger name: the property's `acc.ledger` override, else the built-in, else the code (twin of `accAssetRegister().ledgerName`). Sorting is `StringComparer.InvariantCulture`, closer to the prototype's `localeCompare`.
- **N5 WDV (Opus decision)**: the column label stays "WDV", because the catalogue §3.10 and `stream-meas-rpt-hk.mjs:29` pin it (outside this lane).
  - Under DEC-138, depreciation is booked per block, so no per-asset WDV exists.
  - The basis text on both twins now says so: the column shows the register cost, and the block WDV is in the fixed-asset register.
  - If the owner prefers the label "Cost", a later change must also update the catalogue and that test.
- **L2 linen**: `LinenCostReport.cs:84` now uses `LinenCostReport.DivHalfUp` (integer, half away from zero, sign-aware) instead of `double`.

## Tests
- `tests/Hcc.Meas.Tests/Analytics/AnalyticsInputSqlTests.cs`:
  - The T-1 reversal is now written as production writes it: kind `'correction'` with `reverses_charge_id`, original marked `reversed_by_charge_id`. The existing assertions are unchanged.
  - New property `ANR` with a night re-posted exactly as `StayRateCommands.ApplyAsync` does it, plus a zero-rate night.
  - New fact `MEAS2_E_N1_…` asserts room 450000, other 0, total 504000, charged nights 2 (the zero night is excluded), and pack facts night 2 = 200000.
- New `tests/Hcc.Meas.Tests/Hk/AssetLifeSqlTests.cs`:
  - N2: a re-posted night; reserve 9000 = 3 % × the live revenue; capex 12000; red.
  - N5: order, ledger-name filter, property override wins, the old "Fixed assets 1510" kind matches nothing, WDV = register cost.
  - L2: `DivHalfUp` theory with 5 cases, including 2^40 × 3 + 1.
- New prototype twin `03 Prototype/tests/stream-meas2-e-asset-life.mjs`: 2 tests (N2, N5).

## Commands actually run (through tools/Invoke-Serialized.ps1, label meas2-e)
- `dotnet test tests/Hcc.Meas.Tests/Hcc.Meas.Tests.csproj -maxcpucount:1` (in `08 Production`): **Passed 105 / 105, 0 failed**. The filtered log showed no warning or error lines.
- `HCC_TEST_ONLY='meas-rpt-hk|meas2-e' npm test` (in `03 Prototype`): **90/90 passed**. This is the core suite plus `stream-meas-rpt-hk` and the new file.
- `npx tsc --noEmit`: exit 0.

## Not done / not verified
- Not run: the full solution test, the full `npm test`, and the legacy golden re-run (`tools/meas-legacy-golden.ps1`). These belong to the wave gate. N1 touches only live `bill.charge` rows, so the legacy golden should not move.
- I did not re-run the new tests against the old code to watch them fail. They fail by arithmetic: old room column 700000 / reserve 18000.
- A2 bill-correction lines (`BillCorrectionService.cs:163`, `FolioInvoiceSource.cs:74`: kind `'correction'` with no `reverses_charge_id`) still land in "other" in the study. This is out of scope and needs a ruling on how a credit-note correction of a room line should count.
- `SqlStudyPackFacts.RoomNightsAsync` still counts zero-amount room nights. The file is not in lane E; flagged for the pack owner.
