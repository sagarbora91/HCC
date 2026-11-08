# F8D-PHONE DONE (8 Nov 2026)

Branch claude/f8d-phone, code commit b92491e9 (cut from claude/f8c 0af7a992). Not pushed. Docs commit follows it.

## What changed (all under 03 Prototype)
1. "Stay details" (DeskFields) on the phone: `src/styles/mobile-frontdesk.css` (appended block "F8D phone"), selectors scoped to `.modal.m-sheet` / `.m-shell` so the desk modal and `src/styles/lf-desk.css` are untouched. Card look, stacked labels, 48 px inputs/selects, 16 px text, single column. `features/guests/DeskFields.tsx` NOT edited.
2. Walk-in "Who pays" step: `src/mobile/frontdesk/Arrivals.tsx` (MfWalkin): new screen 1 (billing permission only; others skip it), `doRoute` runs `mfRoutingPlan` (same routing.ts) after BOOK + REGISTER and before CHECKIN, as its own recorded step "Set who pays". On refusal: message kept, buttons Retry routing / Change who pays / Guest pays everything (undoes any half-applied routing, then checks in) / guided check-in / cancel.
3. "Another guest in the party" payer: `routing.ts` (`mfPersonKey`, `mfPersonOf`, `mfWantProblem`; existing other-person folios now shown as that person, not "guest"), `BillRouting.tsx` (new exported `MfPayerFields`, shared by the check-in card and the walk-in step; option + name + optional GSTIN). Desk rules copied from Split.tsx / SF_FOLIO_OPEN: name 2-120 chars, GSTIN blank or 15 chars, opens a payer kind `guest` folio B-D.
4. Tests: `tests/stream-mf-routing.mjs` +3 ([F8D-PHONE]).

## Tests actually run
- tsc --noEmit: clean. npm run build: OK.
- HCC_TEST_ONLY='stream-mf-routing|stream-split-ui|stream-mobile-frontdesk|stream-split-folio|fit|stream-lf-desk|css|phone': 390/390 passed after commit (CT-A line-count/F8C-insertion guards diff against HEAD, so they need the change committed; they failed uncommitted, passed committed).
- NOT run: full npm test, server tests (no server change), fit-screen-check.mjs.

## Browser (port 5188, Reception Aarav, fixtures made at runtime through the app's own run(): company "Tiny Fixture Co" limit Rs 500, guest G-0001 Udhaar account Rs 50,000; no seed change). Screenshots in 05 Delivery Evidence/F8D/phone/
02 Stay details after (390x844); 03/04 card + editor with company and another guest; 05 company over-limit refusal text ("Credit limit exceeded ... remaining Rs 500; this split folio needs Rs 6,300 ..."); 06 Udhaar room + Meera Shah extras saved; 07 walk-in Who pays step; 08 walk-in routing refused (Rs 2,100 needed) with recovery buttons; 09 "Guest pays everything" then checked in.
- Udhaar option seen and saved. Over-limit refusal seen for a company. An Udhaar over-limit refusal is NOT shown at routing: SF_FOLIO_OPEN does not check the Udhaar limit (contract: limit applies when the guest-credit request is made at check-out); not seen on the phone.
- Not seen: housekeeping / supervisor / maintenance in the browser (card and step are gated on permission('billing'); command refusal tested in stream-mf-routing). Walk-in with a company that is within its limit was not run in the browser (covered by the plan test).
- Tablet: at 767 and 820 px the app serves the desktop guided check-in (tablet = desktop layout), where DeskFields is unchanged by construction (CSS scoped to phone shell); no tablet screenshot of the phone card. Honest gap.
- Note: the shared Playwright browser was navigated by another lane mid-session; I re-opened my own tab on 5188 and re-took 07-09; 02-06 were taken before and were checked by eye as mine.

## Defaults relied on (F8_Owner_Questions, owner has not answered)
SFQ-1 max 4 folios; SFQ-2 room / extras only; SFQ-4 billing permission, no approval; SFQ-5 later postings only; SFQ-9 guest payer may carry a GSTIN; SFQ-10 group member refused; SFQ-14 one company folio; SFQ-16 Udhaar allowed.

## Stopped / open
- Nothing stopped on a money rule. Walk-in company limit is checked only once the reservation exists (estimate of the new booking), so a refusal happens after BOOK + REGISTER; the recovery buttons handle it.
- Pre-existing, not fixed: the phone step bar overflows to the right at 390 px on steps 2-3 ("Advance" label clipped is fine on step 6).
