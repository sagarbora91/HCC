# Hotel Control Centre — Master Product Requirements (PRD) v1

| Field | Value |
|---|---|
| Version | 1.0 (M0 draft for owner review) |
| Prepared | 24 October 2026 |
| Owner | Sagar |
| Sources | `01 Research Stage/Hotel_Control_Centre_Chat_Export.md` (**[C]**), `01 Research Stage/HCC_Claude_Execution_Plan_v1.md` (**[P]**), owner feedback (**[O]**) |
| Status | M0 — awaiting owner approval of prototype structure and walkthrough |

**Source tags.** [C] = established in the planning conversation. [P] = detail added by the execution plan; treated as *Proposed* until approved. [O] = owner feedback (none recorded yet beyond the instruction to proceed with M0 on 24 Oct 2026).

**Status vocabulary.** Agreed · Proposed · Needs owner decision · Unverified (see `HCC_Decision_Register.md`).

**Prototype depth.** Full = primary workflow plus listed exceptions are interactive · Partial = representative subset · Mock = visual/metadata only, no real behaviour · Backlog = not in prototype, no enabled navigation.

**Current build** (from code inspection of `03 Prototype/src`, 24 Oct 2026; not yet verified by click-through): Built · Partial · Missing.

---

## 1. Product summary

HCC is an internal operations application for the owner's hotel/lodging business in India **[C, DEC-01]**. It runs local-network-first on a Windows host, reached from Windows browsers and Android browsers/PWA over hotel Wi-Fi **[C, DEC-07, DEC-08, DEC-19]**. It is not a public commercial PMS.

Core design objective **[C §9]**: reception must not manually update five modules for one stay. One command (e.g. checkout) produces all its consequences (folio closed, room dirty, housekeeping task raised, report figures updated) consistently.

Delivery path **[C DEC-17, P §3]**: clickable React + TypeScript prototype with fictional data → owner acceptance → production design → NestJS + SQL Server 2025 Express vertical slice → MVP → pilot.

## 2. Users and usage contexts

| User (role template) | Main device / layout | Key jobs | Source |
|---|---|---|---|
| Owner | Desktop or tablet | Oversight, approvals, policy, access control, reports | [C §7] |
| Manager | Desktop / tablet | Daily operations, bounded approvals, exceptions | [C §7] |
| Reception / Front desk | Desktop (dense, keyboard) | Availability, bookings, registration, check-in/out, folio | [C §7] |
| Cashier | Desktop | Receipts, refunds (authorised), cash shift close | [C §7] |
| Housekeeping supervisor | Tablet | Assign, inspect, release rooms, raise blocks | [C §7] |
| Housekeeper | Android phone | Own tasks, checklist, complete | [C §7, §10] |
| Accountant | Desktop | Reports, reconciliations, permitted corrections | [C §7] — prototype: Backlog role |
| Technical administrator | Desktop | Identity and technical configuration (no automatic financial power) | [P §12] — prototype: Backlog role |
| Maintenance | Phone | Assigned repair tasks, block requests | [C §7] — prototype: Backlog role |

## 3. Scope — all 26 functional areas

MVP = areas 01–12 **[C DEC-02]**. Areas 13–26 are retained in the roadmap with explicit phases; none are dropped.

