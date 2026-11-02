# APK v19 checklist (versionCode 19, versionName 0.1.0-proto)

Status: **executed 2 Nov 2026 on the work PC (emulators only).** Results are in the Pass column as Pass / Partial / Fail / Not tested with a note and the evidence file (folders `Acceptance/v19/phone`, `v19/tablet`); the Fail column is left empty. Detail: `APK_v19_Emulator_Report.md`.

## Build prerequisites (hotel PC)
- JDK 21: `C:\Program Files\Microsoft\jdk-21.0.12.101-hotspot` (Java on PATH is 1.8, Android Studio JBR is 25; both rejected). Set `JAVA_HOME` and `Path` in every PowerShell session (`04 Android\README.md`).
- Android SDK `E:\Android\Sdk`; AVDs in `E:\Androidvd`: `hcc_tablet_api35` (tablet) and `codex_api35_google` (phone). They exist only on the hotel PC. One emulator at a time.
- Node 25 (`E:\Tools\nodejs` first on PATH); `npm run build` in `03 Prototype`, then the Capacitor sync per the README.
- Builds: demo `assembleDebug` (`in.hcc.prototype`), server `assembleProductionDebug` with a throwaway test CA (`in.hcc.prototype.server`). `productionRelease` needs the release keystore (not on this PC).
- Confirm with `dumpsys package` that versionCode is 19 on both AVDs.

