# Hotel Control Centre — Financial Rulebook (DRAFT for prototype)

Version: 0.1 draft (M0) · 24 October 2026
Status: **Proposed**. Tax content is **synthetic** and is not a statement of applicable GST. Before production finance (M5), tax classification, CGST/SGST/IGST applicability, invoice series, credit notes, rounding and retention must be validated against current authoritative requirements and qualified advice (NFR-COMP-001).

## 1. Money

| Rule | Value |
|---|---|
| Currency | INR only |
| Representation | Integer paise (DEC-023); no floating point anywhere in calculation |
| Display | `₹1,23,456.00` (en-IN grouping); paise hidden when zero |
| Tax rounding | Per line, half-up to the paisa; totals = sum of rounded line taxes |
| Discount order | Discount lines are posted before tax and carry the same synthetic tax rate (negative tax) — DEC-039 |
| Final rounding | None in prototype (no round-off line). Production: decide invoice-level round-off with adviser |
| Maximum single amount | ₹10,00,000 (prototype guard) |

## 2. Record types — never mixed

| Record | Effect on folio balance | Revenue? | Cash drawer? |
|---|---|---|---|
| Room-night charge | + | Room revenue | — |
| Service charge | + | Service revenue (separate from room) | — |
| Discount | − (before tax) | Reduces revenue | — |
| Synthetic tax | + | Tax liability, not revenue | — |
| Payment / advance receipt | − | **No** | + if cash |
| Refundable deposit | **none** (liability) | **No** | + if cash |
| Deposit return | none | No | − if cash |
| Refund | + (reverses a receipt) | No | − if cash |
| Company credit (city ledger) | − (moves balance to receivable) | No | — |
| Expense (drawer-funded, posted) | n/a | Expense | − once |
| Expense (vendor credit / UPI) | n/a | Expense | — |

Folio balance = Σ charges + Σ tax − Σ payments + Σ refunds − company credit.
Deposit held = Σ deposits − Σ deposit returns (shown separately; checkout requires 0).
Advance: recorded as a payment receipt allocated to the folio when received; never deducted again at checkout (DEC-034).

## 3. Worked fixture (oracle for AC-006)

Fictional guest, Room 105 (Classic, ₹2,000/night), 24 → 26 Oct 2026, synthetic tax 10%.

| # | Business date | Event | Amount | Folio balance after | Deposit held | Drawer effect |
|---|---|---|---:|---:|---:|---:|
| 1 | 24 Oct | Advance, UPI `DEMO-UPI-0001` | ₹1,000 | −₹1,000 | 0 | — |
| 2 | 24 Oct | Refundable deposit, cash | ₹500 | −₹1,000 | ₹500 | +₹500 |
| 3 | 24 Oct | Night 24 Oct + tax | ₹2,000 + ₹200 | ₹1,200 | ₹500 | — |
| 4 | 25 Oct | Laundry service + tax | ₹500 + ₹50 | ₹1,750 | ₹500 | — |
| 5 | 25 Oct | Approved discount (above ₹250 limit) incl. tax effect | −₹500 − ₹50 | ₹1,200 | ₹500 | — |
| 6 | 25 Oct | Night 25 Oct + tax | ₹2,000 + ₹200 | ₹3,400 | ₹500 | — |
| 7 | 26 Oct | Settlement: ₹2,000 cash + ₹1,400 UPI `DEMO-UPI-0002` | ₹3,400 | ₹0 | ₹500 | +₹2,000 |
| 8 | 26 Oct | Deposit returned, cash | ₹500 | ₹0 | ₹0 | −₹500 |

Totals: net charges ₹4,000 (room ₹4,000, service ₹500, discount −₹500) · tax ₹400 · invoice ₹4,400 · gross receipts ₹4,900 (₹4,400 payments + ₹500 deposit) · deposit refunds ₹500 · net receipts ₹4,400.

Daily receipts follow actual receipt dates: 24 Oct ₹1,500 (₹1,000 UPI + ₹500 deposit), 26 Oct ₹3,400 payments and −₹500 deposit return. Daily cash is **not** expected to equal daily revenue.

**Verification (24 Oct 2026):** this sequence was executed against the current prototype domain (`03 Prototype/src/domain.ts`) with the S0 adjustments in the fixture catalogue. Result: net ₹4,000, tax ₹400, total ₹4,400, paid ₹4,400, deposit ₹0, balance ₹0. Script and output: `05 Delivery Evidence/M0/m0_walkthrough_sim.mjs` and `m0_walkthrough_sim_output.txt`.

## 4. Cash drawer

Expected cash = opening float + cash payments + cash deposits + cash transfers in − cash refunds − cash deposit returns − drawer-funded posted expenses − transfers out.

- Non-cash tenders are excluded.
- Opening float of a new business day = counted cash at the previous close (DEC-035).
- A difference ≠ 0 requires a reason and approval (PERM-SHF-002, PERM-SOD-004). *Current build: rejects any difference; approval path to add.*

## 5. Invoices, corrections, refunds

| Rule | Detail |
|---|---|
| Invoice number | Sample series `DEMO/2026-27/NNNN`, allocated once at issue; never reused; printed "Demonstration document — not a tax invoice" |
| Posted documents | Immutable; no edit or delete |
| Draft vs posted | Draft lines (before posting) may be edited; posted lines only reversed |
| Reversal | Opposite-sign line linked to original, with reason and approval |
| Credit note | Sample record linked to the invoice; reduces receivable; approval required |
| Refund | Must reference an eligible original receipt; ≤ refundable remainder; approval + separate execution (DEC-031) |
| Manual UPI/card | Confirmation says the bank transaction is not verified |
| Unknown payment result | Treat as "pending reconciliation"; retry uses the same key and must not create a second receipt |

## 6. Report dictionary (REQ-RPT-006)

| Metric | Definition | Zero handling |
|---|---|---|
| Sellable rooms | Rooms not blocked on the business date | — |
| Occupied rooms | Stays in house on the business date | — |
| Occupancy % | Occupied ÷ sellable × 100, rounded to 1 dp | sellable = 0 → "—" |
| Room revenue | Σ room-night lines (net of room discounts) with service date = date | — |
| ADR | Room revenue ÷ occupied room nights | 0 nights → "—" |
| RevPAR | Room revenue ÷ sellable rooms | 0 → "—" |
| Receipts | Σ payment receipts − refunds by receipt date; deposits shown separately | — |
| Deposits held | Σ deposit − deposit returns, all open folios | — |
| Outstanding | Σ positive folio balances of open stays | — |
| Expenses | Σ posted expenses by business date, split by funding source | — |

Note: the current build rounds occupancy to whole numbers and counts "available" as vacant + ready + unblocked (so dirty vacant rooms are not available). To be aligned with this dictionary.

## 7. Phase-2 hooks (area 20)

Every financial record keeps: stable source ID, record type, business date, service date, timestamp, actor, folio/stay/booking IDs, tax snapshot (rate, rule ID, effective date), tender and reference. No general-ledger posting in MVP.
