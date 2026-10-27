# HCC agent tools: the heavy-job lock, the gate, the DONE file

For every lane agent and the coordinator. Ported from ETP's workflow (`E:\ETP\Code\SaagarCC-V6-ETP-Source-and-Report-Engine-605002f\knowledge\07-Operations\Agent Workflow - Lanes, Briefs and Gates.md`, DEC-154). Work PC rules: no new batch while another wave runs, one emulator at a time, up to 16 agents only while RAM / CPU are free.

## 1. Heavy jobs go through the lock

Every `dotnet build`, `dotnet test`, `npm test`, `npm run build`, `npx tsc --noEmit` and Gradle build runs through `tools\Invoke-Serialized.ps1` (named mutex `Global\HccHeavyJobLock`; one heavy job at a time across all worktrees; at most 8 CPUs, below-normal priority; Node 25, JDK 21 and `pwsh` 7 on PATH; a 20 s cool-down after each job). Writing code is parallel; building and testing is not.

Exact command lines (replace `<lane>`; the script lives in the wave worktree `acc1`, any copy works):

```
pwsh -NoProfile -File "E:\Projects\HCC\worktrees\acc1\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\<lane>\03 Prototype" -Label <lane> -Command "npx tsc --noEmit"
pwsh -NoProfile -File "E:\Projects\HCC\worktrees\acc1\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\<lane>\03 Prototype" -Label <lane> -Command "npm test"
pwsh -NoProfile -File "E:\Projects\HCC\worktrees\acc1\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\<lane>\03 Prototype" -Label <lane> -Command "npm run build"
pwsh -NoProfile -File "E:\Projects\HCC\worktrees\acc1\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\<lane>\08 Production" -Label <lane> -Command "dotnet build Hcc.slnx"
pwsh -NoProfile -File "E:\Projects\HCC\worktrees\acc1\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\<lane>\08 Production" -Label <lane> -Command "dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1 --logger trx"
```

- **Exit 75 = busy** (the lock stayed taken for `-WaitMinutes`, default 8, or lane builds are paused for a priority job): wait a little and run the **same call again**. Any other exit code is the command's own.
- **Never filter test output** (no `| Select-String`, no `| tail`): a filtered run once lost the name of the only failing test. Send the whole output to a file in your lane's scratch folder (`<worktree>\.scratch\`, not committed) and read it from there. Put the command in a `.ps1` there and pass `-Command "& '<worktree>\.scratch\run-tests.ps1'"`, which keeps quoting simple. `--logger trx` keeps the machine-readable result.
- Lanes run **only the test projects they touch** on the server. The full `Hcc.slnx` suite is the integrator's and the gate's.
- SQL tests need `.\SQLEXPRESS` (each class makes and drops its own `HccTest_*` database; env `HCC_TEST_SQL_SERVER` overrides). The Windows login needs `dbcreator`, which this PC has.

## 2. The log and the pause file

| File | Meaning |
|---|---|
| `E:\Projects\HCC\worktrees\_tools\heavy-jobs.log` | One `START` and one `END` line per heavy job: UTC time, `[label]`, working directory and command, exit code, seconds. Read it to see who holds the lock and how long the queue is. |
| `E:\Projects\HCC\worktrees\_tools\PAUSE-LANE-BUILDS` | Exists only while a priority job (the wave gate, an emulator run) is running. Each line is a label allowed through; every other label waits (then exits 75) so the priority job gets the lock next. `Start-Gate.ps1` writes `gate-<wave>` and removes the file in a `finally`. The `finally` runs on errors and Ctrl+C but not when the PC restarts or the process is killed: after such a crash check that the file is gone (`Test-Path E:\Projects\HCC\worktrees\_tools\PAUSE-LANE-BUILDS`), and if a stale one is left the coordinator deletes it by hand (a lane must not). A new `Start-Gate.ps1` for the same wave simply reuses a stale file of its own label; for another label it refuses. |

A lane build that gets exit 75 during a gate simply retries later; nobody is stopped.

## 3. File ownership

A lane edits **only the files in its ownership table** (wave brief `02 Planning\agents\<WAVE>_Briefs.md`). A change needed in someone else's file is written in the DONE file under **Integration needed** with the exact edit; the integrator makes it. The few append-only shared files (`commands.ts`, `services/mock/index.ts`, `HccComposition.cs` / endpoint registration) are edited only by appending one line inside the wave's marked block. Shared interfaces are committed first as a contract file; the consuming lane copies it verbatim.

