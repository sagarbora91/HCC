# APK v21 emulator report (5 Nov 2026, versionCode 22, versionName 21)

Scope: demo APK only (`Builds\2026-11-05\HCC-21-vc22-demo-debug.apk`, package `in.hcc.prototype`, mock data). Server APK not run. No production code changed. Screenshots in `Acceptance\v21\`.
Both AVDs run one at a time (`-no-snapshot -no-audio`), stopped at the end. `dumpsys`: versionCode 22, versionName 21. Logcat: 0 `FATAL EXCEPTION` on both AVDs.

## Part A - tablet AVD hcc_tablet_api35 (2560x1600)
| Check | Result | Evidence |
|---|---|---|
| Launches, no AndroidRuntime crash | PASS | tablet-home.png |
| Modules home | PASS | tablet-home.png |
| Rotate portrait and back to landscape | PASS, layout reflows, no clipping | tablet-portrait-folios.png, tablet-after-rotate-back.png |
| Cash & Billing page + list page (Guests & folios) | PASS | tablet-cash-billing.png, tablet-folios.png |
| D20-1 stat cards fill the row (Dashboard) | PASS. Wrapped 3+1: the fourth card (Payment receipts) now stretches to the full strip width, no lone card beside empty space | tablet-dashboard.png |
| Stat row on Guests & folios | Landscape: all four cards in one row. Portrait: four cards in one scroll row, the fourth ("Approved refunds to execute") is cut at the right edge (see D21-2) | tablet-folios.png, tablet-portrait-folios.png |
| V17-1 window insets: modal footer visible (landscape, after opening New reservation) | PASS, Cancel / Hold 2 hours / Confirm reservation fully visible | tablet-modal-landscape.png |
| V17-1 modal footer in portrait, also after rotating | PASS | tablet-modal-portrait.png |

## Part B - phone AVD codex_api35_google (1080x2400)
| # | Check | Result | Evidence |
|---|---|---|---|
| 1a | Counter bill: add a line, payment-method picker shown | PASS (Method: Cash, UPI, Card, Bank transfer) | phone-counter-method-options.png |
| 1b | Cash: till shown | PASS as text "Cash goes to: Front-desk cash drawer" (one till in the demo, so no picker to choose from; a picker with several tills is NOT testable here) | phone-counter-cash-ready.png |
| 1c | Non-cash method asks for a transaction reference | PASS. UPI adds a required Reference field; Issue bill without it is stopped by the field check ("Please fill out this field") | phone-counter-upi-ref.png, phone-counter-upi-noref2.png |
| 1d | Issue with no method: refusal 'Select a payment method (Cash, UPI, Card or Bank transfer).' | NOT TESTABLE in the UI. The Method select has no blank option (Cash is the default), so the text can only come from the handler (`handlers/counter.ts`, `folio.ts`), not from a tap. Not guessed. | phone-counter-method-options.png |
| 1e | Issue one valid cash bill | PASS. "Counter bill issued.", DEMO/2026-27/1156, Paid now, Rs 63 | phone-counter-issued.png |
| 2a | Folio payment, then the SAME amount again: duplicate-suspected prompt | PASS. Folio HCC-1041: Rs 110 cash posted; second Rs 110 shows the notice "A Rs 110 Cash receipt was recorded at 08:09. Post again only for a second, separate payment", error "A Cash payment of Rs 110 was recorded ... receipt RCPT-1121", and a "This is a second, separate payment" checkbox; Try again is disabled until it is ticked | phone-folio-pay2-duplicate-scrolled.png |
| 2b | Confirm it: exactly ONE extra receipt | PASS. Receipts 1 -> 2 (first payment) -> 3 (confirmed second) | phone-folio-pay2-final.png |
| 2c | Split, two UPI rows Rs 50 + Rs 50, DIFFERENT references (R1 / R2) | No prompt; posted straight away; receipts 3 -> 5 (2 receipts). Sensible, different references are not a duplicate | phone-split-review.png |
| 2d | Split, two UPI rows Rs 50 + Rs 50, SAME reference R3, "second, separate payment" confirmation | PASS. Prompt shown ("A Rs 50 UPI receipt was recorded at 08:11 ..."), ticked, Try again: receipts 5 -> 7, exactly 2 for the split | phone-split2-review.png, phone-split2-final.png |
| 3 | Customers: open a customer, page loads, no phone number in URL/key | PASS. Ananya Rao opens (Stay history 4, Profile, Requests). Webview URL over devtools is `https://localhost/` with no path or hash. The phone number is only visible as display text in the list. Local/session storage keys were not enumerated. | phone-customers.png, phone-customer-detail.png |
| 4 | Reports hub (Report library): favourite and unfavourite | PASS. "Add to favourites" on Daily operations puts it in a Favourites section at the top; "Favourite" toggled off removes the section | phone-report-fav-on.png, phone-report-hub-fav.png, phone-report-fav-off.png |

## Defects
| ID | Sev | Finding |
|---|---|---|
| D21-1 | S4 | Phone payment modal after a duplicate-suspected refusal repeats the same message three times (notice, in-card warning with checkbox, "Not posted" panel) and a fourth copy sits below the footer (also a toast). The confirm checkbox is easy to miss, and the red "Not posted ... You can safely try again" text reads as a failure rather than a confirm step. Works, wording and layout only. |
| D21-2 | S4 | Tablet portrait, Guests & folios: the fourth stat card ("Approved refunds to execute") is cut off at the right edge (horizontal scroll row), beside the Folios / Folio requests tabs. Not wrapped, so D20-1 does not apply as written, but a card is clipped. |
| D21-3 | S4 | Phone Report library: the "Search reports" box sits in a very tall empty bordered card (about 570 px high) and the report date pickers (From / To) are unstyled grey boxes with the label inline, not full width. Cosmetic. |
| D21-4 | S4 (note) | Phone counter-bill Method has no "Select a method" placeholder, so the refusal text for no method can never show in the UI (the guard is only in the handler). Fine if Cash default is intended. |

No S1-S3 found. Not covered: server APK pages, physical Galaxy Tab S7, multi-till picker, any server-only behaviour.
Note: the Android back key opens an "Exit Hotel Control Centre?" dialog from Modules home (seen once, Stay works); not a defect.
