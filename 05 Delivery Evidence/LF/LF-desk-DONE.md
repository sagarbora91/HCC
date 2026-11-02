# LF lane L3 (lf-desk + lf-alerts) — DONE report (2 Nov 2026, branch `claude/lf-desk`)

Contract: `02 Planning/agents/LF-CONTRACT.md` §8 (A3 A4 A5 A6) and §9 (B3 B4 A8). Fictional data only.

## Status in one line
**Prototype and server: complete for A3–A6, B3, B4, A8** (coordinator follow-up, 2 Nov: server wiring, masters / import, sync files, reminders roles). Remaining items are listed under "Not done".

## Prototype (all in `03 Prototype`)
| Item | Where |
|---|---|
| A3 masters `purposes`, `vehicleTypes` (seeded 8 / 5; CSV wire `purposes`, `vehicle-types`; Owner / Tech admin) | `domain/streams/frontdesk-master.ts` LF-DESK block, `fixtures/ext/masters-seed.ts`, `domain/streams/mastersio.ts` |
| Pure rules (vehicle normalise + search, Form C validation, visa mask, foreigner register + CSV rows, `stayDueOut`, `stayLateCheckout`, early-check-in warning) | `domain/streams/desk-fields.ts` |
| Six Front-desk settings (purposeRequired, formCRequired, defaultCheckoutBasis, lateCheckoutGraceHours, earlyCheckinGraceHours, halfDayPct = money, Owner only) | `desk-fields.ts` `deskSettingDefs` → `config.ts` (one registration line) |
| Commands `STAY_CHECKOUT_BASIS`, `STAY_FORM_C`, `STAY_LATE_CHARGE` + the BOOK / CHECKIN / CHECKOUT blocks | `services/mock/handlers/desk.ts`, one marked line each in `reservations.ts`, `guests.ts`, `folio.ts` |
| Check-in "Stay details" (purpose, vehicle, basis, Form C form when foreign) desktop + phone | `features/guests/DeskFields.tsx`, `CheckIn.tsx`, `mobile/frontdesk/Arrivals.tsx` |
| Registration card lines, Form C print sheet (`form-c-print` modal), Foreigner register tab + CSV, booking-dialog "Form C" button | `GuestProfile.tsx`, `FormCPrint.tsx`, `register.tsx`, `customers/Customers.tsx` (page tabs Customers · Foreigner register) |
| Due-out on In-house list and room tile; vehicle search (In-house, Arrivals, Customers) | `InHouse.tsx`, `rooms/RoomCard.tsx`, `CheckIn.tsx`, `domain/streams/customers.ts` |
| B3 `GSVRequest.wakeAt` validation + `GSV_WAKE_SNOOZE` | `handlers/services.ts` LF-ALERTS blocks |
| B4 reminders: slice helpers, `RMD_SAVE` / `RMD_DONE` / `RMD_DISMISS`, setting `reminders.leadDaysDefault` | `domain/streams/reminders.ts`, `handlers/reminders.ts` |
| Desk banner (both shells), shortcuts (desktop `keydown`), user-menu "Keyboard shortcuts", palette key hint, Dashboard attention line | `app/DeskAlerts.tsx`, `app/shortcuts.ts`, `App.tsx` (2 blocks), `mobile/sheets.tsx`, `SearchPalette.tsx`, `overview/Overview.tsx` |
| Page `desk-alerts` (FIT split, tabs Today · Upcoming · Done · Wake-ups, phone cards), modals `rmd-edit`, `shortcuts` | `features/alerts/*`, `navmap.ts`, `subscreens.ts`, `registry.ts` |
| Extractor switches (names only) + fictional fixture | `tools/legacy/extract.py --purposes --vehicle-types`, `tools/legacy/selftest-lf/` |
| Import templates + samples | `08 Production/deployment/import-templates/{templates,samples}/purposes.csv, vehicle-types.csv` |

Tests: `tests/stream-lf-desk.mjs` (13) and `tests/stream-lf-alerts.mjs` (9), incl. "24h stay frees the room after check-out". Edited existing tests (counts / rows only): `stream-masters-io.mjs` (30 lists), `stream-masters-phone.mjs` (30 masters), `stream-rbac-matrix.mjs` (4 commands + page), `stream-fit-guest.mjs` (Customers page tabs).
`npm test`: **2413 / 2415** after the follow-up (2 sync expectations took `desk:true`; re-run: sync-frontdesk, sync-money, stream-lf-* green); before the follow-up 2412 / 2413 (before this lane on the same base: 2390 tests, all green except the known FIT phone-layout test; the one failure now is that same test). `npx tsc --noEmit` clean. `npm run build` ok. `tools/fit-screen-check.mjs` run for desk-alerts, customers, inhouse, rooms, arrivals (see "Fit check").

