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

## Tablet AVD pass (hcc_tablet_api35, 2560x1600 landscape, v20 demo APK 21/20)

Done 4 Nov 2026. Screens: Modules home, Dashboard (Arrivals, In house), Accounts & Reports, Accounting. Screenshots in `Acceptance\v20\tablet-*.png`.

| Check | Result |
|---|---|
| V19-1 stat strips, Dashboard In-house tab | PASS. Same four tiles on every tab; strip has a collapse toggle; no overlap or clipping. |
| V19-1 stat strips, Accounting | PASS. Four tiles, collapse toggle, tabs and voucher table below. |
| FIT: lists scroll inside their panel, task never scrolls | PASS on Dashboard (guest list, room map, attention rail) and Accounting (voucher table). |
| Accounts & Reports page lists Report library, Revenue by category, Owner digest | Present (not opened; server-only data). |

### Defects
- **D20-1 (S4, cosmetic) - FIXED on branch `claude/apk20` (not in the v20 APKs; ships in the next build):** on the tablet the stat strip wraps 3+1, so the fourth tile (Payment receipts on Dashboard, Exceptions on Accounting) sits alone on a second row with empty space to its right. Readable, not blocking.

Not covered on the tablet: Housekeeper no-prices, Administration and the other server pages (server APK needs an elevated shell, see above).
