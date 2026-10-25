# D5 · AC-M05…M08 phone flows — EMULATED

**Progress:** 25 Oct, API 35 (work PC), APK v8. Done: Part 1/AC-02 (earlier), AC-M08 block check, AC-M05 (incl. restricted stay + manager override). Next: deposit + cashier flow (AC-M06), M08 close, M07, BL-01. Results are in the "Parts 2-3 on API 35" section at the end.

- Date: 24 Oct 2026 · Agent D-C
- Device: **EMULATED** — AVD `codex_apk_review` (`sdk_phone_x86_64`, Android 11 / API 30, AOSP image without Play), serial `emulator-5556`, 1080×1920 @ 420 dpi (CSS viewport 412×660, DPR 2.625), DevTools port 9232
- WebView provider: `com.android.webview` **83.0.4103.120** (the only provider on this image; cannot be updated without installing an outside APK)
- APK: `05 Delivery Evidence\APK\HCC-0.1.0-proto-debug.apk`, versionCode 6, versionName 0.1.0-proto, SHA-256 `49c184d32d5e7eb099e474b185b9b9926e8bb38f8eb1ff940ccf86eddf75946b` (fresh install; cold start `am start -W` TotalTime 2,228 ms)

## Results

| Check | Result | Evidence | Notes |
|---|---|---|---|
| Install versionCode 6 | Pass | `adb install -r` → Success; `dumpsys package` versionCode=6 | — |
| Cold start / app loads to sign-in | **Fail** | `defects\AC-01-webview83-stuck-loading.png` | Stays on “Loading demo data…” forever; `RangeError: maximumFractionDigits value is out of range` at `new Intl.NumberFormat` (defect AC-01) |
| AC-M05 reception booking → check-in (+ restricted 7→8 Nov stay, manager override) | Not run | — | Blocked on this AVD (AC-01); paused by owner; to be run on `emulator-5554` |
| AC-M06 cashier ₹3,400 split + deposit return + shift close = S0 oracle | Not run | — | same |
| AC-M07 housekeeper cleans Room 105 (48 px, no money) | Not run | — | same |
| AC-M08 manager closes the day (open checks block first; report totals = desktop) | Not run | — | same |

**Test-only shim tried (abandoned):** a CDP `Page.addScriptToEvaluateOnNewDocument` script (scratchpad only; nothing in the APK or repo changed) wrapped `Intl.NumberFormat`. Loading then failed at the next incompatibility, `ReferenceError: structuredClone is not defined` (needs Chrome 98+; used in `03 Prototype\src\services\mock\commands.ts:18` `execute()` and in fixtures). Shimming further was not pursued: results would not represent the real APK, and the owner paused the run.

## AC-01 fix (product code, uncommitted, owner-approved 24 Oct)

- `03 Prototype\src\domain\money.ts`: `minimumFractionDigits` now set with `maximumFractionDigits`.
- New `03 Prototype\src\domain\clone.ts` (`clone` = JSON deep copy); `structuredClone` replaced in `fixtures\ext\pos.ts`, `fixtures\history\generate.ts`, `services\mock\commands.ts`, `services\mock\handlers\assistant.ts`.
- `npx tsc --noEmit` clean; `npm test` 490/490 (one earlier run showed 489/490, then 490/490 on re-run twice; likely a timing-sensitive test — not investigated).
- NOT yet rebuilt: `dist\index.html` and the APK still contain the old bundle, so AC-01 stays open until the lead rebuilds (versionCode 7) and the AC flows are re-run. Other Chrome-83 gaps beyond these two were not checked.

## Defects

| ID | Severity | Steps | Expected | Actual | Evidence |
|---|---|---|---|---|---|
| AC-01 | S1 blocker (on WebView < ~85; APK declares minSdk 24) | Fresh install of the v6 APK on an Android 11 AVD whose WebView is Chrome 83 → open app | App reaches sign-in | Hangs on “Loading demo data…”. Console: `RangeError: maximumFractionDigits value is out of range` from `money()` in `03 Prototype\src\domain\money.ts:2` — `new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR',maximumFractionDigits:0})`; older V8 (8.3) rejects max < the currency default minimum (2). Reproduced directly in the page: same call throws, adding `minimumFractionDigits:0` returns `₹10,750`. Suggested fix (lead): pass `minimumFractionDigits` explicitly (0 or 2) alongside `maximumFractionDigits`; same pattern in `channels.ts:38`, `yield.ts:61`, `BookingSite.tsx:10` is harmless (no currency style) but worth aligning. Also consider a minimum WebView version check/message. **Second blocker behind it:** `structuredClone` (Chrome 98+) is used by `execute()` (`services\mock\commands.ts:18`), `fixtures\ext\pos.ts`, `fixtures\history\generate.ts`, `handlers\assistant.ts` → on WebView < 98 the app cannot load even with the Intl fix. Decision for the lead/owner: either raise the supported floor (WebView ≥ 98, i.e. updated Android System WebView; document it, maybe raise minSdk) or replace `structuredClone` with a JSON/own deep-copy. The owner's Tab S7 (Android 13, updatable WebView) is not affected. | `defects\AC-01-webview83-stuck-loading.png` |


## AC-01 re-test (v7) — 25 Oct 2026 · EMULATED, WebView 83

- Device: AVD `codex_apk_review` (Android 11, `com.android.webview` 83.0.4103.120), `emulator-5556`, DevTools 9232. APK versionCode 7, SHA-256 `E87C692BD2DFE7EB70FE500D847CF1087340ADD21F4B5272CCAFCD4C7A5173EF`, fresh install (`pm clear`), cold start `am start -W` TotalTime 2,355 ms.

