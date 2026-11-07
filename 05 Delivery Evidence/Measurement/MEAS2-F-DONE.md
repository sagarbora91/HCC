# MEAS2-F: SQL smoke test of the 32 measurement reports

Branch `claude/meas2-f` (cut from a43a9316). New files only:
- `08 Production/tests/Hcc.Meas.Tests/Reports/MeasReportSqlFixture.cs` (one fresh HccTest_* database, one fictional property "Meas smoke", business date 5 Nov 2026)
- `08 Production/tests/Hcc.Meas.Tests/Reports/MeasReportSqlTests.cs`

## What is covered
- All 32 `SelectAsync` calls as the Owner (theory, one case per report): no exception, report id, expected row count. A guard test asserts exactly 32 distinct ids.
- Pinned values for the money reports and many others (flash, voids, high balance, cashier, rate variance, regrets, upsell, service recovery, linen, OOO, utilities, asset life, pickup, overbooking, rate parity, restriction effect, scorecard, ...).
- Three re-posted nights are seeded exactly as StayRateCommands writes them (original room line marked reversed_by, reversal of kind `correction` with reverses_charge_id dated the business date, new room line, gst.stay_rate_change with reposted_json): approved (S3), Owner direct (S1, S4).
- N1: `SqlAnalyticsInputSource` bill B-SM-1: room 450000, total 604000, 2 charged nights (re-post counted once).
- N2: asset-life reserve accrued 49500 = 3 % of 1,650,000 live room lines (old query: 71,100); red because capex 60000 is above it.
- N3: voids report: rate re-posts are kind `rate-change` with the change's user/approver (approved, and Owner-direct), discount has its approver, refund listed, only the real void (no approver) is red.
- N4: room discrepancy at 15:00 local, offline set 14:00 local: age 1 h, not red; the sold-but-vacant row is 3 h and red.

## Run (actually executed)
`pwsh tools\Invoke-Serialized.ps1 -Label meas2-f -Command "dotnet test tests/Hcc.Meas.Tests -maxcpucount:1 --filter FullyQualifiedName~Hcc.Meas.Tests.Reports"`
Result: 59 passed, 0 failed (32 report cases + 27 facts). Only the Hcc.Meas.Tests project was run; the full solution was not.

## Findings (no production code changed; no test skipped)
No report threw and no SQL error was found. Two behaviours differ from what a reader would expect; both are pinned as they are today:
- F-1 (low, KPI): `UtilitiesPerRoomReport.cs` (month row, `monthRn` ~ line 549 and `OccupiedAsync` call ~ line 522 over the whole month) divides the month's spend and kWh by the occupied nights of the WHOLE month, including future booked nights of in-house guests. With the To date 5 Nov the fixture gives 10 nights (cost per night 58000) where nights to the To date are 8 (72500). Expected: nights up to the To date for a month in progress. Test `Utilities_pins_unit_cost_and_cost_per_occupied_night` pins 58000 with a comment.
- F-2 (low, wording): `PickupPaceReport.Nights` keeps a NoShow reservation on the books (only cancelled ones are removed), so a past no-show night counts in "Rooms on books" and revenue. Probably the prototype twin; check before changing. Pinned (17 rooms, 4,340,000).
Also noted, not tested: `rpt.rooms.room-discrepancy` has no SQL branch for "vacant-but-occupied" (already stated in the sizing report).

## Not done / unverified
- Study reports (21) are not part of the 32 and are only touched through the N1 loader check.
- Settings-driven targets use the contract defaults (no ISettingsReader).
- Left-over HccTest_* databases from earlier runs on this PC were not touched.
