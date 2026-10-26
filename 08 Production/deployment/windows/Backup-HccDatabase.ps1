<#
.SYNOPSIS
  Full or differential backup of the HCC database, verified, encrypted, with retention. Run by Task Scheduler (Register-HccBackupTasks.ps1).
.DESCRIPTION
  BACKUP DATABASE ... WITH CHECKSUM, then RESTORE VERIFYONLY ... WITH CHECKSUM, then (F-H03) encrypts the .bak into <name>.bak.hccenc
  (AES-256 + HMAC-SHA256 with the DPAPI-protected backup key from New-HccBackupKey.ps1; see Hcc-Common.ps1) and deletes the plain file,
  then deletes old backups per the retention rules in Hcc-Common.ps1. SQL Server Express has no native backup encryption, hence this step.
  The backup folder must be private (inheritance removed; SYSTEM, Administrators, the SQL Server service and the backup account only):
  Initialize-HccDatabase.ps1 sets that; this script warns in its log when the folder is no longer protected.
  The SQL Server service account (NT Service\MSSQL$SQLEXPRESS) writes the .bak itself. SQL Express has no SQL Agent, hence Task Scheduler.
  -WhatIf prints the plan and touches nothing (no connection to SQL Server, no files).
  -CopyOnly -Tag premigrate_X_to_Y is what the migration backup gate uses (does not disturb the differential chain).
  EncryptBackups=false in hcc-ops.json turns encryption off (not recommended: then BitLocker on the backup drive is the only protection).
.EXAMPLE
  .\Backup-HccDatabase.ps1 -Type Full
  .\Backup-HccDatabase.ps1 -Type Differential -WhatIf
# ETP-REUSE: replace/align with ETP's real backup script (naming, folder layout, retention, e-mail on failure) when V:\ETP is available.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][ValidateSet('Full', 'Differential')][string]$Type,
    [string]$SqlInstance, [string]$Database, [string]$BackupDirectory, [string]$LogDirectory,
    [int]$RetentionDays = -1, [int]$KeepMinFulls = -1,
    [switch]$CopyOnly, [string]$Tag, [switch]$TrustServerCertificate, [string]$ConfigPath,
    [string]$BackupKeyFile, [switch]$NoEncryption
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Hcc-Common.ps1"
$cfg = Get-HccOpsConfig -Path $ConfigPath
if (-not $SqlInstance) { $SqlInstance = $cfg.SqlInstance }
if (-not $Database) { $Database = $cfg.Database }
if (-not $BackupDirectory) { $BackupDirectory = $cfg.BackupDirectory }
if (-not $LogDirectory) { $LogDirectory = if ($cfg.LogDirectory) { $cfg.LogDirectory } elseif ($BackupDirectory) { Join-Path $BackupDirectory 'Logs' } else { '' } }
if ($RetentionDays -lt 0) { $RetentionDays = [int]$cfg.RetentionDays }
if ($KeepMinFulls -lt 0) { $KeepMinFulls = [int]$cfg.KeepMinFulls }
if (-not $BackupKeyFile) { $BackupKeyFile = if ($cfg.BackupKeyFile) { $cfg.BackupKeyFile } else { Get-HccDefaultBackupKeyFile } }
$encrypt = [bool]$cfg.EncryptBackups -and -not $NoEncryption
$explicitTrust = if ($PSBoundParameters.ContainsKey('TrustServerCertificate')) { [bool]$TrustServerCertificate } else { $null }
$trust = Resolve-HccTrustServerCertificate -Instance $SqlInstance -Explicit $explicitTrust -Configured $cfg.TrustServerCertificate   # A09-01
Assert-HccDatabaseName $Database | Out-Null
if (-not $BackupDirectory) { throw 'No backup folder: set BackupDirectory in hcc-ops.json or pass -BackupDirectory.' }
if (-not [IO.Path]::IsPathRooted($BackupDirectory)) { throw 'BackupDirectory must be a full path.' }
if ($Tag -and -not $CopyOnly) { throw '-Tag is for pre-migration backups and requires -CopyOnly.' }
if ($Tag -and $Type -ne 'Full') { throw 'A tagged backup is always Full.' }

$now = (Get-Date).ToUniversalTime()
$name = New-HccBackupFileName -Database $Database -Type $Type -UtcNow $now -Tag $Tag
$target = Join-Path $BackupDirectory $name
$log = if ($LogDirectory) { Join-Path $LogDirectory ('backup_{0:yyyyMM}.log' -f $now) } else { $null }
$sqlService = Get-HccSqlServiceAccount -Instance $SqlInstance

$with = @('CHECKSUM', 'INIT', 'STATS = 10')
if ($Type -eq 'Differential') { $with = @('DIFFERENTIAL') + $with }
if ($CopyOnly) { $with = @('COPY_ONLY') + $with }
$backupSql = 'BACKUP DATABASE {0} TO DISK = {1} WITH {2};' -f (ConvertTo-HccSqlName $Database), (ConvertTo-HccSqlLiteral $target), ($with -join ', ')
$verifySql = 'RESTORE VERIFYONLY FROM DISK = {0} WITH CHECKSUM;' -f (ConvertTo-HccSqlLiteral $target)

