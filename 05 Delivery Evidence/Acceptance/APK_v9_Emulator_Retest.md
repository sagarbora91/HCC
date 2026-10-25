# APK v9 emulator re-test (W0-D AC-04 closure + Wave 0.5 smoke)

Date 25 Oct 2026 · EMULATED: AVD `codex_api35_google` (Android 15 / API 35, 1080x2400, 420 dpi, gpu host) · WebView Chrome 124.0.6367.219 · driven by WebView DevTools (Node 25) with real `adb input tap` for key actions.

## Build facts
- Code: worktree `E:\Projects\HCC\worktrees\integration`, branch `integration/w05`; base efafbd7 (SCHEMA 8). One new commit `652adce` (only `04 Android\android\app\build.gradle`: versionCode 8 -> 9; versionName stays 0.1.0-proto). Not pushed.
- `03 Prototype\dist\index.html` SHA-256 `85916176da1ff9b46359068c61b2684f15743fa726568b88a0efee72481aeb60` (verified before build).
- `node scripts\copy-web.mjs` + `npx cap sync android` + `gradlew.bat assembleDebug` -> BUILD SUCCESSFUL (4m15s).
- Toolchain note: the Android Studio JBR on this PC is Java 25 (Gradle failed with "Unsupported class file major version 69"). Built with Microsoft JDK 21 (`C:\Program Files\Microsoft\jdk-21.0.12.101-hotspot`), ANDROID_HOME `E:\Android\Sdk`, GRADLE_USER_HOME `E:\Cache\gradle`. No PKIX problem. The README hint to use the Android Studio JBR is out of date.
- APK `05 Delivery Evidence\APK\HCC-0.1.0-proto-debug-v9.apk`, SHA-256 `600aba4172c711ce6d6d454df6f0185168c50333d94eb206800c448849642369` (5,903,896 bytes). The v8 file `HCC-0.1.0-proto-debug.apk` is kept.
- The bundled `assets/public/index.html` inside the APK has SHA-256 `85916176da1ff9b46359068c61b2684f15743fa726568b88a0efee72481aeb60`, byte-identical to dist.
- Signing: v9 debug cert SHA-256 3c49e759417d2294fe6ab2570ea6b776616d62b8e6d984b5fa3fb7e9c61fc8f0 (this PC); v8 cert faae9739c054b88b4c9cee8f62bf1dfa103807b7a45a835d901467cb26fe05dc (old PC). See V9-1.

## Upgrade from v8
| Step | Result | Notes |
|---|---|---|
| `adb install -r` v9 over v8 (data kept) | **Fail (blocked)** | INSTALL_FAILED_UPDATE_INCOMPATIBLE, signature mismatch. Defect V9-1. |
| Substitute: export the v8 state (IndexedDB `hcc/state/current`, schema 7: 24 rooms, 12 bookings, 36 audit rows, 4 closed dates), uninstall, install v9, inject the same state, relaunch | **Pass (simulated upgrade)** | Opened to sign-in, then Modules Home; no "reset" or data-lost notice. Owner > Compliance calendar had obligations (37: Overdue 1, Due this week 1, Filed 32; GSTR-1 overdue 15 days, PT due in 5 days). Staff & payroll > Statutory rates present (PF 12+13% to 15,000, ESIC 0.75+3.25%, MH PT, OT 1x, structure 50/80/10). After one action (Mark filed) the stored state was `schema: 8` (was 7) with all counts preserved. Shots `v9-upgrade-*`. Not a true in-place upgrade. |
| Clean install of v9 | Pass | First launch shows sign-in with the FY26 scenario; no state row until used; S0 loads at schema 8. |

