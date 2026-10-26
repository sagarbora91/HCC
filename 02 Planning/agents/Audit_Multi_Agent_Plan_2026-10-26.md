# HCC independent audit — multi-agent execution plan (Fable lead)

Companion to `Audit_Prompt_Fable.md` (the Opus-written prompt, commit 0a91418). That prompt says *what* to audit and *what* to deliver. This plan says *who* does each part, in what order, with which model, under which locks, and how claims get verified before they reach the report. **Nothing in this plan has been started.** Written 26 Oct 2026 from a read of the prompt, `CLAUDE.md`, `HANDOFF.md` Sessions 12–12b, `08 Production` (28 test projects, 47 migrations, 291 `MapGet/MapPost` calls across 31 endpoint files, 43 PERM ids in contracts), `03 Prototype/src/clients/sync` (20 area files), the security review (4 High / 7 Medium / 11 Low / 6 Info) and `ETP_Reuse_Pending.md` (38 stand-ins).

## 0. Facts the plan is built on

| Fact | Consequence |
|---|---|
| Work PC: 12 cores, 202 GB free on E:, parallel agents welcome (~16), one emulator at a time | 13 agents in the main wave is fine; no emulator by default |
| .NET tests must run serially (`dotnet test Hcc.slnx -maxcpucount:1`, ~20–30 min) on `.\SQLEXPRESS` with `HccTest_*` DBs only | **One test lane.** Only agent A1 runs the full suite. Anyone else who needs `dotnet test` takes a lock file first (§4) |
| Node 25 must be first on PATH (`E:\Tools\nodejs`) | Every agent that runs `npm`/`node` prefixes `$env:PATH="E:\Tools\nodejs;$env:PATH"` and proves it with `node -v` (v25) in its evidence |
| Audit is read-only on product code; outputs only in `05 Delivery Evidence\Audit\` | Each agent writes **only** its own file `Audit\agents\A<nn>_*.md` and its own folder `Audit\evidence\A<nn>\`. No two agents share a file, so there is nothing to merge |
| HANDOFF 12b says APK v12 emulator tests were "running at handoff", but `05 Delivery Evidence\Acceptance\APK_v12_Emulator_Report.md` **does not exist**; only an untracked `Acceptance\v12\` screenshot folder (a-01…a-10) is there | A10 audits the APK statically and records this as an unverified claim; the untracked `v12\` folder is **not** added to the audit commit |
| `main` is 173+ commits ahead of the public GitHub origin; push waits for the owner | Audit branch is local only. `git add` only the `Audit` folder (plus the one HANDOFF entry in §2 Phase 4) |
| 38 `ETP-REUSE` stand-ins; ETP itself is not on this PC | A9 classifies each stand-in as go-live blocking / not; nobody tries to fetch ETP |

## 1. Roles and models

| Model | Used for | Why |
|---|---|---|
| **Fable** (lead = the session itself, plus 2 Fable subagents) | Orchestration, security, data protection/legal, verification of every Critical/High, readiness %, final report | Judgement on money, law, security and "does this really block go-live" |
| **Opus** (6 agents + V1) | Money/GST recomputation, payroll/statutory, architecture, production readiness, go-live blocking calls on coverage gaps, blind re-verification | Needs reasoning over rules and code, not just counting |
| **Sonnet** (6 agents) | Re-running builds/tests, enumerations, route tables, grep sweeps, doc-vs-code diffs, APK static checks | Mechanical, high-volume, cheap |

Spawn with the Agent tool: `model: "sonnet" | "opus" | "fable"`, `subagent_type: "general-purpose"`. Each agent gets the brief in §5 verbatim plus the **common preamble** in §3. Fable subagents and the lead share one model, so the lead may do A6/A7 itself if the agent budget is tight; the plan keeps them as subagents so the lead stays free to coordinate.

## 2. Phases and timeline (wall clock, approximate)

```
Phase 0  Lead (Fable)            20 min   setup, branch, env checks, spawn
Phase 1  13 agents in parallel   60–90 min
         A1  Sonnet  claims vs reality (owns the test lane)
         A2  Sonnet  coverage table (mechanical)        ─┐
         A2b Opus    go-live judgement on each gap       ─┘ A2b starts when A2's file lands
         A3a Opus    billing / GST / folio / invoices / credit notes / groups
         A3b Opus    cash book / drawer / day close / vouchers / TB / Tally
         A4  Opus    payroll & statutory
         A5  Sonnet  route → permission → role table
         A6  Fable   security follow-up (22 findings + Wave 4 surfaces)
         A7  Fable   data protection + legal note
         A8  Opus    architecture + test quality
         A9  Opus    production readiness + ETP stand-ins
         A10 Sonnet  APK static
         A11 Sonnet  docs vs code
Phase 2  Verification             45 min
         V1  Opus    blind re-verification of a sample of findings
         Lead        reproduces every Critical/High itself
