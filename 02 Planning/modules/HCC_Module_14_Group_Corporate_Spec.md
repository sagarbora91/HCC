# HCC Module 14 · Group & corporate — prototype spec (v1)

**Purpose.** Manage corporate (company) accounts with negotiated rates and credit limits, hold group room blocks (allotments) for events/tours/weddings, convert block rooms into real bookings from a rooming list, track pickup, release unpicked rooms at cut-off, and show a bill-to-company master folio. Prototype only: fictional companies, GSTIN-format placeholders marked "Fictional", simulated clock/business date.

**Roles.** Page `groups` (REVENUE & SALES), permission `reservations` (Owner, Manager, Reception). Credit-limit *set* needs `admin` (Owner); others with `reservations` *request* a change, decided by a different user with `approve` (Owner, Manager) — no self-approval. Housekeeping/maintenance never see this page (no money exposure).

**Screens.**
- Desktop `groups` page, tabs: **Blocks** (KPIs, block cards with allotment/pickup/remaining per type, cut-off, status, risk flag), **Companies** (table: GSTIN, contact, negotiated rates, limit, exposure, % used, warning), **Pickup report** (all blocks × room type, printable A4), **Master folio** (select block → member folios aggregated, read-only, printable).
- Dialogs: `grp-company` (create/edit), `grp-limit` (set/request limit), `grp-block` (create block), `grp-rooming` (rooming list → bookings), `grp-release` (manual release), `grp-folio` (master folio print view).
- Phone `groups` page: chip filter Blocks / Companies / Pickup; blocks and companies as ListCards; block detail sheet; one primary action (Fab "New block"); rooming list as a step-by-step sheet (one guest card per row, ≥48 px targets, `inputMode` tel/numeric).

**Commands** (state `s.groupsX`). `GRP_COMPANY_SAVE`, `GRP_LIMIT_SET`, `GRP_LIMIT_DECIDE`, `GRP_BLOCK_CREATE`, `GRP_BLOCK_ALLOT`, `GRP_ROOMING`, `GRP_RELEASE`, `GRP_SWEEP`, `GRP_BLOCK_CANCEL`. All go through `execute()`; idempotency keys honoured; every one is audited.

**Rules.**
1. Money in paise; negotiated rate per room type; members are booked at the block rate (agreed rate snapshot), synthetic tax via existing lines.
2. Block: arrival ≥ business date, 1–30 nights, cut-off between business date and arrival, allotment per type ≥ 0 and ≤ rooms of that type that are free for the whole stay minus other open blocks' unpicked rooms (soft hold inside the module).
3. Rooming list rows become bookings through the existing `BOOK` handler (same validation: availability, capacity, phone). The whole list is atomic: one failure → nothing booked. A row may not exceed the remaining allotment of its type. Pickup closed after cut-off or once released/cancelled.
4. Auto-release: when business date > cut-off, unpicked rooms are released (status Released). Runs on every GRP command and via `GRP_SWEEP`; screens derive the same result before it is persisted.
5. Credit: exposure = opening outstanding + estimated unpaid value (posted + unposted agreed nights + 10 % synthetic tax − payments) of the company's member bookings. ≥ 80 % → amber warning; a pickup that takes exposure above the limit is refused unless the user has `approve` and gives an override reason (audited).
6. Day-closed rule applies to all writes except limit decisions are still blocked when closed (all writes use `open()`).

**Known gap (shared file).** Unpicked block rooms are not subtracted by the shared `available()` selector (owned by stream A), so other screens still see them as free. The module shows pickup vs allotment and an **At risk** flag when free inventory of a type is below the remaining allotment. Needed change: `domain/selectors.ts available()` to count open-block remaining allotment per type.

**Acceptance tests (tests/stream-groups.mjs).** Fixture companies/blocks present and fictional; block over-allotment rejected; rooming list creates bookings at block rate with company; atomic failure; allotment exhaustion; idempotent rooming key; auto-release after NEXT_DAY past cut-off; pickup refused after release; credit-limit refusal and approved override; limit set by admin vs request + no self-approval; reception forbidden to set; housekeeper forbidden; master folio aggregation totals; existing fixture figures unchanged.

**Simulated parts.** No real GST validation, no invoices to companies, no e-mail of rooming lists, no AR ledger posting (exposure is an estimate). Labelled "Simulated" in the UI.
