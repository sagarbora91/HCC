# MEAS contract — wave F12 measurement reports + study-report port (5 Nov 2026)

Base `claude/meas` from `main` 403d3172 (F7 report engine cd6b5961, AF2, ADM-2 all in). Plan: `E:\Projects\HCC\02 Planning\HCC_Measurement_Reports_Plan_MEAS_2026-11-05.md` (wins on intent; this file wins on names, shapes, numbers). Rules for every lane: `E:\Projects\HCC\02 Planning\HCC_Wave_Plan_F1-Fn_2026-11-02.md` §1 and Appendix A. Catalogue rules (`HCC_Report_Catalogue_v1.md` §1) win over this file on any report rule. Coordinator for the wave: Opus deputy; commits carry `Co-Authored-By: Claude Opus 5.5` (Sonnet lanes: `Claude Sonnet 5.5`).

**Deviations from the plan, decided by the setup commit (each with the reason):**
1. **SCHEMA stays 15.** The plan said "SCHEMA bump". A bump changes the FY26 / seed pin hashes in `stream-catalogue.mjs`, `stream-frontdesk-master.mjs` and `stream-expenses-decisions.mjs` and would turn every lane red at once. No existing persisted shape changes: every extra field on an existing record lives in a **side table keyed by the existing id** inside the new optional slice `s.measX` (§2), exactly as F7 did with `rptX`. The integrator bumps to 16 only if a lane proves it changed an existing shape, and re-pins once.
2. **Migrations start at 4802, not 4800.** 4800 `roles_as_data` and 4801 `cfg_change_source_cascade` are applied (ADM-2). Block is **4802–4869**. The lock's highest is 4920 (AF2: 4900 / 4910 / 4920), so **every MEAS migration is below the highest** and needs `node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order` (the ACC-2 precedent for 4255 / 4256); the gate's lock check still must pass.
3. **Report count.** The plan's "26 + 22" does not reconcile with its own tables: they define **32** report ids (9 daily, 6 revenue, 6 guest, 6 housekeeping/assets, 5 people) plus 2 non-report measures (2.2 forecast curve, 3.4 preferences) = **34 measures**, and **22** study entries. All **54** report entries are in catalogue §3.10 (§3 below). Catalogue total becomes 130 + 54 = 184.
4. **Default targets.** Plan §1 rule 5 (FY 2025-26 baseline: occupancy 31 %, ADR Rs 1,954) and §8 M-9 (occupancy 35 %, ADR Rs 2,050) differ. **M-9 is the owner-default table and wins**; the baseline figures are shown beside each target in the panel as "FY 2025-26 actual" (read-only hint).

## 1. Switches, targets, `measOn`, `MeasurementGate`

### 1.1 Settings group
New configuration group **`measure`** "Measurement reports", section *Software and security*, `money:false`, link none, blurb as plan §2. **Only `meas-settings` edits `config.ts`** (adds `'measure'` to `CfgGroupId`, one `CFG_GROUPS` row, and updates the tests that pin the group list). All settings: approver Owner, `store:{kind:'value'}` (live in `s.configX.values`, server `cfg.value`), effective immediately, exportable in the configuration JSON, written through the existing `CFG_SET` path so `rpt.admin.config-history` logs them. Permission **`PERM-MEAS-001`** (measurement settings, Owner only; Manager and Tech admin read-only).

### 1.2 Keys (frozen)
Pack switches (boolean, default on): `meas.pack.daily`, `meas.pack.revenue`, `meas.pack.guest`, `meas.pack.hk`, `meas.pack.people`, `meas.pack.study`.
Item switches (boolean, default on) — exactly these keys, one per measure (34) and per study entry (22):

