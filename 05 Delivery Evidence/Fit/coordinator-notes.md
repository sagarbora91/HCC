# FIT coordinator notes (for the integrator)

- From admin-a: `FitPage layout="split"` and `SplitView` share the CSS class `fit-split` (breaks page header/toolbar) — shell bug, fix in z-fit.css/fit.tsx.
- From admin-a: tests/stream-fit.mjs harness gives no nav context so `data-fit-page` is missing (arrivals fails too) and the phone-files test compares against the old FIT_BASE — set FIT_BASE to the merged pre-lane head 3dac5afb and fix the harness.
- From shell DONE: apply the three check-script edits (check-print-layout uid, check-tablet-back, check-overlay-compat .m-icon) listed under "Integration needed".
- Append every lane's modal openers to tools/fit-modals.json (admin-a: settings `Change…`).
- admin-a open item: a master with very many rows scrolls the whole record pane.
