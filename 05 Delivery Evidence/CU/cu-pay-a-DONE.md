# cu-pay-a DONE (payment-method master in POS settle and the expense till)

Branch `claude/cu-pay-a`, base c45bbce3.

## Changed
- `03 Prototype/src/clients/sync/pos.ts`: `POS_SETTLE` precheck uses `mnyPick(s, method, 'pos')` (active, usable at the POS; name, code or earlier name; retired and unknown refused with the AF2/server words); the reference requirement follows the row's `needsReference` (text "Enter the transaction reference." kept). `send` sends the master's current name as `tender` and takes the till by `mnyIsDrawer` (settles to a till) instead of the literal "Cash".
- `03 Prototype/src/clients/sync/accounting.ts`: `expensePrecheck` uses `mnyPick(..., 'expenses')` (a retired method is named as retired; unknown / not-for-expenses keeps "Choose a funding source."). `EXPENSE_APPROVE` and `EXPENSE_REVERSE` send the till and reload the cash slices by `mnyIsDrawer(state, e.method)` instead of `method==='Cash'`.
- New `03 Prototype/tests/stream-cu-pay-a.mjs` (11 tests).
- Server: no change. `PosService` already uses `PaymentMethodBook.Pick` (pos) and `ExpenseCommands` uses `SettlesToTill`/`KindOf` from the master. Prototype in-memory handlers (`handlers/pos.ts`, `handlers/expenses.ts`) were already master-driven; covered by tests, unchanged.

## Tests
- `npx tsc --noEmit`: clean.
- `node tests/_one.mjs tests/stream-cu-pay-a.mjs`: 11 passed, 0 failed.
- Same runner over sync-pos, sync-pos-master, sync-pos-gst, sync-accounting, sync-af2-paymethods, stream-money-master, stream-masters-fixture, stream-pos plus the new file: 119 passed, 0 failed.

## Not verified
- Full `npm test` and `npm run build` not run (not through the lock; only single-file runs). No server tests (no server file changed). No UI check.

## Defaults used
- Reference wording on POS kept as before ("Enter the transaction reference."), not the folio wording.
- Expense approve/reverse of an expense whose method cannot be resolved in the master sends no till (previously only literal "Cash" did); the server decides.
- Retired text for POS/expenses is the AF2 text ("X is retired — choose another payment method.").

## Open questions
None. No SCHEMA bump, no migration.

## Shared files touched
None.

## Fix round (audit FIX 1)
- `accounting.ts`: the four till decisions (EXPENSE_APPROVE tillId and reload, EXPENSE_REVERSE tillId and reload) now use AF2's `folioIsDrawer` from `./money` (master row via name / code / case, else the literal "Cash"), which works without the rename history that only the Owner loads. A Manager approving or reversing an expense stored as `Cash` after the Owner renamed Cash sends the till again.
- New test in `tests/stream-cu-pay-a.mjs`: renamed master, empty history, stored `Cash` and `cash`: approve and reverse send `tillId` 3 and reload the cash slices; UPI sends none.
- `pos.ts`: the same exact-text weakness does NOT apply. `send` first resolves the command's method through `mnyPick` (name, code in capitals, case, earlier name) to the master's current name and only then calls `mnyIsDrawer` on that current name, which is always a key. A stored value is never tested there.
- Tests: `tsc --noEmit` clean; `stream-cu-pay-a` 12 tests; with the related POS / accounting / payment-method / money-master suites: 131 passed, 0 failed.
- Not changed (audit notes 3-5): reference wording, in-memory expense retired wording, client-side drawer figures in closing / selectors.
