# D5 · AC-M05…M08 phone flows — EMULATED

**Progress:** PAUSED by the owner (laptop overheating). Last completed = install + cold start + AC-01 diagnosis on `codex_apk_review` (emulator now shut down). Next = run AC-M05 → M06 → M07 → M08 on `emulator-5554` (modern WebView) once the lead says D-B is finished. `codex_apk_review` (WebView 83) cannot run the app — see AC-01.

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

## Defects

| ID | Severity | Steps | Expected | Actual | Evidence |
|---|---|---|---|---|---|
| AC-01 | S1 blocker (on WebView < ~85; APK declares minSdk 24) | Fresh install of the v6 APK on an Android 11 AVD whose WebView is Chrome 83 → open app | App reaches sign-in | Hangs on “Loading demo data…”. Console: `RangeError: maximumFractionDigits value is out of range` from `money()` in `03 Prototype\src\domain\money.ts:2` — `new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR',maximumFractionDigits:0})`; older V8 (8.3) rejects max < the currency default minimum (2). Reproduced directly in the page: same call throws, adding `minimumFractionDigits:0` returns `₹10,750`. Suggested fix (lead): pass `minimumFractionDigits` explicitly (0 or 2) alongside `maximumFractionDigits`; same pattern in `channels.ts:38`, `yield.ts:61`, `BookingSite.tsx:10` is harmless (no currency style) but worth aligning. Also consider a minimum WebView version check/message. **Second blocker behind it:** `structuredClone` (Chrome 98+) is used by `execute()` (`services\mock\commands.ts:18`), `fixtures\ext\pos.ts`, `fixtures\history\generate.ts`, `handlers\assistant.ts` → on WebView < 98 the app cannot load even with the Intl fix. Decision for the lead/owner: either raise the supported floor (WebView ≥ 98, i.e. updated Android System WebView; document it, maybe raise minSdk) or replace `structuredClone` with a JSON/own deep-copy. The owner's Tab S7 (Android 13, updatable WebView) is not affected. | `defects\AC-01-webview83-stuck-loading.png` |
