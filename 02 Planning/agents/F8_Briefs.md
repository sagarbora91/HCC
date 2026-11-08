# F8 briefs — wave F8 split folio + P2 reports + quick wins (8 Nov 2026)

Base: `claude/f8` **setup commit tagged `f8-setup`; hash in `05 Delivery Evidence\F8\F8-LEDGER.md`**. Contract: `02 Planning/agents/SF-CONTRACT.md`. Owner questions and the defaults the lanes build: `02 Planning/F8_Owner_Questions_2026-11-08.md`. Template: `02 Planning/HCC_Wave_Plan_F1-Fn_2026-11-02.md` §1 + Appendix A, instantiated per lane below (format of `MEAS_Briefs.md`). **No lane is launched without the owner's "go F8".** Lane worktrees `E:\Projects\HCC\worktrees\f8-<lane>` on `claude/f8-<lane>` (cut from the setup commit, each with the `03 Prototype\node_modules` junction to `worktrees\fixwave-a`). Paste one fenced block as the lane agent's prompt.

| # | Lane | Model | Worktree / branch | PORT | Migrations | Depends on | Heavy slots |
|---|---|---|---|---|---|---|---|
| 1 | sf-domain | **Opus** | f8-sf-domain / claude/f8-sf-domain | 4271 | 4350–4359 | setup | 3 |
| 2 | sf-server | Sonnet | f8-sf-server / claude/f8-sf-server | 4272 | 4360–4364 | sf-domain step 1 commit | 2 |
| 3 | sf-ui | Sonnet | f8-sf-ui / claude/f8-sf-ui | 4273 | — | sf-domain step 1 commit (names) | 2 |
| 4 | sf-tests | Sonnet | f8-sf-tests / claude/f8-sf-tests | 4274 | — | setup (names in the contract) | 2 |
| 5 | rpt-p2-a | Sonnet | f8-rpt-p2-a / claude/f8-rpt-p2-a | 4275 | 4450–4451 | F7 engine (on main) | 2 |
| 6 | rpt-p2-b | Sonnet | f8-rpt-p2-b / claude/f8-rpt-p2-b | 4276 | 4452–4453 | F7 engine | 2 |
| 7 | rpt-p2-c | Sonnet | f8-rpt-p2-c / claude/f8-rpt-p2-c | 4277 | 4454–4455 | F7 engine | 2 |
| 8 | qw-nightaudit | Sonnet | f8-qw-na / claude/f8-qw-na | 4278 | 4470–4473 | — | 2 |
| 9 | qw-noshow | Sonnet | f8-qw-ns / claude/f8-qw-ns | 4279 | 4474–4477 | — | 2 |
| 10 | qw-kds | Sonnet | f8-qw-kds / claude/f8-qw-kds | 4280 | 4478–4480 | — | 2 |
| 11 | qw-advances | Sonnet | f8-qw-adv / claude/f8-qw-adv | 4281 | — | — | 2 |
| 12 | qw-pm | Sonnet | f8-qw-pm / claude/f8-qw-pm | 4282 | 4481–4484 | — | 2 |
| — | f8-review | Sonnet (read-only) | none (reads lane branches) | — | — | DONE files | none |
| — | f8-int | **Opus** | `worktrees\f8` / `claude/f8` | 4270 | 4365–4369, 4485–4489 | all | suites + gate |
| — | f8-docs | Sonnet | HCC root after the fast-forward | — | — | gate PASS | graphify |

Sonnet 13 / Opus 2 lane agents (+ the coordinator). Phases: **day 1** sf-domain starts; when its step-1 commit lands, sf-server and sf-ui start (they `git merge claude/f8-sf-domain` at that commit); sf-tests, the three report lanes and the five quick wins start at once (≤ 11 alive). Review, integration, gate on **day 2**. What may slip without hurting go-live: every rpt-p2 lane, qw-kds, qw-pm.

Common definitions used in every brief: **INV** = `pwsh -NoProfile -File "E:\Projects\HCC\worktrees\f8\tools\Invoke-Serialized.ps1"`; exit 75 = busy → run the same call again (five times max, then DONE and stop).

## Lane: sf-domain (Opus) — PORT 4271
```
Base: claude/f8 setup commit tagged f8-setup (hash in 05 Delivery Evidence\F8\F8-LEDGER.md). Worktree E:\Projects\HCC\worktrees\f8-sf-domain, branch claude/f8-sf-domain.
Read first (only these): 02 Planning/agents/SF-CONTRACT.md (all of it — you own the money rules); 02 Planning/F8_Owner_Questions_2026-11-08.md section A; tools/README-agents.md §1–§4; 03 Prototype/src/domain/streams/folio-split.ts; the files in your ownership row. Do not read HANDOFF.md or the Decision Register: the rulings you need are cited in the contract.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only by appending inside your // F8-SF-DOMAIN block (list below). Anything else -> DONE "Integration needed" with the exact edit.
2. Every build / test / check through INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-sf-domain\<03 Prototype|08 Production>" -Label f8-sf-domain -Command "..."; exit 75 = busy -> retry the same call; never filter output; logs in E:\Projects\HCC\worktrees\f8-sf-domain\.scratch\.
3. Run only your test projects (Hcc.Billing.Tests, Hcc.Groups.Tests, Hcc.Invoices.Tests, Hcc.Accounting.Tests) and npm test; never dotnet test Hcc.slnx; no emulator.
4. Money = integer paise; GST from the dated master, per line, half away from zero; simulated clock; fictional data; Owner-only money settings (DEC-058); Housekeeping / Maintenance see no money (DEC-061); every new desktop page is a FitPage; no new npm packages; SCHEMA stays 16 (already bumped by the setup commit); never change BASE or FY26.
5. Migrations only 4350–4359: node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order "F8 sf-domain: reserved range (wave plan 1.4)" + a Hcc.slnx line inside <!-- F8-SF-DOMAIN -->; never edit an applied one.
6. STOP and write DONE if: the contract differs from the code or a DEC; an existing golden would have to change (accounting-golden.json, ACC-RULEBOOK, LF-PARITY, tests/rpt-golden, FY26 hashes, server accounting goldens); gstForFolioAdjustment gives another rate than the contract's golden expects; a test fails twice after a fix; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step (<= 1 h uncommitted); never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 90 tool turns / about 3 h; then DONE and stop.

Ownership: prototype `src/domain/streams/folio-split.ts` (add helpers, keep the setup signatures), `src/services/mock/handlers/folio-split.ts` (new: SF_FOLIO_OPEN, SF_ROUTE_SET, SF_FOLIO_REMOVE), `// F8-SF-DOMAIN` blocks inside `src/services/mock/handlers/folio.ts`, `src/services/mock/commands.ts` (routing hook), `src/domain/streams/groups.ts` (exposure), `src/domain/streams/folio.ts`, `src/domain/gstr1.ts`, `src/domain/reports/tax/shared.ts`, `src/services/mock/handlers/counter.ts` (gcrCheckout per folio), `src/services/mock/handlers/bill-correction.ts` (split refusal), `src/services/mock/handlers/stay-rate.ts` (re-post folio), `src/domain/streams/catalogue.ts` (plan line folio), `tests/stream-split-folio.mjs`; server `Hcc.Domain/Billing/FolioRouting.cs`, `Hcc.Domain/Billing/SplitFolioRules.cs`, `Hcc.Domain/Groups/CompanyCredit.cs` (folio-aware exposure, F8 block), `Hcc.Contracts/Billing/SplitFolioContracts.cs`, `Hcc.Application/Billing/SplitFolio*.cs`, the checkout path in `Hcc.Application/Billing/BillingService.cs` (F8 block), `Hcc.Infrastructure.SqlServer/Groups/SqlCompanyCreditStore.cs` (exposure SQL, F8 block), migrations 4350 / 4351, `tests/Hcc.Billing.Tests/SplitFolioTests.cs` (+ `Sql/` folio exposure tests in Hcc.Groups.Tests).
Append-only: `F8-SF-DOMAIN` in handlers/index.ts, mock/index.ts, HccComposition.cs (services + endpoints), Hcc.slnx. Forbidden: UI files (`features/**`, `mobile/**`), `SqlBillingStore.cs` and the folio-join audit (sf-server), report files other than tax/shared.ts and gstr1.ts, `types.ts`, BASE / FY26 fixtures.

