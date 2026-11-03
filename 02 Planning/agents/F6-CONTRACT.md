# F6 contract — Hall booking (module 27), quotation + T&C

Branch `claude/f6` (from main e14c0ead, F1–F5). Spec: `02 Planning/modules/HCC_Module_27_Hall_Booking_Spec.md` (owner approval OP-17 required before lanes start). Plan: Wave Plan F1–Fn §F6, §1.4, Appendix D A26.
Rules from CLAUDE.md apply: all mutations via `execute`, money = integer paise, `s.clock` only, copy-on-write slices, no new npm packages, colours as tokens, `migrations-lock.mjs --add` for every migration.

## 1. Schema
- **SCHEMA 13 → 14** (setup commit by H1). New optional slice `s.hallX?: HallState`; `Booking.hallEventId?: string` (room block linked to an event).
- `restoreState` treats a missing `hallX` as the seed from `fixtures/ext/hall-seed.ts`.

## 2. Types (`src/domain/streams/hall.ts`)
```ts
type HallSlot = 'MORNING' | 'AFTERNOON' | 'EVENING' | 'FULL_DAY';
type HallSetup = 'THEATRE' | 'ROUND' | 'USHAPE' | 'FLOATING' | 'CLASSROOM';
type HallEventType = 'WEDDING'|'RECEPTION'|'BIRTHDAY'|'ENGAGEMENT'|'CORPORATE'|'CONFERENCE'|'RELIGIOUS'|'OTHER';
interface Venue { id; name; kind: 'HALL'|'LAWN'|'TERRACE'; capacity: Partial<Record<HallSetup, number>>;
  slots: { slot: HallSlot; from: string; to: string; ratePaise: number }[]; minPlates?: number;
  gstClass: string; combinedOf?: string[]; active: boolean; version: number }
interface Enquiry { id; customerId?; name; phone; partyId?; eventType: HallEventType; dates: string[]; slot: HallSlot;
  pax: number; venueId?; source: 'WALKIN'|'PHONE'|'REFERENCE'|'WEBSITE'|'REPEAT'; followUp: string; note?;
  status: 'OPEN'|'QUOTED'|'WON'|'LOST'; lostReasonId?; lostNote?; softHold?: { venueId; until: string; by: string } }
interface Quotation { id; number: string /* QT/2627/0001 */; fy: string; enquiryId; versions: QuoteVersion[]; currentVersion: number }
interface QuoteVersion { v: number; status: 'DRAFT'|'SENT'|'SUPERSEDED'|'EXPIRED'|'ACCEPTED'|'LOST'|'CANCELLED';
  header: { eventType; dates; slot; venueId; setup: HallSetup; pax; guaranteedPax };
  lines: QuoteLine[]; advance: AdvanceRow[]; tnc: TncSnapshot; validUntil: string;
  createdBy; createdAt; sentAt?; placeholderOverride?: { by; reason; at }; approvalId? }
interface QuoteLine { id; kind: 'HALL'|'MENU'|'EXTRA'|'ROOM'|'OTHER'; refId?; desc; qty: number; unit: string;
  ratePaise: number; listRatePaise: number; discountPaise: number; gstClass: string; taxBps?: number /* set at invoice */ }
interface AdvanceRow { id; label; pctBps?: number; amountPaise?: number; due: string | 'ON_ACCEPT' | 'EVENT_DAY'; paidPaise: number; receiptIds: string[] }
interface TncTemplate { id; eventType: HallEventType | 'GENERAL'; versions: { v; effectiveFrom; clauses: { n: number; heading; text }[] }[] }
interface TncSnapshot { templateId; v; clauses: { n; heading; text }[]; hasPlaceholder: boolean }
interface MenuPackage { id; name; perPlatePaise: number; posItemIds: string[]; veg: boolean; active: boolean }
interface HallEvent { id; quotationId; version: number; venueIds: string[]; date: string; slot: HallSlot; status: 'CONFIRMED'|'BILLED'|'CANCELLED';
  lines: QuoteLine[]; actualPax?; extras: QuoteLine[]; blockId?; invoiceId?; cancellation?: { at; keptPaise; refundPaise; by } }
interface HallState { venues; enquiries; quotations; templates; menus; events; lostReasons: { id; label; needsNote }[]; settings: HallSettings }
interface HallSettings { validityDays: 15; expiryReminderDays: 2; advanceDefault: [2500, 5000, 'BALANCE']; secondDueDays: 30;
  discountLimitBps: { RECEPTION: 0; MANAGER: 1000; OWNER: null }; refundManagerLimitPaise: 5_000_000;
  cancelSlabs: { minDays: number; keptBps: number }[]; paxReduceFreeBps: 1000; enquiryAutoLostDays: 30 }
```
Placeholder detection: clause text contains `OWNER TO REPLACE`.

