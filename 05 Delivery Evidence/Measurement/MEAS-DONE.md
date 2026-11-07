# MEAS (F12) DONE: f12-int final integration

Integrator f12-int (Opus 5.5) worked in `E:\Projects\HCC\worktrees\meas` on branch `claude/meas`. It started at 6dbd8e77. Two helper lanes, f12-int-cap and f12-int-an, worked in the same worktree; they did not commit, and the integrator committed their work.
- Nothing is pushed.
- **Local `main` was NOT fast-forwarded and the gate was NOT run.** The legacy golden test has mismatches (section 5). The brief says a mismatch stops the integrator: the mismatches are written down below, not papered over.
- Heavy jobs ran one at a time through `tools/Invoke-Serialized.ps1 -Label meas-int`.
- Logs are in `.scratch/int/`, `.scratch/cap/`, `.scratch/an/` and `.scratch/golden/lgc-1/`. They are not committed.

## Commits (on claude/meas, after 6dbd8e77)
- c6e7ca4d: step 1 (capture-side server), plus review fixes H1, H2, H3, H4, M4 and M8.
- 166419bc: step 2, server side (analytics loader, pack facts, digest source, target reads).
- The final commit of this run: prototype wiring (step 2, prototype side, and step 3), the receivables and in-service rules found by the golden test, the golden extract mode, this file and the ledger.

## Counts before and after

| Check | Before (6dbd8e77) | After |
|---|---|---|
| `npx tsc --noEmit` | clean | clean |
| `npm test` (03 Prototype) | 3133/3133 | 3141 tests: full run 3140/3141, the one failure fixed and its file re-run green (RESULTS) |
| `dotnet build src/Hcc.Api` | 0 errors, 0 warnings | 0 errors, 0 warnings |
| Hcc.Meas.Tests | 86 | 97 (+8 capture: service rules plus one SQL round trip; +3 analytics loader on SQL) |
| Hcc.Reporting.Tests | 117 | 117 |
| Hcc.Configuration.Tests | 47 (group count 13) | 47 (group count 14, `measure`) |
| Hcc.Closing.Tests / Hcc.Counter.Tests (digest) | not run | 83 / 89 |
| Full `dotnet test Hcc.slnx -maxcpucount:1` | not run | **not run** (it is part of the gate) |
| Legacy golden, extract mode | never run | **FAIL**: 48 lines ok, 32 mismatches (section 5) |

## What was built

### Step 1: capture-side server
- **Capture routes.** All 23 MEAS capture commands have `POST /api/v1/commands/<Name>` routes. Each route carries `.RequireHccMeasurement(key)`.
  - Files: `Hcc.Contracts/Meas`, `Hcc.Application/Meas/MeasCapturePorts.cs`, `Hcc.Operations/Meas/MeasCaptureService.cs`, `Hcc.Infrastructure.SqlServer/Meas/SqlMeasCaptureStore.cs` and `Hcc.Api/Endpoints/MeasCaptureEndpoints.cs`.
  - Gate keys are the prototype's own guard keys. For example, `LogWalkedGuest` is gated on `meas.rev.overbooking`.
  - Refusal: FORBIDDEN with `details.reason` MEAS_OFF. No new HccErrorCode was added.
  - Idempotency comes from ICommandRunner and the tables' client ids. A meter reading that goes down is refused with VALIDATION and `MEAS_READING_NOT_MONOTONIC`.
  - Pulse anonymity: no runner row, no audit row and no timestamp.
- **One read and one loader.** `GET /api/v1/meas/captures` is masked under DEC-061: Housekeeping, Maintenance and Supervisor get no money. Training and exit rows go to Owner and Manager only.
  - The prototype has one shared `measX` loader, `src/clients/sync/meas-captures.ts`.
  - The HK and forecast commands are mapped (they were listed as unmapped before), and every MEAS mapper reloads `measX`.
