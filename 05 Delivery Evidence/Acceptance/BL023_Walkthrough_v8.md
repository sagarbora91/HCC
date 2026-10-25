# BL-023 · S0 owner walkthrough on the final v8 bundle (browser) — W0-D re-run + BL-02 confirmation

- **Date:** 25 Oct 2026 (simulated clock 24→26 Oct 2026, then 27 Oct for the T-04 booking)
- **Environment:** BROWSER — Claude desktop built-in browser pane, own tab, viewport emulated at **1366×768**, Display = Auto (desktop layout). `node serve.mjs` on `http://127.0.0.1:4184` (my PID stopped at the end).
- **Bundle under test:** `03 Prototype\dist\index.html`, SHA-256 starts `76519ceba8a14ef9` (verified before serving) = APK v8 web bundle. Nothing rebuilt, no product code changed, nothing committed.
- **Method:** scenario S0 loaded from Demo controls → Scenario; each step entered through the UI (Walkthrough "Go there", real form fields, buttons, role switcher "Preview as"). Clicks and field entry were driven with DOM scripting (native value setter + click) because the pane screenshot is cropped (~800 px of 1366); figures were read from DOM text and screenshots are supporting evidence. `window.confirm` was wrapped with a logger (returns true) to detect AC-04.
- **Note on a retried run:** my first pass slipped at step 6 (a mistyped deposit-return option posted a stray ₹500 payment), so I reloaded S0 and re-ran steps 2–12 cleanly; all figures below are from the clean run (step 1, the settle-dialog shot and the BL-01 shot came from the first pass, identical sequence/figures).
- **Result: 15/15 S0 guide steps Pass on every expected figure; BL-01, D-01 and T-04 confirmed fixed; BL-02 confirmed fixed in the UI; AC-04 reproduces on desktop (Day closing count sheet only).**

## Step results

