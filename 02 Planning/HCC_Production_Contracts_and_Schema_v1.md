# HCC production contracts & schema package (A3)

Version 1.0 · 24 Oct 2026 · Status: **design package + verified SQL spike. Production construction (A4, `08 Production/`) is NOT authorized yet** — prototype acceptance (D5/D6, BL-023 walkthrough) and an owner go-ahead are still the gate (DEC-17, APR-001). Inputs: assembly plan §6, instructions §5–§7, RBAC matrix, Financial Rulebook, A1 register.

## 1. Identity and session model
- **Principal** = staff user (`plat.app_user`: id, display name, enabled, valid_from/until, property scope). Demo users, the role switcher and fixture identities never ship.
- **Credential**: local password (PBKDF2-SHA256 via .NET `Rfc2898DeriveBytes`, or Argon2id if a reviewed library is approved) or PIN + device binding for shared tablets — owner decision (§9 Q4). No donor identity stack.
- **Session**: server-issued opaque token (HttpOnly cookie for the browser, secure storage in the APK), idle timeout 30 min, absolute 12 h, revocation on disable/role change takes effect on the next request (tested as AS-07).
- **Authorization**: every command/query resolves `principal → roles → grants (PERM ID, scope, limit) − explicit denies` (DEC-040 order). Menu visibility comes from a `/me/capabilities` query; the server re-checks every request. Actor identity comes from the session, never a client `uid`.
- **Scopes**: property · own shift (cashier) · assigned task/work order (housekeeping/maintenance) · field masks (ID document numbers, salaries, money for housekeeping/maintenance).

## 2. API conventions
- JSON over HTTPS on the hotel LAN; `/api/v1`. Commands are `POST /api/v1/commands/<Name>`; queries `GET` resources with `page`, `pageSize ≤ 100`, filters. No endpoint returns the whole hotel state.
- Money: integer paise as JSON numbers (safe-integer checked; max ₹10 crore per field). Dates `YYYY-MM-DD`; event times ISO-8601 UTC. Business date is returned by the server, never computed by the client.
- Every command carries `Idempotency-Key` (≤ 80 chars). Stored per `(property, principal, operation, key)` with SHA-256 of the canonical payload (spike T08). Same key + same payload → original result; different payload → `409 IDEMPOTENCY_MISMATCH`.
- Mutations of versioned records carry `expectedVersion` (SQL `rowversion`, base64). Mismatch → `409 STALE_VERSION` (spike T04).
- Errors: `{code, message, details?}`; codes and HTTP status:

| HCC code (prototype `ErrorCode`) | HTTP | Notes |
|---|---|---|
| VALIDATION | 422 | field errors in `details` |
| FORBIDDEN | 403 | never reveals hidden records |
| NOT_FOUND | 404 | also for records outside the principal's scope |
| NOT_AVAILABLE | 409 | inventory conflicts incl. lost race (spike C1/C4) |
| RESTRICTED | 409 | stay restriction; `details.violations[]` (A2.1) |
| INVALID_TRANSITION | 409 | status machine (A1 H01 map) |
| STALE_VERSION | 409 | |
| DAY_CLOSED | 409 | late write after close barrier |
| APPROVAL_REQUIRED | 403 | `details.approvalKind`, limit |
| IDEMPOTENCY_MISMATCH | 409 | |
| UNAVAILABLE | 503 | DB/LAN; client disables critical mutations, no offline writes |

## 3. Contracts (first slice and core)
Request → result. `K` = Idempotency-Key required; `V` = expectedVersion required.

