# F8-SF-DOMAIN DONE

Lane sf-domain / Opus 5.5 / branch `claude/f8-sf-domain` / worktree `E:\Projects\HCC\worktrees\f8-sf-domain` / base `f8-setup` 037bd7bc.

STEP 1 commit: **e6021c85**

## Commits
e6021c85 F8 sf-domain step 1: split folio DDL, server rules/contracts, prototype SF commands + routing hook
9b80154a F8 sf-domain: record step-1 hash e6021c85
(+ this DONE commit)

## Files
Prototype
- `03 Prototype/src/domain/streams/folio-split.ts` — F8-SF-DOMAIN block: `SF_REASONS`, `SF_NIGHT_COMMANDS`, `sfCompanyFolio`, `sfCompanyLines` (R8), `sfNewLineFolio`, `sfNewReceiptFolio`, `sfCmdFolio`. Setup signatures unchanged.
- `03 Prototype/src/services/mock/handlers/folio-split.ts` (new) — `SF_FOLIO_OPEN`, `SF_ROUTE_SET`, `SF_FOLIO_REMOVE`, `sfFail`, routing hook `sfAfterCommand`.
- `03 Prototype/src/services/mock/commands.ts` — F8-SF-DOMAIN block: `sfAfterCommand(input,s,cmd)` after the handler, before the commit.
- `03 Prototype/src/services/mock/handlers/folio.ts` — F8-SF-DOMAIN blocks: PAYMENT / CHARGE / DISCOUNT / COMPANY_CREDIT_REQUEST / CREDIT_NOTE `{folio?}`, APPROVAL (discount, company credit, credit note per folio), refund departed check per folio, CHECKOUT of a split stay.
- `03 Prototype/src/services/mock/handlers/counter.ts` — guest credit (LF B2) per folio: A or an Udhaar folio; `gcrCheckout(k,x,folio)`.
- `03 Prototype/src/services/mock/handlers/bill-correction.ts` — `SF_SPLIT_BILL_CORRECTION`.
- `03 Prototype/src/services/mock/handlers/stay-rate.ts` — rate re-post on the original night's folio.
- `03 Prototype/src/domain/streams/groups.ts` — `sfFolioExposure`, `sfStayExposureFor`; `grpCompanyBookings`, `grpExposure`, `grpCreditPreview` folio-aware.
- `03 Prototype/src/domain/gstr1.ts`, `src/domain/reports/tax/shared.ts` — documents enumerated through `sfInvoiceDocs`.
- `handlers/index.ts`, `services/mock/index.ts` — F8-SF-DOMAIN blocks.
- `03 Prototype/tests/stream-split-folio.mjs` (new).
Server
- migrations `4350_sf_folio_payer.sql`, `4351_sf_folio_route.sql` (+ lock, + `Hcc.slnx` F8-SF-DOMAIN block).
- `Hcc.Domain/Billing/FolioRouting.cs`, `Hcc.Domain/Billing/SplitFolioRules.cs`, `Hcc.Contracts/Billing/SplitFolioContracts.cs`, `Hcc.Application/Billing/SplitFolioService.cs` (new).
- `Hcc.Application/Billing/BillingService.cs` — F8 block `SettleSplitForCheckoutAsync`.
- `Hcc.Infrastructure.SqlServer/Groups/SqlCompanyCreditStore.cs` — F8 block: exposure per company folio.
- `tests/Hcc.Billing.Tests/SplitFolioTests.cs` (new).

## What was built
- §2.3 DDL exactly as frozen (4350, 4351); 4352 not used (SFQ-4 default: no new permission).
- §2.4 server domain + contracts. **Name deviation:** enum `FolioRouteCategory` (not `RouteCategory`): `Hcc.Domain.Groups.RouteCategory` already exists and `GroupPorts.cs` / `GroupCommands.cs` failed with CS0104 (ambiguous). Existing billing DTOs were NOT given `folioSeq`: they already take `FolioId`, and a split folio is addressed by the `FolioId` from `FolioSummaryDto` (BillingContracts.cs is outside my row).
- §3 prototype routing: hook `sfAfterCommand` in `execute()` (only when the bookings slice was touched; stamps new lines and receipts, plan line → its night's folio, late check-out half day → route(extras), reversal / credit / correction → the original's folio). `SF_FOLIO_OPEN` stamps every existing line and receipt with 'A'. A rate re-post goes on the original night's folio. Bill correction is refused on a split stay.
- §4 commands and every SF_* reason (`details.reason`). §5 checkout of a split stay: settles every folio (company credit per company folio, OTA credit on A, guest credit on A or an Udhaar folio), then INV numbers in letter order, a ₹0 folio gets none, `Booking.invoiceNo` = the first one. `gstr1.ts` / `tax/shared.ts` enumerate through `sfInvoiceDocs`. §6 credit note per folio (cap, target invoice, `gstForFolioAdjustment` on the view, `creditNotes[].folio`).
- §7 D7: prototype `sfFolioExposure` / `sfStayExposureFor` (company folio only, nights only while room is routed to it), `grpCompanyBookings` includes stays whose company folio names the company. The hook refuses any non-night command that adds a line to (or opens / re-routes) a company folio when the company goes over its limit. Night commands are never refused. Manager ≤ ₹10,000 per company folio, read through `adminLimitFor(s,'Manager','PERM-PAY-005')` (split path only, so single-folio stays are unchanged). Server: `SqlCompanyCreditStore` facts SQL per company folio (F8 block), `SplitFolioRules.EnsureCompanyCredit` / `FolioExposure`.
- Server application: `SplitFolioService` (OpenFolio / SetFolioRouting / RemoveFolio, D7 estimate check, audited) over the new port `ISplitFolioStore`; `BillingService.SettleSplitForCheckoutAsync` settles every folio in one transaction and returns the per-folio totals for invoicing.

