# HCC Module 13 · Advanced tariff & yield — prototype spec (v1)

**Purpose.** Give the Owner a transparent, rule-based way to set nightly room tariffs for the next 14 days: rate plans, seasons, day-of-week and occupancy-based yield rules, stay restrictions, and a "Publish" step that writes effective-dated tariffs into `s.tariffs` (the source that `rateFor()` already uses for new bookings). Every suggested rate shows *why* it is what it is. Existing bookings keep their agreed rates — publishing never re-prices a booking or a posted line.

**Page.** `yield` (nav: REVENUE & SALES → Tariff & yield). Prefix `yld` (types `YLD…`, commands `YLD_*`, CSS `yld-`, modals `yld-`). State slice `s.yieldX` (`YLDState`, absent = fixture defaults).

## Roles (coarse permissions only)
| Role | View (`reports`) | Change rules / publish (`admin` + `reports`) | Propose publish (`approve`) | Decide proposal |
|---|---|---|---|---|
| Owner | ✓ | ✓ | ✓ (not needed) | ✓ (not own) |
| Manager | ✓ | — | ✓ → approval request | — |
| Accountant, Cashier | ✓ (read-only) | — | — | — |
| Tech admin | — (no `reports`; technical and financial power stay separate) | — | — | — |
| Reception, Supervisor, Housekeeping, Maintenance | page hidden | — | — | — |

Manager proposals use the existing approvals mechanism (`s.approvals`, `kind:'yield'`, `meta.command:'YLD_DECIDE'`), so they also appear in Approvals & audit. Only an Owner (admin) can decide; self-approval is blocked; requests expire after the configured approval hours; a request is **stale** if the tariffs for its room types changed after it was raised.

## Rules (pure, in `domain/streams/yield.ts`)
1. **Base rate** for a room type and night = the latest *manual* tariff (Hotel configuration schedule) with `effectiveFrom ≤ night`, else the room-type rack rate. Tariff rows written by this module are tracked (`owned`) and excluded, so yield never compounds on itself.
2. **Adjustments are additive percentages** on the base: season (highest-priority matching season, optional room-type filter) + day-of-week (Sun…Sat) + occupancy tier (highest `≥` tier met; lowest `<` tier met).
3. **Forecast occupancy** = max(rooms on the books, simulated forecast rooms) ÷ sellable rooms (not blocked that night). On the books = Confirmed / In house / live Held stays covering the night. The forecast curve is **simulated** fixture data.
4. **Guardrails**: total adjustment clamped to [floor %, ceiling %] (default −20 % / +40 %), then rounded to the nearest ₹50. A **manual override** (Owner, with reason) replaces the computed rate for one type/night.
5. **Rate plans** derive from the suggested BAR of the night: BAR (0 %), Corporate (−12 %), Weekend (−5 %, Fri–Sun nights, min 2), Long-stay (−15 %, 7+ nights), **Day-use** (−45 %, 10:00–17:00, no overnight; closed when forecast ≥ 90 %). Plans can be activated/deactivated and their % / min nights edited (BAR is fixed at 0 %). Only BAR is published to `s.tariffs`; the other plans are a rate sheet + quote tool.
6. **Restrictions** per room type (or all) and arrival-date range: min-stay N nights, closed-to-arrival (CTA). `yldCheckStay()` returns violations for a quote; front-desk enforcement in BOOK is a shared follow-up (advisory in this prototype).
7. **Publish** (from…to within the 14-day window, chosen types): for each night writes the suggested rate as an effective-dated tariff only where the live rate differs, and adds a closing row on `to + 1` so the rate after the window is unchanged. Nights before the business date (or the business date once closed) cannot be published. Bookings, posted lines and agreed rates are untouched.
8. **History**: every rule change (before → after, actor, simulated time, reason) and every publish (rows before → after, approval link) is kept in the slice.

## Commands (handlers in `services/mock/handlers/yield.ts`)
`YLD_PLAN_SAVE`, `YLD_SEASON_SAVE` (create/update/`remove`), `YLD_DOW_SET`, `YLD_OCC_SAVE` (create/update/`remove`), `YLD_GUARD_SET`, `YLD_RESTRICTION_SAVE` (create/update/`remove`), `YLD_OVERRIDE` (set/clear), `YLD_PUBLISH` — Owner. `YLD_PROPOSE` — `approve` holders (Manager). `YLD_DECIDE {id,approve}` — Owner, not own request. Idempotency keys via `execute`.

## Screens
- **Desktop**: title + Publish/Propose action; stats (avg BAR next 7 nights, high-demand nights, nights awaiting publish, pending proposals); tabs **Rate calendar** (room type × 14 nights: suggested vs live rate, Δ, occupancy row, restriction badges; click → night breakdown dialog: base → each adjustment → guardrail → suggested, plan rates, restrictions, override), **Rate plans** (plan table + edit; rate sheet; quote / check-stay tool incl. day-use), **Rules** (seasons, day-of-week, occupancy tiers, guardrails), **Restrictions**, **History** (rule changes + publish log). Pending proposals panel with Approve / Reject.
- **Phone**: KPI cards, room-type chips, 7-day strip (Next 7 / Following 7), selected-night card with the step-by-step explanation and plan chips, cards for proposals, rules and history; one primary action in a sticky footer (Publish / Propose). Dialogs render as sheets.

## Acceptance tests (tests/stream-yield.mjs)
Fixture defaults & explanation figures; season + weekday + occupancy tiers; guardrail clamp & ₹50 rounding; override; owner-only rule edits (Manager/Accountant/Tech admin denied); publish writes `s.tariffs`, new BOOK uses the yield rate, existing bookings and posted lines unchanged, rate after the window unchanged; idempotent publish; no past nights; Manager propose → Owner approve publishes; self-approval blocked; stale proposal rejected; restriction violations & day-use closure in quotes; validation errors.

## Simulated / out of scope
Forecast occupancy curve, competitor/market data (none), channel push of rates (none — Channels module), automatic re-pricing (Owner publishes explicitly), restriction enforcement inside BOOK (advisory helper only).
