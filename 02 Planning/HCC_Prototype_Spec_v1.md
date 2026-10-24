# Hotel Control Centre — Prototype Specification v1

Version: 1.0 (M0, for approval) · 24 October 2026
Related: `HCC_Master_PRD_v1.md` (REQ IDs) · `HCC_RBAC_Matrix_v1.md` · `HCC_Business_Workflow_Spec_v1.md` · `HCC_Fixture_and_Scenario_Catalog.md`

Everything in this document is **Proposed** until APR-003 is recorded.

---

## 1. Sitemap

Grouped task-based navigation. A section appears only if the selected user has at least one permitted screen in it. Deferred modules (areas 13–26) have **no** enabled navigation; they are listed on the Roadmap page only.

```text
Hotel Control Centre
├── Overview                         SCR-01 Dashboard
├── Front Desk
│   ├── Room board                   SCR-02
│   ├── Calendar                     SCR-03
│   ├── Reservations                 SCR-04  (+ booking panel)
│   └── Arrivals / check-in          SCR-06
├── Guests & Stays
│   ├── Guests                       SCR-05 Guest profile
│   └── In-house stays               SCR-07  (move, extend, checkout → SCR-10)
├── Cash & Billing
│   ├── Folios                       SCR-08  (+ SCR-09 payment dialog)
│   └── Cashier shift                SCR-11
├── Housekeeping                     SCR-12  (board / my tasks / inspection)
├── Expenses                         SCR-13
├── Closing & Reports
│   ├── Day closing                  SCR-14
│   └── Reports                      SCR-15
├── Administration
│   ├── Access control               SCR-16
│   ├── Approvals & audit            SCR-17
│   ├── Hotel configuration          SCR-18
│   ├── Backup & recovery (simulated) SCR-19
│   └── Roadmap (areas 13–26, read-only)
└── Demo toolbar (separate strip)    SCR-20  scenario · user preview · reset · fault simulation · guide
```

**Change from current build:** the build has a flat 10-item menu (Overview, Room board, Reservations, Guests & folios, Housekeeping, Expenses, Day closing, Reports, Access control, Hotel settings). Proposed additions: Calendar, Arrivals/check-in, Guest profile, In-house stays, Cashier shift, Approvals & audit, Backup, Roadmap, plus grouping.

### 1.1 Default landing page per role

| Role | Lands on | Visible sections |
|---|---|---|
| Owner | Overview | All |
| Manager | Overview | All except Access-control editing (view only) |
| Reception | Room board | Front Desk, Guests & Stays, Folios (operational), Expenses (submit) |
| Cashier | Cashier shift | Folios, Cashier, Expenses, Reports (cash) |
| Accountant | Reports | Reports, Folios (read), Expenses, Day closing (read) |
| Housekeeping supervisor | Housekeeping (board) | Housekeeping, Room board (read, no guest finance) |
| Housekeeper | Housekeeping (my tasks) | Housekeeping only |
| Maintenance | Housekeeping (repairs filter) | Room board (blocks), repair tasks |
| Technical administrator | Access control | Administration only |

## 2. Persistent shell

**Header (always visible):** property name · current user + role · simulated business date (e.g. "Business date 24 Oct 2026 · Open") · connectivity mode chip (Online / Simulated offline) · fixed label **"Demo — fictional data"**.

**Demo toolbar (SCR-20)** — visually distinct strip, never mixed with staff actions:

| Control | Behaviour |
|---|---|
| Scenario selector | Loads S0–S6 from the fixture catalogue; confirms before replacing state |
| User preview | Switch demo user; shows that this is *not* authentication |
| Reset | Restores the selected scenario to its seed; confirmation dialog |
| Fault simulation | Toggle per next command: latency 2 s · rejection · lost response · stale version |
| Guided walkthrough | Opens the 12-step owner walkthrough with "go to step" links |
| Simulated offline | Disables mutations with a clear banner; read-only views show data age |

## 3. Screen contracts

Common rules for every screen:

- Loading, empty, error and success states are defined. A success toast appears only after the state has actually changed.
- Every visible button works, navigates, or is disabled with a visible reason ("Requires approval permission").
- Validation messages sit next to their field and are announced to assistive technology.
- Dialogs trap focus and return it to the triggering control on close.
- Money shown as INR with Indian grouping (₹1,23,456.00); dates as "24 Oct 2026".

