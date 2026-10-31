# ACC-2 · lane acc2-tests — the whole rulebook month (T1 … T19), both sides

Lane acc2-tests (Sonnet-class lane, written by Fable) · branch `claude/acc2-tests` · worktree `E:\Projects\HCC\worktrees\acc2-tests` · base = `claude/acc2` @ `b78cb069` (wave setup). Fictional data only; money = integer paise; nothing pushed. **No product code edited** — fixtures and tests only. The tests are written against `02 Planning/agents/ACC2-POSTING-CONTRACT.md`; they fail today where a lane has not landed (each failure says "waits for lane <x>"), and are meant to go green after `git merge claude/acc2`.

## Commits

| Commit | What |
|---|---|
| `4decf123` | Prototype: scenario `ACC-RULEBOOK` drives the whole of §8.2; `tests/_rulebook-month.mjs` (DRIVEN = ALL, `SERVER_EVENTS` / `SERVER_WAITING`, §8.4 / §8.5 / §8.6 data); `tests/stream-accounting-rulebook.mjs` 17 → 36 tests |
| `df830726` | Server: `build-rulebook-month.mjs` + regenerated `rulebook-month.json`, `RulebookMonthTests` (per-step theory, stand-in full-month test, skipped lane tests), `RulebookMonthSqlTests` (loan party, skipped whole-month test) |
| (last) | This DONE file; test-name fix |

## Files

Prototype (`03 Prototype`)
- `src/fixtures/ext/accounting-rulebook.ts` — the scenario (see "What the scenario drives").
- `tests/_rulebook-month.mjs` — the rulebook as data. The HDFC loan is now a party (`2400/HDFC-loan` → `2400/LN-HDFC`); `DRIVEN = ALL` (26 steps); `SERVER_EVENTS` (19) / `SERVER_WAITING` (7: T11 T14a T14b T16 T17 T18 T19); `ACC1_DRIVEN` / `ACC1_SERVER` kept for history; `PNL_84`, `BS_85`, `GST_86`.
- `tests/stream-accounting-rulebook.mjs` — 36 tests, prefixes `[ACC1-RULEBOOK]` (the 12 ACC-1 tests kept and updated) and `[ACC2-TESTS]` (24 new / reworked).

Server (`08 Production/tests/Hcc.Accounting.Tests`)
- `Golden/build-rulebook-month.mjs` → `Golden/rulebook-month.json` (regenerate: `node "08 Production/tests/Hcc.Accounting.Tests/Golden/build-rulebook-month.mjs"`): opening, 26 steps, 24 events for the 19 event-driven steps, `waiting` (7 steps with their intended event payloads), `expected.closing` (opening + 19 steps), `expected.flow` (without the opening), `expected.full` (all 26), `fullMonthControl`, `rulebookTrialBalance83`, `profitAndLoss84`, `balanceSheet85`, `gst86`. `mappingOverrides` is now empty.
- `Golden/RulebookMonthTests.cs`, `Sql/RulebookMonthSqlTests.cs`.

## What the scenario drives (real commands, day by day, 1 – 30 April, 1 May open)

