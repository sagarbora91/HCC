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
4. What this certificate can and cannot do (F-M04): the hotel CA carries a critical **name constraint**: it is valid only for the HCC server's own names and addresses (those given to `New-HccCertificate.ps1`, plus `localhost`/`127.0.0.1` and any `-PermittedNetwork`). A certificate it signed for any other site is rejected by Chrome and Windows, so installing it does not let anyone impersonate other websites there (some other Android apps may not check name constraints: install the CA only on the hotel's own devices). Its private key is not on the server PC (it is in the safe, `HCC-Hotel-CA.pfx`).
5. The **server** certificate is short-lived (397 days) and is renewed on the server PC once a year (INSTALL.md section 4b). The devices and the APK need nothing for that: they trust the CA, which stays the same for 10 years. If the server's LAN address changes outside the CA's permitted addresses, a new CA (and a re-install of `HCC-Hotel-CA.cer` plus an APK rebuild) is needed; give `-PermittedNetwork 192.168.1.0/24` at the start to avoid that.

## The APK (W4-12: done)
- The production APK (`04 Android`, build type `productionDebug`, app "HCC Server") bundles the hotel CA certificate at build time (`network_security_config.xml` + `res/raw/hcc_hotel_ca.pem`, cleartext refused), so the device-wide CA install above is only needed for Chrome. Build steps and how to hand over `HCC-Hotel-CA.cer`: `04 Android/README.md`, section "Production (server) APK".
- The APK sends its requests natively (CapacitorHttp), so the server needs **no CORS** setting; it signs in with `ReturnToken = true` and sends `Authorization: Bearer` (already accepted by `RequireHccSession`).
- First start asks for the server address and checks `/system/discovery` + `/system/health` before saving `hcc-server-url`; it can be changed later in Master configuration › Server and this device.