### SCR-01 Dashboard (REQ-RPT-001/002)

| Element | Content |
|---|---|
| Cards | Occupied / sellable rooms, occupancy %, arrivals, departures, HK pending, blocked rooms, outstanding balances, deposits held, today's receipts, today's expenses, expected drawer cash |
| Lists | Today's arrivals and departures (tabs), exceptions (pending approvals, unpaid departures, dirty rooms with arrivals) |
| Actions | Every card drills down to the filtered source list |
| Roles | Owner, Manager full; Cashier sees cash cards only; financial cards hidden for HK roles |
| States | Empty hotel (S1), normal day (S0), exceptions (S3), restricted financial cards |
| Mobile | Cards stack 1 column; exceptions first |
| Current build | Built (cards derived via `metrics()`); drill-down to verify |

### SCR-02 Room board (REQ-RACK-001..003, REQ-ROOM-004)

| Element | Content |
|---|---|
| Grid | 24 tiles by floor; each tile shows room no., type, occupancy text, cleaning text + icon, block badge, guest initials (if permitted), next arrival |
| Filters | Floor, room type, status (Vacant ready, Vacant dirty, Occupied, Reserved today, Blocked), search |
| Room panel | Current guest/stay link, next reservation, HK task state, block reason; actions: New booking, Check in, Open folio, Block/Unblock, Raise repair |
| States | Vacant dirty, ready, occupied, reserved next, out of service, cleaning, awaiting inspection |
| Mobile | 2-column tiles; panel opens as bottom sheet |
| Current build | Built (tile grid + room modal); block lacks reason/dates |

### SCR-03 Calendar (REQ-RACK-004, REQ-RACK-005)

| Element | Content |
|---|---|
| Grid | Rows = rooms grouped by type; columns = 14 days from a chosen date; bars = stays/holds/blocks with status colour + label |
| Controls | Previous/next week, today (business date), date picker, room-type filter |
| Actions | Click empty cell → new booking prefilled; click bar → booking panel; "Move / change dates" dialog (non-drag) |
| States | No availability, overlapping move rejected, stale selection (record changed since opened) |
| Mobile | Horizontal scroll within grid only; page itself never scrolls sideways |
| Current build | **Missing** |

### SCR-04 Reservations (REQ-BOOK-001..008)

| Element | Content |
|---|---|
| List | ID, guest, dates, nights, room, type, status, source, advance, balance; filter by status/date/source; search |
| Booking panel fields | Guest name* (≥2 chars), phone*, email, adults* (1–max for type), children, arrival* (≥ business date), departure* (> arrival, ≤30 nights), room type*, room (optional → auto-assign), rate (from tariff, override needs permission), source*, OTA channel name (if OTA), company (optional), notes, advance amount + tender |
| Actions | Check availability · Hold (expiry shown) · Confirm · Save changes · Cancel (reason) · Mark no-show · Print confirmation |
| States | Hold expiring/expired, cancellation with money held (must refund/forfeit first), no-show, invalid dates, capacity exceeded, room unavailable |
| Current build | Partial — create/edit/move/cancel/no-show present; Hold, room-type search, phone, company, children missing |

### SCR-05 Guest profile (REQ-GUEST-001..007)

| Element | Content |
|---|---|
| Fields | Name*, phone*, email, address, nationality, ID type, ID number (masked: last 4 visible), company + GSTIN (fictional format), preferences, notes |
| Party | Accompanying guests (name, age band, relation) |
| History | Past and future stays with folio totals (if permitted) |
| Actions | Save · Print registration card · Mark verified |
| States | Duplicate suggestion banner ("Possible match: G-0007, same phone"), missing required data, masked fields for roles without `guest.id.view` |
| Current build | **Missing** (only a `registered` flag exists) |

### SCR-06 Arrivals / check-in (REQ-STAY-001)

Steps (single page with progress): 1 Find reservation or walk-in → 2 Verify guest (links SCR-05) → 3 Confirm room eligibility (vacant + ready + unblocked) or pick an alternative → 4 Confirm tariff → 5 Advance/deposit (optional, SCR-09) → 6 Check in.

States: dirty/blocked room (denied with reason and alternative rooms), disabled user, expired hold, unauthorised rate override (approval request).
Current build: Partial (check-in via booking modal; no stepper, no walk-in path).

