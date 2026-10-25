# Polish browser verification, 25 Oct 2026

Build under test: `E:\Projects\HCC\worktrees\int-polish` (integration/polish at 78f8833, 757 tests), built in place (`dist/index.html` 2,271 KB) and served on 127.0.0.1:4193 (server stopped at the end). No product code changed, nothing committed. Scenario FY26.

## 0. Method and limits

- Playwright (Chrome, headless-equivalent, own fresh browser context per run so IndexedDB is empty on first load); CDP `Emulation.setCPUThrottlingRate` 1 and 4; desktop 1366x768, phone 360x800 (mobile emulation, touch, 2x). Other agents were using this PC, so figures at 4x are order of magnitude (runs differ up to about 1.5x).
- Page-open time = input event to last DOM mutation or second animation frame, whichever is later (settled). Long task = main-thread task over 50 ms (PerformanceObserver). IndexedDB writes counted by wrapping `IDBObjectStore.put` (characters of the stored JSON string, about equal to bytes).
- "Before" = A11y_Performance_Audit_2026-10-25.md (branch w05, desktop 1x/4x). Its 6x runs are not repeated.
- Not done: real device or old WebView, screen reader, 200 % zoom, Housekeeper reaching a guest profile (no route exists, see 6).

## 1. Performance, before and after (desktop, 1366x768)

| Measure | Before 1x | After 1x | Before 4x | After 4x |
|---|---|---|---|---|
| First run (empty IndexedDB) to sign-in screen | 0.67 to 1.0 s | 1.1 s | 6.7 to 13.6 s | **2.7 s** |
| Longest main-thread task on first run | 570 to 850 ms | **207 ms** | 6.1 to 12.3 s | **1.29 s** |
| Returning user, sign-in screen visible (4 loads) | about 0.3 s to interactive; first paint 130 to 150 ms | 140 to 240 ms (FCP about 90 ms) | 1 to 2 s; first paint 520 to 690 ms | 0.73 to 0.89 s (FCP 285 to 355 ms); longest task 130 to 233 ms |
| Command: payment (Post receipt) | n/a (housekeeping start 177 ms long task) | 64 to 71 ms, no long task (first command of a session had one 301 ms task) | 398 to 1,036 ms (housekeeping) | 242 to 312 ms, longest task 172 to 251 ms |
| Command: housekeeping Start cleaning | 177 ms long task | 67 ms, no long task | 398 to 1,036 ms | 305 ms, longest task 236 ms |
| Command: new booking (Confirm reservation) | n/a | 119 ms, no long task | n/a | 672 ms, longest task 398 ms |
| IndexedDB written per command | whole state, 8.28 M chars, 1 record | payment 7 records 1.30 M; housekeeping 7 records 0.17 M; booking 11 records 1.37 M | same | same |
| Reports, full FY ("FY history") | 730 ms (634 ms task) | **54 ms** (no long task) | 4.9 to 6.3 s (4.7 to 6.0 s task) | **378 ms** (278 ms task) |
| Reservations open | 203 ms (131) | 67 ms (56) | 1.07 to 1.37 s (0.86 to 1.1 s) | 542 ms (459) |
| Guests and folios open | 167 ms (97) | 65 ms (51) | 1.19 s (913) | 537 ms (473) |
| Customers open | 171 ms (89) | 42 ms (0) | 1.39 s (1.24 s) | 391 ms (347) |
| Cash book, 14 days | 101 ms | 35 ms | 321 ms (220) | 257 ms (242) |
| Cash book, 1 Apr to 24 Oct range | 202 ms (76) | 52 ms (0) | 1.12 to 1.20 s (895) | 278 ms (246) |
| Dashboard / Reports today | 181 / 118 ms | 57 / 27 ms | 404 to 437 / 432 ms | 413 / 241 ms |

