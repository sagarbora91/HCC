# INST lane `sql` - DONE (12 Nov 2026)

Branch `claude/inst-sql` (base c7438465). Nothing was installed, no SQL Server installer or the new script was run for real, the live SQL instance was not touched.

## What changed (files)
- NEW `08 Production/deployment/windows/Bootstrap-HccSql.ps1` - the INST-CONTRACT section 2 interface: `-Mode InstallBundled|UseExisting -MediaDir <dir> [-PlanOnly] [-LogFile] [-ManifestPath]`; exit 0 ready / 2 restart needed / 1 failed. `-WhatIf` = `-PlanOnly`.
- NEW `08 Production/deployment/windows/sql-media.manifest.json` - names, sizes, SHA-256 of the five files in `E:\ETP\Installers\SQL payload for the ETP installer (2025 + ODBC 17)` (roles engine / sqlcmd / odbc17 / odbc18 / unused). Payload itself is not in the repo. **Lane server/build: ship this json next to the scripts in `Operations\`** (the script reads `$PSScriptRoot\sql-media.manifest.json`; the manifest deliberately never lives in the media folder).
- EDIT `08 Production/deployment/windows/tests/Test-HccDeployment.ps1` - appended block `# INST-SQL` ... `# /INST-SQL` (18 checks) before the final summary lines. No existing line changed.

## Behaviour
- Instance present (service `MSSQL$SQLEXPRESS`), either mode: ADOPT. Read-only probe only (Windows authentication, `-C`): sysadmin yes/no, major version >= 16, auth mode, TCP flag. Stopped service is started (never restarted/reconfigured). Mixed-mode auth or TCP on are logged as notes, not changed or refused. Not sysadmin or version < 16 = exit 1. No database or login is touched. If the PC has no sqlcmd, only ODBC 17 (if absent) and `MsSqlCmdLnUtils.msi` are installed from verified media.
- Instance absent + `UseExisting` = exit 1 (plain message). Absent + `InstallBundled`: restart pending (CBS RebootPending, WU RebootRequired, PendingFileRenameOperations) = exit 2 before anything runs; else verify media hashes, extract `SQLEXPR_x64_ENU.exe` to a SYSTEM+Administrators-only scratch folder under `%ProgramData%\HCC\SqlSetup\<guid>` (removed in `finally`), run setup silently: `/ACTION=Install /QUIET /IACCEPTSQLSERVERLICENSETERMS /FEATURES=SQLENGINE /INSTANCENAME=SQLEXPRESS /SQLSYSADMINACCOUNTS=BUILTIN\Administrators /ADDCURRENTUSERASSQLADMIN=False /SQLCOLLATION=Latin1_General_CI_AS /TCPENABLED=0 /NPENABLED=0 /UPDATEENABLED=0` (no SECURITYMODE = Windows authentication only), then ODBC 17 + sqlcmd if still missing, then probe (retries while the engine starts). Exit code 3010 from any installer = finish, then return 2.
- Media: all needed files are hash-checked (and refused if a link) before the first installer runs; the media folder must be writable only by SYSTEM/Administrators/service SIDs (else refused: these files run elevated).
- sqlcmd folder is put in front of this process's PATH; **lane server: the orchestrator should refresh its own `$env:Path` from the registry after step 1** (a child script cannot change its parent's PATH).
- Log: `-LogFile`, default `%ProgramData%\HCC\Logs\install-<yyyyMMdd-HHmmss>.log` (the orchestrator should pass its own log path). Never written in `-PlanOnly`. No secret is handled by this script.

## What was run (exact)
1. PowerShell parser on `Bootstrap-HccSql.ps1` (5.1): 0 errors.
2. `Bootstrap-HccSql.ps1 -Mode UseExisting -PlanOnly` on this PC (real, read-only: reads the service list and sqlcmd path only): exit 0, 3 PLAN lines "Adopt ... / Check ... / Plan finished. Nothing was changed."; `C:\ProgramData\HCC` was not created.
3. Read-only hash check of the real ETP payload folder against the committed manifest (`Test-HccSqlMedia`, all 5 roles): 0 problems.
4. My 18 INST-SQL checks in isolation under Windows PowerShell 5.1 and under PowerShell 7: 18/18 pass in both. They cover manifest shape, setup arguments, media check (good, wrong hash, missing, unknown role, missing folder), pure plan outcomes, adopt (running / stopped / no sqlcmd / non-sysadmin / old version), UseExisting with no instance, install path order and scratch removal, wrong-hash / missing / non-admin-writable media refused before any process, restart pending = 2, setup 3010 = 2, 1603 = 1, non-admin = 1, `-PlanOnly` changes nothing (no process, no service start, no probe, no log file) also with bad media (exit 1), the real script as a separate `-PlanOnly` process, and no password/SQL-login switches in the file.
5. Full `tests/Test-HccDeployment.ps1` (5.1): see result line at the end of this file.
A first run found a real bug (an empty restart-reasons list was read as one reason, which would have made every clean PC answer "restart"): fixed (the helper functions now return plain arrays) and covered by the install-path checks.

## NOT verified
- The script was never run for real: no actual SQL Server install, no msiexec, no real extractor, no real `setup.exe` switch acceptance (switches are ETP's proven set; the `/X:` extractor path and `setup.exe` location are ETP's assumptions, also unverified there beyond ETP's own use).
- The probe's T-SQL (`sys.dm_server_registry` TCP flag, `ProductMajorVersion`) was not executed against an instance by me; sqlcmd in `-PlanOnly` is never called.
- The ACL check `Test-HccPathAdminOnly` and the protected scratch folder creation ran only as mocks; real ACLs not exercised. Restart-pending registry probes were not run on a pending PC.
- Not tested on a PC that has ODBC 18 sqlcmd only, or where `C:\Program Files\HCC\SqlSetupMedia` inherits Users:Modify (it would be refused by design).
- No cumulative-update step (ETP applies a `SQLServer2025-KB*` CU if present in its payload; this payload has none, not ported).

## Defaults used
- Sysadmin = `BUILTIN\Administrators` (as ETP), not just the installing user; `ADDCURRENTUSERASSQLADMIN=False`.
- Minimum SQL version 16 (2022), ETP's floor. ODBC 18 and LocalDB are not installed (role `odbc18`/`unused` in the manifest; the build lane may omit `msodbcsql.msi` and `SqlLocalDB.msi` (68 MB) from the shipped media).
- An adopted instance that is mixed-mode or has TCP on is accepted with a warning (ETP refuses; HCC must not reconfigure a shared instance).
- A stopped adopted instance is started, never restarted.
- Fixed instance name `SQLEXPRESS` (no parameter).
- Media folder not admin-only = refuse (no copy-to-safe-place fallback).

## Open questions
- None blocking. Does the owner want adoption to refuse mixed-mode instances like ETP does? (Default: warn only.)

## Licence sentence for the wizard (lane server), same as ETP's task text
"Install Microsoft SQL Server 2025 Express, its ODBC drivers and Sqlcmd from the media included with this installer (accepts Microsoft's licence terms)"

## Shared files touched
`tests/Test-HccDeployment.ps1` (INST-SQL block only). `Hcc-Common.ps1`, `INSTALL.md`, `.gitignore` not touched.

Full `tests/Test-HccDeployment.ps1` under Windows PowerShell 5.1: Passed 110, Failed 0 (92 existing + 18 INST-SQL).
