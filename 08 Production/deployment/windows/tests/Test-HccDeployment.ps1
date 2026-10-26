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

# --- name constraints (F-M04) -----------------------------------------------------------------------------------------------
Check 'name constraints: CIDR to address+mask bytes, invalid input refused' {
    $b = ConvertTo-HccIpConstraintBytes '192.168.1.77/24'
    $one = ConvertTo-HccIpConstraintBytes '192.168.1.20'
    (($b -join ',') -eq '192,168,1,0,255,255,255,0') -and (($one -join ',') -eq '192,168,1,20,255,255,255,255') -and (ConvertTo-HccIpConstraintBytes 'fd00::/8').Length -eq 32 -and
    (Throws { ConvertTo-HccIpConstraintBytes '300.1.1.1' }) -and (Throws { ConvertTo-HccIpConstraintBytes '10.0.0.0/33' })
}
Check 'name constraints: critical extension permits only the hotel names and addresses, excludes all IPv6' {
    $ext = New-HccNameConstraintsExtension -DnsNames @('hotel-pc', 'localhost') -IpNetworks @('192.168.1.20', '127.0.0.1')
    $der = $ext.RawData
    $text = $ext.Format($true)
    $ext.Critical -and $ext.Oid.Value -eq '2.5.29.30' -and $der[0] -eq 0x30 -and $der[2] -eq 0xA0 -and
    $text -match 'hotel-pc' -and $text -match 'localhost' -and $text -match '192\.168\.1\.20' -and $text -match '255\.255\.255\.255' -and $text -match 'Excluded'
}

