# ACC-1 · lane acc1-ui — screens, desktop and phone (27 Oct 2026)

Branch `claude/acc1-ui`, base `39930edc`. Commits: `e6cb0f34` (screens, stubs, test), `08a7d9d2` (stylesheet moved to a new file, stub groups from the contract table, page guard), plus this DONE file. Screens only; no domain logic, no handler.

## Built (against contract §2, §3, §5, §6, §7)
Accounting page tabs (desktop): Vouchers · Trial balance · Statements · Day book · Chart of accounts · Parties · Opening balances · Exceptions · Reconciliation · Mapping · Tally export.
- **Trial balance** (§6): six columns (opening / period / closing, Dr and Cr), from / to range with presets, control vs "With parties" toggle, balanced pill, "Opening balances pending" note, CSV, a row opens the statement.
- **Statements** (§6): ledger / party picker with search, opening, rows with counter ledger and running balance in Dr / Cr (no negative numbers), closing, CSV, voucher link.
- **Day book** (§6): voucher rows with their ledger lines, range, CSV.
- **Chart of accounts** (§1, §2, §7): group tree (28 Tally groups + custom sub-groups), ledger rows code / name / group / nature / control / active, search, Active / Retired / All, hide empty groups, history; Owner only: add ledger, add sub-group, edit, retire / reactivate (dialogs `acc-ledger`, `acc-group`); Manager / Accountant read only.
- **Parties** (§3): grouped by control ledger with balances (as of the business date), opens the statement.
- **Opening balances** (§5, §7): entry grid (ledger / party, Dr, Cr, one side), the automatic 1400 line and the parked amount shown, status pills (Not entered / Draft / Approved + "Pending — first period lock refused"), Save draft (Owner / Manager / Accountant), Approve and Reopen (with reason) Owner only, read-only once approved.
- **Phone** (`Mobile.tsx`): view chips Day · Trial balance · Statements · Day book · Chart · Parties · Opening · Tally export; date presets + 48 px date inputs, cards not tables, `ShowMore` paging, sheets for the dialogs, one primary action in the sticky footer (Add ledger; Add line / Approve / Reopen for the opening entry).
- Dialogs registered in `register.tsx`: `acc-ledger`, `acc-group`, `acc-opening-line` (ids: code / group id / account, or `new`, `new:<group>`).
- Gating: the page and the phone page show "Not available" unless `rbacSeesBooks` (not the Cashier, DEC-079) and `permission('reports')` (housekeeping / maintenance / reception have no accounting; nav item `p:'reports'`). Chart edit = `permission('admin')` and role Owner; opening enter = `reports` and role in `ACC_LOCK_ROLES`; approve / reopen = Owner (`useAccPerms` gained `canChart`, `canOpeningEnter`, `canOpeningApprove`).

## Files
`03 Prototype/src/features/accounting/`: `Accounting.tsx`, `Mobile.tsx`, `parts.tsx`, `register.tsx` (edited); `_contract-stubs.ts`, `shared.tsx`, `books.tsx`, `chart.tsx`, `opening.tsx` (new). `src/app/navmap.ts` (keywords appended: statement, day book, chart of accounts, party, parties, opening balance(s), sub-ledger, debtors, creditors). `src/styles/m-accounting2.css` (new). `tests/stream-accounting-ui.mjs` (new, 15 tests `[ACC1-UI]`).

**Deviation:** the new rules are in **`m-accounting2.css`**, not `m-accounting.css`: the CT-B round-trip tests pin `m-accounting.css` to git 799e4cc (any added line fails three tests). `m-accounting.css` is unchanged. The `2` makes it load after `m-accounting.css` (sheets load sorted by name). Tokens only, no new colour (`themes.mjs --write` not needed).

## Stubbed names (`_contract-stubs.ts`; everything the screens import from the contract comes from this file only)
- Selectors / types: `accChart`, `accGroups`, `accPartyLedgers` (+ type `ACCPartyLedger`), `accOpening`, `accOpeningPending`, `accStatement`, `accTrialBalance6`, `accDayBook` (+ types `ACCStatementRow`, `ACCStatement`, `ACCTB6Row`, `ACCTB6` copied from §6, and `ACCDayBookRow`).
- Commands (typed shapes): `ACC_LEDGER_UPSERT`, `ACC_LEDGER_RETIRE`, `ACC_GROUP_UPSERT`, `ACC_OPENING_SAVE`, `ACC_OPENING_APPROVE`, `ACC_OPENING_REOPEN` (types `AccLedgerUpsert`, `AccLedgerRetire`, `AccGroupUpsert`, `AccOpeningSave`, `AccOpeningApprove`, `AccOpeningReopen`).
- Not in the contract, invented by the screens (rename or provide in the real lanes): `accChartHistory(s)` (newest-first `chart.history`), `accLegacyOfGroup`, CSV builders `accStatementCsv`, `accTrialBalance6Csv`, `accDayBookCsv`, re-export of `ACC_SUSPENSE`.
- Stub bodies derive plausible results from the existing `ACC_CHART` / vouchers; they are not rules.

