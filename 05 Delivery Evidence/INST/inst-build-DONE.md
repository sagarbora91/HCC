# INST lane `build` - DONE (12 Nov 2026)

Branch `claude/inst-build` (base c7438465). Executor: Sonnet.

## What changed
- `08 Production/deployment/windows/installer/Build-Installer.ps1` - rewritten (PowerShell 5.1 and 7, tested on both).
- `08 Production/deployment/windows/INSTALL.md` - one block `<!-- INST-BUILD -->` ("Installing with the setup program"), inserted before section 0; nothing else touched.
- Not touched: `Install-HccService.ps1`, any `.iss`, server code, `Hcc-Common.ps1`, tests.

## Script behaviour
Parameters: `-Version`, `-SqlPayloadDir`, `-SkipSqlPayload`, `-OutDir` (default `E:\Projects\HCC\Installers`), `-DryRun`, `-SignThumbprint`, `-TimestampUrl`, and for testing/operation `-InnoSetupCompiler`, `-ServerIss`, `-ClientIss`, `-WorkDir` (default `%TEMP%\HccInstallerBuild`), `-PublishDir` (reuse a built publish folder), `-LockScript` (run npm/dotnet through `Invoke-Serialized.ps1`; busy lock = exit 75 = build fails).
Order: version (`-Version`, else `<Version>` in `08 Production/Directory.Build.props`, must be x.y.z) -> ISCC (given path, PATH, registry, default folders; missing = error exit 1, also in `-DryRun`) -> SQL media -> `npm run build:production` in `03 Prototype` -> `dotnet publish src/Hcc.Api -c Release -r win-x64 --self-contained true -p:Version=<ver>` into `<WorkDir>\publish` -> copy `dist\production\index.html` to `publish\web\index.html` -> check `Hcc.Api.exe`, `hostfxr.dll`, `web\index.html` -> client setup -> server installer -> optional signtool -> `SHA256SUMS.txt` (lower-case hex, two spaces, server then client). Any failure prints `BUILD FAILED: ...` on stderr and exits 1; success prints `BUILD OK` and exits 0. Unsigned builds print a warning.
SQL media: only `SQLEXPR_x64_ENU.exe`, `MsSqlCmdLnUtils.msi`, `msodbcsql17.msi` are staged (copied) into `<WorkDir>\sqlmedia` and that folder is `SqlMediaDir`. Default source = the ETP payload folder; neither flag and no folder = error. Hashes are checked against `sql-media.manifest.json` (next to the scripts) when it exists (mismatch fails the build); when absent (this branch) a warning says they were not verified.
Client version: `HccClient.iss` needs four parts, so it is compiled with `/DAppVersion=x.y.z.0` and its exe is renamed to `HCC-Client-Setup-x.y.z.exe`; both installers and `SHA256SUMS.txt` carry the three-part version.

## `/D` defines table (exact names)
| Define | Value | Passed to |
|---|---|---|
| `AppVersion` | `x.y.z` for the server; `x.y.z.0` for the client | both |
| `OutputDir` | `-OutDir` (also given as `/O<dir>`) | both |
| `OpsDir` | `08 Production\deployment\windows` | both |
| `PublishDir` | self-contained publish folder, includes `web\index.html` | server |
| `TemplatesDir` | `08 Production\deployment\import-templates` | server |
| `SqlMediaDir` | staged folder with the three media files; NOT defined with `-SkipSqlPayload` (use `#ifdef`) | server |
| `ClientSetupExe` | full path of the built `HCC-Client-Setup-x.y.z.exe` | server |
Output names the `.iss` files must produce: `HCC-Client-Setup-{#AppVersion}` (client), `HCC-Server-Setup-{#AppVersion}-x64` (server).

## What was run, and results
All with ISCC at `E:\Tools\Inno Setup 6\ISCC.exe` (Inno Setup 6.7.3). Nothing was installed; no system change. Output only into temp folders.
1. `-DryRun` under pwsh 7 and Windows PowerShell 5.1: prints all steps and ISCC lines, exit 0, creates nothing (the output folder did not exist afterwards). It correctly reported `HccClient.iss` "not present yet" on this branch.
2. Stub run (two tiny stub `.iss` in a temp folder, fake publish folder via `-PublishDir`, with and without the real SQL folder, pwsh 7 and 5.1): both stub setups compiled, `BUILD OK`, `SHA256SUMS.txt` correct, client exe renamed from `.0` to three parts, exit 0. The stub with `#ifdef SqlMediaDir` and the staged folder (3 files only) verified.
3. Failure paths each gave `BUILD FAILED` and exit 1: missing `.iss`, bad ISCC path, publish folder without `hostfxr.dll`, stub with a syntax error (ISCC exit 2).
4. **Real heavy steps through the lock** (`-LockScript`, waited about 2 h for the gate): `npm run build:production` ok (`dist\production\index.html`, 5.48 MB copied to `publish\web\index.html`), `dotnet publish ... --self-contained true` ok (147 MB; `Hcc.Api.exe`, `hostfxr.dll`, `web\index.html` present, `appsettings.json` only, no `appsettings.Production.json`); then stub client + stub server compiled from that publish: `BUILD OK`, exit 0, SHA256SUMS written.
5. `Install-HccService.ps1` runtime check: `$selfContained = Test-Path <InstallDirectory>\hostfxr.dll` (line 54). The publish contains `hostfxr.dll` at its root and the installer places it in `{app}\Server`, so the check accepts it (read, not executed: running it would create a service). No edit needed.

## NOT verified
- A real build with the real `Hcc.iss` and `HccClient.iss` (the orchestrator runs it after the merge). Only stub `.iss` files were compiled; the `/D` names are those in the contract and the lanes' messages.
- Signing (`-SignThumbprint`/signtool) - no certificate; code path only read, not run. `signtool` lookup untested.
- SQL manifest verification (manifest not on this branch): only the warning path ran.
- Nothing was installed or started; the installers were never run.
- The staged SQL copy (about 750 MB) was run once with the real payload folder; the staged compile of the real server installer with embedded media was not (stub only).

## Defaults used
- Default output `E:\Projects\HCC\Installers`; default work folder `%TEMP%\HccInstallerBuild`; version from `Directory.Build.props` (0.1.0); SQL source defaults to the ETP payload folder; timestamp server `http://timestamp.digicert.com`; heavy jobs run directly unless `-LockScript` is passed (the orchestrator should pass `E:\Projects\HCC\worktrees\fx\tools\Invoke-Serialized.ps1`).
- A missing ISCC fails a `-DryRun` too.
- SHA256SUMS lower-case.

## Open questions
- Should a final build refuse `-SkipSqlPayload`? Currently only a warning.
- An old stray `%TEMP%\hccstub` folder was shared with another lane's tests; my stubs use `%TEMP%\inst-build-stub`.

## Shared files touched
`INSTALL.md` (block `INST-BUILD`). No other shared file.

## Finding: `dotnet publish -r win-x64` rewrites `packages.lock.json`
The self-contained publish (runtime-specific restore) modified the eight `08 Production/src/*/packages.lock.json` files in the worktree (about 1,500 lines). I restored them to the base (final commit) because rule 4 forbids server changes. Whoever runs the real build in a repository checkout must discard those changes afterwards (`git checkout -- "08 Production/src/*/packages.lock.json"`), or build from a throw-away worktree. Under `CI=true` the restore is locked-mode and would instead FAIL for the same reason; a follow-up could add `-p:RestorePackagesWithLockFile=false` to the publish line (not done: it is a build-policy decision).
