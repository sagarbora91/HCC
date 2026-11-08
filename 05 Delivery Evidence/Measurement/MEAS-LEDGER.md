# MEAS wave ledger (F12)

Setup commit: **3040fa7f** on `claude/meas` (tag `meas-setup`), base main 403d3172. Phase A lanes launched and merged 5 Nov 2026; integration commit d43bc4bc, prototype 2976/2976. Phase B built 6-7 Nov; see the final status below.

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
| meas2 lanes A-F (follow-up fixes, SQL smoke) | Opus (A, C, E), Sonnet (B, D, F) | claude/meas2-a … claude/meas2-f | worktrees\meas2-* | — | a43a9316 (merge of D; F 0c58e102) | DONE, merged into claude/meas (`MEAS2-*-DONE.md`) |
| Gate `-Wave meas` | this PC | 133bc20d | FAIL (stale role-read fixture only; fixed 1c31b661) |
| Gate `-Wave meas` | this PC | 79f7abe7 | PASS (prototype 3141, server full run 52 min) |
| Gate `-Wave meas` (FINAL) | this PC | aa8d0195 | **PASS**, 1 h 04 min: prototype 3159/3159, server 4571/4571, 43 projects (report `91fb2dad`) |
| Head re-check after merging origin/main 3a0c2d0e (APK v24, ADM-UX) | this PC | 6e35d580 | `tsc` + `npm test` 3166/3166; server not re-run (unchanged by the merge) |
| Local main fast-forward and tag | this PC | 6e35d580 | done 8 Nov 2026: `main` = `claude/meas` = tag `wave/meas`; **NOT pushed to GitHub** |

Final status (8 Nov 2026): wave MEAS and meas2 complete and gated; legacy golden partial (room-nights, occupancy, ADR, repeat % match on 10 FYs; billed, bad debt, zero-rate and hall events wait for owner decisions). Owner questions: `02 Planning/MEAS_Owner_Questions_2026-11-08.md`. DEC-268…DEC-276 in the register.
