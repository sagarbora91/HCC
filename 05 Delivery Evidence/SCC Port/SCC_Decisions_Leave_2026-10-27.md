# SCC decisions wave — Leave (DEC-122, DEC-123), 27 Oct 2026

Branch `claude/dec-leave` from `34d3de32`. The owner's answer on 27 Oct 2026 ("apply your recommendations for all 12") makes Decision Register rows DEC-122 and DEC-123 binding. Everything else in leave stays as the SCC leave port built it (DEC-116 / DEC-119; `SCC_Leave_Port_2026-10-27.md`).

## DEC-122 — the quota warning again at approval (owner question A2 / port Q4)

**Before:** SCC checked the balance only when leave was recorded. Pending leave does not count as used, so several pending requests could together go over a person's entitlement and all be approved without a warning.

**Now:** when one leave date is **approved**, HCC checks that person's balance again for that category and that date's leave year: approved days (counted by date, the same way the balance counts them) + this date (full day 1, morning or afternoon 0.5). If the total goes over the entitlement (the per-staff override, else the default), approval shows the SCC over-quota warning and goes ahead only after an explicit confirmation. The text is the same as on a request. Only the closing question changes, to "Approve it anyway?", because "it will still need approval" makes no sense at approval:

> Ravi Shinde has used 3 of 3 Casual Leave day(s) this FY 2026–27.
> This request adds 1 more and EXCEEDS the balance.
>
> Approve it anyway?

- **Rejecting** is never checked. **LOP** and the categories without a quota (Maternity / Paternity, Comp-Off, Other) are never checked.
- **Each date is decided on its own.** With two pending requests that together pass the quota, the first approves without a warning and the second warns.
- **No bulk or "approve all" path exists**, on the prototype or the server. All four approve buttons use one shared hook (`useLeaveDecide`): the approvals list, the day view ✓, the leave detail and the older decide dialog, on both desktop and phone. Whatever a client sends, the server enforces the check.
- An approval given after the warning is recorded in the audit line as "· over the quota (confirmed)", the same way a request is.
- The HCC safeguards are unchanged. Nobody decides their own leave or request (DEC-119), and attendance already recorded on the date still blocks approval.

| Layer | Change |
|---|---|
| Prototype domain | `src/domain/streams/staff-leave.ts`: `hrLeaveApproveOverBalance(s, record)` returns the warning or undefined. `hrLeaveDecideCmd(s, record, approve, ask, note)` builds the command a screen sends: it asks first and adds `confirmOverBalance`, or returns undefined when the person cancels. |
| Mock handler | `HR_LEAVE_DECIDE` (`src/services/mock/handlers/staff.ts`): when approving over the quota without `confirmOverBalance`, it refuses with `INVALID_TRANSITION` and the warning text. It is the same pattern as `HR_LEAVE_REQUEST`. |
| Screens | `src/features/staff/Leave.tsx` exports `useLeaveDecide()`, which shows the warning in a confirm dialog (Cancel sends nothing). The approvals view (`Leave.tsx`) and the day view, leave detail and decide dialog (`leaveModals.tsx`) use it. The phone uses the same components (approvals cards, day-view sheet). |
| Production client | `src/clients/sync/staff.ts`: `HR_LEAVE_DECIDE` → `DecideLeave` sends `confirmOverBalance: true` only on a confirmed approval, never on a rejection. |
| Server | `Hcc.Contracts` `DecideLeaveRequest(..., bool ConfirmOverBalance = false)`. `Hcc.Domain/Payroll/SccLeave.ApproveOverBalanceText`. `PayrollService.DecideLeaveAsync` loads the leave policy and the person's leave, runs the check under the staff-row lock (two approvals for the same person run one after the other, so the second sees the first), and refuses with `INVALID_TRANSITION` and the same text until confirmed. Everything still runs through `ICommandRunner` (audit + outbox). |

## DEC-123 — the leave calendar from April 2026 (owner question A3 / port Q13)

