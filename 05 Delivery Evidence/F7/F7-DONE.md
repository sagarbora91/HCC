# F7 DONE: report module (cloud integration, 3 Nov 2026)

Branch `claude/f7` (worktree `/home/user/wt-f7`). It started from main e14c0ead and now includes main 81bd2a8e (F6 hall booking, SCHEMA 14). Contracts: `02 Planning/agents/RPT-ARCHITECTURE.md`, `F7-CONTRACT.md`, `F7_Cloud_Lanes.md`. Lane reports: `F7-R1-DONE.md` … `F7-R8-DONE.md` in this folder.

## Lanes merged
| Lane | What it delivered |
|---|---|
| R1 rpt-engine (Opus) | The engine on both sides: catalogue; parameters by date class; rules V and M; masks; the OP-13 fixed-code computed-column hook; screen, Excel, PDF, CSV and print renderers drawn from one result. Also schedules, runs and the export log (migrations 4400–4405, PERM-RPT-004), the `/reports/*` endpoints, and the golden harness with `tools/pdf-drift.mjs` and `tools/rpt-catalogue.mjs`. The F4 revenue-by-category-tender report was moved onto the engine. |
| R2 hub | Reports hub on the catalogue: search, favourites, parameters, preview, export and print, schedule editor, phone cards. |
| R3 guests + rooms | Movement register, guest register (statutory), Form C register, rate changes and room status, on both sides. Registration-card and day-end layouts in the Lodge look with the HCC letterhead (A16). |
| R4 money-a | Shift report (X / Z), collection detail, credit bills, expense register and expenses by head, on both sides. Deviations are in `F7-R4-DONE.md` lines 21–28. |
| R5 tax | SAC summary, documents issued, GST by bill, invoice register, bill corrections and GST summary (wired to ACC-3), on both sides. Tax reports gate on **PERM-ACC-013 (Owner + Accountant)** per the coordinator ruling. |
| R6 accounting | Trial balance, statements, day book, P&L, balance sheet, cash ratio, ageing and Tally on both sides. ACC goldens are unchanged. Roles follow the catalogue (ACC-013 / ACC-001 / ACC-006). |
| R7 documents | Folio, tax invoice, credit note, receipt, quotation, rate card, payslip, KOT, outlet bill and hand-over pack as Lodge-look documents on the renderer, on both sides. |
| R8 built moves | 22 already-built reports moved onto the engine as adapters, **prototype only**, plus the new `rpt.admin.export-log` report. |

