# Android app: trusting the hotel server and choosing its address

## How the app picks the server (today's code)
- `03 Prototype\src\clients\sync\instance.ts` reads `localStorage["hcc-server-url"]` (function `serverBaseUrl`). Empty = same origin (a browser opened from the server's own address needs no setting). The Capacitor app runs from `https://localhost`, so **in the APK the value must be set**, e.g. `https://192.168.1.20:5443` (no trailing slash, no `/api/v1`).
- W4-12: the APK's first-start screen (and Master configuration › Server and this device) writes that key, as follows: the owner types the address or the PC name; the app calls `GET <address>/api/v1/system/discovery` (no sign-in) and only saves the value when the answer has `"product":"Hotel Control Centre"`; it shows `serverName` and `version` as confirmation. Example answer:
  `{"product":"Hotel Control Centre","serverName":"Hotel PC","version":"0.1.0","apiVersion":"v1","apiBasePath":"/api/v1"}`. It contains no secrets and no database details.
- A fixed IP (router reservation) is more reliable than a PC name on Android (`.local` names often do not resolve).

## Installing the hotel CA certificate on Android (once per device)
1. Copy `HCC-Hotel-CA.cer` (from `deployment\windows\certs` on the server PC; it is only the public certificate, safe to share) to the device (USB, e-mail to yourself, shared folder).
2. Android 13 (Galaxy Tab S7): Settings > Security and privacy > More security settings > Install from device storage > **CA certificate** > "Install anyway" > pick the file. (A screen lock PIN/pattern must be set.) Menu names vary slightly by phone.
3. Check in Chrome on the device: `https://192.168.1.20:5443/api/v1/system/health` loads without a warning.

## The APK (W4-12: done)
- The production APK (`04 Android`, build type `productionDebug`, app "HCC Server") bundles the hotel CA certificate at build time (`network_security_config.xml` + `res/raw/hcc_hotel_ca.pem`, cleartext refused), so the device-wide CA install above is only needed for Chrome. Build steps and how to hand over `HCC-Hotel-CA.cer`: `04 Android/README.md`, section "Production (server) APK".
- The APK sends its requests natively (CapacitorHttp), so the server needs **no CORS** setting; it signs in with `ReturnToken = true` and sends `Authorization: Bearer` (already accepted by `RequireHccSession`).
- First start asks for the server address and checks `/system/discovery` + `/system/health` before saving `hcc-server-url`; it can be changed later in Master configuration › Server and this device.