| # | Area | Phase | Prototype depth | Current build |
|---|---|---|---|---|
| 01 | Hotel, rooms and tariffs | MVP | Full (records, rates, occupancy, block/unblock); Partial for rate plans | Partial — fixed rates per type; block toggle; no room editor, no dated blocks |
| 02 | Reservations | MVP | Full | Partial — book/edit/move/cancel/no-show; no hold/expiry, no room-type search |
| 03 | Room rack / calendar | MVP | Full | Partial — floor board built; calendar timeline missing |
| 04 | Guests / registration | MVP | Full (fictional) | Partial — registration flag only; no guest profile, party, company, masked ID |
| 05 | Stay, check-in, checkout | MVP | Full | Built — check-in, move/extend, checkout with dirty + task |
| 06 | Folio / GST billing | MVP | Full with synthetic tax | Partial — lines, discount, invoice print; no credit note, no split folio |
| 07 | Payments / cashier | MVP | Full (manual tenders only) | Partial — receipts, deposits, refunds, single shift close; no cashier screen, no mixed-tender dialog |
| 08 | Housekeeping | MVP | Full | Built — assign/start/checklist/complete/inspect/reject; repair flag via block |
| 09 | Expenses / petty cash | MVP | Full | Built — submit/approve/post; no reversal, no receipt metadata |
| 10 | Night audit / closing | MVP | Full | Partial — preflight, night posting, shift close, day close, next day; no retry/correction request UI |
| 11 | Owner reports | MVP | Full | Partial — derived metrics; drill-down and exports to confirm |
| 12 | Access, audit, backup | MVP | Full (access/audit), Mock (backup) | Partial — role grant editor, user enable/disable, audit list; no scopes/limits/approvals screen/backup UI |
| 13 | Advanced tariff / yield | Phase 2 | Backlog | — |
| 14 | Group / corporate | Phase 2 | Backlog; company-billing context only where MVP needs it | — |
| 15 | Maintenance | Phase 2 | MVP room block/repair handoff only | Partial (block toggle) |
| 16 | Linen / laundry / stock | Phase 2 | Backlog | — |
| 17 | HR / payroll | Phase 2 | Demo staff identities only | Demo users present |
| 18 | Guest services / complaints / lost & found | Phase 2 | Manual service charge in folio only | Built (charge) |
| 19 | Checklists / shift handover | Phase 2 | MVP housekeeping + cashier essentials | Partial |
| 20 | Accounting automation | Phase 2 | Stable source IDs and classifications only | Partial (IDs exist) |
| 21 | OTA channel management | Phase 3 | Manual booking-source selection | Built (source field) |
| 22 | Direct booking website | Phase 3 | Backlog | — |
| 23 | Restaurant POS / room service | Phase 3 | Manual authorised folio charge | Built (charge) |
| 24 | WhatsApp / notifications | Phase 3 | Optional mock preview; no sends | — |
| 25 | OCR / document capture | Phase 3 | Mock document metadata | — |
| 26 | AI management assistant | Phase 3 | Backlog | — |

### 3.1 Advanced Phase-1 items needing an explicit disposition [P §6]

These appeared in the conversation's Phase-1 lists. Proposed disposition for owner approval (no silent drops):

| Item | Prototype depth | Production milestone | Acceptance case |
|---|---|---|---|
| Weekday/weekend & seasonal rates | Partial — one future-dated rate example | M6 (full); advanced combos → area 13 | AC-019 |
| Corporate rates / company billing | Partial — one company-credit stay | M6 basic; area 14 full | AC-008 |
| Day-use / hourly rate | Backlog (documented rule only) | M6, needs time-interval design | AC-002 (variant) |
| Extra bed / extra person charge | Partial — manual charge line | M6 | AC-006 (variant) |
| Guest duplicate detection & merge | Partial — duplicate suggestion, no merge | M6 merge with approval | AC-021 |
| Bill splitting / split folio | Partial — mixed tender only; no split folio | M6 | AC-006 (variant) |
| Credit notes / invoice correction | Partial — mock credit note record | M6, after tax validation | AC-007 |
| Cash denomination count | Partial — total count; denominations optional | M6 | AC-015 |
| YTD / last-year comparison | Mock — synthetic history | M6 | AC-020 |
| Excel / CSV / PDF exports | CSV + print-to-PDF; Excel Backlog | M6 all three | AC-020, AC-029 |
| Foreign-guest / guest-register reporting | Backlog — requirement recorded, legally Unverified | M6 after legal validation | — |
| Real-time cross-device updates | Not demonstrable (browser-local state) | M5/M6 | — |

## 4. Functional requirements (MVP)

Format: ID · requirement · source · prototype depth. Full workflow semantics live in `HCC_Business_Workflow_Spec_v1.md`; permissions in `HCC_RBAC_Matrix_v1.md`.

### 01 Hotel, rooms and tariffs

| ID | Requirement | Src | Depth |
|---|---|---|---|
| REQ-ROOM-001 | Maintain property identity (name, legal entity, address, GSTIN placeholder, timezone, currency). | C | Full (fictional) |
| REQ-ROOM-002 | Maintain floors, rooms, room types, bed configuration, max adults/children, amenities. | C | Full |
| REQ-ROOM-003 | Maintain base tariff per room type with effective dates; existing agreed booking rates are not repriced automatically. | C, P | Partial |
| REQ-ROOM-004 | Block/unblock a room with reason, start/end date and author; conflicts with existing allocations must be resolved, never silently displaced. | C, P | Full |
| REQ-ROOM-005 | Archive a room only when it has no active or future allocation. | P | Partial |
| REQ-ROOM-006 | Room status vocabulary keeps three independent dimensions: occupancy, cleaning readiness, inventory block. | C, P | Full |

