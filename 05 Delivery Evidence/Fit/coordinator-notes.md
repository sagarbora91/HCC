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

## From ops (done, 7df4eb23)
- Shell bug is wider: FitPage layout="board"|"steps"|"split"|"form" puts the same class names on the page root that z-fit.css uses for component grids (page lays out as columns); the check does not catch it (measures boxes only). Every lane used default fill as workaround; integrator fixes in shell, then re-checks visually.
- New stylesheet src/styles/fit-ops.css (picked up by name order).
- Baseline should be recorded from every element, not the first visible one (36 font-changed false positives: hk/mnt filter labels 10px etc.).
- Append the 4 modal openers from FIT-fit-ops-DONE.md to tools/fit-modals.json.

## From people-a (done, 9fc55aee)
- Shell defects: (1) FitPage layout="steps" puts `fit-steps` grid class on the page root (squeezes page into 260 px column) — overridden in closing-fit.css, remove override once shell fixed; (2) `HeaderActions` never moves buttons into More (overflow goes to the left where it is not measured).
- Compliance: contract's "Filings / Licences" tabs don't exist on main; used Calendar + Dashboard.
- New hint text on Day closing (close/next-day buttons are in header, step-rail counts): flagged for review — fit-review must confirm it is layout-only copy.
- Reopened-day charge form not seen in a browser (no reopened day in FY26): integrator/review to check by test.
- stream-reopen.mjs updated to open the right step (not loosened).
- New stylesheets staff-fit.css, closing-fit.css, compliance-fit.css; 4 modal openers for fit-modals.json.

## From front-b (done, 706369f8)
- `guests` has no nav item so the check tool never visits it — add to the tool's page list (browser check missing for it).
- No CSS touched (guests.css/reservations.css are token-checked sheets); two small inline overrides; alternative z-fit.css rules are in its DONE file — integrator to move to a desktop-only sheet.
- Modal openers: `+ New reservation`, `Print in-house list`, `Walk-in`, `New guest`.
- Lock contention: ~4 h of retries for this lane.

## From revenue (done, f080c76a)
- Booking-site has only Bookings + Preview tabs (page has no content for Settings / Rooms & rates) — review to confirm nothing dropped vs main.
- Yield "publish requests" card became a sub-screen shown only while a request exists — review to confirm nothing hidden.
- Groups re-run after the fit-split fix passes; yield/channels/booking-site not re-run after; remaining font-changed on 4 sub-screens unverified vs main (integrator's full run decides).
- New sheet src/styles/fit-revenue.css; touched `channels()` helper in stream-wd5-gaps.mjs (passes tab via FitNavContext, assertions unchanged).
- A static server for its checks may still run on port 4197 (stop was denied to the agent) — coordinator to ask the user / check at integration time.
