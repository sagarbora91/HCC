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
cd "V:\Codex\Hotel Control Centre\04 Android"; npm ci                 # before EVERY build and after every checkout (a stale node_modules silently drops @capacitor/preferences at cap sync)
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
- **Session token** is kept in Capacitor Preferences (`@capacitor/preferences`, app-private SharedPreferences) so staff stay signed in after Android closes the app, until the server session expires; it is dropped on sign-out, on a 401 and on a server-address change (which also forgets the device registration). Only if the plugin is missing does it fall back to sessionStorage.
- **Stored secrets (security review 2026-10-26, F-M07 / F-H01 / F-L09):** the session token, the device token and the read cache's resume key are sealed with AES-256-GCM before they reach Preferences, using a non-extractable WebCrypto key kept in the web view's IndexedDB (`hcc-keys`). The offline read cache is encrypted and shown only to a signed-in user (or after re-entering the PIN / password while the server is unreachable); it is deleted on sign-out and on idle / absolute session expiry. The staff roster on the tablet holds display names and opaque ids only. Limit: script running inside the app can still use the wrapping key; Android Keystore-backed storage (EncryptedSharedPreferences) needs a native plugin and is not added. Hand out only the **release** APK below, which is not debuggable and excluded from backups, so these files cannot be pulled with `adb run-as` / `adb backup`.

## Production release APK — security review F-H02
Build type `productionRelease` (`./gradlew assembleProductionRelease`): the APK that goes to the hotel's tablets. `productionDebug` stays for development only.
- app id **`in.hcc.server`** (installs next to the prototype and the debug server build), name "HCC Server", same web build, hotel CA and HTTPS-only network config as `productionDebug` (`src/productionDebug/res`, `assets`);
- **not debuggable** (no `adb run-as`, no `chrome://inspect`: Capacitor turns WebView debugging off in non-debuggable builds and `MainActivity` enforces it);
- **`allowBackup="false"`** plus `hcc_data_extraction_rules.xml` / `hcc_full_backup_rules.xml` excluding every domain (no cloud backup, no device-to-device transfer) — `src/productionRelease/`;
- **release-signed** with the hotel's own keystore. Gradle reads the signing values from environment variables or from `04 Android\android\keystore.properties` (git-ignored); the keystore path must be **absolute and outside the repository** — the build refuses a missing, unsigned or in-repo keystore (`checkHccReleaseSigning`). Never commit a keystore (`*.jks`, `*.keystore`, `*.p12` are git-ignored).
- R8 / minify is off for now (Capacitor plugins use reflection; turn it on only after a device test with keep rules).

**Owner: create the release keystore (once) and keep it safe** — on the PC that builds the APK, in a folder outside the repo (e.g. `D:\HCC-Keys`), with JDK 21's `keytool`:
```powershell
& "C:\Program Files\Microsoft\jdk-21.0.12.101-hotspot\bin\keytool.exe" -genkeypair -keystore D:\HCC-Keys\hcc-release.jks -storetype PKCS12 -alias hcc -keyalg RSA -keysize 3072 -validity 10000 -dname "CN=Hotel Control Centre, O=<hotel name>, C=IN"
```
Back up `hcc-release.jks` and its password (e.g. USB in the safe + password with the owner): every later APK update must be signed with the **same** key, or tablets must uninstall (losing local data) before installing. Then build:
```powershell
$env:HCC_RELEASE_STORE_FILE="D:\HCC-Keys\hcc-release.jks"; $env:HCC_RELEASE_STORE_PASSWORD="<password>"; $env:HCC_RELEASE_KEY_ALIAS="hcc"   # HCC_RELEASE_KEY_PASSWORD if it differs
cd "E:\Projects\HCC\Hotel Control Centre\03 Prototype"; npm run build; npm run build:production
cd "..\04 Android"; node scripts\copy-web.mjs; npx cap sync android
node scripts\build-production.mjs --release --ca "<path>\HCC-Hotel-CA.cer" --assemble      # checks the config, builds, verifies the APK
```
(or put `storeFile=D:/HCC-Keys/hcc-release.jks`, `storePassword=…`, `keyAlias=hcc`, `keyPassword=…` in `android\keystore.properties` instead of the variables). APK: `04 Android\android\app\build\outputs\apk\productionRelease\app-productionRelease.apk`. The script runs `aapt` (ANDROID_HOME) and fails unless the APK is `in.hcc.server`, not debuggable, `allowBackup=false` and cleartext-free; re-check any APK with `node scripts\build-production.mjs --verify <apk>`, and the signer with `apksigner verify --print-certs <apk>`. On a tablet, `adb shell run-as in.hcc.server` must fail. Raise `versionCode` for every APK you hand out.

