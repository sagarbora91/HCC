# Android app: trusting the hotel server and choosing its address

## How the app picks the server (today's code)
- `03 Prototype\src\clients\react.tsx` reads `localStorage["hcc-server-url"]` (function `serverBaseUrl`). Empty = same origin (a browser opened from the server's own address needs no setting). The Capacitor app runs from `https://localhost`, so **in the APK the value must be set**, e.g. `https://192.168.1.20:5443` (no trailing slash, no `/api/v1`).
- Nothing in the prototype writes that key yet (the Settings field is a later prototype/P6 task, not P7). Until then, set it once per tablet from a debugging session, or as the first thing the Settings screen does: `localStorage.setItem('hcc-server-url','https://192.168.1.20:5443')`.
- Recommended Settings behaviour (for whoever builds the screen): the owner types the address or the PC name; the app calls `GET <address>/api/v1/system/discovery` (no sign-in) and only saves the value when the answer has `"product":"Hotel Control Centre"`; it shows `serverName` and `version` as confirmation. Example answer:
  `{"product":"Hotel Control Centre","serverName":"Hotel PC","version":"0.1.0","apiVersion":"v1","apiBasePath":"/api/v1"}`. It contains no secrets and no database details.
- A fixed IP (router reservation) is more reliable than a PC name on Android (`.local` names often do not resolve).

## Installing the hotel CA certificate on Android (once per device)
1. Copy `HCC-Hotel-CA.cer` (from `deployment\windows\certs` on the server PC; it is only the public certificate, safe to share) to the device (USB, e-mail to yourself, shared folder).
2. Android 13 (Galaxy Tab S7): Settings > Security and privacy > More security settings > Install from device storage > **CA certificate** > "Install anyway" > pick the file. (A screen lock PIN/pattern must be set.) Menu names vary slightly by phone.
3. Check in Chrome on the device: `https://192.168.1.20:5443/api/v1/system/health` loads without a warning.

## Known requirement for the APK itself (not done in P7; owner decision to schedule)
Chrome uses the certificate store you just changed, but **apps trust user-installed CAs only if the app opts in**. Before the APK can talk to the server over HTTPS, `04 Android` needs:
1. `android\app\src\main\res\xml\network_security_config.xml` with `<base-config><trust-anchors><certificates src="system"/><certificates src="user"/></trust-anchors></base-config>`, referenced from `AndroidManifest.xml` as `android:networkSecurityConfig="@xml/network_security_config"` (alternatively pin the hotel CA by shipping it in `res/raw` and `src="@raw/hcc_hotel_ca"`, which does not depend on each device's install step).
2. `server.androidScheme` stays `https` and `allowMixedContent` stays `false` (no plain HTTP).
3. The server must allow the app's origin `https://localhost` (CORS) and the app must use token sign-in (`SignInRequest.ReturnToken = true`, header `Authorization`) because the session cookie is not sent cross-origin. Both are server/P1/P6 follow-ups; list them in the Wave 1 integration notes.
Until those land, use the web app in Chrome on tablets/phones (same server, nothing to configure after the CA is installed).
