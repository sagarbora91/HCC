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

## Fix round (audit FIX 1 + coordinator items; migration 4935 approved)
Note on wording (audit note 4): the PAY-08 guard matches when the labels are equal OR the two values are the same master row (a strict superset of the old guard), not "by row id only".

- **FIX 1 (hall refund, retired method)**: a retired method is allowed on a hall refund only when it is the same master row (`mnySameMethod` / `PaymentMethodBook.SameRow`) as the tender of one of the event's ADVANCE / BALANCE receipts. `handlers/hall.ts` (`tender(..., e)`), `clients/sync/hall.ts` (`hallPay(..., refundOf)`), server `HallBillingCommands.TenderAsync(..., eventId)` with new store read `IHallBillingStore.PaidTendersAsync`. Tests: retired + original row accepted, retired + other row refused with `MASTER_RETIRED` and nothing written (prototype `stream-cu-pay-b.mjs`, server `HallPaymentMethodSqlTests`).
- **WORK 2 (server hall on the master)**: migration `4935_hall_receipt_tender_master.sql` (re-runnable; drops `CK_hall_event_receipt_tender` / `_side` if present, widens `tender` to NVARCHAR(60) when not already, re-adds the tender check as length 1-60 and the side check exactly as 4320; rows untouched). Lock entry + `Hcc.slnx` line inside `CU-PAY-B` block. `HallBillingCommands`: `IPaymentMethodSource` injected (optional, defaults), `PaymentMethodBook.Pick` (front desk), 3-character reference rule kept, stores the current master name, drawer test = `SettlesToTill` (not the literal Cash), store parameter NVarChar. The old text "Choose a tender: Cash, UPI, Card or Bank transfer." is now the `PaymentMethodBook.Choose` wording; no existing server test pinned the old text.
- **WORK 3 (group master-folio refund)**: `GroupCommands.RecordReceiptAsync`: a retired method on a refund is allowed only when it is the row of the refunded entry's tender (`MASTER_RETIRED` otherwise). New `GroupRefundRetiredMethodTests`.
- **Renamed Cash in hall (coordinator item)**: the prototype hall paths decide reference by the master row's `needsReference` and the drawer by `mnyIsDrawer` (settles-to-till), not the literal "Cash" - the mock handler, the modals, and the device prechecks. New test: Cash renamed "Front till": a cash advance needs no reference and moves the drawer (`M.drawer`), a UPI one still needs its reference (mock and device precheck). Server equivalent: `Renamed_cash_row_still_moves_the_drawer...`.
- **Audit note 2**: hall device prechecks refuse a till method when the shift is closed (`shiftClosedText`).

Tests (through the lock): `npx tsc --noEmit` clean. `npm test` filtered `cu-pay-b|pay08|af2-paymethods|hall|sync-lf|sync-money`: 170/170; filtered `cu-pay-b`: 65/65 (10 stream tests). Server: `Hcc.Hall.Tests` full 36 tests (35 passed + my 1 test with a helper mistake, fixed, then `HallPaymentMethod` 5/5); `Hcc.Groups.Tests` filtered (GroupRefundRetiredMethod, PaymentMethodMasterGroup, GroupBooks) 4/4; `Hcc.Billing.Tests` DuplicateReceipt 8/8; `Hcc.Closing.Tests` PaymentMethodFolio + CuPayB 5/5; `Hcc.Platform.Tests` MigrationLock 9/9.

Still open:
- Audit note 3 (`PaymentMethodBook.Find` / `mnyMethodIndex.get` try the code before former names; a stored label can resolve to the wrong row in a rare code/rename clash): not changed, follow-up.
- `SqlFlowsStore` (`CashParty.Hall`, outside this lane) sums hall cash by the literal `r.tender = 'Cash'`, so the per-event cash receipt limit misses a renamed cash row; follow-up (resolve through the master).
- Full `npm test` and the full server suite were not run.
