# F8D-TESTS-B DONE (lane f8d-tests-b, Sonnet 5.5), 8 Nov 2026

Branch `claude/f8d-tests-b` (from claude/f8c 0af7a992). Test files only; no production source touched. Head hash: `git log -1` of the branch.

## Tests added
- `08 Production/tests/Hcc.Integration.Tests/F8dTestsBTests.cs` (10 tests, HTTP + SQL):
  - per-folio DocumentSql print: folio A and B print only their own lines and totals (A Laundry 590, B room night 3,150), export log has both rows; unknown folio is 4xx, Housekeeping 403; **skipped** `company_folio_names_the_company_as_payer` (bug B1).
  - night-audit endpoint: eight lines in order, blocking flags (keys/backup never block), nights line follows PostRoomNight, show-only (day still closes); roles (Owner 200, Reception/Housekeeper 403, anonymous 401); key-card signal (superseded/checked_out/lost not counted, active+manual on one room = "1 room still with an active card"); backup signal (failed run and RestoreDrill ignored, yesterday's good run shown with its hotel time, today's good run ok at 05:00).
  - no-show-due endpoint: lists the unarrived confirmed arrival of the business date only (not tomorrow's, not the checked-in one), read only (status Confirmed, no charge), MarkNoShow removes it; before the cut-off (business date 2099) empty, Housekeeping 403, anonymous 401.
  - folio-join audit (R9) on a split stay: movement register (one arrival + one departure row, balance due 315,000 not doubled), desk folio list (one row), MEAS high-balance (one row, balance 315,000), MEAS manager's flash (Arrivals 1, In-house 1, ADR 300,000).
- `08 Production/tests/Hcc.Ota.Tests/Sql/OtaOverbookCapSqlTests.cs` (3 tests): `yld.overbook.Deluxe` cap (a fake ISettingsReader) is added to the staff push preview of that type only, unset = identical output, clamp 0..10, and to the automatic push rows the projector queues.
- `08 Production/tests/Hcc.Reporting.Tests/P2B/P2BP2CSourceSqlTests.cs` (1 test): first SQL execution of `SqlP2BReportSource.DiscountsAsync/RefundsAsync` and `SqlP2CReportSource.CancelledAsync/NamesAsync` with key totals (discount 200 kept, reversed 100 not listed; refund 300 with reason/approver; correction 100).

## Runs (all through Invoke-Serialized.ps1, label f8d-tests-b, filtered)
- `dotnet test tests/Hcc.Integration.Tests --filter FullyQualifiedName~F8dTestsBTests`: 9 passed, 1 skipped, 0 failed (10).
- B1 test un-skipped once: FAILS as expected ("Deccan Agro Ltd" not found); skip restored (not re-run after restoring).
- `dotnet test tests/Hcc.Ota.Tests --filter OtaOverbookCapSqlTests`: 3/3.
- `dotnet test tests/Hcc.Reporting.Tests --filter P2BP2CSourceSqlTests`: 1/1.

## Bug found (not fixed)
- **B1** `08 Production/src/Hcc.Reporting/Documents/DocumentSql.cs:36`: `FolioHeaderAsync` builds `FolioHeader` from five reservation columns and never sets `InvoiceNo`, `BillTo`, `BuyerGstin` (DocumentBuilders.cs:18, used at :91-94). Every folio prints Bill to = the stay's guest, empty Buyer GSTIN and "Invoice no: Not yet issued", so the company folio B of a split stay does not name the company or its GSTIN, and an issued invoice is never shown. Fix: read `bill.folio.payer_*` and `ivc.invoice` for that folio.

## Not written / not run
- P2 report key totals not covered by SQL: GRN register, sales-by-item, voids, leave balances (`SqlP2CReportSource.GrnsAsync/SalesAsync/VoidsAsync/LeaveAsync`), counter bills and cash-count grids (`SqlP2BReportSource.CounterBillsAsync/CountGridsAsync`): not seeded (many NOT NULL tables), still unexecuted against SQL Server. P2A already has SQL tests.
- SF-GOLDEN / H6-H9 / R6,R8,R9 left to the other lane. The full suites were not run.
- The no-show test relies on the real wall clock being later than business date 2026-10-24 (cut-off passed) and earlier than 2099-01-01.

## Defaults relied on
SFQ-6 (night posting never refused), QWQ-1 (night audit show only), QWQ-7 (no-show-due read only), M9 cap clamp 0..10. No owner answer was needed.
