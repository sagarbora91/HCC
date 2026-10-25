# GST master (DEC-069) — Wave 1 agent C

Replaces the DEC-039 synthetic 10 % (`ITaxRateSource` / `FixedTaxRateSource` are gone). **Every seeded rate is "Illustrative — CA to
confirm"** (public sources, 25 Oct 2026). Pure rules: `src/Hcc.Domain/Tax/GstMaster.cs` (port of `03 Prototype/src/domain/gst.ts`).

## Rules
| Supply | Rate (seed) | Test |
|---|---|---|
| Room night, per unit per night on the **actual price charged** | ≤ ₹7,500 → 5 % without ITC from 22 Sep 2025 (12 % with ITC before); > ₹7,500 → 18 % with ITC | ₹7,500.00 → 5 %, ₹7,500.01 → 18 %; never averaged |
| Restaurant / room service | 5 % without ITC; 18 % with ITC when "specified premises" | specified = any unit > ₹7,500 on any day of the previous FY (rack rate, dated tariffs, or a room night actually charged) **or** the Owner's opt-in in force on the date |
| Laundry, banquet/hall, other | one dated rate each, seed 18 % | category from `PostCharge.gstCategory` (food/laundry/banquet/other) or guessed from the label |
| Discount | follows the folio's room-night rate (that date's line, else latest earlier, else latest, else room rule at the booked rate) | CA to confirm apportionment on mixed-rate folios |
| Reversal (correction) | copies the original line's rate, category, ITC | |

Per line: tax = amount × bps / 10 000, half away from zero; SGST = half toward zero, CGST = the rest (takes the odd paisa).
Ledgers (`GstPosting`): 1100 Guest ledger dr · revenue cr (4000 room, 4100 other, 4900 discounts, 4910 adjustments) · **2110 Output CGST**
cr · **2120 Output SGST** cr. Intra-state only (IGST out of scope).

## Storage (migration 0600, schema `tax`)
- `tax.gst_setting` — the 10 keys (`gst.room.threshold`, `gst.room.lowBps`, `gst.room.lowItc`, `gst.room.highBps`, `gst.food.bps`,
  `gst.food.specifiedBps`, `gst.food.optIn`, `gst.svc.laundryBps`, `gst.svc.banquetBps`, `gst.svc.otherBps`).
- `tax.gst_setting_version` — dated versions: statutory seed rows (`property_id NULL`) + Owner changes per property. Append-only
  (trigger): history is every row; a change not yet in force can be withdrawn (status), nothing is edited or deleted.
- `tax.charge_gst` — per-charge snapshot beside `bill.charge` (category, bps, ITC, tax, CGST, SGST, rule), written in the posting
  transaction. `bill.charge.tax_bps / tax_paise` stay the folio's snapshot: master changes never alter posted lines.

## API
- `POST /commands/SetGstSetting {key, value, effectiveFrom?, reason}` — Owner only (PERM-ADM-003 'A'); effective from the business date
  or later (never backdated, ≤ 2 years ahead); rejects out-of-range values (0–28 %, ₹0.01–₹10 crore, 0/1) and no-op changes.
- `POST /commands/WithdrawGstSetting {versionId, reason}` — Owner only; only before it takes effect. To undo a change already in force,
  set the previous value again from today (history keeps both).
- `GET /tax/gst?date=` — Owner and Tech admin (read-only, `canChange=false`): values in force, summary lines, FY specified-premises
  status with the reason, full history.

## For other modules (agent A: closing / reports)
`IGstRateResolver` (`RoomNightAsync`, `RoomNightsAsync`, `ChargeAsync`) — pass the command transaction when inside one (the database
runs READ COMMITTED with locks). Reports should read `tax.charge_gst` (CGST/SGST per line) or recompute with `GstMath.Split` per line
— never split a total.
