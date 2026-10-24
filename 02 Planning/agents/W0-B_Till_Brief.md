# W0-B · Shared till (restaurant cash into the front-desk drawer) — agent brief

Wave 0 of `02 Planning/HCC_Multi_Agent_Plan_v1.md`. Owner decision **DEC-052 (APR-005): Option A — shared till.** You run in your own git worktree/branch; two other streams work in parallel (W0-A navigation in the main checkout, W0-C booking restriction UX). File ownership is strict.

## Setup (once)
- Your worktree has no `node_modules`. Create junctions, never `npm install`:
  `cmd /c mklink /J "03 Prototype\node_modules" "V:\Codex\Hotel Control Centre\03 Prototype\node_modules"`
- Read: `CLAUDE.md`, `02 Planning/HCC_A2_Till_Decision_Pack.md`, `02 Planning/HCC_Parallel_Agent_Brief.md` (architecture, phone rules, theme — ignore its Linux paths/Playwright setup), `02 Planning/HCC_Financial_Rulebook.md` §4, Decision Register DEC-035/052.

## Task
1. **Drawer and shift:** POS cash sales and same-day voids of restaurant / room-service bills settled in cash (`s.posX.receipts`, `method==='Cash'`, outlet mode) count **once** in `shiftMovements()` (label e.g. "Restaurant · <bill no>") and therefore in `drawer()` (`drawer` must keep equalling `shiftExpected().expected`). UPI/card POS settlements are non-cash (show them in daily receipts by method). Room service charged to a folio (`mode:'folio'`) is a folio charge only — never POS cash.
2. **Closing:** the closing pack / day-closing checklist shows restaurant cash inside expected cash; **open (unsettled) POS checks block day close** with a clear item linking to the POS page (settle, charge to room, or void with approval). Cash receipts after shift close are rejected for POS too (same rule as folio cash).
3. **FY26 history:** regenerate coherently — restaurant cash enters each day's expected cash and the daily count; closing records still come from the real `closingSummary` (check the order in which `fixtures/history/operations.ts` and `modules-sales.ts` run; POS history may need to exist before closings are computed). Opening float stays ₹5,000 each morning via banking.
4. **Figures that change legitimately** (e.g. BASE drawer ₹10,200 if BASE has cash POS bills on 24 Oct): recompute, update the assertions and the fixture/acceptance docs (`02 Planning/HCC_Fixture_and_Scenario_Catalog.md`, S0 oracle references in `HCC_Acceptance_Matrix.md`) with an old → new derivation line. Never weaken an assertion or edit fixtures to keep old numbers.
5. Desktop + phone: cashier shift page and phone cashier show the restaurant cash lines; POS page shows "counted in the front-desk drawer".

## You own (write only these)
`src/domain/selectors.ts` (`drawer` and directly related helpers only), `src/domain/streams/closing.ts`, `src/domain/streams/pos.ts`, `src/services/mock/handlers/closing.ts`, `src/services/mock/handlers/pos.ts`, `src/features/closing/*`, `src/features/pos/*`, `src/mobile/money/Cashier.tsx` + `src/mobile/money/logic.ts`, `src/fixtures/ext/pos.ts`, `src/fixtures/history/operations.ts`, `src/fixtures/history/modules-sales.ts`, `src/fixtures/history/generate.ts` (ordering only), `src/styles/m-pos.css` / closing styles, existing tests that assert changed figures, new `tests/stream-a2-till.mjs`, the two planning docs named in step 4.
**Do not touch:** `src/app/*`, `src/app/mobile/*`, navigation files, `src/features/reservations/*`, `src/mobile/frontdesk/*`, `src/domain/types.ts`, `src/domain/errors.ts`, `src/services/mock/index.ts`, `registry.ts`, `package.json`. If you need one of them, describe the exact change in your final answer.

## Tests (new file `tests/stream-a2-till.mjs`, names prefixed `[TILL]`)
cash POS sale enters drawer once; UPI sale does not; room-service to folio adds a folio charge and no cash; same-day void nets to zero; POS cash rejected after shift close; replay with the same key changes nothing; open POS check blocks DAY_CLOSE; `drawer === shiftExpected().expected` on BASE and on FY26 23 Oct; FY26 closing records equal a full recompute.

## Done
`npx tsc --noEmit` clean, `npm test` all pass, `npm run build` OK (in your worktree's `03 Prototype`); browser check of cashier shift, day closing and POS at 1366 px and 360 px (no console errors, no sideways scroll). Commit on your branch only (message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`); do not push or merge. Final answer ≤ 25 lines: what changed, figures old → new, test counts, files touched, shared changes needed, gaps.
