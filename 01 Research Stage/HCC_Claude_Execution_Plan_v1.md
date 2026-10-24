# Hotel Control Centre — Claude Execution Plan

Version: 1.0  
Prepared: 24 October 2026  
Owner: Sagar  
Source: `Hotel_Control_Centre_Chat_Export.md` in this folder  
Immediate objective: specify, build and demonstrate an interactive hotel-operations prototype before production development.

## 1. Your assignment

Act as the implementation lead for Hotel Control Centre (HCC), an internal hotel/lodging operations application for the owner's business in India. Deliver coherent workflows, maintainable software and reproducible evidence of what works.

Read the source conversation completely before implementation. It is a reconstructed planning record, not a verbatim transcript or proof that any external repository works. Preserve its accepted direction and distinguish it from assumptions and proposals in this plan.

Work in this order:

1. Convert the source and this brief into a traceable specification, decision register, preliminary permission matrix and prototype backlog.
2. Present the prototype structure, walkthrough and proposed defaults together for owner approval. Prepare the complete review package before requesting that approval.
3. Once approved, build a working React prototype using fictional records and mock services. Complete and test the connected workflows, including failure and permission cases.
4. Demonstrate the prototype, address feedback and obtain explicit workflow/UI acceptance.
5. Only then prepare the detailed production contracts, database design and implementation estimate. Obtain authorization for production construction before building the backend.
6. Implement a real end-to-end vertical slice before expanding the remaining MVP.

The approval of this execution brief alone does not mean the actual screen structure, hotel policies or production release have been approved. Record approvals by date, artifact version and scope. Do not repeatedly ask about decisions already approved.

Do not end a delivery with a design description when the authorized milestone requires working software. Equally, do not expand a prototype milestone into production backend work.

## 2. Decision status and precedence

Use four statuses throughout the project: **Agreed**, **Proposed**, **Needs owner decision**, and **Unverified**. Changes requested later by the owner take precedence; record their impact rather than silently rewriting history. Where this plan fills a gap in the conversation, treat the added detail as a proposed implementation specification until approved.

| Area | Established direction | What remains open |
|---|---|---|
| Purpose | Internal hotel operations, local-network-first | Actual property, room count, operating policies and staffing |
| Scope | 26 functional areas; first 12 form the intended MVP | Detailed release boundary and acceptance of any deferred subfeatures |
| Architecture | Modular monolith, hexagonal modules, bounded contexts and internal events | Precise boundaries, contracts and orchestration |
| Backend | NestJS + TypeScript is the preferred direction | Supported versions, database library and SQL Server compatibility |
| Database | SQL Server 2025 Express is the authoritative production database | Schema, transactions, indexes, migrations, capacity and recovery |
| Frontend | Responsive React + TypeScript | Component system, layout and visual approval |
| Devices | Windows browser and Android browser/PWA over hotel LAN | HTTPS/certificates, real devices, installation and deployment |
| Packaging | Browser first; Windows wrapper optional | Whether a wrapper has an actual operational requirement |
| Access | Master Access Control Panel, role/action/scope controls | Final role assignments, thresholds and delegation rules |
| Configuration | Separate Hotel Configuration Control Panel | Setting values, effective dates and approval policies |
| Reuse | Selective reuse and independent implementation | Exact files, revisions, licensing, quality and adaptation cost |
| Offline | No second authoritative booking or financial store | Restricted offline housekeeping is a later design exercise |
| Next milestone | Clickable prototype before production | Sitemap, workflows, fixture set and screen acceptance |
| ETP | Private source sharing is deferred | No assumed access, integration or reviewed internals |
| Schedule | Earlier estimates are provisional | Bottom-up estimate after scope and compatibility evidence |

Do not reopen established architectural choices merely because another technology is familiar. If evidence reveals a material incompatibility, write a short decision record with options and seek a targeted decision.

## 3. Delivery sequence and approval gates

| Milestone | Deliverable | Completion evidence | Boundary |
|---|---|---|---|
| M0 — Specification | Requirements, assumptions, sitemap, workflows, preliminary access matrix, fixtures and backlog | Every MVP area mapped to screens and acceptance cases; unresolved decisions visible | Owner approves prototype structure and walkthrough |
| M1 — Prototype foundation | App shell, design system, mock services, scenario reset, user/role preview | Starts from a clean checkout; navigation, shared state and error states work | No SQL Server or production identity required |
| M2 — Connected guest journey | Booking through checkout, housekeeping and reports | Repeatable scenario with reconciled figures and state transitions | Fictional data only |
| M3 — Complete prototype | All 12 MVP areas, permissions, configuration, expenses, closing and exception scenarios | Acceptance matrix, screenshots, automated results and limitations | Owner accepts UI/workflows after demonstration |
| M4 — Production design | Detailed domains, contracts, schemas, financial rules, security/deployment design and estimate | Technical design tied to approved requirements; open policy blockers identified | Owner authorizes backend construction |
| M5 — Real vertical slice | One complete hotel journey on a SQL Server test database | Concurrency, transaction recovery, permissions and financial reconciliation pass | Expand only after critical invariants pass |
| M6 — MVP completion | Remaining agreed production features | Traceable acceptance coverage, Windows deployment and restore rehearsal | Pilot readiness decision |
| M7 — Pilot and release | Trained users, opening balances, controlled rollout and support runbook | Reconciled trial operation, signed readiness and recovery evidence | Explicit live-use authorization |

At each boundary report: delivered items, how to run/review them, passed checks, failed or unrun checks, known limitations, decisions required and the next bounded milestone. A screenshot proves layout, not backend correctness. A mock payment proves the workflow, not payment settlement.

## 4. Working files and traceability

Inspect the workspace and applicable repository instructions first. Preserve existing files. Propose a layout if another codebase is already present; do not overwrite it or create nested repositories casually.

Suggested layout under `V:\Codex\Hotel Control Centre`:

```text
01 Research Stage/                 original conversation and this handoff
02 Planning/                       specifications and decision records
03 Prototype/                      runnable React prototype
04 Production/                     created only when authorized
05 Delivery Evidence/              test results, screenshots and walkthroughs
```

