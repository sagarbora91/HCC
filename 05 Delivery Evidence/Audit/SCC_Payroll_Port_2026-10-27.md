# SCC payroll rules in HCC, in full (27 Oct 2026, DEC-115)

Owner, 27 Oct 2026: *"Import the entire payroll rules from SCC."* Source: `E:\Projects\HCC\Migration data on 25 oct\HCC-Migration-2026-10-25\reference-inputs\SCC-modules\payroll\index.html` (calcAttendance, calcGM, ptFor, advScheduled, the DSR/Leave feed `attFeedDiff`, runBlockingErrors). Branch `claude/scc-payroll`. Replaces DEC-099 ("extra days not ported").

## Rule by rule

| SCC rule (payroll\index.html) | HCC prototype | HCC server | Test |
|---|---|---|---|
| Total days = days in month (or "Days Override") − "Mgmt Holidays" (`totalWorkingDays`) | `hrSccTotalDays`: calendar days or the rate version's `daysOverride`, less the paid holidays of the Holiday calendar in the month (`counts.mgmtHol`) | `PayrollRules.SccTotalDays`, `PayrollRates.DaysOverride`, `AttendanceCounts.MgmtHol` | `[PAY-SCC-PORT] total days…`; `DEC083_DEC115_paid_holiday…` |
| Feed: absent = days − present − leave − half; half = half days; leaves = approved leave (`attFeedDiff`) | `hrSccDays`: present = P + L + worked weekly offs (OFF) + no-thumb days + the worked half of a half-day leave; leave = approved **paid** leave; half = H; absent by subtraction (weekly offs taken, A, unmarked, leave without pay, days out of service) | `PayrollRules.SccDays` (same) | grid tests below |
| Late deduction = ½ day per 3 late marks (floor) | `lateBlock` (default 3) | `LateBlock` | grid |
| Half day = ½, No Thumb = ½ (`noThumb × 0.5`) | new attendance mark **NT** "No thumb punch" (present, ½ day) | `AttendanceStatus.NT`, migration **4100** (status list) | `[PAY-SCC-PORT] No Thumb…`, `DEC075_raises_pay…` |
| Total deduction = late + absent + half + no-thumb (each rounded to ½) | same | same | grid |
| Extra days: ≤ 5.5 → 4, 6–6.5 → 2, ≥ 7 → 0 | `hrSccExtra` | `SccExtra` | `[PAY-SCC-PORT] the extra-day bands…` |
| Final present = total − deduction; salary days = min(total, final + leaves + extra); OT days = the rest | `hrSccDays` | `SccDays` | grid: **9,000+ points equal SCC's own calcAttendance** (prototype oracle = SCC's code verbatim; server oracle = C# transcription) |
| grossPayable = gross × salary days ÷ total days; OT = gross ÷ total days × OT days (outside PF / ESIC / PT) | `hrComputeLine`; OT day rate `otBps` (1×) | `ComputeLine` | `[PAY-SCC-PORT] pay equals SCC calcGM…` (420 cases, ≤ 2 paise) |
| Structure S: basic 50 %, HRA 80 % of basic, washing 10 %, special = rest; B: basic only | unchanged | unchanged | existing |
| PF 12 % / 13 % on min(basic, ₹15,000), per-employee toggle | unchanged | unchanged | existing |
| ESIC 0.75 % / 3.25 % on earned − washing while earned ≤ ₹21,000, **month by month** | default `esicPeriod:false` (contribution-period rule now optional) | `Defaults()` EsicPeriod false | `[PAY-FIX F-07]`, `F07_contribution_period…` |
| PT (Maharashtra) on earned pay, ₹300 in February | unchanged | unchanged | existing |
| Advance: EMI or full balance | unchanged (one open advance; recovery capped at take-home — see question 2) | unchanged | existing |
| Issues: leaves > 5.5 and deduction ≥ total days are **risk** (block the lock); high deduction, OT days, ≥ 3 late marks, no-thumb marks warn | `hrSccIssues` → run warnings; `hrRunErrors` | `SccIssues`, `LockErrors`, calculator warnings | `[PAY-SCC-PORT] SCC issues…` |
| No per-minute overtime | overtime minutes are still recorded on attendance but not paid | same | `OT_days_at_gross_over_total_days…` |

Not in SCC, mapped: **daily-wage staff** (SCC has only monthly gross) get gross = daily rate × total days, so pay = daily rate × salary days and OT = daily rate × OT days (`[PAY-SCC-PORT] daily-wage staff…`). Money stays integer paise, rounded at each step (HCC rule): at most 2 paise from SCC's floating-point figures. SCC's slip text "21st to 20th" is only a printed label; SCC computes calendar months, as HCC does.

## Screens and data
- Payslip: "Attendance (SCC)" block — paid holidays, total days, present, absent (incl. weekly offs), half days, no-thumb marks, late marks / deduction, total deduction, leaves, final present, extra days, salary days, OT days; earnings line "OT days (n)".
- Payroll table: days + "+ n OT". Leave policy and statutory-rates screens state the SCC rule (`HR_SCC_DAYS`); the "Not ported from SCC" note is gone.
- New rate setting "Total days of a month" (SCC Days Override, 0 = calendar days) in Statutory rates and Master configuration; "OT day rate".
- Server: `PayLine.Days` (stored in `line_json`), `SccDaysView` on board and pay-run lines, `AttendanceCountsView.MgmtHol / Elapsed / Nt`, `SetStatutoryRatesRequest.DaysOverride`; production client maps them.

## Figures (demo data, by hand in the tests)
- Reference payroll (`payroll-base.golden.json`, regenerated with the day sheet and the 15 Aug holiday): August net **₹2,10,810.40** (was ₹2,12,101.66), September **₹1,99,439.41** (was ₹2,08,889.00). Server = prototype on every line.
- Ravi, September: deduction 5.5 → 4 extra days → 30 salary days + 0.5 OT day, net ₹11,618.81. Aarav: deduction 7 → no extra days → 23 days, ₹13,800.
- A cliff SCC has: one more absent day at deduction 5.5 → 6 costs 2.5 days (4 extra → 2); at 6.5 → 7 another 2.

## Owner answers (27 Oct 2026)
1. **Approved leave counts twice** (SCC's feed takes leave out of "absent" and adds "Leaves" on top; Kavya, 16 Sep absent → CL: +₹900): **exactly as SCC** (DEC-117) — as built.
2. **Advance larger than the take-home**: **carry the rest over** (DEC-118) — as built; net pay never goes negative.

## Tests
Prototype `npm test` **1779/1779** (incl. `stream-payroll-scc-port.mjs`: SCC's own calcAttendance / calcGM as the oracle), both web builds; server `dotnet test Hcc.slnx -maxcpucount:1` **3274/3274** (35 projects; payroll 51 incl. the SCC grid). Golden payroll regenerated (day sheet + paid holidays). Migration 4100 in `migrations.lock.json` (82).
