# HCC Module 20 — Accounting automation (prototype spec)

Status: built as a working mock in the interactive prototype (stream `accounting`). Fictional data, synthetic 10% tax, no GST claim. Prefix `acc` / `ACC`.

## 1. Purpose
Turn the hotel's existing operational records into **balanced double-entry vouchers per business date**, without anyone keying journals. The accountant (or owner) reviews a day's vouchers, trial balance and exceptions, locks the period once the day is closed, and exports a generic voucher file. Mapping to Tally (or any other ledger package) is a later step; the export is a **generic import format**.

## 2. Principles
- **Derived, not entered.** Vouchers are produced by a pure selector (`accLiveVouchers(s, date)`) from source records. No voucher can be created or edited by hand.
- **Traceable.** Every voucher carries a stable source ID (`<booking>/<line or receipt id>`, `<expense id>`, `CLOSE/<date>/cash-difference`) and source kind. One voucher per source record.
- **Balanced.** Each voucher's debits = credits (integer paise). Trial balance per date: Σ debits = Σ credits.
- **Reconciles to existing reports.** Ledger movements equal the Reports/Closing figures for the same date (room/service revenue, discounts, adjustments, tax, net receipts, deposits, expenses, cash).
- **Frozen after lock.** Locking a closed day stores the vouchers as a snapshot in `s.accountingX.locks[date]`. Later mapping changes do not alter locked days; a "live differs from snapshot" warning appears if sources change.
- Money is integer paise; simulated clock only; no network.

## 3. Synthetic chart of accounts
| Code | Ledger | Group |
|---|---|---|
| 1000 | Cash in hand | Asset |
| 1010 | UPI clearing | Asset |
| 1020 | Card clearing | Asset |
| 1030 | Bank account | Asset |
| 1100 | Guest ledger | Asset |
| 1110 | Company receivables (city ledger) | Asset |
| 2000 | Advance deposits | Liability |
| 2100 | Synthetic tax payable | Liability |
| 2200 | Vendor payables | Liability |
| 4000 | Room revenue | Income |
| 4100 | Other revenue | Income |
| 4900 | Discounts allowed | Income (contra) |
| 4910 | Refunds, reversals & credit notes | Income (contra) |
| 5000–5050 | Supplies, Laundry, Repairs & maintenance, Utilities, Petty cash & sundry, General expenses | Expense |
| 5900 | Cash over / short | Expense |
| 9999 | Suspense (unmapped) | Suspense — never a mapping target |

## 4. Mapping table (source classification → ledger)
Keys and defaults (owner/admin may change any key to another non-suspense ledger; changes are recorded with before/after, actor and simulated time, and apply to **unlocked** dates only):
- Folio lines by kind: `line:room`→4000, `line:service`→4100, `line:discount`→4900, `line:adjustment`→4910; counter-ledger `folio:guest`→1100; tax `folio:tax`→2100.
- Receipt methods (tender): `method:Cash`→1000, `method:UPI`→1010, `method:Card`→1020, `method:Bank transfer`→1030.
- Receipt kinds (counter-ledger): `receipt:payment`→1100, `receipt:refund`→1100, `receipt:deposit`→2000, `receipt:deposit-return`→2000, `receipt:company-credit`→1110.
- Expense categories: `expense:Supplies`→5000, `Laundry`→5010, `Maintenance`→5020, `Utilities`→5030, `Petty cash`→5040.
- Expense funding: `funding:Cash`→1000, `funding:UPI`→1010, `funding:Card`→1020, `funding:Bank transfer`→1030, `funding:Vendor credit`→2200.
- Closing: `closing:cash-difference`→5900.

Any classification found in data without a mapping (e.g. a new expense category, a new line kind or tender added by another module) is posted to **9999 Suspense** and raised as an **Unmapped** exception that blocks locking.

