# APK v26 emulator report (12 Nov 2026, versionCode 27, versionName 26)

Branch claude/fx, base 318288c1 (gate PASS on 4f7366ee; the head only adds the gate reports and the version bump). Nothing in product code was changed. Phone AVD, then tablet AVD, one at a time; every adb call used `-s emulator-5554` (the only device attached; no physical device was present). Both AVDs are shut down.

## Builds
Folder `E:\Projects\HCC\Builds\2026-11-12\` (outside the repo, `SHA256.txt` in the v25 format).

| File | SHA-256 | Manifest (aapt dump badging) |
|---|---|---|
| HCC-26-vc27-demo-debug.apk | e34d8a2405865f458c83423c05a36e49cd745aeb5a9f39e1386dfeac0fcb33d7 | in.hcc.prototype, versionCode 27, versionName 26 |
| HCC-26-vc27-server-debug.apk | 43aa36ed9be6548ea220fdccb1f8a8207036312afef77009a16a5a2057941845 | in.hcc.prototype.server, versionCode 27, versionName 26-server |
| HCC-Test-CA-throwaway.cer | da467d0f5226af75ce7998ddc61acfbd79e5b5d9f96d2e24a2a0b03d3d2bed04 | the 5 Nov throwaway test CA (same file as v24/v25) |

Pipeline as v25, one serialized job (label fx-apk, through `tools\Invoke-Serialized.ps1`): `npm run build`, `npm run build:production`, `npm ci --prefer-offline` in 04 Android, `copy-web.mjs`, `cap sync android`, then `gradlew assembleDebug --offline` and `build-production.mjs --ca <cer> --assemble` (JDK 21). The first pass stopped at the Gradle step (I called `gradlew.bat` instead of `.\gradlew.bat`); the Gradle steps were then re-run through the lock on the same synced tree. `04 Android\node_modules` was not present in the fx worktree: `npm ci --prefer-offline` created a real folder there (git-ignored, no junction, nothing else touched). `03 Prototype\node_modules` stays the existing junction to the fixwave-a worktree and was not modified. `git status` clean after the build. The server APK was only built, installed and version-checked: **not run**, no server, no certificate installed. `dumpsys package` on both AVDs: demo versionName=26 / versionCode=27, server versionName=26-server / versionCode=27.

## AVDs
- Phone `codex_api35_google`: 1080x2400 at 420 dpi = **412 css px wide** (innerWidth 412 x 842), Android 15 (API 35).
- Tablet `hcc_tablet_api35`: 2560x1600 at 320 dpi = 1280 x 744 css px (desktop layout), dark theme at start.
- Method: UI driven through `adb` plus the WebView DevTools socket (DOM clicks, DOM text and layout rectangles read from the page; screenshots by `adb exec-out screencap`). Where a fact comes from the DOM and not from a screenshot it is marked "DOM". Phone/tablet screenshots are in `05 Delivery Evidence/Acceptance/v26/` (`ph-*`, `tab-*`).
- The tablet AVD stayed `offline` for about 15 minutes after a quick-boot; I killed the emulator process and cold-booted with `-no-snapshot`. The v25 demo data (hall terms v2, counter bill, etc.) was no longer in the tablet app; it started from the seed (hall terms v1). Cause not investigated.

## A. Regression smoke (the 12 checks of the v25 report)
| # | Check | Result | Screenshot / notes |
|---|---|---|---|
| A1 | App starts, version 26 | PASS | ph-01-launch, tab-01-launch; version from `dumpsys package` (the demo app has no About page) |
| A2 | Phone drawer: one "Admin & Setup" for Owner, admin pages reachable | PASS | ph-07-owner-drawer, ph-09-admin-setup. DOM: exactly 1 "Admin & Setup" entry inside the drawer; it opens Admin & Setup with Administration, Approvals & audit, Master configuration, Backup & recovery, System health, Legacy import, Roadmap, Appearance. Administration / Master configuration not opened one by one this time (Master configuration was opened, see A6) |
| A3 | Display mode Desktop on phone as Owner, Cashier, Tech admin | PASS (with a limit) | ph-12, ph-14, ph-15. DOM scrollWidth = clientWidth = 412 for Owner, Manager, Cashier, Tech admin; "24 Oct 2026 · Open" and DEMO pill present. Breadcrumb overlap judged from screenshots only. Set back to Mobile afterwards |
| A4 | Wake-up alert | PASS | ph-25-wake-form, ph-42, ph-43, ph-44. Logged a wake-up call for room 101 at 08:30 (Guest services > New request > Wake-up call), advanced the demo clock with Manager > Checklists & handover > Dashboard +30/+60 min. Reception: banner "Wake-up . Room 101 . 08:30 . Ananya Rao" with Done and Snooze 5 min; Cashier: "Ask Reception to make the call", no Done; Reception Done: toast "Wake-up call for room 101 done", banner gone |
| A5 | Cashier shift page (phone) | PASS (formula only) | ph-27-cashier-shift: "5,000 + 7,525 - 1,800 = 10,725 ... UPI, card, bank, vendor credit and room charges are excluded". The after-till-transfer wording of v25 was NOT repeated (no transfer made) |
| A6 | Phone payment sheet methods from the master | PASS | ph-34-payment-sheet (sheet on folio HCC-1041: Cash, UPI, Card, Bank, Split), ph-36-payment-methods-master ("6 payment methods, 1 retired", Cash, UPI, Card, Bank transfer, Vendor credit, Company credit listed). The retired method's name and "Wallet app not offered" were not re-read |
| A7 | Tablet: Manager menu, lists, Messages, counter bill, payments | PASS | tab-03, tab-04 (Manager Admin & Setup = Approvals & audit, Roadmap, Appearance), tab-05 (document scroll 744/744; list body is the scroller, 380 px over 6,532 px), tab-06 (row opens the folio panel), tab-07 + tab-09 (Record payment: Cash/UPI/Card/Bank transfer, Review, Post receipt, toast "Receipt recorded once"), tab-10 + tab-12 (counter bill Laundry 500 + GST 90 = 590, DEMO/2026-27/1156 Issued; needs a customer name, first Issue with no name did nothing), tab-14 + tab-16 (template "Payment receipt", toast "Message queued (simulated)", MSG-1111 at the top). Alt+B itself not pressed |
| A8 | Hall booking > Templates: edit and save terms | PASS | tab-17, tab-19 (Owner: General v1 -> v2 -> v3 "Terms saved as a new version"). See B4 |
| A9 | Cashier shift expected vs close | PASS | tab-21, tab-22: expected 11,415, counted 11,415, "Cash matches exactly - shift closed" |
| A10 | OTA inbox accept | PASS | tab-24-ota-accepted: inbox 6 -> 5, "TK-30017 accepted - booking created"; 5 rows "Can accept", 1 "Cannot accept" |
| A11 | Reports: Cash book, Daily operations | PASS | tab-26, tab-27; numbers as v25 (Cash book range total receipts 8,215, payments 1,800, closing 11,415; Daily operations 8 room-nights, receipts 21,600, expenses 1,800). D25-1 is fixed, see B1 |
| A12 | Dark and high-contrast themes on a folio | PASS | tab-71-folio-dark, tab-70-folio-high-contrast (viewed: legible, thick outlines, the high-contrast "In house" chip is again plain small text as in v25) |

## B. New in wave FX
| # | Check | Result | Screenshot / notes |
|---|---|---|---|
| B1a | Cash book, phone: total/range row has no "Invalid Date" | PASS | ph-71-cash-book. DOM text has no "Invalid Date"; "Range total" card present |
| B1b | Daily operations, phone | PASS | ph-70-daily-operations ("Total" card, no "Invalid Date") |
| B1c | Receipts and deposits, phone | PASS | ph-72-receipts-deposits |
| B1d | Expenses and cash, phone | PASS | ph-73-expenses-cash |
| B1e | Cash book, tablet | PASS | tab-26-cash-book: "Range total | 5,000 | 8,215 | 1,800 | 11,415 | | 0", no "Invalid Date" |
| B1f | Daily operations, tablet | PASS | tab-27-daily-operations: Total row "0 0 8 0 21,600 1,800", no "Invalid Date" |
| B1g | Receipts and deposits, tablet | PASS | tab-28-receipts-deposits: Total row 7,100 / 14,500 / 0 / 0 / 21,600 |
| B1h | Expenses and cash, tablet | PASS | tab-29-expenses-cash: Total row 1,800 |
| B2a | Paging on the phone (Daily operations, From 2026-04-01, 207 rows) | PASS | ph-74, ph-75, ph-76. First page 50 rows, then the Total card (Arrivals 1,164, Net revenue 70,99,275 ...), "Showing 50 of 207 rows. Totals above include every row." and "Show 50 more"; after pressing: "Showing 100 of 207 rows", Total still directly above the button |
| B2b | Paging on the tablet (same report, 207 rows) | PASS | tab-30, tab-31. 100 rows, then the Total row (y 5,369 of the page, the "Show 100 more" button right under it); after pressing: 200 rows, "Showing 200 of 207 rows", exactly one Total row (1,163 / 1,155 / 2,671 ...) |
| B3a | Owner: Customers > Possible duplicates lists pairs with a reason (phone sheet from the Customers page, tablet tab) | PASS | ph-50, ph-51, tab-40: 4 pairs (Pooja Bhosale, Jay Wagh x2, Anil Agarwal), reason "Same e-mail address" |
| B3b | Open a pair, compare | PASS | ph-52-pair-compare, tab-41-pair-compare (mobile, e-mail, stays, nights, first/last stay, lifetime spend, preferences, notes; "suggested to keep" marker) |
| B3c | "Merge into ..." confirm text | PASS | ph-53, tab-42: stays / tags and notes / preferences / credit account / "not deleted: history is kept", "This cannot be undone from this screen. A second Owner or Manager must approve it first; nothing moves until then." plus a Reason field (pre-filled "Same e-mail address") |
| B3d | Same user cannot approve | PASS (UI level) | ph-54, tab-43: the request is listed "Needs another approver" / "Waiting for another approver"; the proposer gets no Approve / Reject button (DOM: none; phone card not tappable). The command-level refusal (a forged APPROVE by the proposer) was NOT exercised |
| B3e | The other person approves (Owner proposes, Manager approves; Manager proposes, Owner approves) | PASS | ph-55, ph-56, ph-57, tab-44, tab-45, tab-46 (Owner -> Manager, phone + tablet), tab-49, tab-50 (Manager -> Owner, tablet): toast "X merged into Y", "Recent decisions ... Approved" |
| B3f | Merged-away customer gone, its stays under the survivor | PASS (list level) | ph-59, tab-48: searching "Pooja Bhosale" shows the unrelated namesake (+91 97818 50373) and the survivor +91 91040 21099 now "2 stays"; the merged-away +91 92644 73708 is no longer listed; customer count on the tablet 1,154 after the first merge and 1,153 after the second (phone: 1,156 before). The survivor's stay list itself was not opened |
| B3g | "Not the same person" dismisses a pair | PASS | ph-58, tab-47: toast "Marked as not the same person. It will not be suggested again."; pair count 4 -> 2 (one merged, one dismissed) |
| B3h | Reception (and Accountant on tablet) see no tab / button | PASS | ph-60, tab-51: no "Possible duplicates" button on the phone, no tab on the tablet (DOM count 0). Cashier / Supervisor not checked (shell quoting error in my probe) |
| B3i | Phone sheet layout: opens from the Customers page, cards, no sideways scroll, targets >= 48 px | PASS | ph-51..ph-56. DOM scrollWidth 412 = innerWidth 412; Reason input 380x48, "Send merge for approval" 229x48, "Cancel" 79x48, "Approve and merge" 193x48, "Reject this request instead" 220x48, Back 89x48, list cards 63 px, Close 48x48 (measured with `getBoundingClientRect`, css px; `uiautomator dump` not used). The toast "Dismiss message" control is 28x18 (not part of the sheet) |
| B4a | Hall terms editable by the Owner (demo) | PASS | tab-17, tab-19, tab-23: General v1 -> v2 -> v3, Wedding v1 -> v2, each "Terms saved as a new version" |
| B4b | Edited terms appear on a hall quotation print preview | PASS | tab-24-wedding-quotation-print: preview of QT/2627/0002 (Wedding) shows the edited clause 1 text (DOM contains "Wedding: 25 percent advance ... (v26 check)"); the nine unedited clauses still read OWNER TO REPLACE with the Owner-to-replace warning. tab-22-hall-quotation-print is QT/2627/0001 made before the Wedding template was edited (it correctly shows the old Wedding terms: a quotation uses the template of its event type, General edits do not flow into Wedding) |
| B5a | Seven stock reports, phone, Owner | PASS | ph-81 Reorder alerts (rows), ph-82 Laundry batches (rows), ph-83 Stock counts (rows), ph-85 Consumption (rows), ph-86 Par and minimums (rows); Losses (ph-84) and Stock movements (ph-87) say "No rows" for the default one-day range and show rows when From = 2026-04-01 (ph-101, ph-102) |
| B5b | Seven stock reports, tablet, Owner, From = 2026-04-01 | PASS | tab-81..tab-87: Reorder 3 rows, Laundry 60, Counts 12, Losses 86 (with a Total row), Consumption 18, Par 7, Movements 100 (paged) |
| B5c | Totals on the stock reports | NOT CHECKED | Only Losses showed a Total row; I did not check which of the other six define one |
| B5d | The seven other newly server-side reports, phone | PASS (partly empty) | ph-95 Report dictionary (rows), ph-96 Earlier closing records (rows), ph-98 Expenses month and tax feed (Total), ph-99 Closed bills (Total), ph-97 Banking, transfers and drawings (no records in the demo: "No rows", totals per kind 0), ph-93 / ph-103 Configuration and masters history (no rows even from 1 April: no setting changes in the demo data). Export and print log: not found in the phone library (Admin & audit chips list Audit log, Configuration history, Rate sheet, Report dictionary, Scheduled runs; the Export log is the separate tab) -> NOT CHECKED on the phone |
| B5e | The same seven, tablet | PASS (partly empty) | tab-88..tab-94: config history 0 rows, Export and print log opens with 0 rows, Dictionary 12, Closing records 100, Banking 3 type rows + Total (no records), Expenses month 6 rows + Total, Closed bills 7 rows + Total; no "Invalid Date" anywhere |
| B6a | Merge screen in dark, phone | PASS | ph-6x-dark-list / -compare / -confirm (confirm viewed: readable, amber warning box legible); contrast probe over the dialog text: 0 of 12 text elements below 4.5:1 |
| B6b | Merge screen in high contrast, phone | PASS | ph-6x-hc-list / -compare / -confirm (compare viewed); probe 0 of 12 below 4.5:1 |
| B6c | Merge screen in dark, tablet | PASS | tab-6x-dark-list / -compare / -confirm; probe 0 of 9 below 4.5:1 (not all images viewed one by one) |
| B6d | Merge screen in high contrast, tablet | PASS (one defect) | tab-6x-hc-list / -compare / -confirm (compare viewed); probe 0 of 9 below 4.5:1; see D26-2 |

## C. Logcat (read before each shutdown)
- Phone: `adb logcat -d` 18,912 lines; `FATAL`: 0, `ANR in`: 0 (no AndroidRuntime crash).
- Tablet: 24,856 lines; `FATAL`: 0, `ANR in`: 0. Unrelated errors only: `.apps.wellbeing: Failed to open APK '/data/app/.../in.hcc.prototype-.../base.apk': I/O error` and `ResourcesManager: failed to add asset path ...` at 06:53, i.e. while `adb install -r` replaced the APK (Digital Wellbeing holding the old path); no HCC process line.
- No FATAL or ANR line to quote.

## Defects and observations
- **D26-1 (S4, cosmetic):** the compare table in Possible duplicates shows a lifetime spend with one decimal, "₹2,803.5" and "₹21,262.5" (ph-52, tab-41, tab-6x-hc-compare), where report totals show two ("₹74,40,838.90"). Steps: Customers > Possible duplicates > open the Pooja Bhosale or Anil Agarwal pair.
- **D26-2 (S4, cosmetic, high contrast only seen):** in the compare table the Notes row of the two customers runs together without a gap ("Fictional guest . FY 2026-27 history profileFictional guest . FY 2026-27 history profile", tab-6x-hc-compare). Not checked in Light.
- **D26-3 (S4, usability):** both "Merge into <name>" buttons and the pair titles carry the same text when the two customers share a name (all four demo pairs: "Merge into Pooja Bhosale" twice, "Pooja Bhosale . Pooja Bhosale"); the person has to know that the first button belongs to the first card ("suggested to keep"). A mobile number or e-mail suffix in the label would remove the doubt.
- **Observation:** a counter bill with an empty customer name does nothing visible when "Issue counter bill" is pressed (no error text seen); with a name it issues. Not investigated, may be existing behaviour.
- **Observation:** the proposer of a merge cannot approve in the UI (no button), but the screen says only "Needs another approver"; the server-side refusal was not tested.

## Not verified
- Real device (Galaxy Tab S7), release APK, rotation, physical printing (the print preview is the in-app dialog only; the Android print service was not driven), Alt+B itself.
- **Server APK pages:** installed and version-checked only; not started, no hotel server, no certificate installed, so no production sync of merges (the server path of Possible duplicates, the report paging against the real server, the server-side stock and FxC reports) was run. The demo run only proves the demo definitions.
- 360 px width: the phone AVD is 412 css px; the 48 px measures are in css px on that AVD.
- Command-level refusal of a same-person approval; survivor's stay list opened; Cashier / Supervisor / Housekeeping roles on Possible duplicates; totals on six of the seven stock reports (B5c); phone Export and print log; the till-transfer wording of A5; breadcrumb overlap in desktop mode on the phone (screenshot judgement only).
- `uiautomator dump` was not used; heights were taken from the page layout (DOM).
- Demo data left changed inside both AVDs (wake-up request, demo clock +90 min, payments, counter bill, hall terms v3 / Wedding v2, two hall quotations, OTA TK-30017 accepted, cashier shift closed, three customer merges and one dismissal; tablet theme dark, phone theme Light, phone display mode Mobile). Both AVDs were shut down.
