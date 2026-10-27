# ACC-1 lane `acc1-opening` — opening balances by hand — DONE

Branch `claude/acc1-opening` (worktree `E:\Projects\HCC\worktrees\acc1-opening`), base `39930edc`, merged `claude/acc1` at `83be423d` (the coordinator's SCHEMA 11 re-pins). Not pushed.
Contract: `02 Planning/agents/ACC1-POSTING-CONTRACT.md` §5 (opening balances), §7 (PERM-ACC-008 / 009), §8 (state, registration), §9 (migrations 4220–4224). Rules: rulebook §G2, §6, §8.1; DEC-139, DEC-148.

## Commits

| commit | what |
|---|---|
| `a8143185` | Prototype: domain file, handler, `accounting.ts` lines, registrations, production sync mappers, tests |
| `333114a7` | WIP server (committed after the PC restart): service, queries, SQL store, endpoints, guard, TB call, migrations 4220–4221 |
| `edd932b4` | Server tests (Domain + Sql), missing `using` fix |
| `2300df90` | Merge `claude/acc1` (re-pinned fixture hashes; test files only) |
| `a3a9b40d` | `tests/sync-accounting-opening.mjs` (production sync mappers) |
| (last commit) | this DONE file |

## Files

**Prototype (`03 Prototype`)**
- new `src/domain/streams/accounting-opening.ts`: `accOpening(s)`, `accOpeningVoucher(s)` (`JRN/<asOf yyyymmdd>/000`, source `{kind:'opening', id:'opening', sub:'opening-entry'}`, any status), `accOpeningPosted(s)` (only an **approved** entry posts; `[]` in production, where the server carries it), `accOpeningBalancingLine` / `accOpeningWithBalance` / `accOpeningNormalise` (the 1400 line), `accOpeningDifference`, `accOpeningTotals`, `accOpeningPending(s)`, `accOpeningLockBlocker(s, date)`, `accOpeningLocksFrom`, `accOpeningLineProblems`, `accOpeningProblems(s)`, `accFyStart`, constants `ACC_OPENING_DIFF` ('1400'), `ACC_OPENING_ACCOUNT` (code or `<code>/<party>`).
- new `src/services/mock/handlers/accounting-opening.ts`: `ACC_OPENING_SAVE {asOf, lines, note?}` (`reports` + role in `ACC_LOCK_ROLES`), `ACC_OPENING_APPROVE {}` (Owner), `ACC_OPENING_REOPEN {reason}` (Owner, no lock on or after asOf). Audit rows come from `execute()` (`ACC OPENING SAVE / APPROVE / REOPEN`).
- `src/domain/streams/accounting.ts`: one import line; `accLockBlocker` one line (after the open-day check); `accVouchersUpTo` one line (approved opening first).
- `src/services/mock/handlers/index.ts` (ACC-1 import line + `{...accountingOpening,}` in `ACC1`), `src/services/mock/index.ts` (ACC-1 export line).
- new `src/clients/sync/accounting-opening.ts` (area `accounting-opening`: three command mappers; patch loader `accountingOpeningLoader` exported but **not registered**, see Integration 4); `src/clients/sync/registry.ts` (ACC-1 import line + AREAS line).
- tests: new `tests/stream-accounting-opening.mjs` (11), new `tests/sync-accounting-opening.mjs` (3); `tests/stream-rbac-matrix.mjs`: one appended `CMDS` line for the three commands (the matrix test fails for any undocumented command, so a lane adding commands must add it).

**Server (`08 Production`)**
- new `src/Hcc.Contracts/Accounting/OpeningBalanceContracts.cs` (requests, result, `OpeningEntryView`, `OpeningBalancesStateView`).
- new `src/Hcc.Application/Accounting/OpeningBalanceService.cs`: `AccountingOpeningPermissions`, `OpeningLine` / `OpeningEntry`, port `IOpeningEntryStore`, pure `OpeningBalanceRules` (validation, 1400 line, FY start, voucher, lock blocker, pending, names), `OpeningBalanceGuard.RequireLockableAsync`, `OpeningBalanceService` (`SaveOpeningBalances`, `ApproveOpeningBalances`, `ReopenOpeningBalances`; audit `ACC_OPENING_SAVED / APPROVED / REOPENED`).
- new `src/Hcc.Application/Accounting/OpeningBalanceQueries.cs`: `GET /accounting/opening` view; `EffectiveOpeningAsync` (approved entry → else imported opening → else empty).
- new `src/Hcc.Infrastructure.SqlServer/Accounting/SqlOpeningStore.cs`.
- new `src/Hcc.Api/Endpoints/AccountingOpeningEndpoints.cs` (`AddAccountingOpeningModule`, `MapAccountingOpeningEndpoints`; works without the services line by building its own store/service per request).
- `AccountingService.cs`: optional ctor parameter `IOpeningEntryStore? openingEntries = null` + **one guard call** in `LockAsync` (after the open-day check, inside the lock transaction).
- `AccountingQueries.cs`: optional ctor parameter `IOpeningEntryStore? openingEntries = null` + **one call** (`OpeningBalanceQueries.EffectiveOpeningAsync`) replacing `opening.GetAsync` in the cumulative trial balance (early-return condition widened to "no import and no entry store").
- `HccComposition.cs`: one line in the ACC-1 block `api.MapAccountingOpeningEndpoints();`.
- migrations `4220_acc_opening_entry.sql` (`acc.opening_entry`, one row per property, FY-start / approval / reopen checks, row version; `acc.opening_line`, one side only, unique account, code or `code/party` check), `4221_acc_rbac_opening.sql` (PERM-ACC-008 → OWN, MGR, ACC; PERM-ACC-009 → OWN; both `is_money`); `migrations.lock.json` via `--add` (90 entries, in step); two lines in `Hcc.slnx`.
- tests: new `tests/Hcc.Accounting.Tests/Domain/OpeningBalanceTests.cs` (11 cases), new `Sql/OpeningBalanceSqlTests.cs` (2).

## Built against the contract

- §5 shape copied: `ACCOpening` / `ACCOpeningLine` (state `accountingX.opening`, SCHEMA 11, no new fields). One entry per property; save replaces the draft; an unbalanced draft gets the **1400 Opening-balance difference** line added (short side), recomputed on every save (a 1400 line sent back is dropped, never doubled); approval requires the entry to balance after 1400 (always true by construction, still checked) and may leave 1400 ≠ 0; the period lock of any date ≥ asOf is refused while the entry is a draft or 1400 ≠ 0 with a message starting "Opening balances pending" (prototype `INVALID_TRANSITION` through `ACC_LOCK`; server `INVALID_TRANSITION` from `LockAccountingPeriod`); journal `JRN/<asOf>/000` dated asOf, in `accVouchersUpTo` and the server cumulative TB, where the approved entry supersedes the imported opening (`opening-import` stays for the CSV path and is used when no approved entry exists).
- §7: enter = `reports` + Owner / Manager / Accountant (server PERM-ACC-008); approve and reopen = Owner (PERM-ACC-009). Reopen needs a reason (3–200 chars), sets `draft`, clears the approval, and is refused while a period lock exists on or after asOf (server keeps the reason on the row and audits it).
- Party ledger ids (`1110/CMP-…`, `1030/BA-HDFC`, `2400/LN-HDFC`) are accepted as plain strings (`^\d{4}(/[A-Za-z0-9][A-Za-z0-9._-]{0,35})?$`); 9999 and 1400 refused as entered lines; one line per ledger / party; a code not in the chart is a warning only (the §2 codes 1400 / 15xx / 2400 / 3100 are not in this branch's `ACC_CHART`; the chart lane adds them).
- Concurrency (server): save / approve / reopen and the lock guard take the application lock `acc:opening:<property>`, so a reopen cannot race a period lock.

## Tests

| suite | before | after |
|---|---|---|
| prototype `npm test` | 1867 at the wave base after the coordinator's re-pin `83be423d` (measured on the original base `39930edc`: 1854/1867, the 13 SCHEMA 10 → 11 pins) | **1881/1881** (+11 `[ACC1-OPENING]`, +3 `[ACC1-OPENING-SYNC]`) |
| server `dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1 --logger trx` | 58 (= 71 − the 13 new tests; the base project was not run separately) | **71/71** |
| `npx tsc --noEmit`, `npm run build`, `dotnet build Hcc.slnx` | — | tsc 0 errors; build OK (`dist/index.html` 3206 KB); dotnet build 0 errors, 0 warnings |

Logs (uncommitted): `E:\Projects\HCC\worktrees\acc1-opening\.scratch\` (`npm-test-baseline-clean.log`, `npm-test-2.log`, `tsc-2.log`, `build-1.log`, `dotnet-build-4.log`, `dotnet-test-1.log`, the `.trx`).

## Open items

1. **Contract vs rulebook — no entry at all.** Rulebook §G2 / DEC-148: "the first period lock refuses while 1400 ≠ 0 **or no opening entry exists**". Contract §5: "the first period lock of any date ≥ `asOf` is refused while 1400 ≠ 0 or no approved opening exists" — with no entry there is no `asOf`. Built: **no entry → locks are not gated** (otherwise every existing lock test and every day lock before the Manager enters the opening — DEC-148 says "later, once the whole software is functional" — would be refused). `accOpeningPending(s)` / `OpeningBalancesStateView.Pending` are still **true** without an entry (the TB banner says "opening balances pending"). Coordinator / owner to confirm; if locks must wait from day one, change `accOpeningLockBlocker` / `OpeningBalanceRules.LockBlocker` to refuse when the entry is absent (one line each) and decide from which date.
2. `asOf` must equal the FY start of the business date (contract: "business FY of the save date"); omitted = that date. An opening for an earlier FY is refused.
3. A draft never posts (neither prototype nor server); only the approved entry counts in balances.
4. Production screen data: the patch loader for `GET /accounting/opening` is written but not registered (Integration 4), so in the production build `accountingX.opening` stays empty until then; the commands work and the server's cumulative TB already carries the approved entry.

## Integration needed (files this lane does not own)

1. `08 Production/src/Hcc.Api/HccComposition.cs` — services (no ACC-1 block there): after `services.AddAccountingModule();` add
   `services.AddAccountingOpeningModule();                // ACC-1 opening balances (IOpeningEntryStore for the lock guard and the cumulative TB)`
   Without it the endpoints still work, but `AccountingService` gets no `IOpeningEntryStore` → **the lock is not gated in production**.
2. `08 Production/src/Hcc.Api/Endpoints/AccountingEndpoints.cs` `AddAccountingModule` — pass the store to the queries:
   `services.AddSingleton(sp => new AccountingQueries(sp.GetRequiredService<IAccountingStore>(), sp.GetRequiredService<Hcc.Application.Access.IAccessPolicy>(), sp.GetService<Hcc.Application.Import.IOpeningBalanceSource>(), sp.GetRequiredService<SqlPlatform>().Commands, sp.GetRequiredService<TimeProvider>(), sp.GetService<IOpeningEntryStore>()));`
3. `08 Production/tests/Hcc.Access.Tests/Sql/RbacV2MatrixTests.cs` `LaterPermissions` (roles OWN MGR ACC REC CSH SUP HKP MNT RST TAD) — not run by this lane, will fail on the two new permissions:
   `["PERM-ACC-008"] = ["A", "A", "A", "—", "—", "—", "—", "—", "—", "—"],`
   `["PERM-ACC-009"] = ["A", "—", "—", "—", "—", "—", "—", "—", "—", "—"],`
4. Production loader: `03 Prototype/src/clients/sync/reads.ts` READS add `'GET /accounting/opening':ACCOUNTING,`; add the route to `tests/_role-reads.json` (regenerate with `HCC_UPDATE_ROLE_READS=1 dotnet test tests/Hcc.Integration.Tests --filter RoleReadMatrix`; it answers exactly like `GET /accounting/trial-balance`, PERM-ACC-001 view); then in `src/clients/sync/accounting-opening.ts` set `loaders:[accountingOpeningLoader]` and add `reads:['GET /accounting/opening']` to `accountingOpeningLoader`, and in `tests/sync-accounting-opening.mjs` replace the last assertion (`area.loaders` empty) with a loader test (`openingFromServer` fills `accountingX.opening`).
5. `03 Prototype/src/domain/streams/accounting.ts`: `ACCSourceKind` gains `'opening'` (the opening voucher casts it today: `ACC_OPENING_SOURCE.kind as ACCSourceKind`).
6. After the chart lane: `ACC_CHART` / `ChartOfAccounts` carry 1400, 1500–1540, 2400, 3100 → the "not in the chart" warnings of the §8.1 fixture disappear; optionally switch `accOpeningLineProblems` from `accAccount` to the chart lane's `accChart(s)` so Owner-added ledgers are known.
7. After the parties lane: if `ChartOfAccounts.Find` resolves the control part of `1110/CMP-…`, `AccountingQueries.OpeningRows` will fold party opening rows into their control code in the (non-detail) cumulative TB; `OpeningBalanceSqlTests` asserts the rows `1110/CMP-DECCAN` and `2400/LN-HDFC` — change those two asserts to the control rows (`1110`, `2400`) if that happens.
8. `03 Prototype/tests/stream-rbac-matrix.mjs`: this lane appended one `CMDS` line (after `ACC_MAP_RESET`); other ACC-1 lanes adding commands append at the same place — keep every line when merging.

## HANDOFF bullet

- **ACC-1 opening balances (lane acc1-opening, DEC-139 / DEC-148, contract §5 / §7):** the Manager / Accountant / Owner enter the opening entry by hand (`ACC_OPENING_SAVE`; server `SaveOpeningBalances`, PERM-ACC-008), dated the business FY start, one line per ledger or party (`<code>/<party>`); an unbalanced draft parks the gap on **1400 Opening-balance difference**; the Owner approves (`ACC_OPENING_APPROVE` / PERM-ACC-009) and may reopen with a reason only while no period lock exists on or after the date. The approved journal `JRN/<asOf>/000` counts in `accVouchersUpTo` and the server cumulative TB (superseding the imported opening); the period lock of any date on or after the opening date is refused with "Opening balances pending" while the entry is a draft or 1400 ≠ 0 (no entry yet → locks not gated, open item 1). Migrations 4220–4221; prototype 1881/1881, Accounting.Tests 71/71. Integration: services line `AddAccountingOpeningModule`, `AccountingQueries` store argument, RBAC v2 `LaterPermissions`, READS + role matrix for `GET /accounting/opening` (`05 Delivery Evidence/Accounting/ACC1-OPENING-DONE.md`).
