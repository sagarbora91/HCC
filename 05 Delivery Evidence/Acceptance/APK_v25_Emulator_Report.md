# APK v25 emulator report (11 Nov 2026, versionCode 26, versionName 25)

Branch claude/cu, head 13254225 (gate PASS on 27f01bca; the head only adds the gate report and the version bump). Nothing in product code was changed. Phone AVD then tablet AVD, one at a time, every adb call used `-s emulator-5554`; no physical device was attached.

## Builds
Folder `E:\Projects\HCC\Builds\2026-11-11\` (outside the repo, `SHA256.txt` in the v24 format).

| File | SHA-256 | Manifest (aapt dump badging) |
|---|---|---|
| HCC-25-vc26-demo-debug.apk | dae309a551ac46f0e73661ebb3f419dab0d1738b855c4cf9943a1f03ab3f38cd | in.hcc.prototype, versionCode 26, versionName 25 |
| HCC-25-vc26-server-debug.apk | ad709dcb6718ae4876cdeb6dffaed246850b4749647183b6fd9a12b2181dc554 | in.hcc.prototype.server, versionCode 26, versionName 25-server |
| HCC-Test-CA-throwaway.cer | da467d0f5226af75ce7998ddc61acfbd79e5b5d9f96d2e24a2a0b03d3d2bed04 | the 5 Nov throwaway test CA (same file the v24 server build took) |

Pipeline as v24, every heavy step through `tools\Invoke-Serialized.ps1` (label cu-apk): `npm run build`, `npm run build:production`, `npm ci --prefer-offline` in 04 Android, `copy-web.mjs`, `cap sync android`, `gradlew assembleDebug --offline` (JDK 21), `build-production.mjs --ca <cer> --assemble`. `git status` was clean after the build. The server APK was only built and version-checked: **not run** on any device, no server started, no certificate installed.

## Checks
Phone = `codex_api35_google` (1080x2400, 412 css px wide), screenshots `v25\ph-*`. Tablet = `hcc_tablet_api35` (2560x1600, 1280x744 css px, dark theme), screenshots `v25\tab-*`. Demo data only. Some facts were read from the page DOM through webview DevTools rather than from a screenshot; this is stated where it applies.

| # | Check | Result | Evidence / notes |
|---|---|---|---|
| 1 | App starts, version 25 | PASS | `dumpsys package` on both AVDs: versionName=25, versionCode=26; app opens to Modules Home (ph-01, tab-01). The demo app has no About page; version taken from package info. |
| 2 | Phone drawer: one "Admin & Setup" for Owner, admin pages reachable | PASS | Drawer has exactly one "Admin & Setup" entry (ph-07, ph-08); it opens Admin & Setup with Administration, Approvals & audit, Master configuration, Backup, System health, Legacy import, Roadmap, Appearance (ph-09); Administration and Master configuration opened. The Modules Home page also has an "Admin & Setup" tile; that is the home page, not the drawer. |
| 3 | Display mode Desktop on phone as Owner, Cashier, Tech admin | PASS (with a limit) | No horizontal scroll: scrollWidth = clientWidth = 412 for Owner (Administration page), Cashier, Tech admin (ph-12, ph-14, ph-15). Business date "24 Oct 2026 · Open" and the DEMO pill are present in the top bar in all three. Breadcrumb overlap was judged from screenshots only, not measured; none seen. Display mode set back to Mobile afterwards. |
| 4 | Wake-up alert | PASS | Logged a wake-up for room 101 at 08:30, advanced the demo clock +30 min as Manager (Housekeeping > Checklists > Dashboard). Reception: banner "Wake-up · Room 101 · 08:30 · Ananya Rao" with Done and Snooze 5 min (ph-42); Done pressed, toast "Wake-up call for room 101 done", banner gone (ph-44). Cashier: banner shows "Ask Reception to make the call", no Done (ph-43). |
| 5 | Cashier shift page (phone) | PASS | Formula "₹5,000 + ₹7,525 − ₹1,800 = ₹10,725 ... UPI, card, bank, vendor credit and room charges are excluded" (ph-27). After a ₹1,000 till transfer: "₹5,000 + ₹7,525 − ₹2,800 = ₹9,725 ... bank deposits, till transfers, Owner drawings and vendor payments net -₹1,000" (ph-31). The vendor-payments wording appears only when transfers are non-zero. No real vendor payment was made; the transfer stands in for it. The transfer stays pending in the demo data. |
| 6 | Phone payment sheet methods from the master | PASS | Record payment sheet on folio HCC-1041 offers Cash, UPI, Card, Bank, Split (read from the DOM; the screenshot of the sheet was not kept, ph-33 shows the folio). The master lists 6 methods plus retired "Wallet app" (ph-36, ph-37); Wallet app, Vendor credit and Company credit are not offered. |
| 7 | Tablet: Manager menu, lists, Messages, counter bill, payments | PASS | Manager menu shows the right entries (Admin & Setup reduced to Approvals & audit, Roadmap, Appearance: tab-03, tab-04). Tapping a folio row opens it in the side panel (tab-06). Fixed height: document scroll 0 on Folios, the list body is the scroller (380 px window over 6,532 px of rows) (tab-05). Messages: Compose, template "Payment receipt" fills the text, Queue message, toast "Message queued (simulated)", MSG-1111 at the top of the outbox as Queued (tab-14, tab-16). Counter bill (from the menu; the Alt+B key itself was not pressed): Laundry ₹500 + GST ₹90 = ₹590, Issue, cash, DEMO/2026-27/1156 Issued (tab-10, tab-12). Folio payment: Record payment, methods Cash / UPI / Card / Bank transfer, Review, Post receipt, toast "Receipt recorded once", balance ₹0 (tab-07, tab-09). |
| 8 | Hall booking > Templates: edit and save terms | PASS | As Owner: clause 1 text changed, "Save as new version": toast "Terms saved as a new version", General (all events) v1 to v2 from 24/10/2026 (tab-17, tab-19). As Manager the page is read-only (no Save button), as the page text says. |
| 9 | Cashier shift expected vs close | PASS | As Owner on the cashier shift page: expected ₹11,415 after the ₹100 receipt and the ₹590 counter bill; counted 11415, "None · exact", toast "Cash matches exactly — shift closed" (tab-21, tab-22). |
| 10 | OTA inbox accept | PASS | Inbox of 6; pre-check "Can accept · Room 106 (2 free)"; Accept on TK-30017 gives "TK-30017 accepted — booking created", inbox 5 (tab-24). One row correctly shows "Cannot accept" (party of 3 over capacity 2). |
| 11 | Reports: Cash book, Daily operations | PASS, one defect | Both open and run in the Report library (tab-26, tab-27). Cash book: receipts ₹8,215 (7,525 + 100 + 590), payments ₹1,800, closing ₹11,415, counted ₹11,415. Daily operations: 8 room-nights, receipts ₹21,600, expenses ₹1,800. See D25-1. |
| 12 | Dark and high-contrast themes on a folio | PASS | Dark: folio list, folio panel and payment dialog render dark with readable text (tab-06, tab-09). High contrast: white background, dark maroon text, thick outlines (tab-30). Honest note: in high contrast the "In house" status chips lose their filled background and read as small plain text; legible but weaker. Not compared pixel by pixel with Light. |

## Defects and observations
- **D25-1 (S4, cosmetic):** the total row of the Cash book and Daily operations reports shows "Invalid Date" in the Business date column (tab-26, tab-27); numbers are right. Steps: Accounts & Reports > Report library > Cash book or Daily operations, default range.
- **Observation, not investigated:** the tablet logcat dump had one line matching "FATAL EXCEPTION" or "ANR in" (phone: 0). The emulator was shut down before I read it, so process and cause are unknown; no step was affected.

## Not run / limits
- Server APK: built and version-checked only (not run, no server, no CA installed).
- Physical Galaxy Tab S7, release APK, rotation, the Alt+B shortcut itself, a real vendor payment, printing.
- Phone checks ran on the 412 css px AVD, not a 360 dp device.
- Demo data left changed inside the AVDs (a wake-up request, a pending till transfer, a counter bill, a receipt, hall terms v2, OTA TK-30017 accepted, shift closed). Both AVDs were shut down.
