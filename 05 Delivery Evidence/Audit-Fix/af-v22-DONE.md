# af-v22 - three cosmetic fixes from APK v21 testing (5 Nov 2026)

Branch claude/af-v22 (from claude/auditfix 73ec28d1 + claude/apk21-report). Report: Acceptance/APK_v21_Emulator_Report.md. D21-4 (Cash default) untouched.

- D21-1: a duplicate-payment refusal is shown once, as an amber "Needs your confirmation" note, with a full-width 48 px tick row "This is a second, separate payment" (phone Payment.tsx and desktop modals.tsx). The early-warning note and the phone "Not posted" panel are hidden while it shows; the modal-footer error banner is not set for a duplicate refusal; the toast is a short non-error line (App.tsx DUP_TOAST); the confirm button reads "Confirm" not "Try again". Server message, codes and tick text unchanged. Styles in src/styles/v22-fixes.css (mobile-money.css is frozen by the CT-A token tests).
- D21-2: src/styles/folios.css - tabs row and four stat cards wrap inside the width (no sideways scroll). At 800 px the last card ended at 791 against a 784 container; now 784.
- D21-3: src/styles/rpt-hub.css (phone .rpt-m) - search box no longer 220 px tall (flex-basis in a column; now 50 px); report parameters use normal field styling (label above, 48 px, 16 px, From/To side by side).

Verification: tsc clean; npm test 2840/2840 (baseline 2839 + 1 new, [AF-V22 D21-1]); npm run build OK; tools/fit-screen-check.mjs --pages folios,report-hub --roles owner,reception exit 0 (PORT=4183 node serve.mjs); tracked evidence files unchanged; the untracked Fit screens folder it created was deleted.
Screenshots (headless Chrome, before vs after): Acceptance/v22/ (tablet-portrait 800x1280 folios; phone 390x844 report library, report params, duplicate payment).
Not run: check-print-layout, check-tablet-back, emulator.

## Emulator recheck (5 Nov 2026, APK HCC-22-vc23-demo-debug.apk, demo, mock data)
One emulator at a time, both stopped at the end. dumpsys: versionCode 23, versionName 22. Logcat: 0 FATAL EXCEPTION (phone run; tablet run also 0). Screenshots: Acceptance/v22/v22-*.png.

| Item | Result | Evidence |
|---|---|---|
| D21-2 tablet portrait (hcc_tablet_api35), Guests & folios: 4th stat card "Approved refunds to execute" fully inside right edge | PASS (card ends inside the page gutter, no clipping) | v22-tablet-portrait-folios.png |
| D21-1 phone (codex_api35_google), folio HCC-1041: same amount again shows the message once as an amber "Needs your confirmation" note, full-width tick row "This is a second, separate payment", button "Confirm Rs 110" | PASS (the old early-warning note on the review page is a short neutral line, the refusal itself shows once; no red "Not posted" panel; toast is a short line "Needs your confirmation: tick ...") | v22-phone-duplicate-prompt.png, -scrolled, v22-phone-second-prompt.png |
| D21-1 tick + confirm gives exactly one extra receipt | PASS (receipts 7 -> 8 -> 9, toast "Receipt recorded once") | v22-phone-folio-after-first.png, v22-phone-second-final.png |
| Split, two equal UPI Rs 50 rows, same reference R9: exactly 2 receipts | PASS (prompt shown, ticked, receipts 9 -> 11, toast "2 receipts recorded once") | v22-phone-split-prompt.png, v22-phone-split-final.png |
| D21-3 phone Report library: search box normal short box (about 50 px); From/To label above, normal field styling, side by side | PASS | v22-phone-report-library.png, v22-phone-report-params.png |
| Version 22 and no crash | PASS | dumpsys versionName=22, no FATAL EXCEPTION |

Notes: the phone AVD kept data from the v21 run, so the first Rs 110 payment already met a recent Rs 110 receipt and showed the prompt at once; the receipt counts above are from the folio Receipts badge. Tablet rotation done with `cmd window user-rotation lock 1` (the settings keys did not rotate it). Server APK and physical tablet not covered.