Phase 3  Lead writes the report   45 min
Phase 4  Commit on the audit branch, chat summary   10 min
```

Total about 3–4 hours. The 13 Phase-1 agents plus the lead stay under the 16-agent comfort limit; V1 runs after most of them finish.

### Phase 0 — lead setup (nothing product-side changes)

1. `git status` must show only the untracked `Acceptance/v12/` folder. `git checkout -b claude/audit-fable-2026-10-26` in the same checkout, no worktree: the prototype needs its `node_modules`, and a worktree would cost disk and an `npm install`.
2. Create `05 Delivery Evidence\Audit\{agents,evidence,probes,locks}`. Write `Audit\README.md` with the layout and the finding template (§6).
3. Environment proof into `Audit\evidence\lead\env.txt`: `node -v` with Node 25 first on PATH, `dotnet --info` (SDK 10), `sqlcmd -S .\SQLEXPRESS -Q "SELECT name FROM sys.databases"` (confirm no leftover `HccTest_*`; confirm `EtpReporting`, `EtpScratch_*` and `Hcc` are untouched and will stay so), free space on E:, no running emulator (`adb devices`), no other `dotnet test` process.
4. Read `Audit_Prompt_Fable.md`, `CLAUDE.md`, HANDOFF Sessions 10–12b, `MIGRATION.md`, `W4_Briefs.md`, the security review summary and `ETP_Reuse_Pending.md` once, so the lead can judge agents' outputs without re-reading everything later.
5. Spawn A1 first (it owns the test lane and runs longest), then the other 12 in one message.

### Phase 1 — rules every agent follows
See §3 (preamble) and §4 (locks). Agents report back with the path of their file; the lead takes findings from the file, not from the chat return value.

### Phase 2 — verification
- **V1 (Opus, blind):** receives the agents' findings with the *evidence columns stripped* (the lead makes `Audit\probes\v1-input.md` with ID, severity, area, file:line, claim only). V1 must reproduce each sampled finding independently and mark `CONFIRMED / PARTLY / NOT REPRODUCED`. Sample: every Critical and High, every finding from a Sonnet agent that the report will cite, and 1 in 3 of the rest, chosen by the lead.
- **Lead:** personally reproduces every Critical/High (command output or probe in `Audit\evidence\lead\`), reads every "unverified claim" entry from A1, A10 and A11, and resolves disagreements between an agent and V1 by running the check itself. Anything that fails verification goes into the report's "Claims we could not confirm" section instead of the findings table.

### Phase 3 — report (lead only)
`Audit\HCC_Independent_Audit_2026-10-26.md` in the order the prompt asks: one-page plain-English summary with readiness % (rubric in §8), findings table, coverage table (from A2/A2b), golden-figure recomputations (from A3a/A3b/A4), unverified claims (A1, A10, A11), open owner/CA decisions with the code each one blocks (A4, A9, A11, A2b), prioritised go-live plan with hours.

### Phase 4 — commit
`git add "05 Delivery Evidence/Audit"` only; commit on the audit branch with the Co-Authored-By line. Then one more commit that appends a 3-line "Session 13 — independent audit" entry to `HANDOFF.md` pointing at the report (the one documentation exception to read-only, kept in its own commit so the audit commit stays clean). No merge, no push. Chat summary: readiness %, Critical/High counts, top 5 fixes, decisions the owner must make.

## 3. Common preamble (paste at the top of every agent brief)

> You are one auditor on an independent audit of Hotel Control Centre (HCC) in `E:\Projects\HCC\Hotel Control Centre`. Read `02 Planning\agents\Audit_Prompt_Fable.md` section "Ground rules" first; they bind you.
> **Hard rules.** Read-only on the repository. Write only to your own file `05 Delivery Evidence\Audit\agents\A<nn>_<name>.md` and your own folder `05 Delivery Evidence\Audit\evidence\A<nn>\`; probe scripts go in `05 Delivery Evidence\Audit\probes\A<nn>_*.{mjs,ps1,sql}`. Never run `git checkout`, `git stash`, `git add`, `git commit`, `git push`, `git merge`, `git rebase` or anything that changes the working tree outside the Audit folder. Never run `npm install`. Never touch databases other than `HccTest_*` on `.\SQLEXPRESS`; never `EtpReporting`, `EtpScratch_*`, or `Hcc`. Do not start an Android emulator. Do not run `dotnet test` unless your brief says you own or may take the test lock (see "Locks"). Node commands: first `$env:PATH="E:\Tools\nodejs;$env:PATH"` and record `node -v` (must be v25). Before any build, check free space on E: (`Get-PSDrive E`); stop and report if under 20 GB.
> **Evidence standard.** Every finding carries `file:line` (or a test name, a command-output file, or a probe script path) that someone else can open and confirm. No finding without evidence. Say "could not verify" rather than guessing. Distinguish *what the code does* from *what the docs say* from *what you think the law requires*.
> **Output format.** Use the finding template in `05 Delivery Evidence\Audit\README.md` (ID = `A<nn>-<seq>`, severity Critical/High/Medium/Low/Info, area, file:line, evidence, impact, recommended fix, effort hours, confidence High/Medium/Low). End your file with three lists: "Claims I could not confirm", "Open owner/CA decisions I hit (and the code that depends on each)", "Questions for the lead". Keep your chat reply to one paragraph plus the file path; everything else goes in the file.
> **Locks.** `05 Delivery Evidence\Audit\locks\dotnet-test.lock` and `emulator.lock`. To take one: create the file with your agent ID and a timestamp only if it does not exist; delete it when done; never delete another agent's lock. If a lock exists, do static work and retry later.

## 4. Locks and shared resources

| Resource | Owner | Others |
|---|---|---|
| Full `dotnet test Hcc.slnx -maxcpucount:1` | A1 (holds `dotnet-test.lock` for the run) | May run a *single* test project or a probe test only after taking the lock; A3a/A3b/A4/A8/A9/V1 are allowed, Sonnet agents are not |
| `dotnet build Hcc.slnx` | A1 first (produces the 0-warnings evidence) | Others may build (it is idempotent) but should prefer reading A1's output in `evidence\A01\` to save time |
| Full `npm test` (1008), `npm run build`, `npm run build:production`, `npx tsc --noEmit` | A1 | Others may run a single test file (`node tests/<file>.mjs`) freely; no full suite |
| Android emulator | nobody by default | A10 may take `emulator.lock` **only** if the lead says so in Phase 2 |
| SQL Server | A1 (tests) | A9's restore drill is reviewed on paper, plus a parameterised dry run into a `HccTest_drill_*` DB if, and only if, the script accepts a target-name parameter; otherwise paper only |
| `Audit\agents\A<nn>_*.md` | that agent | read-only for everyone else |

## 5. Agent briefs (one block each; §3 preamble goes on top)

### A1 — Claims vs reality (Sonnet, owns the test lane)
Inputs: `HANDOFF.md` Sessions 10–12b, `CLAUDE.md`, `08 Production\README.md`, `03 Prototype\README.md`.
1. Take `dotnet-test.lock`. In `08 Production`: `dotnet build Hcc.slnx` (tee to `evidence\A01\dotnet-build.txt`), then `dotnet test Hcc.slnx -maxcpucount:1` (tee to `dotnet-test.txt`). Record per-project pass/fail counts, total, warnings, duration. Afterwards `SELECT name FROM sys.databases WHERE name LIKE 'HccTest%'` must be empty; record it. Release the lock.
2. In `03 Prototype` with Node 25: `npx tsc --noEmit`, `npm test`, `npm run build`, `npm run build:production`; tee each. Record test count, build sizes, warnings, that `dist\index.html` is self-contained (no `http://`/`https://` asset references; grep it), and that the production build contains no demo controls (`stream-clients.mjs` covers this; name the assertion).
3. Extract **every quantitative or checkable claim** from HANDOFF 10–12b and the CLAUDE.md status lines (test counts, "0 warnings", "28 projects", "all 22 findings fixed except F-M03", "secret scan clean", "APK v12 built", "86 GB free", "E2E run 2 all pass", migration counts, golden figures quoted) into a table: claim → where stated → how checked → Confirmed / Not confirmed / Could not check. Re-run the secret scan the HANDOFF describes (no keystores, APKs or keys tracked: `git ls-files` filtered by extension, plus a grep for `password=`, `BEGIN PRIVATE KEY`, `Pwd=` in tracked text) and record the result.
4. Note that `Acceptance\APK_v12_Emulator_Report.md` is absent; list what evidence of v12 exists (the untracked `v12\` folder, `git log` for versionCode 12).
Output: `agents\A01_claims_vs_reality.md`. Do not judge severity beyond Confirmed/Not confirmed; the lead does.

### A2 — Feature coverage table (Sonnet)
Inputs: `03 Prototype\src\services\mock\commands.ts` and `handlers\*.ts` (every command `type`), `src\features\*` (every screen and the commands it dispatches), `src\clients\sync\*.ts` (`commands:[{type:…}]` and `unmapped:{…}` in every area file incl. `services\*`), `src\clients\sync\registry.ts`, `capabilities.ts` (`CAPABILITY_GAPS`), `tests\_role-reads.json`.
1. Build the complete set of prototype command types (script it: `probes\A02_commands.mjs`; count them).
2. For each: mapped (area file, server command name, reload slices) / unmapped with reason / **absent** (neither mapped nor listed as unmapped: these are silent refusals, flag them).
3. For each prototype screen (desktop and phone registrations in `registry.ts` / `register.tsx`): which commands it needs, how many are mapped, whether its loaders exist (slice filled by some loader or left empty from `empty.ts`).
4. Cross-check with the server: every mapped server command name exists in `08 Production\src\Hcc.Api\Endpoints\*.cs` (`/commands/<Name>`); list any mapper that targets a command the server does not expose.
Output: `agents\A02_coverage_table.md` with the table *area → commands total / mapped / unmapped / absent* and a per-command appendix. No blocking judgement; that is A2b.

### A2b — Go-live judgement on coverage gaps (Opus, starts when A2's file exists)
Inputs: A2's file, `02 Planning\HCC_Business_Workflow_Spec_v1.md`, `HCC_Implementation_Backlog.md`, `HCC_Acceptance_Matrix.md`, `W4_Briefs.md`.
For every unmapped or absent command and every screen with empty slices: Blocks go-live (a daily hotel task cannot be done on the server) / Workaround exists (say which) / Not needed for go-live (why). Tie each to a workflow in the business spec. Produce the ranked list of gaps with effort hours (mapper + any missing server endpoint + test). Flag any mapper whose target command the server lacks as High.
Output: `agents\A02b_coverage_judgement.md`.

### A3a — Billing, GST, folio, invoices, credit notes, groups (Opus; may take the test lock for single projects)
Inputs: `src\Hcc.Domain\{Money,Pricing,Tax,Billing,Invoices,Groups}`, `src\Hcc.Application\{Billing,Tax,Invoices,Groups}`, migrations `0400`, `0600`, `1201`, `2200–2202`, `2400–2401`, prototype `src\domain\gst.ts` and `money.ts`, golden tests (`tests\Hcc.GstIds.Tests`, `Hcc.Billing.Tests`, `Hcc.Invoices.Tests`, `Hcc.Groups.Tests\Kernel\GroupRulesGoldenTests.cs`), `tests\Hcc.Platform.Tests\Golden\base-fixture.golden.json`, DEC-041, DEC-063, DEC-069 in the Decision Register, CA pack G1–G12.
1. **Recompute independently** (own script in `probes\A03a_*.mjs`, not by calling HCC code): room 105 ₹4,265 incl. ₹265 GST; the BASE drawer ₹10,725; GB-1/GB-2/CO-1/CO-2 group figures; one mixed folio (one room night ≤ ₹7,500, one above, restaurant, laundry) with CGST/SGST per line, odd paisa to CGST, half away from zero; a refund and a credit note; invoice number format `INV/2627/0000001`. Compare with the golden JSON and with the server tests' expected values. Report each as Match / Mismatch with the arithmetic shown.
2. Check invoice numbering is gap-free under concurrency (how the lock works, what happens on rollback after number allocation), the immutability trigger, GSTIN checksum, IGST path (place of supply), credit-note approval (Owner only).
3. Check posted lines keep `taxBps` snapshots and that a GST master change does not rewrite history (find the test; if none, write a probe).
4. Rounding: find every place money is divided or multiplied by a rate in Domain/Application; confirm the paise kernel is used (grep for `decimal`, `double`, `Math.Round` outside the kernel).
5. List rules that are guesses pending the CA (map to G-ids) and the code line each depends on.
Output: `agents\A03a_billing_gst.md`.

### A3b — Cash book, drawer, day close, accounting, trial balance, Tally (Opus; may take the test lock for single projects)
Inputs: `src\Hcc.Domain\{Accounting,Closing,Payments}`, `src\Hcc.Application\{Accounting,Closing,Payments,Reporting}`, `AccountingProjectorWorker`, migrations `0500`, `1000–1001`, `2702`, prototype `src\domain\streams\{accounting,closing,cashbook}.ts`, tests `Hcc.Accounting.Tests`, `Hcc.Closing.Tests`, `Hcc.Platform.Tests` golden, DEC-052 shared till, DEC-028, HANDOFF notes on vouchers 2110/2120 and opening balances.
1. Recompute independently: a day's closing summary (receipts by tender, refunds, expenses, drawer expected vs counted) from the BASE fixture; the trial balance including opening balances (debits = credits; the opening-TB import path); the two voucher types 2110/2120 for one receipt and one refund; one Tally XML voucher (ISDEEMEDPOSITIVE signs).
2. Verify the projector's outbox consumption is idempotent (replaying an event does not double-post) and what happens if the projector is down at day close.
3. Day close: what blocks it (open shifts, unposted nights, pending adjustments), what a closed date refuses, whether "adjustment-only after close" holds everywhere (grep for commands that accept a past business date).
4. Shared till/drawer: refund shift rule, count sheet, variance posting, maker-checker amounts.
5. Known gaps to confirm or refute from HANDOFF: Accounting does not book `GroupReceiptRecorded`/`GroupAttritionPosted`; the group master folio takes no cash; the cash book has no export.
Output: `agents\A03b_cashbook_accounting.md`.

### A4 — Payroll and statutory (Opus; may take the test lock for single projects)
Inputs: `src\Hcc.Domain\Payroll`, `src\Hcc.Application\Payroll` (incl. `PayrollService.Pay.cs`, `PayrollFiles.cs`), migrations `0900–0901`, `tests\Hcc.Payroll.Tests`, prototype `src\domain\streams\payroll*.ts`, `tests\stream-payroll-scc.mjs`, SCC rule sources at `E:\Projects\HCC\Migration data on 25 oct\HCC-Migration-2026-10-25\reference-inputs\SCC-modules`, `CA_Question_Pack_2026-10-25.md` P1–P17 and C1–C7, DEC-056, DEC-061.
1. Recompute one monthly payslip and one daily-wage payslip by hand (own script): basic/HRA/washing/special split, PF 12 %/13 % capped at ₹15,000, ESIC 0.75 %/3.25 % with the Apr–Sep / Oct–Mar period rule, Maharashtra PT slabs incl. February, overtime per minute, advance recovery not exceeding net, F&F with the whole advance. Compare with the server's figures (tests or a probe through the kernel).
2. For every rate or rule, mark: configurable dated setting (where) / hard-coded (file:line) / absent. Map each to its CA question id. Produce the table "rules that are still guesses" with the code line each depends on and what changes if the CA answers differently (setting change vs code change).
3. Lock/unlock, bank file, PF/ESIC/PT statement: formula-guarded CSV, DRAFT footer, masked account numbers, who may unlock, audit of unlock.
4. DEC-061 own-pay visibility: show the server read gate and the prototype `can()` path; confirm Housekeeping and Maintenance see only their own pay and never hotel money (also list for A5 the PERM ids you touched).
5. Note what is not calculated (gratuity, bonus, notice pay, TDS) and whether the UI says so.
Output: `agents\A04_payroll_statutory.md`.

### A5 — Route → permission → role table (Sonnet)
Inputs: `08 Production\src\Hcc.Api\Endpoints\*.cs`, `HccComposition.cs`, `MapHccSecured` / `RequireHccSession`, `IAccessPolicy` calls, all `*_rbac_seed.sql` migrations (`0101`, `0901`, `1001`, `1101`, `2001`, `2101`, `2201`, `2301`, `2401`, `2501`, `2701`), `src\Hcc.Domain\Access\FieldMasks.cs`, the route table in `Security_Review_2026-10-26.md`, `02 Planning\HCC_RBAC_Matrix_v1.md`, `HCC_RBAC_Matrix_v2_Proposal.md` (D1–D7), DEC-057/058/062/063, `03 Prototype\src\clients\sync\capabilities.ts` and `reads.ts`, `tests\_role-reads.json`.
1. Script the enumeration (`probes\A05_routes.mjs` or `.ps1`): every `MapGet`/`MapPost` → path, method, session required?, PERM id(s) checked in the handler (or "session-only" / "anonymous"). Expect 291 map calls; reconcile with the review's 373 routes (explain the difference, e.g. route groups and helpers).
2. Join with the seeds: PERM → roles granted. Join with matrix v1 and the v2 proposal: Match / Matrix says no but code grants / Matrix says yes but code refuses / Not in matrix.
3. Mechanical sweeps: routes returning money fields reachable by HK or MNT roles (list the money fields by DTO); every route a Tech-admin role can call that touches money roles' credentials or money data (DEC-057/058); maker-checker: commands that need a second approver and whether the same principal can do both; field masks applied on every DTO that carries ID numbers or phones; PIN sign-in routes require `X-HCC-Device` and the device is bound to a property (DEC-062).
4. Prototype side: `PERMISSION_MAP` completeness (server PERM ids without a prototype permission, and vice versa) and `reads.ts` per-role gating vs `_role-reads.json`.
Output: `agents\A05_access_table.md`: tables only, with a "Needs judgement" column for the lead. No severity assignment except for hard mismatches (Matrix says no, code grants = High).

### A6 — Security follow-up (Fable subagent)
Inputs: `Security_Review_2026-10-26.md` (all 22 findings F-H01…F-L11 plus F-I01…I06), commits `779c79d`, `4b70254`, `08fd127`, `621cb6a`, the Wave 4 S3 deployment commit, `src\clients\sync\{runtime,cache,seal,auth,apk,instance}.ts`, `SqlSessionService.cs`, `AccessEndpoints.cs`, `HccHosting.cs`, `HccErrorMiddleware.cs`, `WebBookingEndpoints.cs`, `ImportEndpoints.cs`, `GuestIdEndpoints.cs`, `DocumentsEndpoints.cs`, file-download routes, `deployment\windows\*.ps1`, `04 Android` manifest, gradle and network config.
1. For each of the 22 findings: quote the review's file:line, show the current code at the fixed location, state Fixed / Partly / Not fixed / Regressed with the test that pins it (name it) or a probe. Treat F-M03 (forced credential change after admin reset) as known open and say what an attacker holding Tech admin can still do.
2. New in Wave 4, look for: cache key derivation and lock record (can a second user open the first user's sealed cache? what is the KDF input?), token storage in Capacitor Preferences (sealed how, where the wrapping key lives), 401 handling, idle/absolute expiry enforced on the server not only the client, `X-HCC-Device` spoofing (what binds it), public booking listener and rate limiter (in-memory, resets on restart, per-address behind a proxy), CSV import (formula injection on the way *in*, size limits, path handling), file downloads (path traversal, content-disposition), guest-ID reveal replay (F-L04 follow-up), CSP headers from N3, generic 5xx not leaking in other middleware, health endpoint minimal.
3. Dependency check: `third-party\packages.json`, `Directory.Packages.props`, `package-lock.json` for known-vulnerable versions (`npm audit --omit=dev` and `dotnet list package --vulnerable` are read-only and allowed).
Output: `agents\A06_security_followup.md` with severities. Reproductions only against a local `HccTest_*` database or the fake server harness; never against a running service you did not start.

### A7 — Data protection and legal note (Fable subagent)
Inputs: DEC-068, `src\Hcc.Application\GuestIds` (+ its README legal note), `GuestIdProtector.cs`, `DpapiGuestIdKeyRing.cs`, `Documents\DocumentService.cs`, `SimulatedIdOcrEngine`, migrations `0601`, `2100`, `RetentionWorker.cs`, `FieldMasks.cs`, `SqlCommandRunner.cs` (audit, outbox and idempotency payloads), logging configuration, `src\clients\sync\{guestid,guestid-ui,cache}.ts`, export code (`VoucherExport`, `PayrollFiles`, `ImportMasters`, invoice renderer, CSV helpers), `05 Delivery Evidence\**\*.png` and `.md`, `New-HccGuestIdKey.ps1`, `Backup-HccDatabase.ps1`, CA pack L1–L2.
1. Trace an ID number end to end: capture → seal (AAD = property + guest + key; confirm) → store → masked read → audited reveal → messages → exports → backup. At each hop: can plaintext land in logs, audit text, outbox payload, idempotency request snapshot, server error responses, the client read cache, IndexedDB, screenshots, CSVs, Tally XML, invoice HTML?
2. Grep the evidence folder and the repo for 12-digit Aadhaar-shaped numbers, PAN patterns and passport patterns (fictional data is fine, but note the path by which a *real* install would carry such values into screenshots and evidence).
3. Key management: where the DPAPI keys live, what happens on a new machine or after a restore (is a restored backup readable? is the key backed up? F-M05 follow-up), rotation path, retention (`documents.retentionDays` 90; is the purge job running?).
4. Write the owner's legal-risk note in plain English (half a page): what is stored, who can see it, the Aadhaar Act / DPDP Act 2023 position for a hotel storing full Aadhaar numbers against advice, what would reduce exposure (last-4 plus sealed image, or no storage), and the decision the owner must record. Mark clearly that this is not legal advice and that CA/lawyer question L1 is open.
Output: `agents\A07_data_protection.md`.

### A8 — Architecture and test quality (Opus; may take the test lock for single projects)
Inputs: `WAVE1_PARALLEL_RULES.md`, `HCC_Production_Contracts_and_Schema_v1.md`, `Hcc.slnx`, project references, `MigrationRunner.cs`, `MigrationRunnerTests`, all 47 migrations, `SqlCommandRunner.cs`, `ICommandRunner` usages, `*Endpoints.cs`, all `Sql*Store.cs`, `tests\**`.
1. Layering: project references point only downward (Contracts ← Domain ← Application ← Infrastructure ← Api); list violations. Module boundaries: for every `Sql*Store.cs`, the schemas it writes (`INSERT/UPDATE/DELETE/MERGE … <schema>.<table>`) vs the schema its module owns; list cross-module writes.
2. Migrations: block numbering vs the ownership table, the "applied migrations are never edited" rule (compare the SHA-256 journal expectation with git history: `git log --follow` on `0001`/`0002` after first application), `GO` batch and transaction discipline, FK direction across modules (the platform test exists: name it and say what it does not cover).
3. Commands: every `POST /commands/*` goes through `ICommandRunner` (grep for handlers that write without it); idempotency-key scope (per principal? per property?); audit row mandatory (the test "Command without audit is refused and rolled back": confirm it exercises the real runner); outbox in the same transaction; `expectedVersion` honoured on every mutable aggregate (list the ones that accept writes without a version).
4. Concurrency: the allocator-release deadlock noted for W3-7, `UPDLOCK` ordering in stock, the invoice number lock, shift/drawer row locks; projector workers polling every 2 s (load on SQL with three workers), restart behaviour.
5. Test quality: sample 40 tests across 10 projects; classify as behaviour-asserting (independent expected values) vs mirror (expected value computed by the code under test, or trivially true) vs smoke. Note the ratio and any project with no SQL integration test. Check the golden JSON is regenerated from the prototype and not hand-edited (`git log` on it).
6. Dead code and duplication: unused DTOs/commands; logic that exists in both the prototype `src\domain` and the server `Hcc.Domain` with different results (pick GST, rounding, availability, yield rounding).
Output: `agents\A08_architecture_tests.md`.

### A9 — Production readiness and ETP stand-ins (Opus; may take the test lock)
Inputs: `deployment\windows\*` (INSTALL.md, all `.ps1`, `installer\`, samples, `tests\`), `HccHosting.cs` (config validator), `Program.cs -- migrate`, `SetupEndpoints.cs` and the first-time setup flow, `deployment\import-templates\*`, `ImportEndpoints.cs` / `ImportMasters.cs`, `src\clients\sync\{runtime,cache}.ts` (read-only/offline), `ETP_Reuse_Pending.md` (38 rows), `RetentionWorker`, the projector workers, logging config, `W4_E2E2_Report.md`.
1. Walk `INSTALL.md` step by step as if on a clean hotel PC: prerequisites, service account, CA and server certificate, guest-ID key, backup key, encrypted backups, Task Scheduler registration, restore drill, web root served by the API, APK server address. For each step: script exists / tested by `deployment\windows\tests` / only documented / missing. Flag anything that needs admin rights without saying so, any hard-coded path, any step that would silently use `TrustServerCertificate`.
2. Backups: walk `Backup-HccDatabase.ps1` and `Restore-HccDrill.ps1` **on paper**; if they accept a database-name parameter, dry-run against a throwaway `HccTest_drill_*` database (take the test lock) and record the output; confirm encryption (F-H03 fix), the retention arithmetic, and that a restore on a *different* machine can open guest IDs (DPAPI scope).
3. Masters import: templates vs `ImportMasters` columns; a wrong row's error message; opening-balances import; idempotency of re-import.
4. Read-only/offline: what staff can do when the server is down (nothing is queued; confirm), banner wording, cache expiry.
5. Performance with a year of data: build a bounded probe (take the test lock): seed a `HccTest_perf_*` DB with FY26-scale volumes (about 1,279 bookings, 1,155 customers, daily closings for 365 days, room nights ≈ 365 × rooms) using the import or direct SQL inserts, then time the 8 heaviest reads (board, bookings list, folio, cash book range, trial balance, GSTR-1 view, customers search, reports day pack). Report ms per call and the indexes involved (`SET STATISTICS IO`). Delete the DB afterwards. If the seed cannot be built in 30 minutes, give an estimate from the query plans and say so.
6. ETP stand-ins: for each of the 38 rows: Blocks go-live / Needed in the first month / Later. Count how many are "simulated" (messaging, OCR, payment, channel manager, printing) and state what the hotel cannot do on day one as a result (no printed invoice? no KOT?).
7. Logging and monitoring: where logs go, rotation, what an operator sees when a worker dies; upgrade path (new build over old: migrate step, settings kept by the installer).
Output: `agents\A09_production_readiness.md`.

### A10 — APK static audit (Sonnet; no emulator unless the lead says so)
Inputs: `04 Android\README.md`, `android\app\build.gradle`, `src\main\AndroidManifest.xml`, `src\productionRelease\AndroidManifest.xml`, `src\productionDebug\res\xml\network_security_config.xml`, `res\xml\file_paths.xml`, `scripts\build-production.mjs`, `capacitor.config.json`, `src\clients\sync\{apk,auth,instance}.ts`, `Acceptance\APK_v11_Emulator_Report.md`, the untracked `Acceptance\v12\` screenshots, the `.apk` file names in `05 Delivery Evidence\APK`.
1. Build-types table: demo debug / productionDebug / productionRelease → `debuggable`, `allowBackup`, `usesCleartextTraffic`, `minifyEnabled`, signing-config source, `versionCode`, what the hotel CA pem is and how it is injected, whether `network_security_config` pins only the hotel CA for the server host, whether WebView remote debugging is off in release.
2. Signing: where the release keystore is expected, that it is git-ignored and absent from the repo, what happens when it is missing (`hccSigningProblem`), and that no APK in `05 Delivery Evidence\APK` is a release build (read `META-INF` / the manifest with `aapt` or `apkanalyzer` if present on PATH; otherwise write "not checked").
3. Token/PIN/device flow from code: where the token is stored (Preferences, sealed with what), device-id generation, `X-HCC-Device`, PIN roster storage (F-L09 fix), what clears on sign-out, what survives an uninstall.
4. Evidence check: v11 report defects V11-1…7 → which are claimed fixed in G7 and which the v12 screenshots show. State plainly that no v12 report exists.
Output: `agents\A10_apk_static.md`. If `emulator.lock` is granted later: run the v11 checklist on the server APK against a local HTTPS server **only after** A1 has finished and nothing else uses SQL.

### A11 — Documentation and decisions vs code (Sonnet)
Inputs: `HANDOFF.md`, `CLAUDE.md`, `MIGRATION.md`, `08 Production\README.md`, `03 Prototype\README.md`, `src\clients\sync\README.md`, `04 Android\README.md`, `deployment\windows\INSTALL.md`, `HCC_Decision_Register.md` (DEC-001…069, APR-*), `HCC_Implementation_Backlog.md`, `RBAC_Mismatch_Rulings_Proposed.md`.
1. Every file path, command, test count, migration number, permission id and endpoint named in those documents: does it exist in the code as described? Script the path checks. Produce a table of stale statements.
2. Decision Register: for each DEC/APR with a code effect, find the code (file:line) that implements it; list decisions with no trace in code and code behaviours with no decision (from the HANDOFF "owner decisions raised" lines that never got a DEC id).
3. Open owner/CA items: merge the lists from HANDOFF 12 and 12b, the backlog and the CA pack into one de-duplicated register with: item, raised where, status, which code path waits on it (A4, A3a and A9 add theirs; you provide the skeleton).
4. CLAUDE.md "Read first" and "Commands" sections: still true after Wave 4? (e.g. `SCHEMA` 8, 1008 tests, the Node 25 note).
Output: `agents\A11_docs_vs_code.md`.

### V1 — Blind re-verification (Opus, Phase 2)
Input: `probes\v1-input.md` (findings with evidence stripped). For each: reproduce from the repo alone, record what you ran or read, verdict CONFIRMED / PARTLY / NOT REPRODUCED, and if your severity differs, say why. May take the test lock for single projects. Output: `agents\V1_verification.md`.

## 6. Finding template (goes into `Audit\README.md`)

```
| ID | Sev | Area | File:line | Evidence | Impact | Fix | Hours | Conf |
|----|-----|------|-----------|----------|--------|-----|-------|------|
| A03a-01 | High | GST | Hcc.Domain/Tax/GstRules.cs:88 | probes/A03a_gst.mjs gives ₹…, server test X expects ₹… | wrong tax on … | … | 3 | High |
```

Severity meaning for this audit: **Critical** = money, tax or data loss in normal use, or an auth bypass; **High** = wrong result in a common flow, or a go-live step that cannot be done; **Medium** = wrong in an edge case, or a missing control with a manual workaround; **Low** = quality, clarity, hardening; **Info** = confirmed good, or a note. The lead may re-grade.

## 7. What the lead keeps for itself

- Reproducing every Critical and High (no exceptions), and any finding that will appear in the one-page summary.
- The readiness %, the go-live plan and its hour estimates (agents give hours per finding; the lead owns the plan).
- The owner-facing language: plain English, no agent jargon, decisions phrased as questions the owner can answer yes or no.
- Deciding whether A10 may use the emulator (only if A1 is done, SQL is idle, and the v12 gap matters for the readiness %).
- The HANDOFF.md Session-13 entry (3 lines) and the commits.

## 8. Readiness % rubric (so the number is reasoned, not felt)

Ten gates, each scored 0 / 0.5 / 1 by the lead from the agents' evidence, weighted:

| Gate | Weight | 1 means |
|---|---|---|
| Builds and tests reproduce HANDOFF (A1) | 10 | all counts match, 0 warnings, no leftover DBs |
| Daily front-desk and billing flows mapped to the server (A2/A2b) | 15 | no "blocks go-live" gap |
| Money/GST golden figures match independent recomputation (A3a/A3b) | 15 | all match; CA-pending items are settings, not code |
| Payroll correct for the rules as configured (A4) | 10 | recomputed payslips match; guesses are dated settings |
| Access control matches matrix and rulings (A5 + lead) | 10 | no "matrix says no, code grants" |
| Security review fixes hold; no new High (A6) | 10 | 21/22 fixed, F-M03 accepted as known |
| Guest-ID data cannot leak in normal operation (A7) | 10 | no plaintext path found |
| Install, backup, restore, upgrade rehearsable from scripts (A9) | 10 | every INSTALL step scripted and tested |
| APK production build hardened and evidenced (A10) | 5 | release build verified, v12 evidence exists |
| Docs and decisions consistent with code (A11) | 5 | no stale critical statement |

Readiness % = Σ(score × weight). Report the per-gate scores next to the %, and separately list the **owner decisions** and **CA answers** that cap the score regardless of code quality.

## 9. What the Opus prompt left open, now covered here

1. A serial test lane with lock files, so 13 parallel agents never run `dotnet test` at once.
2. Per-agent output files, so nothing needs merging and no agent can overwrite another's work.
3. The missing APK v12 report is treated as an unverified claim, not assumed.
4. The untracked `Acceptance\v12\` folder stays out of the audit commit.
5. A blind re-verification agent (V1) plus lead reproduction of every Critical/High, so "verify what agents claim" is a step, not a wish.
6. A readiness rubric with weights, so the % can be defended to the owner.
7. The one documentation exception (a 3-line HANDOFF entry in a separate commit) to satisfy the house rule that HANDOFF is updated at the end of every session.
8. A bounded performance probe (30-minute cap, throwaway DB, deleted afterwards) instead of an open-ended "estimate or measure".
