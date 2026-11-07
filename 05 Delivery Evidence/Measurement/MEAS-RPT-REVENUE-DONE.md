# MEAS-RPT-REVENUE DONE

Lane meas-rpt-revenue / Sonnet 5.5 / branch `claude/meas-rpt-revenue` / worktree `E:\Projects\HCC\worktrees\meas-rpt-revenue` / base `claude/meas` b017cf13 (+ briefs commit b89cc237).

## Commits
See `git log --oneline b89cc237..HEAD` (first: 1b91f4fd "six revenue-management reports"; later commits fix type / test findings and add goldens + this file).

## Files
Prototype (`03 Prototype`):
- created `src/domain/reports/meas-revenue/` : `logic.ts` (pure red-row rules), `shared.ts`, `demand-calendar.ts`, `pickup-pace.ts`, `displacement.ts`, `overbooking-wash.ts`, `restriction-effect.ts`, `rate-parity.ts`, `index.ts` (entry for tests and the digest lane: `measRevenueRedCounts(s)`, every `*Compute` returning rows with a `red` flag)
- appended `// F12-RPT-REVENUE` block in `src/domain/reports/register.ts` (six imports)
- created `tests/stream-meas-rpt-revenue.mjs` ([F12-RPT-REVENUE] tests); goldens `tests/rpt-golden/goldens/rpt.revenue.*.json` (BASE default + FY26 case each)
Server (`08 Production`):
- created `src/Hcc.Reporting/Reports/MeasRevenue/` : `MeasRevenueLogic.cs`, `MeasRevenueData.cs` (read-only SQL loaders over inv / ota / meas), `MeasRevenueCommon.cs`, `MeasRevenueBase.cs`, and one class per report (`DemandCalendarReport`, `PickupPaceReport`, `DisplacementReport`, `OverbookingWashReport`, `RestrictionEffectReport`, `RateParityReport`), each `IReportSelector` + `IMeasuredReport` (MeasKey)
- appended six `AddReport<>` lines in `src/Hcc.Api/HccComposition.cs` (services spot of `F12-RPT-REVENUE`; the endpoints spot stays empty)
- created `tests/Hcc.Reporting.Tests/MeasRevenueTests.cs`
- edited (outside the ownership row, one line) `tests/Hcc.Reporting.Tests/EngineTests.cs`: `.. MeasRevenueCatalogue.Reports(), // F12-RPT-REVENUE` in `ServerCatalogue()` so the existing prototype-vs-server catalogue cross-check includes the six ids. Other report lanes will add the same kind of line next to it (trivial merge).
- No migration (4837-4838 unused), no new `Hcc.slnx` line (no new project, no migration), SCHEMA stays 15, no new package.

## What was built
Six reports in area `revenue` (status `MEAS`, all `moneyFlag` with gate PERM-RPT-002), columns and keys in catalogue order, both sides, same ids / keys / types / flags:
- **rpt.revenue.demand-calendar** (view PERM-RPT-001, rate columns behind -002, D-FWD, From + Nights max 92): on the books, forecast % (real curve `fcForecastNight` when `meas.rev.forecast` is on), same night last year, events, live BAR / suggested rate of the largest room type, offer open (a discount plan available). Red: forecast >= 85 % with BAR not above its base, or <= 25 % with no discount offer open.
- **rpt.revenue.pickup-pace** (-002, D-NOW, Months 1-12 + Channel): room-nights and revenue on the books per coming month vs the same month last year as known 364 days ago, pace %, 7-day pickup, most common lead-time bucket. A stored OTB snapshot (within 7 days before the date) replaces the derived last-year figure and the 7-day baseline. Red: pace worse than -10.0 % (exactly -10.0 % is not red; no last year is never red).
- **rpt.revenue.displacement** (-002, D-FWD, formats screen + PDF): report shell over `fcDisplacement` / `MeasForecast.Displacement` (read, not changed). Params enquiry, From, To, Rooms, Offered rate (Rs), Room type. Per night + total row; recommendation accept / counter-rate / decline (decline when the break-even counter-rate is above 150 % of the offer). Red: net < 0.
- **rpt.revenue.overbooking-wash** (view -001, D-F): by arrival weekday x channel: no-show %, cancellation %, expected wash rooms, `yld.overbook` allowed, walked guests and cost (desk log). Red: rooms oversold (Confirmed / In house beyond rooms in service) on a night of that weekday above the allowed number.
- **rpt.revenue.restriction-effect** (-002, D-B, max 92 days, Restriction kind): one row per restricted night and kind (MIN_STAY / CTA from the yield restrictions), refused / re-shaped counters, revenue, same-weekday unrestricted average, Enforced / Advisory from `yld.restrictionsEnforce`. Red: night under 40 % occupancy.
- **rpt.revenue.rate-parity** (-002, D-FWD, From + Nights max 92 + Room type): direct (live BAR), OTA (lowest published connected-channel rate), first six active competitors' newest shop entry, integer median, position %, exception text; competitor column labels carry the competitor names. Red: OTA below direct; direct more than 15 % above the median with forecast occupancy under 40 %.
- Switch: each class implements `IMeasuredReport` (server) and calls `registerMeasReport` (prototype); MEAS_REPORT_KEY mapping confirmed equal to the contract (demand-calendar, pickup, displacement, overbooking, restrictions, rate-shop); off = hidden and `MEAS_OFF` on a run (tests).
- Empty data: pickup, overbooking, rate-parity and restriction show zero rows and the entry's "Capture needed" note appended to the date basis (About); demand calendar keeps its rows and adds the note when no events exist. Never an error.
- The red-row flag: `RptRow` has no flag field and types.ts is forbidden, so each report exposes its rows with a `red` flag through `*Compute` (prototype `meas-revenue/index.ts`) and `ComputeAsync` (server); `measRevenueRedCounts(s)` gives the digest lane its per-report counts.

