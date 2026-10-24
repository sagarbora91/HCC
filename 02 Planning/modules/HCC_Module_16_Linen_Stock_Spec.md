# HCC Module 16 — Linen, laundry & stock (prototype spec)

Status: built in the interactive prototype (mock service, fictional data). Stream `inventory`, prefix `inv` / `INV_`.
Page `inventory` (nav OPERATIONS · "Linen & stock", gate permission `housekeeping`). Desktop page + phone page.

## 1. Purpose
Know where every piece of linen and every store item is, keep floors supplied, control the laundry vendor, record losses with a reason, raise reorder alerts, and measure consumption against occupied room-nights. Physical counts expose variances, which a second person reviews.

## 2. Scope (prototype)
| # | Capability | Notes |
|---|---|---|
| 1 | Item master | Linen (pieces), amenities, cleaning supplies. Unit, standard cost (paise, weighted average), minimum (clean stock), reorder quantity, standard use per occupied room-night. |
| 2 | Par levels per room type | Linen pieces per room for Classic / Deluxe / Suite. Required circulation = Σ rooms of type × par × par sets (3: in room, in laundry, on shelf). Coverage % and shortfall shown. |
| 3 | Stock by location | Main store, Floor 1–3 pantries, In rooms (linen on beds), Soiled (awaiting dispatch), At laundry. |
| 4 | Stock in (GRN, simulated) | Supplier + reference + lines (item, qty, optional unit cost). Goes to Main store. No purchase order, no accounting posting; supplier bill still goes through Expenses. |
| 5 | Issue / return / use | Store ↔ floor pantry transfers; amenities & cleaning supplies used from a floor pantry; linen room change (pantry → room, room → soiled). |
| 6 | Laundry batches | Soiled linen sent to the fictional vendor *Fresh Linen Services* (simulated pickup). Return counts per item: received clean → Main store; damaged → written off; missing → vendor shortage (loss record, claim). Laundry charge = (received + damaged) × vendor rate per piece (money roles only). |
| 7 | Shortage / damage / loss | Reason required (Damaged / Stained beyond use / Missing / Taken by guest / Expired / Other with note). Quantity ≤ stock at that location. Value at standard cost (money roles only). |
| 8 | Reorder alerts | Clean stock (store + floor pantries) below minimum → alert with suggested quantity = max(reorder qty, 2 × min − clean). "Receive" pre-fills the GRN. |
| 9 | Consumption report | Per item for a date range: used (amenities: use moves; linen: room-change moves), occupied room-nights (existing bookings In house/Departed; fixture pre-go-live history for earlier dates), per room-night, standard, variance % (flag > ±15%). Printable. |
| 10 | Physical count with variance | Count sheet per countable location (store, floor pantries), pre-filled with system quantity ("confirm or correct"). Submit snapshots system qty. No variance → posted at once. Variance → awaits review by a user with `inspect` who is not the counter; approval needs a reason and adjusts stock by the variance (moves since the count are preserved); shortages also land in the loss register as "Count shortage". |

## 3. Roles & permissions (coarse model; no new permission values)
| Action | Permission |
|---|---|
| See page, quantities, issue/use/room change, send/receive laundry, report loss, submit count | `housekeeping` (Housekeeper, Supervisor, Maintenance, Manager, Owner) |
| Receive stock (GRN) | `housekeeping` + (`inspect` or `expenses`) |
| Enter a GRN unit cost | additionally `expenses` or `reports` |
| Post / reject a count variance (not own count), edit minimums & par | `inspect` |
| See stock values, costs, laundry charges, loss values | `reports` or `expenses` (Owner, Manager). Housekeepers and supervisors see quantities only. |

All commands are dated with the business date and refuse when the day is closed (`DAY_CLOSED`). Idempotency keys on every UI submit.

## 4. Commands
`INV_GRN` · `INV_ISSUE` · `INV_USE` · `INV_ROOM_CHANGE` · `INV_LAUNDRY_SEND` · `INV_LAUNDRY_RECEIVE` · `INV_LOSS` · `INV_COUNT_SUBMIT` · `INV_COUNT_POST` · `INV_ITEM_SET`.
Errors: `VALIDATION` (quantities must be whole numbers 1–10,000, not more than the stock at the source), `FORBIDDEN` (permission, self-review), `INVALID_TRANSITION` (batch already returned, count already decided, another open count for the location), `NOT_FOUND`, `DAY_CLOSED`.

## 5. State (`s.inventoryX`, type `INVState`)
`items[]` (with `stock` per location), `moves[]` (ledger, newest last), `batches[]`, `losses[]`, `counts[]`, `grns[]`, `parSets`, `vendor`, `occHistory` (fixture occupancy before go-live). Absent slice = empty module.

## 6. Fixture (fictional)
17 items (7 linen, 6 amenities, 4 cleaning). Seven days of consumption history (17–23 Oct). One laundry batch out (sent 23 Oct), one returned short on 22 Oct (1 damaged, 1 missing). Three items below minimum (bath towel, shampoo, garbage bags). One posted store count (20 Oct) and one Floor 2 count by Ravi awaiting review. Vendors: Fresh Linen Services (laundry), Morning Supplies, Deccan Hygiene Traders.

## 7. Screens
Desktop: KPI strip, reorder-alert card, tabs Stock · Laundry · Counts · Losses · Consumption · Par & minimums · Movements; dialogs for GRN, issue/use, laundry send/receive, loss, count sheet, count review, item settings, printable stock / consumption report (A4).
Phone: KPI cards, sections Stock · Count · Laundry · Alerts (chips). Stock cards per item for a chosen location; quick count with `NumberStepper` per item and a sticky submit; laundry batches as cards with receive sheet; Fab "Issue / use". Print stays available.

## 8. Known gaps (later)
Blind count option; partial laundry returns; purchase orders and supplier bills linked to Expenses; per-room linen tracking (RFID); charging a guest for taken items; linen life-cycle (wash count) and discard ageing.