Phone 360x800 (after): first run to sign-in 1.12 s at 1x (longest task 187 ms), 2.75 s at 4x (longest 1.42 s); returning 149 ms (1x), 810 ms (4x, longest 212 ms). Page opens at 1x / 4x: Reservations 87 / 321 ms, Folios 69 / 257, Customers 37 / 241, Cash book 18 / 144, Reports 26 / 195, Reports FY via range sheet 83 / 384 ms (335 ms task), housekeeping Start cleaning 49 ms / 125 ms (IndexedDB 7 records, 0.17 M chars). Scenario switch back to FY26 1.2 s; Reset to a fresh FY26 about 3 s.

Reading: the first-run block, FY report, page opens and command latency are all clearly better (first run 4x 6.7 to 13.6 s to 2.7 s; FY report 5 to 6 s to 0.38 s; command long tasks cut to about 0.2 to 0.4 s at 4x). Per-command storage fell from 8.28 M to 0.17 to 1.4 M characters, but any command that touches bookings (payment, new booking) still rewrites the 1.25 M character bookings slice, which is now the dominant cost. Reservations, Folios, Customers and Housekeeping still show 0.3 to 0.5 s tasks at 4x (2,300 DOM nodes on the two lists).

## 2. Data safety

| Check | Result |
|---|---|
| New booking, housekeeping start and a payment, then reload (the app stays signed in) | PASS: booking found by search, housekeeping In progress 2 kept, folio balance 0 kept, payment toast "Receipt recorded once" |
| "Reset" notice on a normal reload | PASS: none (only the Reset button text) |
| Scenario switch to BASE, reload | PASS: still BASE after reload, no notice; switching back to FY26 1.2 s |
| Reset (button, then "Reset demo" confirm) after an edit | PASS: edit reverted (In progress 2 back to 1), toast "Scenario FY26 loaded. All fictional records restored from the fixture." |
| Console errors | PASS: none (see 7) |

## 3. Accessibility

| Check | Result |
|---|---|
| Focus moves into each dialog, Tab stays inside (30 Tab, 8 Shift+Tab), Escape closes, focus returns to opener: Reservations "+ New reservation", Folio open guest, Arrivals "Start check-in", Compliance "Mark filed", Housekeeping "Room details" | PASS 5 of 5 (0 escapes; focus returned to the opener each time; audit had 6 of 6 failing) |
| Phone sheet: Cashier "Count drawer" (360 px) | PASS: focus inside, 0 escapes, Escape closes, focus back on opener |
| First focus | Sheet and new-reservation open on a field; Folio and Room details dialogs focus the dialog container (fine) |
| Visible focus ring | PASS: sign-in tile, Modules tiles, drawer items, search (ring on the label), date inputs all show a 3 px #7A1F2B outline (audit: none on sign-in, search, date). Screens: `polish-browser\focus-*.jpg` |
| Input border contrast | PASS: borders #8C7766 measured 3.94 to 4.25:1 on page/white (audit 1.29 to 1.9:1) |
| Disabled buttons | PASS: Complete cleaning is sand fill with dark text (about 6.6:1 computed), border, not-allowed cursor, and the reason text "Tick all checklist items to complete." beside it (audit 3.65:1) |
| Skip link | PASS: first Tab on Home shows "Skip to main content" (8,8 164x39); Enter moves focus to `main#main-content`. Page title changes per page ("Cash book · Hotel Control Centre") |
| AC-04: no "Discard the details you entered?" after a successful count submit | PASS: exact count (21x500, 1x200, 1x50) submitted, sheet closed, toast "Cash matches exactly - shift closed", no prompt |
| Unsaved sheet still asks | PASS: typing 3 notes then Escape raised the native confirm "Discard the details you entered?" and the sheet stayed open when dismissed |
| Observations | PB-1 (Escape ignored when focus is on BODY with a dialog open), PB-3 (phone buttons 15 px) |

## 4. V9-2 on phone (360 and 412 px)

| Item | Result |
|---|---|
| Cashier count switch (Denominations / Total) | PASS: 158x48 px (spec 48) |
| Reports range sheet labels "From (business date)" / "To" | PASS: 16 px at both widths (inputs 16 px, targets 48; the 3 wrapped preset buttons are 59 px) |
| Compliance chips and pills | PASS: 16 px (no text under 14 px found in main) |
| No sideways scroll at 360 px on 35 pages | PASS |

