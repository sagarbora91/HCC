# af-v22 - three cosmetic fixes from APK v21 testing (5 Nov 2026)

Branch claude/af-v22 (from claude/auditfix 73ec28d1 + claude/apk21-report). Report: Acceptance/APK_v21_Emulator_Report.md. D21-4 (Cash default) untouched.

- D21-1: a duplicate-payment refusal is shown once, as an amber "Needs your confirmation" note, with a full-width 48 px tick row "This is a second, separate payment" (phone Payment.tsx and desktop modals.tsx). The early-warning note and the phone "Not posted" panel are hidden while it shows; the modal-footer error banner is not set for a duplicate refusal; the toast is a short non-error line (App.tsx DUP_TOAST); the confirm button reads "Confirm" not "Try again". Server message, codes and tick text unchanged. Styles in src/styles/v22-fixes.css (mobile-money.css is frozen by the CT-A token tests).
- D21-2: src/styles/folios.css - tabs row and four stat cards wrap inside the width (no sideways scroll). At 800 px the last card ended at 791 against a 784 container; now 784.
- D21-3: src/styles/rpt-hub.css (phone .rpt-m) - search box no longer 220 px tall (flex-basis in a column; now 50 px); report parameters use normal field styling (label above, 48 px, 16 px, From/To side by side).

Verification: tsc clean; npm test 2840/2840 (baseline 2839 + 1 new, [AF-V22 D21-1]); npm run build OK; tools/fit-screen-check.mjs --pages folios,report-hub --roles owner,reception exit 0 (PORT=4183 node serve.mjs); tracked evidence files unchanged; the untracked Fit screens folder it created was deleted.
Screenshots (headless Chrome, before vs after): Acceptance/v22/ (tablet-portrait 800x1280 folios; phone 390x844 report library, report params, duplicate payment).
Not run: check-print-layout, check-tablet-back, emulator.