## 5. Voucher rules (per business date d)
| Source (date used) | Type | Entries |
|---|---|---|
| Folio line, `l.date = d` (amount a, tax t = round(a × taxBps / 10000)) | Sales | Dr Guest ledger a+t · Cr line-kind ledger a · Cr Tax payable t (negative amounts swap sides: discounts, reversals, credit notes) |
| Receipt `payment` / `deposit`, `r.date = d` | Receipt | Dr tender ledger · Cr receipt-kind ledger |
| Receipt `refund` / `deposit-return` | Payment | Dr receipt-kind ledger · Cr tender ledger |
| Receipt `company-credit` | Journal | Dr Company receivables · Cr Guest ledger |
| Expense Posted/Reversed, `e.date = d` | Payment (Journal for vendor credit) | Dr category ledger · Cr funding ledger (reversal record, negative amount, swaps sides) |
| Closing record (or closed shift) cash difference ≠ 0 | Journal | Short: Dr Cash over/short · Cr Cash; Over: Dr Cash · Cr Cash over/short |

Pending, draft and rejected expenses produce no voucher. Zero-value sources are skipped.
Voucher numbers: `SAL|RCT|PMT|JRN/<yyyymmdd>/<nnn>`, numbered per type in source order; **provisional until the day is locked**, then fixed in the snapshot.

## 6. Exceptions (per date)
- **Unbalanced** (error): a voucher whose Dr ≠ Cr, or a trial balance with Σ Dr ≠ Σ Cr.
- **Unmapped** (error): a source classification with no ledger (posted to Suspense).
- **Snapshot drift** (warning, locked days): live source vouchers differ from the locked snapshot (source IDs or amounts).
- **Day open** (info): the business day is not yet closed — vouchers are provisional and the day cannot be locked.

## 7. Reconciliation checks (shown per date, tested)
Ledger movement vs. Reports/closing figure: Room revenue, Other revenue, Discounts, Adjustments, Synthetic tax, Net receipts (payments − refunds, e.g. **₹21,500** on the 24 Oct fixture = Overview "collections"), Deposits net, Posted expenses, Cash in hand (open day: drawer − opening float; closed day with record: counted − opening). Each check shows both figures and OK / Differs.

## 8. Commands (mock service)
| Command | Permission | Rules |
|---|---|---|
| `ACC_LOCK {date}` | `reports` + role Owner, Manager or Accountant | Date must be closed (`closedDates`), not already locked, no error exceptions. Stores snapshot (vouchers, totals, lockedBy, lockedAt). Idempotent via key; a second lock is rejected (`INVALID_TRANSITION`). |
| `ACC_UNLOCK {date, reason}` | `admin` + `reports` (Owner) | Reason required; history kept. Vouchers return to live derivation. |
| `ACC_MAP_SET {source, account}` | `admin` | Key must be a known or discovered classification; account must exist and not be Suspense; no-op change rejected. History kept. |
| `ACC_MAP_RESET {source}` | `admin` | Returns the key to its default ledger. |

Housekeepers, maintenance and tech admin cannot open the page (no `reports`). Cashier can view and export but not lock.

## 9. Export
`accCsv(vouchers)` → header `Date,Voucher No,Voucher Type,Ledger,Debit,Credit,Narration,Source ID`, one row per entry, amounts in rupees with 2 decimals, CRLF. File name `HCC-Demo-vouchers-<from>_<to>.csv`. Labelled in the UI: "Generic import format · fictional data · Tally/other mapping later".

## 10. Screens
**Desktop — CLOSING & REPORTS › Accounting** (`reports`):
- Header: business-date selector (dates with activity, closed dates, today), status pill (Open / Closed / Locked), Export CSV, Lock period (or Unlock for owner).
- Summary stats: vouchers, total debits, total credits (balanced pill), exceptions.
- Tabs: **Vouchers** (table; click → voucher dialog with entries and source), **Trial balance** (day movement or cumulative to date), **Exceptions**, **Reconciliation**, **Mapping** (edit = admin), **Chart of accounts**.

**Phone** (same page ID): date chips, day summary KPI cards, exceptions list, reconciliation status card, voucher list cards (tap → voucher sheet), sticky footer with one primary action (Lock period when eligible, else Export CSV). Mapping editing stays desktop-first but works in a sheet.

## 11. Fixture / scenario
No base-fixture changes (existing figures untouched). Scenario **ACC — Accounting exceptions**: base day plus a posted UPI expense in an unmapped category "Kitchen gas" (→ Unmapped exception) and 23 Oct already locked.

## 12. Out of scope (later)
Tally XML / other package mappings, GST ledgers, opening balances, multi-property, voucher-number series configuration, bank reconciliation.
