# FIT coordinator notes (for the integrator)

- From admin-a: `FitPage layout="split"` and `SplitView` share the CSS class `fit-split` (breaks page header/toolbar) — shell bug, fix in z-fit.css/fit.tsx.
- From admin-a: tests/stream-fit.mjs harness gives no nav context so `data-fit-page` is missing (arrivals fails too) and the phone-files test compares against the old FIT_BASE — set FIT_BASE to the merged pre-lane head 3dac5afb and fix the harness.
- From shell DONE: apply the three check-script edits (check-print-layout uid, check-tablet-back, check-overlay-compat .m-icon) listed under "Integration needed".
- Append every lane's modal openers to tools/fit-modals.json (admin-a: settings `Change…`).
- admin-a open item: a master with very many rows scrolls the whole record pane.

## From people-b (done, 0cc7e249)
- Same fit-split class collision confirmed (Reports uses default fill as workaround). The Component Guide's split example hits it in every lane — fix in shell CSS/fit.tsx.
- Baseline artifacts: re-record `select`/`label` inside `.content`; allow the existing 10 px `td.muted` (Opening balances).
- Modal openers to append: accounting voucher link, New voucher, Add ledger, Reports Print.
- 6 failing `[FIT]` tests in stream-fit.mjs fail on claude/fit too (harness FitNavContext, FIT_BASE).
