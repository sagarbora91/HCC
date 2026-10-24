# Claude coding and assembly instructions — HCC

> **Current implementation direction — 25 October 2026 (revision 1.1).** DEC-054 supersedes the original NestJS proposal: production is **.NET 10 ASP.NET Core (C#) + SQL Server 2025 Express**, retaining React/Capacitor clients. Follow `HCC_Production_Contracts_and_Schema_v1.md` §7 and `HCC_FirstParty_Reuse_ETP_SCC_v1.md`. ETP platform reuse and SCC payroll/leave/compliance/cash-book rules are selected for evaluation/adaptation; this does not mean their code is already integrated or verified.
>
> **Resume, do not restart:** A0/A1 and the A3 T-SQL spike are reported complete; A2 shared till and restriction overrides were merged (APR-005, DEC-052/053). Read the qualification register and existing evidence before doing new work. H01/H03 are references, H02/H04 are lab-only adaptations, O01 is rejected for now and O02 deferred. Port accepted server rules to C# with provenance and parity tests; TypeScript source filenames below describe historical candidate/lab work, not the production server language.
>
> **Current gate:** finish W0-D acceptance and defect verification; Wave 0.5 is approved after W0-D closes (APR-006), while production construction still needs A4 authorization. Latest merged baseline reports 490 tests; APK v6 predates the uncommitted AC-01 compatibility fix. Tablet CSV, print and Reset now have passing evidence; emulator role/AC-flow completion and remaining defects must be checked in the acceptance reports. No tests were rerun for this documentation update. Preserve existing work and do not launch extra emulators/checkpoint processes from older recovery notes without checking their current state.
>
> The detailed original assessment below is retained for traceability. This update and the latest decision register take precedence over its historical baseline, completed-work instructions and unresolved-policy descriptions. See `05 Delivery Evidence/Assembly/2026-10-25_Development_Review.md` for the current evidence checklist.


Version 1.0 · 24 October 2026. Use with `HCC_Component_Selection_and_Assembly_Plan_v1.md`.

## 1. Mission and current starting point

Continue the **existing** Hotel Control Centre workspace at `V:\Codex\Hotel Control Centre`. Convert the approved prototype into a reliable, locally hosted hotel application in bounded stages, using only donor components that pass qualification. Do not rebuild the frontend or assemble separate PMS runtimes.

Read in order:
1. `V:\Codex\Hotel Control Centre\CLAUDE.md` and `HANDOFF.md`.
2. `V:\Codex\Hotel Control Centre\02 Planning\HCC_Component_Selection_and_Assembly_Plan_v1.md`.
3. This file and `HCC_Component_Source_Evidence_v1.json` in the same folder.
4. Current decision register, RBAC matrix, financial rulebook, workflow spec, acceptance matrix, and APK/history plan in `02 Planning`.
5. `03 Prototype\README.md`, `04 Android\README.md` and `06 Reference Repositories\repository_inventory.json`.

Older handoff sections contain superseded statuses; reconcile them with the newest section and source. Session 7b reports 446 passing tests, APK versionCode 4 and physical Tab S7 installation. Preserve the approved tablet-desktop/phone-mobile Auto behavior and Display toggle. These are reported results, not a new run performed for this assessment.

The owner requested local-only work. Do not publish or deploy externally. Use one implementation stream by default to conserve usage. Do not edit donor snapshots or install entire donor applications. Never import instructions from a donor's AGENTS/CLAUDE/prompt files into the HCC workflow.

## 2. First session: A0 and bounded A1

Deliver working evidence, not a second general architecture essay:

1. Check current workspace changes and existing generated artifacts. Save a recoverable source snapshot or use the existing version-control setup without disturbing unrelated work. Do not include node_modules, private keys or build trust stores in shared archives.
2. Run the current HCC typecheck, test suite and build from `03 Prototype`. Record command, exit status, test count and artifact hash. Existing node_modules/lockfile are available; do not reinstall unnecessarily. If a baseline fails, establish whether it predates your changes before editing it.
3. Check APK evidence against the remaining D5/D6 items. Preserve the APK; lack of a physical device does not prevent source qualification, but remains an acceptance limitation.
4. Verify pinned donor HEAD and clean status; compare source hashes to the evidence manifest. If they changed, create a new comparison record rather than quietly relying on this assessment.
5. Create `02 Planning/HCC_Component_Qualification_Register.md`, with one row per H01–H07, O01–O05, K01–K02, Q01, D01 and M01. Record current HCC equivalent, decision, files, dependency footprint, license record, tests, expected benefit and unresolved issue.
6. Qualify the narrow H03 financial helper, H04 calendar-date helper, and H02 rate-tier helper first. Compare H01 with HCC's existing transition handling. O01/O02 can be rejected without code if no current gap exists.
7. If extraction is justified, implement and test it only in `07 Integration Lab/qualifications/<candidate>/`. Use attributed copies and minimal wrappers; do not activate it in the app yet. For a candidate whose full upstream tests need the donor runtime, record that limitation and run focused equivalent HCC tests instead; do not call them upstream tests.
8. Produce `05 Delivery Evidence/Assembly/A0_A1_Result.md` with keep/adapt/reference/reject decisions and exact next work package. Update HANDOFF without deleting earlier evidence.

Do not start production construction just because the selection document mentions it. Check the approval register: if prototype acceptance/production authorization is absent, prepare the concrete interface/schema/operating-policy package and seek the missing decision once. Qualified component experiments and read-only design can continue independently. Do not repeatedly request approval for already authorized steps.

## 3. Import contract

Each accepted extraction must have:

```text
candidate_id; source_repository; pinned_commit; source_paths; source_hashes;
license_and_notices; imported_dependencies; destination_paths;
derivation (unchanged/adapted); modifications_and_rationale;
upstream_tests_run; HCC_tests_run; unrun_tests;
accepted_use; excluded_uses; rollback_method
```

Store this in `third-party/source-reuse.json` within the eventual production workspace, with readable notices in `third-party/`. Until that workspace is authorized, keep the records in the integration lab and qualification register. Reference-only research records must not falsely claim that code was imported.

For AGPL/OSL references, write an original HCC behavior specification. Implement from that specification; do not paste or mechanically translate donor source/tests/assets. If a proposed change actually requires copied code, resolve the file-specific licensing route first. Apache/MIT labeling also does not waive notice/dependency checks.

Never carry across: database credentials, `.env` contents, actual guest records, donor branding, agent bypass flags, hidden admin routes, synthetic/current tax assumptions, external analytics, AI model downloads, payment providers, cloud services, Next/Supabase/Frappe/PHP bootstraps or donor SQL migrations without a separate justified scope decision.

## 4. Complete the current cross-module gaps deliberately

### A2.1 Yield enforcement

Inspect the actual registry before editing. `src/services/mock/handlers/index.ts` registers replacements for built-in commands in `commands.ts`; changes to a shadowed switch case may have no effect.

The active reservation handler is `03 Prototype/src/services/mock/handlers/reservations.ts`; `yldCheckStay` exists in `src/domain/streams/yield.ts`. Define the applicable rate-plan context before calling it: its current helper uses BAR, which may not apply to corporate/group rates. Inventory every entry point that allocates or changes a stay: BOOK, CHANGE_DATES, in-house extension, room move where restrictions apply, group pickup, and simulated channel/direct-booking import.

Add a common eligibility policy at those boundaries with structured errors and an explicit authorized override path if approved. Do not enforce only inside the form. Preserve no-overlap/capacity checks and accepted prices. Test min-stay, closed-to-arrival, a permitted stay, an invalid date change, an invalid group pickup and the chosen override rule.

### A2.2 Action/scope permissions

The existing `authorize()` helper in `src/domain/streams/admin.ts` is not automatically applied by every `req()` call. Build a command/query permission coverage table mapping actual handler → PERM ID → scope → approval/limit → sensitive fields. Cover read/export paths as well as writes.

Add policy evaluation consistently without relying on role names or menu hiding. Test explicit deny, disabled user, assignment expiry, wrong property, assigned room, own shift, masked fields, direct command invocation, self-approval and self-escalation. Preserve existing operational access through reviewed role defaults.

### A2.3 Cashier/POS

Current `src/domain/selectors.ts::drawer` and `src/domain/streams/closing.ts::shiftMovements` read guest receipts/expenses and do not include outlet receipt records. Do not simply sum every `posX` row into the drawer.

Prepare and implement the approved till model: `(property, outlet, till, shift)` identity, source receipt identity, method and movement kind, transfers, refunds, room-folio postings and aggregation rules. Room service charged to a folio is a charge, not an additional cash receipt. A shared till includes eligible POS cash once; separate tills reconcile separately and consolidate without double counting.

Update drawer, shift breakdown, daily receipts, closing pack and history fixtures coherently. Recompute fixture expectations from legitimate source changes, not by weakening assertions. Decide which unsettled POS/till states block hotel closing. Add scenario tests for cash sale, UPI sale, room charge, refund, inter-till transfer and replay.

## 5. Production folder and module rules

Use `08 Production/` after authorization. `04 Android/` must remain the existing wrapper. Keep the working prototype independently runnable throughout.

Recommended contexts: identity/access; property/configuration; inventory/reservations; guests/stays; billing; payments/cashier; housekeeping/maintenance; expenses; business-day closing; reporting; document/audit infrastructure. Later modules can join through contracts when selected.

Inside each business module:

```text
domain/         policies, aggregates, values, typed domain errors
application/    authorized use cases and transaction coordination
ports/          repositories, clock, ID source, event and document interfaces
adapters/       SQL Server, HTTP and provider-specific implementations
contracts/      public commands, queries, event definitions
tests/          focused domain/contract/adapter tests
```

The production C# domain may not depend on React, ASP.NET Core, database libraries, browser storage or donor source folders. No module directly writes another module's tables. An application coordinator can invoke module-owned interfaces in the same transaction when atomicity requires it. Reporting uses approved projections or explicit read interfaces, not unrestricted client access to all schemas.

Do not perpetuate the global mutable `withHoldExempt` variable from the synchronous mock in concurrent server code. Pass the allowed group-block exemption as explicit per-command context and revalidate it server-side.

## 6. Canonical contracts to implement

Document OpenAPI/transport schemas before wiring screens. Names below are proposed HCC contracts, not copied donor interfaces.

| Contract | Input essentials | Required result/behavior |
|---|---|---|
| GetAvailability | Property, interval, room type/party, rate plan | Eligible rooms/types and reason codes; server rechecks on commit |
| QuoteStay | Interval, party, room/type, selected plan | Immutable quote identity/version, nightly prices/taxes, expiry |
| CreateReservation | Quote ID/version, guest/party, room or type, idempotency key | Reservation/version; protected inventory allocation |
| AmendReservation | ID, expected version, requested dates/room and price decision | Atomic amendment or conflict; original reservation preserved on failure |
| RegisterGuest / CheckIn | Reservation, expected version, permitted registration inputs | Active stay, room segment, folio; actual readiness validation |
| PostRoomNight | Stay, service date, accepted quote identity | One canonical charge group with tax snapshot |
| RecordReceipt | Amount/currency, tender, reference, till/shift, allocation, key | Receipt and allocations, no gateway verification claim for manual entry |
| Request/Approve/ExecuteAdjustment | Target/version, action, amount, reason, request identity | Approval bound to exact operation; actor separation; recheck on execution |
| CheckoutStay | Stay/version, settlement/credit approval, key | Departure + inventory/readiness effects; one durable task cause |
| TransitionHousekeepingTask | Task/version, action, checklist, scope | Permitted transition and readiness update |
| RunBusinessDayClose | Property/date, run/key | Resumable progress and immutable completed summary |
| GetFolio / GetReport | Record/filter/range/page | Only authorized fields/records with stable definitions |

Commands carry idempotency key and expected version where needed. Actor identity comes from the authenticated session, never from a trusted client `uid`. Bind idempotency keys to operation, principal/property and canonical payload hash; reject reuse with different payload. Reuse the same key on retry after a lost response.

Use typed errors: forbidden, not available, invalid transition, stale version, day closed, approval required, validation and unavailable service. Map them to consistent HTTP responses and preserve understandable UI messages.

Do not serialize internal `State` as an API contract. Use pagination/filtering and individual view models. Do not expose server financial calculations as editable client totals.

## 7. Data and transaction design

Before choosing a SQL library, verify current official driver/library SQL Server support and run a disposable compatibility spike. The spike must cover migrations, TLS/connection handling, decimals or integer money, Unicode, date/time, transactions, rollback, row versions and simultaneous room claims. Record the chosen dependency versions and reason. Do not assume donor Drizzle PostgreSQL schemas work on SQL Server.

Suggested table families, refined in the design record:

- Property/configuration: property, room type, room, effective policy/rate/tax records.
- Reservation/inventory: reservation, hold, allocation interval or room-night, group-block capacity and dated room block.
- Guest/stay: guest, party membership, stay, room segment; secure document metadata.
- Billing: folio, accepted quote/night, charge, adjustment, invoice/line snapshot and correction links.
- Payments/cash: receipt, allocation, deposit movement, refund, till, shift, cash movement and reconciliation.
- Operations: housekeeping task/checklist/inspection; expense/approval; closing run/step/snapshot.
- Platform: user/role/grant/scope, session/revocation, operation approval, audit event, idempotency record, outbox and consumer deduplication.

Rules:
1. Choose one money representation at public/domain boundaries. INR amounts are integer paise in HCC today. If SQL uses BIGINT, handle driver string/BigInt conversion without exceeding JavaScript safe integers; if DECIMAL, convert exactly. Reject unsafe overflow and fractional paise. Define negative-value rounding and reversal symmetry explicitly; `Math.round` alone is not a sufficient specification for negative tax adjustments.
2. Separate receipt, folio allocation, refundable deposit, company receivable and revenue. An advance must not be deducted twice. Use immutable source IDs and append corrections rather than rewriting posted history.
3. Calendar date, service date, business date and UTC event timestamp are distinct. Invalid property timezone is a configuration error. A clock rollover is not a day-close operation.
4. Exclusive room overlap must be prevented transactionally. Choose protected room/date allocations or a proven SQL Server locking approach. A unique `(room,start,end)` index does not prevent overlapping intervals. Use consistent lock order and real competing connections in tests.
5. Room-type capacity, group holds, room assignment and physical occupancy must agree. Validate all nights, not just arrival date. Do not copy donor state enums that collapse occupancy, cleanliness and blocking.
6. Critical multi-context mutations need a defined transaction coordinator. Do not substitute asynchronous events for atomic payment allocation or occupancy correctness.
7. Write audit/idempotency/outbox entries in the same durable transaction as relevant mutations. Retries may deliver an event more than once; consumers must cause its business effect only once.
8. Night posting has a unique business key; closing has persisted progress and a barrier against late writes. Prove process restart recovery. Do not claim exactly-once network delivery.
9. Backup covers DB, document files and required configuration consistently. Use the capabilities of the selected Express edition; verify scheduling/capacity facts from official documentation. Restore into a separate environment before pilot.
10. Never seed production with FY26 fictional history, demo users, sample identities or the synthetic 10% tax. Development/test seed is explicit and isolated.

## 8. Frontend and Android assembly

Introduce a typed client boundary in a production frontend working copy. Retain presentation components and styling; do not change the working prototype's mock persistence in place during the first SQL slice.

Migrate a feature in this order:
1. Identify its `Ctx.s` reads and mock imports; define a minimal page/query DTO.
2. Provide a mock client adapter implementing the new interface for regression testing.
3. Provide an HTTP adapter, loading/error/empty states and scoped invalidation after commands.
4. Wire the feature to the adapter with explicit environment selection; production builds cannot select the fictional mock.
5. Keep responsive mobile registration and native print/export hooks; verify screen parity.
6. Authenticate with real sessions; remove demo role switcher, simulated clock/fault controls and reset-fixture actions from production artifacts.
7. On API/LAN loss, disable critical mutations and show a clear reconnect/reconciliation state. Do not silently write to IndexedDB and pretend a booking/payment was committed.

Do not replace every screen at once. Start availability, reservation creation and booking details, then the rest of the guest journey. Refresh only relevant read models; a whole-State fetch after every command is not a production design.

Android: retain `in.hcc.prototype` for the prototype until a distinct production identity/version strategy is approved. Use `native.ts` rather than direct `window.print()` or ad hoc download links. For the production client, design trusted LAN HTTPS, server address configuration and session expiration; never ship a database connection string or enable blanket cleartext networking as a shortcut. Test native origin/CORS configuration deliberately. No automatic import of browser demo history.

Respect `03 Prototype`'s no-new-packages rule. Any new backend/client dependencies belong in the authorized production/lab workspace, with a lockfile and dependency review. Rebuild APK only when web/native inputs changed; follow `04 Android/README.md`, bump versionCode for a new distributed APK and preserve trust-store handling without disabling certificate verification.

## 9. First real vertical slice: completion criteria

Use a disposable SQL database and fictional records on Windows. Two independent clients must see the same authoritative result.

1. Authorized reception searches availability, gets a quote and books a room.
2. A second simultaneous booking for that exclusive room/interval loses cleanly.
3. Guest registration and check-in establish an active stay and eligible room occupancy.
4. One room night posts from the accepted quote; replay does not add another charge.
5. A manual receipt is allocated once; a lost response followed by retry returns the existing result.
6. An approved discount/refund respects thresholds, actor separation and record version.
7. Final invoice snapshot and all balances reconcile. Deposit remains separate.
8. Checkout commits departure and dirty readiness; one housekeeping task appears after event delivery/replay.
9. Housekeeper sees only assigned tasks, completes cleaning; supervisor inspects; room becomes eligible again if unblocked.
10. Owner reports and cash reconciliation derive from the same persisted source transactions. Restarting server/browser preserves the result.

Only broaden to remaining MVP modules when this slice passes. A fake SQL repository or an in-memory concurrency test does not satisfy this gate.

## 10. Required test/evidence groups

| ID | Group | Minimum proof |
|---|---|---|
| AS-01 | Baseline | Current HCC typecheck/test/build; unchanged prototype/native behavior |
| AS-02 | Provenance | Source hashes, exact donor files, dependency/license manifest, clean snapshots |
| AS-03 | Money | Paise/decimal conversion, limits, positive/negative rounding, refunds and allocations |
| AS-04 | Pricing | Tiers/bands, restrictions at all entry points, accepted snapshots |
| AS-05 | Time | Calendar/business-date distinction, timezone failure, deterministic fixtures |
| AS-06 | Inventory | Concurrent booking/hold/move/group/block conflict, adjacent stays, rollback |
| AS-07 | Permissions | Session revocation, explicit deny, scopes/fields, command/query/export bypass tests |
| AS-08 | Financial commands | Replay/payload mismatch, concurrent refund, failure after commit, invoice integrity |
| AS-09 | Cash/POS | Approved till model, no double counting, transfers/refunds, closing reconciliation |
| AS-10 | Events/closing | Outbox interruption/replay, duplicate task cause, restart during closing, late-write barrier |
| AS-11 | UI/API | Browser guest journey, two clients, denied/failed/empty/loading states |
| AS-12 | Android | Emulator and physical-device evidence distinguished, 9 roles, back, print, CSV, persistence, LAN loss |
| AS-13 | Recovery | Versioned migrations, backup + separate restore, failed upgrade recovery |
| AS-14 | Production boundary | No demo controls/data, browser DB credentials, donor cloud dependency or implicit offline mutation |

Run targeted checks during development and full affected regression at milestone completion. Record passed/failed/not run with build/version, environment and evidence location. Do not lower assertions to hide changed totals. Do not claim current GST compliance, live OTA, verified UPI settlement, OCR or AI based on mock behavior.

## 11. Delivery and stop conditions

For each work package, deliver:
- What changed and which requirement it satisfies.
- Imported files and retained notices, or an explicit “no donor code imported” statement.
- Commands executed and actual results; unrun checks and why.
- Runnable artifact/location and a short reproducible walkthrough.
- Remaining mocks, policy decisions, limitations and next package.
- Updated HANDOFF and qualification/acceptance register.

Stop only the dependent work for an unresolved license, missing production authorization, failed critical invariant or unavailable required infrastructure. Continue independent authorized work. Ask for a concrete decision with options and impact rather than a general request to proceed.

Do not declare assembly complete until the authorized scope is implemented and verified. A production release additionally requires approved operating policies, device acceptance, recovery evidence and explicit live-use authorization.
