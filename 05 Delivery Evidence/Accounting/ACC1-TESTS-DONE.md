# ACC-1 · lane acc1-tests — the rulebook golden month, ACC-1 subset

Branch `claude/acc1-tests` (worktree `E:\Projects\HCC\worktrees\acc1-tests`), base = `claude/acc1` @ 39930edc. Fictional data only; money = integer paise; nothing pushed. No product code edited (the three shared-file lines below excepted).

## Commits

| Commit | What |
|---|---|
| 38b16ea1 | Scenario `ACC-RULEBOOK` (fixture + binder + registration) and `tests/stream-accounting-rulebook.mjs` |
| 3785804c | Rulebook month as shared data (`tests/_rulebook-month.mjs`), server `Golden/rulebook-month.json` + generator, `RulebookMonthTests`, `RulebookMonthSqlTests` |
| 68d4737f | WIP after the PC restart: statement rows per checkout, 2000 spot check, plan text |
| (last) | this DONE file and the final tweaks — see the last commit hash in the hand-back |

## Files

Prototype (`03 Prototype`)
- `src/fixtures/ext/accounting-rulebook.ts` — scenario **ACC-RULEBOOK**: clears the base fixture, dates it 1 April 2026 (₹20,000 float) and drives **April 2026 day by day with real commands** (night audit, exact cash count, day close, next day, 30 times); ends closed on 30 April, open on 1 May. Master data by command: bank account HDFC (`BA-HDFC`), payment method "Advance adjusted", vendors Morning Supplies (`EV-MORNING`) and Kaveri Textiles (`EV-KAVERI`), company Deccan Agro Ltd (GRP_COMPANY_SAVE, credit limit ₹10 lakh, opening outstanding ₹40,000), OTA Goibibo (`goibibo`, a direct channel row — there is no command), staff = the existing `HR-S07` ("Staff-07"). Opening entry via `ACC_OPENING_SAVE` (Manager) + `ACC_OPENING_APPROVE` (Owner); Owner mapping `line:service → 4200`.
- `src/services/mock/rulebook-bind.ts` — binds the real `execute` to the fixture (a fixture must not import the command registry: the scenario worker would carry every handler).
- `tests/_rulebook-month.mjs` — the rulebook as data (opening §8.1, 26 entries of §8.2, §8.3), shared by both suites. Not a test file.
- `tests/stream-accounting-rulebook.mjs` — 17 tests, names `[ACC1-RULEBOOK]`.

Shared-file edits (one line each, inside the ACC-1 block / array): `src/services/mock/index.ts` (`export * from './rulebook-bind'`), `src/fixtures/ext/index.ts` (import + `accountingRulebook` in `extensions`). Integrator: keep both.

Server (`08 Production/tests/Hcc.Accounting.Tests`)
- `Golden/build-rulebook-month.mjs` → `Golden/rulebook-month.json` (opening, 26 steps, 20 events, expectations; regenerate with `node "08 Production/tests/Hcc.Accounting.Tests/Golden/build-rulebook-month.mjs"`).
- `Golden/RulebookMonthTests.cs` — 8 test methods (+ one Theory row per ledger).
- `Sql/RulebookMonthSqlTests.cs` — 1 test (real outbox → consumer → cumulative trial balance on a disposable database).

## What is driven, and what is not

Driven in the prototype through real commands: **T1** (155 room nights = ₹4,10,000 + the ₹10,000 Owner discount), **T2** (20 room-service checks of ₹5,000 charged to folios), **T3** (10 walk-in checks of ₹6,000 net, cash / UPI), **T5a** (advance applied), **T5b** (six company-credit checkouts, ₹63,000), **T5c** (cash 1,52,000 / UPI 1,50,000 / card 1,50,000, mixed tenders at check-out), **T7a**, **T8**, **T9**, **T10**, **T12**, **T15a** (slip + photo, confirmed by the Owner), **T15b** (₹200 short accepted by the Owner), **T13a** (HR_ADVANCE_ADD — no voucher, see gap 1).
Server events cover the same steps **plus T13a, T13b, T13c** (the server books payroll).

