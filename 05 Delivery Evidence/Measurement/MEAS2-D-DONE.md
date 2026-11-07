# MEAS2 lane D (hygiene) - done 7 Nov 2026

Branch claude/meas2-d (from claude/meas 79f7abe7). Verified each sizing-report claim in code first; line numbers had drifted slightly, all claims held.

## Fixed
- L1 banker's rounding: HkProductivityReport.cs (RoundMinutes helper, used in vacated-to-inspected and Mins), UtilitiesPerRoomReport.cs:44 (Use). All AwayFromZero. Lines 90-91 of HkProductivity already used AwayFromZero.
- L2 (HK utilities + pack Div): StudyPack/ManagementPackReport.cs PackRules.DivP is now integer, sign-aware, half away from zero (same results as before; exact above 2^53). UtilitiesPerRoomReport costPerNight now uses PackRules.DivP(spend, monthRn).
  NOT changed: unitCost = spend / monthKwh (kWh is a decimal quantity, so double stays; rounding rule unchanged), Div1 and Pct1 (percentages stay double, per report), MeasDailyReportsA and LinenCostReport (lane A / E files, not in lane D).
- L4: tests only (MeasHkReportsTests.cs): housekeeping caller (PERM-HK-001 + PERM-STK-001, no money gate) through ReportRunner for asset-life and linen-cost: money keys absent from columns and every row, listed in Removed; control run with the gate shows them. Uses the real Definition with a filled stub selector (no SQL).
- L5: LegacyOccupancy.AgeDays uses DateOnly.ParseExact yyyy-MM-dd, invariant, DayNumber difference. Time part (if any) is cut off at 10 characters.
- L6: StudyPackFailureLog (warning on first failure and every 30th in a row per property, information on recovery) and StudyPackTick (per-property try, so one bad property no longer stops the others; cancellation still propagates). StudyPackScheduleWorker takes ILogger<> (DI-resolved; registration unchanged).

## Tests added
MeasHkReportsTests.cs: 4 new cases (2.5 min -> 3; meter delta 0.125 -> 0.13; L4 x2).
StudyPackWorkerTests.cs (new): DivP x2, culture x3 (en-US, hi-IN, de-DE), worker log x3.

## Run
`Invoke-Serialized.ps1 ... dotnet test tests/Hcc.Meas.Tests --filter "FullyQualifiedName~MeasHkReportsTests|FullyQualifiedName~StudyPackWorkerTests"` -> Passed 28, Failed 0.

## Not run / unverified
Full Hcc.Meas.Tests, Hcc.Reporting.Tests (MeasStudyPackTests uses DivP/Div1) and the whole solution were not run. HkProductivity SQL path not executed against a database.
