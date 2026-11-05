# af2-hub-server DONE

Lane af2-hub-server / branch `claude/af2-hub-server` / worktree `E:\Projects\HCC\worktrees\af2-hub-server` / base origin/main e949a9e5.
Task: audit "Missed A" (AUDITFIX-PLAN §2 Q6) — in the production build the Report library (F7 hub) ran every report on the device's partial synced
slices (DEC-108). Now the hub's catalogue, runs, Excel / CSV files and export log come from the server; the demo / mock flavour is unchanged.

## What still ran client-side in production (found)
| Piece | Before (production) | Now |
|---|---|---|
| Report list / role filter | `reportCatalogue(s,uid)` + `hubDefs` on the device (prototype grants over synced state) | `GET /reports/catalogue` (server rule V + DEC-061 guard), merged with the prototype-only reports labelled "Not on the server yet" |
| Parameters | prototype definitions | the server's definitions (`params`, date class, options, defaults) mapped 1:1 |
| Run | `runReport(s,uid,…)` over the synced slices (180 days / 5,000 bookings) | `GET /reports/{id}?<params>&all=true` (server engine, masks, rule M) |
| Excel / CSV | built on the device from the device result + `RPT_LOG` print-log | `GET /reports/{id}/export?format=xlsx|csv&encoding=base64` — server builds the file, checks view + export, writes the `rpt.export_log` row |
| Print | device result | server result printed with the existing print model + `RPT_LOG` (format print) |
| Export right | `canExportReport` on the device | the catalogue entry's `canExport` (server) |
| Export log tab | `rptX.exportLog` (always empty in production) | `GET /reports/export-log` (new) |
| Favourites | already server (`RPT_FAV_TOGGLE`) | unchanged |
| Schedules / runs | already server | editor's eligible list uses the server `canExport`; prototype-only reports are not offered |
| DEC-061 money guard | only in the hub's device-side `hubDefs` | enforced on the server (ReportService) for catalogue, run, export, print-log, favourite, schedule save and scheduled runs |

## Server (08 Production, all LF)
- `src/Hcc.Reporting/ReportService.cs`: `NoMoneyAsync` (every current role in `MoneyMasker.NoMoneyRoles` = no money, whatever the grants), `GuardAsync` (money report → 403), `StripMoney` (money + dependent computed columns removed from every block); `CatalogueAsync` drops money reports for such callers; `RunAsync(..., all)` overload (every row, ≤ FileMax, no log row); `ExportLogAsync` (OWN / MGR / ACC every row, others own rows); `PrintLogAsync` now needs the export permission for a non-print format (a device-built file) and applies the DEC-061 guard; new `IReportExportLogReader` (optional last ctor parameter).
- `src/Hcc.Reporting/Sql/SqlReportStores.cs`: implements `IReportExportLogReader` (existing `ExportLogAsync`); `ReportingModule.cs` registers it.
- `src/Hcc.Api/Endpoints/ReportEndpoints.cs`: `GET /reports/export-log`; `all=true` on `GET /reports/{id}`; `encoding=base64` on `/export` (Content-Disposition + `X-Hcc-Content-Type` kept); `all` / `encoding` reserved (not report parameters).
- No migration, no permission seed change, no new read loader (so `_role-reads.json` / RoleReadMatrix untouched).

## Client (03 Prototype, all LF)
- new `src/clients/sync/reports-hub-run.ts` (pure mappers + loaders: numeric / named enums → prototype values, results, export log, base64 files, `mergeHubDefs`), new `src/clients/sync/reportsHubHooks.ts` (hooks on `useServerFetch`), `src/clients/sync/index.ts` (test surface).
- new `src/features/reports/hubServer.tsx` (production-only glue); `HubPage.tsx` (`useHubDefsState`), `ReportView.tsx` (`useReportRun` / `ExportBar` / not-on-server view), `mobileHub.tsx`, `ScheduleTabs.tsx` — every change behind `process.env.HCC_BUILD==='production'`; the prototype branches are the old code.
- GETs are plain reads (no Idempotency-Key needed); no new POST was added (favourite / print-log keep their existing behaviour).

