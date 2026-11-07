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