### 02 Reservations

| ID | Requirement | Src | Depth |
|---|---|---|---|
| REQ-BOOK-001 | Search availability by dates, party size and room type; show available rooms and the reason others are excluded. | C | Full |
| REQ-BOOK-002 | Create a reservation with guest, contact, dates, party size, room type/room, rate, source, notes. | C | Full |
| REQ-BOOK-003 | Booking sources: walk-in, phone, WhatsApp, direct, manual OTA (named channel), corporate. | C | Full |
| REQ-BOOK-004 | Hold a room with explicit expiry; expired holds release inventory; refreshing must not extend a hold. | C, P | Full |
| REQ-BOOK-005 | Confirm, edit, change dates, change room, cancel and mark no-show, each revalidating the full affected interval. | C | Full |
| REQ-BOOK-006 | Exclusive room occupancy never overlaps (arrival-inclusive, departure-exclusive). | C, P | Full |
| REQ-BOOK-007 | Record advance receipts against a reservation; cancellation with money held requires refund/forfeit handling per policy. | C | Full |
| REQ-BOOK-008 | Reservation history and printable confirmation (marked demonstration). | C | Partial |

### 03 Room rack / calendar

| ID | Requirement | Src | Depth |
|---|---|---|---|
| REQ-RACK-001 | Floor grid of all rooms showing occupancy, cleaning state and block with text/icon plus colour. | C | Full |
| REQ-RACK-002 | Filters: floor, room type, status; today's arrivals, departures, stayovers. | C | Full |
| REQ-RACK-003 | Room panel: current guest, next reservation, housekeeping and block state, quick actions. | C | Full |
| REQ-RACK-004 | Calendar timeline (rooms × dates) with stays and blocks, date navigation, horizontal scroll inside the grid. | C | Full |
| REQ-RACK-005 | Move/extend via a non-drag alternative (dialog); any drag gesture still passes server-side validation. | C, P | Full (dialog); drag optional |

### 04 Guests / registration

| ID | Requirement | Src | Depth |
|---|---|---|---|
| REQ-GUEST-001 | Search and create guest profiles (name, phone, email, address, nationality). | C | Full (fictional) |
| REQ-GUEST-002 | Accompanying guests, company name and company GSTIN on the stay. | C | Full |
| REQ-GUEST-003 | Masked ID metadata (type, last 4 characters) — **no real ID images in the prototype**. | C, P | Mock |
| REQ-GUEST-004 | Duplicate suggestion on matching phone/name. | C | Partial |
| REQ-GUEST-005 | Registration card print (demonstration watermark). | C, P | Full |
| REQ-GUEST-006 | Restricted fields masked for roles without the field grant. | C | Full |
| REQ-GUEST-007 | Stay history per guest. | C | Partial |

### 05 Stay, check-in, checkout

| ID | Requirement | Src | Depth |
|---|---|---|---|
| REQ-STAY-001 | Check-in from reservation or walk-in; requires eligible room (vacant, ready, unblocked), complete registration, valid rate, permitted user. | C | Full |
| REQ-STAY-002 | Room move preserves dated room segments; target reserved before source released; old room becomes dirty. | C, P | Full |
| REQ-STAY-003 | Extension/shortening revalidates availability; conflict shows alternatives and preserves the original. | C | Full |
| REQ-STAY-004 | Checkout previews final charges and deposit disposition; settles or records authorised company credit. | C | Full |
| REQ-STAY-005 | Successful checkout ends occupancy, marks room dirty and creates exactly one housekeeping task. | C | Full |
| REQ-STAY-006 | Repeated checkout returns the existing result; no duplicate task or receipt. | P | Full |
| REQ-STAY-007 | Post-checkout billing correction does not reopen physical occupancy. | P | Partial |
| REQ-STAY-008 | Early check-in / late checkout charge per configured policy, exceptions need approval. | C | Partial |

### 06 Folio / billing

| ID | Requirement | Src | Depth |
|---|---|---|---|
| REQ-FOLIO-001 | Itemised, dated folio lines: room nights, services, discounts, tax (synthetic), adjustments. | C | Full |
| REQ-FOLIO-002 | Each posted line keeps its rate and tax snapshot; later configuration changes do not rewrite it. | C, P | Full |
| REQ-FOLIO-003 | Discount requires a reason; above the configured threshold it routes for approval bound to amount + folio version. | C, P | Full |
| REQ-FOLIO-004 | Advances allocated once; refundable deposits shown separately as liability, never as revenue. | C | Full |
| REQ-FOLIO-005 | Sample invoice with a sample series number; posted documents cannot be edited or deleted. | C, P | Full (mock numbering) |
| REQ-FOLIO-006 | Credit note / correction creates a linked record; history is never erased. | C | Partial |
| REQ-FOLIO-007 | Duplicate charge protection: same stay + service date + charge kind posts once. | C | Full |

