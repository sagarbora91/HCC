# F8D-LOW DONE (lane f8d-low), 8 Nov 2026

Worktree `E:\Projects\HCC\worktrees\f8d-low`, branch `claude/f8d-low`, cut from `claude/f8c` 0af7a992. Code commit **0d12db13** (L2–L9), then 12261dde (this report). The follow-up commit (Udhaar payer name, PostExtra L6) is described at the end. Nothing was pushed; `main` was not touched.

The review text came from the coordinator's `F8-MONEY-REVIEW.md` (not committed; it was in the coordinator's scratchpad). Items are L2, L3, L4, L5, L6 and L9 from `05 Delivery Evidence/F8/F8-INT-DONE.md` (open items).

## What changed

| Item | Fix | Where |
|---|---|---|
| L2: the departed-stay refund check was per booking | `folioOutstandingRefunds(s, bookingId, except, b?, folio?)`: on a split stay, only refund requests whose target receipt is on that folio count. Both REFUND_REQUEST and REFUND_EXECUTE pass the receipt's folio. Single-folio stays: unchanged. | `03 Prototype/src/domain/streams/folio.ts` (folioOutstandingRefunds), `src/services/mock/handlers/folio.ts` (REFUND_REQUEST, REFUND_EXECUTE) |
| L2 (server) | No change. `BillingService` already reads `ApprovalsForFolioAsync(f.FolioId)`, so the check is per folio (`BillingService.cs` RequestAdjustment refund case and ExecuteRefund). | — |
| L3: the pending-request guards were per booking | `pendingOf(k, x, kind, folio?)`: on a split stay only a request on the same folio (`meta.folio`, absent = A) blocks. Applies to discount, company credit and credit note. Guest credit does the same per folio (A or an Udhaar folio). CHECKOUT still refuses while any folio has a pending request. | `handlers/folio.ts` (pendingOf + its 4 callers), `handlers/counter.ts` (GUEST_CREDIT_REQUEST) |
| L3 (server) | No change. Server approvals are already per folio (`ApprovalsForFolioAsync`, `GuestCreditCommands`). | — |
| L4: Udhaar `gstBps` metadata | `gcrCheckout` on a split stay takes the settled folio's room-night rate, else the one rate all its lines share, else 0 (mixed rates, SFQ-20). Single-folio stays: the first room line, as before. | `handlers/counter.ts` (gcrFolioBps, gcrCheckout) |
| L4 (server) | No change. The server writes `gst_bps = 0` on every guest-credit receivable (`SqlGuestCreditStore.InsertReceivableAsync`). This is metadata only: there is no Sales voucher for a guest-credit receivable. | — |
| L5: the company-business report duplicated `sfCompanyLines` | `companyLinesOf(s, b, companyId)` now calls `sfCompanyLines`. Stays in a bill-to-company block now count for the company; the old copy missed them. | `03 Prototype/src/domain/reports/p2-a/company-business.ts` |
| L5 (server) | No change. `CompanyBusinessReport.cs` already follows the same R8 rule (company folio, else `r.company_id`). The server has no prototype-style bill-to-company block (groups use master-folio routing). | — |
| L6: `ChargeResult.FolioId` was the addressed folio | New port `IBillingStore.ChargeFolioAsync(tx, chargeId)` (SQL: the charge's `folio_id` + that folio's `row_ver`). PostRoomNight (new night and already-posted night) and PostCharge return the routed folio and its version when routing put the line on another folio. Otherwise the result is unchanged. Not changed: audit target, `ChargePosted` folioId, and CatalogueCommands PostExtra (still the addressed folio, see below). | `08 Production/src/Hcc.Application/Billing/BillingPorts.cs`, `BillingService.cs` (PostedOnAsync + 3 call sites), `Hcc.Infrastructure.SqlServer/Billing/SqlBillingStore.cs` (ChargeFolioAsync) |
| L9: the D7 hook after CHECKOUT subtracted a Departed folio | On a Departed stay: `used` = the company's full exposure (grpExposure no longer counts the stay), `needed` = the folio's open balance. sfFolioExposure would count the part already in the receivable a second time. In-house stays: unchanged. | `handlers/folio-split.ts` (sfAfterCommand) |
| L9 (server) | No change. `CompanyFolioPostingGuard` gets `UsedWithoutFolio` from SQL; it does not subtract. | — |