Create these documents progressively; do not produce empty documents solely to satisfy a file list:

| Artifact | Required content | Due |
|---|---|---|
| HCC_Master_PRD_v1.md | Users, scope, workflows, nonfunctional requirements and requirement IDs | M0 |
| HCC_Decision_Register.md | Status, source, owner, rationale, affected features and decision date | M0 onward |
| HCC_Prototype_Spec_v1.md | Sitemap, screen states, fields, interactions and responsive behavior | M0 |
| HCC_RBAC_Matrix_v1.md | Resource/action/scope/field/threshold rules and approval paths | Preliminary M0; final M4 |
| HCC_Business_Workflow_Spec_v1.md | States, commands, preconditions, effects, denials and exceptions | Prototype M0; production M4 |
| HCC_Fixture_and_Scenario_Catalog.md | Fictional records, deterministic clocks and expected totals | M0–M2 |
| HCC_Acceptance_Matrix.md | Requirement → screen → workflow → test → evidence → status | M0 onward |
| HCC_Source_Reuse_Register.md | Provenance, revisions, files, licensing, modifications and tests | Before any imported code |
| HCC_Architecture_and_Contracts.md | Context ownership, ports, transactions and event catalog | M4 |
| HCC_SQL_Schema_v1.md | Tables, keys, ownership, types, constraints, indexes and migration strategy | M4 |
| HCC_Financial_Rulebook.md | Money, tax, postings, advances, deposits, settlement and corrections | Draft M0; validated before production finance |
| HCC_Quality_Gates.md | Prototype and production checks, thresholds and evidence rules | M0 onward |
| HCC_Release_Runbook.md | Install, TLS, accounts, backups, restore, upgrade and incident recovery | Draft M4; rehearsed M6 |
| HCC_Delivery_Summary.md | Current milestone, completed scope, failures, limitations and next steps | Every delivery |

Assign stable IDs such as `REQ-BOOK-001`, `WF-CHECKOUT-001`, `PERM-REFUND-001`, `AC-014` and `DEC-021`. Preserve the original DEC-01–DEC-20 references from the conversation. Every new requirement must say whether it comes from the conversation, owner feedback or this plan's proposed detail.

## 5. Questions and provisional operating model

Ask one consolidated set of material operating questions during M0. Separate questions needed now from questions that can wait until production. Do not ask for private data to seed the prototype.

| Topic | Information needed | Proposed prototype default | Production blocker? |
|---|---|---|---|
| Property | Room count, floors, room categories and legal entity | One fictional property, 24 rooms, 3 floors, 3 room types | Yes for live setup |
| Users | Staff roles, shift patterns and responsibilities | Owner, manager, reception, cashier, supervisor and housekeeper demo users | Yes for real access |
| Currency and time | Currency, timezone, business-day rules | INR, Asia/Kolkata, explicit simulated business date | Yes for posting rules |
| Stay times | Check-in/out, grace, early/late charges and day use | Editable sample times; exceptions need approval | Yes |
| Payment | Collection channels, advance/deposit policy and company credit | Manual cash/UPI/card recording; advances and deposits separate | Yes |
| Checkout | Unpaid departure, disputes and invoice corrections | Block outstanding balance unless authorized approved credit exists | Yes |
| Discounts/refunds | Role limits and approval authority | Configurable demonstration limits; no self-approval | Yes |
| Tax/invoices | Applicable classifications, rounding, invoice series and correction rules | Explicitly synthetic tax rules and sample invoice series | Yes; qualified validation |
| Housekeeping | Inspection requirements and room release | Completion followed by supervisor inspection | Yes for operating policy |
| Availability | Holds, blocks, overbooking and room-type allocation | No exclusive room overlap; timed holds modeled explicitly | Yes |
| Documents | Required guest data, permitted ID collection and retention | Fictional metadata; no real identity images | Yes |
| Backup | Acceptable loss/downtime, destinations and responsible person | Display a clearly simulated backup status | Yes |
| Devices | Windows host, Android devices and LAN conditions | Browser first; emulated desktop/tablet/mobile layouts | Yes for deployment |

Document defaults as assumptions, not as statements about Sagar's hotel. Continue independent planning while waiting. Unanswered questions may use labeled defaults for the prototype, but elapsed time is not approval for a production policy.

## 6. Scope: all 26 areas retained

MVP means the first 12 functional areas. The prototype must demonstrate their primary workflow and meaningful exception handling. It need not reproduce every production integration or all advanced variants.

