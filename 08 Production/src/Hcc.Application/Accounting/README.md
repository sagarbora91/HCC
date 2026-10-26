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

## Tally export: voucher ids and provisional days (A03b-05, migration 3950)
- **REMOTEID.** Every voucher in the simulated Tally XML carries `REMOTEID` and the same `<GUID>` = `HCC-<identity>-<source key>` (`VoucherExport.RemoteId`). The identity is one random GUID per property (`acc.tally_identity`, created once, never changed; a reinstalled HCC with a new database gets a new one); the source key is unique per property and never changes. So a voucher has the same id in every export, live or locked, and every voucher has its own.
- **Tally import behaviour (as Tally documents it; not tried against a real Tally here).** Tally takes the `REMOTEID` as the voucher's GUID. With *Overwrite vouchers when a voucher with same GUID exists* set to **Yes** (TallyPrime: Import › Transactions; Tally.ERP 9: F12 *Overwrite vouchers during import*), importing a voucher whose GUID Tally already holds replaces it instead of adding a second copy. `ACTION` stays `Create`: `Alter` fails for a voucher Tally does not hold yet, and HCC cannot know what the accountant imported. HCC never invents `VCHKEY` / `MASTERID` (Tally's own keys). Vouchers are never removed from a date (corrections are new vouchers), so a re-import never has to delete one. The XML says this in its second comment line (`VoucherExport.TallyIdNote`).
- **Re-export notes.** A day reopened after it was exported (DEC-077) is carried again with a `RE-EXPORT` note that now says the import replaces its vouchers (no delete first). A day that an export recorded **before** this migration took to Tally (`acc.tally_export.remote_ids = 0`) gets a `LEGACY` note and is listed in `legacyDates`: those copies have no id, so delete them in Tally before importing.
- **Provisional while events are pending.** Every export checks the pending count that blocks a lock (`AccountingVoucherProjector.PendingSql`: unprocessed and parked events of the property). While it is above zero every carried date that is not locked is `PROVISIONAL` (a locked date is its snapshot), the note and `pendingEvents` give the count, and the date is flagged (`acc.tally_reexport`, `reopen_id` NULL) so that the first export after the events are processed carries it again and clears the flag. The production client shows the count and the legacy days as a warning after the download.