if (-not $PSCmdlet.ShouldProcess($target, "$Type backup of [$Database] on $SqlInstance")) {
    Write-HccLog "DRY RUN: would run on ${SqlInstance}: $backupSql" -LogFile $null
    Write-HccLog "DRY RUN: sqlcmd trusts the server certificate (-C): $(if ($trust) { 'yes' } else { 'no' })" -LogFile $null
    Write-HccLog "DRY RUN: would run: $verifySql" -LogFile $null
    if ($encrypt) { Write-HccLog "DRY RUN: would encrypt it to $target$script:HccEncryptedSuffix (AES-256 + HMAC-SHA256, key $BackupKeyFile) and delete the plain .bak" -LogFile $null }
    else { Write-HccLog 'DRY RUN: encryption is OFF (EncryptBackups=false): the .bak stays readable to anyone who can open the folder' -LogFile $null }
    Write-HccLog "DRY RUN: would check that $BackupDirectory is private (SYSTEM, Administrators, $sqlService$(if ($cfg.BackupAccount) { ', ' + $cfg.BackupAccount }))" -LogFile $null
    Write-HccLog "DRY RUN: would keep >= $KeepMinFulls full backups and delete older than $RetentionDays days from $BackupDirectory" -LogFile $null
    return
}

$key = $null
try {
    if (-not (Test-Path -LiteralPath $BackupDirectory)) { New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null }
    $aclProblems = @(Test-HccProtectedDirectory -Path $BackupDirectory -AllowedAccounts @($sqlService, $cfg.BackupAccount))
    if ($aclProblems.Count -gt 0) { Write-HccLog ("Backup folder is NOT private: " + ($aclProblems -join '; ') + '. Re-run Initialize-HccDatabase.ps1 (it sets the folder permissions).') -LogFile $log -Level WARN }
    if ($encrypt) { $key = Read-HccBackupKey -Path $BackupKeyFile }   # before the backup: no plain .bak is left behind when the key is missing

    $existing = @(Get-ChildItem -LiteralPath $BackupDirectory -Filter '*.bak*' -File | ForEach-Object Name)
    if ($Type -eq 'Differential' -and -not ($existing | Where-Object { (ConvertFrom-HccBackupFileName $_) -and (ConvertFrom-HccBackupFileName $_).Type -eq 'FULL' })) {
        Write-HccLog 'No full backup in the folder yet; taking a FULL backup instead of a differential.' -LogFile $log -Level WARN
        $Type = 'Full'
        $name = New-HccBackupFileName -Database $Database -Type Full -UtcNow $now
        $target = Join-Path $BackupDirectory $name
        $backupSql = 'BACKUP DATABASE {0} TO DISK = {1} WITH CHECKSUM, INIT, STATS = 10;' -f (ConvertTo-HccSqlName $Database), (ConvertTo-HccSqlLiteral $target)
        $verifySql = 'RESTORE VERIFYONLY FROM DISK = {0} WITH CHECKSUM;' -f (ConvertTo-HccSqlLiteral $target)
    }
    Write-HccLog "Starting $Type backup of [$Database] -> $target" -LogFile $log
    Invoke-HccSql -Instance $SqlInstance -Query $backupSql -TrustServerCertificate:$trust | Out-Null
    Invoke-HccSql -Instance $SqlInstance -Query $verifySql -TrustServerCertificate:$trust | Out-Null
    $size = (Get-Item -LiteralPath $target).Length
    Write-HccLog ("Backup OK and verified: {0} ({1:N1} MB)" -f $name, ($size / 1MB)) -LogFile $log
    $result = $target
    if ($encrypt) {
        $enc = $target + $script:HccEncryptedSuffix
        Protect-HccBackupFile -Source $target -Destination $enc -Key $key
        Remove-Item -LiteralPath $target -Force
        Write-HccLog ("Encrypted: {0} ({1:N1} MB); plain .bak deleted." -f (Split-Path -Leaf $enc), ((Get-Item -LiteralPath $enc).Length / 1MB)) -LogFile $log
        $result = $enc
    }

    $all = @(Get-ChildItem -LiteralPath $BackupDirectory -Filter '*.bak*' -File | Where-Object { $_.Name -like "${Database}_*" } | ForEach-Object Name)
    $del = Select-HccBackupsToDelete -Names $all -UtcNow $now -RetentionDays $RetentionDays -KeepMinFulls $KeepMinFulls -PreMigrateRetentionDays ([int]$cfg.PreMigrateRetentionDays)
    foreach ($d in $del) {
        Remove-Item -LiteralPath (Join-Path $BackupDirectory $d) -Force
        Write-HccLog "Retention: deleted $d" -LogFile $log
    }
    Write-Output $result
}
catch {
    Write-HccLog ("Backup FAILED: " + $_.Exception.Message) -LogFile $log -Level ERROR
    throw
}
finally { if ($key) { [Array]::Clear($key, 0, $key.Length) } }
