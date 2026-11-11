# cu-rpt-a DONE

Lane rpt-a / branch claude/cu-rpt-a / base c45bbce3. Four prototype-only P1 reports now run on the server report hub: rpt.admin.audit-log, rpt.guests.daily-ops, rpt.money.cash-book, rpt.money.expenses-cash.

## Files changed
Server (08 Production), new folder `src/Hcc.Reporting/Reports/CuA/`: `CuASql.cs` (shared helpers + audit SQL), `AuditLogReport.cs`, `DailyOpsReport.cs`, `CashBookReport.cs`, `ExpensesCashReport.cs`.
Registry (appended in `// CU-RPT-A` blocks): `src/Hcc.Api/HccComposition.cs` (4 AddReport lines), `tests/Hcc.Reporting.Tests/EngineTests.cs` (ServerCatalogue list).
Prototype: `serverMirror:false` removed from the four reports (`src/domain/reports/built/admin.ts`, `money.ts`, `pack.ts`); `src/domain/reports/catalogue.json` and `08 Production/tests/Hcc.Reporting.Tests/catalogue.json` regenerated with `node tools/rpt-catalogue.mjs` (side becomes "both"). No client code change: the production hub (`reports-hub-run.ts`) routes by the catalogue flag, so the four ids now come from the server instead of "Not on the server yet".
Tests: `tests/Hcc.Reporting.Tests/CuA/` (CuARig, AuditLogReportTests, DailyOpsReportTests, CashBookReportTests, ExpensesCashReportTests).

## Figures (same columns / permissions as the prototype definitions; EngineTests cross-checks them against catalogue.json)
- audit-log: plat.audit_event, hotel-local time, newest first, view PERM-ADM-004, no money column. Detail only for OWN / MGR / ACC roles; amounts masked "₹ ••" without PERM-RPT-002.
- daily-ops: DailyPackCalculator (same as closing snapshot) + counted non-capital expenses by date; status Closed / Open / Future / dash. Gate PERM-RPT-002.
- cash-book: CashBookCalculator (same as the cash book page) days + range total + entry block with running balance. Gate PERM-RPT-002, date class short (62 days).
- expenses-cash: cash-book arithmetic per date + drawer-cash expenses via payment-method master + 7 range tiles.

## Tests run (through Invoke-Serialized)
- `dotnet test tests/Hcc.Reporting.Tests --filter FullyQualifiedName~Tests.CuA`: 19 passed, 0 failed.
- `--filter FullyQualifiedName~EngineTests` (catalogue cross-check incl. the four): 15 passed, 0 failed.
- `node tools/rpt-catalogue.mjs --check`: up to date (124 reports).
- `npm test` (03 Prototype, once): 3335 / 3336. The one failure, `[TRN] production bundle contains no part of the training driver`, is NOT from this lane: the production bundle holds the string "TRAINING" from `src/clients/sync/meas-people.ts` / `src/services/mock/handlers/meas-people.ts` (MEAS code), files this lane did not touch. Treat as pre-existing on the base; not fixed here.
Not run: full server suite, `dotnet test Hcc.slnx`, tsc (no TypeScript logic changed), APK, emulator, fit check.

## Not verified
- Closed-day figures against a real closing snapshot (tests seed closed days directly in cls.business_day).
- The prototype goldens vs server numbers on the same dataset (no shared dataset; tests assert hand-computed paise).
- Viewer behaviour in the app (no UI run).

## Defaults used
- audit-log: "detail" visible to roles OWN / MGR / ACC only (role-code test); a reader without it cannot search inside detail text. Id column = id part of the audit target, else the HCC-1234 style token / key in the detail, else the audit row id. Actor filter matches display or login name.
- audit-log is capped at 50,001 rows (runner then answers RPT_TOO_LARGE).
- cash-book: booking and guest columns of the entry block are blank (the server drawer movement has no booking); reference id is "CM-<movement id>"; status text is the server's (Closed / Open / Past, " · locked").
- expenses-cash: a date with no cash-book day shows a dash and blank cash figures; "bank" expenses = counted, non-capital, non-till, non-credit methods (unknown method counts as bank); cash in = payment + deposit + pos-sale movements, cash out = refund + deposit-return + pos-void movements (signed).
- daily-ops: financial date class refuses dates after the business date, so "Future" appears only in the pure builder (tested there).
- Cash-book / daily-ops do not use CashBookQueries / ReportingQueries (their 93 / 31 day caps); the hub's date class limits apply.

## Open questions
None blocking. Pre-existing TRN test failure (above) needs the owning lane.

## Shared files touched
`HccComposition.cs` and `EngineTests.cs` (my `CU-RPT-A` blocks; lane rpt-b appends its own block at the same spots, expect a trivial textual merge), both `catalogue.json` files (generated; rpt-b will regenerate too, so regenerate after merging), `built/pack.ts` (two single-line edits).
