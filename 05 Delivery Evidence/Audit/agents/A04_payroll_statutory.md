# A04 — Payroll and statutory (independent audit, 26 Oct 2026)

Agent A4 (Opus). Branch `claude/audit-fable-2026-10-26`, read-only on product code. Scope per plan §5 A4: `08 Production/src/Hcc.Domain/Payroll`, `src/Hcc.Application/Payroll`, migrations 0900–0901, `tests/Hcc.Payroll.Tests`, prototype `src/domain/streams/staff.ts` (the prototype's payroll lives there, not in a `payroll*.ts` file), the SCC sources, CA pack P1–P17 / C1–C7 / T1, DEC-056, DEC-061.

Paths below are relative to `E:\Projects\HCC\Hotel Control Centre\`. "Server" = `08 Production/src/…`, "prototype" = `03 Prototype/src/…`. Money in ₹ (paise in the evidence files).

**Evidence produced**
- `05 Delivery Evidence/Audit/probes/A04_recompute.mjs` — my own arithmetic (BigInt paise), no product code imported; rules taken from the CA pack text. Output `evidence/A04/recompute.json`, `recompute-console.txt`, `scenarios.json`.
- `05 Delivery Evidence/Audit/probes/A04_kernel/` (`Program.cs`, `A04_kernel.csproj`) — runs the **server kernel** (`Hcc.Domain.Payroll`, `PayrollCalculator`, prebuilt DLLs, no SQL) on the same inputs. Output `evidence/A04/kernel.json`, build log `kernel-build.txt`.
- `05 Delivery Evidence/Audit/probes/A04_compare.mjs` — field-by-field diff, output `evidence/A04/compare.json`, `compare-console.txt`.
- Server payroll tests: A1's serial run `evidence/A01/dotnet-test.txt` line 140: `Hcc.Payroll.Tests` 32/32 passed (includes `Golden_BASE_august_and_september_payroll_match_the_prototype`, `PayrollRulesTests.cs:264`). I did not re-run them (see Lock log).
- Node v25.9.0 (`E:\Tools\nodejs` first on PATH), recorded in `recompute-console.txt` header via `recompute.json` (`"node"`).

## 1. Findings

| ID | Sev | Area | File:line | Evidence | Impact | Fix | Hours | Conf |
|----|-----|------|-----------|----------|--------|-----|-------|------|
| A04-01 | High | Statutory / F&F | Server `Hcc.Application/Payroll/PayrollCalculator.cs:32-33,44`; `PayrollQueries.cs:148-157`; `Hcc.Domain/Payroll/PayrollFiles.cs:85-104`; `PayrollService.Pay.cs:344` (event with no consumer) | A staff member settled F&F with the exit month's salary inside the settlement is removed from that month's run (`SettledFor`). The PF/ESIC/PT statement is built only from run lines, so the PF, ESIC and PT deducted in the F&F never appear in any statutory file. `FnfSettled` is published but `grep -rl FnfSettled 08 Production/src` finds only the publisher: no accounting voucher either (salary expense, PF/ESIC/PT payable, net payable). | Under-reporting of PF/ESIC/PT for every leaver's last month (ECR / ESIC challan / PT return would omit them); final salary not in the books. Normal use: every exit. | Include settled F&F lines in the month's statutory statement (member row "F&F"), and book an F&F voucher (5100/5110/2310–2330/1200/2300) from `FnfSettled`. | 6 | High |
| A04-02 | High | Attendance → pay | Server `PayrollService.cs:372` (attendance only for the business date), `PayrollService.Leave.cs:42` (no back-dated leave), `PayrollRules.cs:148,183` (unmarked = absent); prototype `services/mock/handlers/staff.ts:148`, `features/staff/HrDesk.tsx:106` | Attendance can be marked or corrected only for today, and not after day close; leave cannot start before the business date. A day nobody marked stays "unmarked" forever and is deducted as loss of pay (monthly) or unpaid (daily). The pay-run only warns ("N unmarked days counted as absent", `PayrollRules.cs:242`); lock is not blocked. | A forgotten mark = a wrong payslip with no in-app correction; only workaround is an F&F-style manual adjustment, which does not exist for a normal run. Common flow. | Manager/Owner correction of past attendance in the open (unlocked) month, audited, maker-checker if it changes pay; or a "regularise" command. | 6 | High |
| A04-03 | Medium | Doc vs code (CA pack) | `05 Delivery Evidence/CA_Question_Pack_2026-10-25.md` "About the app" + "How to reply"; server `PayrollRules.cs:36,186,192,205,209,212`, `PayrollModel.cs:112,218`, `LeaveAndFnf.cs:45,50,66-71` | The pack tells the CA that **every** payroll rate or rule is a configurable dated setting and that "no software change is needed for rates, thresholds or due dates". In code, several thresholds and rules are constants: daily wage × 26 (P5), 8-hour day and days-in-month divisor (P8), 15-minute grace (P9), PF base = basic only (P1), ESIC base excludes washing and OT (P4), PT base excludes OT and applies to any month with pay (P7), F&F adjustment cap ₹2,00,000 and 4 rows (P14), EL encashment formula (P13). Leave entitlements are a setting but **not dated** (`PayrollModel.cs:215`, "Not dated: applies to new requests"). Full table in §3. | A CA answer of "change to …" on any of these needs a developer, not Master configuration; the owner has been told otherwise. | Correct the pack's two sentences (list which answers are settings and which are code), or make the listed items dated settings. | 1 (doc) / 12 (code) | High |
| A04-04 | Medium | Daily wage / doc vs code | Server `PayrollRules.cs:190`; CA pack P9 | P9 says "Weekly offs are paid". For daily-wage staff the kernel pays only days worked (P, L, worked-off-day, half-days); a rostered weekly off is not paid. Probe: Imran Shaikh (₹600/day) would earn ₹2,400 more in Aug 2026 and ₹3,000 more in Sep 2026 if weekly offs were paid (`recompute.json` → `variantWeeklyOffsPaid`). My reading of the law (not verified, CA to confirm): Maharashtra shops law has long required a daily-wage worker to be paid for the weekly holiday. | CA is answering a question that does not describe the daily-wage behaviour; possible under-payment of daily staff. | Ask the CA explicitly (new question P9a); if confirmed, add a dated "daily weekly-off paid" setting. | 2 | High (code) / Low (law) |
| A04-05 | Medium | Holidays | Server `PayrollRules.cs:141` (comment "holidays would belong in this branch too"), no holiday model anywhere in `Hcc.Domain/Payroll`; SCC `payroll/index.html:1606` (`totalWorkingDays` = days − holidays) | HCC has no paid/festival-holiday calendar; SCC had a holidays field. A holiday not worked is "unmarked" → loss of pay (and combined with A04-02 cannot be fixed later). Not asked in the CA pack (my reading: Maharashtra law provides paid festival holidays — CA to confirm). | Staff lose pay on public holidays unless a manager marks each person Present that day. | Add a dated holiday calendar treated like weekly offs (paid, worked = OFFW). New CA question. | 6 | High (code) / Low (law) |
| A04-06 | Medium | Advances / F&F | Server `PayrollService.Pay.cs:340-341`; `FnfRules.Compute` `LeaveAndFnf.cs:53-62`; prototype `HrDesk.tsx:290` | On F&F confirmation the whole advance balance is recorded as recovered even when the settlement net is negative ("recoverable from employee"). The amount still owed then exists only as a negative net on the F&F record; the advance shows ₹0 and nothing tracks collection. | Money owed by a leaver silently disappears from the advance register. Edge case (net < 0). | Record only the part actually covered by the settlement; keep the rest as an open receivable (or block confirmation with negative net without an Owner note). | 3 | High |
| A04-07 | Medium | Advances accounting | `Hcc.Domain/Accounting/ChartOfAccounts.cs:36,93` (1200 Staff advances credited on recovery); `PayrollService.Pay.cs:44` publishes `AdvanceGiven` — no consumer (grep) | Recoveries credit 1200 but giving an advance is never debited to 1200 (nor credited to bank). After the first recovery, 1200 runs a credit balance. (Accounting side is A3b's area — flagged for cross-check.) | Trial balance shows a wrong staff-advances balance. | Book `AdvanceGiven` (Dr 1200 / Cr bank). | 2 | High |
| A04-08 | Medium | Rounding default (P6) | Server `PayrollModel.cs:162-163` (`StatutoryRounding.Paise` default) | Default is paise rounding; my understanding (CA to confirm) is that EPF ECR is in whole rupees and ESIC contributions are rounded up to the next rupee. The statutory CSV therefore will not match portal figures by default (e.g. E5: ESIC ₹149.63 vs ₹150). Setting exists ("rupee"). | Reconciliation differences in every filing until changed. | Ask CA (P6 already), then flip the dated setting; consider rupee as default. | 0.5 | Medium |
| A04-09 | Medium | Overtime (P8) | Server `PayrollModel.cs:162` (`OtBps 10000`), `PayrollRules.cs:186,192` | OT is paid at 1× (dated setting), base = whole monthly pay ÷ days ÷ 8. CA pack itself notes the Acts are understood to require 2×. No comp-off or extra pay for work on a weekly off for monthly staff (`OFFW` is just a paid day, `PayrollRules.cs:183`). No OT hour limit beyond 480 min/day input (`PayrollService.cs:361`). | Possible under-payment of OT for all staff if 2× applies. The rate is a setting; the base and weekly-off rule are code. | CA answer → dated setting for rate; code change for base / weekly-off. | 0.5 / 4 | High (code) |
| A04-10 | Medium | Advance limit (P11) | Server `PayrollRules.cs:213-214`; `PayrollService.Pay.cs:22` | Recovery is capped only at net pay; probe E10 (₹10,000, 25 days absent, ₹5,000 instalment) recovers ₹1,554.79 and leaves **net ₹0.00** (kernel agrees). Advance size has no relation to salary (₹1 to ₹10,00,000). My reading (CA to confirm): the Payment of Wages Act limits total deductions to 50 % of wages. | Payslips with zero take-home pay. | Dated setting "maximum recovery % of net/wages"; CA P11. | 3 | High (code) |
| A04-11 | Medium | Not calculated, not disclosed | Prototype `features/staff/HrDesk.tsx:300`, `features/staff/modals.tsx:271`; no other mention of bonus / notice pay / TDS in `03 Prototype/src` (grep) | The UI states only "Gratuity is not calculated" (F&F card and modal; the modal says "in this prototype" even in the production build). Notice-pay recovery, statutory bonus and TDS on salary are not calculated and the payslip, run and F&F screens do not say so. Labour Welfare Fund (Maharashtra, half-yearly employee/employer contribution — my reading, CA to confirm) and minimum-wage checks are absent and not in the CA pack. | Owner may assume the F&F/payslip is complete. | One "Not calculated: gratuity, notice pay, bonus, TDS on salary, LWF" note on payslip, run and F&F; add LWF + minimum wage to the CA pack. | 1 | High |
| A04-12 | Low | Exit date | Server `PayrollService.cs:284` (`ExitedOn = BusinessDate`), `:374` | The exit date is always the business date (cannot record a past or future last day); after the exit is recorded the same day's attendance can no longer be marked (inactive), so an unmarked exit day becomes loss of pay in the F&F. | Wrong last-day pay if the exit is recorded before attendance; F&F needs manual adjustment. | Allow an explicit last working day; mark attendance up to it. | 2 | High |
| A04-13 | Low | Bank file | Server `SqlPayrollStore.cs:663-666` (`ISNULL(... '')`), `PayrollRules.cs:247` (lock errors = salary only), `PayrollFiles.cs:76` | Lock freezes bank details even when empty; bank file then has rows with blank account/IFSC. Nothing warns. F&F net payable is never put in any bank file ("simulated transfer"). | Upload rejected / a leaver paid outside any record. | Warning or lock error for missing bank details; list F&F payments in a bank file or a payment record. | 2 | High |
| A04-14 | Low | ESIC coverage flag | Server `PayrollRules.cs:218` (`EsicCovered = EsicOn && covered`), `:233-234` | The stored coverage mixes "ESIC applicable" (a per-person switch) with "covered by wage". If ESIC was switched off for a person in the first locked month of a period, switching it on later in the same period leaves them uncovered until the period ends. | Missed ESIC for the rest of the period in that edge case. | Store wage coverage separately from the applicability switch. | 1 | High |
| A04-15 | Low | Night-shift lateness | Server `PayrollRules.cs:50` | Night (N) shift self check-in is always "Present"; lateness is never recorded for night staff (security guard HR-S12). | Late-mark rule (P9) does not apply to night staff. | Handle the 23:00 start across midnight. | 1 | High |
| A04-16 | Low | F&F constants | Server `LeaveAndFnf.cs:45,48,50`; RBAC `0901_payroll_rbac_seed.sql:42` | The ₹1,00,000 Owner threshold is enforced by the PERM-HR-008 role limit, but the text shown to users uses the constant `FnfRules.OwnerAbove`; the ₹2,00,000 per-adjustment cap and 4 rows are constants (DEC-066). Changing the role limit would make the message wrong. | Misleading message after a policy change. | Read the threshold from the grant. | 1 | High |
| A04-17 | Info | Golden recomputation | §2 | My independent arithmetic equals the prototype golden (which the server test asserts) for Ravi Aug/Sep and Imran Aug/Sep, field by field; kernel probe equals both: 24 of 24 comparisons, 0 mismatches (`evidence/A04/compare.json`). | Confirms PF/ESIC/PT/OT/advance arithmetic for these cases. | — | — | High |
| A04-18 | Info | Lock / unlock / files | §4 | Maker-checker lock (service + DB check `CK_pay_run_lock`), stale-draft recompute check, frozen bank details per lock, Owner-only unlock with reason and history, re-issue confirmation, DRAFT footer, formula guard, masked account numbers on screens and audit — all present. | Good. | — | — | High |
| A04-19 | Info | DEC-061 | §5 | Server and prototype both restrict Housekeeping and Maintenance to their own locked payslips and own staff record; no money permission is granted to HKP/MNT. | Good. | — | — | High |

## 2. Recomputation table

Inputs: staff values from the BASE fixture `03 Prototype/src/fixtures/ext/staff.ts:11-24` (Ravi Shinde HR-S05 ₹13,500/month, man, weekly off Friday, advance ₹3,000 at ₹1,000/month from Aug 2026, `:47`; Imran Shaikh HR-S10 ₹600/day, man, weekly off Tuesday, joined 1 Jun 2026), attendance from the golden export of that fixture (`08 Production/tests/Hcc.Payroll.Tests/Golden/payroll-base.golden.json`), October regenerated with the fixture's own data generator (`staff.ts:27-35`). Rates = CA pack P1–P8 (default dated version from 1 Apr 2025).

### 2a. Monthly payslip — Ravi Shinde, August 2026 (31 days)
Counts (mine = golden): Present 23, Late 2, Half-day 1, Absent 1, weekly offs 4 (paid), OT 360 min.
| Item | My arithmetic | ₹ |
|---|---|---|
| Payable days | 31 − 1 absent − ½ half-day (2 lates < 3, no penalty) | 29.5 |
| Earned | 13,500 × 29.5 / 31 | 12,846.77 |
| Basic 50 % / HRA 80 % of basic / washing 10 % of basic / special | | 6,423.39 / 5,138.71 / 642.34 / 642.33 |
| OT | 13,500 / 31 / 8 / 60 × 360 × 1 | 326.61 |
| Gross | earned + OT | 13,173.38 |
| PF employee 12 % / employer 13 % of min(6,423.39, 15,000) | | 770.81 / 835.04 |
| ESIC 0.75 % / 3.25 % of (12,846.77 − 642.34 = 12,204.43); covered (13,500 ≤ 21,000) | | 91.53 / 396.64 |
| PT (man, > ₹10,000, not Feb) | | 200.00 |
| Advance recovery min(1,000, balance 3,000, gross − statutory) | | 1,000.00 |
| **Net** | 13,173.38 − 770.81 − 91.53 − 200 − 1,000 | **11,111.04** |
| CTC | gross + 835.04 + 396.64 | 14,405.06 |

September 2026 (30 days; CL 14–15 Sep paid): payable 28.5, earned 12,825.00, OT 112.50, PF 769.50, ESIC 91.38, PT 200, advance 1,000, **net 10,876.62**.

### 2b. Daily-wage payslip — Imran Shaikh, August 2026
Counts: Present 23, Late 1, Half 1, Absent 2, weekly offs 4 (not paid for daily staff in code), OT 180 min.
| Item | ₹ |
|---|---|
| Payable days 23 + 1 + ½ | 24.5 |
| Earned 600 × 24.5 (basic only, PF off by default, P3) | 14,700.00 |
| OT 600 / 8 / 60 × 180 | 225.00 |
| Gross | 14,925.00 |
| ESIC 0.75 % / 3.25 % of 14,700 (covered: 600 × 26 = 15,600 ≤ 21,000) | 110.25 / 477.75 |
| PT | 200.00 |
| **Net** | **14,614.75** |
If weekly offs were paid as P9 literally says: earned 17,100, net 16,996.75 (+₹2,382 net) — A04-04.
September: earned 14,400, OT 375, ESIC 108.00, PT 200, **net 14,467.00**.

### 2c. Advance recovery
Ravi: Aug ₹1,000 (instalment), Sep ₹1,000 (balance 2,000 → 1,000). Edge E10 (₹10,000/month, 25 days absent, instalment ₹5,000): earned 1,666.67, PF 100, ESIC 11.88, PT 0 (≤ ₹7,500), recovery capped at 1,554.79, **net 0.00**. E11 (instalment 0 = full balance ₹4,000): recovered 4,000.

### 2d. Full and final — Ravi exits 24 Oct 2026 (business date), August locked, September not locked
Counts Oct 1–24: Present 18, Late 1, Absent 1, weekly offs 4, not in service 7 (25–31 Oct), OT 240 min → payable 23 days.
| Item | ₹ |
|---|---|
| Earned 13,500 × 23 / 31 | 10,016.13 |
| OT | 217.74 |
| Final-month gross | 10,233.87 |
| + EL encashment (15 EL × 13,500 / 31, P13) | 6,532.26 |
| Earnings | 16,766.13 |
| − PF 600.97, ESIC 71.36, PT 200.00 (part month still ₹200: earned > ₹10,000) | 872.33 |
| − **Whole** outstanding advance (3,000 − 1,000 recovered in August; not the ₹1,000 instalment) | 2,000.00 |
| − Uniform deduction | 500.00 |
| Recoveries | 3,372.33 |
| **Net payable** | **13,393.80** |

### 2e. Comparison with the server
| Case | Mine | Prototype golden (asserted by server test `PayrollRulesTests.cs:264`) | Server kernel probe (`kernel.json`) |
|---|---|---|---|
| Ravi Aug 2026 net | 11,111.04 | 11,111.04 — all 20 fields equal | 11,111.04 — all fields equal |
| Ravi Sep 2026 net | 10,876.62 | 10,876.62 — all fields equal | 10,876.62 — all fields equal |
| Imran Aug 2026 net | 14,614.75 | 14,614.75 — all fields equal | 14,614.75 — all fields equal |
| Imran Sep 2026 net | 14,467.00 | 14,467.00 — all fields equal | 14,467.00 — all fields equal |
| Ravi F&F net | 13,393.80 | (no golden) | 13,393.80 — line, outstanding ₹2,000, EL ₹6,532.26, earnings ₹16,766.13, recoveries ₹3,372.33 all equal |
| 14 edge scenarios E1–E14 (Feb PT ₹300 / ₹175, women ₹25,000 nil / ₹25,000.01 ₹200, ESIC at ₹21,000, ₹22,000 with LOP not covered, raised to ₹23,000 in Dec still covered, OT 600 min = ₹500, 3 lates = ½ day, advance cap, full-balance advance, daily ₹850 × 26 not covered, PF cap ₹15,000, part-month joiner PT nil) | `recompute.json` → `edges` | — | 14 of 14 equal, field by field (`compare.json`: 0 mismatches) |

## 3. Every rate / rule → where it lives → CA id → what a different CA answer needs

S = dated setting (Master configuration › Statutory rates; server `RateVersions.cs` fields, Owner-only `SetStatutoryRates`, PERM-HR-009, history in `hr.rate_change`); H = hard-coded; A = absent.

| Rate / rule | Where | Current value | CA id | If CA says otherwise |
|---|---|---|---|---|
| Basic % of earned, HRA % of basic, washing % of basic | S `PayrollModel.cs:155,162` | 50 % / 80 % / 10 % | P1 | Setting |
| Special allowance = balance | H `PayrollRules.cs:203` | balance | P1 | Code |
| PF wages = basic only (special allowance excluded) | H `PayrollRules.cs:205` | min(basic, cap) | P1, P16 | Code |
| PF EE / ER %, wage cap | S `PayrollModel.cs:156` | 12 % / 13 % / ₹15,000 | P1 | Setting |
| Employer PF split EPF / EPS (cap) / EDLI / admin | A (one 13 % figure) | — | P2, P17 | Code (statement + accounting) |
| PF default per person (structured on, basic-only/daily off) | H `PayrollModel.cs:102`; per-person override in staff master | — | P3 | Per-person setting; default = code |
| ESIC EE / ER %, ceiling | S `PayrollModel.cs:157` | 0.75 % / 3.25 % / ₹21,000 | P4 | Setting |
| ESIC wages = earned − washing, OT excluded | H `PayrollRules.cs:209` | — | P4 | Code |
| ESIC contribution-period rule on/off | S `esicPeriod` `PayrollModel.cs:159` | on | P5 | Setting |
| Period months Apr–Sep / Oct–Mar | H `PayrollModel.cs:65` | — | P5 | Code |
| Earliest locked month decides; else contract wage | H `PayrollRules.cs:226-235` | — | P5 | Code |
| Daily contract wage = daily × 26 | H `PayrollModel.cs:112` | 26 | P5 | Code |
| Rounding paise / rupee | S `PayrollModel.cs:159`, `PayrollRules.cs:25-30` | paise | P6 | Setting |
| PT slab amounts and limits | S `PayrollModel.cs:158` | 7,500 / 10,000 / 175 / 200 / Feb 300 / women 25,000 | P7 | Setting |
| PT slab shape (3 male tiers, 1 female, Feb only on top) | H `PayrollRules.cs:155-163` | — | P7 | Code |
| PT base = earned without OT; charged in any month with pay (incl. part month) | H `PayrollRules.cs:212` | — | P7 | Code |
| Gender default M | H `PayrollModel.cs:107` | M | P7 | Code |
| OT multiplier | S `otBps` | 1× | P8 | Setting |
| OT base monthly ÷ days in month ÷ 8 h; daily ÷ 8 h | H `PayrollRules.cs:186,192` | — | P8 | Code |
| Weekly-off work: no extra pay / comp-off | H `PayrollRules.cs:183` (OFFW paid as normal day) | — | P8 | Code |
| Late marks per ½ day | S `lateBlock` | 3 | P9 | Setting |
| Grace minutes | H `PayrollRules.cs:36` | 15 | P9 | Code |
| Shift start times | H `PayrollRules.cs:38-45` | 07/15/23/09 | P9 | Code |
| Half-day = ½ day LOP; unmarked = absent | H `PayrollRules.cs:183` | — | P9 | Code |
| Weekly offs paid (monthly) / not paid (daily) | H `PayrollRules.cs:183,190` | — | P9 (pack misstates daily) | Code |
| Paid holidays | A | — | none (new) | Code |
| SCC extra-days rule / 5.5-day cap | A (deliberately not ported, `PayrollModel.cs:11-12`) | — | P10 | Code |
| Advance instalment, 0 = full | per advance (data) `PayrollService.Pay.cs:22-24` | — | P11 | — |
| Advance recovery ≤ net, no % limit | H `PayrollRules.cs:213-214` | — | P11 | Code (+ new setting) |
| One open advance per person; advance ₹1–₹10,00,000 | H `PayrollService.Pay.cs:22,39` | — | P11 | Code |
| Leave entitlements CL / SL / EL | S, **not dated** `PayrollModel.cs:215-218`, `SetLeavePolicy` | 12 / 7 / 15 | P12 | Setting (no history by date) |
| FY reset, no carry-forward, full year to joiners | H `LeaveAndFnf.cs` `Used`/`Balance` | — | P12 | Code |
| Daily staff: LOP only | H `PayrollService.Leave.cs:41` | — | P12 | Code |
| Max leave span 15 days | H `LeaveAndFnf.cs:12` | 15 | P12 | Code |
| EL encashment = EL × monthly ÷ days in exit month (prefill, editable) | H `LeaveAndFnf.cs:66-71` | — | P13 | Code (manual edit is the workaround) |
| F&F = final month − statutory − whole advance ± adjustments | H `LeaveAndFnf.cs:53-62` | — | P14 | Code |
| F&F adjustment cap / rows / Owner above | H `LeaveAndFnf.cs:45,48,50`; RBAC limit `0901…sql:42` | ₹2,00,000 / 4 / ₹1,00,000 | P14 (DEC-066) | Code (cap, rows); RBAC data (Owner limit) |
| Gratuity, notice pay, bonus | A | — | P14, P15 | Code |
| Labour Codes | A | — | P16 | Code |
| Payslip / ECR / ESIC / PT layouts | H `PayrollFiles.cs` | own CSV | P17 | Code |
| Compliance due dates GSTR-1 11th, 3B 20th, GSTR-9 31 Dec, EPF 15th, ESIC 15th, PT month end, TDS 7th (Mar → 30 Apr) | S dated rules `Hcc.Domain/Compliance/ComplianceRules.cs:45-48,148`, defaults `:297-306`, `SaveObligation` (`ComplianceCommands.cs:100`) | as listed | C1–C6, T1 | Setting (date); monthly → quarterly (QRMP) = add a new quarterly obligation and switch the monthly one off (frequency of an existing obligation cannot change, `ComplianceCommands.cs:134`) |
| Other obligations | S (add obligation) | 7 + none | C7 | Setting |
| TDS calculation / returns | A | — | T1 | Code |
| LWF, minimum wages | A | — | none (new) | Code |

**Rules that are still guesses, with the line each depends on:** every row above with a CA id — P1 (`PayrollRules.cs:198-205`), P2 (absent), P3 (`PayrollModel.cs:102`), P4 (`PayrollRules.cs:208-211`), P5 (`PayrollRules.cs:226-235`, `PayrollModel.cs:112`), P6 (`PayrollRules.cs:25-30`), P7 (`PayrollRules.cs:155-163,212`), P8 (`PayrollRules.cs:186,192`), P9 (`PayrollRules.cs:36,177,183,190`), P11 (`PayrollRules.cs:213-214`), P12 (`PayrollModel.cs:218`, `PayrollService.Leave.cs:41`), P13 (`LeaveAndFnf.cs:66-71`), P14 (`LeaveAndFnf.cs:53-62`, `PayrollCalculator.cs:56-71`). Of these, a different answer is a **setting change only** for: the percentages and caps of P1/P4, P5 on/off, P6, PT amounts in P7, the OT multiplier in P8, the late-mark count in P9, the entitlements in P12. Everything else is a code change.

## 4. Lock / unlock and files

| Control | Where | Result |
|---|---|---|
| Prepare only for a completed month; locked month cannot be recalculated | `PayrollService.Pay.cs:111,114` | OK |
| Maker-checker lock (preparer cannot lock) | service `:151-152`; DB `CK_pay_run_lock` `0900_payroll.sql:152` (`locked_by <> prepared_by`) | OK, enforced twice |
| Lock refuses a stale draft (fresh recompute must equal stored lines and rates) | `:156-160` | OK |
| Lock errors | `PayrollRules.cs:247` — salary not set only | gap: missing bank details (A04-13) |
| Bank details frozen per lock | `SqlPayrollStore.cs:661-667` → `hr.pay_run_bank` | OK |
| Advance recoveries posted at lock, reversed at unlock (history kept) | `PayrollService.Pay.cs:162-164,198`; `hr.advance_recovery.reversed_*` | OK |
| Who may unlock | Owner only, PERM-HR-009 (`0901…sql:43`), latest locked month only, reason ≥ 5 chars, refused if an F&F exists for that month or later, needs confirmation if a bank file was issued | `PayrollService.Pay.cs:182-196` — OK |
| Audit of unlock | `HR_PAYROLL_UNLOCKED` audit text (reason, recoveries reversed, payslips superseded, bank-file warning) + `hr.pay_run_unlock` row with every net per staff (`SqlPayrollStore.cs:684`); accounting reversal event | OK |
| Bank file | only after lock; re-issue needs confirmation and says "REISSUE — do not pay twice"; first file per lock books the payment voucher | `PayrollService.Pay.cs:224-237`, `PayrollFiles.cs:66-82` — OK |
| Account numbers | masked `••••1234` on screens, board and audit (`PayrollFiles.cs:11`, `PayrollQueries.cs:40`, `PayrollService.cs:246`); the bank file itself carries the full number (needed for payment), with a leading apostrophe, download audited `HR_BANK_FILE_DOWNLOADED` (`PayrollQueries.cs:188`) | OK (by design) |
| Formula-guarded CSV | `PayrollFiles.cs:21-35` (text starting `= + - @ TAB CR` gets `'`; numbers untouched); test `PayrollRulesTests.cs:244` | OK |
| DRAFT footer | statutory `PayrollFiles.cs:103` "DRAFT — NOT FINAL, do not file"; register "Draft run" `:61` | OK |
| PF/ESIC/PT statement completeness | run lines only | gap: F&F leavers missing (A04-01) |
| Payslip delivery (DEC-056) | `SendPayslipAsync` `PayrollService.Pay.cs:245-265`: locked runs only, PERM-HR-006, simulated (nothing sent), masked number in audit; PDF via native share/print hook (prototype `modals.tsx:296-309`) | OK |
| SCC deviations | SCC let advance recovery push net negative (`payroll/index.html:1846-1847`), had a holidays field and day-based OT, single-user F&F with re-auth (`:2985`), F&F rounded each item to the rupee (`:2919-2925`), undated rates (`:3353-3366`). HCC: advance capped at net, no holidays (A04-05), per-minute OT, maker-checker F&F, paise, dated rates. | documented in code `PayrollModel.cs:11-12`, prototype `staff.ts:11-14` (except holidays and F&F rounding, not documented) |

## 5. DEC-061 own-pay visibility trace

DEC-061 (`02 Planning/HCC_Decision_Register.md:86`): Housekeeping and Maintenance may see their own pay and payslip; never hotel money.

**PERM ids** (`08 Production/database/migrations/0901_payroll_rbac_seed.sql:8-43`): PERM-HR-001 staff view (OWN, MGR, ACC; TAD view-only), PERM-HR-002 manage (OWN, MGR), PERM-HR-003 leave decide (OWN, MGR), **PERM-HR-004 self, scope O** (OWN, MGR, REC, CSH, ACC, SUP, **HKP, MNT**, TAD), PERM-HR-005 salary (OWN, ACC), PERM-HR-006 run/files/payslips (OWN, ACC), PERM-HR-007 advance (OWN, ACC), PERM-HR-008 F&F (OWN; ACC limited to ₹1,00,000 additions), PERM-HR-009 rates/policy/unlock/link (OWN). HKP and MNT hold only PERM-HK-001, PERM-HK-003 (HKP), PERM-ROOM-001 (`0101_access_rbac_seed.sql:146-163`) and PERM-HR-004 — none is a money permission (`is_money` = 0).

**Server read gate** (`Hcc.Application/Payroll/PayrollQueries.cs`):
- `GetStaffAsync` `:25-42` — without PERM-HR-001/005 the caller must hold PERM-HR-004 and receives only rows where `UserId == principal`; salary fields shown when `pay || own` (`:35`); PT slab, PF/ESIC flags, bank only for PERM-HR-005.
- `GetPayslipAsync` `:98-115` — without PERM-HR-006: must be the caller's own staff record **and** a locked run, else NOT_FOUND (hides existence).
- `GetMyPayslipsAsync` `:118-134` — PERM-HR-004; own lines of locked runs only.
- `GetBoardAsync` `:218-285` — non-payroll callers get only their own line of locked runs (`:248-251`), no warnings, sends, bank files, unlocks, rate versions, advances or F&F (`:253-281`).
- `GetPayRunAsync`, `GetRatesAsync`, register/statutory/bank CSVs require PERM-HR-006 (`:60,84,138,150,162,175`).
- Login link (what "own" means) is Owner-only and never your own (`PayrollService.cs:167-188`).
- Tests: `PayrollSqlTests.cs:312` (housekeeper sees only August own payslip; another person's payslip NOT_FOUND; GetPayRun FORBIDDEN; maintenance sees own), `PayrollBoardTests.cs:19` (housekeeper board = own record, own line of the locked run only, no draft). A1's run: 32/32 passed.

**Prototype `can()` path:**
- `can()` `03 Prototype/src/domain/selectors.ts:32`; grants `fixtures/base.ts:41-45` — Housekeeper `['housekeeping']`, Maintenance `['housekeeping']`.
- `hrCanPay` `domain/streams/staff.ts:219` = role in {Owner, Accountant} **and** `can('reports')`; `hrCanManage` `:217` = `can('approve')`. Both false for HK/MNT.
- Nav: "Staff & payroll" needs `reports` (`features/staff/register.tsx:14`) → hidden; "My shifts & pay" needs any of `housekeeping, reservations, payments, reports, admin` (`:15`) → shown.
- `HrDesk` `features/staff/HrDesk.tsx:33-37`: no manage/pay → falls back to `MyHr`.
- `MyHr` `features/staff/MyHr.tsx:45` uses `hrVisiblePayslips` (`staff.ts:449-452`: locked runs, `pay || line.staffId === me.id`), then filters again to `me.id`.
- Payslip modal `features/staff/modals.tsx:300`: `allowed = pay || (me.id === staffId && run Locked)`.
- Production build: `clients/sync/staff.ts:102` loads `/payroll/staff` and `/payroll/board`, so the browser only ever holds what the server gate above returns (the demo build keeps all fictional data in memory; gating there is UI-only, acceptable for the demo).

Conclusion: confirmed in code and tests that HK/MNT see only their own pay and payslips and no hotel money in the payroll area. Other areas (folios, rooms board rates, reports) are A5's route table. **PERM ids touched, for A5:** PERM-HR-001…009, PERM-HK-001, PERM-HK-003, PERM-ROOM-001.

## 6. What is not calculated, and whether the UI says so

| Item | Calculated? | UI says so? |
|---|---|---|
| Gratuity (and gratuity provision, P15) | No | Yes — F&F card `HrDesk.tsx:300` and F&F modal `modals.tsx:271` ("…in this prototype", also shown in the production build) |
| Notice-pay recovery | No (only a manual F&F deduction) | No |
| Statutory bonus | No | No |
| TDS on salary | No; compliance calendar tracks the deposit date only (T1) | No on payslip/run; compliance page says "Nothing is filed from here" (`CompliancePage.tsx:40`) |
| Leave encashment | Prefill only, editable (P13) | Yes ("EL encashment" button, modal) |
| Employer PF split, EDLI, admin charges | No (P2) | No |
| Labour Welfare Fund, minimum-wage check | No; not in the CA pack | No |
| Paid holidays | No | No |
| Filing / payment | Nothing filed or paid; bank file and payslip send simulated | Yes (bank file footer, statutory footer, advance and F&F text) |

## 7. Lock log
- 15:05 `locks/dotnet-test.lock` held by A1 (`A1 2026-10-26T14:56:50`); static work and the Node probe done meanwhile. A1's serial run already covers `Hcc.Payroll.Tests` (32/32, `evidence/A01/dotnet-test.txt:137-140`), so I did not re-run that project.
- 15:15 A1 released; A9 took the lock at 15:15:19. 15:44:20 A4 took it (`A4 2026-10-26T15:44:20+05:30`), built the kernel probe in the scratchpad (`dotnet build`, 0 warnings, `evidence/A04/kernel-build.txt`) and ran it in memory (`kernel.json`); 15:45:26 A4 deleted its own lock. No `dotnet test` run by A4.
- No SQL database touched; the kernel probe is in-memory only.

## 8. Files reviewed
Server: `src/Hcc.Domain/Payroll/{PayrollModel,PayrollRules,RateVersions,LeaveAndFnf,PayrollFiles}.cs`; `src/Hcc.Application/Payroll/{PayrollService,PayrollService.Pay,PayrollService.Leave (parts),PayrollCalculator,PayrollQueries,PayrollPorts (records)}.cs`; `src/Hcc.Api/Endpoints/PayrollEndpoints.cs`; `src/Hcc.Infrastructure.SqlServer/Payroll/SqlPayrollStore.cs` (lock/unlock/bank parts); `src/Hcc.Application/Accounting/AccountingEventMapper.cs:75-110`; `src/Hcc.Domain/Accounting/ChartOfAccounts.cs`; `src/Hcc.Domain/Compliance/ComplianceRules.cs`, `src/Hcc.Application/Compliance/ComplianceCommands.cs:100-157`; migrations `0900_payroll.sql`, `0901_payroll_rbac_seed.sql`, `0101_access_rbac_seed.sql` (HKP/MNT rows); tests `Hcc.Payroll.Tests/{Domain/PayrollRulesTests,Sql/PayrollSqlTests (F12, DEC061),Sql/PayrollBoardTests,Harness/PayrollGolden}.cs`, `Golden/export-payroll-golden.mjs`, `payroll-base.golden.json`.
Prototype: `src/domain/streams/staff.ts`, `src/domain/selectors.ts:32`, `src/fixtures/base.ts:41-45`, `src/fixtures/ext/staff.ts`, `src/features/staff/{register,HrDesk,MyHr,modals}.tsx`, `src/services/mock/handlers/staff.ts:147-159`, `src/clients/sync/{staff,reads,payfiles}.ts` (headers).
SCC: `reference-inputs/SCC-modules/payroll/index.html` (calcAttendance, advScheduled, ptFor, calcGM, fnfAssemble/fnfCompose, settings).
Docs: Audit plan §3–5, Audit README, CA pack §4–6, Decision Register DEC-056/061.

## Claims I could not confirm
- That the prebuilt DLLs used by the kernel probe equal the current branch source (they were built by A1 at 14:57 today from this checkout; I did not rebuild).
- Any statement above marked "my reading of the law": daily-wage weekly-off pay, paid festival holidays, 2× overtime, 50 % deduction limit, ECR/ESIC rupee rounding, Labour Welfare Fund. These are what I think the law requires, not verified; they are for the CA.
- Whether the EPF and Gratuity Acts apply at all to a 12-staff hotel (EPF threshold 20; gratuity 10 — fictional staff count, real headcount unknown).
- The prototype payroll test files (`tests/stream-payroll-scc.mjs`, `stream-payroll-fixes.mjs`) were not run on their own (the runner has no file filter; A1 runs the full suite).

## Open owner/CA decisions I hit (and the code that depends on each)
- P1 / P16 PF base and Labour Codes wage definition → `PayrollRules.cs:198-205`.
- P2 employer PF split → absent; `PayrollFiles.Statutory`, accounting 2310.
- P4 ESIC base (washing, OT) → `PayrollRules.cs:209`.
- P5 contribution-period rule and × 26 → `PayrollRules.cs:226-235`, `PayrollModel.cs:112`.
- P6 rounding default → `PayrollModel.cs:163`.
- P7 PT base and part month → `PayrollRules.cs:155-163,212`.
- P8 OT rate and weekly-off work → `PayrollModel.cs:162` (otBps), `PayrollRules.cs:183-192`.
- P9 grace/late/unmarked/weekly offs (incl. new daily-wage question, A04-04) → `PayrollRules.cs:36,177-190`.
- P10 SCC extra-days rule (not ported) → `PayrollModel.cs:11-12`.
- P11 advance limit → `PayrollRules.cs:213-214`.
- P12 leave entitlements / carry-forward / pro-rating → `PayrollModel.cs:215-222`, `LeaveAndFnf.cs`.
- P13 encashment formula → `LeaveAndFnf.cs:66-71`.
- P14 / P15 gratuity, notice pay, bonus, F&F payment deadline → `LeaveAndFnf.cs:53-62`.
- DEC-066 F&F limits → `LeaveAndFnf.cs:45-50`, `0901…sql:42`.
- New (not in the pack): paid holidays (A04-05), Labour Welfare Fund, minimum wages, daily-wage weekly off (A04-04).
- C1 QRMP → needs a new obligation (frequency fixed per obligation).

## Questions for the lead
1. A04-01 and A04-07 overlap with A3b (accounting): please merge or cross-reference with A3b's findings on F&F/advance vouchers.
2. Should the CA pack correction (A04-03) go into the owner report as a "send before the CA meeting" item? The pack is what the CA will answer from.
3. A04-02 (no past attendance correction) is the one I would rate as a go-live blocker for payroll; please confirm the grade.