| Step | Actor | Expected | Observed (v8) | Result | Evidence |
|---|---|---|---|---|---|
| 1 Review the hotel (24 Oct) | Owner | 8 occupied · 23 sellable · drawer ₹10,750 · outstanding ₹1,950 | 8 occupied of 23 sellable (35%), drawer ₹10,750 (dashboard and guide banner), outstanding ₹1,950, deposits ₹0 | Pass | `01-overview-24oct.jpg` |
| 2 Reserve Room 105 | Reception | Confirmed · folio −₹1,000 · drawer unchanged | HCC-1101 Confirmed 24→26 Oct, 2 nights · 2 guests, Phone; RCPT-1103 ₹1,000 UPI DEMO-UPI-0001; credit balance −₹1,000; drawer unchanged (reads ₹11,250 after the ₹500 deposit, so the UPI advance added nothing) | Pass | — |
| 3 Register & check in | Reception | Room 105 Occupied · arrivals updated | 6-step guided check-in (Aadhaar demo, last 4 `1234`); "checked in to room 105 … room board and arrivals list are updated" | Pass | — |
| 3b Take deposit | Cashier | Deposit ₹500 · balance −₹1,000 · drawer ₹11,250 | RCPT-1108 by Priya; deposit held ₹500; credit −₹1,000; guide banner "expected drawer ₹11,250" | Pass | — |
| 4a Close 24 Oct | Manager | 105 balance ₹1,200 · 24 Oct closed · float ₹11,250 | Approved EXP-102 (₹850 UPI); T2 KOT sent, T2 ₹1,914 and room 201 ₹330 settled by UPI (refs required); cash count ₹11,250 exact; preflight all clear; posted 9 nights; closed 24 Oct; summary revenue ₹21,500, restaurant UPI ₹2,992 (748+1,914+330), cash ₹550; opened 25 Oct, float ₹11,250. 105 balance ₹1,200 | Pass | `04-settle-dialog-d01.jpg` |
| 4b Service & discount (25 Oct) | Reception | Discount > ₹250 → approval pending · balance ₹1,750 | Laundry ₹500 posted; discount ₹500 "Service delay goodwill" → APR-1123 Pending ("Your own request — another authorised user must decide it"); balance ₹1,750 | Pass | — |
| 5 Approve discount | Manager | Discount −₹500 (tax −₹50) · balance ₹1,200 | Reception: no approve control, own-request notice. Housekeeper (Ravi): no Cash & Billing module, so recording a payment is denied at navigation. Meera approved: lines Laundry ₹500/₹50 and Approved discount −₹500/−₹50; balance ₹1,200 | Pass (note 1) | `05-approved-discount-bl01.jpg` |
| 5b Close 25 Oct | Manager | 105 balance ₹3,400 · invoice ₹4,400 | Posted 9 nights; counted ₹11,250 exact in the Day closing "Count & submit" sheet; closed; opened 26 Oct. Balance ₹3,400; charges incl. tax ₹4,400; summary revenue ₹21,500, service ₹500, discounts −₹500 | Pass | — |
| 6 Settle & return deposit (26 Oct) | Cashier | Balance ₹0 · deposit ₹0 · drawer ₹12,750 | Split ₹2,000 cash + ₹1,400 UPI (DEMO-UPI-0002) → "Post 2 receipts" (TND-1131: RCPT-1132/1133); balance ₹0; deposit return ₹500 cash (RCPT-1135, −₹500), deposit held ₹0; guide banner "expected drawer ₹12,750" | Pass | — |
| 7 Check out & print | Reception | Departed · one housekeeping task (Ravi) · invoice ₹4,400 | Checkout review 2 of 2 nights, ₹4,400, ₹0 due, deposit ₹0, no pending approvals → Confirm checkout → Departed, invoice DEMO/2026-27/0001. Invoice preview 2,000 + 500 − 500 + 2,000, total ₹4,400, balance ₹0. Exactly one Room 105 task, assigned to Ravi ("Checkout clean · To do"). "Dirty" board status not read directly (note 2) | Pass | `08-invoice-4400.jpg` |
| 8 Clean & inspect | Housekeeper → Supervisor | Room 105 Ready | Ravi Start cleaning, ticked 3 items, Complete cleaning → "Awaiting inspection". Neha "Inspect & release" → "Inspection passed". Dashboard tile `Room 105: Ready` | Pass | — |
| 9 Cash expense | Cashier → Manager | Drawer ₹12,450 | Priya EXP-1145 "Aqua Water Cans" ₹300 Drawer cash; Meera "Approve & post"; guide banner and dashboard drawer ₹12,450 | Pass | — |
| 10 Close 26 Oct | Manager | Closed · rerun refused · totals unchanged | Posted 8 nights; sheet count ₹12,450 exact (expected = 11,250 + 2,000 − 800); closed. Summary revenue ₹19,500, receipts ₹3,400 (cash 2,000 / UPI 1,400), deposits returned ₹500, expenses ₹300, occupancy 34.8%. "Try closing again" → "Business day closed: This business day is closed. Open the next business day first."; net revenue still ₹19,500 | Pass | `09-day-closed-rerun-refused-26oct.jpg` |
| 11 Drill into reports | Owner | 105: ₹4,000 net / ₹400 tax · deposit in ₹500 / out ₹500 · expense ₹300 | Range 24–26 Oct: net revenue ₹62,500 (28 rows), tax ₹6,250, receipts ₹25,900, expenses ₹2,950. Drill-downs: Room 105 revenue rows 2,000 + 500 − 500 + 2,000 = ₹4,000; tax 200 + 50 − 50 + 200 = ₹400; deposits received RCPT-1108 ₹500; returned RCPT-1135 −₹500; drawer-cash expenses EXP-101 ₹1,800 + EXP-1145 ₹300 = ₹2,100. Every "Total (n rows) — equals the figure above" matched. Expenses & cash now shows "Cash into drawer (payments + deposits + POS sales)" ₹10,050 (9,500 + 550 restaurant cash) and cash out −₹500 | Pass | `10-reports-24-26oct.jpg` |
| 12 Access policy | Owner → Reception | Next command → user disabled · audit shows change and denial | Reception template `reservation.create / hold / confirm` allow → not granted (policy v2); disabled Aarav ("their next action is denied"). As Aarav: "This user has no active access", no modules, so a booking cannot be attempted. Audit lists POLICY SET rows for the template change and Aarav enabled → disabled. No separate "denied command" row (UI blocks first) | Pass with deviation (note 3) | — |

### Notes
1. Step 5 denials are stricter than the guide's "denied" error: the control or page is simply absent for Reception/Housekeeper.
2. Step 7 "Room 105 Dirty" was not read from the board itself (same as the earlier run); consistent with the single "Checkout clean" task and the later Ready tile.
3. Step 12: the guide's "next command → user disabled" error is not reachable for a disabled user through the UI; access is denied before any command, so no denial row.
4. Browser console: no errors during the run.

## Fix confirmations (v8)

