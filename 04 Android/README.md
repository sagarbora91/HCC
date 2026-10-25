# Hotel Control Centre — Android debug app (Capacitor)

Packages the prototype in `..\03 Prototype` (single self-contained HTML) as a **debug APK** (`in.hcc.prototype`, name "Hotel Control Centre", version `0.1.0-proto`).
Debug build only: demo data stays on the device (IndexedDB), nothing is sent anywhere, no backend. Not a Play Store release.

## Prerequisites (this PC)
- Node 25, Android SDK `C:\Users\Sagar\AppData\Local\Android\Sdk` (platform 36), Android Studio (its JBR is Java 21).
- Java on PATH is 1.8 — too old. Set these in **every** PowerShell session:
```powershell
$env:JAVA_HOME="C:\Program Files\Android\Android Studio\jbr"; $env:Path="$env:JAVA_HOME\bin;$env:Path"
$env:ANDROID_HOME="C:\Users\Sagar\AppData\Local\Android\Sdk"; $env:Path="$env:ANDROID_HOME\platform-tools;$env:ANDROID_HOME\emulator;$env:Path"
java -version   # must show 21
```
- **Norton SSL scanning**: this PC's Norton Antivirus re-signs HTTPS traffic with its own root, which Java does not trust, so Gradle cannot download Gradle/AndroidX/Maven files ("PKIX path building failed"). Workaround used here (no system setting changed): a private copy of Java's trust store that also contains Norton's root, in `04 Android\.build\hcc-cacerts.jks`, selected only for the build session:
```powershell
$ts=(Resolve-Path ".build\hcc-cacerts.jks").Path.Replace('\','/')
$env:JAVA_TOOL_OPTIONS="-Djavax.net.ssl.trustStore=`"$ts`" -Djavax.net.ssl.trustStorePassword=changeit"
```
  (Recreate it with `keytool -importcert` of the "Norton Web/Mail Shield Root" certificate exported from the Windows certificate store if `.build` is missing. Once all dependencies are cached in `%USERPROFILE%\.gradle`, `--offline` builds work without it.)

## Build
```powershell
cd "V:\Codex\Hotel Control Centre\03 Prototype"; npm run build
cd "V:\Codex\Hotel Control Centre\04 Android"; npm install            # first time only
node scripts\copy-web.mjs; npx cap sync android                       # refuses a stale build
cd android; .\gradlew.bat assembleDebug
```
APK: `04 Android\android\app\build\outputs\apk\debug\app-debug.apk` → copy to `05 Delivery Evidence\APK\HCC-0.1.0-proto-debug.apk`. Raise `versionCode` in `android\app\build.gradle` for every build you hand out.
Icons/splash: `node scripts\make-icons.mjs` then `npx capacitor-assets generate --android` (maroon "H" on sand).

## Install
- Emulator: `emulator -avd codex_api35_google`, then `adb install -r "…\HCC-0.1.0-proto-debug.apk"` and `adb shell am start -n in.hcc.prototype/.MainActivity`.
- Galaxy Tab S7 (SM-T875, Android 13): Settings → About tablet → tap Build number 7× → Developer options → USB debugging, then `adb install -r …apk`; or copy the APK to the tablet and allow "Install unknown apps" for the file manager.

## What the app adds over the browser build (`03 Prototype\src\app\native.ts`)
Android back button (sheet → wizard step → previous page → landing → "Exit?"), maroon status bar, CSV export (saved to `Documents/HCC/…` + share sheet), printing through Android's print service (`@capgo/capacitor-printer`), falling back to sharing the printable page as an HTML file.

## Known limits
Debug build only · demo data lives per device (no sync) · no backend · Auto display = desktop on tablet-sized native screens and phone layout on phones; Display toggle allows an explicit choice.

## HCC HTML printing adaptation (25 October 2026)
`npm install` runs `scripts/patch-printer.mjs`; `scripts/copy-web.mjs` also verifies/applies it before copying a fresh web build. Printer 8.1.8 HTML/WebView jobs request ISO A4 in both callback paths; PDF/file print jobs are unchanged and the Android print dialog can still let the user choose another size. Unknown plugin versions/source layouts stop with a review error instead of silently losing the change. Re-run `node scripts/patch-printer.test.mjs` after dependency changes.

The installed printer package's LICENSE is MPL-2.0. Preserve its notices and include the adapted source/patch with applicable source-availability material when distributing the modified plugin; this note is a provenance record, not blanket dependency clearance. The reproducible patch is tracked in HCC; do not rely on an unrecorded node_modules edit. Printable HTML uses an explicit white root canvas and A4 CSS, with visible table overflow and repeating headings.

Local verification: `05 Delivery Evidence/Acceptance/Print_Tablet_Back_Verification_2026-10-25.md`. Native paper-selection behavior still requires the rebuilt APK/device check. No APK was rebuilt by this print/tablet task. Tablet/phone Auto behavior is retained.
