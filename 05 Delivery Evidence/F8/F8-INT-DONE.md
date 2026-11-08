# F8-INT DONE (integrator f8-int, Opus 5.5), 8 Nov 2026

Worktree `E:\Projects\HCC\worktrees\f8`, branch `claude/f8` (from `f8-setup` 037bd7bc). The gate was **not** started, `main` was **not** moved and nothing was pushed: those steps belong to the coordinator.

## Merged
All merges used `git merge --no-edit -Xignore-cr-at-eol`, in this order: sf-domain 26789255 → sf-server 438bd0b3 → sf-ui fa3faf32 → qw-na 17ee92aa → qw-ns 02fdf33d → qw-kds ce565188 → qw-adv 731e51e6 → qw-pm c7ee1f61 → rpt-p2-a b123808d → rpt-p2-b 6c784a2a → rpt-p2-c c14e761c.

**sf-tests was not merged.** That lane has not run yet; as instructed it is left to a later lane.

- **Conflicts:** only `features/closing/DayClosing.tsx`, where qw-na and qw-ns both added imports. I took the union of the imports. The no-show list is now placed inline in the Preflight body, after the night-audit panel. It replaces the qw-ns wrapper that reached into another element's props.
- **No conflict in `migrations.lock.json` or `catalogue.json`.** Only sf-domain added migrations, and rpt-p2-a and rpt-p2-c had reverted their `catalogue.json`.
- **Migrations:** the lock is in step at 235. f8-int needed no migration (4365–4369 and 4485–4489 are unused).
- **Invariants:** SCHEMA stays 16. The BASE and FY26 pins are untouched. No golden was regenerated (I1).

Commits after the merges:
- 629b87c7: integration items.
- 07569d00: split-stay golden on both sides, plus the server fixes it found.
- 8e1154b8: money-review fixes and registries.
- 081b798d: `kds` in the server feature catalogue; split checkout port made optional in partial hosts.
- This DONE commit.

## Integration items (work list)
1. **Server report catalogue.** `EngineTests.ServerCatalogue()` now lists the 23 P2 selectors (blocks F8-RPT-P2-A/B/C). Both `catalogue.json` copies were regenerated; `--check` reports them up to date (124 reports).
2. **Fit tests.** `stream-fit-ops.mjs` now expects the `pm-calendar` tab. `stream-fit.mjs` allows the four F8 phone files: `MaintenanceMobile.tsx`, `mobile/admin/Closing.tsx`, `mobile/money/Checkout.tsx` and `mobile/money/Folio.tsx`.
3. **OTA push.** In `OtaEndpoints.cs` the projector now receives `ISettingsReader`, so the automatic push honours the overbooking cap.
4. **Kitchen display page `kds`.**
   - `rbac.ts`: added to the Restaurant role's pages.
   - Feature catalogue, both twins: added to `checks` / `pos.checks`.
   - Added to the POS module's pages.
   - The RBAC matrix test now documents `kds` and the three SF commands.
5. **Night-audit signals.** New `Hcc.Infrastructure.SqlServer/Closing/SqlNightAuditSignals.cs`, wired in `NightAuditEndpoints.cs`. It reads:
   - departed stays that still hold an active or manual key card;
   - the last good backup, in hotel time.
6. **Split folio.** See the money review below.
7. **Company exposure.** In `SqlCompanyCreditStore` FactsSql, on split stays only, a night already posted on any folio is no longer counted again as an unposted night of the company folio.
8. **Contract.** Added SF-CONTRACT deviation 8:
   - server enum `FolioRouteCategory`;
   - endpoint `/folios/by-reservation/{id}/summary`;
   - billing DTOs address a folio by `FolioId`;
   - `CheckoutStayRequest.CreditApprovalIds`, `CheckoutStayResult.InvoiceNos` and `PostChargeRequest.ExplicitFolio`;
   - golden GSTIN `27AAPFU0939F1ZV`.
9. **Lane tests and screens.** The lane tests now run in the full suites (counts below). Not done:
   - the split-folio screens have not been looked at in a browser;
   - no seeded split stay was added, because it would move the pins;
   - the phone check-in routing editor is still missing (open items).