| # | Functional area | Prototype requirement | Production/release boundary |
|---|---|---|---|
| 01 | Hotel, rooms and tariffs | Editable hotel/room/type records; rates; occupancy limits; dated block/unblock | Full master validation; propose explicit phasing for sophisticated rate combinations |
| 02 | Reservations | Search availability; hold/confirm/edit/cancel/no-show; dates, assignment, source, advance | Transactional allocation; expiry, rate and cancellation policies |
| 03 | Room rack/calendar | Floor grid, timeline, filters, arrivals/departures and room panel | Cross-device updates; server revalidation; accessible move alternative |
| 04 | Guests/registration | Fictional guest search/create, party members, masked ID metadata, company details | Secure documents, retention, duplicates/merge and applicable reporting |
| 05 | Stay/check-in/out | Check-in, room change, extension, checkout and denied exceptions | Atomic effects and recoverable correction paths |
| 06 | Folio/GST billing | Itemized charges, discounts, advances, deposits, split payment, sample invoice/credit note | Validated tax, numbering, rounding, correction and settlement rules |
| 07 | Payments/cashier | Manual receipts, mixed tenders, references, refunds, shift opening/close and cash difference | Reliable posting/reversal and reconciliation; no presumed gateway integration |
| 08 | Housekeeping | Assignment, start, checklist, complete, reject/inspect, ready and repair flag | Scoped users, timestamps, duplicate-event safety and real cross-device state |
| 09 | Expenses/petty cash | Vendor/category, draft/approve/post/reverse, receipt metadata and cash linkage | SCC adaptation after inspection; secure documents and duplicate controls |
| 10 | Night audit | Preflight exceptions, nightly charges, close, retry and correction request | Restart-safe posting, immutable close and explicit correction/reopen policy |
| 11 | Owner reports | Operational totals, revenue versus receipts, balances, expenses, tax and exports | Reconciled report definitions, authorized export and realistic performance |
| 12 | Access/audit/backup | Editable roles, user enable/disable, scopes, approvals, audit viewer and simulated backup UI | Server enforcement, credentials/sessions, durable audit, real backup/restore |
| 13 | Advanced tariff/yield | Backlog and dependencies | Phase 2 |
| 14 | Group/corporate management | Backlog; simple company billing context where required by MVP | Phase 2 full blocks, master folios and credit controls |
| 15 | Maintenance management | MVP room block/repair handoff only | Phase 2 full job cards and preventive schedules |
| 16 | Linen/laundry/stock | Backlog | Phase 2 |
| 17 | HR/payroll | Demo user/staff identities only | Phase 2 attendance, leave, payroll and statutory rules |
| 18 | Guest services/complaints/lost property | Manual service charge input in folio only | Phase 2 complete task/service workflows |
| 19 | Checklists/shift handover | MVP housekeeping and cashier essentials | Phase 2 cross-department task management |
| 20 | Accounting automation | Preserve financial source identities and posting semantics | Phase 2 general ledger, trial balance and accounting integration |
| 21 | OTA channel management | Manual booking-source selection only | Phase 3 authorized APIs and reconciliation |
| 22 | Direct booking website | Backlog | Phase 3 |
| 23 | Restaurant POS | Manual authorized folio charge only | Phase 3 POS/kitchen/inventory |
| 24 | WhatsApp/notifications | Mock preview only if useful; no sends | Phase 3 approved integrations |
| 25 | OCR/document capture | Mock document metadata only | Phase 3 verified extraction/privacy workflow |
| 26 | AI management assistant | Backlog | Phase 3 separately scoped and controlled |

For advanced items present in the original Phase 1 list—such as guest merge, bill splitting, day-use rates, corporate rates, YTD comparisons and each export format—record an explicit `prototype depth / production milestone / acceptance case` disposition. Do not silently drop them because the primary guest journey is working. Obtain approval for scope reductions. Deferred modules need a backlog entry; they do not need misleading enabled navigation.

## 7. Prototype sitemap and screen contracts

Use one coherent application with task-based navigation. Suggested desktop sections: Overview, Front Desk, Guests & Stays, Cash & Billing, Housekeeping, Expenses, Closing & Reports, Administration. Show only the appropriate operational sections for the selected demo user.

The persistent header shows property, current user/role, simulated business date, connectivity mode and a visible **Demo — fictional data** label. A dedicated demo toolbar provides scenario selection, user preview and reset. Keep these demo controls separate from normal staff actions.

| Screen | Required controls and contents | Important states |
|---|---|---|
| Dashboard | Arrivals, departures, occupancy, readiness, outstanding balances and linked financial totals | Empty hotel, normal day, exceptions, restricted financial cards |
| Room rack | Room/floor filters, occupancy + cleaning + block indicators, room detail actions | Vacant dirty, ready, occupied, reserved next, out of service |
| Calendar | Date navigation, room rows, stays, blocks, booking/move controls | No availability, overlapping move, outdated selection |
| Reservations | Search/filter, list, new/edit panel, guest/dates/rate/room/source fields | Hold expiry, cancellation, no-show, invalid dates, capacity violation |
| Guest profile | Contact, accompanying guests, company, history, masked documents | Duplicate suggestion, missing required data, restricted fields |
| Check-in | Reservation or walk-in flow, guest verification, room eligibility, tariff and advance/deposit | Dirty/blocked room, disabled user, expired hold, unauthorized override |
| In-house stay | Occupants, room history, dates, folio, room move and extension | Future room conflict, pricing change, stale update |
| Folio | Dated itemized charges/taxes/adjustments, receipts, advances, deposits and balance | Duplicate charge, over-limit discount, refund request, posted reversal |
| Payment dialog | Amount, tender, reference, allocation and confirmation summary | Partial/mixed payment, missing reference, retry, unknown result |
| Checkout | Preview final charges, settle or approve credit, sample invoice, confirm departure | Unpaid balance, duplicate checkout, failed step, correction request |
| Cashier | Opening float, receipts/refunds/expenses/transfers, count, difference and close | Wrong shift, unapproved difference, already closed |
| Housekeeping | Assigned tasks, room/priority, checklist, start/finish, inspection/rejection | Wrong assignee, incomplete checklist, maintenance block, failed inspection |
| Expenses | Vendor/category, date, amount, funding source, attachment metadata and approval | Draft, pending, approved, posted, rejected, reversed |
| Night audit | Business date, preflight list, drill-down, charge preview, progress and closing summary | Blocking issue, restart, already posted/closed, privileged correction |
| Reports | Operational/financial tabs, date/property filters, drill-down, download/print | No records, restricted scope, synthetic historical comparison |
| Access control | Users, enable/disable, roles, action matrix, scope, limits, exceptions and preview | Conflicting grants, expiry, last-owner protection, unauthorized self-escalation |
| Approvals/audit | Pending requests, before/after, reason, initiator, approver, time and outcome | Rejected, expired, changed underlying transaction, duplicate approval |
| Hotel configuration | Policy categories, effective dates, validation, impact preview and history | Invalid combination, proposed change, approval required |
| Backup/recovery | Sample status, retention and restore workflow explanation | Clearly marked simulation, failed status and recovery instructions |

For each screen specify fields, required/optional status, defaults, validation, permitted roles, scope filters, primary/secondary actions, loading/empty/error/success states, mobile layout and resulting state changes. Every visible button must work, navigate correctly, or explain a deliberate unavailable state. A success toast without a corresponding state change is not completion.

## 8. Visual and responsive specification

Create an original, consistent HCC visual system informed by the referenced hotel workflows. Avoid assembling unrelated donor interfaces.

