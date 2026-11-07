# MEAS2-B DONE: guest exceptions (lane B of the meas2 wave, 7 Nov 2026)

Branch `claude/meas2-b`, cut from `claude/meas` 79f7abe7. Files touched: only lane B's files (section 3 of the sizing report), plus one new test file and this note. `MeasurementCatalogue.cs` is not touched.

## Sizing claims checked in code
All the lane B claims were correct:
- The 48-hour rule existed only in the prototype helpers. Neither twin's report called it.
- The other five reports emitted no red group.
- `upgrade_approver` (column 7) was selected but never used.
- The late check-out label `Late check-out · half day` never matched `LCO1 · / LCO2 ·`.
- The upsell sums did not filter on `kind`.
- In the N5 denominator, satisfaction used the booked type first, while `SqlMeasCaptureStore.cs:270` stores the assigned type first.
- `parameters.From!.Value` was used even when the month had been defaulted.

One extra finding: the prototype upsell `sum` left out reversal lines but still counted the reversed original. The server already left out both, so the prototype now matches it.

## Items fixed
| Item | Server (`08 Production/src/Hcc.Reporting/Reports/MeasGuest/`) | Prototype (`03 Prototype/src/domain/reports/meas-guest/`) |
|---|---|---|
| M1 satisfaction 48 h | `SatisfactionReport.cs:79-109`: `TimeProvider? time`. The SQL marks a survey answered when a complaint was raised on the same booking at or after the survey (`EXISTS` on `ops.guest_complaint` joined to `gst.stay`). Red = `SatisfactionRed(nps, target, unanswered)`. Below the 5-answer floor the row is red on the unanswered count only (owner 7 Nov) and still shows no score. The count is written in the basis (`UnansweredNote`, :130). | `satisfaction.ts:38-56`: `lowUnanswered` is called in `select`, with the same floor behaviour and the same `unansweredNote`. |
| M1 reputation | `ReputationIndexReport.cs:35`: also reads the month before the range, so April is compared with March. That month is not shown. Red = `ReputationRed`. | `reputation-index.ts:30` (`reputationRed` + `prevReview`) |
| M1 service recovery | `ServiceRecoveryReport.cs:79`: red = a repeat within 90 days, or `recovery > 0` with no approval | `service-recovery.ts:43` (`recoveryRed`) |
| M1 desk timing | `DeskTimingReport.cs:82`: targets `meas.target.checkinMinutes` (8) and `checkoutMinutes` (6), read through `TargetAsync`. Red = `DeskRed`. | `desk-timing.ts:67` (`deskRed`) |
| M1 upsell + L7 read | `UpsellReport.cs:79-97`: counts free upgrades with `upgrade_approver IS NULL`. Red = `UpsellRed(freeNoApprover, perArrival, TargetAsync("meas.target.upsellPerArrival", 5000))`. `TargetKey` is at :26. | `upsell.ts:57` (`upsellRed`, `measTarget(...)??5000`) |
| M1 repeat | `RepeatLoyaltyReport.cs:100`: the summary row is red when `RepeatFalling(m, m-1, m-2)` | `repeat-loyalty.ts:43` (`repeatFalling`) |
| M3 step (a) | `UpsellReport.cs:34-45`: `kind = 'service'`, live lines only (the `Live` fragment). Late check-out = `LCO1 ·% OR LCO2 ·% OR = GuestsSql.LateChargeLabel`. The basis text is updated. | `upsell.ts:17-23`: `kind==='service'`, the reversed original is left out, `LATE_CHARGE_LABEL` is added, and the basis text is the same as the server's. |
| N5 | `SatisfactionReport.cs` denominator: `COALESCE(rm.room_type_id, r.room_type_id)` (assigned type first). Month bounds are used when From / To are null. | already used the assigned room's type |

Not done (out of scope, as briefed):
- M3 step (b), the `bill.charge.item_code` column.
- Breakfast and extra bed are still matched with "contains", but only on service lines. A `BRK ·` / `EXB ·` prefix rule was not added, because no posting path writes those prefixes today.
- The digest wiring needed no change on either side: it already picks up any row with `group: 'red'`.

## Tests added
- `03 Prototype/tests/stream-meas-rpt-guest.mjs`: 8 `[MEAS2-B]` tests, all run through `runReport`. They cover 47:59 / 48:00 (below the floor and with 5 answers), a complaint at the survey minute or before it, the QR survey, drop 0.30 / 0.29 with April against March, response 79 / 80 %, repeat 90 / 91 days, unapproved compensation, desk 9 / 8 and 7 / 6 plus a stored standard, upsell 4999 / 5000 plus an Owner target of 6000 plus a free upgrade with no approver, the M3 lines (food breakfast, half-day late check-out, reversed UPG) and repeat falling / flat. One test checks that the satisfaction red row reaches `measDigestBlock`; it uses a second esbuild bundle that includes `meas-digest-wiring`.
  - `stream-meas-digest.mjs` is not changed: its bundle does not include the wiring.
- `08 Production/tests/Hcc.Reporting.Tests/MeasGuest/MeasGuestSqlTests.cs` (new): 9 SQL tests on a disposable `HccTest_*` database (`ReportDatabase`). They call `SelectAsync` with the same boundaries, the N5 denominator (booked Classic, slept in Deluxe), a defaulted month (From / To null), the M3 lines written as production writes them (a `correction` reversal with `reverses_charge_id` / `reversed_by_charge_id`), and L7 through a fake `ISettingsReader`.

## Commands run (through `tools/Invoke-Serialized.ps1`, label meas2-b)
- `03 Prototype`: `HCC_TEST_ONLY=stream-meas-rpt-guest node --max-old-space-size=8192 tests/run.mjs` → **83/83 passed**. This is the core tests plus this suite; the full `npm test` was not run.
- `03 Prototype`: `npx tsc --noEmit` → exit 0.
- `08 Production`: `dotnet test tests/Hcc.Reporting.Tests -maxcpucount:1 --filter "FullyQualifiedName~Hcc.Reporting.Tests.MeasGuest"` → **16/16 passed** (7 existing rule tests + 9 new SQL tests).

## Not run or not verified
- The full prototype `npm test`, the full `dotnet test Hcc.slnx`, and the golden re-run.
- The server digest (`MeasReportDigestSource`) was not run end to end with these reports. It reads `group == "red"`, which these reports now set.
- L7 depends on lane C adding `meas.target.upsellPerArrival` to `MeasurementCatalogue.Targets` and `MEAS_TARGETS`. Until then both twins use the Rs 50 default: the server's `TargetAsync` falls back on an unregistered key, and the prototype uses `??5000`.
