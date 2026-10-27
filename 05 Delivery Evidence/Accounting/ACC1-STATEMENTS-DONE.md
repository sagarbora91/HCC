# ACC-1 lane acc1-statements — DONE (27 Oct 2026)

Lane: ledger / party statement, six-column trial balance, day book (contract §6, §9; rulebook §7.1–7.3, §G2).
Branch `claude/acc1-statements` (base `39930edc`), worktree `E:\Projects\HCC\worktrees\acc1-statements`. Not pushed.

## Commits

| commit | what |
|---|---|
| `b45308d8` | prototype: `accounting-reports.ts` (statement, six-column TB, day book, CSV builders), mock barrel line, `tests/stream-accounting-reports.mjs` |
| `27dd5a78` | server: `AccountingReportQueries.cs`, `AccountingReportContracts.cs`, `AccountingReportEndpoints.cs`, `HccComposition.cs` ACC-1 line, `Domain/ReportQueriesTests.cs`, `Sql/ReportQueriesSqlTests.cs`; prototype test names made chart-rename-proof (WIP commit after the PC restart) |
| `4c448714` | line endings restored to LF: a Windows text-mode rewrite had turned `HccComposition.cs` (and three lane files) to CRLF in `27dd5a78`; against the base the net change to `HccComposition.cs` is again the one appended line |
| `13d7e444` | DONE file (draft) |
| `2379e39f` | merge of `claude/acc1` (`83be423d`, SCHEMA-11 test re-pins only) |
| (last) | DONE file with the final counts |

## Files

New (owned):
- `03 Prototype/src/domain/streams/accounting-reports.ts`
- `03 Prototype/tests/stream-accounting-reports.mjs` (10 tests, `[ACC1-STATEMENTS]`)
- `08 Production/src/Hcc.Application/Accounting/AccountingReportQueries.cs` (`AccountingReportQueries`, pure `AccountingReports`, `LedgerNames`, ports `IReportOpeningSource` + `ReportOpening`, `IReportPartyNames`)
- `08 Production/src/Hcc.Contracts/Accounting/AccountingReportContracts.cs` (views)
- `08 Production/src/Hcc.Api/Endpoints/AccountingReportEndpoints.cs`
- `08 Production/tests/Hcc.Accounting.Tests/Domain/ReportQueriesTests.cs` (6 tests)
- `08 Production/tests/Hcc.Accounting.Tests/Sql/ReportQueriesSqlTests.cs` (2 tests)

Append-only shared files (one line each, inside the `// ACC-1` blocks):
- `03 Prototype/src/services/mock/index.ts`: `export * from '../../domain/streams/accounting-reports'; // acc1-statements`
- `08 Production/src/Hcc.Api/HccComposition.cs`: `api.MapAccountingReportEndpoints(); // ACC-1 acc1-statements`

Not touched: `accounting.ts` (only imported), `SqlAccountingStore.cs`, the UI, the other lanes' files. **No `SqlAccountingStore.Reports.cs` and no `partial`**: the reports need no new store read — they reuse `AccountingDays.RangeAsync` (sources + lock snapshots + effective mapping) and the Import module's `IOpeningBalanceSource`. **No migrations** (4225–4229 unused; no view was needed).

## Built against the contract

**§6 shapes, prototype — copied exactly:** `ACCStatementRow`, `ACCStatement`, `ACCTB6Row`, `ACCTB6` (a test pins every field set). Selectors `accStatement(s, account, from, to)`, `accTrialBalance6(s, from, to, detail)`, `accDayBook(s, from, to)`. The contract names no day-book type, so the lane added `ACCDayBookRow {date, no, type, narration, account, ledger, debit, credit, source}` and `ACCDayBook {from, to, rows, vouchers, debit, credit, balanced}`. CSV builders: `accStatementCsv`, `accTrialBalance6Csv`, `accDayBookCsv` (CRLF, rupees with 2 decimals, the existing `csvMinimal` formula guard; headers `ACC_STATEMENT_CSV_HEADER`, `ACC_TB6_CSV_HEADER`, `ACC_DAYBOOK_CSV_HEADER`). Display helpers: `accDrCr(n) → {amount, side}` and `accDrCrText(n)` (`1234.56 Dr` / `987.00 Cr`) — never a negative number on screen or in the CSV. Other helpers: `accRptControl`, `accRptParty`, `accRptLedgerName`, `accRptLedgerGroup`, `accRptIsOpening`, `accRptVouchers`, `accRptOpeningPending`.

