# MEAS (measurement reports): owner decision sheet

For the owner. Plain words, one decision per question. Nothing here has been built on "main" yet: the wave is held back until these are answered (MEAS-DONE.md section 5 and 6).

Source key: DONE = `05 Delivery Evidence/Measurement/MEAS-DONE.md`; REVIEW = `MEAS-REVIEW.md` (same folder); PLAN = `02 Planning/HCC_Measurement_Reports_Plan_MEAS_2026-11-05.md` section 8; other `MEAS-*-DONE.md` files are in the same folder as DONE.

Part A holds the questions that block the merge. Part B holds the settings and rules the system is waiting on. Part C holds the older plan questions M-1 to M-11, which apply their defaults if you say nothing.

---

## PART A. Blocks the merge

### 1. Old-years money totals: should hall bills be left out?
- Plain question: When HCC shows billed money for the old (legacy) years, should banquet and hall bills be left out? Your old Year Comparison workbook includes them. HCC leaves them out, as the contract says.
- Options: (a) Leave halls out. The old workbook's figure will then always look higher than HCC's by exactly the hall bills. (b) Include halls in the old-years money total.
- If unanswered: halls stay out (HCC today).
- What it changes: Billed totals for the old financial years. Nine of ten years do not match the workbook for this reason. It was checked bill by bill: HCC equals the workbook minus the hall bills in every year. No data is lost. The same choice decides whether the old unpaid hall bills count as bad debt (see 2).
- Recommendation: (a), leave halls out, because the study is about rooms. Compare against a hall-free workbook figure. If you want halls, add a separate "halls" line instead of mixing them in.
- Source: DONE section 5, "Owner or integrator decisions" item 1, and the Billed row.

### 2. Bad debt: which year does an old unpaid bill belong to?
- Plain question: For old unpaid bills, do we file the bad debt under the year printed on the bill number (the old workbook's way) or under the date the bill was made (HCC's way)?
- Options: (a) By bill-number year (`legacy_fy`). (b) By bill date.
- If unanswered: bill date (HCC today).
- What it changes: Which year carries each bad debt. This matters in 2025-26 and 2026-27, where the two methods put the same bill in different years. The two-year total is the same apart from hall bills. Four of ten years currently fail this check.
- Recommendation: (a), the old workbook's way, so the new report matches the numbers you already know. Lower risk when comparing years.
- Source: DONE section 5 (Bad debt row; decision 2).

### 3. Halls and bad debt: confirm the hall exclusion for unpaid bills
- Plain question: Two old years (2017-18 and 2018-19) have unpaid hall bills. If halls are excluded (question 1), these will not show as bad debt. Is that right?
- Options: (a) Yes, rooms only. (b) Show unpaid hall bills in bad debt even though halls are left out elsewhere.
- If unanswered: (a).
- What it changes: Two years' bad-debt figures.
- Recommendation: (a), and keep the same rule as question 1. This avoids a report that mixes two rules.
- Source: DONE section 5 (Bad debt row).

### 4. Should the real demand curve change room prices?
- Plain question: The new forecast uses your real past two years of occupancy. Do you want the price suggestions (the suggested BAR / room rate) to use this real curve, replacing the old simulated one?
- Options: (a) Yes, wire it in now. (b) No, keep it off for now. It only feeds the events, displacement and demand-calendar pages.
- If unanswered: (b), nothing changes. The curve is not wired into price tiers.
- What it changes: ROOM PRICES. With (a), the suggested rate changes for every night the moment the wave is on, because the forecast switch is on by default. Your manual override still wins.
- Recommendation: (b) for now. First look at the forecast pages for a month and check they look sensible. Say yes later when you trust them. A back-test on real data has not been run yet.
- Source: DONE section 4 (first bullet); MEAS-FORECAST-DONE.md Open items 2.

