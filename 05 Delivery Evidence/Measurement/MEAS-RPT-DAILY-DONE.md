# MEAS-RPT-DAILY DONE

Lane meas-rpt-daily / Sonnet 5.5 / branch claude/meas-rpt-daily / worktree E:\Projects\HCC\worktrees\meas-rpt-daily / base b017cf13.

## Commits
`git log --oneline b017cf13..HEAD` (reports + server, catalogue/goldens/tests, rules tests, fixes).

## Files
Prototype: `03 Prototype/src/domain/reports/meas-daily/*` (shared, nine reports, night-audit.ts, index.ts), register.ts F12-RPT-DAILY imports, `tests/stream-meas-rpt-daily.mjs`, nine `tests/rpt-golden/goldens/*.json`.
Server: `08 Production/src/Hcc.Reporting/Reports/MeasDaily/{MeasDailySupport,MeasDailyReportsA,MeasDailyReportsB}.cs` (nine classes, IReportSelector + IMeasuredReport), HccComposition.cs F12-RPT-DAILY (AddReport x9), `tests/Hcc.Meas.Tests/MeasDailyRulesTests.cs`.
Outside the row (needed so the cross-check test passes): regenerated `catalogue.json` (both copies, by tools/rpt-catalogue.mjs) and an F12-RPT-DAILY block in `tests/Hcc.Reporting.Tests/EngineTests.cs` ServerCatalogue().

## What was built
Nine reports, ids/columns/flags identical both sides; each has a trailing text column `exception` (empty = not red, else the rule fired); rules are pure functions (`*Rows`, `flashRed`, `rateBelowFloor`, ...; server `MeasDailyRules`). MEAS_REPORT_KEY mappings confirmed correct. Hidden / MEAS_OFF through measOn (core: flash, inventory, high-balance with credit on). `measNightAuditLines(s)` and `measBlockClose(s)` (meas.daily.blockClose, default off) in night-audit.ts. No SQL migrations (none needed). Reports with money columns carry moneyFlag + PERM-RPT-002 gate (regrets too: its Quoted rate column is money although the catalogue says flag n).

## Tests
Prototype `npm test`: 2998/2998 (before ~2976 + 22 new, incl. 9 goldens); tsc clean. Server: Hcc.Meas.Tests 61 passed, Hcc.Reporting.Tests 83 passed. All through Invoke-Serialized.ps1 (label meas-rpt-daily).

## Open items
- Data gaps, prototype: no booking date (lead days blank in no-show), no early-departure record, no user on discount lines unless an approval exists, retention/waiver detected by line label; complimentary needs a `complimentary` marker (DEC-266 pending); sleep-out mismatch has no source; no-show target fixed 5 % (no meas.target key).
- Server SQL was written against migration columns but not run against SQL Server (no SQL test in this lane); no per-user daily cap and cash paid-outs = 0 on the server; room-discrepancy check-in time fixed 12:00; complimentary/waiver/retention rows empty until those flows post.

## Integration needed
- `03 Prototype/src/services/mock/index.ts`: no F12-RPT-DAILY block exists; to expose the pure functions to the digest add `export * from '../../domain/reports/meas-daily';` (digest wiring: flashRows, rateVarianceRows, discrepancyRows, highBalanceRows, allowanceRows, noShowRows, inventoryRows, cashierRows, regretsRows).
- Night-audit: no seam used; call `measNightAuditLines(s)` / `measBlockClose(s)` from closing preflight.
- Merge: catalogue.json and EngineTests block will conflict with other report lanes; regenerate with `node tools/rpt-catalogue.mjs`.

## HANDOFF bullet
F12 meas-rpt-daily: nine daily-control reports (flash, regrets, rate variance, room discrepancy, high balance, allowances/voids, no-show, inventory exceptions, cashier variance) both sides with red-row `exception` column, night-audit lines + blockClose pure functions; prototype 2998, Meas.Tests 61, Reporting.Tests 83; server SQL unrun against SQL Server.