| Check | Result | Evidence | Notes |
|---|---|---|---|
| App gets past "Loading demo data…" to sign-in | **Pass** | `d5-ac\v7-ac01-signin-webview83.png` | AC-01 (Intl + structuredClone) is fixed. |
| Sign in as Owner (Sagar) → Modules Home | Pass | tap on staff card | Home renders, bottom tabs OK. |
| Cashier shift shows ₹10,750 with ₹ symbol and correct digits (S0) | Pass* | `d5-ac\v7-ac01-cashier-shift-webview83.png` | ₹5,000 + ₹7,550 − ₹1,800 = ₹10,750; restaurant ₹550 line. *Needed the Demo sheet (scenario S0) — see AC-02 shim note. |
| Post one payment | Pass* | `d5-ac\v7-ac01-payment-posted-webview83.png` | Folio HCC-1041 ₹200 cash: Review → Confirm → toast "Receipt recorded once", outstanding ₹1,950 → ₹1,750, folio Settled. *Done with the AC-02 shim. |

**AC-02 (new, S2 on WebView < 87):** every modal / bottom sheet / search / exit prompt is rendered **below the viewport** (invisible) because `.modal-overlay{position:fixed;inset:0}` (`styles.css`, also `search.css`, `native.css`, `home.css`, `mobile-ops.css`, `reservations.css`) — the `inset` shorthand needs Chrome 87+. On WebView 83 tapping DEMO, Record payment, Count drawer etc. opens a sheet at top=1449 px (page height) that the user cannot see; the app seems unresponsive. **Test-only workaround used for this re-test (CDP-injected style, nothing changed in repo/APK):** `.modal-overlay,.search-overlay,.exit-ask,.sidebar-overlay{top:0;right:0;bottom:0;left:0}` — with it all flows work. Fix for lead: replace `inset:0` by `top:0;right:0;bottom:0;left:0` (and `inset:6px` in `mobile.css:21`). Minor on WebView 83: `:has()` rules (toast offset when a sheet is open, `mm-invoice` sheet) are ignored. Evidence: `defects\AC-02-webview83-sheets-offscreen-no-inset.png` (tap on DEMO — nothing visible).


## AC-02 re-test (v8) — 25 Oct 2026 · EMULATED, WebView 83

APK versionCode 8, SHA-256 `2BA3FE68B03E2C535B758D288B3B8862F6CE3F87BB592A39E63A32C00F8A52C3`, `codex_apk_review`, fresh data, **no shim injected**.

| Check | Result | Evidence | Notes |
|---|---|---|---|
| DEMO sheet visible | **Pass** | `d5-ac\v8-ac02-demo-sheet-webview83.png` | overlay y=0, height = viewport (660 css px); previously y=1449. |
| Scenario picker (S0 load) and Record payment sheet visible | **Pass** | `d5-ac\v8-ac02-record-payment-sheet-webview83.png` | Real taps; "Record a payment · HCC-1041 Ananya Rao" sheet on screen (overlay y=0). |

AC-02 is **fixed** in v8. Cold start 3,284 ms.


## Parts 2–3 on API 35 (work PC) — 25 Oct 2026 · EMULATED

Device: `codex_api35_google` (Android 15 / API 35, WebView 124), `emulator-5554`, phone layout 412×842 css px; APK v8 SHA-256 `2BA3FE68…8A52C3` (verified). The earlier WebView-83 notes above are kept. Scenario S0 loaded via DEMO → Scenario. Real taps (`adb shell input tap`) for key actions; DevTools only for reading the DOM and typing into fields.

| Check | Result | Evidence | Notes |
|---|---|---|---|
| AC-M08a Open restaurant checks block the close (24 Oct) | Pass | `d5-ac\v8-m08-close-blocked-open-checks.png` | Manager Closing: "Table T2 · POS-F03 · ₹1,914 open", "Room 201 · POS-F04 · ₹330 open", EXP-102 unresolved; button "Resolve preflight (3 left)"; close step "4 checks still blocking" |
| AC-M05 Reception books Room 105, 24→26 Oct, 2 adults, Phone, Rahul Verma (fictional) | Pass | DOM; `v8-m05-checkin-done.png` | 4-step wizard, ₹2,000/night, ₹4,000 + tax; booking HCC-1101 confirmed |
| AC-M05 ₹1,000 UPI advance DEMO-UPI-0001 | Pass | DOM | Folio credit ₹1,000, balance −₹1,000 |
| AC-M05 Check in on phone (Arrivals, 6-step wizard) | Pass | `v8-m05-checkin-done.png` | "checked in to room 105"; needed address, ID type, last 4 and the tariff-confirmation tick |
| AC-M05 Restricted 7→8 Nov stay shows warning (Reception) | Pass | `v8-m05-restricted-warning.png`, `v8-m05-restricted-reception-blocked.png` | "Minimum stay 2 nights … (Diwali weekend). Ask a manager to override…"; Next does not advance |
| AC-M05 Manager override | Pass | DOM | Meera: "Override restriction" tick + reason; review shows "Overridden: …"; booking HCC-1108 confirmed |
| T-04 phone booking list "1 night" | Pass* | `d5-roles\v8-booking-list-1-night.png` | Card reads "Room 101 · 7 Nov – 8 Nov · 1 night". *Phone cards show no guest count, so "1 night · 1 guest" exists only in the desktop table; not a defect |