10. **Owner questions.** Six added to `02 Planning/F8_Owner_Questions_2026-11-08.md`, 33 in total:
    - RPQ-3: payment corrections report not built;
    - RPQ-4: asset register depreciation / WDV columns;
    - RPQ-5: PERM-YLD-001 granted to Owner + Manager;
    - RPQ-6: permission codes vs the catalogue's role text;
    - SFQ-19: server checkout without a hotel GSTIN;
    - SFQ-20: the GST rate of an adjustment on a mixed-rate folio with no nights.
11. **Excel.** A fractional value in an Int column (for example 0.5 leave days) is now written as a General number (`Renderers.cs` + `OpenXmlExporter.cs`).

## Money review (coordinator's F8-MONEY-REVIEW.md), each item checked in the code first

| Item | Status | Where |
|---|---|---|
| H1 company-credit receipt party | fixed | `domain/streams/accounting.ts:272` uses the folio payer's company. The credit-bills report, collection detail and the P2-B company statement now show the folio's invoice number (L1). |
| H2 adjustment rate on a folio with no room nights | fixed, with a default | Prototype: `domain/gst.ts` (single folio rate; `gstAdjustmentMixed`). Refusals in `handlers/folio.ts`: discount, approved discount, credit-note request and credit-note approval. Server twin: `BillingService.AdjustmentRateAsync`. **Mixed rates are refused rather than guessed** (SFQ-20, the CA decides the apportionment). Server credit notes are planned per invoice line and were not changed. |
| H3 split checkout not wired | fixed | New `Billing/SplitCheckoutPort.cs`; F8-INT lines in `StaysCommands.CheckoutStayAsync`; `IStaySplitCheckoutPort`. Invoices are issued inside the checkout transaction in letter order; a ₹0 folio gets none; the buyer is the folio payer (B2B with its GSTIN). Without a valid hotel GSTIN the checkout goes ahead with no invoice numbers (SFQ-19). |
| H4 receipt-folio rules | fixed | `BillingService.EnsureReceiptFolioAsync` is called in RecordReceipt and in every company-credit path. Server guest credit is requested by stay, so it always lands on folio A. Guest credit on an Udhaar folio cannot be requested on the server yet (open). |
| H5 company folio on a booking not linked to the company | fixed | `SqlBillingStore` FolioColumns: a company folio's company is its payer; the other folios of a split stay get NULL; anything else gets the reservation's company. |
| H6 rate re-post follows the current route | fixed | `StayRateCommands.ApplyAsync` reads the nights of every folio. The reversal and the new night both post with ExplicitFolio on the night's own folio. |
| H7 meal-plan line routed as extras | fixed | `CatalogueCommands.cs` posts the plan line with ExplicitFolio on the folio of its room night. |
| H8 explicit postings re-routed | fixed | A discount always posts with ExplicitFolio. New `PostChargeRequest.ExplicitFolio` flag. A charge routed to another folio now bumps that folio's version (DEC-041). |
| H9 Udhaar receivable raised on the stay guest | fixed | `SqlGuestCreditStore.GuestOfFolioAsync` uses the payer's credit account on an Udhaar folio. |
| H10 study loaders drop folios B–D | fixed | `SqlAnalyticsInputSource` LiveChargeSql and LiveReceiptSql are keyed by the stay's primary folio, so every folio is summed once. |
| M1 exposure double count | fixed | `SqlCompanyCreditStore` |
| M2 server D7 on extras to the company folio | **open** | Company credit still re-checks D7 at request, approval and execute, so the receivable stays capped. The posting-time refusal of variant 5.7c is not on the server. |
| M3 bill correction on a split stay | fixed | `BillCorrectionService.RequestAsync` refuses with `SF_SPLIT_BILL_CORRECTION` |
| M4 company-business report (R8) | fixed | `Reports/P2A/CompanyBusinessReport.cs` |
| M5 Udhaar journal matched by amount | fixed | the `accounting.ts` journal finds its UD row through the receipt's approval |
| M6 late-checkout line refused by D7 at checkout | fixed | `handlers/folio-split.ts` exempts the `late-half` line during CHECKOUT |
| M7 advances per stay | fixed | `domain/streams/advances.ts` counts per folio on split stays |
| M8 / R6 high balance | fixed | prototype `meas-daily/high-balance.ts`; server `HighBalanceReport` + `MeasDailyRules.HighBalanceSplit*` |
| L1, L7 | fixed | folio invoice references; `GuestsSql.LateChargeExpr` reads every folio |
| L2, L3, L4, L5, L6, L9 | open (LOW) | see Open items |

