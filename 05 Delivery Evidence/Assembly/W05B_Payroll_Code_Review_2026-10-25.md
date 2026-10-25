# W05-B payroll, leave and payslips: code review (25 Oct 2026)

**What was reviewed:** `git diff 7c205d6...claude/w05b-payroll-c92985 -- "03 Prototype"`, which is W05-B's 4 commits (f18409a, 0a8e52b, 051e84c, 558e915) across 11 files, +1,105/−259 lines. I also read the code around the diff in `E:\Projects\HCC\worktrees\w05b` and the merged hub in `E:\Projects\HCC\worktrees\integration` (`src/domain/streams/config.ts`, `hrStatutoryDefs`). All of this was read only. I changed no code and made no commits.
**SCC sources compared:** `payroll\index.html` (calcAttendance 1710, advScheduled 1763, ptFor 1809, calcGM 1821, lockRun 3203, unlockRun 3236) and `leave\index.html` (MAX_DAY 963, leaveDaysOf 1015, ENT_DEFAULTS 1022, isWeekOffDate 1058, decideLeave 1222, getCapacity 1480).
**How findings were checked:** I bundled the mock service with the project's esbuild into my scratchpad folder and ran throwaway Node 25 scripts (t1–t6) against `seed()` and `loadScenario('FY26')`. Nothing was written to the repo. Every finding below is either **Confirmed** (I reproduced it) or **Plausible** (I describe a concrete scenario where it would fail).
Line numbers refer to the w05b branch head 558e915 unless marked *integration*.

## Summary

| Sev | Count | Findings |
|---|---|---|
| S1 | 0 | — |
| S2 | 5 | F-01 LOP leave deducts the weekly off · F-02 bank file uses live bank details, not the locked run · F-03 a non-payroll manager can relink a login and read anyone's payslip · F-04 old saves: no SCHEMA bump or rates migration, so NaN appears in statutory files and payslips · F-05 dated rate versions are full snapshots, so a later version silently reverts an earlier one |
| S3 | 8 | F-06 hub edits are dated from the month after the last locked run (can be retroactive) · F-07 ESIC switches on and off month by month with earned pay · F-08 SCC extra-days rule not ported, and nothing says so · F-09 unlock keeps the "sent" WhatsApp marks and has no record that the bank file was already paid · F-10 self-service money: Accountant sets own salary and gives self an advance; Manager records own overtime · F-11 non-payroll managers change PT slab (gender) and the payslip WhatsApp number · F-12 full and final settlement has no checker; adjustments up to ₹10 lakh each · F-13 R-01: housekeeping and maintenance see their own pay |
| S4 | 4 | F-14 payslip shows fixed "12%" / "0.75%" labels · F-15 cutting an entitlement makes balances negative · F-16 FY26 history gives daily-wage staff sick leave · F-17 statutory CSV has no Draft mark, and CSV cells are not protected against formulas |

**What checks out against SCC:**
- PF: 12% / 13% on min(basic, ₹15,000).
- ESIC: 0.75% / 3.25% on (earned − washing) while earned ≤ ₹21,000.
- Maharashtra PT: nil / ₹175 / ₹200, February ₹300 only on the top slab, women nil up to ₹25,000. Tested at 25,000 / 25,001 / 10,000 / 10,001 / 7,500.
- Pay split: basic 50%, HRA 80% of basic, washing 10% of basic, special is the rest.
- Late marks: 3 lates = ½ day. A half-day = ½ day loss of pay.
- Advances: instalment 0 = recover the full balance.
- Rounding: integer paise via BigInt, half-up. Rupee mode rounds PF to the nearest rupee and ESIC up to the next rupee.

**What checks out in the money flows:**
- Lock is maker-checker. A user who prepared the run cannot lock it (FORBIDDEN), and the run must match a fresh recalculation (STALE_VERSION).
- Unlock is Owner-only, needs a reason, works on the latest locked month only, and reverses that run's advance recoveries.
- Bank CSV rows add up to the run's net total exactly (₹2,08,745.31 for September in BASE).
- Payslip figures come straight from the run line.
- Keyed commands are idempotent.
- FY26 locked runs (Sep, the only month with attendance kept) match their recalculation exactly. The "locked runs verified" test [HIST-C] holds.

---

## S2 findings