### 07 Payments / cashier

| ID | Requirement | Src | Depth |
|---|---|---|---|
| REQ-PAY-001 | Record manual receipts: cash, UPI, card, bank transfer; non-cash needs a reference; confirmation states the bank transaction is **not** verified. | C, P | Full |
| REQ-PAY-002 | Mixed/split tenders for one settlement. | C | Full |
| REQ-PAY-003 | Refund references an eligible original receipt, cannot exceed refundable amount, needs reason and authority. | C, P | Full |
| REQ-PAY-004 | Cash shift: opening float, receipts, refunds, cash expenses, transfers, expected vs counted, discrepancy reason/approval, close. | C | Full |
| REQ-PAY-005 | Cash receipts rejected when the user's shift is closed. | P | Full |
| REQ-PAY-006 | Retry with the same command identity returns the first result (no duplicate money). | C | Full |
| REQ-PAY-007 | Company credit (city ledger) recorded explicitly with authorisation. | C | Partial |

### 08 Housekeeping

| ID | Requirement | Src | Depth |
|---|---|---|---|
| REQ-HK-001 | Task list of checkout, stayover and scheduled cleans; assign by staff/floor. | C | Full (stayover Partial) |
| REQ-HK-002 | Start → checklist → complete with timestamps. | C | Full |
| REQ-HK-003 | Supervisor inspection passes (Ready) or rejects with reason (back to cleaning), when inspection policy is on. | C | Full |
| REQ-HK-004 | Repair issue found during cleaning raises a block request. | C | Full |
| REQ-HK-005 | Housekeeper sees and acts only on own tasks; no financial navigation. | C | Full |
| REQ-HK-006 | Mobile layout: own tasks first, large controls. | C | Full |

### 09 Expenses / petty cash

| ID | Requirement | Src | Depth |
|---|---|---|---|
| REQ-EXP-001 | Expense with vendor, category, date, amount, funding source (drawer cash / UPI / vendor credit), notes. | C | Full |
| REQ-EXP-002 | Draft → Pending → Approved/Rejected → Posted → Reversed; no self-approval. | C, P | Full |
| REQ-EXP-003 | Drawer-funded posted expense reduces expected drawer cash exactly once; vendor-credit does not. | P | Full |
| REQ-EXP-004 | Receipt attachment metadata (placeholder only). | C | Mock |
| REQ-EXP-005 | Duplicate warning on same vendor + amount + date. | C | Partial |

### 10 Night audit / business-day closing

| ID | Requirement | Src | Depth |
|---|---|---|---|
| REQ-CLOSE-001 | Explicit simulated business date separate from calendar date. | C, P | Full |
| REQ-CLOSE-002 | Preflight lists blocking issues with drill-down: unresolved arrivals, overdue departures, unposted nights, pending approvals/expenses, open shift. | C | Full |
| REQ-CLOSE-003 | Night posting posts each in-house room night once; rerun changes nothing. | C | Full |
| REQ-CLOSE-004 | Close produces one immutable closing summary (revenue, tax, receipts, deposits, expenses, occupancy, exceptions). | C | Full |
| REQ-CLOSE-005 | Interrupted close is resumable; completed steps are not repeated. | P | Full (simulated) |
| REQ-CLOSE-006 | Closed-date correction only via privileged correction request (policy: adjustment vs reopen — DEC-028). | C | Partial |

### 11 Owner dashboard and reports

| ID | Requirement | Src | Depth |
|---|---|---|---|
| REQ-RPT-001 | Dashboard: rooms, occupied, available, occupancy %, arrivals, departures, HK pending, blocked, balances, new bookings. | C | Full |
| REQ-RPT-002 | Financial: room vs service revenue, tax, receipts by tender, deposits held, expenses, cash position, ADR, RevPAR. | C | Full |
| REQ-RPT-003 | All figures derived from transactions; none hard-coded. | P | Full |
| REQ-RPT-004 | Drill-down from every total to its source records under the same filters. | P | Full |
| REQ-RPT-005 | Exports CSV and print/PDF obeying the same scope/masking as the screen. | C, P | Partial |
| REQ-RPT-006 | Report dictionary defines occupancy denominator, ADR, RevPAR, zero-denominator handling. | P | Full |

