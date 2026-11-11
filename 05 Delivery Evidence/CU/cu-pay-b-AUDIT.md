# cu-pay-b AUDIT (Opus, auditor A3)

Branch `claude/cu-pay-b`, head 72ce8e01, base c45bbce3. Read: CU-CONTRACT §1, §2, §3.4, §3.5; `af2-paymethods-DONE.md`, `af-pay08-DONE.md`, `cu-pay-b-DONE.md`; `git diff c45bbce3..HEAD` (15 files); `PaymentMethodBook.cs`, `money-master.ts`, `clients/sync/money.ts`, `HallBillingCommands.cs`, migrations 4320 / 2890, `SqlPaymentMethodSource.cs`, Decision Register (DEC-MA-11a, AF-DEC-2).
Checks run: `npx tsc --noEmit` clean; `git diff --stat` equals `--ignore-cr-at-eol` (endings kept: the two server `.cs` files CRLF, everything new LF). No heavy job was needed: every claim could be checked by reading.

## Verdict: FIX (one item, small). Everything else is PASS WITH NOTES.

## (a) PAY-08 across a rename: correct, a strict superset of the old guard
- Prototype `recentEqualReceipt(..., same)` and server `DuplicateReceiptGuard.FindEqual(..., sameTender)` keep the old label test and **OR** the same-row test. Kind, exact paise, folio (server query per `folio_id`; prototype per booking) and the 10-minute window (absolute difference on the server) are unchanged. So **every receipt the old guard caught is still caught**. The 8 `DuplicateReceipt` server tests and the pay08 / af2 suites stay green (DONE file; same inputs, same outcome by construction).
- Resolution of a label-only stored receipt: server `PaymentMethodBook.Find` (already in place, AF2) tries current name, then code, then former names (`mst.master_history`, loaded for every caller by `SqlPaymentMethodSource`), then name ignoring case. Retired rows resolve. Prototype `mnySameMethod`: the same order through `mnyMethodIndex`; if either side does not resolve, it falls back to the label ignoring case (old behaviour).
- Case by case: plain rename: now caught (the fix). Retired method: resolves, caught. Seed rows on a device without the rename history: still resolve **by code** (`UPI`, `CASH`), so the early note works for them; Owner-added rows with their own code fall back to the label, and the server enforces the rule. Two methods that **swapped names**: a current name always wins, so the stored old label is given to the *other* row. A new receipt on the row that owned the label is missed, and one on the row that now has the name is flagged through the label test. **This is exactly the old behaviour**, so nothing gets worse. Duplicate code / reused code: see note 3.
- False duplicates added: only through note 3 (rare). A false duplicate is a confirm tick (`confirmDuplicate`), not a hard refusal.
- Server: the `RecentReceiptsAsync` query is unchanged and runs in the same `runner.RunAsync` transaction. The comparison runs in memory on at most a few rows. `methodList` is the list already loaded for `Pick` before the transaction, as before. No index impact.
- Tests fail on the old code: the server test (`CuPayBRenameDuplicateTests`) expects a refusal for "Paytm" after "Paytm wallet" was renamed, and the old label test accepts it. The prototype test asserts that `recentEqualReceipt` without `same` returns undefined, and that the mock `PAYMENT` is refused.

## (b) Hall booking
- Receipts store the master's current name (`mnyPick(...).method.name`). The Reference field follows the row's `needsReference`. No literal `'Cash'` is left in the hall modals or the handler. Drawer: this lane makes **no** drawer decision in hall code. `selectors.ts hallDrawerCash` (not changed) uses `mnyIsDrawer` on the stored value. Because the stored value is now always the exact current name, the exact-text weakness is *reduced* here: before, a lowercase "cash" could be stored and dropped out of the drawer. `lfmoney.ts:148/157` passes `ctrMethod(...)`, the resolved current name, to `mnyIsDrawer`, so it is safe. The diff adds no new exact-text drawer test.

