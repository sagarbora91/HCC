# fx-rpt-d DONE (wave FX, lane rpt-d)

Branch `claude/fx-rpt-d`, base 9c5abf01. The seven demo-only Stock-area reports now run on the server report hub (found by comparing the prototype registry, `serverMirror:false` in `built/stock.ts`, with the server registry).

## Report ids now on the server
`rpt.stock.reorder`, `rpt.stock.laundry`, `rpt.stock.counts`, `rpt.stock.losses`, `rpt.stock.consumption`, `rpt.stock.par`, `rpt.stock.movements`.
All seven have data on the server (`stk.item`, `stk.stock`, `stk.item_par`, `stk.laundry_*`, `stk.count*`, `stk.loss`, `stk.move`, `stk.occupancy_history`, `stk.setting`); none stays demo-only. No migration.

## Files
New (server, LF) `08 Production/src/Hcc.Reporting/Reports/FxD/`: `FxDSupport.cs` (labels, row helper, name helper), `StockReorderReport.cs`, `StockLaundryReport.cs`, `StockCountsReport.cs`, `StockLossesReport.cs`, `StockConsumptionReport.cs`, `StockParReport.cs`, `StockMovementsReport.cs`.
New tests `08 Production/tests/Hcc.Reporting.Tests/FxD/`: `FxDRig.cs` + one SQL-backed class per report (`StockReorderReportTests` 4, `StockLaundryReportTests` 3, `StockCountsReportTests` 3, `StockLossesReportTests` 4, `StockConsumptionReportTests` 4, `StockParReportTests` 3, `StockMovementsReportTests` 3 = 24 tests).
Prototype: `03 Prototype/src/domain/reports/built/stock.ts` lost `serverMirror:false` on the seven reports (the hub then treats them as server-run: it takes server-offered ids from `GET /reports/catalogue`, no further client routing needed, as in CuA / CuB); both `catalogue.json` files regenerated with `node tools/rpt-catalogue.mjs` (only seven `"side"` lines changed each).

Shared files (append only, inside `// FX-RPT-D` ... `// /FX-RPT-D`, after the CU-RPT-B block): `08 Production/src/Hcc.Api/HccComposition.cs` (seven `AddReport<...>` lines), `08 Production/tests/Hcc.Reporting.Tests/EngineTests.cs` (the seven in `ServerCatalogue()`). Merge note for lane rpt-c: same two files and the two `catalogue.json` copies; on conflict keep both marked blocks and re-run `node tools/rpt-catalogue.mjs`. `git diff --stat` equals `--ignore-cr-at-eol`.

## How each report is computed (same ids, columns, params, totals, permissions as the prototype)
Permissions: view PERM-STK-001; reorder, laundry, counts, losses are money reports with gate PERM-STK-004 (unit cost, laundry charge, variance value, loss value removed by the engine without it, rule M); consumption, par, movements are no-money; export PERM-RPT-003; hub DEC-061 guard refuses SUP / HKP / MNT for the four money reports.
- reorder: items with clean stock (store + floor pantries) below minimum; suggested = max(reorder, 2 x min - clean) (`StockRules`); order = shortage (clean - min) then name.
- laundry: all batches, sent date desc then id desc; received / damaged / missing blank while `Out`; charge as stored.
- counts: counts by date desc then id desc; only lines with counted <> system; variance value (sum over all lines of difference x current unit cost) on the first line; "No variance" line with value 0.
- losses: business-date range, oldest first (date, id), value = stored `value_paise`; total row (qty, value) only when there are rows.
- consumption: same sums as `StockQueries.ConsumptionAsync` (use moves for amenities / cleaning, room-change moves for linen), room-nights per date by `StockRules.Occupied` (live nights, else imported history, none after the business date), per room-night text, standard, variance % (half up) and the +/-15 % flag; last row "Occupied room-nights N".
- par: linen items with par rows; per set, required (par sets from `stk.setting`, default 3), held (all locations), coverage % half up (100 when nothing required), minimum, clean.
- movements: `stk.move` in range, newest first, optional item-name filter (trimmed, case-insensitive), "When" = IST text, location labels for known ids (supplier / "used" kept as is).

## Tests run (all through the lock)
- `dotnet test tests/Hcc.Reporting.Tests -maxcpucount:1 --filter "FullyQualifiedName~Hcc.Reporting.Tests.FxD|FullyQualifiedName~Catalogue_json|FullyQualifiedName~EngineTests"`: **40 passed, 0 failed, 0 skipped** (24 new FxD tests + EngineTests + the catalogue cross-check `Catalogue_json_from_the_prototype_equals_the_server_registry`, which proves ids, columns, perms and params equal the prototype). Each result also passes the three-way check screen = Excel = PDF text = print.
- `dotnet build src/Hcc.Api` (checks the HccComposition registration compiles): succeeded, 0 warnings, 0 errors (DI construction at runtime not exercised).
- Prototype: `npx tsc --noEmit` clean; `node tools/rpt-catalogue.mjs --check` up to date (124 reports); `node --test` on `stream-rpt-engine`, `stream-rpt-built`, `stream-rpt-hub`, `sync-hub-server`: 4/4 pass. Full `npm test` not run (no shared prototype code beyond flag flips).
Permission tests use the real `SqlAccessPolicy`: OWN / MGR / ACC allowed with money columns; SUP gets quantities without the money column (runner level); REC, CSH, HKP refused (FORBIDDEN); hub level: HKP and SUP refused for the four money reports (DEC-061), SUP allowed for consumption and movements.

## NOT verified
- Full Hcc.Reporting.Tests project, other test projects, `Hcc.Integration.Tests` (hub HTTP / DI resolution of the seven selectors at runtime) not run (targeted-only rule); DI dependencies are `IStockReadStore` and `IMoneyReportSource`, both singletons already used by the Stock endpoints and CuA.
- Consumption with live stays (`inv.room_night`) not seeded in the new tests (only the imported-history branch, the live-vs-history rule is `StockRules.Occupied`, covered in the Stock tests).
- No device / phone check, no PDF visual check beyond text equality.

## Defaults used
1. Item order follows item code (the server list order); the prototype uses slice order. Reorder order is by shortage then name (invariant culture compare vs the prototype `localeCompare`).
2. Batch and count references are `LB-<id>` and `CNT-<id>` (the references the stock commands already write), the prototype shows its own ids.
3. Consumption refuses a range over 93 days with VALIDATION (as the stock page does); the prototype silently counted only the first 93 dates for room-nights.
4. Par: an item counts as "with a par" when it has at least one par row (the server cannot store an empty par).
5. Counts: variance value uses the item's current unit cost (as the prototype and the stock page), not a cost frozen at count time.
6. Movements read at most 50,001 rows (the engine's file limit), then the engine refuses an oversize range as for every report.
7. "By" columns show the user's display name; an unknown id shows "user N".

## Open questions
None. No SCHEMA / APK bump needed.
