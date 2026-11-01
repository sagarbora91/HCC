# FIT-fit-front-b DONE

Lane **fit-front-b** (phase B) · model Sonnet · branch `claude/fit-front-b` · worktree `E:\Projects\HCC\worktrees\fit-front-b` · base `3dac5afb` (claude/fit). Nothing pushed.

## Commits
```
0dd13366 FIT front-b: reservations, inhouse, guests converted to FitPage (fill / split), tests
(+ this file)
```

## Files
Prototype: `src/features/reservations/Reservations.tsx`, `src/features/guests/InHouse.tsx`, `src/features/guests/GuestProfile.tsx` (`GuestsPage`), new `src/features/guests/useWideSplit.ts`, `src/styles/guests.css` and `src/styles/reservations.css` (appended, `.app-shell`-guarded, no font-size, no colours), `src/app/subscreens.ts` (one comment line in the FIT-FRONT block).
Tests: `tests/stream-fit-front-b.mjs` (6 tests `[FIT-FRONT-B]`) + `tests/fit-front-b-entry.tsx`.
`BookingTable.tsx`, `RestrictionNotice.tsx`, `Confirmation.tsx`, `modals.tsx`, `masters.tsx`, `Calendar.tsx`: untouched. `arrivals` (`CheckIn.tsx`): untouched (already the reference page; no polish needed in code).
Phone paths: untouched.

## What was built (per page)
| Page | Pattern | Sub-screens | Default tab / filter changed | Check |
|---|---|---|---|---|
| `reservations` (and `folios`, which renders the same `Reservations` component with `folio`) | fill: `FitPage` + header primary action (`+ New reservation`, via `act()`) + one toolbar (search, status filter buttons, source select, "Staying on" date, count) + `BookingTable` in one `ScrollPanel`; `ShowMore` and the fictional-data note (`FitNote`) in the panel footer; the guest link / open button keeps opening the existing booking modal | none | none (status filter stays `All`) | see below |
| `arrivals` | already `FitPage` fill (fit-shell) | none | none | see below |
| `inhouse` | split: stays list `ScrollPanel` left, `StayDetail` record pane right (`SplitView` 2fr 3fr); slide-over with Back below 900 px; Print list in the header | none | none | see below |
| `guests` | split: profiles list left, `GuestPanel` right (`SplitView`); New guest in the header | none | none | see below |

Wide layout keeps the old "first record pre-selected" behaviour; below 900 px nothing is pre-selected so the list shows first and a tap opens the record (`useWideSplit`).
No selector, command, permission, fixture, type, money or phone change. Fonts untouched.

## Tests
Through the lock (label `fit-front-b`), output in `.scratch/` of the worktree: `npx tsc --noEmit` clean (exit 0). `npm test` and the check: see the "Results" section below.
Existing tests updated: none (no test asserted the old structure of these pages).

## Modal openers for tools/fit-modals.json
- `reservations`: opener `+ New reservation` (roles owner, reception, manager) — already in the shell sample.
- `inhouse`: opener `Print in-house list` (roles owner, manager, reception) — wide `front-list` dialog.
- `guests`: opener `New guest` (roles owner, manager, reception) — `guest-profile` form.
- `arrivals`: opener `Walk-in` (roles owner, reception, manager).

## Integration needed
None.

## Could not fit / open items
See "Results".

## HANDOFF bullet
FIT front-b: reservations / folios view, in-house stays and guests converted to FitPage (fill / split); arrivals unchanged; no sub-screens; layout only.