## Tests
- Server unit (`Hcc.Reporting.Tests`): +2 — DEC-061 with a mis-seeded HKP grant (catalogue, run, export, print-log, favourite refused; plain report runs with money + computed columns removed on screen and in the CSV), all-rows run / print-log file formats need export / export-log visibility.
- Server HTTP (`Hcc.Integration.Tests/ReportHubHttpTests.cs`, SQL): catalogue per role (Owner / Cashier / Housekeeper), 403 on a report the role cannot view, DEC-061 403, export 403 and device-file print-log 403 without PERM-RPT-003, print 200, `all=true` shape, base64 xlsx ("PK", server file name), export log Owner = all / Cashier = own / Housekeeper = none.
- Prototype (`tests/sync-hub-server.mjs`, +9): mappers, loaders over the fake server (query incl. `all=true`, blank params dropped, 403 surfaced, no POST on run / export), production-build render of hub, phone hub, export-log tab, ReportView with / without export right and not-on-server.
- Counts: see "Results" below.

## Results
All through `tools\Invoke-Serialized.ps1` (pwsh 7), on this worktree after the last source edit:
- `npx tsc --noEmit`: clean.
- `npm test`: **2849 / 2849** passed (baseline 2840; +9 in `tests/sync-hub-server.mjs`). The first full run caught the prototype bundle carrying the sync layer (an unconditional `serverFile` const in `ReportView.tsx`); fixed by declaring it only in the production branch — prototype `dist/index.html` back to 4513 KB with no `hcc-sync-layer`, `/reports/catalogue`, `/reports/export-log`, `getSyncRuntime` or HTTP-adapter marker ([P5] and [W4-0] bundle-separation tests pass).
- `npm run build:production`: built (4852 KB). `npm run build`: built.
- `dotnet test Hcc.slnx -maxcpucount:1` (SQL Express): **4264 / 4264** passed in 42 projects, 0 skipped (baseline 4261; +2 Hcc.Reporting.Tests, +1 Hcc.Integration.Tests `ReportHubHttpTests`).

## Remainder (not moved; precise)
1. The 22 prototype-only reports (`serverMirror:false`, `side:"prototype"` in `catalogue.json`) have no Hcc.Reporting selector; in production they are listed as "Not on the server yet" and not run. P1 among them: `rpt.admin.audit-log`, `rpt.guests.daily-ops`, `rpt.money.cash-book`, `rpt.money.expenses-cash`, `rpt.money.receipts-deposits`, `rpt.rest.day-summary`, `rpt.rooms.occupancy`, `rpt.stock.stock-sheet`. P2/P3: `rpt.admin.config-history`, `rpt.admin.export-log`, `rpt.admin.report-dictionary`, `rpt.money.cash-out-register`, `rpt.money.closing-records`, `rpt.money.expense-month`, `rpt.rest.closed-bills`, `rpt.stock.consumption`, `rpt.stock.counts`, `rpt.stock.laundry`, `rpt.stock.losses`, `rpt.stock.movements`, `rpt.stock.par`, `rpt.stock.reorder`. Each needs a selector + `AddReport` line + catalogue side flip (cross-check test).
2. Screen paging: the production viewer asks for every row (`all=true`, ≤ 50,000 rows, else the server's RPT_TOO_LARGE message) and pages locally with Show more, as the prototype did. A server-paged viewer (page / pageSize + "load more") is not built.
3. PDF: the server offers PDF for most reports; the hub (both flavours) still shows only Excel / CSV / Print. Adding "Export PDF" in production is one button on `exportHubFile(…,'pdf',…)`.
4. Print uses the server result rendered by the device print model (identical figures); the server's PrintHtml renderer is not used for the screen print.
5. The production catalogue / run / export-log reads are on-demand (not sync loaders), so they are not in `sync/reads.ts` / `_role-reads.json`; the server answers every signed-in role for the catalogue and export log, and filters itself.
6. Not run: APK / phone checks, emulators (none in this lane).

## HANDOFF bullet
Missed A (af2-hub-server, claude/af2-hub-server): the production Report library now runs on the server — catalogue (`GET /reports/catalogue`, DEC-061 enforced on the server), runs (`?all=true`), Excel / CSV from `/export?encoding=base64` (server export log), export-log tab (`GET /reports/export-log`); prototype-only reports labelled "Not on the server yet"; demo flavour unchanged.
