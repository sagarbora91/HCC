# F7 contract — report module (3 Nov 2026)

Base `claude/f7` from main e14c0ead (F1–F5). Architecture: `RPT-ARCHITECTURE.md`. Lanes: `F7_Cloud_Lanes.md`. Catalogue rules = `HCC_Report_Catalogue_v1.md` §1 (they win over this file on any rule; this file wins on names and shapes). SCHEMA stays **13** (one new optional slice `s.rptX`; plan guessed 14 — bump only if a lane changes an existing shape, and say so in its DONE).

## 1. Types (declared by the setup commit — lanes import, never redeclare)

**Prototype** `03 Prototype/src/domain/reports/types.ts`:
`RptArea`, `RptStatus`, `RptDateClass` (`'D-DAY'|'D-S'|'D-B'|'D-F'|'D-M'|'D-FY'|'D-ASOF'|'D-NOW'|'D-FWD'`), `RptFormat` (`'screen'|'xlsx'|'pdf'|'csv'|'zip'|'xml'`), `RptPhone`, `RptColType` (`'text'|'int'|'money'|'date'|'pct'|'bool'`), `RptMask` (`'guestName'|'idNumber'|'bankAccount'`), `RptParam`, `RptColumn` (`key,label,type,money,mask?,computed?:{expr,deps}`), `RptDefinition<P>` (§2 of the architecture: `id,title,area,priority,status,moneyFlag,moneyGate,viewPerms,deskPerms?,exportPerm?,dateClass?,params,columns,dateBasis,formats,phone,layout?,legacyIds?,select`), `RptRow` (`{kind:'row'|'subtotal'|'total';cells:Record<string,string|number|null>;group?:string;drill?:string}`), `RptResult` (`id,params,columns,rows,about:{runAt,user,dateBasis,provisional,legacy,from?,to?},page?:{index,size,total}`), `RptPrintModel` (`orientation,header:{property,title,params:[string,string][],runAt,user},pages:{tables:{columns,rows}[]}[],footer:{basis}`), `RptSchedule`, `RptRun`, `RptExportLogEntry`, `RptState` (`favourites:Record<uid,string[]>;schedules;runs;exportLog`).
`State.rptX?: RptState` (types.ts, `// F7` block). Selector signature: `select(s:State, uid:string, p:BoundParams) => RptResult` (pure; uses `s.clock`).

**Server** `08 Production/src/Hcc.Application/Reporting/Rpt/ReportContracts.cs` (namespace `Hcc.Application.Reporting.Rpt`): records `ReportColumn`, `ReportParam`, `ReportDefinition`, `ReportRow`, `ReportResult`, `ReportAbout`, `RenderedFile`, `ReportScheduleRecord`, `ReportRunRecord`, `ReportExportLogRecord`; enums `ReportDateClass`, `ReportFormat`, `ReportPhone`; interfaces `IReportSelector` (`Definition`, `SelectAsync(ReportCaller, BoundParams, ct)`), `IReportRenderer` (`Format`, `Render(ReportResult, RenderContext)`), `IReportCatalogue`, `IComputedColumnEngine`, `IReportScheduleStore`, `IReportRunStore`, `IReportExportLog`. Money = `long` paise. rpt-engine may add members; may not rename.

## 2. Engine functions (rpt-engine implements; others call)

Prototype `src/domain/reports/engine.ts`: `registerReport`, `reportCatalogue(s,uid)`, `bindParams(def,raw,s)`, `runReport(s,uid,id,raw)` (bind → permission → select → mask → computed → page), `toXlsx(result)` (→ `XSheet[]` incl. About), `toPrintModel(result)`, `printHtml(model)`, `toCsv(result)`, `computeColumns`, `canViewReport` / `canExportReport`. Commands (handlers `src/services/mock/handlers/reports.ts`): `RPT_FAV_TOGGLE`, `RPT_SCHEDULE_SAVE`, `RPT_SCHEDULE_DELETE`, `RPT_RUN_DUE` (mock outbox tick; delivery `held` while egress OFF), `RPT_LOG` (print / export row). Errors: `FORBIDDEN`, `VALIDATION`, `RPT_TOO_LARGE`, `NOT_FOUND`.

Server `Hcc.Reporting`: `ReportCatalogue`, `ParamBinder`, `ReportRunner`, `MoneyMasker`, `XlsxRenderer` (on `OpenXmlExporter`), `PdfRenderer` (`MinimalPdfWriter`), `CsvRenderer`, `PrintHtmlRenderer`, `FixedCodeEngine`, `ScheduleWorker` (outbox consumer), `AddReportingModule()` + `AddReport<T>()`.

## 3. Server tables and endpoints

| Migration | Lane | Content |
|---|---|---|
| 4400 | rpt-engine | `rpt` schema; `rpt.catalogue_override` (id, enabled, owner note — catalogue itself is code) |
| 4401 | rpt-engine | `rpt.favourite` (principal, report id) |
| 4402 | rpt-engine | `rpt.schedule` (§5 of architecture) |
| 4403 | rpt-engine | `rpt.run` |
| 4404 | rpt-engine | `rpt.export_log` |
| 4405 | rpt-engine | RBAC seed: PERM-RPT-001/002/003 rows exactly per catalogue §1.3 + `report.schedule` |
| 4406–4419 | rpt-engine | reserve |
| 4420–4421 | batch 1 guests | as needed (e.g. registration-card layout setting) |
| 4422–4423 | batch 1 rooms | |
| 4424–4427 | batch 2 money | |
| 4428–4431 | batch 2 tax | |
| 4432–4433 | batch 3 accounting | |
| 4434–4435 | batch 3 documents | |
| 4436–4437 | batch 3 people / stock / rest / admin | |
| 4438–4444 | spare (ask the integrator) | |
| 4445–4449 | f7-int | fallout |

