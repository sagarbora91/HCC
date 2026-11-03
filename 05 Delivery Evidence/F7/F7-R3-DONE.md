# F7-R3 DONE — guests + rooms (batch 1)

Lane R3 guests+rooms · Sonnet · branch `claude/f7-r3` (worktree `/home/user/wt-f7-r3`) · base `526e0776` (engine R1 + hub R2 merged and wired).

## Commits
- `e466babd` hub tests on the real engine; prototype reports, Lodge layouts, goldens
- `1d173fc0` server reports + Lodge layout builders + SQL tests
- (this commit) F7-R3-DONE

## First task — hub tests (`03 Prototype/tests/stream-rpt-hub.mjs`)
- "honest empty state while the catalogue is empty" now runs on the real catalogue: Owner / Reception get the hub with reports (no empty state); the empty state is tested for roles that may open no report (Cashier, Tech admin), asserted against `reportCatalogue(s,uid).length===0`.
- "viewer shows the engine error" now uses an unknown report id (`rpt.rooms.not-a-report`): NOT_FOUND message shown, no table / cards, every export button disabled; a second test runs a real report (room status) for the Owner with no error pane. `tests/rpt-hub-entry.tsx` exports `reportCatalogue` (hub-engine) for it.

## Reports built (prototype AND server, same column keys / order / flags)
| Id | Prototype | Server | Notes |
|---|---|---|---|
| `rpt.guests.movement-register` | `src/domain/reports/guests/movement-register.ts` | `Reports/Guests/MovementRegisterReport.cs` | D-FWD, 92 days max, 90 ahead (selector guard); money = late charge + balance (RPT-002) |
| `rpt.guests.guest-register` | `guest-register.ts` | `GuestRegisterReport.cs` | desk right PERM-STY-001; ID masked everywhere; server also lists `lgc.stay` for ranges before 2026-04-01 |
| `rpt.guests.form-c-register` | `form-c-register.ts` | `FormCRegisterReport.cs` | passport + visa masked; "To submit: N" subtotal row on top |
| `rpt.guests.rate-changes` | `rate-changes.ts` | `RateChangesReport.cs` | viewPerms + gate PERM-RPT-002; total row = count and net change (new - old) |
| `rpt.rooms.room-status` | `rooms/room-status.ts` | `Reports/Rooms/RoomStatusReport.cs` | D-NOW, `ownShift` (HK / Maintenance A·A grants count), names only with PERM-RES-001 |
| A16 layouts | `guests/layouts.ts` (`registrationCardLayout`, `dayEndLayout`) | `LodgeLayouts.cs` (+ `RegistrationCardAsync`) | Lodge look, HCC letterhead, ID masked, money block only with PERM-RPT-002; no hex colour |

Helpers: `guests/shared.ts` / `GuestsSql.cs` (city of address, selector range guard, folio-balance SQL = FolioTotals arithmetic).
Migrations: **none needed** (4420–4423 unused; all data was already in LF / stays tables).

## Tests
- Prototype: `tests/stream-rpt-guests-rooms.mjs` **14 tests** (registry/columns/flags, totals = sum of rows, filters, range and look-ahead limits, legacy flag, roles per report incl. Cashier / Tech admin refused, masks: full ID / passport / visa never in JSON, Excel, CSV or print text, formula neutralisation, Form C and rate-change fixtures, layouts, goldens). Hub test file: 13 tests (was 12 + 1 new).
- Goldens: 5 new files in `tests/rpt-golden/goldens/` (12 cases), screen = Excel = print on each.
- Server: `tests/Hcc.Reporting.Tests/Guests/GuestsRoomsSqlTests.cs` **10 SQL tests** on a disposable `HccTest_*` DB (seeded rooms, guests, stays, folio charges/receipts, Form C, rate changes, block, legacy stay): every result goes through `RendererTests.ThreeWay` (screen = Excel = PDF text = print). `Hcc.Reporting.Tests` 25 -> **35/35**.
- Counts: prototype `npm test` 2586 -> **2614/2614** (tsc clean); `dotnet build Hcc.slnx` 0 warnings, 0 errors. `Hcc.Operations.Tests` HostingTests: 3 failures from Windows-only backup-folder paths on this Linux box (not from this lane).

## Integration needed (exact edits for f7-int)
- `03 Prototype/src/domain/reports/catalogue.json` and `08 Production/tests/Hcc.Reporting.Tests/catalogue.json`: regenerate with `node tools/rpt-catalogue.mjs` after merging with R4 (both lanes touch it).
- `08 Production/tests/Hcc.Reporting.Tests/EngineTests.cs`: `ServerCatalogue()` now spreads `F7R3Reports()` (an `// F7-R3` block); R4 adds its own selectors to the same list.
- `08 Production/tests/Hcc.Reporting.Tests/RendererTests.cs` `ExcelOf`: two lines (`DateOnly` -> Excel serial day, `bool` -> Yes/No) marked `// F7-R3` so the three-way check covers date and Yes/No cells.
- `HccComposition.cs`: five `services.AddReport<...>()` lines in the `// F7-RPT` block (marked `// F7-R3`).
- Catalogue §2 status lines: the five ids are `built` in code (were `planned`).
- Entry points: the registration-card / day-end layouts are pure builders, not wired to buttons (GuestProfile's existing `guest-regcard` modal and the Closing report print stay as they are until the owner approves the switch).
- Windows PC only: fit-check `reports` for the new reports, print preview of the layouts on the Tab, golden PDF via `tools/pdf-drift.mjs` (no PDF golden added for these five — they need a seeded DB; the three-way text check covers them).

## Open questions / decisions for f7-int (not stated in the catalogue)
1. **Form C "submitted" / "Submitted on"**: HCC has no FRRO submission record. Built as submitted = Form C recorded on the stay; "Submitted on" empty. Needs a field (LF A5 follow-up) if the owner wants real submission dates.
2. **Check-out time** (movement register, guest register): the prototype records no instant, so a departed stay shows the scheduled time (departure date + property check-out time); the server uses `gst.stay.checked_out_utc`. Check-in time = recorded, else arrival + property check-in time (prototype, old bookings).
3. **City** (guest register): no city field exists; derived from the address (segment before the state). Same rule both sides.
4. **Desk right**: the catalogue says "page permission `checkin`"; the matrix row of that page is PERM-STY-001, used as `deskPerms` for guest register, Form C register and room status (plus PERM-HK-001 for room status). Reception holds PERM-STY-001, not PERM-HK-001.
5. **Room-status guest column** is empty (not masked) without PERM-RES-001, so the Accountant sees no names (catalogue: names only Owner, Manager, Reception); the engine's generic mask would have shown them to anyone with billing.
6. **Out of order** on the server = an active `ops.room_block` tonight; the prototype also counts a group-block hold (`roomStatus` -> Blocked). Group holds are not read on the server yet.
7. Balance due (movement register) can be negative (advance held, nothing posted yet) — shown as the folio's real balance; not clipped.
8. Rate-change footer uses ASCII "-" in "(new - old)": the in-repo PDF writer (WinAnsi) cannot print U+2212.
9. Legacy: guest register lists legacy stays (server, from `lgc.stay`); movement register and Form C have no legacy data (the old software had no such fields) and return empty with the "legacy" flag set; the prototype has no legacy rows at all.

## HANDOFF bullet
F7-R3 guests + rooms done (claude/f7-r3): movement register, guest register (statutory, ID masked, legacy stays on the server), Form C register, rate-change detail and room status on the report engine both sides, plus Lodge-look registration-card and day-end layouts; hub tests moved to the real catalogue; 14 prototype + 10 SQL tests, 5 goldens; no migrations.