Gaps (the brief's wording could not be met as written) — decisions needed:
1. **Payroll is not booked by the prototype.** `accLiveVouchers` has no source for `HR_ADVANCE_ADD` or the pay run / net-pay (T13a–c), and `HR_ADVANCE_ADD` is "a simulated bank transfer, not drawer cash" (the rulebook says cash). The server books all three (`AdvanceGiven`, `PayrollPosted`, `PayrollPaid`). Needed: prototype vouchers `payroll:advances` (Dr 1200/<staff>), pay run (Dr 5100/5110 · Cr 2300/2310/2320/2330/1200), net pay paid. The contract §3 mentions the `payroll:advances` party but no lane builds the prototype source.
2. **T16 Owner drawing from the bank cannot be driven.** `OWNER_DRAWING` / `OwnerDrawingRecorded` take cash from the drawer (Dr 3000 · Cr 1000); a drawing from the bank is rulebook B6 "new" (a manual payment voucher). A cash drawing of ₹50,000 is impossible anyway (the drawer holds ₹40,500 after banking). Missing command: manual voucher / drawing from bank.
3. **T5a (advance applied)** has no command either. The fixture uses a payment method **"Advance adjusted"** whose master ledger is 2000, settling to a placeholder bank row `BA-ADV` that never moves money: Dr 2000 · Cr 1100 exactly as the rulebook, via the mapping. Missing command: apply an advance held since before HCC to a folio. (If the parties lane resolves a party from the method's settles-to account, this tender must stay un-partied: 2000 is not a control, so none is expected — the test checks.)
4. **T2 → 4200 needs an Owner mapping.** A room-service / restaurant charge on a folio is a `service` line (`line:service` → 4100); the contract re-points only `pos:sales`. The fixture sets `ACC_MAP_SET line:service → 4200` (valid once the chart lane adds 4200). Rulebook A3 says "moves to 4200": either add a line class `restaurant`, or keep the override. Server JSON does the same (`mappingOverrides`).
5. **Brief figures vs the rulebook:** "Deccan Agro 63,000 Dr after T5b then 23,000 after T7a" leaves out the ₹40,000 opening. With the opening (rulebook §8.1) it is **1,03,000 after T5b, 63,000 after T7a**; the subset closes at 63,000 Dr (T7b is ACC-2). 63,000 − 40,000 = 23,000 is the flow without the opening. Tests use the rulebook's figures. T5b in the prototype is six checkouts, so the statement has six rows of ₹10,500 on 6 April.
6. Left out as instructed (ACC-2): T4, T6, T7b, T11, T14a/b, T17, T18, T19.

## Subset expectation tables (closing at 30 April 2026, paise-exact in the tests; rupees here)

Columns: **Prototype** = what the scenario drives (13 steps + opening); **Server** = + T13a–c; **Brief** = + T16 (the target once a bank drawing exists); **Full** = §8.3 (all 26 steps; the ACC-2 target, ₹84,27,500 both sides).

| Ledger | Prototype Dr | Cr | Server Dr | Cr | Brief Dr | Cr | Full §8.3 Dr | Cr |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 1000 Cash in hand | 45,300 |  | 40,300 |  | 40,300 |  | 40,300 |  |
| 1010 UPI clearing | 1,81,500 |  | 1,81,500 |  | 1,81,500 |  |  |  |
| 1020 Card clearing | 1,50,000 |  | 1,50,000 |  | 1,50,000 |  |  |  |
| 1030 Bank accounts | 4,45,000 |  | 2,31,500 |  | 1,81,500 |  | 5,01,500 |  |
| 1110 Company receivables | 63,000 |  | 63,000 |  | 63,000 |  |  |  |
| 1340 GST electronic cash ledger |  |  |  |  |  |  | 100 |  |
| 1360 TDS receivable |  |  |  |  |  |  | 6,000 |  |
| 1500 Buildings | 60,00,000 |  | 60,00,000 |  | 60,00,000 |  | 60,00,000 |  |
| 1510 Furniture & fittings | 8,00,000 |  | 8,00,000 |  | 8,00,000 |  | 8,00,000 |  |
| 1520 Plant & machinery | 5,00,000 |  | 5,00,000 |  | 5,00,000 |  | 5,00,000 |  |
| 1530 Computers | 80,000 |  | 80,000 |  | 80,000 |  | 80,000 |  |
| 2000 Advance deposits |  |  |  |  |  |  |  | 31,500 |
| 2110 Output CGST |  | 14,000 |  | 14,000 |  | 14,000 |  | 14,500 |
| 2120 Output SGST |  | 14,000 |  | 14,000 |  | 14,000 |  | 14,500 |
| 2200 Vendor payables |  | 1,26,000 |  | 1,26,000 |  | 1,26,000 |  | 66,000 |
| 2310 PF payable |  |  |  | 36,000 |  | 36,000 |  | 36,000 |
| 2320 ESIC payable |  |  |  | 8,000 |  | 8,000 |  | 8,000 |
| 2330 PT payable |  |  |  | 2,000 |  | 2,000 |  | 2,000 |
| 2400 Bank loans |  | 20,00,000 |  | 20,00,000 |  | 20,00,000 |  | 19,60,000 |
| 3000 Owner's drawings |  |  |  |  | 50,000 |  | 50,000 |  |
| 3100 Owner's capital |  | 57,05,000 |  | 57,05,000 |  | 57,05,000 |  | 57,05,000 |
| 4000 Room revenue |  | 4,10,000 |  | 4,10,000 |  | 4,10,000 |  | 4,30,000 |
| 4200 F&B revenue |  | 1,60,000 |  | 1,60,000 |  | 1,60,000 |  | 1,60,000 |
| 4900 Discounts allowed | 10,000 |  | 10,000 |  | 10,000 |  | 10,000 |  |
| 5000 Housekeeping & guest supplies | 59,000 |  | 59,000 |  | 59,000 |  | 59,000 |  |
| 5010 Linen & laundry | 42,000 |  | 42,000 |  | 42,000 |  | 42,000 |  |
| 5030 Electricity, water & fuel | 45,000 |  | 45,000 |  | 45,000 |  | 45,000 |  |
| 5040 Petty cash & sundry | 8,000 |  | 8,000 |  | 8,000 |  | 8,000 |  |
| 5070 OTA commission |  |  |  |  |  |  | 3,540 |  |
| 5080 Bank & card charges |  |  |  |  |  |  | 2,360 |  |
| 5090 Interest on loans |  |  |  |  |  |  | 15,000 |  |
| 5100 Salaries & wages |  |  | 2,40,000 |  | 2,40,000 |  | 2,40,000 |  |
| 5110 Employer PF & ESIC |  |  | 24,500 |  | 24,500 |  | 24,500 |  |
| 5900 Cash short | 200 |  | 200 |  | 200 |  | 200 |  |
| **Total** | **84,29,000** | **84,29,000** | **84,75,000** | **84,75,000** | **84,75,000** | **84,75,000** | **84,27,500** | **84,27,500** |

(1100 Guest ledger, 1115, 1200 and 2300 close at nil in every column; 2000 is nil in the first three because the opening ₹10,000 advance is applied by T5a and the May advance T6 is ACC-2.)

Party balances (rulebook name → contract id): Deccan Agro `1110/<company id>` **63,000 Dr** (1,03,000 after T5b, 63,000 after T7a); Morning Supplies `2200/EV-MORNING` **84,000 Cr** (25,000 opening + T8 59,000); Kaveri Textiles `2200/EV-KAVERI` **42,000 Cr**; HDFC `1030/BA-HDFC` **4,45,000 Dr** in the prototype (3,00,000 + 40,000 − 45,000 + 1,50,000), 2,31,500 on the server (− T13c 2,13,500), 1,81,500 in the brief's subset (− T16); Goibibo `1115/goibibo` nil; Staff-07 `1200/HR-S07` nil (T13a recovered by T13b); the till `1000/TILL` 45,300 Dr.

## Tests — what runs today, what waits

Prototype `tests/stream-accounting-rulebook.mjs` (17 tests):
- **Pass today on this branch alone (5):** the four self-checks of the encoded rulebook (opening ₹77,40,000; 26 entries balance and close to the §8.3 ₹84,27,500; subset arithmetic; Deccan statement in the model) and the scenario-loads test, once `ACC_OPENING_*` exists… *(see below: that test needs acc1-opening)*.
- **Wait for lanes (they fail with `waits for lane <x>: <name> is not exported…` until `claude/acc1` is merged):** `acc1-opening` (scenario load, opening entry, lock refusal / allowance, Manager-enters-Owner-approves), `acc1-parties` (party sub-ledgers, party balances, step-by-step vouchers, every entry resolves), `acc1-statements` (six-column TB, statement, day book), `acc1-chart` (4200 in the closing ledgers).
- **Checked against the lanes' committed work** (scratch overlay of the four lanes' committed prototype states on this branch, outside the repository, conflicts in the append-only files resolved by keeping every line): **16 of 17 pass**. The one failure is the statement title: `accStatement(...).name` reads "Company receivables (city ledger) · CO-xxxx" because acc1-statements has the marked INTEGRATION item "replace `partyName` with `accPartyName` from accounting-parties.ts" — **the integrator wires it**; the test asserts the party's name ("Deccan Agro").
- Full-suite effect on this branch alone: counts in "Test counts" below. The scenario ACC-RULEBOOK also passes the existing "every available scenario loads with its own ID and schema" test once the opening commands exist.

Server `Hcc.Accounting.Tests` (run: `dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1 --logger trx`):
- **Pass today:** `The_json_encodes_the_rulebook…` (opening, 26 balanced steps, §8.3), `Every_driven_step_has_events…`, `Voucher_dates_and_numbers_follow_the_rulebook_calendar` (T8 5 Apr JRN/20260405/001, T7a Receipt 10 Apr, T15a/T15b dates, Deccan 40,000 → 1,03,000 → 63,000), and every `Closing_balance…(account)` row except 4200 (26 of 27 ledgers).
- **Fail until acc1-chart merges (4200 is not in the server chart):** `Closing_balance…(4200)`, `Each_driven_step_posts…` (T2, T3), `Trial_balance_agrees_nothing_in_suspense…`, and the Sql test (`SetMapping line:service → 4200` refuses). Marked `[Trait("Lane","acc1-chart")]`.
- **Skipped with a reason (3):** party balances (acc1-parties: `SourceEntry.Party` is not in this branch), the opening endpoint (acc1-opening: `SaveOpeningBalances` / `ApproveOpeningBalances`), six-column TB / statement / day book (acc1-statements). Each carries the intended assertions in its reason and the figures in `rulebook-month.json`.
- The Sql test publishes the month's 20 events through the real outbox, runs the consumer once (exactly once per event), sets the Owner's mapping, checks every day view (balanced, no error, closed), the bank deposit / cash difference / company payment on their dates, then the cumulative trial balance at 30 April equals `expected.flow` (the month without the opening, which comes through the opening endpoint).
- Not mine: `MappingAndExportTests.Exports_follow_DEC063_G13…` failed once in my run (`Expected 1, Actual 5` at line 116, a voucher count over S2…S2+30 days). That fixture shares one database between its tests, so the count depends on the order xUnit runs them; my tests use their own database. Not touched.

## Test counts

TEST_COUNTS_PLACEHOLDER

## Integration needed (for the coordinator / integrator)

1. Keep the two registration lines (`services/mock/index.ts`: `export * from './rulebook-bind'`; `fixtures/ext/index.ts`: `accountingRulebook` in `extensions`) and the new file `services/mock/rulebook-bind.ts`.
2. `accStatement` / `accTrialBalance6` / `accDayBook` names: replace `partyName` in `accounting-reports.ts` by `accPartyName` (acc1-statements' own note) — then my statement test passes.
3. After `git merge claude/acc1` into this branch: run `npm test`; expected red only where a lane differs from the contract, listed in the failure text. Server: regenerate nothing (`rulebook-month.json` is independent of the code under test).
4. Decide gaps 1–4 above (payroll vouchers in the prototype, a bank drawing, an apply-advance command, a restaurant line class). Until then the prototype suite asserts the "Prototype" column and the server suite the "Server" column.
5. Contract note: "Opening = vouchers before `from`" vs acc1-statements counting the opening journal dated **on** `from` as opening (rulebook §7.2 "incl. opening journal") — my tests accept both for `from` = 1 April and use 2 April for the strict case.
6. Contract note: the contract says the seeded till is `1000/TILL-MAIN`; the master's code is `TILL` and acc1-parties derives `1000/TILL`. The fixture builds the opening cash line from the master's till code.

## Open items

- Re-run the golden suites after the merge; update the "Prototype" / "Server" expectation to the "Brief" column when gaps 1 and 2 are closed (`DRIVEN`/`SERVER`/`BRIEF` in `tests/_rulebook-month.mjs`), and to "Full" in ACC-2.
- The server Sql test does not load the opening balances (endpoint pending); the skipped Domain test records the intended check.
- No UI, no product code touched; `npm run build` not run (nothing user-facing changed except the scenario list gaining one entry).

## HANDOFF bullet

- **ACC-1 tests lane (27 Oct 2026):** scenario `ACC-RULEBOOK` (`src/fixtures/ext/accounting-rulebook.ts`, bound by `services/mock/rulebook-bind.ts`) drives the rulebook's April 2026 day by day through real commands — hand-entered opening ₹77,40,000 (Manager saves, Owner approves), 155 room nights + Owner discount, 20 room-service folio checks, 10 walk-in checks, company credit and the company's bank payment, guest tenders, two vendor bills on credit, electricity by bank, sundry cash, cash banked, ₹200 short — closed 1–30 April. `tests/stream-accounting-rulebook.mjs` (17 tests) and server `Golden/RulebookMonthTests` + `Sql/RulebookMonthSqlTests` + `rulebook-month.json` assert every ledger and party balance of the subset (shared data `tests/_rulebook-month.mjs`); the full month (T4, T6, T7b, T11, T14, T17–T19) is the ACC-2 target. Gaps: the prototype books no payroll vouchers, a drawing from the bank is not an ACC-1 command, no apply-advance command (placeholder tender "Advance adjusted"), folio restaurant lines need a mapping override to reach 4200. Report: `05 Delivery Evidence/Accounting/ACC1-TESTS-DONE.md`.
