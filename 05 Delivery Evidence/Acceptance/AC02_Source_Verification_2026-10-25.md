# AC-02 saved checkpoint — 25 October 2026

User requested stopping and saving because usage is 2%. Resume from this checkpoint.

## Saved implementation
Expanded all 9 CSS inset declarations into explicit top/right/bottom/left properties across 7 source files under 03 Prototype: styles.css and styles/{home,mobile-ops,mobile,native,reservations,search}. Preserves sidebar right:auto and avatar 6px offsets. Intended to fix invisible sheets on Android WebView 83.

Added tests/stream-inset-compat.mjs (automatically discovered by existing test runner) and tests/check-overlay-compat.mjs (local real-app browser geometry check).

## Verified
- npm run build passed; updated local dist/index.html exists.
- Focused stream-inset-compat test passed: no inset shorthand, explicit overlay anchors.
- Full suite was NOT rerun for AC-02. Previous 501/501 result belongs to earlier work.

## Pending / exact resume point
Browser script FAILED at tests/check-overlay-compat.mjs line 32: locator('.modal-close').click() timed out after opening Demo controls. Investigate actual Demo dialog close selector and update harness, then rerun. Do not claim browser checks passed; no completed results.json exists.
Run from 03 Prototype with environment variables:
HCC_PLAYWRIGHT_PATH=C:/Users/Sagar/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright
HCC_CHROME_PATH=C:/Program Files/Google/Chrome/Application/chrome.exe
Command: node tests/check-overlay-compat.mjs
Script covers phone/tablet drawer, Demo, search, booking, negative control removing positional edges, and mocked-native exit prompt. Review screenshots and results in 05 Delivery Evidence/Acceptance/AC02-local after success. Consider rerunning existing tests/check-tablet-back.mjs, then git diff --check.

Real WebView 83/device acceptance remains pending. User explicitly requested NO APK rebuild. Do not rebuild/install APK or start emulator without new instruction. AC-02 is source-fixed but NOT acceptance-closed.

Preserve all unrelated local modifications (tablet/back/print/reuse maps). No commit or push performed. Update this checkpoint and HANDOFF.md when remaining verification completes.