Every migration: `node tools/migrations-lock.mjs --add`. Endpoints (`Hcc.Api/Endpoints/ReportEndpoints.cs`, rpt-engine): `GET /reports/catalogue`, `GET /reports/{id}`, `GET /reports/{id}/export?format=`, `POST /reports/{id}/print-log`, `GET|POST /reports/schedules`, `PUT|DELETE /reports/schedules/{sid}`, `GET /reports/runs`, `POST /reports/favourites/{id}` (toggle).

## 4. P1 reports — 3 batches (planned 18 + F4's built one)

| Batch | Lane | Reports | Notes |
|---|---|---|---|
| **1** (phase 2a) | R3 guests+rooms | `rpt.guests.movement-register`, `rpt.guests.guest-register` (statutory), `rpt.guests.form-c-register`, `rpt.guests.rate-changes`, `rpt.rooms.room-status` + day-end and registration-card layouts (A16 Lodge look + HCC letterhead) | desk perms (rule V.5); ID numbers masked in every format |
| **1** | R4 money-a | `rpt.money.shift-report` (X / Z + hand-over), `rpt.money.collection-detail`, `rpt.money.credit-bills`, `rpt.money.expense-register` (partial → built), `rpt.money.expenses-by-head` | Cashier own shift view + print, no export |
| **2** (phase 2b) | R5 tax | `rpt.tax.sac-summary`, `rpt.tax.documents-issued` (both series, DEC-197), `rpt.tax.gst-by-bill`, `rpt.tax.invoice-register`, `rpt.tax.bill-corrections`, `rpt.tax.gst-summary` (wire ACC-3) | figures = `gstr1Summary`; PERM-INV-001 |
| **2** | R6 acc | `rpt.acc.tally-xml` (ACC-006, xml product), `rpt.acc.ageing` (wire ACC-3) + P&L / BS / TB / statements onto the renderer | ACC goldens unchanged |
| **3** (phase 3) | R7 documents | invoice / credit note / folio / receipt / quotation / rate card / payslip / KOT / hand-over pack as `layout:'document'` (ETP spots 9, 11, 19, 20, 25, 30, 32, 38); letterhead + QR text, no IRN | own legal layouts |
| **3** | R8 built moves | rooms (5), people (13), stock (9), restaurant (2), admin (8), remaining money / guests built + partial → renderer; `rpt.admin.export-log` report on `rpt.export_log` | adapter selectors (§5) |

`rpt.money.revenue-by-category-tender` moves in phase 1 as **the engine's demo report** (rpt-engine).

## 5. Moving the built reports (≈ 62 built + 13 partial + 7 ACC-3)

- One adapter selector per report wrapping the existing function — no new figures. Mapping: closing `ReportPack` tabs (`daily-ops`, `occupancy`, `daily-collections`, `revenue-tax`, `receipts-deposits`, `expenses-cash`, `closing-report`, `day-pack`, …) via `packTabSelector(tabId)`; ACC-3 views via `ReportExports.*` tables → `ReportResult`; module reports (stock, people, POS, admin, maintenance, handover, OTA, compliance) via their existing domain selectors.
- Owner of each move: R6 = all `rpt.acc.*` + `rpt.tax.gst-*` / `tds-deductee`; R4 = `rpt.money.*` money reports touched in batch 1 plus `daily-collections`, `revenue-tax`; R8 = everything else.
- Entry-point switch and removal of old per-page export code = f7-int only, after the report's golden passes. Failing → stays old, listed in `RPT-DONE.md`.

## 6. Append blocks (`// F7-<LANE>` … `// F7-<LANE> end`)

| File | Blocks |
|---|---|
| `03 Prototype/src/domain/types.ts` | `F7` (setup: `rptX`) |
| `03 Prototype/src/services/mock/handlers/index.ts` | `F7-R1` (engine handlers), `F7-RPT` (report lanes register via `src/domain/reports/register.ts`, not here) |
| `03 Prototype/src/services/mock/index.ts` | `F7-R1` |
| `03 Prototype/src/domain/reports/register.ts` | `F7-R3` … `F7-R8` (one import line each) |
| `03 Prototype/src/app/subscreens.ts` | `F7-R2` |
| `08 Production/src/Hcc.Api/HccComposition.cs` | `F7-R1`, `F7-RPT` (report lanes add `services.AddReport<…>()` lines inside, one per line) |
| `08 Production/Hcc.slnx` | `F7-R1` (Hcc.Reporting + Hcc.Reporting.Tests) |
| `02 Planning/HCC_Report_Catalogue_v1.md` | `<!-- F7-<LANE> -->` status lines at the end (integrator sorts) |

## 7. Tests each lane owns

R1 `Hcc.Reporting.Tests` (new) + `tests/stream-rpt-engine.mjs`; R2 `tests/stream-rpt-hub.mjs` (+ fit-check `reports` on the PC); goldens `tests/rpt-golden/` (R1 builds the harness, report lanes add goldens); each report: three-way equality, role matrix, date-class edges, provisional, CSV neutralisation.