| Contract | Request essentials | Result / behaviour | Permission (RBAC) |
|---|---|---|---|
| GetAvailability (query) | arrival, departure, party, roomTypeId?, ratePlanId? | rooms/types with `available` + reason codes (Blocked, Over capacity, Booked, Held, GroupHold, RESTRICTED) | PERM-RES-001 |
| QuoteStay | arrival, departure, party, roomId/typeId, ratePlanId | `quoteId`, version, nightly `{date, ratePaise, taxBps, taxPaise}`, totals, `expiresAt`, violations | PERM-RES-002 |
| CreateReservation K | quoteId+version, guest (name, phone?), roomId, hold?, restrictionOverride? | reservationId, publicNo, version; room nights allocated in the same transaction (spike C1–C4) | PERM-RES-002; override needs approve grant |
| AmendReservation K V | reservationId, arrival/departure/roomId, priceDecision (keep/requote) | atomic re-allocation or conflict; original untouched on failure (spike T02) | PERM-RES-003 |
| CancelReservation / MarkNoShow K V | reservationId, reason | status per H01 map; no-show only on/after arrival; money must be settled first | PERM-RES-004 |
| RegisterGuest K V | reservationId, permitted registration fields | guest + registration; ID numbers field-masked on read | PERM-GST-002 |
| CheckIn K V | reservationId | stay + room segment + folio active; room must be Ready, unblocked | PERM-STY-001 |
| PostRoomNight K | stayId, serviceDate | one canonical charge with tax snapshot; replay returns existing (spike T07) | PERM-CLS (night audit) |
| RecordReceipt K | folioId, amountPaise, tender, reference, kind (payment/deposit/deposit-return), tillId | receipt (append-only); cash requires open shift on that till | PERM-PAY-001/002 |
| RequestAdjustment / DecideApproval / ExecuteAdjustment K V | target+version, action (discount/refund/credit/correction), amount, reason | approval bound to action+target+amount+version (DEC-041); approver ≠ requester; refund remainder under row lock (spike C3) | PERM-FOL-003/005, PERM-PAY-003/004, PERM-APR-001 |
| CheckoutStay K V | stayId, companyCreditApprovalId? | departure + room Dirty in one transaction; outbox `StayDeparted` → one housekeeping task (spike T09 dedup) | PERM-STY-003 |
| TransitionHousekeepingTask K V | taskId, action, checklist | permitted transition; readiness update | PERM-HK-*; assigned scope |
| RunBusinessDayClose K | businessDate, runId | resumable steps, persisted progress, immutable closing snapshot, late-write barrier | PERM-CLS-* |
| GetFolio / GetReport (query) | id or filter/range/page | authorized fields only; report definitions shared with closing | PERM-FOL-001, PERM-RPT-* |

Prototype command → contract mapping and the permission coverage of all 165 live mock commands: `07 Integration Lab/evidence/permission-coverage.md`.

## 4. Schema (SQL Server 2025 Express)
Schemas: `inv` (property, room types, rooms, reservations, room nights, holds/blocks), `gst` (guests, registrations, document metadata), `bill` (folios, quotes/quote nights, charges, invoices, credit notes), `pay` (receipts, allocations, tills, shifts, cash movements, counts), `ops` (housekeeping tasks/inspections, maintenance blocks, expenses), `cls` (closing runs/steps/snapshots), `plat` (users, roles, grants, sessions, approvals, audit, idempotency, outbox, consumer dedup).

Rules (all verified in the spike unless marked *design*):
1. **Exclusive inventory = `inv.room_night(room_id, night)` primary key**, one row per room per night, inserted in ascending night order (consistent lock order). Overlap is impossible in any interleaving; adjacent stays share no row (T02, T03, C1, C2, C4: 12 parallel connections → 1 winner, 0 deadlocks).
2. Room-type capacity and group holds: *design* — `inv.type_night_count` with `CHECK (sold + held ≤ sellable)` updated in the same transaction as `room_night`; group pickup passes its block id as explicit command context (no global `withHoldExempt`).
3. Money `BIGINT` paise with `CHECK (> 0 AND ≤ 10^10)` on receipts; signed amounts only on charges/adjustments. Rounding: half away from zero, symmetric (lab H02 `applyBps`). Drivers return BIGINT as string/BigInt — convert with a safe-integer check at the adapter (A4).
4. Receipts are append-only; refunds are linked child rows (`CK_refund_link`); remainder checked under `UPDLOCK, HOLDLOCK` on the parent (T05, T06, C3).
5. Charges carry their tax snapshot; one room charge per folio/service date (filtered unique index + locked check, T07).
6. Every command transaction writes audit + outbox (+ idempotency record) atomically (T09). Consumers record `(consumer, outbox_id)` before applying effects.
7. Every stored procedure / application transaction uses `XACT_ABORT ON` **and** `CATCH → IF @@TRANCOUNT>0 ROLLBACK; THROW` — spike finding: without the explicit rollback a caller's TRY/CATCH sees a doomed transaction whose rows are still visible (T02 first run).
8. Do not implement "insert and catch duplicate key" inside a caller's transaction — the transaction becomes uncommittable (T07 first run). Use locked check-then-insert with the unique index as backstop.
9. Connections must set `QUOTED_IDENTIFIER ON` (filtered indexes; sqlcmd needs `-I`). Drivers default ON — assert it at connection start.
10. Dates: `DATE` for calendar/business/service dates; `DATETIME2(3)` UTC for events (T10). Property timezone validated on save (lab H04).

Draft DDL subset (executable): `07 Integration Lab/sql-spike/01-schema.sql`. Full migration set is A4 work.

## 5. Spike evidence (24 Oct 2026)
`node "07 Integration Lab/sql-spike/run-spike.mjs"` → **15/15 passed** on Microsoft SQL Server 2025 (RTM) 17.0.1000.7 Express, local instance `.\SQLEXPRESS`, Windows authentication, disposable database created and dropped (no other database touched). Results: `07 Integration Lab/evidence/sql-spike-results.json`.

