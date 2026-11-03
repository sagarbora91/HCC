# APK v20 emulator report (3 Nov 2026, versionCode 21, versionName 20)

**Scope actually run: phone smoke of the demo APK only.** The full `APK_v19_Checklist.md`, the tablet AVD run, and every server-APK item were NOT executed.

## Why the server APK was not exercised
The production pages (Administration, Master configuration > Internet access, Year end, Bank reconciliation, Report library, hall booking, System health and the rest) need the hotel server over HTTPS. `Hosting/HccHosting.cs` loads the server certificate from `LocalMachine\My`, which needs an elevated shell; this session is not elevated. The throwaway test CA used for the build had its private key discarded, so it cannot sign a server certificate either. Run the server-APK steps from an elevated PowerShell (README "Dev / emulator flow") or on the hotel PC.

## Builds
| File (Builds\2026-11-03) | Package | versionCode / name | SHA-256 |
|---|---|---|---|
| HCC-20-vc21-demo-debug.apk | in.hcc.prototype | 21 / 20 | b8a1d6e7e195ef049c8b151756384c4b91e9b02bac9e3aca357b4ea915d7d13d |
| HCC-20-vc21-server-debug.apk | in.hcc.prototype.server | 21 / 20-server | dbef555a58330cf00007e086b34fa157cf9c400f954e6a599fd83164b7d25a1b |

Server APK built with `node scripts\build-production.mjs --ca <test CA> --assemble`: **V19-4 confirmed fixed** (it found Gradle and produced the APK). No release APK (no keystore on this PC).

## V19 defects
| ID | Result |
|---|---|
| V19-4 `--assemble` build | FIXED (above) |
| V19-2 phone date line | Looks fixed: Room board app bar shows "24 Oct · Open" (screenshot `v20\phone-room-board.png`) |
| V19-3 phone Room keys filters | Looks fixed: filters in a two-column grid, no overflow (`v20\phone-room-keys.png`) |
| V19-1 stat strips (tablet Dashboard, Accounting) | NOT CHECKED (tablet not run) |

## Run
Phone AVD `codex_api35_google`: both APKs installed (`dumpsys`: versionCode 21, versionName 20), demo opened, Room board, Menu, Room keys. Logcat: 0 `FATAL EXCEPTION`.

## New defects
None found (D20-n unused). Nothing else was tested.
