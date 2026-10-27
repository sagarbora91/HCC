# ACC-1 lane briefs — wave `claude/acc1` (27 Oct 2026)

Read first, in order: this file; `02 Planning/agents/ACC1-POSTING-CONTRACT.md` (the shapes — copy, never reinterpret); `02 Planning/HCC_Accounting_Rulebook_v1.md` §3, §3.3, §5 A17, §6, §7.2–7.3, §G2; `CLAUDE.md` (architecture rules, commands); `02 Planning/modules/HCC_Module_20_Accounting_Spec.md`; the existing code named in your lane. Base = the head of `claude/acc1` when your worktree was created (`git log -1`).

## Rules every lane follows

1. **Work only in your worktree** `E:\Projects\HCC\worktrees\<lane>` on branch `claude/<lane>`. Commit early and often with clear messages; **never push**; never use bare `git stash`; never touch `.claude/launch.json`.
2. **Edit only the files in your ownership table.** A change you need elsewhere goes in your DONE file under "Integration needed" with the exact edit. The three append-only shared files (`commands.ts`, `services/mock/index.ts`, `HccComposition.cs` / endpoint registration) are edited only by **appending lines inside the `// ACC-1` block**, one line per lane, so merges are trivial.
3. **Every build and test run goes through the lock:** `pwsh -NoProfile -File "E:\Projects\HCC\worktrees\acc1\tools\Invoke-Serialized.ps1" -WorkingDirectory "<your worktree>\03 Prototype" -Label <lane> -Command "npm test"` (and `npx tsc --noEmit`, `npm run build`, `dotnet build`, `dotnet test`). Exit 75 = busy, wait and retry the same call. Run **only the test projects you touch** on the server (`dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1 --logger trx`; the full `Hcc.slnx` suite is the integrator's). The prototype suite is one command (`npm test`), run it whole. **Never filter test output**; keep the whole log in your scratch folder.
4. Money = integer paise; simulated clock; fictional data; dated GST master (`src/domain/gst.ts`); Owner-only money settings (DEC-058); housekeeping / maintenance see no money (DEC-061); SCC rules in full where SCC is the source (DEC-116); no new npm packages; one self-contained HTML; colours only as tokens.
5. Migrations: only your numbers (contract §9); `node "08 Production/tools/migrations-lock.mjs" --add` + a line in `Hcc.slnx` per migration; never edit an applied migration.
6. Prototype tests live in `03 Prototype/tests/stream-<name>.mjs` (`register({test,M,assert})`, names prefixed `[ACC1-<LANE>]`); the harness discovers files by name. Server tests in `08 Production/tests/Hcc.Accounting.Tests` (Domain / Sql / Golden folders).
7. **DONE file** `05 Delivery Evidence/Accounting/ACC1-<LANE>-DONE.md`: commits, files, what was built against which contract section, test counts (before / after, both suites you ran), open items, "Integration needed", the HANDOFF bullet text. Write it even if you stop early.
8. Commit messages end with: `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
9. If a rule in the rulebook and the code disagree, the rulebook wins; if the contract and the rulebook disagree, stop and write it in the DONE file; do not pick one silently.

## Lane: acc1-chart (Opus) — groups, chart as a master, re-pointed mapping

Contract §1, §2, §7 (PERM-ACC-007), §8, §9.
- **Prototype:** `ACC_GROUPS` and the built-in ledgers (`ACC_CHART` extended to the §2 table, every row carrying `group`, `system`, `control`) in `src/domain/streams/accounting.ts` (your sections: chart constants, mapping defaults, `accAccount`, `accLedgerName`, `ACC_DEFAULT_MAP`, the `KEY_LABEL` table; the derived legacy `ACCGroup` of a group); new `src/domain/streams/accounting-chart.ts` (`accChart(s)` = built-ins + Owner overrides + Owner-added ledgers, `accGroups(s)`, `accLedgerNature`, `accChartProblems`, retire rules of contract §2); handler `src/services/mock/handlers/accounting-chart.ts` with `ACC_LEDGER_UPSERT`, `ACC_LEDGER_RETIRE`, `ACC_GROUP_UPSERT` (custom sub-groups), history rows; the mapping re-points of contract §2 (`pos:sales` → 4200, `line:attrition` → 4300, cash over → 4950, `tax:gst-input` → 1310 + `tax:gst-input-sgst` → 1320) with locked snapshots untouched; `accMapRows` key list extended; existing tests updated where a default changed (say which in the DONE file).
- **Server:** `Hcc.Domain/Accounting/ChartOfAccounts.cs` (groups, the §2 table, `GroupOf`, nature, control flag, override merge), `Hcc.Application/Accounting/ChartService.cs` + `ChartQueries.cs` (upsert / retire / list with permission PERM-ACC-007), `Hcc.Infrastructure.SqlServer/Accounting/SqlChartStore.cs`, endpoints `Hcc.Api/Endpoints/AccountingChartEndpoints.cs`, migrations 4200–4209 (`acc.ledger_group` seeded with the 28 groups, `acc.ledger` with the built-ins + override columns, `acc.ledger_change`, RBAC seed PERM-ACC-007 → Owner), `AccountingEventMapper` mapping defaults re-pointed.
- **Tests:** prototype `tests/stream-accounting-chart.mjs`; server `Hcc.Accounting.Tests/Domain/ChartTests.cs` + a Sql test for the store.
- Do **not** touch `accLiveVouchers`, the statements, the opening entry or the UI pages (other lanes).

## Lane: acc1-parties (Opus) — party sub-ledgers, Udhaar credit raised

Contract §3, §4, §9.
- **Prototype:** new `src/domain/streams/accounting-parties.ts` (`accPartyLedgers(s)`, `accPartyName`, `accIsParty`, `accControlOf`, the per-source resolvers of contract §3); your sections of `src/domain/streams/accounting.ts`: `accLiveVouchers` (post to `<control>/<party>` per §3, the `no-party` warning in `accExceptions`), `accMapRows` uses, `accDates`, `accSourceOf`, `accTrialBalance` (sum parties into controls unless `detail`); Udhaar credit raised (contract §4) in `src/domain/streams/expenses-scc.ts` (`kind`, `gstBps` on `ExpUdReceivable`, the voucher source) and its handler lines in `src/services/mock/handlers/expenses-scc.ts` (only the raise path); master retire guard: `mnyVendorUsage` / `mnyAccountUsage` in `src/domain/streams/money-master.ts` and the company / staff retire checks gain "non-zero ledger balance" (ask the coordinator through the DONE file if a check lives in a file you do not own).
- **Server:** `Hcc.Domain/Accounting/AccountingSource.cs` (`SourceEntry.Party`, `SourceRules.UdhaarCredit`, party on every rule of contract §3), `Vouchers.cs` (`VoucherBuilder` appends the party suffix; `TrialBalance(detail)`), `AccountingEventMapper.cs` (party from each event), `SqlAccountingStore.cs` (`party_id` read / write), `AccountingVoucherProjector.cs`, the Udhaar raise command in `Hcc.Application/Expenses` (event `UdhaarCreditRaised`), migrations 4210–4219.
- **Tests:** `tests/stream-accounting-parties.mjs`; server `Domain/PartyLedgerTests.cs`, `Golden/` regenerated only if a mapping default you own changed (say so).
- Do **not** touch the chart constants, the opening entry, the statements file or the UI.

## Lane: acc1-opening (Opus) — opening balances by hand

Contract §5, §7 (PERM-ACC-008/009), §9.
- **Prototype:** new `src/domain/streams/accounting-opening.ts` (`accOpening(s)`, `accOpeningVoucher(s)`, balancing line on 1400, `accOpeningPending(s)`, problems), handler `src/services/mock/handlers/accounting-opening.ts` (`ACC_OPENING_SAVE / APPROVE / REOPEN`, permissions of §7, audit rows), the lock refusal in `accLockBlocker` (your one edit in `accounting.ts`, a single line calling your function), `accVouchersUpTo` includes the opening voucher (one line).
- **Server:** `Hcc.Application/Accounting/OpeningBalanceService.cs` + `OpeningBalanceQueries.cs`, `Hcc.Infrastructure.SqlServer/Accounting/SqlOpeningStore.cs`, endpoints `Hcc.Api/Endpoints/AccountingOpeningEndpoints.cs`, the lock refusal in `AccountingService` (one guard call), `AccountingQueries` cumulative TB reads the approved entry ahead of the imported one (one call), migrations 4220–4224 (`acc.opening_entry`, `acc.opening_line`, RBAC PERM-ACC-008/009).
- **Tests:** `tests/stream-accounting-opening.mjs` (the rulebook §8.1 opening as the fixture: ₹77,40,000 both sides; an unbalanced draft parks the gap in 1400; approval; lock refused while pending); server `Domain/OpeningBalanceTests.cs` + Sql.
- Do **not** touch the chart, parties or statements files, or the UI.

## Lane: acc1-statements (Opus) — ledger / party statement, six-column trial balance, day book

Contract §6, §9.
- **Prototype:** new `src/domain/streams/accounting-reports.ts` (`accStatement`, `accTrialBalance6`, `accDayBook`, CSV builders for each); no edits to `accounting.ts` beyond importing.
- **Server:** `Hcc.Application/Accounting/AccountingReportQueries.cs` (statement, six-column TB with `detail`, day book; opening = vouchers before `from` incl. the opening entry and locked snapshots), `SqlAccountingStore` additions **in a new partial file** `SqlAccountingStore.Reports.cs`, endpoints `Hcc.Api/Endpoints/AccountingReportEndpoints.cs`, migrations 4225–4229 only if a view is needed.
- **Tests:** `tests/stream-accounting-reports.mjs` (running balance, Dr / Cr display, opening before `from`, control vs detail); server `Domain/ReportQueriesTests.cs` + Sql.
- Do **not** touch the UI or the other lanes' files.

## Lane: acc1-ui (Sonnet) — screens, desktop and phone

Contract §2, §3, §5, §6, §7. Build against the **selector and command names in the contract** (stub them locally in your branch under `src/features/accounting/_contract-stubs.ts` if a lane has not landed yet; the integrator deletes the stubs).
- Files: `src/features/accounting/Accounting.tsx` (tabs: Vouchers · Trial balance (six-column, from / to, detail toggle) · Statements (ledger or party picker, running balance, CSV) · Day book · Chart of accounts (editable for the Owner: group tree, ledger rows, add / edit / retire, history) · Parties (list by control with balances) · Opening balances (entry grid, 1400 line shown, Save / Approve / Reopen per role) · Exceptions · Reconciliation · Mapping · Tally export), `Mobile.tsx` (phone versions: cards, sheets, sticky footer, 48 px targets), `parts.tsx` (new modals `acc-ledger`, `acc-group`, `acc-opening-line`), `register.tsx`, `src/styles/m-accounting.css` (tokens only; run `node tools/themes.mjs --write` if a colour is added), `src/app/navmap.ts` keywords line for accounting (append words only).
- Tests: `tests/stream-accounting-ui.mjs` (registration, permission gating per role, phone pages registered, `PAGE_CATEGORY` test still green).
- Do **not** write domain logic; if a selector is missing, stub it and note it.

## Lane: acc1-tests (Sonnet) — the rulebook golden month, ACC-1 subset

Rulebook §8 is the golden test; ACC-1 can produce only the subset the engine posts today plus the opening entry. Build the fixture and the expectations for that subset; record the full-month figures as the ACC-2 target.
- **Prototype:** `src/fixtures/ext/accounting-rulebook.ts` (scenario `ACC-RULEBOOK`: the §8.1 opening as an approved opening entry via the commands of contract §5; parties: company Deccan Agro Ltd, vendors Morning Supplies and Kaveri Textiles, bank HDFC, OTA Goibibo, staff STF-007; then the transactions the engine can post in ACC-1: T1, T2, T3, T5a–c, T7a, T8, T9, T10, T12, T13a–c, T15a–b, T16 driven through real commands — bookings, folio lines, receipts, POS bills, expenses with vendor credit, salary advance, pay run, banking, drawing); `tests/stream-accounting-rulebook.mjs`: every ledger balance of the subset, party balances (Deccan 63,000 Dr after T5b then 23,000 after T7a; Morning Supplies 84,000 Cr; Kaveri 42,000 Cr; HDFC per the subset), the six-column TB balanced, the statement of `1110/<Deccan>` with running balance, opening pending → lock refused, approved → allowed. Compute the subset's expected closing trial balance yourself from §8.2 and put the table in your DONE file.
- **Server:** `Hcc.Accounting.Tests/Golden/RulebookMonthTests.cs` + `rulebook-month.json` (the same subset through the server's commands / events; opening through the opening endpoint), run with `dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1`.
- Depends on the other lanes' selectors: start with the fixture and the expectation tables, write the tests against the contract names, and run them as the lanes land on `claude/acc1` (the coordinator merges early lanes into the wave branch; `git merge claude/acc1` into your branch when told).

## Lane: acc1-tools (Sonnet) — gate scripts and the agent how-to

- Port ETP's gate chain to HCC: read `E:\ETP\Migration 2026-10-04\Start-Gate-1.13.0.ps1`, `Run-Gate-1.13.0.ps1`, `Rerun-Gate-1.13.0.ps1` and `E:\ETP\Code\Worktrees\_tools\1145-fastgate.ps1`; write `tools\Start-Gate.ps1` (writes `E:\Projects\HCC\worktrees\_tools\PAUSE-LANE-BUILDS` with the gate label, then runs `Run-Gate.ps1` through `Invoke-Serialized.ps1`), `tools\Run-Gate.ps1 -Repo <worktree> -Wave <name>` (clean-clone check, `npm ci`-free install via the junction, `npx tsc --noEmit`, `npm test`, `npm run build`, `dotnet build Hcc.slnx`, `dotnet test Hcc.slnx -maxcpucount:1 --logger trx`, `node 08 Production/tools/migrations-lock.mjs --check` if it exists, a secret scan over the diff since `main`, the result written to `05 Delivery Evidence/Gates/<wave>-gate-<timestamp>.md` with PASS / FAIL and every failing test name), `tools\Rerun-Gate.ps1`. No installer step yet (W9).
- `tools\README-agents.md`: how a lane calls the serializer, where the log and the pause file are, the DONE-file template, the ownership rule.
- Tests: run the gate once against `claude/acc1` itself as a dry run (it may fail on the unfinished lanes; record the output).
- Do **not** edit any product code.

## Integrator (Opus, after the lanes' DONE files)

Merge `claude/acc1-chart`, `-parties`, `-opening`, `-statements`, `-ui`, `-tests`, `-tools` into `claude/acc1` in that order; resolve the three append-only files and any hunk overlap in `accounting.ts`; delete `_contract-stubs.ts`; `npx tsc --noEmit`; full `npm test`; `npm run build`; `dotnet build Hcc.slnx`; `dotnet test Hcc.slnx -maxcpucount:1`; regenerate `accounting-golden.json` only with a stated reason; phone smoke of the accounting pages at 360 px; write `05 Delivery Evidence/Accounting/ACC1-DONE.md` with counts before / after (baseline 1867 / 3324) and the HANDOFF bullets. The coordinator then runs the gate, fast-forwards `main`, updates HANDOFF / CLAUDE.md / memory / graph.
