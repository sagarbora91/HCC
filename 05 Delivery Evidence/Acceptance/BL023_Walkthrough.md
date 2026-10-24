# BL-023 · S0 owner walkthrough (browser) — D-A

**Progress:** COMPLETE — last completed step: 12 (access policy); next: none (report finished, 15/15 pass).

- **Date:** 24 Oct 2026 (simulated clock 24→26 Oct 2026)
- **Environment:** BROWSER — Claude desktop built-in browser pane, `hcc-prototype` (`node "03 Prototype/serve.mjs"`, http://localhost:4173), viewport emulated at **1366×768**, Display = Auto (desktop layout), scenario **S0** loaded via Demo controls → Scenario.
- **Build under test:** `03 Prototype\dist\index.html` (SHA-256 `4e33f7d6…c1dd`, same bundle as APK versionCode 6).
- **Method:** driven through the UI (clicks, typed input, role switcher "Preview as", guide "Go there"). Some reads, a few list-row clicks and the Reports date change used DOM scripting (text reads / `click()` / native value setter); amounts and names were typed with real key input. The pane screenshot is cropped to ~800 px of the 1366 px viewport, so figures were verified from DOM text; screenshots are supporting evidence only.
- **Result: 15/15 guide steps Pass on every expected figure.** 2 cosmetic/observation defects (BL-01, BL-02); D-01 already known (not re-logged).

## Step results

| Step | Actor | Expected | Observed | Result | Evidence |
|---|---|---|---|---|---|
| 1 Review the hotel (24 Oct) | Owner | 8 occupied · 23 sellable · drawer ₹10,750 (incl. ₹550 restaurant cash) · outstanding ₹1,950 | 8 of 23 sellable (35%), drawer ₹10,750, outstanding ₹1,950, deposits ₹0 | Pass | `bl023/01-overview-24oct.jpg` |
| 2 Reserve Room 105 | Reception | Confirmed · folio balance −₹1,000 · drawer unchanged | HCC-1101 Confirmed, 24→26 Oct, 2 guests, Phone; RCPT-1103 ₹1,000 UPI DEMO-UPI-0001; credit −₹1,000; drawer still ₹10,750 | Pass | `02-booking-105-advance.jpg` |
| 3 Register & check in | Reception | Room 105 Occupied · arrivals list updated · audit | 6-step guided check-in; "Checked in to room 105. Room board updated."; folios list shows 9 in house, 105 In house | Pass | `03-checked-in-105.jpg` |
| 3b Take deposit | Cashier | Deposit held ₹500 · balance −₹1,000 · drawer ₹11,250 | RCPT-1108 by Priya; deposit held ₹500; credit −₹1,000; guide banner drawer ₹11,250 | Pass | `04-deposit-500.jpg` |
| 4a Close 24 Oct | Manager | 105 balance ₹1,200 · 24 Oct closed · float ₹11,250 | Preflight blocked by EXP-102 and restaurant checks POS-F03 (T2 ₹1,914) / POS-F04 (201 ₹330). Approved EXP-102; sent T2 KOT (2 items); settled both by UPI (demo refs required); counted ₹11,250 (exact); posted 9 nights; closed. Opened 25 Oct with float ₹11,250; 105 balance ₹1,200 | Pass | `05-shift-count-11250-24oct.jpg`, `06-closed-24oct.jpg` |
| 4b Service & discount (25 Oct) | Reception | Discount > ₹250 → approval pending · balance ₹1,750 | Laundry ₹500 posted (+₹50 tax); discount ₹500 "Service delay goodwill" → APR-1123 Pending, own-request notice; balance ₹1,750 | Pass | `07-laundry-discount-pending.jpg` |
| 5 Approve discount | Manager | Discount line −₹500 (tax −₹50) · balance ₹1,200 | Reception cannot approve own request (no approve control, notice shown); Housekeeper has no Folios/Cash page at all (search: "Nothing matches"), so recording a payment is denied at navigation level. Manager approved: line −₹500/−₹50, balance ₹1,200 | Pass (see note 1) | — |
| 5b Close 25 Oct | Manager | 105 balance ₹3,400 · invoice ₹4,400 (net ₹4,000 + tax ₹400) | Posted 9 nights, counted ₹11,250 exact, closed, opened 26 Oct (float ₹11,250). Balance ₹3,400; charges incl. tax ₹4,400 | Pass | — |
| 6 Settle & return deposit (26 Oct) | Cashier | Balance ₹0 · deposit ₹0 · drawer ₹12,750 | Split ₹2,000 cash + ₹1,400 UPI (DEMO-UPI-0002) → "Post 2 receipts" (TND-1131); balance ₹0; deposit returned ₹500 cash (RCPT-1135); banner drawer ₹12,750 | Pass | — |
| 7 Check out & print | Reception | Departed · Room 105 Dirty · one housekeeping task (Ravi) · invoice ₹4,400 | Checkout review (2 of 2 nights, ₹4,400, ₹0 due, deposit ₹0) → Departed, invoice DEMO/2026-27/0001, toast "Housekeeping task created". Invoice preview: net 2,000+500−500+2,000, total ₹4,400, balance ₹0. Exactly one Room 105 task, assigned Ravi. "Dirty" not read directly (inferred from the task; the room later shows Ready) | Pass (note 2) | `08-invoice-4400.jpg` |
| 8 Clean & inspect | Housekeeper → Supervisor | Room 105 Ready | Ravi started, ticked 3 items, completed ("Cleaning completed", Awaiting inspection). Neha "Inspect & release" → "Inspection passed". Dashboard tile `Room 105: Ready` | Pass | — |
| 9 Cash expense | Cashier → Manager | Drawer ₹12,450 | Priya EXP-1145 Aqua Water Cans ₹300 drawer cash; Meera "Approve & post"; guide banner drawer ₹12,450 | Pass | — |
| 10 Close 26 Oct | Manager | Day closed · rerun refused ("closed") · totals unchanged | Posted 8 nights; counted ₹12,450 exact; closed (summary: revenue ₹19,500, receipts ₹3,400, expenses ₹300, float 11,250 → expected/counted 12,450). "Try closing again" → "Business day closed: This business day is closed. Open the next business day first."; summary unchanged | Pass | `09-shift-count-12450-26oct.jpg` |
| 11 Drill into reports | Owner | Room 105: ₹4,000 net / ₹400 tax · deposit in ₹500 / out ₹500 · expense ₹300 | Range 24–26 Oct: net revenue ₹62,500 (28 rows, = 21,500+21,500+19,500), receipts ₹25,900, expenses ₹2,950. Drill-downs: Room 105 revenue rows 2,000+500−500+2,000 = ₹4,000; tax 200+50−50+200 = ₹400; deposit in ₹500 (RCPT) and out −₹500; drawer-cash expenses: EXP-101 ₹1,800 + EXP-1145 Aqua Water Cans ₹300 = ₹2,100. Each total's "Total (n rows) — equals the figure above" line matched | Pass | `10-reports-24-26oct.jpg` |
| 12 Access policy | Owner → Reception | Next command → user disabled · audit shows change and denial | Removed `reservation.create` from Reception template (allow → not granted, policy v2); disabled Aarav ("their next action is denied"). Switching to Aarav shows "This user has no active access" and no pages, so a booking cannot be attempted. Audit lists POLICY SET entries for the template and for Aarav (enabled → disabled). No explicit "denied command" audit row is produced because the UI blocks before any command | Pass with deviation (note 3) | — |

### Notes
1. Step 5 denial checks: Reception sees its own APR-1123 as "Your own request — another authorised user must decide it"; Housekeeper cannot reach Folios. Both are stricter than the "denied" error the guide describes (the control or page is simply absent).
2. Step 7 "Room 105 Dirty": not asserted from the board; the Housekeeping task list and later Ready status are consistent with it.
3. Step 12: the guide's "next command → user disabled" error is not reachable through the UI for a disabled user. Not a failure of the policy (access is denied), but the audit trail has no denial row.
4. Closing-summary cross-check: 24 Oct restaurant UPI ₹2,992 (748 + 1,914 + 330), cash ₹550; Cash shift 24 Oct = 5,000 + 7,000 + 500 + 550 − 1,800 = ₹11,250 (shared till, DEC-052 holds).
5. Known D-01 (settle dialog says outlet cash is separate from the drawer) was seen again on the T2 settle dialog; not re-logged.
6. Observation (not logged): Reports → Expenses & cash "Cash into drawer (payments + deposits)" is ₹9,500 and excludes the ₹550 restaurant cash although the shared till includes it; Reports is range-based so this may be by design — worth a product decision.

## Defects

| ID | Severity | Steps | Expected | Actual | Evidence |
|---|---|---|---|---|---|
| BL-01 | S4 cosmetic | After step 5 (discount approved by Meera), open the Room 105 folio (after checkout in step 7) and look at "Folio requests" | Request shows "Approved" (Expenses → Approval requests shows "Approved" for APR-1123) | Folio dialog shows APR-1123 "decided by Meera" with an **Expired** pill | `defects/BL-01-approved-discount-shows-expired.jpg` |
| BL-02 | S4 observation | Owner → Access control → Role templates → Reception → set `reservation.create` to none → Save | Only the template change is recorded | Audit also records "Limit · Reception · PERM-FOL-003 Discount without approval (per request) · not set → ₹250 · policy v3" and toast says "Limit updated"; policy history jumped 1 → 3. Save appears to persist the role's limit values along with the template (side effect) | audit text (no screenshot) |

## Screenshot index (`bl023\`)
`01-overview-24oct.jpg`, `02-booking-105-advance.jpg`, `03-checked-in-105.jpg`, `04-deposit-500.jpg`, `05-shift-count-11250-24oct.jpg`, `06-closed-24oct.jpg`, `07-laundry-discount-pending.jpg`, `08-invoice-4400.jpg`, `09-shift-count-12450-26oct.jpg`, `10-reports-24-26oct.jpg` (earlier shots replaced). Console: no errors during the run.

## Follow-up — 25 October 2026
BL-01 and BL-02 have source fixes and passing automated regression evidence in `BL01_BL02_Fixes_2026-10-25.md` (497/497 full suite). The original observations above remain historical evidence. Fresh browser/device confirmation is still pending; this does not close all W0-D acceptance.
