# MEAS Phase B review (read-only, meas-review)

Scope: `claude/meas` (6dbd8e77) against tag `meas-setup`. Read only, no build and no test run. Checked against MEAS-CONTRACT sections 1, 4, 8, 9 and catalogue 3.10.
Line numbers refer to the `claude/meas` tree. Severity: HIGH = wrong money, privacy or switch behaviour in production wiring; MEDIUM = wrong or missing behaviour with a workaround; LOW = hygiene.

Count: HIGH 4, MEDIUM 9, LOW 8.

---

## HIGH

### H1. Server measurement switches, targets and pack schedule do not work (gate fails open, reports may throw)
- Where: `08 Production/src/Hcc.Domain/Configuration/ConfigCatalog.cs:51-65` (no `measure` group), `Hcc.Application/Configuration/MeasurementCatalogue.cs:146` (`Provider()` returns null, so no setting is registered), `MeasurementGate.cs:31-35` (`registry.Find(key) is null` returns true), `Hcc.Reporting/Reports/MeasDaily/MeasDailySupport.cs:40-45` (`MeasTargets.GetAsync` calls `reader.GetAsync` with no try/catch), `StudyPackScheduler` (`ManagementPackReport.cs:225-226`, `v = null` falls back to "monthly").
- Scenario: the Owner switches `meas.daily.regrets` or the whole pack off. On the server nothing is stored, because the keys are not in `SettingsRegistry`. `ReportRunner` asks the gate, which answers "on", so the report still runs and is listed: contract 1.4 "refuses MEAS_OFF" fails. The same missing registration means `SettingsReader.GetAsync` (`registry.Get(key)`) cannot find `meas.target.*`; Manager's flash, high-balance, cashier variance and every other target-reading report is likely to throw instead of using its default. `packSchedule = off` is never honoured.
- Fix: add the `measure` group to `ConfigCatalog.Groups` in this wave (the DONE file calls it an integration item). Make the gate fail closed for unknown keys, or at least log. Wrap `MeasTargets.GetAsync` in a registry-miss fallback. Add a test that boots `HccComposition` and asserts `SettingsRegistry.Find("meas.pack.daily")` is not null.

### H2. Manager's flash overstates room revenue after a rate re-post (ADR and RevPAR wrong)
- Where: `MeasDailyReportsA.cs:41` (`c.kind='room' ... AND c.reverses_charge_id IS NULL` only).
- Scenario: migration 4686 (LF A1) re-posts a room night as a reversal row plus a new charge and sets `reversed_by_charge_id` on the original. This filter drops the reversal row but keeps the reversed original, so the night counts twice (old rate plus new rate). ADR, RevPAR, variance and the red-row rule are all inflated. The prototype sums live lines only, so screen and server disagree.
- Fix: add `AND c.reversed_by_charge_id IS NULL` (as `SqlBillingStore.cs:139` does). Same check for any other new SQL that sums `bill.charge` (`UpsellReport.cs:32-36`, see M3).

### H3. Survey report has no minimum-answer rule and can show one guest's answer
- Where: `MeasGuest/SatisfactionReport.cs` (rows built from any `list.Count > 0`, `verbatim` joins up to 5 comments), `Definition` params `roomType` and `channel`.
- Scenario: Manager filters month + room type 106 + channel Direct and sees one response with its scores and consented comment, which identifies the guest from the front-desk diary. Contract 8 / task rule: minimum 5 answers. Pulse has `PulseMinAnswers`; the survey and the reputation drill-down do not.
- Fix: suppress the row (counts only, "fewer than 5 responses") when `n < 5`, in both `satisfaction.ts` and `SatisfactionReport.cs`; drop verbatim below the floor; add boundary tests at 4 and 5.

### H4. Staff pulse can be re-identified from a timestamp; the migration text promises a different floor
- Where: `08 Production/database/migrations/4826_meas_people_exit_pulse.sql:27` (`created_utc DATETIME2(3)` kept on an "anonymous" table), line 4 (comment says 3 answers; report and contract use 5).
- Scenario: `meas.pulse` has department and a millisecond timestamp. Anyone with DB access, or the command audit log of the form submission (the user is signed in), can match a submission time to a user and read that person's pay-fairness and eNPS answers. The "no staff id" claim is only true column by column.
- Fix: store only the quarter (drop `created_utc`, or truncate to the quarter), and keep `client_id` random. Fix the comment to 5. Add a test that the table has no timestamp finer than a day. Consider complementary suppression (a department with exactly 5 answers is nearly one team).

---

## MEDIUM

