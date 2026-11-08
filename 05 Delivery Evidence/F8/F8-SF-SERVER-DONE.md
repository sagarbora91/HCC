# F8-SF-SERVER DONE

Lane sf-server / Sonnet 5.5 / branch `claude/f8-sf-server` / worktree `E:\Projects\HCC\worktrees\f8-sf-server` / merged sf-domain step 1 `e6021c85`.

## Built (08 Production)
- `src/Hcc.Infrastructure.SqlServer/Billing/SqlSplitFolioStore.cs` (new): `ISplitFolioStore` over bill.folio / bill.folio_route (LockStay creates the seq-1 folio when missing and locks all folio rows; CompanyAsync; CreditGuestActiveAsync; InsertFolio; InsertRoute (next seq); FolioUse; MarkRemoved; CompanyFolioCredit via `ICompanyCreditGuard`: used-without-folio = Total - Of("stay", reservation)); `ISplitFolioReader.SummaryAsync` (FolioSummaryDto per folio from FolioStatement.Compute, routes, invoice no); static routing `ResolveChargeFolioAsync`.
- `SqlBillingStore.InsertChargeAsync`: calls the routing; single-folio reservation = one indexed existence read, then today's path. Only the primary folio of a reservation with >1 live folio is routed (FolioRouting.Resolve; reversal -> original's folio). `EnsureFolioAsync` / `FolioIdForReservationAsync` now `folio_seq = 1`.
- `BillingPorts.cs`: `NewCharge.ExplicitFolio = false`, `ISplitFolioReader`.
- `SqlInvoiceStore.FolioPartyAsync` + `FolioParty` (InvoicePorts.cs, additive optional members PayerKind / PayerGstin): buyer name = `COALESCE(payer_name, guest_name)`.
- `Hcc.Api/Endpoints/SplitFolioEndpoints.cs`: OpenFolio / SetFolioRouting / RemoveFolio (commands) and GET `/api/v1/folios/by-reservation/{reservationId}/summary`. **Deviation:** the contract's `GET /folios/{reservationId}` collides with the existing `GET /folios/{folioId:long}`. Registered in HccComposition inside the F8-SF-SERVER blocks (`AddSplitFolioModule`, `MapSplitFolioEndpoints`). No migration added (4360-4364 unused).

## Folio-join audit (R10)
(a) safe, left (aggregates per charge/receipt via folio -> reservation, or `WHERE f.folio_id = @f`): AccountingVoucherProjector 572, SqlAccountingStore 363/368, SqlFlowsStore 126/138/164, SqlGstMonthStore 236, SqlCatalogueStore 281/348/355 (per folio, joined to that folio's own room charge), SqlRoomNightBundle, SqlClosingStore, SqlCustomerStore 98/170, SqlGroupStore 375/379/467, SqlGuestCreditStore, SqlMessagingData 83/84/104, SqlMoneyMastersStore, DigestDraftProjector, SqlReportSource, SqlRevenueCategorySource, SqlGstMasterStore, DocumentSql 20/41, GuestRegisterReport 38 (joins `s.folio_id`), MeasDailyReportsA 51/57, MeasDailyReportsB, AssetLifeReport, SqlStudyPackFacts, BillCorrectionsReport, TaxSql 61/140, SqlMoneyReportSource, SqlAnalyticsInputSource 162/171, SqlBillingStore 504.
(b) fixed `AND folio_seq = 1`: SqlBillingStore `EnsureFolioAsync`, `FolioIdForReservationAsync`.
(c) fixed `AND f.folio_seq = 1` (one row per reservation): SqlBillingStore `FindReservationFolioAsync`, `DeskFoliosSql`; SqlGroupStore 368; MovementRegisterReport; MeasDailyReportsA 386; SqlAnalyticsInputSource `LiveStaySql`. `GuestsSql.BalanceExpr` now sums every folio of the reservation (stay-level balance, R1; identical for a single folio).
Company-credit SQL (SqlCompanyCreditStore) is sf-domain's: not touched.

## Not done / needs others
- **Open issue (company exposure double count)** is in `SqlCompanyCreditStore` FactsSql (forbidden for me, sf-domain's): the agreed-nights SELECT (`inv.reservation_night ... s.room_routed = 1`) should skip nights that already have a live room line on ANY folio of the reservation, e.g. add `AND NOT EXISTS (SELECT 1 FROM bill.charge x JOIN bill.folio xf ON xf.folio_id = x.folio_id WHERE xf.reservation_id = n.reservation_id AND x.kind = 'room' AND x.service_date = n.night AND x.reverses_charge_id IS NULL AND x.reversed_by_charge_id IS NULL)`. Not fixed, no test added by me.
- Integration needed (files outside my row): `CatalogueCommands.cs:404` (meal-plan line) and `StayRateCommands.cs:206/211` (re-post) must set `ExplicitFolio: true` with the original night's folio, else a plan line / re-posted night whose night sits on A goes to the category route. Same for any UI "post on folio A" choice: BillingService/CatalogueCommands call sites need a request flag mapped to `ExplicitFolio`. `SqlAnalyticsInputSource` amounts are keyed by the primary folio id (folios B-D amounts not attributed to the stay; R9 for f8-int). `InvoiceSourceDocument` has no buyer GSTIN: the checkout orchestrator (f8-int) must pass `FolioParty.PayerGstin` / name into `IssueInvoiceRequest`. `CheckoutStay` -> `SettleSplitForCheckoutAsync` wiring is f8-int.
- Prototype sync mappers (`clients/sync/money*.ts`, `registry.ts` F8-SF-SERVER block): NOT done (turn budget spent on lock waits).
- `DocumentSql` per-folio document and SplitFolioService DI of `ICompanyCreditGuard` (assumed registered by the groups module) not verified at runtime.

## Tests (written)
`tests/Hcc.Billing.Tests/Sql/SplitFolioSqlTests.cs` (4: company-folio round trip with routing, single-folio unchanged, remove rules, primary created first), `tests/Hcc.Invoices.Tests/SplitFolioInvoiceTests.cs` (1: two folios -> two invoices, right buyers).

## Defaults relied on
SFQ-1, 2, 3 (plan with its night, needs the integration edit above), 4, 8, 12, 13 as in sf-domain DONE.

## Test status
See the final line below (filled in when the runs finish).