## (c) CTR_BILL_ISSUE precheck
- `mnyPick` gives the same words as the server's `PaymentMethodBook.Pick` / `Choose` and the same pattern as the folio `PAYMENT` precheck (AF2). Retired is refused on the device as a VALIDATION precheck string, and the server keeps `INVALID_TRANSITION` / `MASTER_RETIRED`. That is the same split AF2 accepted. Empty-list wording now equals the server's ("No payment method is usable here…") instead of the old `?? 'Cash'` text. That is correct.
- Stale master: the picker is built from the same device master (`mnyNames`, active only). So a method that is retired on the device but active again on the server is not offered in the first place, and the precheck adds no new block. A method renamed on the server but not yet on the device is sent under its old name and resolves on the server through FormerNames. Acceptable.

## Findings

1. **FIX: hall refund may use *any* retired method.** `03 Prototype/src/services/mock/handlers/hall.ts:217` (`tender(k,k.cmd.method,k.cmd.reference,true)`) and `03 Prototype/src/clients/sync/hall.ts:138` (`HALL_REFUND` precheck `hallPay(...,true)` and `hallName(...,true)`). The test codifies this: `tests/stream-cu-pay-b.mjs:48` refunds by retired "Card" when the advance was UPI, and line 63 asserts the same. DEC-MA-11a says "a refund may go back by **the original receipt's** retired method". The folio refund follows that exactly: `BillingService.cs:560` uses `allowRetired` only when the tender is the parent's, and `money.ts:396-398` uses `allowRetired:same` by row. The DONE file's default "as the folio refund does" is therefore wrong. Right: allow a retired method only when it is the same master row (`mnySameMethod`) as the method of one of the event's ADVANCE / BALANCE receipts (`e.receipts`). Any other method must be active, and the refusal must keep the `retired` words. Fix both files and change the test. Retired "Card" must be refused when the event was paid by UPI, and accepted when an advance was taken by Card before it was retired. (The group master-folio refund `GroupCommands.cs:317` has the same looseness. It is outside this lane: report it to the orchestrator, do not fix it here.)
2. NOTE: the hall device prechecks (`clients/sync/hall.ts` advance / accept / close / refund) do not check `folioIsDrawer(s,method)&&!s.shiftOpen` the way folio `PAYMENT` / `REFUND_EXECUTE` do. The server's hall `CashAsync` refuses a cash tender with no single open till only after the request has been sent. This is low priority, the wording is in `clients/sync/money.ts shiftClosedText`, and it fits naturally into the (d) work.
3. NOTE (pre-existing, affects PAY-08, refund `SameRow` and `SettlesToTill`): `PaymentMethodBook.Find` (`PaymentMethodBook.cs:59-62`) and `mnyMethodIndex.get` (`money-master.ts:81`) try the **code before the former names**. Example: row A renamed "Wallet" to "Paytm", and row B has code `WALLET`. The stored label "Wallet" is then given to B: a B receipt is flagged falsely and an A receipt is missed. Stored values are always names, never codes. The safe order for a *stored* value is current name, then former names, then code, then case-insensitive name. Raise it with the orchestrator as a follow-up, not in this lane.
4. NOTE: the DONE file and the comments say the guard "compares by master row id". In fact it is label **or** row, and that is the right, conservative choice (no regression). The wording should say so.
5. NOTE, prototype / production mismatch (DONE open question 1, confirmed): `HallBillingCommands.cs:133/141-147/158` uses a fixed list and the literal "Cash". The column `hall.event_receipt.tender VARCHAR(16)` has the check `CK_hall_event_receipt_tender IN ('Cash','UPI','Card','Bank transfer')` (migration 4320:31). The executor was right to stop under rule §1.3. The design is below.

## Scope, rules, claims
- Files: hall modals, hall handler, hall sync, `lfmoney.ts`, the PAY-08 guard (folio.ts, folio handler, two payment UIs), `money-master.ts` (one appended helper), `BillingRules.cs`, `BillingService.cs`, plus two new tests. No `pos.ts`, `accounting.ts`, migration, `SCHEMA` or shared marked-block file. Existing tests were not touched. The optional parameters keep every old caller's behaviour.
- DONE claims match the branch: 8 prototype tests, 1 server test, server unchanged on the hall path, no migration.