### 5. Complimentary (free) stays: build a proper type? (DEC-266)
- Plain question: You ruled (DEC-266) that a free stay needs its own stay type, with an approver and a reason, and that moving a guest between rooms is not a free night. This is still not built. Build it now as part of this wave, or later?
- Options: (a) Build now. (b) Later, as its own small job.
- If unanswered: later. Until then the "free stay" column is blank, shown as "pending DEC-266".
- What it changes: Reports on zero-rate and free stays cannot separate real comps from rate mistakes. For now they use a guess: zero rate counts as free.
- Recommendation: (b), separately, because it needs a new command, a permission row and a database table. Do not hold up the merge for it.
- Source: DONE section 4 (DEC-266 bullet); MEAS-STUDY-A-DONE.md definitions.

### 6. Old zero-rate events and hall events: do we add the missing data?
- Plain question: Two old-years figures do not match your workbooks. "Rate changed from A to B" is read from the old stay notes, and the number of hall events is missing. Do you want them loaded properly?
- Options: (a) Yes, extend the loader (a code change). (b) No, keep a proxy count and say so on the report.
- If unanswered: (b), the proxy count stays. Zero of ten years match zero-rate. One of ten match hall events.
- What it changes: Those two study figures only. No money booked anywhere.
- Recommendation: (a) for zero-rate, since you used it in the study. (b) for halls, as you chose to exclude them (question 1).
- Source: DONE section 5 (Zero-rate and Hall events rows; decision 3).

---

## PART B. Settings and rules waiting for you

### 7. Upsell target per arrival
- Plain question: The upsell report needs a target: how much extra money (upgrade, breakfast, extra bed) should each arriving guest bring? There is no value in the contract.
- Options: Rs 50 per arrival (as built), or your own number.
- If unanswered: Rs 50 per arrival. This is fixed in the code (5000 paise) and you cannot change it yet.
- What it changes: When the upsell report turns a row red ("below target"). No money is posted.
- Recommendation: Keep Rs 50 to start. Add the setting `meas.target.upsellPerArrival` so you can change it from the panel. It is a small job.
- Source: MEAS-RPT-GUEST-DONE.md Open items 1; MEAS-REVIEW.md L7; DONE section 6 last bullet.

### 8. Linen cost per night target
- Plain question: Do you want a limit on linen cost per occupied night? Without a number the report does not flag it.
- Options: set a Rs figure, or leave it unset.
- If unanswered: unset. The rule does not fire.
- What it changes: Only the red flag on the linen report. Linen loss over 2 % of stock is flagged anyway.
- Recommendation: Add the setting and leave it empty until you have two months of data. Then set it from what you see.
- Source: MEAS-RPT-HK-DONE.md Open items; DONE section 6.

### 9. Anonymous survey: who may submit through the QR path?
- Plain question: The guest QR survey is built to accept an answer from any signed-in user. The desk entry is for Owner, Manager and Reception. Is "any signed-in role" for the QR path acceptable?
- Options: (a) Any signed-in role, rate-limited to 10 answers per 10 minutes, as built. (b) Limit it to guests' own link or the desk roles only.
- If unanswered: (a).
- What it changes: Who could add a survey answer. A staff member could file survey answers as if they were a guest, which would spoil the scores. A true public login-free QR route is not built either, so in the live server today the QR path has no web route at all.
- Recommendation: (b) for the staff apps. Desk roles only. Build the public QR route separately when you want guests to scan it, with the existing rate limit and a check that the stay is real.
- Source: MEAS-CAP-GUEST-DONE.md (survey line and Integration item 0); DONE section 6 ("no public login-free QR survey route").

### 10. Paid upgrade and late check-out charges: what GST line?
- Plain question: Paid room upgrades (UPG), late check-out Rs 300 after 14:00 (LCO1) and Rs 600 after 18:00 (LCO2) are built to post as a bill charge. The live server refuses to post them (code MEAS_CHARGE_NOT_WIRED) until the tax line is decided. The prototype posts them as "other services" at 18 % GST.
- Options: (a) Other services, 18 %. (b) Follow the room GST slab (5 % up to Rs 7,500 per night, 18 % above). (c) Something your CA says.
- If unanswered: server keeps refusing. The charges cannot be posted live.
- What it changes: GST on every late check-out and paid upgrade, and your GST returns.
- Recommendation: Ask your CA. Until then (a), which matches the prototype. Related: the Rs 300 and Rs 600 amounts and the 14:00 / 18:00 times are plan defaults (question 22, M-1).
- Source: DONE section 6 (Capture gaps); MEAS-CAP-DESK-DONE.md (command line).

