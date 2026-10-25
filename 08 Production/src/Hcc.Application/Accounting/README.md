# Accounting & Tally (W2-3)

Vouchers are derived, never keyed. The outbox consumer `accounting-vouchers` (`AccountingVoucherProjector`, worker every 2 s) records one **source** per operational record (`acc.source` + `acc.source_entry`, append-only, unique key per property). Each source carries classification keys (`line:room`, `method:UPI`, `expense:Laundry`, …). The mapping (defaults in `ChartOfAccounts`, Owner overrides in `acc.mapping`) turns them into ledgers when the vouchers are read, for unlocked dates. `LockAccountingPeriod` stores the numbered vouchers as a snapshot. Rules come from the prototype `accounting.ts` and module spec 20. The golden tests check them against the prototype figures.

## Events consumed (publishers: use exactly these names and payloads)
Payloads are camelCase JSON written by `ICommandTransaction.PublishAsync`. The `plat.outbox.property_id` column is required. Dates are `yyyy-MM-dd` and money is paise. Records are in `Hcc.Contracts.Accounting`.

| Event | Publisher | Payload | Voucher |
|---|---|---|---|
| `ChargePosted` | P4 (exists) | folioId, chargeId, kind room/service/discount/correction, serviceDate, amountPaise, taxPaise | Sales: Dr 1100 · Cr revenue · Cr 2110/2120 (stored CGST/SGST via `IChargeGstReader`) |
| `ReceiptRecorded` | P4 (exists) | folioId, receiptId, kind, tender, amountPaise, businessDate | Receipt / Payment / Journal (company credit) |
| `ApprovalDecided` | P4 (exists) | correction + Executed → reversal line read from `bill.charge` | Sales (negative) |
| `ShiftDifferenceDecided` | **P4 requested** | `ShiftDifferenceDecidedEvent` | Journal 5900 ↔ 1000 (until it exists: swept from `pay.shift`) |
| `PosBillSettled` | **W2-6 POS** | `PosBillSettledEvent` (only bills settled at the outlet; room-posted bills = ChargePosted service) | Sales: Dr tender · Cr 4100 · Cr 2110/2120 |
| `ExpensePosted` / `ExpenseReversed` | **W2-4 Expenses** | `ExpensePostedEvent` (reversal: negative amount + reversalOf) | Payment (Journal for Vendor credit) |
| `PayrollPosted` | **W2-2 Payroll** | `PayrollPostedEvent` (gross = net + employee deductions) | Journal: 5100/5110 · 2300/2310/2320/2330/2340/1200 |
| `PayrollPaid` | **W2-2 Payroll** | `PayrollPaidEvent` | Payment: Dr 2300 · Cr funding |

Published by accounting: `AccountingPeriodLocked` and `AccountingPeriodUnlocked`.

Unknown categories, tenders or funding methods are posted to 9999 Suspense. This raises an *Unmapped* error that blocks the lock until the Owner maps the key. Events that are not yet processed and cash differences that are not yet swept also block the lock (*pending*).
