# F8-RPT-P2-A DONE

Lane rpt-p2-a / Sonnet / branch `claude/f8-rpt-p2-a` / worktree `E:\Projects\HCC\worktrees\f8-rpt-p2-a` / base `f8-setup` 037bd7bc.

## Reports (batch A, SF-CONTRACT section 12)
All eight are **built** on both sides (prototype selector + server `IReportSelector` twin, same column keys, rules and labels).

| Report | Prototype | Server | Status |
|---|---|---|---|
| rpt.guests.reservations-register | `src/domain/reports/p2-a/reservations-register.ts` | `Hcc.Reporting/Reports/P2A/ReservationsRegisterReport.cs` | built |
| rpt.guests.guest-tariff | `p2-a/guest-tariff.ts` | `GuestTariffReport.cs` | built |
| rpt.guests.company-business | `p2-a/company-business.ts` | `CompanyBusinessReport.cs` | built (split-stay rule on the server see Integration) |
| rpt.guests.userwise-activity | `p2-a/userwise-activity.ts` | `UserwiseActivityReport.cs` | built |
| rpt.guests.service-lines | `p2-a/service-lines.ts` | `ServiceLinesReport.cs` | built |
| rpt.rooms.room-wise-revenue | `p2-a/room-wise-revenue.ts` | `RoomWiseRevenueReport.cs` | built |
| rpt.rooms.rooms-left | `p2-a/rooms-left.ts` | `RoomsLeftReport.cs` | built |
| rpt.rooms.allocation-grid | `p2-a/allocation-grid.ts` | `AllocationGridReport.cs` | built |

Shared helpers: `p2-a/common.ts`, server `P2ASupport.cs`. Registration: `register.ts` block `F8-RPT-P2-A`, `HccComposition.cs` services block `F8-RPT-P2-A`, catalogue status lines in `<!-- F8-RPT-P2-A -->`. No migrations used (4450-4451 unused). Tests: `03 Prototype/tests/stream-rpt-p2-a.mjs` (11 tests), `08 Production/tests/Hcc.Reporting.Tests/P2A/P2AReportsSqlTests.cs` (9 tests on a disposable HccTest_* database). Goldens written by `accept.mjs` for the 8 ids (`tests/rpt-golden/goldens/<id>.json`; guest-tariff golden case is FY26 with party `an`, since the report needs a party).

## Commits
See `git log --oneline f8-setup..HEAD` (first commit 0ff63c5b, then the fix/golden/DONE commit).

## Tests actually run (all through Invoke-Serialized.ps1, label f8-rpt-p2-a)
1. `npx tsc --noEmit` in `03 Prototype`: exit 0.
2. `node tools/rpt-catalogue.mjs`: exit 0 (output reverted, see below).
3. `node tests/rpt-golden/accept.mjs`: first run failed (guest-tariff needs `party`), after adding the golden case file it exited 0 and wrote the 8 goldens.
4. `HCC_TEST_ONLY='rpt-p2-a|rpt-engine|golden' npm test`: **113/113 passed** (includes 11 `[F8-RPT-P2-A]` tests and the goldens suite with the 8 new goldens). The first run before the golden stub: 112/113 (only the golden suite failed).
5. `dotnet test tests/Hcc.Reporting.Tests -maxcpucount:1 --logger trx`: 135 tests, 134 passed, 1 failed. All 9 P2A tests passed. The failing test is `EngineTests.Catalogue_json_from_the_prototype_equals_the_server_registry`: it compares the regenerated `catalogue.json` with a hand-written list of server selectors in `EngineTests.cs` (not mine). I then reverted both generated `catalogue.json` files to the base (the integrator regenerates once) so that test is back to its base state; **that revert was not re-run**.

## Not run / unverified
- The full `npm test` (all streams) once at the end: not run (lock queue of 12 lanes, five exit-75 retries per job used); only the filtered run above.
- `dotnet test` after reverting catalogue.json: not re-run.
- Fit check, emulator: not applicable (no UI page).

## Defaults and decisions relied on (no owner questions touched)
- Catalogue says D-S default "last 7 days"; the engine's `bindParams` defaults every non-forward range to the business day. Left to the engine (engine files are forbidden). rooms-left is registered as D-FWD (catalogue: forward dates allowed); allocation-grid stays D-S (past only, max 31 days).
- userwise-activity: permission PERM-ADM-004 (matrix also gives it to Accountant in the prototype; catalogue says Owner, Manager). Server source is the stay record (checked_in_by/utc, checked_out_by/utc), the same facts as the audit entries.
- reservations-register: Created = BOOK audit date (prototype) / `created_utc` local date (server); Quoted total = rate x nights + room GST (prototype) vs quote total or nightly rate x nights (server, no GST when unquoted).
- room-wise-revenue: "Other room charges" = meal plan lines + discounts + corrections; food/laundry/other service lines are excluded (they are in service-lines).
- service-lines: group is label based (same GST label rule on both sides) plus catalogue extra; qty is 1 in the prototype (lines carry no qty), `bill.charge_catalogue.quantity` on the server.
- rooms-left server: held = allotted - picked - released of open group blocks from the business date; out of order = active `ops.room_block`; occupancy from `inv.room_night`.

## Integration needed
- `08 Production/tests/Hcc.Reporting.Tests/EngineTests.cs` `ServerCatalogue()` needs the eight P2A selectors added (an `F8-RPT-P2-A` block with `new Hcc.Reporting.Reports.P2A.<X>Report(null!)`; constructors take SqlPlatform), then `node tools/rpt-catalogue.mjs` once after the merge.
- company-business on the server uses the whole stay of a reservation billed to the company (`inv.reservation.company_id`). SF-CONTRACT R8 (company folio lines only on a split stay) needs the folio payer tables of sf-server / sf-domain's `sfCompanyLines`; the prototype already applies it with `sfFolios` / `sfView`.
- All server aggregates sum charges per reservation through `bill.folio` subqueries (no per-folio join), so they stay correct when `folio_seq` arrives.
- Catalogue statuses (section 2 table) of the eight ids can be set to built from the `F8-RPT-P2-A` lines.

## HANDOFF bullet
F8 rpt-p2-a: eight guests + rooms P2 reports built on the engine (prototype p2-a/, server Reports/P2A); filtered npm test 113/113, Hcc.Reporting.Tests 134/135 (only the catalogue cross-check, which needs the EngineTests.cs list + regenerated catalogue.json from the integrator).