Build (contract sections), in two commits at least:
- STEP 1 (first commit, <= 60 min, tag it in the ledger line of your DONE): migrations 4350 + 4351 exactly as contract §2.3; `FolioRouting.cs`, `SplitFolioRules.cs`, `SplitFolioContracts.cs` (§2.4); prototype handler stubs with the final command names and payload shapes (§4) and the helper `sfCompanyLines(s,b,companyId)` (§8 R8). sf-server and sf-ui merge this commit and start.
- STEP 2: §3 routing (prototype hook in commands.ts; stamping of existing lines at the first SF_FOLIO_OPEN; explicit folio from PAYMENT / CHARGE / DISCOUNT / COMPANY_CREDIT_REQUEST / CREDIT_NOTE / CHECKOUT; receipt-kind table; plan lines with their night; rate re-post on the original folio; bill-correction refusal); §4 command rules and the SF_* reasons; §5 checkout settles every folio and allocates invoice numbers in letter order, ₹0 folio gets none, Booking.invoiceNo = first; gstr1.ts / tax/shared.ts enumerate through sfInvoiceDocs; §6 credit note per folio; §7 D7 per company folio (exposure, the four checks, night posting never refused, Manager ₹10,000 per company folio) on both twins; server SplitFolio commands + checkout orchestrator (all folio invoices in one transaction via the existing issuer).
- Invariance (I1): a single-folio stay produces byte-identical state / server rows to today; prove it with the unchanged goldens and a dedicated test.

Definition of done: the golden stay of contract §5.7 (both twins, in your own tests; sf-tests builds the scenario) — two invoices, two GST documents, Σ = ₹11,300, accounting as listed; variants a–c; every SF_* refusal; D7 checks incl. "night posting is not refused"; DEC-061 nothing new shown to Housekeeping / Maintenance. Tests tests/stream-split-folio.mjs ([F8-SF-DOMAIN] names) and SplitFolioTests.cs; npx tsc --noEmit, npm test and your server test projects green; counts before / after recorded.

Test commands:
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-sf-domain\03 Prototype" -Label f8-sf-domain -Command "npx tsc --noEmit"
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-sf-domain\03 Prototype" -Label f8-sf-domain -Command "npm test"
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-sf-domain\08 Production" -Label f8-sf-domain -Command "dotnet test tests/Hcc.Billing.Tests -maxcpucount:1 --logger trx"
  (same for Hcc.Groups.Tests, Hcc.Invoices.Tests, Hcc.Accounting.Tests)

