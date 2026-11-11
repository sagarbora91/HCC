# cu-pay-b DONE (payment-method master in hall booking and counter bill; PAY-08 across a rename)

Branch `claude/cu-pay-b`, base c45bbce3. Executor Sonnet.

## What changed
Prototype (all LF):
- `03 Prototype/src/domain/streams/money-master.ts`: new `mnySameMethod(s,a,b)` (two stored method values name the same master row, by row id; an unresolvable value falls back to the label, case-insensitive).
- `src/domain/streams/folio.ts`: `recentEqualReceipt` gets an optional `same` test (label equal OR same master row). `src/services/mock/handlers/folio.ts`, `src/features/folio/modals.tsx`, `src/mobile/money/Payment.tsx` pass `mnySameMethod` (mock guard, desktop early note, phone early note).
- `src/services/mock/handlers/hall.ts`: `tender()` goes through `mnyPick` (front-desk place): earlier name / code resolves to the row, the receipt stores the CURRENT master name, retired refused, reference rule follows the row's `needsReference` (min 3 characters, old wording kept), no literal "Cash" test. Hall refund passes `allowRetired` (same as the folio refund).
- `src/features/hall/ModalEvent.tsx`, `ModalQuote.tsx`: the Reference field shows by the row's `needsReference`, not `method !== 'Cash'`.
- `src/clients/sync/hall.ts`: prechecks on `QUO_ACCEPT` (advance), `HALL_ADVANCE_RECEIVE`, `HALL_EVENT_CLOSE` (TENDER with a method), `HALL_REFUND` (retired allowed) against the master; the request carries the master's current name.
- `src/clients/sync/lfmoney.ts`: `CTR_BILL_ISSUE` precheck now uses `mnyPick` (a RETIRED method is refused on the device; before, only the server refused it). Same words as before for unknown / reference / shift.
- New `tests/stream-cu-pay-b.mjs` (8 tests).
Server:
- `08 Production/src/Hcc.Domain/Billing/BillingRules.cs` (CRLF kept): `DuplicateReceiptGuard.FindEqual` takes an optional `sameTender` test. `src/Hcc.Application/Billing/BillingService.cs` (CRLF kept): passes `PaymentMethodBook.SameRow(methodList, a, b)`, so a receipt stored under an earlier name matches an equal one after a rename (by row, using the rename history already loaded by `SqlPaymentMethodSource`). No migration.
- New `tests/Hcc.Closing.Tests/Sql/CuPayBRenameDuplicateTests.cs` (1 test, LF).

## Tests run (through the lock)
- `npx tsc --noEmit`: clean.
- `HCC_TEST_ONLY='cu-pay-b' npm test`: 64/64 passed (the 8 new tests plus the base unit tests). An earlier wider run (`cu-pay-b|pay08|af2-paymethods|hall|sync-lf`) was 140/141; the one failure was my own refund test's setup, fixed, and re-verified in the 64/64 run above (pay08, af2-paymethods, hall and sync-lf suites passed in the wider run).
- `dotnet test tests/Hcc.Closing.Tests --filter CuPayBRenameDuplicate`: 1/1 passed.
- Server regression: `Hcc.Billing.Tests --filter DuplicateReceipt` 8/8, `Hcc.Closing.Tests --filter PaymentMethodFolio` 4/4.

## NOT verified
- Full `npm test` was NOT run (rule: at most once, at the end); full server suite not run (rule).
- Hall UI in a browser/emulator not looked at; only source and mock tests.
- The server hall path was NOT changed (see open question 1).

## Defaults used
- Hall reference rule: a row that needs a reference needs at least 3 characters (the old hall rule); the master decides which rows need one.
- Hall refund may go back by a retired method (as the folio refund does).
- An unresolvable stored method (no master row) is compared by label, case-insensitive (old behaviour), in PAY-08.
- Old receipts are not rewritten: they keep the label they were stored under; PAY-08 resolves it through the master rename history. Without the Owner's history read the client cannot resolve an earlier name that is neither a code nor a current name; the label fallback then applies.

## Open questions / needs a migration (not done: rule 3)
1. Server hall tenders are still the fixed list Cash / UPI / Card / Bank transfer: `HallBillingCommands.Tenders` and the DB check `CK_hall_event_receipt_tender` (migration 4320, `tender VARCHAR(16)`) refuse any other name, so an Owner-added or renamed method cannot be stored in `hall.event_receipt`. Making the server hall master-driven needs a migration (drop/replace that check, widen `tender`) and a code change in `HallBillingCommands` (resolve through `PaymentMethodBook.Pick`, `SettlesToTill` instead of `== "Cash"`); I wrote and reverted that code because it would raise a SQL check error without the migration. Until then a device on the production server sees the server's own refusal for a non-default hall method.
2. The mock (demo) hall accepts any master method; production will refuse non-default ones until item 1 is done.
3. No SCHEMA bump or APK bump needed.

## Shared files touched
None of the marked-block shared files (no `Hcc.slnx`, no migrations lock, no registry, no CSS, no `stream-fit.mjs`).
