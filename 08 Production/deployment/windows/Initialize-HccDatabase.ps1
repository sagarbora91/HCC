<#
.SYNOPSIS
  Creates the empty Hcc database, the SQL logins/permissions for the HCC service and the backup account, and the private backup
  folder. Run once (and again after an upgrade of these scripts: it is safe to repeat), from an elevated PowerShell, as a Windows
  user who is sysadmin on the SQL Server instance.
.DESCRIPTION
  - CREATE DATABASE [Hcc] if missing (never touches any other database; only names starting with "Hcc" are accepted).
  - Runtime login (F-L08): CREATE LOGIN [<ServiceAccount>] FROM WINDOWS; in Hcc only db_datareader + db_datawriter + EXECUTE. It is NOT
    db_owner: the HCC service never changes the schema, so it cannot alter tables or disable the append-only audit trigger. An
    earlier db_owner membership is removed. The schema is built and upgraded by the MIGRATION principal: the administrator who runs
    "Hcc.Api.exe migrate" in an elevated PowerShell (their own Windows login, sysadmin), only at install and upgrade.
    -RuntimeDbOwner keeps the old db_owner grant (not recommended; only if a future version needs it and says so).
  - Backup account (default NT AUTHORITY\SYSTEM, the account the scheduled tasks run as): login + db_backupoperator + db_datareader
    (row-count check of the drill), and dbcreator with -GrantDrillRights so the restore drill can create its scratch copy.
  - Backup folder (F-H03): created or re-protected with inheritance REMOVED; only SYSTEM, Administrators (full), the SQL Server
    service NT Service\MSSQL$<instance> (it writes the .bak) and the backup account (Modify) get in. Logs and drill folders below it
    inherit that. No other Windows user of this PC can open the backups.
  After this, run:  Hcc.Api.exe migrate   (or the installer does) to build the tables, start the service, then
  Initialize-HccProperty.ps1 once to create the hotel and the first Owner (first-time setup; no manual SQL).
  -WhatIf prints the SQL and folder actions and changes nothing (no connection is opened).
# ETP-REUSE: replace/align with ETP's database/login provisioning step of the installer when V:\ETP is available.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SqlInstance = '.\SQLEXPRESS',
    [string]$Database = 'Hcc',
    [Parameter(Mandatory)][string]$ServiceAccount,     # e.g. WORKPC\svc-hcc  or  NT SERVICE\HccServer
    [Parameter(Mandatory)][string]$BackupDirectory,
    [string]$BackupAccount = 'NT AUTHORITY\SYSTEM',   # the account Register-HccBackupTasks.ps1 runs the tasks as
    [switch]$GrantDrillRights,
    [switch]$RuntimeDbOwner,
    [switch]$TrustServerCertificate
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Hcc-Common.ps1"
Assert-HccDatabaseName $Database | Out-Null
foreach ($acct in @($ServiceAccount, $BackupAccount)) { if ($acct -notmatch '^[^\[\]'';]+$') { throw "Account '$acct' contains characters that are not allowed." } }
if (-not [IO.Path]::IsPathRooted($BackupDirectory)) { throw 'BackupDirectory must be a full path.' }
$sqlService = Get-HccSqlServiceAccount -Instance $SqlInstance
$db = ConvertTo-HccSqlName $Database
$login = ConvertTo-HccSqlName $ServiceAccount
$backupLogin = ConvertTo-HccSqlName $BackupAccount
$runtime = if ($RuntimeDbOwner) { "ALTER ROLE db_owner ADD MEMBER $login;" } else { (Get-HccRuntimeGrantSql -User $login) + (" IF IS_ROLEMEMBER('db_owner', {0}) = 1 ALTER ROLE db_owner DROP MEMBER {1};" -f (ConvertTo-HccSqlLiteral $ServiceAccount), $login) }
$steps = @(
    ('IF DB_ID({0}) IS NULL CREATE DATABASE {1};' -f (ConvertTo-HccSqlLiteral $Database), $db),
    ('IF SUSER_ID({0}) IS NULL CREATE LOGIN {1} FROM WINDOWS WITH DEFAULT_DATABASE = {2};' -f (ConvertTo-HccSqlLiteral $ServiceAccount), $login, $db),
    ('USE {0}; IF USER_ID({1}) IS NULL CREATE USER {2} FOR LOGIN {2}; {3}' -f $db, (ConvertTo-HccSqlLiteral $ServiceAccount), $login, $runtime)
)
if ($BackupAccount -ne $ServiceAccount) {
    $steps += ('IF SUSER_ID({0}) IS NULL CREATE LOGIN {1} FROM WINDOWS;' -f (ConvertTo-HccSqlLiteral $BackupAccount), $backupLogin)
    $steps += ('USE {0}; IF USER_ID({1}) IS NULL CREATE USER {2} FOR LOGIN {2}; ALTER ROLE db_backupoperator ADD MEMBER {2}; ALTER ROLE db_datareader ADD MEMBER {2};' -f $db, (ConvertTo-HccSqlLiteral $BackupAccount), $backupLogin)
}
if ($GrantDrillRights) { $steps += ('ALTER SERVER ROLE dbcreator ADD MEMBER {0};' -f $backupLogin) }
$folderWriters = @($sqlService) + @($BackupAccount | Where-Object { $_ -and $_ -ne 'NT AUTHORITY\SYSTEM' -and $_ -ne 'SYSTEM' })
$icacls = Get-HccProtectedDirectoryArgs -Path $BackupDirectory -ModifyAccounts $folderWriters

if (-not $PSCmdlet.ShouldProcess("$SqlInstance database [$Database]", 'Create database, logins, permissions, private backup folder')) {
    foreach ($s in $steps) { Write-Host "DRY RUN SQL: $s" }
    Write-Host "DRY RUN: runtime login $ServiceAccount = $(if ($RuntimeDbOwner) { 'db_owner (legacy, -RuntimeDbOwner)' } else { 'db_datareader + db_datawriter + EXECUTE (no db_owner, no schema changes)' }); migrations run as the administrator who runs Hcc.Api.exe migrate"
    Write-Host "DRY RUN: create/protect folder $BackupDirectory with: icacls $($icacls -join ' ')"
    Write-Host 'DRY RUN: next steps: Hcc.Api.exe migrate, Start-Service HccServer, Initialize-HccProperty.ps1 (first-time setup)'
    return
}
foreach ($s in $steps) { Invoke-HccSql -Instance $SqlInstance -Query $s -TrustServerCertificate:$TrustServerCertificate | Out-Null }
Set-HccProtectedDirectory -Path $BackupDirectory -ModifyAccounts $folderWriters
$problems = @(Test-HccProtectedDirectory -Path $BackupDirectory -AllowedAccounts $folderWriters)
if ($problems.Count -gt 0) { throw ("The backup folder is still not private: " + ($problems -join '; ')) }
Write-Host "Database [$Database] ready on $SqlInstance. Runtime login ${ServiceAccount}: $(if ($RuntimeDbOwner) { 'db_owner' } else { 'read/write/execute only' }). Backup account: $BackupAccount."
Write-Host "Backup folder $BackupDirectory is private (SYSTEM, Administrators, $($folderWriters -join ', ')). Turn on BitLocker for that drive (INSTALL.md section 7)."
Write-Host 'Next: Hcc.Api.exe migrate (as this administrator), Start-Service HccServer, then .\Initialize-HccProperty.ps1 (creates the hotel and the first Owner).'
