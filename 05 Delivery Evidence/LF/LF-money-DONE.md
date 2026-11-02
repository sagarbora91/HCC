# LF-money (lane L1, Opus) — DONE report · 2 Nov 2026

Branch `claude/lf-money` (from setup f71e6fbb). Contract `02 Planning/agents/LF-CONTRACT.md` §2–§7, §11, §12.

## Built

**Prototype (complete for §2–§7, no screens):**
- §2 A1 `STAY_RATE_CHANGE` — `src/domain/streams/stay-rate.ts`, `src/services/mock/handlers/stay-rate.ts`; approval kind `rate-change` (LF-RATE block, `handlers/folio.ts`). Scopes next / today-next / all; closed nights never touched; posted nights of open days re-post (reversal `adjustment` + new `room` line at the new price's GST slab); Owner direct, Manager direct within PERM-RATE-001 (15 %) below tariff, raise of guests with an extra-person charge → approval; Reception always requests; Manager approves within limit, Owner anything; log `s.stayRateX.changes`; `strHeaderLine` for the folio header.
- §3 B1 `GUEST_RATE_SET` / `GUEST_RATE_CLEAR` (Owner), `BOOK` / `CHECKIN` `rate:'agreed'` (LF-RATE blocks) → `rateSource 'agreed'`; `guestMoneyFilter` (domain/streams/guests.ts LF-RATE block) hides `agreedRates` / `creditAccount` from non-money roles.
- §4 A2 `BILL_CORRECTION_REQUEST` — `domain/streams/bill-correction.ts`, `handlers/bill-correction.ts`; approval `bill-correction` (Owner, added to `RBAC_OWNER_DECIDES`) → whole-invoice CRN (reversal of every live line at its own rate) + corrected lines (ids `<BLC>/n<i>`) + new INV (`invoiceRevisions`), record in `s.billCorrectionX`; s.34(2) window; PAYMENT on a departed folio for exactly the correction balance (LF-BILL block); refund flow unchanged. Ledgers carry the delta only (line-based vouchers, all balanced).
- §5 B7 `gstr1.ts`: one INV document per revision (superseded counts as issued), counter bills in INV / their credit notes in CRN, optional 4th arg `legacyBills` → `LGC` row; `gstr1Table12` / `gstr1Table12Csv` (UQC OTH, quantity = documents); `features/customers/legacy-source.ts getLegacyBills`.
- §6 A7 `CTR_BILL_ISSUE`, `CTR_BILL_VOID_REQUEST` — `domain/streams/counter.ts`, `handlers/counter.ts`; lines from extras / POS menu / free text; GST per line; discount by DEC-063 role limits (refused above); settlement paid (tender rules, cash guard) / udhaar (receivable `counterBill`) / company (limit incl. open counter bills); INV number; accounting source `{kind:'counter'}` (`accCounterVouchers`, accounting.ts); void → Owner → CRN, cash back / receivable void.
- §7 B2 `GUEST_CREDIT_ACCOUNT_SET` (Owner), `GUEST_CREDIT_REQUEST` → approval `guest-credit` (Manager ≤ PERM-PAY-005, Owner beyond) → CHECKOUT receipt `guest-credit` + `ExpUdReceivable {kind:'room', guestId, booking}`; Journal Dr 1120/UD-<key> · Cr 1100; `udGuestOutstanding` (bill-wise, ageing 0–30/31–60/61–90/90+); `customerKeyForCredit` in `customers.ts`.

**Server (schema + domain rules; application layer NOT built — see open points):** migrations 4675–4684, 4687–4689, 4692–4694 (locked with `--accept-out-of-order`: KEYS 4720/4721 landed above the reserved LF block; Hcc.slnx lines in the LF-RATE / LF-BILL / LF-COUNTER comments). Domain: `Stays/StayRateRules.cs` (+ `GuestAgreedRateRules`), `Stays/GuestCreditRules.cs`, `Customers/CustomerRules.cs` `CustomerCreditKey`, `Invoices/BillCorrectionRules.cs`, `Invoices/Gstr1Summary.cs` (`Build(…, legacy)` LGC row, `Table12`), `Counter/CounterBillRules.cs` (totals, discount limits, balanced Sales entries).

## Follow-up (coordinator request, 2 Nov)
Server now also has: `LfMoneyGuestCommands` (`SetGuestAgreedRate`, `ClearGuestAgreedRate`, `SetGuestCreditAccount`; Owner only via PERM-GST-010/011) + `GET /guests/{id}/money` (money roles), `SqlLfGuestMoneyStore`, `LfMoneyEndpoints` (HccComposition LF-RATE block); GSTR-1 `LGC` row wired into `InvoiceQueries.Gstr1*` from `lgc.bill` via `IInvoiceStore.LegacyBillsInRangeAsync`; migration **4685** `lgc.bill.cancelled` (legacy cancel flag; LG extractor must set it). Tests: `Hcc.Counter.Tests` 9/9 (incl. `LfGuestMoneySqlTests` on a real HccTest_* hotel), Invoices filtered 9/9, build 0 warnings.

## Tests
- Prototype `npm test`: before (setup) 2388 → after **2419** (2418 pass; the only failure is the known `[FIT] no phone-layout file changed`). New: `stream-lf-rate.mjs` 13, `stream-lf-bill.mjs` 6, `stream-lf-counter.mjs` 12. `tsc` clean.
- Server: `dotnet build Hcc.slnx` 0 warnings, 0 errors. `Hcc.Counter.Tests` (new) 7/7 incl. a SQL test that migrates a fresh HccTest_* DB through every migration and checks the LF-money objects; `Hcc.Stays.Tests` StayRateTests + GuestCreditTests 7/7; `Hcc.Invoices.Tests` BillCorrectionTests + Gstr1LegacyTests 5/5; `Hcc.Platform.Tests` migration tests 13/13. Full suites of those projects not run (CPU sharing).

## Edits outside the marked blocks (integrator please keep)
- `src/domain/selectors.ts`: `totals` counts `guest-credit` as credit; `drawer` adds counter-bill cash (`lfCounterCash`, inlined — importing counter.ts there breaks module order).
- `src/domain/streams/accounting.ts`: `ACCSourceKind` + `'counter'`; receipt loop `guest-credit` journal; Udhaar-credit loop skips receivables with `booking` / `counterBill` (no second sale); counter section + `accCounterVouchers`; `accDates`, key uses, `accSourceOf`.
- `src/domain/streams/rbac.ts` one line: `RBAC_OWNER_DECIDES` + `'bill-correction'`, `'counter-credit-note'`.
- `tests/stream-rbac-matrix.mjs` CMDS rows for the 8 new commands.

## Integration needed (other lanes / integrator)
- `domain/streams/folio.ts` (not mine): add `rate-change`, `bill-correction`, `guest-credit` to `FOLIO_APPROVAL_KINDS` + `folioKindLabel` so folio screens list them.
- `guestView` should pass its result through `guestMoneyFilter`.
- `features/compliance/Gstr1.tsx` (L4): pass `getLegacyBills` as the 4th arg; SAC tab from `gstr1Table12` / CSV; note `GSTR1_TABLE12_NOTE`.
- Stub modal ids for L4: `stay-rate-change`, `guest-agreed-rate`, `bill-correction`, `counter-bill`, `counter-bill-void`, `guest-credit-account`, `guest-credit-request`; folio header line from `strHeaderLine`; print notes from `blcInvoiceNote`.
- `accounting-golden.json` / FY26 re-pin by the integrator if the goldens move (my suites did not touch them; full run green).

## Deviations from the contract
- `GUEST_RATE_SET/CLEAR` take `roomType` (not `type`: `cmd.type` is the command name in the prototype).
- Counter-bill void approval kind is `counter-credit-note` (server action `counter-credit`), not `credit-note`: the existing folio `credit-note` branch is booking-bound and runs first.
- Counter bill tests for the Accounting posting live in `Hcc.Counter.Tests` (balanced entries), not `Hcc.Accounting.Tests/Domain/CounterPostingTests.cs` / `CorrectionPostingTests.cs`.
- Server credit key for a phone-less guest = `G-<guest_id>` (prototype: the guest id string).

## Open points
1. **Server application layer still not built** for the posting features: commands `ChangeStayRate` (note `UX_charge_room_night` blocks a second live room charge per night — re-post needs kind `correction` or an index change), `RequestBillCorrection` + approval executor + invoice store revision/supersession + print model, `IssueCounterBill` / `RequestCounterBillVoid` + `SourceRules.Counter` / `CounterCredit`, `GuestCreditRequest` + check-out receipt + `UdhaarCreditRaised`, their stores / endpoints, `CorrectionPostingTests` / `CounterPostingTests` against SourceRules, `BookRequest`/`CheckInRequest` `rate:"agreed"`, sync areas (`clients/sync/registry.ts` LF blocks empty). Needs a follow-up lane (schema + rules are ready).
2. `lgc.bill.cancelled` (4685) exists; the LG extractor must still map the old software's cancel marker into it.
3. Phone-less guest: the Udhaar party id (name slug) differs from `customerKey` (guest id) — one rule for both sides needs the integrator / owner.
4. s.34(2) window and SAC / UQC are illustrative — CA to confirm (OQ-2).
