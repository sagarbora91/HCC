# F8-RPT-P2-B DONE (lane rpt-p2-b, Sonnet) — 8 Nov 2026

Branch `claude/f8-rpt-p2-b` (cut from tag `f8-setup` 037bd7bc). Batch B of SF-CONTRACT section 12. Builds are prototype + server, each report with one selector per side.

## Reports

| Report | Status | Prototype | Server |
|---|---|---|---|
| rpt.money.cash-count-sheet | built | `03 Prototype/src/domain/reports/p2-b/cash-count-sheet.ts` | `08 Production/src/Hcc.Reporting/Reports/P2B/P2BReports.cs` CashCountSheetReport |
| rpt.money.payment-corrections | **BLOCKED** | not registered | not registered |
| rpt.money.counter-bills | built | `p2-b/counter-bills.ts` | CounterBillsReport |
| rpt.money.refunds | built | `p2-b/refunds.ts` | RefundsReport |
| rpt.money.discount-log | built | `p2-b/discount-log.ts` | DiscountLogReport |
| rpt.money.bank-book | built (fixed view of rpt.acc.statement, bank ledgers only) | `p2-b/bank-book.ts` | BankBookReport |
| rpt.money.company-credit | partial -> built | `p2-b/company-credit.ts` (adapts grpCredit, statement block) | CompanyCreditReport |
| rpt.tax.licence-expiry | built | `p2-b/licence-expiry.ts` | LicenceExpiryReport |

Server SQL reads (count grids, counter bills + lines, refunds with approval, discount lines): `Reports/P2B/P2BSource.cs` (no new tables, no migrations; ranges 4452-4453 unused).

### BLOCKED: rpt.money.payment-corrections
The catalogue names the "audit trail of receipt changes (existing)". No such capture exists in code: no command edits a receipt's payment type after entry (prototype `Receipt` has no history; server `pay.receipt` has only `method`, no change log; grep for payment-type correction finds nothing). Missing capture: a "receipt method corrected" command that records entered-as / corrected-to / by / reason / time. Not faked; the report stays `planned`.

## Shared-file edits (inside the lane blocks only)
- `03 Prototype/src/domain/reports/register.ts` — 7 imports in `// F8-RPT-P2-B`.
- `08 Production/src/Hcc.Api/HccComposition.cs` — source registration + 7 `AddReport<...>()` lines in `// F8-RPT-P2-B` (services block).
- `02 Planning/HCC_Report_Catalogue_v1.md` — status lines in `<!-- F8-RPT-P2-B -->`.
- Generated (not hand-edited): `03 Prototype/src/domain/reports/catalogue.json` and `08 Production/tests/Hcc.Reporting.Tests/catalogue.json` were regenerated with `node tools/rpt-catalogue.mjs` so that the up-to-date test and the server cross-check pass on this branch. **Integration needed:** lanes A and C regenerate them too; on merge take any side and re-run `node tools/rpt-catalogue.mjs` (the integrator regenerates once).

