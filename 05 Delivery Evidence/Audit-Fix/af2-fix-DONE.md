# af2-fix — verifier should-fix defects (5 Nov 2026)

Branch `claude/af2-fix` (cut from `claude/af2-int` 834b4637). Not merged; `main` untouched.

## Finding 1 — CTL-01 bypass through move room / extend stay (fixed)

**Server**
- `08 Production/src/Hcc.Application/Stays/StayChangeCommands.cs`: new optional dependencies `IStayRateStore` + `ISettingsReader` (DI already registers both: LF-money module and configuration module). For a move or an extension whose new rate actually differs from the current rate, after the existing PERM-APR-001 rupee-loss check (unchanged) the command now:
  - reads the caller's PERM-RATE-001 authority, the threshold `rate.agreedReductionBps` and the stay's agreed rate (`StayRateRules.AgreedBase` over `gst.stay_rate_change` history, the same helper ChangeStayRate uses);
  - refuses anyone but the Owner when the rate is more than the threshold below the agreed rate (`StayRateRules.NeedsOwner`), FORBIDDEN with the ChangeStayRate wording (`StayRateRules.OwnerDecidesText`, now shared by both commands). There is no approval path for a move or an extension, so the refusal sends the user to the Owner (or to ChangeStayRate, which raises a request);
  - writes a `gst.stay_rate_change` row (scope `today-next`, effective = business date, no re-posts because posted nights stay untouched, reason `Room move · …` / `Stay extended · …`, tariff, agreed rate, agreed rate after it, threshold, over_threshold). `agreed_after` = the new rate only when the Owner set it; a Manager's or Reception's row keeps the old agreed rate, so `AgreedAsync` can no longer fall back to a Manager-lowered current rate;
  - over the threshold (Owner only): audit `RATE_REDUCTION_OVER_THRESHOLD` (same text shape as ChangeStayRate) and the row's `over_threshold = 1` feeds the owner digest (`SqlRateReductionDigest`) unchanged.
- `Hcc.Domain/Stays/StayRateRules.cs`: `NeedsOwner`, `OwnerDecidesText`.
- Tariff limit (DEC-063 / PERM-RATE-001) and the PERM-APR-001 loss gate on move/extend are unchanged; the agreed-rate check is additional (stricter wins).

**Prototype**
- `src/domain/streams/stay-rate.ts`: `strNeedsOwner`, `strOwnerDecidesText`, `strUnpostedNights`, `strStayChangeRateRefusal` (Owner always; a lower rate that gives revenue up: Manager only, within the PERM-APR-001 limit; then the agreed-rate threshold).
- `src/services/mock/handlers/stay-rate.ts`: `strRecordStayChange` (records the change in `s.stayRateX` with the CTL-01 fields, audit line, digest feed via `dgsRateCuts`).
- `src/services/mock/handlers/guests.ts` `MOVE`: optional `rate` (in-house only, reason required) judged by the rule above before any mutation, recorded after the move. Before this the prototype MOVE had no rate at all (always "agreed tariff preserved"); the prototype client (`clients/sync/frontdesk.ts`) still sends `rateDecision:'keep'` and the UI offers no rate on the move dialog — unchanged.

## Finding 2 — a change that does not move the rate (fixed, server and prototype agree)
- Server 9-argument `CanApplyDirectly` and 7-argument `CanApprove` take an optional `currentRate`; the agreed-rate threshold applies only when the rate moves (`NeedsOwner`). `StayRateCommands.ChangeAsync` / `DecideAsync` pass the current rate. Prototype `strCanDirect` / `strCanApprove` now use the same `strNeedsOwner`.
- Behaviour: a guest-count or scope-only change on a stay already below the agreed floor is applied by a Manager. The tariff limit is NOT relaxed (unchanged DEC-063 behaviour): a guest-only change on a stay below the tariff floor still needs an approval, on both sides.

## Finding 3 — export log filtered after the row limit (fixed)
- `IReportExportLogReader.ExportLogAsync(propertyId, take, principalId, ct)`; `SqlReportStores` filters `(@u IS NULL OR principal_id = @u)` before `TOP (@n)`; `ReportService.ExportLogAsync` passes null for Owner / Manager / Accountant, else the caller's id. The old 3-argument store method stays (all rows).

## SafeName (fixed)
- `CustomerCommands.SafeName`: when the name holds 8 or more digits in total, every digit run (digits joined by spaces, hyphens, plus signs, parentheses) is replaced by `***`; names with fewer digits, Devanagari and ordinary names are unchanged.

