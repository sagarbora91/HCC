<#
Self-contained tests for the deployment scripts (no Pester needed: the hotel PC has Pester 3 only). No SQL Server, service,
certificate or scheduled task is touched: pure functions are unit-tested, every script is run once with -WhatIf.
Run:  powershell -NoProfile -File tests\Test-HccDeployment.ps1      (exit code 0 = all passed)
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'Hcc-Common.ps1')
. (Join-Path $root 'New-HccCertificate.ps1')   # dot-sourced: defines Get-HccCertificatePlan only
$script:failed = 0; $script:passed = 0
function Check([string]$Name, [scriptblock]$Test) {
    try { if (& $Test) { $script:passed++; Write-Host "PASS $Name" } else { $script:failed++; Write-Host "FAIL $Name" -ForegroundColor Red } }
    catch { $script:failed++; Write-Host "FAIL $Name : $($_.Exception.Message)" -ForegroundColor Red }
}
function Throws([scriptblock]$Test) { try { & $Test | Out-Null; return $false } catch { return $true } }
$now = [datetime]::SpecifyKind([datetime]'2026-10-25 12:00:00', 'Utc')

# --- safety -----------------------------------------------------------------------------------------------------------
Check 'database guard accepts Hcc and HccTest_ names' { (Assert-HccDatabaseName 'Hcc') -eq 'Hcc' -and (Assert-HccDatabaseName 'HccTest_1_ab') -eq 'HccTest_1_ab' }
Check 'database guard refuses ETP and system databases' { (Throws { Assert-HccDatabaseName 'EtpReporting' }) -and (Throws { Assert-HccDatabaseName 'EtpScratch_1' }) -and (Throws { Assert-HccDatabaseName 'master' }) -and (Throws { Assert-HccDatabaseName 'Hcc]; DROP' }) }
Check 'scratch guard only HccDrill_/HccTest_' { (Assert-HccScratchName 'HccDrill_20261025') -and (Throws { Assert-HccScratchName 'Hcc' }) -and (Throws { Assert-HccScratchName 'EtpScratch_x' }) }
Check 'sql literal and name quoting' { (ConvertTo-HccSqlLiteral "O'Brien") -eq "N'O''Brien'" -and (ConvertTo-HccSqlName 'a]b') -eq '[a]]b]' }

# --- file naming / retention / chain ----------------------------------------------------------------------------------
Check 'backup names' {
    (New-HccBackupFileName -Database Hcc -Type Full -UtcNow $now) -eq 'Hcc_FULL_20261025T120000Z.bak' -and
    (New-HccBackupFileName -Database Hcc -Type Differential -UtcNow $now) -eq 'Hcc_DIFF_20261025T120000Z.bak' -and
    (New-HccBackupFileName -Database Hcc -Type Full -UtcNow $now -Tag premigrate_0100_to_0200) -eq 'Hcc_PRE_20261025T120000Z_premigrate_0100_to_0200.bak' -and
    (Throws { New-HccBackupFileName -Database Hcc -Type Differential -UtcNow $now -Tag x }) -and (Throws { New-HccBackupFileName -Database Hcc -Type Full -UtcNow $now -Tag 'a b' })
}
Check 'name parsing' {
    $p = ConvertFrom-HccBackupFileName 'Hcc_PRE_20261025T120000Z_premigrate_0100_to_0200.bak'
    $p.Type -eq 'PRE' -and $p.Database -eq 'Hcc' -and $p.Tag -eq 'premigrate_0100_to_0200' -and $p.TimeUtc -eq $now -and ($null -eq (ConvertFrom-HccBackupFileName 'notes.txt'))
}
function Nm([string]$Type, [int]$DaysAgo) { New-HccBackupFileName -Database Hcc -Type $Type -UtcNow $now.AddDays(-$DaysAgo) }
Check 'retention deletes old fulls but keeps the newest three' {
    $names = @((Nm Full 1), (Nm Full 2), (Nm Full 40), (Nm Full 50), (Nm Full 60))
    $del = Select-HccBackupsToDelete -Names $names -UtcNow $now -RetentionDays 30 -KeepMinFulls 3
    ($del -contains (Nm Full 50)) -and ($del -contains (Nm Full 60)) -and -not ($del -contains (Nm Full 40)) -and $del.Count -eq 2
}
Check 'retention never deletes below the minimum even when all are old' {
    $names = @((Nm Full 100), (Nm Full 200), (Nm Full 300), (Nm Full 400))
    $del = Select-HccBackupsToDelete -Names $names -UtcNow $now -RetentionDays 30 -KeepMinFulls 3
    $del.Count -eq 1 -and $del[0] -eq (Nm Full 400)
}
Check 'retention drops differentials older than the oldest kept full' {
    $names = @((Nm Full 1), (Nm Full 10), (Nm Full 20), (Nm Differential 25), (Nm Differential 5), 'readme.txt')
    $del = Select-HccBackupsToDelete -Names $names -UtcNow $now -RetentionDays 30 -KeepMinFulls 3
    $del.Count -eq 1 -and $del[0] -eq (Nm Differential 25)
}
Check 'pre-migration backups expire after 90 days' {
    $old = New-HccBackupFileName -Database Hcc -Type Full -UtcNow $now.AddDays(-100) -Tag premigrate_1_to_2
    $new = New-HccBackupFileName -Database Hcc -Type Full -UtcNow $now.AddDays(-10) -Tag premigrate_2_to_3
    $del = Select-HccBackupsToDelete -Names @($old, $new, (Nm Full 1)) -UtcNow $now
    ($del -contains $old) -and -not ($del -contains $new)
}
Check 'restore chain = newest full + newest later differential; pre-migration backups are not part of it' {
    $names = @((Nm Full 3), (Nm Full 1), (Nm Differential 2), (Nm Differential 0.5), (New-HccBackupFileName -Database Hcc -Type Full -UtcNow $now -Tag t))
    $c = Select-HccRestoreChain -Names $names -Database Hcc
    $c.Full -eq (Nm Full 1) -and $c.Differential -eq (Nm Differential 0.5)
}
Check 'restore chain ignores a differential older than the newest full; none when no full' {
    $c = Select-HccRestoreChain -Names @((Nm Full 1), (Nm Differential 2)) -Database Hcc
    $c.Differential -eq $null -and ($null -eq (Select-HccRestoreChain -Names @((Nm Differential 2)) -Database Hcc))
}