- Use clear typography, restrained colors, INR formatting, unambiguous dates and legible tabular numbers.
- Give room status both text/icon and color. Distinguish occupancy, housekeeping and inventory block dimensions.
- Desktop reception: dense but readable room board, efficient keyboard entry, searchable lists and quick guest/folio access.
- Tablet supervisor: touch-friendly room/task cards, assignment and inspection actions with relevant room context.
- Mobile housekeeper: own tasks first, large controls, minimal fields, no unrelated financial navigation.
- Support keyboard focus, accessible labels, validation associated with fields and focus restoration after dialogs. Provide a non-drag alternative to drag-and-drop operations.
- Proposed verification sizes: 1440×900 and 1280×800 desktop, 1024×768 landscape tablet, 768×1024 portrait tablet, and 390×844 mobile. Check 200% zoom on critical forms.
- Permit horizontal scrolling inside a calendar when necessary; avoid page-wide overflow and clipped modal actions.
- Provide print layouts for the sample invoice, registration card and closing report. Mark financial printouts as demonstration documents.

Emulated viewport tests must be described as emulated. Only claim physical Android validation after an actual device test. Browser/PWA readiness is separate from installability and offline operation.

## 9. Mock architecture and deterministic state

Use React + TypeScript with a small, documented build setup. A Vite-style frontend setup is a proposed default, subject to the available environment. Check current supported dependency versions when implementation begins and record them in a lockfile.

Suggested internal structure:

```text
src/
  app/                  shell, routing, providers and demo controls
  features/             reservations, stays, folios, housekeeping, etc.
  components/           shared presentation components
  domain/               pure prototype rules, money and date types
  services/contracts/   typed requests and responses
  services/mock/        commands, validation and scenario state
  fixtures/             coherent fictional records and expected totals
  styles/               design tokens and responsive rules
tests/                  meaningful workflow, permission and browser checks
```

Required behavior:

1. All screens read the same normalized mock state and stable record IDs. Do not maintain separate dashboard, booking and folio datasets that drift apart.
2. Route mutations through typed service commands; components must not directly alter records owned by other features.
3. Mock commands validate transitions, permissions and scope. Return clear typed errors such as unavailable room, forbidden action, invalid transition and stale version.
4. Derive reports from transactions and state; do not hard-code KPI totals independently.
5. Use an explicit simulated clock/business date. The test result must not change because the real calendar changes.
6. Provide fixture reset and scenario selection. If local persistence is used, version it and provide a safe reset. Describe whether refresh retains data.
7. Support simulated latency, rejection, lost response and stale data for critical commands. A repeated command identity must return its previous result rather than duplicate effects.
8. Document that browser-local mock state is not shared across devices. Cross-device visual testing can use reset fixtures; do not imply it proves production synchronization.
9. Use fictional attachment placeholders. Never ask staff to upload real identity documents into this prototype.
10. Keep demo shortcuts, mock storage and role switching removable. These are not production authentication or authorization.

Deliver a README with prerequisites, install/start/build/test instructions, the actual tested URL/port, reset instructions and known limitations. Support serving the build through a local server; do not promise that opening `index.html` directly will work. Explain LAN binding/firewall requirements before tablet testing, and distinguish a local preview from public hosting.

## 10. State machines and operational invariants

Document each transition with actor, permission, scope, precondition, atomic effects, approval requirement, audit entry, idempotency key and error response. The following states are proposals for validation.

### Reservations and inventory

`Draft → Held → Confirmed → Fulfilled`; branches include `Cancelled`, `Expired` and `NoShow`. A direct confirmed reservation is allowed when policy permits. Active occupancy belongs to a separate stay record.

- Define stay intervals explicitly, normally arrival-inclusive/departure-exclusive. Define separate time-based rules for day use.
- Check both room-level allocations and room-type capacity. A future reservation is different from present physical occupancy.
- Holds need explicit expiry and inventory semantics. Refreshing a screen must not silently extend a hold.
- Date/room changes revalidate the entire affected interval. An unavailable target must leave the original booking intact.
- A room block has a reason, start/end and author. Conflicts with existing allocations require resolution, not invisible displacement.
- No-overlap is an invariant for exclusive room occupancy. Any later room-type oversell policy needs separate explicit design.

### Stay and checkout

`Expected → InHouse → CheckedOut`; cancellation/no-show normally belongs to the reservation. Corrections must retain the original history.

- Check-in requires an eligible room, permitted user, approved guest-data completeness and a valid rate.
- Room movement preserves historical room/date segments and safely reserves the target before releasing the old assignment.
- An extension cannot overwrite another reservation. Show alternate room options or a clear failure.
- Checkout previews outstanding amounts and deposit disposition. Collect/allocate settlement before confirming a settled departure, or record authorized company credit explicitly.
- Successful checkout ends occupancy, marks the room dirty and creates exactly one corresponding housekeeping task.
- A repeated checkout returns the existing result. A failed checkout must not leave a departed guest in an occupied room or a released room with an active stay.
- Post-checkout billing corrections are not the same as reopening physical occupancy. Re-entry requires fresh availability validation.

### Housekeeping

`Dirty → Assigned → Cleaning → AwaitingInspection → Ready`; inspection rejection returns to cleaning with a reason. Model stayover cleaning independently of releasing a vacant room.

Occupancy, cleaning readiness and inventory block are separate dimensions. A clean blocked room remains unavailable. A future reservation can exist for a room currently occupied. A room's arrival readiness must not be inferred solely from its future booking availability.

### Approvals

`Requested → Approved / Rejected / Expired / Cancelled → Executed` where applicable. Bind approval to action, amount, target record and relevant version. Changed amounts invalidate the approval. Approval does not itself prove the underlying transaction succeeded; execution must be idempotent and checked again.

### Cash shift and business day

Shift: `Open → Submitted → Closed`, with a discrepancy path. Business day: `Open → Preflight → Posting → Closed`, with restartable failed progress.

Separate calendar date, business date, event timestamp and service/stay date. Record Asia/Kolkata business interpretation and UTC audit timestamps in the production design. A rerun must not repost a room night or produce another closing record. Decide whether corrections require adjustments or controlled reopen before production; do not implement both ambiguously.

## 11. Financial rulebook and worked fixture