## Tests added
- `Hcc.Stays.Tests/StayRateTests.cs` `Ctl01_a_change_that_does_not_move_the_rate_is_not_blocked_by_the_agreed_threshold`.
- `Hcc.Stays.Tests/Sql/StayChangeSqlTests.cs` (move with a lower rate): stay_rate_change rows for a Manager and an Owner move, the over-threshold audit.
- `Hcc.Counter.Tests/Sql/RateThresholdTests.cs`: Manager extend / move beyond the threshold inside the rupee limit refused (exact wording), within it recorded with the agreed rate kept, baseline not lowered (move and ChangeStayRate after the extension); Owner move flagged, audited, listed in the digest, becomes the agreed rate; guest-count-only change on a below-floor stay applied by a Manager.
- `Hcc.Reporting.Tests/Sql/ReportSqlTests.cs` (store filter before TOP), `Hcc.Reporting.Tests/ScheduleTests.cs` memory store updated.
- `Hcc.Integration.Tests/ReportHubHttpTests.cs` `Export_log_of_a_cashier_older_than_the_newest_200_property_rows_is_still_shown` (250 newer Owner rows).
- `Hcc.Customers.Tests/Kernel/CustomerSafeNameTests.cs`.
- Prototype `tests/stream-af2-ctl01.mjs`: three tests (move / extend refusal + recording + baseline, Owner move + audit + digest, guest-only change).

## Results
- `npx tsc --noEmit`: clean.
- Server (serial, `.\SQLEXPRESS`): Hcc.Stays.Tests 110/110, Hcc.Counter.Tests 89/89, Hcc.Reporting.Tests 83/83, Hcc.Integration.Tests `ReportHub` 2/2, Hcc.Customers.Tests 116/116, Hcc.Masters.Tests 36/36. The full server suite was not run (owner's gate).
- Prototype `npm test`: 2883/2884. The one failure is NOT from this lane: `[FIT] no phone-layout file changed since the wave base` flags `03 Prototype/src/mobile/money/logic.ts`, last changed by b59c34c3 "Folio payment methods from the payment-methods master (verifier note O8)", already on the af2-int base. Untouched here; the merger should refresh the FIT baseline or confirm the O8 change.

## Assumptions the owner must confirm
1. Move / extend have no approval path: a Manager's rate beyond the agreed-rate threshold is refused (FORBIDDEN) and must be set by the Owner (or requested through Rate / guest change). Alternative: raise a 'rate-change' approval from the move dialog.
2. Every move / extension that changes the rate now writes a `gst.stay_rate_change` row (scope `today-next`, no re-posts) so it shows in the Rate change report (A36) and the stay's rate history. Raises are recorded too.
3. A Reception raise on a move or extension is recorded but does not move the agreed rate (only an Owner-set rate becomes the agreed rate) — same as ChangeStayRate.
4. A rate that moves but is still below the agreed floor (e.g. a raise from ₹2,000 to ₹2,050 on a ₹2,500 agreed rate) is still treated as over the threshold (the Owner decides) — same as before in both server and prototype.
5. Prototype MOVE uses FORBIDDEN where the server answers APPROVAL_REQUIRED for a Manager loss above the PERM-APR-001 limit (the prototype has no APPROVAL_REQUIRED code).

## Paths that change a stay's nightly rate
| Path | Covered |
|---|---|
| Server `StayRateCommands.ChangeAsync` / `DecideAsync` (`SqlStayRateStore.SetReservationAsync`) | yes (CTL-01 already; finding 2 fixed) |
| Server `StayChangeCommands.MoveRoomAsync` (`SqlStayChangeStore.ApplyMoveAsync`, `WriteNightPricesAsync`) | yes (this fix) |
| Server `StayChangeCommands.ExtendStayAsync` (`ApplyDeparturesAsync`, `WriteNightPricesAsync`) | yes (this fix) |
| Server `StayChangeCommands` shorten / early departure | no rate change (rate null) |
| Server `ReservationService.AmendAsync` (`SqlReservationStore` ~104) | not an in-house path (`EnsureAmendable` refuses InHouse) — not covered, pre-arrival pricing |
| Server group pickup / group block rates (`SqlGroupStore`), OTA import (`SqlOtaStore`), legacy import (`SqlImportStore`), yield publication | set the rate at booking / import time, not during a stay — not covered |
| Server folio discounts / adjustments (PERM-FOL-003 / PERM-APR-001) | change charges, not the nightly rate — separate control, not covered |
| Prototype `STAY_RATE_CHANGE` + `APPROVAL` rate-change branch (`handlers/stay-rate.ts`, `handlers/folio.ts`) | yes |
| Prototype `MOVE` (`handlers/guests.ts`; the built-in `commands.ts` MOVE is replaced by it) | yes (this fix) |
| Prototype `CHECKIN rate:'agreed'` / `BOOK rate:'agreed'` (`guests.ts` ~112, `reservations.ts` ~100) | Owner-set guest agreed rate at check-in / booking — not a reduction path, not covered |
| Prototype group pickup (`groups.ts` ~174) | booking creation from a block — not covered |
