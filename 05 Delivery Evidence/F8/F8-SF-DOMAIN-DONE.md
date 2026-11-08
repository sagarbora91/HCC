# F8-SF-DOMAIN DONE

Lane sf-domain / Opus 5.5 / branch `claude/f8-sf-domain` / worktree `E:\Projects\HCC\worktrees\f8-sf-domain` / base `f8-setup` 037bd7bc.

STEP 1 commit: see `05 Delivery Evidence/F8/SF-DOMAIN-FIRST-COMMIT.txt` (filled in below at the end).

## Commits
(filled at the end)

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
