# F8 lane RPT-P2-C — DONE (8 Nov 2026)

Worktree `E:\Projects\HCC\worktrees\f8-rpt-p2-c`, branch `claude/f8-rpt-p2-c` (from tag `f8-setup`). Batch c of SF-CONTRACT §12. Written by the Sonnet lane.

## Reports

| Report | State | Prototype | Server |
|---|---|---|---|
| rpt.acc.asset-register | built; per-asset Depreciation and Written-down value columns NOT built (BLOCKED, see below) | `src/domain/reports/p2-c/asset-register.ts` | `Reports/P2C/P2CReports.cs` AssetRegisterReport (IFixedAssetStore.ListAsync) |
| rpt.people.leave-balances | built | `p2-c/leave-balances.ts` | LeaveBalancesReport (IP2CReportSource.LeaveAsync) |
| rpt.stock.grn-register | built | `p2-c/grn-register.ts` | GrnRegisterReport |
| rpt.rest.sales-by-item | built | `p2-c/sales-by-item.ts` | SalesByItemReport (Cashier own shift) |
| rpt.rest.voids | built | `p2-c/voids.ts` | PosVoidsReport |
| rpt.admin.scheduled-runs | built | `p2-c/scheduled-runs.ts` | ScheduledRunsReport (IReportRunStore + IReportScheduleStore) |
| rpt.admin.cancelled-lines | built | `p2-c/cancelled-lines.ts` | CancelledLinesReport |
| rpt.admin.rate-sheet | built (3 views, per-view columns) | `p2-c/rate-sheet.ts` | `Reports/P2C/RateSheetReport.cs` (IYieldStore, IYieldRateQuery) |

Shared: `p2-c/shared.ts` (92-day cap, share to one decimal), server `P2CRules`, `IP2CReportSource` with `SqlP2CReportSource.cs` (plain SELECTs on stk.grn, pos.*, hr.leave_*, bill.charge).
Registered in `register.ts` (F8-RPT-P2-C block), `HccComposition.cs` (F8-RPT-P2-C services block: one `AddReport<>` per report plus one line for the `IP2CReportSource` registration, which the brief did not list), catalogue status line (F8-RPT-P2-C block of the catalogue document).
Tests: `03 Prototype/tests/stream-rpt-p2-c.mjs`, goldens `tests/rpt-golden/goldens/<8 ids>.json` (cases BASE and FY26; rate sheet also publish log and change history), `08 Production/tests/Hcc.Reporting.Tests/P2C/P2CReportsTests.cs`.
Migrations: none used (4454–4455 unused).

## Defaults and decisions relied on (no owner question covers batch c)
- Owner questions sheet not consulted for a default; none of the eight reports needed one.
- Asset register: DEC-138 / DEC-140 (the CA passes depreciation in Tally; HCC keeps the register). Cost shown is the register cost (bill less ITC, net of posted returns), as `accAssetRegister`.
- Leave balances: used = approved days dated up to the as-on date; pending = Pending days of the FY; balance = entitlement less used (SCC no carry-forward, DEC-116/119). Tracked types CL, SL, EL only.
- Sales by item: value = quantity × price before tax; voided lines excluded; Cashier sees only bills they settled (ownShift, like rest.closed-bills).
- Cancelled lines: a cancellation = a reversal line whose approval is a correction (WF-FOLIO-002); credit notes are not. Qty is always 1 (a folio line has no quantity). Cancelled by = requester of the correction (as rpt.tax.bill-corrections). SF commands write no money rows (R7), so nothing from them appears.
- D-B reports cap at 92 days inside the report (the engine binder allows 366 for every D-B report; the engine is not mine to change).
- PERM-YLD-001 has no row in the prototype's v1 matrix: `rate-sheet.ts` sets `RPT_V2_GRANTS['PERM-YLD-001']=['Owner','Manager']` (the pattern mask.ts uses for ACC-006), mirroring the server seed 3700.

## BLOCKED / not done
- asset-register columns 10 and 11 (Depreciation, Written-down value per asset): HCC keeps no per-asset depreciation rate table; rulebook F2 gives block rates but nothing in code computes them and DEC-138/140 leave depreciation to the CA. Money rule not covered, so not guessed. Needs an owner/CA ruling, then ACC-3 C1 factor `accAssetDepreciationFactor` can be used.
- rate-sheet: the change history leaves out the textual yield configuration history (YLDHistory before/after are free text and could leak money past the money gate).

## Integration needed
- `08 Production/tests/Hcc.Reporting.Tests/EngineTests.cs` `ServerCatalogue()` must list the eight new selectors (outside this lane's row, no F8 marker): without them `Catalogue_json_from_the_prototype_equals_the_server_registry` fails once the integrator regenerates the catalogue. I added them locally, ran the test green, then reverted. Line to add: `new Hcc.Reporting.Reports.P2C.AssetRegisterReport(null!), new ...LeaveBalancesReport(null!), new ...GrnRegisterReport(null!), new ...SalesByItemReport(null!, null!), new ...PosVoidsReport(null!), new ...ScheduledRunsReport(null!, null!, null!), new ...CancelledLinesReport(null!), new ...RateSheetReport(null!, null!, null!)`.
- The shared XlsxRenderer writes a non-integer value in an Int column as text: leave balances with half days (0.5) show as text in the server Excel (the prototype writes the number). Server three-way test therefore runs on whole days. Engine fix needed (write double for a fractional Int cell).
- `src/domain/reports/catalogue.json` and `08 Production/tests/Hcc.Reporting.Tests/catalogue.json` are generated: NOT committed by this lane (integrator regenerates once after the merge; I regenerated them locally only to run the cross-check, then reverted them).
- Server-side ledger name in the asset register uses `ChartOfAccounts.NameOf`, so an Owner-renamed ledger shows its built-in name on the server and the renamed one in the prototype.
- Server voids and cancelled-lines check numbers use bill_no / the order id, the prototype uses its own ids; figures are the same.
- Leave days that are halves (0.5) appear in `int` columns, as the catalogue types them.
- `SqlP2CReportSource` and the rate-sheet queries compile but were NOT run against SQL Server (no SQL smoke in this lane).

## Commands actually run and counts
Run through `tools/Invoke-Serialized.ps1` (label f8-rpt-p2-c), logs in `.scratch/` (not committed):
- `npx tsc --noEmit` in `03 Prototype`: exit 0.
- `node tools/rpt-catalogue.mjs` (local run only, output reverted) and `node tests/rpt-golden/accept.mjs`: goldens written for the 8 ids (cases BASE and FY26; rate sheet also publish log and change history).
- `npm test` (full, no HCC_TEST_ONLY): 3181/3181 tests passed (30 async), exit 0. The batch-c file `tests/stream-rpt-p2-c.mjs` adds 13 tests (an earlier filtered run `HCC_TEST_ONLY='rpt-p2-c|rpt-engine|goldens'` had 94/95 with one test-expectation error, fixed before the full run).
- `dotnet build src/Hcc.Api -maxcpucount:1` : exit 0.
- `dotnet test tests/Hcc.Reporting.Tests -maxcpucount:1 --logger trx`: 138/138 passed with the eight selectors temporarily added to `EngineTests.ServerCatalogue()` (see Integration needed); `P2CReportsTests.cs` adds 14 tests. Without that temporary line, 137/138 (only the catalogue cross-check fails, as expected).
- NOT run: SQL Server smoke of `SqlP2CReportSource`, the `dotnet test Hcc.slnx` solution, any emulator.
