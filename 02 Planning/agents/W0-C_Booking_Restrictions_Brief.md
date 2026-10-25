# W0-C · Stay-restriction warnings and approver override in the booking screens — agent brief

Wave 0 of `02 Planning/HCC_Multi_Agent_Plan_v1.md`. Owner decision **DEC-053 (APR-005):** min-stay / closed-to-arrival restrictions are enforced for every booking source; users with the **approve** permission (Owner, Manager) may override **with a reason**, recorded on the booking and in the audit log. The command side is already done (Session 9, A2.1): `yldStayViolations(s,type,arrival,departure)` in `src/domain/streams/yield.ts` (exported via `services/mock`), error code `RESTRICTED`, and `restrictionOverride` (reason string) accepted by `BOOK`, `CHANGE_DATES` and `GRP_ROOMING`. Tests: `tests/stream-a2.mjs`. Your job is the UI. You run in your own git worktree/branch; W0-A (navigation, main checkout) and W0-B (till) run in parallel. File ownership is strict.

## Setup (once)
- Create the junction, never `npm install`: `cmd /c mklink /J "03 Prototype\node_modules" "E:\Projects\HCC\Hotel Control Centre\03 Prototype\node_modules"`
- Read: `CLAUDE.md`, `02 Planning/HCC_Parallel_Agent_Brief.md` (architecture, phone rules, theme; ignore its Linux paths), `02 Planning/modules/HCC_Module_13_Tariff_Yield_Spec.md`, `src/services/mock/handlers/reservations.ts` (the `restrictions()` helper).

## Task
1. Reusable `RestrictionNotice` (your new file `src/features/reservations/RestrictionNotice.tsx`): given room type + dates, lists violations in plain words (amber warning style with a text label, not colour only). When the user `can(...,'approve')`, shows an "Override restriction" checkbox + reason field (min 3 characters); otherwise a line "Ask a manager to override, or choose other dates".
2. Wire it into: desktop New booking and Change dates/Move dialogs (`src/features/reservations/modals.tsx`), phone New booking wizard (`src/mobile/frontdesk/NewBooking.tsx` / `logic.ts` — show it on the dates/room step; block Next unless resolved or overridden), Groups rooming list dialog (`src/features/groups/modals.tsx` — block-level notice + override reason passed as `restrictionOverride`).
3. Submit passes `restrictionOverride` only when the override is ticked. Submit stays disabled while violations exist and no override is given (the server still re-checks). A `RESTRICTED` error from the server (e.g. rules changed meanwhile) shows its message in the dialog.
4. Booking details (desktop + phone) already show `notes`; make sure an override note is visible there (read-only).

## You own (write only these)
`src/features/reservations/*` (incl. the new file), `src/mobile/frontdesk/NewBooking.tsx`, `src/mobile/frontdesk/logic.ts`, `src/mobile/frontdesk/Reservations.tsx` (details view only), `src/features/groups/modals.tsx`, new `src/styles/restriction.css` (classes prefixed `rst-`; the build picks up every `src/styles/*.css` automatically), new `tests/stream-a2-ui.mjs`.
**Do not touch:** `src/app/*`, `src/app/mobile/*`, navigation/search files, `src/mobile/frontdesk/Arrivals.tsx`, `src/features/guests/*` (W0-A owns booking-ID search there — if the walk-in booking form there needs the notice, describe the change in your final answer), closing/POS/cash files, `src/domain/*`, `src/services/mock/*`, `registry.ts`, `package.json`.

## Tests (`tests/stream-a2-ui.mjs`, names prefixed `[A2UI]`)
Pure logic you add (e.g. a `rstState(s,uid,type,arrival,departure,override)` helper in your feature folder returning `{violations, canOverride, canSubmit}`): no violations → submit; violations + reception → cannot submit; violations + manager + reason ≥ 3 → submit with override; short reason → cannot; group rooming in a restricted window. Plus end-to-end through `execute` for the phone wizard payload.

## Done
`npx tsc --noEmit` clean, `npm test` all pass, `npm run build` OK (in your worktree's `03 Prototype`); browser check at 1366 px and 360 px as Reception and as Manager booking a Classic room 7→8 Nov 2026 and a Suite arriving 8 Nov (CTA): warning visible, reception blocked, manager override works, booking notes show the override; no console errors, no sideways scroll, 48 px touch targets. Commit on your branch only (message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`); do not push or merge. Final answer ≤ 25 lines: what changed, test counts, files touched, shared changes needed, gaps.
