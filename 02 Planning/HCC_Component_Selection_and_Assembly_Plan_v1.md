# HCC component selection and assembly plan

Prepared: 24 October 2026. Version 1.0. Status: proposed implementation specification, grounded in the downloaded source revisions.

## 1. Decision in one paragraph

Keep HCC's existing React desktop/mobile interface, Maroon & Sand design, Capacitor wrapper, native printing/export bridge and fictional acceptance scenarios. Use **small HAIP TypeScript components and selected regression scenarios** as the primary third-party candidates; use **small Open Hotel PMS helpers or interaction patterns only where they fill a demonstrated HCC gap**. Use **Kamra, QloApps and HotelDruid for explicitly recorded behavior requirements and edge cases**, with independent HCC implementations under the project policy below. Keep **miniCal as an archive/reference**, with no planned code import. Build HCC's SQL Server persistence, server authorization, transaction coordination and durable event delivery specifically for HCC. Do not assemble six complete applications or replace a functioning HCC feature merely to increase reuse.

## 2. What this assessment establishes

This is a targeted **static source assessment**, not a complete functional/security audit or a legal opinion. Selected implementation files, imports, root licenses, test descriptions and existing HCC integration points were read. Donor applications were not installed, built or run. Donor test files being present does not mean those tests pass. Recommendations below remain conditional on component-level qualification.

HCC's latest saved handoff (Session 7b) reports 446 passing tests, APK versionCode 4 and installation on the physical Galaxy Tab S7. The tablet now uses the desktop layout in Auto; phones retain the phone layout. That suite was **not rerun during this assessment**. Remaining emulator/tablet role checks, AC-M05–M08, tablet CSV/print and Reset still need acceptance evidence. Use the actual current suite on resumption, not an obsolete count from an earlier conversation.

The source snapshot is reproducible through `V:\Codex\Hotel Control Centre\06 Reference Repositories\repository_inventory.json`. The accompanying `HCC_Component_Source_Evidence_v1.json` records inspected paths and hashes. The six checkouts were verified clean at download time; verify again before extraction.

### Pinned repositories