Use integer paise or an appropriate decimal representation; do not rely on binary floating-point calculations for money. Specify currency, precision, tax rounding level, discount order and final rounding. Keep rate/tax snapshots on posted charges so later configuration changes do not rewrite history.

Separate charges, discounts, tax, payment receipts, receipt allocations, unapplied advances, refundable deposits, refunds, company receivables and cash movements. A deposit is a liability, not room revenue. A payment reduces a balance; it is not a negative revenue line. Do not subtract an advance again if its receipt has already been allocated to the folio.

For demonstration only, use this exact fictional arithmetic oracle. The 10% rate is synthetic test data, not a statement of applicable GST.

| Event | Amount | Expected effect |
|---|---:|---|
| Two room nights at ₹2,000 | ₹4,000 | Room charges |
| Additional service | ₹500 | Service charge |
| Approved discount before test tax | −₹500 | Net taxable charges ₹4,000 |
| Synthetic tax at 10% | ₹400 | Total invoice ₹4,400 |
| Advance received and allocated once | ₹1,000 | Remaining folio balance ₹3,400 |
| Refundable deposit received separately | ₹500 | Deposit liability ₹500; folio remains ₹3,400 due |
| Final settlement | ₹3,400 | Folio balance zero |
| Deposit returned | ₹500 | Deposit liability zero |

Across this complete example: gross receipts are ₹4,900, deposit refunds are ₹500, net receipts are ₹4,400, net charges excluding tax are ₹4,000 and tax is ₹400. Keep room versus service revenue separately reportable. When money was received on different business dates, daily receipts must follow their actual receipt dates; do not force every daily cash total to equal that day's revenue.

Additional required rules:

- Invoice identity and number are allocated consistently; posted documents cannot be silently edited or deleted.
- Draft changes, posted reversals, refunds and credit notes have different semantics and permissions.
- Manual UPI/card receipt entry does not verify a bank transaction. State this clearly in prototype confirmations.
- Failed or unknown payment outcomes need reconciliation; do not blindly retry a potentially completed capture in future gateway integrations.
- Refunds reference eligible original receipts and cannot exceed the refundable amount.
- A cashier-funded posted expense reduces drawer cash exactly once; vendor-credit expenses do not automatically reduce it.
- Expected drawer cash equals opening float + cash receipts + cash transfers in − cash refunds − cash disbursements − transfers out. Noncash tenders are excluded.
- Define occupancy denominator, excluded rooms, ADR room revenue and RevPAR available-room basis in a report dictionary. Handle zero denominators explicitly.
- General-ledger automation remains Phase 2, but Phase 1 transactions require stable source IDs and traceable classifications.

Before production, validate tax classifications, CGST/SGST/IGST applicability, invoices, credits, retention and guest reporting against current authoritative requirements and qualified advice. Do not treat earlier repository behavior or this synthetic fixture as legal guidance.

## 12. Master Access Control Panel

This is a first-class feature, not a hard-coded role dropdown. The prototype role switcher is only a demonstration tool; the actual administration UI must edit policy and show its effect.

Model: **user → role assignments → resource/action → scope → field restrictions → financial limits/approvals**, with effective dates and explicit user exceptions where allowed.

Proposed policy evaluation: disabled/expired identity or assignment denies; resource scope must match; explicit deny overrides applicable grants; absence of a grant denies; approval/threshold conditions must be satisfied. Inherited means no local override. Specify multi-role combination, field masking and user exceptions unambiguously before implementing.

| Role | Proposed baseline | Restrictions to demonstrate |
|---|---|---|
| Owner | Business oversight, policy and privileged approvals | Audited actions; no invisible financial history edits |
| Technical administrator | Identity and technical configuration | Financial authority is a separate grant, not automatic |
| Manager | Operational oversight and bounded approvals | Cannot self-approve or grant unrestricted ownership |
| Reception | Availability, reservations, registration, check-in/out and operational folio access | No unrestricted refunds, tax edits, payroll or role administration |
| Cashier | Assigned shift receipts, authorized refund execution and cash close | No arbitrary rate override, role edits or approval of own discrepancy |
| Housekeeping supervisor | Assign/inspect/release scoped rooms | No guest ID documents or financial exports |
| Housekeeper | Assigned rooms and permitted checklist actions | No other staff's tasks, guest finance or ready release requiring inspection |
| Accountant | Scoped reports, permitted finance corrections and reconciliation | No unrestricted access-policy changes |
| Maintenance | Assigned repair tasks and permitted block requests | No guest finance or occupancy override |

These are preliminary templates, not final approved assignments. Additional night-closing/read-only roles can be added when required by staffing.

Implement and demonstrate:

- Create/edit role template; module/action matrix; own/assigned/shift/property scope; restricted fields; limits; reasoned exceptions; expiry.
- Create/update/enable/disable users, change role assignment and preview effective access.
- Explain why a permission is allowed or denied, including inherited grants and conflicting roles.
- Approval requests for discounts, refunds, overrides, cash discrepancies, closed-date corrections and sensitive configuration.
- Prevent self-escalation, self-approval and removal of the last active authorized owner account.
- Record policy version, initiator, before/after, reason and timestamp. Show approval and denial history without exposing forbidden record details.
- Simulate immediate denial after a user is disabled. Production must invalidate/recheck sessions and cached policy promptly, with a defined maximum delay.
- Demonstrate direct route/service denial as well as hidden UI controls. Production must enforce equivalent policy on backend commands, queries, exports and document downloads.
- Specify emergency access separately: permitted actor, limited duration/scope, reason, audit and owner notification. Do not add a universal hidden bypass.

## 13. Hotel Configuration Control Panel

Keep operating settings separate from access policy. Group them into property, rooms, reservations, stay timings, rates, finance/tax, approvals, housekeeping, shifts, documents and operations.

For every setting record: key, type, unit, default, allowed values, property scope, effective date, permission, approval need, validation, audit behavior and effect on existing records.

Show these examples working in the prototype:

