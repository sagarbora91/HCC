# F1-int — integrator, wave F1 (branch `claude/f1-gate`, 2 Nov 2026, Linux cloud container)

## 1. FIT state at fb194a2
`git status` clean; nothing half-done. Open items named by FIT-DONE / FIT2-DONE (unchanged): Reservations ≈ 5.8 rows at tab-land; compact stat strips on Dashboard / Accounting cut the last card's edge at tab-land; Approvals list rows covered only by a seeded render test (no pending approval in the demo seed); duplicated desktop render copies to keep in step (review N6).

## 2. Merge origin/main (KEYS, 8a377f32) — commit 04dbc3b
| File | Resolution |
|---|---|
| `03 Prototype/src/app/navmap.ts` | both: `guests:'front'` (FIT) + `keys:'front'` (KEYS) |
| `03 Prototype/tests/stream-rbac-matrix.mjs` | whole-file conflict was CRLF (fit) vs LF (main); took main's file + FIT's `guests` page row |
| `08 Production/tests/Hcc.Accounting.Tests/Sql/TallyFileSqlTests.cs` | both sides fixed the same flaky count; took main's order-independent `>= 2` assertion |

## 3. Prototype (Node v22.23.4 here; CLAUDE.md wants 25)
- `npm ci` OK; `npx tsc --noEmit` clean; `npm run build` + `npm run build:production` OK, 0 warnings.
- Tests: fit head 2373 (FIT2-DONE); merged **2388 / 2388**.
- First merged run aborted: V8 heap out of memory (Node 22 default heap; passes with `NODE_OPTIONS=--max-old-space-size=8192`), plus 24 CT-A/B/C token tests failed only because the clone was shallow (commit 799e4cc missing; fixed by `git fetch --unshallow`). Two real failures, fixed: `[FIT] page keys renders a fit-page root` and `[FIT] no phone-layout file changed` (KEYS' new `m-keys.css` came from main; allowed by name in `tests/stream-fit.mjs`).

## 4. Room keys page
Converted to FitPage + FitTabs (`useFitTab('keys')`) + ScrollPanel, layout only; sub-screens `keys:log/locks/encoder` in `subscreens.ts` (locks / encoder gated by `keyCanConfigure`); `z-fit.css` releases the module's own max-height boxes in the fit page. Fit check `--pages keys` (owner, reception; tab-land, lap-1366; text 1): first 6 / 112 cramped (log panel 144 px), after moving the totals into the panel **PASS 0 / 112**.

## 5. Layout checks
- check-print-layout, check-tablet-back, check-overlay-compat, tablet-masters-check: all PASS (their rewritten evidence images not committed).
- **Full fit check not run**: in this container one page in full mode took >20 min (roles × 5 viewports × scale pass), and one run died with "browser has been closed"; the full 5,666-check run needs the work PC.

## 6. .NET
Not run: no SDK — `dot.net` / `builds.dotnet.microsoft.com` refused by the egress proxy (2 attempts). SQL server tests not run (as instructed).