## (d) Recommendation: do the migration (4935) and make the server hall path master-driven. Do not make the device refuse.
Reason: AF-DEC-2 and DEC-MA-11a make every money-taking path use the front-desk place of the master. Migration 2890 already removed the same fixed-list checks from `pay.receipt`, `pos.*` and `ops.expense`. Hall is the last one left. A production-mode refusal on the device would keep a known split and would block the Owner's own renamed "Cash" row ("Front till") on halls only.

**Migration `08 Production/database/migrations/4935_hall_receipt_tender_master.sql`** (pattern of 2890, re-runnable, safe on existing rows: values are only widened, never rewritten):
- `IF OBJECT_ID('hall.CK_hall_event_receipt_tender','C') IS NOT NULL ALTER TABLE hall.event_receipt DROP CONSTRAINT CK_hall_event_receipt_tender;`
- `IF OBJECT_ID('hall.CK_hall_event_receipt_side','C') IS NOT NULL ALTER TABLE hall.event_receipt DROP CONSTRAINT CK_hall_event_receipt_side;` (this check reads `tender`, so it blocks `ALTER COLUMN`)
- `IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('hall.event_receipt') AND name='tender' AND system_type_id<>TYPE_ID('nvarchar')) ALTER TABLE hall.event_receipt ALTER COLUMN tender NVARCHAR(60) NULL;` (neither index, 4320 `IX_hall_event_receipt_event` nor 4346 `IX_hall_event_receipt_date`, contains `tender`, so no index rebuild is needed; check `sys.sql_expression_dependencies` for a schema-bound view first)
- Add both checks again inside `IF OBJECT_ID(...) IS NULL`: `CK_hall_event_receipt_tender CHECK (tender IS NULL OR LEN(tender) BETWEEN 1 AND 60)` and `CK_hall_event_receipt_side` exactly as in 4320 (`WITH CHECK`; existing rows already satisfy both).
- Add the migrations lock entry (`node tools/migrations-lock.mjs --add`) and the `Hcc.slnx` line. Note: under §1.3 only lane `nvarchar` may add migrations, so the orchestrator must approve 4935 for this lane.

**Server code** (`HallBillingCommands.cs`): inject `MoneyMasters.IPaymentMethodSource? methods = null` (as `GroupCommands` / `CompanyCredit` did in AF2) and load the list once per command.
- Replace `Tender()` with `PaymentMethodBook.Pick(list, value, FrontDesk, reference, requireReference:false, allowRetired)`, then apply the hall rule "a row that needs a reference needs ≥ 3 characters" (keep the text "Enter the payment reference (UPI / card / bank).").
- Store and publish `m.Name`, the current master name, as the snapshot (same as `pay.receipt.method`). The accounting key `"method:"+name` already resolves through the master ledgers in the mapping (MA-11).
- `CashAsync`: replace `method != "Cash"` with `!m.SettlesToTill`.
- Refund: `allowRetired` only when `SameRow(list, req.Method, <any ADVANCE/BALANCE tender of the event>)`, the same rule as finding 1.
- Change `SqlHallBillingStore.cs:303` `SqlDbType.VarChar` to `NVarChar` (60).
- No row-id column is needed. Like folio receipts, records keep the name, and rename history resolves it (`SameRow` / `SettlesToTill`).

**Tests** (`tests/Hcc.Hall.Tests` or the existing hall SQL tests):
- An Owner-added method ("Paytm wallet") is accepted for advance, balance and refund, and the stored tender equals the master name.
- A renamed Cash row ("Front till") moves the drawer and needs an open till.
- A non-till method moves no drawer.
- A retired method is refused for an advance with `MASTER_RETIRED`. A refund is accepted by the retired original method and refused by a retired other method.
- An earlier name or code is stored under the current name.
- A 60-character Devanagari name round-trips.
- Migration: run 4935 twice, then check both constraints exist and old rows are intact.
- Existing hall tests stay green. The old message "Choose a tender: Cash, UPI, Card or Bank transfer." becomes the `PaymentMethodBook.Choose` wording "Select a payment method (Cash, UPI, Card or Bank transfer)." on the default list. This is the wording the device and the prototype mock already show. Change any server test that pins the old text minimally and say so in the DONE file.
