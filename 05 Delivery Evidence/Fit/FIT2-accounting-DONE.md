# FIT2-accounting-DONE (wave FIT-2, lane accounting, Sonnet)

## What changed
- `03 Prototype/src/features/accounting/Accounting.tsx`: the 4-figure StatStrip is now `compact collapseOnTablet page="accounting"` (one line per figure; starts compact at <= 1180 px, expandable, choice remembered per page). Nothing else in the page changed.
- The 21 views remain `FitTabs` with unchanged ids; the shell's FitTabs now moves what does not fit into **More** (no tab cut off, selected tab stays in the bar). The Vouchers table and every other view keep the existing single `ScrollPanel`, so the table gets all remaining height and scrolls in its own panel.
- New test `03 Prototype/tests/stream-fit2-accounting.mjs` (26 assertions: compact/collapseOnTablet, 21 distinct views all rendered, no own tab scroller, no font-size).
- No new sub-screens (accounting is a tabbed list page, not a task), so no `// FIT2-ACCOUNTING` lines were needed in subscreens.ts; no new stylesheet. No money logic, mobile code or font sizes touched.

## Rail rows
None: accounting has no StepFlow/FormSections; the stat strip is the always-visible result line.

## Checks
See the lane report (tsc, npm test, build, fit-screen-check --pages accounting at tab-land and lap-1366).