## Server (`08 Production`)
* Migrations **4695** purpose master + `inv.reservation.purpose_code`; **4696** vehicle master + vehicle columns + index; **4697** `gst.stay_form_c`; **4698** `checkout_basis`; **4699** `gst.stay.late_checkout_decision`; **4700** `imp.batch` master check (purposes, vehicle-types); **4703** `ops.guest_request.wake_at_utc` (backfilled, written on insert); **4704** `plat.reminder`; **4705** `PERM-RMD-001`. In `migrations.lock.json` (out-of-order accepted, coordinator ruling 1) and `Hcc.slnx`.
* Rules: `Hcc.Domain/Stays/DeskFieldRules.cs` (vehicle, Form C, visa mask, due-out, late check-out) and `Hcc.Domain/Reminders/ReminderRules.cs`.
* Stays: `CheckInRequest` (+ PurposeCode, VehicleNo, VehicleType, CheckoutBasis, FormC, **Desk**), `CheckoutStayRequest` (+ LateCharge, LateReason, **Desk**), `CreateReservationRequest` (+ PurposeCode, VehicleNo, VehicleType, CheckoutBasis); `DeskFieldService` (settings, validation, Form C, late decision, register), `SqlDeskFieldStore`; commands `PostLateCheckoutCharge` (half-day posts a service-kind line at the room slab of the night rate through `BillingService.PostLateCheckoutAsync`; waived needs PERM-APR-001 + reason) and `SaveStayFormC`; `GET /stays/form-c?from&to` (PERM-DOC-004, visa masked for readers without it); six Front-desk settings registered by `StaysSettings.Provider` (half-day % = money, Owner only).
* Masters / import: `purposes` and `vehicleTypes` in the front-desk masters framework (commands `SavePurpose|RetirePurpose|…`, `SaveVehicleType|…`, `GET /masters/purposes|vehicle-types`, built-in defaults until the first change) and the import lists `purposes`, `vehicle-types` (templates + samples exist).
* Reminders: contracts, `ReminderCommands` / `ReminderQueries`, `SqlReminderStore` (role validity windows honoured), `/commands/SaveReminder|MarkReminderDone|DismissReminder`, `GET /reminders`.
* Wake-up: `wake_at_utc` written when a Wake-up call is created.
* Tests: `DeskFieldTests` (16 kernel), `DeskFieldSqlTests` (6 SQL: purpose required by Desk, vehicle, Form C + masking + register, late decision half-day / waived, booking fields), `ReminderSqlTests` (2), `ReminderRulesTests` (kernel), `FrontDeskMastersSqlTests` (new LF lists), `ListMastersSqlTests` + `ImportListsKernelTests` (26 lists). Runs: Stays.Tests 24 LF tests pass (whole project: 1 failure, `RoomBoardQuerySqlTests` page-read ceiling, **identical on the untouched base commit**); FrontDeskMasters.Tests 20/20; Import.Tests 21/21; Platform lock/rules tests pass. `dotnet build "08 Production/Hcc.slnx"`: 0 warnings.

## Deviations from the contract (decisions for the integrator / owner)
1. Migration numbers are below the lock's highest (4721, KEYS); added with `--accept-out-of-order` (coordinator ruling: fine).
2. **`desk:true` rule (coordinator ruling 2).** `frontdesk.purposeRequired` is enforced only when the check-in carries `desk:true`; every UI check-in path sends it (guided check-in desktop + phone, walk-in desktop + phone, which now ask for a purpose). The server mirrors it: `CheckInRequest.Desk`, sent by the sync layer from `cmd.desk`. Same gate on the late check-out rule: `CheckoutStayRequest.Desk` (the sync layer always sends `desk:true`; the prototype applies the rule unconditionally); Form C for foreign guests is enforced regardless of the flag.
3. **Late check-out `full-night`** is not a CHECKOUT option: the handler answers "A full night is an extension: use Move / extend". `half-day` and `waived` work inside `CHECKOUT`; the new `STAY_LATE_CHARGE` posts the same line (or the zero waiver line) *before* checkout so the folio can be settled first (the half-day line makes the balance positive, and `CHECKOUT` is atomic).
4. **Reminder roles aligned to the contract (coordinator ruling 3)**: prototype and server both = Owner, Manager, Reception, Accountant (`rmdCanManage`, nav gate `navRoleOk`, commands, RBAC matrix). The desk banner (§9.1 text) stays on Owner, Manager, Reception, Cashier for wake-ups; the Cashier sees no reminders.
5. `GSVRequest.wakeAt` is also set for the existing "time" field of the Wake-up call dialog (HH:mm), so old requests and the old dialog keep working; the explicit `wakeAt` (today / tomorrow, after the clock) is validated when given.
6. No `registerRoomHold` / `selectors.ts` change: a stay already holds only nights `[arrival, departure)`, so a 24h stay never holds the departure night; the test proves the room is free after check-out and clean.
7. F9 (rate change) and Alt+B (counter bill) degrade to their page until lf-rate / lf-ui register `stay-rate` / `counter-bills`; a missing page makes the key inert.

## Not done (hand-over)
* `reminders` slice has no sync **loader**: it needs a `GET /reminders` route in `sync/reads.ts` and a row in the role-read matrix (`Hcc.Integration.Tests` `RoleReadMatrixTests` / `tests/_role-reads.json`); the three reminder commands are mapped (`clients/sync/reminders.ts`). `GSV_WAKE_SNOOZE` has no server command (listed as unmapped).
* `reminders.leadDaysDefault` is the constant 3 on the server (not a registered setting).
* The typed reservation client (`BOOK` over HTTP) does not yet send purpose / vehicle / basis (the server accepts them in `CreateReservationRequest`).
* Late check-out `full-night` = extend the stay (message), as in the prototype; the server posts the half-day line as a service-kind line (a room-kind line is unique per night).
* `tools/fit-modals.json` openers for `rmd-edit`, `shortcuts`, `form-c-print` (integrator).

## Fit check
`node tools/fit-screen-check.mjs --pages desk-alerts,customers,inhouse,rooms,arrivals --roles owner --viewports lap-1366 --no-shots --no-scale-pass --no-modals` (with `PLAYWRIGHT_DIR=/opt/node-tools/node_modules/playwright`, built `dist`): **PASS** — desk-alerts (page + 4 tabs), customers (+ Customers and Foreigner register tabs, after one font fix), inhouse, rooms, arrivals. Not run here (CPU-bound, 4 CPUs shared): the other four viewports, the reception role, the 1.3 text-scale pass, modal clipping, phone widths. The coordinator's full `--pages counter-bills,desk-alerts,customers,cashier,inhouse,folios,syshealth` run still has to cover them.
