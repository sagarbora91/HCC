# ACC-4 contract — year lock, carry-forward, reopen, bank reconciliation, clearing (wave F5)

Written 2 Nov 2026 by the F5 coordinator (Opus), docs-only. Sources: Wave Plan F1–Fn §F5, §1.4, Appendix D; Accounting Rulebook v1 §5 B8, §7.5, §8, §10 G3 / G4; `ACC3-DONE.md` §7; HANDOFF 13m; DEC-077, DEC-139, DEC-148, DEC-155, DEC-206. Owner defaults applied: **A23** no bank CSV yet → generic layout + mapping master; C-6…C-8 CA items change guard text / formulas later only. HCC stays a feeder to Tally (DEC-140): the year lock and the carry-forward are HCC's book-keeping steps, the statutory close stays Tally's / the CA's.

Rules for both sides: money = integer paise; simulated clock (`s.clock`) in the prototype; every command through `execute` / the application layer, typed `HccError`, idempotency keys; prototype and server give the same figures for the same fixture. New state fields optional; **SCHEMA stays 13** unless the integrator finds a shape change it cannot keep optional (then 14, recorded in `ACC4-DONE.md`).

## 1. Year lock, carry-forward, reopen (lane acc4-year; rulebook G3 step 5–6, G4)

### 1.1 Types (prototype `src/domain/streams/accounting-year.ts`; C# `Hcc.Application/Accounting/YearLock*`)
```ts
type AccFy = string;                         // '2026-27' (1 April → 31 March, per DEC-122 business-date rule)
interface AccYearState {
  fy: AccFy;
  status: 'open' | 'closing' | 'locked' | 'reopened';
  lockedAt?: string; lockedBy?: string;
  reopenedAt?: string; reopenedBy?: string; reopenReason?: string;
  provisionalExports?: boolean;              // true from a reopen until the next lock (DEC-077 extended)
  carryForwardId?: string;                   // the AccCarryForward produced at lock
}
interface AccCarryForward {
  id: string; fromFy: AccFy; toFy: AccFy;
  status: 'draft' | 'approved' | 'superseded';
  lines: { ledger: string; party?: string; dr: number; cr: number }[];  // paise
  profitToCapital: number;                   // P&L result moved to 3100 (signed paise)
  drawingsToCapital: number;                 // 3000 closed to 3100
  generatedAt: string; approvedBy?: string; approvedAt?: string; supersededBy?: string;
}
```
State slice `s.accYearX?: { years: AccYearState[]; carryForwards: AccCarryForward[] }`.

### 1.2 Commands
| Command (prototype / server) | Who | Rule |
|---|---|---|
| `ACC_YEAR_PREVIEW {fy}` (read) / `GET /accounting/year/{fy}/preview` | Owner, Accountant | Checklist: every month of the FY GST-locked, every day locked (DEC-155 gate), 1400 Opening-balance difference and 9999 suspense zero, 1010 / 1020 clearing zero at 31 March (§2.6), no pending manual vouchers / expenses / credit notes dated in the FY. Returns the draft carry-forward lines without saving. |
| `ACC_YEAR_CARRY_FORWARD {fy}` | Owner, Accountant | Builds the **draft** `AccCarryForward` (G3 step 5, generated, never typed): balance-sheet ledgers (assets, liabilities, capital — groups per §7.5, party sub-ledgers kept per party) closing balance → opening of `fy+1`; Sales / Direct / Indirect income and expense ledgers closed to P&L, result to **3100**; 3000 Drawings to 3100. Lines must balance; a difference refuses `ACC_CF_UNBALANCED`. |
| `ACC_YEAR_LOCK {fy, carryForwardId}` | **Owner** | Requires every preview check green (else `ACC_YEAR_NOT_READY` with the failed list) and the draft. Approves the carry-forward = it becomes the next FY's **opening entry** through the same opening store as DEC-139 / DEC-148 (source `carry-forward`, approved by the Owner, so no Manager entry is needed for `fy+1`). Status → `locked`. After the lock every post / edit / void dated in the FY is refused `ACC_YEAR_LOCKED`; a correction is a journal in the new year with `refersToFy` (G4). |
| `ACC_YEAR_REOPEN {fy, reason}` | **Owner** | Reason ≥ 10 chars, audited (DEC-077 extended to a year). Only the **latest** locked year can be reopened, and only while `fy+1` has no locked month (else `ACC_YEAR_REOPEN_BLOCKED`). Status → `reopened`, `provisionalExports = true`: every Tally / CSV / Excel export of either year carries "PROVISIONAL — year reopened" until relocked. Issued invoice / credit-note numbers and their GST never change (DEC-077 build rule). |
| `ACC_YEAR_RELOCK {fy}` | **Owner** | Regenerates the carry-forward; old one `superseded`; the `fy+1` opening is replaced by the new lines (a delta journal is **not** used — the opening is regenerated); vouchers already exported for `fy+1` openings are marked changed (DEC-077). |

