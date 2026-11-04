# af-ctr-svc DONE (wave 1)

Lane af-ctr-svc / Sonnet / branch claude/af-ctr-svc / worktree E:\Projects\HCC\worktrees\af-ctr-svc / base 6a29f3e6 (+ merge of claude/auditfix, wave-0 runner fix).

## Commits
See `git log --oneline 6a29f3e6..HEAD` (one code commit "Counter bills take the tender from the payment-methods master" plus this DONE commit).

## Files
- 08 Production/src/Hcc.Application/Counter/CounterBillService.cs
- 08 Production/tests/Hcc.Counter.Tests/Sql/CounterPostingTests.cs
Nothing else changed.

## What was built
- BIL-02 / Missed E: `CounterBillService` now has the optional `MoneyMasters.IPaymentMethodSource? methods = null` constructor parameter (same private `MethodsAsync` helper as `BillingService`). For mode `paid` the method goes through `PaymentMethodBook.Pick(stored, s.Method, MoneyMasterValues.FrontDesk, s.Reference)` (AF-DEC-2); `cash = method.SettlesToTill`; cash without `TillId` is refused. The master's canonical name (`tender`) is what is stored in `ctr.bill.method` and published as `Tender` in `CounterBillIssued` ("upi" becomes "UPI"). The old "blank reference" check is gone (Pick covers it). Cash guard, drawer, Udhaar, company branches, line-price logic (DEC-241), permissions, GST and event shape are untouched.
- BIL-01 server half: the server stays strict (cash needs a till); no change.

## Exact refusal texts (VALIDATION unless noted)
- "Cashh" (any unknown method): `Select a payment method (Cash, UPI, Card or Bank transfer).` (default front-desk list; with an Owner-edited master the list shows its active front-desk names)
- Missing reference for UPI / Card / Bank transfer (any method that needs one): `Enter the transaction reference for a non-cash payment.` (replaces the old "Enter the payment reference (UPI / card / bank).")
- Cash without till: `Choose the till that receives the cash.`
- Retired method: code INVALID_TRANSITION with details reason `MASTER_RETIRED`, text `<Name> is retired — choose another payment method.`
- Blank method: falls under the unknown-method text above (old "Choose the payment method." no longer produced by the server; the client precheck still shows its own text).

## Tests (through the lock, `dotnet test tests/<project> -maxcpucount:1 --logger trx`)
- Hcc.Counter.Tests: 22 before, 29 after (7 new: Counter_cash_without_till_is_refused_and_nothing_is_written, Counter_misspelled_tender_Cashh_with_reference_is_refused_and_nothing_is_written, Counter_upi_without_reference_is_refused, Counter_retired_method_is_refused_MASTER_RETIRED, Counter_lower_case_upi_is_accepted_and_stored_as_UPI, Counter_cash_with_open_shift_till_posts_one_bill_one_movement_one_outbox_event, Counter_same_key_replay_posts_once). All pass. "Nothing written" = row counts of ctr.bill, pay.cash_movement, ivc.invoice, plat.outbox unchanged.
- Hcc.Accounting.Tests: 320 / 320 pass. Hcc.Integration.Tests: 22 / 22 pass (LfParityTests needed no change).
- `dotnet build Hcc.slnx`: 0 warnings, 0 errors.
- Logs: .scratch\*.log (uncommitted). Secret scan: ok true, 0 findings.
- The first run of the cash test failed because the movement kind is stored as 'pos-sale' (test fixed, not code).

## Open items
None. DEC-241 (typed price vs master price) untouched, as instructed.

## Integration needed
None. DI: `CounterBillService` is registered with `AddSingleton<T>()` (LfMoneyEndpoints.cs:35), so the new optional parameter resolves automatically; no composition edit.

## HANDOFF bullet
af-ctr-svc (4 Nov): counter bills now take the tender from the payment-methods master (front-desk place): unknown / misspelled / retired methods and missing references are refused with nothing written, tender stored canonical ("UPI"). Counter tests 22 to 29; Accounting 320 and Integration 22 green. Closes BIL-02 and Missed E; server stays strict for BIL-01.
