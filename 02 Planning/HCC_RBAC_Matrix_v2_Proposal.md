# Hotel Control Centre: Access Matrix v2 (dedicated permissions per module): proposal for owner decision

26 Oct 2026 · Status: **Proposed. No code until the owner approves.** · Author: W3-10 (documents only)
Inputs: `HCC_RBAC_Matrix_v1.md`, DEC-055 / 057 / 058 / 061 / 062 / 063, `RBAC_Mismatch_Rulings_Proposed.md`, and the permission seeds actually on branch `claude/wave1-integration` (migrations 0101, 0901, 1001, 1101 and the module code that names permissions).

**What this changes in one line:** every module gets its own permission ids, so that changing who may do something in one module can never silently change another module. Day-to-day access stays the same except for the small list of deliberate changes in section 3.4.

---

## 1. Today's state

### 1.1 What is seeded

| Seed | Permissions | Note |
|---|---|---|
| 0101 access | 45 matrix permissions (`PERM-RES-…` to `PERM-ADM-005`) | Matrix v1 with DEC-063 applied. Roles seeded: OWN, MGR, REC, CSH, ACC, SUP, HKP, MNT, TAD |
| 0901 payroll | `PERM-HR-001…009` | Dedicated. Own pay for every role through `HR-004` (DEC-061) |
| 1001 accounting | `PERM-ACC-001…006` | Dedicated |
| 1101 compliance | `PERM-CMP-001…002` | Dedicated |
| POS, stock, maintenance, guest services, messaging, handover, yield, property setup, master configuration | **none** | These modules have a table migration (1200 to 1900) but **no RBAC seed**. Their code points at matrix permissions that were written for something else |

There is no Restaurant role in the seed (nine roles only). POS today is operated by whoever holds the billing and payment permissions.

### 1.2 Which module borrows which permission

| Module | Borrowed permissions (from code) | What the borrowed id was really written for |
|---|---|---|
| POS | FOL-002 operate, PAY-001 settle, APR-001 void decide, ADM-003 setup | Folio charge, payment, approval, configuration |
| Stock / linen | HK-003 work, HK-004 supervise, RPT-002 money | Housekeeping task work, inspection, finance reports |
| Maintenance | HK-001 view, HK-002 assign, HK-003, HK-004 manage, ROOM-001 work, ROOM-002 block, RPT-002 money | Housekeeping board, room blocks, finance reports |
| Guest services | GST-002 manage, HK-002, HK-003, ROOM-001, APR-001 approve, RPT-001 report | Guest register edit, housekeeping, approvals, operations reports |
| Messaging | RES-001 view, RES-002 send, STY-001 consent, APR-001 edit templates | Reservations, check-in, approvals |
| Handover | HK-001 board, PAY-001 payments, APR-001 approve, RPT-002 finance | Housekeeping board, payments, approvals, finance reports |
| Yield / tariffs | RPT-001 view, APR-001 propose, ADM-003 manage and decide | Operations reports, approvals, configuration |
| Property setup / configuration | ADM-003 | Admin configuration |

### 1.3 Why this is a problem

