# F8C-SYNC DONE (lane f8c-sync), 8 Nov 2026

Worktree `E:\Projects\HCC\worktrees\f8c-sync`, branch `claude/f8c-sync`, cut from main 8f99a17c. Code commit **c1bdb638** (this DONE file is the commit after it). Not pushed, `main` not touched.

Open item closed: "Production sync: in the production build the split-folio commands are refused and folios B-D are not loaded".

## What changed

**No server change.** The existing endpoints supply everything the client needs: `GET /folios/by-reservation/{id}/summary`, `GET /folios/{folioId}`, `GET /invoices/by-source/folio/{folioId}`, OpenFolio / SetFolioRouting / RemoveFolio, and the F8 fields `PostChargeRequest.ExplicitFolio`, `GuestCreditRequestBody.FolioId`, `CheckoutStayRequest.CreditApprovalIds` and `CheckoutStayResult.InvoiceNos`.

The mock build is unchanged: no file outside `src/clients/sync` changed in `src/`.

### `03 Prototype/src/clients/sync/money-split.ts` (new)

**Reads: `sfLoadSplit` (:93)**
- It reads the summary, then `GET /folios/{id}` for each live folio B–D, and on a departed stay the invoice of each folio. A removed folio is not read.
- If only one folio is live, it returns null, and the booking takes today's single-folio path unchanged (I1).
- Each folio is reconciled against the server to the paisa through `folioIntoBooking`; the stay is the sum of its folios (I2).
- Lines and receipts are stamped with their folio letter.
- `Booking.folios` is filled from the summary: payer, effective routes, invoice number, removed.
- `Booking.invoiceNo` is the invoice of the first folio in letter order.
- Credit notes carry their folio. Folio approvals carry `meta.folio`.

**Letters and ids**
- The folio letter is the server letter (seq 1..4 = A..D). The read refuses data where a letter does not match its seq (PROTOCOL).
- IdDirectory `folio` / `invoice` entries are keyed by the booking id for folio A (as before) and by `<booking>#<letter>` for B–D: `sfKey` :44, `sfFolioRef` :60.
- Each folio keeps its own version (DEC-041).

**Commands**
- `SF_FOLIO_OPEN` → OpenFolio.
  - company → `companyId`;
  - udhaar → `customerKey` and `payerName` from the credit guest;
  - guest → name and GSTIN.
- `SF_ROUTE_SET` → SetFolioRouting with `folioSeq`.
  - On folio A, the client sends SetFolioRouting to every folio that holds the category, without that category. The server refuses A directly (SF_ROUTE_TAKEN, pinned by the server test below).
  - One step per holder (`<key>:route-<letter>`).
- `SF_FOLIO_REMOVE` → RemoveFolio with `folioSeq`.
- Prechecks use the prototype's wording.

**Helpers**
- `sfCheckoutCreditApprovals` (:146), `sfInvoiceWarning` (:158), `sfIssueInvoices` (:168).
- The area `moneySplit` (:213) is registered in the F8-SF-SERVER block of `registry.ts:145`.

### `03 Prototype/src/clients/sync/money.ts` (blocks marked F8C-SYNC)

| Where | Change |
|---|---|
| :87–95 | `patchBookings` calls the split read. A single-folio booking never carries `folios`. |
| :131 | `patchApprovals`: credit-note requests are mapped on every folio invoice, with `meta.folio`. |
| :185 | `folioOf(c, booking, letter)`. |
| :230 | `requestAdjustment` takes the folio letter. |
| :238 | PAYMENT precheck: a deposit on any folio other than A is refused; cash on the company folio is refused. |
| :311 | PAYMENT uses the named folio's id. |
| :348 | CHARGE: a named folio sends `explicitFolio: true`. Without a folio, the charge goes on folio A's id and the server routes it. |
| :357 | DISCOUNT: default = the folio that takes room. |
| :362 | REFUND_REQUEST: the folio of the receipt being refunded. |
| :368–380 | COMPANY_CREDIT_REQUEST: the company folio, for that folio's balance. |
| :389 | CORRECTION_REQUEST: the folio of the line being corrected. |
| :418–422 | INVOICE_ISSUE on a split stay: one IssueInvoice per folio with charges and no number, in letter order. Buyer = the folio payer (B2B with GSTIN). Steps `invoice-<letter>`. |
| :427–432 | CREDIT_NOTE: the folio's own invoice id. |

### Other files

- **`frontdesk.ts:146–157` CHECKOUT, split stay:**
  - CheckoutStay sends `creditApprovalIds`: per folio, the approved company or guest credit for exactly that folio's balance, with `meta.folio`.
  - No IssueInvoice follows (the server numbers the folio invoices inside the checkout).
  - Empty `invoiceNos` while some folio has charges → the SFQ-19 warning.
  - The single-folio path is unchanged.
