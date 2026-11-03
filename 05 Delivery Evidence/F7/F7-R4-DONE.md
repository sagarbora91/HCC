# F7-R4 DONE — money-a (batch 1 money reports)

Lane R4 · Sonnet · branch `claude/f7-r4` (worktree `/home/user/wt-f7-r4`) · base `526e0776`.

## Reports (prototype AND server, status `built`)
| Id | Prototype | Server | Notes |
|---|---|---|---|
| `rpt.money.shift-report` | `03 Prototype/src/domain/reports/money/shift-report.ts` | `Hcc.Reporting/Reports/Money/MoneyReports.cs` `ShiftReport` | X (open) / Z (closed) per shift; receipts / payments from the drawer movements, expected = opening + receipts - payments; hand-over from the cash-out records; second block "Totals per cashier"; Cashier own shifts only, view + print, no export |
| `rpt.money.collection-detail` | `collection-detail.ts` | `CollectionDetailReport` | stay receipts, counter-bill receipts, company payments (TDS), Udhaar payments; Include receipts / advances / corrections; group by tender / bill / user; summary by tender block; Cashier own entries |
| `rpt.money.credit-bills` | `credit-bills.ts` | `CreditBillsReport` | company-credit and guest-credit receipts + company counter bills; limit; used after this bill = running credit - payments up to its date |
| `rpt.money.expense-register` | `expense-register.ts` | `ExpenseRegisterReport` | partial → built; ledger rows with GST / rate / ITC; subtotal per category + grand total |
| `rpt.money.expenses-by-head` | `expenses-by-head.ts` | `ExpensesByHeadReport` | group / head / entries / amount / share |

Shared: `money/common.ts` (prototype), `MoneyRules` (server). Server reads: `Hcc.Reporting/Sql/SqlMoneyReportSource.cs` (`IMoneyReportSource`, plain SELECTs, no new tables) and `IExpenseStore.LedgerAsync`. Registered with `// F7-R4` blocks in `register.ts` and `HccComposition.cs` (F7-RPT). **No migrations used** (4424–4427 free).

## Ties (tests)
Prototype `tests/stream-rpt-money-a.mjs` (18 tests): shift rows = closing record (opening / expected / counted / difference) and opening + receipts - payments = expected on every day of the April month and FY26; receipts / payments = closing pack drawer metrics; collection receipts + corrections of stays = Receipts & deposits net receipts, advances = deposits in + returned (+ applied); group subtotals = grand total = summary by tender; credit = company-credit receipts; expense register and expenses-by-head grand totals = closing pack expenses total (April month and FY26); roles (Owner / Manager / Accountant allowed + export; Reception, Housekeeping, Maintenance refused; Cashier own-shift on shift report and collection detail only, no export); finance-gate deny and export deny; date-class edges; provisional; CSV neutralisation; three-way (screen = Excel = print) through the golden harness.
Goldens: `tests/rpt-golden/goldens/rpt.money.{shift-report,collection-detail,credit-bills,expense-register,expenses-by-head}.json` — three cases each (default BASE, ACC-RULEBOOK April 2026, FY26 1–24 Oct). `catalogue.json` regenerated (both copies).
Server `Hcc.Reporting.Tests/MoneyAReportsTests.cs` (15 tests, builders driven with in-memory facts + an `IExpenseStore` DispatchProxy): same tie-outs, Cashier / Reception / HK / Maintenance / unknown / Accountant matrix, finance-gate and export deny, date edges, three-way (Excel + PDF text + print) for all five, CSV neutralisation. `Hcc.Reporting.Tests` 42/42, `Hcc.Configuration.Tests` 35/35.

## Rulings / deviations to know
- Shift-report and collection-detail carry `ownShift:true` (Cashier V·S); the other three are Owner / Manager / Accountant. Shift report view = PERM-RPT-002 only, so without the money gate the report is refused (nothing to blank).
- Expense register: view = PERM-EXP-001 **or** PERM-RPT-002, money gate PERM-RPT-002 (catalogue says EXP-001; the matrix gives the Accountant EXP-001 as own-only, so the finance gate carries the register). Totals count Posted / Reversed (+ reversal records) and leave out capital purchases (asset register), like the closing pack; Pending / Rejected / Void rows are listed.
- Expenses by head: HCC has no expense-group master, so Group = Operating expenses / Petty cash / Recurring bills (by expense source). Share to one decimal.
- Credit bills "Used after this bill" excludes balances carried in from before HCC (the demo's `openingOutstanding` has no server column; accounting holds them). Both sides equal.
- Server receipt numbers are `RC-000123` (no stored receipt number); company payments `GCP-n`, Udhaar payments `UDP-n`. Udhaar payment reversals are not listed on the server (prototype lists the negative settlement).
- Server shift label is `<till name> (Z)` / `(X, open)`; the prototype has one drawer shift per day, `Day (Z)`.
- Engine touches (small, flagged `F7-R4`): `Renderers.cs` percent cells written via decimal division (57.1 writes 0.571, not 0.5710000000000001); `RendererTests.ExcelOf` handles `DateOnly`; `EngineTests.ServerCatalogue` lists the five new reports.

## Tests run
`npx tsc --noEmit` clean; `dotnet build "08 Production/Hcc.slnx"` 0 warnings, 0 errors; `Hcc.Reporting.Tests` 42/42; `Hcc.Configuration.Tests` 35/35. Full `npm test`: only two failures, both `[F7R2]` in `tests/stream-rpt-hub.mjs` (lane R3 owns it: the tests assume an empty catalogue, but revenue-by-category-tender is registered since R1) — not touched; `stream-rpt-engine.mjs` (incl. golden drift for all six reports) 15/15; `stream-rpt-money-a.mjs` 18/18. (The second full npm run was killed by the sandbox near the end; the first full run, 2610/2616, had these two plus failures since fixed.)

## HANDOFF bullet
F7-R4 money-a done (claude/f7-r4): shift report (X / Z + hand-over), collection detail, credit bills, expense register, expenses by head on the engine both sides, goldens on the ACC-RULEBOOK April month + FY26, no migrations.
