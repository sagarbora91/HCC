# W05-B payroll/leave + W05-A Master configuration — browser check (in progress)

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
