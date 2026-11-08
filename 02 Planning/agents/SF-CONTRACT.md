# SF contract — wave F8 split folio (DEC-152) + D7 credit limits per folio (8 Nov 2026)

Base `claude/f8` from local `main` 6699b4e4 (MEAS F12 merged, tag `wave/meas`). Plan: `02 Planning/HCC_Wave_Plan_F1-Fn_2026-11-02.md` §F8 (wins on intent); Build Plan v2 §W6; DEC-152 (split folio), DEC-076 (company credit limits, audit D7), DEC-063 (discount / correction / credit-note approvals), DEC-069 (dated GST master), DEC-197 (invoice series). **This file wins on names, shapes, numbers and money rules.** Lane rules: wave plan §1 and `02 Planning/agents/F8_Briefs.md`. Report rules: `HCC_Report_Catalogue_v1.md` §1 and `F7-CONTRACT.md`. Owner questions with the defaults applied here: `02 Planning/F8_Owner_Questions_2026-11-08.md` (ids **SFQ-n**, **RPQ-n**, **QWQ-n**). Coordinator: Opus; commits carry `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` (Sonnet lanes: `Claude Sonnet 5.5`).

**Deviations from the plan, decided by the setup commit (each with its reason):**
1. **SCHEMA 15 → 16, not 14 → 15.** ADM-2 already took 15 (custom roles). The setup commit declares the optional fields (§2.1) and bumps to 16 **and re-pins the three state hashes once** (BASE `c613bb7b…`, FY26 history `ef28d4b1…`, FY26 scenario `ae519b00…`; the FY26 *bookings* hash `8b253c91…` is unchanged, which proves no booking changed). Precedent: the F6 setup commit (13 → 14). No lane may change BASE or FY26 (that would move the pins again); the integrator re-pins only if a merged lane proves it had to.
2. **Branch / worktree.** The plan said `claude/sf` in `worktrees\sf`; the wave branch is `claude/f8` in `E:\Projects\HCC\worktrees\f8` (gate `-Wave f8`). Lanes are `claude/f8-<lane>` in `E:\Projects\HCC\worktrees\f8-<lane>`.
3. **Migrations.** The lock's highest version is **4920**; every F8 number (4350–4369, 4450–4489) is below it, so each needs `node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order "F8 <lane>: reserved range (wave plan §1.4)"`. Checked at the setup commit: nothing in 4350–4369 or 4450–4489 on disk or in the lock (233 migrations, lock in step). The money-carrying DDL (folio payer, routing) is **sf-domain's (Opus) 4350–4352**; sf-server keeps 4360–4364 for indexes and query support.
4. **Routing categories are two (`room`, `extras`)**, the vocabulary the server's groups module already uses (`grp.routing_rule`, migration 2200). Finer categories are SFQ-2.
5. **Group master payer is declared, not built.** The groups module (2200) already routes member lines to the master folio with its own rules and transfers; a second routing on the same stay would double count. F8 refuses a split on a group member (`SF_GROUP_MEMBER`, SFQ-10).
6. **No transfer of an already posted line between folios in F8** (the server's `bill.charge` is append-only and a reversal + re-post would show up as a correction in the MEAS allowances report). Routing applies to postings after it is set (SFQ-5).
7. **Quick-win scopes follow what MEAS already built** (`yld.overbook.<type>`, `yld.restrictionsEnforce`, `measNightAuditLines`, `fd.noShowTime`, `fdmNoShowCharge`, the kitchen board `posKitchenAt`, PM plans `mntPmDue`); see §13.
8. **(f8-int, after the lanes)** Server enum is `FolioRouteCategory` (not `RouteCategory`: `Hcc.Domain.Groups.RouteCategory` already exists, CS0104); the folio summary endpoint is `GET /api/v1/folios/by-reservation/{reservationId}/summary` (`/folios/{id}` is the existing folio-id route); existing billing DTOs address a split folio by its `FolioId` (from `FolioSummaryDto`) instead of a new `folioSeq`; `CheckoutStayRequest` gains `CreditApprovalIds` and `CheckoutStayResult` gains `InvoiceNos` (split stay). The golden GSTIN of §5.7 `27AAACD1111A1Z1` fails the GSTIN check digit: tests use the valid fictional `27AAPFU0939F1ZV`.

---

## 1. Invariants (every lane, every test)

| # | Rule |
|---|---|
| I1 | **A stay without `folios` is exactly today's stay.** `sfView(b,'A')` returns the booking itself; every existing golden stays green unchanged: `accounting-golden.json`, `ACC-RULEBOOK`, `LF-PARITY`, `tests/rpt-golden/goldens/*`, the FY26 hashes of the setup commit, server `Hcc.Accounting.Tests` goldens, `rulebook-reports.json`. The integrator proves it by not regenerating any of them. |
| I2 | Every line and every receipt belongs to **exactly one** folio. Stay totals = Σ folio totals, to the paisa (tax is per line, half away from zero, DEC-023/096 — never recomputed on a folio sum). |
| I3 | **GST stays with its line.** A line's net and tax are on the same folio and the same invoice; the groups module's "tax routed separately" is not offered in SF. Posted lines keep their `taxBps` snapshot (DEC-069). |
| I4 | A reversal, a rate re-post, a credit-note line and a correction always sit on the folio of the line they reverse / replace / credit (never routed by category). |
| I5 | **Accounting unchanged.** Revenue and GST vouchers are per line as today; receipts post as today (1100 Guest ledger per booking); a company-credit receipt goes to `1110/<company of that folio's payer>`; a guest-credit receipt to `1120/<Udhaar party of that folio's payer>`. No new ledger, no new mapping key. |
| I6 | A line's `folio` is set **once at posting** and never edited (prototype) / `bill.charge.folio_id` is never updated (server, append-only trigger). |
| I7 | Invoice numbers are allocated **only at checkout**, in the one hotel INV series (DEC-197 `HPE/2627/0001`; prototype `fdmAllocate(s,'INV')`), consecutively in folio letter order. Credit notes in the one CRN series. |
| I8 | D7: a company's exposure counts **only the company folio** of a split stay (§7). |

## 2. Shapes

### 2.1 Prototype (declared in the setup commit — lanes import, never redeclare)
`03 Prototype/src/domain/streams/folio-split.ts` (exported names `sf*` / `SF*`, re-exported from `services/mock/index.ts` block `F8-SETUP`):
- `SFPayerKind = 'guest'|'company'|'group'|'udhaar'`; `SFRouteCategory = 'room'|'extras'`; `SF_FOLIO_IDS = ['A','B','C','D']`; `SF_PRIMARY = 'A'`.
- `SFPayer = {kind; name; ref?; gstin?}` — company: `ref` = company account id (`GRPCompany.id`), name + GSTIN copied from the account at open; udhaar: `ref` = the guest key of an LF B2 credit guest; guest: optional GSTIN (a guest who is a registered business, SFQ-9).
- `SFFolio = {id; payer; routes; openedAt; openedBy; invoiceNo?; removedAt?}`.
- `types.ts` (`// F8` blocks): `Line.folio?`, `Receipt.folio?`, `Booking.folios?: SFFolio[]`, `Booking.creditNotes[].folio?`. Absent = `'A'`. On a split stay `Booking.invoiceNo` = the first folio invoice issued (back-compat for code that reads one number); each folio keeps its own in `SFFolio.invoiceNo`.
- Helpers (setup commit, pure, single-folio identity): `sfIsSplit(b)`, `sfFolios(b)` (implicit folio A when absent), `sfFolio(b,id)`, `sfRouteCategory(line)` (room / discount / meal-plan line → `room`, else `extras`), `sfRouteTarget(b,cat)`, `sfLineFolio(b,line)` (own `folio`, else the reversed original's, else A), `sfReceiptFolio(b,r)`, **`sfView(b,id)`** (Booking-shaped view: that folio's lines, receipts, credit notes, own invoice number, payer as the invoice party), **`sfInvoiceDocs(b)`** (one row per issued folio invoice). sf-domain may add helpers; it never changes these signatures.

**The view principle.** Every existing per-booking rule runs on `sfView(b,f)` unchanged: `totals()`, `folioRefundable`, the credit-note cap, `companyCreditCheck`, `gstForFolioAdjustment`, the invoice / folio document, the checkout blockers. New code never re-implements a folio sum.

### 2.2 Payer kinds

| Kind | Folio | Who / ref | Settles at checkout by | Invoice buyer |
|---|---|---|---|---|
| guest | A (always) and optionally B–D (another person in the party) | `name`; optional `gstin` | payment / advance / deposit-apply (A only) / refund; guest-credit if the guest has an LF B2 credit account | the payer name; B2B only when `gstin` is set |
| company | at most **one** of B–D | `ref` = company account; refused when the booking's `company.account` names another company (`SF_COMPANY_MISMATCH`) | approved company credit (`COMPANY_CREDIT_REQUEST {folio}`) or a payment (Bank transfer / UPI / Card, DEC-076: no cash) | company name + GSTIN (B2B) |
| udhaar | any of B–D | `ref` = LF B2 credit guest key (DEC-243 key form pending) | guest-credit receipt to that customer's account, within its limit (LF B2 rules) | the customer's name |
| group | — | refused in F8 (`SF_PAYER_KIND`); a group member refuses any split (`SF_GROUP_MEMBER`) | — | — |

Limits: at most 4 live folios (`SF_FOLIO_LIMIT`, SFQ-1); folio A cannot be removed and its payer is always the guest; a folio is removed only while it has no line and no receipt (`SF_FOLIO_NOT_EMPTY`); a removed letter is not reused.

### 2.3 Server DDL (frozen; sf-domain writes it as migrations 4350 / 4351 in its first commit)
**4350 `sf_folio_payer.sql`:** `ALTER TABLE bill.folio DROP CONSTRAINT UQ_folio_reservation;` then add
`folio_seq TINYINT NOT NULL CONSTRAINT DF_folio_seq DEFAULT 1 CONSTRAINT CK_folio_seq CHECK (folio_seq BETWEEN 1 AND 4)`,
`payer_kind VARCHAR(8) NOT NULL CONSTRAINT DF_folio_payer_kind DEFAULT 'guest' CONSTRAINT CK_folio_payer_kind CHECK (payer_kind IN ('guest','company','group','udhaar'))`,
`payer_company_id BIGINT NULL CONSTRAINT FK_folio_payer_company REFERENCES grp.company_account`, `payer_customer_key NVARCHAR(80) NULL`, `payer_name NVARCHAR(200) NULL`, `payer_gstin CHAR(15) NULL`, `opened_by INT NULL`, `opened_utc DATETIME2(3) NULL`, `removed_utc DATETIME2(3) NULL`;
`CREATE UNIQUE INDEX UX_folio_reservation_seq ON bill.folio(reservation_id, folio_seq);`
checks: `CK_folio_primary_guest CHECK (folio_seq > 1 OR payer_kind = 'guest')`, `CK_folio_company CHECK (payer_kind <> 'company' OR payer_company_id IS NOT NULL)`, `CK_folio_udhaar CHECK (payer_kind <> 'udhaar' OR payer_customer_key IS NOT NULL)`, `CK_folio_payer_gstin CHECK (payer_gstin IS NULL OR LEN(payer_gstin) = 15)`; filtered unique index: one company folio per reservation (`WHERE payer_kind = 'company' AND removed_utc IS NULL`). Existing rows become `folio_seq = 1, payer_kind = 'guest'` by the defaults — **every existing stay is a single-folio stay**.
**4351 `sf_folio_route.sql`:** `bill.folio_route(route_id BIGINT IDENTITY PK, reservation_id BIGINT NOT NULL FK inv.reservation, category VARCHAR(8) NOT NULL CHECK (category IN ('room','extras')), folio_id BIGINT NOT NULL FK bill.folio, seq INT NOT NULL CHECK (seq >= 1), reason NVARCHAR(200) NULL, changed_by INT NOT NULL, changed_utc DATETIME2(3) NOT NULL, CONSTRAINT UQ_folio_route UNIQUE (reservation_id, category, seq))` + append-only trigger (`THROW 52350, 'bill.folio_route is append-only; set the routing again.', 1`). Effective route = highest `seq` per (reservation, category); none = `folio_seq = 1`.
**4352** reserved (sf-domain: RBAC rows only if the owner answers SFQ-4 with a new permission; default none). 4353–4359 spare for sf-domain.

### 2.4 Server domain / contracts (sf-domain step 1, before sf-server starts)
- `Hcc.Domain/Billing/FolioRouting.cs`: `enum FolioRouteCategory { Room, Extras }` (deviation 8); `static FolioRouteCategory CategoryOf(ChargeKind kind, bool mealPlan)`; `sealed record FolioRef(long FolioId, int Seq, string PayerKind, long? CompanyId, string? CustomerKey, string? PayerName, string? PayerGstin, bool Removed)`; `sealed record FolioRoutingSnapshot(IReadOnlyList<FolioRef> Folios, IReadOnlyDictionary<FolioRouteCategory,long> Routes)`; `static long Resolve(FolioRoutingSnapshot snap, ChargeKind kind, bool mealPlan, long? reversesFolioId, long? explicitFolioId)` — explicit wins, then the reversed original's folio, then the route of the category, then seq 1.
- `Hcc.Domain/Billing/SplitFolioRules.cs`: max 4, one company folio, exclusive categories, primary is guest, remove only when empty, group refusal, D7 helpers shared with `CompanyCreditRules` (§7).
- `Hcc.Contracts/Billing/SplitFolioContracts.cs`: `OpenFolioRequest {reservationId, payerKind, companyId?, customerKey?, payerName?, payerGstin?, routes[], reason?}`, `SetFolioRoutingRequest {reservationId, folioSeq, routes[], reason}`, `RemoveFolioRequest {reservationId, folioSeq}`, `FolioSummaryDto {folioId, seq, letter, payerKind, payerName, payerGstin?, companyId?, customerKey?, routes[], netPaise, taxPaise, totalPaise, paidPaise, depositPaise, balancePaise, invoiceNo?, removed}`; existing command DTOs gain optional `folioSeq` (payment, deposit, refund request, discount, charge, company-credit request, credit-note request).
- `Hcc.Application/Billing/SplitFolio*.cs` (commands `OpenFolio`, `SetFolioRouting`, `RemoveFolio`, the checkout "settle and invoice every folio" orchestrator) — sf-domain.

## 3. Routing rule per charge kind

| Posting | Prototype line | Server row | Category | Folio it lands on |
|---|---|---|---|---|
| Room night (night run, POST_NIGHTS, CLOSE_RUN, extend, move) | `kind:'room'` | `ChargeKind.Room` | room | route(room) |
| Meal-plan line posted with the night | `kind:'service'` + `planCode` | Service + `bill.charge_catalogue.meal_plan_id` (`SqlRoomNightBundle` passes the room line's folio explicitly) | room | **the room night's folio** (a bundled plan is one composite supply with the room; SFQ-3) |
| Discount | `kind:'discount'` | Discount | room | the folio named by the command (`folio`, UI default = the folio on screen); absent → route(room) |
| Extra / service / laundry / LCO1 / LCO2 / UPG / attrition / no-show charge | `kind:'service'` | Service | extras | `folio` on CHARGE when given, else route(extras) |
| Food posted to the room (POS room-post) | `kind:'food'` | Service (GST category food) | extras | route(extras) |
| Correction reversal (WF-FOLIO-002) | `kind:'adjustment'` + `reversalOf` | Correction + `reverses_charge_id` | — | the original's folio |
| Rate re-post (LF A1 / AF2 CTL-01: reversal + new night) | reversal + `kind:'room'` | Correction + Room | — | **both on the original night's folio** (never re-routed) |
| Credit-note line | `kind:'adjustment'`, `approvalId` | Correction (credit note) | — | the credited folio |
| Closed-day adjustment (DEC-028 / DEC-077) | `kind:'adjustment'` + `correctionOf` | Correction | — | the folio of the line it corrects, else the command's `folio`, else A |
| Bill correction (LF A2, new invoice revision) | — | — | — | **refused on a split stay** (`SF_SPLIT_BILL_CORRECTION`, SFQ-11): use a credit note per folio |

| Receipt kind | Allowed folio |
|---|---|
| payment (incl. advance before arrival) | any folio (`folio`, default A); company folio: no cash (DEC-076) |
| deposit, deposit-return, deposit-apply | **A only** (`SF_DEPOSIT_FOLIO`, SFQ-12) |
| refund | the folio of the receipt it refunds |
| company-credit | the company folio only (single-folio stay: A, as today) |
| guest-credit (LF B2) | A, or an udhaar folio |
| ota-credit (ACC-2 A13) | A only |

**Prototype mechanism (sf-domain).** One hook inside `services/mock/commands.ts` (`// F8-SF-DOMAIN` block, after the handler, before the result is committed): for every booking with `folios`, each new line without `folio` gets `sfLineFolio`-or-route as above, each new receipt without `folio` gets `'A'` (or is refused when the table above refuses it). Handlers that know the folio (PAYMENT, DISCOUNT, CHARGE, COMPANY_CREDIT_REQUEST, CREDIT_NOTE approval, CHECKOUT) set it explicitly from `cmd.folio` / `approval.meta.folio`. `SF_FOLIO_OPEN` on a stay that had no `folios` writes `folios=[A,newFolio]` and stamps `folio:'A'` on the stay's existing lines and receipts in the same command (the only time a posted line gains a field; amount, tax and date never change), so later lines are the only unstamped ones. Because `execute` is copy-on-write and atomic, a refusal in the hook leaves the state untouched.
**Server mechanism (sf-server).** `SqlBillingStore.InsertChargeAsync` is the single insert into `bill.charge`. Callers keep passing the primary folio (`folio_seq = 1`) unless they pass an explicit folio (`NewCharge` gains `bool ExplicitFolio = false`, an optional member with a default). For a reservation with more than one live folio the store resolves the target with `FolioRouting.Resolve` inside the same transaction. Single-folio reservations are untouched (no extra query when only `folio_seq = 1` exists — read the folio count with the folio lock already taken).

## 4. Commands

| Prototype | Server (`POST /api/v1/commands/<Name>`) | Permission | Rules / errors |
|---|---|---|---|
| `SF_FOLIO_OPEN {id, payer:{kind,ref?,name?,gstin?}, routes, reason?}` | `OpenFolio` | billing (PERM-FOL-002; SFQ-4) | stay Confirmed or In house; not a group member; ≤ 4 folios; one company folio; routes exclusive (`SF_ROUTE_TAKEN`); company needs an active account, D7 estimate check (§7); udhaar needs an LF B2 credit guest; audited |
| `SF_ROUTE_SET {id, folio, routes, reason}` | `SetFolioRouting` | billing | open folios only; applies to later postings only; exclusive; D7 estimate check when room moves to a company folio |
| `SF_FOLIO_REMOVE {id, folio}` | `RemoveFolio` | billing | not A; no lines and no receipts |
| `PAYMENT {…, folio?}` | existing + `folioSeq?` | as today | §3 receipt table; duplicate guard (AF-PAY08) per folio |
| `CHARGE {…, folio?}`, `DISCOUNT {…, folio?}` | existing + `folioSeq?` | as today | discount cap = that folio's net; approval meta carries the folio |
| `COMPANY_CREDIT_REQUEST {id, folio?}` | existing + `folioSeq?` | as today | amount = that folio's balance; Manager ≤ ₹10,000 per company folio (SFQ-13), Owner above; D7 at request, approval and checkout |
| `CREDIT_NOTE {id, folio?, amount, reason}` | existing request + `folioSeq?` | as today (Owner approves, DEC-063 G3) | departed stay; cap = that folio's net; target = that folio's invoice |
| `CHECKOUT` | existing | as today | settles **every** folio (one sheet in the UI): late-checkout lines first (LF-DESK, routed as extras), then each folio: approved company credit / OTA credit / guest-credit / payment; every folio balance 0; deposit on A returned; no pending approval on any folio; then invoice numbers (§5) |

Errors: existing codes only (`VALIDATION`, `INVALID_TRANSITION`, `FORBIDDEN`, `NOT_FOUND`, `STALE_VERSION`) with `details.reason` = `SF_FOLIO_LIMIT`, `SF_ROUTE_TAKEN`, `SF_COMPANY_FOLIO`, `SF_COMPANY_MISMATCH`, `SF_GROUP_MEMBER`, `SF_PAYER_KIND`, `SF_FOLIO_NOT_EMPTY`, `SF_FOLIO_INVOICED`, `SF_SPLIT_BILL_CORRECTION`, `SF_DEPOSIT_FOLIO`, `SF_FOLIO_UNKNOWN` (prototype pattern of `FOLIO_DUP_REASON`; server pattern of MEAS `details.reason`). D7 refusals keep today's `INVALID_TRANSITION` and the `grpCreditRefusal` / `CompanyCreditRules` text with `what = 'split folio'`.

## 5. Settlement and invoicing per folio (same series)
1. Per-folio figures are `totals(sfView(b,f))`; server `FolioStatement.Compute(lines of folio, receipts of folio)`.
2. At checkout, after every folio is settled, numbers are allocated in letter order A, B, C, D from the one INV series. **A folio whose total (net + tax) is 0 gets no tax invoice** (SFQ-8); its lines stay on the stay statement.
3. `Booking.invoiceNo` = the first number allocated; `SFFolio.invoiceNo` = each folio's own.
4. **Prototype tax reports enumerate invoices only through `sfInvoiceDocs`** (`domain/gstr1.ts`, `domain/reports/tax/shared.ts` → invoice register, GST by bill, documents issued, SAC summary, bill corrections). sf-domain makes that switch; invariance = the tax goldens do not move.
5. Server: `FolioInvoiceSource` already issues per `folio_id` (`UX_ivc_invoice_source` = one invoice per folio). The buyer comes from the folio payer (`IInvoiceStore.FolioPartyAsync` reads `payer_*`; company → B2B with its GSTIN). The checkout orchestrator issues all folio invoices in one transaction. `ivc.invoice` rows are the documents, so the server tax reports need no change beyond the folio-join audit (§8 R10).
6. Documents: the invoice / folio print is drawn from `sfView(b,f)` (prototype `domain/reports/documents/folio.ts`, server `Hcc.Reporting/Documents/DocumentSql.cs` per folio).
7. **Golden stay (sf-tests, both suites):** company account *Deccan Agro Ltd (fictional)*, GSTIN `27AAPFU0939F1ZV` (fictional, valid check digit; the `27AAACD1111A1Z1` first written here fails the check — deviation 8), credit limit ₹50,000, no other exposure. Guest checks in to a Deluxe room at ₹3,000 a night for 3 nights; at check-in `SF_FOLIO_OPEN {payer company Deccan, routes ['room']}`. Postings: 3 nights ₹3,000 at 5 % (₹150 each), laundry ₹500 at 18 % (₹90), room-service food ₹1,200 at 5 % (₹60). Guest pays an advance ₹1,000 (UPI) on A at check-in and ₹850 (Cash) at checkout; folio B goes on company credit ₹9,450 (a Manager may approve: below ₹10,000).
   - Folio A (guest, B2C): net ₹1,700, CGST ₹75, SGST ₹75, total ₹1,850 → invoice n.
   - Folio B (Deccan Agro, B2B): net ₹9,000, CGST ₹225, SGST ₹225, total ₹9,450 → invoice n+1.
   - **Two invoices, two GST documents**, consecutive numbers; Σ = the unsplit stay's ₹11,300.00 to the paisa (control: the same stay without the split gives one invoice of ₹11,300 with the same six lines).
   - Accounting: revenue 4000 ₹9,000, 4100 ₹500, 4200 ₹1,200; output CGST ₹300, SGST ₹300; `1110/<Deccan>` Dr ₹9,450; receipts ₹1,850; 1100 nets to 0. Trial balance identical to the unsplit control except the party split of the receivable.
   - Variants: (a) credit note ₹3,000 on folio B (one night) → CRN number, GST at `gstForFolioAdjustment(sfView(b,'B'))` (expected 5 %, ₹150: if the function gives another rate, STOP and report), folio A untouched; (b) D7 — limit lowered to ₹9,000 before checkout → company credit refused with the D7 text, guest folio unaffected; (c) an extra ₹600 posted with `folio:'B'` when B would exceed the limit → refused, posting with `folio:'A'` passes; (d) DEC-061 walk: Housekeeping / Maintenance see no folio money on any new screen.

## 6. Credit note per folio
`CREDIT_NOTE {id, folio, amount, reason}` → approval `kind:'credit-note'`, `meta.folio`, target = that folio's invoice number; Owner approves (DEC-063 G3; DEC-088 POS exception unchanged); number from the CRN series (`fdmAllocate(s,'CRN')`); lines posted on that folio with `approvalId`; `Booking.creditNotes[]` row carries `folio`; GST by `gstForFolioAdjustment(s, sfView(b,f), date)` with the A03a-04 exact-pieces rule applied to the folio view. A refund after a credit note is checked against **that folio's** receipts and balance (`DEPARTED_REFUND_MSG` on the view). A company folio that went on company credit behaves exactly as a single-folio company-credit stay does today (no new rule; SFQ-18 asks whether the owner wants the receivable reduced instead). Server: `ivc.credit_note_request.invoice_id` is already per invoice, hence per folio.

## 7. D7 — company credit limits enforced per folio (money rule, owner rulings DEC-076 / A20)
1. **Unchanged:** only the Owner sets or raises a limit (a Manager requests); a refusal has no override; no cash on company credit.
2. **Exposure** of company C (prototype `grpExposure`, server `CompanyCreditRules` + `SqlCompanyCreditStore`): receivable (opening + company credit − payments, TDS included) **plus**, for each live stay billed to C: a single-folio stay billed to C (booking `company.account` or bill-to-company block) as today (`grpBookingExposure`), a **split stay with a company folio for C: only that folio** (posted lines of the folio + agreed unposted room nights when `room` is routed to it + their GST − receipts on that folio). A split stay's guest folio never counts for C. `grpCompanyBookings` / the server "billed to company" query include stays whose live company folio names C (`payer_company_id`).
3. **Checks** (refusal text = `grpCreditRefusal(c, used, needed, 'split folio')`, `used` = exposure without this stay's company folio):
   - `SF_FOLIO_OPEN` / `SF_ROUTE_SET` with a company payer and `room` routed: `needed` = the folio's estimate (agreed nights × rate + GST + what is already on it).
   - Any command other than a night posting that adds a line to the company folio: refused when the folio would go over (variant 5.7c).
   - **Night posting (POST_NIGHTS, NIGHT_POST, CLOSE_RUN, server night run) is never refused** — it would stop the day close; the night posts to its routed folio, `rpt.money.high-balance` shows the row red and company credit at checkout is refused until the Owner raises the limit or the guest pays (SFQ-6).
   - `COMPANY_CREDIT_REQUEST`, its approval and CHECKOUT re-check as today, on the company folio's balance.
4. **Manager limit:** company credit above ₹10,000 per **company folio** needs the Owner (`FOLIO_MANAGER_CREDIT_LIMIT`; with one company folio per stay this equals "per stay", SFQ-13). The Administration-panel override of that limit (`adminLimitFor(s,'Manager','PERM-PAY-005')`, already read by the counter-bill path) keeps working and is read the same way by the folio path. Server: `Hcc.Domain/Groups/CompanyCredit.cs` `CompanyCreditRules` (sf-domain) gains the folio-aware exposure; `SqlCompanyCreditStore` (sf-domain block) supplies it.
5. Owner-default A20 applies unchanged: company limits Owner-only; cash-out second person above ₹20,000 (DEC-073 territory, not touched); attendance correction Manager enters / Owner approves (DEC-075, not touched).

## 8. MEAS interplay — no double counting

| # | Rule |
|---|---|
| R1 | **Stay-level** figures (occupancy, ADR, RevPAR, room revenue, flash, pickup, study loaders, high balance) sum every line of the stay once — all folios together — and never add a folio view to its stay. |
| R2 | **Document-level** figures (invoice register, GST by bill, documents issued, SAC summary, GSTR-1, credit bills, collection detail) count per invoice / per receipt; each line is on exactly one folio, hence on exactly one invoice. |
| R3 | MEAS N1 (`SqlAnalyticsInputSource.LiveChargeSql`: a `correction` with `reverses_charge_id` is summed under the kind of the line it reverses): unaffected — the reversal sits on the original's folio (I4) and the join is on the charge, not the folio. |
| R4 | MEAS N3 (rate re-post = reversal + new night, kind `rate-change` in `rpt.money.allowances-voids`, approver from `stay_rate_change.reposted_json` / `stayRateX.changes[].reposted`): unaffected — both rows stay on the original night's folio; identification by label and change record is unchanged. |
| R5 | MEAS M5 (ADR over charged nights = live room lines with amount > 0): unaffected — SF never splits a line's amount between folios, so a night is one line on one folio. A future "company pays up to ₹X a night" cap (which would split a night) is **out of F8** (SFQ-2). |
| R6 | `rpt.money.high-balance` (both twins, f8-int): one row per stay as today; for a split stay the "over" test is the company folio's balance against the company limit (deposit not counted, it is on A) and the guest folio's balance against `meas.target.highBalance`; the row is red if either is over. No column change. |
| R7 | SF commands write no money rows: `SF_FOLIO_OPEN` / `SF_ROUTE_SET` / `SF_FOLIO_REMOVE` are audited configuration of the stay, so nothing new appears in `rpt.money.allowances-voids`, `rpt.admin.cancelled-lines` or the bill-corrections report. |
| R8 | `rpt.guests.company-business` (P2-A) and `rpt.money.company-credit` (P2-B) attribute to a company the lines of its company folio on split stays, the whole stay otherwise (helper `sfCompanyLines(s,b,companyId)` added by sf-domain in step 1; until it merges the report lanes call `sfView`/`sfFolios` directly). |
| R9 | Study / analytics bills: the study loaders that read invoices sum `ivc.invoice` (one row per folio) **or** charges per stay — never both for one figure. f8-int checks `SqlAnalyticsInputSource`, `SqlStudyPackFacts`, the prototype `domain/analytics` input builder on the golden split stay: billed total = ₹11,300 once. |
| R10 | **Server folio-join audit** (sf-server; ~70 `bill.folio` references in 31 files at the setup commit, list in its DONE): (a) aggregates of charges / receipts per reservation through `bill.folio` — safe, leave; (b) single-folio lookups (`SELECT folio_id FROM bill.folio WHERE reservation_id = @r`, `FolioIdForReservationAsync`, the "insert folio if missing" in `SqlBillingStore`) — add `AND folio_seq = 1` (primary) or call the routing; (c) `LEFT JOIN bill.folio f ON f.reservation_id = r.reservation_id` that returns one row per reservation — add `AND f.folio_seq = 1` or aggregate, else split stays duplicate rows. Reports in `Hcc.Reporting` listed by the audit go to f8-int if they are outside sf-server's ownership. |

## 9. Migrations (all `--add --accept-out-of-order`, each also a `Hcc.slnx` line inside the lane's `<!-- F8-<LANE> -->` block)

| Range | Lane | Content |
|---|---|---|
| 4350–4352 | sf-domain | 4350 `sf_folio_payer`, 4351 `sf_folio_route`, 4352 reserved (RBAC only if SFQ-4 answered "new permission") |
| 4353–4359 | sf-domain | spare |
| 4360–4364 | sf-server | indexes / query support (e.g. `IX_charge_folio_kind`), nothing that changes money |
| 4365–4369 | f8-int | merge fallout |
| 4450–4451 / 4452–4453 / 4454–4455 | rpt-p2-a / -b / -c | only if a report needs an index or RBAC row |
| 4456–4469 | spare (ask the integrator) | |
| 4470–4473 | qw-nightaudit | production night-audit template seed (if templates are rows) |
| 4474–4477 | qw-noshow | settings seed only if a new setting is added (default none: `fd.noShowTime` exists) |
| 4478–4480 | qw-kds | KDS thresholds settings seed (QWQ-3) |
| 4481–4484 | qw-pm | CCTV / DG shift template seed (optional) |
| 4485–4489 | f8-int | merge fallout |

## 10. Append-only blocks (`// F8-<LANE>` … `// F8-<LANE> end`; created empty by the setup commit)

| File | Blocks |
|---|---|
| `03 Prototype/src/domain/types.ts` | `F8` (setup only; integrator only afterwards) |
| `03 Prototype/src/services/mock/handlers/index.ts` | imports + `const F8` (spread into `registry0`): SF-DOMAIN, QW-NA, QW-NS, QW-KDS, QW-ADV, QW-PM |
| `03 Prototype/src/services/mock/index.ts` | `F8-SETUP` (folio-split exports) + SF-DOMAIN, QW-NA, QW-NS, QW-KDS, QW-ADV, QW-PM |
| `03 Prototype/src/clients/sync/registry.ts` | imports + `AREAS`: SF-SERVER, QW-NA, QW-NS, QW-KDS, QW-ADV, QW-PM |
| `03 Prototype/src/domain/reports/register.ts` | RPT-P2-A, RPT-P2-B, RPT-P2-C (one import line per report file) |
| `03 Prototype/src/app/navmap.ts` | QW-KDS in `PAGE_CATEGORY`, `PAGE_BLURB`, `PAGE_KEYWORDS` (the only new nav page of the wave) |
| `03 Prototype/src/app/subscreens.ts` | SF-UI, QW-ADV, QW-PM, QW-KDS (gates go into `SUB_SCREEN_GATES` on one line each with the lane tag) |
| `03 Prototype/src/app/registry.ts` | imports + `registrations`: SF-UI, QW-KDS, QW-ADV, QW-PM |
| `08 Production/src/Hcc.Api/HccComposition.cs` | services block and endpoints block: SF-DOMAIN, SF-SERVER, RPT-P2-A/B/C, QW-NA, QW-NS, QW-KDS, QW-ADV, QW-PM |
| `08 Production/Hcc.slnx` | the same ten lanes + INT (migration `File` lines; no new project) |
| `02 Planning/HCC_Report_Catalogue_v1.md` | status lines at the end only, `<!-- F8-<LANE> -->` (integrator sorts) |

**Owned files that are not append-only (one lane each, edits inside an `// F8-<LANE>` block in the file):** `services/mock/handlers/folio.ts`, `services/mock/commands.ts` (routing hook), `domain/streams/groups.ts` (exposure), `domain/streams/folio.ts`, `domain/gstr1.ts`, `domain/reports/tax/shared.ts`, `services/mock/handlers/counter.ts` (`gcrCheckout` per folio), `services/mock/handlers/bill-correction.ts` (split refusal), `services/mock/handlers/stay-rate.ts` (re-post folio), `domain/streams/catalogue.ts` (`catPlanNight` folio) → **sf-domain**. `features/folio/*` (except new `Split*` files) and `mobile/money/Folio.tsx` / `Checkout.tsx` → **sf-ui**. `domain/reports/documents/folio.ts` → **sf-ui** (view only). `domain/reports/meas-daily/high-balance.ts` (R6) → **f8-int**. `features/closing/DayClosing.tsx` + `domain/streams/closing.ts` preflight → **qw-nightaudit**. `services/mock/handlers/reservations.ts` (`CANCEL` no-show charge text) → **qw-noshow**. `features/closing/Cashier.tsx` (one step) → **qw-advances**. `features/maintenance/Maintenance.tsx` (one tab) → **qw-pm**. `features/pos/PosPage.tsx` (link to the KDS page only) → **qw-kds**. Server: `SqlBillingStore`, `SqlInvoiceStore.FolioPartyAsync`, `DocumentSql`, the audited `bill.folio` queries, sync mappers `clients/sync/money*.ts` → **sf-server**; `CompanyCreditRules`, `SqlCompanyCreditStore` exposure SQL, `BillingService` checkout path → **sf-domain**.
Generated files: `src/domain/reports/catalogue.json` and `08 Production/tests/Hcc.Reporting.Tests/catalogue.json` (`node tools/rpt-catalogue.mjs`), `08 Production/database/feature-catalogue.json` (`node tools/feature-catalogue.mjs --write`), `src/styles/themes.css` (`node tools/themes.mjs --write`): each lane regenerates them in its branch; on a merge conflict the integrator takes either side and regenerates once.

## 11. Tests each lane owns (names `[F8-<LANE>]`)
sf-domain `tests/stream-split-folio.mjs` + `08 Production/tests/Hcc.Billing.Tests/SplitFolioTests.cs` (rules, routing, D7, credit note per folio, invariance of a single-folio stay). sf-server store / endpoint tests in `Hcc.Billing.Tests/Sql/SplitFolioSqlTests.cs` + `Hcc.Invoices.Tests` (buyer per folio) + the folio-join audit tests (a split stay must not duplicate any audited list). sf-ui `tests/stream-split-ui.mjs` + fit-check page `folios`. sf-tests: scenario `SF-GOLDEN` (both suites; `tests/stream-sf-golden.mjs`, `Hcc.Integration.Tests` or `Hcc.Billing.Tests/Golden/SfGoldenTests.cs`), GST per invoice, DEC-061 walk. Report lanes: `tests/stream-rpt-p2-<a|b|c>.mjs` (three-way equality screen = Excel = PDF on the fixture, role matrix, date-class edges, provisional, CSV neutralisation, a golden per report through `tests/rpt-golden/accept.mjs`) + `Hcc.Reporting.Tests/P2<A|B|C>/*`. Quick wins: `tests/stream-night-audit.mjs`, `stream-noshow.mjs`, `stream-kds.mjs`, `stream-advances-view.mjs`, `stream-pm-calendar.mjs` (+ the server test files named in the briefs). The setup commit changes no test except the SCHEMA 16 re-pin.

## 12. P2 report batches (Sonnet ×3, ≤ 8 each, no overlap; catalogue entries are the spec)
| Batch | Lane | Reports |
|---|---|---|
| A guests + rooms | rpt-p2-a | `rpt.guests.reservations-register`, `rpt.guests.guest-tariff`, `rpt.guests.company-business`, `rpt.guests.userwise-activity`, `rpt.guests.service-lines`, `rpt.rooms.room-wise-revenue`, `rpt.rooms.rooms-left`, `rpt.rooms.allocation-grid` |
| B money + tax | rpt-p2-b | `rpt.money.cash-count-sheet`, `rpt.money.payment-corrections`, `rpt.money.counter-bills`, `rpt.money.refunds`, `rpt.money.discount-log`, `rpt.money.bank-book`, `rpt.money.company-credit` (partial → built), `rpt.tax.licence-expiry` |
| C accounting + people + stock + restaurant + admin | rpt-p2-c | `rpt.acc.asset-register` (partial → built), `rpt.people.leave-balances`, `rpt.stock.grn-register`, `rpt.rest.sales-by-item`, `rpt.rest.voids`, `rpt.admin.scheduled-runs`, `rpt.admin.cancelled-lines`, `rpt.admin.rate-sheet` (partial → built: the rate-change audit) |

Not in F8 (P2 that remain: 12 entries + 14 built screens not yet on the engine, RPQ-1 / RPQ-2): `rpt.guests.history-summary`, `rpt.guests.source-mix`, `rpt.guests.customer-history` (partials with screens), `rpt.rooms.adr-trend` (covered day-by-day by `rpt.rooms.occupancy` and by month in `rpt.study.revenue-kpi`), `rpt.rooms.hk-tasks`, `rpt.rooms.hk-task-sla`, `rpt.rooms.hk-turnaround` (extended by MEAS `rpt.rooms.hk-productivity`), `rpt.rooms.work-orders`, `rpt.rooms.guest-services` (partials), `rpt.money.source-rows` (partial, drill-down), `rpt.people.leave-report`, `rpt.people.attendance` (partials), plus the built-but-not-on-engine P2 screens (groups pickup, group master folio, maintenance report, handover dashboard, settles-to, vendor ageing, OTA commission / payouts, compliance dashboard, chart / exceptions / manual register, advances-FNF, system health). Also not in F8: P3 `rpt.rest.sales-by-outlet-hour`.
Report rules: money flag and column flags per catalogue §1.2 (DEC-061: removed, not blanked); rule V for print / export; a report whose data source does not exist in code is **not** faked — the lane writes it as blocked in its DONE with the missing capture; overlapping MEAS entries are not rebuilt (allowances-voids is the summary, the P2 registers are the detail rows it already lists as sources).

## 13. Quick-win scopes (what exists → what F8 adds)
| Lane | Exists on `main` | F8 adds |
|---|---|---|
| qw-nightaudit | `closingPreflight` blockers (pending arrivals, …), template `HOVT-NA-PRE` (na1–na6), `measNightAuditLines(s)` (MEAS lines, never blocking unless `meas.daily.blockClose`) | pure `naChecklist(s)` = operational lines (arrivals resolved, departures out, POS checks closed, cash count submitted, key cards cancelled for departed rooms, backup OK) + the MEAS lines as an "Exceptions" group; shown as the first panel of Day closing › Preflight; auto-tick of `HOVT-NA-PRE` items from the same lines; **only today's preflight blockers block** (QWQ-1); server `GET /api/v1/closing/night-audit` twin |
| qw-noshow | BOOK refuses yield restrictions with an approver override (A2.1); `yld.overbook.<type>` + `yldOversold` (report only); `fd.noShowTime` (23:00); policies `noShowChargeBps` + `fdmNoShowCharge`; CANCEL `noShow` | "Due no-shows" list after `fd.noShowTime` (desk + Day closing preflight link) with one-click CANCEL `noShow` showing the policy charge text — **posting nothing** (DEC-MA-5b); overbooking cap honoured where a type-level sale happens (channel inventory push: sellable + `yldOverbookAllowed`, default 0 = no change, QWQ-2); `yld.restrictionsEnforce` reconciled with today's refusal (QWQ-4: keep the refusal) |
| qw-kds | kitchen board `posKitchenAt(s, station)`, `POS_KOT_STATUS` Preparing → Ready → Served, KOT stations (DEC-MA-4c) | page `kds` (tablet, FitPage): station picker, KOT cards with elapsed time from `s.clock`, amber / red thresholds (QWQ-3: 10 / 20 min), bump = `POS_KOT_STATUS`; Restaurant, Manager, Owner; no money on the page |
| qw-advances | deposits / advances on bookings, `REFUND_REQUEST` → approval → `REFUND_EXECUTE` (DEC-031), deposit-return; F4 bridge block | Cashier step "Advances held": one row per booking with an advance or deposit, age, status, red rows (QWQ-5), actions that call the existing commands only; per folio on split stays (deposits on A) |
| qw-pm | PM plans `mntPmDue` / `mntPlanState`, AMC `mntAmcStatus`, assets (module 15) and the ACC-2 fixed-asset register, templates `HOVT-MNT-RND` | "PM calendar" (month grid of plan occurrences + AMC expiry cards, compliance-card style) as a sub-screen of Maintenance; "Add standard CCTV / DG shift checklist" button that saves a template through the existing template command (no BASE change, QWQ-6) |

## 14. Decisions to record at close (f8-docs; next free DEC-277 — check the register first)
Proposed: (a) split folio shape: up to four folios, payer guest / company / Udhaar, two routing categories, GST stays with its line, routing applies to later postings only; (b) one invoice per non-zero folio at checkout in the one INV series, credit notes per folio; (c) D7 per company folio, night posting never refused; (d) group master payer deferred to the groups module; (e) SCHEMA 16; (f) P2 batches A–C built, the rest listed; (g) the five quick wins with their QWQ defaults.

## 15. Wave mechanics
Worktree `E:\Projects\HCC\worktrees\f8`, branch `claude/f8`; lanes `E:\Projects\HCC\worktrees\f8-<lane>` on `claude/f8-<lane>`, each with the `03 Prototype\node_modules` junction to `worktrees\fixwave-a`. `PORT` = 4270 + lane number (briefs). Ledger `05 Delivery Evidence\F8\F8-LEDGER.md`; DONE files `05 Delivery Evidence\F8\F8-<LANE>-DONE.md`. Heavy jobs only through `tools\Invoke-Serialized.ps1` (exit 75 = busy, retry). Phases: day 1 sf-domain step 1 → sf-server, sf-tests, rpt-p2-a/b/c, qw-* (≤ 11 alive); sf-ui joins after sf-domain step 1; f8-review; f8-int (merge order §16); gate `-Wave f8` on day 2; f8-docs after the fast-forward. **No lane starts without the owner's "go F8".**

## 16. Merge order (f8-int)
sf-domain → sf-server → sf-ui → sf-tests → rpt-p2-a → rpt-p2-b → rpt-p2-c → qw-nightaudit → qw-noshow → qw-kds → qw-advances → qw-pm. After each merge: `npx tsc --noEmit` + the lane's own tests; after all: R6 high-balance, R9 study check, regenerate the generated files once, full `npm test`, `dotnet build`, full serial `dotnet test Hcc.slnx -maxcpucount:1`, `node "08 Production/tools/migrations-lock.mjs"`, the goldens unchanged (I1), `F8-DONE.md`, gate. Merging tips (MEAS): `git merge -Xignore-cr-at-eol`; `migrations.lock.json` conflicts on every merge — union in original order with `format()` from `08 Production/tools/migrations-lock.mjs`.