- **ConfigCatalog group `measure`** ("Measurement reports", section Software and security) is now on the server, so MeasurementGate reads stored values (review H1). Target reads fall back to the defaults if a key is missing.
- **Migration 4860** (`4860_meas_pulse_no_timestamp.sql`, lock entry `--accept-out-of-order`, Hcc.slnx F12-INT block) drops `meas.pulse.created_utc` (review H4).

### Step 2: report-side data and wiring
- **Server analytics loader.** `SqlAnalyticsInputSource` builds one AnInput from live data plus `lgc` history, for both study-a and study-b. Its rules:
  - halls (CON, CONAC, CONF) and duplicate stays are excluded;
  - 211 is the Suite; legacy rows carry the legacy marker; open legacy stays are cut at 30 Oct 2026;
  - receipts are advance and payment only.
  - The golden run added two rules: rooms are in service from the **month** of their first sale, and **legacy receivables use the workbook's reconciliation rule** (section 5).
  - `SqlStudyPackFacts` is registered.
- **Server digest.** `IMeasDigestSource` / `MeasReportDigestSource` feed the Owner digest from every measured report outside the study area. A row counts when it is in the red group or has a truthy exception cell. It respects the gate and the `meas.digest.line.<key>` lines. Email gets the lines; WhatsApp gets a count only.
- **Server target reads.** HK and people reports read `meas.target.*` and `ffe.reservePct` through ISettingsReader, with the defaults as fallback (review M4).
- **Prototype digest.** `src/domain/reports/meas-digest-wiring.ts` is the same generic red-row provider, registered for every non-study item. The digest card and the email and WhatsApp bodies carry the Measurement block. `OwnerDigest.tsx` shows `DigestMeasurement`. The `meas.digest.line.<key>` settings are registered, so the subscription checkbox works.
- **Prototype reports.** `ReportView` draws every section of a multi-section report, with charts as inline SVG.
  - `RPT_RUN_DUE` syncs the study pack schedule and skips any report that is switched off (`measScheduleSkips`).
  - `mask.ts` grants PERM-MNT-001 to Owner, Manager and Maintenance.
  - The pulse audit actor is `Anonymous`.
  - Study-b now joins the registered legacy rows, as study-a does (one series, as on the server).
- **Report catalogue.** Section 3.10 of the catalogue: all 54 entries are now `built (MEAS)`; ids are unchanged.
  - Money flag corrected to y: overbooking-wash, ooo-days, utilities-per-room and linen-cost.
  - An F12-INT status note is at the end of the file.

### Step 3: UI mounting
- **Settings panel.** `MeasurePanel` is mounted in `HotelSettings.tsx` and in `src/mobile/admin/Settings.tsx`.
- **Capture dialogs.** The dialogs are registered in `app/registry.ts` (F12-INT block):
  - `TurnawayRegister` (turn-away, upsell, preferences, plus the booking-dialog button);
  - `SurveyRegister` (survey and loyalty);
  - the new `features/reports/MeasCaptureRegister.tsx`, which holds the meter, reading, asset-life, offline, discard, training, exit and pulse dialogs.
- **Search actions.** Eight quick actions in the search palette open the dialogs. They are filtered by `measOn` and the capture's roles, and listed only when searched (not in the empty palette).
- **Revenue pages.** Local events, Rate shop and Displacement are FitPages in REVENUE & SALES, Owner and Manager only. Each has `PAGE_CATEGORY`, keywords and a tile blurb, and sits in the yield feature's pages on both sides (`feature-catalogue.json` regenerated).
- **Not done:**
  - The search palette, phone tabs and scheduled runs filter reports by `measReportOn` already (hub-engine); no report entries exist in the search palette.
  - The Turnaway button in the reservation search header and the known-guest badge in the arrivals row are not mounted; both dialogs are reachable through the quick actions and the booking dialog.
  - **`node tools/fit-screen-check.mjs` was NOT run on the three new pages** (it needs `npm run build` plus a served build; there was no time inside the cap).

