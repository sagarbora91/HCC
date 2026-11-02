# APK v19 checklist (versionCode 19, versionName 0.1.0-proto)

Status: **prepared in cloud, not executed.** No Android or Gradle build was run. All results columns are empty until the hotel PC run (wave F2).

## Build prerequisites (hotel PC)
- JDK 21: `C:\Program Files\Microsoft\jdk-21.0.12.101-hotspot` (Java on PATH is 1.8, Android Studio JBR is 25; both rejected). Set `JAVA_HOME` and `Path` in every PowerShell session (`04 Android\README.md`).
- Android SDK `E:\Android\Sdk`; AVDs in `E:\Androidvd`: `hcc_tablet_api35` (tablet) and `codex_api35_google` (phone). They exist only on the hotel PC. One emulator at a time.
- Node 25 (`E:\Tools\nodejs` first on PATH); `npm run build` in `03 Prototype`, then the Capacitor sync per the README.
- Builds: demo `assembleDebug` (`in.hcc.prototype`), server `assembleProductionDebug` with a throwaway test CA (`in.hcc.prototype.server`). `productionRelease` needs the release keystore (not on this PC).
- Confirm with `dumpsys package` that versionCode is 19 on both AVDs.

## A. Carried forward from the v17 emulator report
| # | Item | Pass | Fail | Notes |
|---|---|---|---|---|
| A1 | V16-1 slip-photo camera opens (phone demo, tablet server); Pictures folder cleaned; 0 fallback lines in logcat | | | |
| A2 | V16-2 bar colours after night-mode flips: Dark, Light, High contrast, Follow device (phone + tablet + server APK) | | | |
| A3 | V15-1 Dark survives rotation and recents | | | |
| A4 | V15-2 inline master form: Back hides keyboard, then "Discard changes?"; Keep editing / Discard; clean form leaves | | | |
| A5 | V15-3 two-line titles: Appearance & display, Master configuration, Approvals & audit; Room board title | | | |
| A6 | V14-4 Extra large text: body 18.2 px, no horizontal scroll | | | |
| A7 | FLAG_SECURE on Show ID (screencap black, recents black, normal after Hide ID) | | | |
| A8 | R1 first run and sign-in as Cashier, Owner, front desk | | | |
| A9 | R2-R3 add rooms 401-403, retire 403 with in-app confirm | | | |
| A10 | R4-R5 new booking, guided check-in, Room board count | | | |
| A11 | R6 folio preview and print dialog (A4, 1 page) | | | |
| A12 | R7 OS font 1.3 with Honour ON / OFF | | | |
| A13 | S1-S2 server address, Test connection, owner sign-in, device registration | | | |
| A14 | S3-S4 server rooms load, add floor (check `mst.floor`) | | | |
| A15 | S6 staff PIN sign-in with must-change PIN | | | |
| A16 | S7 bank deposit with camera on server APK | | | |
| A17 | S8 reservation and guided check-in on server APK | | | |

## B. FIT items (new in v19)
| # | Item | Pass | Fail | Notes |
|---|---|---|---|---|
| B1 | No page scroll at tablet landscape on every FIT page (a list scrolls in its panel, a task never scrolls) | | | |
| B2 | h1 is 22 px on tablet pages | | | |
| B3 | Tabs render and switch on FIT pages (tablet and phone) | | | |
| B4 | Modal footer sits above the window insets; not clipped on the tablet AVD (V17-1) | | | |
| B5 | Room keys page (`room-keys`) lays out at tablet landscape and phone; mock encoder only | | | |
| B6 | Open items from F1-int-DONE: Reservations about 5.8 rows at tab-land; compact stat strips on Dashboard / Accounting cut the last card edge; Approvals list rows (seeded render only) | | | |
| B7 | Rotation portrait / landscape keeps layout and bar colours on tablet and phone | | | |

## C. Open defects to recheck (from v17 report)
| # | Sev | Item | Pass | Fail | Notes |
|---|---|---|---|---|---|
| V17-1 | S3 | Window insets lost until a rotation; innerHeight flips 842/915 (phone), 744/800 (tablet); footers, toasts, dialogs clipped. Confirm on the physical Galaxy Tab first | | | |
| V17-2 | S4 | `resize` events fire continuously at idle (about 20 a second); status-bar restyle debounce repaints often | | | |
| V17-3 | S4 | Transient wrong bar colours for 1-2 s after a night flip or rotation | | | |
| V17-4 | S4 | Phone Room board app-bar date line loses the status word ("Business date 24 Oct ·") | | | |
| V15-4 | S4 | Cosmetic, from the v15 report; not retested in v17 | | | |
| V16-3 | S4 | Cosmetic, from the v16 report; not retested in v17 | | | |

## D. Sign-off
Tester, date, device/AVD, APK SHA-256 for demo and server builds: ______
Physical Galaxy Tab S7 run uses `APK_v17_Tablet_Test_Prompt.md` as the base (to be refreshed in F2); results to `Acceptance/device-v19/`.