| Step | When | Command(s) |
|---|---|---|
| setup | 1 Apr | `MST_MASTER_SAVE` HDFC bank / vendors, `GRP_COMPANY_SAVE`, **`ACC_LOAN_ADD`** (`2400/LN-HDFC`), `ACC_OPENING_SAVE` (Manager) + `_APPROVE` (Owner) with the loan on its party; the payroll roster is written directly (14 staff: ten men on ₹20,000 with PF / ESIC, four women on ₹10,000 without; weekly off Sunday; the base fixture's October HR history is cleared) and `HR_STAT_RATES_SET {from:'2026-04-01', pfErBps:1200}` |
| T1 T2 T3 | days 1–11 | as ACC-1 (155 room nights + ₹10,000 Owner discount, 20 room-service folio checks, 10 outlet checks). The Owner mapping `line:service → 4200` is **gone** (acc2-parity: `line:food` → 4200 by default) |
| T4 | 7 Apr in, 17 Apr out | `BOOK {source:'OTA', otaChannel:'Goibibo'}` room 101 (cleaned first through the housekeeping `TASK` flow), 10 nights ₹20,000 + 5 %, **`CHECKOUT {otaCredit:true}`** (ASSUMPTION 3) |
| T5a | 6 Apr | **`DEPOSIT_APPLY {booking, amount: ₹10,000}`** (Manager) on room 308 — replaces the "Advance adjusted" method and the `BA-ADV` row |
| T5b T5c T7a T8 T9 T10 T12 T15a T15b | as ACC-1 | unchanged |
| T6 | 20 Apr | `BOOK` room 207 for 10 – 20 May, `PAYMENT {kind:'deposit', method:'UPI', amount: ₹31,500}`; `gstOnAdvances` stays off |
| T7b | 30 Apr | `GRP_COMPANY_PAYMENT {amount: ₹57,000, tds: ₹6,000}` |
| T11 | 18 Apr | `EXP_VENDOR_PAY {vendor:'Morning Supplies', amount: ₹60,000, method:'Bank transfer'}` — default (oldest-first) allocation |
| T13a | 8 Apr | `HR_ADVANCE_ADD` ₹5,000 with `funding:'Cash'` (ASSUMPTION 5) |
| T13b T13c | 1 May | `HR_ATT_MARK` P every working day for 14 staff; `HR_PAYROLL_RUN` (Accountant) → `HR_PAYROLL_LOCK` (Owner) → `HR_PAYROLL_BANK_FILE`. The fixture asserts the run totals equal the rulebook (gross 2,40,000 · PF 18,000 + 18,000 · ESIC 1,500 + 6,500 · PT 2,000 · advance 5,000 · net 2,13,500) — **verified today** against the real HR engine |
| T14a / T14b | 27 Apr | **`ACC_MANUAL_SAVE`** Contra (template `contra`) UPI 2,13,000 → `1030/BA-HDFC`; Journal (template `journal`) card 1,50,000 → bank 1,48,230 + 5080 1,770 |
| T16 T17 T18 | 28 Apr, 5 Apr, 30 Apr | `ACC_MANUAL_SAVE` templates `owner-drawing-bank`, `loan-emi` (Dr `2400/LN-HDFC` 40,000 · Dr 5090 15,000 · Cr bank 55,000), `bank-charges` (Dr 5080 590) — Payment vouchers, Owner (approved at once) |
| T19 | 29 Apr | **`CHN_PAYOUT`** gross 21,000 = net 17,360 + commission 3,000 + GST 540 + TCS 100 |

Partially verified today with a scratch wrapper (outside the repository) that turned the ACC-2 commands into no-ops / stand-in payments: the rest of the scenario (bookings, housekeeping, pay run, OTA stay, May advance) runs, 21 bookings, load ≈ 1 s.

## The full-month expectation (rulebook §8.3, paise-exact in the tests; rupees here)

| Ledger | Dr | Cr | | Ledger | Dr | Cr |
|---|---:|---:|---|---|---:|---:|
| 1000 Cash in hand | 40,300 | | | 2000 Advance deposits | | 31,500 |
| 1030/HDFC Bank | 5,01,500 | | | 2110 Output CGST | | 14,500 |
| 1010 · 1020 · 1100 · 1110 · 1115 · 1200 · 2300 | nil | nil | | 2120 Output SGST | | 14,500 |
| 1340 GST electronic cash ledger | 100 | | | 2200/Morning Supplies | | 24,000 |
| 1360 TDS receivable | 6,000 | | | 2200/Kaveri Textiles | | 42,000 |
| 1500 – 1530 Fixed assets | 73,80,000 | | | 2310 PF · 2320 ESIC · 2330 PT | | 36,000 · 8,000 · 2,000 |
| 3000 Owner's drawings | 50,000 | | | 2400/LN-HDFC term loan | | 19,60,000 |
| 4900 Discounts allowed | 10,000 | | | 3100 Owner's capital | | 57,05,000 |
| 5000 59,000 · 5010 42,000 · 5030 45,000 · 5040 8,000 · 5100 2,40,000 · 5110 24,500 · 5080 2,360 · 5900 200 · 5090 15,000 · 5070 3,540 | 4,39,600 | | | 4000 Room revenue | | 4,30,000 |
| | | | | 4200 F&B revenue | | 1,60,000 |
| **Total** | **84,27,500** | | | **Total** | | **84,27,500** |

Derived and asserted: **§8.4** profit ₹1,40,400 (P&L ledgers 4xxx / 5xxx only: revenue 5,80,000 − consumption 1,01,000 − employee 2,64,500 − finance 17,360 − other 56,740); **§8.5** totals ₹79,27,900 both sides (owner's funds 57,95,400 + loan 19,60,000 + creditors 66,000 + duties 75,000 + advances 31,500 = assets 73,80,000 + 40,300 + 5,01,500 + 6,000 + 100) as a six-column-TB check; **§8.6** output 14,500 + 14,500, ITC nil, TCS 100 → challan ₹28,900: set-off voucher `Dr 2110 14,500 · Dr 2120 14,500 · Cr 1340 29,000` and `ACC_GST_PAY` `Dr 1340 28,900 · Cr 1030/BA-HDFC` in May, leaving 2110 / 2120 / 1340 at nil; party balances (Deccan nil, Morning 24,000 Cr, Kaveri 42,000 Cr, HDFC 5,01,500 Dr, loan 19,60,000 Cr, Goibibo nil, Staff-07 nil); statements of Deccan Agro (… T7b → nil), Goibibo (T4 check-out, T19 payout → nil), Morning (T8, T11 → 24,000 Cr), the loan, HDFC; Form 168 report (1360, Deccan Agro, April); ageing at 30 April (Morning 24,000, Kaveri 42,000 in 0 – 30); manual-voucher guards (unbalanced, empty, unknown ledger, control without party, 9999 / 1400, future date, Contra with a non-bank line, Payment with no / two cash-bank sides, one-sided lines; locked day "unlock the day first"; Accountant pending vs Owner approved vs rejected; void on an unlocked day vs a reversing voucher on a locked day; role gates); cash guards (₹2,00,000 cash receipt and two receipts totalling it refused; ₹12,000 cash vendor payment allowed with `cashDeductionLost`, ₹10,000 / bank not, 6,000 + 6,000 together yes); the `gstOnAdvances` ON case (₹10,500 deposit → Dr 1350 500 · Cr 2110 250 · Cr 2120 250); T19 / T11 guards; month lock (refused without the set-off and with a pending manual voucher; Accountant only requests; Owner locks, every day locked; locked month refuses entries; unlock needs a reason).

## Tests — what passes today, what waits

**Prototype** (`npx tsc --noEmit` 0 errors; `npm test` → **1937 / 1967**; baseline 1948 + 19 new tests in this file, 17 → 36): the 30 red are all "waits for lane …": the existing m1 "every available scenario loads" plus 29 tests of this file. The scenario cannot load until `ACC_LOAN_ADD`, `ACC_MANUAL_SAVE`, `DEPOSIT_APPLY`, `EXP_VENDOR_PAY`, `CHN_PAYOUT` and the OTA `CHECKOUT` path exist, so every test that reads it fails with that reason; four more name a missing selector (`accGstMonth`, `accMonthLockBlocker`, `expVendorAgeing`, `tdsReceivableReport`). **Pass today (7 self-checks):** §8.1 opening 77,40,000; the 26 entries close to §8.3 84,27,500; the driven / waiting split; Deccan model; §8.4 profit 1,40,400; §8.5 79,27,900; §8.6 challan 28,900. No BASE / FY26 pin changed.

| Waits for lane | Prototype tests that fail until it lands |
|---|---|
| acc2-flows | scenario load (`DEPOSIT_APPLY`, OTA `CHECKOUT`, `CHN_PAYOUT`), T4 / T5a / T19 steps, cash guard B9, OTA payout guards |
| acc2-manual | scenario load (`ACC_LOAN_ADD`, `ACC_MANUAL_SAVE`), T14 T16 T17 T18, every manual-voucher guard test |
| acc2-purchase | `EXP_VENDOR_PAY`, T11 allocation, ageing, cash guard B10, vendor-payment guards |
| acc2-tds | `GRP_COMPANY_PAYMENT {tds}`, Form 168 report, `CMP_PROFILE_SET` switches |
| acc2-gst | `gstOnAdvances` ON case, `accGstMonth`, `ACC_GST_SETOFF` / `_PAY`, month lock |
| acc2-parity | payroll vouchers (T13a-c on the party), `line:food` → 4200 in the closing trial balance, payroll date |

**Server** (`dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1 --logger trx`, built on this branch alone): **175 tests — 153 passed, 15 failed, 7 skipped** (before my change the project had 147 / 147). The 15 red are lane-dependent by design: `Each_driven_step…(T2 → acc2-parity, T4 and T5a → acc2-flows, T7b → acc2-tds)`, `Closing_balance…(1115, 1360, 2000, 4200)`, `Trial_balance_agrees…` (Suspense until the new keys are mapped), `Voucher_dates_and_numbers…` (Deccan trace needs T7b's TDS line), `Party_ledgers_hold_the_party_balances` (Deccan 6,000 left until TDS; 1200 per staff, acc2-parity), `Opening_entry_through_the_rules…` and `Six_column_trial_balance_and_the_party_statement…` (the same Suspense / TDS gap), `The_engine_vouchers_plus_the_rulebook_entries_of_the_waiting_steps_close_to_84_27_500` (the stand-in test: it composes once T2 T4 T5a T7b post), and the SQL month test (unmapped keys = day errors). **Skipped with a reason (7):** `T11_vendor_payment_allocated_oldest_first`, `T14_T16_T17_T18_manual_vouchers_post_their_template_entries`, `T19_ota_payout_from_the_statement`, `GST_set_off_and_the_month_lock_for_April`, `The_full_month_from_the_engine_only`, `Ota_receivable_is_a_party_ledger_per_channel`, and SQL `The_whole_month_through_the_real_commands`. Each reason holds the intended entries. `dotnet build` of the test project is clean. Logs: `.scratch\` (uncommitted).

## Missing commands / assumptions to confirm (one line each to change)

1. **`ACC_LOAN_ADD`** payload `{code:'LN-HDFC', name, lender, accountLast4, secured:true}` — the contract names the command and the row fields only. The opening line is `2400/LN-HDFC` (2400 is already a control on both sides).
2. **`ACC_MANUAL_SAVE` voucher type key** — the contract writes `{id?, date, type, …}` but the command's own `type` is its name; the fixture/tests send the voucher type as `voucherType` and also `vtype` / `kind`. **Contract amendment needed: name the key.** One helper (`manual()` in the fixture, `mv()` in the test).
3. **OTA check-out command** — contract §4 names receipt kind `ota-credit` and the rule, not a command; the fixture sends `CHECKOUT {id, otaCredit:true}` (`day===17`, one line).
4. **`DEPOSIT_APPLY {booking, amount}`** must work for an advance that has **no deposit receipt on the folio** (booked before HCC) and `totals()` must count a `deposit-apply` receipt as paid; sent by the Manager (an open shift is his).
5. **`HR_ADVANCE_ADD`** needs a funding: sent as `funding:'Cash'` and `method:'Cash'` (rulebook T13a is cash, 1000 → 40,300 at the close; today the handler says "simulated bank transfer, not drawer cash"). If parity books it from the bank the closing 1000 is 45,300 and 1030 4,96,500 — the test follows the rulebook. The drawer count must then include the advance.
6. **Pay-run date** — a month can be paid only when complete, so the run is prepared / locked / paid on **1 May**; the tests accept the payroll vouchers dated 30 April (rulebook) or 1 May (lock) and read the month through 1 May (30 April is checked without the pay run when it is dated 1 May). Parity lane: dating at the pay month's end would make §8.3 hold at 30 April exactly.
7. **T14b is a Journal**, not a Contra: the card MDR line (5080) is not a cash / bank ledger and contract §2 limits a Contra to cash / bank ledgers (the test asserts the refusal). The brief says "manual Contra" for T14a only.
8. **Vendor payment** `vendor` = the vendor's name (as `Expense.vendor`); default allocation puts the vendor's **opening balance (₹25,000) first**, then ₹35,000 on bill T8 — the opening line is not an expense, so the allocation list needs an "opening" row (or the ageing must read the opening line as the oldest item). The test asserts 35,000 on the T8 expense and ≤ 25,000 elsewhere.
9. Selector shapes the contract does not fix: `expVendorAgeing(s, asOf)` (a row per vendor with the buckets), `tdsReceivableReport(s, fy|month)`, `accGstMonth(s, 'YYYY-MM')` (must carry 14,500 · 28,900 · 100), `accMonthLockBlocker(s, month)` (string, empty = none); the tests read them tolerantly (numbers found in the JSON). Tighten after the lanes land.
10. **Server**: `PartyContext` needs the OTA channel so `ota-credit` posts `1115/<channel>`; the pay-run event carries `advanceRecoveries:[{staffId, paise}]`; a company payment event carries `tdsPaise`; charge kind `food`; receipt kinds `ota-credit`, `deposit-apply`, `deposit`. Field names are my guess from the contract — `rulebook-month.json` is generated, so a rename is one edit in `build-rulebook-month.mjs`.
11. The scenario writes the pay-run roster (14 staff) and clears the base fixture's October HR history directly, as it already did for the company's opening outstanding and the OTA channel row.

## Integration needed (for the integrator)

- After `git merge claude/acc2` run both suites; expected red only where a lane differs from the contract or from assumptions 1 – 10 — adjust the one line named above (tests are the specification, the lane's DONE file says which side moves).
- `m1` "Scenarios: every available scenario loads" goes green with the scenario; nothing else in `src` is touched. Keep the registrations from ACC-1 (`services/mock/index.ts`, `fixtures/ext/index.ts`, `services/mock/rulebook-bind.ts`).
- `rulebook-month.json` must be regenerated only by `build-rulebook-month.mjs` (the `[GOLDEN]` drift check owns every `export-*.mjs`; mine is named `build-…` and is independent of the code under test). Replace the stand-in test and the 7 skips by the engine tests as each waiting step gets its event (the skip reasons list the intended entries and `waiting.events` the intended payloads).
- Contract amendment suggestions: name the voucher-type key of `ACC_MANUAL_SAVE` (2); state the OTA check-out command (3); say whether `DEPOSIT_APPLY` needs a deposit receipt (4); say how a vendor's opening balance is allocated (8).

## HANDOFF bullet

- **ACC-2 tests lane (28 Oct 2026):** scenario `ACC-RULEBOOK` now drives the whole of rulebook §8.2 through real commands (OTA stay `ota-credit` + `CHN_PAYOUT`, `DEPOSIT_APPLY`, May advance, company payment net of TDS, vendor payment oldest first, April pay run on 14 staff, manual Contra / Journal / Payment templates, HDFC loan party `2400/LN-HDFC`) and the expectations assert closing ₹84,27,500, profit ₹1,40,400, balance-sheet ₹79,27,900, GST challan ₹28,900 in May, ageing, Form 168 report, manual-voucher / cash-guard / month-lock cases; server `rulebook-month.json` + `RulebookMonthTests` / `RulebookMonthSqlTests` cover the 19 event-driven steps with 7 skipped for lanes. Written against the contract: red until `claude/acc2` is merged (prototype 1937/1967, server 153 passed / 15 failed / 7 skipped, all "waits for lane"). Report: `05 Delivery Evidence/Accounting/ACC2-TESTS-DONE.md`.