Day reopen (DEC-077) and month unlock (PERM-ACC-012) inside a locked year are refused `ACC_YEAR_LOCKED`.

### 1.3 First year
FY 2026-27 has the hand-entered opening (DEC-148). The carry-forward from FY 2025-26 is **not** generated (no HCC book for it); the year list starts at 2026-27. The legacy `lgc` history is never part of the books.

## 2. Bank reconciliation and clearing (lane acc4-bank; rulebook B8)

### 2.1 Statement mapping master (`mst.bank_statement_layout`, Owner master)
```ts
interface BankStatementLayout {
  id: string; name: string; bankLedger: string;        // e.g. '1030/HDFC'
  delimiter: ',' | ';' | '\t'; headerRows: number; dateFormat: 'DD/MM/YYYY' | 'DD-MM-YYYY' | 'YYYY-MM-DD' | 'DD-MMM-YYYY';
  columns: { date: string; narration: string; ref?: string; debit?: string; credit?: string; amount?: string; drCrFlag?: string; balance?: string };
  amountSign?: 'debit-negative';                         // when a single amount column is used
  active: boolean;
}
```
Seed row **"Generic"** (A23 default): columns `date, narration, ref, debit, credit, balance`, `DD/MM/YYYY`, comma, one header row. The owner's real layout becomes a new row later (no code change). CSV formula guard (Wave D) applies to imported cells. Import template `deployment/import-templates/bank-statement-layouts.csv`.

### 2.2 Statement rows
```ts
interface BankStatementImport { id: string; bankLedger: string; layoutId: string; fileName: string; fileHash: string;
  from: string; to: string; openingBalance: number; closingBalance: number; rows: number; importedBy: string; importedAt: string }
interface BankStatementRow { id: string; importId: string; bankLedger: string; date: string; narration: string; ref?: string;
  debit: number; credit: number; balance?: number;        // paise, bank's view: credit = money in
  status: 'unmatched' | 'matched' | 'ignored'; matchId?: string }
interface BankMatch { id: string; rowIds: string[]; voucherLineRefs: { voucherId: string; line: number }[];
  rule: 'auto-ref' | 'auto-amount-date' | 'manual' | 'created'; clearedDate: string; by: string; at: string; undoneAt?: string }
```
Import refuses: same `fileHash` already imported (`BANK_DUP_FILE`); a row date outside an open FY; a running-balance break when `balance` is mapped (`BANK_BALANCE_BREAK` with the row number). Rows overlapping a previous import by (date, amount, ref, narration) are flagged `duplicate?` and skipped unless the user keeps them.

