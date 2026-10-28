# ACC-2 posting contract (wave `claude/acc2`, 28 Oct 2026)

Shared shapes for every ACC-2 lane. **Copy, never reinterpret.** A lane that needs a change writes it in its DONE file; the coordinator amends this file. Rules: `02 Planning/HCC_Accounting_Rulebook_v1.md` §4, §5 A6 / A9 / A13 / A17b / A18, B5–B12, C1–C11, D5–D7, E1–E5, §G1; rulings DEC-142 (no GST on advances; switch off), DEC-143 (ITC only on bills directly for 18 % supplies), DEC-145 (RCM flag on categories), DEC-146 (HCC deducts TDS, CA deposits), DEC-147 (expense when paid), DEC-150 (OTAs pay net with a statement); ACC-1 carry-overs in `05 Delivery Evidence/Accounting/ACC1-DONE.md` §7. ACC-1's contract (`ACC1-POSTING-CONTRACT.md`) stays in force: ledger codes, party ids `<control>/<partyId>`, the opening entry, the statement / six-column TB shapes.

## 1. Voucher types and source kinds

- `ACCVoucherType` gains `'Purchase'` and `'Contra'` (Tally's names; prefixes `PUR`, `CTR`). Server `VoucherType` enum and the Tally `VOUCHERTYPENAME` likewise. Existing derived sources keep their types (a bank deposit stays `Journal`, as built — no golden churn for that).
- `ACCSourceKind` gains `'manual' | 'purchase' | 'vendor' | 'tax' | 'ota' | 'payroll'` (the prototype gets payroll vouchers in this wave, see §8). Server `SourceKinds` adds `Manual, Purchase, Vendor, Tax, Ota`; `acc.source.source_kind` CHECK list widened (migration 4238).

## 2. Manual vouchers (rulebook §1 principle 2, §6; B5, B6, B7, B11, A18)

```ts
type ACCManualLine = {account: string; debit: number; credit: number; note?: string};   // ledger code or party ledger id; paise; one side only
type ACCManualVoucher = {id: string; date: string; type: 'Journal'|'Contra'|'Payment'|'Receipt'; narration: string; reference?: string;
  lines: ACCManualLine[]; template?: ACCManualTemplate;                                 // template = which guided form produced it (see below)
  status: 'pending'|'approved'|'rejected'|'void'; by: string; at: string;
  decidedBy?: string; decidedAt?: string; reason?: string;                               // reason = rejection / void reason
  voidOf?: string; voidedBy?: string;                                                    // a reversing voucher names the one it voids
  bookedOn?: string};                                                                    // the business date the posting fell on (= date, or the reversal date)
type ACCManualTemplate = 'journal'|'contra'|'capital-introduced'|'owner-drawing-bank'|'loan-received'|'loan-emi'|'bank-charges'|'interest-income'|'bad-debt'|'statutory-remittance'|'gst-payment'|'tds-remittance';
```

- Commands (prototype): `ACC_MANUAL_SAVE {id?, date, type, narration, reference?, lines, template?}` — Owner or Accountant (`PERM-ACC-010`); an **Accountant's** voucher is `pending`; the **Owner's** own is `approved` at once (the Owner is the checker). `ACC_MANUAL_DECIDE {id, approve, reason?}` — Owner (`PERM-ACC-011`). `ACC_MANUAL_VOID {id, reason}` — Owner: on an unlocked day the voucher becomes `void` and posts nothing; on a locked day a **reversing** voucher (same lines swapped, `voidOf`) is created and approved, dated the business date.
- Guards (both sides, same messages): balanced (Σ Dr = Σ Cr > 0); every account an **active** ledger of the effective chart or a party ledger; never 9999, 1400, or a **control** ledger without a party; `Contra` only between cash / bank ledgers (`cash`, `bank` groups); `Payment` / `Receipt` must have exactly one cash-or-bank side; date ≤ business date; date not in a locked day (refused: "unlock the day first" — Owner reopen per DEC-077); the cash guards of §7 apply to `Payment` / `Receipt` lines on 1000 / 1005 with a party.
- Posting: an `approved` voucher is a source `{kind: 'manual', id, sub: template ?? 'journal'}` on its `date`, numbered with the day's derived vouchers at lock. Server: tables `acc.manual_voucher`, `acc.manual_line` (migrations 4238–4241), outbox events `ManualVoucherApproved`, `ManualVoucherVoided` → `SourceRules.Manual(...)`.
- Guided templates (UI lane) fill the lines: capital introduced (Dr bank / cash · Cr 3100), owner drawing from the bank (Dr 3000 · Cr 1030/[bank]), loan received (Dr 1030/[bank] · Cr 2400/[loan]), loan EMI (Dr 2400/[loan] principal · Dr 5090 interest · Cr 1030/[bank]), bank charges (Dr 5080 · Cr 1030/[bank]), interest income (Dr 1030/[bank] · Cr 4960), bad debt (Dr 5300 · Cr 1110/[company] or 1120/[customer]; A18, Owner only), statutory remittance / GST payment / TDS remittance (§5, §6).
- Loan party rows: an Owner-added ledger under `secured-loans` / `unsecured-loans` is a loan; the chart screen offers "Add loan" which creates `2400/<LN-code>` style party rows (control 2400 keeps its parties as Owner-added rows with `kind: 'loan'`, `lender`, `accountLast4`).

## 3. Purchases, vendor payments, returns (C1, C3, C4, C5, C8, C10; DEC-143, DEC-145, DEC-147)

Expense record additions (`Expense`, optional, old records unaffected):

```ts
gstKind?: 'intra'|'inter';          // inter = IGST on the bill (vendor outside Maharashtra): the whole GST goes to 1330 when ITC is allowed
rcm?: boolean;                      // reverse charge (from the category flag at the time of posting): bill has no GST; HCC posts the RCM GST (§3.3)
rcmBps?: number;                    // the RCM rate (category default; editable)
capital?: boolean;                  // fixed-asset purchase (category flag): the debit goes to the category's ledger (an asset block 15x0), never to expense reports / budgets
assetNo?: string;                   // fixed-asset register id (created on post; §3.4)
tdsPaise?: number; tdsSection?: string;   // TDS held back on this bill (§5); the vendor is credited net
cashDeductionLost?: boolean;        // §7 B10 guard tripped (cash > ₹10,000 to one person in a day)
```

Category master (`MnyCategory`) additions: `rcm?: boolean; rcmBps?: number; capital?: boolean; itcBlocked?: boolean` (s.17(5) list, DEC-143 guidance: when set the ITC switch cannot be turned on); vendor master (`MnyVendor`) additions: `state?: string` (two-letter GST state code; `MH` = intra), `tdsSection?: string`.

3.1 **Vendor bill on credit (C1)** — exists (`EXPENSE…` with funding `Vendor credit` → `Purchase` voucher type from this wave): Dr category ledger (bill − allowed ITC) · Dr 1310 / 1320 (intra, allowed) or 1330 (inter, allowed) · Cr 2200/[vendor] (bill − TDS) · Cr 2150 (TDS, §5). ITC allowed only when the category is not `itcBlocked` (guard message cites DEC-143).

3.2 **Vendor payment (C3) / advance (C4)** — new record `ExpVendorPayment` in `s.expensesX.vendorPayments`:

```ts
type ExpVendorPayment = {id: string; vendor: string; date: string; amount: number; method: string; reference: string; by: string; at: string;
  allocations: {expense: string; amount: number}[];            // oldest open bill first by default; the screen may re-allocate; Σ ≤ amount; the rest is an advance
  void?: boolean; voidReason?: string; voidBy?: string; voidAt?: string};
```
Command `EXP_VENDOR_PAY {vendor, amount, method, reference, date?, allocations?}` — `payments` permission and role Owner / Manager / Accountant; cash from the drawer needs an open shift and goes through the cash guard; bank / UPI need a reference. Posts `Payment`: Dr 2200/[vendor] · Cr tender (`method:` ledger; party = the settles-to bank). `EXP_VENDOR_PAY_VOID {id, reason}` (Owner; same-day or reversing). Open bills and ageing read `allocations` (bill date buckets 0–30 / 31–60 / 61–90 / 90+).

3.3 **Reverse charge (C8)**: category `rcm` → the bill is posted without vendor GST; HCC adds a second source `{kind: 'tax', sub: 'rcm'}`: Dr category ledger (or Dr 1310 / 1320 when ITC is allowed and not blocked) · Cr 2140 at `rcmBps`. 2140 is settled only by `gst-payment` (§6), never by set-off.

3.4 **Fixed-asset purchase (C10)**: category `capital` → debit to the category's ledger, which must be a `fixed-assets` group ledger (guard); ITC follows §4.4 / §4.5 (vehicles 1540 and building work 1500 are `itcBlocked`); an **asset register row** is created: `s.accountingX.assets[]` `{id, no: 'FA-<yyyy>-<nnn>', ledger, name, cost, date, vendor, expense, itc, putToUse?: string, disposed?: {date, amount, voucher}}`. The register is a list with CSV export; no depreciation journal in ACC-2 (DEC-140 / 138 — Tally).

3.5 **Return / vendor credit note (C5)**: `EXP_VENDOR_RETURN {expense, amount, gstPaise, reason}` (Manager request → Owner approve, like a credit note) posts a negative `Purchase` (same keys, swapped), reversing the ITC taken.

Server: `exp.vendor_payment`, `exp.vendor_payment_alloc`, `acc.fixed_asset`, new columns on `exp.expense`, `mst.expense_category`, `mst.expense_vendor` (migrations 4230–4237); events `VendorPaymentRecorded`, `VendorPaymentVoided`, `ExpenseRcmPosted`, `ExpenseReturned` → `SourceRules.VendorPayment`, `Rcm`, `ExpenseReturn`.

## 4. GST on advances switch (A6, DEC-142) and OTA (A13, DEC-150) and company receipts (A9)

- Firm profile (`CMPProfile`) gains `gstOnAdvances: boolean` (default **false**), `hotelDeductsTds: boolean` (default **true**, DEC-146), `tdsDeductorType: 'individual'|'other'` (rate column). Owner-only (money settings).
- With `gstOnAdvances` on, a folio deposit receipt also posts `{kind: 'tax', sub: 'advance-gst'}`: Dr 1350 · Cr 2110 · Cr 2120 (cum-tax on the amount at the booked room's rate); the deposit applied / refunded posts the reversal. With it off (today): nothing. Both paths tested.
- **Apply a deposit at check-out (A6a)** becomes a real command `DEPOSIT_APPLY {booking, amount}` (Reception / Cashier on an open shift): receipt kind `'deposit-apply'`, voucher `Journal` Dr 2000 · Cr 1100 (replaces the tests lane's "Advance adjusted" method, which the scenario drops).
- **OTA-paid stays**: a receipt kind `'ota-credit'` (like `company-credit`): at check-out the folio balance moves to `1115/<channel>` (`Journal` Dr 1115/[channel] · Cr 1100), allowed only when the booking's `source` is a connected channel. New record `CHNPayout` in `s.chnX.payouts`: `{id, channel, date, statementRef, gross, commission, commissionGst, tcs, net, bank, bookings: string[], by, at}`; command `CHN_PAYOUT` (Owner / Accountant, `PERM-OTA-004` holders) posts `Receipt`: Dr 1030/[bank] net · Dr 5070 (commission + its GST; ITC per §4.4 only if allowed on the channel setting `itcAllowed`) · Dr 1340 tcs · Cr 1115/[channel] gross. Guard: gross = net + commission + commissionGst + tcs; gross ≤ the channel's open receivable.
- **Company receipt net of TDS (A9)**: `GRP_COMPANY_PAYMENT` gains `tds?: number` → Dr tender (amount) · Dr 1360 (tds) · Cr 1110/[company] (amount + tds); the Form 168 reconciliation report lists 1360 entries by company and month.

## 5. TDS deducted by the hotel (C9, DEC-146)

- Dated **TDS-rate table** `TDS_SEED` next to `GST_SEED` (`src/domain/tds.ts`; server `TdsRates`), keys `tds.<section>.<field>` with versions from 2026-04-01: rows `rent` (s.393 Sl.2(ii), old 194-I: threshold ₹50,000 per month, 10 % land / building, 2 % plant), `contractor` (old 194C: ₹30,000 single / ₹1,00,000 year, 1 % individual / 2 % other), `professional` (old 194J: ₹50,000 year, 10 %; technical 2 %), `commission` (old 194H: ₹20,000, 2 %). Every row marked "CA to confirm", Owner-editable, source cited.
- When `hotelDeductsTds` and the vendor has `tds: true` with a `tdsSection`: on posting a bill / immediate payment, `tdsPaise = round(taxable × rate)` where taxable = amount − GST (Circular 23/2017); threshold tested on the vendor's FY total; the vendor is credited net; Cr 2150 TDS payable; `acc` source lines come from the expense rule (no separate voucher).
- `ACC_TDS_REMIT {month, section, amount, method, challan, date}` (Owner / Accountant): manual template `tds-remittance` → Dr 2150 · Cr 1030/[bank]. Report `tdsDeducteeReport(s, fy|month)`: vendor, PAN, section, taxable, TDS, remitted (CSV).

## 6. GST month end and the month lock (E1–E5, §G1)

- `accGstMonth(s, 'YYYY-MM')` computes: output by head (2110, 2120, 2130) from the month's vouchers, input by head (1310, 1320, 1330), RCM 2140, TCS credit and cash-ledger balance 1340, then the **set-off in s.49 order** (IGST credit → IGST, CGST, SGST in any order; CGST credit → CGST then IGST; SGST credit → SGST then IGST; never CGST ↔ SGST) and the net payable per head. Same function on the server (`GstSetOff`), unit-tested against six hand-worked cases (including the rulebook §8.6 month: output 14,500 + 14,500, ITC 0, TCS 100 → challan 28,900).
- `ACC_GST_SETOFF {month}` (Owner / Accountant) posts the generated `Journal` (source `{kind: 'tax', sub: 'gst-setoff'}`): Dr 2110 · Cr 1310 … and the remaining output Dr 2110 / 2120 / 2130 · Cr 1340; idempotent per month (a second call replaces an unlocked one). `ACC_GST_PAY {month, amount, method, challan, date}` → template `gst-payment` Dr 1340 · Cr 1030/[bank] (also used for RCM: Dr 2140 · Cr 1340).
- **Statutory remittances (D5)**: `ACC_STAT_REMIT {kind: 'pf'|'esic'|'pt', month, amount, method, challan, date}` → template `statutory-remittance` Dr 2310 / 2320 / 2330 · Cr 1030/[bank]; the matching compliance card is marked done with the challan as evidence.
- **Month lock (G1)**: `ACC_MONTH_LOCK {month}` (Owner; Accountant may `ACC_MONTH_LOCK_REQUEST`) requires every day of the month closed and locked (locks any closed-but-unlocked day in the same command, refusing on the first exception), the set-off posted, and no pending manual voucher dated in the month; it also sets the SCC expense month lock (`EXP_MONTH_LOCK` semantics). `ACC_MONTH_UNLOCK {month, reason}` (Owner) mirrors DEC-131. State `s.accountingX.monthLocks: Record<'YYYY-MM', {lockedAt, lockedBy, setoff: voucherId, unlocks?: {...}[]}>`.

## 7. Cash guards (B9, B10; Income-tax Act 2025 s.186 and the old 40A(3) rule)

- `accCashGuard(s, {kind: 'receipt'|'payment', party, amount, date})`: **receipt**: cash from one party (`1110/…`, `1120/…`, a guest's booking id, an OTA) totalling ≥ ₹2,00,000 on one day or for one booking / event is **refused** (`CASH_LIMIT`, message cites s.186). **payment**: cash to one party (vendor, staff, loan) totalling > ₹10,000 on one day is **allowed with a warning** and the record carries `cashDeductionLost: true` (the P&L notes list them; message cites the ₹10,000 rule; ₹35,000 for goods transport categories flagged `transport`). Applied in: folio / POS receipts, Udhaar settlements, company payments, expenses, vendor payments, manual Payment / Receipt, staff advances. Amounts in rupees are read from the dated table `TDS_SEED`-style keys `cash.receiptLimit`, `cash.paymentLimit`, `cash.transportLimit` (Owner-editable, "VERIFY section numbers" note).

## 8. Parity and carry-overs (ACC1-DONE §7)

- **Prototype payroll vouchers**: `accLiveVouchers` posts payroll like the server: advance given (`HR_ADVANCE_ADD`, Dr 1200/[staff] · Cr funding), pay run on lock (`HR_PAYROLL_LOCK`: the server's `PayRun` lines, with advance recovery **per staff** `Cr 1200/[staff]`), net pay paid when the bank file is generated / cash paid (`payroll:net-payable` Dr · Cr funding), F&F on confirm. Server: per-staff advance recovery lines (`PayRunFigures` gains `AdvanceRecoveries: (staffId, paise)[]`) — the 1200 control stops carrying a `no-party` warning.
- **F&B folio line class**: `Line.kind` gains `'food'`; POS charge-to-folio and catalogue extras of GST category `food` create `food` lines; mapping key `line:food` → 4200 (default); existing `service` lines unchanged. The ACC-RULEBOOK scenario drops its `line:service → 4200` override.
- **Company retire**: `GRP_COMPANY_SAVE` gains `active` (default true); retiring refuses on a non-zero `1110/[company]` balance (`accPartyRetireBlocker`), on open stays / blocks; retired companies are hidden from pickers.
- **Udhaar**: `EXP_UD_PAY_REVERSE {receivable, settlement, reason}` (Owner; same day or reversing) — A17b; the Udhaar screen always sends `kind` + `gstBps`.
- **Pre-4215 Udhaar credits** on the server: a one-off command `BackfillUdhaarCredits` (Owner, idempotent) publishes `UdhaarCreditRaised` for rows without a sale voucher; prototype rows before ACC-1 stay "other" (CA note).
- **Report readers beyond the 62-day window**: production loaders for `/accounting/statement`, `/trial-balance?from`, `/day-book` read any range from the server (no device derivation); aggregated **opening read** on the server (`acc.ledger_month_balance` materialised per locked month, migration 4257) so an opening is a sum of month rows + the current month.

## 9. Permissions (new codes)

| code | meaning | roles |
|---|---|---|
| PERM-ACC-010 | accounting.manual.enter | Owner, Accountant |
| PERM-ACC-011 | accounting.manual.approve (and void) | Owner |
| PERM-ACC-012 | accounting.month.lock / unlock | Owner (Accountant: request) |
| PERM-ACC-013 | accounting.tax (GST set-off, GST / TDS / statutory remittance entries, TDS report) | Owner, Accountant |
| PERM-EXP-0xx | expenses.vendor.pay (vendor payments, returns) | Owner, Manager, Accountant |
| PERM-OTA-004 (existing) | OTA payout entry | Owner, Manager, Accountant |

Prototype equivalents: `permission('admin')` + Owner for approve / lock; `permission('reports')` + role in Owner / Accountant for enter / tax; `permission('payments')` + role in Owner / Manager / Accountant for vendor payments and OTA payouts.

## 10. State, schema, migrations

- `SCHEMA` 11 → **12** (coordinator). `ACCState` gains `manual?: ACCManualVoucher[]`, `assets?: ACCAsset[]`, `monthLocks?: …`, `gstSetoffs?: Record<month, string>`; `EXPState` gains `vendorPayments?: ExpVendorPayment[]`; `CHNState` gains `payouts?: CHNPayout[]`; `CMPProfile` gains the three switches; the `Expense`, `MnyCategory`, `MnyVendor`, `Line.kind` additions of §3 / §8. The coordinator adds every type above to the wave branch before the lanes branch; lanes implement, never redeclare.
- Append-only blocks as in ACC-1 (`// ACC-2` blocks in `handlers/index.ts` (`ACC2` object), `services/mock/index.ts`, `clients/sync/registry.ts`, `HccComposition.cs`).
- Migrations: purchase 4230–4237 · manual 4238–4241 · tds 4242–4245 · gst 4246–4249 · flows 4250–4254 · parity 4255–4259. `migrations-lock.mjs --add` (`--accept-out-of-order` where the merge order needs it) and `Hcc.slnx` for each; RBAC seeds named `*_rbac_seed.sql`.