| Check | Result | Detail |
|---|---|---|
| D-01 settle wording | Fixed | Settle Table T2 dialog: "Cash sales count once in the shared front-desk drawer and appear in the POS day summary. UPI/card payments and charges to a room folio do not add drawer cash." (`04-settle-dialog-d01.jpg`). Cashier shift page also says "one shared till". |
| BL-01 approved discount shows Approved | Fixed | APR-1123 shows **Approved** ("decided by Meera") on the Room 105 folio right after approval (25 Oct), on 26 Oct after the clock passed the original 08:01 expiry, and after checkout on the Departed folio; Expenses → Approval requests also Approved. No "Expired" pill. |
| T-04 "1 nights" | Fixed on desktop | 1-night booking (14→15 Nov, HCC-1156): reservations list "1 night · 1 guest" (`11-t04-one-night-booking-list.jpg`), booking details "14 Nov — 15 Nov · 1 night · 1 guest", availability search "1 night · party of 1". |
| AC-04 native "Discard the details you entered?" on desktop | **Yes, partly** | `window.confirm` is called after a successful **count submit from the Day closing "Count & submit" sheet** (3 of 3). Not after the inline count on Cashier shift (0 of 2), payment / deposit / charge / discount / settle sheets, or Reports (desktop has direct From/To date inputs and no "Apply range"; changing the range gave 0 confirms). Logged as BL-v8-1. |

## BL-02 — policy limit editor (Owner → Admin & Setup → Access control → Role templates → Reception)

Editor text now reads: "Permission and scope changes submit immediately, with approval where required. Save limit applies only to its edited amount." The `folio.discount` row (PERM-FOL-003) shows "Limit ₹ [250] **Save limit**" with "Follows Hotel configuration → discount threshold (₹250)". Baseline: Exceptions 0, Policy history 1 (v1).

| Check | Result | Evidence |
|---|---|---|
| Unchanged inherited value 250 → Save limit disabled (re-typing 250 also disabled) | Pass | `bl02-1-save-limit-disabled-unchanged.jpg` |
| Blank, `-5`, `1.234` (fractional paise) → disabled; `abc` is refused by the number input (field stays blank, disabled) | Pass | DOM state per value |
| Changed valid values (`300`, `0`, `1.5`, `100.25`, `101`, `1000000000`) enable Save limit | Pass (a rupee limit has no upper bound; the percentage cap is not exercised by this field) | DOM state |
| Save 300 → toast "Limit updated", button disabled again (now unchanged), history v3 "not set → ₹300" | Pass | Policy history |
| Save 0 → saved, history v4 "₹300 → ₹0" (zero is saveable) | Pass | Policy history, audit |
| Toggling a permission (Reception `reservation.create` allow → not granted, which is also step 12) creates **no** inherited-limit override | **Pass** | Policy history 1 → **2** only (v2 "Role template · Reception · PERM-RES-002 allow → not granted"); limit still "250 / Follows Hotel configuration", Save limit still disabled, Exceptions 0. The old behaviour was 1 → 3 with an extra "Limit … not set → ₹250" row. `bl02-2-history-v2-only-permission.jpg` |
| Audit | Pass | Approvals & audit shows POLICY SET rows only for what was changed (v2 template, v3/v4 limits, Aarav disabled) |

**BL-02: confirmed fixed in the UI.** (Limit saves made during the test, ₹300 then ₹0, are test data in this browser only.)

## Defects

| ID | Severity | Steps | Expected | Actual | Evidence |
|---|---|---|---|---|---|
| BL-v8-1 (= AC-04 on desktop) | S3 | Day closing → Count & submit → enter counted amount → Submit count & close shift | Sheet closes silently after success | `window.confirm('Discard the details you entered?')` is called after the successful submit (native OK/Cancel in a real browser) | `defects/BL-v8-1-discard-confirm-after-count-sheet-desktop.md` |

No other defects found. Earlier BL-01, BL-02, D-01 and T-04 (desktop) do not reproduce on v8.

## Screenshot index (`bl023-v8\`)
`01-overview-24oct.jpg`, `04-settle-dialog-d01.jpg`, `05-approved-discount-bl01.jpg`, `08-invoice-4400.jpg`, `09-day-closed-rerun-refused-26oct.jpg`, `10-reports-24-26oct.jpg`, `11-t04-one-night-booking-list.jpg`, `bl02-1-save-limit-disabled-unchanged.jpg`, `bl02-2-history-v2-only-permission.jpg`.
