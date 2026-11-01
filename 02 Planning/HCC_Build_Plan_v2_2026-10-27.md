# HCC Build Plan v2 — multi-agent, multi-model (27 Oct 2026)

Everything that remains after the SCC decisions wave, in execution order, with the agents and models that do it. Supersedes the "Next" lines of HANDOFF Sessions 13–13k and the audit's go-live plan §7 (`05 Delivery Evidence/Audit/HCC_Independent_Audit_2026-10-26.md`), which stay as history. Rulings behind this plan: DEC-140…150 (accounting), **DEC-151** (hall booking), **DEC-152** (split folio), **DEC-153** (report module on the ETP Reporting Engine), **DEC-154** (ETP lanes / briefs / gate workflow; gate + installer pipeline last), **DEC-161…164** (legacy "Lodge" data → wave W1a, added 28 Oct 2026).

## 1. The way of working — borrowed from ETP

ETP shipped eleven releases in October with Claude agents by one method (`E:\ETP\Code\SaagarCC-V6-ETP-Source-and-Report-Engine-605002f\knowledge\07-Operations\Agent Workflow - Lanes, Briefs and Gates.md`). HCC adopts it as is, with HCC names.

| Role | Who | Does |
|---|---|---|
| **Coordinator** | **Fable** (this session) | Writes the wave brief with the file-ownership table and the contract files, launches lanes, reviews DONE files, runs the integrator, updates HANDOFF / CLAUDE.md / Decision Register / memory / graph, writes the gate scripts |
| **Design lane** | **Fable** | The pieces where a wrong design costs a rebuild: the accounting posting contract, the hall-booking spec, the report-module architecture, the Tally import format |
| **Money / statutory lane** | **Opus** | Anything that posts money, computes GST or TDS, touches payroll, settings, SQL migrations or the server's command pipeline |
| **Mechanical lane** | **Sonnet** | Phone and desktop screens from an approved spec, CSV / Excel exports, fixtures, tests, emulator runs, documentation, checklists |
| **Integrator** | **Opus** | Merges a wave's lanes into the wave branch, builds once, runs both suites once, fixes fallout, writes the wave DONE file |
| **Reviewer** | **Opus** (money) / **Sonnet** (UI, docs) | Read-only; writes a findings file; never edits |
| **Owner (Sagar)** | — | Decisions, the elevated gate on the hotel PC, device tests, live installs, the GitHub push |

**Mechanics, every wave:**

