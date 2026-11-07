# MEAS-STUDY-A DONE (lane meas-study-a, branch claude/meas-study-a)

Nine legacy-study reports ported as pure analytics selectors over one `AnInput` (stays, bills, receipts, rooms, lgc history), prototype and server twin.

## What was built
| Report id | Switch | Money | Prototype selector | Server selector |
|---|---|---|---|---|
| rpt.study.revenue-monthly | meas.study.revenue-monthly | y | `revenueMonthly` | `LegacyRevenue.RevenueMonthly` |
| rpt.study.year-comparison | meas.study.year-comparison | y | `yearComparison` | `LegacyRevenue.YearComparison` |
| rpt.study.index-vs-base | meas.study.covid-index | y | `indexVsBase` | `LegacyRevenue.IndexVsBase` |
| rpt.study.fy-deep-dive | meas.study.deep-dive | y | `fyDeepDive` | `LegacyRevenue.FyDeepDive` |
| rpt.study.occupancy-history | meas.study.occupancy | n | `occupancyHistory` | `LegacyOccupancy.OccupancyHistory` |
| rpt.study.operations-patterns | meas.study.patterns | n | `operationsPatterns` | `LegacyOccupancy.OperationsPatterns` |
| rpt.study.rooms-and-rates | meas.study.rooms-rates | y | `roomsAndRates` | `LegacyOccupancy.RoomsAndRates` |
| rpt.study.zero-rate-complimentary | meas.study.zero-rate | y | `zeroRateComplimentary` | `LegacyZero.ZeroRateComplimentary` |
| rpt.study.receivables-bad-debt | meas.study.receivables | y | `receivablesBadDebt` | `LegacyZero.ReceivablesBadDebt` |

Files: `03 Prototype/src/domain/analytics/legacy-{core,input,revenue,occupancy,zero}.ts`, `src/domain/reports/study-a/*` (9 registrations + `shared.ts`), `tests/stream-meas-study-a.mjs`; `08 Production/src/Hcc.Reporting/Analytics/Legacy{Core,Revenue,Occupancy,Zero}.cs`, `Reports/MeasStudyA/StudyReports.cs`, `tests/Hcc.Reporting.Tests/MeasStudyA/*`. Reports are multi-section (`sections`), one section per workbook sheet; chart series are sections keyed `series.<name>` with title `bar|line|stacked|<name>` and columns label / value (for ChartBlock). No SQL, no migration, SCHEMA stays 15, no new package.

Definitions as contract section 9: nights from arrival (nights x rooms, same-day = 1); occupancy = distinct room-nights / (rooms in service x days in window), plus workbook alternatives A (rooms that sold) and B (fixed room count); ADR = room charge / charged nights by bill month; billed = total incl. GST; FY April-March; room 211 = Suite; COVID window excluded from normal years; DEC-265 legacy dues = bad debt (100 % provision, GST an estimate); DEC-266 zero-rate flag = complimentary OR zero room-charge bill OR zero stay rate (the explicit complimentary type is not on main). Percent = one decimal by integer arithmetic on both sides.

## Integration needed (exact edits outside my row)
1. `tools/rpt-catalogue.mjs` run once more by the integrator after the merge (catalogue.json in both places was regenerated here; it will conflict with the other lanes, regenerate, do not hand-merge).
2. `08 Production/tests/Hcc.Reporting.Tests/EngineTests.cs` `ServerCatalogue()` carries my `F12-STUDY-A` block (9 selectors) and the test csproj copies `MeasStudyA/*.json` (`F12-STUDY-A` block): keep both blocks when merging.
3. Production data source: `IAnalyticsInputSource` (Hcc.Reporting.Analytics) is registered with an empty default (`EmptyAnalyticsInputSource`, HccComposition F12-STUDY-A). The integrator or study-pack lane must register the SQL loader (stays, bills, receipts, `lgc.*`, rooms master) that builds `AnInput` with the same rules as `legacy-input.ts` (halls and duplicates out, one `AnStay` per room, bill date = departure). Prototype: `setLegacyAnSource(() => rows)` supplies the lgc history (production wiring of the fetcher, as for `setLegacyBillsFetcher`).
4. `AnInput` carries dates only. These workbook sheets need extra fields (types.ts is not mine): CheckIn_Hour, CheckOut_Hour, Turnover_EarlyLate, AdvanceLead (check-in / out time, booking date), Rate Overrides and Zero-Rate reasons (rate-change events with reason text), Debtor Contacts (phone, GSTIN). They appear as "Pending" rows; proxies are used (billed rate vs stay rate, stay purpose).
5. `meas-legacy-golden.ps1` (study-pack) compares the headline numbers below with the workbooks; legacy data is not in the repo.
6. Live bills: a stay's bill date = departure; legacy and live use the same code.

## Headline-number surface for the golden harness
Per FY the selectors give: billed (`fy-summary.billed`), room-nights (`fy-summary.sold` in occupancy-history), occupancy % (`fy-summary.occ`), ADR (`fy-summary.adr` in revenue-monthly), bad-debt total (`bad-debt-schedule` total row), zero-rate events (`zero-by-fy-category.total`). The FY26 fixture (fictional) values are in `08 Production/tests/Hcc.Reporting.Tests/MeasStudyA/fy26-parity.json` (input plus expected tables); the real FY table is filled by the golden harness on this PC.

| FY | billed | room-nights | occupancy | ADR |
|---|---|---|---|---|
| 2026-27 (FY26 fixture, Apr to 24 Oct) | see fy26-parity.json revenue-monthly / occupancy-history | | | |

## Counts
(see final lines below)