# --- row counts -------------------------------------------------------------------------------------------------------
Check 'row counts: equal ok; missing table or empty restore fails; differences only reported' {
    $live = @{ 'a.t1' = 5; 'a.t2' = 0 }
    (Compare-HccRowCounts -Restored @{ 'a.t1' = 5; 'a.t2' = 0 } -Live $live).Ok -and
    (Compare-HccRowCounts -Restored @{ 'a.t1' = 4; 'a.t2' = 0 } -Live $live).Ok -and
    (Compare-HccRowCounts -Restored @{ 'a.t1' = 4; 'a.t2' = 0 } -Live $live).DifferingTables.Count -eq 1 -and
    -not (Compare-HccRowCounts -Restored @{ 'a.t1' = 5 } -Live $live).Ok -and
    -not (Compare-HccRowCounts -Restored @{ 'a.t1' = 0; 'a.t2' = 0 } -Live $live).Ok
}

# --- certificate plan -------------------------------------------------------------------------------------------------
Check 'certificate plan: names lower-cased, localhost and 127.0.0.1 added, IPs validated' {
    $p = Get-HccCertificatePlan -ServerName 'HOTEL-PC' -DnsName @('hcc.lan') -IpAddress @('192.168.1.20')
    ($p.DnsNames -contains 'hotel-pc') -and ($p.DnsNames -contains 'localhost') -and ($p.IpAddresses -contains '192.168.1.20') -and ($p.IpAddresses -contains '127.0.0.1') -and (Throws { Get-HccCertificatePlan -ServerName 'x' -IpAddress @('999.1.1.1') }) -and (Throws { Get-HccCertificatePlan -ServerName 'bad name!' })
}

