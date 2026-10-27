# ACC1-TOOLS DONE

Lane acc1-tools (Sonnet), branch `claude/acc1-tools`, worktree `E:\Projects\HCC\worktrees\acc1-tools`, base `39930edc` (re-based onto `claude/acc1` 83be423d by a merge, so the prototype suite is green). PowerShell scripts and documentation only; no product code.

## Commits (on top of 39930edc)
- 746c43b8 ACC-1 tools: Run-Gate / Start-Gate / Rerun-Gate, GateCommon, secret-scan allowlist, README-agents
- 489d3a5d Run-Gate: accept a comma list for -Only
- d8c0b90c README-agents: stale pause file after a crash
- 93389988 Merge claude/acc1 (83be423d, re-pinned SCHEMA / fixture-hash tests)
- the commit that adds this DONE file and the dry-run gate report (see the last commit hash in the lane's final message)

## Files
- `tools\Run-Gate.ps1`: `-Repo <worktree> -Wave <name>` (+ `-ExpectedBranch` default `claude/<wave>`, `-BaseRef main`, `-LogRoot`, `-WaitMinutes 90`, `-Only`, `-CarryFrom`).
- `tools\Start-Gate.ps1`: writes `_tools\PAUSE-LANE-BUILDS` (line `gate-<wave>`), runs Run-Gate, removes the file in `finally` (only if it still holds its own label; refuses to start if another label holds it).
- `tools\Rerun-Gate.ps1`: reads the latest `<wave>-gate-*.md`, reruns only its FAIL / SKIPPED / NOT-RUN / STALE steps (through Start-Gate; `-NoPause` for Run-Gate), carries earlier passes only when HEAD is unchanged (else PARTIAL, exit 2).
- `tools\GateCommon.ps1`: shared helpers (step runner with timeout + exit-75 retry, trx / npm / tsc / build parsers, secret scan, report-state reader).
- `tools\secret-scan-allow.txt`: allowlist (`rule | path-regex | optional line-regex`); seeded with the fictional test-fixture passwords under `08 Production/tests/` and `03 Prototype/tests/` (credential-literal only).
- `tools\README-agents.md`: serializer command lines, log + pause file, ownership rule, DONE template, how the coordinator runs the gate.
- `05 Delivery Evidence\Gates\acc1-dryrun-gate-20261027-1943.md`: the dry-run report.

## What the gate does
Checks clean worktree (ignoring `.scratch\` and `05 Delivery Evidence\Gates\`) and branch; records the commit; then, each through `Invoke-Serialized.ps1` with label `gate-<wave>`: `npx tsc --noEmit`, `npm test`, `npm run build`, `dotnet build Hcc.slnx`, `dotnet test Hcc.slnx -maxcpucount:1 --logger trx` (fails if fewer trx files than the 35 test projects in `Hcc.slnx`), `node tools/migrations-lock.mjs` (check mode is the default; `--add` is never run), and an in-process secret scan of the added lines since `merge-base(main, HEAD)` (private keys, connection strings with password, hard-coded credential literals, GitHub / AWS / Slack / Anthropic / sk- / Google / JWT / Bearer tokens; values masked to 3 characters). All steps run even after a failure (so one report lists everything) except dotnet test after a failed dotnet build. Every command writes to a file, never through a pipe; step timeouts kill the process tree; exit 75 is retried. The report has the PASS / FAIL, the step table with durations, every failing test (npm: `evidence/domain-tests.json`; server: all trx files with project and message), tsc / build errors, scan findings and a machine-readable `json gate-state` block. Exit 0 PASS, 1 FAIL, 2 partial. Logs under `<repo>\.scratch\gate\<wave>-<stamp>\` (uncommitted). Nothing is deleted anywhere; only the pause file is removed, by Start-Gate.

## Dry run
`pwsh -NoProfile -File tools\Run-Gate.ps1 -Repo E:\Projects\HCC\worktrees\acc1-tools -Wave acc1-dryrun -ExpectedBranch claude/acc1-tools` at commit 93389988: **PASS**, 50m 42s (queued behind the other lanes). tsc ok; prototype 1867 / 1867; npm build ok; dotnet build ok; server 3324 / 3324 in 35 trx files (27m 21s); migrations lock in step; secret scan 1208 added lines in 27 files, 0 findings. Report: `05 Delivery Evidence\Gates\acc1-dryrun-gate-20261027-1943.md`; logs `E:\Projects\HCC\worktrees\acc1-tools\.scratch\gate\acc1-dryrun-20261027-1943\`.
History: a first dry run at 489d3a5d (before the merge) reached `npm test` and correctly reported FAIL with the 13 SCHEMA 11 / fixture-hash tests, which is the parser working on real failures; it was stopped, and the earlier run was cut by the PC restart. Also verified: Rerun-Gate on a doctored report (rerun only `tsc`, carried 6 passes, PASS, pause file written and removed), the secret scan on a synthetic bad diff (private key, connection string, token, credential literal found; placeholder, `Integrated Security` and the allow marker skipped), the trx and npm failure parsers on synthetic input, and 96,509 added lines of real history (4 hits, all test passwords, now allowlisted).

## Open items
- A kill / PC restart cannot run the `finally`; README documents checking for a stale `PAUSE-LANE-BUILDS` (none was left after the 19:05 restart).
- Steps queue behind other lanes each time (the lock is not fair and each step takes it separately); a full gate under load took 50 min. Run it with `Start-Gate.ps1` so lanes pause.
- The gate does not run the end-to-end script, emulator smoke or installer (wave W9), nor SQL leftovers cleanup (`HccTest_*` databases from crashed runs exist on `.\SQLEXPRESS`).
- The secret scan is pattern based (no entropy rule); tune `secret-scan-allow.txt` narrowly.
- `dotnet test` runs a second build (the brief's exact command, no `--no-build`).

## Integration needed
None in product code. Merge order: `claude/acc1-tools` last (adds only `tools\` files, this DONE file and the dry-run report). The integrator may delete `05 Delivery Evidence\Gates\acc1-dryrun-gate-*.md` if it should not live on `claude/acc1`.

## HANDOFF bullet
ACC-1 tools lane: `tools\Start-Gate.ps1 / Run-Gate.ps1 / Rerun-Gate.ps1` (+ `GateCommon.ps1`, `secret-scan-allow.txt`, `README-agents.md`) port ETP's gate chain to HCC: clean-worktree and branch check, tsc, npm test, npm run build, dotnet build, dotnet test Hcc.slnx -maxcpucount:1 (trx), migrations-lock check, secret scan of the diff since main, all through the heavy-job lock with label `gate-<wave>`; report `05 Delivery Evidence/Gates/<wave>-gate-<stamp>.md` with every failing test name; Start-Gate pauses lane builds via `_tools\PAUSE-LANE-BUILDS`. Dry run on claude/acc1-tools: PASS (1867 + 3324, 50 min). Run: `pwsh -NoProfile -File tools\Start-Gate.ps1 -Repo <wave worktree> -Wave <wave>`.