## 5. DEC-065 cash-book lock (Owner locks 23 Oct, a day counted by the cashier, then tests)

| Step | Result |
|---|---|
| Owner "Lock this day" 23 Oct | Locked by Sagar; text says corrections, expense reversals, refunds, deposit returns and bill voids are blocked until the Owner reopens |
| Day closing "Request correction" for 23 Oct (folio, amount 100, reason filled) | PASS: button disabled, message "Cash book 2026-10-23 is locked, so a correction is blocked. The Owner must reopen 2026-10-23 (with a reason) first." |
| Expense reversal EXP-H0492 (23 Oct) | PASS (refused): after reason and submit, "Business day closed: Cash book 2026-10-23 is locked, so reversing this expense is blocked. The Owner must reopen ..."; button itself not disabled (PB-2) |
| Refund request on receipt H-RC-02310 (23 Oct, HCC-H1248) | PASS (refused): same style message after submit; button not disabled (PB-2) |
| Owner "Reopen..." with reason | PASS: reason required (Reopen disabled until typed); status "Reopened by Sagar ... Perf test: reopening ...", audit line "Cash book 2026-10-23 reopened - <reason>" |
| After reopen | PASS: correction button enabled; expense reversal posted (EXP-1103, -Rs 605, dated 24 Oct) |
| Not checked | A counter or difference approver trying to lock (covered by `stream-cashlock` tests only), deposit-return and POS void paths |

Screens: `polish-browser\dec065-*.jpg`.

## 6. DEC-068 guest ID (Show ID)

| Check | Result |
|---|---|
| Register a guest with a fictional full ID (HCC-1049 Ishaan Bose, Driving licence "MH24 2026 0001234") in guided check-in | PASS: "Guest verified. Registration saved." Aadhaar entry is checksum-validated (a made-up Aadhaar number was rejected: "fails the checksum"; Aadhaar not used) |
| Profile shows masked ID | PASS: "Driving licence (demo) - XXXX-XXXX-1234"; full number not in page text before Show ID |
| Show ID roles | PASS: Owner, Manager (Meera) and Reception (Aarav) see "Show ID"; click reveals the number with toast "ID shown - this view is logged" and a Hide ID toggle |
| Audit | PASS: audit line "G-0009 - ID number viewed (Driving licence (demo))", number absent from the audit page text |
| Cashier (Priya) | PASS: ID row reads "Restricted", no button |
| Housekeeping | Not reachable: Housekeeper search finds no guest, folio, arrivals or customer page; only Guest requests (no ID). Supervisor and Accountant have no route to the folio page in my script (not retested) |
| Registration card | PASS: shows masked XXXX-XXXX-1234 even after Show ID was used |

Screens: `polish-browser\dec068-*.jpg`.

## 7. Console errors

None (no errors or warnings): sign-in, all 35 pages reachable through search on desktop and the same 35 on phone, all dialogs and sheets above, scenario switch, Reset, lock/reopen flows. The page "inventory" is not found by the search term "inventory" (module is called Stock; not an error).

## 8. Defects and observations

| ID | Sev | Summary |
|---|---|---|
| PB-1 | S4 | Escape does not close a dialog while focus is on BODY (after using the search palette over an open dialog) |
| PB-2 | S4 | DEC-065: expense "Reverse..." and folio "Request refund" buttons are not disabled for a locked day; refusal only on submit (rule itself enforced) |
| PB-3 | S4 | Phone buttons 15 px, rule is 16 px |

No S1 to S3 defects. Remaining performance headroom: the 1.25 M character bookings slice is rewritten on every booking-touching command; 2,300-node Reservations and Folios lists cost 0.45 s long tasks at 4x.

Files: screenshots in `E:\Projects\HCC\Hotel Control Centre\05 Delivery Evidence\Acceptance\polish-browser\`, defects in `...\Acceptance\defects\PB-*.md`.
