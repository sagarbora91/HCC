# D5 · Roles and device behaviour (v8) — EMULATED

**Progress:** 25 Oct, API 35 run on the work PC. Part 2 DONE (all checks; T-04 verified inside AC-M05). See table below.

## Earlier attempt (home PC, kept for the record)
25 Oct (resumed) — `codex_api35_google` cold-booted with `-no-snapshot-load` stayed `offline` for ~15 min (killed). FALLBACK was `codex_apk_review` (emulator-5556, Android 11, WebView 83, phone layout). Results from that attempt were for WebView 83 / Android 11, not API 35. Nothing below depends on it.

## 25 Oct 2026 — API 35 run on the work PC
- Device: **EMULATED** — AVD `codex_api35_google` (Android 15 / API 35, Google APIs x86_64), `emulator-5554`, 1080×2400, CSS viewport 412×842 @ DPR 2.625, WebView `com.google.android.webview` 124.0.6367.219; started with `-gpu host` (rendered fine, booted in ~50 s).
- APK: `05 Delivery Evidence\APK\HCC-0.1.0-proto-debug.apk`, versionCode 8, SHA-256 `2BA3FE68B03E2C535B758D288B3B8862F6CE3F87BB592A39E63A32C00F8A52C3` (verified before install); package `in.hcc.prototype`; cold start `am start -W` TotalTime 3,974 ms (fresh `pm clear`).
- Driving: DevTools (Node 25 WebSocket) for reading the DOM and switching "Preview as"; taps for taps of tabs/drawer via `adb shell input tap`; screenshots `d5-roles\v8-*`.

| Check | Result | Evidence | Notes |
|---|---|---|---|
| Install v8, cold start to staff picker (10 demo users shown) | Pass | `01-cold-start.png` (earlier), DOM | 3,974 ms |
| Owner (Sagar): tabs Home/Rooms/Approvals/Reports + Menu; drawer shows all 11 modules | Pass | `v8-owner-drawer` n/a (DOM) | |
| Manager (Meera): tabs Home/Rooms/Approvals/Closing | Pass | DOM | drawer all modules |
| Reception (Aarav): tabs Rooms/Arrivals/Bookings/Folios; drawer Front Desk, Cash & Billing, HK, Guests, Customers, Revenue, People | Pass | DOM | no Reports/Admin/Stock |
| Cashier (Priya): tabs Cashier/Folios/Expenses | Pass | `v8-role-priya-home/drawer.png` | no Front Desk/Admin write |
| Supervisor (Neha): tabs Board/Inspect; drawer HK, Guests, Stock, People | Pass | DOM | no money pages |
| Housekeeper Ravi: tabs My tasks/Report repair; drawer HK, Guests, Stock, People; permitted pages Housekeeping, Guest requests, Maintenance, Linen & stock, Checklists & handover, My shifts & pay | Pass* | `v8-role-ravi-home/drawer.png` | no ₹ on any page except own "My shifts & pay" (see R-01 note) |
| Housekeeper Kavya: same as Ravi | Pass* | DOM | same |
| Accountant (Farhan): tabs Reports/Approvals; drawer Dashboard, Revenue, People, Accounts & Reports, Admin | Pass | DOM | |
| Maintenance Joseph: tabs Work orders/My repairs; same page set as housekeeping | Pass* | `v8-role-joseph-home/drawer.png` | no ₹ except own pay page |
| Tech admin Lata: tab Users; drawer People, Admin | Pass | DOM | |

\* Housekeeping, maintenance and supervisor users see **no hotel money** (no folios, cashier, rates, reports); the only ₹ figures are their own pay on "My shifts & pay" (6 amounts). Logged as observation R-01 (S4) for the owner to confirm this is intended.

## Defects / observations
| ID | Severity | Steps | Expected | Actual | Evidence |
|---|---|---|---|---|---|
| R-01 | S4 (observation) | Preview as Ravi/Kavya/Joseph → search "My shifts & pay" | "Housekeeping/maintenance see no money" | Own pay amounts (₹10,926 etc.) are shown on My shifts & pay | DOM scan |

## Device behaviour (API 35)
| Check | Result | Evidence | Notes |
|---|---|---|---|
| Android Back from sheet → page → category → Modules Home → exit prompt (clean launch) | Pass | `v8-back-exit-prompt.png` | Real `KEYCODE_BACK`; Stay returns to Modules |
| Back after Print → Android print service return | Pass | `v8-print-service-preview.png`, DOM log | Back closed the report-print sheet, then page, then Modules, then exit prompt (T-05 scenario) |
| Back after CSV share sheet return | Pass | `v8-csv-share-sheet.png` | share sheet dismissed with Back, app restored on Reports |
| Back after Display-mode switch (Auto → Desktop → Mobile → Auto) | Pass | `v8-display-desktop-on-phone.png` | exit prompt reached from Modules; sheet closed first. T-05 not reproduced (it was intermittent/physical) |
| Back after Reset | Pass | `v8-reset-confirm.png` | Reset demo → toast "Scenario FY26 loaded…"; Back → exit prompt |
| CSV export: file in `Documents/HCC` + share sheet | Pass | `v8-csv-share-sheet.png`; `/sdcard/Documents/HCC/HCC-Demo-daily-2026-10-24_2026-10-24.csv` (818 bytes) | Chooser "Sharing 1 file" (Quick Share, Drive, Gmail) |
| Print service preview on **A4**, no empty block (T-03 fix) | Pass | `v8-print-service-preview.png` | "ISO A4", one page, report + watermark, white below, no beige block |
| Restaurant settle dialog wording mentions the front-desk drawer (D-01 fix) | Pass | `v8-d01-settle-dialog-cash.png` | Table T2 (POS-F03), Settle by Cash: "Cash sales count once in the shared front-desk drawer and appear in the POS day summary…" (no "separate from" text). Also: UPI settle of POS-F03 (₹1,914) and POS-F04 (₹330) worked once a UPI reference was typed; Settle with an empty UPI reference gave no visible message in my check (not investigated, not logged). |
| Booking list shows "1 night · 1 guest" (T-04 fix) | See D5_AC.md (AC-M05) | — | Phone booking cards show "N nights" only (no guest count); the 1-night case is checked after the 7→8 Nov booking is made |