## Tests
Actually run (through Invoke-Serialized, label f8-sf-domain):
- `npx tsc --noEmit` → 0 errors (after two fixes: gstr1 `invoiceNo!`, the enum rename).
- `dotnet build src/Hcc.Infrastructure.SqlServer -maxcpucount:1` → 0 errors, 0 warnings.
**NOT run:** `tests/stream-split-folio.mjs` (7 tests written), full `npm test`, `dotnet test` of Hcc.Billing / Groups / Invoices / Accounting (`SplitFolioTests.cs`, 8 tests written). The lock gave exit 75 five times in a row (other lanes' npm test runs), so I stopped as the brief says. No count is claimed. Scripts are ready: `.scratch\sf-test.ps1`, `.scratch\npm-test.ps1`, `.scratch\dotnet-test.ps1`. I1 (goldens unchanged) is NOT proven yet: the full `npm test` must run.

## Defaults relied on (owner questions unanswered)
SFQ-1 max 4 folios · SFQ-2 two categories · SFQ-3 plan line with its night · SFQ-4 billing permission, no 4352 · SFQ-5 routing for later postings only · SFQ-6 night posting never refused · SFQ-8 no invoice for a ₹0 folio · SFQ-9 guest payer may carry a GSTIN · SFQ-10 group member refused · SFQ-11 bill correction refused on a split stay · SFQ-12 deposits on A · SFQ-13 Manager ₹10,000 per company folio · SFQ-18 credit note on a company-credit folio behaves as today.

## Open items
- Run the tests above. Fix any failure. Check that the goldens do not move (I1).
- Server SQL adapter of `ISplitFolioStore` is not written. Also open: the endpoints, `SplitFolioService` registration in HccComposition, and the `CheckoutStay` call to `SettleSplitForCheckoutAsync` + per-folio invoice issue (`CheckoutStay` is outside my row). No server SQL test of the folio exposure.
- SQL exposure edge: when room is routed to the company folio after some nights were already posted on A, those nights look unposted to `GroupEstimate` and are counted again on the company folio. The prototype checks posted nights over the whole stay.
- Prototype: a split stay with no company folio but booked to a company counts whole, as today (the contract does not cover this case).
- Prototype `SF_ROUTE_SET` on folio A takes the categories back from the other folios.
- `domain/streams/folio.ts` and `catalogue.ts` were not changed (not needed: the hook routes the plan line).
- The golden-stay figures (§5.7) are not asserted here: that is sf-tests' scenario.
- Variant 5.7a (credit note GST 5 %) is only asserted as "credit at the room line's rate".

## Integration needed
- `03 Prototype/src/domain/streams/accounting.ts` line ~272 (I5): company-credit receipt party must be the folio payer: `ctx.company = (r.folio&&b.folios?.find(f=>f.id===r.folio&&f.payer.kind==='company')?.payer.ref) ?? b.company?.account`. Without this, a split stay whose booking has no `company.account` posts 1110 without a party.
- `domain/reports/money/credit-bills.ts`: company of a company-credit receipt from its folio payer (same rule). This belongs to the rpt-p2-b / f8-int lanes.
- sf-server: implement `ISplitFolioStore` (LockStayAsync creates the seq-1 folio if missing), endpoints, `CompanyFolioCreditAsync` from the F8 facts SQL; use `FolioRouteCategory`.
- f8-int: in `CheckoutStay`, for a reservation with more than one live folio call `SettleSplitForCheckoutAsync`, then issue invoices per non-zero folio in seq order.

## HANDOFF bullet
F8 sf-domain: split folio DDL (4350/4351), server routing / rules / contracts / SplitFolioService, prototype SF_* commands + routing hook + per-folio checkout / invoices / credit notes + D7 per company folio. Step 1 = e6021c85. tsc and server build green. Lane tests are written but NOT run (lock busy 5×). SQL store, endpoints and the CheckoutStay wiring are owed.