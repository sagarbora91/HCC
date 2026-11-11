# fx-merge-ui DONE (wave FX, lane merge-ui)

Customer merge screen inside Customers 360 (training finding F18): possible duplicates, side-by-side compare, merge with a confirm, dismiss. Desktop and phone, demo and production.

## What the brief assumed and what was true
- The brief says the prototype already had mock merge commands. It did not (`frontdesk.ts` said "no prototype screen yet"). Added `CUSTMERGE_PROPOSE`, `CUSTMERGE_DECIDE`, `CUSTMERGE_DISMISS` as mock handlers that copy the server rules.
- The server has no "dismiss a pair" command. Dismiss is therefore demo-only; the production screen hides "Not the same person" and `CUSTMERGE_DISMISS` is `unmapped` with the reason. No migration was added.
- The server merge is maker-checker (`ProposeCustomerMerge`, then `DecideCustomerMerge` by a different person, PERM-CRM-003 = Owner and Manager for both steps). The screen copies that: "Merge into ..." sends a request (confirm says what moves and that it cannot be undone from the screen); a second Owner / Manager approves or rejects from "Waiting for approval". Permission is not widened: demo uses `approve` (held by exactly Owner and Manager), production uses PERM-CRM-003 from `/me/capabilities`.

## What changed (files, all in `03 Prototype` unless noted)
- New `src/domain/streams/customer-merge.ts`: slice type `customerMergeX` (aliases, requests, dismissed pairs, optional `server` block), pair finder (same normalised e-mail; same name where one record has no mobile; same rules as `CustomerRules.FindDuplicates` / `SuggestSurvivor`), compare rows, "what moves" texts, `cmxView`, `cmxMayMerge`.
- New `src/services/mock/handlers/customer-merge.ts` (registered in `handlers/index.ts`, exported from `services/mock/index.ts`). Approval makes the merged-away key an alias of the survivor: stays and old Lodge stays show under the survivor, preferences follow the server rule (survivor kept; a different text of the other becomes a note; empty is copied), guest notes are carried over. The credit account is not touched (said in the confirm).
- `src/domain/streams/customers.ts`: `customerIndex` resolves aliases (cache key includes the aliases; the survivor's own stays decide name / phone / profile). No change when there are no aliases.
- `src/domain/types.ts`: optional `customerMergeX` (no SCHEMA bump).
- New `src/features/customers/Merge.tsx` (desktop: list of requests and pairs scrolls in its panel, compare table / confirm in the detail pane) and `MergeMobile.tsx` (phone sheet: cards, 48 px buttons). `Customers.tsx`: desktop tab "Possible duplicates" (Owner / Manager only, with count), phone button that opens the sheet. `register.tsx`: `mobile.modals['customer-merge']`. `src/app/subscreens.ts`: sub-screen entry + visibility.
- New `src/clients/sync/customer-merge.ts` + registry lines: loader for `customerMergeX` from `GET /customers/duplicates`, `/customers/merges`, `/customers/profile` (first 20 groups), commands to `ProposeCustomerMerge` / `DecideCustomerMerge` (row version from the load as `expectedVersion`). A role without PERM-CRM-003 sends no GET.
- `src/styles/v22-fixes.css`: block `/* FX-MERGE-UI */ ... /* /FX-MERGE-UI */`, tokens only.
- Tests: `tests/stream-fx-merge-ui.mjs` (new, 20 tests); `tests/_sync-harness.mjs` gained two default empty routes (`GET /customers/duplicates`, `/customers/merges`) so every sync test with an Owner login still loads; `tests/stream-fit.mjs` allow-list line for `MergeMobile.tsx`.

## Tests run (all through the lock)
- `HCC_TEST_ONLY=fx-merge-ui`: 76/76 passed (20 new + 56 base tests the runner always runs).
- FIT: `HCC_TEST_ONLY='stream-fit\.mjs'`: 115/115 passed (includes `[FIT] no phone-layout file changed since the wave base`). Note: the contract's filter `\[FIT\]` matches file names only, so it selected no stream file (56 base tests); the file-name filter above is what actually runs the FIT file.
- `npx tsc --noEmit`: clean.
- Full `npm test` (once, at the end, through the lock): **3405/3405 passed, 0 failed** (the first full run had 14 failures from tests that list commands, reads and tabs; fixed below, then rerun).
- `npm run build` was not run (CSS and TS compile checked by tsc and the tests only).

## Existing tests changed (minimal, because the new feature is listed there)
- `tests/stream-rbac-matrix.mjs`: CMDS lists `CUSTMERGE_*` as OWN MGR.
- `tests/stream-fit-guest.mjs`: Owner's Customers page tab list gained `duplicates` (page tabs and sub-screens).
- `tests/_role-reads.json`: routes `GET /customers/duplicates` and `/customers/merges` added; status written by hand (200 for OWN and MGR, which hold PERM-CRM-003 in the seed; 403 for every other role) because the real-server regeneration (`RoleReadMatrixTests`, HCC_UPDATE_ROLE_READS=1) was not run. The server test compares this file, so run it once to confirm.
- `src/clients/sync/reads.ts`: two READS rules (`one(wide('PERM-CRM-003'))`), marked.

## NOT verified
- The hand-written role-read rows against the real server (see above).
- No browser / emulator / APK run: screens were checked as static markup in tests and by the CSS rules only (no 360 px visual check, no screenshot).
- No server was run; the production mapping is tested against the fake server shaped from the .NET DTOs, not the real API. The server side is unchanged (no server file touched, so no dotnet test).
- Production Customers 360 list is derived on the client from bookings and guests (phone keys) while the server's aliases use opaque tokens. After a merge approved on the server the merged-away customer disappears from the duplicates list and the merge shows as Approved, but the main Customers list in production still lists both until that list is fed by the server. Not solvable without a token to phone-key mapping.
- Tags and CRM notes do not exist in the prototype's Customers 360, so the demo moves guest-profile notes and preferences only; the confirm text names tags and notes because the server moves them.
- The production compare shows preferences / notes for the first 20 groups only (others show "Not loaded").

## Defaults used
- Merge stays maker-checker in the demo too (a single user cannot complete a merge alone), because widening or narrowing the server rule was forbidden.
- "Merge into X" is one button per customer ("Merge into" plus the name) instead of the literal "Merge into this one", so the two buttons are distinguishable.
- Dismiss is one click with a toast; no extra confirm. Demo only (see above).
- Reason field is pre-filled with the match reason; 5 to 300 characters as on the server.
- Groups of more than two customers are cut into pairs (suggested survivor against each other member), merged one pair at a time like the server.
- Dismissed pairs are stored in the demo slice only; a pair with a pending request leaves the list until the request is decided.
- Credit account is shown in the compare only to the Owner (DEC-058) and is never moved.

## Open questions
- Should the server get a dismiss command (needs a table, so a migration)? Not added.
- Should production Customers 360 be fed from `GET /customers` so merges show there at once? Out of scope here.

## Shared files touched
`src/styles/v22-fixes.css` (marked block), `src/clients/sync/registry.ts` (marked lines), `src/app/subscreens.ts` (marked lines), `tests/stream-fit.mjs` (allow-list line), `tests/_sync-harness.mjs` (one default-route line), `src/services/mock/handlers/index.ts` and `services/mock/index.ts` (one line each, marked). No migration, no SCHEMA bump, no HANDOFF / CLAUDE.md edits. LF line endings kept.
