# ACC2-GST DONE

Lane **acc2-gst** (Opus) · branch `claude/acc2-gst` · worktree `E:\Projects\HCC\worktrees\acc2-gst` · base `b78cb069` (ACC-2 wave setup).
Scope: contract `02 Planning/agents/ACC2-POSTING-CONTRACT.md` §4 (the GST-on-advances switch only), §6, §9, §10; rulebook §4.6, §4.12, §4.15,
§5 A6 / A6a / A6b, D5, E1–E5, §G1, §8.6; DEC-131, DEC-142.

## Commits

```
9910cf7e acc2-gst: production sync area (seven commands), RBAC matrix rows, server tests GstSetOffTests (six cases) + GstMonthSqlTests
ae458259 acc2-gst: prototype tests (six set-off cases, rulebook §8.6, month lock, remittance card hook, advance GST); server GstSetOff, GstMonthService, SqlGstMonthStore, migrations 4246-4249, endpoints, projector advance-GST hook
ad0d79b5 acc2-gst: GST set-off (s.49 order), GST month, advance GST switch, month lock — prototype domain + handlers (manual-voucher save stubbed)
(+ the commit of this DONE file)
```

## Files

**Prototype (03 Prototype)**

| file | what |
|---|---|
| `src/domain/streams/accounting-gst.ts` (new) | `accGstSetoff` (s.49 / 49A / 49B / Rule 88A), `accGstSetoffEntries`, `accGstMonth`, `accGstSetoffVouchers`, `accAdvanceGstVouchers`, `accCumTaxGst`, `accAdvanceGstBps`, `accGstOnAdvances`, `accGstLiveVouchers`, month helpers (`accMonthRange`, `accMonthDates`, `accMonthOf`), set-off ids (`accGstSetoffId`, `accGstSetoffRef`, `accGstSetoffPosted`), the month lock (`accMonthLockOf`, `accMonthLocked`, `accMonthLockedMessage`, `accMonthLockBlocker`, `accMonthDaysToLock`, `accMonthPendingManual`, `accDayLockSnapshot`, `accMonthLockRequests`), D5 tables (`ACC_STAT_LEDGER`, `ACC_STAT_LABEL`, `ACC_STAT_CARD_KIND`), `ACC_GST_LEDGER` |
| `src/services/mock/handlers/accounting-gst.ts` (new) | `ACC_GST_SETOFF`, `ACC_GST_PAY`, `ACC_STAT_REMIT`, `ACC_MONTH_LOCK`, `ACC_MONTH_LOCK_REQUEST`, `ACC_MONTH_UNLOCK`, `ACC_GST_ADVANCES_SET`; **local stub `accManualSave`** (see Open items) |
| `src/clients/sync/accounting-gst.ts` (new) | production sync: the seven commands → `PostGstSetoff`, `RecordGstPayment`, `RecordStatutoryRemittance`, `LockAccountingMonth`, `RequestAccountingMonthLock`, `UnlockAccountingMonth`, `SetGstOnAdvances`; `accountingGstLoader` (GET `/accounting/gst/locks`) written, **not registered** (READS — integrator) |
| `src/domain/streams/accounting.ts` | one import line + the marked block `// ── ACC-2 gst lane …` in `accLiveVouchers` (advance-GST lines + set-off journal on its date; a retired / unknown ledger posts to 9999 with an `ledger:<code>` unmapped key) |
| `src/services/mock/handlers/index.ts`, `src/services/mock/index.ts`, `src/clients/sync/registry.ts` | one line each inside the `// ACC-2` append blocks |
| `tests/stream-accounting-gst.mjs` (new) | 20 tests `[ACC2-GST]` |
| `tests/sync-accounting-gst.mjs` (new) | 2 tests `[ACC2-GST-SYNC]` |
| `tests/stream-rbac-matrix.mjs` | CMDS: the seven commands, one line after the ACC-1 opening line (as the ACC-1 lanes did) |

**Server (08 Production)**