| Pack | Item keys |
|---|---|
| daily | `meas.daily.flash` (core), `.regrets`, `.rate-variance`, `.discrepancy`, `.high-balance` (core when credit on), `.adjustments`, `.no-show`, `.inventory` (core), `.cashier` |
| revenue | `meas.rev.demand-calendar`, `.forecast`, `.pickup`, `.displacement`, `.overbooking`, `.restrictions`, `.rate-shop` |
| guest | `meas.guest.survey`, `.reputation`, `.recovery`, `.preferences`, `.desk-timing`, `.upsell`, `.repeat` |
| hk | `meas.hk.productivity`, `.ooo`, `.ppm`, `.utilities`, `.linen`, `.assets` |
| people | `meas.people.productivity`, `.attrition`, `.scorecard`, `.training`, `.pulse` |
| study | `meas.study.revenue-monthly`, `.year-comparison`, `.covid-index`, `.deep-dive`, `.occupancy`, `.patterns`, `.rooms-rates`, `.zero-rate`, `.receivables`, `.rm-kpi`, `.rm-channel`, `.rm-pricing`, `.guest-repeat`, `.guest-origin`, `.hk-ops`, `.fnb-halls`, `.finance`, `.sales`, `.compliance`, `.forecast`, `.dashboard`, `.management-pack` |

Other settings (all Owner, in the same group): `meas.study.packSchedule` (choice off / monthly / quarterly, default monthly), `meas.daily.blockClose` (boolean, default off), `meas.regrets.mandatory` (boolean, default off), `yld.overbook.<roomTypeId>` (integer, default 0), `yld.restrictionsEnforce` (boolean, default off), `ffe.reservePct` (percent, default 3).
Targets (`meas.target.*`, Owner-only; money in paise): `occupancy` 35 (percent), `adr` 205000, `nps` 30, `repeatShare` 25, `lateDeparture` 10, `ot` 10 (all percent unless named), `highBalance` 500000, `cashVariance` 10000, `checkinMinutes` 8, `checkoutMinutes` 6, `absenteeism` 5, `attrition` 30, `lossPct` 2.