## 3. Commands (`execute` cmd types; idempotency key on each)
| Command | Perm | Lane | Notes |
|---|---|---|---|
| `HALL_VENUE_UPSERT` | PERM-HAL-006 | H1 | master; versioned |
| `HALL_MENU_UPSERT` | PERM-HAL-006 (Manager allowed) | H1 | POS item ids must exist |
| `HALL_ENQUIRY_ADD` / `_UPDATE` / `_LOST` / `_SOFT_HOLD` | 001 (soft hold: Manager+) | H1 | lost reason required |
| `QUO_CREATE` | 002 | H2 | next `QUO` number from number series master |
| `QUO_EDIT` (draft only) / `QUO_REVISE` | 002 | H2 | revise copies to v+1, old → SUPERSEDED |
| `QUO_SEND` | 002 | H2 | `HCC_TNC_PLACEHOLDER` unless override; locks version; outbox held |
| `QUO_PLACEHOLDER_OVERRIDE` | 005 (Owner) | H2 | reason required, logged |
| `QUO_ACCEPT` | 003 | H1 (+H2 money) | creates event, hard hold, optional block, optional first advance |
| `QUO_LOST` / `QUO_CANCEL` / `QUO_EXPIRE` (night run) | 002 / system | H2 | |
| `HALL_TNC_TEMPLATE_SAVE` | 005 (Owner only) | H2 | new version; never mutates snapshots |
| `HALL_ADVANCE_RECEIVE` | 003 (Cashier allowed) | H1 billing | Dr tender Cr 2000, no GST |
| `HALL_EVENT_CLOSE` | 004 | H1 billing | bill = max(guaranteed, actual); `HPE` invoice; advance applied |
| `HALL_EVENT_CANCEL` / `HALL_REFUND` | 004 / approve | H1 billing | slabs; kept → 4300 with GST |
Errors (new `HccError` codes): `HCC_HALL_CLASH`, `HCC_TNC_PLACEHOLDER`, `HCC_QUO_NOT_LATEST`, `HCC_QUO_EXPIRED`, `HCC_DISCOUNT_LIMIT` (→ approval), `HCC_HALL_CAPACITY` (warn/override), `HCC_TNC_OWNER_ONLY`.

## 4. Permissions
| Id | Meaning | Roles |
|---|---|---|
| PERM-HAL-001 | Enquiries | Owner, Manager, Reception |
| PERM-HAL-002 | Quotations create / revise / send | Owner, Manager, Reception (Accountant view) |
| PERM-HAL-003 | Accept + advances | Owner, Manager, Reception; Cashier advances only |
| PERM-HAL-004 | Event bill, cancel, refund | Owner, Manager, Reception (cancel = request); Cashier settle |
| PERM-HAL-005 | T&C templates + placeholder override | Owner |
| PERM-HAL-006 | Halls master, menus | Owner (menus also Manager) |
Calendar / function-sheet read without prices: all staff roles; HK / maintenance never receive `*Paise` fields (DEC-061; server DTO strips them).

