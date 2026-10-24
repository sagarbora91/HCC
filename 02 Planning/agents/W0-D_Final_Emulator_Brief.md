# W0-D final · Emulator checks on APK versionCode 7 — agent brief

25 Oct 2026. Finishes W0-D. Read `W0-D_Parallel_Acceptance_Brief.md` (fixed facts, DevTools driving tip, report format) and `W0-D_Acceptance_Brief.md` (scope/rules) first. Work in the main checkout `V:\Codex\Hotel Control Centre`; **do not commit, do not create branches, never push, do not change product code** (record defects). The lead commits evidence; a background job commits `05 Delivery Evidence` every 7 minutes.

**Laptop overheats — rules:** only ONE emulator running at any time; start it with `-no-boot-anim -no-audio -gpu swiftshader_indirect -memory 2048`; few screenshots; shut it down (`adb -s <serial> emu kill`) when its part is done. **Write your report file after EVERY check**, with a `**Progress:**` line at the top (last done / next).

APK under test: `05 Delivery Evidence\APK\HCC-0.1.0-proto-debug.apk`, **versionCode 7**, SHA-256 `E87C692BD2DFE7EB70FE500D847CF1087340ADD21F4B5272CCAFCD4C7A5173EF`. Its web bundle equals `03 Prototype\dist\index.html` (SHA-256 `631f4589…5012`). It contains fixes for AC-01, BL-01, BL-02, D-01, T-03, T-04, T-05.

## Part 1 — AC-01 re-test on the old WebView (≈10 min)
AVD `codex_apk_review` (WebView 83), port 5556 → `emulator-5556`, DevTools port 9232. Install v7; does the app get past "Loading demo data…" to the sign-in screen? Sign in as Owner, open Cashier shift (₹10,750 shown with ₹ symbol and correct digits), post one payment. Record in `05 Delivery Evidence\Acceptance\D5_AC.md` under a new section "AC-01 re-test (v7)". Shut this emulator down.

## Part 2 — roles and device behaviour (D-B scope)
AVD `codex_api35_google`, port 5554 → `emulator-5554`, DevTools port 9231. EMULATED. Install v7. All 10 demo users: drawer/tabs show only permitted pages; housekeeping/maintenance see no money. Android Back: sheet → page → Modules Home → exit prompt, including after print/share returns and a Display-mode switch (T-05 fix). Reset. CSV + share sheet. Print service preview on **A4** with no empty block (T-03 fix). Restaurant settle dialog wording mentions the front-desk drawer (D-01 fix). Booking list shows "1 night · 1 guest" (T-04 fix). Report: `05 Delivery Evidence\Acceptance\D5_Roles.md`, screenshots `…\Acceptance\d5-roles\v7-*`, defects `…\defects\R-*`.

## Part 3 — AC-M05…M08 on the same emulator (D-C scope)
AC-M05 reception booking → check-in on the phone (plus a restricted 7→8 Nov stay showing the warning; manager override). AC-M06 cashier ₹3,400 split (₹2,000 cash + ₹1,400 UPI), deposit return, shift close matching the S0 oracle (drawer path ₹10,750 → ₹11,250 → ₹12,750 → ₹12,450). AC-M07 housekeeper cleans Room 105 (48 px targets, no money). AC-M08 manager closes the day (open restaurant checks block first; report totals equal desktop). Also confirm BL-01 (approved discount shows Approved, not Expired). Report: `05 Delivery Evidence\Acceptance\D5_AC.md` (update the existing file; replace its PAUSED progress line), screenshots `…\Acceptance\d5-ac\v7-*`, defects `…\defects\AC-*`. Shut the emulator down at the end.

Time-box: a blocked check >10 min → record **Not run** with the reason and move on.

## Final answer (≤ 20 lines)
Pass/Fail/Not run counts per part, AC-01 result on WebView 83, defects (ID + one line), anything blocked.
