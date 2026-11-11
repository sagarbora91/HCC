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
- `dotnet test tests/Hcc.Reporting.Tests -maxcpucount:1 --filter "FullyQualifiedName~Hcc.Reporting.Tests.CuB|FullyQualifiedName~Catalogue_json"`: **16 passed, 0 failed, 0 skipped** (14 new SQL-backed tests: occupancy 4, receipts 3, day summary 4, stock sheet 3 + the catalogue cross-check `Catalogue_json_from_the_prototype_equals_the_server_registry`). Each new result also passes the three-way check screen = Excel = PDF text = print.
- `dotnet build src/Hcc.Api`: succeeded, 0 warnings, 0 errors (checks the HccComposition registration compiles; DI construction at runtime not exercised).
- Prototype: `npx tsc --noEmit` clean; `node tools/rpt-catalogue.mjs --check` up to date (124 reports); `node --test` on `stream-rpt-engine`, `stream-rpt-built`, `stream-rpt-hub`, `sync-hub-server`: 4/4 pass. Full `npm test` not run (no shared prototype code beyond the flag flips).
Permission tests use the real `SqlAccessPolicy` on migrated roles: OWN/MGR/ACC allowed; REC, CSH, HKP refused (FORBIDDEN) for occupancy, receipts, day summary, stock sheet; SUP sees stock quantities with cost/value removed at runner level only (the hub refuses SUP/HKP/MNT for these money reports, DEC-061); future date and multi-day range refused (VALIDATION).

## NOT verified
- Full Hcc.Reporting.Tests project, other test projects, `Hcc.Integration.Tests` (hub HTTP) not run (targeted-only rule).
- Hub-level DEC-061 guard (`ReportService`) for the four ids: all four are money reports, so SUP/HKP/MNT are refused there by the existing guard; only the runner level was tested.
- No device/phone check, no real production data, no PDF/print visual check beyond the text equality.

## Defaults used
1. Item order of the stock sheet = item code (server list order); the prototype uses slice order.
2. Occupied nights follow the stay interval (early check-out ends the stay), as the daily pack does; the prototype is stay-based too (a check-out needs every night up to departure charged), so the two agree and no ruling is needed.
3. Receipt tender list: master methods usable at the front desk, falling back to Cash/UPI/Card/Bank transfer when the master has none; unlisted tenders found in the range are appended in ordinal order.
4. Day-summary settlement rows: POS methods of the master (fallback Cash/UPI/Card), then "Posted to room folios", then other names found.
5. Stock sheet view uses the engine default (property scope): HKP (assigned scope) and SUP do not reach it at hub level (money report + DEC-061), same as the prototype hub.
6. Company credit = kind `company-credit` only (not OTA / guest credit), as the prototype.

## Open questions
None.

## Fix round (audit commit 4533f9c2, verdict FIX)
- FIX 1: `depositsOut` is now negative (a deposit returned) per day and in the total row, as the prototype `receiptRow`; net receipts and the second block are unchanged. Test expects -10000.
- Note 2: unlisted tenders are now ordered by first receipt (lowest receipt id), as the prototype.
- New SQL-backed cases (3 new tests, now 17 CuB tests; the other cases extend the existing seeds and assertions): deposit-return sign; split stay (folios A and B) in receipts-deposits (Card on both folios) and in occupancy (one stay over two folios, 1 occupied night, revenue from both folios); a method once named differently ("Paytm", not in the master) keeps its own column after Wallet; room-charge correction excluded from room revenue; `inv.room_block` block in occupancy (sellable 3); voided POS bill (status Void) excluded from every day-summary figure; empty stock list gives only the total row; hub-level (ReportService) refusal of HKP and SUP for rpt.stock.stock-sheet, Owner allowed.
- DONE corrections: 14 seeded cases (not 15); occupancy is stay-based on both sides; the Supervisor sees quantities only at runner level.
- Counts after the fix: Hcc.Reporting.Tests filter CuB + EngineTests + Catalogue_json: 33 passed, 0 failed, 0 skipped. `Hcc.Integration.Tests` ReportHubHttpTests: 2 passed, 0 failed. Prototype: `tsc --noEmit` clean, 4/4 report test files pass, catalogue check up to date. `catalogue.json` unchanged.
- Open: none.
