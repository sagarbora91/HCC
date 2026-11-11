# cu-rpt-b DONE (wave CU, lane rpt-b)

Branch `claude/cu-rpt-b`, base c45bbce3. Four prototype-only P1 reports now run on the server report hub.

## Files changed
New (server, folder `08 Production/src/Hcc.Reporting/Reports/CuB/`, LF):
- `CuBSupport.cs` shared helpers (payment-method names from `mst.payment_method`, row helper).
- `ReceiptsDepositsReport.cs` `rpt.money.receipts-deposits`
- `RestDaySummaryReport.cs` `rpt.rest.day-summary`
- `RoomsOccupancyReport.cs` `rpt.rooms.occupancy`
- `StockSheetReport.cs` `rpt.stock.stock-sheet`

New tests `08 Production/tests/Hcc.Reporting.Tests/CuB/`: `CuBRig.cs`, `ReceiptsDepositsReportTests.cs`, `RestDaySummaryReportTests.cs`, `RoomsOccupancyReportTests.cs`, `StockSheetReportTests.cs`.

Shared files touched (append only, inside `// CU-RPT-B` ... `// /CU-RPT-B`):
- `08 Production/src/Hcc.Api/HccComposition.cs`: four `AddReport<...>` lines after the `F8-RPT-P2-C end` line.
- `08 Production/tests/Hcc.Reporting.Tests/EngineTests.cs`: the four reports in `ServerCatalogue()`.

Prototype: `built/pack.ts`, `built/rest.ts`, `built/stock.ts` lost `serverMirror:false` on the four reports (so the hub treats them as server-run, no more "Not on the server yet"); `catalogue.json` and `08 Production/tests/Hcc.Reporting.Tests/catalogue.json` regenerated with `node tools/rpt-catalogue.mjs` (only four `"side"` lines changed prototype -> both). The production client needs no further routing: the hub takes server-offered ids from `GET /reports/catalogue`.
Merge note: lane rpt-a regenerates the same two catalogue.json files and edits the same two shared files; on conflict re-run `node tools/rpt-catalogue.mjs` after merging the `pack.ts`/`rest.ts`/`stock.ts`/admin edits.

## How each report is computed (same ids, columns, params, totals, permissions as the prototype)
- receipts-deposits (PERM-RPT-002, money, D-F): `IReportSource` (daily-pack source records), tender columns = active desk methods of the payment-method master in list order + any other tender found; payments minus refunds per tender per day, deposits in/out, total row; second block company credit in range and deposits held now (SQL over all folios: deposit - return - apply, opening-advance apply excluded).
- rest.day-summary (PERM-POS-004, money, D-DAY): `IPosStore.DaySummaryAsync` + `IPosTaxRates` (same figures as GET /pos/day-summary); four blocks outlets / settlement / top items / open checks; settlement by tender name over `pos.bill`.
- rooms.occupancy (PERM-RPT-002, money, D-F): `IReportSource`; sellable / occupied / room revenue per date, % half-up 1 dp, ADR and RevPAR as rupee text (half away from zero, dash when divisor 0), total row recomputed. Equals `DailyPackCalculator.Compute` (tested).
- stock.stock-sheet (view PERM-STK-001, money gate PERM-STK-004, no date): `IStockReadStore.ItemsAsync`; unit cost / value removed by the engine without the gate.

## Tests run (all through the lock)
- `dotnet test tests/Hcc.Reporting.Tests -maxcpucount:1 --filter "FullyQualifiedName~Hcc.Reporting.Tests.CuB|FullyQualifiedName~Catalogue_json"`: **16 passed, 0 failed, 0 skipped** (15 new SQL-backed tests: occupancy 4, receipts 3, day summary 4, stock sheet 3 + the catalogue cross-check `Catalogue_json_from_the_prototype_equals_the_server_registry`). Each new result also passes the three-way check screen = Excel = PDF text = print.
- `dotnet build src/Hcc.Api`: succeeded, 0 warnings, 0 errors (checks the HccComposition registration compiles; DI construction at runtime not exercised).
- Prototype: `npx tsc --noEmit` clean; `node tools/rpt-catalogue.mjs --check` up to date (124 reports); `node --test` on `stream-rpt-engine`, `stream-rpt-built`, `stream-rpt-hub`, `sync-hub-server`: 4/4 pass. Full `npm test` not run (no shared prototype code beyond the flag flips).
Permission tests use the real `SqlAccessPolicy` on migrated roles: OWN/MGR/ACC allowed; REC, CSH, HKP refused (FORBIDDEN) for occupancy, receipts, day summary, stock sheet; SUP sees stock quantities with cost/value removed; future date and multi-day range refused (VALIDATION).

## NOT verified
- Full Hcc.Reporting.Tests project, other test projects, `Hcc.Integration.Tests` (hub HTTP) not run (targeted-only rule).
- Hub-level DEC-061 guard (`ReportService`) for the four ids: all four are money reports, so SUP/HKP/MNT are refused there by the existing guard; only the runner level was tested.
- No device/phone check, no real production data, no PDF/print visual check beyond the text equality.

## Defaults used
1. Item order of the stock sheet = item code (server list order); the prototype uses slice order.
2. Occupied nights follow the server stay interval (early check-out ends the stay), as the daily pack does; the prototype counts booking intervals.
3. Receipt tender list: master methods usable at the front desk, falling back to Cash/UPI/Card/Bank transfer when the master has none; unlisted tenders found in the range are appended in ordinal order.
4. Day-summary settlement rows: POS methods of the master (fallback Cash/UPI/Card), then "Posted to room folios", then other names found.
5. Stock sheet view uses the engine default (property scope): HKP (assigned scope) and SUP do not reach it at hub level (money report + DEC-061), same as the prototype hub.
6. Company credit = kind `company-credit` only (not OTA / guest credit), as the prototype.

## Open questions
None blocking. The prototype counts booking-based occupancy, the server stay-based; if the owner wants them identical on an early-checkout day, one side needs a ruling.
