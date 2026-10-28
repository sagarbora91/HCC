# ACC2-MANUAL DONE

Lane **acc2-manual** (Opus) · branch `claude/acc2-manual` · worktree `E:\Projects\HCC\worktrees\acc2-manual` · base `b78cb069` (ACC-2 wave setup).
**Coordinator rulings adopted (28 Oct, mid-lane):** (1) `ACC_MANUAL_SAVE` carries the voucher type in **`voucherType`** (never `type`, the command's own name); the server request field is `VoucherType` (the sync body sends `voucherType`). (2) `ACC_LOAN_ADD {code, name, lender, accountLast4, kind:'secured'|'unsecured'}` creates the party row **`2400/<CODE>`** (code upper-case, e.g. `2400/LN-HDFC`; without a code `LN-<lender>`); `kind` picks the group `secured-loans` / `unsecured-loans` (server `acc.loan.kind`). (3) T14b (card settlement with MDR) is a **Journal**, not a Contra — the Contra guard (cash / bank ledgers only) stays.

Contract: `02 Planning/agents/ACC2-POSTING-CONTRACT.md` §1 (source kinds, CHECK widened), §2 (manual vouchers, templates, loans), §7 (cash guard hook), §9 (PERM-ACC-010 / 011), §10 (state / migrations 4238–4241).

## Commits

```
24dc1914 ACC-2 manual: loan list order in the test (rows sorted by code)
a21d5337 ACC-2 manual: coordinator rulings (voucherType / VoucherType, ACC_LOAN_ADD 2400/<CODE> with kind secured | unsecured, acc.loan.kind), server build fixes, DONE draft
219df468 ACC-2 manual (server WIP): migrations 4238-4241, ManualVoucherService / SqlManualVoucherStore, SourceRules.Manual + mapper + projector, endpoints, tests; prototype accManualSave API
8344992a ACC-2 manual (prototype): manual vouchers (guards, templates, Owner approve / Accountant pending, void + locked-day reversal), loan party rows, posting section, sync mappers, tests
```
(+ the commit that adds this file's final counts.)

## Files

**Prototype (03 Prototype)**
- new `src/domain/streams/accounting-manual.ts` — guards (`accManualProblems`, messages `ACC_MANUAL_MSG`), `accManualSource`, `accManualPosts`, `accManualVouchersOn`, `accManualDates`, `accManualSwap`, template line builders `accManualTemplateLines` (+ labels), loans (`accLoans`, `accLoanAddPlan`, `ACCLoanLedger`), cash-guard hook `accManualCashCheck` + local stub `accManualCashGuardStub`.
- new `src/services/mock/handlers/accounting-manual.ts` — `ACC_MANUAL_SAVE`, `ACC_MANUAL_DECIDE`, `ACC_MANUAL_VOID`, `ACC_LOAN_ADD`, and the exported manual-voucher API `accManualSave(k, input)` for the tds / gst lanes.
- new `src/clients/sync/accounting-manual.ts` — four command mappers; `ACC_MANUAL_LOADER` (exported, not registered — see Integration).
- edited `src/domain/streams/accounting.ts` — own ACC-2 manual section in `accLiveVouchers` (one `out.push(...accManualVouchersOn(s,date))`), numbering type list + `'Purchase','Contra'`, `accDates` + manual dates, `accSourceOf` manual → `{}`; one import line.
- append-only blocks: `src/services/mock/handlers/index.ts` (import + `ACC2` spread), `src/services/mock/index.ts` (barrel line), `src/clients/sync/registry.ts` (import + area).
- tests: new `tests/stream-accounting-manual.mjs` (11), new `tests/sync-accounting-manual.mjs` (3); `tests/stream-rbac-matrix.mjs` CMDS + 4 rows (`ACC_MANUAL_SAVE` OWN ACC, `ACC_MANUAL_DECIDE` OWN, `ACC_MANUAL_VOID` OWN, `ACC_LOAN_ADD` OWN — the matrix test requires every registered command to be documented).

**Server (08 Production)**
- migrations `4238_acc_source_kinds_acc2.sql` (acc.source `source_kind` CHECK = contract §1 list, `voucher_type` CHECK + Purchase / Contra; drop-if-present then re-create, so it is safe after any earlier ACC-2 migration), `4239_acc_manual_voucher.sql` (acc.manual_voucher + acc.manual_line; persisted `source_key` = `manual:<id>`), `4240_acc_loan.sql` (acc.loan: `2400/LN-<code>` parties), `4241_acc_manual_rbac_seed.sql` (PERM-ACC-010 OWN + ACC, PERM-ACC-011 OWN); `migrations.lock.json` via `--add` (100 migrations, in step); four `Hcc.slnx` lines.
- new `src/Hcc.Contracts/Accounting/ManualVoucherContracts.cs` (requests, results, views, event payloads).
- new `src/Hcc.Application/Accounting/ManualVoucherService.cs` (`AccountingManualPermissions`, `ManualVoucherRules` — same guards / order / messages as the prototype, `ManualCashGuard` local stub, `IManualVoucherStore`, `ManualVoucherService`, `ManualVoucherQueries`).
- new `src/Hcc.Infrastructure.SqlServer/Accounting/SqlManualVoucherStore.cs`.
- new `src/Hcc.Api/Endpoints/AccountingManualEndpoints.cs`; `HccComposition.cs` one line in the `// ACC-2` block.
- shared files, minimal edits: `AccountingContracts.cs` (events `ManualVoucherApproved`, `ManualVoucherVoided` + `Consumed`), `AccountingSource.cs` (`SourceRules.LedgerKeyPrefix`, `ManualKey`, `Manual(...)` in an ACC-2 block), `ChartOfAccounts.Resolve` (a `ledger:<code>` key resolves to that active ledger, never through the mapping), `Vouchers.cs` `TypeOrder` + Purchase, Contra, `AccountingEventMapper.ManualVoucher`, `AccountingVoucherProjector` (two cases), `SqlAccountingStore` (`SourcesAsync` leaves out the source of a manual voucher voided on an unlocked day; `KeyUsesAsync` ignores `ledger:` keys).
- tests: new `tests/Hcc.Accounting.Tests/Domain/ManualVoucherTests.cs` (8), new `tests/Hcc.Accounting.Tests/Sql/ManualVoucherSqlTests.cs` (1 end-to-end).

## What was built

- **Save** (`ACC_MANUAL_SAVE {id?, date, voucherType, narration, reference?, lines, template?}` / `SaveManualVoucher`): Owner or Accountant (prototype `reports` + role; server PERM-ACC-010). The Owner's own voucher is `approved` at once (decidedBy = Owner, bookedOn = date; server publishes `ManualVoucherApproved`); the Accountant's is `pending`. With `id`: a pending / rejected voucher is replaced by its maker or the Owner. A bad debt (template `bad-debt` or any debit on 5300) is Owner only (A18).
- **Guards, in this order, same text both sides** (`ACC_MANUAL_MSG` = `ManualVoucherRules.Msg*`): type ∈ Journal / Contra / Payment / Receipt; template from the list; narration 3–400; reference ≤ 60; date valid, ≤ business date, not a locked day ("`<date>` is locked — unlock the day first (the Owner reopens a locked day with a reason, DEC-077)." → INVALID_TRANSITION); 2–100 lines; per line: account shape, exactly one side in whole paise > 0, ≤ ₹1,000 crore, never 9999 / 1400, never a control ledger without its party, an **active** ledger of the effective chart or an existing party ledger (masters + loans); balanced (Σ Dr = Σ Cr > 0); Contra only cash / bank (`cash`, `bank` groups and below); Payment = cash / bank on the credit side only; Receipt = on the debit side only. Approval re-runs every guard.
- **Decide** (`ACC_MANUAL_DECIDE {id, approve, reason?}` / `DecideManualVoucher`): Owner (PERM-ACC-011); reject needs a reason (3–200).
- **Void** (`ACC_MANUAL_VOID {id, reason}` / `VoidManualVoucher`): Owner; approved, non-reversal vouchers only. Unlocked day → `void`, posts nothing (prototype: dropped from the derivation; server: the append-only source is filtered out in `SourcesAsync`). Locked day → the original becomes `void` but stays in its snapshot and keeps deriving on its date (no drift), and a **reversing voucher** (lines swapped, `voidOf`, approved, dated and booked on the business date) undoes it; refused when the business date itself is locked.
- **Posting**: approved (and voided-with-reversal) vouchers are `{kind:'manual', id, sub: template ?? 'journal'}` on their date, entries as entered (prototype) / `ledger:<code>` keys + party (server), numbered with the day's vouchers at lock (`PMT/…`, `RCT/…`, `JRN/…`, `CTR/…`).
- **Loans** (`ACC_LOAN_ADD {code, name, lender, accountLast4?, kind:'secured'|'unsecured', note?}` / `AddLoan`, Owner / PERM-ACC-007): `2400/<CODE>` rows in `accountingX.chart.ledgers` (`kind:'loan'`, `lender`, `accountLast4`, group `secured-loans` / `unsecured-loans` from the command's kind) / `acc.loan` (with `kind`); templates `loan-received`, `loan-emi` post to them.
- **Templates**: `accManualTemplateLines(template, input)` builds the contract §2 lines + default type / narration (capital introduced, drawing from bank, loan received, EMI, bank charges, interest income, bad debt, PF / ESIC / PT, GST payment incl. RCM `2140 → 1340`, TDS remittance, contra).
- **Read**: `GET /accounting/manual` (PERM-ACC-001) → vouchers newest first (line names; loan names) + loans.

## Tests

All through `E:\Projects\HCC\worktreescc2	ools\Invoke-Serialized.ps1 -Label acc2-manual` (exit 75 retried); full logs in `E:\Projects\HCC\worktreescc2-manual\.scratch\` (uncommitted: `tsc.log`, `npm-test.log`, `build.log`, `acctest.log`, `trx\`).

| suite | before (base b78cb069) | after |
|---|---|---|
| prototype `npx tsc --noEmit` | 0 errors | **0 errors** |
| prototype `npm test` (whole suite) | 1948 | **1962 / 1962** (+11 `[ACC2-MANUAL]`, +3 `[ACC2-MANUAL-SYNC]`) |
| server `dotnet build Hcc.slnx` | 0 warnings | **0 warnings, 0 errors** |
| server `dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1 --logger trx` | 147 | **156 / 156** (+8 `ManualVoucherTests`, +1 `ManualVoucherSqlTests`) |

Command lines: `-Command "& '…\.scratch\proto.ps1'"` (tsc then `npm test`, in `03 Prototype`) and `-Command "& '…\.scratchoth.ps1'"` (`dotnet build Hcc.slnx` then `dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1 --logger trx`, in `08 Production`). `node tools/migrations-lock.mjs`: in step (100 migrations). FY26 pins and goldens untouched (no fixture carries a manual voucher). Other server test projects (e.g. `Hcc.Access.Tests` RBAC rows) not run — the integrator's.

## Open items

1. **Cash guards stubbed** (contract §7): prototype `accManualCashGuardStub` and server `ManualCashGuard.Check` allow everything until acc2-flows merges `accCashGuard` / `CashGuard`. The hook already finds the party and amount (cash on 1000 / 1005 in a Payment / Receipt, party = the other side's party ledger); a warning goes into the audit text. `ACCManualVoucher` has no `cashDeductionLost` field (contract §2 shape) — the flag lives only in the audit detail; coordinator to decide whether the type gains it.
2. **Rulebook B8 vs contract §2 — ruled**: B8 shows a card / UPI settlement with an MDR line as a *Contra*; the contract's Contra guard allows cash / bank ledgers only. Coordinator ruling (28 Oct): T14b is a **Journal**; the guard stays (test `[ACC2-MANUAL] guards: Contra …` refuses the MDR Contra). Rulebook B8's "Contra" wording should be amended to Journal by the coordinator.
3. **Command field name — ruled**: `ACC_MANUAL_SAVE` uses `voucherType` (server `VoucherType`). The domain input `accManualProblems` and the exported `accManualSave(k, {type, …})` API keep `type` (no command-name clash there). Contract §2 text still says `type` — coordinator to amend; acc2-ui sends `voucherType`.
4. Rulebook §6 lets the Manager "request only" a manual voucher; contract §9 gives the Manager nothing — built per the contract.
5. Rulebook B11 "loans ≥ ₹20,000 only through the bank (s.185 / s.188)" is not a contract §2 guard — not built (a candidate for the flows lane's cash guard).
6. Server narration names vouchers by number (`manual 41`, `reversal of 7`), the prototype by id (`manual MV-41`); ids differ by nature.

## Integration needed

1. **Loan names in the party list** — `03 Prototype/src/domain/streams/accounting-parties.ts` (leaf module; reads the chart rows directly):
   - in `accPartyLedgers`, after `for(const v of mnyVendors(s))add('2200',v.code,v.name,v.active);` add
     `for(const l of (s.accountingX?.chart?.ledgers??[]) as {code:string;name:string;active:boolean;kind?:string}[])if(l.kind==='loan'&&l.code.startsWith('2400/'))add('2400',l.code.slice(5),l.name,l.active);`
   - in `accPartyName`, before `default:` add
     `case '2400':return ((s.accountingX?.chart?.ledgers??[]) as {code:string;name:string;kind?:string}[]).find(l=>l.kind==='loan'&&l.code===id)?.name;`
   - server `SqlReportPartyNames.Sql`: append `UNION ALL SELECT '2400', party_id, name, NULL FROM acc.loan WHERE property_id = @p` before the `;`.
2. **Production loader**: in `src/clients/sync/accounting-manual.ts` set `loaders:[ACC_MANUAL_LOADER]` and drop the `as unknown as ReadRoute` cast; `src/clients/sync/reads.ts` add `'GET /accounting/manual':ACCOUNTING,` after `'GET /accounting/opening':ACCOUNTING,`; regenerate `tests/_role-reads.json` from the server (`HCC_UPDATE_ROLE_READS=1 … --filter RoleReadMatrix`); default fake route `GET /accounting/manual` → `{vouchers:[],loans:[]}` in `tests/_sync-harness.mjs`; update `tests/sync-accounting-manual.mjs` last test (`area.loaders` length 1, reads `['GET /accounting/manual']`). The loader is a patch that also puts the loan rows into `accountingX.chart.ledgers` — it must run **after** the chart loader (which replaces `chart`).
3. **Cash guards** (after acc2-flows): prototype `accManualCashGuardStub` body → `return accCashGuard(s,x)` mapped to `{refused, warning}` (import from `./accounting-guards`); server `ManualCashGuard.Check` → `CashGuard` with the same party / amount; add one prototype + one server test (a ₹2,00,000 cash receipt refused, a ₹12,000 cash payment warned).
4. **tds / gst lanes**: replace their local `accManualSave` stub with `import {accManualSave} from './accounting-manual'` (handlers folder). Signature `accManualSave(k:Kit, {id?, date, type, narration, reference?, lines, template?}) → {voucher, detail}`; it runs every guard and stores approved (Owner) / pending (Accountant). Server: call `ManualVoucherService.SaveAsync` or reuse `ManualVoucherRules` + `IManualVoucherStore.InsertAsync` + publish `ManualVoucherApproved` inside their own transaction.
5. **Shared lines that the purchase lane may also touch**: prototype `accLiveVouchers` numbering list `(['Sales','Receipt','Payment','Journal','Purchase','Contra'] …)` and server `VoucherBuilder.TypeOrder` (… `Journal, Purchase, Contra`) — keep one copy; migration 4238 drops and re-creates `CK_acc_source_kind` / `CK_acc_source_type` with the full contract list, so a 423x purchase migration touching them is harmless (4238 runs later).
6. **`Hcc.Access.Tests`** RBAC rows for PERM-ACC-010 (OWN, ACC) and PERM-ACC-011 (OWN) — not run by this lane (only `Hcc.Accounting.Tests`).
7. FY26 history / goldens: untouched (no manual vouchers in the fixtures); pins unchanged.

## HANDOFF bullet

- **ACC-2 manual vouchers (lane acc2-manual)**: `ACC_MANUAL_SAVE / DECIDE / VOID` + `ACC_LOAN_ADD` (prototype) and `SaveManualVoucher / DecideManualVoucher / VoidManualVoucher / AddLoan` + `GET /accounting/manual` (server; migrations 4238–4241, PERM-ACC-010 / 011): Owner approved at once, Accountant pending, every contract §2 guard with the same messages both sides, void on an unlocked day posts nothing / on a locked day a reversal on the business date, loans `2400/<CODE>` (secured / unsecured), templates; source `manual` booked through `ManualVoucherApproved`. Cash guards stubbed until acc2-flows; B8 Contra-with-MDR needs a ruling. `05 Delivery Evidence/Accounting/ACC2-MANUAL-DONE.md`.