1. **Changing one grant changes several modules.** Example: `RPT-002` (finance reports) is the "may see money" switch for stock **and** maintenance **and** handover. Matrix v1 gives the Cashier `RPT-002` (own shift, BL-024 plans to narrow it). The moment someone narrows or widens it, stock values, part prices and handover cash move with it. Nobody reviewing a report-access change would think of linen costs.
2. **Money-hiding for Housekeeping and Maintenance rests on a negative.** DEC-061 / DEC-063 G12 say these roles never see hotel money. Today that is true only because they happen to lack `RPT-002`. If a future grant of `RPT-002` or `APR-001` is made for any reason, money leaks into three modules at once. There is no permission whose name says "this is money".
3. **DEC-058 cannot be expressed.** DEC-058: the Tech admin may change non-money groups (branding, templates, modules on/off, housekeeping settings). The seed gives the Tech admin `ADM-003` as **view only**, and the Manager nothing. There is no id for "non-money configuration", so either the Tech admin does everything or nothing. Message templates are edited through `APR-001` (the approval permission, with the Manager's rupee limit), so the Tech admin cannot edit them although DEC-058 allows it.
4. **Wrong-looking grants.** Reception holds `ROOM-001` property-wide but is blocked from the maintenance module by an extra code check (DEC-063 G7). Every borrowed permission needs such a hand-written guard, and a missed guard is a silent hole.
5. **Reports and audit say the wrong thing.** The audit trail and Access control screens would show "approval.decide" for a template edit and "hk.inspect" for a stock count approval. Auditors and the owner cannot read the permission list.
6. **No place for the Restaurant role and Wave 3.** OTA, web booking, document capture, groups billing, CRM and invoices/credit notes would otherwise borrow another round of ids (FOL-004/005, RES-00x, GST-00x), multiplying the problem.

---

## 2. Proposed design

### 2.1 Naming scheme

`PERM-<MOD>-<nnn>` where `<MOD>` is a three-letter module code and `nnn` follows one pattern in every module:

| Number | Meaning | Money flag |
|---|---|---|
| 001 | **View** (see the screen and records, money fields hidden) | no |
| 002 | **Act** (create, edit, work the normal day-to-day actions) | no, unless the action moves money |
| 003 | **Approve / decide** (the step that needs a second person or a limit) | yes where it decides money |
| 004 | **Reveal money** (the only permission that unlocks money fields in the module) | **always yes** |
| 005 and up | Module extras (configure, assign, export, special override), described per row | per row |

A gap means "not applicable for this module" (for example yield has no separate reveal because the whole module is money).

**Money-hiding rule (DEC-061, DEC-063 G12), enforced in the seed and in tests:**

- Every money field in an API response is null unless the caller holds that module's `…-004` (or the module's money-flagged permission) property-wide.
- Housekeeping supervisor, Housekeeper, Maintenance and Restaurant hold **no permission with `is_money = 1`**, with one exception: `HR-004` (own pay and payslip, scope O, DEC-061).
- Restaurant staff necessarily see item prices and check totals on their own checks (that is the job); they hold no day-sales, margin, cost or other-staff-pay permission.
- A test (section 4) fails the build if any of SUP, HKP, MNT or RST is granted any other money-flagged permission.

### 2.2 Roles

The ten columns requested. **RST (Restaurant) is new**: it does not exist in the seed or matrix v1 (see decision D1). It would be a PIN role like Reception, owner-assigned.

Legend as in matrix v1: **A** allow · **L** allow up to limit · **R** request only · **V** view only · **M** masked fields · — deny · `·S` own shift · `·A` assigned records · `·O` own records. Roles are listed in this order: OWN, MGR, ACC, REC, CSH, SUP, HKP, MNT, RST, TAD.

### 2.3 Role by permission grid: existing modules (replace borrowed ids)

**POS (restaurant, new RST role)**

| Perm | What | OWN | MGR | ACC | REC | CSH | SUP | HKP | MNT | RST | TAD |
|---|---|---|---|---|---|---|---|---|---|---|---|
| PERM-POS-001 | View checks, menu, kitchen board | A | A | V | A | A | — | — | — | A | — |
| PERM-POS-002 | Operate: open check, add items, send to kitchen | A | A | — | A | A | — | — | — | A | — |
| PERM-POS-003 | Approve void / discount (Manager up to ₹1,000, others request) | A | L | — | R | R | — | — | — | R | — |
| PERM-POS-004 | Reveal money: day sales summary, margins, item cost | A | A | V | — | A·S | — | — | — | — | — |
| PERM-POS-005 | Settle check (cash, card, charge to room) | A | A | — | A·S | A·S | — | — | — | — | — |
| PERM-POS-006 | Setup: outlets, menu items, prices, tax category | A | R | — | — | — | — | — | — | — | — |

**Stock, linen and laundry**

