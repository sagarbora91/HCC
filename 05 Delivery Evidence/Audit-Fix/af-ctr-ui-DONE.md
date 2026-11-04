# af-ctr-ui DONE

Lane af-ctr-ui (wave 1, Sonnet) / branch `claude/af-ctr-ui` / worktree `E:\Projects\HCC\worktrees\af-ctr-ui` / base 6a29f3e6 + `claude/auditfix` merged in (wave-0 runner fix, `HCC_TEST_ONLY`, async tests awaited).

## Commits
Own commits (`git log --oneline 08c0aa6f..HEAD`; the three af-runner commits below them came in through the `claude/auditfix` merge):
- Counter bills: till of the open shift, method/company/item pickers from the masters, older-bill lookup (BIL-01, UX-01, HIS-01)
- Counter bills: keep the pure helpers out of the sync-layer module so the prototype bundle stays clean
- Counter bills: inline production guards so the prototype bundle drops the lookup hook
- Counter bills: restore LF line endings in lfmoney.ts (line endings only)
- af-ctr-ui: DONE file

## Files (all in `03 Prototype`)
Edited: `src/clients/sync/lfmoney.ts`, `src/features/lfmoney/CounterBills.tsx`, `src/features/lfmoney/Mobile.tsx`.
Created: `src/clients/sync/counterHooks.ts`, `tests/sync-counter-bills.mjs`, `tests/stream-af-counter-ui.mjs`.
Created, not in the brief's list: `src/features/lfmoney/counterLocal.ts` (pure helpers). Reason: the first version kept `ctrLookupLocal` and the pickers in `counterHooks.ts`; that file imports the sync layer, so the prototype bundle then contained `hcc-sync-layer` and the two bundle tests ([P5], [W4-0]) failed. The pure code now lives in a feature file with no sync imports; `counterHooks.ts` holds only the production hook. Same folder as the screens, no other lane touches it.
Not created: `src/styles/m-lf.css` does not exist in the repo and no new style was needed (existing classes and the phone `T48` inline style are used), so no CSS was changed.

## What was built
- **BIL-01 (client).** `CTR_BILL_ISSUE` (lfmoney.ts): when the settlement is Paid and the chosen method is a drawer method of the payment-methods master (`mnyIsDrawer`, not the literal "Cash"), the command carries `settlement.tillId` = the till of the open shift (`ids.server('till','current')`). With no known till the mapper throws `INVALID_TRANSITION`: "The cash shift is closed. Open a cashier shift first, then issue the cash bill (or settle by UPI / card)." A `precheck` refuses on the device before any request: empty method ("Choose the payment method."), a method that needs a reference with a blank reference ("Enter the payment reference."), a drawer method with `!state.shiftOpen` (the existing `shiftClosedText`). Note: the precheck refusal arrives as reason `precheck` with a `VALIDATION` error; the `INVALID_TRANSITION` error is the mapper's own defence (tested directly). UPI/Card/Bank send no `tillId`.
- **UX-01.** Both screens (desktop `CounterBills.tsx`, phone `Mobile.tsx`): Method is a `<select>` of `mnyNames(s,'frontdesk')` (default = first drawer method); Reference input appears only for a method with `needsReference` and is required; Company is a `<select>` of the active company accounts (`grpActiveCompanies`), a free-text "Company id" input stays only when no companies are loaded; each line has a "From catalogue / menu" `<select>` (active catalogue extras + active POS menu items, the same sources `ctrLineFrom` reads) that sets `extraId` / `menuItemId` and prefills label, unit price and GST category, with a "Free text" option; the unit price stays editable (DEC-241 pending: nothing blocks or approves a price). A "Cash goes to: <settles-to account name of the master>" line shows for a drawer method, or "No cash shift is open - cash bills cannot be issued until a shift opens..." and Issue is disabled. Note: there is no till name in the client state (the sync layer only learns till ids), so the line shows the master's "settles to" account name (seed: "Front-desk cash drawer").
- **HIS-01 (client).** `counterBillLookup(api,{invoiceNo,from,to})` in lfmoney.ts -> `GET /counter-bills` with only the filled query keys, mapped by `counterBillsFromServer` (returned oldest first like the list). `counterHooks.ts` `useCounterBillLookup(query,enabled)` (production, `useServerFetch` pattern). Bills tab on both screens: "Find an older bill" row (invoice no., From, To, Find, Clear); production uses the hook, the prototype uses `ctrLookupLocal` (invoice contains, date range, newest first); results use the same table / cards with the same "Credit note..." action (`CTR_BILL_VOID_REQUEST` with the found bill's id); empty result shows "No bill matches". The server filter is af-ctr-reads' (same wave); the fake server in my tests answers it.
- Server refusal texts from af-ctr-svc: to confirm at integration (that lane's DONE was not available when this lane finished). The client no longer sends cash without a till, "Cashh"-type values cannot be picked, and a missing reference is stopped on the device with "Enter the payment reference.".

## Tests
Counts: `npm test` 2778 before (af-runner total after the wave-0 merge) -> 2791 after (+13: 5 `[AF-CTR]` in `sync-counter-bills.mjs`, 8 `[AF-CTR-UI]` in `stream-af-counter-ui.mjs`), 0 failed, 20 async tests counted.
- `npx tsc --noEmit` clean.
- `npm test` through the lock: 2791/2791.
- `npm run build` and `npm run build:production`: exit 0, no warnings in the logs.
- Quick loop used: `HCC_TEST_ONLY='sync-counter-bills|stream-af-counter-ui'` 69/69 (includes the other suites matching the regex).
- Fit check: see Open items.
- Secret scan (`Invoke-SecretScan`, base 6a29f3e6): ok true, 0 findings, 586 lines in 13 files.
Named tests (exact): `[AF-CTR] CTR_BILL_ISSUE cash sends tillId 7 of the open shift and the method name`; `[AF-CTR] cash with the shift closed is refused before any request (0 commands sent, INVALID_TRANSITION text)`; `[AF-CTR] UPI sends no tillId and keeps the reference`; `[AF-CTR] a drawer method from the master that is not named "Cash" still sends tillId`; `[AF-CTR] counterBillLookup sends GET /counter-bills?invoiceNo=… and maps rows`. Stream tests: method select / company select / item picker / till line / Find row / editable price on desktop and phone (6), `ctrLookupLocal` filter, picker + default method.

## Open items
- Fit check `node tools/fit-screen-check.mjs --pages counter-bills`: FIT_RESULT_PLACEHOLDER
- Phone check (`tests/check-phone-*.mjs`): none exists for counter bills; not run. The phone screen is checked by the source tests only.
- The prototype build (no server) cannot show the production lookup; it uses the local filter (by design in the brief).
- DEC-241 untouched: the picked item's master price is only a suggestion; a typed price below it goes through unchanged, as before.
- Nothing stopped on; no money, GST, posting or permission rule was invented.

## Integration needed
- None required of other lanes. At integration: af-ctr-svc's refusal texts ("Cashh", missing reference) should be quoted by the verifier; the lookup needs af-ctr-reads' `GET /counter-bills?invoiceNo=&from=&to=` (query names used exactly as in the brief: `invoiceNo`, `from`, `to`, dates `yyyy-MM-dd`).

## HANDOFF bullet
af-ctr-ui (BIL-01 client, UX-01, HIS-01 lookup UI) on `claude/af-ctr-ui`: counter-bill cash now sends the open shift's till, method/company/item come from the masters on desktop and phone, older bills are found by invoice number / dates (server read by af-ctr-reads). Prototype tests 2778 -> 2791; tsc, both builds, secret scan clean. Fit check and server refusal texts to confirm at integration.
