# F4 contract — tariff card, revenue by category and tender, owner digest, quick wins

Wave F4 of `02 Planning/HCC_Wave_Plan_F1-Fn_2026-11-02.md` (section "### F4"); source specs `HCC_Pending_Inventory_and_Roadmap_2026-11-02.md` §3.1, §3.3 and the "Owner daily digest" item; rulings DEC-058, DEC-061, DEC-063, DEC-069, DEC-142, DEC-198, DEC-202, DEC-MA-102. Branch `claude/f4` (cut from `claude/lf` 80163754 — F3 is not on `main` yet; F4 stacks on it). Cloud lane plan: `F4_Cloud_Lanes.md`. Written 2 Nov 2026 by the F4 integrator (Opus).

**Owner defaults applied (plan Appendix D, owner silent):** A25 weekend = Fri / Sat / Sun nights (Owner-editable); one card a year + dated seasonal versions; child band 5–12, **child price blank** (row exists, no money until the owner gives it; a blank child row publishes nothing); Manager **proposes only**, Owner publishes; day-use row yes; English print only. A27 revenue headline both incl. and excl. GST; restaurant / counter / hall rows on the same page; advances shown both received and adjusted. A29 digest yes; e-mail carries money, WhatsApp counts only (DEC-202 #7).

**Schema.** `SCHEMA` stays **13**. Every F4 persisted addition is a *new optional slice* (`tariffCardX`, `digestX`, `feedbackX`) or a *new optional field* on an existing record (`tariffs[].days`, `tariffs[].cardVersion`). A v13 save without them restores unchanged (absent slice = empty; absent `days` = `'all'`), so there is no migration step and nothing to bump; the plan's F4 exit line also says SCHEMA 13. A lane that needs a non-optional or reshaped field stops and writes it for the integrator.

---

## 1. Tariff card (§3.1) — lane T1

### 1.1 Prototype types (declared in `src/domain/types.ts`, F4 section; import, never redeclare)

```ts
TRCLineKind = 'room' | 'occupancy' | 'meal' | 'extra' | 'hall' | 'day-use'
TRCDaySet   = 'all' | 'weekday' | 'weekend'
TRCLine     = { id; kind; roomType?; plan?: 'EP'|'CP'|'MAP'|'AP'|string; occKind?: 'adult'|'child'|'bed';
                mealPlan?; extraId?; hallId?; daySet: TRCDaySet; rateExcl: number|null /* paise, null = blank */;
                taxBps: number /* snapshot at effectiveFrom */; displayIncl: number|null /* derived */;
                enteredIncl?: number /* what the user typed, when entered inclusive */; minGuests?; maxGuests?; ageFrom?; ageTo?; note? }
TRCStatus   = 'draft' | 'proposed' | 'published' | 'superseded' | 'rejected'
TRCVersion  = { id /* TRC-0001 */; label; effectiveFrom /* yyyy-mm-dd */; status; weekendDays: number[] /* 0=Sun … 6=Sat; default [5,6,0] */;
                lines: TRCLine[]; by; at; approvalId?; proposedBy?; publishedBy?; publishedAt?; supersededBy?; notes?;
                publishLog?: { master: 'tariffs'|'occupancyCharges'|'mealPlans'|'extras'; ref: string; before?: number; after: number }[] }
TRCState    = { versions: TRCVersion[]; weekendDaysDefault: number[] }      // s.tariffCardX
```
`State.tariffs[]` gains optional `days?: TRCDaySet` (absent = `'all'`) and `cardVersion?: string` (which card version wrote the row).

### 1.2 Server tables (migrations **4380–4389**, lane T1)

| # | File | Content |
|---|---|---|
| 4380 | `4380_trc_tables.sql` | `mst.tariff_card_version` (id, label, effective_from date, status, weekend_days varchar(13) e.g. `5,6,0`, created_by, created_at, approval_id null, proposed_by, published_by, published_at, superseded_by null, notes, row_version) + `mst.tariff_card_line` (version_id FK, line_no, kind, room_type_id null, plan, occ_kind, meal_plan_id, extra_id, hall_id, day_set, rate_excl_paise bigint null, gst_bps int, display_incl_paise bigint null, entered_incl_paise null, min_guests, max_guests, age_from, age_to, note) |
| 4381 | `4381_trc_tariff_dayset.sql` | `inv.room_type_tariff.day_set` char(1) not null default `'A'` (A/W/E) + `card_version_id` null; unique key extended with `day_set` |
| 4382 | `4382_trc_publish_log.sql` | `mst.tariff_card_publish_log` (version_id, master, row_ref, before_paise, after_paise, at, by) |
| 4383 | `4383_trc_rbac_seed.sql` | PERM-TRF-001..003 + role grants (§1.5) |
| 4384–4389 | reserve | fallout |

Published versions and their lines are **append-only** (no UPDATE except status → superseded and `superseded_by`); enforced in the store and by a trigger in 4380.

### 1.3 Publish semantics (never a second price list — DEC-MA-102)

The card is a **versioned view + snapshot**. Nothing reads `tariffCardX` / `mst.tariff_card_*` for a price: bookings, folios, yield and OTA keep reading the existing masters. `trcPublishPlan(s, version)` returns the writes; `TRC_PUBLISH` applies them **in one command / one SQL transaction, one audit row per master row written** plus one `TRC_PUBLISH` audit row:

| Card line | Master written | Row |
|---|---|---|
| `room` (type, plan EP, daySet) | tariff schedule (`s.tariffs` / `inv.room_type_tariff`) | `{type, rate: rateExcl, effectiveFrom, days: daySet, cardVersion}` — `weekday` + `weekend` pair = **two rows**; `all` = one row |
| `room` plan CP/MAP/AP | meal-plans master: add-on per person per night = (plan line − EP line) ÷ the line's guests basis, dated `effectiveFrom` | only when the difference is not already the master's value |
| `occupancy` adult / child / bed | occupancy-charges master (`MST_OCC_CHARGES`), dated money for the type | child line with `rateExcl:null` → **no write** (blank child price, A25) |
| `meal`, `extra` | meal-plans / extras catalogue, dated | |
| `day-use` | tariff schedule row with `days:'all'` under plan code `DAY` only if the yield day-use rate plan exists; otherwise the line prints only (listed in the publish result as "print only") | |
| `hall` | nothing until F6 (Halls tab placeholder "after hall booking"); a hall line is refused at publish (`TRC_HALL_NOT_READY`) | |

A master row with the same key and `effectiveFrom` already present is **replaced only if it was written by an earlier card version** of the same date; any hand-entered row on that date → refusal `TRC_CONFLICT` naming the row. Supersede = publishing a later version (the earlier becomes `superseded`); there is no unpublish — rollback = publish a new version.

**Rate resolution (the one behavioural change outside the card).** The existing tariff lookup (`rateFor` / the booking rate picker, server `TariffResolver`) picks, per night, the latest row with `effectiveFrom ≤ night` whose `days` matches the night (`weekend` if the night's weekday ∈ the active version's `weekendDays`, else `weekday`), falling back to an `all` row. Rows without `days` behave exactly as today. Existing bookings keep their posted rates (rates snapshot at booking / posting, unchanged).

### 1.4 Rounding

- Money in integer paise. `taxBps` = rooms per unit per night slab from the **dated GST master at `effectiveFrom`** (DEC-069; with the owner's setup of 18 % per DEC-198; code default stays 5 % ≤ ₹7,500 — the card reads the master, never a constant). Slab test uses the **exclusive** rate per unit per night.
- `trcInclusive(excl, bps) = excl + cgst + sgst`, each half = `round_half_away_from_zero(excl × bps / 2 / 10000)` — **the same per-line split as the folio** (DEC-069), so the card's inclusive figure equals what the guest's bill shows.
- Inclusive entry: `trcExclusiveFromInclusive(incl, bps)` = the exclusive paise `e` (integer) that minimises `|trcInclusive(e, bps) − incl|`, ties to the lower `e`; the line stores `rateExcl = e`, `enteredIncl = incl`, `displayIncl = trcInclusive(e)`, and the editor shows `displayIncl − enteredIncl` as "rounding difference" (₹0.00 or ±₹0.01). Golden: Deluxe ₹2,000 incl at 18 % → excl 169,492 paise (₹1,694.92), display ₹2,000.00 (DEC-198).
- If the incl entry crosses the slab boundary (e.g. excl slab would change), the line is refused `TRC_SLAB_AMBIGUOUS` — the user enters exclusive.

### 1.5 Commands, refusals, permissions

Commands (prototype `handlers/tariffcard.ts`, server `TariffCardCommands`): `TRC_DRAFT_SAVE` (create / edit a draft), `TRC_PROPOSE` (Manager or Owner; creates an Approval `kind:'tariff-card'`, target = version id), `TRC_PUBLISH` (Owner; from draft or proposed; approval executed), `TRC_REJECT` (Owner, reason), `TRC_DISCARD` (draft only), `TRC_WEEKEND_SET` (Owner), `TRC_IMPORT` (Owner; DEC-198 CSVs → a draft).

Refusals (`HccError`): `TRC_PAST_EFFECTIVE` (effectiveFrom < business date), `TRC_PUBLISHED_READONLY`, `TRC_CONFLICT`, `TRC_HALL_NOT_READY`, `TRC_SLAB_AMBIGUOUS`, `TRC_EMPTY` (no publishable line), `TRC_DUP_LINE` (same kind/type/plan/daySet twice), `FORBIDDEN`.

| Permission | Meaning | Owner | Manager | Accountant | Reception | Cashier | Restaurant | Housekeeping / Maintenance |
|---|---|---|---|---|---|---|---|---|
| PERM-TRF-001 | view card, history, print | y | y | y | y | y | n | **n** (DEC-061) |
| PERM-TRF-002 | draft + propose | y | y | n | n | n | n | n |
| PERM-TRF-003 | publish / reject / weekend days / import | **y only** (DEC-058) | n | n | n | n | n | n |

Prototype `rbac.ts` gets these as one line in the T1 block (`'tariffcard.view' | 'tariffcard.propose' | 'tariffcard.publish'`).

### 1.6 DEC-198 import

`TRC_IMPORT` reads the five rate-card CSVs (room-types / tariffs / occupancy-charges / meal-plans / rooms; layout as in `Existing Software\DB-PROFILE\rate-card\`, outside the repo — the test uses a fictional copy under `tests/fixtures/f4/rate-card/` with the same headers and DEC-198 figures, which are the owner's public rate card, not guest data) → one draft version effective 1 Nov 2026, `daySet:'all'`, child row blank. Test: the imported draft's `displayIncl` equals the card (Luxury Suite EP 3,300 / CP 3,500 … Non-AC 1,500 / 1,700; 3rd guest 500, Twin / Non-AC 400; breakfast 200 a room).

### 1.7 Screens and print (T1)

Page `tariff-card` in REVENUE & SALES (`PAGE_CATEGORY['tariff-card']='revenue'` — use the existing id of that category in `navmap.ts`), FIT split, tabs **Rooms · Meal plans & extras · Seasons & weekends · Halls (placeholder) · History · Print**; editor grid with an "Enter as incl. GST" toggle and the rounding difference; version strip; buttons by permission; phone: cards, read-only except Owner. Print: `rpt.admin.rate-sheet` extended — A4 letterhead, "Prices include GST @ <rate> %" from the published lines' bps (18 % per DEC-198), "Valid from <effectiveFrom>" line, T&C footer from settings (blank-safe), English only; CSV of the card and of the version history; PDF hook `renderPdf?.('rate-sheet', model)` left for F7. Print figures = screen figures (same selector).

---

## 2. Revenue by room category and tender (§3.3) — lane T2

Catalogue id `rpt.money.revenue-by-category-tender`, P1, money flag y, gate PERM-RPT-002. Roles Owner, Accountant, Manager (DEC-202 #1); Reception / Cashier / Restaurant / Housekeeping / Maintenance → `FORBIDDEN`. Export (Excel / CSV) Owner, Manager, Accountant (RPT-003). Date class D-F (≤ 366 days, FY / month / day presets, legacy FY allowed per DEC-202 #5).

### 2.1 Shape (`src/domain/streams/rpt-revcat.ts`, types in `types.ts`)

```ts
RCTBasis = 'business-date' | 'stay'          // stay = arrival date of the stay (secondary toggle, Block A only)
RCTBlockARow = { key /* room type key | 'restaurant' | 'counter' | 'hall' | 'other' */; label; rooms; nightsSold; occPct;
                 roomTariff; extraPerson; mealPlans; otherRoomCharges; discounts; corrections; net; cgst; sgst; gross; adr }
RCTBlockBRow = { key /* room type key | 'counter' | 'hall' | 'restaurant' | 'unassigned' */; label;
                 cash; upi; card; bank; companyCredit; udhaar; ota; otherTenders: Record<string,number>;
                 advancesReceived; advancesAdjusted; advancesRefunded; refunds; netReceived }
RCTBridge   = { openingDue; gross; netReceived; advanceMovement; closingDue /* company 1110 + Udhaar 1120 + guest folios */;
                openingAdvances; closingAdvances; difference /* must be 0 */ }
RCTReport   = { from; to; basis; legacy: boolean; a: RCTBlockARow[]; aTotal: RCTBlockARow; b: RCTBlockBRow[]; bTotal: RCTBlockBRow; c: RCTBridge }
```
Headline both **incl.** (gross) and **excl.** (net) (A27). Restaurant / counter / hall rows sit under the room rows on the same page (hall row = 0 until F6).

### 2.2 Sources (no new money logic — view over existing selectors)

- Block A from the **same selector as `rpt.money.revenue-tax`**, grouped by the stay's room type (posting-date basis) — a line on a folio belongs to the room type of the stay **on that night**; POS-to-folio lines stay in the room row ("other room charges"); counter bills (LF A7) → `counter`; restaurant cash sales → `restaurant`.
- Block B from the **same selectors as `rpt.money.daily-collections` / `collection-detail` / `source-rows`**: tender columns from the payment-methods master (cash / UPI / card / bank fixed; any other method in `otherTenders`), receipt kind deposit → advances received, deposit-apply → advances adjusted, deposit-return → advances refunded. An advance before check-in takes the **booked** room type.
- Block C: opening / closing from the accounting stream (`accounting-reports.ts` trial balance) for ledgers 1000, 1010, 1020, bank ledgers, 1110, 1120, 2000, 4000-series, GST output.

### 2.3 Tie-out rules (tests, both suites)

1. `aTotal.net`, `.cgst + .sgst`, `.gross` = `revenue-tax` totals for the range (to the paisa).
2. `bTotal` per tender = Σ `daily-collections` per tender over the days of the range; `advancesReceived / Adjusted / Refunded` = the deposit metrics of the closing record.
3. Block C: `openingDue + gross − netReceived + (closingAdvances − openingAdvances) = closingDue` (signs: advances held are a liability; `advanceMovement = advancesReceived − advancesAdjusted − advancesRefunded`) with `difference = 0`; on the ACC-RULEBOOK golden month (April 2026) the opening / closing figures equal the TB ledgers listed in §2.2.
4. A legacy FY renders with header "Legacy data (old Lodge software)" and equals the LG contract §6 bill and receipt totals; category from the rooms master; an unmapped room → row `unassigned` (never dropped).
5. Excel / CSV export = screen figures.

Server: `Hcc.Application/Reports/RevenueByCategoryQueries.cs` + `GET /api/reports/revenue-by-category-tender?from&to&basis`; migrations **4370–4374** (4370 report catalogue row + PERM-RPT-002 mapping, 4371 supporting index on posting date / room type if needed, 4372–4374 reserve). Tests `stream-rpt-revcat.mjs`, `Hcc.Reports.Tests/RevenueByCategoryTests.cs` (put in the nearest existing reports test project if `Hcc.Reports.Tests` does not exist — no new project).

---

## 3. Owner digest — lane T2

Selector `dgsCard(s, date)` in `src/domain/streams/digest.ts`; "Yesterday" card on the Dashboard (Owner only — `features/overview/Overview.tsx` F4-T2 block) and page `owner-digest` (category `accounts`, Owner only; printable). Content (for business date D = yesterday by default, any closed date selectable):

1. Occupancy % and rooms sold / available; ADR; RevPAR.
2. Revenue by category (Block A rows of §2 for D: net + gross per room type, restaurant, counter, hall).
3. Collections by tender (Block B total for D).
4. Advances received / adjusted / held at close.
5. Expenses of D (approved + pending count).
6. Cash in drawer at close (closing record).
7. Pending approvals (count by kind).
8. Reorder alerts (`rpt.stock.reorder` items below level — T3 supplies `qwReorderAlerts(s)`; T2 reads it, stub until merged).
9. Complaints / L&F over SLA and 7-day unresolved (T3 supplies `fbkOverdue(s, date)`; same stub rule).
10. Feedback of D: count and average stars (T3 `fbkSummary(s, date)`).
11. Tomorrow's arrivals (count, rooms) and events (hall events after F6: 0 now).

**Held outbox draft.** At day close (`CLOSE_DAY` / night audit, one line in the T2 block of the closing handler, or a post-close hook) the stream writes **two drafts** for D into `s.digestX.drafts`: `{channel:'Email', body: full content (money), audience: Owner + Accountant}` and `{channel:'WhatsApp', countsOnly: true, body: items 1 (counts only, no %?) — occupancy rooms count, arrivals count, pending approvals count, reorder count, complaints count; **no names, no money**}` (DEC-202 #7). `status:'held'` always; reason "Delivery waits for the messaging gateway (F9) and the internet switch (F5)". Nothing reads them to send. Idempotent per (date, channel): re-closing replaces the held draft. Server: `digest.draft` table (migrations **4375–4379**: 4375 table, 4376 RBAC PERM-DGS-001 Owner, 4377–4379 reserve); the existing server outbox is **not** used for these (it would deliver); the F9 adapter will move held drafts into it.

Types: `DGSChannel='Email'|'WhatsApp'`; `DGSDraft={id;date;channel;countsOnly:boolean;status:'held';reason;body:string;createdAt;by}`; `DGSState={drafts:DGSDraft[]}` (`s.digestX`). Permission PERM-DGS-001 (view digest / drafts) = Owner only.

---

## 4. Quick wins — lane T3 (money-free)

- **qw-audit (4390–4392):** Audit log page filters actor / entity type / entity id / date range; diff view (before / after JSON, changed keys highlighted) for masters and settings audit rows; CSV via `rpt.admin.audit-log` exporter. Permissions unchanged (existing audit permission). Housekeeping never sees money fields in a diff (DEC-061: money keys masked with the existing mask). 4390 index on audit (at, actor), 4391–4392 reserve.
- **qw-feedback (4393–4396):** at check-out a feedback sheet (1–5 stars + comment, tablet-friendly, optional, never blocks check-out) → `FBK_RECORD`; stars ≤ 2 or a comment with a category → a complaint in module 18 (existing categories, DEC-MA-7c). Bill / invoice print gets one text line "Tell us how we did: <feedback URL text from settings>" (QR text only, no image library). Complaint / L&F unresolved 7 days → digest line + an "Owner follow-up call" task (existing task stream, assignee Owner). Types `FBKEntry={id;booking;stars:1|2|3|4|5;comment?;complaintId?;at;by}`, `FBKFollowUp={id;source:'complaint'|'lostfound';ref;since;taskId?;status:'open'|'done'}`, `FBKState={entries;followUps}` (`s.feedbackX`). Command `FBK_RECORD` (Reception, Manager, Owner, Cashier at check-out), `FBK_FOLLOWUP_SWEEP` (system, at day close). Selectors `fbkSummary`, `fbkOverdue` exported for T2. Server table `gsv.feedback` 4393, follow-up 4394, RBAC 4395, reserve 4396.
- **qw-alerts (4397–4399):** `qwReorderAlerts(s)` over the existing reorder selector (for the digest + a Stock page banner); `app/LanHealth.tsx` banner "Server unreachable — changes are kept on this tablet" (reads the existing sync status; demo = always reachable); printer settings: "Print test page" per printer + offline banner; System health gets a "Printers" block (`syshealth.ts` F4-T3 block). 4397 printer last-test columns, 4398–4399 reserve.

---

## 5. Tests (new files; counts are GUESS)

| Lane | Prototype | Server |
|---|---|---|
| T1 | `stream-tariffcard.mjs` (round-trip 5 % / 18 %, Deluxe golden, 22 Sep 2025 dated change, publish → new booking takes new rate & earlier unchanged, weekend pair → two rows + resolution, blank child no write, Manager propose → Owner approve, Housekeeping FORBIDDEN, past effective refused, published read-only, DEC-198 import reproduces the card), `stream-tariffcard-ui.mjs`, `stream-rate-sheet.mjs` (print = screen) | `Hcc.Masters.Tests/TariffCardTests.cs` (same cases + one transaction + audit rows) |
| T2 | `stream-rpt-revcat.mjs` (§2.3 rules 1–5, Reception FORBIDDEN), `stream-digest.mjs` (Owner only, counts-only WhatsApp has no ₹ and no guest names, held status, idempotent per date) | `RevenueByCategoryTests.cs`, digest draft tests |
| T3 | `stream-audit-viewer.mjs`, `stream-feedback.mjs`, `stream-qw-alerts.mjs` | feedback store tests |

Every new nav page is listed in `PAGE_CATEGORY` (navmap test) and gets blurb + keywords.

---

## 6. Open questions (defaults applied — build on the default, record in DONE)

| # | Question | Default |
|---|---|---|
| Q1 | Child price (A25) | blank row, publishes nothing |
| Q2 | Weekend days | Fri / Sat / Sun nights |
| Q3 | Seasonal cards | one yearly card + dated seasonal versions (each a version with its own effectiveFrom) |
| Q4 | Manager publish | no — propose only |
| Q5 | Day-use on the card | yes (print; master write only if the yield day-use plan exists) |
| Q6 | Card language | English |
| Q7 | Revenue headline | both incl. and excl. |
| Q8 | Restaurant / hall rows | same page |
| Q9 | Advances basis | both received and adjusted |
| Q10 | Digest | yes; e-mail money (Owner + Accountant), WhatsApp counts only |
| Q11 | Room GST 18 % (DEC-198) | CA to confirm; card reads the dated master |
| Q12 | Feedback URL text on the bill | blank until the owner sets it (line hidden when blank) |
