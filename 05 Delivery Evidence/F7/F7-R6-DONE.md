# F7-R6 DONE — accounting (batch 2)

Lane R6 acc · Sonnet · branch `claude/f7-r6` (worktree `/home/user/wt-f7-r6`) · base = `claude/f7` 57e2c4bb (engine, hub, guests/rooms, money-a, tax merged).

## Reports (prototype AND server, same ids, column keys / order, flags)
| Id | Prototype `03 Prototype/src/domain/reports/acc/` | Server `08 Production/src/Hcc.Reporting/Reports/Acc/` | Figures from | Permission |
|---|---|---|---|---|
| `rpt.acc.trial-balance` | `trial-balance.ts` | `TrialBalanceReport.cs` | `accTrialBalance6` / `AccountingReportQueries.TrialBalanceAsync` | PERM-ACC-001 |
| `rpt.acc.statement` | `statement.ts` | `StatementReport.cs` | `accStatement` / `StatementAsync` | PERM-ACC-001 |
| `rpt.acc.day-book` | `day-book.ts` | `DayBookReport.cs` | `accDayBook` / `DayBookAsync` | PERM-ACC-001 |
| `rpt.acc.pl` | `pl.ts` | `PlReport.cs` | `accPl` / `PlQueries.PlAsync` (ICAI vertical + Tally-group view) | PERM-ACC-013 |
| `rpt.acc.balance-sheet` | `balance-sheet.ts` | `BalanceSheetReport.cs` | `accBalanceSheet` / `BalanceSheetAsync` | PERM-ACC-013 |
| `rpt.acc.cash-ratio` | `cash-ratio.ts` | `CashRatioReport.cs` | `accCashRatio` / `CashRatioAsync` | PERM-ACC-013 |
| `rpt.acc.ageing` | `ageing.ts` | `AgeingReport.cs` | `accAgeing` / `AgeingQueries.AgeingAsync` | PERM-ACC-001 |
| `rpt.acc.tally-xml` | `tally-xml.ts` | `TallyXmlReport.cs` | `accTallyMasters` / `accTallyVouchers` / `TallyQueries` (no file log: a report run records no export) | PERM-ACC-006 (view + export) |

Shared: `acc/shared.ts`, `AccShared.cs`, `AccQueries.cs` (builds the ACC queries the way the endpoints do, from the service provider).
No ACC-3 selector was touched: each report is an adapter. Roles follow the catalogue (coordinator ruling, as R5): Owner + Accountant → PERM-ACC-013 (P&L, balance sheet, cash ratio), Owner + Manager + Accountant → PERM-ACC-001, Tally → PERM-ACC-006. Nothing widened; Cashier / Reception / Housekeeping / Maintenance / Tech admin are refused everywhere.
Migrations: **none** (4432–4433 unused).

## Year-lock PROVISIONAL stamp (F5, ACC4 §1.2)
When the range touches a reopened year (or the year after), the report's FIRST ROW is "PROVISIONAL — year reopened" (prototype `accYearProvisional`, server `YearLockRules.Provisional`), so screen = Excel = PDF/print = CSV carry it; `about.provisional` is true. Tally: the text is in the Notes column.

## Goldens (ACC-RULEBOOK April 2026)
P&L profit before tax 14,040,000 paise (₹1,40,400); balance sheet both sides 792,790,000 paise (₹79,27,900), "Balanced", marked "not the statutory statement". Prototype goldens `tests/rpt-golden/goldens/rpt.acc.*.json` (8 files, 12 cases, Accountant on ACC-RULEBOOK) with screen = Excel = print on every case. Server: `Goldens/rulebook-reports.json` (copy of the Accounting.Tests hand-computed expectation) drives the P&L, balance sheet and cash-ratio builders.

## Tests
- Prototype `tests/stream-rpt-acc.mjs` **15 tests** (registry, every figure vs the ACC selectors, roles, edges, stamp, provisional, CSV, goldens).
- Server `tests/Hcc.Reporting.Tests/Acc/AccReportsTests.cs` **10 tests** (views → builders → `RendererTests.ThreeWay`; stamp in CSV; definitions; rule V through the runner). `Hcc.Reporting.Tests` 59 → **69/69**; catalogue cross-check green (`catalogue.json` regenerated, both copies).
- `dotnet build Hcc.slnx` 0 warnings / 0 errors; `npm test`: full run was killed by the box (memory, parallel lanes) — the 7 rpt stream files (94 tests) and `tsc --noEmit` pass; integrator to run the full suite.

## Integration needed (f7-int)
- `HccComposition.cs`: the `F7-R6` block inside `F7-RPT` is in (an `AccQueries` singleton + eight `AddReport<...>()` lines). `EngineTests.cs` `F7R6Reports()` block added. `mask.ts` `F7-R6` block adds the PERM-ACC-006 grant (Owner, Accountant) to `RPT_V2_GRANTS`.
- Catalogue §2: the three `built` reports stay `built`; ACC-3 ones stay `ACC-3` (status text unchanged); lines appended at the end.
- Entry points (Accounting screen buttons) are NOT switched; old per-page CSV / Excel stay reachable.

## Decisions / open points
1. P&L / balance sheet / cash ratio gate on PERM-ACC-013 (Owner + Accountant), not PERM-ACC-001 (which adds the Manager) — catalogue "who may see it".
2. `rpt.acc.tally-xml` on the engine is the file summary (File, Vouchers, Ledgers, Notes); the engine has no XML renderer, so the `xml` download remains the ACC-006 endpoints. Extra params `vFrom` / `vTo` (default: last 7 days of the bound FY range).
3. Statement and trial balance defaults: the engine's D-F default is today..today, not FY start (engine behaviour, catalogue says FY start to today) — hub presets cover it.
4. D-B / D-F maximum is the engine's 366 days (catalogue says 92 for the day book).
5. Cash-ratio Share is percent with one decimal (basis points rounded half up in integer arithmetic on both sides); Flagged is 1 / 0 on the prototype, true / false on the server.
6. "−" (U+2212) is not in the PDF's WinAnsi: P&L / balance-sheet text uses "-".
7. Not done in this lane (still `built` elsewhere, other catalogue entries): `rpt.acc.vouchers`, `chart-parties-opening`, `exceptions-recon-mapping`, `manual-register`, `asset-register`, `lock-history`, `statements-bulk` (zip), `rpt.tax.gst-month`, `tds-deductee`.
8. Server SQL-level test of the query layer was not added: the stores are covered by `Hcc.Accounting.Tests`; this suite tests the builders with the golden's views.

## HANDOFF bullet
F7-R6 accounting done (claude/f7-r6): trial balance, statements, day book, P&L, balance sheet, cash ratio, ageing and the Tally file summary on the report engine both sides; goldens on the April rulebook month (profit 1,40,400; balance sheet 79,27,900), year-lock PROVISIONAL stamp in every format; 15 prototype + 10 server tests; no migrations.