# --- every script in dry-run mode ---------------------------------------------------------------------------------------
$psExe = (Get-Process -Id $PID).Path
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('hcc-ps-test-' + [guid]::NewGuid().ToString('N'))
function DryRun([string]$Script, [string[]]$ScriptArgs) {
    $ErrorActionPreference = 'Continue'
    $out = & $psExe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root $Script) @ScriptArgs -WhatIf 2>&1 | Out-String
    return [pscustomobject]@{ Exit = $LASTEXITCODE; Out = $out }
}
Check 'Backup-HccDatabase -WhatIf full: exit 0, prints plan, creates nothing' {
    $r = DryRun 'Backup-HccDatabase.ps1' @('-Type', 'Full', '-BackupDirectory', $tmp, '-ConfigPath', (Join-Path $tmp 'none.json'))
    $r.Exit -eq 0 -and $r.Out -match 'BACKUP DATABASE \[Hcc\]' -and $r.Out -match 'CHECKSUM' -and $r.Out -match 'RESTORE VERIFYONLY' -and -not (Test-Path $tmp)
}
Check 'Backup-HccDatabase -WhatIf differential + copy-only tag' {
    $d = DryRun 'Backup-HccDatabase.ps1' @('-Type', 'Differential', '-BackupDirectory', $tmp)
    $t = DryRun 'Backup-HccDatabase.ps1' @('-Type', 'Full', '-CopyOnly', '-Tag', 'premigrate_0100_to_0200', '-BackupDirectory', $tmp)
    $d.Exit -eq 0 -and $d.Out -match 'DIFFERENTIAL' -and $t.Exit -eq 0 -and $t.Out -match 'COPY_ONLY' -and $t.Out -match '_PRE_'
}
Check 'Backup-HccDatabase refuses an ETP database and a relative folder' {
    (DryRun 'Backup-HccDatabase.ps1' @('-Type', 'Full', '-Database', 'EtpReporting', '-BackupDirectory', $tmp)).Exit -ne 0 -and (DryRun 'Backup-HccDatabase.ps1' @('-Type', 'Full', '-BackupDirectory', 'rel\dir')).Exit -ne 0
}
Check 'Restore-HccDrill -WhatIf: exit 0, plan mentions scratch DB, CHECKDB, row counts' {
    $r = DryRun 'Restore-HccDrill.ps1' @('-BackupDirectory', $tmp)
    $r.Exit -eq 0 -and $r.Out -match 'HccDrill_' -and $r.Out -match 'DBCC CHECKDB' -and $r.Out -match 'row counts' -and -not (Test-Path $tmp)
}
Check 'Restore-HccDrill refuses a non-scratch name or the live name' {
    (DryRun 'Restore-HccDrill.ps1' @('-BackupDirectory', $tmp, '-ScratchName', 'EtpReporting')).Exit -ne 0 -and (DryRun 'Restore-HccDrill.ps1' @('-BackupDirectory', $tmp, '-ScratchName', 'Hcc')).Exit -ne 0
}
Check 'Register-HccBackupTasks -WhatIf lists three tasks' {
    $r = DryRun 'Register-HccBackupTasks.ps1' @()
    $r.Exit -eq 0 -and $r.Out -match 'HCC Backup Full' -and $r.Out -match 'HCC Backup Differential' -and $r.Out -match 'HCC Restore Drill'
}
Check 'New-HccCertificate -WhatIf prints names and files, creates nothing' {
    $r = DryRun 'New-HccCertificate.ps1' @('-ServerName', 'HOTEL-PC', '-IpAddress', '192.168.1.20', '-OutputDirectory', $tmp)
    $r.Exit -eq 0 -and $r.Out -match 'hotel-pc' -and $r.Out -match '192\.168\.1\.20' -and $r.Out -match 'HCC-Hotel-CA\.cer' -and -not (Test-Path $tmp)
}
Check 'Initialize-HccDatabase -WhatIf prints SQL only' {
    $r = DryRun 'Initialize-HccDatabase.ps1' @('-ServiceAccount', 'NT SERVICE\HccServer', '-BackupDirectory', 'D:\HccBackups', '-GrantDrillRights')
    $r.Exit -eq 0 -and $r.Out -match 'CREATE DATABASE \[Hcc\]' -and $r.Out -match 'db_owner' -and $r.Out -match 'dbcreator' -and $r.Out -match 'MSSQL\$SQLEXPRESS'
}
Check 'Initialize-HccDatabase refuses a non-HCC database' {
    (DryRun 'Initialize-HccDatabase.ps1' @('-Database', 'EtpReporting', '-ServiceAccount', 'x\y', '-BackupDirectory', 'D:\b')).Exit -ne 0
}
Check 'Install-HccService -WhatIf prints plan; -Uninstall -WhatIf changes nothing' {
    $r = DryRun 'Install-HccService.ps1' @()
    $u = DryRun 'Install-HccService.ps1' @('-Uninstall')
    $r.Exit -eq 0 -and $r.Out -match 'New-Service HccServer' -and $r.Out -match 'New-NetFirewallRule' -and $u.Exit -eq 0 -and $null -eq (Get-Service HccServer -ErrorAction SilentlyContinue)
}
Check 'Build-Installer without Inno Setup: skips with exit 0' {
    $out = & $psExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'installer\Build-Installer.ps1') -DryRun 2>&1 | Out-String
    $LASTEXITCODE -eq 0 -and $out -match 'DRY RUN'
}

Write-Host ''
Write-Host "Passed: $script:passed  Failed: $script:failed"
if ($script:failed -gt 0) { exit 1 }
exit 0
