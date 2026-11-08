# F8C-PHONE DONE (8 Nov 2026)

Branch claude/f8c-phone, code commit 007e345e (cut from main 8f99a17c). Not pushed.

## What changed
- New `03 Prototype/src/mobile/frontdesk/routing.ts`: pure plan. Turns "payer of room" / "payer of extras" (guest | company:<id> | udhaar:<id>) into the existing SF commands (SF_ROUTE_SET to give a category back, SF_FOLIO_REMOVE for a folio left empty, then SF_FOLIO_OPEN / SF_ROUTE_SET to add), plus `mfRoutingProblem` (one company folio; an empty company folio may be replaced) and `mfIsGroupMember`.
- New `03 Prototype/src/mobile/frontdesk/BillRouting.tsx`: "Who pays" card (two selects, 48 px, credit left shown as a hint under a chosen company, desk wording for the info note and refusals, saves one command at a time and stops on the first refusal showing the command's own message).
- `03 Prototype/src/mobile/frontdesk/Arrivals.tsx` line 116: card mounted in step 6 (review) of the phone check-in, before the Check in button. Import at the top.
- `03 Prototype/src/styles/mobile-frontdesk.css`: .mf-route styles (token vars only).
- Test: `03 Prototype/tests/stream-mf-routing.mjs` (8 tests).
- Gate: card renders only with permission('billing') and stay Confirmed / In house (DEC-061; Housekeeping, Supervisor, Maintenance have no billing, the commands also refuse them: tested). Group member: card shows the group message, no editor. No new page or modal, so no registry / RBAC registry change was needed.
- Not touched: clients/sync/*, desk split dialogs (reused wording only), production files.

## Tests actually run
- tsc --noEmit: clean (twice).
- HCC_TEST_ONLY='stream-mf-routing|stream-split-ui|stream-mobile-frontdesk|stream-split-folio|fit': 298/298 passed (final run; includes the 8 new tests, F8 split-folio/UI suites, mobile front-desk, fit tests).
- npm run build x3 (via Invoke-Serialized) OK.
- NOT run: full npm test, the CSS theme/token round-trip test as a separate run (only var() tokens used), server tests (no server change), fit-screen browser tool `fit-screen-check.mjs` (not run; checked by eye at 390x844).

## Browser check (390x844 mobile viewport, port 5183, Reception user Aarav, fixture arrival Ishaan Bose HCC-1049)
Screenshots in `05 Delivery Evidence/F8C/phone/`:
01 card collapsed on step 6; 02 editor open; 03 BEFORE fix (credit text in option truncated); 04 saved, room on company; 05 AFTER fix (credit hint under select; two different companies refused with message); 06 empty company folio replaced by another company (plan: remove empty B, open C).
Defects seen and fixed: truncated option text (credit moved to hint); an empty company folio could not be replaced (logic fixed + test).
Defect seen, NOT fixed (other lane's file): the LF-DESK "Stay details" block above the card (features/guests/DeskFields via desk.node) is unstyled on the phone (small native selects, labels not stacked, inputs below 48 px).
Not seen in browser: the Udhaar option (fixture has no active credit guest; covered by command logic only, not by a phone test); the over-credit-limit refusal text on the phone (tested at command level only).

## Defaults relied on (F8_Owner_Questions, owner has not answered)
SFQ-1 max 4 folios; SFQ-2 two categories room / extras (no partial split); SFQ-4 reception, manager, owner may set it, no approval; SFQ-5 routing applies to later postings only (UI says so); SFQ-10 group master payer not offered, group member refused; SFQ-14 one company folio per stay; SFQ-16 Udhaar allowed. "Another guest in the party" payer (desk dialog has it) is NOT offered on the phone: the brief named guest / company / Udhaar only.

## Stopped / open
- Walk-in flow (MfWalkin) has no routing step: it checks in immediately; routing there would need a stay-detail entry (not built). Phone folio screen reaches the desk 'sf-split' dialog via SfFolioBar for that case.
- Tariff tick on step 4 is not remembered when a check-in is reopened (pre-existing, step text says it is).