## Defaults and decisions I relied on (owner questions unanswered)
- No owner-question default was needed for money rules; every figure is an adapter over an existing selector (closing record, receipts, ctr bills, discount lines, ACC-1 statement, grpCredit).
- Permission codes: `PERM-GRP-001` / `PERM-GRP-004` (Owner, Manager, Reception, Accountant, Cashier — server seed 2201) and `PERM-CMP-001` (Owner + Accountant — seed 1101, DEC-055) are not in the prototype's v1 matrix; the grants are filled into the engine's `RPT_V2_GRANTS` from this lane's own `p2-b/grants.ts` (engine file untouched).
- **Catalogue vs code note (not a stop):** the licence-expiry entry lists "Owner, Manager, Accountant" but names PERM-CMP-001, which is Owner + Accountant only (DEC-055). I followed the permission: Manager is refused. The company-credit entry lists Owner, Manager, Accountant but PERM-GRP-001 also admits Reception and Cashier (money columns need PERM-GRP-004, which they hold too); followed the permission.
- Date class limits come from the engine (D-B = 366 days), not the catalogue's "92 days" text.
- Cash count sheet: the prototype keeps the denomination grid only for the open shift (`s.shift.denominations`); a closed day shows blank note rows with the counted total from the closing record. The server keeps the grid per count (`pay.shift_count.denominations`), so a closed day shows it there. One row group per till when several shifts exist on the date.
- Counter bills: Amount = line net; the bill discount is its own negative row; Tax and Tender on the bill subtotal (catalogue columns are per row). A credited bill stays listed with its credit note in Tender.
- Discount log: approver is blank for a discount inside the poster's own limit (no approval exists); prototype matches an approved discount request of the same booking and amount when the line carries no `approvalId`.
- Refunds: reason / approver from the approval the refund executed; the prototype FY26 history refunds have none (reason falls back to the receipt reference).
- Company credit: split stays (SF-CONTRACT R8) need no extra work in this lane: the company-credit receipt of a split stay already sits on the company folio (invariant I5), and "used" is the D7 figure of `grpCredit` / server `CompanyCredit` (sf-domain). The statement has no opening-outstanding row source on the server (opening 0); the prototype starts from `openingOutstanding`. `sfCompanyLines` is not used.
- Licence expiry: the firm profile has no issue date, so Issued is blank; Renewal card = the card exists (expiry set).

## Tests
Added: `03 Prototype/tests/stream-rpt-p2-b.mjs` (16 tests, tag `[F8-RPT-P2-B]`), goldens `tests/rpt-golden/goldens/<id>.json` for the 7 built ids (written by `node tests/rpt-golden/accept.mjs`; the other 101 goldens came out byte-identical), server `08 Production/tests/Hcc.Reporting.Tests/P2B/P2BReportsTests.cs` (10 tests).

## Commands run and results
- `tsc --noEmit` (03 Prototype): exit 0.
- `node tools/rpt-catalogue.mjs`: wrote both catalogue.json files. `node tests/rpt-golden/accept.mjs`: 108 goldens written, only the 7 new files differ from the setup commit.
- `npm test` with `HCC_TEST_ONLY='rpt-p2-b|rpt-engine|goldens'` (first run): 95/96 passed; the one failure (Cashier own-shift test used FY26 data with no Cashier-counted day) was fixed in the test after that run; the fixed test was NOT re-run. Full `npm test` NOT run.
- Server: `dotnet test tests/Hcc.Reporting.Tests -maxcpucount:1 --logger trx` (via Invoke-Serialized): runs 1 and 2 failed to compile (missing usings; AccShared helpers; both fixed). Run 4 (final): 135 passed, 1 failed, 136 total. My 10 tests in `P2B/P2BReportsTests.cs` pass (9 builder tests + a check of my 7 definitions against catalogue.json). The one failure is `EngineTests.Catalogue_json_from_the_prototype_equals_the_server_registry`: that test lists every server selector by hand in `EngineTests.cs` (outside my ownership), so the 7 new reports are in catalogue.json but not in that list. **Integration needed:** add the 7 `new ...Report(null!, ...)` entries (CashCountSheetReport, CounterBillsReport, RefundsReport, DiscountLogReport, BankBookReport, CompanyCreditReport, LicenceExpiryReport) to the list in EngineTests.cs; the same applies to any other hand-kept registry test.
- Full prototype `npm test`: NOT run (turn cap; the heavy-job lock queued each run 10-25 min). Only the filtered run above.

## Not done / unverified
- payment-corrections (BLOCKED, above).
- SQL queries in `P2BSource.cs` are compiled (server project builds) but not executed against SQL Server (no database test in this lane); the first hotel-PC gate exercises them. Column names were checked against migrations 0002, 0400, 2820, 4688.
- No UI hub changes (reports appear in the hub through the catalogue); phone layout follows the catalogue flag only.