## A. Carried forward from the v17 emulator report
| # | Item | Pass | Fail | Notes |
|---|---|---|---|---|
| A1 | V16-1 slip-photo camera opens (phone demo, tablet server); Pictures folder cleaned; 0 fallback lines in logcat | Pass (phone) / Not tested (tablet server) |  | Phone demo: camera app opened (com.android.camera2 in focus), 0-byte JPEG in Android/data/in.hcc.prototype/files/Pictures during capture, folder gone afterwards, form shows 960x1280 image (data URI 24,611 chars), 0 fallback lines in logcat. Tablet server APK: no hotel server in this run. `v19/phone/p-24-camera.png`, `p-25-camera-review.png`, `p-26-photo-in-form.png` |
| A2 | V16-2 bar colours after night-mode flips: Dark, Light, High contrast, Follow device (phone + tablet + server APK) | Pass (phone + tablet demo) / Not tested (server APK) |  | Dark #ae4155 / #201916, Light #7a1f2b / #ffffff, High contrast #5e1620 / #ffffff, Follow device (light and dark with night no/yes): status bar and bottom strip exact after every flip, sampled 0.8 s and 3.8 s after. `p-03-dark-night-flips.png`, `t-07-dark-night-flips.png`. Server APK needs sign-in, see A13 |
| A3 | V15-1 Dark survives rotation and recents | Pass |  | Dark survives rotation (phone portrait/landscape; tablet all four orientations) and recents (app returns Dark, colours intact). `p-04-dark-portrait.png`, `p-05-dark-recents.png`, `t-06-dark-portrait.png`, `t-08-dark-recents.png` |
| A4 | V15-2 inline master form: Back hides keyboard, then "Discard changes?"; Keep editing / Discard; clean form leaves | Pass (phone + tablet) |  | Back hides the keyboard first (no dialog), next Back shows "Discard changes?"; Keep editing keeps the text; Discard leaves and the field is unchanged; Close on a clean form leaves with no dialog (phone). After Keep editing the keyboard returns, so one more Back is needed to reach the dialog again. `p-12-discard-dialog.png`, `t-14-discard-dialog.png`, `t-15-after-discard.png` |
| A5 | V15-3 two-line titles: Appearance & display, Master configuration, Approvals & audit; Room board title | Pass |  | Phone: two-line titles "Appearance & display", "Master configuration", "Approvals & audit"; Room board title keeps the status word "Open" (date part truncated, see V19-2). Tablet: single-line titles (28 px high h1). `p-02-appearance-title.png`, `p-06-master-title.png`, `p-07-approvals-title.png`, `p-08-room-board-title.png`, `t-11/12/13` |
| A6 | V14-4 Extra large text: body 18.2 px, no horizontal scroll | Pass (phone + tablet) |  | Extra large: body font 18.2 px, scrollWidth = clientWidth (no horizontal scroll). `p-09-xl-text.png`, `t-10-xl-text.png` |
| A7 | FLAG_SECURE on Show ID (screencap black, recents black, normal after Hide ID) | Not tested |  | FLAG_SECURE Show ID is a production-server dialog; no hotel server was run (report section 5) |
| A8 | R1 first run and sign-in as Cashier, Owner, front desk | Pass (phone) / partial (tablet) |  | Phone: first-run list, signed in as Owner (Sagar), Reception (Aarav) and Cashier (Priya). Tablet: Owner only. `p-01-start.png`, `p-23-signin-cashier.png`, `t-01-start-landscape.png`, `t-02-signed-in.png` |
| A9 | R2-R3 add rooms 401-403, retire 403 with in-app confirm | Pass (phone) / Not re-run (tablet) |  | Rooms 401-403 added via Master configuration (Floor 3, Classic; the demo has no floor 4), 403 retired with the in-app "Retire Room 403?" confirm; Active 26 / Retired 1. A tester slip saved a room as "4039"; renumbered back to 403. `p-10-rooms-added.png`, `p-14-retire-confirm.png`, `p-15-retired-403.png` |
| A10 | R4-R5 new booking, guided check-in, Room board count | Pass (phone) / Not re-run (tablet) |  | New booking wizard 4 steps, confirmed HCC-1111 room 105; guided check-in 6 steps; Room board Occupied 8 -> 9, All 24 -> 26. `p-16-booking-confirmed.png`, `p-17-checked-in.png`, `p-18-room-board-count.png` |
| A11 | R6 folio preview and print dialog (A4, 1 page) | Pass (phone) / Not re-run (tablet) |  | Folio preview then Print / share: Android print dialog, Paper size ISO A4, page 1/1. `p-19-folio-preview.png`, `p-20-print-dialog.png` |
| A12 | R7 OS font 1.3 with Honour ON / OFF | Pass (phone + tablet) |  | Android font 1.3: Honour ON body 18.2 px, Honour OFF 14 px, no horizontal scroll; restored to 1.0 / ON. `p-21-font13-honour-on.png`, `p-22-font13-honour-off.png`, `t-16-font13-honour-on.png` |
| A13 | S1-S2 server address, Test connection, owner sign-in, device registration | Not tested |  | No hotel server. Smoke only: server APK first run shows the address screen; http:// refused ("Use an https:// address..."); https://10.0.2.2:5443 gives "Cannot reach..." (nothing listening). `p-32-server-first-run.png`, `p-33-server-https-only-test.png`, `t-21-server-https-test.png` |
| A14 | S3-S4 server rooms load, add floor (check `mst.floor`) | Not tested |  | No hotel server |
| A15 | S6 staff PIN sign-in with must-change PIN | Not tested |  | No hotel server |
| A16 | S7 bank deposit with camera on server APK | Not tested |  | No hotel server |
| A17 | S8 reservation and guided check-in on server APK | Not tested |  | No hotel server |

