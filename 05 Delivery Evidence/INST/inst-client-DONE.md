# INST lane `client` - DONE (12 Nov 2026)

Branch `claude/inst-client` (base c7438465). Executor: Sonnet. Nothing was installed, no setup was run, no certificate was imported into any store.

## What changed (files, all under `08 Production/deployment/windows/` unless noted)
| File | What |
|---|---|
| `installer/HccClient.iss` (new) | Inno Setup 6 client setup. Admin; own AppId and folder `{autopf}\HCC Client` (never the server's). Pages: ask server address / ask .cer only when the kit files are missing next to the setup; confirmation page (hotel, address, certificate subject, valid-until, SHA-256 fingerprint, name-constraint statement, tick box "this is my hotel's server"). Trust happens in `PrepareToInstall`, before any file is copied, so a refusal leaves nothing. Creates `{commondesktop}\HCC` and `{commonprograms}\HCC` (wscript -> launcher, icon `hcc.ico`). After install: server check (message only, never fails the setup). Uninstall removes icon and files, asks whether to remove the certificate (silent uninstall keeps it). `#error` if `/DAppVersion` is missing. Defines: `AppVersion` (required), `OutputDir` (default `installer\Output`). Output `HCC-Client-Setup-<AppVersion>.exe`. |
| `installer/Launch-HCC.vbs` (new) | Launcher: Edge `--app=<url>` (App Paths registry), then Chrome `--app`, then default browser. Own profile `%LOCALAPPDATA%\HCC\Browser` (`--user-data-dir`). Reads `server-url.txt` written by the setup; refuses a non-https or quote/space-containing address. |
| `installer/hcc.ico` (new) | Generated icon (maroon disc, "HCC"), 16/32/48/256 px. |
| `Hcc-ClientTrust.ps1` (new) | Shared logic, dot-sourceable (functions only) or run with `-Action Inspect/Trust/Untrust/Check -OutFile` (result file UTF-16 key=value so Unicode hotel names survive). Used by the .iss (compiled into the setup, extracted to `{tmp}`, installed to `{app}` for uninstall) and by the kit script. Because it sits in the Operations folder it ships with the server installer's `*.ps1` glob. |
| `New-HccClientKit.ps1` (new) | INST-CONTRACT 2 interface: `-ServerName -Port -HotelName -CaCer -OutDir` plus optional `-ClientSetupExe`. Writes `HCC-Client-Setup-<ver>.exe`, `HCC-Hotel-CA.cer` (re-exported as DER, public part), `hcc-client.json` `{serverUrl, hotelName, caSha256}` (UTF-8 no BOM), `READ-ME.txt` (with the fingerprint to compare). `-WhatIf` writes nothing. Validates everything before writing. |
| `tests/Test-HccDeployment.ps1` | Appended block `# INST-CLIENT` ... `# /INST-CLIENT` (19 checks). |
| `.gitignore` (repo root) | Appended block INST-CLIENT: `installer/Output/`. |

## Rules enforced by the code
- Refused (setup and kit): not a certificate; not a CA (BasicConstraints); not self-signed; expired / not yet valid; **no Name Constraints extension**; more than one certificate in a PEM; fingerprint different from `caSha256`; non-`https://` address; address with anything but letters, digits, `.`, `-`, `:port`, `/` (so it is safe in the launcher and command lines); `caSha256` that is not 64 hex.
- The trust step re-validates the file itself and compares with the fingerprint that was shown and ticked; only that one certificate goes to LocalMachine\Root (`already` if present: idempotent). Untrust removes only the certificate with the stored fingerprint.
- Silent install shows no confirmation, so it is allowed only when `hcc-client.json` carries `caSha256` (the checker has then compared it); otherwise it stops.
- No secret is involved anywhere (public CA certificate only); the install log holds fingerprint and result words only.
- PowerShell 5.1 and 7 compatible; verified under both.

## Defines (as agreed with lane `build`)
`/DAppVersion=x.y.z.w` (required; numeric form for the version resource) and `/DOutputDir=<folder>`. Nothing else needed. The .iss compiles `..\Hcc-ClientTrust.ps1`, `Launch-HCC.vbs`, `hcc.ico` relative to itself.

## What was run, exact results
- `ISCC /Q /DAppVersion=0.0.1.0 /DOutputDir=<temp> installer\HccClient.iss`: exit 0, `HCC-Client-Setup-0.0.1.0.exe` (2,064,444 bytes). Without `/DAppVersion`: non-zero exit (tested).
- My block alone (extracted harness): Windows PowerShell 5.1 **19 passed, 0 failed**; PowerShell 7 **19 passed, 0 failed**.
- Full `tests\Test-HccDeployment.ps1` under 5.1: see the last line of this file.
- Test certificates are made in memory (`CertificateRequest`), written as files in a temp folder, folder deleted afterwards. The Root store was listed before and after and is identical (a test asserts it). `-Action Trust -WhatIf` was run; a real add/remove was **not**.
- The 19 checks: url rules; certificate rules (DER, PEM, non-CA, no constraint, expired, junk, missing); fingerprint helpers; kit `-WhatIf` writes nothing; kit contents and json shape with a hotel name containing quotes, `&`, `;`, `%`, accented and Devanagari letters; kit idempotent; kit refusals (leaf, unconstrained, bad server name, `https://` as name, multi-line hotel name, missing setup exe, nothing written); setup check pass / fingerprint mismatch / non-https / garbled fingerprint / non-CA; command-line Inspect exit codes and result file; Trust refusals and `-WhatIf` leave the store untouched; reachability classification with mocked exceptions and a real closed local port; .iss layout; ISCC compile; ISCC refuses without a version; Root store unchanged.

## NOT verified (be exact)
- The compiled setup was **never run**: wizard pages, button states, the tick-box gating, message boxes, the uninstall prompt, `{src}` detection and silent behaviour are compiled but unexercised. The Pascal logic is untested at runtime.
- Real store writes in `Add-HccTrustedRoot` / `Remove-HccTrustedRoot` were not executed (development PC).
- The launcher `.vbs` was not run: Edge/Chrome App Paths lookup, `--app` window and profile folder are unverified on a real PC.
- The server check was only tested against a closed port, not against a real HCC server with and without the CA trusted.
- `New-HccClientKit.ps1` was not run with a certificate from the real `New-HccCertificate.ps1` (the test CA uses the same name-constraint builder but is not that script's output). Orchestrator step 11 should be proved on the disposable machine.
- Unsigned setup under SmartScreen (no code signing this wave).

## Defaults used
- A CA **without Name Constraints is refused** (conservative: it would be trusted for any website). An old pre-F-M04 CA needs a new CA and kit.
- Self-signed root required; validity dates checked.
- Install folder locked to `{autopf}\HCC Client` (no folder page); one icon each on the common desktop and Start menu (no Start-menu folder).
- Silent mode requires the fingerprint in `hcc-client.json`.
- Uninstall: silent uninstall keeps the certificate; interactive asks (default No). The per-user browser profile `%LOCALAPPDATA%\HCC\Browser` is left (per-user data stays).
- Without the kit json the person types the address; no fingerprint can then be compared and the confirmation page says so (compare with the kit's READ-ME).
- The kit script finds the client setup exe via `-ClientSetupExe`, else `HCC-Client-Setup-*.exe` in `-OutDir`, else `..\ClientSetup\` or `.\ClientSetup\`; not found = error (no kit without its setup). The server installer should place the exe in `{app}\ClientKit\` (the default OutDir) or pass `-ClientSetupExe`.
- Default OutDir of the kit script: `..\ClientKit` beside the Operations folder.

## Open questions / notes
1. Lanes `server` / `build`: where the client exe sits on the hotel PC (above); the build should pass a numeric `x.y.z.w` `/DAppVersion`.
2. Re-running the setup with a different hotel CA leaves the old CA trusted (only the recorded fingerprint is removed at uninstall). Consider removing the previous one on upgrade.
3. Open per brief: **no kiosk lock-down of Windows** in this wave.
4. The server installer's `*.ps1` glob will now also ship `Hcc-ClientTrust.ps1` to `Operations\` (needed by the kit script).

## Shared files touched
`tests/Test-HccDeployment.ps1` (block appended before the summary lines), root `.gitignore` (block appended). `Hcc-Common.ps1` and `INSTALL.md` not touched.

## Full suite result
`powershell -NoProfile -File tests\Test-HccDeployment.ps1` (Windows PowerShell 5.1), whole file: **Passed: 109  Failed: 0** (90 existing + 19 INST-CLIENT; no existing assertion changed).