### 1.3 Effective state (frozen signature)
`measOn(s:State, key:string): boolean` in `src/domain/streams/measure.ts` = (the item's optional module is on, `cfgModuleOn`) **and** pack on **and** item on; a `core` item is always on (the panel shows "Core — always on" for `meas.daily.flash`, `meas.daily.inventory`, and `meas.daily.high-balance` once credit is enabled). `measTarget(s,key)` returns the stored target. Items tied to optional modules: `meas.hk.ppm / utilities` → `maintenance`, `meas.hk.linen` → `inventory`, `meas.people.*` → `payroll`. The setup commit ships stubs (`measOn` always true); `meas-settings` replaces the bodies and owns the key table `MEAS_ITEMS: MeasItem[]` (type already declared). Every surface calls `measOn`: the hub (`reportCatalogue` filter), search palette, phone tabs, scheduled runs, the Owner digest, the capture forms and capture commands.

### 1.4 Rules (frozen)
- Switching a pack off writes one change row per item. Switching an item on while its pack is off is refused with the reason (`MEAS_PACK_OFF`).
- A switched-off capture keeps its handlers registered and refuses with **`MEAS_OFF`**; a switched-off report is absent from the catalogue and refuses a run with `MEAS_OFF`. Data is kept.
- Off must never break the day close: the three core items stay on; the night-audit auto-tick lines for an off item are skipped, not failed; `meas.daily.blockClose` (default off) is the only way a measurement can block a close.

### 1.5 Server gate (mirror of `IModuleGate`)
`Hcc.Application/Configuration/MeasurementGate.cs`: `IMeasurementGate { Task<bool> IsOnAsync(Guid propertyId, string key, ct); Task EnsureOnAsync(Guid propertyId, string key, ct) }` (throws the platform's `HccException` code `MEAS_OFF`); `MeasurementGate(ISettingsReader)` reads the same `cfg.value` rows and applies the same module + pack + item rule from `MeasurementCatalogue` (a static table of the keys above, mirrored from `MEAS_ITEMS` and pinned by a parity test). Route helper `RequireHccMeasurement(key)` + `HccMeasGateMetadata(key)` like `RequireHccModule`. `ReportRunner` (F7) consults the gate before a report whose definition carries a `measKey`; add `string? MeasKey` to `ReportDefinition` is **not** allowed (F7 type) — the MEAS reports instead implement `IMeasuredReport { string MeasKey {get;} }`, and the setup note to the integrator is to make `ReportCatalogue` / `ReportRunner` honour it (one small edit owned by `meas-settings`, listed in its DONE). Prototype twin: `reportCatalogue(s,uid)` and `runReport` filter / refuse through `measOn` for ids registered in `MEAS_REPORT_KEY` (id → key map, owned by `meas-settings`, filled by report lanes through `registerMeasReport(id,key)`).

## 2. Record types (declared in `src/domain/streams/measure-types.ts`, slice `s.measX`)

Full TypeScript is in the file (setup commit). Summary — table | owner lane | commands | permission:

| Record / side table | Lane | Commands (all prefixed `MEAS_`) | Permission to write |
|---|---|---|---|
| `turnaway[]` (`res.turnaway`) | cap-desk | `MEAS_TURNAWAY_LOG` (button on reservation search: reason, arrival, nights, type, quoted rate, channel) | Reception, Manager, Owner |
| `walked[]` | cap-desk | `MEAS_WALKED_LOG` (on check-in refusal) | Manager, Owner |
| `allowanceReasons[]` | cap-desk | `MEAS_ALLOWANCE_REASON_SAVE` (extends the discount-reasons master) | Owner |
| `bookingExtra{bookingId}` (booked type, upgrade paid/free + approver) and folio items `UPG`, `LCO1`, `LCO2` | cap-desk | `MEAS_BOOKING_EXTRA_SET`; items posted through the existing folio add-line command | Reception, Manager |
| `preference{customerId}` incl. blacklist (reason + approver) and `loyaltyTag` | cap-desk (fields) / cap-guest (loyalty) | `MEAS_PREFERENCE_SET`, `MEAS_LOYALTY_SET` | Reception, Manager (blacklist: Manager + Owner approval) |
| `taskTimes{taskId}` (startedAt, finishedAt, inspectedAt, inspectResult, rejectReason, attendant) | cap-hk | no new command: `housekeeping.ts` start / complete / inspect handlers call `measRecordTaskTime` (append block) | existing HK permissions |
| `offline{roomId}` (reason, expectedReturn, workOrder) | cap-hk | `MEAS_OFFLINE_SET` | Manager, Supervisor |
| `meters[]`, `readings[]` (monotonic per meter, photo optional) | cap-hk | `MEAS_METER_SAVE` (Owner, Manager), `MEAS_READING_ADD` (**PERM-MEAS-002**: Maintenance, night shift, Manager) | |
| `assetLife{assetId}`, `discards[]` | cap-hk | `MEAS_ASSETLIFE_SET`, `MEAS_DISCARD_ADD` | Owner, Accountant / Housekeeping Supervisor |
| `surveys[]` (5 scores 1–5, recommend 0–10, comment, consent; **no ID data**) | cap-guest | `MEAS_SURVEY_ADD` (QR page via `via:'qr'`, desk entry via `via:'desk'` needs **PERM-MEAS-002**) | public page (no login, rate-limited) / Reception |
| `reviews[]` (per month per platform) | cap-guest | `MEAS_REVIEW_SAVE` | Manager, Owner |
| `rootCauseList[]`, `rootCause{complaintId}` | cap-guest | `MEAS_ROOTCAUSE_LIST_SAVE`, `MEAS_ROOTCAUSE_SET` | Manager |
| `training[]`, `exit{staffId}`, `pulse[]` (anonymous: department only, no staff id) | cap-people | `MEAS_TRAINING_SAVE`, `MEAS_EXIT_SET`, `MEAS_PULSE_ADD` | HR-001 gate; pulse: any signed-in staff, department chosen by the form |
| `events[]` (`yld.event`) | forecast | `MEAS_EVENT_SAVE`, `MEAS_EVENT_DELETE` | Manager, Owner |
| `otb[]` (`yld.otb_snapshot`) | forecast | `MEAS_OTB_SNAPSHOT` (scheduled Monday 06:00; back-fill from bookings' createdAt / cancelledAt) | system |
| `competitors[]`, `rateshop[]` | forecast | `MEAS_COMPETITOR_SAVE`, `MEAS_RATESHOP_ADD` | Manager, Owner |
| `restrictions[]` counters | forecast | internal (yield guard increments); `yld.restrictionsEnforce` setting | system |

Errors: `MEAS_OFF`, `MEAS_PACK_OFF`, `FORBIDDEN`, `VALIDATION`, `NOT_FOUND`, `MEAS_READING_NOT_MONOTONIC`. Every command is idempotent on its client id, audited, uses the simulated clock, and refuses first on `measOn`. New permissions: **PERM-MEAS-001** measurement settings (Owner), **PERM-MEAS-002** enter meter readings / survey on behalf of a guest (Owner, Manager, Reception for surveys, Maintenance for readings). Seeded by `4802_meas_rbac_seed.sql`.

**Server mirror (schema `meas`, one table per record above, `property_id` + client id idempotency, append-only audit rows):** `meas.turnaway`, `walked`, `allowance_reason`, `booking_extra`, `customer_preference`, `task_times`, `room_offline`, `meter`, `meter_reading`, `asset_life`, `discard`, `survey`, `review_month`, `root_cause`, `training`, `staff_exit`, `pulse`, `event`, `otb_snapshot`, `competitor`, `rate_shop`, `restriction_counter`. Names are fixed; columns follow the TypeScript fields in snake_case. Sync areas: one `AREAS` entry per capture lane (`registry.ts` F12 blocks).

## 3. Catalogue entries

Catalogue §3.10 "Measurement (MEAS, wave F12)" (54 entries: 32 measurement + 22 study), summary-table rows and the counts addendum are **already in the setup commit** in `E:\Projects\HCC\worktrees\meas\02 Planning\HCC_Report_Catalogue_v1.md` (and appear on `main` after the merge). New areas `revenue` (6 entries) and `study` (22). Status `planned (MEAS)`; the integrator sets `built` from code. Ids never change. Report lanes implement the columns in the entry order; a money flag change goes into DONE.

## 4. Exception rules and defaults
Each entry's *Exception rule (red row)* is the acceptance rule for the lane's tests (one test per rule: row red / not red at the boundary). Red rows feed the Owner digest (F4) block "Measurement" (lane `meas-digest`). Targets come from §1.2.

## 5. Migrations (all `--add --accept-out-of-order`, each also a `Hcc.slnx` line inside the lane's F12 block)

| Range | Lane |
|---|---|
| 4802–4809 | meas-settings (4802 `meas_rbac_seed`, 4803 `meas_settings_seed`) |
| 4810–4814 | meas-cap-desk |
| 4815–4819 | meas-cap-hk |
| 4820–4824 | meas-cap-guest |
| 4825–4829 | meas-cap-people |
| 4830–4834 | meas-forecast |
| 4835–4836 / 4837–4838 / 4839–4840 / 4841–4842 / 4843–4844 | meas-rpt-daily / -revenue / -guest / -hk / -people |
| 4850–4853 / 4854–4857 / 4858–4859 | meas-study-a / -b / -pack |
| 4860–4869 | f12-int (fallout) |
| 4845–4849 | spare (ask the integrator) |

Before any lane starts: `08 Production/database/migrations.lock.json` highest = 4920; 4800 / 4801 / 4900 / 4910 / 4920 taken; nothing else in 4802–4869 (checked at the setup commit).

## 6. Append-only blocks (`// F12-<LANE> … // F12-<LANE> end`, created empty by the setup commit)

| File | Blocks (one per lane unless noted) |
|---|---|
| `03 Prototype/src/domain/types.ts` | `F12` (setup: `measX`) |
| `03 Prototype/src/services/mock/handlers/index.ts` | imports and `const F12` object (spread already added to `registry0`): lanes SETTINGS, CAP-DESK, CAP-HK, CAP-GUEST, CAP-PEOPLE, FORECAST |
| `03 Prototype/src/services/mock/index.ts` | same six lanes (exports) |
| `03 Prototype/src/clients/sync/registry.ts` | same six lanes (import + `AREAS` entry) |
| `03 Prototype/src/domain/reports/register.ts` | RPT-DAILY, RPT-REVENUE, RPT-GUEST, RPT-HK, RPT-PEOPLE, STUDY-A, STUDY-B, STUDY-PACK, DIGEST (one import line per report file) |
| `03 Prototype/src/app/navmap.ts` | `F12-KEYWORDS` (keywords for new pages only; no new nav category, no new top-level module) |
| `08 Production/src/Hcc.Api/HccComposition.cs` | services block and endpoints block, nine lane names + SETTINGS/CAP-*/FORECAST (reports: `Hcc.Reporting.ReportingModule.AddReport<…>(services)`) |
| `08 Production/Hcc.slnx` | project lines and migration `File` lines per lane |
| `02 Planning/HCC_Report_Catalogue_v1.md` | status lines at the end only, `<!-- F12-<LANE> -->` (integrator sorts) |

Not append-only, owned by one lane: `src/domain/streams/config.ts` (meas-settings: group row + `registerSettingsGroup` call), `housekeeping.ts` / `maintenance.ts` / `staff.ts` / `services.ts` / `yield.ts` (the capture lane that owns the row, inside an `// F12-<LANE>` block only), `src/domain/types.ts` and `SCHEMA` (setup commit; integrator only).

## 7. Owner defaults applied (plan §8; the owner's later answer replaces the row)

| Q | Applied default |
|---|---|
| M-1 | Check-in 12:00, check-out 11:00; late check-out items `LCO1` Rs 300 after 14:00 and `LCO2` Rs 600 after 18:00 (paise 30000 / 60000, GST from the dated master), waiver needs Manager |
| M-2 | Regrets capture optional (`meas.regrets.mandatory` off); the weekly digest shows the count |
| M-3 | Survey: QR card on the LAN page **and** desk entry; WhatsApp link when F9 egress is on |
| M-4 | Meters: electricity main, water, LPG cylinders (count), DG hours; floor sub-meters later (meter master allows adding them) |
| M-5 | `ffe.reservePct` 3 % |
| M-6 | `yld.overbook.<type>` 0 for every type |
| M-7 | Yield restrictions advisory (`yld.restrictionsEnforce` off) |
| M-8 | High balance for a walk-in without a company: Rs 5,000 (`meas.target.highBalance`) or any balance older than 2 nights |
| M-9 | Occupancy 35 %, ADR Rs 2,050, NPS 30, repeat share 25 %, uncharged late departures 10 %, OT 10 %; editable in the panel |
| M-10 | Management study pack monthly on the 1st to Owner + Manager (Manager's copy without receivables names); quarterly PDF kept in the document vault |
| M-11 | Repeat customer list and corporate accounts: Owner and Manager; Accountant sees receivables names only (rule V) |

## 8. Rules that bind every lane (do not re-ask)
Integer paise; GST from the dated master (DEC-069); simulated clock; fictional data only. **DEC-058** money / tax / payroll settings are Owner-only. **DEC-061** Housekeeping and Maintenance never see hotel money (money columns removed, not blanked; the survey report never shows guest identity to them). **DEC-265** legacy receivables are bad debts (never opening receivables). **DEC-266** explicit complimentary stay type with approver and reason (1.8 / 6.8 read it; **it is not built in code on `main` today**, so 1.8 reads `roomoffline` + stays flagged `complimentary` if present and marks the comp column "pending DEC-266"). Every new page is a `FitPage` (DEC-194…196), never a smaller font; phone 48 px / 16 px / cards / no sideways scroll; colours are tokens; no new npm packages; no new top-level module; no new role. F8 `qw-noshow` and `qw-nightaudit` are **not** on `main`: `meas-rpt-daily` builds 1.7 on bookings + folio directly and exposes the night-audit auto-tick lines as a pure function `measNightAuditLines(s)` that F8 can adopt later (no dependency on F8).

## 9. Analytics selectors (plan §7A)
1. Signature fixed in `03 Prototype/src/domain/analytics/types.ts` (setup commit): `AnalyticsSelector = (input:AnInput, params:AnParams) => RptResult`; one module per workbook family under `domain/analytics/` (`legacy-*.ts` study-a, `beyond-*.ts` study-b); server twin `Hcc.Reporting/Analytics/*` with the same method names. The report registration wraps the selector as the report's `select`, building `AnInput` from stays / bills / receipts / `lgc` history / rooms master (halls and duplicate stays already excluded by the loader).
2. Definitions (from the workbooks' Notes & Checks): occupancy = distinct room-nights slept ÷ (rooms in service × days); ADR = room charge before GST ÷ charged nights on bills (bill month); revenue per room-night on nights × rooms; FY April–March; room 211 = Suite; halls excluded from room statistics; duplicate stays excluded; COVID window 1 Mar 2020 – 31 Mar 2021 excluded from "normal years"; comp / zero-rate per DEC-266; receivables per DEC-265.
3. One series: legacy rows carry a `legacy` marker; PDF footer "Legacy to 30 Oct 2026 / HCC live".
4. `ChartBlock` (meas-study-pack): PDF bar / line / stacked drawn as PDFsharp shapes from the same series, Excel native OpenXML charts, heat maps as shaded tables; screen reuses the inline-SVG helpers. No new packages.
5. Golden test `tools/meas-legacy-golden.ps1` (this PC only; legacy data and workbooks never in the repository): runs ported reports on imported `lgc` data FY 2017-18…2026-27 and compares headline totals with the workbooks read by `tools/legacy/` Python (billed per FY, room-nights, occupancy, ADR, bad-debt total, zero-rate counts, repeat %, hall events). Tolerance 0.1 % money, 5 room-nights, 0.5 points percentages. Mismatch = the integrator stops. The Python scripts in `E:\Projects\HCC\Existing Software\Reports\` are the independent cross-check and are never deleted.

## 10. Tests each lane owns
`tests/stream-meas-<lane>.mjs` with `[F12-<LANE>]` test names: every command guard, the `MEAS_OFF` refusal, DEC-061 masks, the exception-rule boundary per report, three-way equality (screen = Excel = PDF) on the FY26 fixture. Server: `Hcc.Reporting.Tests` / a new `Hcc.Meas.Tests` project (meas-settings creates it and its `Hcc.slnx` line) — lanes add test files, not projects. `tests/stream-meas-settings.mjs` also pins the key table against `MEAS_ITEMS` and the C# `MeasurementCatalogue` (parity). The setup commit changes no test.

## 11. Wave mechanics
Worktree `E:\Projects\HCC\worktrees\meas`, branch `claude/meas`; lanes `E:\Projects\HCC\worktrees\meas-<lane>` on `claude/meas-<lane>`, each with the `node_modules` junction to `worktrees\fixwave-a`; `PORT` = 4190 + lane number (settings 1, cap-desk 2, cap-hk 3, cap-guest 4, cap-people 5, forecast 6, then 7…15 for Phase B / B2 lanes). Ledger `05 Delivery Evidence\Measurement\MEAS-LEDGER.md`. All heavy jobs through `tools\Invoke-Serialized.ps1`. Phases: A (6 lanes) → merge into `claude/meas` → B (6) → B2 (3) → C (review, f12-int, gate `-Wave meas`, docs). Briefs: `02 Planning/agents/MEAS_Briefs.md`.

## 12. Decisions to record at close (f12-docs; next free DEC-267, check APR)
Proposed: (a) measurement reports are switchable at module / pack / item level, Owner-only, data kept; (b) measurement targets default to the M-9 table; (c) the study workbooks are ported into HCC with the legacy golden test; (d) side-table storage keeps SCHEMA 15 (or the bump, if the integrator needed one).
