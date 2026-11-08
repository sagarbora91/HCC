# F8-FIX DONE (lane f8-fix, Sonnet), 8 Nov 2026

Worktree `E:\Projects\HCC\worktrees\f8-fix`, branch `claude/f8-fix` (cut from `claude/f8` 8b2ec88e). Code commit 51a695a0; the head hash is the commit that adds this file (see `git log -1 claude/f8-fix`). Nothing pushed; main, f8 and the other worktrees were not touched. No migration, no golden, no BASE/FY26 change.

## What changed

### 1. Money review M2: D7 at posting time on the server (SF-CONTRACT §7.3, variant 5.7c)
- New `08 Production/src/Hcc.Application/Billing/CompanyFolioPostingGuard.cs`. It is the twin of the prototype `sfAfterCommand` (`handlers/folio-split.ts:130`).
  - **Before:** on a split stay with a live company folio, it reads that folio's D7 figures through `ISplitFolioStore.CompanyFolioCreditAsync`. That call locks the company row.
  - **After:** it reads the figures again, inside the same transaction. The command is refused only when **both** hold:
    - the company folio's exposure rose;
    - used without this folio + the folio's exposure after the command > limit.
  - The refusal is INVALID_TRANSITION, `details.reason = CREDIT_LIMIT`, with the `CompanyCreditRules` text and `what = "split folio"`.
  - There is no override (DEC-076). A single-folio stay reads nothing beyond `LockStayAsync`.
- Wired into these paths:
  - `BillingService.PostChargeAsync`
  - `CatalogueCommands.PostExtraAsync`, with a new optional `split` constructor parameter wired in `CatalogueEndpoints.cs`
  - `BillingPosFolioPort.PostServiceChargeAsync` (POS room-post), with an optional `split` parameter resolved by DI
  - `StayRateCommands.ApplyAsync` (a rate re-post that raises the company folio)
- **Never refused:**
  - `PostRoomNight` and the night run (D7 / SFQ-6);
  - `PostMealPlanNights` (a night posting);
  - the late check-out half day inside CheckoutStay (review M6);
  - discounts, which lower the exposure.
- **Manager limit:** the ₹10,000 Manager limit (SFQ-13) is an approval limit of company credit. It is unchanged and is not consulted at posting, which matches the prototype.

### 2. Cancel guard reads every folio
- `BillingReservationMoney.HasUnsettledMoneyAsync` (`Billing/BillingIntegration.cs`) now refuses the cancel when **any** folio of the reservation holds net payments or a deposit.
- It reads the folios through the new `IBillingStore.FolioIdsForReservationAsync`:
  - the interface method has a default implementation that reads the primary folio only;
  - `SqlBillingStore` implements it by reading every `bill.folio` row of the reservation, in seq order.
- **Prototype:** not affected. `CANCEL` already uses `totals(b)` over every receipt of the booking. A test now pins this.

### 3. Udhaar folio guest credit on the server (SF-CONTRACT §2.2 / §3, SFQ-16)
- `GuestCreditRequestBody` gains an optional `FolioId` (deviation 8: split folios are addressed by `FolioId`).
  - Absent, or naming the primary folio: folio A, exactly as before.
  - Otherwise it must be a live folio of the same reservation that passes `SplitFolioRules.EnsureReceiptFolio("guest-credit")`, i.e. folio A or an Udhaar folio. Any other folio is refused with VALIDATION `SF_PAYER_KIND`; an unknown folio gives NOT_FOUND `SF_FOLIO_UNKNOWN`.
  - `GuestCreditCommands` takes an optional `ISplitFolioStore`.
- The amount is that folio's balance.
- The limit is the Udhaar payer's credit account (`GuestOfFolioAsync`, review H9).
- `BillingService.ExecuteCoreAsync` (GuestCredit branch) now also applies the receipt-folio rule.
- **Checkout:** the existing `CreditApprovalIds` of a split checkout settles the Udhaar folio, and the receivable is raised on the payer.
- No new money rule was needed; the contract and SFQ-16 cover it. Nothing was stopped.

## Tests added
- `08 Production/tests/Hcc.Integration.Tests/F8FixTests.cs` (HTTP, real composition):
  - M2 variant (c): ₹6,000 laundry onto B with a ₹10,000 limit is refused (needed ₹10,230, CREDIT_LIMIT, nothing posted). The same line onto A passes, a routed line passes, a fitting line onto B passes, and the night onto B posts.
  - Cancel refused when a ₹1,000 UPI advance sits on folio B before arrival.
  - Udhaar: guest credit on a plain guest folio C is refused SF_PAYER_KIND. On Udhaar folio B the request is for ₹590, the Owner approves, and the split checkout puts the guest-credit receipt on B and the `ops.udhaar_receivable` row on the payer.
- `08 Production/tests/Hcc.Billing.Tests/F8FixPostingGuardTests.cs` (pure rule, 5 tests).
- `03 Prototype/tests/stream-f8-fix.mjs` (3 tests, twins: the cancel guard over folio B, variant (c), guest credit on the Udhaar folio).

## Commands and counts actually run (all through `tools/Invoke-Serialized.ps1 -Label f8-fix`)
| Command | Result |
|---|---|
| `HCC_TEST_ONLY='stream-f8-fix\|stream-f8-int\|stream-split-folio' npm test` (03 Prototype) | **70/70** passed (core suite + those three files) |
| `npx tsc --noEmit` | exit 0 |
| `dotnet test tests/Hcc.Billing.Tests --filter F8Fix\|SplitFolio` | **16/16** passed (builds Hcc.Api and every src project: no error) |
| `dotnet test tests/Hcc.Integration.Tests --filter F8Fix\|F8IntSplitGolden` | **4/4** passed (3 new + the §5.7 golden) |
| `dotnet test tests/Hcc.Counter.Tests --filter GuestCredit` | **3/3** passed |

### Not run
- The full prototype `npm test`.
- `npm run build`.
- The full or serial `dotnet test Hcc.slnx`.
- The other test projects that touch the changed constructors or paths, which compile through Hcc.Api but were not executed:
  - Catalogue PostExtra, POS room-post and StayRate tests;
  - Reservations / Inventory / Ota / WebBooking cancel tests.

The gate must cover these. The PostExtra, POS room-post and rate re-post guard paths have **no dedicated test**; only PostCharge is tested end to end.

## Defaults relied on (F8_Owner_Questions_2026-11-08.md)
- **SFQ-6:** a night posting is never refused by D7.
- **SFQ-7:** an extra over the limit onto the company bill is refused; the desk may post it to the guest bill.
- **SFQ-13:** the Manager limit is per company bill, at company-credit approval only.
- **SFQ-12:** deposits are on A. This is unchanged, and the cancel guard counts deposits on any folio.
- **SFQ-16:** Udhaar folio settles to the customer's Udhaar account within its limit (existing credit-guest rules).

## Open / not done
- **Production sync:** `clients/sync/lfmoney.ts` still sends `RequestGuestCredit {stayId, reason}` without a `folioId`. The split-folio production sync and the letter ↔ `folio_seq` mapping are still owed (F8-INT open item).
- **Lock order:** each guarded path locks the addressed folio first and then every folio row of the stay plus the company row. This is the same order as the existing `EnsureReceiptFolioAsync`, but it is not the contract's "seq order" when the addressed folio is B–D. No deadlock was seen; this is not proven.
- **Rate re-post:** the guard compares the company folio's exposure before and after the whole change, including the re-priced unposted nights. This follows the prototype's view rule; it was not tested.
