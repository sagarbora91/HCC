# APK v19 emulator report (demo APK on phone and tablet AVDs; server APK built and smoke-tested only)

Date 2 Nov 2026 · tester: Claude (Sonnet 5.5 agent) · EMULATED, not physical devices · branch `claude/apk19` (from main a806cea8, worktree `E:\Projects\HCC\worktrees\gate-main`) · screenshots in `v19\phone\` (`p-*`) and `v19\tablet\` (`t-*`) · per-row results in `APK_v19_Checklist_Results.md` (same folder). No product code was changed; only these evidence files are committed.

## 1. Builds

All heavy commands ran through `tools\Invoke-Serialized.ps1` (label `apk19`, never busy). JDK 21.0.12.1, node 25.9.0, Gradle `--offline` (cache `E:\Cache\gradle`; no Norton workaround was needed).

| Step | Command (dir) | Result |
|---|---|---|
| Web demo | `npm run build` (03 Prototype) | OK, `dist/index.html` 3,817 KB |
| Web production | `npm run build:production` | OK, `dist/production/index.html` 4,109 KB (CSP meta) |
| Android deps | `npm ci` (04 Android) | OK |
| Web copy + sync | `node scripts\copy-web.mjs`, `npx cap sync android` | OK |
| Demo APK | `gradlew.bat --offline assembleDebug` | BUILD SUCCESSFUL, 1 m 3 s |
| Throwaway CA | `openssl req -x509 ... CN=HCC APK19 Throwaway Test CA` (30 days; key and certs kept in `Builds\2026-11-02\_testca_throwaway`, deleted afterwards; nothing in the repo or any certificate store) | OK, CA SHA-256 1C:18:3A:D0:...:CD:42:6F:6B |
| Server web + CA | `node scripts\build-production.mjs --ca <test CA .cer> --assemble` | CA accepted and web page copied, then **failed**: `'gradlew.bat' is not recognized` (see V19-4) |
| Server APK | `gradlew.bat --offline assembleProductionDebug` (04 Android\android) | BUILD SUCCESSFUL, 8 s |
| productionRelease | not built (no release keystore on this PC) | n/a |

New-HccCertificate.ps1 was not used: it needs an elevated shell and password prompts, and the README itself allows any CA for a compile-only check. `04 Android\node_modules` is a normal `npm ci` install (no junction); `git status` shows no tracked file changed by the builds.

## 2. APKs and SHA-256

Both are in `E:\Projects\HCC\Builds\2026-11-02\` (not in git; `*.apk` is ignored). `SHA256.txt` is in the same folder. `aapt` and `dumpsys package` both show versionCode 19 on both AVDs.

| File | Package | versionName | Size | SHA-256 |
|---|---|---|---|---|
| `HCC-0.1.0-proto-v19-debug.apk` | in.hcc.prototype | 0.1.0-proto | 6,521,724 | `02F92175C041567B5BC1F8869C5A77F8E1C7C7CC8C625240D58397AC91F5A85E` |
| `HCC-Server-v19-productionDebug.apk` | in.hcc.prototype.server | 0.1.0-proto-server | 6,628,819 | `C0820D6A9EB84ED9858B96DDAD0FF1CCB8FEB53C4247624C1B60722340F08366` |

## 3. Method

One emulator at a time: `codex_api35_google` (phone 1080x2400, 420 dpi, 412x842 css px) first, shut down with `adb emu kill` and confirmed gone, then `hcc_tablet_api35` (2560x1600, 320 dpi, 1280x744 css px landscape; natural orientation is landscape). Both started with `-no-snapshot`, `pm clear` before each run, fictional demo data only. The apps are debuggable, so the web view was read through Chrome DevTools (`adb forward` + a small script) for computed styles, scroll sizes, resize events and `innerHeight`; every tap, key, rotation (`user_rotation`), night flip (`cmd uimode night`), font scale (`font_scale`) and screenshot was a real adb action. Status-bar and gesture-strip colours were read from the PNG pixels. Settings were restored on both AVDs (rotation auto, font 1.0, night no).

## 4. Results (summary; per-row notes and evidence in the checklist results file)

| Row | Phone | Tablet | Server APK |
|---|---|---|---|
| A1 camera | Pass | n/a (server) | Not tested |
| A2 bar colours after flips | Pass | Pass | Not tested |
| A3 Dark rotation / recents | Pass | Pass | Not tested |
| A4 discard prompt | Pass | Pass | - |
| A5 titles | Pass | Pass (single line) | - |
| A6 XL text | Pass | Pass | - |
| A7 FLAG_SECURE Show ID | - | - | Not tested |
| A8 sign-in roles | Pass (Owner, Reception, Cashier) | Owner only | - |
| A9 rooms 401-403, retire | Pass | Not re-run | - |
| A10 booking, check-in, board | Pass | Not re-run | - |
| A11 folio preview, print A4 1 page | Pass | Not re-run | - |
| A12 OS font 1.3 Honour ON/OFF | Pass | Pass | - |
| A13-A17 server rows | - | - | Not tested (smoke only) |
| B1 no page scroll (37 pages + tabs) | - | Pass | - |
| B2 h1 22 px | - | Pass | - |
| B3 tabs | Pass (Room keys, Approvals) | Pass | - |
| B4 modal footer / insets | Pass | Pass | Not tested |
| B5 Room keys | Pass | Pass | - |
| B6 open items | - | Partial: Reservations 8.2 rows OK; stat strips still clip (V19-1) | - |
| B7 rotation keeps layout and colours | Pass | Pass | - |
| V17-1 insets | not reproduced | not reproduced | Not tested |
| V17-2 resize events | 0 in 10 s | 0 in 10 s | - |
| V17-3 transient bar colours | not reproduced | not reproduced | - |
| V17-4 date line status word | Partial (word present, date truncated) | - | - |
| V15-4, V16-3 | Not tested (definitions are not in the checklist) | | |

Key measurements: theme colours exact in all four themes before and after night flips and four rotations (tablet) with captures 0.8 s and 3.8 s after the change; innerHeight never changed (842 phone; 744 / 1224 tablet); `scrollHeight - innerHeight` = 0 on 37 pages and every tab of Accounting and Approvals; h1 = 22 px; XL text 18.2 px; slip photo 960x1280, data URI 24,611 chars, `Pictures` folder removed, 0 fallback lines in logcat; no `FATAL` or uncaught web errors in logcat on either AVD.

Server APK smoke (both AVDs): first run shows "Connect this device to the hotel server" with default `https://hcc-server.local:8443`; `http://10.0.2.2:5211` is refused with "Use an https:// address. This app never talks to the hotel server over plain http."; `https://10.0.2.2:5443` with no server gives "Cannot reach ... (Failed to connect to /10.0.2.2:5443)". Evidence `p-32`, `p-33`, `t-21`.