DONE file: 05 Delivery Evidence/F8/F8-SF-DOMAIN-DONE.md (template tools/README-agents.md §4; add the STEP 1 commit hash on its own line). Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hashes (step 1 and last), counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Lane: sf-server (Sonnet) — PORT 4272
```
Base: claude/f8 setup commit tagged f8-setup; then git merge the sf-domain STEP 1 commit (hash in F8-LEDGER.md; wait for it). Worktree E:\Projects\HCC\worktrees\f8-sf-server, branch claude/f8-sf-server.
Read first (only these): 02 Planning/agents/SF-CONTRACT.md §1, §2.3, §2.4, §3 (server mechanism), §5.5, §5.6, §8 R10, §10; tools/README-agents.md §1–§4; the files in your ownership row.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only inside your // F8-SF-SERVER block. Anything else -> DONE "Integration needed" with the exact edit.
2. Every build / test through INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-sf-server\<03 Prototype|08 Production>" -Label f8-sf-server -Command "..."; exit 75 -> retry; never filter output; logs in .scratch\.
3. Run only Hcc.Billing.Tests, Hcc.Invoices.Tests, Hcc.Reporting.Tests (documents) and npm test; never dotnet test Hcc.slnx; no emulator.
4. Money = integer paise; you change WHERE a charge is stored, never its amount, tax, date or kind; simulated clock in the prototype; fictional data; DEC-061; no new npm packages; SCHEMA stays 16.
5. Migrations only 4360–4364 (indexes / query support): --add --accept-out-of-order + Hcc.slnx line in <!-- F8-SF-SERVER -->.
6. STOP and write DONE if: a posting amount, a voucher, an accounting mapping or a GST figure would change; the DDL of 4350/4351 does not fit (tell sf-domain through DONE, do not edit it); a test fails twice; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step; never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 70 tool turns / about 2 h 30; then DONE and stop.

Ownership: `Hcc.Infrastructure.SqlServer/Billing/SqlBillingStore.cs` (folio by seq, InsertChargeAsync routing through FolioRouting.Resolve, folio list / summary queries), `Hcc.Application/Billing/BillingPorts.cs` (`NewCharge.ExplicitFolio = false` optional member; folio read ports), `Hcc.Infrastructure.SqlServer/Invoices/SqlInvoiceStore.cs` (`FolioPartyAsync` from payer_* columns), `Hcc.Reporting/Documents/DocumentSql.cs` (document per folio), the **folio-join audit**: every `bill.folio` query outside sf-domain's files (~70 references in 31 files at the setup commit; classify (a) / (b) / (c) per contract §8 R10 and fix (b) and (c) — list every file and the class in your DONE), `Hcc.Api/Endpoints/SplitFolioEndpoints.cs` (OpenFolio / SetFolioRouting / RemoveFolio over sf-domain's application services; GET /api/v1/folios/{reservationId} → FolioSummaryDto[]), prototype sync mappers `src/clients/sync/money*.ts` + `src/clients/sync/money/wire.ts` (folio letters ↔ folio_seq, SF_* commands to the server commands, folio summaries into Booking.folios), tests `tests/Hcc.Billing.Tests/Sql/SplitFolioSqlTests.cs`, `tests/Hcc.Invoices.Tests/SplitFolioInvoiceTests.cs`.
Append-only: `F8-SF-SERVER` in clients/sync/registry.ts, HccComposition.cs (services + endpoints), Hcc.slnx. Forbidden: `Hcc.Domain/**` (sf-domain), `CompanyCreditRules`, `SqlCompanyCreditStore`, accounting projectors, prototype handlers, UI.

Build: store + endpoints + DTO mapping exactly per the contract; a reservation with only folio_seq 1 takes no new query path; invoice per folio through the existing FolioInvoiceSource (buyer = payer); one invoice per folio (UX_ivc_invoice_source) proven by a test; the audit tests: a split stay (two folios) does not duplicate a row in any list you fixed (arrivals / in-house / guest register / movement register / revenue-by-category / messaging data / customer store / closing store / catalogue store).

Definition of done: SQL round trip on a fresh HccTest_* database: open a company folio, post room + extras, charges land on the routed folio_id, statement per folio, checkout issues two invoices with the right buyers, a single-folio stay unchanged; audit list in DONE; tests green; counts recorded.

Test commands:
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-sf-server\08 Production" -Label f8-sf-server -Command "dotnet test tests/Hcc.Billing.Tests -maxcpucount:1 --logger trx"   (same for Hcc.Invoices.Tests, Hcc.Reporting.Tests)
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-sf-server\03 Prototype" -Label f8-sf-server -Command "npx tsc --noEmit"   and   "npm test"

DONE file: 05 Delivery Evidence/F8/F8-SF-SERVER-DONE.md. Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: sf-ui (Sonnet) — PORT 4273
```
Base: claude/f8 setup commit tagged f8-setup; then git merge the sf-domain STEP 1 commit (hash in F8-LEDGER.md). Worktree E:\Projects\HCC\worktrees\f8-sf-ui, branch claude/f8-sf-ui.
Read first (only these): 02 Planning/agents/SF-CONTRACT.md §2.1, §2.2, §4, §5.6; 02 Planning/agents/FIT_Component_Guide.md; tools/README-agents.md §1–§4; the files in your ownership row.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only inside your // F8-SF-UI block. Anything else -> DONE "Integration needed".
2. Every build / test / check through INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-sf-ui\03 Prototype" -Label f8-sf-ui -Command "..."; exit 75 -> retry; never filter output; logs in .scratch\.
3. Run npm test, tsc, npm run build and the fit check of your pages only; no server work; no emulator.
4. UI only: you call the commands sf-domain defines, you never compute money yourself (use totals(sfView(b,f))); DEC-061 (no money for Housekeeping / Maintenance); every desktop page / tab a FitPage / FitTabs, never a smaller font; phone 48 px targets, 16 px text, cards, no sideways scroll at 360 px; colours as tokens (CLAUDE.md "Colours are tokens"); no new npm packages; SCHEMA stays 16.
5. No migrations.
6. STOP and write DONE if: a command or helper you need is missing or behaves differently from the contract; a test fails twice; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step; never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 60 tool turns / about 2 h; then DONE and stop.

Ownership: `src/features/folio/Split*` (new), the F8-SF-UI block in `src/features/folio/Folios.tsx` and `src/features/folio/modals.tsx` (folio switcher, per-folio actions), `src/mobile/money/Folio.tsx` + `Checkout.tsx` (F8-SF-UI blocks: chips per folio, one settle-all sheet), the check-in routing editor block in the arrivals / check-in dialog (find the CHECKIN dialog in `src/features/reservations/` — one F8-SF-UI block), `src/domain/reports/documents/folio.ts` (print from sfView, one document per folio), `tests/stream-split-ui.mjs`.
Append-only: `F8-SF-UI` in src/app/subscreens.ts and src/app/registry.ts. Forbidden: handlers, domain money files, server.

Build: folio switcher on the stay (desktop FitTabs strip "A · Guest", "B · Deccan Agro (company)" with balance per folio; phone chips); "Split bill" dialog (payer kind guest / company / Udhaar, company picker with "₹ left" from grpCredit, routes room / extras, reason) calling SF_FOLIO_OPEN / SF_ROUTE_SET / SF_FOLIO_REMOVE; routing editor at check-in; every folio action (payment, discount, charge, company credit, credit note) passes `folio`; check-out shows ONE sheet that settles every folio and then lists the invoice numbers; invoice list per folio on a departed stay; SF_* refusals shown with their message. A single-folio stay looks exactly as today (no tab strip).

Definition of done: tests/stream-split-ui.mjs ([F8-SF-UI] names: switcher appears only on split stays, each action carries the folio, check-out sheet totals = Σ folio balances, DEC-061 walk); fit check of `folios` at the five viewports (PORT=4273); tsc, npm test, npm run build green; counts recorded.

Test commands:
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-sf-ui\03 Prototype" -Label f8-sf-ui -Command "npx tsc --noEmit"   /   "npm test"   /   "npm run build"
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-sf-ui\03 Prototype" -Label f8-sf-ui -Command "node tools/fit-screen-check.mjs --pages folios"   (PORT=4273)

DONE file: 05 Delivery Evidence/F8/F8-SF-UI-DONE.md. Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: sf-tests (Sonnet) — PORT 4274
```
Base: claude/f8 setup commit tagged f8-setup. Worktree E:\Projects\HCC\worktrees\f8-sf-tests, branch claude/f8-sf-tests.
Read first (only these): 02 Planning/agents/SF-CONTRACT.md §1, §4, §5.7 (the golden stay), §7, §8; 03 Prototype/src/fixtures/ext/lf-parity.ts + src/services/mock/lf-parity-bind.ts (the pattern of a scenario driven by real commands); tools/README-agents.md §1–§4.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only inside your // F8-SF-TESTS block. Anything else -> DONE "Integration needed".
2. Every build / test through INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-sf-tests\<03 Prototype|08 Production>" -Label f8-sf-tests -Command "..."; exit 75 -> retry; never filter output; logs in .scratch\.
3. Run only your new test files (HCC_TEST_ONLY='sf-golden' node tests/run.mjs while sf-domain is not merged), tsc, and the server test class you add; never dotnet test Hcc.slnx; no emulator.
4. Fictional data only; integer paise; you write tests and the scenario, never product code; DEC-061; SCHEMA stays 16; never change BASE or FY26.
5. No migrations.
6. STOP and write DONE if: an expected figure in contract §5.7 cannot be reached with the commands of §4 (write the figure you got); a test fails twice for a reason in your own code; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step; never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 60 tool turns / about 2 h; then DONE and stop.

Ownership: `src/fixtures/ext/sf-golden.ts` (scenario SF-GOLDEN, engine injected like LF-PARITY), `src/services/mock/sf-golden-bind.ts`, `tests/stream-sf-golden.mjs`, server `tests/Hcc.Billing.Tests/Golden/SfGoldenTests.cs` (SQL, fresh HccTest_* database), `05 Delivery Evidence/F8/SF-GOLDEN.md` (the expected figures table).
Append-only: `F8-SF-TESTS` in src/fixtures/ext/index.ts (one import + append the name to the end of `extensions`, listed in DONE) and in src/services/mock/index.ts (export of the bind). Forbidden: product code, other tests.

Build: the golden stay of contract §5.7 with every figure asserted (per-folio net, CGST, SGST, total; invoice numbers consecutive; Σ = ₹11,300 = the unsplit control; accounting lines and party ledgers; trial balance equality with the control except the receivable party); variants a–d; a D7 night-posting case (posts, high-balance red, company credit refused); GST per invoice (each invoice's tax = Σ of its own lines' tax, no recomputation); the DEC-061 walk (Housekeeper, Supervisor, Maintenance get no folio money from any selector). Tests that need sf-domain's commands are written against the contract names and are expected to fail until sf-domain merges: mark them in DONE as "green after merge", never skip or weaken them.

Definition of done: scenario registered and selectable; tests written; the parts that do not need sf-domain green; DONE lists which tests become green only after the merge. Counts recorded.

Test commands:
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-sf-tests\03 Prototype" -Label f8-sf-tests -Command "npx tsc --noEmit"
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-sf-tests\03 Prototype" -Label f8-sf-tests -Command "node --max-old-space-size=8192 tests/run.mjs"   (with HCC_TEST_ONLY=sf-golden set in a small .ps1 in .scratch)
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-sf-tests\08 Production" -Label f8-sf-tests -Command "dotnet test tests/Hcc.Billing.Tests -maxcpucount:1 --logger trx --filter SfGolden"

DONE file: 05 Delivery Evidence/F8/F8-SF-TESTS-DONE.md. Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lanes rpt-p2-a / rpt-p2-b / rpt-p2-c (Sonnet ×3) — PORT 4275 / 4276 / 4277
One brief, three instances: replace `<x>` by `a`, `b` or `c` and `<X>` by `A`, `B` or `C`. The report list per lane is the contract §12 table.
```
Base: claude/f8 setup commit tagged f8-setup. Worktree E:\Projects\HCC\worktrees\f8-rpt-p2-<x>, branch claude/f8-rpt-p2-<x>.
Your reports (SF-CONTRACT.md §12, batch <X>) — build exactly these, in this order:
  a: rpt.guests.reservations-register, rpt.guests.guest-tariff, rpt.guests.company-business, rpt.guests.userwise-activity, rpt.guests.service-lines, rpt.rooms.room-wise-revenue, rpt.rooms.rooms-left, rpt.rooms.allocation-grid
  b: rpt.money.cash-count-sheet, rpt.money.payment-corrections, rpt.money.counter-bills, rpt.money.refunds, rpt.money.discount-log, rpt.money.bank-book, rpt.money.company-credit, rpt.tax.licence-expiry
  c: rpt.acc.asset-register, rpt.people.leave-balances, rpt.stock.grn-register, rpt.rest.sales-by-item, rpt.rest.voids, rpt.admin.scheduled-runs, rpt.admin.cancelled-lines, rpt.admin.rate-sheet
Read first (only these): 02 Planning/HCC_Report_Catalogue_v1.md §1 (rules) and the entry of each of your reports (search "#### <id> "); 02 Planning/agents/F7-CONTRACT.md; 02 Planning/agents/RPT-ARCHITECTURE.md; SF-CONTRACT.md §8 R1, R2, R8 and §12; one finished F7 report on both sides as the pattern (prototype src/domain/reports/money/credit-bills.ts + server 08 Production/src/Hcc.Reporting/Reports/Money/*, tests tests/stream-rpt-money-a.mjs); tools/README-agents.md §1–§4.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only inside your // F8-RPT-P2-<X> block. Anything else -> DONE "Integration needed".
2. Every build / test through INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-rpt-p2-<x>\<03 Prototype|08 Production>" -Label f8-rpt-p2-<x> -Command "..."; exit 75 -> retry; never filter output; logs in .scratch\.
3. Run only Hcc.Reporting.Tests and your npm tests (HCC_TEST_ONLY='rpt-p2-<x>|rpt-engine|goldens' while iterating, the full npm test once at the end); never dotnet test Hcc.slnx; no emulator.
4. Money = integer paise; money flag per report and per column (catalogue §1.2) — columns removed, not blanked, without the money gate; Housekeeping / Maintenance / Stock-only never see money (DEC-061); rule V for print / export; masking §1.6; one renderer (screen = Excel = PDF from one result); phone per entry; no new npm packages; SCHEMA stays 16; never change BASE or FY26.
5. Migrations only 4450–4451 (a) / 4452–4453 (b) / 4454–4455 (c), only for an index or an RBAC row a report really needs: --add --accept-out-of-order + Hcc.slnx line in <!-- F8-RPT-P2-<X> -->.
6. STOP for a report (write it as BLOCKED in DONE with the missing capture, then continue with the next) if its data source does not exist in code — never fake data; STOP the lane and write DONE if: the catalogue entry contradicts code or a DEC; a test fails twice; exit 75 five times; a file outside your row needs a change.
7. Commit after each finished report (prototype + server + tests + golden); never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 80 tool turns / about 3 h; then DONE and stop (unfinished reports listed).

Ownership: prototype `src/domain/reports/p2-<x>/*` (new folder, one file per report), `tests/stream-rpt-p2-<x>.mjs`, `tests/rpt-golden/goldens/<id>.json` for your ids (written by node tests/rpt-golden/accept.mjs, your ids only); server `08 Production/src/Hcc.Reporting/Reports/P2<X>/*`, `08 Production/tests/Hcc.Reporting.Tests/P2<X>/*`. Generated: src/domain/reports/catalogue.json + 08 Production/tests/Hcc.Reporting.Tests/catalogue.json (node tools/rpt-catalogue.mjs; the integrator regenerates once after the merge).
Append-only: `F8-RPT-P2-<X>` in src/domain/reports/register.ts (one import per report), HccComposition.cs services block (one AddReport<…>() line per report), Hcc.slnx (migrations only), the catalogue status lines `<!-- F8-RPT-P2-<X> -->` at the end of 02 Planning/HCC_Report_Catalogue_v1.md. Forbidden: engine files (engine.ts, catalogue.ts, mask.ts, Hcc.Reporting core), other reports, MEAS reports, handlers.

Build: each report per its catalogue entry (parameters + date class, columns in order with their money flags, grouping / totals, permission, formats, phone mode, legacy equivalents in the definition), prototype selector + server IReportSelector with the same figures; for split stays follow SF-CONTRACT §8 (stay-level sums once; company-business and company-credit take only the company folio's lines on a split stay, via sfFolios / sfView until sf-domain's sfCompanyLines merges); partial entries (company-credit, asset-register, rate-sheet) become built on the engine, adapting the existing selectors (no new figures).

Definition of done per report: three-way equality (screen = Excel = PDF) on the fixture; role matrix (who sees it, who sees the money columns, DEC-061 roles refused or money-free); date-class edges; provisional marker; CSV neutralisation; golden written; server twin test. Lane done = every report done or listed BLOCKED; tsc, npm test, Hcc.Reporting.Tests green; counts recorded.

Test commands:
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-rpt-p2-<x>\03 Prototype" -Label f8-rpt-p2-<x> -Command "npx tsc --noEmit"   /   "node tools/rpt-catalogue.mjs"   /   "node tests/rpt-golden/accept.mjs"   /   "npm test"
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-rpt-p2-<x>\08 Production" -Label f8-rpt-p2-<x> -Command "dotnet test tests/Hcc.Reporting.Tests -maxcpucount:1 --logger trx"

DONE file: 05 Delivery Evidence/F8/F8-RPT-P2-<X>-DONE.md (one row per report: built / BLOCKED + reason). Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: qw-nightaudit (Sonnet) — PORT 4278
```
Base: claude/f8 setup commit tagged f8-setup. Worktree E:\Projects\HCC\worktrees\f8-qw-na, branch claude/f8-qw-na.
Read first (only these): SF-CONTRACT.md §13 (row qw-nightaudit); F8_Owner_Questions_2026-11-08.md QWQ-1; 02 Planning/agents/MEAS-CONTRACT.md §1.4 (measurements never block a close unless meas.daily.blockClose); 03 Prototype/src/domain/reports/meas-daily/night-audit.ts; src/domain/streams/closing.ts (closingPreflight); src/features/closing/DayClosing.tsx; src/domain/streams/handover.ts + src/fixtures/ext/handover.ts (template HOVT-NA-PRE); tools/README-agents.md §1–§4.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only inside your // F8-QW-NA block. Anything else -> DONE "Integration needed".
2. Every build / test through INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-qw-na\<03 Prototype|08 Production>" -Label f8-qw-na -Command "..."; exit 75 -> retry; never filter output; logs in .scratch\.
3. Run npm test, tsc, the fit check of the closing page, and Hcc.Closing.Tests; never dotnet test Hcc.slnx; no emulator.
4. No money changes; simulated clock; fictional data; DEC-061; FitPage / FitTabs, never a smaller font; phone rules; colours as tokens; no new npm packages; SCHEMA stays 16; never change BASE or FY26 (templates in the BASE fixture are NOT edited — auto-ticks are computed).
5. Migrations only 4470–4473 (production template seed, only if templates are rows on the server): --add --accept-out-of-order + Hcc.slnx line in <!-- F8-QW-NA -->.
6. STOP and write DONE if: a new line would have to block the close (QWQ-1 default: show only); preflight behaviour of an existing blocker would change; a test fails twice; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step; never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 60 tool turns / about 2 h; then DONE and stop.

Ownership: `src/domain/streams/night-audit.ts` (new, pure: naChecklist(s) → {key,label,ok,detail,blocking,link}[]), `src/features/closing/NightAudit*` (new panel), F8-QW-NA blocks in `src/features/closing/DayClosing.tsx` (panel first in Preflight) and `src/domain/streams/closing.ts` (export only, no blocker change), `src/mobile/admin/Closing.tsx` (F8-QW-NA block: phone card list), `tests/stream-night-audit.mjs`; server `Hcc.Application/Closing/NightAudit*.cs`, `Hcc.Api/Endpoints/NightAuditEndpoints.cs` (GET /api/v1/closing/night-audit), `tests/Hcc.Closing.Tests/NightAuditTests.cs`.
Append-only: `F8-QW-NA` in handlers/index.ts, mock/index.ts, clients/sync/registry.ts, HccComposition.cs, Hcc.slnx.

Build (QWQ-1 default): lines = arrivals resolved (checked in / cancelled / no-show), departures out, room nights posted, POS checks closed, cash count submitted, key cards cancelled for departed rooms (keys module), backup OK (syshealth last backup today), plus measNightAuditLines(s) as a group "Measurement exceptions"; `blocking` is true only for lines that closingPreflight already blocks; auto-tick view of HOVT-NA-PRE items na1–na5 from the same lines (display + tick through the existing run command with a note "auto" — no fixture change); server twin returns the same lines.

Definition of done: tests/stream-night-audit.mjs ([F8-QW-NA] names: every line ok / not ok at its boundary, nothing new blocks, a measurement item switched off is skipped, the HOVT-NA-PRE auto-ticks); server test; fit check of `closing` (PORT=4278); tsc, npm test green; counts recorded.

Test commands:
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-qw-na\03 Prototype" -Label f8-qw-na -Command "npx tsc --noEmit"   /   "npm test"
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-qw-na\08 Production" -Label f8-qw-na -Command "dotnet test tests/Hcc.Closing.Tests -maxcpucount:1 --logger trx"
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-qw-na\03 Prototype" -Label f8-qw-na -Command "node tools/fit-screen-check.mjs --pages closing"   (PORT=4278)

DONE file: 05 Delivery Evidence/F8/F8-QW-NA-DONE.md. Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: qw-noshow (Sonnet) — PORT 4279
```
Base: claude/f8 setup commit tagged f8-setup. Worktree E:\Projects\HCC\worktrees\f8-qw-ns, branch claude/f8-qw-ns.
Read first (only these): SF-CONTRACT.md §13 (row qw-noshow); F8_Owner_Questions_2026-11-08.md QWQ-2, QWQ-4, QWQ-7; src/services/mock/handlers/reservations.ts (BOOK restrictions(), CANCEL noShow); src/domain/streams/yield.ts (yldStayViolations, the F12-FORECAST block: yldOverbookAllowed, yldRestrictionsEnforced, yldOversold); src/domain/streams/frontdesk-master.ts (policies, fdmNoShowCharge); setting fd.noShowTime in src/domain/streams/config.ts; the channel inventory push in src/domain/streams/channels.ts; tools/README-agents.md §1–§4.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only inside your // F8-QW-NS block. Anything else -> DONE "Integration needed".
2. Every build / test through INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-qw-ns\<03 Prototype|08 Production>" -Label f8-qw-ns -Command "..."; exit 75 -> retry; never filter output; logs in .scratch\.
3. Run npm test, tsc and the server test projects you touch (Hcc.Ota.Tests / Hcc.Yield.Tests / the reservations tests); never dotnet test Hcc.slnx; no emulator.
4. **Post nothing** (DEC-MA-5b): the no-show charge is shown, never posted; simulated clock; fictional data; DEC-061; FitPage; phone rules; tokens; no new npm packages; SCHEMA stays 16; never change BASE or FY26.
5. Migrations only 4474–4477 (only if a new setting is truly needed; default none: fd.noShowTime exists): --add --accept-out-of-order + Hcc.slnx line in <!-- F8-QW-NS -->.
6. STOP and write DONE if: a posting would be needed; today's BOOK refusal / override behaviour would change (QWQ-4 default keeps it); the channel push changes at the default cap 0; a test fails twice; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step; never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 60 tool turns / about 2 h; then DONE and stop.

Ownership: `src/domain/streams/noshow.ts` (new, pure: nsDue(s) = confirmed arrivals of the business date not checked in after fd.noShowTime, with the policy charge text from fdmNoShowCharge), `src/features/reservations/NoShow*` (new list + one-click mark through CANCEL {noShow:true, reason}), F8-QW-NS blocks in `src/services/mock/handlers/reservations.ts` (CANCEL: the reason text carries the shown policy charge; nothing else), `src/domain/streams/channels.ts` (inventory push: sellable + yldOverbookAllowed), the Day closing preflight link (one line inside the F8-QW-NS block of `src/features/closing/DayClosing.tsx`, below qw-nightaudit's block), `tests/stream-noshow.mjs`; server twins: the no-show candidates query + endpoint (`Hcc.Application/Reservations/NoShow*.cs`, `Hcc.Api/Endpoints/NoShowEndpoints.cs`), the OTA inventory push cap (`Hcc.Infrastructure…/Ota/*` push, F8 block), tests in the matching test projects.
Append-only: `F8-QW-NS` in handlers/index.ts, mock/index.ts, clients/sync/registry.ts, HccComposition.cs, Hcc.slnx.

Build (QWQ-2/4/7 defaults): Due no-shows list visible after the cut-off; no automatic marking; the policy charge shown ("Policy <name>: no-show charge ₹… — not posted"); overbooking cap applied only at the channel push and only when the Owner sets yld.overbook.<type> > 0; yld.restrictionsEnforce documented as today's behaviour (no code change unless a test proves the setting is read inconsistently — then DONE).

Definition of done: tests/stream-noshow.mjs ([F8-QW-NS] names: list boundaries at the cut-off, mark = CANCEL noShow with no new line or receipt, policy text, cap 0 unchanged / cap 1 adds one room to the push, BOOK refusal unchanged); server tests; fit check of the new list (PORT=4279); counts recorded.

Test commands:
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-qw-ns\03 Prototype" -Label f8-qw-ns -Command "npx tsc --noEmit"   /   "npm test"
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-qw-ns\08 Production" -Label f8-qw-ns -Command "dotnet test tests/<Project> -maxcpucount:1 --logger trx"

DONE file: 05 Delivery Evidence/F8/F8-QW-NS-DONE.md. Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: qw-kds (Sonnet) — PORT 4280
```
Base: claude/f8 setup commit tagged f8-setup. Worktree E:\Projects\HCC\worktrees\f8-qw-kds, branch claude/f8-qw-kds.
Read first (only these): SF-CONTRACT.md §13 (row qw-kds); F8_Owner_Questions_2026-11-08.md QWQ-3; src/domain/streams/pos.ts (posKitchen, posKitchenAt, posKotLabel, POS_KOT_FLOW) and pos-master.ts (stations); src/services/mock/handlers/pos.ts (POS_KOT_STATUS); src/clients/sync/pos.ts (AdvanceKot); src/features/pos/PosPage.tsx; 02 Planning/agents/FIT_Component_Guide.md; tools/README-agents.md §1–§4.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only inside your // F8-QW-KDS block. Anything else -> DONE "Integration needed".
2. Every build / test through INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-qw-kds\<03 Prototype|08 Production>" -Label f8-qw-kds -Command "..."; exit 75 -> retry; never filter output; logs in .scratch\.
3. Run npm test, tsc, npm run build, the fit check of `kds`, and Hcc.Pos.Tests if you touch the server; never dotnet test Hcc.slnx; no emulator.
4. **No money on the KDS page** (no prices, no totals); simulated clock for elapsed time; fictional data; FitPage for the tablet, never a smaller font; phone: not offered (open on a tablet) unless trivial; 48 px bump buttons; tokens; no new npm packages; SCHEMA stays 16; never change BASE or FY26.
5. Migrations only 4478–4480 (thresholds settings seed if you add the two settings): --add --accept-out-of-order + Hcc.slnx line in <!-- F8-QW-KDS -->.
6. STOP and write DONE if: a new KOT command is needed (use POS_KOT_STATUS / AdvanceKot only); a money figure would appear; a test fails twice; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step; never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 60 tool turns / about 2 h; then DONE and stop.

Ownership: `src/features/pos/Kds*` (new page `kds` + its register file), `src/domain/streams/kds.ts` (new, pure: kdsCards(s, outlet, station) with elapsed minutes and level ok / amber / red), one F8-QW-KDS block in `src/features/pos/PosPage.tsx` ("Open kitchen display" link), `tests/stream-kds.mjs`; optional settings `pos.kds.amberMinutes` (10) / `pos.kds.redMinutes` (20) only through a lane-owned settings group block if the Administration panel needs them — else constants with the QWQ-3 defaults.
Append-only: `F8-QW-KDS` in src/app/navmap.ts (PAGE_CATEGORY 'kds':'restaurant' or the POS page's category, PAGE_BLURB, PAGE_KEYWORDS), src/app/registry.ts, src/app/subscreens.ts (only if the page has tabs), handlers/index.ts and mock/index.ts (exports), clients/sync/registry.ts (only if a loader is needed), HccComposition.cs, Hcc.slnx.

Build: station picker (stations that exist), outlet filter, KOT cards oldest first with label, table / room, items and qty, elapsed time, colour level with a text label (status colours always with text), "Ready" / "Served" bump = POS_KOT_STATUS; permission: Restaurant, Manager, Owner (the POS permission); refresh on every state change; works in production mode through the existing POS sync.

Definition of done: tests/stream-kds.mjs ([F8-QW-KDS] names: card order, elapsed / levels at 10 and 20 min, bump moves the status, no money key in any card, role gate); PAGE_CATEGORY test green (every nav page listed); fit check of `kds` at the five viewports (PORT=4280); tsc, npm test, npm run build green; counts recorded.

Test commands:
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-qw-kds\03 Prototype" -Label f8-qw-kds -Command "npx tsc --noEmit"   /   "npm test"   /   "npm run build"
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-qw-kds\03 Prototype" -Label f8-qw-kds -Command "node tools/fit-screen-check.mjs --pages kds"   (PORT=4280)

DONE file: 05 Delivery Evidence/F8/F8-QW-KDS-DONE.md. Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: qw-advances (Sonnet) — PORT 4281
```
Base: claude/f8 setup commit tagged f8-setup. Worktree E:\Projects\HCC\worktrees\f8-qw-adv, branch claude/f8-qw-adv.
Read first (only these): SF-CONTRACT.md §3 (receipt table: deposits on folio A), §13 (row qw-advances); F8_Owner_Questions_2026-11-08.md QWQ-5; src/features/closing/Cashier.tsx (CASHIER_STEPS); src/services/mock/handlers/folio.ts (PAYMENT deposit / deposit-return, REFUND_REQUEST, REFUND_EXECUTE); src/domain/streams/folio.ts (folioRefundable, folioReceiptLabel); src/domain/streams/folio-split.ts (sfReceiptFolio); tools/README-agents.md §1–§4.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only inside your // F8-QW-ADV block. Anything else -> DONE "Integration needed".
2. Every build / test through INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-qw-adv\03 Prototype" -Label f8-qw-adv -Command "..."; exit 75 -> retry; never filter output; logs in .scratch\.
3. Run npm test, tsc, the fit check of `cashier`; no server work unless DONE proves the production data lacks receipts; no emulator.
4. **Existing commands only** (REFUND_REQUEST → approval → REFUND_EXECUTE by the original tender, PAYMENT deposit-return; DEC-031: the requester never executes); integer paise; money visible only to roles that see the cashier today; DEC-061; FitPage / FitTabs; phone rules; tokens; no new npm packages; SCHEMA stays 16; never change BASE or FY26.
5. No migrations.
6. STOP and write DONE if: a new command or a new posting is needed; production mode cannot load the receipts the view needs; a test fails twice; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step; never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 50 tool turns / about 2 h; then DONE and stop.

Ownership: `src/domain/streams/advances.ts` (new, pure: advHeld(s) — one row per booking holding an advance or a refundable deposit: booking, guest (masked per role), status, arrival / departure, advance, deposit, refunded, held, age in days, folio letter on split stays, red per QWQ-5), `src/features/cashier/AdvancesHeld*` (new), one F8-QW-ADV block in `src/features/closing/Cashier.tsx` (new step 'advances-held' after 'credit-guests'), `tests/stream-advances-view.mjs`.
Append-only: `F8-QW-ADV` in src/app/subscreens.ts ({page:'cashier',id:'advances-held',label:'Advances held'}) and src/app/registry.ts (only if a modal is registered).

Build (QWQ-5 default): red when the booking is Cancelled / No show / Departed and money is still held, or the advance is older than 90 days; actions open the existing refund request and deposit-return dialogs for the row's receipt; a CSV through the existing saveCsv hook.

Definition of done: tests/stream-advances-view.mjs ([F8-QW-ADV] names: rows and figures on the fixture, red boundaries, refund path uses REFUND_REQUEST and cannot be executed by the requester, deposit row on folio A, role gate); fit check of `cashier` (PORT=4281); tsc, npm test green; counts recorded.

Test commands:
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-qw-adv\03 Prototype" -Label f8-qw-adv -Command "npx tsc --noEmit"   /   "npm test"
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-qw-adv\03 Prototype" -Label f8-qw-adv -Command "node tools/fit-screen-check.mjs --pages cashier"   (PORT=4281)

DONE file: 05 Delivery Evidence/F8/F8-QW-ADV-DONE.md. Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: qw-pm (Sonnet) — PORT 4282
```
Base: claude/f8 setup commit tagged f8-setup. Worktree E:\Projects\HCC\worktrees\f8-qw-pm, branch claude/f8-qw-pm.
Read first (only these): SF-CONTRACT.md §13 (row qw-pm); F8_Owner_Questions_2026-11-08.md QWQ-6; src/domain/streams/maintenance.ts (MNTPlan, mntPmDue, mntPlanState, mntAmcStatus, mntPmCompliance); src/features/maintenance/Maintenance.tsx (MNT_TABS, MntPmTab); the ACC-2 fixed-asset register selector (accAssetRegister in src/domain/streams/accounting*.ts); src/domain/streams/handover.ts (template save command); src/domain/streams/compliance.ts (card style only); tools/README-agents.md §1–§4.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only inside your // F8-QW-PM block. Anything else -> DONE "Integration needed".
2. Every build / test through INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-qw-pm\<03 Prototype|08 Production>" -Label f8-qw-pm -Command "..."; exit 75 -> retry; never filter output; logs in .scratch\.
3. Run npm test, tsc, the fit check of `maintenance`, and Hcc.Maintenance.Tests if you touch the server; never dotnet test Hcc.slnx; no emulator.
4. **Maintenance never sees money** (DEC-061: AMC value and asset cost columns are removed for them, not blanked); simulated clock; fictional data; FitPage / FitTabs; phone rules; tokens; no new npm packages; SCHEMA stays 16; never change BASE or FY26 (no template added to the BASE fixture).
5. Migrations only 4481–4484 (optional production seed of the CCTV / DG template): --add --accept-out-of-order + Hcc.slnx line in <!-- F8-QW-PM -->.
6. STOP and write DONE if: a new maintenance or handover command is needed; a money figure would reach Maintenance; a test fails twice; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step; never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 60 tool turns / about 2 h; then DONE and stop.

Ownership: `src/domain/streams/pm-calendar.ts` (new, pure: pmMonth(s, month) = plan occurrences per day incl. done / overdue / due, AMC expiry cards from mntAmcStatus, asset links to the register), `src/features/maintenance/Pm*` (new calendar view), one F8-QW-PM block in `src/features/maintenance/Maintenance.tsx` (a 'pm-calendar' tab after 'pm', or a toggle inside the Preventive tab — pick one, keep FitTabs ids ↔ subscreens in step), `src/features/maintenance/MaintenanceMobile.tsx` (F8-QW-PM block: phone cards), `tests/stream-pm-calendar.mjs`; the "Add standard CCTV / DG shift checklist" button (Manager) that saves a template through the existing handover template command with the QWQ-6 items.
Append-only: `F8-QW-PM` in src/app/subscreens.ts, src/app/registry.ts (if a modal), handlers/index.ts / mock/index.ts (exports only), HccComposition.cs, Hcc.slnx.

Build (QWQ-6 default): month grid (desktop) / day list (phone) of PM occurrences coloured by state with text labels; AMC cards Active / Expiring (30 days) / Expired; a click opens the work order or generates it through the existing MNT_PM_GENERATE path; the template button saves "CCTV and DG shift check" (Morning / Evening / Night: CCTV recorder running and all cameras visible, DG diesel level, DG weekly test run, battery / UPS status, fire panel normal).

Definition of done: tests/stream-pm-calendar.mjs ([F8-QW-PM] names: occurrences per month match mntPmDue / plan everyDays, overdue boundary, AMC 30-day boundary, no money key for Maintenance, the template button saves once and is idempotent); fit check of `maintenance` (PORT=4282); tsc, npm test green; counts recorded.

Test commands:
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-qw-pm\03 Prototype" -Label f8-qw-pm -Command "npx tsc --noEmit"   /   "npm test"
  INV -WorkingDirectory "E:\Projects\HCC\worktrees\f8-qw-pm\03 Prototype" -Label f8-qw-pm -Command "node tools/fit-screen-check.mjs --pages maintenance"   (PORT=4282)

DONE file: 05 Delivery Evidence/F8/F8-QW-PM-DONE.md. Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: f8-review (Sonnet, read-only) — after the lane DONE files
```
Read-only. No worktree of your own: read the lane branches with git show / git diff from E:\Projects\HCC\worktrees\f8 (git diff f8-setup..claude/f8-<lane>).
Scope: sf-server, sf-ui, sf-tests, rpt-p2-a/b/c, qw-nightaudit, qw-noshow, qw-kds, qw-advances, qw-pm (sf-domain's money is reviewed by the Opus integrator).
Read first: SF-CONTRACT.md, F8_Briefs.md (the lane's ownership row), each lane's DONE file.
Check per lane: files outside the ownership row; edits outside the lane's blocks in shared files; DEC-061 (money reaching Housekeeping / Maintenance / Stock-only, blanked instead of removed columns); FitPage / font / phone rules; colour hex in stylesheets; a report built on data that does not exist; tests that were weakened or skipped; claimed counts without a log in .scratch; migrations outside the lane's range or without the lock entry; BASE / FY26 touched.
Output: one file per lane 05 Delivery Evidence/F8/F8-review-<lane>.md (HIGH / MEDIUM / LOW findings with file:line and the rule broken). Never edit code. Commit the review files on claude/f8 only if the coordinator asks; otherwise leave them untracked and report the paths.
Caps: <= 50 tool turns. Final report: <= 120 words, counts of HIGH / MEDIUM / LOW per lane.
```

## Lane: f8-int (Opus) — integrator, `worktrees\f8` / `claude/f8`, PORT 4270
```
Worktree E:\Projects\HCC\worktrees\f8, branch claude/f8 (never main; never push). Read: SF-CONTRACT.md (all), F8_Briefs.md, every F8-*-DONE.md and F8-review-*.md, F8-LEDGER.md.
Merge order (contract §16): sf-domain → sf-server → sf-ui → sf-tests → rpt-p2-a → rpt-p2-b → rpt-p2-c → qw-nightaudit → qw-noshow → qw-kds → qw-advances → qw-pm; git merge -Xignore-cr-at-eol; migrations.lock.json union with format() from 08 Production/tools/migrations-lock.mjs; after each merge tsc + that lane's tests through INV (-Label f8-int).
Then: apply every DONE "Integration needed" item; fix review HIGH findings (MEDIUM where cheap); R6 high-balance per folio (both twins, §8); R9 study / analytics check on the golden split stay (billed ₹11,300 once); regenerate catalogue.json (both copies), feature-catalogue.json, themes.css once; I1 invariance — no golden regenerated (if one must move, STOP and write why); full npm test; npm run build + build:production; dotnet build Hcc.slnx; full serial dotnet test Hcc.slnx -maxcpucount:1 --logger trx; node "08 Production/tools/migrations-lock.mjs"; secret scan of added lines.
Migrations 4365–4369 / 4485–4489 for merge fallout only. SCHEMA stays 16 unless a lane proved a further shape change (then re-pin once, say so).
Write 05 Delivery Evidence/F8/F8-DONE.md (before / after counts per suite, what merged, owner questions still open, DEC proposals of contract §14). Then the coordinator runs the gate: pwsh -NoProfile -File "E:\Projects\HCC\worktrees\f8\tools\Start-Gate.ps1" -Repo "E:\Projects\HCC\worktrees\f8" -Wave f8 (pwsh 7). PASS → report committed; fast-forward local main only on the owner's go (git push . HEAD:main, tag wave/f8).
Caps: <= 120 tool turns. Final report: <= 150 words — head hash, counts, gate result, open items.
Commit trailer: Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Lane: f8-docs (Sonnet) — after the fast-forward
```
In E:\Projects\HCC\Hotel Control Centre (or the f8 worktree if main is not yet fast-forwarded — ask the coordinator). Never push.
HANDOFF.md new "## Session 17 … wave F8" block from F8-DONE.md (counts marked as reported by the integrator); CLAUDE.md status line (SCHEMA 16, counts, main hash, F8); Decision Register rows for contract §14 from the next free DEC number (check the register: last used DEC-276 on 8 Nov) incl. the owner answers or "default applied, owner to confirm"; Build Plan v2 W6 tick; catalogue counts addendum (<!-- F8 counts -->); memory notes hcc-project-state.md + a new hcc-f8-wave.md; then /graphify . --update --obsidian --obsidian-dir "E:\Projects\HCC\HCC Knowledge Vault" with the shrink-guard procedure (restore from graphify-out\.graphify_old.json if the write is refused; never force).
Caps: <= 60 tool turns. Final report: <= 100 words.
Commit trailer: Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```
