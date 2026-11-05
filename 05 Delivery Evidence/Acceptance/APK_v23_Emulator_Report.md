# APK v23 emulator report (5 Nov 2026, versionCode 24, versionName 23)

Branch claude/af2-int at 98456bc0 (no source change, nothing committed or pushed; `capacitor.settings.gradle` unchanged after `cap sync`, `git status` clean apart from this untracked evidence).

## Builds
Folder `E:\Projects\HCC\Builds\2026-11-05b\`

| File | SHA-256 | Manifest |
|---|---|---|
| HCC-23-vc24-demo-debug.apk | 0e80076ed06e7222cb9ee157c2b39e0033a8d021db8bf32c83b0b3edf62cd87d | in.hcc.prototype, versionCode 24, versionName 23 (aapt + dumpsys on both AVDs) |
| HCC-23-vc24-server-debug.apk | 00bfb45103bedafc3f80e57c62c7868a60041b30333ff083a5a9cb769dcb3fef | in.hcc.prototype.server, versionCode 24, versionName 23-server (aapt) |
| HCC-Test-CA-throwaway.cer | da467d0f5226af75ce7998ddc61acfbd79e5b5d9f96d2e24a2a0b03d3d2bed04 | copy of the 2026-11-05 test CA |

Pipeline: `npm run build` + `npm run build:production` (03 Prototype) -> in 04 Android `npm ci --prefer-offline`, `node scripts\copy-web.mjs`, `npx cap sync android`, `gradlew assembleDebug --offline` (demo), `node scripts\build-production.mjs --ca ...HCC-Test-CA-throwaway.cer --assemble` (server). All exit 0, run through `tools\Invoke-Serialized.ps1`. Server APK not installed on an emulator (v22 server pages were owner-side); only built and manifest/version checked.

## Checks (screenshots in `05 Delivery Evidence\Acceptance\v23\`; tablet files prefixed `tab-`)

Phone = codex_api35_google (Reception, then Manager for f). Tablet = hcc_tablet_api35 (2560x1600, desktop layout, dark theme, Owner, fresh data).

| # | Check | Phone | Tablet |
|---|---|---|---|
| a | Launches, no crash, version | PASS: launches, 0 FATAL/ANR in logcat, dumpsys versionName 23 / versionCode 24 (no version string is shown in the UI) - 01-launch.png | PASS: same, 0 FATAL/ANR - tab-01-launch.png |
| b1 | Counter bill unit price over 1 crore refused | PASS: Rs 1,00,00,001 gives "The unit price can be at most Rs 1,00,00,000.00. Check the amount (it is in paise).", Issue bill disabled, total Rs 0 - 04b.png | PASS: same message - tab-04a-unit-over-1crore.png |
| b2 | Price exactly 1 crore accepted | PASS: Rs 1,00,00,000 x 1 gives total Rs 1,18,00,000 (18% GST), Issue bill enabled - 04c-1crore.png | NOT SEPARATELY SHOWN (tablet only exercised 10 lakh x 11 = Rs 1.298 crore total, enabled) |
| b3 | Bill total over 10 crore refused | PASS: 11 x Rs 1 crore refused, message "One counter bill can be at most Rs 10,00,00,000.00. Check the quantities and prices, or split the bill.", Issue bill disabled - 04d-bill-over-10crore-refused.png | PASS: same - tab-04d-bill-over-10crore-refused.png |
| c1 | Retired / unknown payment method | PASS (cannot be selected): Tender offers only Cash, UPI, Card, Bank - 05b-payment-tenders.png. The "retired" refusal text itself NOT-REACHABLE from the UI | PASS (cannot be selected): dropdown Cash / UPI / Card / Bank transfer - tab-05d-tender-options.png |
| c2 | Normal Cash payment works | PASS: Rs 100 on HCC-1041, toast "Receipt recorded once", outstanding Rs 975 -> Rs 875 - 05d-after-first-payment.png | PASS: Rs 150 on HCC-1043, outstanding Rs 975 -> Rs 825 - tab-05f-after-first-payment.png |
| c3 | Duplicate payment shows "Needs your confirmation" | PASS: same amount again shows the amber "Needs your confirmation" note with the tick row "This is a second, separate payment", confirm button disabled, short toast; not posted (closed with Discard) - 05g-duplicate-prompt.png | PASS: same, "Needs your confirmation" + tick row, Post receipt disabled - tab-05i-duplicate-prompt-confirm.png |
| d | Customers list opens, search fast, profile opens | PASS: 1,156 customers, "Kamble" narrows to 43 within the ~0.8 s measured (typed + wait), profile of Divya Kamble opens - 06a/06b/06c | PASS: list + auto-opened profile, "Kamble" gives 43 shown - tab-06a/06b |
| e | Reports hub opens and runs a report | PASS: Report library, "Room status today" runs, rows shown (run stamp "by Aarav") - 07a/07b | not run (subset a-d only) |
| f | Manager rate change more than 15% below agreed goes to Owner | PASS: as Meera (Manager), HCC-1042 room 102 agreed Rs 2,000, new rate Rs 1,500: "This needs an approval from the Owner or a Manager within their limit. (agreed Rs 2,000 - 25 % below - over the 15 % threshold (Owner))", button reads "Request approval". Request not submitted. - 09c-rate-1500.png | not run |

## Observations (not defects, for the owner)
- The 10 crore bill cap is on the pre-discount, pre-GST net total (CTR_BILL_MAX_PAISE in counter.ts: "one bill (before discount)"). A bill of 9 x Rs 1 crore net, Rs 10,62,00,000 with GST, was therefore issued (DEMO/2026-27/1156, phone AVD, demo data) while 11 crore net was refused. If the intent is a cap on the GST-inclusive total, this is a spec question. The server's `MaxBillPaise` was not re-checked here.
- Counter bill for Rs 10.62 crore paid in cash first hits the Rs 2,00,000 cash-receipt limit message (s.186 / old 269ST, CA to confirm), which is a different rule and fired before any size cap; UPI was needed to reach the cap.
- On the phone, uiautomator text taps of "Discard" matched the "Discard changes?" title before the button; a test-script quirk only.

## Not verified
- Server APK on any device or emulator, its pages, egress-proof, physical Galaxy Tab S7 (a physical device R52N807PTTE was attached throughout and was never touched; all adb calls targeted `emulator-5554`).
- The retired-payment-method refusal text (no UI path to a retired method in the demo seed).
- Tablet: exactly-1-crore accept, reports (e), manager rate (f). The Owner role is not subject to the Manager threshold, so f was done on the phone only.
- Whether a version label exists in the UI (none found).

## Defects found
None. Emulator data (receipts, one Rs 10.62 crore counter bill, role switched to Manager) is left in the AVD's demo state; both emulators were shut down.