## Assumptions to check at integration (the contract is silent)
- `ACC_LEDGER_UPSERT {code,name,group,gst?,tallyName?,note?}` (no `control` sent; Owner-added ledgers are not control ledgers); `ACC_LEDGER_RETIRE {code,active,reason?}` (`active:true` = reactivate); `ACC_GROUP_UPSERT {id?,name,parent}`; `ACC_OPENING_SAVE {asOf,lines,note?}` where `lines` are WITHOUT the 1400 line and `asOf` is the 1 April of the business year of the date (`fyStart`); the screens strip any 1400 line they receive and compute the displayed 1400 difference themselves (`openingGap` in `opening.tsx`, display only).
- `accOpening(s)` returns the stored `ACCOpening | undefined`; `accOpeningPending(s)` returns a boolean.
- `accDayBook` row = one per voucher entry `{date,no,type,narration,account,name,debit,credit}`; the screen groups them by voucher.
- Retire refusals (`MASTER_IN_USE`-style errors) are shown by the normal `run()` error path.

## Tests
Prototype: baseline 1867 → **1882** run (+15 `[ACC1-UI]`); 1869 pass, 13 fail. All 15 new tests pass; the `PAGE_CATEGORY` / navmap tests are green. The 13 failures are not from this lane and come from the wave base: SCHEMA is 11 (coordinator, contract §8) while `[S8]` expects 10, `[EXP-DEC]` asserts "SCHEMA unchanged" (11 !== 10), and the BASE / FY26 hash tests (`[CAT]`, `[FDM]`, `[HIST-A]`, `[MFX]` x2, `[MST]`, `[OPSM]`, `[PPL]`, `[PERF]`, `[POS-MST]`, `[RMS]`) pin hashes that include the schema number. The integrator or coordinator must update those pins. Logs: `.scratch/test1.log`, `.scratch/test2.log` (uncommitted). `npx tsc --noEmit` clean (re-run after the last code commit); `npm run build` OK (3253 KB). Server tests: none (UI lane).

## Open items
- Phone smoke of the accounting views at 360 px and a visual check of the desktop tabs were NOT done in a browser (session cut by the PC restart). The CSS follows the phone rules (48 px chips / date inputs / selects, 16 px text, cards) and a test asserts there is no `<table>` in the phone views.
- Real handlers for the six commands belong to other lanes; until they land, Save / Retire raise "unknown command" in the mock.
- No `mobile.modals` entry: the three new dialogs adapt through `display.isMobile` / the sheet styles, like `acc-voucher`.
- Parties balances use the six-column TB closing of the business date; the real `accPartyLedgers` should supply `active`.

## Integration needed
1. Delete `src/features/accounting/_contract-stubs.ts`; in `Accounting.tsx`, `Mobile.tsx`, `parts.tsx`, `books.tsx`, `chart.tsx`, `opening.tsx`, `shared.tsx` change `from './_contract-stubs'` to `from '../../services/mock'` and give `accChartHistory`, the three CSV builders and the types `ACCDayBookRow`, `ACCPartyLedger`, `AccLedgerUpsert`, `AccLedgerRetire`, `AccGroupUpsert`, `AccOpeningSave/Approve/Reopen` a home (statements / chart / opening lanes, or a small `types.ts` here).
2. Align the command payloads with the handlers (see Assumptions).
3. The last `[ACC1-UI]` test checks that the stubs file exists and names the contract names: delete or retarget that assertion when the stubs go.
4. Update the SCHEMA-11 pins listed under Tests.

## HANDOFF bullet
- ACC-1 UI (acc1-ui): Accounting tabs Vouchers · Trial balance (six-column, from / to, control vs parties, CSV) · Statements (ledger / party picker, running balance Dr / Cr, CSV) · Day book · Chart of accounts (28 groups + custom sub-groups, Owner add / edit / retire, history; dialogs acc-ledger, acc-group) · Parties · Opening balances (grid, automatic 1400 line, Save Owner / Manager / Accountant, Approve / Reopen Owner; dialog acc-opening-line) · Exceptions · Reconciliation · Mapping · Tally export, plus phone views (cards, sheets, sticky footer); built on `_contract-stubs.ts` (delete at integration); styles in `m-accounting2.css` (m-accounting.css is pinned by CT-B); 15 `[ACC1-UI]` tests.