### 2.3 Matching rules (in this order; one-to-one unless stated)
1. **Ref** — row `ref` (or a UTR / cheque number found in the narration by the layout's regex) equals a voucher line's reference, same amount, date within ±7 days → `auto-ref`.
2. **Amount + date window** — exactly one unmatched book line on that bank ledger with the same amount and date within **0…+3 days** (bank after book) → `auto-amount-date`. Two or more candidates → left for manual.
3. **Settlement (B8)** — a credit row whose narration matches the layout's UPI / card settlement pattern is proposed as a **B8 journal** (§2.5), not matched to an existing line.
4. **Manual** — many-to-one / one-to-many allowed, sums must be equal (`BANK_MATCH_SUM`).
5. **Create** — bank charges, interest, unknown credits: the user creates a voucher from the row (charges → 5080 / 5090 bank charges ledger per chart; interest → indirect income), matched `created`. Unknown credit with no ledger → 9999 suspense is refused; the row stays unmatched.
Auto-match never crosses a locked year; undo (`BANK_UNMATCH`) is allowed until the month is locked.

### 2.4 Cleared date
A matched book line gets `clearedDate` = the statement row date. Books are never re-dated. Reconciliation is by cleared date.

### 2.5 UPI / card clearing (B8)
`BANK_SETTLEMENT_POST {rowId, clearing: '1010'|'1020', gross, mdr, mdrGst, itc}` → **Journal**: Dr bank (net = row credit) · Dr 5080 (MDR + its GST unless ITC allowed, then Dr 1300 for the GST) · Cr 1010 / 1020 gross. `gross − mdr − mdrGst = net` else `BANK_SETTLEMENT_SUM`. UPI with no charge: Dr bank · Cr 1010 (T14a). Matched automatically to its row. Golden: rulebook T14a / T14b.

### 2.6 Clearing view and reconciliation report
- **Clearing view:** 1010 / 1020 balance by day = tenders taken − settlements; ageing of unsettled tender days (> 3 days amber, > 7 red). 1010 / 1020 must be **zero at the year lock** (preview check).
- **Reconciliation report** (per bank ledger, as of a date): balance per books; less cheques / transfers issued not yet debited; add deposits not yet credited; ± bank-only items unmatched (charges, interest, credits); = balance per statement; **difference must be 0** (shown red otherwise). Lists of unmatched book lines and unmatched statement rows. Exports CSV / Excel through the ACC-3 export path; money columns follow DEC-061 masking.

## 3. Carry-overs (lane acc4-bank, server side unless stated)
| # | Item | Source |
|---|---|---|
| C1 | Server vendor returns allocate **explicit first**, then oldest-first only when no allocation is given (match the prototype) | ACC3-R4 / DEC-206 |
| C2 | Server GST tie compares the **posted set-off journal** with the computed set-off (`GstMonthService`) | DEC-206 |
| C3 | Server ageing reads from the **as-of window**, not every day from the first source (`AgeingQueries`) | DEC-206 |
| C4 | Tally explicit allocations include a return's own bill, not payments only | ACC3-DONE §7 |
| C5 | Server GST month **view** (`MonthAsync`) shows the pay-run blocker (ruling 6), as the prototype does | ACC2-DONE / HANDOFF 13m |
| C6 | Dated TDS rates **live**: TDS computation reads the rate effective on the voucher date (today display-only) | HANDOFF 13m |
Each carry-over gets a test on both sides where the prototype differed.

## 4. Permissions (integrator confirms the numbers are unused; RBAC seed `*_rbac_seed.sql`)
| Id | Code | Roles |
|---|---|---|
| PERM-ACC-014 | accounting.year.preview / carry-forward draft | Owner, Accountant |
| PERM-ACC-015 | accounting.year.lock / reopen / relock | Owner |
| PERM-ACC-016 | accounting.bank.import / match / settlement post | Owner, Accountant |
| PERM-ACC-017 | accounting.bank.layout master | Owner |
Reports read with PERM-ACC-001 (DEC-203). Manager: read the clearing view only; Reception, Housekeeping, Maintenance, Restaurant: nothing (DEC-061).

## 5. Migrations (§1.4)
| No. | Lane | Content |
|---|---|---|
| 4280 | acc4-year | `acc.fiscal_year` (status, lock / reopen audit columns) |
| 4281 | acc4-year | `acc.carry_forward` + `acc.carry_forward_line` |
| 4282 | acc4-year | `refers_to_fy` on manual vouchers; opening source `carry-forward` |
| 4283 | acc4-year | year-lock trigger / guard view |
| 4284 | acc4-year | RBAC seed PERM-ACC-014/015 |
| 4285 | acc4-bank | `mst.bank_statement_layout` + Generic seed |
| 4286 | acc4-bank | `acc.bank_statement_import`, `acc.bank_statement_row` |
| 4287 | acc4-bank | `acc.bank_match` + `cleared_date` on voucher lines |
| 4288 | acc4-bank | settlement journal source (B8) |
| 4289 | acc4-bank | reconciliation report view |
| 4290–4292 | acc4-bank | carry-overs C1–C6 (only if a schema change is needed) |
| 4293 | acc4-bank | RBAC seed PERM-ACC-016/017 |
| 4294 | acc4-bank | spare |
| 4295–4299 | integrator | merge fallout |
Every migration: `node tools/migrations-lock.mjs --add` + `Hcc.slnx` line.

## 6. Tests
- **Year (both sides):** `stream-acc4-year.mjs`, `Hcc.Accounting.Tests/YearLock/` — golden: the rulebook April 2026 month taken as the whole FY → carry-forward lines equal the **§8 balance sheet** closing figures, P&L result in 3100, 3000 zero, income / expense ledgers absent from the FY27 opening; lock refusals per check; post into a locked year refused; correction journal in FY27 with `refersToFy`; reopen only the latest year, refused once FY27 has a locked month; exports marked provisional while reopened; relock supersedes and regenerates; Accountant cannot lock.
- **Bank:** `stream-acc4-bank.mjs`, `Hcc.Accounting.Tests/Bank/` — fixture `bank-april-2026.csv` (**fictional**, HDFC-like generic layout) holding the T14a / T14b settlements, one transfer to a vendor, bank charges, interest, one cheque issued and not presented: after auto-match + one manual + two created vouchers the **reconciliation difference is zero** and 1010 / 1020 are zero; duplicate file refused; balance break refused; ambiguous amount left unmatched; undo match; layout mapping row for a single amount + Dr/Cr column file.
- **Carry-overs:** one test per C1–C6.
- **acc4-tests:** `stream-accounting-golden4.mjs`, `RulebookYearTests.cs`, permission matrix rows, reconciliation export (CSV / Excel totals = screen).
- **acc4-ui:** `stream-accounting-ui4.mjs` + fit-check `accounting`.
