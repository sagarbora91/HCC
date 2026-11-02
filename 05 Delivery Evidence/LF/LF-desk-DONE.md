# LF lane L3 (lf-desk + lf-alerts) — DONE report (2 Nov 2026, branch `claude/lf-desk`)

Contract: `02 Planning/agents/LF-CONTRACT.md` §8 (A3 A4 A5 A6) and §9 (B3 B4 A8). Fictional data only.

## Status in one line
**Prototype: complete.** **Server: migrations, rules, reminders module and wake-up column done; the stay / booking / master wiring is NOT done** (listed under "Not done").

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
`npm test`: **2412 / 2413** (before this lane on the same base: 2390 tests, all green except the known FIT phone-layout test; the one failure now is that same test). `npx tsc --noEmit` clean. `npm run build` ok. `tools/fit-screen-check.mjs` run for desk-alerts, customers, inhouse, rooms, arrivals (see "Fit check").

## Server (`08 Production`)
* Migrations **4695** purpose master + `inv.reservation.purpose_code`; **4696** vehicle master + `vehicle_no` / `vehicle_type` + index; **4697** `gst.stay_form_c`; **4698** `checkout_basis`; **4703** `ops.guest_request.wake_at_utc` (backfilled; written on insert by `SqlGuestServicesStore`); **4704** `plat.reminder`; **4705** `PERM-RMD-001`. In `migrations.lock.json` + `Hcc.slnx` (LF-DESK / LF-ALERTS blocks). The Keys SQL tests (full migrate on a fresh database) pass, so the DDL applies.
* `Hcc.Domain/Stays/DeskFieldRules.cs` (+ `Hcc.Stays.Tests/Kernel/DeskFieldTests.cs`, 16 tests) and `Hcc.Domain/Reminders/ReminderRules.cs` (+ `Hcc.Platform.Tests/Kernel/ReminderRulesTests.cs`).
* Reminders module: `Hcc.Contracts/Reminders`, `Hcc.Application/Reminders` (commands, queries, port), `SqlReminderStore`, `RemindersEndpoints` (`POST /commands/SaveReminder|MarkReminderDone|DismissReminder`, `GET /reminders?bucket=`), registered in the two LF-ALERTS blocks of `HccComposition.cs`.
* `dotnet build "08 Production/Hcc.slnx"`: 0 warnings, 0 errors.

## Deviations from the contract (decisions for the integrator / owner)
1. **Migration numbers are below the lock's highest (4721, KEYS).** The contract assumed 4605. Added with `migrations-lock.mjs --add --accept-out-of-order` (reason recorded). The runner on an installed hotel PC that already has 4721 must accept out-of-order scripts, or the block must be renumbered above 4721 at integration.
2. **`frontdesk.purposeRequired` is enforced only when the command carries `desk:true`** (sent by every check-in screen). Fixtures and 53 older calls to `CHECKIN` do not send a purpose; making the rule unconditional breaks the accounting golden month and ~40 tests. The integrator can drop the `cmd.desk` condition and give those callers a purpose. The server must enforce the setting unconditionally on `CheckIn` (not yet built).
3. **Late check-out `full-night`** is not a CHECKOUT option: the handler answers "A full night is an extension: use Move / extend". `half-day` and `waived` work inside `CHECKOUT`; the new `STAY_LATE_CHARGE` posts the same line (or the zero waiver line) *before* checkout so the folio can be settled first (the half-day line makes the balance positive, and `CHECKOUT` is atomic).
4. **Reminder permission**: the prototype maps PERM-RMD-001 to `checkin | expenses | approve` (contract brackets) = Owner, Manager, Reception, **Cashier**; the server seed gives Owner, Manager, Reception, **Accountant** (contract table). Needs an owner answer (add to OQ list).
5. `GSVRequest.wakeAt` is also set for the existing "time" field of the Wake-up call dialog (HH:mm), so old requests and the old dialog keep working; the explicit `wakeAt` (today / tomorrow, after the clock) is validated when given.
6. No `registerRoomHold` / `selectors.ts` change: a stay already holds only nights `[arrival, departure)`, so a 24h stay never holds the departure night; the test proves the room is free after check-out and clean.
7. F9 (rate change) and Alt+B (counter bill) degrade to their page until lf-rate / lf-ui register `stay-rate` / `counter-bills`; a missing page makes the key inert.

## Not done (hand-over)
* **Server wiring of the stay fields**: `BookRequest` / `CheckInRequest` / `CheckOutRequest.LateCharge` / `StayView` fields, reservation and stay stores writing `purpose_code`, `vehicle_*`, `checkout_basis`, `gst.stay_form_c`, `GET /stays/form-c` (PERM-DOC-004), availability for 24h stays, the stays `SettingsProvider` keys, and the masters framework + `ImportLists` entries for `purposes` / `vehicle-types` (tables and templates exist). `Hcc.Stays.Tests` SQL tests for these.
* Reminders: `reminders.leadDaysDefault` is the constant 3 on the server (settings provider not wired); no SQL integration test for `SqlReminderStore` (kernel tests only). `clients/sync/reminders.ts` and the CHECKIN field mapping in `clients/sync/frontdesk.ts` are not written (HTTP mode keeps the old behaviour; the lf-alerts desk banner works from synced requests).
* Walk-in flow (both shells) does not ask for purpose / vehicle: a foreign walk-in fails at check-in with the Form C message and the recovery button "Continue in guided check-in" (existing path) completes it. Booking forms do not yet show purpose / vehicle (the commands accept them).
* `tools/fit-modals.json` openers for `rmd-edit`, `shortcuts`, `form-c-print` (integrator).

## Fit check
`node tools/fit-screen-check.mjs --pages desk-alerts,customers,inhouse,rooms,arrivals --roles owner --viewports lap-1366 --no-shots --no-scale-pass --no-modals` (with `PLAYWRIGHT_DIR=/opt/node-tools/node_modules/playwright`, built `dist`): **PASS** — desk-alerts (page + 4 tabs), customers (+ Customers and Foreigner register tabs, after one font fix), inhouse, rooms, arrivals. Not run here (CPU-bound, 4 CPUs shared): the other four viewports, the reception role, the 1.3 text-scale pass, modal clipping, phone widths. The coordinator's full `--pages counter-bills,desk-alerts,customers,cashier,inhouse,folios,syshealth` run still has to cover them.