### 11. Overbooking-and-wash report: show money?
- Plain question: The overbooking report has a "cost of a walked guest" column. The catalogue said "no money", but the report has money in it. It was built as a money report (Owner, Manager, anyone with the money permission; others cannot see the column).
- Options: (a) Money report, hidden from roles without money access (as built). (b) Remove the money column.
- If unanswered: (a). The catalogue was corrected to "money: y" in the integration.
- What it changes: Which staff roles see the cost of walked guests.
- Recommendation: (a), and confirm the catalogue wording is acceptable. Related rules (the overbooking allowance, M-6) are separate: the default is 0 rooms.
- Source: MEAS-RPT-REVENUE-DONE.md Open items; DONE "Report catalogue" bullet.

### 12. Old extra charges: other income or room revenue?
- Plain question: In the old software some stays had an extra amount (`extra_paise`). Is it room revenue or "other"?
- Options: (a) Other (as built, same as the beyond workbooks). (b) Room revenue.
- If unanswered: (a).
- What it changes: Old-year ADR, RevPAR and room revenue move up with (b), other income moves down. Total stays the same. ADR currently matches the workbook in 10 of 10 years with (a).
- Recommendation: (a). It already matches your workbook. Changing it would break that match.
- Source: DONE section 6 (Helper questions).

### 13. Old stays still open at the changeover
- Plain question: Some old stays are still in-house on the changeover day. They are cut off at the end of October 2026 so old nights stop there. Will those guests be created again live in HCC?
- Options: (a) Yes, they are re-created, so cutting old data is right. (b) No, keep counting their nights to the actual check-out.
- If unanswered: cut at the changeover date.
- What it changes: A few nights of occupancy and revenue near the cutover. Risk is double counting if the guests are re-created, or missing nights if they are not.
- Recommendation: Tell us how you will move in-house guests at cutover. Also note that the two files disagree on the date: DONE section 2 says 30 Oct 2026 and section 6 says 31 Oct 2026. Confirm the exact date.
- Source: DONE section 2 (analytics loader rules) and section 6.

### 14. Which roles count as "no money" for the capture list?
- Plain question: Housekeeping and Maintenance see no money (your DEC-061 rule). The system also treats Supervisor as "no money" for the capture read. Is that right?
- Options: (a) Supervisor sees no money (as built). (b) Supervisor may see money.
- If unanswered: (a).
- What it changes: Whether a Supervisor sees cost fields such as replacement cost of assets. The capture forms for Supervisor already hide money.
- Recommendation: (a), the safe side. A Supervisor works on the floor like Housekeeping.
- Source: DONE section 6; MEAS-CAP-HK-DONE.md (DEC-061 line).

### 15. On-the-books revenue: before or after GST?
- Plain question: For future bookings ("on the books"), revenue is counted as the agreed nightly rate before GST. Is that what you want?
- Options: (a) Before GST (as built). (b) Including GST.
- If unanswered: (a).
- What it changes: The pickup and pace forecast report amounts. Pace percentages should not change, because both years use the same basis.
- Recommendation: (a). It matches how ADR is counted everywhere else (before GST).
- Source: DONE section 6; MEAS-FORECAST-DONE.md section 2.3.

---

## PART C. Plan questions M-1 to M-11, applied by default if you say nothing

Source: PLAN section 8.