## Reports on the engine
- **47 catalogue reports:**
  - 25 work on both sides: guests 4, rooms 1, money 6, tax 6, accounting 8.
  - 22 are prototype-only (R8's moves): guests 1, rooms 1, money 6, restaurant 2, stock 8, admin 4.
- **Plus 10 document layouts** (R7).
- **Prototype-only reports are marked honestly.** These 22 carry `serverMirror:false`, which `catalogue.json` writes as `"side":"prototype"`. The server cross-check (`EngineTests.Catalogue_json_from_the_prototype_equals_the_server_registry`) compares only the shared reports. It also asserts that the server registers none of the prototype-only ones, so a server mirror added later must drop the flag.
- **Housekeeping and Supervisor stock reports are quantities only (DEC-061).** These roles hold no PERM-STK-004, so the value, cost and charge columns are removed (checked on 3 Nov). Maintenance sees only room status.

## Not moved (still on the old path)
- **Closing-pack renderers:** revenue-tax, daily-collections, closing-report, day-pack.
- **Guests:** arrivals-list, inhouse-list, groups-pickup, group-master-folio.
- **Rooms:** maintenance-report, handover-dashboard.
- **Money:** settles-to, udhaar-recurring-petty, vendor-ageing, ota-commission, ota-payouts, plans-extras.
- **People (all 9):** they need the HR v2 grants.
- **Admin:** system-health, backup-status, legacy-import.
- **Tax:** compliance-register, compliance-dashboard, gstr1, gst-month, tds-deductee.
- **Server mirrors** for R8's 22 reports.
- **Hall reports (F6)** stay on their own HTML print.

**Entry points.** The old per-page buttons are **not switched** to the engine viewer in this pass. Every moved report opens from the Reports hub, and the old exports stay reachable (this is the rollback rule). Switching them needs page-by-page UI checks on the PC.

## PDF and ETP
- **No PDF library is used.** PDFsharp and DocumentFormat.OpenXml are not referenced, under the no-new-package rule. PDF comes from the in-repo `MinimalPdfWriter`, with these limits:
  - PDF 1.4, standard Helvetica, WinAnsi encoding, **English only** (A19).
  - **₹ is printed as "Rs."**
  - Text and rules only; output is deterministic.
- Excel uses the existing `OpenXmlExporter` (server) and `xlsx.ts` (prototype).
- **ETP reuse is a later swap** behind `IReportRenderer`. It needs `E:\ETP\Code` on the owner's PC. The golden checks compare text and page count, not bytes, so a swap keeps them green.

## Verification (cloud, 3 Nov)
- `tsc` clean; catalogue `--check` clean.
- Prototype `npm test` **2731 / 2731**, after the main merge (main baseline 2571 before F6).
  - Hub empty-state test: it used the Tech admin, who now sees config-history. It now uses a Restaurant user, the one role that opens no report.
  - Role-read fixture: updated for the new PERM-RPT-004 grant.
- `npm run build` and `build:production` OK.
- Screen checks:
  - Fit check on `report-hub` for owner, accountant and housekeeper: PASS, 0 failing of 71.
  - Phone at 360 px for owner, accountant, housekeeper and cashier: 0 failing. It checked the hub landing page only, not a report opened with rows.
- `dotnet build` 0 warnings; `Hcc.Reporting.Tests` 81 / 81.
- Server suite (`/home/user/suite.sh`, 42 projects, after the main merge): **4059 passed, 6 failed**. All 6 are the known Linux-only failures: GstIds DPAPI, Legacy absolute-folder, 3 Operations HostingTests and the Stays RoomBoardQuerySqlTests ceiling. Main baseline was 3946 before F6. One F7 fallout was fixed: `OperatorAlertsTests` now lists `ReportScheduleWorker` as an operator category.
- **Migrations:** 4400–4405 were added to the lock with `--accept-out-of-order`, because F6's 4740 merged first. 4406–4449 are unused.
- **SCHEMA stays 14:** F7 adds only the optional `rptX` slice.
- **Windows PC still to do:** the gate `-Wave f7`; fit-check `reports` at every viewport; a print preview on the Tab.

## DEC proposals
- **DEC-261 report engine architecture.** One definition, one selector and one result, drawn by the screen, Excel, PDF / print and CSV renderers, so the figures are identical by construction. Each side has one catalogue, kept in step by `catalogue.json` and its cross-check. The OP-13 computed-column hook is fixed code until the calculation engine lands. Schedules run through the outbox and are held while egress is OFF.
- **DEC-262 catalogue roles win over permission codes.** When a permission code grants more roles than the catalogue entry names, the report uses a code whose seeded grants match the entry. Where no such code exists, the selector checks the roles itself.
- **DEC-263 tax reports are Owner + Accountant only** (PERM-ACC-013): SAC summary, documents issued, GST by bill, invoice register and GST summary. Bill corrections stays on PERM-RPT-002.
- **DEC-264 PDF writer limits.** The in-repo PDF writer prints English only, with "Rs." in place of ₹, in standard fonts. Devanagari, the ₹ glyph and embedded fonts wait for the ETP / PDFsharp swap.

**Decision renumbering (ADM-int, 3 Nov 2026):** the approved Administration panel plan (APR-028) owns DEC-214…221, so this wave's proposals were renumbered.

| Old proposed | New |
|---|---|
| DEC-235 | DEC-261 |
| DEC-236 | DEC-262 |
| DEC-237 | DEC-263 |
| DEC-238 | DEC-264 |