| Perm | What | OWN | MGR | ACC | REC | CSH | SUP | HKP | MNT | RST | TAD |
|---|---|---|---|---|---|---|---|---|---|---|---|
| PERM-STK-001 | View stock levels (no money) | A | A | V | — | — | A | A·A | — | — | — |
| PERM-STK-002 | Issue, receive, count, send to laundry | A | A | — | — | — | A | A·A | — | — | — |
| PERM-STK-003 | Approve count variance (not your own count), create item | A | A | — | — | — | A | — | — | — | — |
| PERM-STK-004 | Reveal money: unit cost, stock value, laundry rate and charge, loss value | A | A | A | — | — | — | — | — | — | — |
| PERM-STK-005 | Configure par sets and laundry vendor (rates also need 004) | A | A | — | — | — | A | — | — | — | — |

**Maintenance (work orders and AMC)**

| Perm | What | OWN | MGR | ACC | REC | CSH | SUP | HKP | MNT | RST | TAD |
|---|---|---|---|---|---|---|---|---|---|---|---|
| PERM-MNT-001 | View work orders (no money) | A | A | — | — | — | A | A·A | A·A | — | — |
| PERM-MNT-002 | Raise a repair request / work an assigned order | A | A | — | — | — | A | A·A | A·A | — | — |
| PERM-MNT-003 | Assign, close, apply or lift a room block | A | A | — | — | — | A | — | — | — | — |
| PERM-MNT-004 | Reveal money: part prices, order cost, vendor bills | A | A | V | — | — | — | — | — | — | — |
| PERM-MNT-005 | AMC contracts (cost, renewal) | A | A | V | — | — | — | — | — | — | — |

Reception raises repairs through a guest request (`GSV-002`) which creates the linked work order; no direct maintenance access (DEC-063 G7 unchanged).

**Guest services (requests, wake-up, complaints)**

| Perm | What | OWN | MGR | ACC | REC | CSH | SUP | HKP | MNT | RST | TAD |
|---|---|---|---|---|---|---|---|---|---|---|---|
| PERM-GSV-001 | View requests and complaints | A | A | — | A | — | A | A·A | A·A | — | — |
| PERM-GSV-002 | Log a request, work an assigned one | A | A | — | A | — | A | A·A | A·A | — | — |
| PERM-GSV-003 | Close High/Critical complaints, approve recovery (Manager up to ₹1,000) | A | L | — | — | — | — | — | — | — | — |
| PERM-GSV-004 | Reveal money: recovery and compensation amounts | A | A | — | V | — | — | — | — | — | — |
| PERM-GSV-005 | Assign and supervise | A | A | — | — | — | A | — | — | — | — |

**Messaging (guest messages and templates)**

| Perm | What | OWN | MGR | ACC | REC | CSH | SUP | HKP | MNT | RST | TAD |
|---|---|---|---|---|---|---|---|---|---|---|---|
| PERM-MSG-001 | View message log | A | A | — | A | — | — | — | — | — | — |
| PERM-MSG-002 | Send a message | A | A | — | A | — | — | — | — | — | — |
| PERM-MSG-003 | Edit templates (non-money configuration, DEC-058) | A | A | — | — | — | — | — | — | — | A |
| PERM-MSG-005 | Send without opt-in (consent fallback) | A | A | — | A | — | — | — | — | — | — |

**Handover (shift notes)**