### 12 Access control, audit, backup

| ID | Requirement | Src | Depth |
|---|---|---|---|
| REQ-ACC-001 | Users: create, edit, enable/disable, assign roles, expiry. | C | Full |
| REQ-ACC-002 | Role templates with resource × action matrix (allow / deny / inherit). | C | Full |
| REQ-ACC-003 | Scopes: own, assigned, shift, property. | C | Full |
| REQ-ACC-004 | Field restrictions (guest ID, financial adjustments, audit log). | C | Full |
| REQ-ACC-005 | Financial limits and approval routes (discount, refund, rate override, cash difference, closed-date correction, sensitive config). | C | Full |
| REQ-ACC-006 | Effective-access preview with allow/deny explanation. | C, P | Full |
| REQ-ACC-007 | Block self-escalation, self-approval and removal/disable of the last active owner. | C, P | Full |
| REQ-ACC-008 | Disabling a user denies their next command immediately. | P | Full |
| REQ-ACC-009 | Direct service-level denial demonstrated, not only hidden buttons. | P | Full |
| REQ-AUD-001 | Audit trail: initiator, approver, before/after, reason, timestamp, outcome for bookings, moves, invoices, refunds, discounts, settings and policy. | C | Full |
| REQ-AUD-002 | Approvals inbox: pending, approved, rejected, expired, invalidated. | C | Full |
| REQ-CFG-001 | Hotel Configuration Control Panel separate from access control, grouped by category, with effective dates, validation, impact preview and history. | C | Full (selected keys) |
| REQ-CFG-002 | Safety invariants (authorisation, overlap protection, payment duplication guard, audit history) are never configurable. | C | Full |
| REQ-BKP-001 | Backup status screen — clearly simulated in prototype; real backup/restore is production scope. | C, P | Mock |

## 5. Non-functional requirements

| ID | Requirement | Prototype | Production |
|---|---|---|---|
| NFR-SEC-001 | Deny by default; server-side enforcement of every command, query, export and download. | Simulated in mock service | M5 |
| NFR-SEC-002 | Password/MFA for privileged roles, session expiry and revocation. | Out of scope (demo role switch) | M5 |
| NFR-PRIV-001 | Personal-data minimisation, masking, retention and deletion policy. | Fictional data only | M4 policy |
| NFR-REL-001 | Idempotent commands; no duplicate money or allocation on retry. | Command keys | M5 durable keys |
| NFR-REL-002 | Defined behaviour on lost response, stale version, server unavailable. | Simulated faults | M5 |
| NFR-CONC-001 | Concurrent allocation/payment safety. | Not provable in mock | M5 concurrent tests |
| NFR-PERF-001 | Proposed: ≤50 rooms, 10 concurrent users, API p95 < 1 s, screen < 2 s on agreed LAN hardware. | Not measured | M6 |
| NFR-UX-001 | Responsive at 1440×900, 1280×800, 1024×768, 768×1024, 390×844; 200% zoom on critical forms. | Emulated | Real devices M6 |
| NFR-UX-002 | Keyboard operation, labelled fields, colour + text status, focus restore after dialogs. | Required | Required |
| NFR-OFF-001 | v1: LAN loss disables booking/payment/allocation mutations; no silent queue of financial commands. | Connectivity indicator | M5 |
| NFR-DR-001 | Backup, off-device copy, measured restore drill against agreed RPO/RTO. | Mock | M6 |
| NFR-COMP-001 | GST and guest-register obligations validated by qualified advisers. | Synthetic only | Before M5 finance |
| NFR-OBS-001 | Structured logs, correlation IDs, exception dashboards. | — | M5 |
| NFR-UPG-001 | Versioned migrations, pre-upgrade backup, recovery rehearsal. | Versioned local state | M6 |

## 6. Out of scope for the prototype (explicit)

Real authentication, SQL Server, cross-device synchronisation, payment gateways, real tax compliance, OTA connections, notifications, real document storage, real backups, private ETP source.

## 7. Traceability

Every REQ ID maps to screens in `HCC_Prototype_Spec_v1.md`, workflows in `HCC_Business_Workflow_Spec_v1.md`, and acceptance cases in `HCC_Acceptance_Matrix.md`. Backlog tasks in `HCC_Implementation_Backlog.md` cite REQ IDs.