### M1. Guest pack exception (red-row) rules are not wired anywhere
- Where: `MeasGuestRules.SatisfactionRed / ReputationRed / DeskRed / UpsellRed / SurveyLow` have zero callers outside tests (server); `03 Prototype/src/domain/reports/meas-guest/satisfaction.ts:30` `satisfactionExceptions` is exported and never used. Server guest reports return no exception column or red group at all.
- Scenario: NPS 12 against target 30, or a score of 2 unanswered for 3 days: nothing turns red and nothing reaches the digest. Catalogue 3.10 exception rules for 6 guest entries fail; the unit tests pass because they test the pure rule only.
- Fix: add the exception column or red group in each guest report on both twins, and call the rules in tests through the report, not the helper.

### M2. Owner digest block covers 2 of 32 measurement items
- Where: `03 Prototype/src/domain/streams/measure-digest.ts:53-54` (only regrets and high-balance registered); `Hcc.Domain/Reporting/MeasurementDigest.cs` has no caller in `src`.
- Scenario: contract 4 says red rows feed the digest; flash, cashier, desk timing, HK, people and study items never appear, and the server composer is never invoked.
- Fix: register the other lanes' pure red-row functions (`registerMeasRedRows`) and call `MeasurementDigest.Compose` from the digest sender. List the gap in DONE if deferred.

### M3. Upsell report matches charges by free-text label and counts reversed originals
- Where: `UpsellReport.cs:32-36` (`label LIKE N'%breakfast%'`, `'%extra bed%'`, `'UPG ·%'`, `reverses_charge_id IS NULL` only).
- Scenario: a restaurant bill line "Breakfast buffet" or a voided extra bed is counted as upsell revenue per arrival, which feeds the "below target" rule; a reversed upgrade still counts.
- Fix: filter on the charge `kind` or code, add `reversed_by_charge_id IS NULL`, and exclude POS lines.

### M4. FF&E reserve ignores the `ffe.reservePct` setting on the server
- Where: `MeasHk/AssetLifeReport.cs:77` (`DefaultFfePct` constant); prototype `asset-life.ts:18` reads `configX.values['ffe.reservePct']`.
- Scenario: Owner sets 5 %; screen shows 5 %, server PDF / Excel and the red-row rule use 3 %. Money column differs between screen and export.
- Fix: read the setting through `ISettingsReader` (after H1) with 3 as the default.

### M5. ADR in Manager's flash is not the contract 9 definition
- Where: `MeasDailyReportsA.cs:41, 95` and `flash.ts` `dayFigures` (room charge / all occupied rooms).
- Scenario: contract 9: ADR = room charge before GST / charged nights. Zero-rate and complimentary stays are in the divisor here, so the flash ADR is lower than the study ADR (`BeyondRevenue`, `LegacyCore`) for the same day, and the "ADR 10 % below target" red row fires on comp-heavy days.
- Fix: divide by nights with a positive room charge (or document the exception in the basis text and the contract). Same in both twins.

### M6. All 21 study reports return empty results on the server
- Where: `HccComposition.cs:206` registers only `EmptyAnalyticsInputSource` (`LegacyCore.cs:61`); no real `IAnalyticsInputSource` exists.
- Scenario: Owner opens the PDF or Excel pack, or any `rpt.study.*` report, on the server: tables with zero rows and no capture note. Screen (prototype) shows data.
- Fix: implement the loader (stays, bills, receipts, `lgc` history, rooms; halls and duplicates excluded) or make the empty source set `About.Provisional` and a visible "no data source wired" note; record it in DONE.

### M7. Capture endpoints are not gated on the server
- Where: `MeasurementRouteFilters.cs` `RequireHccMeasurement` has no caller (`grep` over `src`); `Hcc.Operations/Meas/*Rules.cs` comments say the rules "back the HTTP endpoints once the gate is merged".
- Scenario: contract 1.4 says a switched-off capture refuses `MEAS_OFF`. No server capture route exists or carries the filter, so the rule is untested end to end.
- Fix: add the route filters with the capture endpoints, or list as an open item. Add a test that every route carrying `HccMeasGateMetadata` has a known key.

### M8. Mixed line endings in four modified files
- Where: `03 Prototype/src/domain/reports/register.ts` (94 of 107 lines CRLF, setup was LF), `08 Production/src/Hcc.Api/HccComposition.cs` (411 of 434), `08 Production/tests/Hcc.Reporting.Tests/EngineTests.cs` (271 of 271), `Hcc.Reporting.Tests.csproj` (27 of 27).
- Scenario: the diff against main shows 822 changed lines in `HccComposition.cs` for 46 real additions; any parallel edit on main conflicts on the whole file and blame is lost.
- Fix: normalise to LF (`git add --renormalize` for these four) before the merge; confirm with `git diff --ignore-cr-at-eol --stat` equal to `git diff --stat`.