**§6 server:** `GET /accounting/statement?account=&from=&to=`, `GET /accounting/day-book?from=&to=` (mapped by `MapAccountingReportEndpoints`), and the six-column trial balance as `AccountingReportEndpoints.TrialBalanceAsync(http, from, to, detail, ct)`. That one needs the dispatch in "Integration needed" 1, because the path is already taken by the existing `?date&cumulative` trial balance. Permission PERM-ACC-001 (the real P1 policy refuses the Cashier, per migration 3700 / DEC-079, and Housekeeping). Range: `from ≤ to`, at most 366 days (VALIDATION). Views: `StatementView`/`StatementRowView`, `TrialBalance6View`/`TrialBalance6RowView`/`TrialBalance6TotalsView`, `DayBookView`/`DayBookRowView`. These carry the same fields as the TS contract with the server's `Paise` suffix on money fields (`opening` = `OpeningPaise`, `running` = `RunningPaise`, `openingDebit` = `OpeningDebitPaise` …).

**Rules as built (both sides identical):**
- **Opening** = every voucher dated before `from`: a locked date reads as its snapshot, any other date as the live derivation. The prototype reads `accVouchersUpTo`, which carries the opening entry once acc1-opening lands it, and in production the server window's opening. The server reads `AccountingDays.RangeAsync(MinValue, to)` plus the opening entry from `IReportOpeningSource`, and otherwise the imported opening (`IOpeningBalanceSource`, N5). An approved opening entry supersedes the import, per contract §5.
- **An opening voucher dated ON `from`** (sub `opening-entry` / `opening-import` / `opening`, or kind `opening`) also counts as opening. This follows rulebook §7.2 "incl. opening journal", so a report from the FY start opens with the opening journal. See "Contract notes" a.
- **Statement:** a ledger code or control takes its party ledgers too (`1110` = `1110` + `1110/*`); a party ledger takes only itself. There is one row per voucher, with the matching entries summed. `counter` = the distinct names of the other entries' ledgers (or of its own, when the voucher has no other side). `running` and `opening`/`closing` are signed Dr + / Cr −.
- **Six-column TB:** opening and closing are netted per row (never Dr and Cr together); period columns are gross. `detail = false` sums `<code>/<party>` into the control. `detail = true` gives a row per party (`control` set), placed right after its control's own row, which appears only when something posted to the control itself (a `no-party` entry), so totals never double-count. Rows with nothing in any column are left out. Order: chart order, then parties by name. `balanced` = all three column pairs equal. Summary and detail period totals tie; closing totals differ only by netting, and each mode balances.
- **`openingPending`** (rulebook §G2) = no opening entry, OR not approved, OR a non-zero 1400 Opening-balance difference. Prototype: reads `s.accountingX.opening` (`status`, lines). Server: reads `IReportOpeningSource` (`Approved`, the voucher's 1400 line).
- **Day book** = the period vouchers (an opening voucher dated on `from` belongs to the opening), in `date, no` order, one row per entry, with the totals and voucher count.
- **Names:** an Owner override / Owner-added ledger (`s.accountingX.chart.ledgers`) wins, then `ACC_CHART`, then the production `server.accounts`. A party ledger is named `<control name> · <party>`, where `<party>` is the party id until "Integration needed" 2/3 is done. The group of a party is its control's group. The prototype group is whatever the chart row carries, so it becomes the Tally group id automatically when acc1-chart changes `ACC_CHART`.

## Tests

| suite | before (base `39930edc`) | after (this branch) |
|---|---|---|
| prototype `npm test` | 1867 (1854 passing on `39930edc`: 13 SCHEMA-11 pin failures; 1867/1867 after the coordinator's `83be423d`) | **1877 / 1877** after merging `claude/acc1` (`83be423d`) — +10 `[ACC1-STATEMENTS]`; before that merge 1864 / 1877 with the same 13 base failures |
| server `dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1` | 58 | **66 / 66** (+6 Domain `ReportQueriesTests`, +2 Sql `ReportQueriesSqlTests`) |
| `npx tsc --noEmit` | — | exit 0 |
| `npm run build` | — | exit 0 |
| `dotnet build Hcc.slnx` | — | 0 warnings, 0 errors |

**The 13 failures seen before the merge were not this lane's** (now re-pinned on the wave branch by `83be423d` and merged here). They were the coordinator's `SCHEMA` 10 → 11 bump in `39930edc`: `[S8] SCHEMA is 10` and `[EXP-DEC] … SCHEMA unchanged` (`11 !== 10`), plus the pinned BASE / FY26 hashes in `[CAT] [FDM] [HIST-A] [MFX]×2 [MST] [OPSM] [PPL] [PERF] [POS-MST] [RMS]`. The lane's 10 tests pass in every run.

The prototype test `[ACC1-STATEMENTS] with the acc1-opening lane merged …` **returns early until `M.accOpeningVoucher` exists**. On `claude/acc1` after the opening lane it drives `ACC_OPENING_SAVE {asOf:'2026-04-01', lines}` (as `manager`) and `ACC_OPENING_APPROVE {}` (as `owner`), per contract §5. The other tests inject party ledgers and an `opening-entry` voucher as a locked snapshot, so they do not depend on other lanes.

Logs (uncommitted): `E:\Projects\HCC\worktrees\acc1-statements\.scratch\` (`npm-test-2.log` (before the merge), `npm-test-3.log` (after it), `tsc-2.log`, `npm-build-2.log`, `dotnet-build-2.log`, `dotnet-test-2.log`, the `.trx`).

## Integration needed (files this lane does not own)

1. **`08 Production/src/Hcc.Api/Endpoints/AccountingEndpoints.cs` — six-column TB on the contract path.** Two `MapGet("/accounting/trial-balance")` would be ambiguous in ASP.NET Core, so replace the existing trial-balance line with a dispatch:
   ```csharp
   g.MapGet("/accounting/trial-balance", async (HttpContext http, AccountingQueries q, CancellationToken ct, DateOnly? date = null, bool cumulative = false,
       DateOnly? from = null, DateOnly? to = null, bool detail = false) =>
       from is { } f ? await AccountingReportEndpoints.TrialBalanceAsync(http, f, to ?? f, detail, ct).ConfigureAwait(false)
       : date is { } d ? Results.Ok(await q.TrialBalanceAsync(http.HccCaller(), d, cumulative, ct).ConfigureAwait(false))
       : throw Hcc.Domain.Errors.HccException.Validation("Give \"date\" (day / cumulative) or \"from\" and \"to\" (six columns)."));
   ```
   The old `?date&cumulative` behaviour is unchanged.
2. **acc1-parties, prototype:** in `03 Prototype/src/domain/streams/accounting-reports.ts`, replace the body of `partyName` with the parties lane's resolver: `import {accPartyName} from './accounting-parties';` and `const partyName=(s:State,account:string)=>accPartyName(s,account);`. Match its real signature; it is used for display and for ordering the detail rows.
3. **acc1-parties, server:** implement `IReportPartyNames` (`<control>/<party id>` → master row name) over the parties lane's party list and register it as a singleton. `AccountingReportEndpoints` picks it up with `GetService`. Absent, a party shows as `<control name> · <party id>`.
4. **acc1-opening, server:** implement `IReportOpeningSource` over `SqlOpeningStore`. It returns `new ReportOpening(voucher, approved)` with the entry's Journal (`JRN/<asOf>/000`, sub `opening-entry`, kind `opening`, dated `asOf`, the 1400 balancing line included), or `null`/`Voucher = null` when there is no entry; register it as a singleton. Until then the server's `openingPending` is always `true` and only the imported opening is used. Whether a **draft** entry posts is the opening lane's choice: the reports include whatever voucher the port returns, and whatever `accVouchersUpTo` returns in the prototype.
5. **acc1-chart (optional):** the server row `Group` is `ChartOfAccounts.Find(control).Group.ToString()` (legacy Asset / Liability …). If the chart lane adds Tally group ids (`GroupOf`), switch `LedgerNames.Group` in `AccountingReportQueries.cs` to that so the server matches the prototype.
6. **acc1-ui:** use the selector names above. Show balances through `accDrCr` / `accDrCrText`, the TB `openingPending` banner ("Opening balances pending") and `balanced === false` as red. Offer CSV through `saveCsv` with `accStatementCsv` / `accTrialBalance6Csv` / `accDayBookCsv`.

## Contract notes (for the coordinator — not silently picked)

a. **Opening voucher dated on `from`:** the contract says "every voucher dated before `from` (the opening entry included)", but the opening entry is dated `asOf` = the FY start, so a report from 1 April would otherwise show it as a period movement. The lane follows rulebook §7.2 ("sum of entries before the range, incl. opening journal"): opening vouchers dated on `from` are opening. If the coordinator wants the literal reading, change `accRptVouchers` / `AccountingReports.Split` (one condition each).
b. **Day book order:** the contract says "`date, no` order", rulebook §7.1 says "posting order". Built as the contract says: within a date, ordinal number order, so JRN < PMT < RCT < SAL and the opening `JRN/…/000` comes first. The existing day screens show Sales, Receipt, Payment, Journal.
c. **`openingPending`** also covers an approved entry whose 1400 line is not zero (rulebook §G2 "until then"), not only "no approved entry".

## Open items

- **Production prototype client:** no `src/clients/sync/` file for the three GETs; the contract assigns sync files only to lanes that add commands. In a production build the selectors work on the server window (`s.accountingX.server`: ≤ 62 days plus one balance-before-window voucher), which is right for any `from` inside the window. A report starting before the window needs a sync reader for `/accounting/statement`, `/trial-balance?from`, `/day-book` (field map `xPaise` → `x`). This belongs to the UI or integrator lane.
- **Server performance:** the opening is computed by deriving every voucher since the first source (`RangeAsync(MinValue, to)`). That is fine for a year; an aggregated opening read (per map key + party for unlocked dates, per account for snapshots) can come in ACC-2 if it becomes slow. It needs the parties lane's `party_id` first.
- **CA note:** none; the reports add no posting rule.

## HANDOFF bullet

- **ACC-1 statements (lane acc1-statements, 27 Oct):** the prototype adds `src/domain/streams/accounting-reports.ts`: `accStatement` (ledger, control or party `<code>/<party>`, opening before `from` incl. the opening journal, running balance Dr + / Cr −), `accTrialBalance6` (six columns, control summary or party `detail`, `balanced`, `openingPending` per §G2), `accDayBook` (date, number order, one row per entry), CSV builders and the `accDrCr` display (never negative). The server adds `AccountingReportQueries` + `GET /accounting/statement`, `/accounting/day-book` and the six-column `/accounting/trial-balance?from&to&detail` (via the dispatch in `AccountingEndpoints`), PERM-ACC-001, with the opening entry / party-name ports `IReportOpeningSource` / `IReportPartyNames`. No migrations. Tests: prototype +10, server +8 (`05 Delivery Evidence/Accounting/ACC1-STATEMENTS-DONE.md`).
