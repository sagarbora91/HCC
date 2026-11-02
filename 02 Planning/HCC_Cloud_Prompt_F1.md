# HCC — cloud prompt for wave F1 (paste the block below into a new cloud Claude Code session)

Prepared 2 Nov 2026. Repository: `https://github.com/sagarbora91/HCC` (public). Everything the cloud session needs is in the repository; the owner's PC-only material (legacy Access files, the Be-Tech lock software, ID images, the Key Agent prototype) is NOT in the repository and is not needed for F1.

**What the cloud can and cannot do (read before launching).**
- Works well in the cloud: the React/TypeScript prototype (`03 Prototype`: `npm ci`, `npx tsc --noEmit`, `npm test`, `npm run build`), documentation, planning, code review, merges, and the **.NET build** (`dotnet build`).
- Does NOT work in the cloud as-is: the **server test suite** (it creates `HccTest_*` databases on SQL Server Express with Windows login, `.\SQLEXPRESS`), Android builds / emulators, the Windows-only gate scripts (`tools\Start-Gate.ps1`), the BIS lock software. The final gate for every wave therefore still runs on the owner's PC; the cloud does the lane work and the prototype-side checks.

---

## PROMPT (copy from here)

You are the coordinator for HCC (Hotel Control Centre), an internal hotel-management system (React 19 + TypeScript prototype in `03 Prototype`, .NET 10 server in `08 Production`, Android wrapper in `04 Android`). Repository: https://github.com/sagarbora91/HCC. Work in a normal clone; do not use Windows paths from the documents literally (`E:\Projects\HCC\...` = the repository root; `Hotel Control Centre\` = the repository root; worktrees become plain branches).

**Task: execute wave F1 of the wave plan.**

1. Read, in this order: `CLAUDE.md`; the top status box of `HANDOFF.md`, then Sessions 13p and 13q; `02 Planning/HCC_Wave_Plan_F1-Fn_2026-11-02.md` sections 0 and 1 and the whole of **F1**; `02 Planning/HCC_Pending_Inventory_and_Roadmap_2026-11-02.md` section 1 only; `02 Planning/HCC_Decision_Register.md` (do not re-ask decided rulings). Do not read other large files unless a step needs them.
2. Branches. `main` = the owner-approved line. The FIT wave is on branch `claude/fit` (about 120 commits ahead of the previous `main`; it has been pushed with this prompt). Create a working branch `claude/f1-gate` from `claude/fit`. Never push to `main`, never force-push, never rewrite history, never delete branches. Open a **pull request into `main`** at the end and stop; the owner merges.
3. F1 lane work (use subagents with `model: sonnet` for mechanical lanes, one `model: opus` integrator for the merge; at most 3 agents alive; compact briefs; each lane commits after each coherent step and writes its DONE note under `05 Delivery Evidence/Fit/`):
   - **f1-int (Opus):** the FIT leftovers listed in F1 (contract note, `HotelSettings.tsx` change, `FIT2-DONE.md`, check reports) were already committed on `claude/fit` at fb194a2c (2 Nov 14:20), so just read `05 Delivery Evidence/Fit/FIT-DONE.md` and `FIT2-DONE.md` and confirm nothing is half-done; `git merge main` into the branch (the room-keys module came in on `main`; conflicts are expected only in the shared append-only files `subscreens.ts`, `navmap.ts`, `registry.ts`, `handlers/index.ts`, `Hcc.slnx` — resolve by keeping both sides); then in `03 Prototype`: `npm ci`, `npx tsc --noEmit`, `npm test`, `npm run build`, and the layout checks named in F1 (`fit-screen-check`, print-layout, tablet checks) — include the new **room-keys** page in the fit check and, if it fails, convert it to the FIT page layout (layout only, no behaviour change). Also run `dotnet build 08 Production/Hcc.slnx` to prove the server still compiles.
   - **f1-docs (Sonnet), after f1-int is green:** HANDOFF `## Session 13r` block (what F1 did, test counts), `CLAUDE.md` status line, Decision Register row for the FIT-2 rule if F1 says so (next free DEC number after DEC-212), Build Plan v2 tick. Do NOT run graphify and do NOT touch memory files (those live on the owner's PC).
   - **f1-apkprep (Sonnet), after f1-int is green:** write `05 Delivery Evidence/Acceptance/APK_v19_Checklist.md` and bump the Android `versionCode` to 19 as F1 describes. No Android build.
4. Server tests: the SQL-backed server suite cannot run here. Say so plainly in the pull request: "server suite NOT run in cloud; run `tools/Start-Gate.ps1` on the hotel PC before merging". Do not claim it passed. If a SQL Server container happens to be available and the test harness connects with it (`HCC_TEST_SQL_SERVER`), you may try the **Hcc.Keys.Tests** project only and report the result honestly; otherwise skip.
5. Safety: never print or commit secrets; run a secret scan of your added lines before pushing (regexes for keys/tokens/passwords/private keys; the repository's allow-list is `tools/secret-scan-allow.txt`). No real guest data exists in the repository and none must be created; fictional data only. Money is integer paise.
6. Usage economy: Sonnet for everything except the integrator; no repeated re-reading of big files; stop and report if a step needs more than two retries; no new agent batches while one runs.
7. Finish by pushing `claude/f1-gate` and opening the pull request with: what changed, test counts (prototype before/after, `tsc`, build, fit check, `dotnet build`), what was NOT run (server suite, Android, graphify), conflicts and how they were resolved, and the owner steps left (run the local gate; merge; then say "go F2").

Report in under 250 words. Do not start F2.

## END OF PROMPT