## 5. Money rules (H1 `hall-billing.ts` + `Hcc.Domain/Hall/HallBilling.cs`, one truth table)
- Line taxable = qty × rate − discount (paise). Tax per line: rate from dated GST master by `gstClass` at invoice date; CGST = SGST = half, half away from zero (`src/domain/gst.ts`).
- Discount % = Σ(list − charged) / Σ list (pre-GST). Above role limit → approval request, no self-approval.
- Advance rows: % of grand total of the accepted version, last row = balance; rounding difference on the balance row.
- Accounting sources in own `accLiveVouchers` section `hall:*`: `hall:advance` (Dr tender Cr 2000), `hall:invoice` (Dr 1100/1110 Cr 4400 + 4200 for MENU lines, Cr 2110/2120), `hall:advance-apply` (Dr 2000 Cr 1100), `hall:cancel` (Dr 2000 Cr 4300 + GST at principal rate), `hall:refund` (Dr 2000 Cr tender). New ledger **4400 Banquet & events revenue** (Sales Accounts). Mapping switch `hall.foodLedger` = 4200 (default) | 4400.
- Invoice number from the existing `HPE` series (DEC-197); credit notes as for rooms.

## 6. Server
Tables (schema `hall`): `venue`, `venue_slot`, `enquiry`, `quotation`, `quotation_version`, `quotation_line`, `quotation_advance`, `quotation_tnc_snapshot`, `tnc_template`, `tnc_template_version`, `tnc_clause`, `menu_package`, `menu_package_item`, `event`, `event_line`, `calendar_hold` (unique filtered index on venue + date + slot for hard holds), `lost_reason`, `print_log`, `settings`.
Endpoints `/api/hall/...`: `GET venues|menus|templates|lost-reasons`, `GET calendar?from&to&venue`, `GET enquiries`, `POST enquiries`, `GET quotations?status`, `GET quotations/{id}`, `POST quotations`, `POST quotations/{id}/revise|send|accept|lost|cancel|override`, `POST templates`, `POST events/{id}/advance|close|cancel|refund`, `GET reports/register|funnel|events|occupancy|advances-due`. Command handlers in `Hcc.Application/Hall`, store `SqlHallStore`, rules only via `HallRules` / `HallBilling`. New test project `Hcc.Hall.Tests` (+ `Hcc.slnx`).

## 7. Migrations 4300–4349
| Range | Content | Lane |
|---|---|---|
| 4300–4319 | `hall` schema: venue, slots, enquiry, quotation header/version/line, event, calendar_hold | H1 |
| 4320–4329 | advance rows, event bill link, accounting source map, ledger 4400 | H1 |
| 4330–4344 | `QUO` number series, T&C templates/versions/clauses, snapshot, menu packages, lost reasons | H2 |
| 4345–4347 | RBAC seed PERM-HAL-001…006, indexes | H3 (server endpoints) |
| 4348 | print log | H3 |
| 4349 | seed: 2 halls + 1 lawn (fictional), templates with `OWNER TO REPLACE`, lost reasons, settings | H2 |

## 8. Tests
| File | Lane | Covers |
|---|---|---|
| `tests/stream-hall.mjs` | H1 | status machine, clash refused (incl. combined hall), soft hold, enquiry auto-lost, expiry + reminder |
| `tests/stream-hall-billing.mjs` + `Hcc.Hall.Tests/BillingTests.cs` | H1 | **HB-GOLDEN** (spec §21), discount limits/approval, advance plan rounding, cancel slab 40 days → 4300, refund, DEC-142 no GST on advance |
| `tests/stream-hall-quote.mjs` | H2 | numbering QT/2627/0001 + FY reset, revise supersedes, placeholder guard + Owner override, snapshot unchanged after template edit, Reception cannot edit template |
| `tests/stream-hall-seed.mjs` | H2 | seed shape, CSV templates `halls.csv`, `tnc-templates.csv` round-trip |
| `tests/stream-hall-ui.mjs`, `stream-hall-print.mjs`, `check-phone-hall.mjs`, fit-check | H3 | navmap PAGE_CATEGORY, roles, print totals = screen, DEC-061 walk, 360 px |
| `Hcc.Hall.Tests/StoreTests.cs`, endpoint tests, `Hcc.Integration.Tests/HallGoldenTests.cs` | H3 | store, RBAC, golden on server |
Expected deltas (guess): prototype +≈120, server +≈90, +1 project.
