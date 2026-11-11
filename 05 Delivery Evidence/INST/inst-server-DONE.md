# INST lane `server` - DONE (12 Nov 2026)

Branch `claude/inst-server` (base c7438465). Executor: Sonnet 5.5 session. Nothing was installed on this PC (see "NOT verified").

## What changed (files)
| File | Change |
|---|---|
| `08 Production/deployment/windows/installer/Hcc.iss` | Rebuilt on the ETP template structure (see below). |
| `08 Production/deployment/windows/Install-HccServer.ps1` | New. The orchestrator of INST-CONTRACT section 2 (+ `-Upgrade`, `-Uninstall`, test/advanced params `-AppDirectory`, `-DataDirectory`, `-OperationsDirectory`). |
| `08 Production/deployment/windows/New-HccCertificate.ps1` | New optional parameter `-CaPfxPassword [securestring]` (replaces the two `Read-Host` prompts when given; prompts remain the default). |
| `08 Production/deployment/windows/Initialize-HccProperty.ps1` | New optional parameter `-OwnerPassword [securestring]` (replaces the two prompts when given). |
| `08 Production/deployment/windows/tests/Test-HccDeployment.ps1` | Block `# INST-SERVER ... # /INST-SERVER` appended (31 checks + 1 skip-able). |
| `08 Production/deployment/windows/INSTALL.md` | Block `<!-- INST-SERVER -->` appended (setup program notes: steps, resume, trial vs production, upgrade, uninstall). |

