# Next-session prompt — independent audit of HCC by Fable

Paste everything below the line into a new Claude Code session (choose the **Fable** model) in `E:\Projects\HCC\Hotel Control Centre`.

---

You are an independent auditor for **Hotel Control Centre (HCC)**, an internal hotel-operations system for the owner's hotel in India. Other Claude sessions built it over two days with many parallel agents. Your job is to find out — with evidence — what is really built, what is correct, what is risky, and what still stands between this code and running a real hotel. **Audit only: do not change product code.** You may write audit documents, throwaway probes outside the repo, and tests in a scratch folder.

## Read first
1. `CLAUDE.md`, `HANDOFF.md` (Sessions 10–12b), `MIGRATION.md`.
2. Plans and decisions: `02 Planning\HCC_Decision_Register.md` (DEC-001…069, APR-*), `HCC_RBAC_Matrix_v1.md` + `HCC_RBAC_Matrix_v2_Proposal.md`, `HCC_Business_Workflow_Spec_v1.md`, `HCC_Production_Contracts_and_Schema_v1.md`, `HCC_Implementation_Backlog.md`, agent briefs in `02 Planning\agents\` (W2, W3, W4).
3. Evidence: `05 Delivery Evidence\` — acceptance reports (W0-D, APK v11, APK v12, W4_E2E, W4_E2E2), `Security\Security_Review_2026-10-26.md`, `CA_Question_Pack_2026-10-25.md`.
4. Code: `03 Prototype\` (React 19 + TS; mock `execute()`; production sync layer `src\clients\sync\` + its `README.md`), `08 Production\` (.NET 10 + SQL Server Express, module folders, `database\migrations\`, `deployment\windows\`, `ETP_Reuse_Pending.md`), `04 Android\` (Capacitor; demo debug and server `productionDebug`/`productionRelease` build types).

## Ground rules
- Read-only on the repo except your audit outputs in `05 Delivery Evidence\Audit\`. Never push, merge, rebase, or run bare `git stash`. Keep line endings.
- SQL: disposable `HccTest_*` databases on `.\SQLEXPRESS` only; never touch `EtpReporting`, `EtpScratch_*` or a database named `Hcc`. Run .NET tests serially: `dotnet test Hcc.slnx -maxcpucount:1` (in `08 Production`).
- Node 25: put `E:\Tools\nodejs` first on PATH (`E:\Program Files\nodejs` v24 comes first otherwise). Prototype checks in `03 Prototype`: `npx tsc --noEmit`, `npm test`, `npm run build`, `npm run build:production`.
- Watch free space on E: (it filled to 0 GB once). One Android emulator at a time if you use one.
- The GitHub repo `github.com/sagarbora91/HCC` is public — do not publish anything.
- Use parallel subagents for independent audit areas (they help a lot here); use Sonnet-class agents for mechanical checks (counting, grepping, re-running tests) and keep judgement calls (money, tax, payroll law, security, architecture) for yourself or a strong model. Verify agents' claims before you put them in the report.

## What to audit (each with evidence: file:line, test name, command output, or a reproduction)
1. **Claims vs reality.** Re-run every test suite and build yourself and compare with HANDOFF's numbers (prototype 1008, server 962 in 28 projects). List every claim in HANDOFF/CLAUDE.md you could not confirm.
2. **Feature completeness.** For every prototype screen and command: is it wired to the server in production (`src\clients\sync\*` mappers/loaders) or in an `unmapped` list? Produce a coverage table (area → commands mapped / unmapped / reason) and judge whether each unmapped item blocks go-live.
3. **Money and GST correctness.** Paise everywhere, half-away-from-zero rounding, GST per DEC-069 (rooms per unit per night ≤ ₹7,500 5 % / above 18 %, restaurant 5 % or 18 %, other 18 %, CGST/SGST split, posted lines keep taxBps snapshots), folio totals, refunds, credit notes, invoice numbering (gap-free, per FY, 16 chars), company credit, group billing/attrition, cash book and drawer, day close, accounting vouchers (2110/2120), trial balance incl. opening balances, Tally export. Recompute golden figures independently.
4. **Payroll / statutory.** PF, ESIC (contribution period), PT, overtime, advances, F&F, lock/unlock, bank file, own-pay visibility (DEC-061). Flag every rate or rule that is a guess pending the CA (cross-check `CA_Question_Pack_2026-10-25.md`).
5. **Access control.** Every server route and command: permission required, matches the matrix + DEC-063 rulings; Housekeeping/Maintenance never see money; Tech admin limits (DEC-057/058); maker-checker rules; field masks; PIN + device binding (DEC-062). Sample the route table in the security review and verify, don't trust.
6. **Security follow-up.** Check each fix for the 22 findings in the security review actually works (F-H01…F-L11); look for anything new introduced by Wave 4 (sync cache, tokens, public booking listener, import CSV, file downloads, guest-ID reveal). Note the known open item: forced credential change after admin reset.
7. **Data protection.** DEC-068 (full guest IDs incl. Aadhaar stored, sealed AES-GCM, DPAPI keys): where plaintext could leak (logs, audit, outbox, idempotency records, caches, exports, screenshots in the repo). Legal risk statement for the owner.
8. **Architecture and code quality.** Layering rules (`08 Production\WAVE1_PARALLEL_RULES.md`), module boundaries (no cross-module table writes), migration block discipline and ordering, ICommandRunner/idempotency/audit/outbox use, concurrency (expectedVersion, deadlocks such as the allocator release noted in W3-7), error handling, dead code, duplicated logic between prototype and server, test quality (are tests asserting real behaviour or just mirroring code?).
9. **Production readiness.** Deployment scripts (`deployment\windows\`: install, HTTPS/CA, guest-ID key, backup key and encrypted backups, restore drill, service account, web root), first-time setup, masters import with the templates, read-only/offline behaviour, performance with a year of data (estimate or measure), backups/restore, logging/monitoring, upgrade path, ETP stand-ins still pending (`ETP_Reuse_Pending.md`).
10. **APK.** Demo vs server build types, release hardening (not debuggable, allowBackup false, signing), hotel-CA trust, token storage, PIN/device flow, evidence in `APK_v12_Emulator_Report.md`.
11. **Documentation and decisions.** Are HANDOFF, CLAUDE.md, READMEs and the Decision Register consistent with the code? Which owner/CA decisions are still open and which code paths depend on them?

## Deliverables (in `05 Delivery Evidence\Audit\`)
- `HCC_Independent_Audit_<date>.md`: executive summary in plain English for the owner (one page: what works, what doesn't, go-live readiness % with reasoning); findings table (ID, severity Critical/High/Medium/Low/Info, area, file:line, evidence, impact, recommended fix, effort hours); coverage table from item 2; golden-figure recomputations from item 3; list of unverified claims; open owner/CA decisions with the code each one blocks; a prioritised go-live plan with honest hour estimates.
- Keep evidence files (logs, probe scripts, screenshots) next to it; nothing secret.
- Commit the audit folder on a new branch `claude/audit-fable-<date>` (message ending with your Co-Authored-By line). Do not merge or push.

Finish with a short chat summary: readiness %, Critical/High counts, top 5 fixes, and the decisions the owner must make.