All paths below are relative to `V:\Codex\Hotel Control Centre\06 Reference Repositories\` unless explicitly marked HCC.

| Repository | Folder | Pinned commit | Root license text observed | Selection |
|---|---|---|---|---|
| HAIP | `haip` | `3a2f1c89139e8d7d915528abac706a512af16970` | Apache-2.0 | Primary extraction candidate; no whole-service import |
| Open Hotel PMS | `open-hotel-pms` | `3f6956a54aa9faef44311f9e943ce7d44b6934e3` | MIT | Secondary helper/UI candidate |
| Kamra | `kamra-pms` | `8c9e63922c81011623b56014cc8e6505539ae100` | AGPL-3.0 text | Behavior reference under current project policy |
| QloApps | `QloApps` | `10251e3591f4e0528a34e1bcf99c30594a93055a` | OSL-3.0; inspected pricing file also carries notice | Behavior reference under current project policy |
| HotelDruid | `hoteldruid` | `c05efc2f9295315f5bf8a8fd04b001ac4ba39d48` | AGPL text; inspected file has a more specific version/proxy notice | Targeted allocation reference only; 2018 snapshot |
| miniCal fork | `minical` | `03cf549916e89170b00f366ac96d91c98cd6cb88` | OSL-3.0 text | Archive; no MVP adoption planned; 2022 snapshot |

Root licensing is not a substitute for checking the exact file, transitive imports, bundled libraries and assets. Before any copy, record the applicable terms, notices and dependencies. Preserve copyright/license notices and record modifications. Do not assume all files inherit the same license. The reference-only classification is a project choice, not a declaration that other forms of use are legally impossible. Do not copy/translate AGPL/OSL source into HCC under the label of independent implementation; first write an HCC behavior specification and implement original code. If actual source reuse is desired, resolve its licensing route explicitly before copying.

## 3. Preserve the existing HCC investment

The current app is more advanced than the original conversation's proposed mockup. These are the first-choice components, subject to baseline verification:

| Existing HCC asset, under `03 Prototype/` | Keep | Production adaptation |
|---|---|---|
| `src/features/`, `src/mobile/`, `src/app/mobile/` | Screens, task flows and presentation | Replace whole-State reads with authorized, paged API view models gradually |
| `src/app/registry.ts`, feature `register.tsx` files | Feature registration/navigation pattern | Server capabilities inform visibility; server still authorizes requests |
| `src/app/native.ts` | Android back, printing and export integration | Test with real API sessions, document authorization and connection failures |
| `src/styles.css`, `src/styles/` | Maroon & Sand styling and phone rules | Preserve 48px targets and 16px phone text; fix observed issues only |
| `src/domain/money.ts`, selected pure domain helpers | Existing INR/date contracts and known behavior | Formalize overflow, rounding, validated dates and clock ports |
| `src/domain/streams/admin.ts` | Permission policy vocabulary and evaluation scenarios | Server policy enforcement and sessions must be new production implementations |
| `src/fixtures/history/`, `tests/` | Synthetic regression/scenario assets | Keep isolated in test/demo environments; never import FY26 fixtures as live records |
| `src/services/mock/handlers/` | Behavior reference and mock mode | Extract domain rules; replace in-memory writes with module-owned commands and transactions |
| `src/app/storage.ts` | Demo persistence | Not a production authority; no automatic real-data fallback to IndexedDB |
| `04 Android/` | Existing Capacitor build, app identity and native settings | Add approved server connection/session behavior; preserve upgrade/data handling |

The app currently imports functions/types from `services/mock`, and `Ctx.s` exposes a whole `State`. Therefore this is **not** a one-line mock-to-HTTP swap. Build a typed client interface and move one feature/read model at a time. Do not send the whole hotel state, user directory, payroll or financial records to every client and filter afterward.

## 4. Component-level selection

Decision labels: **KEEP** existing HCC; **ADAPT** small donor extraction after qualification; **REFERENCE** behavior/testing input, original HCC implementation; **DEFER** useful later; **REJECT** not part of the planned assembly. Effort labels are relative investigation/adaptation cost, not delivery promises.

### H01 — Reservation transition validation

- Source: `haip/apps/api/src/modules/reservation/reservation-state-machine.ts` and `reservation-state-machine.spec.ts`.
- Observed: a compact transition table, `validateTransition`, `assertTransition`, `getValidTransitions`; imports NestJS `BadRequestException`. Donor statuses include `assigned`, `stayover` and `due_out`.
- Decision: **REFERENCE first; ADAPT validator shape only if it simplifies HCC**. HCC already has Held/Confirmed/In house/Departed and expiry semantics. Do not replace them with donor enums.
- Target: production reservation domain `reservation-policy.ts`; use HCC domain errors and a documented state map. The domain must not import NestJS.
- Acceptance: Held expiry; allowed/forbidden transitions; no-show timing; repeated commands; checkout is a stay operation, not just an enum edit. Do not infer business day from midnight.
- Priority/cost: P1, low extraction cost, medium semantic reconciliation. Keep HCC if there is no measurable benefit.

### H02 — Length-of-stay and occupancy pricing helpers

- Source: `haip/apps/api/src/modules/rate-plan/rate-pricing.util.ts` plus `rate-pricing.util.spec.ts`.
- Observed: dependency-free selection functions for LOS tiers and occupancy bands; fixed/percentage adjustment; `number` arithmetic and JavaScript date arithmetic.
- Decision: **ADAPT candidate**, after comparing HCC's existing yield module. Highest-value small donor candidate for advanced pricing; not required for the first real guest journey.
- Target: pricing domain `rate-adjustments.ts`. Keep INR integer paise; perform percentage rounding once under the HCC policy. Validate dates separately. Define disjoint occupancy bands and tie precedence; donor inclusive band endpoints must not make 50% match two rules unpredictably.
- Acceptance: tier boundaries 6/7/14/21 nights; overlapping/invalid bands rejected; 0/100% occupancy; negative/zero outcomes; fixed discount greater than base rate; combination order; integer rounding; existing accepted quotes remain unchanged.
- Priority/cost: P2, low-to-medium. No wholesale rate-plan service import.

### H03 — Refund remainder and payment allocation rules

- Sources: `haip/packages/shared/src/payment-ledger.ts`; `haip/apps/api/src/modules/payment/payment-ledger.ts`; `payment-ledger.spec.ts`; `haip/packages/booking-requests/src/domain/booking-request-money.ts` and its money tests.
- Observed: shared refund helpers use decimal.js; API helpers also import Drizzle/payment schema. Booking-request money code validates cumulative allocations but imports NestJS exceptions and has a broader currency/installment model.
- Decision: **ADAPT a small financial kernel if it beats current HCC helpers; REFERENCE the database predicates and regression cases**. Do not import gateway or booking-request packages as dependencies.
- Target: payments domain `receipt-allocation.ts` and `refund-policy.ts`. One canonical HCC ledger representation: retain positive receipt, append linked refund/return, distinguish deposits and company receivables. Convert donor decimal major units explicitly at the boundary; never multiply/divide implicitly in random callers.
- Acceptance: ₹1,000 receipt minus ₹250 and ₹750 refunds; reject the next paisa; reject negative refund input; cumulative allocation limits; two concurrent refunds; payment versus refundable deposit; parent status change does not erase a receipt from reports. Copied tests carry provenance too.
- Priority/cost: P1, medium. The two-function helper alone is small; its largest value is identifying reconciliation cases.

### H04 — Property timezone date helper

- Source: `haip/apps/api/src/common/date/property-business-date.ts` (`calendarDateInTimeZone`).
- Observed: timezone-aware date formatting, but invalid timezone silently falls back to UTC.
- Decision: **ADAPT candidate** for calendar-date display/conversion only, not business-day authority.
- Target: shared clock/date adapter. Inject time; reject invalid property timezone on configuration; business date remains explicit persisted state advanced by authorized closing.
- Acceptance: Asia/Kolkata UTC-boundary examples; invalid configuration error; local midnight without business-date advancement; deterministic test clock.
- Priority/cost: P1, low. This helper does not implement closing or fiscal-date policy.

### H05 — Reservation/availability service and race tests

- Sources: `haip/apps/api/src/modules/reservation/reservation.service.ts`, `availability.service.ts`, `reservation-race.spec.ts`, `check-in.spec.ts`, `check-out.spec.ts`, `move-room.spec.ts`.
- Observed: direct Drizzle imports, cross-domain tables, transaction callbacks and references to folio/payment/room/webhook/policy services. The race spec includes mocked conditional-update claim outcomes.
- Decision: **REFERENCE application rules and test scenarios; REJECT whole-service transplant**.
- Target: HCC inventory/reservation application commands plus SQL Server adapters. Build real concurrent SQL integration tests; mocked row-claim tests do not prove database overlap protection.
- Acceptance: room and room-type capacity, dated blocks, expired holds, group pickup, adjacent stays, overlapping moves, cancellation versus check-in race, rollback preserving old allocation.
- Priority/cost: P1, high integration cost. Prefer new orchestration over unwinding the donor's persistence graph.

### H06 — Folio and night-audit behavior

- Sources: `haip/apps/api/src/modules/folio/folio.service.ts`, `folio-stay-amendment.spec.ts`, `folio-charge-validation.spec.ts`; `haip/apps/api/src/modules/night-audit/night-audit.service.ts` and `night-audit.service.spec.ts`.
- Observed: transaction-aware charge operations, accepted-price snapshot tests, source-key/idempotency tests; services depend on Drizzle and many other donor services. Tests cover repeated posting, failed audit status and saved nightly prices.
- Decision: **REFERENCE scenarios and carefully selected rule fragments; REJECT wholesale services**.
- Target: HCC accepted quote snapshot, immutable charges/adjustments, resumable business-day close coordinator, SQL posting uniqueness and outbox.
- Acceptance: post once per canonical night; manual service line does not suppress room charge; charge/tax snapshot unaffected by later rate edits; interrupted close resumes without duplicate effects; pending approvals/operational exceptions handled under HCC rules.
- Priority/cost: P1, medium for scenarios, high for service adaptation. Do not copy nightly rate averaging as a substitute for per-night accepted prices.

### H07 — Room status and housekeeping service

- Sources: `haip/apps/api/src/modules/room/room-status.service.ts`; `haip/apps/api/src/modules/housekeeping/housekeeping.service.ts`.
- Observed: donor room status combines occupancy/cleaning/service conditions into values such as `occupied`, `vacant_dirty`, `guest_ready`, `out_of_order`; direct database updates and webhooks.
- Decision: **REFERENCE validation cases; KEEP HCC's separate occupancy, housekeeping and block dimensions**.
- Target: new transactional stay/inventory updates and independent housekeeping tasks. Do not copy the donor room enum/table as HCC's canonical model.
- Acceptance: vacant + dirty; clean + blocked; stayover cleaning; rejected inspection; exactly one checkout task; dirty state durable before asynchronous task delivery.
- Priority/cost: P1, medium. No direct reuse of donor persistence.

### O01 — Select a room-night segment for check-in

- Source: `open-hotel-pms/src/lib/checkin-room-selection.ts` and `.test.ts`.
- Observed: a small helper picks an exact business-date night, else earliest future, else latest past; validates date shape but not full calendar validity.
- Decision: **DEFER / ADAPT only for a proven multi-room-segment display need**. It is not authorization to occupy a room.
- Target: HCC room-segment read model. The server checks the actual arrival interval, readiness and conflicts independently; out-of-stay data must not trigger automatic check-in.
- Acceptance: exact date, gap, future/past-only segments, invalid dates, duplicate conflicting segments; no outside-stay mutation.
- Priority/cost: P2, low; skip if existing HCC segments already satisfy the need.

### O02 — Checkout release-date helper

- Source: `open-hotel-pms/src/lib/checkout-night-release.ts` and `.test.mts`.
- Observed: when a consumed night exists, returns its following calendar date; otherwise business date. This is a donor policy, not a universal checkout rule.
- Decision: **REFERENCE / DEFER**, unless HCC explicitly accepts the semantics for early checkout or split segments.
- Target: inventory release policy derived from actual stay segments and approved early-departure billing rules.
- Acceptance: late checkout, early departure, previously consumed night, room changes and property business date. Release cannot delete past room history or erase incurred charges.
- Priority/cost: P2, low code volume, material policy risk.

### O03 — Room planner drafts and grid

- Sources: `open-hotel-pms/src/components/room-planner/use-draft-engine.ts`, `review-panel.tsx`, `room-grid.tsx`.
- Observed: draft action hook uses React plus donor types, `Date.now` and random IDs; grid imports vehicle helpers and donor calendar models and uses Tailwind classes.
- Decision: **KEEP existing HCC calendar. DEFER a bounded planner-preview adaptation** only if the owner needs multi-action room planning.
- Target: HCC calendar draft/view model with injected clock and existing styling. Apply proposed moves through server commands with version checks; preview is not committed inventory.
- Acceptance: cancel draft without mutations; invalid swap preserves both stays; stale preview fails safely; keyboard alternative; mobile alternative; no hidden vehicle/Next/Tailwind dependencies imported.
- Priority/cost: P3, medium-to-high. Not a UI replacement project.

### O04 — Housekeeping task card interactions

- Source: `open-hotel-pms/src/components/pms/housekeeping/extra-tasks/task-card.tsx`.
- Observed: action callbacks, loading state and elapsed-time display; donor types/Button/Tailwind styling, real wall-clock timer and actions including cancel/delete.
- Decision: **KEEP HCC UI; REFERENCE or ADAPT a small interaction if needed**. Do not import the whole component tree merely for a timer.
- Target: HCC task card with busy/error state, injected demo clock and existing permission rules. Completed operational history is preserved; imported delete buttons do not imply delete permission.
- Acceptance: retry, pending action lock, denied task scope, timer across pause/resume, accessible touch targets.
- Priority/cost: P3, low-to-medium, optional.

### O05 — Folio fees, tax invoice and printing

- Sources: `open-hotel-pms/src/lib/folio-fees.ts`, `src/lib/folio/printFolioHtml.ts`.
- Observed: fee helper imports server Supabase and satang money conversions, with Asia/Bangkok default. Print helper includes Thai labels/Buddhist-year dates and a `MAX_VISIBLE_ROWS = 16` row slice.
- Decision: **REJECT direct finance/print transplant; KEEP HCC printing and native bridge**. Reference pagination/escaping tests only after examining them individually.
- Acceptance for HCC: INR, correct dates, safe HTML escaping, all invoice lines printed across pages, totals reconcile; native print/share and CSV still work.
- Priority: P1 exclusion. A visually attractive invoice is not sufficient if rows or locale semantics differ.

### K01 — Kamra cash desk and closing checklist

- Sources: `kamra-pms/kamra/cashier.py`, `night_audit.py`, `tests/test_cashier_terminals.py`, `tests/test_night_audit.py`.
- Observed: unified front-office/POS cash-trail concept; open-till and open-POS checks; saved closing packs. Cashier code also has agent/admin bypass behavior; nightly auto-roll policy is configurable in that product.
- Decision: **REFERENCE behavioral requirements, original HCC implementation**. Useful for the current POS/cashier gap, not a Python/Frappe port.
- Target: an approved cashier/outlet model and HCC closing preflight. No agent bypass, no automatic close through critical unresolved exceptions by default.
- Acceptance: outlet cash assigned to the correct till, room-service transfer not a second receipt, bank transfers excluded from drawer, refunds and cash drops traced, closed tills immutable, recovery without repeated money effects.
- Priority/cost: P1, medium specification effort. Owner must choose one shared till versus separate outlet tills; current POS omission is not resolved by blindly adding all outlet receipts to one total.

### K02 — Kamra folio/guest workflows

- Sources: `kamra-pms/kamra/folio.py`, `reservation_state.py`; existing docs-site registration/reservation/housekeeping material.
- Observed: line tax and localization hooks, Frappe persistence and some Decimal-to-float conversions; reservation-state vocabulary differs from HCC.
- Decision: **REFERENCE feature checklist only; independent HCC finance and guest implementation**. Do not copy tax percentages or treat the source as current Indian legal guidance.
- Acceptance: current approved tax configuration, line snapshots, guest-data minimization, refund/deposit distinction and retained invoice correction history.
- Priority: P2. Actual tax/legal requirements must be verified separately before live billing.

### Q01 — QloApps rate restrictions and room blocks

- Sources: `QloApps/modules/hotelreservationsystem/classes/HotelRoomTypeFeaturePricing.php`, `HotelRoomTypeFeaturePricingRestriction.php`, `HotelRoomTypeRestrictionDateRange.php`, `HotelRoomDisableDates.php`.
- Observed: date/rule pricing fields, restriction records, dated room-block checks; PHP ObjectModel, Db and Context dependencies; the inspected pricing source explicitly carries OSL notices.
- Decision: **REFERENCE the rule catalog and independently implement HCC behavior**. No PHP/PrestaShop/MySQL runtime in HCC.
- Target: approved yield rules and dated inventory block policy. Start by completing the existing HCC `yldCheckStay` integration, not by importing a second pricing engine.
- Acceptance: min-stay, closed-to-arrival, effective dates, precedence, booking/date-change/extension/group-pickup entry points, date-range boundaries and open-stay conflicts.
- Priority/cost: P1 for HCC enforcement gap, P2 for extra pricing features.

### D01 — HotelDruid allocation scenarios

- Sources: `hoteldruid/includes/funzioni_appartamenti.php`, `funzioni_quadro_disp.php`, `funzioni_ins_prenota.php`.
- Observed: legacy PHP procedural allocation code with extensive permissions/rate inputs; local snapshot's last commit is in 2018.
- Decision: **REFERENCE only when investigating a specific unsatisfied allocation case**; otherwise defer. Do not translate long procedures line-by-line.
- Target: original scenario catalog for room constraints, split moves, adjacent stays and capacity. Implement with HCC invariants and SQL Server concurrency.
- Priority/cost: P3; low immediate incremental value over HCC + HAIP scenarios.

### M01 — miniCal

- Sources: `minical/api/application/models/availability_model.php`, `booking_model.php`, `public/js/calendar.js`.
- Observed: CodeIgniter model and MySQL-specific availability SQL; selected fork snapshot is from 2022.
- Decision: **REJECT for planned code assembly; archive for reference**. No uniquely necessary component was established in this targeted review. That is not a claim that every file is defective.
- Revisit only for a named HCC requirement unmet by existing code and higher-priority donors. Never copy bundled calendar libraries without verifying their separate licenses/version/support.

## 5. Mapping all 26 functional areas

| Area | First choice | Donor contribution | Production work still required |
|---|---|---|---|
| 01 Rooms/tariffs | HCC masters/settings | H02, Q01 | Catalog, effective prices, constraints and migration |
| 02 Reservations | HCC workflow | H01/H05 | Transactions, holds, allocation and typed APIs |
| 03 Availability/calendar | HCC room board/calendar | H05; O03 later | Concurrent inventory and scoped updates |
| 04 Guest registration | HCC forms/mobile steps | K02 checklist | Secure documents, privacy, validated requirements |
| 05 Stay/check-in/out | HCC flow | H05/H07; O01/O02 if needed | Atomic orchestration, room segments and corrections |
| 06 Folios/invoices | HCC presentation | H03/H06, K02 | Ledger persistence, validated taxes, numbering/credits |
| 07 Cashier/payments | HCC cash view | H03/K01 | Receipt allocation, multi-till/shift policy, reconciliation |
| 08 Housekeeping | HCC mobile/desktop | H07; O04 optional | Server scope, event-driven tasks, durable readiness |
| 09 Expenses | HCC expense module | No required donor | Approvals, attachments, cash disbursement integration |
| 10 Night audit | HCC closing screen | H06/K01 | Restartable coordinator, immutable closing snapshots |
| 11 Reports | HCC views and report definitions | Selected donor test ideas only | Authorized server queries, pagination and reconciliation |
| 12 Access/audit/backup | HCC access vocabulary/UI | No donor security stack selected | Real identity, fine-grained backend policy, audit and restore |
| 13 Advanced pricing | HCC yield | H02/Q01 | Enforced restrictions and immutable accepted quotes |
| 14 Groups/corporate | HCC groups | H05 scenarios as relevant | Group allocation, company credit and master folios |
| 15 Maintenance | HCC maintenance | H07 readiness cases | Work orders integrated with dated inventory blocks |
| 16 Linen/stock | HCC inventory | No import established | Transactional movements, counts, vendor/laundry records |
| 17 HR/payroll | HCC prototype screens | No import established | Separate verified statutory rules and sensitive access |
| 18 Guest services | HCC services | No import established | Scoped tasks, SLAs, charge links |
| 19 Checklists/handover | HCC handover | O04 optional interactions | Durable assignments/acknowledgements |
| 20 Accounting | HCC prototype concepts | H03/K01 reconciliation scenarios | Professionally validated ledger and postings; later release |
| 21 OTA channels | HCC simulated UI | No provider adapter selected | Authorized provider integration and reconciliation; later |
| 22 Booking site | HCC preview | No donor booking app selected | Separate public edge and secure internal integration; later |
| 23 POS | HCC POS mock | K01 cash/till model | Inventory/folio/cash integration after scope decision |
| 24 Messaging | HCC simulated UI | No donor notification service selected | Provider consent, credentials, retries; later |
| 25 OCR | HCC simulated capture | No donor OCR selected | Actual capture/provider/privacy approval; later |
| 26 Assistant | HCC rule-based preview | No donor AI weights/services selected | Explicitly scoped later; no autonomous financial posting |

No SCC or ETP implementation is included in the six local clones. HCC already contains original expense/workforce mock workflows. Do not make the current assembly depend on uninspected first-party source; ETP remains deferred.

## 6. Assembly architecture

Use the unused `08 Production/` directory for the future production workspace; `04 Android/` is already occupied and must not be repurposed. Qualification experiments go in `07 Integration Lab/`, outside the prototype and donor clones.

```text
07 Integration Lab/
  qualifications/          one isolated extraction/comparison per candidate
  evidence/                original tests, adapted tests, dependency/license records
