<#
.SYNOPSIS
  Creates the empty Hcc database and the SQL login/permissions for the HCC service account, and the backup folder. Run once,
  from an elevated PowerShell, as a Windows user who is sysadmin on the SQL Server instance.
.DESCRIPTION
  - CREATE DATABASE [Hcc] if missing (never touches any other database; only names starting with "Hcc" are accepted).
  - CREATE LOGIN [<ServiceAccount>] FROM WINDOWS; user in Hcc with db_owner (the migration runner creates the schema; this is
    the least privilege that lets HCC upgrade itself) and, optionally, dbcreator so the restore drill can make its scratch copy
    (-GrantDrillRights).
  - Creates the backup folder and gives the SQL Server service (NT Service\MSSQL$<instance>) Modify rights, because BACKUP is
    performed by the SQL Server process, not by the script.
  After this, run:  Hcc.Api.exe migrate   (or the installer does) to build the tables.
  -WhatIf prints the SQL and folder actions and changes nothing (no connection is opened).
# ETP-REUSE: replace/align with ETP's database/login provisioning step of the installer when V:\ETP is available.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SqlInstance = '.\SQLEXPRESS',
    [string]$Database = 'Hcc',
    [Parameter(Mandatory)][string]$ServiceAccount,     # e.g. WORKPC\svc-hcc  or  NT SERVICE\HccServer
    [Parameter(Mandatory)][string]$BackupDirectory,
    [switch]$GrantDrillRights,
    [switch]$TrustServerCertificate
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Hcc-Common.ps1"
Assert-HccDatabaseName $Database | Out-Null
if ($ServiceAccount -notmatch '^[^\[\]'';]+$') { throw 'ServiceAccount contains characters that are not allowed.' }
if (-not [IO.Path]::IsPathRooted($BackupDirectory)) { throw 'BackupDirectory must be a full path.' }
$instanceName = if ($SqlInstance -match '\\(.+)$') { $Matches[1] } else { 'MSSQLSERVER' }
$sqlService = if ($instanceName -eq 'MSSQLSERVER') { 'NT SERVICE\MSSQLSERVER' } else { 'NT SERVICE\MSSQL$' + $instanceName }
$db = ConvertTo-HccSqlName $Database
$login = ConvertTo-HccSqlName $ServiceAccount
$steps = @(
    ('IF DB_ID({0}) IS NULL CREATE DATABASE {1};' -f (ConvertTo-HccSqlLiteral $Database), $db),
    ('IF SUSER_ID({0}) IS NULL CREATE LOGIN {1} FROM WINDOWS WITH DEFAULT_DATABASE = {2};' -f (ConvertTo-HccSqlLiteral $ServiceAccount), $login, $db),
    ('USE {0}; IF USER_ID({1}) IS NULL CREATE USER {2} FOR LOGIN {2}; ALTER ROLE db_owner ADD MEMBER {2};' -f $db, (ConvertTo-HccSqlLiteral $ServiceAccount), $login)
)
if ($GrantDrillRights) { $steps += ('ALTER SERVER ROLE dbcreator ADD MEMBER {0};' -f $login) }

if (-not $PSCmdlet.ShouldProcess("$SqlInstance database [$Database]", 'Create database, login, permissions, backup folder')) {
    foreach ($s in $steps) { Write-Host "DRY RUN SQL: $s" }
    Write-Host "DRY RUN: create folder $BackupDirectory; grant Modify to $sqlService"
    return
}
foreach ($s in $steps) { Invoke-HccSql -Instance $SqlInstance -Query $s -TrustServerCertificate:$TrustServerCertificate | Out-Null }
if (-not (Test-Path -LiteralPath $BackupDirectory)) { New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null }
& icacls.exe $BackupDirectory /grant ("{0}:(OI)(CI)M" -f $sqlService) | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Warning "icacls could not grant $sqlService on $BackupDirectory; grant it manually (SQL Server must be able to write backups there)." }
Write-Host "Database [$Database] ready on $SqlInstance. Login $ServiceAccount has db_owner. Backup folder: $BackupDirectory"
