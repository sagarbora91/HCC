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
