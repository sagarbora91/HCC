# af-fix2 DONE (fixes for the af-verify findings F1..F6)

Lane af-fix2 · branch `claude/af-fix2` · worktree `E:\Projects\HCC\worktrees\af-fix2` · base `claude/auditfix` 5b9d8dda + the af-verify commits cherry-picked (88b1a260..a63fcef3: hostile tests + af-verify-DONE.md). Not merged; main untouched.

## What changed per finding
| # | Sev | File(s) | Change | Red test now |
|---|---|---|---|---|
| F1 | S2 | `03 Prototype/src/clients/sync/money.ts` (PAYMENT) | A confirmed resend of a **split** payment first replays each row under its **original** key `tN` with the original payload (`confirmDuplicate:false`). A row the refused attempt already recorded comes back as an idempotent replay (no second receipt, no second drawer movement). Only a row the server refuses `DUPLICATE_SUSPECTED` (the refused row, or a later equal row) is then sent confirmed under `confirm-tN`. Single-tender path unchanged (`<key>:confirm`). Other errors propagate unchanged. | V-PAY-C03 green (2 receipts) |
| F2 | S3 | `08 Production/src/Hcc.Domain/Counter/CounterBillRules.cs`, `Hcc.Application/Counter/CounterBillService.cs` | Caps: unit price at most ₹1 crore (`MaxUnitPaise` 1e9) in `LineRefusal`; new `BillRefusal` (checked sum, overflow = refusal) caps one counter bill before discount at ₹10 crore (`MaxBillPaise` 1e10), called before the discount/totals. Plain VALIDATION messages; nothing written (inside the runner, before any insert). | V_CTR_10b 8/8 green |
| F3 | S4 | `08 Production/src/Hcc.Domain/Billing/BillingRules.cs` (`DuplicateReceiptGuard`), `Hcc.Application/Billing/BillingService.cs` | `FindEqual` uses the absolute difference `(now - recorded).Duration() <= Window` (the SQL read has no upper bound, so receipts "in the future" after a backwards clock step are seen). The "N minutes ago" text and the audit note clamp to 0, never negative. | V_PAY_09 green |
| F4 | S4 | `03 Prototype/src/clients/sync/lfmoney.ts` (CTR_BILL_ISSUE) | New `ctrMethod`: the paid-mode method resolves to the master's canonical name through `mnyMethod` (name / code / old name / case-insensitive trim), so `cash` / ` CASH ` is a drawer method: precheck asks for the open shift, `tillId` is sent, method sent as `Cash`. Unknown spellings stay as typed (server refuses). | V-CMAP-02 green |
| F5 | S4 | same | Precheck: mode `company` without a company id is refused on the device ("Choose the company account.", the server's words), 0 requests. | V-CMAP-04 green |
| F6 | S4 | `03 Prototype/src/clients/http.ts`, `src/clients/sync/reports-hub.ts`; `08 Production/src/Hcc.Api/Endpoints/ReportEndpoints.cs`, `Hcc.Reporting/ReportService.cs`, `Hcc.Reporting/Sql/SqlReportStores.cs` | `transport.post` takes optional extra headers (still never retried). RPT_FAV_TOGGLE sends the action's `Idempotency-Key` and the **wanted state** `{favourite: !isFavouriteNow}` from the loaded favourites. Server: `POST /reports/favourites/{id}` takes an optional body `FavouriteRequest(bool? Favourite)`; with a value it **sets** (new `IReportFavourites.SetAsync`, default interface method + one idempotent SQL statement in `SqlReportStores`), without a body it toggles as before (older clients). A retry after a lost answer sets the same value, so it cannot flip back. | V-OPS-02 green |

Test edits: `tests/sync-money.mjs` `[AF-PAY08]` assertion updated to the F1 key scheme (a confirmed split now sends `t1`, `t2` unconfirmed first; it previously asserted the buggy `confirm-t1`, `confirm-t2`). Added SetAsync assertions to `Hcc.Reporting.Tests` (`ReportSqlTests` store round trip, `ScheduleTests` service via the memory store / default interface method).

Line endings: each file kept as it was (`git diff --stat` = `--ignore-cr-at-eol`; `BillingService.cs` is mixed in the index and was patched bytewise).

## Test counts (all through `tools\Invoke-Serialized.ps1`, one run each)
| Suite | Command | Result |
|---|---|---|
| Prototype | `npx tsc --noEmit` | 0 errors |
| Prototype | `npm test` (full, verifier file included) | **2839/2839 passed** (21 async) = 2827 baseline + 12 af-verify client tests (the af-verify DONE said 13; the file holds 12 tests) |
| Server | `dotnet test Hcc.slnx -maxcpucount:1` (42 projects) | **4261 passed, 0 failed** = 4162 baseline + 99 af-verify tests; 0 build warnings/errors |

## Not done / notes
- Prototype `domain/streams/counter.ts` (mock path) has no matching ₹1 crore / ₹10 crore caps; the server is the authority. Add if parity is wanted.
- Caps chosen by this lane (₹1 crore unit, ₹10 crore bill): owner may change them (`CounterBillRules.MaxUnitPaise` / `MaxBillPaise`).
- O8 (folio PAYMENT precheck uses a fixed method list) not touched, out of scope.
- No emulator, no browser run (no UI file changed).