**Owner: export the hotel CA certificate (once, on the server PC)** — `deployment\windows\New-HccCertificate.ps1` (P7, INSTALL.md §4) creates the CA and the server certificate and writes `deployment\windows\certs\HCC-Hotel-CA.cer` (public part only; the CA key stays in the PC's store, or in the safe as `.pfx` with `-ExportCaPfx` — never give the `.pfx` to the app build). The server certificate must name the address the tablets use (`-IpAddress 192.168.1.20` and/or `-DnsName hcc-server.local`). Hand `HCC-Hotel-CA.cer` (USB/e-mail) to whoever builds the APK, compare the SHA-256 fingerprint the build prints with `certutil -hashfile HCC-Hotel-CA.cer SHA256` on the server PC, and rebuild the APK if the CA is ever replaced. A hotel that buys a certificate from a public CA can pass that CA's root instead.

**Dev / emulator flow (self-signed):** on the dev PC (elevated PowerShell) run `"08 Production\deployment\windows\New-HccCertificate.ps1" -IpAddress 10.0.2.2,<this PC's LAN IP> -OutputDirectory <folder outside the repo>`, set `Hcc__Hosting__CertificateThumbprint=<printed thumbprint>` for the dev server (Kestrel then serves `https://*:5443`), build with `--ca <folder>\HCC-Hotel-CA.cer`, and enter `https://10.0.2.2:5443` in the emulator (10.0.2.2 = the host PC). Remove the dev CA from `LocalMachine\My` afterwards. For a compile-only check any CA works, e.g. `openssl req -x509 -newkey rsa:2048 -nodes -keyout ca.key -out ca.pem -days 30 -subj "/CN=HCC Dev CA" -addext basicConstraints=critical,CA:TRUE` in a temp folder (never commit the key). Script checks: `node scripts\build-production.test.mjs`.

## Install
- Emulator: `emulator -avd codex_api35_google`, then `adb install -r "…\HCC-0.1.0-proto-debug.apk"` and `adb shell am start -n in.hcc.prototype/.MainActivity`.
- Galaxy Tab S7 (SM-T875, Android 13): Settings → About tablet → tap Build number 7× → Developer options → USB debugging, then `adb install -r …apk`; or copy the APK to the tablet and allow "Install unknown apps" for the file manager.

## What the app adds over the browser build (`03 Prototype\src\app\native.ts`)
Android back button (sheet → wizard step → previous page → landing → "Exit?"), maroon status bar, CSV / Excel export (written to the app's private cache `exports/…` + share sheet, never to public storage; the FileProvider shares only that folder, F-L10 / A10-01; every sign-out, session expiry and offline-copy wipe deletes the folder; files an older build wrote to `Documents/HCC/` are not touched and must be deleted by hand), printing through Android's print service (`@capgo/capacitor-printer`), falling back to sharing the printable page as an HTML file.

## Known limits
Debug build only · demo data lives per device (no sync) · no backend · Auto display = desktop on tablet-sized native screens and phone layout on phones; Display toggle allows an explicit choice.

## HCC HTML printing adaptation (25 October 2026)
`npm install` runs `scripts/patch-printer.mjs`; `scripts/copy-web.mjs` also verifies/applies it before copying a fresh web build. Printer 8.1.8 HTML/WebView jobs request ISO A4 in both callback paths; PDF/file print jobs are unchanged and the Android print dialog can still let the user choose another size. Unknown plugin versions/source layouts stop with a review error instead of silently losing the change. Re-run `node scripts/patch-printer.test.mjs` after dependency changes.

The installed printer package's LICENSE is MPL-2.0. Preserve its notices and include the adapted source/patch with applicable source-availability material when distributing the modified plugin; this note is a provenance record, not blanket dependency clearance. The reproducible patch is tracked in HCC; do not rely on an unrecorded node_modules edit. Printable HTML uses an explicit white root canvas and A4 CSS, with visible table overflow and repeating headings.

Local verification: `05 Delivery Evidence/Acceptance/Print_Tablet_Back_Verification_2026-10-25.md`. Native paper-selection behavior still requires the rebuilt APK/device check. No APK was rebuilt by this print/tablet task. Tablet/phone Auto behavior is retained.