**Sync (coordinator's question):** L6 needs no `clients/sync/*` change. The sync mappers (`money.ts` PostCharge, `catalogue.ts`) never read `ChargeResult.FolioId` or `FolioVersion`. No `clients/sync` file and no folio/phone/kds-pm UI file was edited.

## Tests actually run (all through `tools/Invoke-Serialized.ps1`, label f8d-low)
- `npx tsc --noEmit`: exit 0.
- Prototype, `HCC_TEST_ONLY=stream-(f8|split|rpt-p2-a|counter|folio|groups|lf-counter)`: **140/140** passed. This includes the new `tests/stream-f8d-low.mjs` (9 tests: L2 ×2, L3 ×2, L4 ×2, L5 ×2, L9 ×1), stream-f8-int, f8-fix, f8-tests2, split-folio, split-ui and rpt-p2-a.
- Prototype, `HCC_TEST_ONLY=stream-(rpt-engine|rpt-docs|accounting|expenses|lf)`: **494/494** passed (report goldens via rpt-engine; accounting; the LF suites).
- `dotnet test tests/Hcc.Integration.Tests -maxcpucount:1` filtered to F8dLowChargeFolioTests, F8IntSplitGoldenTests, F8cSyncWireTests, F8Tests2SplitTests and F8FixTests: **12/12** passed (1 + 1 + 1 + 6 + 3, including the new L6 test). The build had 0 warnings.
- `dotnet test tests/Hcc.Billing.Tests -maxcpucount:1`: **81/81** passed.

## Not run
- Full `npm test`, `npm run build` / `build:production`, the full `dotnet test Hcc.slnx`, and every other server project. The gate will run these.
- No browser or emulator check. No UI file was changed.

## Defaults and rules relied on
- SF-CONTRACT §1 view principle (per-booking rules run on `sfView`), §4 CHECKOUT (no pending approval on any folio), §6 (refund after a credit note checked against that folio), §7.2/7.3 (D7 exposure per company folio), §8 R8 (`sfCompanyLines`).
- SFQ-20 default: no single GST rate on a mixed-rate folio without room nights. Used for the L4 value 0.
- Server twin behaviour (approvals and versions per folio, DEC-041) for L2 and L3.

## Notes, not stopped
- **L3:** prototype approvals stay bound to the stay's version (the prototype has no per-folio version). So deciding one folio's request makes another folio's open request stale, and it must be requested again. This already happens after any posting on the stay. The server binds to the folio version. A per-folio version in the prototype would need a state field; it is not built.
- **L6:** CatalogueCommands PostExtra now follows the same rule (follow-up commit, below).
- **L4:** on single-folio stays the twins already differed before this lane (prototype: room rate; server: 0). Not changed.
- No golden was regenerated; the company-business golden still passes.

## Stopped
None of L2–L9.

Nothing (the coordinator's extra item is done in the follow-up below).

## Follow-up (coordinator's extra items)
| Item | Fix | Where |
|---|---|---|
| Udhaar payer name (SF-CONTRACT §2.2) | OpenFolio on an Udhaar folio now reads the credit customer's name from the customer key. If the desk sends no name, the folio's `payer_name` is that customer's name, so the invoice (`FolioPartyAsync`) and the folio print show the customer, not the stay guest. A name the desk sends is kept, and an unknown or inactive key is still refused (NOT_FOUND). The store port `CreditGuestActiveAsync` (bool) is replaced by `CreditGuestNameAsync` (name or null); it has one implementation. | `08 Production/src/Hcc.Application/Billing/SplitFolioService.cs` (ISplitFolioStore, OpenAsync), `Hcc.Infrastructure.SqlServer/Billing/SqlSplitFolioStore.cs` |
| L6 on PostExtra | PostExtra returns the routed folio and its version, the same rule as PostCharge. | `08 Production/src/Hcc.Application/Catalogue/CatalogueCommands.cs` (PostExtraAsync) |

Tests, run through `Invoke-Serialized` on the follow-up code. The build had 0 warnings.
- `Hcc.Integration.Tests`, filtered to F8dLowChargeFolioTests, F8IntSplitGoldenTests, F8cSyncWireTests, F8Tests2SplitTests and F8FixTests: **13/13** passed. This includes the new fact `F8D_LOW_udhaar_folio_payer_name_is_the_credit_customers_when_none_is_sent` and the PostExtra step added to the L6 fact.
- `Hcc.Catalogue.Tests`: **15/15** passed.
- `Hcc.Billing.Tests`: **81/81** passed.
- No prototype file changed in the follow-up, so the prototype tests were not re-run. The full solution was not run.

## Screenshots
None (no UI change).
