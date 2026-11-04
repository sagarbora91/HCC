# APK v19.5 emulator report (3 Nov 2026, versionCode 20)

**Scope actually run: smoke only.** The full `APK_v19_Checklist.md` was NOT executed. Server-APK items (A13-A17, every production page: Reports hub, Year end, Bank reconciliation, Internet access, System health Egress, admin pages, room keys, counter bills, desk reminders, hall booking, tariff card) need the hotel server running over HTTPS with a certificate from the bundled CA and were not attempted.

## Builds
| File | Package | versionCode / name | SHA-256 |
|---|---|---|---|
| `Builds\2026-11-03\HCC-19.5-vc20-demo-debug.apk` | in.hcc.prototype | 20 / 19.5 | e22a13d69f5b8a524a9c22af1501ffeb857f9d9a559c4555dd6b836e77608e36 |
| `Builds\2026-11-03\HCC-19.5-vc20-server-debug.apk` | in.hcc.prototype.server | 20 / 19.5-server | 52b24a3f0360a16b30abf73a77f52bd5daa6b28f5352a57a9fd79bcf5a829b95 |

Server APK is built with a throwaway test CA (`HCC-Test-CA-throwaway.cer`, public cert only, private key discarded): emulator use only. No `assembleRelease` / `productionRelease`: no release keystore on this PC.

## What was run
- Phone AVD `codex_api35_google` (1080x2400): both APKs installed (`dumpsys` versionCode 20, versionName 19.5), demo launched, Modules home, Reports, Rooms, Menu drawer (Room keys and Reminders & wake-ups are listed under Front Desk). Logcat: 0 `FATAL EXCEPTION`; only Chromium warnings (variations seed, tile memory).
- Tablet AVD `hcc_tablet_api35` (2560x1600, landscape): both APKs installed, demo launched, Modules home fits with no page scroll. Logcat: 0 fatal.
- Screenshots are in the worktree `.scratch\shots\` (not committed).

## Defects
None found in this smoke run. D195-n numbering starts at D195-1 when the server APK run is done.

## Not run
Checklist sections A (A13-A17 server items; rest not stepped through), B (B1-B7 on the FIT pages), C (V17-1..4, V15-4, V16-3 recheck), sign-off.