### Step 4: forecast and DEC-266
- **`yldOccReal` is NOT wired into `yldOcc`. This is an owner decision.** `meas.rev.forecast` defaults on. Wiring the real curve into `yldOcc` changes the yield tiers, and so the suggested BAR (room prices), for every night at the defaults. Neither the contract nor M-1…M-11 cover that. The function is ready; the owner must say yes first. Until then `yldOccReal` feeds the events, displacement and demand-calendar pages only.
- **The forecast back-test on real lgc data was not run.** The harness from section 5 can drive it (`fcStaysFromAn` over the same rows).
- **DEC-266 complimentary stay type: still not built.** It needs a command, an RBAC row, a server table and the loader read. Study-a still marks the comp column "pending DEC-266"; `AnStay.complimentary` is always false.

## 5. Legacy golden test (this PC, 7 Nov 2026): FAIL, written down

There is no HCC database with imported `lgc` data on this PC: `.\SQLEXPRESS` holds only EtpReporting, EtpScratch and the test databases, and the LG run of 1 Nov was a dry run. So `tools/meas-legacy-golden.ps1` gained a mode, `-Extract <LG-EXTRACT folder>`.
- The new `tools/legacy/meas_golden_lgc.mjs` reads `lgc_*.csv` from `E:\Projects\HCC\Existing Software\LG-EXTRACT\20261101-0531` (read only; never committed).
- It builds the legacy rows with the server loader's rules and runs the 21 ported study selectors. The prototype selectors are the parity-tested twins of the server.
- The headlines are then compared with the workbooks in `Existing Software\Reports` through `meas_golden.py`.

Map regexes were aligned with the built reports; no figure was changed:
- billed is compared with the Year Comparison Summary, because the Monthly Revenue FY Summary has formulas without cached values;
- ADR is read from the KPI by FY sheet;
- bad debt is the Due column, zero-rate the Zero rate column, repeat the Repeat % column.
- The workbook reader now ignores note lines that start with "FY".

