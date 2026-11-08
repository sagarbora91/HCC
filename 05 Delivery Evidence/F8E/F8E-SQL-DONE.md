# F8E-SQL DONE (lane sql, Sonnet 5.5), 9 Nov 2026

Branch `claude/f8e-sql` from main 674929a3. Head hash: see `git log -1` of the branch. Tests only; no production source changed.

## Added
`08 Production/tests/Hcc.Reporting.Tests/Sql/F8ESqlGapTests.cs` (6 SQL tests, disposable HccTest_* database via `ReportDatabase`). Each seeds data, asserts the exact facts from the real SELECT (paise/qty/days), then feeds them to the report's own pure builder for totals.
- GRN register (`SqlP2CReportSource.GrnsAsync`): 40x15050 + 200x1275 etc, total 1,010,815 paise, supplier block, blank bill when ref = GRN number; edges: out-of-range GRN, other property, one-day inclusive range, empty period (no total row).
- Sales by item (`SalesAsync`): pre-tax value across a 5 % and an 18 % bill, snapshot price, legacy-category fallback, total 95,000 / 10 units, share 38.9 %, outlet filter; edges: voided line, voided bill, open check, other date excluded; empty period.
- POS voids (`VoidsAsync`): approver from executed void request, no approver when none, check key falls back to POS-<order>, whole-check void stamp 14:00 IST from 08:30 UTC, reason fallback to the check's reason, total 52,000 paise; edges: out-of-range, empty period.
- Leave balances (`LeaveAsync` + `LeaveBalancesReport.Build`): policy 10/6/14, per-employee CL override 10.5, SCC defaults 12/7/15 with no policy row, FY 1 Apr to 31 Mar boundaries, Rejected/Cancelled/LOP/other-year excluded, half day 0.5, as-on date logic, leaver listed before leaving and not after; empty year.
- Counter bills (`SqlP2BReportSource.CounterBillsAsync`): multi-rate bill (5 % food + 18 % laundry, discount 5,000 at dominant rate) cross-checked against `CounterBillRules.Totals` (75,000 / 7,650 / 82,650), Udhaar and Credited tender text, grand total net 110,000 tax 12,000; edges: out-of-range bill, empty period.
- Cash-count grids (`CountGridsAsync`): latest count of two used, notes sum + coins = counted 386,050 paise, no-breakdown shift gives null grid, empty-braces grid, other date / no count / other property excluded; empty date.

## Run
`dotnet test tests/Hcc.Reporting.Tests -maxcpucount:1 --filter F8ESqlGapTests` via Invoke-Serialized: **6/6 passed** (first run after fixing compile slips in the test file). Nothing else run (no full project, no solution).

## Bugs found
None. No production code changed.

## Defaults relied on
None from the owner-questions file (read-only report SELECTs; no money rule decided). Company-settled counter bills not seeded (needs a company account row); GRN items seeded only as linen/amenity.

## Not covered
Company-credit counter bill tender text ("Company credit"); count grid via the full CashCountSheetReport (needs the money source); SalesByItem own-shift filtering by policy.
