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

---

# Fix round (12 Nov 2026) - answers to the Opus audit (inst-server-AUDIT.md, items 1-19, notes 20-26)

Merged `origin/claude/inst` first (real `Bootstrap-HccSql.ps1`, `New-HccClientKit.ps1`, `Hcc-ClientTrust.ps1`, `HccClient.iss`, manifest); the test file keeps the marked blocks SQL, CLIENT, then SERVER (mine last, before the summary lines).

## Step order (changed on the orchestrator's decision; contract section 2 and INSTALL.md need the same)
1 sql, 2 **service-register** (`Install-HccService.ps1`, creates the service stopped, so `NT SERVICE\HccServer` exists), 3 database, 4 certificate, 5 settings, 6 keys, 7 migrate, 8 **service-start** (certificate-key grant, FAILS the install if it fails; then start), 9 property, 10 backup-tasks, 11 egress, 12 client-kit, 13 health. `Install-HccService.ps1` needed no change (it never starts the service). `INSTALL.md` (my block) says in one sentence that the service must be registered before the database step.

## Items
1. Order above; `Step-Database` refuses without the service; test asserts the call order service < database < migrate < start (and that the database step throws without the service).
2. `Assert-HccSettingsAcl` after service-register, settings and keys (SYSTEM, Administrators and the service only; same rule as the server's start-up check); no reliance on the SQL-media folder's lock. Test with the failing ACL and with a real temp file (protected ACL passes, one extra account is reported).
3. `Set-HccTrialPage`: only the first `<title>` and the first `<body>` that lie before the first `<script`; refuses any other shape; checks the result with `Test-HccPageCsp` (hashes computed like `WebAppHosting.cs`) before writing; Trial health step re-checks the served page's inline script hash against its CSP meta. Fixtures: `<title>`/`<body>` inside the script, no title, body after script, no CSP meta, stale hash; the old blind replace is shown to break the page.
4/19. `CreatePrivateFolder` (installer) and `Repair-HccPrivateDirectory` (orchestrator): refuse links, set owner Administrators (`icacls /setowner`), REPLACE the DACL (new `DirectorySecurity` + `Set-Acl`), never merge. `DataDir` is now created from the installer at ssInstall (not by `[Dirs]`). The answer file is emptied and deleted before it is written fresh (installer), and the orchestrator checks folder AND file (owner, link, no other account) before reading. Real temp folder/file checks run without mocks.
5. Answer-file parse error logs a fixed sentence only (test: marker password not in log).
6. Stray `Setup\answers.json` is removed at the start of upgrade and uninstall (after the folder check); early returns are inside the `try`. Installer `RemoveAnswerFile` empties before it deletes.
7. Upgrade failure writes `UpgradeFailed` with the current log and the backup folder; the wizard treats `UpgradeFailed` as an upgrade and says "HCC could not be updated and is STOPPED ... backup folder ... run this setup again".
8. Key grant failure fails the start step.
9. `Update-HccProcessPath` adds registry entries to the current PATH (keeps what the bootstrap put in front) and puts the protected sqlcmd folder (`...\Client SDK\ODBC\180|170\Tools\Binn`) first; runs at the start of every real install/upgrade run (resume included).
10. Property step is idempotent (`/setup/status`: if not required only the listener is closed and the service restarted); `-ServerUrl http://127.0.0.1:<LoopbackHttpPort>` is passed.
11. Port 5080 refused (orchestrator and wizard).
12. Backup folder: refused on a drive top, under Users, Windows, Program Files, ProgramData, `{app}`, and when it holds other files (accepted: new, empty, only HCC backup files/`Logs`/`DrillScratch`, or already carrying the HCC backup ACL); wizard checks the same plus that the drive exists.
13. Production USB folder must be on a removable drive (`Get-HccDriveType` / `GetDriveTypeW`); Trial does not ask.
14. Only four-part private IPv4 addresses (10/8, 172.16/12, 192.168/16) that this PC's adapters have; normalised address passed on.
15. Merge never fills `AllowedHosts`, `Hcc:Hosting:LoopbackHttpPort`, `...CertificateThumbprint`, `...ServerName`, `Hcc:Sql:BackupDirectory`, `Hcc:GuestIdKeys` from the sample and logs a WARN naming any that are missing.
16. `*.before-upgrade` copies are written into the private `Setup` folder.
17. Mode recorded in `install-state.json` and `Hcc:Install:Mode` (ignored by the server). Upgrade: record, then setting, then Trial files (`TrialKeyCopies`, `Keys\HCC-Hotel-CA-password.txt`); with none it stops with a plain message and `UpgradeFailed`.
18. Uninstall: passes the HTTPS port, does not need healthy data folders, reports `UninstallPartial` with a note (shown on the uninstall end message) and says the scripts are removed with the program files. Egress removal sits behind an adapter (mocked in tests).
20. Start-menu address uses the parsed port. 21. Hotel-name rule = client kit's. 22. Resume with changed answers is refused and names them. 23/26. Documented in INSTALL.md (module logging; helper script in the setup's temp folder). 24. 3010 kept; the message now says setup is NOT finished and must be run again. 25. Timeout/failure messages name the execution policy `AllSigned` as a likely cause.

Also: finish page says the hotel address and display name are set inside HCC; Format-HccFingerprint renamed `Format-HccCaFingerprint` (the client lane has a function of the same name); `#ifdef SqlMediaDir` as the build lane needs.

## Results
- `powershell -NoProfile -File tests\Test-HccDeployment.ps1` (Windows PowerShell 5.1.26100): **Passed 213, Failed 0** (includes the SQL, CLIENT and SERVER blocks and the ISCC compiles with stub inputs).
- PowerShell 7: PS7_PLACEHOLDER
- ISCC compile of `Hcc.iss` with stub inputs (with and without SQL media): exit 0 (inside the test run); refuses a compile without `AppVersion` / `ClientSetupExe`.

## Still NOT verified (nothing was installed)
Same list as above, plus: the new Pascal code (IPv4 / backup-folder / drive-type checks, `CreatePrivateFolder` with owner + link refusal, uninstall messages) compiles but never ran; `Repair-HccPrivateDirectory` and `New-HccPrivateDirectory` were not run against real ProgramData; `Get-HccFileProblems` / `Get-HccFolderProblems` are exercised only on temp items; the real `Install-HccService.ps1` was not run, so "service registered stopped, then SQL login for `NT SERVICE\HccServer`" is by reading the scripts and the audit's checks, not by execution.

## Notes 20-26 left as documented
24 (3010 meaning "success + restart" to deployment tools): kept by the orchestrator's decision. 26 (helper `.ps1` in `{tmp}`): kept, documented in INSTALL.md.