The server golden also found three faults outside the review. Each looked at the primary folio only:
- `SqlBillingStore.FindRoomNightAsync` (the "night already posted" check);
- `SqlClosingStore`'s check that tonight's night is posted;
- `SqlStayChangeStore.PostedNightsAsync`.

As a result the day close failed with NOT_AVAILABLE when the nights sat on folio B. All three now read every folio of the stay (fixed).

## Golden stay §5.7, both sides end to end

### Prototype: `03 Prototype/tests/stream-f8-int.mjs` (5 tests, `[F8-INT]`)
- **Folio totals:**
  - A: net ₹1,700 + ₹150 tax = ₹1,850;
  - B: net ₹9,000 + ₹450 tax = ₹9,450.
- **Invoices:** two, numbered consecutively, A then B. `Booking.invoiceNo` holds A's number.
- **GST documents:** two.
  - B2C: taxable ₹1,700, CGST ₹75, SGST ₹75.
  - B2B: taxable ₹9,000, CGST ₹225, SGST ₹225.
- **Control:** the sum of each tax head over the two documents equals the unsplit control's one invoice of ₹11,300.00.
- **Accounting:**
  - every revenue and output-GST ledger equals the control;
  - `1110/<Deccan>` Dr ₹9,450;
  - 1100 nets to 0.
- **Report rules:**
  - R8: the company's lines are exactly the three nights.
  - R9: the analytics input bills the stay once, at ₹11,300.
  - R6: with the company limit lowered, the company folio row turns red.
- **Review items:**
  - H2: a credit note on folio A is refused.
  - Variant (a): a credit note on B is credited at 5 %, i.e. ₹150 GST, and recorded on B.
  - M7: an overpaid folio A shows as an advance held while B still owes.
- **Note:** food posted with CHARGE lands on ledger 4100. The 4200 split in §5.7 needs a POS room-post (sf-tests).

### Server: `08 Production/tests/Hcc.Integration.Tests/F8IntSplitGoldenTests.cs` (HTTP, real composition)
- **Steps run:**
  1. Book, register the guest, check in.
  2. OpenFolio: company Deccan Agro, routing room.
  3. ₹1,000 UPI advance on A.
  4. Laundry and food on A.
  5. Three nights, routed to B, each followed by a day close.
  6. ₹850 cash on A.
  7. Company credit ₹9,450 on B: Reception requests, the Owner approves.
  8. CheckoutStay.
- **Invoices:** CheckoutStay returns two consecutive `InvoiceNos`.
  - A: B2C, taxable ₹1,700, CGST ₹75, SGST ₹75, total ₹1,850.
  - B: B2B with GSTIN 27AAPFU0939F1ZV, taxable ₹9,000, CGST ₹225, SGST ₹225, total ₹9,450.
  - Σ = ₹11,300.
- **After checkout:** the company-credit receipt sits on folio B for Deccan. Deccan's D7 exposure is ₹9,450, folio B only.
- **Refusals (review H4 / H5):** a deposit on B, cash on B and company credit on A are each refused.

