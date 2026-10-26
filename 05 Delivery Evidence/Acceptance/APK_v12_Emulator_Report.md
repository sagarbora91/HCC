# APK v12 emulator report (demo APK and server APK against the real server)

Date 26 Oct 2026 · tester: Claude (Sonnet 5.5) · EMULATED, not physical devices · screenshots in `v12\` (a-* demo APK phone, s-* server APK phone, t-* server APK tablet; numbered in test order). No product code changed; the only commit besides this report is `versionCode 12` (b629b5e).

## Builds (versionCode 12 in all three, `dumpsys package` on the AVDs)
| APK | Package | SHA-256 | Notes |
|---|---|---|---|
| `APK\HCC-0.1.0-proto-debug.apk` (v11 kept as `-v11.apk`) | `in.hcc.prototype` | `39C6C9F7846C94A1...` | `assembleDebug`, demo data on device |
| `APK\HCC-0.1.0-server-debug-TESTCA.apk` | `in.hcc.prototype.server` | `3635D571F794861E...` | `productionDebug`, bundles a THROWAWAY test CA (CN "HCC APK12 Throwaway Test CA", valid 30 days, SHA-256 16:A6:01:56...), HTTPS only, debuggable. Test use only |
| `productionRelease` (not copied, signed with a throwaway keystore) | `in.hcc.server` | `C74147E865305F43...` | `build-production.mjs --verify`: "not debuggable, allowBackup=false, no cleartext"; aapt: no `debuggable`, `allowBackup=0x0`, `usesCleartextTraffic=0x0`, network security config present; apksigner verify OK (throwaway signer) |

APKs are git-ignored (`*.apk`); keys, certs, keystore, credentials, guest-ID key and the offline key copy were created in `%TEMP%` and deleted at the end; nothing secret is in the repo.
Build note: the first Gradle build ran before `npm ci` in `04 Android`; `cap sync` then dropped `@capacitor/preferences` from the tracked gradle files. Fixed by `npm ci --prefer-offline` in `04 Android` and `git checkout` of those two files, then all three APKs were rebuilt (the delivered ones contain the Preferences plugin). Anyone building on a fresh checkout must run `npm ci` in `04 Android` first (README says `npm install` "first time only"; it should say so for every fresh clone).

## Server set-up (deviation from INSTALL.md, no admin rights)
- Disposable DB `HccTest_APK12_20261026123249` on `.\SQLEXPRESS` (migrated 0 -> 2702, 45 migrations); dropped at the end (verified: only EtpReporting, EtpScratch_20261020 and system DBs remain).
- API: `dotnet run` Development, Kestrel HTTPS `https://0.0.0.0:5443` from a PFX via `Kestrel__Endpoints__Https__*` (the documented `LocalMachine\My` thumbprint path needs an elevated shell; not tested here). Test CA + server cert (SAN 10.0.2.2, 127.0.0.1, localhost) made with openssl. Guest-ID key made with `New-HccGuestIdKey.ps1 -Scope User -SkipAcl`, passed as `Hcc__GuestIdKeys__*`.
- Setup with the W4_E2E2 scripts (copied to scratch, base URL changed): SetupProperty, 11 sample masters (0 errors), GSTIN 27AAAAA0000A1Z2 accepted, Owner password, PINs for 4 staff, one fictional accountant password.

## Demo APK, phone AVD `codex_api35_google` (412 css px, 1080x2400)
| # | Check | Result | Evidence |
|---|---|---|---|
| A1 | Install, cold start, FY26 data, business date 24 Oct, scrollWidth 412 = viewport | Pass | a-01 |
| A2 | Role switch via DEMO sheet (Owner, Reception, Housekeeper, Maintenance), tiles and bottom tabs per role | Pass | a-03, a-04 |
| A3 | Booking wizard 4 steps, confirm; check-in wizard 6 steps (ID validation message for licence format works), checked in | Pass | a-07..a-21 (HCC-1111, room 106) |
| A4 | Folios list for Reception; in-house filter; checkout checklist dialog | Pass | a-24, a-31 |
| A5 | V10-1 folio preview CGST/SGST split (2,000 net, 50 + 50 @2.5 %, total 2,100) | Pass | a-32 |
| A6 | Print / share opens Android print with "ISO A4" (uiautomator) | Pass | a-33 |
| A7 | V11-1 touch sizes: no interactive control under 47.5 px / 16 px on the home and wizard pages (room-type chips 48 px, DEMO pill and bottom tab labels 16 px; only the stepper +/- icon buttons report 15 px font, icons only) | Pass (fixed) | a-07 |
| A8 | V11-3 CGST/SGST values right-aligned | Pass (fixed) | a-32 |
| A9 | V11-4 "Add charge" label no longer touches its border | Pass (fixed) | a-25 |
| A10 | V11-7 Back after a role switch (Owner on Folios, switch to Ravi, Back x3) | Pass: Back lands on Modules or a page Ravi may see (Linen & stock), never a money page; "Nothing here for this user" did not appear | a-29, a-30 |
| A11 | V11-2 / V11-5 are tablet items, see T7 | n/a | |

