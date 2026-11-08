# F8-QW-KDS DONE

Lane qw-kds / Sonnet / branch claude/f8-qw-kds / worktree E:\Projects\HCC\worktrees\f8-qw-kds / base f8-setup 037bd7bc.
STOPPED EARLY by brief rule 6: the heavy-job lock answered exit 75 five times in a row while other lanes ran full suites. Full `npm test`, `npm run build` and the fit check were NOT run.

## Commits
See `git log --oneline f8-setup..HEAD` (feature commit 1bbb43aa "F8 QW-KDS: kitchen display page, kdsCards domain, tests", plus this DONE file).

## Files (prototype only; no server, no migrations)
- `03 Prototype/src/domain/streams/kds.ts` (new, pure): `kdsCards(s, outlet, station)`, `kdsLevel`, `KDS_AMBER_MINUTES=10`, `KDS_RED_MINUTES=20`, `KDS_LEVEL_TEXT` (On time / Slow / Late). Wraps `posKitchenAt`; open KOTs (Preparing, Ready) oldest first; card = label, spot, outlet, station, status, sentAt, minutes, level, next status, items (name + qty only).
- `03 Prototype/src/features/pos/KdsPage.tsx` (new page `kds`, FitPage `fill`, BoardColumns Preparing / Ready to serve, outlet and station pickers from `posOutletList` / `posStations`, 48 px bump button "Ready" / "Served" calling `POS_KOT_STATUS`, level shown as colour plus text, no money).
- `03 Prototype/src/features/pos/KdsRegister.tsx` (new): page + nav item (group CASH & BILLING, after `pos`, permission `billing`).
- `03 Prototype/src/styles/kds.css` (new, tokens only).
- Appends inside `F8-QW-KDS` blocks: `src/app/navmap.ts` (PAGE_CATEGORY `kds:'cash'`, PAGE_BLURB, PAGE_KEYWORDS), `src/app/registry.ts` (import + registration), `src/services/mock/index.ts` (export kds).
- `src/features/pos/PosPage.tsx`: one F8-QW-KDS marked button "Open kitchen display" on the Kitchen tab (adds `go` to the destructure on the PosPage line).
- `03 Prototype/tests/stream-kds.mjs` (new): 7 tests.

## Tests actually run
- `npx tsc --noEmit` via the lock: exit 0.
- `HCC_TEST_ONLY=stream-kds npm test` via the lock: 63/63 passed (56 core m1 tests that always run + 7 `[F8-QW-KDS]`): thresholds, card order, elapsed/levels at 9/10/19/20 min, bump Preparing > Ready > Served (skip refused INVALID_TRANSITION), outlet/station filters, no money key, role gate (nav `p` = billing, PAGE_CATEGORY, Housekeeper FORBIDDEN, Restaurant role can bump).
- Logs: `.scratch/t1.log`.

## Defaults relied on
- QWQ-3: amber 10 min, red 20 min, all outlets. Kept as constants (no settings, so NO migrations 4478-4480 used).

## Not done / unverified
- Full `npm test`, `npm run build`, fit check of `kds` at five viewports (PORT=4280): not run (lock busy 5x). Script ready at `.scratch/full.ps1` (npm test, build, serve on 4280, fit check).
- PAGE_CATEGORY full-nav test not run in the full suite (kds is listed; the filtered test asserts it).
- Phone: not offered (desktop FitPage only; no mobile registration).
- Visual check not done.
- Ready cards age from `readyAt` (time waiting for service), Preparing from sent time: my reading of "elapsed time".

## Integration needed
- `src/domain/streams/rbac.ts` line 58 `RBAC_RESTAURANT_PAGES=['pos','my-hr']`: add `'kds'`, otherwise the Restaurant role cannot open the page (navmap.ts:274 filters by that list). Outside my ownership row.
- Optional: handlers/index.ts, subscreens.ts, clients/sync/registry.ts, HccComposition.cs, Hcc.slnx blocks left empty (not needed; the page uses existing POS sync AdvanceKot).

## HANDOFF bullet
QW-KDS: new page `kds` (Kitchen display) with `kdsCards` in domain/streams/kds.ts; thresholds 10/20 min constants; bump uses POS_KOT_STATUS; 7 tests green; full suite, build and fit check still owed; rbac.ts needs `'kds'` in RBAC_RESTAURANT_PAGES.
