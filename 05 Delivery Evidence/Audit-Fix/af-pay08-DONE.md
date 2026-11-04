# af-pay08 DONE (PAY-08: second receipt after RESULT_UNKNOWN)

Lane af-pay08 / Sonnet / branch `claude/af-pay08` / worktree `E:\Projects\HCC\worktrees\af-pay08` / base `6a29f3e6` + merge of `claude/auditfix` (wave-0 runner fix).

## Commits
See `git log --oneline 6a29f3e6..HEAD` (server commit, client commit, this DONE file).

## Files
Server (CRLF kept): `08 Production/src/Hcc.Contracts/Billing/BillingContracts.cs` (`RecordReceiptRequest.ConfirmDuplicate = false`), `src/Hcc.Domain/Billing/BillingRules.cs` (appended `RecentReceipt`, `DuplicateReceiptGuard`; `Tenders` untouched), `src/Hcc.Application/Billing/BillingPorts.cs` + `src/Hcc.Infrastructure.SqlServer/Billing/SqlBillingStore.cs` (new `RecentReceiptsAsync`; no existing read exposes the receipt time, `pay.receipt.recorded_utc` already exists, `IX_receipt_folio` serves the folio filter, no migration), `src/Hcc.Application/Billing/BillingService.cs` (guard in `RecordReceiptAsync`), new `tests/Hcc.Billing.Tests/Sql/DuplicateReceiptTests.cs` (LF, like its neighbours).
Prototype (LF): `src/clients/sync/money.ts`, `src/app/App.tsx` (marked `// AF-PAY08` block + 3 import lines + 1 line in the mock catch), `src/features/folio/modals.tsx` (payment modal), `src/services/mock/handlers/folio.ts`, `src/domain/streams/folio.ts` (helpers `recentEqualReceipt`, `folioDupMessage`, `folioDupDetails`, `announceFolioDuplicate`, `onFolioDuplicate`), new `tests/stream-pay08.mjs`, `tests/sync-money.mjs` (appended + two existing body assertions gained `confirmDuplicate:false`).

## What was built
Server: an equal (same kind payment/deposit, same canonical tender, same paise) receipt on the same folio within 10 minutes of the server clock (`TimeProvider`) is refused unless `ConfirmDuplicate`. Checked inside the transaction after the open-folio / deposit checks, before the cash branch, so the corrected-bill payment on a departed folio passes through it too. Refusal: `VALIDATION`, `details.reason = DUPLICATE_SUSPECTED`, `details.receiptId`. Text: `A UPI payment of <amount> was recorded on this folio at HH:mm IST (N minutes ago), receipt <id>. If this is a second, separate payment, confirm it.` A confirmed second payment appends ` · confirmed second payment (receipt <id> <n> min earlier)` to the `RECEIPT_RECORDED` audit text. Deposit-return and refund execution are not guarded (the guard applies to kinds payment and deposit only; the brief says "refund / deposit-return paths are not affected").
Idempotency: verified in `SqlCommandRunner`: a failed handler rolls back and stores NO idempotency row (class comment: "A failed handler stores nothing, so the client may retry with the same key"). Because the payload hash includes `ConfirmDuplicate`, the confirmed resend still uses its own key suffix (`:confirm`) as briefed.
Client: PAYMENT mapper sends `confirmDuplicate` (false by default; true uses step `confirm`, multi-tender `confirm-t1…`). App.tsx `run()`: refused duplicate is announced (window event) to the dialog that owns the key; on RESULT_UNKNOWN the sync runtime's public `refresh()` (`getSyncRuntime().refresh()`) runs after 3 s. Payment dialog: amber note when an equal receipt in the last 10 minutes is known, and after a refusal the text plus the checkbox "This is a second, separate payment"; Post stays disabled until ticked; the resend is the same dialog and same `modal.key`. Mock mirror: same rule on `s.clock` (receipts now carry optional `at`), after execute()'s same-key check so the idempotent replay still returns the identical state.

## Tests
Before: `Hcc.Billing.Tests` 41, prototype per `claude/auditfix` baseline. After: `Hcc.Billing.Tests` 49/49 (8 new, all named as briefed); `Hcc.Integration.Tests` 22/22; `npx tsc --noEmit` clean. Prototype `npm test`, builds and secret scan: see "Final run" below.
Commands (all through the lock): `dotnet build Hcc.slnx`; `dotnet test tests/Hcc.Billing.Tests -maxcpucount:1 --logger trx`; `dotnet test tests/Hcc.Integration.Tests -maxcpucount:1 --logger trx`; `npx tsc --noEmit`; `npm test`.
`Hcc.Pos.Tests` not run: it does not use `RecordReceipt` / `RecentReceiptsAsync`.

## Open items
- Production client receipts carry no time (the server folio view has no `recordedUtc`), so the early amber note shows only in the demo mock; in production the server refusal + checkbox covers it. Optional follow-up: add `recordedUtc` to the folio receipt view and map it to `Receipt.at`.
- Multi-tender: each tender is its own receipt/command. If row N>1 is refused, rows before it are already recorded; the message says so ("Tender rows 1 to N were already recorded; check the folio before confirming."). A confirmed resend uses new keys (`confirm-t*`) for all rows, so it would re-post rows already recorded; the cashier must check the folio first. Not a rule I could decide.
- Two truly concurrent equal requests with different keys are not serialised by the guard (it reads before inserting inside the transaction; no folio lock added). Not in the brief.
- `receipts` in the mock now get an extra optional field `at`; no SCHEMA bump (optional field).

## Integration needed
1. Phone payment sheet `03 Prototype/src/mobile/money/Payment.tsx` (not in my ownership list; the brief asks for the same note and checkbox there). Exact edit: import `{recentEqualReceipt,onFolioDuplicate}` from `'../../domain/streams/folio'`; add `const [dup,setDup]=useState<{receiptId:string;message:string}|null>(null),[confirmDup,setConfirmDup]=useState(false);` and `useEffect(()=>onFolioDuplicate(modal?.key,setDup),[modal?.key])` before the early return; in `post`, send `{...mmPaymentCommand(b.id,kind,total,rows,key),...(dup&&confirmDup?{confirmDuplicate:true}:{})}`; on the confirm step render the `dup.message` in an `info-note` with a 48 px checkbox "This is a second, separate payment" and disable the Confirm button while `dup&&!confirmDup`; optional early note from `recentEqualReceipt(b,eff.map(r=>({method:r.method,amount:mmPaise(r.amount)})),s.clock,kind)`. Without it the phone sheet shows the server refusal text (nothing is posted twice) but cannot confirm a genuine second payment.
2. Optional: `src/domain/types.ts` `Receipt` could declare `at?:string` (the code casts today).
3. Re-run the v15 phone checks later; no HANDOFF edits made.

## HANDOFF bullet
PAY-08 (af-pay08): server refuses an equal payment/deposit on the same folio within 10 min (server clock) with `DUPLICATE_SUSPECTED` unless `ConfirmDuplicate`; confirmed second payment is audited; desktop payment dialog offers "This is a second, separate payment" (resend with key `:confirm`); App.run refreshes the server data 3 s after RESULT_UNKNOWN. Phone sheet still owed (see Integration needed). AF-DEC-1 (10 min) pending owner approval.
