# HCC Module 17 — HR / Payroll (prototype spec)

Stream `staff` · prefix `hr` (types `HR…`, commands `HR_*`, CSS `hr-`, modals `hr-`) · state slice `s.staffX` (`HRState`).
Status: interactive prototype, fictional staff only. **All deduction rates are synthetic placeholders — "illustrative — not statutory advice".** No claim of PF / ESIC / Professional Tax / Shops & Establishments / Payment of Wages Act correctness.

## 1. Purpose
Give the owner one place to keep the staff master, plan the weekly roster, record daily attendance against the business date, approve leave, and run a monthly payroll with a printable payslip and a register export. Give every staff member with a demo login a self-service view (my roster, mark attendance, my leave, my payslip) that works on the phone.

## 2. Roles and visibility (coarse permissions only)
| Capability | Who | Rule in code |
|---|---|---|
| HR desk page `staff` ("Staff & payroll", ADMINISTRATION) | Owner, Manager, Cashier, Accountant | nav permission `reports` |
| Self-service page `my-hr` ("My shifts & pay", OPERATIONS) | Owner, Manager, Supervisor, Housekeeper, Maintenance | nav permission `housekeeping` |
| Manage staff (non-salary), roster, attendance for others, leave decisions | users holding `approve` (Owner, Manager) | `hrCanManage` = `can(approve)` |
| See salaries, run / lock payroll, advances, payslips of others, register CSV | payroll roles **Owner, Accountant** (role check) + `reports` | `hrCanPay` |
| Change illustrative rates | Owner (payroll role + `admin`) | `hrCanRates` |
| Own record (roster, attendance, leave, own payslips) | the user linked to the staff record | `staff.userId === uid` |

- Manager sees the staff master, roster, attendance and leave but **no salary figures** and no payroll tab.
- Cashier (has `reports`, not a manager or payroll role) sees only "My HR" content on the desk page.
- Tech admin and Reception cannot open either page with the current single-permission nav item (gap, see §9).
- Housekeepers / maintenance never see hotel money; on `my-hr` they see only their **own** payslip.

## 3. Data (state slice `s.staffX`, all optional; absent = empty)
- `depts: HRDept[]` — {id, name}. Fixture: Management, Front Office, Housekeeping, Maintenance, Kitchen, Security, Accounts.
- `staff: HRStaff[]` — {id, name, dept, designation, userId?, payType `monthly|daily`, monthly (paise), daily (paise), defaultShift, weeklyOff (0=Sun…6=Sat), joined, phone (fictional), active}.
- `roster: Record<"date|staffId", shiftCode>` — explicit overrides. Effective shift = override → else weekly off → else default shift.
- `att: Record<"date|staffId", HRAtt>` — {status `P|L|H|A|OFF`, at (clock), otMin, by, note}. P present, L late, H half-day, A absent, OFF worked-off. Leave is derived from approved leave.
- `leaves: HRLeave[]` — {id, staffId, type `CL|SL|EL|LOP`, from, to, days, reason, status `Pending|Approved|Rejected|Cancelled`, requestedBy, decidedBy?, decidedAt?, note?}.
- `advances: HRAdvance[]` — {id, staffId, amount, installment, recovered, date, reason, by, recoveries[{runId, amount}]}.
- `rates: HRRates` — illustrative config (§5).
- `runs: HRRun[]` — {id, month `YYYY-MM`, status `Draft|Locked`, rates snapshot, lines HRLine[], warnings, preparedBy/At, lockedBy/At}.

Shifts (fixed): `M` Morning 07:00–15:00 · `E` Evening 15:00–23:00 · `N` Night 23:00–07:00 · `G` General 09:00–18:00 · `OFF` Weekly off. Late grace 15 minutes.

## 4. Workflows and rules
### 4.1 Staff master — `HR_STAFF_SAVE`, `HR_STAFF_ACTIVE`
- Managers or payroll roles may add/edit. Name ≥ 2 chars, department must exist, user link unique and must be an existing demo user.
- Salary fields (pay type, monthly, daily rate) may be set/changed only by payroll roles. A manager-created record gets salary 0 → payroll warning "salary not set".
- Deactivate = exit; inactive staff are excluded from rosters and from payroll runs for months after their exit.

### 4.2 Roster — `HR_ROSTER_SET`, `HR_ROSTER_COPY`
- Week view Monday–Sunday around the business date. Managers set a shift per staff per day; **only dates ≥ business date** may change (past roster is history). Copy-week copies the effective week onto the following week (only dates ≥ business date).

### 4.3 Attendance — `HR_ATT_MARK`
- Always for the **business date** `s.date`; blocked when the day is closed (`DAY_CLOSED`). Past days are not editable in the prototype.
- Self check-in (linked user, no manager rights needed): status computed from simulated clock vs shift start + 15 min grace → Present or Late. Only once per day.
- Managers can mark/correct anyone: Present, Late, Half-day, Absent, Worked-off; overtime 0–480 minutes (manager only).
- Cannot mark a staff member who has approved leave covering today (cancel the leave first) or inactive staff.