Command: `pwsh tools\meas-legacy-golden.ps1 -Extract "E:\Projects\HCC\Existing Software\LG-EXTRACT\20261101-0531"`. Output: `.scratch\golden\lgc-1\`.

| Metric | Result (10 FYs, 2017-18 … 2026-27) | Cause |
|---|---|---|
| Room-nights | **10/10 exact** | none |
| Occupancy | **10/10 within 0.5 points** | After the fix (rooms in service from the first-sale month), 2017-18 is in limit too; before it, 2017-18 was off by 0.9 points. |
| ADR | **10/10 within 0.1 %** | none |
| Repeat % | **10/10** | none |
| Billed | 1/10 ok, 9 mismatches | The reference (Year Comparison "Total billed") **includes hall bills**; HCC excludes halls by contract §9.2. Checked bill by bill from `lgc_bill.csv`: HCC equals reference minus hall-bill net in every FY. A definition difference, not a loss of data. |
| Bad debt (DEC-265) | 6/10 ok, 4 mismatches | Before the fix, HCC showed **zero** bad debt: the loader took paid = net − balance, and `balance_paise` is 0 on every legacy bill. The workbook rule (paid_flag N, reconciled with linked receipts) is now in both the server loader and the harness. The 4 remaining FYs: 2017-18 and 2018-19 are unpaid **hall** bills, excluded by HCC. In 2025-26 / 2026-27, the workbook files a bill under its numbering FY (`legacy_fy`); HCC files it under its bill date. The two-year sum differs only by hall bills. |
| Zero-rate events | 0/10 | The workbook counts "Rate changed A → B" entries **parsed from stay notes**. AnInput carries no notes or rate-change events (MEAS-STUDY-A item 4), so HCC counts a proxy (billed rate against stay rate). Needs AnInput rate-change events from `lgc.stay.notes`: a model change, not done. |
| Hall events | 1/10 (2026-27: 0 = 0) | HCC drops hall bills from the study input, and no hall-event extras are loaded from `lgc`. Needs a legacy hall-events extra in the loader (CON / CONAC bills per FY). Not done. |

Owner or integrator decisions coming out of the golden test:
1. **Billed and bad debt:** is "excluding halls" the wanted definition for the study series? If yes, the reference figure must be the hall-free one and the map should compare that.
2. **Bad debt FY:** should legacy bad debt be filed under `legacy_fy` (the workbook) or under the bill date (HCC)?
3. Zero-rate and hall events need the two loader extensions above.

## 6. Still red, unverified, or not run
- **No gate. Local `main` was not fast-forwarded. No tag `wave/meas`.** The cause is the golden stop above.
- The full `dotnet test Hcc.slnx` was not run.
- Fit-screen check on the three new pages: not run.
- **Server SQL**, by what has been executed against a real database:
  - Executed once on a fresh `HccTest_*` database through the new tests: the capture store (round trip), the analytics loader and the pack facts.
  - **Never executed: the report SQL of the 32 measurement reports**, in all report lanes. They are compiled and their rules are tested; no measurement report has run its SQL against a database.
- Review findings:
  - Done: H1–H4, M4, M7 (the routes exist and are gated), M8 and M2 (the digest now covers every item that has a red-row rule).
  - M1 is partial: the satisfaction report has a red row on both sides when NPS is below target. The 48-hour unanswered-score rule is still only in the prototype's `satisfactionExceptions`. The other five guest reports were not changed.
  - Open: M3 (upsell is matched by free-text label), M5 (flash ADR divisor), M9 (`yld.overbook.<type>` is not in the server definitions), and L1–L8.
- Capture gaps (open items in the helpers' reports):
  - The server refuses the paid UPG, LCO1 and LCO2 folio charges with `MEAS_CHARGE_NOT_WIRED`: posting them is a GST-line decision (M-1 says GST from the dated master).
  - There is no public login-free QR survey route.
  - There is no Monday 06:00 OTB snapshot job.
  - The server has no task-time hook.
  - The 30-day certificate reminder is not written.
- Helper questions still open (money; not guessed):
  - Legacy `extra_paise` is counted as "other", not as room revenue (as `beyond_lib.py` does).
  - Legacy stays still open at the cutover are cut at 31 Oct 2026. Does the cutover re-create these guests live?
  - Which roles count as "no money" for the capture read: Supervisor is included.
  - On-the-books revenue basis = agreed nightly rate before GST.
- `meas.target.linenCost` and `upsellPerArrival` are not added; that is the Owner's choice.

## HANDOFF bullet
F12 MEAS f12-int (7 Nov):
- Built: server capture routes (23, gated) and the `measX` sync loader; group `measure` on the server; the SQL analytics input (live + lgc); the digest Measurement block on both sides; settings panel, capture dialogs and the three revenue pages mounted; catalogue section 3.10 `built`.
- Review H1–H4 fixed.
- Legacy golden (extract mode) passes room-nights, occupancy, ADR and repeat %. It fails billed and bad debt (halls, and the bill FY), zero-rate (no note events) and hall events.
- No gate and no main fast-forward yet: owner decisions in MEAS-DONE.md section 5. Also owed: the `yldOcc` go and DEC-266.

## RESULTS
Final run on the committed tree minus one blurb word: `dotnet build src/Hcc.Api` 0 errors / 0 warnings; Hcc.Meas.Tests 97/97, Hcc.Reporting.Tests 117/117, Hcc.Configuration.Tests 47/47 (Hcc.Closing 83/83 and Hcc.Counter 89/89 earlier the same day after the digest change); `npx tsc --noEmit` clean; `npm test` 3140/3141 - the one failure ([PPL] navmap must not contain the word "holidays") was the new Local events tile blurb, reworded; stream-people-master + stream-navmap re-run 43/43. So prototype 3141/3141 by parts (3133 before: +8 tests net). Full serial `dotnet test Hcc.slnx` and the gate: not run (golden stop).