Not covered by this spike (A4): the .NET data layer (Microsoft.Data.SqlClient 6.x as in ETP) and its BIGINT/Unicode/TLS/rowversion behaviour — rerun T01–T10 and C1–C4 through the C# adapter; TLS with a real certificate (spike used `-C` trust-server-certificate); SQL Server 2025 Express edition limits (**Unverified** — read Microsoft's current edition page before sizing; DEC-03); backup/restore (no SQL Agent in Express — reuse ETP's Task Scheduler scripts); restart during day close.

## 6. Frontend boundary
Typed client interface per feature (`ReservationsClient`, `FolioClient`, …) with a mock adapter wrapping today's `execute()` for regression and an HTTP adapter for production; first migrate availability → quote → create reservation → booking details (instructions §8). Production builds cannot select the mock adapter; demo toolbar, scenario switcher, simulated clock and Reset are excluded at build time. On API loss: read-only banner, critical mutations disabled, no IndexedDB writes presented as committed.

## 7. Server stack (DEC-054, 24 Oct 2026)
.NET 10 ASP.NET Core Web API (C#) hosted as a Windows service on the hotel PC, SQL Server 2025 Express (`.\SQLEXPRESS`, its own `Hcc` database next to ETP's), Microsoft.Data.SqlClient, Windows-integrated SQL login for the service account. Clients: the existing React web app and the Capacitor APK over LAN HTTPS. Solution layout mirrors ETP: `Hcc.Domain` (rules, no I/O), `Hcc.Application` (use cases + authorization), `Hcc.Infrastructure.SqlServer` (transactions, migrations, outbox, audit), `Hcc.Reporting` (OpenXML/PDFsharp exports), `Hcc.Api` (HTTP, sessions), `Hcc.Operations` (backup, restore drill, scheduled jobs), plus tests. ETP platform code (migration runner with backup gate + `DBCC CHECKDB`, connection policy, backup/restore/drill scripts, installer template, exporters, e-mail, Tally export) is reused with provenance recorded per file; shared pieces may later move to a common `Saagar.Platform` library used by both products. Prototype business rules are ported to C#, with the prototype's tests and fixtures as golden figures. Lab adaptations H02/H04 are ported to C# keeping their Apache-2.0 notices.

## 8. Open decisions (owner)
1. Till model — shared vs separate outlet tills (`HCC_A2_Till_Decision_Pack.md`).
2. Stay-restriction override — prototype default: users with the approve permission (Owner/Manager) may override with a reason (A2.1). Confirm, or restrict to Owner only.
3. Closed-date corrections: adjustment-only vs controlled reopen (DEC-028).
4. Staff sign-in for production: password per user, or PIN on shared tablets.
5. Release scope: first 12 MVP areas for production (DEC-02) — confirm before A4.
6. Production go-ahead (A4) after D5/D6 acceptance.

## Appendix M. Masters wave API (MA-0…MA-7, reviewed by MA-11 on 27 Oct 2026)

Conventions for every master below (plan Part 2 §9): commands are `POST /api/v1/commands/<Name>` with `Idempotency-Key` (same key + same body = the stored result, header `Idempotent-Replayed: true`; same key + different body = `IDEMPOTENCY_MISMATCH`), run through `ICommandRunner` (audit + outbox `MasterChanged` + idempotency record in one transaction). An edit / retire / reactivate / delete carries `expectedVersion` (base64 rowversion; mismatch = `STALE_VERSION`). Master errors are the frozen wire codes with `details.reason` = `MASTER_CODE_TAKEN` / `MASTER_INVALID` (VALIDATION) or `MASTER_IN_USE` / `MASTER_RETIRED` / `MASTER_LOCKED_BY_BOOKINGS` (INVALID_TRANSITION) and `details.field`. Lists are paged (`page`, `pageSize` ≤ 200, `status=active|retired|all`); history routes `…/history` and `…/{id}/history` read `mst.master_history`. Permissions: **PERM-MST-001** (operational lists: Owner, Tech admin — DEC-MA-1) and **PERM-MST-002** (money lists and money fields: Owner — DEC-058). Money values in list reads are null for roles without hotel-money visibility (DEC-061, DEC-MA-11e).

| Module (agent) | Commands | Reads | Permission | Migrations |
|---|---|---|---|---|
| Masters (MA-0) | `SaveFloor`, `RetireFloor`, `ReactivateFloor`, `DeleteFloor`, `ReorderFloors` | `GET /masters/floors` (+ history) | MST-001 | 2801, 2802 (RBAC seed) |
| RoomMasters (MA-1) | `Save/Retire/Reactivate/Delete` + `Reorder…` for `RoomType`, `Amenity`, `OccupancyCharge`; `AddRooms`, `SaveRoom`, `RenumberRoom`, `RetireRoom`, `ReactivateRoom`, `DeleteRoom` | `GET /masters/room-types`, `/masters/amenities`, `/masters/occupancy-charges`, `/masters/rooms` (+ history) | MST-001; rack rates and occupancy charges MST-002; rates visible with MST-002 or PERM-RES-001 | 2810 |
| Catalogue (MA-2) | `Save/Retire/Reactivate/Delete` + `Reorder…` for `MealPlan`, `ExtraItem`, `DiscountReason`; `PostExtra`, `SetReservationMealPlan`, `PostMealPlanNights`, `RequestReasonedDiscount` | `GET /catalogue/meal-plans`, `/catalogue/extras`, `/catalogue/discount-reasons` (+ history), `/catalogue/links` | masters MST-002; PostExtra PERM-FOL-002; PostMealPlanNights PERM-CLS-002 (also run by the close's night step, MA-11); discount PERM-FOL-003 | 2820 |
| MoneyMasters (MA-3) | `Save/Retire/Reactivate/Delete` + `Reorder…` for `PaymentMethod`, `BankAccount`, `ExpenseCategory`, `ExpenseVendor` | `GET /masters/payment-methods`, `/masters/bank-accounts`, `/masters/expense-categories`, `/masters/expense-vendors` (+ history) | MST-002 | 2830, **2890** (MA-11: method/tender columns NVARCHAR(60), fixed CHECK lists removed) |
| POS masters (MA-4) | `Save/Retire/Reactivate/Delete` + `Reorder…` for `Outlet`, `OutletTable`, `MenuCategory`, `MenuItem` (`SaveMenuItem` by a Manager = a request the Owner decides with `DecideMenuChange`; GST class / station not requestable — DEC-MA-11b) | `GET /pos/outlets`, `/pos/menu`, `/pos/menu-categories`, `/pos/menu-requests` | MST-001; prices, GST class, outlet GST profile MST-002; reads PERM-POS-001 or MST-001 | 2840 |
| FrontDeskMasters (MA-5) | `Save/Retire/Reactivate/Delete` + `Reorder…` for `BookingSource`, `IdType`, `NumberSeries`, `BookingPolicy` | `GET /masters/booking-sources`, `/masters/id-types`, `/masters/number-series`, `/masters/policies` (+ history) | sources / ID types MST-001 (commission MST-002); numbering and policies MST-002 | 2850, **2891** (MA-11: RST document) |
| PeopleLists (MA-6) | `Save/Retire/Reactivate/Delete` + `Reorder…` for `Designation`, `ShiftPattern`, `LeaveType`, `Holiday` (Wave B's `SavePaidHoliday` / `RemoveHoliday` write the same holiday table) | `GET /hr/designations`, `/hr/shift-patterns`, `/hr/leave-types`, `/hr/holidays` (+ history) | MST-001; pay fields and the holiday calendar MST-002; a paid holiday of a locked pay month cannot change (MA-11) | 2860, 3800 (holiday calendar) |
| OpsMasters (MA-7) | `Save/Retire/Reactivate/Delete` + `Reorder…` for `HkTaskType`, `HkZone`, `ComplaintCategory`, `LostFoundCategory` | `GET /housekeeping/task-types`, `/housekeeping/zones`, `/guest-services/complaint-categories`, `/guest-services/lost-found-categories` (+ history) | MST-001 | 2870, 2871 (seed) |

**Contract changes made by the MA-11 review** (wire-compatible for the production sync layer):
- `PaymentMethodView.surchargeBps`, `BookingPolicyView.cancelChargeBps / noShowChargeBps / depositBps`: now nullable — null for roles without hotel-money visibility; `ExpenseCategoryView.approvalLimitPaise` null for them too.
- `DaySummaryView.otherPaise` (POS day summary): outlet bills paid by a method other than cash (any method settling to a till), UPI or Card; such bills are no longer counted in `roomFolioPaise`.
- `RecordReceipt`, `ExecuteAdjustment` (refund), `SettleCheck`, `SubmitExpense` accept any **active** Payment-methods row usable at that place (by name or code; stored by name); a retired one is refused with `MASTER_RETIRED`. Drawer cash = a method that settles to a till.
- `CreateReservation` refuses a retired booking source (`MASTER_RETIRED`, field `source`).
- `NumberSeries.document` accepts `RST` (restaurant tax invoices, GST rules); RCP / KOT / REG series are reserved (not used for numbering yet).
- `RunBusinessDayClose` / `PostRoomNights`: the night step also posts meal-plan lines (`detail.planLines` on the nights step).