1. Change a discount threshold; the next qualifying request routes for approval.
2. Change the housekeeping inspection rule; demonstrate its effect on newly eligible tasks under the documented policy.
3. Add a future-dated rate; newly priced bookings use the selected policy while existing agreed rates remain unchanged unless explicitly repriced.
4. Change a sample tax rule effective date; historical posted charges retain their original tax snapshot.
5. Change a check-in/out setting; display a warning about affected open stays and apply the documented policy.

Never expose a setting that disables authorization, exclusive-room overlap protection, payment duplication protection or financial audit history.

## 14. Fixture catalog and walkthrough

Seed one fictional property with 24 rooms across three floors and three categories. Include occupied, ready, dirty, cleaning, awaiting inspection and blocked rooms; future reservations; a hold; a cancellation; a no-show; a company-credit example; pending approvals; open and closed shifts; and sample expense records.

All guests, contact details, IDs, receipts and company information must be fictional and visibly nonproduction. Keep fixture keys stable. Provide a clean starting scenario and additional scenarios rather than forcing incompatible examples into one dataset. Room 105 may be ready in the main journey and blocked in a separate exception scenario; scenario state must be explicit.

Required owner walkthrough:

1. Owner reviews opening occupancy, cash, advances and room status.
2. Reception checks dates, selects Room 105, creates a two-night booking and records an advance.
3. Reception completes guest registration and checks in; room rack and arrivals update.
4. Authorized staff posts each room night, adds a service and requests a discount.
5. Manager approves the exact proposed discount; unauthorized attempts are rejected.
6. Cashier records remaining settlement, handles the separate deposit and reviews the folio.
7. Reception checks out and prints the sample invoice; occupancy ends and a housekeeping task appears automatically.
8. Housekeeper completes the assigned task; supervisor inspects and releases the room.
9. Cashier posts an approved petty-cash expense and closes the shift with a reconciled count.
10. Authorized closing user resolves preflight issues and closes the business date. A rerun changes no totals.
11. Owner drills from reports to the same source transactions and compares revenue, tax, receipts, deposits and expenses.
12. Owner changes a role permission and disables a user; demonstrate immediate effective denial and the recorded audit event.

Supply exact starting fixture, actors, clicks, expected balances and final states. The full journey must run after a reset without manually repairing mock data.

## 15. Acceptance matrix and verification

Use `Passed / Failed / Not run / Deferred with approval` with an evidence location. Never label a test passed because its code exists. Screenshots, test logs and manual observations must state the tested build and scenario.

| ID | Test | Observable expected result | Stage |
|---|---|---|---|
| AC-001 | Complete reset-to-checkout-to-cleaning journey | Linked records, state and reports reconcile without manual repairs | Prototype + production |
| AC-002 | Duplicate exclusive room allocation | Second conflicting allocation is rejected; first remains intact | Simulated prototype; concurrent production |
| AC-003 | Date change/extension into future booking | Clear conflict; original stay/allocation preserved | Both |
| AC-004 | Check in to dirty or blocked room | Denied under approved readiness policy | Both |
| AC-005 | Repeated receipt/charge/checkout command | One financial effect and one housekeeping consequence | Both; restart/retry in production |
| AC-006 | Worked financial fixture | ₹4,400 invoice, correct advance allocation and deposit separation | Both |
| AC-007 | Refund and correction | Eligible source, reason, approval and linked reversal/credit; no erased history | Both |
| AC-008 | Unpaid checkout | Blocked or completed through the approved credit exception | Both |
| AC-009 | Room move fails halfway | Original state remains valid; no double occupancy/lost assignment | Simulated prototype; transaction fault in production |
| AC-010 | Housekeeping completion and inspection | Not ready before required inspection; blocked room stays blocked | Both |
| AC-011 | Housekeeper requests another user's task/finance | Scope and field access denied, including direct service/API request | Both |
| AC-012 | Role change and disabled user | Effective policy changes; disabled identity cannot mutate records | Both; real sessions in production |
| AC-013 | Self-approval/self-escalation/last-owner removal | Denied with understandable explanation | Both |
| AC-014 | Approval target changes after approval | Old approval cannot authorize changed amount/version | Both |
| AC-015 | Expense paid from drawer | Cash changes once; report and shift reconcile | Both |
| AC-016 | Night charge/close rerun | No duplicate charge, close or inflated report | Both |
| AC-017 | Night close interrupted and resumed | Progress recoverable; completed postings not repeated | Simulated prototype; process restart in production |
| AC-018 | Business date crosses midnight | Business-date rules, service dates and timestamps remain consistent | Both |
| AC-019 | Effective-dated rate/tax update | Existing posted amounts unchanged; new records use correct policy | Both |
| AC-020 | KPI and export drill-down | Totals reconcile to allowed source records and same filters | Both |
| AC-021 | Invalid/missing data and no results | Clear validation/empty state; no false success | Both |
| AC-022 | Lost response/stale version/retry | Clear pending/recovery state; no blind duplicate mutation | Both |
| AC-023 | Responsive and keyboard walkthrough | No inaccessible critical controls or clipped actions at target sizes | Prototype + production |
| AC-024 | Refresh, fixture reset and clean startup | Documented persistence; reproducible starting state | Prototype |
| AC-025 | Database access and authorization bypass attempts | Clients cannot access DB directly or bypass server policy | Production |
| AC-026 | Outbox delivery failure and replay | Business transaction durable; subscribers idempotent; lag visible | Production |
| AC-027 | Backup restoration to separate environment | DB and documents recover consistently; measured loss/time recorded | Production |
| AC-028 | Upgrade and rollback/recovery rehearsal | No unplanned lost bookings, receipts or audit history | Production |
| AC-029 | Export/download of restricted fields | Same scope/masking policy as authorized screen/API view | Both |

Prototype verification: type checking, build, focused money/state/policy tests, meaningful end-to-end browser scenarios, visual inspection and a complete manual walkthrough. Add screenshot evidence for room rack, booking, check-in, folio, checkout, housekeeping, access control, closing and reports at representative sizes.

Production verification adds real SQL Server integration, parallel API requests, transaction fault injection, permissions, durable events, migration tests, realistic load and recovery drills. Mock overlap prevention is not proof of database concurrency safety.