Also in every lane: work only in your own worktree `E:\Projects\HCC\worktrees\<lane>` on branch `claude/<lane>`; commit early and often (messages end with the Co-Authored-By line of the brief); **never push**; never use bare `git stash`; never touch `.claude\launch.json`; migrations only in your numbered block, each with `node "08 Production/tools/migrations-lock.mjs" --add` and a `Hcc.slnx` line.

## 4. DONE-file template

`05 Delivery Evidence\<Area>\<WAVE>-<LANE>-DONE.md` (for ACC-1: `05 Delivery Evidence\Accounting\ACC1-<LANE>-DONE.md`). Write it even if you stop early.

```
# <WAVE>-<LANE> DONE

Lane / model / branch / worktree / base commit.

## Commits
<hash> <subject>          (git log --oneline <base>..HEAD)

## Files
Created / edited, grouped (prototype, server, tests, docs). Only files from your ownership table.

## What was built
Per brief / contract section: what exists, how it behaves, command or endpoint names.

## Tests
Suites you ran through the lock, counts before / after, the exact command lines. Failures and why.

## Open items
What is not finished, what the rulebook or contract left open, known limits.

## Integration needed
Exact edits that another lane's or the integrator's files need (file, location, text); merge-order notes.

## HANDOFF bullet
The text the coordinator pastes into HANDOFF.md (2 to 4 lines).
```

## 5. How the coordinator runs the gate

The gate checks **one commit of one worktree** (normally the wave worktree after the integrator's merge). It needs a clean worktree on the expected branch (`claude/<wave>`; `-ExpectedBranch` overrides). `.scratch\` and `05 Delivery Evidence\Gates\` do not count as dirty.

```
pwsh -NoProfile -File "E:\Projects\HCC\worktrees\acc1\tools\Start-Gate.ps1" -Repo "E:\Projects\HCC\worktrees\acc1" -Wave acc1
```

`Start-Gate.ps1` writes the pause file (`gate-acc1`), runs `Run-Gate.ps1`, removes the pause file. `Run-Gate.ps1` can also be run alone (then lanes are not paused). It does, in order, each heavy step through the serializer with label `gate-<wave>`:

1. Worktree clean? On branch `claude/<wave>`? Records the commit.
2. `npx tsc --noEmit`, `npm test`, `npm run build` in `03 Prototype`.
3. `dotnet build Hcc.slnx`, `dotnet test Hcc.slnx -maxcpucount:1 --logger trx` in `08 Production`. The full serial suite takes long. Every test project must produce a trx file or the gate fails.
4. `node tools/migrations-lock.mjs` (the lock check; the gate never runs `--add`).
5. Secret scan of the lines added since `merge-base(main, HEAD)`: private keys, connection strings with a password, hard-coded credentials, tokens (GitHub, AWS, Slack, Anthropic / `sk-`, Google, JWT, Bearer). Findings show only the first 3 characters of a value. Fictional fixtures go in `tools\secret-scan-allow.txt` (`rule | path-regex | optional line-regex`, one alternative per line) or carry the marker `secret-scan:allow` on the line.

Output: `05 Delivery Evidence\Gates\<wave>-gate-<yyyyMMdd-HHmm>.md` (PASS / FAIL, a step table with durations, **every failing test name** parsed from the trx files and the npm run, tsc and build errors, scan findings, a machine-readable block) and the full logs in `<repo>\.scratch\gate\<wave>-<stamp>\` (uncommitted: `tsc.log`, `npm-test.log`, `dotnet-test.log`, `trx\*.trx`, the step scripts). Exit 0 = PASS, 1 = FAIL, 2 = a rerun passed on a different commit (the full gate is still required). Steps run even after an earlier failure, so one report lists everything, except `dotnet test` after a failed `dotnet build`.

After a failure and a fix: `Rerun-Gate.ps1 -Repo ... -Wave ...` reruns only the failed steps named in the latest report for the wave and writes a new report. If HEAD is still the reported commit, the earlier passes are carried and the result can be PASS. If a fix commit moved HEAD, the other steps are marked STALE, the result is PARTIAL (exit 2) and the **full** `Start-Gate.ps1` must run before the merge to `main`.

On PASS the coordinator commits the report, fast-forwards `main`, tags `wave/<name>`, and updates HANDOFF / CLAUDE.md / Decision Register / memory / graph. Never push without the owner's word. The installer pipeline is wave W9, not here.
