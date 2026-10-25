# APK v10 emulator check

Date 25 Oct 2026 · EMULATED: AVD `codex_api35_google` (Android 15 / API 35, 1080x2400, gpu host), WebView DevTools (Node 25) plus real `adb input tap` for key actions (payment, deposit, check-in, count submit, checkout, housekeeping start, share sheets); screenshots `apk-v10\v10-*`.

## Build facts
- Code: main, one new commit `26f1abf` (only `04 Android\android\app\build.gradle`, versionCode 9 -> 10). Not pushed.
- `03 Prototype\dist\index.html` SHA-256 `79d90c3f2eba5e801f87cc5f9a1b6ee526a3d7dc5aea998a6112c18e60519d0f` (verified before build).
- `node scripts\copy-web.mjs` + `npx cap sync android` + `gradlew.bat assembleDebug` with Microsoft JDK 21: BUILD SUCCESSFUL (31 s, warm cache).
- APK `05 Delivery Evidence\APK\HCC-0.1.0-proto-debug-v10.apk`, SHA-256 `1ca1616a03a2e05c84a8260e5ece30f5201a9069cd8ed9dcc621c7922c762846` (5,982,096 bytes); `HCC-0.1.0-proto-debug.apk` replaced with the same file; v8 and v9 copies kept (v9 `600aba41...2369`).
- Bundled `assets/public/index.html` inside the APK SHA-256 `79d90c3f2eba5e80...19d0f`, identical to dist. Manifest: package `in.hcc.prototype`, versionCode 10, versionName 0.1.0-proto.
- Signing: debug cert SHA-256 `3c49e759417d2294fe6ab2570ea6b776616d62b8e6d984b5fa3fb7e9c61fc8f0` = v9 cert (DEC-064).

## Upgrade and first run
| Step | Result | Notes |
|---|---|---|
| Install v9 clean, sign in as Owner, Housekeeping: "Start cleaning" (state saved as a single blob `state/current`, 8.3 MB, schema 8, FY26) | Done | Cleaning count 2 |
| `adb install -r` v10 over v9 | **Pass** | versionCode 10, no signature error, session kept (Owner still signed in) |
| First open of v10 | **Pass** | Same data (Cleaning 2), no reset / data-lost notice. v9 blob still present until the first save |
| First action in v10 (Start cleaning) | **Pass** | Stored layout changed to `manifest` + 31 `slice:*` records; `current` removed (migration done) |
| Force-stop and relaunch | **Pass** | Cleaning 3 / To do 2 kept after the migrated save |
| Clean install of v10 (uninstall, install, launch) | **Pass** | Cold launch to sign-in 4.5 s from `am start` (app start 2.6 s + data build). Reload test with data cleared: loading screen "Loading demo data… First start on this device: Generating stays and folios (5%)" with a progress bar, steps 5% to 93%, sign-in after 1.9 s from reload. An inline `Worker` was created exactly once (hooked `window.Worker`), so the FY26 build ran in the worker. Shots `v10-clean-loading`, `v10-clean-signin` |

