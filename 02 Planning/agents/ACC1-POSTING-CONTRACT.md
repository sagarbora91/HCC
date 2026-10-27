# ACC-1 posting contract (wave `claude/acc1`, 27 Oct 2026)

The shared shapes every ACC-1 lane builds against. **Copy, never reinterpret.** A lane that needs a change writes it in its DONE file; the coordinator amends this file and tells the other lanes. Source of truth for the rules: `02 Planning/HCC_Accounting_Rulebook_v1.md` (§3 chart, §3.3 parties, §5 A17, §6 permissions, §7.2–7.3 reports, §G2 opening; rulings DEC-139, DEC-141, DEC-144, DEC-148).

## 1. Ledger groups — Tally's 28, fixed ids

| id | name | parent | nature | side |
|---|---|---|---|---|
| `capital` | Capital Account | — | Cr | BS |
| `reserves` | Reserves & Surplus | `capital` | Cr | BS |
| `current-assets` | Current Assets | — | Dr | BS |
| `bank` | Bank Accounts | `current-assets` | Dr | BS |
| `cash` | Cash-in-Hand | `current-assets` | Dr | BS |
| `deposits-asset` | Deposits (Asset) | `current-assets` | Dr | BS |
| `loans-advances` | Loans & Advances (Asset) | `current-assets` | Dr | BS |
| `stock` | Stock-in-Hand | `current-assets` | Dr | BS |
| `debtors` | Sundry Debtors | `current-assets` | Dr | BS |
| `current-liabilities` | Current Liabilities | — | Cr | BS |
| `duties-taxes` | Duties & Taxes | `current-liabilities` | Cr | BS |
| `provisions` | Provisions | `current-liabilities` | Cr | BS |
| `creditors` | Sundry Creditors | `current-liabilities` | Cr | BS |
| `fixed-assets` | Fixed Assets | — | Dr | BS |
| `investments` | Investments | — | Dr | BS |
| `loans` | Loans (Liability) | — | Cr | BS |
| `bank-od` | Bank OD A/c | `loans` | Cr | BS |
| `secured-loans` | Secured Loans | `loans` | Cr | BS |
| `unsecured-loans` | Unsecured Loans | `loans` | Cr | BS |
| `suspense` | Suspense A/c | — | Dr | BS |
| `misc-expenses-asset` | Misc. Expenses (Asset) | — | Dr | BS |
| `branches` | Branch / Divisions | — | Dr | BS |
| `sales` | Sales Accounts | — | Cr | PL |
| `purchases` | Purchase Accounts | — | Dr | PL |
| `direct-income` | Direct Incomes | — | Cr | PL |
| `direct-expenses` | Direct Expenses | — | Dr | PL |
| `indirect-income` | Indirect Incomes | — | Cr | PL |
| `indirect-expenses` | Indirect Expenses | — | Dr | PL |

Primary groups are never deleted or re-parented. The Owner may add a **custom sub-group** under any group: id `g-<slug>`, name, parent. The legacy `ACCGroup` ('Asset' | 'Liability' | 'Income' | 'Expense' | 'Suspense') stays as a derived property of a group: BS + Dr = Asset, BS + Cr = Liability, PL + Cr = Income, PL + Dr = Expense, `suspense` = Suspense (so every existing selector and test keeps working).

## 2. Ledgers

```ts
type ACCLedger = {
  code: string;            // 4 digits for built-ins; Owner-added: 4 digits chosen by the Owner, must be unused
  name: string;
  group: string;           // group id of §1 (or a custom sub-group id)
  system: boolean;         // true = built-in (never deleted; name / group editable within the same nature)
  control?: boolean;       // true = holds party sub-ledgers (§3); never posted to directly
  gst?: 'none'|'input'|'output';
  tallyName?: string;      // name used in the Tally XML masters; default = name
  active: boolean;         // retiring is refused while the ledger (or any of its parties) has a non-zero balance or a mapping points at it
  note?: string;
};
```

**Built-in ledgers and their groups** (rulebook §3.2; existing codes keep their meaning):