- **`lfmoney.ts:126` GUEST_CREDIT_REQUEST:**
  - sends `folioId` for an Udhaar folio;
  - a company or guest folio other than A is refused in the precheck;
  - the default reason is "Guest credit · <payer>". The split checkout sheet sends no reason, and the server requires one.
- **`lfmoney.ts:165` BILL_CORRECTION_REQUEST:** refused on a split stay in the precheck (SFQ-11).
- **`money/wire.ts:53, :82`:** the `guest-credit` receipt kind and approval action are now mapped.
  - **Pre-existing bug:** before this, any folio holding a guest-credit receipt failed the whole bookings slice with PROTOCOL. Single-folio LF B2 stays were affected too.
- **`reads.ts:69–70`:** `GET /folios/by-reservation/:id/summary` and `GET /folios/:id`, both PERM-FOL-001 wide view.
- **Tests:**
  - `tests/_role-reads.json`: the two new routes are added for all 10 roles, with the same status as `by-reservation`.
  - `tests/sync-roles.mjs`: fake handlers for the two new routes.
  - `tests/_sync-harness.mjs`: default summary `[]`.

## Which SF commands work in production now (fake-server sync tests + real-server wire test)

| Command | Status |
|---|---|
| SF_FOLIO_OPEN, SF_ROUTE_SET, SF_FOLIO_REMOVE | work |
| PAYMENT {folio}, CHARGE {folio}, DISCOUNT {folio} | work |
| REFUND_REQUEST, CORRECTION_REQUEST (on any folio) | work |
| COMPANY_CREDIT_REQUEST {folio} | works |
| GUEST_CREDIT_REQUEST {folio} (A or Udhaar) | works |
| CREDIT_NOTE {folio} | works |
| INVOICE_ISSUE on a split stay | works |
| CHECKOUT of a split stay | works |
| BILL_CORRECTION_REQUEST on a split stay | refused, as on the server (SFQ-11) |
| APPROVAL, REFUND_EXECUTE | unchanged: they act on the approval id, and the server knows its folio |

**What does not work, or is limited:**
- **Group payer:** refused on both sides by design (SFQ-10).
- **SF_ROUTE_SET on folio A** when categories sit on two different folios: sent as two server commands, not one transaction. The UI never routes folio A; it edits folios B–D.
- **Opening time and user of a folio** are not on the summary: `openedAt` / `openedBy` are blank in production. `removedAt` is a marker, not a time.
- **Extra GET per booking:** every booking with a folio now costs one more GET (the summary), and a split stay one more per live folio B–D.
  - A server field "live folio count" on `ReservationFolioView` would save that.
  - Not added, because the brief allows server changes only when needed.
- **Approval matching at checkout** uses the screen's folio balances; the server re-checks. A late check-out line posted inside CheckoutStay can change a folio's balance, and the server then refuses the settle. This is the same as single-folio stays today.
- **Udhaar payer `ref`** in production is the customer key, not the prototype guest id. Screens show only the name.

## Tests actually run (all via `tools\Invoke-Serialized.ps1`, label f8c-sync)

- **`npx tsc --noEmit`:** exit 0.
- **Prototype, `HCC_TEST_ONLY='^(sync-|stream-clients|stream-split|stream-f8)'`:** **588/588** passed (10 async). This includes:
  - the new `sync-f8c-split.mjs`: 16/16;
  - `sync-roles` D5: 25;
  - `stream-clients` bundle leak checks (prototype vs production bundle).
- **Server, `dotnet test tests/Hcc.Integration.Tests -maxcpucount:1 --filter "FullyQualifiedName~F8cSyncWire|FullyQualifiedName~RoleReadMatrix"`:** **2/2** passed. The build showed no warnings.
  - `F8cSyncWireTests`: the client's exact bodies and read field names on the real server.
  - `RoleReadMatrix`: the edited fixture matches the real server for all 10 roles.

**Not run:**
- full `npm test`;
- `npm run build` / `npm run build:production` (the production bundle is built by stream-clients only);
- full `dotnet test` and `dotnet build Hcc.slnx`;
- no browser and no screenshots: no `05 Delivery Evidence/F8C/sync/` folder.

## Defaults relied on (owner has not answered)

| Question | Default applied |
|---|---|
| SFQ-4 | billing permission, no new one |
| SFQ-8 | no invoice for a ₹0 folio |
| SFQ-9 | folio A is B2C unless the guest's own GSTIN is entered |
| SFQ-11 | no bill correction on a split stay |
| SFQ-12 | deposits on A only |
| SFQ-16 | Udhaar folio |
| SFQ-19 | split checkout without a hotel GSTIN → Issue invoice afterwards, per folio, in letter order |

## Stopped

Nothing. No money rule was needed beyond the contract and the defaults above.
