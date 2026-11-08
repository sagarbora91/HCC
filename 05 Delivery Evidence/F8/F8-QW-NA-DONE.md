# F8-QW-NA DONE

Lane qw-nightaudit / Sonnet 5.5 / branch claude/f8-qw-na / worktree E:\Projects\HCC\worktrees\f8-qw-na / base f8-setup 037bd7bc.

## Commits
See `git log --oneline f8-setup..HEAD` (build commit f71963e0 plus this DONE commit).

## Files
Prototype: `src/domain/streams/night-audit.ts` (new), `src/features/closing/NightAuditPanel.tsx` (new), `src/features/closing/DayClosing.tsx` (F8-QW-NA import + panel first in Preflight), `src/mobile/admin/Closing.tsx` (F8-QW-NA card list in the preflight step), `src/services/mock/index.ts` (F8-QW-NA export block), `tests/stream-night-audit.mjs` (new).
Server: `Hcc.Application/Closing/NightAuditLines.cs` (rules + NightAuditService), `Hcc.Api/Endpoints/NightAuditEndpoints.cs` (GET /api/v1/closing/night-audit?date=), `HccComposition.cs` (F8-QW-NA blocks: AddNightAuditModule, MapNightAuditEndpoints), `tests/Hcc.Closing.Tests/NightAuditTests.cs`.

## What was built
- `naChecklist(s)` returns {key,label,ok,detail,blocking,link,group}: arrivals, departures, room nights, approvals + expenses, POS checks, cash count submitted, key cards on departed rooms (keys slice), backup OK today (sysHealthX), then the MEAS lines as group "measurement" (measNightAuditLines; switched-off items skipped). `blocking` is true only for lines whose records closingPreflight already blocks (arrivals, departures, nights, approvals/expenses, pos, cash). Keys, backup and measurement never block. closingPreflight / closing.ts were not changed (no edit needed; the brief allowed an export only).
- `naAutoTicks` / `naTicksToSend`: HOVT-NA-PRE na1 arrivals, na2 nights, na3 approvals, na4 cash, na5 backup; na6 stays manual. The panel button sends the existing HOV_ITEM {done:true, note:'auto'}; no fixture change.
- Panel: first block of Day closing > Preflight; phone: card list above the blockers.
- Server twin: same lines from the existing preflight checks; keys and backup come from `NightAuditSignals` (see open items).

## Tests (actually run)
- `HCC_TEST_ONLY=night-audit npm test`: 67/67 (11 new [F8-QW-NA] tests plus the two base tests).
- `npm test` (full): 3177/3177.
- `npx tsc --noEmit`: clean (exit 0).
- `dotnet test tests/Hcc.Closing.Tests -maxcpucount:1 --logger trx`: 88/88 (5 new NightAuditTests).
- `node tools/fit-screen-check.mjs --pages closing` on PORT 4278 after `npm run build`: PASS, 0 failing of 871 (report in .scratch\fit, not committed).
All through Invoke-Serialized.ps1 (label f8-qw-na); two exit-75 retries.

## Defaults used
QWQ-1: show only, nothing new blocks; measurement lines never block (meas.daily.blockClose is not read by DAY_CLOSE today and was left alone).

## Open items / not done
- Server: no signals source is registered, so `na.keys` reports "Not checked on this server" (ok) and `na.backup` reports "No backup recorded" (not ok). The Keys and system-health reads must be wired by an integrator (a `Func<propertyId,date,ct,NightAuditSignals>` passed to NightAuditService). The MEAS lines are not on the server twin (no server measNightAuditLines).
- No SQL/HTTP test of the endpoint; the server test covers the pure rules only.
- na4 ticks when the drawer is counted (Submitted or Closed), not only when reconciled.
- No migration used (templates are not rows needing a seed); 4470-4473 untouched.
- Panel not exercised in a browser with screenshots beyond the fit check.

## Integration needed
Wire NightAuditSignals (keys active cards for departed stays, SystemHealth last good backup) in HccComposition; page id for measurement exceptions link (none used).

## HANDOFF bullet
F8 QW-NA: Day closing > Preflight now opens with a Night audit panel (arrivals, departures, nights, approvals, POS, cash, key cards, backup, MEAS exceptions); show only, nothing new blocks; HOVT-NA-PRE na1-na5 can be auto-ticked. Server twin GET /closing/night-audit (keys/backup signals still to wire). Tests: prototype 3177, Closing 88.