| code | name | group | control |
|---|---|---|---|
| 1000 | Cash in hand | cash | yes (one party per till; the seeded till is `1000/TILL`, the seeded row's code — amended 27 Oct, ruling 4) |
| 1005 | Petty cash float | cash | |
| 1010 | UPI clearing | bank | |
| 1020 | Card clearing | bank | |
| 1030 | Bank accounts | bank | yes (party = Bank accounts master row, kind `bank`) |
| 1100 | Guest ledger (in-house folios) | debtors | |
| 1110 | Company receivables (city ledger) | debtors | yes (party = company account) |
| 1115 | OTA receivables | debtors | yes (party = OTA channel) |
| 1120 | Udhaar receivables (customer credit) | debtors | yes (party = Udhaar customer) |
| 1200 | Staff advances | loans-advances | yes (party = staff member) |
| 1210 | Recoverable from leavers (F&F) | loans-advances | |
| 1300 | GST input credit (legacy, locked snapshots only) | duties-taxes | |
| 1310 | Input CGST | duties-taxes | |
| 1320 | Input SGST | duties-taxes | |
| 1330 | Input IGST | duties-taxes | |
| 1340 | GST electronic cash ledger | duties-taxes | |
| 1350 | GST paid on advances (adjustable) | duties-taxes | |
| 1360 | TDS receivable | loans-advances | |
| 1370 | Prepaid expenses | loans-advances | |
| 1400 | Opening-balance difference | suspense | |
| 1500 | Buildings | fixed-assets | |
| 1510 | Furniture, fittings & electrical fittings | fixed-assets | |
| 1520 | Plant & machinery | fixed-assets | |
| 1530 | Computers, software & POS hardware | fixed-assets | |
| 1540 | Vehicles | fixed-assets | |
| 2000 | Advance deposits (guest and group) | current-liabilities | |
| 2100 | Synthetic tax payable (legacy) | duties-taxes | |
| 2110 | Output CGST | duties-taxes | |
| 2120 | Output SGST | duties-taxes | |
| 2130 | Output IGST | duties-taxes | |
| 2140 | GST payable under reverse charge | duties-taxes | |
| 2150 | TDS payable | duties-taxes | |
| 2200 | Vendor payables | creditors | yes (party = expense vendor) |
| 2300 | Salaries payable (net) | current-liabilities | |
| 2310 | PF payable | duties-taxes | |
| 2320 | ESIC payable | duties-taxes | |
| 2330 | Professional tax payable | duties-taxes | |
| 2340 | Other payroll deductions | current-liabilities | |
| 2400 | Bank loans (secured) | secured-loans | yes (party = loan, Owner-added rows; empty in ACC-1) |
| 2410 | Unsecured loans | unsecured-loans | |
| 3000 | Owner's drawings | capital | |
| 3100 | Owner's capital | capital | |
| 4000 | Room revenue | sales | |
| 4100 | Other services revenue | sales | |
| 4200 | Food & beverage revenue | sales | |
| 4300 | Cancellation, no-show & attrition charges | sales | |
| 4400 | Banquet & events revenue | sales | |
| 4900 | Discounts allowed | sales | |
| 4910 | Refunds, reversals & credit notes | sales | |
| 4950 | Cash over | indirect-income | |
| 4960 | Interest & other income | indirect-income | |
| 5000 | Housekeeping & guest supplies | direct-expenses | |
| 5010 | Linen & laundry | direct-expenses | |
| 5015 | Kitchen provisions & beverages | direct-expenses | |
| 5020 | Repairs & maintenance | indirect-expenses | |
| 5030 | Electricity, water & fuel | direct-expenses | |
| 5040 | Petty cash & sundry | indirect-expenses | |
| 5050 | General & administrative expenses | indirect-expenses | |
| 5060 | Rent & licence fees | indirect-expenses | |
| 5070 | OTA commission | indirect-expenses | |
| 5080 | Bank & card charges | indirect-expenses | |
| 5090 | Interest on loans | indirect-expenses | |
| 5100 | Salaries & wages | indirect-expenses | |
| 5110 | Employer PF & ESIC | indirect-expenses | |
| 5120 | Staff welfare | indirect-expenses | |
| 5200 | Depreciation | indirect-expenses | |
| 5300 | Bad debts written off | indirect-expenses | |
| 5400 | GST / TDS interest & late fees | indirect-expenses | |
| 5900 | Cash short | indirect-expenses | |
| 9999 | Suspense (unmapped) | suspense | |

**Mapping key re-points in ACC-1** (defaults; the Owner override table keeps working): `pos:sales` → 4200; `line:attrition` → 4300; `closing:cash-difference` splits — a short posts Dr 5900 (`closing:cash-difference`), an over posts Cr **4950** under the key **`closing:cash-over`** (amended 27 Oct, coordinator ruling 1; `accCashDiffKey` / `ChartOfAccounts.CashDiffKey`); `tax:gst-input` → **1310** for the CGST half and a new key `tax:gst-input-sgst` → 1320 (the expense ITC amount is split half / half as output GST is; IGST bills come with ACC-2); `receipt:udhaar` stays 1120 (party-resolved, §3). Everything else unchanged. Locked snapshots are never re-pointed.

## 3. Party sub-ledgers

A party ledger id is `<control code>/<party id>`, e.g. `1110/CMP-0007`, `2200/EV-MORNING`, `1030/BA-HDFC`, `1200/STF-007`, `1115/CHN-GOIBIBO`, `1120/UD-9876543210`.

| control | party source (prototype) | party id | display name |
|---|---|---|---|
| 1000 | tills = Bank accounts master rows kind `till` (`MNY_BA`) | row `code` | row name |
| 1030 | Bank accounts master rows kind `bank` (`MNY_BA`) | row `code` | row name |
| 1110 | `s.grpX.companies` | company `id` | company name |
| 1115 | `s.chnX.channels` | channel `id` | channel name |
| 1120 | Udhaar customers = distinct `(name, mobile)` of `s.expensesX.udhaar` rows; id `UD-<10-digit mobile>` or, without a mobile, `UD-<slug of name>` | as stated | name |
| 1200 | `s.hrX.staff` | staff `id` | staff name |
| 2200 | Expense vendors master rows (`MNY_EV`) | row `code` | row name |
| 2400 | Owner-added loan rows (ACC-2); none in ACC-1 | — | — |

Rules: parties are **derived from the masters, never stored twice**; `accPartyLedgers(s)` returns `{id, control, partyId, name, active}[]`. A voucher entry posts to the party ledger when the source names the party; when it cannot (an old record without the party, a method with no settles-to account) it posts to the **control code itself** and the day gets a `warning` exception `kind: 'no-party'` (never blocks a lock). A retired master row with a non-zero balance refuses to retire (`MASTER_IN_USE`). The trial balance has a `detail` flag: `false` = one row per control (parties summed into it), `true` = a row per party under its control.

**Party resolution per source** (prototype `accLiveVouchers`, server `SourceEntry.Party`):

| source | key | party |
|---|---|---|
| folio receipt kind `company-credit` / company payment | `receipt:company-credit` | the company id on the booking / payment |
| group master receivable, deposit, settlement | `receipt:company-credit`, `receipt:group-deposit` | the block's company id (none → control) |
| Udhaar settlement (desk) and Udhaar credit raised (A17) | `receipt:udhaar` | the Udhaar customer id |
| expense funded by `Vendor credit` | `funding:Vendor credit` | the expense's vendor (by code / name) |
| receipt or expense by a method that settles to a **bank** account | `method:<name>` / `funding:<name>` | the settles-to account code |
| cash methods | `method:Cash` etc. | the till the shift is on (`1000/<till>`); one seeded till → `1000/TILL` (amended 27 Oct, ruling 4: the seeded row's code is `TILL`) |
| staff advance given / recovered | `payroll:advances` | staff id |
| OTA receivable (ACC-2 posts it; ACC-1 only defines it) | `receipt:ota` | channel id |

**Server:** `SourceEntry` gains `string? Party` (null = control); `acc.source_entry` gains `party_id VARCHAR(40) NULL` (migration 4210); `VoucherEntry.Account` carries the full `<code>/<party>` id; `ChartOfAccounts.Find` resolves the control part for name / group; the mapping resolves the key, then the party suffix is appended.

## 4. Udhaar credit raised (A17, DEC-144)

Raising an Udhaar credit posts a **Sales** voucher the same day: Dr `1120/<customer>` (gross) · Cr 4200 or 4000 (`kind: 'restaurant' | 'room'` chosen on the screen; `'other'` → 4100) · Cr 2110 · Cr 2120 (GST at the rate of the thing sold, half / half, rounding as `gstLineTax`). Source key `udhaar:<receivable id>`, sub `udhaar-credit`. The settlement (`udhaar-payment`) is unchanged except that it credits the party ledger. Prototype: `ExpUdReceivable` gains optional `kind` and `gstBps` (old rows: `kind` absent → 'other', `gstBps` 0 → no GST lines; CA note in the DONE file). Server: `ops.udhaar_receivable` (amended 27 Oct, ruling 4: the table is in schema `ops`, migration 4150) gains `kind`, `gst_bps` (migration 4215) and the outbox event `UdhaarCreditRaised` feeds a new `SourceRules.UdhaarCredit(...)`.

## 5. Opening balances (DEC-139 / DEC-148)

```ts
type ACCOpeningLine = {account: string; debit: number; credit: number};        // account = ledger code or party ledger id; paise; one side only
type ACCOpening = {asOf: string;                                             // the FY start the entry is dated (business FY of the save date)
  lines: ACCOpeningLine[]; status: 'draft'|'approved'; by: string; at: string; approvedBy?: string; approvedAt?: string; note?: string};
```

- One opening entry per property. Saving replaces the draft; a `draft` can be saved unbalanced: the module **adds** the balancing line on **1400 Opening-balance difference** and shows it. Approval (Owner, `PERM-ACC-009`) requires the entry to balance **after** the 1400 line, i.e. 1400 may be non-zero at approval but, **once an entry exists** (draft, or approved with 1400 ≠ 0), the **period lock** of any date ≥ `asOf` is refused ("opening balances pending"). **With no opening entry at all, locks are not gated** and the "opening balances pending" flag stays visible (amended 27 Oct, coordinator ruling 5; rulebook §G2 amended the same way).
- Posted as one Journal `JRN/<asOf yyyymmdd>/000`, source `{kind: 'opening', id: 'opening', sub: 'opening-entry'}`, dated `asOf`, included by `accVouchersUpTo` and the cumulative trial balance exactly like the server's imported opening (`opening-import`, which stays for the CSV path and is superseded when an approved entry exists).
- Enter: Manager or Accountant (`PERM-ACC-008`; prototype `permission('reports')` and role in Owner / Manager / Accountant). Approve: Owner only. After approval the entry is read-only until the Owner reopens it (`status` back to `draft`, reason, audited) — only possible while no lock exists on or after `asOf`.
- Commands (prototype): `ACC_OPENING_SAVE {asOf, lines, note?}`, `ACC_OPENING_APPROVE {}`, `ACC_OPENING_REOPEN {reason}`. Server: `SaveOpeningBalances`, `ApproveOpeningBalances`, `ReopenOpeningBalances`, tables `acc.opening_entry` + `acc.opening_line` (migration 4220).

## 6. Statements and the six-column trial balance

```ts
type ACCStatementRow = {date: string; no: string; type: ACCVoucherType; narration: string; counter: string;  // counter = the other side's ledger name(s)
  debit: number; credit: number; running: number};                                                        // running = Dr positive / Cr negative
type ACCStatement = {account: string; name: string; from: string; to: string;
  opening: number; debit: number; credit: number; closing: number; rows: ACCStatementRow[]};              // opening/closing signed as `running`
type ACCTB6Row = {account: string; name: string; group: string; control?: string;                          // control set on party rows (detail = true)
  openingDebit: number; openingCredit: number; debit: number; credit: number; closingDebit: number; closingCredit: number};
type ACCTB6 = {from: string; to: string; detail: boolean; rows: ACCTB6Row[];
  totals: Omit<ACCTB6Row, 'account'|'name'|'group'|'control'>; balanced: boolean; openingPending: boolean};
```

- Opening of a statement / TB = every voucher dated **before** `from`, **plus an opening voucher (the opening entry, the imported opening) dated ON `from`** (rulebook §7.2 "incl. opening journal" wins — amended 27 Oct, ruling 6); locked snapshots as locked, live derivation elsewhere. Closing = opening + period. Dr balances show as Dr, Cr as Cr; never negative numbers on screen.
- Day book = every voucher in the range in `date`, then voucher-number order (amended 27 Oct, ruling 6), one row per entry.
- `openingPending` = no opening entry, or not approved, **or approved with 1400 ≠ 0** (amended 27 Oct, ruling 6).
- Server: `GET /accounting/statement?account=&from=&to=`, `GET /accounting/trial-balance?from=&to=&detail=`, `GET /accounting/day-book?from=&to=`; prototype selectors `accStatement(s, account, from, to)`, `accTrialBalance6(s, from, to, detail)`, `accDayBook(s, from, to)` in a new file `src/domain/streams/accounting-reports.ts`.

## 7. Permissions

| code | meaning | roles (seed) |
|---|---|---|
| PERM-ACC-007 | accounting.chart.edit (ledgers, custom sub-groups) | Owner |
| PERM-ACC-008 | accounting.opening.enter | Owner, Manager, Accountant |
| PERM-ACC-009 | accounting.opening.approve | Owner |
| PERM-ACC-001 | view (statements, trial balance, day book, parties) | as today (Owner, Manager, Accountant; Cashier excluded, DEC-079) |

Prototype equivalents: chart edit = `permission('admin')` and role Owner; opening enter = `permission('reports')` and role in `ACC_LOCK_ROLES`; opening approve = role Owner.

## 8. State and schema

- `SCHEMA` 10 → **11** (done by the coordinator on the wave branch): `ACCState` gains `chart?: {ledgers: ACCLedger[]; groups: ACCGroupDef[]; history: ACCChartChange[]}` and `opening?: ACCOpening`; old saves without them behave as before. The coordinator also added the `ACCGroupDef`, `ACCLedger`, `ACCChartChange`, `ACCOpening*` types and the `ACC_GROUPS` constant to `src/domain/streams/accounting.ts` so every lane imports the same names.
- Prototype commands are added **one handler file per lane** (`src/services/mock/handlers/accounting-<lane>.ts`, command types start with `ACC_`), registered by **one appended import line and one spread inside the `ACC1` object** in `src/services/mock/handlers/index.ts` (the `// ACC-1` block); new domain files are exported by **one appended line** in the `// ACC-1` block of `src/services/mock/index.ts`. Production builds map commands to the server in `src/clients/sync/accounting.ts`: a lane that adds commands adds its own `src/clients/sync/accounting-<lane>.ts` and one appended registration line where the sync files are registered (see `src/clients/sync/index.ts`). The integrator resolves these append-only files' merges.
- Server: each lane adds its own files under `Hcc.Application/Accounting/` and `Hcc.Api/Endpoints/` (new file per lane) and appends its `MapXxx(...)` call in `HccComposition.cs` / the endpoint registration inside an `// ACC-1` block.

## 9. Migrations owned

| lane | numbers |
|---|---|
| acc1-chart | 4200–4209 (`4200_acc_ledger_groups.sql`, `4201_acc_ledger.sql`, `4202_acc_chart_rbac_seed.sql` — RBAC seeds end `_rbac_seed.sql`, ruling 3) |
| acc1-parties | 4210–4219 (`4210_acc_source_entry_party.sql`, `4215_exp_udhaar_kind_gst.sql`, …) |
| acc1-opening | 4220–4224 (`4220_acc_opening_entry.sql`, `4221_acc_opening_rbac_seed.sql` — renamed at integration, ruling 3) |
| acc1-statements | 4225–4229 (views, if any) |

Every migration: `node "08 Production/tools/migrations-lock.mjs" --add` and a line in `Hcc.slnx`; never edit an applied one.
