# af-small DONE

Lane af-small (wave 1) / Sonnet / branch `claude/af-small` / worktree `E:\Projects\HCC\worktrees\af-small` / base 6a29f3e6 plus `claude/auditfix` (wave-0 runner) merged.

## Commits
See `git log --oneline 6a29f3e6..HEAD` (merge of claude/auditfix, then the af-small commits).

## Files
Prototype src: `03 Prototype/src/app/ModalHost.tsx`, `src/clients/sync/reports.ts`, `src/clients/sync/runtime.ts` (UNMAPPED_TEXT only), `src/features/assistant/register.tsx`, `src/features/reports/serverBits.tsx` (the history note; HistorySummary.tsx is the prototype-only screen and was not touched), `src/mobile/admin/sheets.tsx` (one setDirty line, see UX-05 audit).
Prototype tests: `tests/stream-a11y.mjs`, `tests/stream-phonefix.mjs`, `tests/sync-reports.mjs`, `tests/sync-notconfigured.mjs`, new `tests/stream-af-small.mjs`.
Server: `08 Production/deployment/windows/Set-HccEgress.ps1`, `08 Production/deployment/windows/tests/Test-HccDeployment.ps1`.
`reportsHooks.ts` was not touched (the hook returns `ServerHistory`, the new fields flow through).

## What was built
- **UX-05 / Missed F**: removed `onSubmitCapture={()=>setDirty(false)}` from ModalHost; only a successful `run()` (App.tsx AC-04) clears the flag. a11y and phonefix tests updated (phonefix renamed `[PF] UX-05 ...`). New `[AF-UX05]` test.
  Fallout audit (script over every `onSubmit` handler in `src/**/*.tsx` that closes a dialog without run/await): exactly one hit, `src/mobile/admin/sheets.tsx` `MaAuditFilterSheet` (local filter form, `maSetUi` then `closeModal`). Fixed with `setDirty(false)` before `closeModal()` (one line, plus the `setDirty` destructure).
- **DEP-01**: the catch-block detail write in `Set-HccEgress.ps1` is now behind `$PSCmdlet.ShouldProcess('ops.egress_firewall_state', "record detail = not applied: $msg")`; Write-HccLog and `exit 1` kept.
  The plan said existing `Set-HccEgress -WhatIf` checks exist in `Test-HccDeployment.ps1`; there were none (no mention of Egress in the file). The new check therefore uses the file's own `DryRun` helper with `-State Apply -Instance localhost\NoSuchInstance_dep01 -ConfigPath <none>`: the first SQL read fails, the catch runs, output must contain `What if: Performing the operation "record detail = not applied: ` for `ops.egress_firewall_state`, exit code 1, and no `record applied_state`. Note: this check takes about 30 s because sqlcmd waits for the unreachable instance.
- **PERF-06**: `loadServerHistory` loads months in batches of 3 with `Promise.allSettled`; a failed month = zero cell with `error:true` and an entry in `ServerHistory.errors:{month,text}[]` (month = label, e.g. "Jun 2026"); totals sum loaded months; `ServerHistorySummary` (serverBits.tsx) shows `"<n> month(s) could not be loaded from the server: <labels>. Totals cover the loaded months."`. `inBatches` was not reused (money.ts untouched; the batching is inline in reports.ts).
- **Items 24 / 25**: 16 keys added to `UNMAPPED_TEXT` with the texts from the brief (KEY_* four share one text, AI_* three share one, the five server-schedule keys share "Runs on the hotel server on its own schedule."). Assistant registration (`nav`, `pages`, `mobile.pages`) empty when `process.env.HCC_BUILD==='production'`, written inline so esbuild folds it and the Assistant page is dropped from the production bundle.

## Tests
- `npm test` baseline after merging claude/auditfix: 2778/2778 (20 async). After: **2783/2783 (20 async)**, 0 failures (`.scratch/full.txt`; re-run after the last register.tsx tweak in `.scratch/full2.txt`, 2783/2783 again, tsc clean).
- New tests: `[AF-UX05]`, `[AF-SMALL]` assistant production branch, `[AF-SMALL]` unmapped texts (sync-notconfigured), `[AF-PERF06]` x2 = 5. Two existing tests re-worded (a11y, phonefix).
- `npx tsc --noEmit` clean; `npm run build` and `npm run build:production` 0 warnings.
- Deployment tests, `08 Production\deployment\windows\tests\Test-HccDeployment.ps1`: pwsh 7 before 91 / after 92; Windows PowerShell 5.1 after 92 (both 0 failed). Logs `.scratch/dep-*.txt`.
- Production bundle check: `Management assistant` appears 4 times in `dist/production/index.html` against 5 in the demo build; the remaining occurrences are catalogue/config strings (`feature-catalogue.ts`, `streams/config.ts`, `housekeeping.ts`), not the page, and are outside this lane's files. The nav entry and page are gone.
- Secret scan (`Invoke-SecretScan`, base 6a29f3e6): `ok:true`, 0 findings.

## Open items
- Owed to the gate/APK wave: phone check `tools/v15-fixes-check.mjs` (Discard prompt, V15-2 / AC-M03) on the next APK, now that the dirty flag survives Review.
- `feature-catalogue.ts` / `streams/config.ts` still list the "assistant" feature switch in production; hiding that is a separate decision (not in the brief).
- Nothing stopped: no money, GST, posting, permission or ledger rule was touched.

## Integration needed
None.

## HANDOFF bullet
af-small (wave 1, claude/af-small): UX-05 (ModalHost no longer clears the unsaved flag at submit; one local-filter sheet patched), DEP-01 (Set-HccEgress catch write behind ShouldProcess, deployment tests 91 -> 92), PERF-06 (year history 3 months at a time, failed month = error cell + note), items 24/25 (16 unmapped-command texts, assistant hidden in production). Prototype 2778 -> 2783. Owed: v15-fixes-check on the next APK.