### F-01 · A leave-without-pay request that spans a weekly off deducts the weekly off as unpaid · Confirmed
**Where:** `src/domain/streams/staff.ts:199-209` (`hrCounts`). It checks approved full-day leave (line 206) **before** the rostered-off check (line 208).
**What happens:** The request handler skips weekly offs when it counts leave days (`hrLeaveDays`, line 216-219; this is SCC's `isWeekOffDate`). Payroll does not. Any date inside an approved leave counts as a leave day, including the weekly off. For LOP leave that day goes into `lopLeave`, and monthly staff lose it from pay (`hrComputeLine` line 267).
**Reproduced (t1):** Aarav, monthly ₹18,000, weekly off Tuesday. LOP request Mon 9 to Wed 11 Nov 2026. Stored `days` = 2. November payroll counts `lopLeave` = 3 and `payableDays` = 27 instead of 28. That is ₹600 too much deducted. Paid leave across an off day also inflates "Paid leave" on the payslip, although pay is not affected.
**History:** The order dates from before W05-B, but W05-B rewrote `hrCounts` and states the "weekly offs skipped" rule.
**Fix:** In `hrCounts`, when there is no attendance and `hrShiftFor(...)==='OFF'`, count the day as `off` before looking at leave. Equivalently, check `full` only on rostered working days. Add a test: LOP over a weekly off gives `lopLeave` = stored `days`.

### F-02 · The bank transfer file reads live staff bank details, not the locked run · Confirmed
**Where:** `staff.ts:339-344` (`hrBankCsv` uses `hrStaffById(s,l.staffId)` for bank name, account and IFSC) together with `handlers/staff.ts:68-81` (any payroll user can change bank details with no second approval).
**Reproduced (t4):** Accountant prepares September, Owner locks it, then Accountant edits Ravi's account to `999999999999` / IFSC `EVIL0000001`. The next bank file shows the new account against Ravi's locked net of ₹10,876.62. The audit line only says "HR-S05 · Ravi Shinde updated", with no before or after values.
**Why it matters:** This is the classic payroll fraud path. The checker (Owner) approved figures, not where the money goes, and the file can be produced after the lock.
**Fix:**
1. Copy `bankName/accountNo/ifsc` into each `HRLine`, or into a `run.bank` map, at lock time, and build the bank CSV from that copy.
2. Make bank-detail changes a maker-checker request decided by the other payroll user. At minimum, record the masked old and new account in the audit detail.
3. Block changes to your own staff record's bank details.

### F-03 · A manager without payroll rights can relink a demo login and read any staff member's payslips · Confirmed
**Where:** `handlers/staff.ts:56-57,82` (`HR_STAFF_SAVE` lets anyone with `hrCanManage` set or clear `userId`) and `staff.ts:322-326` (`hrVisiblePayslips` decides "own" through `userId`).
**Reproduced (t6):** Manager saves own record HR-S01 with no login, then saves HR-S07 (the Accountant) with `userId:'manager'`. The Manager's "My shifts & pay" now lists Farhan's payslips (net ₹24,873.55). The same trick lets Manager give `housekeeper2` the Accountant's payslips (t4).
**Rule broken:** The page's own promise that "salary figures are visible to the Owner and Accountant only". The audit only says "updated".
**History:** The linking logic predates W05-B, but W05-B made the payslip a prominent card on "My shifts & pay".
**Fix:**
- Treat `userId` linking as a payroll-role or Owner action.
- Block changing your own link.
- Refuse to relink a record that already has payslips in locked runs unless the Owner confirms.
- Audit old → new.

### F-04 · Old saves break: SCHEMA not bumped, old `rates` not migrated, old run lines show NaN · Confirmed
**Where:**
- `src/domain/types.ts:259` (`SCHEMA=7`, unchanged; also unchanged in *integration* `types.ts:262`).
- `staff.ts:91-93,158` (`hrSlice`/`hrRateVersions`/`hrEnsure` fall back to the defaults).
- `staff.ts:306-310,346-353` and `modals.tsx:236-249` (totals, statutory CSV and payslip read the new fields).

**What happens:** The state shape changed:
- `rates` became `rateVersions`.
- `HRLine` gained `earned/hra/washing/special/pfWages/pfEr/esicWages/esicEr/ctc/structure/gender`.
- `HRCounts` gained `ym/HL`.

The project rule is "bump SCHEMA when state shape changes". IndexedDB saves made before W05-B (emulator, Tab S7) load as valid schema 7.

**Reproduced (t3):** I simulated a pre-W05-B slice.
- `hrRunTotals` gives `earned/pfEr/esicEr/ctc` = NaN (null in JSON). The statutory CSV prints "Provident Fund, 11625.01, NaN, NaN".
- The payslip shows ₹NaN for HRA, washing and employer PF/ESIC, and "Half-days" is NaN (`c.H+c.HL`).
- "Rates from undefined" appears in the run header, the payslip and the rates table.
- Old custom rates are silently dropped. Old `pfBps:1000` comes back as the default 1200 for October.
- Old staff have no `gender`, so women default to the men's PT slab.

**Note:** CLAUDE.md says the lead will bump SCHEMA 7 → 8 when `integration/w05` merges to main. That covers this finding **only if** the bump actually ships with W05-B's code. Until then, the integration branch and the APK builds made from it are exposed.
**Fix:** Either bump `SCHEMA` to 8 (old saves reset to FY26, the project's accepted path), or add a one-time migration in `hrEnsure`/restore:
- Map `rates` to one `rateVersions` entry.
- Mark old runs `legacy:true` and fill missing line fields from what can be derived (`earned = basic`, others 0, `ym = run.month`, `HL = 0`).
- Make totals and the payslip tolerate missing fields (`?? 0`).

### F-05 · Dated rate versions are full snapshots copied from the current month, so a later version can silently undo an earlier one · Confirmed
**Where:** `handlers/staff.ts:28` (`base=hrRatesFor(s,s.date.slice(0,7))`), `:206`.
**Reproduced (t3):** Owner saves a version from 2027-01-01 with PF employee 10%, then a version from 2027-04-01 that changes only ESIC to 1%. Result: 2027-01 has PF 10%; 2027-04 has **PF back to 12%** and ESIC 1%. The PF change quietly stops after March. The hub wrappers (*integration* `config.ts:529`) send one field at a time, so they hit the same problem whenever a future version exists.
**Fix:** Build a new version from the version in force on `r.from`, i.e. `hrRatesFor(s, r.from.slice(0,7))`. When later versions exist, warn: "a version from X already exists; this change applies only until then". Or store field-level overrides per date.

---

## S3 findings

### F-06 · Hub edits are dated from the month after the last locked run, which can be a past month · Confirmed
**Where:** *integration* `config.ts:521-529` (the command has no `from`; the hub displays `hrRatesFor(s, curYm)`) together with `handlers/staff.ts:29-30` (default `from` = month after the last locked run).
**Reproduced (t3, BASE):** Business date 24 Oct, last locked month August. A hub edit to PF creates a version **from 2026-09-01**, which changes the already completed September (unlocked). The hub's help text says only "from the 1st of the month". In FY26 the same edit lands on 1 Oct (the current month), which is fine.
**Mismatch with W05-B's own dialog:** the W05-B dialog makes the user pick the month.
**Fix:**
- The hub should send `from` explicitly, defaulting to the next month (or the current month if no run exists yet), and show the date before saving.
- Or W05-B should default to `max(next after last locked, current month)`.
- Warn when a completed month with no locked run will change.

### F-07 · ESIC switches on and off month by month with earned pay · Confirmed (faithful to SCC; statutory risk)
**Where:** `staff.ts:279`.
**Reproduced (t1):**
- ₹22,000 salary, full month: ESIC 0.
- Same staff with 2 days LOP: earned ₹20,533.33, so ESIC ₹146.30 employee and ₹633.97 employer are charged for that month only.
- ₹21,000 salary plus ₹875 OT: ESIC on 19,950 (OT excluded). ₹20,000 salary plus ₹3,333 OT (gross ₹23,333): still covered.

**Statutory position:** The ESIC contribution period (Apr–Sep and Oct–Mar) normally keeps an employee covered for the whole period once they are covered at its start. Coverage is judged on wages, not on a month's LOP. So someone can cross the threshold mid-period in either direction and HCC will deduct inconsistently.
**Fix:** Decide coverage once per contribution period from the contractual wage at the start of the period, store it on the staff record or rate version, and keep contributing until the period ends. CA to confirm (see Items for the CA, item 2).

### F-08 · SCC's extra-days rule and day-based OT were not ported, and nothing says so · Confirmed (difference from SCC)
**Where:** SCC `calcAttendance` lines 1717-1722 versus HCC `hrComputeLine`, `staff.ts:266-273`.
**What SCC does:**
- It adds `extraDays` = 4 when total deductions are ≤ 5.5 days, 2 when they are under 7, and 0 above that. In effect, 4 paid days are forfeited when absence is high.
- It caps "leaves applied" at 5.5 a month.
- It pays surplus days as OT at gross ÷ days.

**What HCC does:** It models weekly offs from the roster, pays them always, and pays OT per minute. The difference can be large. In SCC, 3 absences may still mean full pay; in HCC they cost 3 days.
**Why it matters:** The header comment (`staff.ts:4-6`) lists calcAttendance as ported without mentioning these omissions.
**Fix:** Get an Owner decision. Either port extra-days as an optional rule in the rate version, or record in the Decision Register and the header comment that HCC uses roster weekly offs instead.

### F-09 · Unlocking keeps the "sent" WhatsApp marks and has no record that the bank file was already paid · Confirmed
**Where:** `handlers/staff.ts:234-245` together with `HrDesk.tsx:237` and `modals.tsx:259-261`.
**Reproduced (t2):**
1. Payslip for Ravi sent.
2. Owner unlocks: `sent` is kept.
3. Run recalculated and relocked: the register still shows "Payslip · sent" even though the figures could have changed.

The unlock dialog says payslips are "withdrawn", but staff already have the old figures on WhatsApp. If the first bank file was already used, nothing records "paid". A new full bank file after relock is a double-payment risk, and there is no copy of the original locked figures.
**Fix:**
- On unlock, move `sent` and the locked figures into `unlocks[i]`, and clear `sent`.
- After relock, flag staff whose net changed so payslips are re-sent.
- Add a "bank file issued" flag. Once it is set, unlocking requires confirmation, and the next bank file contains only the differences.

### F-10 · Payroll and manager users can award themselves money · Confirmed
**Where:**
- `handlers/staff.ts:47-86` (no own-record check on salary).
- `:192-202` (`HR_ADVANCE_ADD` has no `isOwn` check).
- `:128-133` (`HR_ATT_MARK` lets a manager mark their own attendance with up to 480 OT minutes).

**Reproduced (t4):**
- Accountant sets own salary to ₹90,000.
- Accountant gives self a ₹50,000 advance (shown as a simulated bank transfer; there is no checker).
- Manager records own attendance "P" with 480 OT minutes.

Lock maker-checker catches salary and OT only if the Owner reads every line.
**History:** Mostly predates W05-B, but W05-B touched these handlers. Its own `HR_FNF_SETTLE` already blocks your own record.
**Fix:** Use the `isOwn()` block that W05-B already has in `HR_FNF_SETTLE` in all three places, or route own changes to the other payroll user or the Owner.

### F-11 · Non-payroll managers change payroll-relevant fields: gender (PT slab) and the payslip WhatsApp number · Confirmed
**Where:** `handlers/staff.ts:77-78`. `gender` and `phone` sit in `base`, outside `statKeys`.
**Reproduced (t4):** Manager changes Ravi to the women's slab (PT ₹200 → 0 below ₹25,000) and changes his mobile number. The next `HR_PAYSLIP_SEND` goes to the new number, which leaks salary to whoever holds it.
**Fix:**
- Add `gender` to the payroll-only fields.
- Treat a phone change on a record that receives payslips as payroll-only, or require confirmation and an audit of old → new.
- Show the destination number on the send button. It is already shown in the dialog.

### F-12 · Full and final settlement has no second approver, and adjustments up to ₹10 lakh each · Confirmed
**Where:** `handlers/staff.ts:256-270`.
**What happens:** One payroll user issues the settlement with up to 4 "add" adjustments of up to ₹10,00,000 each. The full outstanding advance is written off as recovered immediately (line 266).
**Reproduced (t4):** Net ₹20,07,361.54 issued by Accountant alone. Monthly runs need maker-checker; the settlement, which is the largest single payment, does not.
**Fix:** Settle as a Draft, then have the other payroll user or the Owner confirm, as for runs. Post the advance write-off only on confirmation.

### F-13 · R-01: housekeeping and maintenance see their own pay on "My shifts & pay" · Confirmed, needs a decision
**Where:** `register.tsx:15` (nav includes `housekeeping`, which the Maintenance role also holds) and `MyHr.tsx` (`PayslipCard` plus the payslip list).
**Assessment:** Only the user's **own** locked payslip is shown (`hrVisiblePayslips` filters on the linked staff record; the payslip dialog checks `me.id===staffId && Locked`). No hotel money (folios, cash, revenue, other people's pay) reaches these roles. The CLAUDE.md rule "housekeeping/maintenance never see money" was written about hotel money, and every employee is legally entitled to their own payslip. The real exposure is F-03: relinking makes "own" mean someone else's.
**Recommendation:** Keep the feature. CLAUDE.md now cites **DEC-061** ("they may see only their own pay"), which matches this assessment, so only the enforcement gaps remain. Fix F-03 and F-11 so "own" cannot be redirected. Optionally, hide amounts behind a tap ("Show my pay") on shared phones.

---

## S4 findings

### F-14 · The payslip shows fixed rate labels · Confirmed
**Where:** `modals.tsx:244`.
**What happens:** The labels "Provident Fund (12%)" and "ESIC (0.75%)" are hardcoded. After a dated rate change the payslip shows the wrong percentages.
**Fix:** Build the label from `run.rates.pfEeBps/100` and `run.rates.esicEeBps/100`.

### F-15 · Cutting an entitlement makes balances negative · Confirmed
**Where:** `handlers/staff.ts:183-191`, `staff.ts:226-228`.
**Reproduced (t3):** A 4-day CL request is pending, then the Owner sets CL = 2. The balance shows −2 left. Approval is correctly refused afterwards, but approved leave already over the new limit stays and shows negative.
**Fix:** Clamp the displayed value at 0 and add "over by N". Warn in the policy dialog how many staff go over.

### F-16 · FY26 history gives daily-wage staff paid leave types · Confirmed
**Where:** `fixtures/history/modules-people.ts:22-28` (the generator picks from every staff member).
**What happens:** HR-LV-H10 and HR-LV-H12 are sick leave for Imran (daily wage). The live rule allows only LOP for daily-wage staff (`handlers/staff.ts:141`). It shows as "Paid leave" on his payslip but is not paid.
**Fix:** Filter to `payType==='monthly'`, or use LOP for daily-wage staff.

### F-17 · The statutory CSV has no Draft mark, and CSV cells are not protected against formulas · Confirmed
**Where:** `staff.ts:346-353` and `HrDesk.tsx:221` (the PF / ESIC / PT statement can be downloaded for a Draft run, and its footer does not say Draft, unlike the register CSV). `staff.ts:328` (`csvCell` does not neutralise a leading `= + - @` in names or labels).
**Fix:** Add `run.status` to the footer, or allow the file only once the run is locked. Prefix risky leading characters with `'`.

---

## Items for the CA (statutory questions, not bugs)

1. **Labour Codes:** the Code on Wages and the Code on Social Security took effect on 21 Nov 2025. Does the "wages ≥ 50% of remuneration" rule change the PF/ESIC base? HCC's basic is exactly 50%, with HRA 40%, washing 5% and special 5%.
2. **ESIC:**
   - Coverage by contribution period (Apr–Sep, Oct–Mar) versus HCC's month-by-month test on earned pay (F-07).
   - Should coverage be judged on contractual or earned wages?
   - Overtime is currently excluded from ESIC wages. Should it be included?
   - Washing allowance is excluded from ESIC wages. Is that right for this hotel?
3. **PF:**
   - PF is currently on basic only, capped at ₹15,000. After the Supreme Court's 2019 Surya Roshni judgment, should the universally paid special allowance count as PF wages?
   - The employer's 13% is treated as one figure. Should it be split into EPF 3.67%, EPS 8.33% (capped at ₹15,000), EDLI 0.5% and admin 0.5%?
   - Should PF on 'B' (basic-only) and daily-wage staff stay off by default?
4. **Rounding:** the default is to paise, as in SCC. Should the default be rupees for ECR and ESIC filing (PF to the nearest rupee, ESIC up to the next rupee)?
5. **Maharashtra PT:** the base is earned pay excluding OT (SCC's "earned" mode). Should OT count? Example: ₹10,000 earned plus ₹4,167 OT currently pays ₹175, not ₹200. Should PT be charged in a partial exit month?
6. **Overtime:** HCC pays 1× the ordinary rate (SCC). The Maharashtra Shops and Establishments Act and the Factories Act require 2×. Also, monthly staff who work on their weekly off ("Worked on off day") get no pay and no comp-off.
7. **Leave:**
   - Statutory minimums under the Maharashtra Shops and Establishments Act 2017 versus SCC's CL 12 / SL 7 / EL 15.
   - EL carry-forward and accumulation: HCC resets every financial year with no carry-forward.
   - Pro-rata entitlement for staff who join mid-year: HCC gives the full year.
   - Whether EL encashment at exit uses monthly ÷ calendar days (HCC's prefill) or basic ÷ 26.
8. **Full and final:** gratuity after 5 years is not calculated; notice-pay recovery; statutory bonus (Payment of Bonus Act); time limit for paying the settlement.
9. **SCC attendance rules not ported (F-08):** forfeiting the 4 extra days when absence is high, and the 5.5-day leave cap. Are these lawful or company policy?
10. **Advances:** is recovering the full balance in one month allowed when it would take more than 50% of wages (Payment of Wages Act deduction limits)? HCC caps recovery at net pay but has no percentage limit.

## Reproduction scripts
Throwaway scripts in `C:\Users\Sagar\AppData\Local\Temp\claude\E--Projects-HCC\6a150a4d-06c3-4432-94fc-b460e9c5e876\scratchpad\w05b\` (t1.mjs to t6.mjs, run against a bundle of `src/services/mock/index.ts` at 558e915).

## Fixes applied (25 Oct 2026)

Branch `claude/w05b-review-fixes` (worktree `E:\Projects\HCC\worktrees\payfix`, from `integration/w05` 61e1369). Regression tests are in `03 Prototype\tests\stream-payroll-fixes.mjs`, named `[PAY-FIX F-nn]`. Each test was run against the code before its fix and failed, then passed after it. Totals after the fixes: tsc clean, **604/604 tests** (584 + 20), build OK. SCHEMA is unchanged at 7.

| Finding | Commit | Test | What changed |
|---|---|---|---|
| F-01 | 0a71663 | `[PAY-FIX F-01]` | `hrCounts` and `hrDayStatus` check the rostered weekly off before approved leave. LOP over a weekly off now gives `lopLeave` = stored `days`. HCC has no holiday calendar yet; the code comment marks where holidays belong. |
| F-02 | 604091c | `[PAY-FIX F-02]` | Lock copies the bank details into `run.bank`, and the bank CSV reads that copy. Unlock clears it. Later staff-master edits apply only to future runs. The audit shows masked old → new values. You cannot edit your own bank details. Fixture locked runs carry the snapshot too. |
| F-03 | d749d08 | `[PAY-FIX F-03]` | Linking or unlinking a login is Owner-only (payroll + admin). Nobody can change the link on their own record. Relinking a record that has locked payslips needs `confirmRelink`. The audit shows login old → new. The dialog marks the field "Owner only". |
| F-04 | 484b8ca | `[PAY-FIX F-04]` | `hrMigrateLegacy` turns old `rates` into `rateVersions`, keeping custom values. Old runs get a full rate snapshot and their missing fields, and are marked `legacy`. `restoreState` now migrates saves with schema 7…SCHEMA (keyed on schema < 8) instead of resetting them. Totals, CSVs and payslips never show NaN or undefined. |
| F-05 | 78e4e96 | `[PAY-FIX F-05]` | A new version starts from the version in force on its own date, and only the fields in the command change. A changed field carries forward into later versions that had inherited the old value. A later version that set its own value keeps it. The dialog reloads for the chosen month and sends only the fields that changed. |
| F-06 | 5346e27 | `[PAY-FIX F-06]` | `hrRatesDefaultFrom` = max(current month, month after the last locked run). The hub adapter sends `from` and shows "Effective from 1 <month>". If you choose a completed month that is not locked, the audit and the dialog name that month. |
| F-07 | 8b38d1d | `[PAY-FIX F-07]` | Dated `esicPeriod` rule (on in the default version; marked illustrative, CA to confirm). Coverage is decided per contribution period: from the earliest locked run in the period, or else from the contractual wage. ₹22,000 with 2 LOP days: ESIC 0. |
| F-08 | 37eb031 | `[PAY-FIX F-08]` | Header comment, plus an `HR_NOT_PORTED` note on the leave-policy and rates screens. |
| F-09 | 1b2631c | `[PAY-FIX F-09]` | New `HR_PAYROLL_BANK_FILE` records each bank file; a repeat needs confirmation and is audited as REISSUE. Unlock needs `confirmBankFile` once a file was issued. It moves the sent marks, bank files and locked net figures into `unlocks[i]`. After relock, staff whose net changed are flagged "re-send". The bank file that pays only the differences is **not** built. |
| F-10 | 1a70bf7 | `[PAY-FIX F-10]` | You cannot change your own salary or PF/ESIC flags, give yourself an advance, or mark or correct your own attendance or overtime. The error is FORBIDDEN "Self-approval is not allowed". |
| F-11 | 0ce633a | `[PAY-FIX F-11]` | Changing gender (PT slab) or the phone number of an existing record needs Owner or Accountant. Omitting the phone no longer blanks it. The audit shows old → new, with the mobile masked. The send button shows the destination number. |
| F-12 | 3c34c1f | `[PAY-FIX F-12]` | The settlement is saved as a Draft, then `HR_FNF_CONFIRM` (by the other payroll user, never the maker) re-checks the figures, settles and writes off the advance. `HR_FNF_DISCARD` drops a draft. Each adjustment is capped at ₹2,00,000. Additions above ₹1,00,000 in total need the Owner as checker. Both limits are illustrative. |
| F-13 | — | (F-03/F-11 tests) | DEC-061 already covers the policy. The enforcement gaps are closed by F-03 and F-11. |
| F-14…F-17 | aec6a87 | `[PAY-FIX F-14]`…`[F-17]` | F-14: payslip rate labels come from `run.rates`. F-15: `left` is clamped at 0 and shows "over by N"; the policy dialog warns. F-16: daily-wage history leave is LOP, and the random draw is kept so the rest of the history is unchanged. F-17: the statutory CSV footer says Locked or DRAFT, and text cells starting with `= + - @` are neutralised. |
| W05P-1/2, compat | bb3a3d0 | `[PAY-FIX W05P-1]`, `[W05P-2]`, `[COMPAT]` | When Payroll is switched off, the payroll tabs are hidden. Hub rate rows show the real effective rule (new optional `CfgProviderDef.effectiveText`), and the long help appears once per setting. Staff files no longer use `.at()` or `structuredClone`. |
| W05P-3 | 9b87552 | `[PAY-FIX W05P-3]` | 48 px targets and 16 px text for HR and config on the phone shell (`m-staff.css`, `m-config.css`). |

**FY26 locked-run figures (old → new).** The "locked runs verified" test [HIST-C] still passes.
- **F-07:** Neha (₹22,000) is no longer charged ESIC in the months when LOP took her earned pay under ₹21,000.
  - May net ₹1,93,697.62 → ₹1,93,846.79
  - June net ₹2,11,184.79 → ₹2,11,331.09
  - September net ₹2,08,745.31 → ₹2,08,889.00
  - Employee ESIC falls by ₹149.17, ₹146.30 and ₹143.69, and employer ESIC by ₹646.38, ₹633.97 and ₹622.65 in those three months.
- **F-01 and F-16:** these change only the paid-leave, LOP and weekly-off counts in Apr, Jun and Jul (5 lines). No money changes.
- BASE August run: unchanged.

**Not done / needs a decision**
- F-02: bank-detail changes are not a full maker-checker request. They are audited (masked), frozen at lock, and blocked on your own record.
- F-09: there is no bank file that pays only the differences. The Owner is warned and must confirm instead.
- Owner/CA decisions:
  - F-07 rule and its contractual-wage basis (daily wage × 26)
  - F-08 extra-days rule
  - F-12 limits (₹2 lakh per adjustment, ₹1 lakh Owner threshold)
  - F-04: old staff have no gender recorded, so they default to the men's PT slab; payroll must set gender where it applies.