## Checks
| # | Check | Result | Evidence | Notes |
|---|---|---|---|---|
| 1a | AC-04 Cashier "Count drawer" (Priya, S0 24 Oct, 22x500 + 200 + 50 = 11,250): after Submit & close shift no "Discard the details you entered?" dialog | Pass | v9-ac04-cashier-after-submit-no-dialog | Page stayed responsive (a native confirm hangs DevTools); shift Closed, difference None. |
| 1b | AC-04 Day closing "Count & submit cash" (Meera; 25 Oct 11,250, and 24 Oct on later runs) after submit | Pass | v9-ac04-closing-after-submit-no-dialog | Close day and Open next day worked afterwards. Leaving the untouched sheet with Back is also silent. |
| 1c | AC-04 Reports > Change > Apply range (24-26 Oct) | Pass | v9-ac04-reports-after-apply-no-dialog | Sheet closed, range 24 Oct - 26 Oct applied, no dialog. |
| 1d | Leaving a sheet with unsaved edits still asks | Pass | v9-ac04-cashier-dirty-discard-dialog, v9-ac04-closing-dirty-discard-dialog, v9-ac04-reports-dirty-discard-dialog | Entered 5x500 / "5" / a changed From date then Cancel/Back: "Discard the details you entered?"; CANCEL keeps the sheet. |
| 2a | AC-03 count formula shows the 550 restaurant cash and adds up | Pass | v9-ac03-count-formula | Day closing sheet 24 Oct: "Expected cash: 10,750 = opening float 5,000 + cash in 7,550 (receipts and deposits 7,000, restaurant / room-service 550) - cash out 1,800." 5,000 + 7,550 - 1,800 = 10,750. Cashier page also says "Cash in includes restaurant cash 550". (Second S0 run, no Room 105 deposit; on the full run the drawer was 11,250 after the 500 deposit.) |
| 2b | RC-1 count-method switch >= 48 px | Partial | v9-rc1-cashier-count-sheet | Day closing sheet: Total / Denominations 48 px (Pass). Cashier "Count drawer" sheet: 44 px (Fail, V9-2). |
| 2c | RC-1 range-sheet labels 16 px | **Fail** | v9-rc1-range-sheet | Labels 14 px, inputs 16 px. V9-2. |
| 3a | S0 drawer 10,750 -> 11,250 (500 deposit) -> settle -> 12,750 -> 12,450 (300 expense) | Pass | v9-s0-drawer-12450 | Reception booked Room 105 (24-26 Oct, 2 adults, 1,000 UPI advance; source left as Direct, not Phone) and checked in; Cashier took the 500 cash deposit -> 11,250; counted 11,250 and closed 24 Oct (EXP-102 approved, T2 and Room 201 settled by UPI after KOT sent); 25 Oct laundry 500 and discount, closed with count 11,250; 26 Oct folio balance 3,400, invoice 4,400, discount line -500; split 2,000 cash + 1,400 UPI and 500 deposit returned -> 12,750; Aqua Water Cans 300 recorded by cashier, approved by manager -> 12,450. |
| 3b | Reception raises 500 discount, Manager approves (DEC-063 G1) | Pass | v9-s0-discount-pending | Pending in Approvals (limit 250), approved by Meera; folio showed -500 (tax -50), balance 3,400. |
| 3c | Android Back: sheet -> page -> Modules Home -> exit prompt | Pass | v9-back-exit-prompt | From Reports with the range sheet open: Back closed the sheet, then Reports -> Room board -> Front Desk -> Modules Home -> "Exit Hotel Control Centre?" (Stay / Exit). Back walks the visited-page history, not a fixed parent. |
| 4a | Cash book day view; lock refused for the counter | Pass | v9-w05-cashbook-lock-refused-counter, v9-w05-cashbook-owner-locked | 24 Oct: opening 5,000, in 7,550, out 1,800, closing 10,750. Meera counted and closed; "Lock this day" disabled with "A different person from the one who counted must lock the day". Owner locked it; Reopen... shown. |
| 4b | Compliance: Owner sees it, Manager does not | Pass | v9-w05-compliance-owner, v9-w05-compliance-manager-absent | Manager's Accounts & Reports lists Day closing, Reports, Accounting, Management assistant only. |
| 4c | Payroll payslip share + simulated WhatsApp | Pass | v9-w05-payslip-whatsapp, v9-w05-payslip-a4-print | FY26 Sept 2026 (locked run): Meera payslip; "Send on WhatsApp" -> "Payslip sent on WhatsApp (simulated - nothing left this device)", log "sent 1x"; "Payslip PDF" opens Android print preview ISO A4, 1/1 page, net 41,687.50. (S0 has no payroll run; runs are tablet/desktop only.) |
| 4d | Master configuration (Owner) | Pass | v9-w05-master-config-owner | 12 groups listed; Money and tax opens (synthetic tax 10%, discount threshold 250). |
| 4e | Excel export saves to Documents/HCC + share sheet | Pass | v9-w05-excel-share-sheet | Cash book > Excel: "Sharing 1 file HCC-Demo-cash-book-2026-10-24.xlsx" sheet; file in `/sdcard/Documents/HCC/` (14,524 bytes, PK zip header). |
| 4f | Day pack prints A4 preview | Pass | v9-w05-daypack-a4-preview | Closing summary > Day pack (A4 PDF) > Print / save A4 PDF: Android print preview ISO A4, 2 pages (run on scenario S1, 24 Oct closed). |
| 5 | Chrome 83 note | Not run | - | Emulator WebView here is Chrome 124.0.6367.219. The old-WebView AVD does not exist on this PC. |

Counts: Pass 15, Partial 1 (2b), Fail 1 (2c), Not run 1 (5); upgrade-in-place blocked (V9-1).

## Defects
| ID | Severity | Summary | File |
|---|---|---|---|
| V9-1 | S3 | v9 (this PC's debug keystore) cannot update v8 (old PC's keystore): INSTALL_FAILED_UPDATE_INCOMPATIBLE; data upgrade verified only by state re-injection | defects\V9-1-debug-keystore-differs-no-inplace-upgrade-from-v8.md |
| V9-2 | S4 | RC-1 residual: range-sheet labels 14 px; cashier count-sheet switch 44 px | defects\V9-2-rc1-residual-label-14px-and-cashier-switch-44px.md |

Notes: while a native print job is open the WebView exposes a second `about:blank` target (scripts must pick `https://localhost`). The emulator was shut down at the end (`adb emu kill`).
