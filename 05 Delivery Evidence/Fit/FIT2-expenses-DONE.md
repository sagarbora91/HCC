# FIT-2 lane expenses

Files: `03 Prototype/src/features/expenses/Expenses.tsx`, `ExpenseModal.tsx`, `src/styles/fit2-expenses.css` (new), tests `stream-fit-expenses.mjs` (+ `fit2-expenses-entry.tsx`), `stream-fit-cash-b.mjs` (status test now reads the select).

## Register (page `expenses`, sub-screens unchanged)
- The two tab rows are one: the status filter is a `Status` select (`All (n)` ... `Void (n)`, real counts) inside the single filter row with Mode / Month / Search / Lists & photos.
- `StatStrip compact collapseOnTablet` (figures on one line, toggle to expand).
- The register card takes the remaining height; the table scrolls inside itself.
- Approval requests: `SplitView` (list-only until asked). Button "Approval requests (N pending)" in the filter row opens the pane beside the register; "Back to list" closes it. Approve/reject logic untouched.
- Other sub-screens (recurring, petty cash, budgets, Udhaar, vendors, month) are lists inside one ScrollPanel, unchanged.

## Add expense dialog (task)
`FormSections` with rail: "What was bought" (vendor, category, funding, amount, description), "Date and tax" (date, GST, ITC, purchase fields, bill number), "Photo and notes". Rail rows show the real form values (vendor, category, funding, amount, description; date, GST, ITC, bill no., duplicate count, tax-lock; photo, notes). Submit / Save draft / Cancel and any error stay in the action bar. `required` removed from the amount input (the same check runs in `problem()`), so a hidden section cannot block submit silently. `ExpenseSccFields` (used by the phone sheet) is now composed of the three exported parts, same markup. The dialog is widened to 900 px through `.modal.fit-modal:has(.exp-add-task)`.

## Not done / notes
- No new `subscreens.ts` lines (no new sub-screens).
- Checks: see the lane report (tsc / npm test / build / fit-screen-check were queued behind the shared lock).

## Checks (2 Nov 2026)
tsc clean; npm test 2352/2352; npm run build OK; `node tools/fit-screen-check.mjs --pages expenses` PASS, 0 failing of 948 (no page scroll, fonts equal to the baseline) - report fit-check-20261102-0625.md.
