# TRAINING_NOTES: oddities and defects seen in the build while scripting (wave TRN)

Written while scripting the course; nothing here was fixed in HCC code (plan section 8). For the owner and the next HCC fix wave. Build: `claude/training` from origin/main 3a0c2d0e, prototype flavour, scenario FY26, business date 24 Oct 2026.

## Facts that differ from the plan text (not defects)

| # | Plan said | Build shows | What the course does |
|---|---|---|---|
| N1 | "there is no sign-in screen in the prototype" | The prototype starts on a staff picker, "Who is using this device?" (no password, `SignIn` in `ModulesHome.tsx`); the choice is remembered in localStorage `hcc-session`. | Training starts signed in as the chapter's operator; the course says staff pick their name on the real device and use their PIN on the production server. |
| N2 | 60 sub-functions, 39 pages | The catalogue lists **67** features in 18 modules; navmap lists 48 registered pages + Dashboard + Modules Home = **50** | coverage.md ticks 67 / 50. |
| N3 | Reception works from the Dashboard (chapter 0 "Aarav") | The Reception role has no Dashboard tile (Owner, Manager, Accountant and Cashier have it). | Chapter 0 shows Aarav's Modules Home, then switches to Meera (Manager) for the Dashboard step. |
| N4 | 10 demo users + maybe a Restaurant user | 12 demo users: owner, manager, reception, cashier, supervisor, housekeeper (Ravi), housekeeper2 (Kavya), accountant, maint, techadmin, **daymgr (Tanvi, Day shift manager), nightmgr (Rohan, Night shift manager)**. No Restaurant user. | Restaurant chapter uses Priya (Cashier), as the plan allows. |
| N5 | `dist` about 2.4 MB | `dist/index.html` is 4.5 MB (prototype), 4.9 MB (production). | Nothing. |
| N6 | Hotel name | The fixture hotel is "HCC Demo Residency". | Deck footer and slides say so. |

## Behaviour worth knowing

- A module that has exactly one page for the signed-in role opens that page straight away from its tile (Admin & Setup for Reception opens Appearance & display). The driver's `menu:` act handles this.
- The "Scenario FY26 loaded" toast is only drawn after somebody is signed in (the sign-in screen has no toast element). Training removes it before the first shot.
- On the Dashboard the breadcrumb reads `Modules Home > Dashboard > Arrivals` (the selected tab becomes a last crumb). Intended by FIT (DEC-196), looks odd on a page whose first tab is "Arrivals".
- Search box and menu close with Escape only when focus is inside them; the driver sends the key to the focused element.

## Changes made under `03 Prototype` (all prototype-flavour only, listed for review)

- `src/app/demo/training/` (new): `hooks.ts`, `targets.ts`, `acts.ts`, `driver.ts`, `index.ts`.
- `src/app/App.tsx`: import + one guarded call `setTrainingHooks({ctx,signedIn,signIn})`.
- `src/main.tsx`: one guarded dynamic import, only with `?training`.
- `src/app/storage.ts`: `saveStored` returns at once in training mode (decision: saving disabled, not cleared).
- `serve.mjs`: two dev-server routes `/training/steps.json` and `/training/phone.json` read the course steps from `06 Training` (the single-file build never serves them).
- `tests/stream-training.mjs` (new).
- `data-tr` attributes: none needed yet.

## Findings while scripting chapters 1-8