Critical failures include double booking, duplicate money, unexplained reconciliation difference, unauthorized access, lost transaction history and failed recovery. Do not pass a delivery gate with an unresolved critical failure. Document minor limitations with impact and an agreed disposition.

## 16. Source reuse and research work

The following are candidates recorded in the source conversation. Their current availability, license, completeness and quality have not been independently verified by this plan.

| Candidate | Intended use | Required evaluation |
|---|---|---|
| [HAIP](https://github.com/TelivityAI/haip) | Reservation/folio/housekeeping/domain logic | Pin revision, verify Apache claim/file notices, run relevant tests and measure SQL Server adaptation |
| [Open Hotel PMS](https://github.com/peeraseepat-cell/open-hotel-pms) | Selected UI and operational components | Verify MIT claim, public completeness, dependencies and Supabase/Next-specific coupling |
| [Kamra](https://github.com/Kamra-PMS/kamra-pms) | Indian hotel workflows and behavior specifications | Treat AGPL label as unverified until inspected; avoid unapproved source copying |
| [QloApps](https://github.com/Qloapps/QloApps) | Booking/rate/admin workflow reference | Verify OSL file notices and which features require paid extensions |
| [HotelDruid](https://github.com/digital-druid/hoteldruid) | Availability and assignment concepts | Confirm source/version freshness, license and proprietary boundaries |
| [miniCal fork](https://github.com/pankaj9296/minical) | Low-priority calendar/extension reference | Establish provenance and maintenance before considering reuse |
| [Saagar Control Centre](https://github.com/sagarbora91/saagar-control-centre) | Own expense, workforce, checklist and reporting logic | Inspect exact assets; replace browser/SQLite persistence; verify dependencies' licenses |
| ETP Report Engine | Deferred | Do not access, infer internals or claim reuse without owner-provided access |

For each reused component record repository, exact commit, source paths, license/notices, dependencies, functionality actually tested, adaptation plan, destination paths, modifications and retained attribution. Report `documented`, `inspected`, `built`, `tested` and `accepted` separately.

Do not combine donor databases or require Frappe, Supabase, PHP and NestJS runtimes merely to preserve isolated features. Compare the full adaptation/test/maintenance cost with a smaller independent implementation.

Use documented behavior to guide independent implementations where copying is not approved. Do not disguise a line-by-line translation as independent work. Internal use is not itself a blanket license exemption. Resolve file-specific obligations before import; obtain appropriate advice for uncertain cases.

The prototype may use original components and fictional behavior without waiting for an exhaustive six-repository audit. Production reuse decisions require actual evidence. Do not assert that all repositories are fully working based on their README or demo.

## 17. Production architecture design after prototype acceptance

Preserve one modular application deployment. Functional areas and bounded contexts are not necessarily one-to-one.

Proposed context ownership:

| Context | Owns | Important public interactions |
|---|---|---|
| Identity/access | Users, sessions, roles, policy and approval authority | Evaluate access, change policy, revoke session |
| Property/configuration | Property, room catalog and effective policies | Room/policy queries and validated changes |
| Reservations/inventory | Bookings, holds, allocation intervals and dated blocks | Availability, reserve, amend, cancel, allocate |
| Guests/stays | Guest profiles, occupants, stay segments and arrival/departure | Register, check-in, move, extend, checkout |
| Billing | Folios, charges, tax snapshots, invoices and credits | Post charge, adjust, issue invoice, query balance |
| Payments/cash | Receipts, allocations, deposits, refunds and shifts | Receive/allocate/refund, reconcile/close shift |
| Housekeeping | Tasks, checklists, inspections and cleaning readiness | Create task, assign, complete, inspect |
| Expenses | Vendors, expense records and approvals | Approve/post/reverse; request cash disbursement |
| Business-day closing | Closing progress, exception resolution and close records | Coordinate preflight/post/close/recovery |
| Reporting | Defined projections and report queries | Authorized cross-context reporting |
| Platform services | Document metadata/storage access, audit delivery and event transport | Controlled interfaces; no arbitrary business writes |

Each context separates domain, application, ports, adapters and public contracts. Prevent circular imports and cross-context direct writes with automated boundary checks. Shared code stays limited to stable primitives such as money, identifiers and time.

Resolve critical consistency deliberately:

- Decide which operations require a single database transaction through module-owned application interfaces, and which downstream work can be asynchronous.
- Room allocation, stay activation and charge/payment effects cannot depend on an eventually consistent UI projection for correctness.
- Checkout must durably establish departure and dirty/not-ready state before a delayed housekeeping subscriber could make the room appear sellable. Task creation may use a durable event with a unique causal key.
- Specify outbox ownership, retries, dead-letter/recovery handling and idempotent subscribers. Avoid exactly-once-delivery claims; prove exactly-once business effects under replay.
- Define closing barriers so late events or payments cannot silently alter a closed business date.

Event catalog candidates: `ReservationConfirmed`, `GuestCheckedIn`, `RoomChanged`, `RoomChargePosted`, `PaymentRecorded`, `GuestCheckedOut`, `HousekeepingInspectionPassed`, `ExpensePosted`, `BusinessDayClosed` and `AccessPolicyChanged`.

For each event specify ID, schema version, aggregate ID/version, property, business date where applicable, occurrence time, actor, correlation/causation IDs, payload, consumers, retry/idempotency behavior and privacy constraints. Minimize personal data in event payloads.

For commands/queries specify input/output schemas, authorization, scope, idempotency, optimistic versioning, errors, transaction boundary and audit effects. Browser clients use APIs only; never expose SQL credentials.

## 18. SQL Server and reliability design

Verify the selected SQL Server 2025 Express edition's current official capabilities, limits and platform requirements before infrastructure decisions. Do not repeat the conversation's capacity figures as validated facts.

Before choosing a driver/ORM, implement a small compatibility spike in a disposable environment: connection lifecycle, migrations, transactions, decimal/date behavior, Unicode, indexes, optimistic concurrency and concurrent inventory/financial writes. Document findings and choose based on evidence.

Required design work:

- Module-owned schemas/tables, stable primary keys, foreign keys, uniqueness and check constraints.
- Dates versus timestamps, property timezone, business-date semantics and effective-dated records.
- Decimal precision/scale and posting identifiers; unique charge identity per stay/service date/charge kind as applicable.
- Optimistic concurrency for editable records; deterministic locking for inventory allocation and financial invariants.
- A SQL Server-compatible overlap strategy. A unique room ID plus start/end index alone does not prevent overlapping intervals. Evaluate normalized allocation units where suitable, or correct indexed range/application locking with consistent lock order; prove with concurrent tests, including day use and date changes.
- No UI-only `check then insert` booking safety. The decisive availability check and allocation must share the appropriate protected transaction.
- Durable idempotency records; duplicate commands return the original committed result. Bind keys to actor/operation/payload appropriately and reject mismatched reuse.
- Read-model ownership and reporting access; avoid uncontrolled reporting queries against every operational table.
- Secure file storage with database metadata, scoped downloads and coordinated backups. Do not place real guest documents in a public static folder.
- Forward migrations, representative-data rehearsal, pre-upgrade backup, recovery procedure and compatibility period where needed. A schema rollback may be destructive; document restoration and post-backup transaction reconciliation.
- Backup scheduling supported by the actual edition/deployment, retention, encrypted off-device copy and restore drills. Do not presume an unavailable scheduling feature.

Proposed reliability scenarios: power loss during receipt entry; response lost after commit; server restart during closing; outbox subscriber failure; stale tablet action; disk full; document save failure; database unavailable; and recovery from a backup missing later transactions. Specify operator-visible behavior for each.

## 19. Deployment, security and operating readiness

Production must work on hotel LAN without internet for core operations. Package frontend assets locally; document any optional feature with an external dependency.

The Windows deployment plan must specify service identities, start/restart behavior, configuration/secrets, host naming, firewall boundaries, TLS and trusted certificates on Android, log locations, health status, upgrade ownership and a tested uninstall/recovery path. PWA service workers/installability depend on suitable browser/security conditions; LAN HTTP is not automatically an installable offline PWA.

Specify password/MFA policy for privileged roles, session expiry, revocation, least-privilege database access, document authorization, audit retention, dependency updates and sensitive-data redaction in logs. Keep technical administration and financial power separable.

For v1, loss of LAN/server connectivity should clearly disable booking, payment and allocation mutations. Read-only cached information, if present, must show its age. Do not silently queue critical financial commands for later execution. Restricted offline housekeeping requires a separate approved conflict/sync design.

Define measurable targets before production. Proposed starting targets for discussion: single property up to 50 rooms, 10 concurrent active users, common API operations p95 below 1 second and interactive screen completion below 2 seconds on the agreed LAN/test hardware. Record dataset size and machine configuration; revise targets with evidence. These are proposals, not achieved performance.

Agree recovery-point and recovery-time objectives with the owner; derive backup cadence from them. A backup schedule alone is not evidence that recovery meets those objectives. Restore DB, attachments and configuration into a separate environment and measure the result.

Pilot prerequisites: approved real users and scopes, room/rate/configuration masters, reconciled opening balances and active reservations, staff training, fallback procedure, support contact, tested backup/restore and a bounded rollout plan. Rehearse conversion with synthetic or approved sanitized records before touching live hotel data.

## 20. Work packages and estimation

Create implementation tasks small enough to complete and verify independently. Every task has requirement IDs, dependencies, files/contexts affected, output, acceptance tests and completion evidence. Mark blocked work explicitly without pretending the entire project is blocked when independent work remains.

| Package | Depends on | Deliverable |
|---|---|---|
| WP-01 | Source review | M0 planning/decision package |
| WP-02 | Prototype structure approval | Design tokens, shell, mock contracts, fixtures and reset |
| WP-03 | WP-02 | Room rack, availability and reservation workflows |
| WP-04 | WP-03 | Guest registration, check-in, room change and extension |
| WP-05 | WP-02 + financial draft | Folio, receipts, deposits, approvals and worked arithmetic |
| WP-06 | WP-04/05 | Checkout and housekeeping chain |
| WP-07 | WP-02 | Access/configuration editors and policy demonstration across features |
| WP-08 | WP-05/06/07 | Expenses, shifts, night audit, reports and exports |
| WP-09 | WP-03–08 | Exceptions, responsive QA, reproducible walkthrough and evidence |
| WP-10 | Prototype acceptance | Production contracts, SQL design, compatibility spike and estimate |
| WP-11 | Production authorization | Platform foundations and real vertical slice |
| WP-12 | Slice gate passed | Remaining approved MVP coverage |
| WP-13 | MVP gate passed | Deployment rehearsal, restore drill, training and pilot |

Design permissions from WP-01 and apply them in every feature; WP-07 is completion of the editable administration experience, not the first introduction of access control.

Use ranges, assumptions and dependencies when estimating. Separate specification, implementation, testing, fixes, owner walkthrough time, compatibility uncertainty and deployment/training. Do not mechanically reuse the earlier 8–12-week estimate or convert AI output speed into a production-readiness promise. Update the forecast after the prototype and SQL Server spike.

## 21. Completion rules and first response

The prototype is complete when all 12 functional areas have their approved depth, the connected journey and required exceptions work, financial fixtures reconcile, access/configuration changes affect behavior, representative layouts have been inspected, the app starts from documented instructions, and evidence identifies any unrun/deferred cases. Owner acceptance is a separate gate from technical completion.

Production is complete only when the approved MVP works with real server-enforced controls and SQL Server persistence, integration/concurrency/recovery tests pass, deployment and restore are rehearsed, staff can operate it, and live use is authorized.

Your first response should contain:

1. A concise interpretation of the objective and established choices.
2. A decision-status summary and the material owner questions, with proposed prototype defaults.
3. The prepared M0 deliverables or their exact locations, including the sitemap and role-based walkthrough.
4. A bounded implementation sequence and the concrete prototype structure to approve.
5. Explicit distinction between facts inspected, assumptions adopted and work still unverified.

After approval, continue through the authorized milestone, fixing failures and delivering runnable work with evidence. Report limitations candidly. Do not claim unseen source was audited, a simulated integration is live, a mock permission system is secure, or a tax rule is current without supporting verification.
