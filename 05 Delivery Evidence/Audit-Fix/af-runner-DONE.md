# af-runner DONE (REG-07)

Lane af-runner / Sonnet / branch claude/af-runner / worktree E:\Projects\HCC\worktrees\af-runner / base 6a29f3e6. Wave 0 (claude/auditfix not merged).

## Commits
- d09d0854 REG-07: mark async results in the evidence file
- bfa1d90f REG-07: await async tests in the runner, always write the evidence file, add runner self-test
(plus the DONE-file commit)

## Files (all in 03 Prototype)
Created: tests/_runner.mjs, tests/stream-runner-selftest.mjs, tools/runner-selftest.mjs. Edited: tests/run.mjs, package.json (script "test:runner"). No other suite file edited (no class B/C fixes were needed).

## What was built
- createRunner({log,setExitCode,hookProcess}) -> {test,drain,finish,results,pending}. test() runs fn() immediately; a thenable result is recorded only when it settles (own name, error.message, exit code 1). unhandledRejection / uncaughtException become failed results "[runner] unhandled: ...". finish() always writes evidence/domain-tests.json {runAt,passed,failed,total,asyncTests,results}; async results carry "async":true.
- run.mjs: try/finally evidence write; drain after m1 and after every suite register(); a suite whose import/register throws is recorded as failed "[suite] <file>: <message>" and the loop continues; HCC_TEST_ONLY=<regex> filters the stream|sync files (prints "filter: <regex>").
- Self-test suite (own private runner, setExitCode:false) and tools/runner-selftest.mjs ("runner self-test PASS": delayed fail -> exit 1 + evidence names it; delayed pass -> exit 0).

## Tests
- npx tsc --noEmit (through the lock): exit 0.
- npm test through the lock, twice: run 1 2778/2778 passed (20 async), run 2 2778/2778 passed (20 async), 0 failures, exit 0. Logs: .scratch/test1.log, test2.log (uncommitted).
- Before: 2,777 (main). After: 2,778 (+1 = the [RUNNER] self-test).
- node tools/runner-selftest.mjs: PASS. Deliberate check (temp suite with a late assert.fail plus a stray rejection, then deleted): exit 1, evidence names "[TMP] deliberate late failure" (message "deliberate") and "[runner] unhandled: stray", no crash, evidence file written.
- Secret scan: ok true, 0 findings (143 lines, 5 files).

## Async tests (callbacks that returned a promise): 20 found, not 19 (extra: stream-accounting-ui has two). All 20 PASS
stream-accounting-exports: [ACC3-EXPORTS] saveText ...; stream-accounting-ui: [ACC1-UI] source discipline, [ACC2-UI] source discipline; stream-leave-scc-decisions: [LV-DEC] DEC-122 screens, DEC-123 calendar; stream-leave-scc-port: [LV-SCC-PORT] screens; stream-lf-images: 3 x [LF-IMAGES]; stream-n3-polish: [N3-1] staff directory loader; stream-phonefix: 5 x [PF] (AC-04, W05-2..5, W05-6, DEC-060, V11 S4); stream-polish-fixes: [PX] V9-2, G4, G13; sync-frontdesk: [v12fix-b] booking reference; sync-ops-masters: [OPSM-SYNC] floors route.

## Previously-uncounted async tests that now fail: 0
Classes A / B / C: none. Nothing to add to the plan's risk register from this lane.

## Open items
None. Note: stream-phonefix [PF] AC-04 (async) pins the ModalHost onSubmitCapture line; af-small changes it in wave 1.

## Integration needed
None.

## HANDOFF bullet
REG-07 closed (af-runner, claude/af-runner): tests/_runner.mjs awaits async tests under their own names, records unhandled rejections as failures and always writes evidence/domain-tests.json (asyncTests count); HCC_TEST_ONLY=<regex> dev filter; npm run test:runner. npm test 2778/2778 twice (2777 + 1 self-test); all 20 previously-unawaited async tests pass.
