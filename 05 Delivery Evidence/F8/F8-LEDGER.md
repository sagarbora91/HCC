# F8 wave ledger (split folio + P2 reports + quick wins)

Setup commit: **see the `f8-setup` tag** on `claude/f8` (base local `main` 6699b4e4, MEAS F12 merged, tag `wave/meas`). Contract `02 Planning/agents/SF-CONTRACT.md`, briefs `02 Planning/agents/F8_Briefs.md`, owner questions `02 Planning/F8_Owner_Questions_2026-11-08.md` (27, all with defaults). SCHEMA 16 (bumped and re-pinned by the setup commit). **No lane launched: waiting for the owner's "go F8".**

One line per lane; update at each DONE (lane → last commit → state). After a restart: check `E:\Projects\HCC\worktrees\_tools\PAUSE-LANE-BUILDS` (delete a stale one), read this table, relaunch only the lanes without a DONE file, each from its own branch head with a two-line resume note.

| # | Lane | Model | Branch | Worktree | PORT | Migrations | Last commit | State |
|---|---|---|---|---|---|---|---|---|
| 1 | sf-domain | Opus | claude/f8-sf-domain | worktrees\f8-sf-domain | 4271 | 4350–4359 | f8-setup | not started |
| 2 | sf-server | Sonnet | claude/f8-sf-server | worktrees\f8-sf-server | 4272 | 4360–4364 | f8-setup | not started (waits for sf-domain STEP 1: hash here) |
| 3 | sf-ui | Sonnet | claude/f8-sf-ui | worktrees\f8-sf-ui | 4273 | — | f8-setup | not started (waits for sf-domain STEP 1) |
| 4 | sf-tests | Sonnet | claude/f8-sf-tests | worktrees\f8-sf-tests | 4274 | — | f8-setup | not started |
| 5 | rpt-p2-a | Sonnet | claude/f8-rpt-p2-a | worktrees\f8-rpt-p2-a | 4275 | 4450–4451 | f8-setup | not started |
| 6 | rpt-p2-b | Sonnet | claude/f8-rpt-p2-b | worktrees\f8-rpt-p2-b | 4276 | 4452–4453 | f8-setup | not started |
| 7 | rpt-p2-c | Sonnet | claude/f8-rpt-p2-c | worktrees\f8-rpt-p2-c | 4277 | 4454–4455 | f8-setup | not started |
| 8 | qw-nightaudit | Sonnet | claude/f8-qw-na | worktrees\f8-qw-na | 4278 | 4470–4473 | f8-setup | not started |
| 9 | qw-noshow | Sonnet | claude/f8-qw-ns | worktrees\f8-qw-ns | 4279 | 4474–4477 | f8-setup | not started |
| 10 | qw-kds | Sonnet | claude/f8-qw-kds | worktrees\f8-qw-kds | 4280 | 4478–4480 | f8-setup | not started |
| 11 | qw-advances | Sonnet | claude/f8-qw-adv | worktrees\f8-qw-adv | 4281 | — | f8-setup | not started |
| 12 | qw-pm | Sonnet | claude/f8-qw-pm | worktrees\f8-qw-pm | 4282 | 4481–4484 | f8-setup | not started |
| — | f8-review | Sonnet (read-only) | — | — | — | — | — | after the DONE files |
| — | f8-int | Opus | claude/f8 | worktrees\f8 | 4270 | 4365–4369, 4485–4489 | — | after review |
| — | gate `-Wave f8` | coordinator | claude/f8 | worktrees\f8 | — | — | — | after f8-int |
| — | f8-docs | Sonnet | HCC root | — | — | — | — | after the fast-forward |

**Migration reservations (checked at the setup commit):** `node "08 Production/tools/migrations-lock.mjs"` → in step, 233 migrations, highest 4920; no file and no lock entry in 4350–4369 or 4450–4489. Every F8 migration is below 4920 and needs `--add --accept-out-of-order "F8 <lane>: reserved range (wave plan 1.4)"`.

**Setup commit checks (run through `tools/Invoke-Serialized.ps1`, label f8-setup):** see the setup commit message for the exact results.