| file | what |
|---|---|
| `src/Hcc.Domain/Accounting/GstSetOff.cs` (new) | `GstHeads`, `GstSetOffResult`, `GstSetOff.Compute` (same order as the prototype), `Entries`, `CumTax`, ledger codes and mapping keys |
| `src/Hcc.Domain/Accounting/AccountingSource.cs` | own section `// ── ACC-2 gst lane`: `SourceRules.AdvanceGst`, `AdvanceGstReversal`, `GstSetoff`, `GstSetoffReversal` (+ sub constants) |
| `src/Hcc.Domain/Accounting/ChartOfAccounts.cs` | `// ACC-2 gst lane` block in `DefaultMapping`: `tax:output-cgst` 2110, `tax:output-sgst` 2120, `tax:output-igst` 2130, `tax:gst-input-igst` 1330, `tax:gst-cash` 1340, `tax:advance-gst` 1350, `tax:rcm-payable` 2140 (server-only keys; the prototype posts these codes directly, see Open items) |
| `src/Hcc.Application/Accounting/GstMonthService.cs` (new) | `AccountingGstPermissions` (PERM-ACC-012 / 013), `IGstMonthStore`, `IGstManualVouchers` + `UnwiredGstManualVouchers`, `IExpenseMonthLockPort` + `ExpenseMonthLockAdapter`, `IComplianceCardHook` + `ComplianceCardHook`, `GstMonthRules` (month arithmetic), `GstMonthService` (month query, locks query, the seven commands) |
| `src/Hcc.Contracts/Accounting/GstContracts.cs` (new) | requests / results / views |
| `src/Hcc.Infrastructure.SqlServer/Accounting/SqlGstMonthStore.cs` (new) | SQL store + the deposit rule's reads |
| `src/Hcc.Infrastructure.SqlServer/Accounting/AccountingVoucherProjector.cs` | own section `// ── ACC-2 gst lane`: `ReceiptRecorded` deposit / deposit-return / deposit-apply → advance-GST source (switch on), `AdvanceGstKey` |
| `src/Hcc.Api/Endpoints/AccountingGstEndpoints.cs` (new) | the seven commands + `GET /accounting/gst/month?month=`, `GET /accounting/gst/locks`; `AddAccountingGstModule()` |
| `src/Hcc.Api/HccComposition.cs` | one line in the ACC-2 block: `api.MapAccountingGstEndpoints();` |
| `database/migrations/4246_acc_month_lock.sql` | `acc.month_lock` (+ filtered unique current row), `acc.month_lock_request` |
| `database/migrations/4247_acc_gst_setoff.sql` | `acc.gst_setoff` (one current row per month); widens `CK_acc_source_kind` to the contract §1 kinds **only if `'tax'` is not already allowed** (manual lane's 4238) |
| `database/migrations/4248_cmp_profile_gst_on_advances.sql` | `cmp.profile.gst_on_advances` (+ by / utc), default 0 |
| `database/migrations/4249_acc_gst_rbac_seed.sql` | PERM-ACC-012 (OWN 'A', ACC 'R'); PERM-ACC-013 (OWN, ACC) **guarded** with IF NOT EXISTS (acc2-tds seeds it too) |
| `database/migrations.lock.json`, `Hcc.slnx` | `--add` (4 entries, lock in step, 100 migrations), four `<File>` lines |
| `tests/Hcc.Accounting.Tests/Domain/GstSetOffTests.cs` (new) | the same six cases + lines, balance, keys, sources, advance GST, month arithmetic (11 tests) |
| `tests/Hcc.Accounting.Tests/Sql/GstMonthSqlTests.cs` (new) | one end-to-end SQL test (month, set-off, payments via the port, remittance card, month lock / request / unlock, advance GST on / off) |

## What was built

**§6 set-off (E1, rulebook §4.12).** `accGstSetoff(output, credit, cashAvailable)` / `GstSetOff.Compute`: IGST credit → IGST; then to CGST / SGST where the
head's own credit falls short; then (s.49A) the rest of the IGST credit until exhausted; CGST credit → CGST then IGST; SGST credit → SGST then IGST
(only what CGST could not pay, s.49(5)(d) proviso); never CGST ↔ SGST. Output left = the net payable per head, paid from 1340 (E1: Dr output ·
Cr 1340); `challan` = payable − cash already in 1340 (≥ 0). Six hand-worked cases on both sides, case 1 = rulebook §8.6 (14,500 + 14,500, ITC 0,
TCS 100 → challan **₹28,900**, journal Dr 2110 14,500 · Dr 2120 14,500 · Cr 1340 29,000).

**`accGstMonth(s, month)` / `GstMonthService.MonthAsync`.** Flows per month from every voucher up to the month end (locked days = snapshots), the
set-off journals left out: output 2130 / 2110 / 2120 (Cr − Dr), input 1330 / 1310 / 1320 (Dr − Cr), RCM 2140, TCS (Dr 1340 of `ota` sources), 1350.
Credit and 1340 cash are carried from earlier months **net of what their posted set-offs used** (so a set-off posted late never uses a credit twice).
Rulebook month: driven T1–T3 give 14,000 + 14,000 → challan 28,000; with T4 + T19 (injected into the 29 April snapshot in the test as acc2-tests will
drive them) **14,500 + 14,500, TCS 100, challan 28,900**, set-off posted on 1 May (§8.6 "entries in May"), April's own TB keeps 2110 / 2120 at 14,500 Cr.

**`ACC_GST_SETOFF {month}`** (Owner / Accountant): the month must be over; refused in a locked month; posts on the business date: prototype
`accountingX.gstSetoffs[month] = 'gst-setoff:<month>@<date>'` and the journal is **derived** by `accLiveVouchers` on that date (`{kind:'tax',
sub:'gst-setoff'}`, Journal); same day again = "unchanged"; a later day replaces it (refused when the earlier set-off's day is locked). Server:
`acc.gst_setoff` row + an `acc.source` with the same key (append-only: a replacement books the reversal `…/reversed@<date>#<id>` and a new source;
a second set-off on one day gets `#n`); idempotency key replay returns the stored result.

**`ACC_GST_PAY {month, amount, method, challan, date, rcm?}`** → manual template `gst-payment`: Dr 1340 · Cr tender (the method's ledger on its
settles-to party, e.g. `1030/BA-HDFC`), and with `rcm` Dr 2140 · Cr 1340 (E3). **`ACC_STAT_REMIT {kind pf|esic|pt, …}`** → template
`statutory-remittance` Dr 2310 / 2320 / 2330 · Cr tender, and the month's EPF / ESIC / PT compliance card (`cmpLegacyCard` / `ComplianceRules.LegacyCard`,
the card on the following month's page) is marked **filed with the challan as evidence**, filed-on = the payment date (a card already filed or NA
is left; no firm profile → not marked, said in the audit). Guards: challan 3–40 characters, amount > 0, dated after the month and on / before the
business date, not in a locked month.

**Month lock (G1).** `ACC_MONTH_LOCK {month}` (Owner, PERM-ACC-012): refused until the month is over, the set-off is posted, no manual voucher of the
month is pending, and every day is closed with no blocking exception (the day-lock blocker incl. the ACC-1 opening gate); then it **locks every
closed-but-unlocked day** (snapshots, lock history `reason: month lock <m>`), records `monthLocks[m] = {lockedAt, lockedBy, setoff}` and sets the
SCC expense month lock (`EXP_MONTH_LOCK` figures). `ACC_MONTH_LOCK_REQUEST` (Accountant): same blockers, kept in the audit trail
(`accMonthLockRequests`; server `acc.month_lock_request`). `ACC_MONTH_UNLOCK {month, reason}` (Owner, 3–200 chars): unlock history kept,
expense month unlocked (DEC-131), **day locks stay** (the Owner unlocks a day with its own reason). Relock keeps the history.

**GST on advances (§4, A6 / A6a / A6b, DEC-142).** `ACC_GST_ADVANCES_SET {on}` (Owner money setting; server `SetGstOnAdvances`, PERM-CFG-003)
sets `CMPProfile.gstOnAdvances` / `cmp.profile.gst_on_advances` (needs a firm profile; refuses a no-op). **Off (default): nothing** — tested.
**On:** each folio deposit posts `{kind:'tax', sub:'advance-gst'}` Journal Dr 1350 · Cr 2110 · Cr 2120, cum-tax at the booked room's rate
(prototype: GST master on the booking's rate at arrival; server: the first agreed night's `tax_bps`, else the master on `nightly_rate_paise` at
arrival); a deposit returned (`deposit-return`) or applied (`deposit-apply`, acc2-flows) posts `sub:'advance-gst-reversal'` Dr 2110 · Dr 2120 ·
Cr 1350, **capped by the GST still held** for the booking / folio (an advance without GST — before the switch, or the opening advance T5a —
reverses nothing). Tested: 5 % (₹10,500 → 250 + 250), partial return (125 + 125), 18 % room (₹11,800 → 900 + 900), apply capped.

## Tests

Command lines (all through `E:\Projects\HCC\worktrees\acc2\tools\Invoke-Serialized.ps1 -Label acc2-gst`; logs in `E:\Projects\HCC\worktrees\acc2-gst\.scratch\`):
`npx tsc --noEmit` (0 errors), `npm test` (whole suite), `dotnet build Hcc.slnx`, `dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1 --logger trx`.

| suite | before (base b78cb069) | after |
|---|---|---|
| prototype `npx tsc --noEmit` | 0 errors | **0 errors** |
| prototype `npm test` | 1948 / 1948 | **1970 / 1970** (+20 `[ACC2-GST]`, +2 `[ACC2-GST-SYNC]`; FY26 pins unchanged) |
| `dotnet build Hcc.slnx` | 0 warnings | **0 warnings, 0 errors** |
| `dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1 --logger trx` | 147 / 147 | **159 / 159** (+11 `GstSetOffTests`, +1 `GstMonthSqlTests`) |
| `node tools/migrations-lock.mjs` | in step (96) | **in step (100)** |

Runs on the way: the first `npm test` failed only the `[RBAC]` matrix (the seven new commands not yet in CMDS) → rows added; the first SQL run
expected FORBIDDEN for the Accountant's month lock but the policy answers **APPROVAL_REQUIRED** (PERM-ACC-012 'R' = request only) → the test now
asserts that. Other server test projects were not run (lane rule); `Hcc.Access.Tests` needs the new rows (Integration needed 6).

## Open items

1. **Manual-voucher API is stubbed.** Prototype: `accManualSave` in `handlers/accounting-gst.ts` is a LOCAL STUB (stores the voucher in
   `accountingX.manual`, Owner → approved, Accountant → pending, balanced / date / locked-day checks only). Nothing posts it in this branch — the
   manual lane's section of `accLiveVouchers` does. Server: `IGstManualVouchers` defaults to `UnwiredGstManualVouchers` (UNAVAILABLE "not
   configured"; pending count 0). Both replaced at integration (below).
2. **Prototype set-off is live-derived, server set-off is frozen.** The prototype re-derives the journal from `accGstMonth` (a later change to
   an unlocked day of the month moves it; a locked set-off day shows the drift warning); the server stores the figures at posting time. After
   the month lock (every day locked) both are stable and equal.
3. **Tax mapping keys exist on the server only.** The prototype posts 1350 / 2110 / 2120 / 1340 … by code (no new `ACC_DEFAULT_MAP` keys, so the
   `[GOLDEN]` drift check and the mapping-row tests are untouched); the server set-off uses `tax:*` keys (defaults = the same codes) and the
   advance GST `tax:advance-gst` + `folio:cgst` / `folio:sgst`. The golden test is one-way (prototype ⊆ server), so it stays green.
4. **The switch is read live.** Flipping `gstOnAdvances` changes the derivation of every unlocked day with deposits (locked days keep their
   snapshot); the reversal cap keeps 1350 from going negative. A dated switch (`effectiveFrom`) is a possible refinement (CA / Owner).
5. **Month-lock requests** live in the audit trail in the prototype (no state field in the contract); the server has `acc.month_lock_request`.
6. **Rule 42 / 43 reversals, interest (E4) and GSTR-3B filing** are not in scope; E4 is a manual voucher (Dr 5400) with the existing templates.
7. FY26 history is untouched: nothing in the history sets `gstSetoffs` or the switch, so `accLiveVouchers` returns the same vouchers and the FY26
   pins are unchanged (the full prototype suite passes with the pins as they are).

## Integration needed

1. **acc2-manual (prototype):** in `03 Prototype/src/services/mock/handlers/accounting-gst.ts` delete the block between `// ── LOCAL STUB of acc2-manual's
   accManualSave` and `// ── end LOCAL STUB ──` and import the manual lane's save; keep the call sites' shape
   `accManualSave(k,{date,type,narration,reference,lines,template})` → `ACCManualVoucher` (adapt the import if the real signature differs — the call
   sites are `ACC_GST_PAY` and `ACC_STAT_REMIT`). The tests read `s.accountingX.manual.at(-1)` (template, status, lines) and stay valid.
2. **acc2-manual (server):** register an `IGstManualVouchers` over `ManualVoucherService` (save the draft with its template inside the caller's
   transaction; `PendingAsync` = count of `acc.manual_voucher` with status `pending` and date in [from, to]) in `AddAccountingGstModule()` /
   `HccComposition.cs` (`services.AddSingleton<IGstManualVouchers, …>()`), and add `services.AddAccountingGstModule();` next to the other modules.
3. **Day unlock inside a locked month** (prototype `handlers/accounting.ts` `ACC_UNLOCK`, after the "not locked" assert):
   `if(accMonthLocked(s,cmd.date))k.fail('INVALID_TRANSITION',accMonthLockedMessage(String(cmd.date).slice(0,7)));` (import from
   `accounting-gst`); server `AccountingService.UnlockAsync`: refuse with the same text when `acc.month_lock` has a current row for the date's month
   (`IGstMonthStore.MonthLocksAsync`).
4. **`CMP_PROFILE_SET` keeps the ACC-2 switches** (prototype `handlers/compliance.ts`, inside the `x.profile={…}` literal):
   `...(x.profile?.gstOnAdvances!==undefined?{gstOnAdvances:x.profile.gstOnAdvances}:{}),` (and the same for `hotelDeductsTds` /
   `tdsDeductorType` unless acc2-tds already does it). Today a profile save drops the switch (back to off). Server: `SaveProfileAsync` does not
   touch `gst_on_advances`, nothing to do.
5. **Sync reads:** `03 Prototype/src/clients/sync/reads.ts` READS gains `'GET /accounting/gst/locks':ACCOUNTING,` and
   `'GET /accounting/gst/month':ACCOUNTING,`; regenerate `tests/_role-reads.json` from the real server (`HCC_UPDATE_ROLE_READS=1 … --filter
   RoleReadMatrix`) and the fake routes; then put `accountingGstLoader` in the area's `loaders` (remove the `as never` cast in its `reads`) and flip
   the `[ACC2-GST-SYNC]` assertion `area.loaders.length` to 1.
6. **`Hcc.Access.Tests` RbacV2MatrixTests:** rows PERM-ACC-012 (OWN 'A', ACC 'R') and PERM-ACC-013 (OWN, ACC 'A').
7. **Migrations:** 4247 widens `CK_acc_source_kind` only when `'tax'` is missing — if the manual lane's 4238 does the same, nothing happens;
   4249 inserts PERM-ACC-013 only if acc2-tds has not. No `--accept-out-of-order` needed when merging in the brief's order (4230… → 4246…).
8. **acc2-flows:** `DEPOSIT_APPLY` creates receipt kind `'deposit-apply'` — the reversal reads exactly that kind (prototype `b.receipts`, server
   `ReceiptRecorded` payload `kind`). If the server publishes a separate `DepositApplied` event instead, call the projector's
   `AdvanceGstAsync` for it as well (same payload fields).
9. **acc2-tests:** with T4 / T19 driven, `M.accGstMonth(s,'2026-04')` gives output 14,500 + 14,500, TCS 100, `setoff.challan` 28,900;
   `ACC_GST_SETOFF {month:'2026-04'}` on 1 May posts Dr 2110 14,500 · Dr 2120 14,500 · Cr 1340 29,000.
10. **acc2-ui:** GST month tab = `accGstMonth`, `accMonthLockBlocker`, `accMonthLockRequests`, `accGstSetoffPosted`, commands above; Owner
    settings switch = `ACC_GST_ADVANCES_SET {on}` (server `SetGstOnAdvances`).

## HANDOFF bullet

- **ACC-2 gst lane (28 Oct 2026, `claude/acc2-gst`):** GST month end — s.49 / 49A / 49B set-off (six hand-worked cases on both sides incl. the
  rulebook §8.6 challan ₹28,900), `ACC_GST_SETOFF` journal derived on the posting date (server `acc.gst_setoff` + append-only source),
  `ACC_GST_PAY` / `ACC_STAT_REMIT` through the manual templates (stubbed until acc2-manual; the remittance marks the EPF / ESIC / PT compliance card
  filed with the challan), the month lock (`ACC_MONTH_LOCK` / `_REQUEST` / `_UNLOCK`, locks the days and the SCC expense month), GST on advances
  switch (off by default, DEC-142; on = Dr 1350 · Cr 2110 · Cr 2120 with capped reversals); migrations 4246–4249. `05 Delivery Evidence/Accounting/ACC2-GST-DONE.md`.
