# F8-QW-NS DONE

Lane qw-noshow / Sonnet / branch claude/f8-qw-ns / worktree E:\Projects\HCC\worktrees\f8-qw-ns / base f8-setup 037bd7bc.

## What was built
Prototype (03 Prototype):
- `src/domain/streams/noshow.ts` (new, pure): `nsCutoff` (fd.noShowTime, default 23:00), `nsPastCutoff` (simulated clock), `nsDue(s)` (Confirmed, arrival = business date, empty before the cut-off, with `blocked` when an advance/deposit is held), `nsChargeText` ("Policy <name>: no-show charge ₹… — not posted"), `nsReason`.
- `src/features/reservations/NoShowList.tsx` (new): the list with Open and one-click "Mark no-show" = `CANCEL {noShow:true, reason}`; shown inside the Day closing Preflight step (`src/features/closing/DayClosing.tsx`, F8-QW-NS block after the body chain, plus one import).
- `src/services/mock/handlers/reservations.ts` CANCEL: F8-QW-NS block appends the shown policy charge to the reason of a no-show (nothing else changed; no line, receipt or ledger entry).
- `src/domain/streams/channels.ts` `chnPushPreview`: avail = sellable + `yldOverbookAllowed` (cap 0 = identical output).
- `src/services/mock/index.ts`: F8-QW-NS export of noshow.ts. `tests/stream-noshow.mjs`: 6 tests.
Server (08 Production):
- `Hcc.Domain/Ota/OtaRules.cs` `PushAvail(sellable, cap)`.
- `Hcc.Application/Ota/OtaQueries.cs`: optional `ISettingsReader` (default null), push preview adds the yld.overbook.<code> cap.
- `Hcc.Infrastructure.SqlServer/Ota/OtaChannelManagerProjector.cs`: optional `ISettingsReader` (4th ctor argument, default null); automatic availability push adds the cap (F8-QW-NS blocks).
- `Hcc.Application/Reservations/NoShowRules.cs`, `NoShowQueries.cs` (new): due list with policy name, charge, charge text; `Hcc.Api/Endpoints/NoShowEndpoints.cs` (new): `GET /reservations/no-show-due`; registered in the two F8-QW-NS blocks of HccComposition.cs. Marking uses the existing `MarkNoShow` command.
- `tests/Hcc.Ota.Tests/Domain/NoShowOverbookTests.cs`: pure tests.

## Defaults relied on
QWQ-2 (cap +0 for every type; honoured only at the channel push), QWQ-4 (keep BOOK refusal with override; yld.restrictionsEnforce untouched), QWQ-7 (no automatic marking; charge shown, not posted).

## Tests (actually run, through the serialized lock)
Prototype, through the lock (label f8-qw-ns, 03 Prototype):
- `npx tsc --noEmit` exit 0.
- `HCC_TEST_ONLY=stream-noshow npm test` (runs the base tests plus the new suite): 62/62 passed, of which 6 are the new [F8-QW-NS] tests. The FULL `npm test` was NOT run (only the filtered dev loop); `npm run build` not run.
Server: **NOT RUN.** `dotnet build src/Hcc.Api` and `dotnet test tests/Hcc.Ota.Tests -maxcpucount:1` were queued behind the heavy-job lock from the lane's ~09:56 mark; another lane's `npm test` (f8-qw-pm, started 09:58:45) held the lock for over 30 minutes, so I stopped waiting. The C# (OtaRules.PushAvail, OtaQueries, OtaChannelManagerProjector, NoShowRules/NoShowQueries/NoShowEndpoints, HccComposition blocks, NoShowOverbookTests.cs) has never been compiled. Script to run: `.scratch\dn.ps1` (build Hcc.Api, then test Hcc.Ota.Tests).

## Not done / unverified
- Fit check of the new list (PORT=4279) NOT run: the list is a section inside the Day closing Preflight step, not a page, and appears only after 23:00 on the simulated clock, which the seeded 08:00 state never reaches; `fit-screen-check` would not show it. Needs a manual look with the clock advanced.
- No migration (fd.noShowTime exists). Hcc.slnx unchanged (no new project).
- Server SQL for the no-show endpoint (policy matching by `Source` text equal to a policy source code) is covered only by pure tests; no SQL test of `GET /reservations/no-show-due` or of the projector cap with a seeded setting.

## Integration needed
1. `src/Hcc.Api/Endpoints/OtaEndpoints.cs` (not my file), the projector registration, add the reader so the AUTOMATIC availability push honours the cap (OtaQueries gets it by DI already):
   `new OtaChannelManagerProjector(outbox, connections, sp.GetRequiredService<TimeProvider>(), sp.GetService<Hcc.Application.Configuration.ISettingsReader>())`
   Until then the automatic push is unchanged (cap 0 behaviour).
2. DayClosing.tsx: the block sits after the step-body chain; qw-nightaudit's block (not present on this branch) must stay above it; a merge conflict there is union.
3. Prototype only lists no-shows after the cut-off on the desk through Day closing; a link from the Reservations page was not added (file outside my row).
