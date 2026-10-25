# W05 browser check (integration/w05, served on 127.0.0.1:4180, Playwright, 25 Oct 2026)
Scenario FY26 (S1 for Day pack). No browser console errors or warnings on any page. Screenshots in `w05-browser\`. Defects in `defects\W05-*.md`. Server stopped.

| Check | Desktop 1366x768 | Phone 360x800 | Notes |
|---|---|---|---|
| Cash book role visibility | Pass (Cashier, Manager, Owner, Accountant only) | Not separately run | Reception, Supervisor, HK, Maintenance, Tech admin do not see it |
| Cash book day + range view | Pass | Pass | Range default 11-24 Oct, totals 5,000 / 2,89,370 / 2,83,590 / 10,750 |
| Matches Cashier shift (24 Oct) | Pass | n/a | opening 5,000, in 7,550, out 1,800, closing 10,750 = shift expected cash; 6 movements identical |
| Carry forward | Partial | Partial | W05-7 (S4) |
| Lock by checker / owner reopen with reason | Pass | Lock section renders, buttons 48 px | counter and non-checker blocked with message; Owner locked, reopen needs reason, audit line shown |
| Cash book CSV / Excel | CSV Pass, Excel absent | same | W05-8 (S4) |
| Link from Day closing | Pass | Fail | W05-4 |
| Excel on Reports | Pass (all 6 tabs download a valid xlsx, PK zip, 12 sheets) | Fail (no button) | W05-2; same whole-workbook file for each tab |
| Day pack PDF after close | Pass (A4 modal; Print dialog not triggered) | Fail | W05-4 |
| Tally export tab | Pass (Simulated label, XML preview, range 18-24 Oct, download) | Fail (no tab) | W05-3 |
| Compliance visibility | Fail for Manager and Cashier nav (page blocked) | not run | W05-1; Owner and Accountant OK |
| Compliance counters, statuses, mark filed, N/A with reason, manage, CA pack | Pass | Pass (cards, counters) | filed recorded with ref, counters updated; CA pack simulated |
| Dashboard Due this week card | Pass | Fail | W05-5; Manager does not see card |
| Accounts tile badge | Pass | Pass | |
| Assistant "what is due" | Pass (Owner), Manager gets "Not available for your role" | not run | |
| Sideways scroll 360 px | n/a | Pass | |
| Touch targets / 16 px text | n/a | Fail | W05-6 |

Notes: the Day closing count modal asks "Discard the details you entered?" after a successful submit (appears older, not W05); Day closing needed S1 because FY26 has blocking checks. Phone role visibility, phone assistant and phone lock flows were not run.