## B. FIT items (new in v19)
| # | Item | Pass | Fail | Notes |
|---|---|---|---|---|
| B1 | No page scroll at tablet landscape on every FIT page (a list scrolls in its panel, a task never scrolls) | Pass |  | On 37 pages at tablet landscape (1280x744 css) the document never scrolls (scrollHeight - innerHeight = 0, no horizontal overflow); lists scroll in their panels. Also 0 on every tab of Accounting (11) and Approvals. Restaurant POS page was not reached by search. `t-02`, `t-04`, `t-05` |
| B2 | h1 is 22 px on tablet pages | Pass |  | Computed h1 font-size 22 px on every page measured. `t-04-reservations.png` |
| B3 | Tabs render and switch on FIT pages (tablet and phone) | Pass (tablet) / Pass (phone, Room keys + Approvals) |  | Tabs render and switch: Room keys 3, Accounting 11, Approvals 9, Maintenance 6, Staff 8, Linen 7. Other phone FIT pages not individually tab-tested. `p-31-room-keys-lock-mapping.png`, `t-03-accounting-last-tab.png` |
| B4 | Modal footer sits above the window insets; not clipped on the tablet AVD (V17-1) | Pass (demo APK) / Not tested (server APK) |  | Tablet New reservation modal: dialog bottom 728 < innerHeight 744, footer buttons fully visible above the bar. innerHeight stayed 744 (tablet) / 842 (phone) through rotations, recents and night flips. The V17-1 clip was seen on the server APK, which could not be run. `t-18-modal-footer.png` |
| B5 | Room keys page (`room-keys`) lays out at tablet landscape and phone; mock encoder only | Pass |  | Room keys lays out on phone and tablet landscape; tabs Key log / Lock mapping / Encoder switch; mock encoder panel "Encoder offline". `p-27-room-keys.png`, `t-19-room-keys.png`, `t-20-room-keys-encoder.png` |
| B6 | Open items from F1-int-DONE: Reservations about 5.8 rows at tab-land; compact stat strips on Dashboard / Accounting cut the last card edge; Approvals list rows (seeded render only) | Partial (strips Fail) |  | Reservations: 8.2 rows visible (466 px panel / 57 px rows), better than 5.8. Compact stat strips on Dashboard and Accounting still cut the last card (V19-1). Approvals list: phone seeded render shows the one expense card correctly (`p-07`); tablet list not inspected row by row. `t-04`, `t-05-dashboard.png`, `t-03-accounting-last-tab.png` |
| B7 | Rotation portrait / landscape keeps layout and bar colours on tablet and phone | Pass |  | Rotation portrait/landscape keeps layout and bar colours (tablet 4 orientations x 4 themes; phone portrait/landscape Dark and Light). `p-04`, `t-06`, `t-09-*` |

## C. Open defects to recheck (from v17 report)
| # | Sev | Item | Pass | Fail | Notes |
|---|---|---|---|---|---|
| V17-1 | S3 | Window insets lost until a rotation; innerHeight flips 842/915 (phone), 744/800 (tablet); footers, toasts, dialogs clipped. Confirm on the physical Galaxy Tab first | Pass (not reproduced) |  | innerHeight constant (842 phone; 744 / 1224 tablet landscape / portrait) across rotation, recents, night flips and keyboard; no insets loss seen. Physical Galaxy Tab and server APK still to confirm |
| V17-2 | S4 | `resize` events fire continuously at idle (about 20 a second); status-bar restyle debounce repaints often | Pass (not reproduced) |  | 0 resize events in 10 s idle on phone and on tablet |
| V17-3 | S4 | Transient wrong bar colours for 1-2 s after a night flip or rotation | Pass (not reproduced) |  | Bars correct 0.8 s after a night flip on both AVDs; only mid-rotation capture frames showed transitional colours |
| V17-4 | S4 | Phone Room board app-bar date line loses the status word ("Business date 24 Oct ·") | Partial |  | Status word "Open" now shown on phone Room board and other pages; the date part truncates to "Business date ..." (V19-2). `p-08`, `p-27` |
| V15-4 | S4 | Cosmetic, from the v15 report; not retested in v17 | Not tested |  | Defect text is only in the v15 report, not in the checklist; not retested |
| V16-3 | S4 | Cosmetic, from the v16 report; not retested in v17 | Not tested |  | Defect text is only in the v16 report, not in the checklist; not retested |

## D. Sign-off
Tester: Claude (Sonnet 5.5 agent), 2 Nov 2026, AVDs codex_api35_google (phone 1080x2400, 420 dpi) and hcc_tablet_api35 (2560x1600, 320 dpi), Android 15 / API 35.
Demo `HCC-0.1.0-proto-v19-debug.apk` SHA-256 `02F92175C041567B5BC1F8869C5A77F8E1C7C7CC8C625240D58397AC91F5A85E`; server `HCC-Server-v19-productionDebug.apk` SHA-256 `C0820D6A9EB84ED9858B96DDAD0FF1CCB8FEB53C4247624C1B60722340F08366`.
Physical Galaxy Tab S7 run uses `APK_v17_Tablet_Test_Prompt.md` as the base (to be refreshed in F2); results to `Acceptance/device-v19/`.
