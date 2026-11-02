# FIT2 closing lane - DONE (2 Nov 2026, branch claude/fit2-closing)

Rule: a task never scrolls; sub-screens fit; a fixed rail shows each step's result (contract section 12). Layout/summary only; no logic, no phone code, no font change.

## Day closing (closing, 7 steps)
Rail summary per step (read-only from existing state): Preflight "N to resolve - titles" / All clear; Post room nights "n of n rooms posted"; Cash shift "Counted X - exact/over/short Y" / "awaiting approval" / "Not counted - expected X"; Close business date "Blocked by ..." / Ready / closed; Closing summary "Net receipts X (preview)"; Open next day "Opens date - float X"; Reopen "Nothing reopened / N corrections waiting / date reopened". Old one-word step notes dropped (now in the summaries).

## Cashier shift (cashier)
Was two columns with four scroll panels; now layout=steps, four sub-screens: Expected cash, Count & result, Cash movements (list scrolls in its panel), Cash out. Rail: status + expected + float; counted/difference/awaiting; movement count + net; pending/records today. Ids in subscreens.ts under `// FIT2-CLOSING`. Header actions unchanged. Style: src/styles/fit2-closing.css (old cashier.css left untouched).
Not moved: the count form's submit stays in its step (sticky bar), because it is bound to the form inputs and validation.

## Handover run
Checklists tab StepFlow: each run row shows pct, done/total, mandatory x/y, overdue or "Submitted hh:mm". Dashboard StatStrip gets collapseOnTablet.

## Checks
tsc OK; npm test 2351/2351 (new tests/stream-fit2-closing.mjs, updated cash-a and ops assertions); build OK; fit-screen-check --pages closing,cashier,handover, 5 roles, tab-land + lap-1366: PASS 0 of 288 failing (fonts, no page scroll). Text-scale 1.3 pass not run. Shell defects: none found.