## Checks
| # | Check | Result | Evidence | Notes |
|---|---|---|---|---|
| 1 | Upgrade keeps data; reload keeps changes; clean first run shows progress and reaches sign-in; worker ran | Pass | v10-upgrade-*, v10-clean-loading | See table above. After the whole S0 run a force-stop and relaunch kept drawer 12,425 and business date 26 Oct |
| 2a | S0 drawer BASE 10,725 | Pass | v10-s0-cashier-start | 5,000 + 7,525 - 1,800; restaurant cash 525 |
| 2b | 500 cash deposit -> 11,225 | Pass | v10-s0-drawer-11225 | After Reception booking of Room 105 (Phone, 2 adults, 1,000 UPI DEMO-UPI-0001), guided check-in |
| 2c | 24 Oct close (EXP-102 approved, T2 and room 201 settled by UPI, post nights, count 11,225), 25 Oct (Laundry 500 + GST 90, 500 discount, close, count 11,225) | Pass | v10-s0-24oct-closed, v10-s0-folio-balance-3265 | Opening float 25 Oct 11,225; folio on 26 Oct: charges 4,265, balance 3,265, deposit 500 |
| 2d | Settlement 2,000 cash + 1,265 UPI (DEMO-UPI-0002), deposit 500 returned -> 12,725 | Pass | v10-s0-drawer-12725 | Deposit return needed the Owner to reopen the locked 24 Oct (see 5a, V10-3) |
| 2e | 300 expense (Aqua Water Cans, cashier) approved by Manager -> 12,425 | Pass | v10-s0-drawer-12425 | |
| 2f | Room 105 invoice 4,265 with CGST/SGST | **Partial** | v10-invoice-gst, v10-invoice-print-preview | Total 4,265 correct (net 4,000 + GST 265). Phone invoice and its A4 print show one GST/Tax column only, no CGST/SGST split (V10-1). Checkout created one housekeeping task |
| 2g | Reception 500 discount, Manager approves | Pass | v10-s0-discount-pending, v10-s0-discount-pending-approvals | Limit 250 so pending; Meera approved; folio line -500 with tax -25 |
| 2h | Housekeeping and inspection of 105 | Pass | v10-s0-hk-105-ravi | Ravi started, 3 ticks, completed; Neha passed; all in audit |
| 3 | Command speed (payment, housekeeping start) | Pass | timestamps below | requestAnimationFrame gap monitor + PerformanceObserver longtask during real taps: payment 1,000 UPI confirm max frame gap 44 ms; deposit 500 max 41 ms; Post room nights (9) 31 ms; split payment 43 ms; checkout 42 ms; housekeeping Start cleaning 28 ms. No frame over 50 ms, no long task |
| 4a | Sheet opens with focus inside; Back closes and focus returns | Pass | v10-a11y-sheet-focus, v10-a11y-focus-returned | Room board tile 105: sheet `aria-modal=true`, focus on the dialog container; Back closed it and focus returned to the "105 Classic Ready" button |
| 4b | No "Discard the details you entered?" after a successful count submit | Pass | v10-a11y-after-count-submit | Day closing "Count & submit cash" 11,225: sheet closed, page responsive (no native confirm), shift reconciled |
| 5a | Cash-book lock message on a locked day | Pass | v10-cashbook-locked, v10-cashbook-lock-message, v10-deposit-return-blocked | Owner locked 24 Oct (Meera counted); "Business day closed: Cash book 2026-10-24 is locked, so reversing this expense is blocked. The Owner must reopen 2026-10-24 (with a reason) first." Same for deposit return. Owner "Reopen" with reason restored it |
| 5b | Show ID for Reception, logged in audit | Pass | v10-showid-masked-reception, v10-showid-shown-reception, v10-audit-guest-id-view | Guest profile > ID: masked `XXXX-XXXX-105X`, Show ID reveals the full value, Hide ID toggles; audit row "GUEST ID VIEW G-1105 ... Aarav · Reception" without the number |
| 5c | "Restricted" ID for Housekeeping | Partial | v10-id-restricted-accountant | Housekeeping and Maintenance have no phone route to a guest profile, so not reachable. Accountant (no check-in permission) sees "Restricted" and no Show ID (extra footnote, V10-2) |
| 5d | GST master visible (Owner) | Pass | v10-master-config-money-tax | Master configuration > Money and tax: 5% rooms <= 7,500, 18% above, 5% restaurant, 18% laundry/banquet/other, all "CGST + SGST halves, illustrative" |
| 6a | Android Back: sheet -> page -> Modules Home -> exit prompt | Pass | v10-back-exit-prompt, v10-back-exit-prompt2 | Sheet closes first and the page stays; then Back walks the visited-page history (Cash book > Master configuration > Approvals > Modules Home) and on Modules Home shows "Exit Hotel Control Centre? Stay / Exit"; Stay returns |
| 6b | CSV and Excel share sheet | Pass | v10-csv-share-sheet, v10-excel-share-sheet | Cash book 24-26 Oct: "Sharing 1 file cash-book-2026-10-24_2026-10-26.csv" and the .xlsx (18,392 bytes), both saved in `/sdcard/Documents/HCC/` |
| 6c | A4 print preview | Pass | v10-invoice-print-preview | Invoice > Print / share opens Android print preview, ISO A4, 1/1 |

Counts: Pass 17, Partial 2 (2f, 5c), Fail 0, Not run 0.

## Defects
| ID | Severity | Summary | File |
|---|---|---|---|
| V10-1 | S3 | Phone invoice and its A4 print show one GST column, no CGST/SGST split (desktop has it) | defects\V10-1-phone-invoice-no-cgst-sgst-split.md |
| V10-2 | S4 | "Show ID is logged" footnote shown on the ID section for a role with no ID access | defects\V10-2-showid-footnote-for-restricted-role.md |
| V10-3 | S4 | Observation: a locked cash-book day blocks the deposit return at checkout until the Owner reopens it (DEC-065 as designed) | defects\V10-3-lock-blocks-deposit-return-observation.md |

Notes: the emulator was shut down with `adb emu kill`. The 4,265 total was reached with the new GST (room nights 100 + 100, laundry 90, discount tax -25). The DevTools helper scripts are in the agent temp folder, not the repo. `05 Delivery Evidence\Acceptance\polish-browser\` was already untracked and was not touched.