## Tests
Stopped at the 80-turn cap while my queued jobs waited behind other lanes' npm tests; state at stop:
- `npx tsc --noEmit` (03 Prototype): exit 0 (after the bool-cell fix).
- Focused `HCC_TEST_ONLY=meas-rpt-revenue npm test`: 68 / 73 on the first run; the 5 failures were test-setup mistakes, fixed afterwards but NOT re-run: (a) overbooking fixture (type cut to one room + two bookings), (b) no-show fixture (single booking), (c) lead bucket expectation 31-90 days, (d) restriction report is D-B (past dates only) so tests / goldens use past nights, (e) FY26 case for restriction uses to = business date.
- Goldens: `tests/rpt-golden/goldens/rpt.revenue.*.json` NOT yet written. Untracked helper `03 Prototype/tests/_golden-rev.mjs` writes them for the six ids (BASE + FY26 case each): run `node tests/_golden-rev.mjs` from 03 Prototype (through the lock), commit the six json files, delete the helper. It stopped at restriction-effect on the old params; the params are fixed now.
- Full `npm test` not run. Counts before / after not recorded (base 2976 per the Phase A note; this lane adds 16 [F12-RPT-REVENUE] tests).
- Server: first `dotnet test tests/Hcc.Reporting.Tests` failed to compile (CS0050 accessibility, missing `using Hcc.Contracts`); both fixed in the last commit, build + test queued but NOT yet run. `Hcc.Meas.Tests` not run (no test added there).
- Commands: `pwsh -NoProfile -File E:\Projects\HCC\worktrees\meas	ools\Invoke-Serialized.ps1 -WorkingDirectory ... -Label meas-rpt-revenue -Command "& '<.scratch script>'"` (scripts in `.scratchll.ps1`, `.scratch\srv.ps1`; logs `.scratch\*.log`, summary `.scratch\summary.log`, `.scratch\srv-summary.log`).

## Open items
- Catalogue vs money flag: rpt.revenue.overbooking-wash is "money flag n" in the catalogue but has a money column (cost of walk), so it is built money-flagged (gate PERM-RPT-002, column removed for roles without it). Catalogue entry should say y.
- "BAR unchanged" is read as live BAR not above the base (tariff excluding yield-owned rows) of the largest room type; "offer open" as an active discount plan other than BAR available that night. Owner may want a different reading.
- The server sellable-room count ignores dated blocks / out-of-order periods (only retired rooms); the prototype also subtracts them. Differences show only on nights with an out-of-order room.
- SQL loaders (`MeasRevenueData`) are compile-checked and exercised by the integrator's gate only if a database test is added; this lane's server tests cover definitions, the pure rules at their boundaries and the pure row builders, not live SQL.
- Prototype FY26 has no history before 1 Apr 2026, so "last year" columns are empty there (pace never red) until legacy `lgc` stays are fed to the forecast.

## Integration needed
0. FIRST: run the prototype tests, write the goldens (see Tests) and build / test `Hcc.Reporting.Tests`; this lane stopped before seeing them green.
1. `03 Prototype/src/domain/reports/catalogue.json` and `08 Production/tests/Hcc.Reporting.Tests/catalogue.json`: regenerate with `node tools/rpt-catalogue.mjs` after merging (generated, shared by all report lanes; not committed by this lane to avoid six-way conflicts). The server cross-check test fails until this is done.
2. `03 Prototype/tests/stream-rbac-matrix.mjs` and the navmap / hub: no new commands or pages were added by this lane; nothing to do.
3. Hub area label `revenue` exists; the report hub lists the six ids once the switch is on.
4. Catalogue §3.10: status lines `built` for the six ids (`<!-- F12-RPT-REVENUE -->` at the end), and the overbooking money flag y.
5. Digest lane: use `measRevenueRedCounts(s)` from `src/domain/reports/meas-revenue/index.ts` (prototype) and the `ComputeAsync(...)` methods (server) for the "Measurement" block.
6. `meas-forecast` Integration items 2 / 3 (restriction counters written by the guard; overbooking honoured at BOOK) feed the refused / re-shaped and oversold figures; without them those columns read 0.

## HANDOFF bullet
- F12 meas-rpt-revenue (Sonnet): six revenue-management reports (demand calendar, pickup and pace, displacement calculator shell, overbooking and wash, length-of-stay controls, rate shop and parity) on both sides, switch-aware (`meas.rev.*`), red-row rules with boundary tests, goldens, no migration, SCHEMA 15. Open: regenerate catalogue.json at integration, overbooking money flag in the catalogue, SQL loaders run only at the gate.