| # | Where | What | Course handling |
|---|---|---|---|
| F1 | Story vs fixture | Room 206 is occupied (fixture stay-over) and 207 is reserved for an arrival today (Aditi Nair): Vikram Jadhav gets **Suite 106**, Rahul moves **203 to 107** | Said on the slides |
| F2 | Calendar of the story | A guest arriving today cannot be checked out before the night audit has posted every agreed night, so Rahul (24 to 26 Oct) can only leave on 26 Oct | Driver act `closeday` (REGISTER+CHECKIN pending arrivals, approve the pending expense, settle open POS checks, NIGHT_POST, SHIFT_CLOSE, DAY_CLOSE, NEXT_DAY, SHIFT_OPEN through the app's own run()) runs twice at the start of chapter 8 and chapter 8's second day. Chapters 9 onward are on **26 Oct 2026**. Chapter 19 teaches the Day close page on that day |
| F3 | Company credit | Reception can create a company account, but the credit limit is Owner-only; a company booking is refused without a limit | Chapter 1 switches to Sagar (Owner) for the account |
| F4 | Aadhaar | The ID number is checked with the Verhoeff check digit: `XXXX XXXX 4321` cannot be typed | Fictional valid numbers 2345 1009 4321 (Rahul) and 3456 7890 1238 (Anita) |
| F5 | Discount | Service recovery is capped at 25 % of the charges posted so far (Rs 175 on a folio of Rs 700): the story's Rs 300 is refused | Rs 150 before GST used |
| F6 | Booking with advance | Cancel and No-show stay disabled while money is held on a booking (refund first) | Chapter 1 cancels a hold; the fixture arrivals Ishaan Bose and Aditi Nair are checked in by `closeday` |
| F7 | POS | UPI and card settlement need a reference before the Settle button acts (no message when it is missing, the button just does nothing) | Reference typed; shot checks now look for the result |
| F8 | Desk alerts | Reception clicking Done on a wake-up banner is refused (`housekeeping permission is required`); Manager can | Meera clicks Done in chapter 8 |
| F9 | Cashier | The Checkout button is not offered to the Cashier role on In-house stays / folios; Reception checks out | Priya takes the payment, Aarav checks out |
| F10 | Invoice | Number series is `DEMO/2026-27/NNNN` in the prototype (DEC-197 series `HPE/2627/0001` is for Hotel Parth) and the page is stamped "not a tax invoice" | Said on the slides |
| F11 | Bank deposit | The deposit slip photo is required and cannot be attached by the driver | Dialog is read, not submitted |
| F12 | Shift close | Closing the cashier shift in chapter 7 would refuse later cash receipts | Shift is counted and closed by `closeday`; chapter 19 shows the page |
| F13 | Booking numbers | Booking ids are sequence based: Rahul HCC-1101, Meena Kulkarni HCC-1103, Vikram HCC-1107, Anita HCC-1115 | Deterministic on a fresh FY26 load |

## Findings while scripting chapters 9-19

| # | Where | What | Course handling |
|---|---|---|---|
| F14 | Cashier shift vs close command | On 26 Oct the Cashier shift page shows expected cash Rs 9,625 (float 12,825 net movements -3,200); typing 9625 and submitting is refused with "A reason is required when the count differs from the expected cash". The command compares against another figure (`drawer()`, Rs 30,255). The page and the command disagree. | Chapter 19 types the amount and stops before Submit; `closeday` uses `drawer()` |
| F15 | Day close on 26 Oct | Ten fixture stays are due out on 26 Oct (Ananya Rao 101 ... Aditi Nair 207) and block the close | Chapter 19 teaches the seven steps and the blocked preflight; the day is not closed, so chapters 20+ stay on 26 Oct |
| F16 | Payroll | Only Apr to Sep can be opened in Payroll; October is run after the month ends | Chapter 18 reads the locked September run and payslips |
| F17 | Hall terms | Quotation terms are all "OWNER TO REPLACE" and the Templates tab lists none; only the Owner can send with an override and a reason | Chapter 15 uses Override (Owner); halls are Sagar Hall, Moti Hall and Garden Lawn (the plan said Hall A) |
| F18 | Customers CRM | No merge screen: duplicates are flagged "Possible duplicate" on Guests and never merged | Said on the slide |
| F19 | OTA | Four of six inbox bookings cannot be accepted (arrival before the business date, party too big) | Chapter 14 accepts BookNest BN-88231 and explains the pre-check |
| F20 | Expense bill photo and bank deposit slip | File inputs cannot be filled by the driver | Sections are opened and described |
| F21 | Advances | One open advance per person: Ravi already has one | Chapter 18 records the advance for Sunil Pawar |
| F22 | Tariff card | A card with the same start date as a just-published rate set is refused (hand-entered tariff exists) | Chapter 13 leaves the card as a draft |