### 16. M-1: Check-in / check-out times and late check-out fees
- Plain question: Confirm check-in 12:00 and check-out 11:00? Late check-out fees?
- Default: 12:00 / 11:00. Rs 300 after 14:00 and Rs 600 after 18:00 (items LCO1, LCO2). A waiver needs a Manager.
- What it changes: What guests are charged at late check-out. Fees cannot post live until question 10 is decided.
- Recommendation: Keep the defaults and confirm.

### 17. M-2: Must staff log "booked" or "could not book" for every search?
- Default: Optional. The weekly digest shows how many were logged, so the Manager sees under-logging.
- What it changes: Staff workload against better demand data on lost business.
- Recommendation: Keep optional for the first month. The setting `meas.regrets.mandatory` exists to turn it on later.

### 18. M-3: Survey channel at go-live
- Default: Both the QR card on the local network and desk entry for the guest. WhatsApp link when the outside-connection feature (F9) is on.
- Recommendation: Keep the default, subject to question 9.

### 19. M-4: Which utility meters are read daily?
- Default: Electricity main, water, LPG cylinders (count) and generator hours. Floor sub-meters later.
- Recommendation: Keep the default. Tell us which meters really exist.

### 20. M-5: FF&E reserve (furniture and equipment set-aside), percent of revenue
- Default: 3 %. Note that the server report ignored the stored setting and used 3 % (review M4). The integration fixed it to read your setting.
- Recommendation: Confirm 3 % or give your number.
- Source also: MEAS-REVIEW.md M4; DONE "Server target reads".

### 21. M-6 and M-7: Overbooking allowance, and yield restrictions enforced or advisory
- M-6 default: 0 rooms for every type (overbooking off). What it changes: Each room type you set above 0 allows that many oversold rooms and changes the red flag. On the server this setting is not yet registered (review M9), so the allowed number reads as 0 until fixed.
- M-7 default: advisory (off). Careful: the plan wording is wrong. On main, minimum-stay and closed-to-arrival rules are already enforced at booking, with an approver override. So "off" can only mean today's behaviour (MEAS-FORECAST-DONE.md Open items 1). Suggested reading: off = today, on = the rate-plan rules also enforced.
- Recommendation: Keep both at defaults. Confirm the reading of M-7.

### 22. M-8: High-balance threshold for walk-in guests with no company
- Default: Rs 5,000, or any balance older than 2 nights.
- What it changes: When a bill is flagged red on the daily reports.
- Recommendation: Confirm Rs 5,000.

### 23. M-9: Targets
- Default: occupancy 35 %, ADR Rs 2,050, NPS 30, repeat guests 25 %, uncharged late departures 10 %, overtime 10 %. All can be edited in the Measurement panel. The settings file also carries cash variance Rs 100, check-in 8 and check-out 6 (minutes), absenteeism 5 %, attrition 30 %, linen/stock loss 2 % (MEAS-SETTINGS-DONE.md).
- Recommendation: Check the occupancy and ADR figures against your own budget. These drive the red rows.

### 24. M-10: Study pack schedule and recipients
- Default: Monthly on the 1st, to Owner and Manager (Manager's copy without receivable names). Quarterly PDF is kept in the document vault.
- Recommendation: Keep the default.

### 25. M-11: Which sheets with names and mobile numbers may the Manager open?
- Default: Repeat customer list and corporate accounts: Owner and Manager. The Accountant sees receivable names only.
- Recommendation: Keep the default. These are customer contact details.

---

## Also needed from you, not a choice

### 26. Go-ahead to merge, once questions 1 to 6 are answered
- Plain question: When the answers above are in, do you want the wave merged into main and gated on this PC?
- If unanswered: nothing is merged. Status today: the gate was not run, local main was not advanced, the full server test and the fit-to-screen check on the three new pages were not run, and the SQL for the 32 measurement reports has never run against a database (DONE section 6).
- Recommendation: Yes after answers 1 to 6. Allow a gate run before any push to main.
- Source: DONE section 6; MEAS-LEDGER.md.

---
Count: 26 questions (6 blocking, 9 settings and rules, 10 older plan items, 1 go-ahead; M-6 and M-7 are covered together in 21).
