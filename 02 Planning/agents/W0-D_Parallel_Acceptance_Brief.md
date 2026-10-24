# W0-D (parallel) · Acceptance split into four agents — shared brief

Owner instruction (24 Oct 2026): finish W0-D with maximum agents. Replaces the single-agent run in `W0-D_Acceptance_Brief.md` (read that first for scope and rules). Everyone works **in the main checkout** `V:\Codex\Hotel Control Centre` on `main` — **do not commit, do not create branches/worktrees, never push**; the lead commits all evidence. Do **not** change product code: record defects.

## Fixed facts
- APK under test: `05 Delivery Evidence\APK\HCC-0.1.0-proto-debug.apk` — versionCode **6**, SHA-256 `49c184d32d5e7eb099e474b185b9b9926e8bb38f8eb1ff940ccf86eddf75946b`; its web bundle is byte-identical to `03 Prototype\dist\index.html` (SHA-256 `4e33f7d6…c1dd`). Do not rebuild it.
- Tools: `adb`/`emulator` in `C:\Users\Sagar\AppData\Local\Android\Sdk\platform-tools` and `\emulator` (add to PATH). Always pass `-s <serial>`.
- Expected S0 figures (demo guide `03 Prototype\src\app\demo\DemoModals.tsx`): drawer ₹10,750 → ₹11,250 (after ₹500 deposit) → count ₹11,250 on 24 and 25 Oct → ₹12,750 after settlement → ₹12,450 after the ₹300 expense; 105 invoice ₹4,400. Open restaurant checks POS-F03 (table T2) and POS-F04 (room 201) must be settled before closing 24 Oct.
- Known defect already logged: **D-01** settle dialog says outlet cash is "separate from the front-desk cashier drawer" (contradicts DEC-052). Screenshot: `05 Delivery Evidence\Acceptance\defects\D-01-settle-dialog-separate-drawer-text.jpg`. Don't re-log it.
- Efficient driving: the debug APK's WebView is debuggable. `adb -s <serial> shell cat /proc/net/unix | findstr webview_devtools_remote` → `adb -s <serial> forward tcp:<yourPort> localabstract:webview_devtools_remote_<pid>` → `http://127.0.0.1:<yourPort>/json` → drive the DOM over the DevTools WebSocket with a small Node script (Node 25 has a global `WebSocket`; no npm packages). Use real taps (`adb shell input tap`) for at least the key actions per flow, and `adb exec-out screencap -p` for evidence. Keep scripts in your scratchpad, not the repo.
- Time-box: if a check is blocked for more than ~10 minutes, record it as **Not run** with the reason and move on. Never stall silently.

## Agents (each writes ONLY its own files)

| Agent | Device / port | Scope | Writes |
|---|---|---|---|
| **D-A Walkthrough** | Browser pane (own session) via `.claude/launch.json` `hcc-prototype` | BL-023: all 15 S0 guide steps 24→26 Oct at 1366 px, scenario S0 (Demo controls → scenario), real figures vs expected for every step; the earlier run reached step 4a (steps 1–3b passed, drawer ₹11,250). Drive via the UI; DOM events allowed if the pane does not paint, say so. | `05 Delivery Evidence\Acceptance\BL023_Walkthrough.md`, `…\Acceptance\bl023\*` (replace earlier shots if re-run), defects `…\Acceptance\defects\BL-*` |
| **D-B Emulator roles** | AVD `codex_api35_google`, `emulator -avd codex_api35_google -port 5554` → serial `emulator-5554`, DevTools port 9231 | EMULATED. Install APK; 10 demo users: tabs/drawer show only permitted pages, housekeeping/maintenance see no money; Android back (drawer, sheet, page, exit prompt); Reset; CSV to `Documents/HCC` + share sheet; print service; restaurant settlement on the phone POS screen; cold-start time | `…\Acceptance\D5_Roles.md`, `…\Acceptance\d5-roles\*`, defects `…\defects\R-*` |
| **D-C Emulator AC flows** | AVD `codex_apk_review`, `emulator -avd codex_apk_review -port 5556` → `emulator-5556`, DevTools port 9232 | EMULATED. AC-M05 reception booking → check-in on phone (plus a restricted 7→8 Nov stay showing the warning; manager override), AC-M06 cashier ₹3,400 split (₹2,000 cash + ₹1,400 UPI), deposit return, shift close = S0 oracle, AC-M07 housekeeper cleans Room 105 (48 px targets, no money), AC-M08 manager closes the day (open restaurant checks block first; report totals equal desktop) | `…\Acceptance\D5_AC.md`, `…\Acceptance\d5-ac\*`, defects `…\defects\AC-*` |
| **D-D Tab S7** | Physical SM-T875, serial `R52N807PTTE`, DevTools port 9233 | PHYSICAL. `adb install -r` (keep data), cold start, desktop layout in Auto, sign-in → Modules Home → drawer, Android back closes drawer/sheet, cashier shift ₹10,750 with the restaurant line (after Reset to BASE/S0), booking with restriction + Manager override, CSV + share sheet, print service, Reset, rotation, compare once with Display → Mobile, touch-comfort notes (incl. the portrait Arrivals sideways scroll noted in Session 8). Don't change tablet settings or install anything else. | `…\Acceptance\D6_TabS7.md`, `05 Delivery Evidence\APK\Tab-S7\w0d-*`, defects `…\defects\T-*` |

Never touch another agent's device, emulator or port. Shut your emulator down when finished (`adb -s <serial> emu kill`).

## Report file format
Header (date, device/environment, EMULATED/PHYSICAL/BROWSER, APK SHA-256), then a table `Check | Result (Pass/Fail/Not run) | Evidence | Notes`, then defects `ID | Severity (S1 blocker … S4 cosmetic) | Steps | Expected | Actual | Evidence`.

## Final answer (≤ 20 lines)
Pass/Fail/Not run counts, defects (ID + one line), anything blocked and why.
