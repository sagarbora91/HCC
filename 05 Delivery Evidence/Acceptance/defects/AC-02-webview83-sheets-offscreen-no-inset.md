# AC-02 · Sheets/modals invisible on WebView < 87 (CSS `inset`)  — S2 (S1 for devices with WebView 83–86)

- Found: 25 Oct 2026, v7 APK, AVD `codex_apk_review` (WebView 83.0.4103.120), phone layout.
- Steps: sign in as Owner → tap DEMO (or any action that opens a sheet, e.g. Record payment).
- Expected: sheet slides over the page.
- Actual: nothing visible; the `.modal-overlay` (position:fixed) sits at top=1449 px (below the viewport) because `inset:0` is unsupported (Chrome 87+). Page scrollHeight grows to 1448 px.
- Cause: `03 Prototype\src\styles.css` `.modal-overlay{…inset:0…}`; same shorthand in `search.css:2`, `native.css:2` (exit prompt), `home.css:46`, `mobile-ops.css:25`, `reservations.css:73`, `styles.css` sidebar-overlay, `mobile.css:21`.
- Fix: write `top:0;right:0;bottom:0;left:0` instead of `inset:0` (or raise the supported WebView floor to ≥ 87). Not a problem on the Tab S7 / API 35 emulator.
- Evidence: `AC-02-webview83-sheets-offscreen-no-inset.png`; AC-01 re-test in `..\D5_AC.md`.
