# Print, tablet and Back-button local verification

25 October 2026. Source work and local QA complete. No APK rebuilt or installed, no emulator started, no production backend constructed by this task.

## T-03 print layout
- Investigated the physical screenshot: paper selector showed Letter and the lower unused report area was cream. The standalone print document only overrode body background; application :root background remained sand. Printer 8.1.8 HTML jobs used empty PrintAttributes without a paper-size preference.
- Applied white html/body canvas, A4/14mm CSS, visible document/table overflow and repeating table headers. Maintained the fictional-data notice and watermark.
- Version-checked `04 Android/scripts/patch-printer.mjs` changes only two HTML/WebView print paths to ISO_A4. Runs after install and during copy-web; changed upstream versions/source require review. Bundled LICENSE observed: MPL-2.0. No PDF/file-print defaults changed. Printer-patch assertions: 5 passed.
- `tests/check-print-layout.mjs` rendered the actual ReportPrintModal with fictional FY26 data using bundled Playwright and installed Chrome. Computed root/body backgrounds were white, no visible action buttons or horizontal document overflow.
- Generated and rendered samples in `print-layout/`: short report **1 A4 page**, long report **4 A4 pages**, **all 62 dated rows** recovered from PDF text. Every page nonblank; repeated headings and final footer retained. All five PDF page images were visually inspected; no lower cream filler or clipped table rows observed. PDF results: `print-layout/pdf-results.json`; browser layout metrics: `layout-results.json`.
- This proves local Chromium rendering, not Android PrintSpooler behavior. The OS/user can choose a different paper size. Confirm A4 selection and layout on the next rebuilt APK before closing physical-device T-03.

## T-01/T-02 tablet usability
- New `z-tablet.css` applies larger controls only on coarse-pointer screens at least 600 CSS px wide. Mouse/desktop density and the selected phone/tablet display policy are retained.
- Compact touch-tablet header; duplicated demo/display/user/sign-out controls remain available from the drawer. Demo and display tools collapse in the footer; Sign out remains directly accessible. Drawer navigation has a minimum flex height and footer scrolling bound.
- Browser QA at **673×1006 portrait** and **1078×601 landscape**, touch enabled with a mocked native bridge: header heights **85 / 65 px**, versus the recorded physical portrait problem around 248 px. Navigation list heights **754 / 349 px**, versus the recorded physical landscape list around 254 px. These comparisons use local viewport emulation, not fresh physical measurements.
- Visible header controls and sampled Reservations buttons/inputs/selects all at least **48 CSS px** high. Expanded Front Desk items reachable. One Show more button initially measured 44 px; tablet override corrected and the check rerun successfully. This is sampled screen coverage, not a claim that every control in all 26 modules has been measured.
- Screenshots and exact measurements: `tablet-local/`, including portrait home, expanded landscape drawer and reservations. These remain local-browser evidence; real finger comfort and rotation need tablet confirmation.

## T-05 Back-button reliability
- The lead's Session 10 fix was already present: history-entry idx and a 400ms fallback when history.back produces no popstate. Kept that work.
- Added cancellation of pending fallback on navigation, modal/menu/search opening, popstate, another Back press, scenario load, sign-out and unmount. Exhaustion recovery resets both React depth and the stored history idx to zero, avoiding a stale index being reused on the next navigation.
- `tests/check-tablet-back.mjs` drives the real compiled app with a mocked Capacitor App listener. Verified drawer dismissal, two-page history unwinding, exit confirmation at root, no-popstate exhaustion recovery, stored idx reset, and a new navigation remaining intact after an earlier Back fallback. No page errors.
- Test simulates failure conditions deliberately; it does not reproduce Android share/print/rotation lifecycle or establish the exact cause of the original intermittent physical incident. Recheck that long-session sequence on the tablet later.

## Checks and source state
- Typecheck passed; full regression **501/501 passed** on the print/tablet/back work before the final stored-idx reset and 48px specificity refinement. Final local-browser checks passed after those refinements; no claim of a second full-suite run. Final typecheck/build results are recorded in the handoff.
- Full log: `05 Delivery Evidence/Assembly/tablet-print-back-tests-2026-10-25.log`; final browser log: `tablet-browser-checks-2026-10-25.log`. `tablet-local/results.json` contains final measurements/results.
- Source whitespace checks passed. No donor business code copied. ETP/SCC implementation mapping is a separate read-only artifact in `02 Planning/HCC_FirstParty_Implementation_Map_2026-10-25.md` with a 49-file hash manifest.
- Existing APK v7 and the lead's earlier device evidence are separate from this source work. AC-02 old-WebView inset fallback and remaining W0-D acceptance are still open; this report does not silently close them.

## Reproduce local checks
From `03 Prototype`, build, set `HCC_PLAYWRIGHT_PATH` to an existing Playwright installation and `HCC_CHROME_PATH` to Chrome, then run `node tests/check-print-layout.mjs` and `node tests/check-tablet-back.mjs`. These scripts create isolated local browser sessions and fictional data; no new application package is needed. Run `node scripts/patch-printer.test.mjs` from `04 Android`. Native APK build/testing remains a separate step.
