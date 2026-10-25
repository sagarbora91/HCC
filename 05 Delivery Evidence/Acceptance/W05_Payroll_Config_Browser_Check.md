# W05-B payroll/leave + W05-A Master configuration — browser check

Build: integration/w05 `dist/index.html` served on 127.0.0.1:4181, Playwright, FY26 scenario, 25 Oct 2026. Screenshots: `w05-browser2\`.

| Check | Desktop 1366x768 | Phone 360x800 | Notes |
|---|---|---|---|
| Pay-run unlock (Owner, reason, latest month only) | Pass | | Only Sep (latest) has Unlock; reason prompt; run returned to Draft, toast "Payroll unlocked". Accountant sees no Unlock. |
| Draft -> lock by second person | Pass | | Recalculate as Owner -> "Prepared by Sagar", Lock disabled with maker-checker note; Accountant locked it ("locked by Farhan"). |
| PF/ESIC/PT agree run vs payslip vs exports | Pass | | Aarav: PF 1,008, ESIC 119.70, PT 200, net 15,622.30 on run, payslip, CSV, WhatsApp text, My shifts card. Totals PF 11,576.50 / ESIC 1,076.12 / PT 1,800 equal statement CSV. |
| Register CSV / Excel, bank CSV, statutory statement | Pass | | 4 downloads OK; xlsx is valid zip with sheet1.xml. |
| Payslip PDF + simulated WhatsApp | Pass | | Print preview shows payslip only (print media); WhatsApp toast "simulated - nothing left this device". |
| Full and final | Pass | | Ganesh More exit -> settlement net 8,540.61 -> + EL encashment 6,048.39 -> 14,589; status Settled. |
| Statutory rates tab | Pass | | Dated version "1 Apr 2025 in force now", "Illustrative - CA to confirm for a hotel"; no add-version for Accountant. |
| Leave: half-day apply, other approves, no self-approve | Pass | | Half-day shows single Date; "Your own request - another manager decides"; Meera approved. |
| Dept cap blocks 3rd person | Pass | | Housekeeping 2/day: Neha refused "already has 2 staff off on 10 Nov (maximum 2 per day)". |
| Leave calendar / balances | Pass | | pending outlined chips; balances in modal and My shifts. |
| My shifts and pay (staff) | Pass | | payslip card, leave balance 10.5/12 after pending half-day, apply, payslip list. |
| Manager sees no money | Pass | | Manager has no Payroll tab; subtitle "salary figures visible to Owner and Accountant only". |
| Phone: payroll / statutory rates read-only | n/a | Pass | Phone Payroll tab: month chips + per-staff cards (net, gross, PF, ESIC, PT) and note "Running, locking, unlocking and the bank file are done on the tablet or desktop"; rate versions added on desktop only. Figures match desktop. |
| Phone: leave (requests, calendar, policy, F&F list, advances) | n/a | Pass | Cards; half-day shown "CL 1/2 0.5d"; calendar shows "Housekeeping 1/2"; Approve/Reject present. |
| Phone: My shifts and pay (Ravi) | n/a | Pass | Week, month so far, payslip card with deductions, leave balances (CL 7 of 12, used 5), Request leave sheet, payslip viewer. Housekeeper sees only own pay. |
| Master configuration: 12 groups, search, summaries | Pass | Pass | 12 groups in 3 headings with one-line summaries; search "PF" -> People (4) + Compliance (1); "GSTIN" -> Property (1). |
| Hub layout | Pass (list beside details) | Pass (list -> details -> "All groups") | App-bar Back goes up to Admin & Setup; "All groups" returns to the list. |
| Tech admin: money groups read-only with reason | Pass | Pass | Rooms, Money, Restaurant, People, Compliance "Read only"; Change... disabled; reason "Owner changes this - money, tax, payroll, compliance and approval limits are Owner-only (DEC-058)". |
| Tech admin change needing approval; no self-approval | Pass | Not run | PIN idle 5 -> 10 "Request approval" = APR-1118 pending; dialog says "You cannot approve your own request"; Tech admin has no Approvals page; Owner approved. |
| Modules on/off: Payroll off then on | Partial (W05P-1) | Partial | Toast "Payroll switched off - data kept"; commands refused; switched back on. Payroll tabs not hidden. Other 9 switches not exercised. |
| HR statutory + Compliance groups wired; PF rate from hub -> Statutory rates | Pass | n/a | PF employee 12% -> 11% in hub; Statutory rates shows new version 1 Oct 2026 "11% + 13%"; Apr-Sep locked runs keep 12%. "Immediately" label inconsistent (W05P-2). |
| Export / import JSON preview | Pass | Not run | Export JSON; secret msg.gatewayKey only in an excluded list; import preview "0 will change, 97 unchanged, 0 skipped"; nothing applied. |
| Console errors | Pass | Pass | 0 errors, 0 warnings. |
| Sideways scroll at 360 | n/a | Pass | scrollWidth = clientWidth on every screen (345 with scrollbar). Tab strips scroll inside their container. |
| Touch targets 48 px / 16 px text | n/a | Fail (S4) | W05P-3 (same family as W05-6). |

## Not run / limits
- Unlock with empty reason (button enabled; submit not tested). October draft impossible (Apr-Sep all locked), so draft-lock was tested by unlocking September and recalculating (state in browser only, FY26 scenario; PF rate left at 11% from 1 Oct 2026 in that browser profile).
- Phone: WhatsApp send from the manager view, other 9 module switches, Tech admin approval on phone.
- Real print dialog: print media emulated (payslip only, clean A4-like layout); window.print was called.

## Observations
- Housekeeping cap error appears only after Send request; the modal gives no pre-warning.
- A pending half-day immediately reduces displayed balance (CL 10.5/12).
- The Staff & payroll tab once fell back to "Staff" after a failed-command toast (not reproduced).

## Defects
W05P-1 S3 payroll tabs still visible when Payroll off; W05P-2 S4 "Immediately" label vs dated effect, repeated long help text; W05P-3 S4 phone touch targets/text size on HR and config screens.
| Phone: payroll / statutory rates read-only | n/a | Pass | Phone Payroll tab shows month chips + per-staff cards (net, gross, PF, ESIC, PT) and "Running, locking, unlocking and the bank file are done on tablet or desktop"; rates versions added on desktop only. Register CSV button available. Figures match desktop. No page-level sideways scroll. |
| Phone: leave (requests, calendar, policy, F&F list, advances) | n/a | Pass | Cards, half-day shown as CL 1/2 0.5d; calendar shows "Housekeeping 1/2" capacity; Approve/Reject buttons present. |
| Phone: My shifts and pay (Ravi) | n/a | Pass | Week, month so far, latest payslip card with deductions, leave balances (CL 7 of 12 used 5), Request leave sheet, payslip viewer. Housekeeper sees only own pay. |
| Master configuration: 12 groups, search, summaries | Pass | Pass | 12 groups under 3 headings with live one-line summaries; search "PF" -> People (4) + Compliance (1); "GSTIN" -> Property (1). |
| Hub layout | Pass (list beside details) | Pass (list -> details -> "All groups"/Back) | App-bar Back on phone goes up to Admin & Setup, "All groups" returns to the list. |
| Tech admin: money groups read-only with reason | Pass | Pass | Rooms, Money, Restaurant, People, Compliance marked Read only; Change... disabled; reason "Owner changes this - money, tax, payroll, compliance and approval limits are Owner-only (DEC-058)". |
| Tech admin change needing approval; no self-approval | Pass | not run | PIN idle minutes 5 -> 10 "Request approval" -> APR-1118 pending; dialog states "You cannot approve your own request"; Tech admin has no Approvals page; Owner approved. |
| Modules on/off: Payroll off then on | Partial (W05P-1) | Partial | Toast "Payroll switched off - data kept"; commands refused; switched back on OK. Payroll tabs not hidden. Other 9 switches not exercised. |
| HR statutory + Compliance groups wired; PF rate from hub -> Statutory rates | Pass | n/a | PF employee 12% -> 11% in hub; Statutory rates shows new version 1 Oct 2026 "11% + 13%"; locked runs unchanged (Apr-Sep keep 12%). "Immediately" label inconsistent (W05P-2). |
| Export / import JSON preview | Pass | not run | Export JSON (97 values; secret msg.gatewayKey listed only as excluded); import preview "0 will change, 97 unchanged, 0 skipped"; nothing applied. |
| Console errors | Pass | Pass | 0 errors, 0 warnings across the session. |
| Sideways scroll at 360 | n/a | Pass | scrollWidth = clientWidth on every screen checked (345 with scrollbar). Tab strips scroll inside their own container. |
| Touch targets >= 48 px / 16 px text | n/a | Fail (S4) | See W05P-3 (same family as W05-6). |

## Not run / limits
- Unlock with an empty reason (button was enabled; submit not tested). Oct 2026 draft: no completed month left to run (Apr-Sep all locked), so the draft-lock was done by unlocking September and recalculating.
- Phone: payslip send on WhatsApp from phone manager view, phone pay-run actions (read-only by design), other 9 module switches, Tech admin approval from phone.
- Real PDF/print dialog: print media emulated (payslip only, A4-like layout OK); `window.print` was called.

## Observations (not defects)
- A failed command ("Not available: Housekeeping already has 2 staff off") is shown only after pressing Send request; the modal gives no pre-warning of the cap.
- Pending half-day immediately reduces the displayed balance (CL 10.5/12).
- Staff tab selection once fell back to "Staff" after a failed command toast during testing (not reproduced on retest).

## Defects
W05P-1 S3 payroll tabs still visible when Payroll is off; W05P-2 S4 "Immediately" label vs dated effect and long repeated help text; W05P-3 S4 phone touch targets/text size on HR and config screens.
