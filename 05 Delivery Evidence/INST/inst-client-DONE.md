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


---
# Fix round (after the Opus audit, FIX with 8 items) - 12 Nov 2026

All eight items done. Nothing run for real (ISCC compile, `-WhatIf`, in-memory test certificates and a fake store only).

| # | Item | Done |
|---|---|---|
| 1 | Name Constraints really parsed | `Hcc-ClientTrust.ps1`: DER reader (`Read-HccTlv`), `Get-HccNameConstraintInfo`, `Test-HccHostPermitted`. Requires: extension critical; permitted subtrees non-empty; every permitted DNS name a plain host (no empty, no leading dot, no wildcard, no bare top-level domain or public suffix such as `com`, `in`, `co.in`, no 1-2 letter label); every permitted IP range inside 10/8, 172.16/12, 192.168/16, 127/8, ::1/128, fc00::/7; each address family either permitted (private) or excluded as a whole; the server host of the json / typed address / kit `-ServerName` inside the permitted names or ranges (not excluded). The permitted list is shown on the confirmation page and in READ-ME and in the kit's console output. Tests: a CA built exactly as `New-HccCertificate.ps1` builds it (`Get-HccCertificatePlan` + `New-HccNameConstraintsExtension`), refusals for non-critical, empty, excluded-only, `com`/`net`/`in`, `co.in`, `0.0.0.0/0`, public IP, wide private-looking range (172.0.0.0/8), leading-dot, wildcard, and a host outside the names (name and IP); kit refuses an uncovered `-ServerName`. |
| 2 | Gating | `HccClient.iss`: `NextButtonClick(ConfirmPage)` returns False unless `InspectOk and ConfirmCheck.Checked` (a silent setup has no tick box, so `WizardSilent` is exempt there and decided in `PrepareToInstall`); `PrepareToInstall` always re-runs the checker, then requires the fingerprint when silent (empty / null `caSha256` = none; `Read-HccClientConfig` treats it so) or the tick box when not. |
| 3 | Circular fingerprint | Confirmation text and READ-ME now say: the files on the stick cannot prove they are the hotel's; compare with the fingerprint shown on the SERVER PC (last page of the HCC server setup / its install log / the kit step output), not with a file on the stick. `New-HccClientKit.ps1` prints the CA SHA-256 prominently and returns it (`Sha256`, `Fingerprint`, `Permitted`). **Dependency for lane `server`:** the server installer's finish page and install log must show the CA SHA-256 (grouped `AA:BB:...`; the kit step output already prints it), and later an Owner page; add it to the owner's paper checklist. |
| 4 | Removal bookkeeping | `{app}\ca-trusted.txt` lists `<sha256> added|already`. A different CA than a recorded `added` one: the setup offers to remove the previous (default Yes, silent Yes) and shows a failure. Uninstall offers removal only for `added` entries (default No, silent keeps), and shows the result per certificate (`removed` / `absent` / `failed: ...`), logged. |
| 5 | `.gitignore` | `**/installer/Output/` (verified with `git check-ignore`). |
| 6 | Store seam | `Add-HccTrustedRoot` / `Remove-HccTrustedRoot` take `-StoreFactory` (default the real `LocalMachine\Root`). Tests use an in-memory fake and never open the live store; before/after enumeration stays as a read-only backstop. The old live-store `-WhatIf` check was removed. New tests: add once / `already` / refusals and `-WhatIf` make no call / remove only the matching certificate / `absent`. |
| 7 | File type and key | `GetCertContentType` must be `Cert` (PFX, PKCS#7, serialized refused), any `PRIVATE KEY` text refused, `HasPrivateKey` refused; also BasicConstraints critical, keyCertSign when KeyUsage is present, no SHA-1/MD5, RSA >= 2048. Tests: PFX, PEM + key, non-critical CA flag, no keyCertSign. |
| 8 | Hotel name | `Test-HccHotelName` (1-100 characters, no `\p{Cc}\p{Cf}\p{Zl}\p{Zp}`), used by the kit and by the client; an invalid name from the json is shown as "(name not shown: not valid)" and does not refuse the setup. Tests for 101 chars, U+2028, BEL, U+200B, blank. |

## Trial-machine checklist (can only be proven by running the setup on the disposable machine)
1. `/VERYSILENT` with a kit whose `hcc-client.json` has `"caSha256": ""` (and with `null`, and with the property removed): must stop with a non-zero exit code, a message naming the missing fingerprint, **nothing trusted, no icon, no files** (the silent / simulated-Next path was reasoned from Inno's documentation, never run).
2. `/VERYSILENT` with a correct kit: trusts, icons, exit 0; and with a fingerprint that differs: stops, nothing changed.
3. Interactive: the Next button on the confirmation page stays disabled until the box is ticked; with a failing check the page says STOP and Next stays disabled; Back to the file page works.
4. Kit files missing next to the setup: the address and file pages appear; the typed address is checked; the fingerprint line says nothing was compared.
5. Real store write: certificate appears once in Local Machine > Trusted Root; second run says `already` and `ca-trusted.txt` keeps `added`.
6. Second run with a different hotel CA kit: offers to remove the previous `added` one; Yes removes it; No keeps it.
7. Uninstall: asks only if an `added` entry exists; Yes removes and reports `removed`; No keeps it; a certificate recorded `already` (e.g. on the server PC) is never offered; `/VERYSILENT` uninstall keeps the certificate.
8. The icon opens an Edge app window with the own profile; Chrome fallback; default-browser fallback; the server check message for server off / wrong network / certificate not accepted.
9. On a PC with a Group Policy execution policy: the setup should fail closed with "could not be checked".
10. Wizard text on the confirmation page fits and is readable (memo height, long permitted list).

## Results of this round
See the last lines below (filled in after the runs).

- ISCC: `ISCC /Q /DAppVersion=0.0.1.0 /DOutputDir=<temp> HccClient.iss` exit 0; the gating text is also asserted by a test, and the test suite compiles it again (and refuses without a version).
- Full `tests\Test-HccDeployment.ps1`: Windows PowerShell 5.1 **Passed: 117 Failed: 0**; PowerShell 7 **Passed: 117 Failed: 0** (INST-CLIENT block now 25 checks).
- Still NOT verified: everything in the trial-machine checklist above (Pascal at runtime, silent path, real store writes, launcher).

---
# Second fix round (after the second Opus audit) - 12 Nov 2026

| # | Done |
|---|---|
| 1 | `HccClient.iss`: `FillConfirmPage` saves `ShownSha` / `ShownUrl`; when not silent, `PrepareToInstall` stops with "The files changed after you checked them. Nothing was installed." if the re-checked fingerprint or address differs. |
| 2 | `Remove-HccTrustedRoot`: searches read-only first; removes only a self-signed CA whose Name Constraints pass `Get-HccNameConstraintInfo`, otherwise answers `refused: not an HCC hotel CA` (never a Microsoft / public root, whatever `ca-trusted.txt` says); `ShouldProcess` is asked before the store is opened read-write. Fake-store tests: a public-style root and a leaf are refused and the store is never opened read-write. |
| 3 | Permitted DNS names are an allow-list (see Defaults used below), in the client and, through the same function, in `New-HccClientKit.ps1` (the kit refuses early and the message names the offending permitted name, tested with `sbi.co.in`). The CA built exactly as `New-HccCertificate.ps1` builds it is still accepted (tested). |
| 4 | A certificate with more than one Name Constraints extension is refused (tested with a mock certificate object, because .NET cannot build a duplicate). |
| 5 | Uninstall keeps `ca-trusted.txt` while any `added` certificate stays trusted (answer No / silent / a failed removal), and deletes it only when none remains. READ-ME says so (removal later in certlm.msc or by running the setup again). |

## Defaults used (added)
- **Private-use suffix allow-list for permitted DNS names:** a single-label name (for example `HOTELPC`, `localhost`; not a known top-level domain, not 1-2 letters) or a name with at least one label before one of: `.local`, `.lan`, `.internal`, `.home.arpa`, `.localdomain`, `.intranet`, `.corp`, `.home`. Every other dotted name is refused. **A hotel with a real DNS domain for the server (for example `hcc.hotel-example.in`) needs this list extended** (`$script:HccPrivateSuffixes` in `Hcc-ClientTrust.ps1`) or the name changed to a private one; this is an owner / keys-session decision.

## Trial-machine checklist (added)
11. Swap the `.cer` (and json) in the kit folder for another valid kit while the Ready page is open, then press Install: the setup must stop with "The files changed after you checked them", nothing trusted, no icon.
12. Uninstall with answer No (or `/VERYSILENT`): the certificate stays and `ca-trusted.txt` stays in `{autopf}\HCC Client`; reinstall then offers it for removal when a different CA kit is installed. Uninstall with Yes and a successful removal: the file is deleted.
13. A tampered `ca-trusted.txt` naming a Microsoft root: the setup / uninstall report `refused: not an HCC hotel CA` and the root stays.

## Results of the second round
- Full `tests\Test-HccDeployment.ps1`: Windows PowerShell 5.1 **Passed: 122 Failed: 0**; PowerShell 7 **Passed: 122 Failed: 0** (INST-CLIENT block 30 checks, includes ISCC compile and refusal without a version).
- Plain ISCC compile of `HccClient.iss`: exit 0. Not verified: everything in the Trial-machine checklist (items 1-13).