### SCR-07 In-house stay (REQ-STAY-002/003/008)

Occupants, room segments history (e.g. "101: 23–24 Oct → 105: 24–26 Oct"), dates, folio summary, actions Move room, Extend/shorten, Add charge, Checkout.
States: future room conflict (shows alternatives), pricing change on extension (agreed rate kept; new nights use tariff unless overridden), stale update.
Current build: Partial (move/extend inside booking modal; no segment history).

### SCR-08 Folio (REQ-FOLIO-001..007)

| Section | Content |
|---|---|
| Charges | Date, description, kind (room/service/discount/adjustment), amount, tax rate snapshot, tax, source ID |
| Receipts | Date, tender, reference, kind (advance/payment/deposit/deposit return/refund), cashier |
| Summary | Net charges, tax, total, receipts applied, **balance**, deposit held (separate box) |
| Actions | Add service charge · Request discount · Record payment · Record deposit · Return deposit · Request refund · Issue sample invoice · Request correction |
| States | Duplicate charge rejected, discount over limit → "Pending approval APR-xxxx", refund request, posted reversal, closed-date line (read-only) |
| Current build | Built (lines, receipts, totals); correction/credit note missing |

### SCR-09 Payment dialog (REQ-PAY-001/002/006)

Fields: amount*, tender* (Cash/UPI/Card/Bank transfer/Company credit), reference (required for non-cash), allocation (this folio), kind. Supports multiple tender rows summing to the amount. Confirmation summary before posting, with the note "Manual entry — bank/UPI transaction not verified."
States: partial payment, mixed tenders, missing reference, closed shift, retry after simulated lost response (shows "Checking previous attempt…" then the original receipt).
Current build: Partial (single tender).

### SCR-10 Checkout (REQ-STAY-004..007)

Preview: posted nights vs agreed nights, unposted items, balance, deposit held, pending approvals. Actions: Settle (SCR-09) · Return deposit · Apply approved company credit · Confirm checkout · Print sample invoice.
States: unpaid balance (blocked with reason), duplicate checkout (returns existing result), failed step (simulated — state unchanged, retry offered), correction request after departure.
Current build: Built (blocking rules); company credit missing.

### SCR-11 Cashier shift (REQ-PAY-004/005, REQ-EXP-003)

Opening float, live list of cash movements (receipts, refunds, cash expenses, transfers), expected cash formula visible, count entry (total; optional denominations), difference, reason, submit → approval if difference ≠ 0 → close.
States: wrong shift user, unapproved difference, already closed.
Current build: Partial (cash count modal on Day closing; strict zero-difference only).

### SCR-12 Housekeeping (REQ-HK-001..006)

| View | Content |
|---|---|
| Board (supervisor) | Columns To do · In progress · Awaiting inspection · Done; card = room, type, reason (checkout/stayover), assignee, priority (arrival today), age |
| My tasks (housekeeper) | Own cards only, big Start / checklist / Complete buttons |
| Inspection | Pass → Ready; Reject → reason required → back to In progress |
| Actions | Assign/reassign, start, tick checklist (3 items in fixture), complete, inspect, reject, report repair (creates block request) |
| States | Wrong assignee (denied), incomplete checklist, maintenance block (stays blocked after inspection), failed inspection |
| Current build | Built; reject reason and repair request missing |

### SCR-13 Expenses (REQ-EXP-001..005)

List with status tabs (Draft, Pending, Posted, Rejected, Reversed); form: vendor*, category*, date, amount*, funding source* (Drawer cash / UPI / Vendor credit), receipt placeholder, notes. Actions: Save draft, Submit, Approve, Reject (reason), Post, Reverse (reason).
Current build: Partial (submit/approve/reject; no draft/reverse/attachment).

### SCR-14 Day closing (REQ-CLOSE-001..006)

Step list: 1 Preflight (blocking issues with links) → 2 Post room nights (count to be posted, preview) → 3 Cash shift close (SCR-11) → 4 Close business date → 5 Closing summary → Open next day.
Progress persists; a simulated interruption at step 2 resumes without reposting.
States: blocking issue, already posted (0 new lines), already closed, correction request.
Current build: Built (preflight, posting, shift, close, next day); resume/correction UI missing.

