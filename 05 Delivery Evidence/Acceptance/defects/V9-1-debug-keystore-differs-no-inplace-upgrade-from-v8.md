# V9-1 · S3 (build/process) · v9 cannot be installed over v8: signing certificate differs

- Steps: emulator `codex_api35_google` holds v8 (versionCode 8, SCHEMA 7 data). `adb install -r HCC-0.1.0-proto-debug-v9.apk` (no uninstall).
- Expected: in-place upgrade, data kept.
- Actual: `INSTALL_FAILED_UPDATE_INCOMPATIBLE: Existing package in.hcc.prototype signatures do not match newer version`.
- Cause: v9 is signed by this PC's `~/.android/debug.keystore` (cert SHA-256 3c49e759...c8); the existing v8 APK (`HCC-0.1.0-proto-debug.apk`) was signed by the old PC's debug keystore (cert SHA-256 faae9739...05dc). The old keystore is not on this PC.
- Impact: any device/tablet that has a v8 (or earlier) build from the old PC must uninstall first, which deletes its data (the Tab S7 included). A real in-place upgrade could not be tested; the data upgrade was tested by exporting the v8 SCHEMA 7 state from the WebView, uninstalling, installing v9 and re-injecting that state (see APK_v9_Emulator_Retest.md).
- Suggested: copy the old PC's `debug.keystore` to this PC (or tell users once to back up/uninstall) before handing v9 to the tablet; keep one keystore from now on.