Demo counts: 10 pass, 0 fail. Not re-run on the demo APK: housekeeper checklist tick/complete, refund and expense-reverse on a locked day, CSV share, 18 % room, phone landscape (all passed in v11; no code in those areas was in this build's diff that I know of).

## Server APK, phone AVD (HTTPS to `https://10.0.2.2:5443`, test CA)
| # | Check | Expected | Actual | Result |
|---|---|---|---|---|
| S1 | Server-address screen, Test connection | discovery + health over TLS with the bundled CA | "Found WORKPC . version 0.1.0 at https://10.0.2.2:5443", Save enabled | Pass (s-01, s-02) |
| S2 | Owner password sign-in | signed in | accepted, then "Register this device?" offered | Pass (s-04) |
| S3 | Register device ("Phone emulator") | registered, Owner home | Modules with Test Owner, business date 26 Oct Open | Pass (s-05) |
| S4 | Sign-out, staff picker, Reception PIN | picker of 4 names + "Someone else", PIN accepted | Priya Kulkarni signed in ("PIN on device 2" in the audit log) | Pass (s-07, s-08) |
| S5 | Room board | 6 rooms | All 6 Ready | Pass (s-09) |
| S6 | Booking + check-in (Reception) | HCC-1101 created, ID stored | Booking, registration "Guest verified" with a driving licence number (no 503 = D3 holds on the APK), checked in, room 101 Occupied | Pass (s-10..s-19) |
| S7 | Cash deposit as Reception | posts | "Not permitted: Not permitted: Outside your assigned scope" (Reception has no open cashier shift; see APK12-3) | Fail, defect |
| S8 | Cashier shift opened by Ramesh, then cash deposit Rs 500 as Ramesh | shift opens, receipt recorded | "Cashier shift opened", folio shows deposit 500, "Receipt recorded once", audit RECEIPT_RECORDED | Pass (s-27..s-30) |
| S9 | Housekeeper (Sunil) screens | My tasks, Report repair | Both open; no task exists to complete: checkout is blocked until room nights are posted by the nightly audit, so no dirty-room task could be created without running a day close | Not completed |
| S10 | Show ID audited dialog | masked until "Show ID"; view logged | Dialog "This view was logged. It hides itself in 28 s"; audit row `GUEST_ID_VIEW guest-10002 full ID number shown` | Pass (s-31..s-33) |
| S11 | Sign-out | back to the staff picker | picker shown; audit "Signed out" | Pass |
| S12 | App killed (`force-stop`) and restarted | still signed in (Preferences) | Modules as Priya Kulkarni, cold start 1.9 s | Pass (s-40) |
| S13 | API stopped: read-only with cached data | banner, cached lists, no writes | "Server unavailable - read-only ... Nothing is saved on this device", list of bookings from cache with "data as of 14:41" | Pass (s-41, s-42) |
| S14 | Offline cold start | cached data after sign-in only | app restarted offline: still signed in, bookings from the cache and the same banner | Pass (s-45, s-46) |
| S15 | New booking while offline | disabled | New booking button is enabled and the wizard opens; step 2 says "No availability" (misleading); nothing is saved (APK12-5) | Partial |
| S16 | API restarted | recovers | not automatic within about 20 s; after "Retry" the banner cleared and "Server data as of 14:44" showed (the first Retry came before the server was listening) | Pass with note (APK12-6) |
| S17 | Wrong-PIN throttling | clear message | server correctly throttles (first failure audited "Wrong PIN (1 in a row)", then 429 per address/name, a correct PIN is also refused). The app shows only "Sign-in failed. Check the name and the password or PIN." (later "Not permitted: Sign-in failed ...") and never the server text "Too many sign-in attempts from this device. Wait a few minutes and try again." | Fail (APK12-2) |

Phone server counts: Pass 12, Fail 2 (S7 as designed-but-confusing, S17), Partial 1 (S15), Not completed 1 (S9). Phone emulator killed after this section.

## Server APK, tablet AVD `hcc_tablet_api35` (2560x1600, 1280x800 css landscape)
| # | Check | Result | Evidence |
|---|---|---|---|
| T1 | Fresh install, address screen, Test connection and Save over TLS | Pass | t-02, t-03 |
| T2 | Owner password sign-in, register "Tablet emulator" | Pass | t-04, t-05 |
| T3 | Desktop layout (drawer, module grid, tables), not the phone shell | Pass | t-05, t-07, t-13 |
| T4 | New reservation dialog: dates show the full year (V11-5 fixed), availability, guest, Confirm | Pass: "Tablet Guest" booking 2 in room 102 | t-08, t-12 |
| T5 | Rotation with the dialog open and text typed: layout stays desktop at 800 px, nothing lost, back to landscape keeps it | Pass | t-09, t-10, t-11 |
| T6 | Room board, sign-out, Reception PIN sign-in, guided check-in with ID, "Checked in today 2" | Pass (form filled by script for the long fields, buttons by DOM) | t-13..t-23 |
| T7 | V11-2 module tile badge no longer covers the title | Pass | t-05 |
Tablet counts: 7 pass, 0 fail. Emulator killed; API stopped; DB dropped.

## Defects and observations (no product code was changed)
| ID | Sev | Summary | Steps / evidence |
|---|---|---|---|
| APK12-1 | S4 | Wording: "The cash shift is closed. Open the next business day first." when the day is Open but no cashier shift is open | Reception records a cash deposit before the Cashier has opened the shift (s-24) |
| APK12-2 | S3 | The app hides the server's throttling message: after repeated wrong PINs the device is locked out (429, 15 min window) but the screen only says "Sign-in failed. Check the name and the password or PIN." Staff will think they forgot the PIN | 12 wrong PINs for Anita, then the correct PIN is refused (s-48, s-49, s-50). Server text exists in `AccessEndpoints.cs`; the client (`03 Prototype\src\clients\sync\auth.ts`) does not show it |
| APK12-3 | S3 | Reception is offered "Record payment" but the server refuses it ("Outside your assigned scope") because PAY-001 is own-shift scoped; the message repeats "Not permitted:" twice and does not say what to do | s-24; matrix v1 gives REC `A·S` for PAY-001 |
| APK12-4 | S4 | Production wording left in the profile sheet: "Preview as (data)" and "Owner. Sign out returns to the staff picker; Preview as is a demo tool." (D10 / N7 follow-up) | s-06 |
| APK12-5 | S4 | Offline: the "read-only" banner says bookings are disabled but "New booking" is enabled; the wizard then reports "No availability" | s-43, s-44 |
| APK12-6 | S4 | After the server returns, the read-only banner stays until "Retry" is pressed (no automatic recovery within 20 s) | s-47 |
| APK12-7 | S4 | Booking reference shows as "1" / "2" (reservation id) on the server phone list and check-in header, not "HCC-1101" as in the audit log | s-20, s-14 |
| APK12-8 | S4 | Show ID dialog: the title "Driving licence" touches the close X and all text is right-aligned | s-33 |
| APK12-9 | S4 (build docs) | `04 Android\README.md` says `npm install` "first time only"; a fresh clone or a stale `node_modules` silently builds the server APK without `@capacitor/preferences` after `cap sync` | see Builds |
| APK12-10 | S4 (test gap) | Checkout needs room nights posted by the nightly audit, so a housekeeping task cannot be created on a fresh database without a day close; HK task completion on the server APK is untested | S9 |

No S1 or S2 defects found. V11-1..5 and V11-7 verified fixed or not reproduced (V11-6 is an observation, not re-checked).

## Not tested
Physical devices, a release-signed APK on a device (only built and verified with aapt/apksigner; `adb run-as` on `in.hcc.server` not run), `LocalMachine\My` thumbprint hosting and the Windows service path, the real Wi-Fi LAN address (emulator uses 10.0.2.2), housekeeper task completion on the server APK, refund/credit-note/day-close/payroll on the APK, printing from the server APK, phone landscape and 360 px on the server APK, session expiry (idle/absolute) and cache deletion on expiry, device-registration removal, address change flow.