## Counts actually run
Everything ran through `tools/Invoke-Serialized.ps1` with label f8-int. Logs are in `.scratch\` and are not committed.

### Prototype
- Full `npm test` on 8e1154b8: **3265/3265** passed (30 async).
  - The first full run, on 07569d00, gave 3238/3264 with 26 failures. All 26 were registry tests that the lanes could not see in their filtered runs: the RBAC page and command registries, the feature catalogue, the Restaurant role's pages, the tax-report count and the payment-dialog regex. They were fixed in 8e1154b8.
- Same run: `tsc` exit 0, `npm run build` exit 0, `npm run build:production` exit 0.

### Server
- `dotnet build Hcc.slnx -maxcpucount:1`: 0 warnings, 0 errors (also on 081b798d).
- Full serial `dotnet test Hcc.slnx -maxcpucount:1 --logger trx` on 8e1154b8, 43 projects: **4629/4631** passed, 2 failed.
  - Configuration `FeatureCatalogTests`: `kds` was missing from the server catalogue.
  - Stays `HttpEndpointTests`: HTTP 500, because the split checkout port could not be built in the Stays-only host.
- Both were fixed in 081b798d. Re-run of the affected projects on 081b798d:
  - Hcc.Stays.Tests **110/110**;
  - Hcc.Configuration.Tests **47/47**;
  - Hcc.Integration.Tests **25/25**, including the golden.
- **The full solution was not re-run after 081b798d.** The gate will run it.

### Other checks
- `migrations-lock.mjs`: in step, 235 migrations.
- `rpt-catalogue.mjs --check`: up to date.
- `themes.mjs` and `feature-catalogue.mjs` regenerated; only `feature-catalogue.json` changed (`kds`).
- Secret scan of the lines added since f8-setup: no match.
- No golden was regenerated by f8-int (I1).

## Split-folio cross-check tests still to write (sf-tests or a later lane)
- **The SF-GOLDEN scenario as briefed:** fixture extension + bind, `stream-sf-golden.mjs` and `SfGoldenTests.cs`. Include the food line through a POS room-post (ledger 4200) and the full trial balance against the control.
- **Variant (b):** the D7 limit is lowered before checkout. Expect the refusal text, with the guest folio unaffected. Both twins.
- **Variant (c):** an extra ₹600 onto B over the limit is refused, while the same onto A passes. The server needs M2 first.
- **Variant (d), the DEC-061 walk:** Housekeeping, Supervisor and Maintenance must see no folio money on the split dialog, the folio switcher, the settle sheet or the folio summary endpoint grants.
- **Server tests for the fixes in this commit range:**
  - H6: rate re-post after the routing changed;
  - H7: meal plan with room routed to B;
  - H8: discount on A stays on A; the PostCharge ExplicitFolio flag;
  - H9: Udhaar folio guest credit, once it can be requested;
  - M3: bill-correction refusal;
  - R6: the high-balance SQL row;
  - R8: the company-business SQL;
  - R9: `SqlAnalyticsInputSource` billed ₹11,300 on a split stay, and `SqlStudyPackFacts` agreeing;
  - M1: night posted on A, then room routed to B.
- **Prototype tests:**
  - M5: two guest credits of the same amount;
  - M6: late half day with extras routed to the company folio;
  - H2 on the server: discount on a mixed-rate folio refused.
- **Folio-join audit SQL tests.** No list may show a split stay twice: movement register, desk folios, guests, MEAS daily.
- **Other SQL / HTTP tests:**
  - the per-folio DocumentSql print;
  - the night-audit endpoint, with key-card and backup signals;
  - the no-show-due endpoint;
  - the OTA projector's overbooking cap with a seeded setting.

## Open items
- **Production sync for split folio is not written** (`clients/sync/money*.ts` and the F8-SF-SERVER registry block). In the production build the SF_* commands are refused as unmapped and folios B–D are not loaded. The mapping between folio letters and `folio_seq` is still owed.
- **M2:** no server-side D7 check when a charge is posted to the company folio.
- **Udhaar guest credit on the server:** it cannot be requested on an Udhaar folio, because the request goes by stay, which means folio A.
- **Cancel guard:** `BillingReservationMoney` reads the primary folio only, so money paid on folio B before arrival is not seen when the booking is cancelled. This is an edge case and was not in the review.
- **L6:** `ChargeResult.FolioId` returns the folio the caller addressed, not the folio the charge was routed to.
- **LOW items still open:**
  - L2: the departed-stay refund check is per booking;
  - L3: the pending-request guards are per booking;
  - L4: Udhaar `gstBps` metadata;
  - L5: the prototype company-business report duplicates `sfCompanyLines`;
  - L9.
- **Screens not checked:**
  - the sf-ui screens have not been looked at in a browser;
  - there is no seeded split stay;
  - phone check-in (`mobile/frontdesk/Arrivals.tsx`) has no routing editor;
  - the split dialogs have not been fit-checked;
  - the qw-kds and qw-pm pages were never fit-checked by any lane.
- **Reviews:** no `F8-review-*.md` files exist; the coordinator's money review was used instead.
- **Owner questions:** all 33 are unanswered; the defaults are applied. The DEC proposals of contract §14 are for f8-docs.
- **Not done by design:** the gate was not started, `main` was not moved and nothing was pushed.