### SCR-15 Reports (REQ-RPT-001..006)

Tabs: Daily operations · Revenue & tax · Receipts & deposits · Expenses & cash · Occupancy (ADR/RevPAR) · Report dictionary. Filters: business date range, tender, source. Every total links to source rows. Export CSV; Print/PDF (watermarked "Demonstration").
States: no records, restricted scope (masked guest names), synthetic history comparison.
Current build: Partial.

### SCR-16 Access control (REQ-ACC-001..009)

| Tab | Content |
|---|---|
| Users | List; create/edit; enable/disable; role assignments with expiry; scope (property/shift/assigned) |
| Role templates | Matrix: resources × actions with Allow / Deny / Inherit; limits (discount ₹, refund ₹); field restrictions |
| Effective access | Pick user → every action shows Allowed/Denied with reason ("Denied: role Housekeeper has no grant for payments.record") |
| Exceptions | User-level grant/deny with reason and expiry |
| Policy history | Version, before/after, actor, reason |
| States | Conflicting grants (deny wins, explained), expired assignment, last-owner protection, self-escalation denied |
| Current build | Partial (role × 12 permissions toggle, user enable/disable) |

### SCR-17 Approvals & audit (REQ-AUD-001/002)

Approvals inbox (pending/approved/rejected/expired/invalidated) with target, amount, version, requester, reason; audit log with filters (actor, action, record), before/after.
States: rejected, expired, invalidated because target changed, duplicate approval click.
Current build: Partial (approvals inside folio/closing; audit list on access page).

### SCR-18 Hotel configuration (REQ-CFG-001/002)

Categories: Property · Rooms & types · Tariffs (with effective dates) · Stay timings · Finance & synthetic tax · Approvals/limits · Housekeeping · Shifts · Documents. Each setting shows type, unit, current value, effective date, approval need, history; save shows an impact preview ("3 open stays affected; existing posted lines unchanged").
Required demonstrations: the five examples in plan §13 (discount threshold, inspection rule, future-dated rate, tax effective date, check-in/out time).
Current build: Partial (6 settings, no effective dates/history).

### SCR-19 Backup & recovery (REQ-BKP-001)

Banner "Simulation — no real backup is taken". Last backup time (simulated), retention, destination label, a failed-backup example, and the restore procedure as text.
Current build: **Missing**.

## 4. Visual system (Proposed)

- Original HCC design; inspired by workflow ideas, no copied donor UI (DEC-11, DEC-043).
- Palette: deep teal primary (`#173c36`, already in build), neutral greys, status colours always paired with text + icon:
  - Ready = green + check · Dirty = amber + broom · Cleaning = blue + spinner icon · Inspection = violet + eye · Occupied = teal + bed · Blocked = red + lock · Reserved = outline + calendar.
- Tabular numerals for money; right-aligned amounts.
- Density: desktop compact (32 px rows), tablet comfortable (44 px touch targets), phone large (48 px buttons).
- Print styles: sample invoice, registration card, closing report — each watermarked "Demonstration document".

## 5. Responsive verification sizes

1440×900 · 1280×800 (desktop reception) · 1024×768 landscape (supervisor tablet) · 768×1024 portrait · 390×844 (housekeeper phone) · 200% zoom on booking, payment and check-in forms. All checks are **emulated** unless a physical device is named in the evidence.

## 6. Mock service contract (summary)

- One normalised state; screens read selectors only.
- All mutations via `execute(state, userId, command)` returning new state or a typed error: `NOT_AVAILABLE`, `FORBIDDEN`, `INVALID_TRANSITION`, `VALIDATION`, `STALE_VERSION`, `DUPLICATE` (returns prior result), `DAY_CLOSED`.
- Commands carry an idempotency key; repeated key returns the first result.
- Fault simulation wraps `execute` (latency, rejection, lost response after commit, stale version).
- Simulated clock supplies every timestamp (DEC-030).
- Demo controls, role switching and storage live in removable modules (`app/demo/*`).

**Current build note:** the domain exists as one file (`src/domain.ts`, 22 KB) and the UI as one file (`src/main.tsx`, 52 KB). Proposed M1 refactor into the plan's folder structure (`app/`, `features/`, `domain/`, `services/mock/`, `fixtures/`) before adding screens, so features stop sharing one component.
