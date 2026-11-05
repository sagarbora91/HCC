# af2-paymethods DONE (verifier note O8: folio payment methods from the master)

Lane af2-paymethods / branch `claude/af2-paymethods` / worktree `E:\Projects\HCC\worktrees\af2-paymethods` / base `origin/main` e949a9e5.

## What was wrong
The folio `PAYMENT` precheck (`03 Prototype/src/clients/sync/money.ts`) used the fixed list Cash / UPI / Card / Bank transfer and `tillFor` tested the literal "Cash", so an Owner-added method was refused on the device, a renamed drawer method was sent without its till, and a retired method passed the precheck. The server folio receipt was already master-driven (MA-11, `PaymentMethodBook.Pick`), but group master-folio receipts and company payments still used the fixed `Tenders.Validate`, and the server did not resolve a method by an earlier (renamed) name.

## Files
Prototype (LF kept):
- `src/domain/streams/money-master.ts`: new `mnyChooseText` (= server `PaymentMethodBook.Choose` words) and `mnyPick` (= server `PaymentMethodBook.Pick`: usable at the place, active unless `allowRetired`, reference when the row needs one). Built on the existing `mnyMethod` / `mnyNames` index (name, code, earlier name from the master history, case), the same resolution `CTR_BILL_ISSUE` uses.
- `src/clients/sync/money.ts`: `PAYMENT` precheck per tender row through `mnyPick` + `mnyIsDrawer` (shift / own-shift checks unchanged for till-settling rows); request `tender` = the master's current name (`folioTender`); `tillFor` uses `folioIsDrawer` (row settles to a till; an unknown value is drawer cash only when it is the literal "Cash", as the server's `SettlesToTill`). `REFUND_EXECUTE` precheck from the master (original method may be retired; an original the client cannot resolve is left to the server); `GRP_COMPANY_PAYMENT` precheck + tender from the master.
- `src/features/folio/modals.tsx` (desktop payment dialog) and `src/mobile/money/logic.ts` (`mmPaymentProblem`, phone sheet): a row whose method the master no longer offers (retired while the dialog was open, unknown) is refused with the server's words. Pickers were already `mnyNames(s,'frontdesk')`.
- `src/mobile/money/Payment.tsx`: the reference field follows the row's "needs reference" (shown, with "(optional)" when not required), not the drawer test.
- New `tests/sync-af2-paymethods.mjs` (11 tests).

Server:
- `src/Hcc.Domain/MoneyMasters/PaymentMethodBook.cs` (LF): `TenderMethod.FormerNames` (optional, last positional parameter), `Find` resolves an earlier name after name and code, new `SameRow`.
- `src/Hcc.Infrastructure.SqlServer/MoneyMasters/SqlPaymentMethodSource.cs` (LF): second result set = rename history (`mst.master_history`, master `paymentMethods`, field `name`; a later rename of the same text wins, as in the master's usage counts). No migration.
- `src/Hcc.Application/Billing/BillingService.cs` (mixed endings kept, patched at byte level): `ExecuteAdjustment` treats the original method under its current name or code as "the original" (`SameRow`) for `allowRetired`.
- `src/Hcc.Domain/Billing/BillingRules.cs` (CRLF): `Tenders.Validate` now delegates to `PaymentMethodBook.Pick(null, …)` (defaults; same words). `Tenders.All` stays for the daily pack columns.
- `src/Hcc.Application/Groups/GroupCommands.cs` (CRLF) and `CompanyCredit.cs` (LF): optional `MoneyMasters.IPaymentMethodSource? methods = null` constructor parameter (resolved by the existing `AddSingleton<T>()`); master-folio receipts and company payments use `Pick` (front desk) and refuse `SettlesToTill` methods (old texts kept); a master-folio refund may go back by a retired method.
- New tests: `tests/Hcc.Closing.Tests/Sql/PaymentMethodFolioTests.cs` (4), `tests/Hcc.Groups.Tests/Sql/PaymentMethodMasterGroupTests.cs` (2), `tests/Hcc.MoneyMasters.Tests/Kernel/PaymentMethodBookTests.cs` (+3).

## Refusal texts (server and client say the same)
- Unknown / not usable at the front desk: `Select a payment method (<active front-desk names>).` (default list: Cash, UPI, Card or Bank transfer).
- Retired: `<Name> is retired — choose another payment method.` (server INVALID_TRANSITION, `details.reason = MASTER_RETIRED`; client precheck text).
- Reference missing for a row that needs one: `Enter the transaction reference for a non-cash payment.` (the desktop / phone dialogs keep their own "demonstration reference" wording for this one).
- Till-settling method with the shift closed: `shiftClosedText` as before.

## PAY-08
Unchanged: 10-minute window, `ConfirmDuplicate`, per-row replay keys `t1…tN` / `confirm-tN`, "Needs your confirmation" note. Covered again with mixed methods (client: replay keys with canonical names; server: wallet duplicate refused, other method not, confirmed second accepted).

## Tests
All through `tools\Invoke-Serialized.ps1` (pwsh 7).
- Prototype: `npx tsc --noEmit` clean; `npm test` **2851/2851** (was 2840; +11 in `tests/sync-af2-paymethods.mjs`: Owner-added methods, reference-required, retired, unknown / POS-only, renamed by earlier name / code / case, renamed till row sends the till, split with mixed methods (t1…t3, till only on the drawer row), PAY-08 confirmed split replay with mixed methods, refund execution, company payment, mnyPick / Choose words, phone check + source checks). `npm run build` and `npm run build:production` built.
- Server: `dotnet build Hcc.slnx` 0 warnings 0 errors; affected projects first (MoneyMasters 27, Groups 50, Closing 79, Billing 65, Counter 80, all pass); then the full `dotnet test Hcc.slnx -maxcpucount:1`: **4270 passed, 0 failed in 42 projects** (was 4261; +4 `PaymentMethodFolioTests`, +2 `PaymentMethodMasterGroupTests`, +3 `PaymentMethodBookTests`).
- Line endings: `git diff --stat` and `git diff --ignore-cr-at-eol --stat` equal (12 files, 158 insertions, 38 deletions before the new files); new files LF like their neighbours.

## Open items
- Other places still test the literal "Cash" or a fixed list and were outside this lane: POS settle precheck `src/clients/sync/pos.ts:192/203`, expense approve / reverse till `src/clients/sync/accounting.ts:369-376`, hall booking modals `src/features/hall/*` + `handlers/hall.ts:26`, history fixtures (data only). `CTR_BILL_ISSUE` (lfmoney.ts, counter lane) could call `mnyPick` instead of its inline checks (it does not check retired rows on the device; the server does).
- PAY-08 compares the stored tender text: a receipt recorded before a rename and an equal one after it under the new name are not matched as duplicates (left as is: "keep PAY-08 exactly").
- Without the Owner's history read (PERM-MST-002) the client cannot resolve an earlier name that is neither a code nor a current name; the server now does, so the request passes there.

## HANDOFF bullet
af2-paymethods (5 Nov): folio payment / refund / company-payment prechecks and the phone + desktop payment checks take the methods from the payment-methods master (retired refused with the server words, reference by the row, till by "settles to a till", renamed methods sent under the current name); server resolves earlier names (rename history) and group master-folio receipts + company payments are master-driven too. Closes verifier note O8.