1. **Wave branch** `claude/<wave>` from `main`; worktree `E:\Projects\HCC\worktrees\<wave>`; lanes `claude/<wave>-<lane>` in `worktrees\<wave>-<lane>` with the `03 Prototype\node_modules` junction (target `worktrees\fixwave-a\03 Prototype\node_modules`; never remove `fixwave-a`).
2. **Brief** `02 Planning/agents/<WAVE>_Briefs.md`: base commit, the rulebook / spec sections the lane implements, rules, a **file-ownership table** (a lane edits only its files; a change needed elsewhere goes in its DONE file), the migration numbers it owns. Shared interfaces are committed first as a **contract file** the consuming lane copies verbatim.
3. **Heavy jobs through one lock.** ETP's `Invoke-Serialized.ps1` is ported to `tools\Invoke-Serialized.ps1` (lock `Global\HccHeavyJobLock`, exit 75 = busy, retry); every `dotnet build`, `dotnet test`, `npm test`, `npm run build` and Gradle build goes through it. One emulator at a time. Up to 16 agents when the PC is free; no new batch while another wave is running; no 7-minute checkpoint job (work PC rules).
4. **DONE file** per lane `05 Delivery Evidence/<Wave>/<WAVE>-<LANE>-DONE.md`: commits, files, test counts, open items, the HANDOFF bullet text, integration notes. Never filter test output.
5. **Integrator** merges, runs the full prototype suite and the serial server suite (`dotnet test Hcc.slnx -maxcpucount:1`), both web builds, fixes fallout, writes `<WAVE>-DONE.md`.
6. **Gate** `tools\Run-Gate-<wave>.ps1` (ported from ETP's `Run-Gate-` / `Chain-Gate-Then-Installer-` scripts in `E:\ETP\Migration 2026-10-04\`): both suites from a clean clone, golden tests, migration-lock check, secret scan, the end-to-end script on a fresh database, emulator smoke for UI waves. PASS → fast-forward `main` (`git push . HEAD:main` from the wave worktree), tag `wave/<name>`, HANDOFF session block, CLAUDE.md status line, Decision Register, memory, `graphify --update --obsidian` (shrink guard: restore, never force).
7. **Migrations**: each lane owns a numbered block (table below); `node tools/migrations-lock.mjs --add` and a line in `Hcc.slnx` for every one; never edit an applied migration.
8. **Money rules** stand in every brief: integer paise, dated GST master, simulated clock, fictional data, SCC rules in full (DEC-116), Owner-only money settings (DEC-058), housekeeping / maintenance see no money (DEC-061).

## 2. The waves in order

| # | Wave | Branch | Model mix | Lanes | Migrations | Gate adds |
|---|---|---|---|---|---|---|
| W0 | **APK v18** | `claude/apk18` | Sonnet 1 (+ Opus for any money defect) | 1 | — | emulator phone + tablet report; physical-tab prompt |
| W1 | **ACC-1 Chart and parties** | `claude/acc1` | Fable design · Opus 4 · Sonnet 2 | 6 | 4200–4229 | rulebook §8 golden month (opening + ledgers + six-column TB) |
| W1a | **LG Legacy data** (old "Lodge" software, DEC-161…164) | `claude/lg` | Fable design · Opus 2 · Sonnet 3 | 5 | 4600–4619 | dry run against the June 2026 backups reconciles per FY (bills, room GST, receipts); 20 known guests reviewed by the Owner |
| W2 | **ACC-2 Purchases, manual vouchers, GST, TDS** | `claude/acc2` | Opus 5 · Sonnet 2 | 7 | 4230–4259 | golden month complete; oracle run in ERPNext / GNUKhata |
| W2a | **FIT Fit-to-screen desktop / tablet layout** (DEC-194…180, added 1 Nov 2026) | `claude/fit` | Opus 3 (shell, people lane, integrator) · Sonnet 7 | 2 + 7 | — | `tools/fit-screen-check.mjs`: no page scroll at five viewports × nine roles × every page and sub-screen; fonts equal the baseline except the 22 px heading; phone layout unchanged |
| W3 | **ACC-3 P&L, ageing, exports, Tally XML** | `claude/acc3` | Fable (Tally format) · Opus 2 · Sonnet 3 | 5 | 4260–4279 | Tally import of the golden month into a fresh Tally company |
| W4 | **ACC-4 Year lock, bank reconciliation** | `claude/acc4` | Opus 2 · Sonnet 1 | 3 | 4280–4299 | statement-import fixture reconciles to zero |
| W5 | **HB Hall booking (module 27)** | `claude/hb` | Fable spec → owner approval → Opus 3 · Sonnet 3 | 6 | 4300–4349 | banquet golden event: enquiry → quote → advance → event bill → accounting |
| W6 | **SF Split folio** | `claude/sf` | Opus 2 · Sonnet 1 | 3 | 4350–4369 | company-pays-room / guest-pays-extras golden stay; GST per invoice |
| W7 | **RPT Report module** | `claude/rpt` | Fable architecture · Opus 2 · Sonnet 3 | 5 | 4400–4449 | every catalogue report renders on screen, Excel and PDF with the same figures |
| W8 | **GL Go-live stand-ins** | `claude/golive` | Opus 4 · Sonnet 5 | 9 | 4500–4599 | ETP-reuse list closed; provider adapters behind switches; Form C and ECR files if chosen |
| W9 | **FINAL Release pipeline** | `claude/release-1.0` | Fable · Opus 2 · Sonnet 2 · **Owner** | 4 + owner sessions | — | ETP gate chain → `HCC-Setup-1.0.0-x64.exe` with SQL bootstrap; VM stage; hotel PC |

Rough wall-clock on this PC, one wave at a time: W0 1 day (after the tablet results) · W1–W4 5–6 days · W1a 2 days · W5 2 days (+ a day for the spec and your approval) · W6 1 day · W7 2 days · W8 3–4 days · W9 2–3 days plus the owner's sessions. About three to four working weeks of agent time if nothing waits on a decision.

**W2a FIT (1 Nov 2026):** runs after W2 ACC-2 and W1a LG are on `main` (it touches every desktop page) and **before W0 APK v18**, so the tablet test sees the new layout. Contract `02 Planning/agents/FIT-LAYOUT-CONTRACT.md`, briefs `FIT_Briefs.md`; the "before" measurement is `05 Delivery Evidence/Fit/fit-check-before-*.md`. About two days on this PC.

**What can overlap:** W0 is one lane and may run beside W1. The HB spec (W5, Fable only, no agents) is written while W2–W4 run, so your approval is in hand before its lanes start. W1a may run any time after W1 and before W8 (its cut-over evening is an owner session in W9); its extractor lane only reads the compacted MDBs and may run beside an ACC wave when RAM allows. Nothing else overlaps: one wave's batch at a time on this PC.

## 3. Lane plans

### W0 — APK v18 (`claude/apk18`)
Gated on your v17 Galaxy Tab results in `05 Delivery Evidence/Acceptance/device-v17/`. Lane **apk18-build** (Sonnet): log D17-n defects, fix the S4 list (V17-2…4, V15-4, V16-3) and anything the tab showed, confirm V17-1 (window insets after rotation) on the physical tab, bump versionCode 18, build with JDK 21, run the v12 emulator checklist on phone and tablet AVDs, write `APK_v18_Emulator_Report.md` and the tablet test prompt. Any defect touching money goes to an Opus micro-lane.

### W1 — ACC-1 Chart and parties (`claude/acc1`)
Rulebook §3, §5 A17, §7.2–7.3, §G2. Contract first (Fable): `ACC1-POSTING-CONTRACT.md` — the posting table (voucher → entry → ledger, date, paise), ledger codes, party sub-ledger ids `code/party-id`, the trial-balance row shape, the opening-entry shape.
- **acc1-chart** (Opus): group master (Tally's 28 groups), chart as an Owner-only master with system ledgers, new ledgers of §3.2, mapping keys re-pointed (F&B → 4200, attrition → 4300, cash over → 4950, input GST → 1310/1320/1330), prototype + server, migrations 4200–4209.
- **acc1-parties** (Opus): sub-ledgers created from companies, OTAs, Udhaar customers, vendors, bank accounts, staff; retire rules; existing vouchers migrated to sub-ledgers; Udhaar credit raised posts A17 (DEC-144); migrations 4210–4219.
- **acc1-opening** (Opus): the DEC-139 / DEC-148 opening-balance screen (Manager enters, Owner approves, 1400 difference, lock refuses while pending), import path kept; migration 4220.
- **acc1-statements** (Opus): ledger / party statement with running balance, six-column trial balance (control summary or party detail), day book; server queries + prototype selectors.
- **acc1-ui** (Sonnet): desktop and phone screens for the chart, parties, statements and trial balance per the existing Accounting page patterns; CSV export.
- **acc1-tests** (Sonnet): `tests/stream-accounting-rulebook.mjs` and `Hcc.Accounting.Tests/RulebookMonthTests.cs` from rulebook §8 (opening, T1–T19, closing TB), fixture `ACC-RULEBOOK` scenario.
- **tools lane** (Sonnet, first day): port `Invoke-Serialized.ps1`, `Run-Gate-` and `Start-Gate-` scripts from ETP to `tools\`; a HANDOFF line on how agents call them.

### W1a — LG Legacy data (`claude/lg`) — DEC-161…164
Source: `02 Planning/HCC_Legacy_Data_Mapping_v1.md` (decided §8) and the profile `E:\Projects\HCC\Existing Software\DB-PROFILE\` (compacted `LodgeYY-YY.mdb`, ten FYs, 251 MB; password hard-coded in the legacy `Forms\Forms\Module1.bas`; **MDBs are never committed**, extractions stay in scratch). Contract first (Fable): `LG-HISTORY-CONTRACT.md` — the `lgc.*` row shapes, the identity rules (phone key = last 10 digits, name fold, alias rule), the reconciliation totals per FY the importer must match.
- **lg-extract** (Sonnet): `tools/legacy/extract.py` (ACE/ODBC, run with `-I`) → masters CSVs in the template layouts (`guests.csv` without ID numbers, `rooms.csv` 36 real rooms, `room-types.csv`, `id-types.csv`, `booking-sources.csv`, `companies.csv` for shared GSTINs), `lgc_*.csv` per FY, `cutover_*.csv/xlsx` (in-house list, unadjusted advances, receivables, `PaidOut` for the CA), the reconciliation totals file.
- **lg-schema** (Opus): migration 4600 `lgc` schema + `gst.guest.legacy_guest_id` + `gst.guest.dob`; views for Customers 360; lock-file and `Hcc.slnx` entries.
- **lg-import** (Opus): Owner-only dry-run legacy importer (server command, result JSON): guest part 2 (sealed ID numbers per DEC-068/070, `legacy_guest_id`, `dob`, photo / ID-scan copy into `doc.*`), `lgc.*` load keyed by `legacy_fy` (re-runnable until a business day is closed), reconciliation against the extractor totals, refuses any CSV with an ID-number column (DEC-MA-8b).
- **lg-ui** (Sonnet): Customers 360 and the guest profile read `lgc` (stays, nights, lifetime, first / last stay, "legacy" badge, old bill numbers); phone-less legacy guests group under the name key.
- **lg-tests** (Sonnet): extractor unit tests on a fixture CSV set shaped like the MDB tables, importer tests (identity, dedup, Title Case, OTA tags, GST 12 → 5 % split on 22 Sep 2025, FY-boundary de-dup), prototype stream tests for the legacy badge; the dry-run script for this PC.
- **Owner:** rate card (OP-18) before the masters import; ask the CA about invoice continuity (OP-19); review the 20-guest sample and the companies list.

### W2 — ACC-2 Purchases, manual vouchers, GST, TDS (`claude/acc2`)
Rulebook §4, §5 B5–B12, C1–C10, D5–D7, E1–E5, §6.
- **acc2-purchase** (Opus): purchase voucher with ITC split (C1, C5), vendor payment allocated oldest-first (C3), vendor advance (C4), fixed-asset purchase to a block (C10), RCM categories flag and entry (C8, DEC-145); migrations 4230–4239.
- **acc2-manual** (Opus): manual Journal / Contra / Payment / Receipt with maker-checker (Accountant enters, Owner approves), reasons, audit, day-lock respect; migration 4240.
- **acc2-tds** (Opus): TDS-rate table (dated, Owner-editable), hold-back on rent / contractor / professional payments (C9, DEC-146), deductee-wise report, remittance entry (E5); migration 4245.
- **acc2-gst** (Opus): month-end set-off E1 per s.49(5) / 49A / 49B, cash-ledger and challan entries (E2–E4), the "GST on advances" switch (default off, DEC-142) with A6 / A6a / A6b behind it, month lock G1; migration 4250.
- **acc2-flows** (Opus): OTA payout from statement (A13, DEC-150), company receipt net of TDS (A9), Udhaar reversal (A17b), bad debt (A18), owner capital / drawings / loans (B5, B6, B11), cash-limit guards (B9, B10), statutory remittances (D5); migration 4255.
- **acc2-ui** (Sonnet): the voucher entry screens (desktop + phone sheet), approval queue, purchase and payment screens on the SCC expense patterns.
- **acc2-tests** (Sonnet): golden month extended (challan, TDS case, RCM case, manual journal), permission matrix tests (§6), guard tests.
- **Oracle** (coordinator after integration): the golden month posted into ERPNext or GNUKhata; trial balance and P&L compared; result in the DONE file.

### W3 — ACC-3 P&L, ageing, exports, Tally XML (`claude/acc3`)
Rulebook §7.4–7.9, §10 ACC-3. Contract first (Fable): `ACC3-TALLY-CONTRACT.md` — the Tally XML envelope for groups, ledger masters and vouchers, REMOTEID / GUID rules as already built, company name, tested by importing into a fresh Tally company on the owner's PC.
- **acc3-pl** (Opus): P&L in the ICAI non-corporate format with the Tally-group view, optional balance-sheet view, cash-ratio report.
- **acc3-ageing** (Opus): receivables / payables outstanding with 0–30 / 31–60 / 61–90 / 90+ ageing, bill-wise allocation; GST summary tied to `gstr1Summary` and 3B figures.
- **acc3-tally** (Sonnet, on Fable's contract): Tally XML masters + vouchers export, ETP's `VoucherExport` / Tally pieces reused where they fit (provenance in `third-party/source-reuse.json`); ETP-reuse spot 21 closed.
- **acc3-exports** (Sonnet): CSV + Excel per ledger and per party, bulk zip, on the prototype `xlsx.ts` writer and the server's OpenXML exporter (ETP-reuse spots 22, 19).
- **acc3-ui** (Sonnet): report screens, date ranges, party filters, phone cards.

### W4 — ACC-4 Year lock and bank (`claude/acc4`)
- **acc4-year** (Opus): year lock, opening carry-forward of balance-sheet ledgers (G3 step 5), reopen rules (G4), DEC-077 extended to a year; migration 4280.
- **acc4-bank** (Opus): bank reconciliation with statement import (CSV of the bank's layout), cleared dates, UPI / card clearing settlement (B8), reconciliation report; migration 4285.
- **acc4-tests** (Sonnet): statement fixture that reconciles to zero; year-lock tests.

### W5 — HB Hall booking, module 27 (`claude/hb`) — DEC-151
**Spec first** (Fable, no agents): `02 Planning/modules/HCC_Module_27_Hall_Booking_Spec.md` — halls as a master (capacity, setups, hourly / day rates, GST 18 % class), **enquiry** (date, hall, pax, occasion, source, follow-up), **quotation** (hall + menu + extras, per-plate pricing, GST, versions, validity, PDF via the report module), **confirmation with advance** (receipt into Advance deposits as A6; DEC-142 applies), **menus** (packages per plate built on the POS menu master, custom items), **event-day billing** (actual pax, extras, hall folio → GST invoice in the hotel series, advance applied, balance by any tender or company credit, Udhaar allowed per DEC-144), calendar with the room board (hall availability, blocks), roles (Reception enquires and quotes, Manager confirms discounts within DEC-063 limits, Owner approves beyond), accounting entries (4400 Banquet & events revenue added to the chart; attrition / cancellation to 4300). **You approve the spec before lanes start.**
- **hb-domain** (Opus): `src/domain/streams/hall.ts`, handlers, fixtures, state slice `s.hallX`, SCHEMA bump; migrations 4300–4319.
- **hb-billing** (Opus): quotation → confirmation → event bill → invoice → accounting; advances and refunds; discounts per role.
- **hb-server** (Opus): `Hcc.Application/Hall`, SQL store, endpoints, accounting sources.
- **hb-ui-desktop** (Sonnet): enquiry board, quotation editor, menu builder, calendar, event-day screen.
- **hb-ui-phone** (Sonnet): phone versions per the mobile rules (48 px targets, cards, sheets).
- **hb-tests** (Sonnet): golden event (enquiry → quote v2 → advance ₹20,000 → event for 150 pax → bill with GST → balance by company credit) in both suites; navmap / permission tests.

### W6 — SF Split folio (`claude/sf`) — DEC-152
- **sf-domain** (Opus): a stay may hold **several folios**, each with a payer (guest, company account, group master, Udhaar customer) and a routing rule per charge kind (room nights → company, extras → guest, or any split); charges post to the routed folio; each folio settles and invoices on its own in the same invoice series; credit notes per folio; accounting unchanged per folio; migration 4350–4359, SCHEMA bump.
- **sf-server** (Opus): billing commands and queries, night run posts to the routed folio, invoice numbering per folio.
- **sf-ui** (Sonnet): folio switcher on the stay (desktop tab strip, phone chips), routing editor at check-in, check-out settles all folios.
- Tests in the money lane: golden stay "Deccan Agro pays rooms, guest pays extras", two invoices, two GST documents.

### W7 — RPT Report module (`claude/rpt`) — DEC-153
Architecture first (Fable): `RPT-ARCHITECTURE.md` — a **report catalogue** (id, name, area, parameters, permission, data query, layout), one renderer for screen / Excel / PDF, scheduled runs with e-mail or WhatsApp delivery through the messaging module.
**Borrowed from the ETP Reporting Engine** (`E:\ETP\Code\SaagarCC-V6-ETP-Source-and-Report-Engine-605002f\src\Etp.Reporting.Reporting`, DEC-054, provenance recorded): `ReportDefinition` and `ReportExecutionContracts` (the catalogue shape), `OpenXmlReportExporter` (Excel), `SimplePdfReportExporter` / `VisualReportPdfDocument` (PDFsharp PDF), `ReportPackExporters` / `ReportPackageService` (report packs), `ManagementMetricEngine` (computed metrics). The **Excel-like calculation engine** you mentioned is not in ETP's current code (checked 27 Oct: `Etp.Reporting.Reporting` has the exporters and the metric engine, no formula engine) — **OP-13:** tell me where it lives or whether it is still being built; the catalogue gets a "computed column" hook for it either way.
- **rpt-engine** (Opus): the catalogue, parameter binding, permission filter, the ETP exporters wired into `Hcc.Reporting`; ETP-reuse spots 9, 11, 19, 20, 24, 25, 30, 32 closed (folio / invoice / credit-note PDF, day pack, cash-count sheet, expense register, CA pack, KOT and bill templates, handover pack, payslip PDF).
- **rpt-catalogue** (Opus): the first forty reports: front desk (arrivals, departures, in-house, occupancy, ADR, source mix), money (collections, cash book, bank book, GST summary, trial balance, P&L, ageing, expenses by head, budgets), POS (sales by item / outlet / hour, voids), housekeeping / maintenance (room status, task SLAs, work orders), stock (consumption, par, GRN), HR (attendance, payroll register, leave balances, statutory statements, PF ECR / ESIC files **if chosen**), compliance (calendar, licence expiry), hall booking (enquiry funnel, events, hall occupancy).
- **rpt-ui** (Sonnet): Reports hub with search, favourites, parameters, preview, export buttons, schedule editor; phone cards.
- **rpt-schedule** (Sonnet): scheduled runs through the outbox worker; delivery through messaging (stand-ins until W8).
- **rpt-tests** (Sonnet): every catalogue report renders on screen, Excel and PDF with identical figures from the fixture; golden PDFs with a drift check.

### W8 — GL Go-live stand-ins (`claude/golive`)
The `08 Production/ETP_Reuse_Pending.md` list (34 spots) closed, plus the provider adapters behind switches.
- **gl-platform** (Opus): spots 1–8, 17–18, 23, 29 (build props, connection policy, backup gate, migration runner alignment, service hosting, background-job hosting, retry / back-off) aligned with ETP.
- **gl-backup** (Opus): spots 12–15 (backup script naming / retention / failure mail, restore drill report, Task Scheduler registration, certificate / service / database provisioning) aligned with ETP's.
- **gl-messaging** (Sonnet): real WhatsApp / SMS / e-mail adapters behind `IMessageGateway` (provider chosen by you; keys in config, never in code), spots 29, 33.
- **gl-ocr** (Sonnet): a real ID OCR engine behind `IIdOcrEngine` (provider chosen by you), spot 34.
- **gl-channel** (Opus): channel-manager connector behind the OTA port (partner API chosen by you), rate / inventory push, booking pull; spot 31.
- **gl-stock** (Sonnet): spots 26–28 alignment.
- **gl-formc** (Sonnet, optional): Form C for foreign guests (FRRO portal data file + register), passport fields complete.
- **gl-ecr** (Sonnet, optional): PF ECR and ESIC contribution files from the payroll register (needs the CA's confirmed layouts).
- **gl-e2e** (Sonnet): the end-to-end script against a fresh database extended to accounting, hall booking, split folio and reports; speed test on a year of data.

### W9 — FINAL Release pipeline (`claude/release-1.0`) — DEC-154, last
Borrowed whole from ETP: `scripts\build-windows-release.ps1` → `build-windows-installer.ps1` → `installer\EtpReportingEngine.iss` (Inno Setup 6 with the SQL Server Express bootstrap task, scheduled-task registration, upgrade and uninstall rules, `release.json` receipt, SHA-256), the gate chain `Start-Gate` / `Run-Gate` / `Rerun-Gate` / `Build-Installer` / `Publish-Installer` / `Chain-Gate-Then-Installer`, and the SQL payload folder (`E:\ETP\Installers\SQL payload for the ETP installer (2025 + ODBC 17)`).
- **rel-installer** (Opus): `deployment\windows\installer\Hcc.iss` rebuilt on ETP's template with HCC branding, the Windows service, certificate import, firewall, Task Scheduler folder `\HCC`, SQL Express bootstrap (offline payload + winget fallback), upgrade keeps settings, uninstall never removes SQL / databases / backups; `Build-HccInstaller.ps1`; version stamping from `Directory.Build.props`.
- **rel-sql** (Opus): database packaging: `Initialize-HccDatabase.ps1` run by the installer, migration journal check, masters import step with the 39 templates, demo-data switch off in Production, schema-version guard.
- **rel-gate** (Sonnet): HCC gate chain scripts in `tools\` with `$repo` = the release worktree; the elevated gate runs the integration suite, both builds, the e2e script, the secret scan, then builds and publishes `HCC-Setup-1.0.0-x64.exe` + `SHA256SUMS.txt` to `E:\Projects\HCC\Installers\`.
- **rel-docs** (Sonnet): `INSTALL.md`, operator runbook, downtime procedure (DEC-081), restore-drill runbook, the APK v18 release-signed build with the real hotel CA.
- **Owner sessions:** (1) run `Chain-Gate-Then-Installer-1.0.0.ps1` elevated; (2) the one-hour **keys session** on the hotel PC (DEC-087: release keystore, hotel CA, guest-ID and backup keys on two USBs, BitLocker); (3) VM stage, then the **hotel-PC install rehearsal** and **restore drill**; (4) physical-device tests and the **staff trial** with fictional data; (5) the **GitHub push** after the secret scan (make the repository private first, per DEC-087, or rule that public is intended); (6) the **legacy cut-over evening** on the hotel PC (DEC-164: live Lodge PC stopped after the last check-out → extractor → masters import → legacy importer → in-house guests re-entered → opening balances → number series → first day close).

## 4. Model policy in one line each

- **Fable**: coordinator; the four contracts (posting, Tally, hall-booking spec, report architecture); final review of every money wave before the gate.
- **Opus**: everything that moves money, tax, payroll or settings; SQL migrations; server command pipeline; integrators; money reviewers.
- **Sonnet**: screens from an approved spec, exports, fixtures, tests, emulator and device checklists, docs, gate scripts, provider adapters that only move bytes.
- When unsure, Opus. A Sonnet lane that discovers it must touch a money rule stops and writes it in its DONE file for the coordinator.

## 5. Open points for this plan

| # | Point | Need |
|---|---|---|
| OP-13 | The **Excel-like calculation engine** for the report module | **Answered 27 Oct:** still being built, about 7 days (≈ 3 Nov 2026); W7 keeps the computed-column hook and takes the engine when it lands | Owner |
| OP-14 | **Form C** for foreign guests and **PF ECR / ESIC files** in W8: build or leave? | Owner |
| OP-15 | Providers for W8: WhatsApp / SMS gateway, ID OCR, channel manager — which vendors, and who signs the contracts | Owner |
| OP-16 | GitHub repository **private** before the push (DEC-087 says private; `sagarbora91/HCC` is public today) | Owner |
| OP-17 | Hall-booking spec approval (W5) — written while W2–W4 run | Owner |
| OP-18 | ~~Rack-rate card~~ **Closed 1 Nov 2026 (DEC-198):** owner's card (GST-inclusive, room GST 18 % — CA to confirm), import files in `Existing Software\DB-PROFILE\rate-card\` | Owner (done) |
| OP-19 | ~~Ask the CA whether the HCC invoice series must continue the legacy bill numbers~~ **Closed 1 Nov 2026 (DEC-197):** fresh series `HPE/2627/0001` from go-live; legacy bills keep their numbers; both series in GSTR-1 Table 13 | Owner (done) |

## 6. What you do, when

| When | You |
|---|---|
| Now | Run the v17 tablet test; give the go for W1 |
| During W2–W4 | Read and approve the hall-booking spec (W5); answer OP-13…OP-16 |
| Before W1a's dry run | Hand over the rate card (OP-18); ask the CA about invoice continuity (OP-19); then review the 20-guest sample and the companies list |
| After W3 | Import the golden month's Tally XML into a fresh Tally company on your PC and confirm it opens cleanly |
| Before W8 | Choose the three providers (OP-15) |
| W9 | The elevated gate, the keys session, the install rehearsal, the legacy cut-over evening (DEC-164), the staff trial, the push |