The calendar minimum is now **April 2026** (start of FY 2026–27; SCC stopped at May 2026). There is one constant, `HR_LEAVE_START_YM = '2026-04'` in `staff-leave.ts`. The previous-month arrow (calendar and monthly report), the month clamp (Today, View as of, opening the desk), and the month jumper (`hrLeaveMonthJumper` now starts at the constant) all use it. Desktop and phone share it. The day view never had a minimum. The server sends all leave on the board, so April 2026 shows in production too. Nothing else about the calendar changed.

## Tests

**Prototype:** `npm test` **1843 / 1843** (was 1838; +5 new). `npx tsc --noEmit`, `npm run build` and `npm run build:production` are clean, with 0 warnings.
- New `tests/stream-leave-scc-decisions.mjs` (5 `[LV-DEC]` tests):
  - two pending requests over the quota: the first approves silently, the second is refused with SCC's text and approved only with `confirmOverBalance`, then shows "over by 1". Rejecting needs no confirmation, and DEC-119 still holds.
  - half day 0.5, override vs default, the next FY starts again at the quota, approved days only; LOP / ML / CO / OTH never warn.
  - 500 random approvals checked against an oracle.
  - screens: the `useLeaveDecide` hook on desktop and phone, with a fake `window.confirm`. Cancel sends nothing, OK sends the flag, and rejections and approvals within the quota never ask.
  - DEC-123: the April 2026 minimum. Clamp, month jumper, arrows disabled at April and enabled at May, FY26 April leave on the calendar, report for April 2026.
- Updated:
  - `stream-leave-scc-port.mjs`: the random entitlements grid now confirms its approvals (that test is about usage).
  - `stream-payroll-fixes.mjs`: F-15 now expects the approval warning and then "over by 3". It used to say "approval re-checks nothing (SCC)".
  - `sync-staff.mjs`: +2 `DecideLeave` mapping rows (confirmed approval; a rejection drops the flag).
- Checked that the new tests fail on the base source (`M.hrLeaveApproveOverBalance` / `hrLeaveDecideCmd` missing; start month May).

**Server:** `dotnet build Hcc.slnx` 0 warnings / 0 errors. Runs were serial with `-maxcpucount:1` on `.\SQLEXPRESS`, under the wave lock.
- **Hcc.Payroll.Tests 63 / 63** (was 61; +2):
  - `Domain/SccLeaveTests.Approval_recheck_is_SCC_fyUsage_plus_this_date_over_the_quota_for_every_pending_record_of_random_histories`: 1,500 random histories across 31 March, against the SCC fyUsage oracle + leaveDaysOf. An approved record never counts against itself, a half day adds 0.5, LOP is never checked.
  - `Sql/LeaveSccSqlTests.Approval_rechecks_the_balance_two_pending_requests_over_the_quota_the_first_approves_the_second_needs_a_confirmation`: on SQL Server, the exact text, the record stays pending until confirmed, the audit line, balance used 4 / over 1, a half day 0.5, rejecting unchecked, the next FY, LOP, another staff member on the default.
- **Hcc.Configuration.Tests ModuleGate 4 / 4** (the `DecideLeave` route).
- Server test methods 3307 → **3309**. The full server suite was not re-run; only the touched projects were.
- No `HccTest_*` database from these runs remains. Two older ones (26 Oct 17:56 and 27 Oct 09:28) are not from this branch and were left alone.

## Migrations, pins, goldens
- **No migration.** Range 4170–4179 is unused, and no database column changed.
- **No pin or golden moved.** The demo data does not change: fixtures never run `HR_LEAVE_DECIDE`. BASE and FY26 hashes and the payroll golden are unchanged.
- `SCHEMA` stays 10. No new state field, no new permission (role matrix unchanged), no new npm package, no CSS change.

## For the owner
1. **Wording at approval:** the warning keeps SCC's request text and asks "Approve it anyway?" instead of "Record it anyway (it will still need approval)?". Keep that, or should the text say "this date"?
2. Unchanged by DEC-122, so please confirm these stay:
   - A JSON import of an SCC backup that has approved (legacy) records is not an approval, so it is not checked.
   - A DEC-075 past-day attendance correction to a quota category still refuses outright when the balance is short (no confirm), as the port built it.
3. The re-check counts approved days only, as DEC-122 says. Other pending dates of the same person are not counted until they are approved.
4. Not checked in a browser or on the APK. The screens are covered by the rendered-component tests above.