# --- backup encryption and folder protection (F-H03, F-M06) -------------------------------------------------------------------
Check 'encrypted backup names parse and form the restore chain' {
    $f = (Nm Full 1) + '.hccenc'; $d = (Nm Differential 0.5) + '.hccenc'
    $p = ConvertFrom-HccBackupFileName $f
    $c = Select-HccRestoreChain -Names @($f, $d, (Nm Full 3)) -Database Hcc
    $p.Encrypted -and $p.Type -eq 'FULL' -and -not (ConvertFrom-HccBackupFileName (Nm Full 1)).Encrypted -and $c.Full -eq $f -and $c.Differential -eq $d -and
    $null -eq (ConvertFrom-HccBackupFileName 'Hcc_FULL_20261025T120000Z.bak.zip')
}
$me = [Security.Principal.WindowsIdentity]::GetCurrent().Name
Check 'backup encryption: round trip (multi-block and odd sizes), tamper and wrong key refused without output' {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('HccEncTest_' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        $key = New-HccRandomBytes 32
        $ok = $true
        foreach ($size in @(0, 15, 16, 1048576, 2621443)) {
            $src = Join-Path $dir "s$size.bak"; $enc = "$src.hccenc"; $back = Join-Path $dir "b$size.bak"
            $data = New-HccRandomBytes ([Math]::Max($size, 1)); if ($size -eq 0) { $data = New-Object byte[] 0 }
            [IO.File]::WriteAllBytes($src, $data)
            Protect-HccBackupFile -Source $src -Destination $enc -Key $key
            Unprotect-HccBackupFile -Source $enc -Destination $back -Key $key
            $same = [Convert]::ToBase64String([IO.File]::ReadAllBytes($back)) -eq [Convert]::ToBase64String($data)
            $expected = 64 + 16 * ([Math]::Floor($size / 16) + 1)
            $ok = $ok -and $same -and ((Get-Item -LiteralPath $enc).Length -eq $expected)
        }
        $enc = Join-Path $dir 's1048576.bak.hccenc'
        $bytes = [IO.File]::ReadAllBytes($enc); $bytes[5000] = $bytes[5000] -bxor 1
        $bad = Join-Path $dir 'tampered.hccenc'; [IO.File]::WriteAllBytes($bad, $bytes)
        $tamper = $false; try { Unprotect-HccBackupFile -Source $bad -Destination (Join-Path $dir 't.bak') -Key $key } catch { $tamper = $_.Exception.Message -match 'damaged or was altered' }
        $wrong = $false; try { Unprotect-HccBackupFile -Source $enc -Destination (Join-Path $dir 'w.bak') -Key (New-HccRandomBytes 32) } catch { $wrong = $_.Exception.Message -match 'different backup key' }
        $noExisting = Throws { Protect-HccBackupFile -Source (Join-Path $dir 's15.bak') -Destination $enc -Key $key }
        $ok -and $tamper -and $wrong -and $noExisting -and -not (Test-Path (Join-Path $dir 't.bak')) -and -not (Test-Path (Join-Path $dir 'w.bak'))
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
}
Check 'backup key: DPAPI key file round trip, never overwritten' {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('HccKeyFile_' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        $key = New-HccRandomBytes 32
        $file = Join-Path $dir 'k.txt'
        $fp = Save-HccBackupKey -Key $key -Path $file -ReaderAccounts @($me)
        $back = Read-HccBackupKey -Path $file
        $text = Get-Content -LiteralPath $file -Raw
        $acl = Get-Acl -LiteralPath $file
        ([Convert]::ToBase64String($back) -eq [Convert]::ToBase64String($key)) -and $fp -eq (Get-HccBackupSubKeys $key).FingerprintHex -and -not $text.Contains([Convert]::ToBase64String($key)) -and
        $acl.AreAccessRulesProtected -and (Throws { Save-HccBackupKey -Key $key -Path $file -ReaderAccounts @($me) }) -and (Throws { Read-HccBackupKey -Path (Join-Path $dir 'none.txt') })
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
}
Check 'protected folder: icacls arguments remove inheritance and grant only SYSTEM, Administrators and the named accounts' {
    $a = Get-HccProtectedDirectoryArgs -Path 'D:\HccBackups' -ModifyAccounts @('NT SERVICE\MSSQL$SQLEXPRESS', '') -ReadAccounts @('X\y')
    $a[0] -eq 'D:\HccBackups' -and $a -contains '/inheritance:r' -and $a -contains '/grant:r' -and $a -contains '*S-1-5-18:(OI)(CI)F' -and $a -contains '*S-1-5-32-544:(OI)(CI)F' -and
    $a -contains 'NT SERVICE\MSSQL$SQLEXPRESS:(OI)(CI)M' -and $a -contains 'X\y:(OI)(CI)RX' -and $a.Count -eq 7 -and (Get-HccSqlServiceAccount '.\SQLEXPRESS') -eq 'NT SERVICE\MSSQL$SQLEXPRESS' -and (Get-HccSqlServiceAccount 'PC') -eq 'NT SERVICE\MSSQLSERVER'
}
Check 'protected folder: a new temp folder is reported, the protected one passes, an extra account is reported' {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('HccAcl_' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        $before = @(Test-HccProtectedDirectory -Path $dir -AllowedAccounts @($me))
        Set-HccProtectedDirectory -Path $dir -ModifyAccounts @($me)
        Set-Content -LiteralPath (Join-Path $dir 'a.bak') -Value 'x'
        $after = @(Test-HccProtectedDirectory -Path $dir -AllowedAccounts @($me))
        $strict = @(Test-HccProtectedDirectory -Path $dir)
        ($before -match 'inherits').Count -ge 1 -and $after.Count -eq 0 -and $strict.Count -ge 1
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
}
Check 'admin-only script folder: a user temp folder is refused, the Windows PowerShell folder passes' {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('HccOps_' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        Set-Content -LiteralPath (Join-Path $dir 'Backup-HccDatabase.ps1') -Value '#'
        $user = @(Test-HccAdminOnlyWritable -Path $dir)
        $sys = @(Test-HccAdminOnlyWritable -Path (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0'))
        $user.Count -ge 1 -and ($user -join ' ') -match 'can change' -and $sys.Count -eq 0 -and (@(Test-HccAdminOnlyWritable -Path (Join-Path $dir 'nope'))).Count -eq 1
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
}
Check 'runtime SQL grants: read, write, execute; never db_owner' {
    $g = Get-HccRuntimeGrantSql -User '[NT SERVICE\HccServer]'
    $g -match 'db_datareader' -and $g -match 'db_datawriter' -and $g -match 'GRANT EXECUTE' -and $g -notmatch 'db_owner'
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
    $r.Exit -eq 0 -and $r.Out -match 'BACKUP DATABASE \[Hcc\]' -and $r.Out -match 'CHECKSUM' -and $r.Out -match 'RESTORE VERIFYONLY' -and $r.Out -match 'encrypt it to .*\.bak\.hccenc' -and $r.Out -match 'delete the plain \.bak' -and $r.Out -match 'private' -and -not (Test-Path $tmp)
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
    $r.Exit -eq 0 -and $r.Out -match 'HccDrill_' -and $r.Out -match 'DBCC CHECKDB' -and $r.Out -match 'row counts' -and $r.Out -match 'decrypted' -and -not (Test-Path $tmp)
}
Check 'Restore-HccDrill refuses a non-scratch name or the live name' {
    (DryRun 'Restore-HccDrill.ps1' @('-BackupDirectory', $tmp, '-ScratchName', 'EtpReporting')).Exit -ne 0 -and (DryRun 'Restore-HccDrill.ps1' @('-BackupDirectory', $tmp, '-ScratchName', 'Hcc')).Exit -ne 0
}
Check 'Register-HccBackupTasks -WhatIf lists three tasks from the Program Files folder' {
    $r = DryRun 'Register-HccBackupTasks.ps1' @()
    $r.Exit -eq 0 -and $r.Out -match 'HCC Backup Full' -and $r.Out -match 'HCC Backup Differential' -and $r.Out -match 'HCC Restore Drill' -and $r.Out -match 'Program Files\\HCC\\Operations\\Backup-HccDatabase\.ps1' -and $r.Out -match 'folder check'
}
Check 'Register-HccBackupTasks refuses a script folder that normal users can change' {
    $r = DryRun 'Register-HccBackupTasks.ps1' @('-ScriptDirectory', $root)
    $r.Exit -eq 0 -and $r.Out -match 'would be REFUSED' -and $r.Out -match 'can change'
}
Check 'Register-HccBackupTasks -InstallScripts -WhatIf plans the copy to an admin-only folder' {
    $r = DryRun 'Register-HccBackupTasks.ps1' @('-InstallScripts')
    $r.Exit -eq 0 -and $r.Out -match 'copy .*Backup-HccDatabase\.ps1' -and $r.Out -match 'inheritance removed'
}
Check 'New-HccCertificate -WhatIf prints names and files, creates nothing' {
    $r = DryRun 'New-HccCertificate.ps1' @('-ServerName', 'HOTEL-PC', '-IpAddress', '192.168.1.20', '-OutputDirectory', $tmp)
    $r.Exit -eq 0 -and $r.Out -match 'hotel-pc' -and $r.Out -match '192\.168\.1\.20' -and $r.Out -match 'HCC-Hotel-CA\.cer' -and $r.Out -match 'critical Name Constraints' -and
    $r.Out -match 'remove the CA key from this PC' -and $r.Out -match 'valid 397 days' -and $r.Out -match 'non-exportable' -and -not (Test-Path $tmp)
}
Check 'New-HccCertificate -RenewServer -WhatIf: non-exportable CA import, removed again; needs -CaPfx' {
    $r = DryRun 'New-HccCertificate.ps1' @('-ServerName', 'HOTEL-PC', '-IpAddress', '192.168.1.20', '-OutputDirectory', $tmp, '-RenewServer', '-CaPfx', 'E:\HCC-Hotel-CA.pfx', '-UpdateConfig', 'C:\x.json')
    $n = DryRun 'New-HccCertificate.ps1' @('-ServerName', 'HOTEL-PC', '-IpAddress', '192.168.1.20', '-RenewServer')
    $r.Exit -eq 0 -and $r.Out -match 'non-exportable, then remove it' -and $r.Out -match 'AllowedHosts=hotel-pc;localhost;192\.168\.1\.20;127\.0\.0\.1' -and $n.Exit -ne 0 -and -not (Test-Path $tmp)
}
Check 'Initialize-HccDatabase -WhatIf prints SQL only' {
    $r = DryRun 'Initialize-HccDatabase.ps1' @('-ServiceAccount', 'NT SERVICE\HccServer', '-BackupDirectory', 'D:\HccBackups', '-GrantDrillRights')
    $r.Exit -eq 0 -and $r.Out -match 'CREATE DATABASE \[Hcc\]' -and $r.Out -match 'db_datareader' -and $r.Out -match 'GRANT EXECUTE' -and $r.Out -notmatch 'ADD MEMBER \[NT SERVICE\\HccServer\]; ALTER ROLE db_owner' -and
    $r.Out -match 'db_owner DROP MEMBER' -and $r.Out -match 'dbcreator ADD MEMBER \[NT AUTHORITY\\SYSTEM\]' -and $r.Out -match 'db_backupoperator' -and $r.Out -match 'MSSQL\$SQLEXPRESS:\(OI\)\(CI\)M' -and
    $r.Out -match '/inheritance:r' -and $r.Out -match 'Hcc\.Api\.exe migrate' -and $r.Out -match 'Initialize-HccProperty'
}
Check 'Initialize-HccDatabase -RuntimeDbOwner keeps the legacy grant; a dedicated backup account gets folder rights' {
    $r = DryRun 'Initialize-HccDatabase.ps1' @('-ServiceAccount', 'NT SERVICE\HccServer', '-BackupDirectory', 'D:\HccBackups', '-RuntimeDbOwner', '-BackupAccount', 'HOTELPC\svc-backup')
    $r.Exit -eq 0 -and $r.Out -match 'ALTER ROLE db_owner ADD MEMBER \[NT SERVICE\\HccServer\]' -and $r.Out -match 'HOTELPC\\svc-backup:\(OI\)\(CI\)M'
}
Check 'Initialize-HccDatabase refuses a non-HCC database' {
    (DryRun 'Initialize-HccDatabase.ps1' @('-Database', 'EtpReporting', '-ServiceAccount', 'x\y', '-BackupDirectory', 'D:\b')).Exit -ne 0
}
Check 'Initialize-HccProperty -WhatIf prints the setup request, password hidden, sends nothing' {
    $r = DryRun 'Initialize-HccProperty.ps1' @('-HotelName', 'Hotel Sagar', '-Address', 'Station Road, Latur', '-OwnerDisplayName', 'Sagar', '-Gstin', '27aaaaa0000a1z2')
    $r.Exit -eq 0 -and $r.Out -match 'POST http://127\.0\.0\.1:5080/api/v1/setup/SetupProperty' -and $r.Out -match '27AAAAA0000A1Z2' -and $r.Out -match 'Maharashtra' -and $r.Out -match 'Asia/Kolkata' -and $r.Out -match '\*{8}' -and $r.Out -match 'LoopbackHttpPort to 0'
}
Check 'Initialize-HccProperty refuses a server address that is not this PC' {
    (DryRun 'Initialize-HccProperty.ps1' @('-HotelName', 'H', '-Address', 'A', '-OwnerDisplayName', 'S', '-ServerUrl', 'https://192.168.1.20:5443')).Exit -ne 0
}
Check 'New-HccGuestIdKey -WhatIf prints the plan and writes nothing' {
    $s = Join-Path $tmp 'server\appsettings.Production.json'; $off = Join-Path $tmp 'usb\key.txt'
    New-Item -ItemType Directory -Path (Join-Path $tmp 'server') -Force | Out-Null; Set-Content -LiteralPath $s -Value '{"Hcc":{"Sql":{"ConnectionString":"x"}}}'
    $r = DryRun 'New-HccGuestIdKey.ps1' @('-SettingsFile', $s, '-OfflineCopyPath', $off, '-KeyId', '2026-10')
    $ok = $r.Exit -eq 0 -and $r.Out -match 'DRY RUN' -and $r.Out -match 'DPAPI' -and -not (Test-Path $off) -and ((Get-Content -LiteralPath $s -Raw) -notmatch 'GuestIdKeys')
    Remove-Item -LiteralPath $tmp -Recurse -Force; $ok
}
Check 'New-HccGuestIdKey installs a DPAPI machine key, writes the offline copy, refuses a second run, rotates with -Rotate' {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('HccKeyTest_' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path "$dir\server", "$dir\usb" -Force | Out-Null
    $s = "$dir\server\appsettings.Production.json"; Set-Content -LiteralPath $s -Value '{"Hcc":{"Sql":{"ConnectionString":"x"}},"_notes":["a"]}'
    $script = Join-Path $root 'New-HccGuestIdKey.ps1'
    $ErrorActionPreference = 'Continue'
    try {
        $r1 = & $psExe -NoProfile -ExecutionPolicy Bypass -File $script -SettingsFile $s -OfflineCopyPath "$dir\usb\k1.txt" -KeyId '2026-10' -SkipAcl 2>&1 | Out-String
        $x1 = $LASTEXITCODE
        $c = Get-Content -LiteralPath $s -Raw | ConvertFrom-Json
        $wrapped = $c.Hcc.GuestIdKeys.Keys.'2026-10'
        $rawB64 = (((Get-Content "$dir\usb\k1.txt") | Where-Object { $_ -like 'Raw key*' }) -split ': ')[1]
        $raw = [Convert]::FromBase64String($rawB64)
        Add-Type -AssemblyName System.Security
        $back = [Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($wrapped.Substring('dpapi-machine:'.Length)), [Text.Encoding]::ASCII.GetBytes('HCC guest ID key v1'), 'LocalMachine')
        $roundTrip = $x1 -eq 0 -and $c.Hcc.GuestIdKeys.Current -eq '2026-10' -and $wrapped.StartsWith('dpapi-machine:') -and $raw.Length -eq 32 -and ([Convert]::ToBase64String($back) -eq $rawB64) -and -not $r1.Contains($rawB64) -and $c.Hcc.Sql.ConnectionString -eq 'x'
        $again = & $psExe -NoProfile -ExecutionPolicy Bypass -File $script -SettingsFile $s -OfflineCopyPath "$dir\usb\k2.txt" -SkipAcl 2>&1 | Out-String
        $refused = $LASTEXITCODE -ne 0 -and $again -match 'already configured' -and -not (Test-Path "$dir\usb\k2.txt")
        $inside = & $psExe -NoProfile -ExecutionPolicy Bypass -File $script -SettingsFile $s -OfflineCopyPath "$dir\server\k3.txt" -SkipAcl 2>&1 | Out-String
        $insideRefused = $LASTEXITCODE -ne 0 -and $inside -match 'NOT be inside'
        & $psExe -NoProfile -ExecutionPolicy Bypass -File $script -SettingsFile $s -OfflineCopyPath "$dir\usb\k4.txt" -KeyId '2027-10' -Rotate -SkipAcl 2>&1 | Out-Null
        $x4 = $LASTEXITCODE
        $c2 = Get-Content -LiteralPath $s -Raw | ConvertFrom-Json
        $rotated = $x4 -eq 0 -and $c2.Hcc.GuestIdKeys.Current -eq '2027-10' -and $c2.Hcc.GuestIdKeys.Keys.'2026-10' -eq $wrapped -and [bool]$c2.Hcc.GuestIdKeys.Keys.'2027-10'
        $roundTrip = $roundTrip -and $c.Hcc.GuestIdKeys.Required -eq $true -and $c.Hcc.GuestIdKeys.AllowMachineScope -eq $true   # F-M05: explicit machine-scope opt-in
        $roundTrip -and $refused -and $insideRefused -and $rotated
    } finally { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
}
Check 'Install-HccService -WhatIf prints plan; -Uninstall -WhatIf changes nothing' {
    $r = DryRun 'Install-HccService.ps1' @()
    $u = DryRun 'Install-HccService.ps1' @('-Uninstall')
    $r.Exit -eq 0 -and $r.Out -match 'New-Service HccServer' -and $r.Out -match 'New-NetFirewallRule' -and $r.Out -match 'appsettings\.Production\.json /inheritance:r' -and $u.Exit -eq 0 -and $null -eq (Get-Service HccServer -ErrorAction SilentlyContinue)
}
Check 'New-HccBackupKey: -WhatIf writes nothing; creates key + offline copy; refuses a second key; imports on another PC' {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('HccBkKey_' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path "$dir\keys", "$dir\keys2", "$dir\usb" -Force | Out-Null
    $script = Join-Path $root 'New-HccBackupKey.ps1'
    $ErrorActionPreference = 'Continue'
    try {
        $w = & $psExe -NoProfile -ExecutionPolicy Bypass -File $script -OfflineCopyPath "$dir\usb\k.txt" -KeyFile "$dir\keys\k.txt" -WhatIf 2>&1 | Out-String
        $dry = $LASTEXITCODE -eq 0 -and $w -match 'DRY RUN' -and -not (Test-Path "$dir\usb\k.txt") -and -not (Test-Path "$dir\keys\k.txt")
        $o = & $psExe -NoProfile -ExecutionPolicy Bypass -File $script -OfflineCopyPath "$dir\usb\k.txt" -KeyFile "$dir\keys\k.txt" -ReaderAccount $me 2>&1 | Out-String
        $made = $LASTEXITCODE -eq 0 -and (Test-Path "$dir\usb\k.txt") -and (Test-Path "$dir\keys\k.txt")
        $raw = (((Get-Content "$dir\usb\k.txt") | Where-Object { $_ -like 'Raw key*' }) -split ': ', 2)[1]
        $key = Read-HccBackupKey -Path "$dir\keys\k.txt"
        $match = [Convert]::ToBase64String($key) -eq $raw -and -not $o.Contains($raw)
        & $psExe -NoProfile -ExecutionPolicy Bypass -File $script -OfflineCopyPath "$dir\usb\k2.txt" -KeyFile "$dir\keys\k.txt" 2>&1 | Out-Null
        $refused = $LASTEXITCODE -ne 0 -and -not (Test-Path "$dir\usb\k2.txt")
        & $psExe -NoProfile -ExecutionPolicy Bypass -File $script -ImportOfflineCopy "$dir\usb\k.txt" -KeyFile "$dir\keys2\k.txt" -ReaderAccount $me 2>&1 | Out-Null
        $imported = $LASTEXITCODE -eq 0 -and ([Convert]::ToBase64String((Read-HccBackupKey -Path "$dir\keys2\k.txt")) -eq $raw)
        $dry -and $made -and $match -and $refused -and $imported
    }
    finally { $ErrorActionPreference = 'Stop'; Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
}
Check 'Build-Installer without Inno Setup: skips with exit 0' {
    $out = & $psExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'installer\Build-Installer.ps1') -DryRun 2>&1 | Out-String
    $LASTEXITCODE -eq 0 -and $out -match 'DRY RUN'
}

Write-Host ''
Write-Host "Passed: $script:passed  Failed: $script:failed"
if ($script:failed -gt 0) { exit 1 }
exit 0
