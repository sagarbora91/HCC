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

## Production (server) APK — W4-12
A second build type, `productionDebug`, packages the **production web build** (`03 Prototype\dist\production\index.html`, talks to the hotel server) as app `in.hcc.prototype.server` ("HCC Server"), installable next to the prototype. `assembleDebug` (prototype) is unchanged; `versionCode` stays as in `build.gradle` (the lead raises it at release).
```powershell
cd "E:\Projects\HCC\Hotel Control Centre\03 Prototype"; npm run build; npm run build:production
cd "..\04 Android"; node scripts\copy-web.mjs; npx cap sync android                 # Capacitor runtime assets (once per change)
node scripts\build-production.mjs --ca "<path>\HCC-Hotel-CA.cer"                   # or $env:HCC_HOTEL_CA_CERT; add --assemble to run Gradle
cd android; .\gradlew.bat assembleProductionDebug                                  # (--offline once the Gradle cache is filled)
```
APK: `04 Android\android\app\build\outputs\apk\productionDebug\app-productionDebug.apk`. Default `--ca` path: `..\08 Production\deployment\windows\certs\HCC-Hotel-CA.cer`.
- **HTTPS only:** `android\app\src\productionDebug\res\xml\network_security_config.xml` refuses cleartext and trusts the system CAs **plus the bundled hotel CA** (`res\raw\hcc_hotel_ca.pem`, written by the script; user-installed CAs are not needed). The script accepts only a public certificate (DER `.cer` or PEM), refuses `.pfx`/private keys, prints its SHA-256 fingerprint, and the generated `res\raw` and `assets` folders are git-ignored.
- **Requests** go through Capacitor's built-in `CapacitorHttp` plugin (native, honours the security config, no CORS needed on the server); sign-in uses `returnToken` + `Authorization: Bearer` (`03 Prototype\src\clients\sync\auth.ts`, `apk.ts`).
- **First start:** server address screen (`https://…`, tested with `/api/v1/system/discovery` + `/system/health`, saved as `hcc-server-url`; later: Master configuration › Server and this device). Then an Owner / Tech admin signs in with the password and registers the device; staff then pick their name and type their PIN (DEC-062).
- **Session token** is kept in sessionStorage (sign in again after Android closes the app) because `@capacitor/preferences` is not installed; adding it here switches to Preferences automatically.

**Owner: export the hotel CA certificate (once, on the server PC)** — `deployment\windows\New-HccCertificate.ps1` (P7, INSTALL.md §4) creates the CA and the server certificate and writes `deployment\windows\certs\HCC-Hotel-CA.cer` (public part only; the CA key stays in the PC's store, or in the safe as `.pfx` with `-ExportCaPfx` — never give the `.pfx` to the app build). The server certificate must name the address the tablets use (`-IpAddress 192.168.1.20` and/or `-DnsName hcc-server.local`). Hand `HCC-Hotel-CA.cer` (USB/e-mail) to whoever builds the APK, compare the SHA-256 fingerprint the build prints with `certutil -hashfile HCC-Hotel-CA.cer SHA256` on the server PC, and rebuild the APK if the CA is ever replaced. A hotel that buys a certificate from a public CA can pass that CA's root instead.

**Dev / emulator flow (self-signed):** on the dev PC (elevated PowerShell) run `"08 Production\deployment\windows\New-HccCertificate.ps1" -IpAddress 10.0.2.2,<this PC's LAN IP> -OutputDirectory <folder outside the repo>`, set `Hcc__Hosting__CertificateThumbprint=<printed thumbprint>` for the dev server (Kestrel then serves `https://*:5443`), build with `--ca <folder>\HCC-Hotel-CA.cer`, and enter `https://10.0.2.2:5443` in the emulator (10.0.2.2 = the host PC). Remove the dev CA from `LocalMachine\My` afterwards. For a compile-only check any CA works, e.g. `openssl req -x509 -newkey rsa:2048 -nodes -keyout ca.key -out ca.pem -days 30 -subj "/CN=HCC Dev CA" -addext basicConstraints=critical,CA:TRUE` in a temp folder (never commit the key). Script checks: `node scripts\build-production.test.mjs`.

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