| Perm | What | OWN | MGR | ACC | REC | CSH | SUP | HKP | MNT | RST | TAD |
|---|---|---|---|---|---|---|---|---|---|---|---|
| PERM-HND-001 | View notes (own department's notes for HKP/MNT) | A | A | — | A | A | A | A·A | A·A | — | — |
| PERM-HND-002 | Write a note | A | A | — | A | A | A | A·O | A·O | — | — |
| PERM-HND-003 | Acknowledge / decide a note | A | A | — | — | — | — | — | — | — | — |
| PERM-HND-004 | Reveal money: cash amounts on front-desk notes | A | A | V | A·S | A·S | — | — | — | — | — |

**Yield and tariffs (whole module is money, so no separate reveal)**

| Perm | What | OWN | MGR | ACC | REC | CSH | SUP | HKP | MNT | RST | TAD |
|---|---|---|---|---|---|---|---|---|---|---|---|
| PERM-YLD-001 | View tariffs and proposals | A | A | V | — | — | — | — | — | — | — |
| PERM-YLD-002 | Propose a tariff change | A | A | — | — | — | — | — | — | — | — |
| PERM-YLD-003 | Decide and publish (Owner only: money, DEC-058) | A | — | — | — | — | — | — | — | — | — |

**Master configuration and property setup (DEC-057, DEC-058)**

| Perm | What | OWN | MGR | ACC | REC | CSH | SUP | HKP | MNT | RST | TAD |
|---|---|---|---|---|---|---|---|---|---|---|---|
| PERM-CFG-001 | View configuration | A | — | — | — | — | — | — | — | — | V |
| PERM-CFG-002 | Change non-money groups (branding, templates, modules on/off, housekeeping, data, system) | A | — | — | — | — | — | — | — | — | A |
| PERM-CFG-003 | Change money groups (tax, payroll, compliance, approval limits, ledger mapping) and decide setting requests; never self-approve | A | — | — | — | — | — | — | — | — | — |

The Manager keeps the Dashboard "last backup" warning card (DEC-063 G9); no configuration page. `ADM-003` stays seeded and is retired after the callers switch.

**Already dedicated (no new ids, only add the RST column)**

| Module | Ids | Change |
|---|---|---|
| Payroll and leave | `PERM-HR-001…009` | RST gets `HR-004` scope O (own roster, leave, payslip) like every other role |
| Accounting | `PERM-ACC-001…006` | none |
| Compliance | `PERM-CMP-001…002` | none |

### 2.4 Role by permission grid: Wave 3 modules (new)

**OTA channels (channel manager)**

| Perm | What | OWN | MGR | ACC | REC | CSH | SUP | HKP | MNT | RST | TAD |
|---|---|---|---|---|---|---|---|---|---|---|---|
| PERM-OTA-001 | View channel status, OTA bookings (no commission) | A | A | V | A | — | — | — | — | — | V |
| PERM-OTA-002 | Resolve OTA bookings, map rooms, push availability | A | A | — | A | — | — | — | — | — | — |
| PERM-OTA-003 | Approve rate parity changes, stop-sell on all channels, commission terms | A | A | — | — | — | — | — | — | — | — |
| PERM-OTA-004 | Reveal money: commission, payout, net rate | A | A | V | — | — | — | — | — | — | — |
| PERM-OTA-005 | Connect or disconnect a channel, credentials (module switch, DEC-057) | A | — | — | — | — | — | — | — | — | A |

**Web booking site**

| Perm | What | OWN | MGR | ACC | REC | CSH | SUP | HKP | MNT | RST | TAD |
|---|---|---|---|---|---|---|---|---|---|---|---|
| PERM-WEB-001 | View web enquiries and bookings | A | A | V | A | — | — | — | — | — | — |
| PERM-WEB-002 | Handle a web booking (confirm, contact guest) | A | A | — | A | — | — | — | — | — | — |
| PERM-WEB-003 | Approve offers, promo codes, publish rates; approve gateway refund | A | A | — | — | — | — | — | — | — | — |
| PERM-WEB-004 | Reveal money: gateway settlement, fees | A | A | A | — | — | — | — | — | — | — |
| PERM-WEB-005 | Site content, branding, languages (non-money) | A | A | — | — | — | — | — | — | — | A |

**Document capture**

| Perm | What | OWN | MGR | ACC | REC | CSH | SUP | HKP | MNT | RST | TAD |
|---|---|---|---|---|---|---|---|---|---|---|---|
| PERM-DOC-001 | View captured documents (bills, forms; not guest ID images) | A | A | A | A | A | — | — | — | — | — |
| PERM-DOC-002 | Capture / upload a document | A | A | A | A | A | — | — | — | — | — |
| PERM-DOC-003 | Verify extracted data and file it | A | A | A | — | — | — | — | — | — | — |
| PERM-DOC-004 | Reveal guest ID images and numbers (same holders as `PERM-GST-003`, DEC-068; audited, never in exports) | A | A | — | A | — | — | — | — | — | — |
| PERM-DOC-005 | Archive / retention rules | A | — | — | — | — | — | — | — | — | — |

Supervisor and Maintenance hand shop bills to Reception or the Cashier (DEC-063 G6), who capture them.

**Groups billing (group bookings, master folio)**

| Perm | What | OWN | MGR | ACC | REC | CSH | SUP | HKP | MNT | RST | TAD |
|---|---|---|---|---|---|---|---|---|---|---|---|
| PERM-GRP-001 | View group bookings, rooming list, master folio | A | A | V | A | V | — | — | — | — | — |
| PERM-GRP-002 | Create group, edit rooming list, post to master folio | A | A | — | A | — | — | — | — | — | — |
| PERM-GRP-003 | Approve group rate, credit terms, split-billing exceptions, block release | A | L | — | R | — | — | — | — | — | — |
| PERM-GRP-004 | Reveal money: master folio totals, group deposits | A | A | V | A | V | — | — | — | — | — |
| PERM-GRP-005 | Collect group deposit or payment (own shift) | A | A | — | A·S | A·S | — | — | — | — | — |

**CRM (guest profiles, history, campaigns)**

| Perm | What | OWN | MGR | ACC | REC | CSH | SUP | HKP | MNT | RST | TAD |
|---|---|---|---|---|---|---|---|---|---|---|---|
| PERM-CRM-001 | View guest profiles and stay history (contact masked for Cashier, as `GST-001`) | A | A | — | A | M | — | — | — | — | — |
| PERM-CRM-002 | Add notes and tags, edit preferences | A | A | — | A | — | — | — | — | — | — |
| PERM-CRM-003 | Approve bulk campaign send, merge duplicates, honour deletion requests | A | A | — | — | — | — | — | — | — | — |
| PERM-CRM-004 | Reveal money: lifetime spend, balances | A | A | V | — | — | — | — | — | — | — |

**Invoices and credit notes (split out of `FOL-004` / `FOL-005`)**

| Perm | What | OWN | MGR | ACC | REC | CSH | SUP | HKP | MNT | RST | TAD |
|---|---|---|---|---|---|---|---|---|---|---|---|
| PERM-INV-001 | View invoices and credit notes | A | A | V | A | A | — | — | — | — | — |
| PERM-INV-002 | Issue / print / reprint an invoice | A | A | — | A | A | — | — | — | — | — |
| PERM-INV-003 | Approve a credit note or correction (Owner only, DEC-063 G3) | A | — | — | — | — | — | — | — | — | — |
| PERM-INV-005 | Request a credit note or correction | — | A | R | R | R | — | — | — | — | — |
| PERM-INV-006 | E-invoice / GST portal submit and cancel | A | — | A | — | — | — | — | — | — | — |

(Owner is `—` on 005 because the Owner holds 003 and needs no request. Number 004 is not used: invoices are money by nature, so the module has no money-free view.)

### 2.5 Matrix v1 permissions that stay as they are

`RES`, `RATE`, `GST`, `STY`, `FOL`, `PAY`, `SHF`, `HK`, `ROOM`, `EXP`, `CLS`, `APR`, `RPT`, `ADM`: they keep their meaning and grants (they are real core-hotel permissions). What changes is that the new module ids above stop *borrowing* them. `FOL-004` and `FOL-005` stay for the in-stay folio; `INV-00x` covers the invoice and credit-note module.

---

## 3. Migration and seed plan

### 3.1 One new seed migration

| Item | Proposal |
|---|---|
| File | `08 Production/database/migrations/2600_rbac_v2_seed.sql` (2600 is the first free block after Wave 3: 1900 property setup, 2000–2599 Wave 3 modules; it runs after all module seeds. If another Wave 3 agent has already taken 2001, use the next free number; only the order matters) |
| Contents | (1) insert the RST role; (2) insert every `PERM-<MOD>-nnn` row with `kind` and `is_money`; (3) insert grants from the grids above; (4) add `HR-004` scope O for RST |
| Style | Same as 0901 / 1001: one header comment naming the decisions, generated from this document, verified by a coverage test |
| Wave 3 modules | Seeded in this same file even though their tables arrive later, so the Access control screen and the tests already know them. Their grants stay inactive until the module switch is on (DEC-057) |
| Not in the seed | Users and role assignments. No staff are created |

### 3.2 Back-compat, in three steps

| Step | What happens | Risk |
|---|---|---|
| 1. Add | Seed 2600 adds the new ids. **Old grants are not touched.** No behaviour changes yet | None |
| 2. Switch callers | Module by module, the `…Ports.cs` constants change from the borrowed id to the dedicated id. Each switch is its own small change with its own tests. Order: money-hiding modules first (stock, maintenance, handover, guest services), then POS, messaging, yield, configuration | One module at a time; revert is one constant |
| 3. Retire | After all callers have switched and a release has run: remove the now-unused hand-written guards (for example the Reception check in maintenance). Old matrix ids stay seeded because the core hotel still uses them. A later migration may drop borrowed grants that nothing reads | Low |

### 3.3 Test plan

| Test | What it proves |
|---|---|
| Golden grid | The seeded grants equal the grids in this document, cell by cell (generated from the document, same approach as `MatrixCoverageTests`) |
| Parity | Before a module is switched, for each role the old permission set and the new one allow the same actions, except the rows in 3.4 |
| Money invariant | For SUP, HKP, MNT and RST: no grant with `is_money = 1` except `HR-004` (own pay). Fails the build otherwise |
| Reveal check | Each module query returns null for every money field unless the caller holds that module's reveal permission property-wide (extends the existing stock and handover null-money tests) |
| Per-endpoint role test | For each command and query, each of the ten roles is allowed or refused as in the grid (one table-driven test per module) |
| Separation of duty | No-self-approval and limit rules still hold after the switch (`SOD-001/002/004`, Manager ₹1,000, Reception ₹250) |
| Prototype alignment | The in-app policy baseline (`src/domain/streams/admin.ts`) gets the same ids only if the owner wants the prototype to follow (decision D6) |

### 3.4 Deliberate behaviour changes (everything else is identical to today)

| # | Change | Why |
|---|---|---|
| C1 | Cashier no longer sees stock values, laundry rates or part prices (they hold `RPT-002` today, which the stock module treats as money) | Money in stock and maintenance should not ride on a reports permission |
| C2 | Tech admin may edit message templates (`MSG-003`) and change non-money configuration (`CFG-002`) | DEC-058 says so; today the seed blocks it |
| C3 | Manager can no longer be locked out of or into yield by a change to `ADM-003`; yield decisions stay Owner only (`YLD-003`) | Same result, clearer reason |
| C4 | Maintenance and Housekeeper keep handover note access, but cash amounts on notes stay hidden (`HND-004`) | Today it depends on an accident of `HK-001` |
| C5 | New Restaurant role operates POS but cannot settle, approve voids or see day sales | New role |

---

## 4. Decisions for the owner

| # | Decision | Recommended answer |
|---|---|---|
| D1 | Add a **Restaurant (RST)** role, PIN sign-in, owner-assigned, POS operate only (no settle, no void approval, no day sales)? | **Yes.** Without it, waiters need Reception or Cashier rights, which carry money powers |
| D2 | Approve the dedicated-permission-per-module design and the numbering (001 view, 002 act, 003 approve, 004 reveal money, 005+ extras)? | **Yes.** Seed one new migration (2600), keep old grants until callers switch |
| D3 | Cashier loses stock and maintenance money (C1)? | **Yes.** The Cashier does not buy stock or approve work orders |
| D4 | Tech admin may edit message templates and non-money configuration (C2), as DEC-058 already says? | **Yes.** Money, tax, payroll, compliance and approval limits stay Owner only |
| D5 | POS menu prices and tax category (`POS-006`): Owner only, Manager may request? | **Yes.** Prices are money; the Manager proposes, the Owner decides |
| D6 | Should the prototype's policy baseline also be renamed to the new ids now? | **No.** Production first; the prototype is accepted and unchanged. Revisit at M4 |
| D7 | Wave 3 grants: OTA and web booking day-to-day to Reception; commission, gateway and rate decisions to Owner and Manager only; credit-note approval Owner only (no change from DEC-063 G3)? | **Yes.** Same pattern as the rest of the hotel |