### M9. Server `yld.overbook.<roomTypeId>` missing from the setting definitions
- Where: `MeasurementCatalogue.cs:126-141` (contract 1.2 lists it; prototype `MEAS_OTHER` comment says built per room type).
- Scenario: the Overbooking report's "allowed" column on the server cannot read the Owner's allowance; falls back to zero, so every oversold room is red.
- Fix: register one definition per active room type at provider build time, or read with a dynamic-key provider.

---

## LOW

- L1. `HkProductivityReport.cs:71, 100` and `UtilitiesPerRoomReport.cs:44` use `Math.Round(double)` (banker's rounding) where the prototype uses `Math.round` (half up). Differs only at exact .5 minutes or a 3-decimal meter delta. Use `MidpointRounding.AwayFromZero`.
- L2. Money passes through `double` in several places (`MeasDailyReportsA.cs:58` `DivRound`, `ManagementPackReport.cs:72-74`, `AssetLifeReport.cs:77`, `BeyondLib.cs:34-36`, `BeyondFinance.cs:95-104`). Exact below 2^53 and tie-safe for these ranges, so no wrong value found, but contract 8 says integer paise; prefer `decimal` or integer half-up helpers.
- L3. `MeasDailySupport.cs:84` high-balance reason prints `threshold / 100` (integer division): a Rs 5,000.50 threshold shows as 5,000 in the exception text. The text carries money in a non-money cell; safe today because view and money gate are both PERM-RPT-002, but it would leak if the view permission is ever widened.
- L4. `AssetLifeReport` and `LinenCostReport` are viewable by Housekeeping / Maintenance (`DeskPerms` / `PERM-HK-001`) while money flag y; masking relies on `MoneyGate` removal. Correct today, but no test pins "HK role sees no money column" for these two (only for OOO). Add one per money-flagged HK report.
- L5. `LegacyOccupancy.cs:106` `DateTime.Parse(asOf)` is culture-sensitive; use `DateOnly.ParseExact(..., "yyyy-MM-dd", InvariantCulture)`.
- L6. `StudyPackScheduleWorker` (`ManagementPackReport.cs:250-252`) swallows every exception with no log; a permanent failure is invisible.
- L7. `MeasGuestRules.UpsellTargetDefault = 5000` is a hard-coded paise target with no `meas.target.*` key, so the Owner cannot change the "upsell below target" rule.
- L8. Flash "last year" is `date - 365` days (`MeasDailyReportsA.cs` `lastYear`, `flash.ts` `ly0`), not the same calendar date; after 29 Feb it is off by one weekday.

## Checked, no finding
- Report id to key mapping: all 54 `MeasurementCatalogue` rows against the 32 server reports with a literal `Id` and `MeasKey` (script comparison): no mismatch. The 22 study ids are built in constructors and match the catalogue by inspection (StudyA keys, StudyB `"meas.study." + key`). Prototype map is seeded from `MEAS_ITEMS`; `registerMeasReport` calls agree.
- Module-tied items: ppm/utilities to maintenance, linen to inventory, people to payroll are identical in both tables.
- FY April-March: every `FyOf` / quarter helper reviewed uses month >= 4.
- Pulse report: department only, minimum 5 per department-quarter, Owner / Manager only (PERM-HR-002), suppressed count shown. Survey report shows no guest identity and no room number.
- DEC-058: all `meas.*` settings use `Approver = Owner`; PERM-MEAS-001 seed 4802 gives Manager and Tech admin view only.
- DEC-061: HK / Maintenance see money only if they hold the money gate; OOO, utilities, linen, asset reports have money columns typed `M` (removed, not blanked).
- No TODO, scratch, console or debug code in changed source; no `.scratch` file tracked; study and golden fixtures are fictional (rooms R1.., 101..); `tools/meas-legacy-golden.ps1` and `tools/legacy/*` only reference the legacy workbook folder, nothing is copied in.
- Exception boundaries checked against catalogue text: flash (exactly 10 % below is not red), regrets (>3, <60 %), discrepancy (>120 min), OOO (>7 days), cashier (>Rs 100, third variance), rate variance (floor ceil), pass rate, idle 14+, overdue >7, P1 MTTR >24, 3 failures in 90 days, utilities >15 %, linen >2 %. All match.