### Hcc.iss
- Required `/D` defines (compile error otherwise, no default version): `AppVersion`, `PublishDir`, `OpsDir`, `TemplatesDir`, `ClientSetupExe`; optional `SqlMediaDir`, `OutputDir`. Output `HCC-Server-Setup-<ver>-x64.exe`. SetupLogging, VersionInfo (3-part `AppVersion`), locked install folder (`DisableDirPage=yes`, `{autopf}\HCC`), admin, x64, `SetupMutex`.
- Downgrade refusal (ETP rule), silent first install refused (the questions are needed); silent upgrade allowed.
- Wizard (plain words): Trial/Production; hotel name; PC name + addresses (pre-filled: `GetComputerNameString`, `Get-NetIPAddress` IPv4) + port; backup folder; Owner name + password twice (server rules DEC-062 stated: 12-128 chars, not containing the name, not one repeated character); SQL (bundled / existing, pre-selected by registry detection of `SQLEXPRESS`; ETP licence wording); Production only: CA password twice + USB folder (must exist and not be on the system drive); Ready page summary (no passwords; TRIAL banner text).
- Post-install (mandatory): locks `%ProgramData%\HCC\Setup`, writes `answers.json` as UTF-8 (JSON-escaped: quotes, backslashes, control chars; Unicode kept), starts the orchestrator, shows "Step n of 12: ..." from `Setup\progress.txt` (pid/status/step/title; stall and crash detection through `OpenProcess`), deletes the answer file and the SQL media afterwards. Failure: plain message naming the log, `SETUP-INCOMPLETE.txt`, exit code **1603**; Windows restart needed: **3010** (message: restart and run the setup again). Never a silent success.
- Upgrade (service exists and last install `Done`, or no record): wizard pages skipped; `net stop HccServer`, replace files, then `Install-HccServer.ps1 -Upgrade`.
- Resume (summary status InProgress / Failed / RestartNeeded): questions asked again (passwords are never kept), orchestrator started with `-Resume`.
- Files: settings from samples `onlyifdoesntexist uninsneveruninstall`; stale `*.dll`/`*.ps1` removed before copy; SQL media: the two client MSIs always (when `SqlMediaDir` defined and not an upgrade), the big `SQLEXPR` payload only when "bundled" is chosen; `sql-media.manifest.json` is shipped from `OpsDir` into `Operations\`.
- Uninstall: `Install-HccServer.ps1 -Uninstall` (service, firewall rules, scheduled tasks); confirmation dialog and final message say that SQL Server, the databases, the backups, the keys, the certificates and the settings files are kept. Start menu: HCC (or "HCC - TRIAL (not for real guest data)"), client kit folder, install logs, uninstall.
- Finish page: shows the hotel CA SHA-256 fingerprint ("compare with the client setup"), SQL warnings, and TRIAL text.

### Install-HccServer.ps1 (orchestrator)
- Steps and order exactly as section 2: `sql, database, certificate, settings, keys, migrate, service, property, backup-tasks, egress, client-kit, health`; each recorded in `Setup\install-state.json`; `-Resume` skips finished steps; every step also detects "already done" (key, service, certificate, property) so a plain re-run is idempotent. Exit 0 / 2 / 1 as contracted. `-PlanOnly` and `-WhatIf` print the steps and change nothing (no folder created, answer file kept).
- Secrets: answer file read only after checking its folder is private; passwords held as SecureString and handed to `New-HccCertificate -CaPfxPassword` / `Initialize-HccProperty -OwnerPassword` in-process (never on a command line); every log line passes a scrubber (raw and JSON-escaped forms); answer file overwritten with zeros and deleted in `finally`, on success and failure; state/summary/progress files hold no secret. Private folders (`Setup`, `Keys`, `TrialKeyCopies`) are created with their permissions in one call (`Directory.CreateDirectory(path, DirectorySecurity)` / `FileSystemAclExtensions.Create`, owner Administrators); the installer does the same for `Setup` and `SqlSetupMedia` through a tiny temp .ps1.
- Trial: CA key in `Keys\HCC-Hotel-CA.pfx` with generated password in `Keys\HCC-Hotel-CA-password.txt`; key copies in `TrialKeyCopies` (the key scripts require a copy path); TRIAL banner + `<title>` prefix inserted into `Server\web\index.html` using only presentational HTML (no script/style, so the page's CSP hashes stay valid); trial cannot become production by this setup.
- Production: CA key `HCC-Hotel-CA-<timestamp>.pfx` and both key copies (timestamped names) go to the USB folder.
- The hotel CA is imported into this PC's Trusted Root (public certificate only) so the server PC itself and the health check trust it; a leftover CA private key in `LocalMachine\My` is removed.
- Bootstrap integration: `-LogFile` passed; `-WarningsFile` passed only if the script declares it; warnings logged as WARN and written to `Setup\warnings.txt` for the wizard; PATH refreshed from the registry after step 1.
- Upgrade: merges new keys from the samples into `appsettings.Production.json` / `hcc-ops.json` (never changes an existing value, skips `_notes`, private `*.before-upgrade` copy), stops service, `migrate`, start, refreshes the client kit, health.

## What was run (exact results)
All on this PC, light commands only. Everything below ran with mocks / stubs; nothing was installed.
- ISCC (Inno Setup 6, `E:\Tools\Inno Setup 6`): `Hcc.iss` compiles with stub inputs with and without `SqlMediaDir` (exit 0, 2.1 MB stub exe written to a temp folder); compile without `AppVersion` / `ClientSetupExe` fails with the intended message.
- `powershell -NoProfile -File tests\Test-HccDeployment.ps1` (Windows PowerShell 5.1.26100): **Passed: 133, Failed: 0** (the file had 109 passing + 1 failing existing check `A09-05 Install-HccService -WhatIf ...` before this lane; that check asserts that `Hcc.iss` still registers the Event Log source, so the rebuilt `.iss` keeps that `[Run]` line and the check passes). 26 of the checks are new (INST-SERVER block: iss layout, ISCC compile with stubs, step order, plan-only, trial/production runs, secrets scrubbing, resume, restart, failure, idempotency, upgrade, uninstall, warnings, fingerprint, private folders, real `Invoke-HccOps`).
- Parser check of `Install-HccServer.ps1` (5.1): no errors. Plan runs via `powershell -File Install-HccServer.ps1 -PlanOnly` (install, `-Upgrade`, `-Uninstall`) print the steps and create nothing.
- Not run under PowerShell 7 (only 5.1 was exercised; the code avoids 7-only syntax and has a 7 branch for the directory-with-ACL call).

## NOT verified (nothing was installed)
- The wizard was never displayed or clicked; the Pascal code compiles but was not executed (page flow, validation dialogs, progress polling through `PeekMessage`/`OpenProcess`, finish-page text, exit codes 1603 / 3010, uninstall dialogs).
- No real run of any script: SQL bootstrap, database creation, certificate creation, ACL calls (`New-HccPrivateDirectory`, `CreatePrivateFolder`), service installation (also the cert-key grant for `NT SERVICE\HccServer`, which may not resolve before the service exists - the orchestrator re-grants after creating the service), `migrate` as administrator, hotel/Owner setup over the loopback listener, scheduled tasks, firewall, Trusted Root import, health check over real HTTPS.
- The TRIAL banner was only checked as text (HTML inserted after `<body>`); it was not looked at in a browser, and its effect on the app layout (an extra row at the top) is unknown.
- `Install-HccService.ps1`'s runtime check with a self-contained publish was not run.
- Interplay with the final `Bootstrap-HccSql.ps1` / `New-HccClientKit.ps1` / `Build-Installer.ps1` of the other lanes: only the interface (parameter names) is used; the client kit and bootstrap are mocked in my tests. `WarningsFile` JSON shape is read defensively (strings or objects with message/text/warning/detail).
- The test script was run under Windows PowerShell 5.1 only.
- Orchestrator parameter `-DataDirectory` defaults to `%ProgramData%\HCC`; the tests pass a temp folder, so the "never a path under a user's TEMP" rule is a property of the default and of the Inno side (which never passes a path), not enforced by a guard.

## Defaults used
- Install folder locked (yes); settings merged on upgrade, never overwriting (yes); plain `.bak` auto-clean (no); hotel CA only (yes); uninstall keeps SQL, databases, backups, keys, certificates, settings (A21).
- Hotel address is not asked: the placeholder "(enter the address in Master configuration after signing in)" is sent; Owner display name = the sign-in name.
- Server name shown by discovery (`Hcc:Hosting:ServerName`) = the PC name.
- Trial key offline copies stay on the PC in `TrialKeyCopies` (the key scripts require a copy path); Production names are timestamped so a retry never collides.
- Health check: if the PC's name does not answer but `https://localhost:<port>/` does, this is a warning in the log, not a failure.
- Exit codes: 1603 failed, 3010 restart needed (wizard); orchestrator 0 / 1 / 2.
- A silent (`/SILENT`) first install or resume is refused (needs the wizard answers); silent upgrade is allowed.
- Fingerprint format on the finish page and in the log: upper-case hex pairs separated by colons; the summary file holds plain hex.
- `Install-HccServer.ps1` also writes `install-summary.txt` (key=value), `progress.txt`, `warnings.txt` in `Setup\` for the installer.

## Open questions
1. Should the first Owner's display name and the hotel address be asked in the wizard (now: sign-in name / placeholder)?
2. Production key copies for the CA: only the USB stick is used; the owner may want a second copy step (DEC-087) - not built.
3. The exit code 3010 for "restart needed" is conventionally "success, reboot": confirm that a distinct non-success code is wanted.
4. Visual check of the TRIAL banner on the trial machine; fallback is the tab title and the Start-menu name.

## Shared files touched
`tests/Test-HccDeployment.ps1` (block `INST-SERVER`), `INSTALL.md` (block `INST-SERVER`). Existing scripts changed only by gaining optional parameters: `New-HccCertificate.ps1` (`-CaPfxPassword`), `Initialize-HccProperty.ps1` (`-OwnerPassword`). `Hcc-Common.ps1` and `.gitignore` untouched.

## Interface notes for the other lanes
- Orchestrator calls: `Bootstrap-HccSql.ps1 -Mode -MediaDir [-LogFile] [-WarningsFile]`; `New-HccClientKit.ps1 -ServerName -Port -HotelName -CaCer -OutDir` (the `.cer` is `C:\ProgramData\HCC\certs\HCC-Hotel-CA.cer`); the client setup exe is placed in `{app}\ClientKit` by the installer.
- `/D` defines used: `AppVersion`, `PublishDir`, `OpsDir`, `TemplatesDir`, `SqlMediaDir`, `ClientSetupExe`, `OutputDir`. `OpsDir` must contain `sql-media.manifest.json` whenever `SqlMediaDir` is passed.