## 5. Not tested and why

- **All server-dependent rows (A7, A13-A17, A1 on the tablet server, A2/B4/V17-1 on the server APK).** The README "Dev / emulator flow" needs an elevated shell for `New-HccCertificate.ps1`, a SQL Express database migrated to the current schema (about 100 migrations), the API built and started with HTTPS endpoint variables and a guest-ID key, the property set up, credentials and a device registered. The earlier setup scripts (W4_E2E2) are not in this checkout, and the run would have been long and was not practical here. The server APK itself was built, installed, started and exercised only as far as the address screen. V17-1 (insets) was last seen on the server APK, so it stays open for that build and for the physical Galaxy Tab.
- **A9-A11 on the tablet** (rooms, booking, folio print): done on the phone only; the tablet got Owner sign-in, A4, A5, A6, A12 and B1-B7.
- **Restaurant POS page** (not reached by the search text used) in the B1 sweep; phone FIT pages other than Room keys and Approvals were not tab-tested.
- **V15-4 and V16-3**: only named in the checklist, with no description to test against.
- Physical Galaxy Tab S7, `productionRelease`, printing to a real printer, installed-APK upgrade over v17: not in scope here.

## 6. Defects

| ID | Sev | Finding | Repro | Evidence |
|---|---|---|---|---|
| V19-1 | S4 | The compact stat strip still cuts its last card: on the tablet Dashboard (In house tab) the 4th card "Payment receipts" runs under the expand chevron; on Accounting the 4th card "Exceptions 0 / Nothing blocks l..." is cut off. (B6 item not fixed.) | Tablet landscape, Owner, Dashboard or Accounting | `t-05-dashboard.png`, `t-03-accounting-last-tab.png` |
| V19-2 | S4 | Phone app-bar date line shows "Business date ..." (the date is truncated away) next to the status word "Open". V17-4's missing status word is fixed, the date is not readable. | Phone, any page with the sub-line (Room board, Cashier shift, Room keys) | `p-08-room-board-title.png`, `p-27-room-keys.png` |
| V19-3 | S4 | Phone Room keys > Key log filters: From / To selects render empty, Room / Booking / User selects are small (well under the 48 px target rule). | Phone, Owner, Room keys | `p-27-room-keys.png` |
| V19-4 | S4 (tooling) | `node scripts\build-production.mjs --ca <cer> --assemble` run from `04 Android` fails with "'gradlew.bat' is not recognized" although the README gives exactly that command. It must run Gradle in `android\`; workaround was the separate `gradlew.bat assembleProductionDebug`. | `04 Android`, run the command above | build log |

Observations that are not defects: after "Keep editing" in the discard dialog the keyboard returns, so one more Back is needed before the dialog shows again (same as v17). The demo data has only 3 floors, so rooms 401-403 went on Floor 3. The master-config rooms list drops into the form when the tester taps near a row ("Save changes" sits where the dialog button was after a dismissed dialog); one accidental rename (4039) was reverted.

## 7. Recommended next step

1. Treat the demo APK v19 as passing the emulator checklist for everything that does not need the server: send it to the physical Galaxy Tab S7 run (`APK_v19_Tablet_Test_Prompt.md`), which should also confirm V17-1, V17-2, V17-3 on hardware.
2. Run the server rows (A7, A13-A17, B4 on the server APK) in one session on the hotel PC where the certificate, SQL Express and setup scripts already exist, with this server APK (or a rebuild with the real hotel CA).
3. Fix V19-1 to V19-3 in a small FIT follow-up (stat-strip overflow, date line ellipsis, Room keys filter controls) and V19-4 in `build-production.mjs` (spawn Gradle in `android\`); none blocks a v19 hand-out to testers.

## 8. Clean-up

Emulators were shut down (`adb emu kill`; no `emulator` or `qemu` process remained) after each run; AVD settings restored; the throwaway CA, server certificate and keys were deleted; nothing was installed into a Windows certificate store; nothing committed to main, nothing pushed, no other worktree touched.
