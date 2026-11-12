# INST lane `server` - Opus audit (12 Nov 2026)

Branch `claude/inst-server`, head audited **99e3b762** (base c7438465). Auditor: Opus. Code of the lane not edited.

## Verdict: **FIX**

The structure is good: the steps follow the contract, the secrets flow (answer file, SecureString, in-process calls, scrubber, `finally` delete) is careful, there is no command line or SQL built from wizard text, and failures end loudly. But a real install on a clean Windows PC **stops at step 2 every time**, and a Trial install **ships a web page that does not start**. Both are invisible to the lane's tests because every script is mocked. There is also one secrets hole in the wizard's Setup folder. These three block the trial machine run. The rest are notes to fix in the same round.

## What I ran (light commands only; nothing installed)
- `powershell -NoProfile -File tests\Test-HccDeployment.ps1` (Windows PowerShell 5.1): **Passed 133, Failed 0** (includes the lane's ISCC compile of `Hcc.iss` with stub inputs). `C:\ProgramData\HCC` still did not exist afterwards (rule 2 holds for the suite).
- `Install-HccServer.ps1 -AnswerFile <scratch> -PlanOnly` with the hotel name `Hotel "Parth" & Co; 100% $(calc) `x`: 12 steps printed, nothing expanded or run, exit 0, answer file kept, no `C:\ProgramData\HCC` created.
- Same with a **malformed** answer file holding a marker password: the error line printed the whole file, password included (finding 5).
- Read-only check of account names: `NT SERVICE\HccServer` (and any not-yet-created service) does **not** translate to a SID on this PC; `NT SERVICE\MSSQL$SQLEXPRESS` does (finding 1).
- `icacls` on a scratch file with `NT SERVICE\HccServer:(R)`: exit 1332, **nothing changed** (no partial ACL) (findings 1, 2).
- `[IO.Directory]::CreateDirectory(path, security)` under Windows PowerShell 5.1 on a scratch path: the **missing parent folders get the same locked ACL** (finding 2). Scratch folders removed afterwards.
- Newest production page on this PC (`worktrees\cu\03 Prototype\dist\production\index.html`, 11 Nov): one real `<title>` in the head and **three more `<title>` strings inside the inline script**; the page has a CSP meta with `script-src 'sha256-...'` (finding 3).
- `git diff --stat` = `git diff --stat --ignore-cr-at-eol` (line endings kept). The shared files `tests/Test-HccDeployment.ps1` and `INSTALL.md` changed only by appending inside the lane's blocks.

## (a) Will a real install work? Step by step on a clean PC

| # | Step | Needs from earlier steps | Result on a clean PC |
|---|---|---|---|
| 1 | sql | media in `{app}\SqlSetupMedia`, manifest | Works against the real `Bootstrap-HccSql.ps1` (`exit (...)` sets `$LASTEXITCODE` for the in-process call; `-WarningsFile` JSON shape `{warnings:[{code,text}]}` is read correctly). PATH refresh: see finding 9. |
| 2 | database | sqlcmd; **service SID** | **FAILS.** `CREATE LOGIN [NT SERVICE\HccServer] FROM WINDOWS` needs the service to exist (SQL resolves the name through LSA; the virtual account does not exist until `New-Service`). Error 15401, step 2 fails, and `-Resume` fails the same way forever. |
| 3 | certificate | settings file (copied from sample) | Works; thumbprint + AllowedHosts written. The key grant to `NT SERVICE\HccServer` inside `New-HccCertificate.ps1` fails (warning only) because the service does not exist; re-granted in step 7 (order sound once the service exists, but see finding 8). |
| 4 | settings | step 3 values | Works (keeps AllowedHosts / thumbprint; sets backup folder, name, port). Trial page: finding 3. |
| 5 | keys | settings file | Works, but `New-HccGuestIdKey.ps1`'s `icacls ... NT SERVICE\HccServer:(R)` fails (warning only) and leaves the settings file with its inherited ACL. Machine-scope DPAPI is the right scope for the service (user scope would wrap for the administrator), but its safety is the file ACL: finding 2. |
| 6 | migrate | thumbprint, AllowedHosts, rooted BackupDirectory, guest key, **settings file readable only by SYSTEM/Admins/service** (`GuestIdKeyFileCheck` runs inside `UseHccHosting`, before the `migrate` branch) | Works on a fresh install only by accident (finding 2); `ASPNETCORE_ENVIRONMENT=Production`, working folder = Server (content root) and admin token are correct. |
| 7 | service | settings, exe, runtime (self-contained: `hostfxr.dll` check) | Works; restricts the settings file; re-grants the key. |
| 8 | property | loopback 5080 open, SQL login (step 2) | Works when step 2 has worked; closes the listener and restarts the service. Not re-runnable after a half-finished run (finding 10); port 5080 collision (finding 11). |
| 9 | backup-tasks | hcc-ops.json, backup key, admin-only Operations | Works. |
| 10 | egress | - | Works. |
| 11 | client-kit | CA .cer, client setup exe in `{app}\ClientKit` | Matches the real `New-HccClientKit.ps1` (`-ServerName -Port -HotelName -CaCer -OutDir`, finds the exe in OutDir, throws on failure). Its hotel-name check is stricter (invisible format characters) than the orchestrator's, so such a name fails here at step 11 instead of at the start (note 21). |
| 12 | health | CA trusted on this PC | Works (`/api/v1/system/health` returns `{"status":"ok"}`; 503 while the schema is behind). It checks only HTTP 200 for `/`, so the broken Trial page of finding 3 passes. |

## Findings (blocking first)

### Blocking

1. **`Install-HccServer.ps1` lines 753-766 (step list), 595-598 (`Step-Database`), 680-689 (`Step-Service`): the service is registered at step 7 but its account is used at steps 2, 3 and 5.** On a clean PC `Initialize-HccDatabase.ps1` fails at `CREATE LOGIN [NT SERVICE\HccServer] FROM WINDOWS` (the virtual account does not resolve before `New-Service`; checked on this PC). Every install stops at step 2 and every resume stops there again. The same missing account makes the key grant in `New-HccCertificate.ps1` and the settings-file `icacls` in `New-HccGuestIdKey.ps1` fail (only warnings). Contract section 2 prescribes this order, and so do INSTALL.md sections 2-3 (the manual procedure has the same flaw). **Right:** register the service (`Install-HccService.ps1`, which creates it stopped, sets recovery, firewall rule and ACLs) as a new step between `sql` and `database`, and keep step 7 as "grant the key (throw on failure), start". It needs only the exe and the settings file, which the wizard has already copied. Record the order change in the DONE file for the orchestrator, because contract section 2 and INSTALL.md section 2/3 need the same change. Add a test that asserts the service exists (mocked `Test-HccServiceInstalled` flips when `Install-HccService.ps1` runs) before `Initialize-HccDatabase.ps1` is called.

2. **`Hcc.iss` line 916 (`CreatePrivateFolder('{app}\SqlSetupMedia')` at ssInstall) and `Install-HccServer.ps1` steps 5-6: the protection of the guest ID key depends on a side effect.** `[IO.Directory]::CreateDirectory(path, security)` in Windows PowerShell 5.1 gives the locked ACL to every missing parent folder (checked), so on a fresh install `C:\Program Files\HCC` is created SYSTEM + Administrators only, and the settings file inherits that. Only because of this does step 6 pass `GuestIdKeyFileCheck`. If `C:\Program Files\HCC` already exists with the normal Program Files ACL (Users: read), the failed `icacls` of step 5 leaves the file with the `dpapi-machine:` key readable by Users, which any local program can unwrap. Users can then read it for the minutes until step 7, and `migrate` refuses to run, so step 6 fails. **Right:** fixed by finding 1: the service exists at step 5, so the `icacls` there works. Also make it intended: create `{app}` locked on purpose (or document it), and have the orchestrator check the settings-file ACL with the same rule as `GuestIdKeyFileCheck` right after step 5, throwing if it is wrong.

3. **`Install-HccServer.ps1` line 530 (`Set-HccTrialPage`): `[regex]::Replace($html, '<title>', '<title>TRIAL - ', 'IgnoreCase')` replaces every `<title>`, including the three inside the inline script of the real production page.** The script text then no longer matches the `script-src 'sha256-...'` in the page's own CSP meta (A10-05). The browser refuses to run it, so the **Trial install shows a blank page**, while step 12 still passes because `/` answers 200. The banner insert after the first `<body` is safe on the current build (the real `<body>` comes before the script, at 523588 of 5.4 MB), but nothing guarantees that. **Right:** replace only the first `<title>`, and only if it lies before the first `<script`. Insert the banner only if the first `<body...>` lies before the first `<script`. Otherwise throw (better a failed step than a broken page). In step 12, for Trial, check that the served page still contains the CSP meta and that the SHA-256 of its inline script equals the hash in the meta. Test with a fixture that has `<title>` and `<body>` strings inside the script and a matching CSP meta.

4. **`Hcc.iss` lines 765-788 (`CreatePrivateFolder`, line 781 `Set-Acl` on an existing folder) and 924-936 (answer file written): the Setup folder can stay readable by a non-administrator.** Standard users may create folders in `C:\ProgramData`. A user who creates `C:\ProgramData\HCC` (or `...\HCC\Setup`, or a pre-made `answers.json`) before the install keeps ownership: `Set-Acl` replaces the DACL but not the owner, and the owner can always re-grant itself access. `SaveStringsToUTF8File` truncates an existing file and keeps its DACL and owner, and it follows a junction or symbolic link. The orchestrator's checks (`Initialize-HccDataFolders`, `Assert-HccAnswerFolderPrivate`, lines 121-163) look at ACEs and links but not at the owner, and they run after the file is already written. Result: the Owner password and the CA password can be read by that user. **Right:** in `CreatePrivateFolder`, refuse a reparse point and always set owner = Administrators on `DataDir` and `SetupDir` (`icacls /setowner *S-1-5-32-544` or `SetOwner` + `Set-Acl`). Delete any existing `answers.json` and refuse a reparse point before writing, then create the file fresh (`CREATE_NEW`). In the orchestrator, add the owner to the folder checks (owner must be SYSTEM or Administrators).

### Should fix in the same round

5. **`Install-HccServer.ps1` line 917: the answer file is parsed before the secrets are registered.** Windows PowerShell 5.1's `ConvertFrom-Json` puts the whole input text in its error message, and the outer `catch` (line 968) writes it to the install log. Checked: a malformed file with a marker password printed the password. **Right:** wrap the read + parse in its own try/catch that logs a fixed sentence ("the answer file is not valid JSON") without the exception text.

6. **Answer file left behind after a crash or power cut** (`Hcc.iss` 878-881, 977-980; `Install-HccServer.ps1` 971-972). If the PC loses power while the orchestrator runs, `answers.json` stays in `Setup\` in plain text (admin-only) until the next setup run. If the owner never runs the setup again, it stays for good. Also, the orchestrator's early `return 1` paths (lines 872-873: not elevated, data folder unsafe) are outside the `try`, so the orchestrator itself does not delete the file there (Inno does). **Right:** the orchestrator removes any `Setup\answers.json` at the start of every real run (install, `-Upgrade`, `-Uninstall`), after the folder check. Move the early returns inside the `try`. Inno's `RemoveAnswerFile` overwrites before it deletes, like the orchestrator does.

7. **Upgrade failure names the wrong log and gives no upgrade-specific message** (`Install-HccServer.ps1` line 909; `Hcc.iss` 883-900). On upgrade failure `install-summary.txt` is not rewritten, so `FailSetup` reads `logFile=` from the original install and shows that old log. After a failed `migrate` the new files are in place, the old ones are deleted (`[InstallDelete]`) and the service is stopped: the hotel is down. **Right:** write the summary (status `UpgradeFailed`, current log) on upgrade failure too. In `FailSetup`, for an upgrade, say plainly: "HCC is stopped. The data is safe (a backup was taken before the database update: <folder>). Run this setup again to retry; if it fails again call support with the log <file>." Keep `UpgradeRun` detection working for the retry.

8. **`Install-HccServer.ps1` line 686: a failed grant of the certificate key to the service is only a warning.** The service then cannot load its certificate, and the failure shows up later as a confusing step 8 timeout. **Right:** throw (fail step 7 with that reason).

9. **`Install-HccServer.ps1` lines 546-550 and 588: `Update-HccProcessPath` replaces `$env:Path` with the registry value.** This throws away the protected sqlcmd folder that `Bootstrap-HccSql.ps1` put in front of PATH in this same process. If that folder is not on the machine PATH, step 2 fails with "sqlcmd.exe was not found" after a successful bootstrap. If another `sqlcmd.exe` comes first on the machine PATH, that one is used. Resume runs (sql already done) never refresh at all. **Right:** at the start of every run, refresh from the registry, then put the protected sqlcmd folder in front (resolve it the bootstrap's way, `Get-HccSqlcmdPath`, or let the bootstrap report it).

10. **`Install-HccServer.ps1` lines 691-701 (`Step-Property`): not idempotent after a half-finished run.** If `SetupProperty` succeeded but the listener switch-off or the restart did not, `LoopbackHttpPort` is still 5080. The next run then calls `Initialize-HccProperty.ps1` again, which throws "already set up", and the install can never finish. The orchestrator also does not pass `-ServerUrl`, so a non-default `LoopbackHttpPort` is ignored. **Right:** read `/api/v1/setup/status`. If `setupRequired` is false, only set `LoopbackHttpPort = 0`, restart, and log "already set up". Pass `-ServerUrl http://127.0.0.1:<port>`.

11. **Port 5080 accepted as the HTTPS port** (`Test-HccAnswers` line 404; `Hcc.iss` 614-622). With HTTPS on 5080 the server's validator refuses "LoopbackHttpPort must differ from HttpsPort", so `migrate` fails at step 6. **Right:** refuse 5080 (the loopback port) in both checks, with a plain sentence.

12. **Backup folder can be an existing folder with other data** (`Test-HccAnswers` 405-406; `Hcc.iss` 627-639). `Initialize-HccDatabase.ps1` removes inheritance on that folder and gives it to SYSTEM, Administrators and SQL Server only, and that change reaches every inherited subfolder and file. "C:\Users\Owner\Documents", "D:\Data" or "C:\Users" would lock their own users out. **Right:** accept only a new or empty folder, or one that holds only HCC backup files (`ConvertFrom-HccBackupFileName` names, `Logs`, `DrillScratch`). Refuse the Windows, Program Files, ProgramData and Users folders and anything under a user profile.

13. **Production key copies can go to a fixed internal disk** (`Test-HccAnswers` 413-421; `Hcc.iss` 676-686). Both checks only compare drive letters with the system or install drive. `Get-HccDriveType` (line 243) is defined but never called, so a second internal disk `D:` passes and the CA key and both raw key copies stay on the PC (DEC-087). **Right:** require `DriveType = Removable` (or warn and ask for confirmation when it is not), in both the wizard and the orchestrator.

14. **IP addresses** (`Test-HccAnswers` 399-403). `[Net.IPAddress]::TryParse` accepts `0.0.0.0`, `255.255.255.255`, `127.x`, multicast, public addresses, short forms (`10.1` becomes `10.0.0.1`) and IPv6 with a scope id (`fe80::1%12`, which makes `New-SelfSignedCertificate` fail at step 3). Each one goes into the CA's name constraints and the certificate SAN. **Right:** accept only unicast IPv4 written as four dotted parts, in 10/8, 172.16/12 or 192.168/16 (warn on anything else), with no scope id. Pass the normalised address on.

15. **Settings merge adds sample placeholders for keys that matter** (`Merge-HccJsonMissing` 442-452). If an older settings file lacks `Hcc:Hosting:LoopbackHttpPort` (the server's default is 0), the merge adds the sample's `5080` and reopens the plain-HTTP setup listener (F-L11). A missing `AllowedHosts` gets `hotel-pc;localhost;192.168.1.20;127.0.0.1`. **Right:** keep a short deny-list of keys that are never taken from the sample (`AllowedHosts`, `Hcc.Hosting.LoopbackHttpPort`, `Hcc.Hosting.CertificateThumbprint`, `Hcc.Hosting.ServerName`, `Hcc.Sql.BackupDirectory`, `Hcc.GuestIdKeys`). Log a WARN if any of them is missing. The merge itself never overwrites a value (rule 9 holds; the test covers nested values, arrays, `false` and empty strings).

16. **`.before-upgrade` copies are briefly readable by Users** (`Save-HccJsonInPlace` 454-461). `Copy-Item` gives the copy the folder's ACL, which on a non-locked `{app}` includes Users, and only then `Protect-HccFile` runs. If `icacls` fails, the copy holding the `dpapi-machine:` key stays readable and the step fails. **Right:** write the safety copy into the private `Setup\` folder, or create it with its ACL first (create an empty file with the security, then write).

17. **The Trial mark can disappear** (`Install-HccServer.ps1` line 885; `Hcc.iss` 436-442). Upgrade takes the mode from `install-state.json` and assumes `Production` when the record is missing. A Trial PC whose `Setup` folder was cleared then loses the banner and the TRIAL Start-menu name on its next upgrade, although its CA key and raw key copies stay on the PC. The server itself does not know it is a trial, and the Android app never shows the banner. **Right:** also record the mode in a place an upgrade cannot lose. Two options: a key in `appsettings.Production.json`, for example `Hcc:Install:Mode = Trial`, which the server could show in System health and discovery, or the `Keys\HCC-Hotel-CA-password.txt` marker as a fallback. When no mode is known, refuse to guess (ask on screen, or treat the presence of `TrialKeyCopies` / the password file as Trial).

18. **Uninstall** (`Install-HccServer.ps1` 847, 873; `Hcc.iss` 999-1004).
    - `Install-HccService.ps1 -Uninstall` is called without `-HttpsPort`, so the firewall rule `HCC HTTPS <port> (LAN)` stays when the port was not 5443. Pass the port from the state or the settings file.
    - `Initialize-HccDataFolders` runs before the uninstall branch. If the data folders look unsafe (for example after Explorer's "Continue" button added the user to `Logs`, reachable from the Start-menu entry "HCC install logs"), the uninstall returns 1 before removing the service, and the files are deleted anyway. The uninstall branch should not need the data-folder checks.
    - The final message says "HCC was removed" even when a removal step failed. Show the partial result.
    - The rest is right: it keeps SQL, the databases, backups, keys, certificates and settings and says so before and after. `Operations\*.ps1` are removed with the program files (only `hcc-ops.json` stays), so a later repair or key import needs the setup again. Say that in the final message or keep a copy of the scripts.

19. **`Initialize-HccDataFolders` lines 132-143: "repair" cannot remove other accounts' explicit ACEs.** `Set-HccProtectedDirectory` uses `icacls /inheritance:r /grant:r`, which keeps explicit entries for other accounts, so a folder with an extra explicit write entry is never repaired and every later setup refuses to run. That fails safe, but the hotel cannot upgrade until someone fixes it by hand. **Right:** for these three folders, rebuild the DACL from nothing (a new `DirectorySecurity` with owner + `Set-Acl`), as `New-HccPrivateDirectory` does.

### Notes (low)

20. `Hcc.iss` line 449/706: `ServerUrl` and the summary use the raw port text, while the answer file uses `IntToStr(StrToIntDef(...))`. Delphi's `StrToIntDef` accepts `$1543` (hex) and spaces, so the Start-menu URL can differ from the real port. Use the parsed number in both.
21. Hotel name: the orchestrator accepts invisible format characters that `New-HccClientKit.ps1` (`Test-HccHotelName`) refuses, so such a name fails late at step 11. Use the client lane's `Test-HccHotelName` in `Test-HccAnswers`.
22. Resume with changed answers (backup folder, hotel name, Owner name) silently keeps the values of finished steps but writes the new values into `install-state.json`. Compare them with the old state and refuse, or say which ones are ignored.
23. Secrets scrubber (`Add-HccSecret` 68-81): it handles exact text and PowerShell's JSON escaping, and works on literal strings (`String.Replace`), so regex characters and quotes are safe. It does not catch System.Text.Json escaping (`\u002B`, `\u0026`), URL encoding or a secret split over two writes. No current child script produces those. If the hotel PC has PowerShell module logging switched on by policy, parameter values (`ConvertTo-HccSecureString -Text`) are logged by Windows: say in INSTALL.md that the setup should not run with module logging on, or build the SecureString without a named cmdlet parameter.
24. 3010 means "success, restart needed" to deployment tools. The wizard is interactive and its message is clear, and the finish page is never shown because of `ExitProcess`. Keep it if the owner agrees (lane open question 3).
25. A Group Policy execution policy of `AllSigned` overrides `-ExecutionPolicy Bypass`. The orchestrator then never starts, and the wizard reports a 2-minute timeout naming the Logs folder. Make the message name the likely cause.
26. `CreatePrivateFolder` writes its helper `.ps1` into `{tmp}` and runs it elevated. This is acceptable only because Inno 6 restricts `{tmp}`. The SQL-media comment ("never in setup's {tmp}") applies here too; consider passing the script with `-EncodedCommand` (it holds no secret).

## (b) Secrets, line by line
- Hand-over: wizard -> `C:\ProgramData\HCC\Setup\answers.json`. The folder is (re)locked (`PrepareSetupFolder`) **before** the file is written (`Hcc.iss` 924-931); owner: finding 4. Only SYSTEM + Administrators can read the file. The orchestrator re-checks the folder before reading (line 916), overwrites the file with zeros and deletes it in `finally` (819-828, 971-972). Inno deletes it again after the run and in `DeinitializeSetup`. Crash or power cut: finding 6.
- Pascal: the passwords are never passed to `Log` or shown on the Ready page (it shows "password as typed"), and they never appear in an `Exec` command line (the parameters are a fixed path plus `-AnswerFile` / `-Resume` / `-Upgrade`), the registry or `/LOG`. `Lines[0]` is cleared after writing. `install-state.json`, `install-summary.txt`, `progress.txt` and `warnings.txt` hold no secret (checked in code and by the marker test).
- SecureString: built in-process (`ConvertTo-HccSecureString`) and passed by splatting to `New-HccCertificate.ps1 -CaPfxPassword` and `Initialize-HccProperty.ps1 -OwnerPassword`. Both edits are optional parameters; the prompts stay the default. `Initialize-HccProperty` clears `$p1`/`$p2`/`$json` in `finally` (existing code).
- Scrubber: note 23; parse-error leak: finding 5.
- Trial CA password: generated (24 random bytes), registered with the scrubber, written to `Keys\HCC-Hotel-CA-password.txt` inside `Keys`, which was created with its ACL in one call (owner Administrators, SYSTEM + Administrators only). This is acceptable for Trial as the contract says. Production cannot take this path: the branch is chosen by `mode`, and Production answers need a 12+ character CA password and a USB folder (drive-type gap: finding 13). `TrialKeyCopies` (raw guest ID key + raw backup key) is created the same way. Acceptable for Trial only, and only while the Trial mark stays reliable (finding 17).

## (c) Injection and quoting
- PowerShell is started as `{sys}\WindowsPowerShell\v1.0\powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "<fixed path>"` with fixed arguments (64-bit mode, so `{sys}` is the 64-bit System32). No wizard text is on any command line.
- In the JSON from the wizard, `JsonEscape` handles `\`, `"` and control characters, keeps Unicode, and writes UTF-8 (the BOM is stripped by `ReadAllText`). Quotes, `&`, `;`, `%`, `$()` and backticks stay plain text (PlanOnly check above). The orchestrator never builds a command string. `icacls` and `sc` get fixed names or argument arrays. SQL uses `ConvertTo-HccSqlName` / `ConvertTo-HccSqlLiteral` (a backup folder with `'` is safe in `Backup-HccDatabase.ps1`). Settings files go through `ConvertTo-Json`. The certificate subject and SAN use the validated host name (`^[a-z0-9]([a-z0-9.-]{0,61}[a-z0-9])?$`) and parsed IPs (finding 14).
- Hotel name: control characters refused, at most 100 characters; non-ASCII kept; invisible characters: note 21.
- Owner name: same rule as the server (`NormalizeLogin` `^[a-z0-9._-]{3,40}$` after lower-casing). The password rules match `PasswordRules.Validate` exactly (12-128, not containing the name ignoring case, at least 4 distinct characters).
- Backup folder: UNC, relative paths and paths inside `{app}` are refused. A trailing backslash is fine. A user profile or an existing data folder: finding 12.

## (d) Failure, resume, idempotency
- Every step failure becomes exit 1. The state records `failedStep` and the log line names the step. Inno shows a plain message naming the log, writes `SETUP-INCOMPLETE.txt` and exits 1603. No silent success, except: the Trial page (finding 3), the uninstall message (finding 18) and the upgrade log name (finding 7).
- `install-state.json` lives in the admin-only folder and is written atomically (tmp + move). A step is added to `completed` only after it succeeds. If the file is lost, everything runs again, and each step detects "already done" (certificate, keys, service, listener) except `Step-Property` (finding 10).
- `-Resume`: the wizard asks every question again, including both passwords. Passwords are never stored.
- Installer run twice on a finished install: it takes the upgrade/repair path (no questions). The same version is allowed; an older one is refused.
- Exit 2 becomes 3010 (note 24).

## (e) Upgrade
- Order: `PrepareToInstall` stops the service (`net stop /y`). The files are replaced (settings `onlyifdoesntexist`, so kept). Then `-Upgrade`: merge, stop, `migrate` (pre-migration backup through `Hcc:Operations:BackupScript`, as the administrator), start, client kit, health.
- The merge code (`Merge-HccJsonMissing`, lines 442-452) adds only missing keys, recurses into objects, never changes a value, skips `_` keys, and takes a private safety copy. Placeholder gap: finding 15; copy window: finding 16.
- A failed `migrate` leaves the hotel down (service stopped, old binaries gone) with a message that names the wrong log (finding 7). Running the setup again retries the upgrade, because the status stays `Done`.

## (f) Uninstall
See finding 18. It removes only the service, the firewall rules, the tasks and the program files, says so on screen, and keeps SQL, the databases, backups, keys, certificates and settings.

## (g) TRIAL banner
The banner itself is presentational HTML (no script, style block or style attribute). The server's CSP header is computed at run time from the script bodies, so the header is not the problem. The `<title>` replacement is: it breaks the page's CSP meta hash (finding 3). Trial can be mistaken for production after an upgrade with a lost record (finding 17). The extra banner row may push the fit-to-screen layout (DEC-213) at the bottom: check on the trial machine.

## (h) Edited scripts, PowerShell 5.1, `.iss` flags
- `New-HccCertificate.ps1` / `Initialize-HccProperty.ps1`: optional `[securestring]` parameters only; defaults and prompts unchanged; the existing assertions run green (133/0). Inserting `-CaPfxPassword` before `-ServerDays` shifts that parameter's implicit position, which no caller uses.
- Windows PowerShell 5.1: the suite passes on 5.1. No `&&`, ternary or `??`. PowerShell 7 not run (the 7-only `FileSystemAclExtensions` branch is unexercised).
- `.iss`: settings `onlyifdoesntexist uninsneveruninstall` kept for both files. Downgrade refusal in `InitializeSetup`. `ArchitecturesAllowed/InstallIn64BitMode=x64compatible`, `PrivilegesRequired=admin`, `DisableDirPage=yes` (folder fixed at `{autopf}\HCC`), `SetupLogging=yes`, `SetupMutex`. A silent first install is refused; a silent upgrade is allowed. The ACL of `{app}` is locked only as the side effect of finding 2.

## (i) Tests
- 26 new checks in the block, all green. Real decisions exercised: step order, plan-only creates nothing, failure / resume / restart paths, bad answers refused before anything runs, Trial vs Production key paths, merge rules, uninstall scope, the real `Invoke-HccOps` adapter (in-process run, SecureString, scrubbing, exit code, throw), the marker-password grep over every file and the log. ISCC compiles with stubs.
- Gaps: every deployment script is mocked, so findings 1, 2, 9 and 10 cannot show. The banner fixture has no `<title>` or `<body>` inside its script (finding 3). Several `.iss` checks are source greps. No test of the malformed-answer leak (finding 5), the folder owner (finding 4) or the upgrade-failure summary (finding 7). Add the tests named under each finding.
- Rule 2: the suite and my runs created nothing outside temp folders. Line endings unchanged.

## For the orchestrator (outside this lane)
- Contract section 2's step order (Install-HccService at 7, after the database logins at 2) cannot work with `NT SERVICE\HccServer` (finding 1). The manual INSTALL.md sections 2 and 3 have the same order and the same failure. Both need the "register the service first, start it later" order.

---

## Second pass (12 Nov 2026): fix round at head f3214bc9

Audited `git diff e3f6aef5..f3214bc9`: the lane's files only, after the merge of `origin/claude/inst` (dddfbb1d). The merged-in files of the `sql` and `client` lanes were already audited and are used here only as real interfaces. Every item was checked against the code, not against the DONE file. Nothing was installed and the lane's code was not edited.

### Verdict: **FIX** (one new blocking item; everything from the first pass is fixed)

### What I ran (light, nothing installed)
- `tests\Test-HccDeployment.ps1`: **Windows PowerShell 5.1: Passed 213, Failed 0. PowerShell 7 (`E:\Tools\PowerShell\7\pwsh.exe`): Passed 213, Failed 0.** The suite includes the ISCC compile with stubs. `C:\ProgramData\HCC` did not exist afterwards.
- `-PlanOnly`:
  - install: 13 steps in the new order, exit 0, hotel name with quotes / `&` / `;` / `%` / `$()` printed plain, answer file kept.
  - `-Upgrade -PlanOnly`: 5 steps, exit 0.
  - `-Uninstall -PlanOnly`: exit 0.
  - Malformed answer file with a marker password: exit 1 and only the line "The answer file is not valid JSON"; **the password is no longer printed**.
- **Trial page on the newest real production page** (`worktrees\inst-build\03 Prototype\dist\production\index.html`, 12 Nov 04:37), on a scratch copy, using the lane's own `Set-HccTrialPage`:
  - The page passed the CSP check before and passes after: exactly one `<title>TRIAL - `, one banner.
  - My own independent hash check: both inline blocks (the `<style>` of 518,879 characters and the `<script>` of 4,949,430 characters) still have their `sha256-` in the page's CSP meta.
  - Removing the banner and the "TRIAL - " prefix gives back the original byte for byte.
  - Scratch copy removed afterwards.

### The first pass's items, checked in the code
| # | Status | Where / how |
|---|---|---|
| 1 service before its account is used | **fixed** | New step 2 `service-register` (`Step-ServiceRegister`), `Step-Database` refuses without the service; certificate-key grant (in `New-HccCertificate.ps1` at step 4) and settings `icacls` (in `New-HccGuestIdKey.ps1` at step 6) now find `NT SERVICE\HccServer`. But see **S1** below: the service does not RUN as that account. |
| 2 settings ACL by side effect | **fixed** | `Assert-HccSettingsAcl` (same rule as the server, via `Test-HccProtectedDirectory` with the service allowed) after service-register, settings and keys. |
| 3 Trial page breaks CSP | **fixed and verified** on the real page (above); the health step re-checks the served page's CSP in Trial. |
| 4 Setup folder owner / links / pre-existing file | **fixed** | Inno `CreatePrivateFolder`: refuses a link, `icacls /setowner` Administrators, `Set-Acl` with a fresh DACL (replaced, not merged). `RemoveAnswerFile` + `FileExists` check before writing (delete and recreate). Orchestrator: `Get-HccFolderProblems` (link, owner, ACL) and `Get-HccFileProblems` checked BEFORE the answers are read; `Repair-HccPrivateDirectory` sets the owner and replaces the DACL. Residual: note S6. |
| 5 parse error leaks the file | **fixed and verified** |
| 6 stray answer file | **fixed** (removed at the start of upgrade and uninstall; the early returns are now inside the `try`; Inno empties the file before deleting it) |
| 7 upgrade failure log / message | **fixed** (summary `UpgradeFailed` with the current log and the backup folder; Inno treats `UpgradeFailed` as an upgrade; message "HCC could not be updated and is STOPPED"). |
| 8 key grant only a warning | **fixed** (step `service-start` throws) |
| 9 PATH | **fixed** (`Update-HccProcessPath` merges, keeps what the bootstrap put first, puts the protected sqlcmd folder first; called at the start of every install/upgrade run) |
| 10 property not idempotent | **fixed** (reads `setupRequired`; closes the listener and restarts; passes `-ServerUrl`) |
| 11 port 5080 | **fixed** in the wizard and the orchestrator |
| 12 backup folder | **fixed** in both: no drive top, not under Windows / Program Files / ProgramData / HCC / `\Users`. The folder must be new or empty, or hold only HCC backups (orchestrator: `ConvertFrom-HccBackupFileName`, or the HCC backup ACL already present). The orchestrator normalises with `GetFullPath`; the wizard does not, but the orchestrator decides. |
| 13 removable drive | **fixed** in both (`GetDriveTypeW = 2` / `DriveInfo.DriveType = Removable`). Note: some USB hard disks report Fixed and will be refused (S7). |
| 14 IP addresses | **fixed** in both: four-part private IPv4. Only the orchestrator also checks that the address belongs to this PC (`Get-NetIPAddress`); it does not trust the wizard. |
| 15 merge deny-list | **fixed** (`AllowedHosts`, `LoopbackHttpPort`, thumbprint, `ServerName`, `BackupDirectory`, `GuestIdKeys` never taken from the sample; WARN when missing) |
| 16 safety copy window | **fixed** (copy into the private `Setup\` folder, then restricted) |
| 17 Trial mark | **mostly fixed**: `Hcc:Install:Mode` is written at the settings step and on upgrade. Upgrade takes the mode from the record, then the settings, then the Trial files, and **stops** when it cannot tell. Residual: S2. |
| 18 uninstall | **fixed** (port from state/settings, no dependence on healthy data folders, `UninstallPartial` message, "scripts removed, run the setup again to repair") |
| 19 repair cannot remove explicit ACEs | **fixed** (DACL replaced) |
| notes 20-22 | **fixed** (parsed port; the client kit's hotel-name rule; changed answers on resume refused and named) |

### New findings

**Blocking**

S1. **The service runs as LocalSystem, not as `NT SERVICE\HccServer`.** `Install-HccService.ps1` lines 75-82 (existing script, called by the new step 2). Without `-ServiceAccount` it calls `New-Service` with no `-Credential`, and the default logon of `New-Service` is LocalSystem. No `sc.exe config ... obj=` follows, and the orchestrator does not check it either. The virtual account then exists only as a name, which is why steps 3, 4 and 6 now work, but the server process runs as SYSTEM:
- SYSTEM is a member of BUILTIN\Administrators, and the bundled SQL Express makes `BUILTIN\Administrators` sysadmin (`Bootstrap-HccSql.ps1` line 144). So the HCC runtime becomes **SQL sysadmin and full machine owner**. That defeats F-L08 (runtime login read/write/execute only, never db_owner; the append-only audit trigger cannot be switched off) and the low-privilege service design. The SQL login made for `NT SERVICE\HccServer` is never used.
- On an **adopted** instance (the "use the SQL Server already on this PC" choice, e.g. ETP's) where Administrators is not sysadmin, SYSTEM has only the backup login's rights (db_backupoperator + db_datareader). The first write, `SetupProperty` at step 9, is then refused and the install fails.
- `Install-HccService.ps1 -WhatIf` even prints "(account NT SERVICE\HccServer)". The DONE file's "needed no change" is right only about not starting the service.

**Right:** register the service with the virtual account. Either `sc.exe config HccServer obj= "NT SERVICE\HccServer"` (a virtual account needs no password) right after `New-Service`, or `New-Service -Credential` with that name and an empty password. Then `sc.exe sidtype HccServer unrestricted`. Rule 3 allows this as a correction of the documented default; keep `-ServiceAccount` for a named account. In `Step-ServiceRegister`, verify `(Get-CimInstance Win32_Service -Filter "Name='HccServer'").StartName -eq 'NT SERVICE\HccServer'` and fail otherwise. Add a test on the `New-Service` / `sc` arguments.

**Should fix**

S2. **The Trial-to-Production refusal still looks only at `install-state.json`** (`Start-HccInstall` lines 1227-1229). If that file is lost after a Trial install (or the `Setup` folder is deleted), a fresh run answered "Production" passes. It keeps the existing Trial certificate (`Step-Certificate`: thumbprint present), whose CA key and password sit in `ProgramData\HCC\Keys`. It keeps the guest ID key and backup key, whose raw copies sit in `TrialKeyCopies`. And it rewrites `Hcc:Install:Mode` to Production. Result: a "Production" install with Trial key material on the PC, which item 17 exists to prevent. **Right:** decide the earlier mode with `Get-HccInstallMode` (record, then the settings key, then the Trial files) and refuse when it differs from the answers. Never overwrite an existing `Hcc:Install:Mode` with a different value.

S3. **The PC name is not checked against the client kit's allow-list until step 12.**
- Orchestrator `^[a-z0-9]([a-z0-9.-]{0,61}[a-z0-9])?$` and the wizard's `IsHostNameOk` accept names that `Hcc-ClientTrust.ps1` `Test-HccPermittedDnsName` refuses: two-letter names (`pc`), names equal to a suffix in its list (`home`, `dev`, `shop`, `cloud`, `site`, `info`, `local`, `lan`, `test`...), and dotted names outside `.local` / `.lan` / `.internal` / `.home.arpa` / `.localdomain` / `.intranet` / `.corp` / `.home`.
- Such a name builds a CA at step 4 that `New-HccClientKit.ps1` (`Get-HccCaFileInfo` -> "too wide") refuses at step 12. That happens after the service, the hotel and the backups are made, and the CA would have to be made again.
- With the wizard's default (the single-label computer name) and the CA as `New-HccCertificate.ps1` builds it, the kit check passes: DNS `<name>` + `localhost`, IPv4 private + `127.0.0.1`, IPv6 excluded as `::/0`. I checked this against `Get-HccNameConstraintInfo` / `Test-HccHostPermitted`.
- **Right:** dot-source `Hcc-ClientTrust.ps1` in the orchestrator and run `Test-HccPermittedDnsName` (and `Test-HccServerUrl`) on the server name in `Test-HccAnswers`; give the same rule in the wizard's text.

**Notes (low)**

S4. **A reboot between steps 2 and 8 can auto-start the stopped service** (delayed-auto). The exit-2 restart cannot cause this: only step 1 (`sql`) returns 2, and that is before the service exists. A power cut or manual restart can.
- Before step 4 (no thumbprint) the server's start-up validation refuses to start; SCM retries three times, then leaves it stopped. Harmless.
- After step 4 the configuration is valid. The server runs and answers 503 (schema behind) on `/api/v1`, setup included, until `migrate`. No migration needs exclusive database access (no `ALTER DATABASE` in the sources), so `migrate` can run beside it.
- `Step-ServiceStart` then finds the service Running and does not restart it. The guest ID key added later is loaded only after the restart that `Initialize-HccProperty.ps1` / `Close-HccSetupListener` does at step 9.
- `-Resume` copes. To be deterministic, stop the service at the start of the install's `migrate` step (as the upgrade does) and restart (not just start) in `service-start`.

S5. **`{tmp}` of an elevated Inno setup is not admin-only.** It is `is-*.tmp` under the installing user's own `%TEMP%`, writable by that user's non-elevated processes. Inno 6 adds random names but no restrictive ACL. The helper `hcc-private-folder.ps1` therefore has exactly the exposure of Inno's own extracted files (ISSetup DLLs) in the same folder: no additional risk, so acceptable. `-EncodedCommand` would remove it.

S6. **Residual on a pre-existing `ProgramData\HCC` (or `Setup`) owned by a standard user.** A handle opened earlier with `WRITE_DAC` survives the owner/DACL reset. This needs a prepared attacker; the strict fix is to rename such a folder aside and create a new one.

S7. **Removable-only USB.** Some USB hard disks and card readers report `Fixed` and are refused. Say in the wizard text that a USB flash stick is needed.

S8. `RemoveAnswerFile` (Inno) truncates and deletes. The bytes stay in free disk clusters; the orchestrator's zero-overwrite does not guarantee wiping on SSD/NTFS either. Acceptable with BitLocker (INSTALL.md section 7).

### Walk of the 13 steps against the real scripts (clean PC, bundled SQL)
1. sql: `Bootstrap-HccSql.ps1 -Mode -MediaDir -LogFile -WarningsFile` matches the real parameters; `exit` codes reach `$LASTEXITCODE`; PATH merged afterwards. OK.
2. service-register: `Install-HccService.ps1` creates the service stopped (no `Start-Service` in the script; `delayed-auto`), firewall rule, folder/settings ACL, Event Log source. **Logon account wrong: S1.**
3. database: `CREATE LOGIN [NT SERVICE\HccServer]` now resolves. OK.
4. certificate: key grant inside the script now works; CA accepted by the client kit for a single-label name (S3 otherwise). OK.
5. settings: `Hcc:Install:Mode`, Trial mark verified, ACL asserted. OK.
6. keys: `icacls` with the service account now works; machine-scope DPAPI + `AllowMachineScope`; ACL asserted. OK.
7. migrate: as administrator, Production, content root = Server. All start-up checks have their inputs (thumbprint, AllowedHosts, rooted backup folder, guest key, protected file). OK.
8. service-start: grant (fails loudly), start. OK, but running as SYSTEM (S1).
9. property: works with S1 only because SYSTEM is sysadmin on the bundled instance; fails on an adopted instance without that.
10-13. backup tasks, egress, client kit (real `New-HccClientKit.ps1` parameters match; `Format-HccFingerprint` clash removed by the rename to `Format-HccCaFingerprint`), health with the Trial CSP check: OK.

### What only the trial machine can prove
- The wizard itself: page flow, validation dialogs, progress polling, finish page, exit codes 1603 / 3010, the uninstall dialogs (the Pascal code is compiled, never executed).
- Real SQL Express install from the media, the restart path (exit 2, then resume), and adopting an existing instance (ETP) with and without Administrators as sysadmin.
- That the service, once S1 is fixed, runs as `NT SERVICE\HccServer` and can read everything it needs at run time. That covers `Server\` and `web\`, the settings file, the certificate private key, `Server\logs`, the Event Log source, both databases through its own login, and anything the server reads under `ProgramData\HCC` or `Operations` (for example a "back up now" action, if the server ever runs the backup script itself).
- `migrate` as administrator against a fresh database, the pre-migration backup on an upgrade, and the server's start-up file check with the real ACLs.
- Loopback setup on 5080, then the listener closed and the restart; the hotel and Owner created, sign-in with the typed password.
- HTTPS from the server PC and from another PC/tablet through the client kit (name constraints in Windows/Chrome/Edge, firewall rule on the Private profile), the fingerprint on the finish page against the client setup.
- The Trial banner and title in a real browser, and the layout effect of the extra row (DEC-213).
- The backup tasks running as SYSTEM, the egress block rule against outbound traffic, uninstall then reinstall finding the kept data, and an upgrade from this version to the next.
- Explorer behaviour on the admin-only folders reached from the Start menu ("HCC install logs", "client kit").