08 Production/            only after production construction is authorized
  apps/api/               NestJS bootstrap, identity/session and HTTP adapters
  apps/web/               retained HCC UI adapted through typed feature clients
  modules/<context>/
    domain/               original rules + accepted attributed extractions
    application/          authorized use cases and transaction orchestration
    ports/                persistence, clock, documents, external service interfaces
    adapters/sqlserver/   new SQL Server implementation
    contracts/            public commands, queries and events
  packages/contracts/     small transport schemas and shared identifiers
  database/migrations/    HCC schema, not donor migration conversion
  tests/                  domain, contract, SQL integration and end-to-end
  deployment/windows/     host service, TLS, backup, restore and upgrades
  third-party/            notices, provenance and approved dependency inventory
```

Presentation → typed feature client → authenticated API → authorization/application command → domain policy + module ports → SQL transaction → durable outbox → idempotent consumers.

Reuse code inside the appropriate domain or adapter, with provenance. Do not create a permanent parallel `haip` application runtime or a miscellaneous business-logic `shared` dump. No production module imports source directly from `06 Reference Repositories` or the prototype's mock service.

Transaction ownership must be explicit. Checkout synchronously commits stay departure, inventory release and dirty readiness through module-owned interfaces; a transactionally persisted event can create the housekeeping task afterward. It must never briefly advertise a dirty room as ready. Async reports cannot decide whether an allocation or refund is permitted.

## 7. Qualification gate: when a component is genuinely worth importing

For each proposed import create a short qualification record:

1. Name the missing HCC behavior. Compare it with current HCC implementation and tests.
2. Pin donor commit, exact files, local content hashes, transitive imports and license notices.
3. Record `reuse unchanged / adapt / original implementation / reference only / reject`, with reasons.
4. Compare full costs: extraction + dependencies + rewrite + tests + future maintenance versus extending HCC. Do not invent percentage savings from line counts.
5. Build a bounded extraction in `07 Integration Lab`, not an installed donor PMS.
6. Run relevant donor tests when practical; record missing infrastructure honestly. Add HCC tests for money, dates, statuses, authorization and adverse inputs.
7. Inspect the accepted diff. No unexplained framework dependencies, cloud credentials, hidden network calls, copied policy bypasses or executable setup scripts.
8. Record source-to-destination mapping and notices before merging; identify exactly how to revert the import without losing data.

Fail the candidate if it requires a second authoritative DB, loses HCC invariants, hides tax/policy changes, has unresolved licensing, or offers no useful advantage over current code. Passing this gate means accepted for a particular purpose, not that the entire donor repository is approved.

## 8. Proposed implementation order

| Work package | Concrete result | Dependency / gate |
|---|---|---|
| A0 Baseline | Current tests/build result, APK evidence status, source hashes, gap and approval register | Start here; no donor import yet |
| A1 Qualification | H01–H04 and O01/O02 comparisons; accept/reject records and focused extraction tests | Can proceed independently of production deployment |
| A2 HCC integration gaps | Enforced yield restrictions; fine-grained permission coverage; cashier/POS design decision and approved correction | Preserve existing feature/phone behavior; policy choices recorded |
| A3 Contracts/schema spike | Identity model, API/DTO contracts, SQL Server library spike and transaction proof | Detailed design; construction scope explicitly authorized |
| A4 Platform | Auth, scoped queries, money/date kernel, migrations, audit, idempotency, outbox | Production construction authorization |
| A5 Real guest slice | Room → booking → registration → check-in → nightly charge → receipt → invoice → checkout → cleaning → report | Actual SQL Server, two independent clients |
| A6 Remaining MVP | Expenses, cashier/closing, reports, masters, roles, corrections | Critical slice tests passed |
| A7 Devices/release | Windows service, LAN/TLS, APK server connection, restore/upgrade and pilot | Owner's live-use acceptance |
| A8 Later modules | Groups/yield/POS/accounting and optional integrations | Explicitly selected scope; mock existence is not production completion |

The saved plan requires prototype acceptance before production construction. This document does not fabricate that acceptance. Claude should complete a concrete readiness package, proceed with authorized independent qualification/design, and ask only for the actual unresolved gate. Once the owner authorizes a milestone, do its routine implementation and tests without asking permission for each file.

## 9. Decisions requiring real hotel input

- **Till ownership:** shared front-office/POS till or separate outlet tills with explicit transfer? Prepare both accounting examples; do not count one receipt twice.
- **Yield enforcement:** which rate plans/restrictions apply to direct, corporate and group pickup, and who can override with approval?
- **Checkout corrections:** adjustment-only versus controlled reopening; unpaid company-credit policy.
- **Tax/invoice requirements:** applicable rates/classifications, effective dates, numbering and credit-note rules. No donor hard-coded rate is accepted as authoritative.
- **Tablet touch acceptance:** the owner already chose desktop layout for tablet-width devices and phone layout for phones (Session 7b); preserve this decision and the Display toggle. Verify touch comfort and remaining workflows on the Tab S7 rather than reopening the layout decision.
- **Release scope:** first 12 production areas versus a separately approved expansion. Twenty-six prototype modules do not imply twenty-six production integrations now.

## 10. Completion evidence

Claude must provide the qualification register, import/notice manifest, changed-file list, test results, actual SQL transaction evidence, API permission tests, browser/Android walkthrough evidence and deployment/recovery notes appropriate to the milestone. Label unrun tests and remaining mocks. Do not present this static assessment, a donor README or the existing APK as proof that production assembly is complete.
