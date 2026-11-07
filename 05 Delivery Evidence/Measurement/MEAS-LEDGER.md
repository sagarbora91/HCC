# MEAS wave ledger (F12)

Setup commit: **3040fa7f** on `claude/meas` (tag `meas-setup`), base main 403d3172. Phase A lanes launched and merged 5 Nov 2026; integration commit d43bc4bc, prototype 2976/2976. Phase B awaits the owner's go.

| Lane | Model | Branch | Worktree | PORT | Last commit | State |
|---|---|---|---|---|---|---|
| meas-settings | Opus | claude/meas-settings | worktrees\meas-settings | 4191 | 3040fa7f | DONE, merged into claude/meas |
| meas-cap-desk | Sonnet | claude/meas-cap-desk | worktrees\meas-cap-desk | 4192 | 3040fa7f | DONE, merged into claude/meas |
| meas-cap-hk | Sonnet | claude/meas-cap-hk | worktrees\meas-cap-hk | 4193 | 3040fa7f | DONE, merged into claude/meas |
| meas-cap-guest | Sonnet | claude/meas-cap-guest | worktrees\meas-cap-guest | 4194 | 3040fa7f | DONE, merged into claude/meas |
| meas-cap-people | Sonnet | claude/meas-cap-people | worktrees\meas-cap-people | 4195 | 3040fa7f | DONE, merged into claude/meas |
| meas-forecast | Opus | claude/meas-forecast | worktrees\meas-forecast | 4196 | 3040fa7f | DONE, merged into claude/meas |

Phase B / B2: all nine lanes merged into claude/meas; integration fixes up to 6dbd8e77 (`MEAS-PHASE-B-INTEGRATION.md`).

| Step | Who | Commit | State |
|---|---|---|---|
| Phase C review (read only) | meas-review | (file `MEAS-REVIEW.md`, committed in c6e7ca4d) | 4 HIGH fixed, M2 / M4 / M7 / M8 fixed, M1 partial, rest open |
| f12-int step 1 capture server | f12-int + helper f12-int-cap | c6e7ca4d | done |
| f12-int step 2 report data (server) | f12-int + helper f12-int-an | 166419bc | done |
| f12-int steps 2-3 prototype wiring, golden extract mode | f12-int | final commit of 7 Nov (see `MEAS-DONE.md`) | done |
| Legacy golden (extract mode) | f12-int | `.scratch\golden\lgc-1` | **FAIL**: 4 metrics match, billed / bad debt / zero-rate / hall events written down (`MEAS-DONE.md` §5) |
| Gate `-Wave meas`, local main fast-forward | f12-int | — | **not run**: stopped on the golden mismatch per brief; waits for owner decisions |