### 4.4 Leave — `HR_LEAVE_REQUEST`, `HR_LEAVE_DECIDE`, `HR_LEAVE_CANCEL`
- Request by the linked user for self, or by a manager for anyone. from ≥ business date, to ≥ from, ≤ 15 days. No overlap with pending/approved leave.
- Days counted exclude rostered weekly offs. Paid types (CL 8 / SL 8 / EL 12 days per year, illustrative) are checked against approved + pending days. Daily-wage staff may request LOP only.
- Decision needs `approve`; **no self-approval** (decider ≠ requester, and decider ≠ the staff member on leave). Requester may cancel only while Pending.

### 4.5 Advances — `HR_ADVANCE_ADD`
- Payroll roles. Amount ₹1–₹10,00,000, installment 1 ≤ installment ≤ amount. Only one open advance per staff. Paid outside the cash drawer (simulated bank transfer) — no effect on cashier figures.

### 4.6 Payroll — `HR_PAYROLL_RUN`, `HR_PAYROLL_LOCK`, `HR_RATES_SET`
- Run only for a **completed month** (month end < business date). Re-running a Draft recomputes it; a Locked run is immutable (`INVALID_TRANSITION`).
- Lock: payroll role, **maker-checker** (locker ≠ preparer, `FORBIDDEN`), and the stored draft must equal a fresh recomputation (else `STALE_VERSION` — "recalculate"). Lock posts advance recoveries.
- Rates changes never alter locked runs (each run snapshots its rates).

## 5. Payroll formula (integer paise; illustrative)
For each active staff member and each day of the month: explicit attendance → status; else approved leave → paid/unpaid leave; else rostered off → off; else **unmarked → counted as absent** (warning listed).
- Late rule: every `lateBlock` lates (default 3; 0 = off) = ½ day loss of pay.
- Monthly: LOP half-units = 2·(A + unmarked + LOP-leave) + H + late penalty. `basic = round(monthly × (2·D − LOP½) / (2·D))`.
- Daily: worked half-units = 2·(P + L + OFF-worked) + H; `basic = round(daily × units / 2)`.
- Hourly rate = monthly/(D·8) or daily/8; `OT = round(hourly × otMin/60 × otMultiplier)` (default 1.5×).
- Gross = basic + OT.
- PF (employee, placeholder) = round(min(basic × basicShare, pfCap) × pfRate) — defaults basicShare 50%, cap ₹15,000, rate 12%.
- ESIC (placeholder) = round(gross × 0.75%) when gross ≤ ₹21,000, else 0.
- PT (placeholder) = ₹200 when gross ≥ ₹10,000, else 0.
- Advance recovery = min(installment, balance, gross − PF − ESIC − PT) — never makes net negative.
- Net = gross − PF − ESIC − PT − advance.
All rates are editable by the owner on the Settings tab and labelled *illustrative — not statutory advice*.

## 6. Screens
Desktop `staff` (tabs by role): **Staff** (master, filters by department, salary column only for payroll roles) · **Roster** (week grid, prev/next week, copy week) · **Attendance** (today's register with mark buttons + month summary) · **Leave** (pending queue with approve/reject, history) · **Payroll** (month picker, run/recalculate, lock, register table, payslip, CSV) · **Advances** · **Settings** (illustrative rates). Non-HR roles see "My HR".
Desktop `my-hr`: today's shift + check-in, my week, my month attendance, my leave + request, my payslips.
Phone: `staff` uses chips for sections and list cards instead of tables; `my-hr` has one primary action (sticky "Check in now" until marked, then "Request leave"). Payslip sheet prints on A4 and in a phone print preview.
Modals: `hr-staff`, `hr-roster`, `hr-att`, `hr-leave`, `hr-advance`, `hr-payslip` (id `runId|staffId`), `hr-rates`.

## 7. Fixture (fictional)
12 staff: 8 linked to demo users (Meera, Aarav, Priya, Neha, Ravi, Kavya, Farhan, Joseph) + 4 without login (cook Suresh Pawar, kitchen helper Imran Shaikh — daily wage, security Balu Jadhav and Ganesh More). Full attendance for September 2026 and 1–23 October 2026 (deterministic pattern with some lates, half-days, absences and overtime), two morning check-ins on 24 October, a locked August 2026 payroll run, September not yet run, one approved and two pending leave requests, one open advance.

## 8. Acceptance (tests in `tests/stream-staff.mjs`)
Permissions per role; salary visibility selectors; roster past-date block; self check-in late detection and once-per-day; day-closed block; leave balance, overlap, daily-wage LOP-only, no self-approval; payroll figures for a known staff member; month-not-complete block; maker-checker lock; stale-draft lock rejection; locked immutability; advance recovery cap; rates validation and snapshot; idempotency; CSV header/rows.

## 9. Known gaps / shared changes not made
- Nav items accept one permission, so Reception and Tech admin cannot reach self-service. Shared change needed: `NavItem.p` accepting `Permission[]` (any-of) in `src/app/nav.ts`, `App.tsx` (`allowedIds`) and `MobileShell` — then `my-hr` would list `['housekeeping','reservations','reports','admin']`.
- No statutory compliance, no bank file, no payslip e-mail (would be simulated), no arrears/corrections after lock (next month adjustment out of scope), no shift swap requests.
