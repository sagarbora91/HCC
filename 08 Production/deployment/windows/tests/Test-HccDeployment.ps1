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

# --- A07-03 / A09-07: guest ID check after a restore (Restore-HccDrill.ps1 -> Hcc.Api.exe verify-guest-id) ------------------------
Check 'A07-03 guest ID check: verify-guest-id arguments name the scratch database, the instance and the server folder' {
    $a = Get-HccGuestIdCheckArgs -Database 'HccDrill_20261026' -SqlInstance '.\SQLEXPRESS' -ServerDirectory 'C:\Program Files\HCC\Server'
    ($a -join ' ') -eq 'verify-guest-id --database HccDrill_20261026 --server .\SQLEXPRESS --environment Production --contentRoot C:\Program Files\HCC\Server' -and $a.Count -eq 9 -and
    (Get-HccDefaultServerDirectory) -eq (Join-Path $env:ProgramFiles 'HCC\Server') -and (Get-HccOpsConfig -Path (Join-Path $root 'hcc-ops.sample.json')).ServerDirectory -eq 'C:\Program Files\HCC\Server' -and
    (Get-HccOpsConfig -Path (Join-Path $env:TEMP 'no-such-hcc-ops.json')).ServerDirectory -eq ''
}
Check 'A07-03 guest ID check: OK and "no guest IDs" pass; FAILED, a wrong exit code or no result line fail with a clear message' {
    $ok = ConvertFrom-HccGuestIdCheckOutput -Lines @('noise', 'Guest ID check: OK (1 ID opened)') -ExitCode 0
    $none = ConvertFrom-HccGuestIdCheckOutput -Lines @('Guest ID check: no guest IDs to check (the database holds no sealed ID numbers).') -ExitCode 0
    $bad = ConvertFrom-HccGuestIdCheckOutput -Lines @("Guest ID check: FAILED - 3 ID number(s) are sealed with key '2026-10', which is not installed on this server.") -ExitCode 1
    $mixed = ConvertFrom-HccGuestIdCheckOutput -Lines @('Guest ID check: OK (1 ID opened)') -ExitCode 1
    $silent = ConvertFrom-HccGuestIdCheckOutput -Lines @('', 'You must install or update .NET to run this application.') -ExitCode -2147450730
    $empty = ConvertFrom-HccGuestIdCheckOutput -Lines @() -ExitCode 1
    $ok.Ok -and $ok.Status -eq 'ok' -and $ok.Message -eq 'Guest ID check: OK (1 ID opened)' -and $none.Ok -and $none.Status -eq 'no guest IDs' -and
    -not $bad.Ok -and $bad.Status -eq 'FAILED' -and $bad.Message -match "key '2026-10'" -and -not $mixed.Ok -and $mixed.Message -match 'unexpected result \(exit code 1\)' -and
    -not $silent.Ok -and $silent.Message -match 'no result: You must install or update \.NET' -and -not $empty.Ok -and $empty.Message -match 'exit code 1 and no result$'
}
Check 'A07-03 guest ID check: a missing Hcc.Api.exe, or a server folder normal users can change (F-M06), is refused before anything runs' {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('HccSrv_' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        $missing = Invoke-HccGuestIdCheck -ServerDirectory $dir -Database 'HccDrill_1' -SqlInstance '.\SQLEXPRESS'
        Set-Content -LiteralPath (Join-Path $dir 'Hcc.Api.exe') -Value 'not a program'
        $unsafe = Invoke-HccGuestIdCheck -ServerDirectory $dir -Database 'HccDrill_1' -SqlInstance '.\SQLEXPRESS'
        $exe = @(Test-HccAdminOnlyWritable -Path $dir -Extensions @('.exe')) -join ' '
        $default = @(Test-HccAdminOnlyWritable -Path $dir) -join ' '
        -not $missing.Ok -and $missing.Status -eq 'FAILED' -and $missing.Message -match 'Hcc\.Api\.exe not found' -and $missing.Message -match 'ServerDirectory' -and
        -not $unsafe.Ok -and $unsafe.Message -match 'refusing to run' -and $unsafe.Message -match 'can change' -and $exe -match 'Hcc\.Api\.exe' -and $default -notmatch 'Hcc\.Api\.exe'
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
}

# --- A09-01: sqlcmd trusts the server certificate of a LOCAL instance by default --------------------------------------------
Check 'A09-01 sqlcmd trust: a local instance (., (local), localhost, 127.0.0.1, this PC, with or without \INSTANCE) is trusted by default (-C)' {
    $local = @('.', '.\SQLEXPRESS', '(local)', '(local)\SQLEXPRESS', 'localhost', 'LOCALHOST\HCC', '127.0.0.1', '127.0.0.1,1433', 'tcp:127.0.0.1\SQLEXPRESS', 'lpc:.\SQLEXPRESS', "$env:COMPUTERNAME", "$($env:COMPUTERNAME.ToLowerInvariant())\SQLEXPRESS")
    $all = @($local | Where-Object { -not (Test-HccLocalSqlInstance $_) -or -not (Resolve-HccTrustServerCertificate -Instance $_ -Explicit $null -Configured $null) }).Count -eq 0
    $a = Get-HccSqlcmdArgs -Instance '.\SQLEXPRESS' -Query 'SELECT 1' -TrustServerCertificate:(Resolve-HccTrustServerCertificate -Instance '.\SQLEXPRESS')
    $all -and ($a -contains '-C') -and $null -eq (Get-HccOpsConfig -Path (Join-Path $env:TEMP 'no-such-hcc-ops.json')).TrustServerCertificate
}
Check 'A09-01 sqlcmd trust: an explicit false (parameter or hcc-ops.json) wins over the local default' {
    (-not (Resolve-HccTrustServerCertificate -Instance '.\SQLEXPRESS' -Explicit $false -Configured $true)) -and
    (-not (Resolve-HccTrustServerCertificate -Instance '.\SQLEXPRESS' -Configured $false)) -and
    (-not (Resolve-HccTrustServerCertificate -Instance 'localhost' -Configured 'false')) -and
    -not ((Get-HccSqlcmdArgs -Instance '.\SQLEXPRESS' -Query 'SELECT 1' -TrustServerCertificate:$false) -contains '-C')
}
Check 'A09-01 sqlcmd trust: a remote instance gets no -C unless asked; the sample hcc-ops.json turns it on for the local instance' {
    $remote = @('DBSERVER\SQLEXPRESS', '192.168.1.30', 'tcp:sql.hotel.local,1433', 'np:\\DBSERVER\pipe\sql\query', '')
    $none = @($remote | Where-Object { (Test-HccLocalSqlInstance $_) -or (Resolve-HccTrustServerCertificate -Instance $_) }).Count -eq 0
    $sample = Get-HccOpsConfig -Path (Join-Path $root 'hcc-ops.sample.json')
    $none -and (Resolve-HccTrustServerCertificate -Instance 'DBSERVER\SQLEXPRESS' -Explicit $true) -and (Resolve-HccTrustServerCertificate -Instance 'DBSERVER' -Configured $true) -and
    $sample.TrustServerCertificate -eq $true -and (Test-HccLocalSqlInstance $sample.SqlInstance)
}

# --- A09-02: no differential starts with the full backup or the restore drill ------------------------------------------------
Check 'A09-02 schedule: differentials every 4 h from 04:30 = 00:30 04:30 08:30 12:30 16:30 20:30, none at the 02:30 full or the 03:30 drill' {
    $t = Get-HccRepetitionTimes -Start '04:30' -EveryHours 4
    ($t -join ' ') -eq '00:30 04:30 08:30 12:30 16:30 20:30' -and
    (Test-HccBackupSchedule -FullTime '02:30' -DifferentialStart '04:30' -DifferentialEveryHours 4 -DrillTime '03:30').Count -eq 0
}
Check 'A09-02 schedule: the old 06:30 start (diff at 02:30), a drifting 5 h interval and diff = drill are reported' {
    $old = Test-HccBackupSchedule -FullTime '02:30' -DifferentialStart '06:30' -DifferentialEveryHours 4 -DrillTime '04:30'
    $drift = Test-HccBackupSchedule -FullTime '02:30' -DifferentialStart '04:30' -DifferentialEveryHours 5 -DrillTime '03:00'
    $drill = Test-HccBackupSchedule -FullTime '02:30' -DifferentialStart '04:30' -DifferentialEveryHours 4 -DrillTime '04:30'
    ((Get-HccRepetitionTimes -Start '06:30' -EveryHours 4) -contains '02:30') -and $old.Count -eq 1 -and $old[0] -match '02:30' -and
    $drift.Count -eq 1 -and $drift[0] -match 'full' -and $drill.Count -eq 1 -and $drill[0] -match 'drill' -and (Throws { ConvertTo-HccTimeOfDay '25:00' })
}

# --- A09-03: .NET 10 runtime prerequisite ------------------------------------------------------------------------------------
Check 'A09-03 .NET runtime: both Microsoft.NETCore.App 10 and Microsoft.AspNetCore.App 10 are required; INSTALL.md lists them' {
    $ok = @('Microsoft.AspNetCore.App 10.0.12 [C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App]', 'Microsoft.NETCore.App 8.0.31 [C:\x]', 'Microsoft.NETCore.App 10.0.12 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]')
    $coreOnly = @('Microsoft.NETCore.App 10.0.12 [C:\x]', 'Microsoft.WindowsDesktop.App 10.0.12 [C:\x]')
    $old = @('Microsoft.AspNetCore.App 8.0.31 [C:\x]', 'Microsoft.NETCore.App 8.0.31 [C:\x]')
    $doc = Get-Content -LiteralPath (Join-Path $root 'INSTALL.md') -Raw
    (Test-HccDotnetRuntime -Lines $ok).Count -eq 0 -and ((Test-HccDotnetRuntime -Lines $coreOnly) -join ',') -eq 'Microsoft.AspNetCore.App 10.x' -and
    (Test-HccDotnetRuntime -Lines $old).Count -eq 2 -and (Test-HccDotnetRuntime -Lines @()).Count -eq 2 -and
    $doc -match 'dotnet --list-runtimes' -and $doc -match 'Microsoft\.AspNetCore\.App 10' -and $doc -match 'Hosting Bundle'
}

# --- A09-10: retention and the differential's base count only this database's files -------------------------------------------
function NmDb([string]$Db, [string]$Type, [int]$DaysAgo) { New-HccBackupFileName -Database $Db -Type $Type -UtcNow $now.AddDays(-$DaysAgo) }
Check 'A09-10 retention: with -Database only that database counts; Hcc_X backups are never deleted and never take the place of Hcc fulls' {
    $names = @((NmDb Hcc_X Full 1), (NmDb Hcc_X Full 2), (NmDb Hcc_X Full 3), (NmDb Hcc Full 40), (NmDb Hcc Full 50), (NmDb Hcc Full 60), (NmDb Hcc Full 70), (NmDb Hcc_X Differential 0))
    $del = Select-HccBackupsToDelete -Names $names -Database Hcc -UtcNow $now -RetentionDays 30 -KeepMinFulls 3
    $other = Select-HccBackupsToDelete -Names $names -Database Hcc_X -UtcNow $now -RetentionDays 30 -KeepMinFulls 3
    $unfiltered = Select-HccBackupsToDelete -Names $names -UtcNow $now -RetentionDays 30 -KeepMinFulls 3   # why: the Hcc_X fulls would fill the newest three
    $del.Count -eq 1 -and $del[0] -eq (NmDb Hcc Full 70) -and $other.Count -eq 0 -and ($unfiltered -contains (NmDb Hcc Full 40)) -and
    (ConvertFrom-HccBackupFileName (NmDb Hcc_X Full 1)).Database -eq 'Hcc_X'
}
Check 'A09-10 a FULL of another HCC database is no base for this database''s differential; Backup-HccDatabase filters both by the database' {
    $src = Get-Content -LiteralPath (Join-Path $root 'Backup-HccDatabase.ps1') -Raw
    $null -eq (Select-HccRestoreChain -Names @((NmDb Hcc_X Full 1)) -Database Hcc) -and (Select-HccRestoreChain -Names @((NmDb Hcc_X Full 1)) -Database Hcc_X).Full -eq (NmDb Hcc_X Full 1) -and
    $src -match 'Select-HccBackupsToDelete -Names \$all -Database \$Database' -and $src -match 'Select-HccRestoreChain -Names \$existing -Database \$Database' -and $src -notmatch '-like "\$\{Database\}_\*"'
}

# --- A09-13: no unencrypted backup file is left behind ---------------------------------------------------------------------------
Check 'A09-13 Invoke-HccBackupToFile: a good backup leaves only the .hccenc; a failed backup/verify or encryption deletes the plain .bak and rethrows' {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('HccA0913_' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        $bk = New-HccRandomBytes 32
        $ok = Join-Path $dir 'Hcc_FULL_20261025T120000Z.bak'
        $r = Invoke-HccBackupToFile -Target $ok -Key $bk -Backup { [IO.File]::WriteAllBytes($ok, (New-HccRandomBytes 4096)) }
        $good = $r -eq "$ok.hccenc" -and (Test-Path -LiteralPath "$ok.hccenc") -and -not (Test-Path -LiteralPath $ok)
        # SQL Server wrote the file, then RESTORE VERIFYONLY failed
        $v = Join-Path $dir 'Hcc_FULL_20261025T120100Z.bak'
        $msg = ''
        try { Invoke-HccBackupToFile -Target $v -Key $bk -Backup { [IO.File]::WriteAllBytes($v, (New-HccRandomBytes 100)); throw 'sqlcmd failed (exit 1): Msg 3013' } | Out-Null } catch { $msg = $_.Exception.Message }
        $verify = $msg -match 'Msg 3013' -and -not (Test-Path -LiteralPath $v) -and -not (Test-Path -LiteralPath "$v.hccenc")
        # the encryption fails (its file name is taken): the plain file goes, the other file is not touched
        $e = Join-Path $dir 'Hcc_DIFF_20261025T120200Z.bak'; Set-Content -LiteralPath "$e.hccenc" -Value 'other'
        $encFailed = Throws { Invoke-HccBackupToFile -Target $e -Key $bk -Backup { [IO.File]::WriteAllBytes($e, (New-HccRandomBytes 100)) } }
        $enc = $encFailed -and -not (Test-Path -LiteralPath $e) -and (Get-Content -LiteralPath "$e.hccenc" -Raw).Trim() -eq 'other'
        # EncryptBackups=false: the plain .bak is the backup and stays
        $p = Join-Path $dir 'Hcc_FULL_20261025T120300Z.bak'
        $plain = (Invoke-HccBackupToFile -Target $p -Backup { [IO.File]::WriteAllBytes($p, (New-HccRandomBytes 10)) }) -eq $p -and (Test-Path -LiteralPath $p)
        $good -and $verify -and $enc -and $plain -and (Get-Content -LiteralPath (Join-Path $root 'Backup-HccDatabase.ps1') -Raw) -match 'Invoke-HccBackupToFile -Target \$target -Key \$key'
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
}
Check 'A09-13 Remove-HccPlainCopies: the drill deletes its decrypted copies and reports (never hides) one it cannot delete' {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('HccA0913d_' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        $a = Join-Path $dir 'HccDrill_1_Hcc_FULL_20261025T120000Z.bak'; $b = Join-Path $dir 'HccDrill_1_Hcc_DIFF_20261025T160000Z.bak'
        Set-Content -LiteralPath $a -Value 'a'; Set-Content -LiteralPath $b -Value 'b'
        $lock = [IO.File]::Open($b, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        try { $left = Remove-HccPlainCopies -Paths @($a, $b, (Join-Path $dir 'gone.bak'), '') } finally { $lock.Dispose() }
        $none = Remove-HccPlainCopies -Paths @($b)
        $src = Get-Content -LiteralPath (Join-Path $root 'Restore-HccDrill.ps1') -Raw
        -not (Test-Path -LiteralPath $a) -and $left.Count -eq 1 -and $left[0] -eq $b -and $none.Count -eq 0 -and -not (Test-Path -LiteralPath $b) -and
        $src -match 'Remove-HccPlainCopies -Paths @\(\$script:plainCopies\)' -and $src -match 'plainCopiesLeft' -and $src -notmatch 'plainCopies\)\) \{ if \(\$p'
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
}

# --- A09-11 / A09-12 / A09-14 / A09-15 / A09-16: INSTALL.md matches the installer -------------------------------------------------
Check 'A09-12 folders: installer default C:\Program Files\HCC, INSTALL says keep it, the samples and script defaults use it' {
    $doc = Get-Content -LiteralPath (Join-Path $root 'INSTALL.md') -Raw
    $iss = Get-Content -LiteralPath (Join-Path $root 'installer\Hcc.iss') -Raw
    $app = Get-Content -LiteralPath (Join-Path $root 'appsettings.Production.sample.json') -Raw | ConvertFrom-Json
    $reg = Get-Content -LiteralPath (Join-Path $root 'Register-HccBackupTasks.ps1') -Raw
    $iss -match '(?m)^DefaultDirName=\{autopf\}\\HCC\s*$' -and $iss -match 'DestDir: "\{app\}\\Server"' -and $iss -match 'DestDir: "\{app\}\\Operations"' -and $iss -match 'DestDir: "\{app\}\\ImportTemplates"' -and
    $doc -match 'keep the folder it suggests, `C:\\Program Files\\HCC`' -and $app.Hcc.Operations.BackupScript -eq 'C:\Program Files\HCC\Operations\Backup-HccDatabase.ps1' -and
    (Get-HccOpsConfig -Path (Join-Path $root 'hcc-ops.sample.json')).ServerDirectory -eq 'C:\Program Files\HCC\Server' -and $reg -match "Join-Path \`$env:ProgramFiles 'HCC\\Operations'"
}
Check 'A09-14 / A09-16 settings files: the installer creates both once (onlyifdoesntexist, kept on uninstall); INSTALL never copies the sample over hcc-ops.json and says new settings are not merged' {
    $doc = Get-Content -LiteralPath (Join-Path $root 'INSTALL.md') -Raw
    $iss = Get-Content -LiteralPath (Join-Path $root 'installer\Hcc.iss') -Raw
    $iss -match 'DestName: "appsettings\.Production\.json"; Flags: onlyifdoesntexist uninsneveruninstall' -and $iss -match 'DestName: "hcc-ops\.json"; Flags: onlyifdoesntexist uninsneveruninstall' -and
    $doc.Contains('if (-not (Test-Path hcc-ops.json)) { Copy-Item hcc-ops.sample.json hcc-ops.json }') -and $doc -notmatch '(?m)^Copy-Item hcc-ops\.sample\.json hcc-ops\.json' -and
    $doc -match 'never copy the sample over it' -and $doc -match 'New settings are not added to your settings files' -and $doc -match 'appsettings\.Production\.sample\.json`, `C:\\Program Files\\HCC\\Operations\\hcc-ops\.sample\.json'
}
Check 'A09-15 upgrade: New-HccBackupKey comes before migrate (the safety backup is encrypted); A09-11 elevation is stated; A09-13 free space is stated' {
    $doc = Get-Content -LiteralPath (Join-Path $root 'INSTALL.md') -Raw
    $up = $doc.Substring($doc.IndexOf('## Upgrading, uninstalling'))
    $key = $up.IndexOf('New-HccBackupKey.ps1'); $mig = $up.IndexOf('Run `Hcc.Api.exe migrate`')
    $key -gt 0 -and $mig -gt $key -and $up -match 'before `migrate`' -and
    $doc -match 'Notepad \*\*started as administrator\*\*' -and $doc -match 'This needs an administrator: in PowerShell \(Admin\) `Copy-Item' -and $doc -match 'Local Machine needs an administrator' -and
    $doc -match 'twice one full backup' -and $doc -match 'twice the database''s size' -and $doc -match 'The unencrypted file of this failed run was deleted' -and
    (Get-Content -LiteralPath (Join-Path $root 'Hcc-Common.ps1') -Raw).Contains('The unencrypted file of this failed run was deleted')
}

# --- masters import (A02b-01) -----------------------------------------------------------------------------------------------
Check 'import order: every shipped template, dependencies first (room types, ledgers, outlets before their users)' {
    $order = Get-HccImportOrder
    $templates = @(Get-ChildItem -LiteralPath (Join-Path (Split-Path -Parent $root) 'import-templates\templates') -Filter '*.csv' | ForEach-Object { $_.BaseName } | Sort-Object)
    (($order | Sort-Object) -join ',') -eq ($templates -join ',') -and $order.Count -eq 41 -and
    $order.IndexOf('room-types') -lt $order.IndexOf('rooms') -and $order.IndexOf('room-types') -lt $order.IndexOf('companies') -and
    $order.IndexOf('ledgers') -lt $order.IndexOf('opening-balances') -and $order.IndexOf('pos-outlets') -lt $order.IndexOf('pos-menu') -and
    $order.IndexOf('bank-accounts') -lt $order.IndexOf('payment-methods') -and $order.IndexOf('booking-sources') -lt $order.IndexOf('policies') -and $order.IndexOf('pos-outlets') -lt $order.IndexOf('pos-tables')
}
Check 'import files: picked from a folder in server order, case-insensitive, unknown CSVs reported, -Only filters, bad -Only refused' {
    $names = @('D:\m\Rooms.CSV', 'D:\m\room-types.csv', 'D:\m\notes.txt', 'D:\m\menu.csv', 'D:\m\pos-menu.csv', 'D:\m\opening-balances.csv', 'D:\m\ledgers.csv')
    $s = Select-HccImportFiles -FileNames $names
    $o = Select-HccImportFiles -FileNames $names -Only @('rooms', 'POS-MENU')
    (($s.Files | ForEach-Object Master) -join ',') -eq 'room-types,rooms,ledgers,opening-balances,pos-menu' -and $s.Files[1].Path -eq 'D:\m\Rooms.CSV' -and
    (@($s.Unknown) -join ',') -eq 'menu.csv' -and (($o.Files | ForEach-Object Master) -join ',') -eq 'rooms,pos-menu' -and (Throws { Select-HccImportFiles -FileNames $names -Only @('vendors') })
}
Check 'import server address: https by default and from hcc-ops.json; plain http only to this PC' {
    $cfgDir = Join-Path ([IO.Path]::GetTempPath()) ('HccImpCfg_' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $cfgDir | Out-Null
    try {
        $cfg = Join-Path $cfgDir 'hcc-ops.json'; Set-Content -LiteralPath $cfg -Value '{"ServerUrl":"https://hotelpc:5443"}'
        (Get-HccImportServerUrl -ConfigPath (Join-Path $cfgDir 'none.json')) -eq 'https://localhost:5443' -and (Get-HccImportServerUrl -ConfigPath $cfg) -eq 'https://hotelpc:5443' -and
        (Get-HccImportServerUrl -ServerUrl 'https://x:5443' -ConfigPath $cfg) -eq 'https://x:5443' -and
        (Get-HccImportApiBase 'https://hotelpc:5443/') -eq 'https://hotelpc:5443/api/v1' -and (Get-HccImportApiBase 'http://127.0.0.1:5211') -eq 'http://127.0.0.1:5211/api/v1' -and
        (Throws { Get-HccImportApiBase 'http://192.168.1.20:5443' }) -and (Throws { Get-HccImportApiBase 'ftp://x' }) -and (Throws { Get-HccImportApiBase 'hotelpc' })
    }
    finally { Remove-Item -LiteralPath $cfgDir -Recurse -Force -ErrorAction SilentlyContinue }
}
Check 'import requests: upload body (asOfDate only for opening balances), commit body, idempotency key' {
    $u = New-HccImportUploadBody -Master 'rooms' -FileName 'rooms.csv' -CsvText "roomNo,roomType`n101,STD`n" -AsOfDate '2026-10-31'
    $o = New-HccImportUploadBody -Master 'opening-balances' -FileName 'ob.csv' -CsvText 'x' -AsOfDate '2026-10-31'
    $c = New-HccImportCommitBody -BatchId 42 -DryRun
    $k1 = New-HccIdempotencyKey 'import'; $k2 = New-HccIdempotencyKey 'import'
    ($u | ConvertTo-Json -Compress) -eq '{"master":"rooms","fileName":"rooms.csv","csvText":"roomNo,roomType\n101,STD\n"}' -and $o.asOfDate -eq '2026-10-31' -and
    ($c | ConvertTo-Json -Compress) -eq '{"batchId":42,"dryRun":true}' -and (New-HccImportCommitBody -BatchId 7).dryRun -eq $false -and
    $k1 -ne $k2 -and $k1 -match '^import-[0-9a-f]{32}$' -and $k1.Length -le 80
}
Check 'import CSV reading: UTF-8 with BOM, plain UTF-8 and Excel ANSI (code page 1252)' {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('HccImpCsv_' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        $name = 'Jos' + [char]0xE9
        [IO.File]::WriteAllBytes("$dir\bom.csv", [byte[]](0xEF, 0xBB, 0xBF) + [Text.Encoding]::UTF8.GetBytes("name`n$name`n"))
        [IO.File]::WriteAllBytes("$dir\utf8.csv", [Text.Encoding]::UTF8.GetBytes("name`n$name`n"))
        [IO.File]::WriteAllBytes("$dir\ansi.csv", [byte[]](0x6E, 0x61, 0x6D, 0x65, 0x0A, 0x4A, 0x6F, 0x73, 0xE9, 0x0A))
        (Read-HccImportCsv "$dir\bom.csv") -eq "name`n$name`n" -and (Read-HccImportCsv "$dir\utf8.csv") -eq "name`n$name`n" -and (Read-HccImportCsv "$dir\ansi.csv") -eq "name`n$name`n"
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
}
$importReport = '{"batchId":5,"master":"rooms","fileName":"rooms.csv","rows":3,"errorCount":2,"errors":[{"row":3,"column":"roomType","message":"Room type XL is not in HCC yet. Import the room types file first."},{"row":4,"column":"roomNo","message":"Duplicate of row 2."}],"warnings":[{"row":1,"column":"Floor","message":"Column Floor was ignored."}],"counts":{"create":1,"update":0,"unchanged":0,"remove":0},"plan":[{"row":2,"action":"Create","key":"101","detail":"New room 101 - STD"}],"debitPaise":null,"creditPaise":null,"commitAllowed":false}' | ConvertFrom-Json
Check 'import report: summary, rejected rows with row/column/fix, warnings and planned changes' {
    $lines = Format-HccImportReport -Report $importReport
    $text = $lines -join "`n"
    $lines[0] -eq 'rooms.csv (rooms): 3 row(s) - 1 new, 0 changed, 0 unchanged - 2 rejected' -and $text -match 'REJECTED row 3 / roomType: Room type XL is not in HCC yet' -and
    $text -match 'REJECTED row 4 / roomNo: Duplicate of row 2\.' -and $text -match 'warning  row 1 / Floor' -and $text -match 'new    101: New room 101 - STD' -and
    (Format-HccImportRow ([pscustomobject]@{ row = 0; column = ''; message = 'Totals differ.' })) -eq 'file: Totals differ.'
}
Check 'import report: only a dry run marks rows waiting for an earlier file of the same run as expected' {
    $dry = Split-HccImportErrors -Master 'rooms' -Errors @($importReport.errors) -DryRun -EarlierInRun @('room-types')
    $real = Split-HccImportErrors -Master 'rooms' -Errors @($importReport.errors) -EarlierInRun @('room-types')
    $alone = Split-HccImportErrors -Master 'rooms' -Errors @($importReport.errors) -DryRun -EarlierInRun @()
    $text = (Format-HccImportReport -Report $importReport -Blocking $dry.Blocking -Expected $dry.Expected) -join "`n"
    $dry.Expected.Count -eq 1 -and $dry.Blocking.Count -eq 1 -and $dry.Needs -eq 'room-types' -and $real.Blocking.Count -eq 2 -and $alone.Blocking.Count -eq 2 -and
    $text -match '1 rejected' -and $text -match 'expected in a dry run .*row 3 / roomType'
}
Check 'import report: rupees in Indian grouping; trial balance totals; commit and new-user lines; server error text' {
    $ob = '{"fileName":"opening-balances.csv","master":"opening-balances","rows":2,"errors":[],"warnings":[],"counts":{"create":2,"update":0,"unchanged":0,"remove":0},"plan":[],"debitPaise":12500050,"creditPaise":12500050}' | ConvertFrom-Json
    $commit = '{"batchId":9,"master":"staff","dryRun":false,"committed":true,"counts":{"create":2,"update":0,"unchanged":1,"remove":0},"plan":[],"usersNeedingCredential":[{"userId":7,"loginName":"priya.reception","displayName":"Priya","version":"x","credentialKind":"PIN"}]}' | ConvertFrom-Json
    $dry = '{"batchId":9,"master":"staff","dryRun":true,"committed":false,"counts":{"create":2,"update":0,"unchanged":1,"remove":0},"plan":[],"usersNeedingCredential":[]}' | ConvertFrom-Json
    $err = ConvertFrom-HccApiError -Body '{"code":"VALIDATION","message":"The file has 1 error(s).","details":{"errors":[{"row":2,"column":"rate","message":"Not an amount."}]}}' -Fallback 'x'
    (Format-HccRupees 12500050) -eq 'Rs 1,25,000.50' -and (Format-HccRupees 420000) -eq 'Rs 4,200' -and (Format-HccRupees 5) -eq 'Rs 0.05' -and (Format-HccRupees 1000000000) -eq 'Rs 1,00,00,000' -and
    ((Format-HccImportReport -Report $ob) -join "`n") -match 'Total debits Rs 1,25,000\.50 = total credits Rs 1,25,000\.50' -and
    ((Format-HccImportCommit -Result $commit) -join "`n") -match 'committed: 2 new, 0 changed, 0 removed, 1 unchanged' -and ((Format-HccImportCommit -Result $commit) -join "`n") -match 'priya\.reception .*first PIN' -and
    ((Format-HccImportCommit -Result $dry) -join "`n") -match 'dry run passed: 2 new.*nothing was kept' -and
    $err -match '^VALIDATION: The file has 1 error' -and $err -match 'REJECTED row 2 / rate: Not an amount\.' -and
    (ConvertFrom-HccApiError -Body '' -Fallback 'The SSL connection could not be established') -match 'HCC-Hotel-CA\.cer' -and (ConvertFrom-HccApiError -Body 'not json' -Fallback 'timeout') -eq 'timeout'
}

# --- A09-05: Windows Event Log source HCC and the System health backup record ---------------------------------------------------
$testSource = 'HccTest-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
Check 'A09-05 event source: Register-HccEventSource -WhatIf plans the source and creates nothing; the shipped source is "HCC" in the Application log' {
    $plan = Register-HccEventSource -Source $testSource -WhatIf
    $plan -eq 'planned' -and -not (Test-HccEventSource -Source $testSource) -and $script:HccEventSource -eq 'HCC' -and $script:HccEventLogName -eq 'Application' -and
    (@('exists', 'planned') -contains (Register-HccEventSource -WhatIf))
}
Check 'A09-05 Write-HccEventLog: one entry through Invoke-HccEventLogWrite (source HCC, level, id); skipped silently without the source or when writing fails' {
    $script:captured = @()
    function Test-HccEventSource { param([string]$Source) return $true }
    function Invoke-HccEventLogWrite { param([string]$Source, [string]$EntryType, [int]$EventId, [string]$Message) $script:captured += [pscustomobject]@{ Source = $Source; Type = $EntryType; Id = $EventId; Message = $Message } }
    $written = Write-HccEventLog -Level Error -EventId 2001 -Message ('x' * 5000)
    $one = $script:captured.Count -eq 1 -and $script:captured[0].Source -eq 'HCC' -and $script:captured[0].Type -eq 'Error' -and $script:captured[0].Id -eq 2001 -and $script:captured[0].Message.Length -eq 4000
    function Invoke-HccEventLogWrite { param([string]$Source, [string]$EntryType, [int]$EventId, [string]$Message) throw 'Access denied' }
    $failedWrite = Write-HccEventLog -Level Warning -EventId 2003 -Message 'm'
    function Test-HccEventSource { param([string]$Source) return $false }
    function Invoke-HccEventLogWrite { param([string]$Source, [string]$EntryType, [int]$EventId, [string]$Message) $script:captured += 'must not be called' }
    $noSource = Write-HccEventLog -Level Error -EventId 2002 -Message 'm'
    $written -and $one -and -not $failedWrite -and -not $noSource -and $script:captured.Count -eq 1
}
Check 'A09-05 failure reason: sqlcmd errors reduced to SQL Server error numbers (no message text), script messages kept on one line, at most 300 characters' {
    $sql = Get-HccFailureReason "sqlcmd failed (exit 1): Msg 2627, Level 14, State 1`r`nViolation of PRIMARY KEY constraint. The duplicate key value is (98765 43210).`r`nMsg 3013, Level 16"
    $down = Get-HccFailureReason 'sqlcmd failed (exit 1): Sqlcmd: Error: Microsoft ODBC Driver 18 for SQL Server : TCP Provider: No connection could be made.'
    $sql -eq 'sqlcmd failed (exit 1): Msg 2627, Msg 3013' -and $down -eq 'sqlcmd failed (exit 1): SQL Server could not be reached' -and
    (Get-HccFailureReason "No backup encryption key at C:\k.txt.`nRun New-HccBackupKey.ps1") -eq 'No backup encryption key at C:\k.txt. Run New-HccBackupKey.ps1' -and
    (Get-HccFailureReason ('y' * 400)).Length -eq 300 -and (Get-HccFailureReason '') -eq 'unknown error'
}
Check 'A09-05 backup failure entry: Error 2001 / 2002 with source HCC, kind, database, reason and log file; returned also without the Event Log' {
    $script:captured = @()
    function Test-HccEventSource { param([string]$Source) return $true }
    function Invoke-HccEventLogWrite { param([string]$Source, [string]$EntryType, [int]$EventId, [string]$Message) $script:captured += [pscustomobject]@{ Source = $Source; Type = $EntryType; Id = $EventId; Message = $Message } }
    $b = Write-HccOperationFailure -Operation Backup -Kind Differential -Database Hcc -SqlInstance '.\SQLEXPRESS' -Reason 'sqlcmd failed (exit 1): Msg 3201, Msg 3013' -LogFile 'D:\HccBackups\Logs\backup_202610.log'
    $d = Write-HccOperationFailure -Operation RestoreDrill -Database Hcc -SqlInstance '.\SQLEXPRESS' -Reason 'No FULL backup of [Hcc] found in D:\HccBackups.'
    $e = $script:captured
    $e.Count -eq 2 -and $e[0].Source -eq 'HCC' -and $e[0].Type -eq 'Error' -and $e[0].Id -eq 2001 -and $e[1].Id -eq 2002 -and $e[0].Message -eq $b -and
    $b -match '^HCC backup FAILED: Differential backup of \[Hcc\] on \.\\SQLEXPRESS at \d{4}-\d\d-\d\d \d\d:\d\d:\d\dZ\. Reason: sqlcmd failed \(exit 1\): Msg 3201, Msg 3013\.' -and
    $b -match 'Log: D:\\HccBackups\\Logs\\backup_202610\.log$' -and $d -match '^HCC restore drill FAILED for \[Hcc\]' -and $d -match 'No FULL backup'
}
Check 'A09-05 backup result SQL: one guarded INSERT into [Hcc].plat.backup_run, every value quoted, NULLs for missing values, never another database' {
    $start = [datetime]::SpecifyKind([datetime]'2026-10-26 02:30:00', 'Utc'); $end = $start.AddSeconds(71)
    $ok = Get-HccBackupRunSql -Database Hcc -Kind Full -Succeeded $true -StartedUtc $start -FinishedUtc $end -FileName 'Hcc_FULL_20261026T023000Z.bak.hccenc' -SizeBytes 52428800 -HostName 'HOTEL-PC'
    $bad = Get-HccBackupRunSql -Database Hcc -Kind Differential -Succeeded $false -StartedUtc $start -FinishedUtc $start.AddSeconds(-5) -Reason "it's broken" -HostName 'HOTEL-PC'
    $ok -eq "IF OBJECT_ID(N'[Hcc].plat.backup_run') IS NOT NULL INSERT [Hcc].plat.backup_run(kind, succeeded, started_utc, finished_utc, file_name, size_bytes, reason, host_name) VALUES (N'Full', 1, '2026-10-26T02:30:00', '2026-10-26T02:31:11', N'Hcc_FULL_20261026T023000Z.bak.hccenc', 52428800, NULL, N'HOTEL-PC');" -and
    $bad -match "VALUES \(N'Differential', 0, '2026-10-26T02:30:00', '2026-10-26T02:30:00', NULL, NULL, N'it''s broken', N'HOTEL-PC'\);$" -and
    (Throws { Get-HccBackupRunSql -Database EtpReporting -Kind Full -Succeeded $true -StartedUtc $start -FinishedUtc $end }) -and
    (Throws { Get-HccBackupRunSql -Database Hcc -Kind Weekly -Succeeded $true -StartedUtc $start -FinishedUtc $end })
}
Check 'A09-05 Write-HccBackupRun: sent through sqlcmd as the guarded INSERT; a failed write only warns (Warning 2003 after a good backup) and never throws' {
    $script:sent = @(); $script:captured = @()
    function Invoke-HccSql { param([string]$Instance, [string]$Query, [switch]$TrustServerCertificate, [switch]$Scalar, [switch]$Table) $script:sent += [pscustomobject]@{ Instance = $Instance; Query = $Query; Trust = [bool]$TrustServerCertificate } }
    function Test-HccEventSource { param([string]$Source) return $true }
    function Invoke-HccEventLogWrite { param([string]$Source, [string]$EntryType, [int]$EventId, [string]$Message) $script:captured += [pscustomobject]@{ Type = $EntryType; Id = $EventId; Message = $Message } }
    $start = (Get-Date).ToUniversalTime().AddMinutes(-2)
    $recorded = Write-HccBackupRun -SqlInstance '.\SQLEXPRESS' -Database Hcc -Kind Full -Succeeded $true -StartedUtc $start -FileName 'f.bak.hccenc' -SizeBytes 10 -TrustServerCertificate
    $sentOk = $recorded -and $script:sent.Count -eq 1 -and $script:sent[0].Trust -and $script:sent[0].Query -match "^IF OBJECT_ID\(N'\[Hcc\]\.plat\.backup_run'\) IS NOT NULL INSERT .*VALUES \(N'Full', 1, " -and $script:captured.Count -eq 0
    function Invoke-HccSql { param([string]$Instance, [string]$Query, [switch]$TrustServerCertificate, [switch]$Scalar, [switch]$Table) throw 'sqlcmd failed (exit 1): Msg 229, Level 14: The INSERT permission was denied' }
    $notRecorded = Write-HccBackupRun -SqlInstance '.\SQLEXPRESS' -Database Hcc -Kind Full -Succeeded $true -StartedUtc $start
    $afterFailure = Write-HccBackupRun -SqlInstance '.\SQLEXPRESS' -Database Hcc -Kind Full -Succeeded $false -StartedUtc $start -Reason 'r'
    $sentOk -and -not $notRecorded -and -not $afterFailure -and $script:captured.Count -eq 1 -and $script:captured[0].Id -eq 2003 -and $script:captured[0].Type -eq 'Warning' -and
    $script:captured[0].Message -match 'Msg 229\)' -and $script:captured[0].Message -notmatch 'permission was denied'
}

# --- every script in dry-run mode ---------------------------------------------------------------------------------------
$psExe = (Get-Process -Id $PID).Path
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('hcc-ps-test-' + [guid]::NewGuid().ToString('N'))
function DryRun([string]$Script, [string[]]$ScriptArgs) {
    $ErrorActionPreference = 'Continue'
    # Windows PowerShell 5.1 drops an empty-string argument passed to a native exe; quote it so the script still receives ''.
    if ($PSVersionTable.PSVersion.Major -lt 7) { $ScriptArgs = @($ScriptArgs | ForEach-Object { if ($_ -eq '') { '""' } else { $_ } }) }
    $out = & $psExe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root $Script) @ScriptArgs -WhatIf 2>&1 | Out-String
    return [pscustomobject]@{ Exit = $LASTEXITCODE; Out = $out }
}
Check 'Backup-HccDatabase -WhatIf full: exit 0, prints plan, creates nothing' {
    $r = DryRun 'Backup-HccDatabase.ps1' @('-Type', 'Full', '-BackupDirectory', $tmp, '-ConfigPath', (Join-Path $tmp 'none.json'))
    $r.Exit -eq 0 -and $r.Out -match 'BACKUP DATABASE \[Hcc\]' -and $r.Out -match 'CHECKSUM' -and $r.Out -match 'RESTORE VERIFYONLY' -and $r.Out -match 'encrypt it to .*\.bak\.hccenc' -and $r.Out -match 'delete the plain \.bak' -and $r.Out -match 'private' -and -not (Test-Path $tmp)
}
Check 'Backup-HccDatabase -WhatIf differential + copy-only tag' {
    $d = DryRun 'Backup-HccDatabase.ps1' @('-Type', 'Differential', '-BackupDirectory', $tmp)
    $t = DryRun 'Backup-HccDatabase.ps1' @('-Type', 'Full', '-CopyOnly', '-Tag', 'premigrate_0100_to_0200', '-Database', 'Hcc', '-BackupDirectory', $tmp)
    $d.Exit -eq 0 -and $d.Out -match 'DIFFERENTIAL' -and $t.Exit -eq 0 -and $t.Out -match 'COPY_ONLY' -and $t.Out -match '_PRE_'
}
Check 'Backup-HccDatabase refuses an ETP database and a relative folder' {
    (DryRun 'Backup-HccDatabase.ps1' @('-Type', 'Full', '-Database', 'EtpReporting', '-BackupDirectory', $tmp)).Exit -ne 0 -and (DryRun 'Backup-HccDatabase.ps1' @('-Type', 'Full', '-BackupDirectory', 'rel\dir')).Exit -ne 0
}
Check 'Restore-HccDrill -WhatIf: exit 0, plan mentions scratch DB, CHECKDB, row counts' {
    $r = DryRun 'Restore-HccDrill.ps1' @('-BackupDirectory', $tmp)
    $r.Exit -eq 0 -and $r.Out -match 'HccDrill_' -and $r.Out -match 'DBCC CHECKDB' -and $r.Out -match 'row counts' -and $r.Out -match 'decrypted' -and -not (Test-Path $tmp)
}
Check 'A07-03 Restore-HccDrill -WhatIf: plans the guest ID check on the scratch copy (server folder from -ServerDirectory); -SkipGuestIdCheck says so' {
    $none = Join-Path $tmp 'none.json'
    $r = DryRun 'Restore-HccDrill.ps1' @('-BackupDirectory', $tmp, '-ConfigPath', $none, '-ScratchName', 'HccDrill_20261026')
    $d = DryRun 'Restore-HccDrill.ps1' @('-BackupDirectory', $tmp, '-ConfigPath', $none, '-ServerDirectory', 'D:\HccServer')
    $s = DryRun 'Restore-HccDrill.ps1' @('-BackupDirectory', $tmp, '-ConfigPath', $none, '-SkipGuestIdCheck')
    $r.Exit -eq 0 -and $r.Out -match ([regex]::Escape((Join-Path $env:ProgramFiles 'HCC\Server\Hcc.Api.exe')) + ' verify-guest-id --database HccDrill_20261026') -and $r.Out -match 'never prints a number' -and
    $r.Out -match 'no guest IDs = reported, not a failure' -and $d.Exit -eq 0 -and $d.Out -match 'D:\\HccServer\\Hcc\.Api\.exe verify-guest-id' -and
    $s.Exit -eq 0 -and $s.Out -match 'guest ID check SKIPPED' -and $s.Out -notmatch 'verify-guest-id' -and -not (Test-Path $tmp)
}
Check 'Restore-HccDrill refuses a non-scratch name or the live name' {
    (DryRun 'Restore-HccDrill.ps1' @('-BackupDirectory', $tmp, '-ScratchName', 'EtpReporting')).Exit -ne 0 -and (DryRun 'Restore-HccDrill.ps1' @('-BackupDirectory', $tmp, '-ScratchName', 'Hcc')).Exit -ne 0
}
Check 'A09-01 Backup/Restore/Initialize -WhatIf: -C for the local instance by default, not for a remote one, not when hcc-ops.json says false' {
    New-Item -ItemType Directory -Path $tmp -Force | Out-Null
    try {
        $off = Join-Path $tmp 'hcc-ops-false.json'; Set-Content -LiteralPath $off -Value '{ "TrustServerCertificate": false }'
        $none = Join-Path $tmp 'none.json'
        $b = DryRun 'Backup-HccDatabase.ps1' @('-Type', 'Full', '-BackupDirectory', $tmp, '-ConfigPath', $none)
        $br = DryRun 'Backup-HccDatabase.ps1' @('-Type', 'Full', '-BackupDirectory', $tmp, '-ConfigPath', $none, '-SqlInstance', 'DBSERVER\SQLEXPRESS')
        $bf = DryRun 'Backup-HccDatabase.ps1' @('-Type', 'Full', '-BackupDirectory', $tmp, '-ConfigPath', $off)
        $r = DryRun 'Restore-HccDrill.ps1' @('-BackupDirectory', $tmp, '-ConfigPath', $none)
        $i = DryRun 'Initialize-HccDatabase.ps1' @('-ServiceAccount', 'NT SERVICE\HccServer', '-BackupDirectory', 'D:\HccBackups')
        $ir = DryRun 'Initialize-HccDatabase.ps1' @('-ServiceAccount', 'NT SERVICE\HccServer', '-BackupDirectory', 'D:\HccBackups', '-SqlInstance', 'DBSERVER\SQLEXPRESS')
        $yes = 'trusts the server certificate \(-C\): yes'; $no = 'trusts the server certificate \(-C\): no'
        $b.Out -match $yes -and $br.Out -match $no -and $bf.Out -match $no -and $r.Out -match $yes -and $i.Out -match $yes -and $ir.Out -match $no -and
        @(@($b, $br, $bf, $r, $i, $ir) | Where-Object { $_.Exit -ne 0 }).Count -eq 0
    }
    finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
}
Check 'A09-02 Register-HccBackupTasks -WhatIf: differentials from 04:30 (none at 02:30), drill Sunday 03:30; a colliding schedule is refused' {
    $r = DryRun 'Register-HccBackupTasks.ps1' @()
    $c = DryRun 'Register-HccBackupTasks.ps1' @('-DifferentialStart', '06:30')
    $r.Exit -eq 0 -and $r.Out -match 'differentials at 00:30, 04:30, 08:30, 12:30, 16:30, 20:30;' -and $r.Out -match 'restore drill Sunday 03:30' -and
    $c.Exit -ne 0 -and $c.Out -match 'Refusing the backup schedule' -and $c.Out -match '02:30'
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
Check 'Import-HccMasters -WhatIf lists the sample files in server order, asks for nothing and sends nothing' {
    $samples = Join-Path (Split-Path -Parent $root) 'import-templates\samples'
    $r = DryRun 'Import-HccMasters.ps1' @('-Folder', $samples, '-OpeningBalancesDate', '2026-10-25', '-ServerUrl', 'https://hotelpc:5443')
    $d = DryRun 'Import-HccMasters.ps1' @('-Folder', $samples, '-OpeningBalancesDate', '2026-10-25', '-DryRun', '-Only', 'rooms')
    $lines = @($r.Out -split "`r?`n" | Where-Object { $_ -match 'UploadImportBatch' })
    $r.Exit -eq 0 -and $lines.Count -eq 41 -and $lines[0] -match ' 1\. room-types\.csv' -and $lines[1] -match 'rooms\.csv' -and $lines[14] -match 'stock-items\.csv' -and
    $r.Out -match 'POST https://hotelpc:5443/api/v1/auth/sign-in' -and $r.Out -match 'opening-balances as at 2026-10-25' -and $r.Out -match '\*{8}' -and
    $d.Exit -eq 0 -and $d.Out -match 'POST https://localhost:5443/api/v1/commands/UploadImportBatch \(master rooms\)' -and $d.Out -match 'dryRun=true' -and @($d.Out -split "`r?`n" | Where-Object { $_ -match 'UploadImportBatch' }).Count -eq 1
}
Check 'Import-HccMasters refuses plain http to another PC, a missing opening-balances date and an empty folder' {
    $samples = Join-Path (Split-Path -Parent $root) 'import-templates\samples'
    New-Item -ItemType Directory -Path $tmp -Force | Out-Null
    try {
        $http = DryRun 'Import-HccMasters.ps1' @('-Folder', $samples, '-OpeningBalancesDate', '2026-10-25', '-ServerUrl', 'http://192.168.1.20:5443')
        $date = DryRun 'Import-HccMasters.ps1' @('-Folder', $samples)
        $empty = DryRun 'Import-HccMasters.ps1' @('-Folder', $tmp)
        $http.Exit -ne 0 -and $http.Out -match 'only over HTTPS' -and $date.Exit -ne 0 -and $date.Out -match 'OpeningBalancesDate' -and $empty.Exit -ne 0 -and $empty.Out -match 'No import files'
    }
    finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
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
Check 'A07-03 New-HccGuestIdKey -ImportOfflineCopy: -WhatIf writes nothing; imports the key under its id (DPAPI round trip, never printed); a second run changes nothing' {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('HccKeyImp_' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path "$dir\old", "$dir\new", "$dir\usb" -Force | Out-Null
    $script = Join-Path $root 'New-HccGuestIdKey.ps1'
    $ErrorActionPreference = 'Continue'
    try {
        # The old PC: a key made by the script, its offline copy on the keys USB stick.
        $old = "$dir\old\appsettings.Production.json"; Set-Content -LiteralPath $old -Value '{"Hcc":{"Sql":{"ConnectionString":"x"}}}'
        & $psExe -NoProfile -ExecutionPolicy Bypass -File $script -SettingsFile $old -OfflineCopyPath "$dir\usb\k.txt" -KeyId '2026-10' -SkipAcl 2>&1 | Out-Null
        $rawB64 = (((Get-Content "$dir\usb\k.txt") | Where-Object { $_ -like 'Raw key*' }) -split ': ', 2)[1].Trim()
        # The replacement PC: the installer's fresh settings file.
        $s = "$dir\new\appsettings.Production.json"; Set-Content -LiteralPath $s -Value '{"Hcc":{"Sql":{"ConnectionString":"x"}},"_notes":["a"]}'
        $w = & $psExe -NoProfile -ExecutionPolicy Bypass -File $script -SettingsFile $s -ImportOfflineCopy "$dir\usb\k.txt" -SkipAcl -WhatIf 2>&1 | Out-String
        $dry = $LASTEXITCODE -eq 0 -and $w -match "DRY RUN: Import guest ID key '2026-10'" -and $w -match 'as the current key' -and -not $w.Contains($rawB64) -and
            ((Get-Content -LiteralPath $s -Raw) -notmatch 'GuestIdKeys') -and -not (Test-Path "$s.before-guestid-key")
        $o = & $psExe -NoProfile -ExecutionPolicy Bypass -File $script -SettingsFile $s -ImportOfflineCopy "$dir\usb\k.txt" -SkipAcl 2>&1 | Out-String
        $x = $LASTEXITCODE
        $c = Get-Content -LiteralPath $s -Raw | ConvertFrom-Json
        $wrapped = $c.Hcc.GuestIdKeys.Keys.'2026-10'
        Add-Type -AssemblyName System.Security
        $back = [Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($wrapped.Substring('dpapi-machine:'.Length)), [Text.Encoding]::ASCII.GetBytes('HCC guest ID key v1'), 'LocalMachine')
        $imported = $x -eq 0 -and $o -match 'imported from the offline copy' -and $o -match 'verify-guest-id' -and $c.Hcc.GuestIdKeys.Current -eq '2026-10' -and $c.Hcc.GuestIdKeys.Required -eq $true -and
            $c.Hcc.GuestIdKeys.AllowMachineScope -eq $true -and $wrapped.StartsWith('dpapi-machine:') -and ([Convert]::ToBase64String($back) -eq $rawB64) -and -not $o.Contains($rawB64) -and
            -not (Get-Content -LiteralPath $s -Raw).Contains($rawB64) -and $c.Hcc.Sql.ConnectionString -eq 'x' -and (Test-Path "$s.before-guestid-key")
        $again = & $psExe -NoProfile -ExecutionPolicy Bypass -File $script -SettingsFile $s -ImportOfflineCopy "$dir\usb\k.txt" -SkipAcl 2>&1 | Out-String
        $idem = $LASTEXITCODE -eq 0 -and $again -match 'nothing to do' -and (Get-Content -LiteralPath $s -Raw | ConvertFrom-Json).Hcc.GuestIdKeys.Keys.'2026-10' -eq $wrapped
        $dry -and $imported -and $idem
    }
    finally { $ErrorActionPreference = 'Stop'; Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
}
Check 'A07-03 New-HccGuestIdKey -ImportOfflineCopy refuses to overwrite a key unless -Force (with a warning); refuses the backup key copy and a wrong -KeyId; adds an older key next to the current one' {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('HccKeyImp2_' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path "$dir\server", "$dir\usb" -Force | Out-Null
    $script = Join-Path $root 'New-HccGuestIdKey.ps1'
    $s = "$dir\server\appsettings.Production.json"
    $ErrorActionPreference = 'Continue'
    try {
        Add-Type -AssemblyName System.Security
        $e = [Text.Encoding]::ASCII.GetBytes('HCC guest ID key v1')
        $raw = New-Object byte[] 32; for ($i = 0; $i -lt 32; $i++) { $raw[$i] = [byte](200 - $i) }   # fictional test key
        $rawB64 = [Convert]::ToBase64String($raw)
        $copy = "$dir\usb\HCC-guest-id-key-2026-10.txt"
        Set-Content -LiteralPath $copy -Value @('HCC guest ID key (OFFLINE COPY - keep in the safe, never e-mail, never on the server PC)', 'Key id: 2026-10', 'Created (UTC): 2026-10-26 10:00', "Raw key (base64, 32 bytes): $rawB64")
        $foreign = 'dpapi-machine:' + [Convert]::ToBase64String((New-HccRandomBytes 180))   # an entry copied from the old PC: DPAPI cannot open it here
        $different = 'dpapi-machine:' + [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect((New-HccRandomBytes 32), $e, 'LocalMachine'))
        function SettingsWith([string]$v) { Set-Content -LiteralPath $s -Value ('{"Hcc":{"Sql":{"ConnectionString":"x"},"GuestIdKeys":{"Current":"2026-10","Required":true,"AllowMachineScope":true,"Keys":{"2026-10":"' + $v + '"}}}}') }
        function Entry([string]$id) { return (Get-Content -LiteralPath $s -Raw | ConvertFrom-Json).Hcc.GuestIdKeys.Keys.$id }
        function Run([string[]]$extra) { $out = & $psExe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $script -SettingsFile $s -SkipAcl @extra 2>&1 | Out-String; return @{ Exit = $LASTEXITCODE; Out = ($out -replace '\s+', ' ') } }

        SettingsWith $foreign
        $r1 = Run @('-ImportOfflineCopy', $copy)
        $kept1 = (Entry '2026-10') -eq $foreign
        $f = Run @('-ImportOfflineCopy', $copy, '-Force')
        $now = Entry '2026-10'
        $opens = [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($now.Substring(14)), $e, 'LocalMachine')) -eq $rawB64
        SettingsWith $different
        $r2 = Run @('-ImportOfflineCopy', $copy)
        $kept2 = (Entry '2026-10') -eq $different
        $bk = "$dir\usb\HCC-backup-key.txt"
        Set-Content -LiteralPath $bk -Value @('HCC BACKUP key (OFFLINE COPY - keep in the safe, apart from the backup disk; never e-mail it)', 'Key fingerprint: 00', "Raw key (base64, 32 bytes): $rawB64")
        $r3 = Run @('-ImportOfflineCopy', $bk)
        $r4 = Run @('-ImportOfflineCopy', $copy, '-KeyId', '2027-10')
        $copy2 = "$dir\usb\HCC-guest-id-key-2025-10.txt"
        Set-Content -LiteralPath $copy2 -Value @('HCC guest ID key (OFFLINE COPY - keep in the safe, never e-mail, never on the server PC)', 'Key id: 2025-10', "Raw key (base64, 32 bytes): $rawB64")
        $r5 = Run @('-ImportOfflineCopy', $copy2)
        $g5 = (Get-Content -LiteralPath $s -Raw | ConvertFrom-Json).Hcc.GuestIdKeys
        $added = $r5.Exit -eq 0 -and $r5.Out -match "next to the current key '2026-10'" -and $g5.Current -eq '2026-10' -and [bool]$g5.Keys.'2025-10' -and $g5.Keys.'2026-10' -eq $different
        $r1.Exit -ne 0 -and $r1.Out -match 'cannot be opened on this PC' -and $r1.Out -match 'with -Force' -and $kept1 -and
        $f.Exit -eq 0 -and $f.Out -match 'WARNING' -and $f.Out -match "Replacing guest ID key '2026-10'" -and $opens -and -not $f.Out.Contains($rawB64) -and
        $r2.Exit -ne 0 -and $r2.Out -match 'DIFFERENT key' -and $kept2 -and
        $r3.Exit -ne 0 -and $r3.Out -match 'not an HCC guest ID key offline copy' -and $r4.Exit -ne 0 -and $r4.Out -match "is key '2026-10', not '2027-10'" -and $added -and
        -not (($r1.Out + $r2.Out + $r3.Out + $r4.Out + $r5.Out).Contains($rawB64))
    }
    finally { $ErrorActionPreference = 'Stop'; Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
}
Check 'Install-HccService -WhatIf prints plan; -Uninstall -WhatIf changes nothing' {
    $r = DryRun 'Install-HccService.ps1' @()
    $u = DryRun 'Install-HccService.ps1' @('-Uninstall')
    $r.Exit -eq 0 -and $r.Out -match 'New-Service HccServer' -and $r.Out -match 'New-NetFirewallRule' -and $r.Out -match 'appsettings\.Production\.json /inheritance:r' -and $u.Exit -eq 0 -and $null -eq (Get-Service HccServer -ErrorAction SilentlyContinue) -and
    $r.Out -match '\.NET runtime check: Microsoft\.NETCore\.App 10 and Microsoft\.AspNetCore\.App 10 found'   # A09-03 (this PC runs the .NET 10 tests); the real run refuses when missing
}
Check 'A09-05 Install-HccService -WhatIf plans the Event Log source HCC; -EventSourceOnly -WhatIf plans only the source and changes nothing' {
    $r = DryRun 'Install-HccService.ps1' @()
    $e = DryRun 'Install-HccService.ps1' @('-EventSourceOnly')
    $r.Exit -eq 0 -and $r.Out -match 'register Windows Event Log source HCC in the Application log \((already registered|not registered yet)\)' -and
    $e.Exit -eq 0 -and $e.Out -match 'DRY RUN: register Windows Event Log source HCC' -and $e.Out -notmatch 'New-Service' -and
    (Get-Content -LiteralPath (Join-Path $root 'installer\Hcc.iss') -Raw) -match 'Install-HccService\.ps1"" -EventSourceOnly'
}
Check 'A09-05 Backup / Restore -WhatIf mention the System health record and the Event Log entry; Initialize-HccDatabase grants the backup account hcc_backup_reporter' {
    $b = DryRun 'Backup-HccDatabase.ps1' @('-Type', 'Full', '-BackupDirectory', $tmp, '-ConfigPath', (Join-Path $tmp 'none.json'))
    $r = DryRun 'Restore-HccDrill.ps1' @('-BackupDirectory', $tmp, '-ConfigPath', (Join-Path $tmp 'none.json'))
    $i = DryRun 'Initialize-HccDatabase.ps1' @('-ServiceAccount', 'NT SERVICE\HccServer', '-BackupDirectory', $tmp)
    $b.Out -match 'record the result \(Full\) in \[Hcc\]\.plat\.backup_run' -and $b.Out -match 'event 2001' -and $r.Out -match 'record the result \(RestoreDrill\)' -and $r.Out -match 'event 2002' -and
    $i.Out -match "IS_ROLEMEMBER\(N'hcc_backup_reporter', N'NT AUTHORITY\\SYSTEM'\) = 0 ALTER ROLE hcc_backup_reporter ADD MEMBER \[NT AUTHORITY\\SYSTEM\]" -and -not (Test-Path $tmp)
}
Check 'A09-05 a real backup / drill failure (no backup key, no backup) exits non-zero, logs FAILED and the unrecordable result, and needs no Event Log source' {
    if (Test-HccEventSource) { Write-Host '  (source HCC is registered on this PC: real runs skipped so no test entry lands in its Application log)'; return $true }
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('HccA0905_' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    $ErrorActionPreference = 'Continue'
    try {
        # SQL Server "on this PC" at a closed port: nothing is ever written to a real database; the result write fails fast and only warns.
        $inst = '127.0.0.1\SQLEXPRESS,1'
        $bo = & $psExe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root 'Backup-HccDatabase.ps1') -Type Full -BackupDirectory $dir -SqlInstance $inst -ConfigPath (Join-Path $dir 'none.json') -BackupKeyFile (Join-Path $dir 'no-key.txt') 2>&1 | Out-String
        $bx = $LASTEXITCODE
        $ro = & $psExe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root 'Restore-HccDrill.ps1') -BackupDirectory $dir -SqlInstance $inst -ConfigPath (Join-Path $dir 'none.json') 2>&1 | Out-String
        $rx = $LASTEXITCODE
        $log = Get-ChildItem -LiteralPath (Join-Path $dir 'Logs') -Filter 'backup_*.log' | Select-Object -First 1
        $bx -ne 0 -and $bo -match 'Backup FAILED: No backup encryption key' -and $bo -match 'Could not record this result for System health' -and $log -and
        (Get-Content -LiteralPath $log.FullName -Raw) -match 'ERROR Backup FAILED' -and
        $rx -eq 1 -and $ro -match 'DRILL FAILED: No FULL backup' -and $ro -match 'Could not record this result for System health' -and
        @(Get-ChildItem -LiteralPath $dir -Filter '*.bak*' -File).Count -eq 0
    }
    finally { $ErrorActionPreference = 'Stop'; Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
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

# --- LF-docdb: the documents database (LF-CONTRACT 1.9) ---------------------------------------------------------------------
Check 'LF-docdb database list: main first, documents second; no documents entry = main only; same name refused' {
    $both = Get-HccDatabaseList -Config ([ordered]@{ Database = 'Hcc'; DocumentsDatabase = 'HccDocuments' })
    $old = Get-HccDatabaseList -Config ([ordered]@{ Database = 'Hcc'; DocumentsDatabase = '' })
    $both.Count -eq 2 -and $both[0] -eq 'Hcc' -and $both[1] -eq 'HccDocuments' -and $old.Count -eq 1 -and $old[0] -eq 'Hcc' -and
    (Throws { Get-HccDatabaseList -Config ([ordered]@{ Database = 'Hcc'; DocumentsDatabase = 'Hcc' }) }) -and (Throws { Get-HccDatabaseList -Config ([ordered]@{ Database = 'Hcc'; DocumentsDatabase = 'EtpReporting' }) })
}
Check 'LF-docdb default config names HccDocuments; the sample hcc-ops.json carries it' {
    (Get-HccOpsConfig -Path (Join-Path $env:TEMP 'none-hcc-ops.json')).DocumentsDatabase -eq 'HccDocuments' -and (Get-Content -LiteralPath (Join-Path $root 'hcc-ops.sample.json') -Raw) -match '"DocumentsDatabase":\s*"HccDocuments"'
}
Check 'LF-docdb backup file names are per database (HccDocuments_FULL_...) and retention never mixes the two' {
    $d = New-HccBackupFileName -Database HccDocuments -Type Full -UtcNow $now
    $del = Select-HccBackupsToDelete -Names @($d, (Nm Full 200), (Nm Full 201), (Nm Full 202), (Nm Full 203)) -Database HccDocuments -UtcNow $now -RetentionDays 30 -KeepMinFulls 3
    $d -eq 'HccDocuments_FULL_20261025T120000Z.bak' -and (ConvertFrom-HccBackupFileName ($d + '.hccenc')).Database -eq 'HccDocuments' -and
    $del.Count -eq 0 -and
    (Select-HccRestoreChain -Names @($d, (Nm Full 1)) -Database HccDocuments).Full -eq $d
}
Check 'LF-docdb backup result of the documents database is written to the MAIN database with database_name; the main statement is unchanged' {
    $start = [datetime]::SpecifyKind([datetime]'2026-10-26 02:30:00', 'Utc')
    $docs = Get-HccBackupRunSql -Database HccDocuments -MainDatabase Hcc -Kind Full -Succeeded $true -StartedUtc $start -FinishedUtc $start.AddSeconds(5) -FileName 'HccDocuments_FULL_20261026T023000Z.bak.hccenc' -SizeBytes 10 -HostName 'HOTEL-PC'
    $main = Get-HccBackupRunSql -Database Hcc -MainDatabase Hcc -Kind Full -Succeeded $true -StartedUtc $start -FinishedUtc $start.AddSeconds(5) -HostName 'HOTEL-PC'
    $docs -match '^IF OBJECT_ID\(N''\[Hcc\]\.plat\.backup_run''\) IS NOT NULL INSERT \[Hcc\]\.plat\.backup_run\(kind, succeeded, started_utc, finished_utc, file_name, size_bytes, reason, host_name, database_name\) VALUES \(N''Full'', 1, .*N''HccDocuments''\);$' -and
    $main -notmatch 'database_name' -and $main -match '^IF OBJECT_ID\(N''\[Hcc\]\.plat\.backup_run''\)'
}
Check 'LF-docdb reference check SQL reads the four content tables of the main database and the live objects of the documents database' {
    $q = Get-HccDocumentRefCheckSql -MainDatabase HccDrill_1 -DocumentsDatabase HccDrill_1Documents
    $q -match 'doc\.id_capture_image' -and $q -match 'ops\.expense_bill_photo' -and $q -match 'pay\.banking_slip' -and $q -match 'cmp\.evidence_content' -and
    $q -match '\[HccDrill_1Documents\]\.dob\.object' -and $q -match 'deleted_utc IS NULL' -and (Throws { Get-HccDocumentRefCheckSql -MainDatabase Hcc -DocumentsDatabase EtpReporting })
}
Check 'LF-docdb Initialize-HccDatabase -WhatIf creates both databases and gives the service the same rights in both; -DocumentsDatabase "" keeps the old single database' {
    $r = DryRun 'Initialize-HccDatabase.ps1' @('-ServiceAccount', 'NT SERVICE\HccServer', '-BackupDirectory', 'D:\HccBackups')
    $o = DryRun 'Initialize-HccDatabase.ps1' @('-ServiceAccount', 'NT SERVICE\HccServer', '-BackupDirectory', 'D:\HccBackups', '-DocumentsDatabase', '')
    $r.Exit -eq 0 -and $r.Out -match 'CREATE DATABASE \[Hcc\]' -and $r.Out -match 'CREATE DATABASE \[HccDocuments\]' -and $r.Out -match 'USE \[HccDocuments\]; IF USER_ID' -and
    $o.Exit -eq 0 -and $o.Out -notmatch 'HccDocuments' -and (DryRun 'Initialize-HccDatabase.ps1' @('-DocumentsDatabase', 'EtpReporting', '-ServiceAccount', 'x\y', '-BackupDirectory', 'D:\b')).Exit -ne 0
}
Check 'LF-docdb Backup -WhatIf plans both databases (main first) and records the documents run in the main database; -Database HccDocuments backs up only that one' {
    $b = DryRun 'Backup-HccDatabase.ps1' @('-Type', 'Full', '-BackupDirectory', $tmp, '-ConfigPath', (Join-Path $tmp 'none.json'))
    $one = DryRun 'Backup-HccDatabase.ps1' @('-Type', 'Full', '-BackupDirectory', $tmp, '-ConfigPath', (Join-Path $tmp 'none.json'), '-Database', 'HccDocuments')
    $iMain = $b.Out.IndexOf('BACKUP DATABASE [Hcc] TO DISK'); $iDocs = $b.Out.IndexOf('BACKUP DATABASE [HccDocuments] TO DISK')
    $b.Exit -eq 0 -and $iMain -ge 0 -and $iDocs -gt $iMain -and $b.Out -match 'database_name = HccDocuments' -and $one.Exit -eq 0 -and $one.Out -notmatch 'BACKUP DATABASE \[Hcc\] '
}
Check 'LF-docdb Restore-HccDrill -WhatIf names both scratch copies and the reference check' {
    $r = DryRun 'Restore-HccDrill.ps1' @('-BackupDirectory', $tmp, '-ConfigPath', (Join-Path $tmp 'none.json'))
    $r.Exit -eq 0 -and $r.Out -match 'documents database: would pick the newest FULL' -and $r.Out -match 'HccDrill_\d+Documents' -and $r.Out -match 'reference check: would count object_ref' -and $r.Out -match 'one RestoreDrill row per database'
}
Check 'DEP-01 Set-HccEgress -State Apply -WhatIf with the rule missing: no database write, exit 1, the catch write is only planned' {
    # No instance answers (and no firewall rule exists on a test PC), so Apply lands in its catch block; under -WhatIf that block may only PLAN the detail write.
    $e = DryRun 'Set-HccEgress.ps1' @('-State', 'Apply', '-Instance', 'localhost\NoSuchInstance_dep01', '-Database', 'Hcc', '-ConfigPath', (Join-Path $tmp 'none.json'))
    $e.Exit -eq 1 -and $e.Out -match 'What if: Performing the operation "record detail = not applied: ' -and $e.Out -match 'ops\.egress_firewall_state' -and $e.Out -notmatch 'record applied_state'
}

# INST-SQL
& {   # own scope: StrictMode, $ErrorActionPreference and the mock functions below must not reach the blocks merged after this one
# Bootstrap-HccSql.ps1: dot-sourced (functions only). Every system seam is replaced by a mock; no installer, service, registry write or SQL connection happens.
. (Join-Path $root 'Bootstrap-HccSql.ps1')
$sqlT = Join-Path ([IO.Path]::GetTempPath()) ('hcc-sqlboot-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $sqlT 'media') -Force | Out-Null
$sqlMedia = Join-Path $sqlT 'media'
$sqlFiles = [ordered]@{ engine = 'SQLEXPR_x64_ENU.exe'; sqlcmd = 'MsSqlCmdLnUtils.msi'; odbc17 = 'msodbcsql17.msi' }
foreach ($k in $sqlFiles.Keys) { [IO.File]::WriteAllText((Join-Path $sqlMedia $sqlFiles[$k]), "fake $k (test only)") }
$sqlManifest = Join-Path $sqlT 'm.json'
$sqlEntries = foreach ($k in $sqlFiles.Keys) { [ordered]@{ name = $sqlFiles[$k]; role = $k; sha256 = (Get-FileHash -LiteralPath (Join-Path $sqlMedia $sqlFiles[$k]) -Algorithm SHA256).Hash.ToLowerInvariant() } }
(@{ version = 1; files = @($sqlEntries) } | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $sqlManifest -Encoding UTF8
$sqlLog = Join-Path $sqlT 'install.log'
$sqlWarn = Join-Path $sqlT 'warnings.json'
$realNewScratch = ${function:New-HccProtectedScratchDirectory}
$realIsAdmin = Test-HccAdministrator
$script:sqlCalls = New-Object System.Collections.Generic.List[string]
$script:sqlSvc = $null; $script:sqlCmdPath = $null; $script:sqlRestart = @(); $script:sqlProcExit = 0; $script:sqlAdmin = $true; $script:sqlMediaOk = $true; $script:sqlOdbc = @(); $script:sqlMediaHook = $null; $script:sqlProbeThrows = $false; $script:sqlProbe = $null
function SqlSvc([string]$Status, [string]$StartMode = 'Auto') { return [pscustomobject]@{ Name = 'MSSQL$SQLEXPRESS'; Status = $Status; StartMode = $StartMode } }
function SqlProbeObj([bool]$Sysadmin = $true, [int]$Major = 17, [bool]$WindowsOnly = $true, $Tcp = $false, $Np = $false, [string[]]$Admins = @('BUILTIN\Administrators')) { return [pscustomobject]@{ IsSysadmin = $Sysadmin; MajorVersion = $Major; WindowsOnly = $WindowsOnly; TcpEnabled = $Tcp; NamedPipesEnabled = $Np; Admins = @($Admins) } }
function Test-HccAdministrator { return $script:sqlAdmin }
function Get-HccSqlServiceState { return $script:sqlSvc }
function Get-HccSqlcmdPath { return $script:sqlCmdPath }
function Get-HccRestartPendingReasons { return $script:sqlRestart }
function Get-HccInstalledOdbcDrivers { return $script:sqlOdbc }
function Get-HccMediaFolderProblems { param([string]$Path) if ($script:sqlMediaHook) { & $script:sqlMediaHook }; if ($script:sqlMediaOk) { return @() }; return @('The media folder can be changed by users.') }
function New-HccProtectedScratchDirectory { $d = Join-Path $sqlT 'scratch'; New-Item -ItemType Directory -Path $d -Force | Out-Null; $script:sqlCalls.Add('scratch'); return $d }
function Start-HccSetupProcess {
    param([string]$FilePath, [string[]]$Arguments = @())
    $leaf = Split-Path -Leaf $FilePath
    $script:sqlCalls.Add('proc:' + $leaf + ' ' + ($Arguments -join ' '))
    if ($leaf -eq 'SQLEXPR_x64_ENU.exe') { Set-Content -LiteralPath (Join-Path $sqlT 'scratch\setup.exe') -Value 'x' }
    if ($leaf -eq 'setup.exe') { $script:sqlSvc = SqlSvc 'Running' }
    if ($leaf -eq 'msiexec.exe' -and ($Arguments -join ' ') -match 'MsSqlCmdLnUtils') { $script:sqlCmdPath = 'C:\fake\SQLCMD.EXE' }
    if ($leaf -eq 'setup.exe') { return $script:sqlProcExit }
    return 0
}
function Start-HccSqlService { $script:sqlCalls.Add('startsvc') }
function Invoke-HccSqlProbe { param($Sqlcmd, $Attempts, $DelaySeconds) $script:sqlCalls.Add('probe'); if ($script:sqlProbeThrows) { throw 'sqlcmd could not reach the instance (mock)' }; return $script:sqlProbe }
function Add-HccSqlcmdToPath { param($Sqlcmd) }
function SqlBoot([string]$Mode, [bool]$Plan = $false, [string]$Media = $sqlMedia) {
    $script:sqlCalls.Clear()
    return (Invoke-HccSqlBootstrap -Mode $Mode -MediaDir $Media -PlanOnly $Plan -LogFile $sqlLog -ManifestPath $sqlManifest -WarningsFile $sqlWarn 6>$null)
}
function SqlReset { $script:sqlSvc = $null; $script:sqlCmdPath = $null; $script:sqlRestart = @(); $script:sqlProcExit = 0; $script:sqlAdmin = $true; $script:sqlOdbc = @(); $script:sqlMediaOk = $true; $script:sqlMediaHook = $null; $script:sqlProbeThrows = $false; $script:sqlProbe = SqlProbeObj; Remove-Item -LiteralPath $sqlLog, $sqlWarn -ErrorAction SilentlyContinue }
$sqlRunning = SqlSvc 'Running'
SqlReset

Check 'INST-SQL committed manifest: five files with roles engine / sqlcmd / odbc17 and valid hashes' {
    $m = Get-HccSqlMediaManifest -Path (Join-Path $root 'sql-media.manifest.json')
    $m.Count -eq 5 -and (@($m | Where-Object role -eq 'engine').name -eq 'SQLEXPR_x64_ENU.exe') -and (@($m | Where-Object role -eq 'odbc17').name -eq 'msodbcsql17.msi') -and (@($m | Where-Object role -eq 'sqlcmd').name -eq 'MsSqlCmdLnUtils.msi')
}
Check 'INST-SQL setup arguments: Windows authentication only (no SECURITYMODE), no TCP / pipes, Administrators sysadmin, instance SQLEXPRESS' {
    $a = (Get-HccSqlSetupArguments) -join ' '
    $a -match '/INSTANCENAME=SQLEXPRESS' -and $a -match '/TCPENABLED=0' -and $a -match '/NPENABLED=0' -and $a -match '/SQLSYSADMINACCOUNTS=BUILTIN\\Administrators' -and $a -match '/QUIET' -and $a -notmatch 'SECURITYMODE|SAPWD'
}
Check 'INST-SQL media check: good files pass; wrong hash, wrong size, missing file, unknown role and missing folder are reported' {
    $m = Get-HccSqlMediaManifest -Path $sqlManifest
    $ok = @(Test-HccSqlMedia -MediaDir $sqlMedia -Manifest $m -Roles @('engine', 'sqlcmd', 'odbc17'))
    [IO.File]::WriteAllText((Join-Path $sqlMedia 'msodbcsql17.msi'), 'tampered')
    $bad = @(Test-HccSqlMedia -MediaDir $sqlMedia -Manifest $m -Roles @('odbc17'))
    [IO.File]::WriteAllText((Join-Path $sqlMedia 'msodbcsql17.msi'), 'fake odbc17 (test only)')
    $sized = @([pscustomobject]@{ name = 'msodbcsql17.msi'; role = 'odbc17'; sha256 = ($m | Where-Object role -eq 'odbc17').sha256; bytes = 5 })
    $wrongSize = @(Test-HccSqlMedia -MediaDir $sqlMedia -Manifest $sized -Roles @('odbc17'))
    Remove-Item (Join-Path $sqlMedia 'MsSqlCmdLnUtils.msi')
    $miss = @(Test-HccSqlMedia -MediaDir $sqlMedia -Manifest $m -Roles @('sqlcmd'))
    [IO.File]::WriteAllText((Join-Path $sqlMedia 'MsSqlCmdLnUtils.msi'), 'fake sqlcmd (test only)')
    $unk = @(Test-HccSqlMedia -MediaDir $sqlMedia -Manifest $m -Roles @('nothing'))
    $nodir = @(Test-HccSqlMedia -MediaDir (Join-Path $sqlT 'nope') -Manifest $m -Roles @('engine'))
    $ok.Count -eq 0 -and $bad.Count -eq 1 -and $bad[0] -match 'SHA-256' -and $wrongSize.Count -eq 1 -and $wrongSize[0] -match 'wrong size' -and $miss.Count -eq 1 -and $miss[0] -match 'Missing' -and $unk.Count -eq 1 -and $nodir.Count -eq 1
}
Check 'INST-SQL plan: pure outcomes (adopt, install, refuse, restart) and the media each needs' {
    $a = Get-HccSqlBootstrapPlan -Mode InstallBundled -ServiceExists $true -SqlcmdPresent $true -Odbc17Present $true
    $i = Get-HccSqlBootstrapPlan -Mode InstallBundled -ServiceExists $false -SqlcmdPresent $false -Odbc17Present $false
    $r = Get-HccSqlBootstrapPlan -Mode UseExisting -ServiceExists $false -SqlcmdPresent $true -Odbc17Present $true
    $w = Get-HccSqlBootstrapPlan -Mode InstallBundled -ServiceExists $false -SqlcmdPresent $true -Odbc17Present $true -RestartReasons @('Windows Update')
    $aw = Get-HccSqlBootstrapPlan -Mode InstallBundled -ServiceExists $true -SqlcmdPresent $true -Odbc17Present $true -RestartReasons @('Windows Update')
    $a.Outcome -eq 'Adopt' -and $a.Roles.Count -eq 0 -and $i.Outcome -eq 'Install' -and (($i.Roles -join ',') -eq 'engine,odbc17,sqlcmd') -and $r.Outcome -eq 'Refuse' -and $w.Outcome -eq 'RestartNeeded' -and $aw.Outcome -eq 'Adopt'
}
Check 'INST-SQL probe parser on real-looking sqlcmd output (CRLF, blank lines, several HCC_ rows): every field read from its own match' {
    $real = "HCC_PROBE:1|17|1`r`nHCC_TCP:0`r`nHCC_NP:0`r`nHCC_ADMIN:BUILTIN\Administrators`r`nHCC_ADMIN:NT SERVICE\MSSQL`$SQLEXPRESS`r`n`r`n"
    $p = ConvertFrom-HccSqlProbeOutput -Text $real
    $mixed = ConvertFrom-HccSqlProbeOutput -Text "HCC_PROBE:1|16|0`r`nHCC_TCP:1`r`nHCC_NP:1`r`nHCC_ADMIN:HOTELPC\owner`r`n"
    $non = ConvertFrom-HccSqlProbeOutput -Text "HCC_PROBE:0|17|1`r`n"
    $p.IsSysadmin -eq $true -and $p.MajorVersion -eq 17 -and $p.WindowsOnly -eq $true -and $p.TcpEnabled -eq $false -and $p.NamedPipesEnabled -eq $false -and $p.Admins.Count -eq 2 -and
    $mixed.IsSysadmin -eq $true -and $mixed.MajorVersion -eq 16 -and $mixed.WindowsOnly -eq $false -and $mixed.TcpEnabled -eq $true -and $mixed.NamedPipesEnabled -eq $true -and $mixed.Admins[0] -eq 'HOTELPC\owner' -and
    $non.IsSysadmin -eq $false -and $non.MajorVersion -eq 17 -and $null -eq $non.TcpEnabled -and
    $null -eq (ConvertFrom-HccSqlProbeOutput -Text 'Msg 18456, Level 14: Login failed') -and $null -eq (ConvertFrom-HccSqlProbeOutput -Text '')
}
Check 'INST-SQL adoption warnings: start type, mixed mode + TCP as one stronger warning, named pipes, other sysadmins; a clean instance gives none' {
    $clean = @(Get-HccSqlAdoptionWarnings -Probe (SqlProbeObj) -StartMode 'Auto')
    $both = @(Get-HccSqlAdoptionWarnings -Probe (SqlProbeObj -WindowsOnly $false -Tcp $true -Np $true -Admins @('BUILTIN\Administrators', 'NT SERVICE\MSSQL$SQLEXPRESS', 'HOTELPC\ownerx')) -StartMode 'Manual')
    $codes = @($both | ForEach-Object Code)
    $mixedOnly = @(Get-HccSqlAdoptionWarnings -Probe (SqlProbeObj -WindowsOnly $false) -StartMode 'Auto')
    $clean.Count -eq 0 -and ($codes -contains 'StartTypeNotAutomatic') -and ($codes -contains 'TcpAndMixedMode') -and ($codes -notcontains 'MixedMode') -and ($codes -notcontains 'TcpEnabled') -and ($codes -contains 'NamedPipesEnabled') -and ($codes -contains 'OtherSysadmins') -and (($both | Where-Object Code -eq 'OtherSysadmins').Text -match 'HOTELPC\\ownerx') -and (($both | Where-Object Code -eq 'OtherSysadmins').Text -notmatch 'NT SERVICE') -and $mixedOnly.Count -eq 1 -and $mixedOnly[0].Code -eq 'MixedMode'
}
Check 'INST-SQL adopt path: instance running and sqlcmd present = exit 0, no installer, no service start, only the read-only probe' {
    SqlReset; $script:sqlSvc = $sqlRunning; $script:sqlCmdPath = 'C:\fake\SQLCMD.EXE'
    $e = SqlBoot 'InstallBundled'
    $e -eq 0 -and ($script:sqlCalls -join '|') -eq 'probe' -and (Get-Content -LiteralPath $sqlLog -Raw) -match 'adopted'
}
Check 'INST-SQL adopt path with the service stopped: starts it (no restart), exit 0, no installer' {
    SqlReset; $script:sqlSvc = SqlSvc 'Stopped'; $script:sqlCmdPath = 'C:\fake\SQLCMD.EXE'
    $e = SqlBoot 'UseExisting'
    $e -eq 0 -and ($script:sqlCalls -join '|') -eq 'startsvc|probe'
}
Check 'INST-SQL adopt path, service Disabled and stopped: plain exit 1, the service is not touched' {
    SqlReset; $script:sqlSvc = SqlSvc 'Stopped' 'Disabled'; $script:sqlCmdPath = 'C:\fake\SQLCMD.EXE'
    $e = SqlBoot 'UseExisting'
    $e -eq 1 -and $script:sqlCalls.Count -eq 0 -and (Get-Content -LiteralPath $sqlLog -Raw) -match 'switched off'
}
Check 'INST-SQL adopt path writes the warnings file (start type Manual + mixed mode + TCP) and still exits 0 without changing anything' {
    SqlReset; $script:sqlSvc = SqlSvc 'Running' 'Manual'; $script:sqlCmdPath = 'C:\fake\SQLCMD.EXE'
    $script:sqlProbe = SqlProbeObj -WindowsOnly $false -Tcp $true
    $e = SqlBoot 'UseExisting'
    $j = Get-Content -LiteralPath $sqlWarn -Raw | ConvertFrom-Json
    $codes = @($j.warnings | ForEach-Object code)
    $e -eq 0 -and ($script:sqlCalls -join '|') -eq 'probe' -and ($codes -contains 'StartTypeNotAutomatic') -and ($codes -contains 'TcpAndMixedMode') -and (@($j.warnings)[0].text.Length -gt 20)
}
Check 'INST-SQL adopt path without sqlcmd installs only ODBC 17 + sqlcmd from verified media, never the engine' {
    SqlReset; $script:sqlSvc = $sqlRunning
    $e = SqlBoot 'UseExisting'
    $c = $script:sqlCalls -join '|'
    $e -eq 0 -and $c -match 'msodbcsql17\.msi' -and $c -match 'MsSqlCmdLnUtils\.msi' -and $c -notmatch 'SQLEXPR' -and $c -notmatch 'setup\.exe' -and $c -match 'IACCEPTMSSQLCMDLNUTILSLICENSETERMS=YES'
}
Check 'INST-SQL adopt: a non-sysadmin account or a too-old SQL Server fails with exit 1; the instance is only probed' {
    SqlReset; $script:sqlSvc = $sqlRunning; $script:sqlCmdPath = 'C:\fake\SQLCMD.EXE'
    $script:sqlProbe = SqlProbeObj -Sysadmin $false; $e1 = SqlBoot 'UseExisting'; $c1 = $script:sqlCalls -join '|'
    $script:sqlProbe = SqlProbeObj -Major 14; $e2 = SqlBoot 'UseExisting'
    $e1 -eq 1 -and $e2 -eq 1 -and $c1 -eq 'probe'
}
Check 'INST-SQL UseExisting with no instance: exit 1, nothing installed' {
    SqlReset; $script:sqlCmdPath = 'C:\fake\SQLCMD.EXE'
    $e = SqlBoot 'UseExisting'
    $e -eq 1 -and $script:sqlCalls.Count -eq 0
}
Check 'INST-SQL install path: extract, setup with the silent switches, then ODBC 17, then sqlcmd; scratch removed; exit 0' {
    SqlReset
    $e = SqlBoot 'InstallBundled'
    $c = @($script:sqlCalls)
    function IdxOf([string[]]$List, [string]$Like) { for ($n = 0; $n -lt $List.Count; $n++) { if ($List[$n] -like $Like) { return $n } }; return -1 }
    $iEx = IdxOf $c 'proc:SQLEXPR*'; $iSet = IdxOf $c 'proc:setup.exe*'; $iOdbc = IdxOf $c '*msodbcsql17*'; $iCmd = IdxOf $c '*MsSqlCmdLnUtils*'
    $e -eq 0 -and $iEx -ge 0 -and $iSet -gt $iEx -and $iOdbc -gt $iSet -and $iCmd -gt $iOdbc -and $c[$iSet] -match '/INSTANCENAME=SQLEXPRESS' -and $c[$iSet] -match '/TCPENABLED=0' -and $c[$iEx] -match '^proc:SQLEXPR_x64_ENU\.exe /Q /X:' -and -not (Test-Path (Join-Path $sqlT 'scratch'))
}
Check 'INST-SQL install path: wrong-hash media is refused before any installer runs (exit 1, no process, no scratch folder)' {
    SqlReset
    [IO.File]::WriteAllText((Join-Path $sqlMedia 'SQLEXPR_x64_ENU.exe'), 'swapped by someone')
    $e = SqlBoot 'InstallBundled'
    [IO.File]::WriteAllText((Join-Path $sqlMedia 'SQLEXPR_x64_ENU.exe'), 'fake engine (test only)')
    $e -eq 1 -and $script:sqlCalls.Count -eq 0 -and (Get-Content -LiteralPath $sqlLog -Raw) -match 'not the expected file'
}
Check 'INST-SQL a file swapped AFTER the first check is caught by the hash taken right before it runs (no msiexec for it)' {
    SqlReset
    $script:sqlMediaHook = { [IO.File]::WriteAllText((Join-Path $sqlMedia 'msodbcsql17.msi'), 'swapped after the check') }
    $e = SqlBoot 'InstallBundled'
    [IO.File]::WriteAllText((Join-Path $sqlMedia 'msodbcsql17.msi'), 'fake odbc17 (test only)')
    $e -eq 1 -and -not (@($script:sqlCalls) -like '*msodbcsql17*') -and (Get-Content -LiteralPath $sqlLog -Raw) -match 'not the expected file'
}
Check 'INST-SQL install path: missing media file, a folder non-administrators can write, and no folder at all are refused with nothing installed' {
    SqlReset
    Remove-Item (Join-Path $sqlMedia 'SQLEXPR_x64_ENU.exe')
    $e1 = SqlBoot 'InstallBundled'; $n1 = $script:sqlCalls.Count
    [IO.File]::WriteAllText((Join-Path $sqlMedia 'SQLEXPR_x64_ENU.exe'), 'fake engine (test only)')
    $script:sqlMediaOk = $false
    $e2 = SqlBoot 'InstallBundled'; $n2 = $script:sqlCalls.Count
    $e3 = SqlBoot 'InstallBundled' $false ''
    $e1 -eq 1 -and $n1 -eq 0 -and $e2 -eq 1 -and $n2 -eq 0 -and $e3 -eq 1
}
Check 'INST-SQL restart pending before an install: exit 2, nothing installed or extracted' {
    SqlReset; $script:sqlRestart = @('Windows Update')
    $e = SqlBoot 'InstallBundled'
    $e -eq 2 -and $script:sqlCalls.Count -eq 0
}
Check 'INST-SQL setup exit 3010 finishes the work and returns 2; exit 1603 returns 1; a non-administrator returns 1 without any process' {
    SqlReset; $script:sqlProcExit = 3010; $e1 = SqlBoot 'InstallBundled'
    SqlReset; $script:sqlProcExit = 1603; $e2 = SqlBoot 'InstallBundled'
    SqlReset; $script:sqlAdmin = $false; $e3 = SqlBoot 'InstallBundled'; $n3 = $script:sqlCalls.Count
    $e1 -eq 2 -and $e2 -eq 1 -and $e3 -eq 1 -and $n3 -eq 0
}
Check 'INST-SQL setup exit 3010 and the probe then fails: exit 2 (restart, run again), not 1; without 3010 the same failure is exit 1' {
    SqlReset; $script:sqlProcExit = 3010; $script:sqlProbeThrows = $true
    $e1 = SqlBoot 'InstallBundled'; $w = Get-Content -LiteralPath $sqlWarn -Raw
    SqlReset; $script:sqlProbeThrows = $true
    $e2 = SqlBoot 'InstallBundled'
    $e1 -eq 2 -and $w -match 'RestartRequested' -and $e2 -eq 1
}
Check 'INST-SQL scratch folder: the security handed to the creation call is owner Administrators, protected, SYSTEM + Administrators only' {
    $sddl = (Get-HccScratchDirectorySecurity).GetSecurityDescriptorSddlForm('All')
    $sddl -match '^O:BA' -and $sddl -match 'D:P' -and $sddl -match '\(A;OICI;FA;;;SY\)' -and $sddl -match '\(A;OICI;FA;;;BA\)' -and ([regex]::Matches($sddl, '\(A;')).Count -eq 2
}
Check 'INST-SQL scratch folder (real function, under TEMP): created with its protection (administrator) or refused and removed (non-administrator); an existing folder is never reused' {
    $before = @(Get-ChildItem -LiteralPath $sqlT -Directory | ForEach-Object Name)
    $made = $null; $threw = $false
    try { $made = & $realNewScratch -BasePath $sqlT } catch { $threw = $true }
    $left = @(Get-ChildItem -LiteralPath $sqlT -Directory -Filter 'HccSqlSetup-*')
    $okAdmin = $false
    if ($made) {
        $acl = Get-Acl -LiteralPath $made
        $okAdmin = $acl.AreAccessRulesProtected -and (@($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) | Where-Object { @('S-1-5-18', 'S-1-5-32-544') -notcontains $_.IdentityReference.Value }).Count -eq 0)
        Remove-Item -LiteralPath $made -Recurse -Force
    }
    if ($realIsAdmin) { $okAdmin -and -not $threw } else { $threw -and $left.Count -eq 0 }
}
Check 'INST-SQL sqlcmd is only taken from SQL Server client folders under Program Files; no PATH fallback in the script' {
    $c = @(Get-HccSqlcmdCandidates)
    $t = Get-Content -LiteralPath (Join-Path $root 'Bootstrap-HccSql.ps1') -Raw
    $c.Count -ge 2 -and (@($c | Where-Object { $_ -notmatch 'Microsoft SQL Server\\Client SDK\\ODBC\\(170|180)\\Tools\\Binn\\SQLCMD\.EXE$' -or $_ -notmatch '(?i)Program Files' }).Count -eq 0) -and $t -notmatch 'Get-Command\s+sqlcmd'
}
Check 'INST-SQL -PlanOnly changes nothing: no process, no service start, no probe, no log file, no warnings file, exit 0 (install and adopt)' {
    SqlReset
    $e1 = SqlBoot 'InstallBundled' $true; $n1 = $script:sqlCalls.Count
    $script:sqlSvc = SqlSvc 'Stopped'
    $e2 = SqlBoot 'InstallBundled' $true; $n2 = $script:sqlCalls.Count
    $e1 -eq 0 -and $n1 -eq 0 -and $e2 -eq 0 -and $n2 -eq 0 -and -not (Test-Path -LiteralPath $sqlLog) -and -not (Test-Path -LiteralPath $sqlWarn)
}
Check 'INST-SQL -PlanOnly with bad media reports it (exit 1) and still changes nothing' {
    SqlReset
    [IO.File]::WriteAllText((Join-Path $sqlMedia 'SQLEXPR_x64_ENU.exe'), 'swapped')
    $e = SqlBoot 'InstallBundled' $true
    [IO.File]::WriteAllText((Join-Path $sqlMedia 'SQLEXPR_x64_ENU.exe'), 'fake engine (test only)')
    $e -eq 1 -and $script:sqlCalls.Count -eq 0 -and -not (Test-Path -LiteralPath $sqlLog)
}
Check 'INST-SQL real script, -PlanOnly, UseExisting: runs as a separate process, only reads, exit 0 (instance there) or 1 (none)' {
    $r = & $psExe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root 'Bootstrap-HccSql.ps1') -Mode UseExisting -PlanOnly 2>&1 | Out-String
    ($LASTEXITCODE -in 0, 1) -and $r -match 'plan only' -and $r -notmatch 'Running SQL Server setup'
}
Check 'INST-SQL script file handles no password: no SQL-login switches or credential handling' {
    $t = Get-Content -LiteralPath (Join-Path $root 'Bootstrap-HccSql.ps1') -Raw
    $t -notmatch '(?i)\bSAPWD\b|SECURITYMODE=SQL|ConvertTo-SecureString|-Password'
}
Remove-Item -LiteralPath $sqlT -Recurse -Force -ErrorAction SilentlyContinue
}
Check 'INST-SQL the block leaks nothing: no mock functions, no strict-mode or error-preference change visible to the blocks after it' {
    -not (Get-Command Get-HccMediaFolderProblems -ErrorAction SilentlyContinue) -and -not (Get-Command Start-HccSetupProcess -ErrorAction SilentlyContinue) -and -not (Get-Command SqlBoot -ErrorAction SilentlyContinue) -and $ErrorActionPreference -eq 'Stop'
}
# /INST-SQL
# INST-CLIENT: client setup + client kit. Test certificates are made in memory with .NET and written as files in a temp folder
# that is deleted afterwards; nothing is imported into any Windows certificate store, no setup is run.
. (Join-Path $root 'Hcc-ClientTrust.ps1')   # functions only (no -Action)
$ct = Join-Path ([IO.Path]::GetTempPath()) ('hcc-client-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $ct | Out-Null
try {
    function New-TestCert([string]$Subject, [bool]$IsCa, [bool]$NameConstraint, [int]$DaysValid = 30, [int]$StartDaysAgo = 1) {
        $rsa = [Security.Cryptography.RSA]::Create(2048)
        $req = New-Object Security.Cryptography.X509Certificates.CertificateRequest($Subject, $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $req.CertificateExtensions.Add((New-Object Security.Cryptography.X509Certificates.X509BasicConstraintsExtension($IsCa, $IsCa, 0, $true)))
        if ($NameConstraint) { $req.CertificateExtensions.Add((New-HccNameConstraintsExtension -DnsNames @('hotel-pc', 'localhost') -IpNetworks @('192.168.1.10', '127.0.0.1'))) }
        $c = $req.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-$StartDaysAgo), [DateTimeOffset]::UtcNow.AddDays($DaysValid))
        return $c
    }
    function Save-Cer($c, [string]$name) { $p = Join-Path $ct $name; [IO.File]::WriteAllBytes($p, $c.RawData); return $p }
    $caCert = New-TestCert 'CN=HCC Hotel CA' $true $true
    $caFile = Save-Cer $caCert 'HCC-Hotel-CA.cer'
    $caSha = Get-HccCertificateSha256 $caCert
    $noNcFile = Save-Cer (New-TestCert 'CN=Plain CA' $true $false) 'plain-ca.cer'
    $leafFile = Save-Cer (New-TestCert 'CN=hotel-pc' $false $false) 'leaf.cer'
    $expiredFile = Save-Cer (New-TestCert 'CN=Old CA' $true $true -5 10) 'expired.cer'
    $junkFile = Join-Path $ct 'junk.cer'; [IO.File]::WriteAllText($junkFile, 'this is not a certificate')
    $pemFile = Join-Path $ct 'ca.pem'
    [IO.File]::WriteAllText($pemFile, "-----BEGIN CERTIFICATE-----`n" + [Convert]::ToBase64String($caCert.RawData, 'InsertLineBreaks') + "`n-----END CERTIFICATE-----`n")
    $storeBefore = @(Get-ChildItem Cert:\LocalMachine\Root | ForEach-Object { $_.Thumbprint })

    Check 'INST-CLIENT url check: https only, plain host and port, normalised' {
        (Test-HccServerUrl 'https://hotel-pc:5443/').Url -eq 'https://hotel-pc:5443/' -and (Test-HccServerUrl 'https://192.168.1.10:5443').Ok -and
        (Test-HccServerUrl 'https://hotel-pc').Url -eq 'https://hotel-pc/' -and
        -not (Test-HccServerUrl 'http://hotel-pc:5443/').Ok -and -not (Test-HccServerUrl 'hotel-pc:5443').Ok -and -not (Test-HccServerUrl '').Ok -and
        -not (Test-HccServerUrl 'https://hotel-pc:5443/a?b=1').Ok -and -not (Test-HccServerUrl 'https://u:p@hotel-pc/').Ok -and
        -not (Test-HccServerUrl 'https://hotel pc/').Ok -and -not (Test-HccServerUrl 'https://a"b/').Ok -and -not (Test-HccServerUrl 'https://hotel-pc:70000/').Ok
    }
    Check 'INST-CLIENT certificate check: accepts a name-constrained CA (DER and PEM), refuses non-CA, no name constraint, expired, junk, missing' {
        $a = Get-HccCaFileInfo $caFile; $p = Get-HccCaFileInfo $pemFile
        $a.Ok -and $a.Sha256 -eq $caSha -and $p.Ok -and $p.Sha256 -eq $caSha -and
        -not (Get-HccCaFileInfo $leafFile).Ok -and (Get-HccCaFileInfo $leafFile).Error -match 'not a CA' -and
        -not (Get-HccCaFileInfo $noNcFile).Ok -and (Get-HccCaFileInfo $noNcFile).Error -match 'name restriction' -and
        -not (Get-HccCaFileInfo $expiredFile).Ok -and (Get-HccCaFileInfo $expiredFile).Error -match 'expired' -and
        -not (Get-HccCaFileInfo $junkFile).Ok -and -not (Get-HccCaFileInfo (Join-Path $ct 'missing.cer')).Ok
    }
    Check 'INST-CLIENT fingerprint helpers' {
        (ConvertTo-HccFingerprint ((Format-HccFingerprint $caSha).ToLowerInvariant())) -eq $caSha -and (ConvertTo-HccFingerprint 'abc') -eq '' -and
        (Format-HccFingerprint 'AABBCC') -eq 'AA:BB:CC'
    }

    # --- the kit -------------------------------------------------------------------------------------------------------------------
    $kit = Join-Path $ct 'kit'; $stub = Join-Path $ct 'HCC-Client-Setup-9.9.9.exe'; [IO.File]::WriteAllText($stub, 'stub')
    $kitScript = Join-Path $root 'New-HccClientKit.ps1'
    function RunKit([string[]]$KitArgs) {
        $ErrorActionPreference = 'Continue'
        # Windows PowerShell 5.1 passes arguments to a native exe without escaping embedded double quotes: escape them the way its parser expects.
        if ($PSVersionTable.PSVersion.Major -lt 7) { $KitArgs = @($KitArgs | ForEach-Object { $_ -replace '"', '\"' }) }
        $o = & $psExe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $kitScript @KitArgs 2>&1 | Out-String
        return [pscustomobject]@{ Exit = $LASTEXITCODE; Out = $o }
    }
    Check 'INST-CLIENT kit -WhatIf writes nothing' {
        $r = RunKit @('-ServerName', 'hotel-pc', '-Port', '5443', '-HotelName', 'Hotel Parth', '-CaCer', $caFile, '-OutDir', $kit, '-ClientSetupExe', $stub, '-WhatIf')
        $r.Exit -eq 0 -and $r.Out -match 'DRY RUN plan' -and -not (Test-Path -LiteralPath $kit)
    }
    $hotelName = "Hotel ""Parth"" & Sons; 100% caf" + [char]0x00E9 + ' ' + [char]0x0939
    Check 'INST-CLIENT kit contents and json shape (hotel name with quotes, &, ;, %, Unicode survives)' {
        $r = RunKit @('-ServerName', 'hotel-pc', '-Port', '5443', '-HotelName', $hotelName, '-CaCer', $pemFile, '-OutDir', $kit, '-ClientSetupExe', $stub)
        $names = @(Get-ChildItem -LiteralPath $kit | ForEach-Object { $_.Name } | Sort-Object)
        $j = [IO.File]::ReadAllText((Join-Path $kit 'hcc-client.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
        $props = @($j.PSObject.Properties | ForEach-Object { $_.Name })
        $cer = Get-HccCaFileInfo (Join-Path $kit 'HCC-Hotel-CA.cer')
        $readme = [IO.File]::ReadAllText((Join-Path $kit 'READ-ME.txt'), [Text.Encoding]::UTF8)
        $r.Exit -eq 0 -and ($names -join '|') -eq ((@('HCC-Client-Setup-9.9.9.exe', 'HCC-Hotel-CA.cer', 'hcc-client.json', 'READ-ME.txt') | Sort-Object) -join '|') -and
        ($props -join ',') -eq 'serverUrl,hotelName,caSha256' -and $j.serverUrl -eq 'https://hotel-pc:5443/' -and $j.hotelName -eq $hotelName -and
        $j.caSha256 -eq $caSha -and $cer.Ok -and $cer.Sha256 -eq $caSha -and $readme.Contains((Format-HccFingerprint $caSha)) -and $readme.Contains('https://hotel-pc:5443/') -and
        ([IO.File]::ReadAllBytes((Join-Path $kit 'HCC-Hotel-CA.cer'))[0] -eq 0x30)
    }
    Check 'INST-CLIENT kit is idempotent (second run, same result)' {
        $before = (Get-Content -LiteralPath (Join-Path $kit 'hcc-client.json') -Raw)
        $r = RunKit @('-ServerName', 'hotel-pc', '-Port', '5443', '-HotelName', $hotelName, '-CaCer', $caFile, '-OutDir', $kit, '-ClientSetupExe', $stub)
        $r.Exit -eq 0 -and (Get-Content -LiteralPath (Join-Path $kit 'hcc-client.json') -Raw) -eq $before
    }
    Check 'INST-CLIENT kit refuses a non-CA certificate, no name constraint, a non-https-capable name, a bad hotel name and a missing setup exe' {
        $bad = @(
            @('-ServerName', 'hotel-pc', '-HotelName', 'H', '-CaCer', $leafFile),
            @('-ServerName', 'hotel-pc', '-HotelName', 'H', '-CaCer', $noNcFile),
            @('-ServerName', 'hotel pc;calc', '-HotelName', 'H', '-CaCer', $caFile),
            @('-ServerName', 'https://hotel-pc', '-HotelName', 'H', '-CaCer', $caFile),
            @('-ServerName', 'hotel-pc', '-HotelName', "a`r`nb", '-CaCer', $caFile)
        )
        $allRefused = $true
        $n = 0
        foreach ($b in $bad) { $n++; $o = Join-Path $ct "bad$n"; $r = RunKit ($b + @('-OutDir', $o, '-ClientSetupExe', $stub)); if ($r.Exit -eq 0 -or (Test-Path -LiteralPath $o)) { $allRefused = $false } }
        $none = RunKit @('-ServerName', 'hotel-pc', '-HotelName', 'H', '-CaCer', $caFile, '-OutDir', (Join-Path $ct 'nosetup'))
        $allRefused -and $none.Exit -ne 0 -and -not (Test-Path -LiteralPath (Join-Path $ct 'nosetup'))
    }

    # --- the setup's checks: fingerprint mismatch, non-https ------------------------------------------------------------------------
    function Write-TestCfg([string]$Name, [string]$Url, [string]$Sha) {
        $p = Join-Path $ct $Name
        [IO.File]::WriteAllText($p, ([ordered]@{ serverUrl = $Url; hotelName = 'Hotel Parth'; caSha256 = $Sha } | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
        return $p
    }
    function Write-TestCfgName([string]$Name, [string]$Hotel) {
        $p = Join-Path $ct $Name
        [IO.File]::WriteAllText($p, ([ordered]@{ serverUrl = 'https://hotel-pc:5443/'; hotelName = $Hotel; caSha256 = $caSha } | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
        return $p
    }
    Check 'INST-CLIENT setup check passes for a matching kit and reports the fingerprint' {
        $i = Test-HccClientInputs -CerPath $caFile -ConfigPath (Write-TestCfg 'ok.json' 'https://hotel-pc:5443/' $caSha)
        $i.Ok -and $i.FingerprintMatches -and $i.ExpectedKnown -and $i.Sha256Display -eq (Format-HccFingerprint $caSha) -and $i.HotelName -eq 'Hotel Parth'
    }
    Check 'INST-CLIENT setup check refuses a fingerprint mismatch' {
        $other = Get-HccCertificateSha256 (New-TestCert 'CN=Other CA' $true $true)
        $i = Test-HccClientInputs -CerPath $caFile -ConfigPath (Write-TestCfg 'mismatch.json' 'https://hotel-pc:5443/' $other)
        -not $i.Ok -and $i.Error -match 'does not match' -and -not $i.FingerprintMatches
    }
    Check 'INST-CLIENT setup check refuses a non-https address, a garbled fingerprint and a non-CA file' {
        (-not (Test-HccClientInputs -CerPath $caFile -ConfigPath (Write-TestCfg 'http.json' 'http://hotel-pc:5443/' $caSha)).Ok) -and
        (-not (Test-HccClientInputs -CerPath $caFile -ConfigPath (Write-TestCfg 'garble.json' 'https://hotel-pc:5443/' 'zz')).Ok) -and
        (-not (Test-HccClientInputs -CerPath $leafFile -ConfigPath (Write-TestCfg 'ok2.json' 'https://hotel-pc:5443/' $caSha)).Ok) -and
        (-not (Test-HccClientInputs -CerPath $caFile -ServerUrl 'http://x/').Ok) -and (Test-HccClientInputs -CerPath $caFile -ServerUrl 'https://hotel-pc:5443/').Ok
    }
    Check 'INST-CLIENT command-line Inspect writes the result file and exits 1 on a mismatch' {
        $script = Join-Path $root 'Hcc-ClientTrust.ps1'
        $outOk = Join-Path $ct 'inspect-ok.txt'; $outBad = Join-Path $ct 'inspect-bad.txt'
        $ErrorActionPreference = 'Continue'
        & $psExe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $script -Action Inspect -CerPath $caFile -ConfigPath (Join-Path $ct 'ok.json') -OutFile $outOk | Out-Null; $e1 = $LASTEXITCODE
        & $psExe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $script -Action Inspect -CerPath $caFile -ConfigPath (Join-Path $ct 'mismatch.json') -OutFile $outBad | Out-Null; $e2 = $LASTEXITCODE
        $ok = [IO.File]::ReadAllText($outOk, [Text.Encoding]::Unicode); $bad = [IO.File]::ReadAllText($outBad, [Text.Encoding]::Unicode)
        $e1 -eq 0 -and $e2 -eq 1 -and $ok -match '(?m)^ok=1' -and $ok -match [regex]::Escape('url=https://hotel-pc:5443/') -and $bad -match '(?m)^ok=0' -and $bad -match 'error=The certificate file does not match'
    }
    Check 'INST-CLIENT reachability classification (mocked exceptions) and a closed local port answers "unreachable" without throwing' {
        $trust = New-Object Net.WebException('x', $null, [Net.WebExceptionStatus]::TrustFailure, $null)
        $conn = New-Object Net.WebException('Unable to connect to the remote server', $null, [Net.WebExceptionStatus]::ConnectFailure, $null)
        $auth = New-Object Net.WebException('outer', (New-Object Security.Authentication.AuthenticationException('The remote certificate is invalid')))
        $r = Test-HccServerReachable -Url 'https://127.0.0.1:1/' -TimeoutSec 3
        (Get-HccReachabilityKind $trust) -eq 'certificate' -and (Get-HccReachabilityKind $conn) -eq 'unreachable' -and (Get-HccReachabilityKind $auth) -eq 'certificate' -and
        (Get-HccReachabilityKind (New-Object InvalidOperationException('boom'))) -eq 'other' -and $r.Kind -eq 'unreachable' -and $r.Message -match 'setup is complete'
    }

    # --- a store seam with a fake: no test touches the live LocalMachine\Root --------------------------------------------------------
    function New-FakeRootStore([object[]]$Initial = @()) {
        $f = [pscustomobject]@{ Certs = (New-Object System.Collections.ArrayList); Calls = (New-Object System.Collections.ArrayList) }
        foreach ($c in $Initial) { [void]$f.Certs.Add($c) }
        $f | Add-Member -MemberType ScriptMethod -Name Open -Value { param($flags) [void]$this.Calls.Add('open:' + $flags) }
        $f | Add-Member -MemberType ScriptMethod -Name Close -Value { [void]$this.Calls.Add('close') }
        $f | Add-Member -MemberType ScriptMethod -Name Add -Value { param($c) [void]$this.Calls.Add('add:' + $c.Thumbprint); [void]$this.Certs.Add($c) }
        $f | Add-Member -MemberType ScriptMethod -Name Remove -Value { param($c) [void]$this.Calls.Add('remove:' + $c.Thumbprint); $this.Certs.Remove($c) }
        $f | Add-Member -MemberType ScriptProperty -Name Certificates -Value { $this.Certs.ToArray() }
        return $f
    }
    Check 'INST-CLIENT Trust (fake store): adds the one approved certificate once, "already" the second time, refusals and -WhatIf never touch the store' {
        $fake = New-FakeRootStore
        $sf = { $fake }.GetNewClosure()
        $r1 = Add-HccTrustedRoot -CerPath $caFile -ApprovedSha256 $caSha -StoreFactory $sf
        $r2 = Add-HccTrustedRoot -CerPath $caFile -ApprovedSha256 $caSha -StoreFactory $sf
        $adds = @($fake.Calls | Where-Object { $_ -like 'add:*' })
        $fake2 = New-FakeRootStore; $sf2 = { $fake2 }.GetNewClosure()
        $t1 = Throws { Add-HccTrustedRoot -CerPath $caFile -ApprovedSha256 ('AB' * 32) -StoreFactory $sf2 }
        $t2 = Throws { Add-HccTrustedRoot -CerPath $leafFile -ApprovedSha256 $caSha -StoreFactory $sf2 }
        $t3 = Throws { Add-HccTrustedRoot -CerPath $caFile -ApprovedSha256 '' -StoreFactory $sf2 }
        $t4 = Throws { Add-HccTrustedRoot -CerPath $noNcFile -ApprovedSha256 $caSha -StoreFactory $sf2 }
        $w = Add-HccTrustedRoot -CerPath $caFile -ApprovedSha256 $caSha -StoreFactory $sf2 -WhatIf
        $r1 -eq 'added' -and $r2 -eq 'already' -and $adds.Count -eq 1 -and $adds[0] -eq ('add:' + $caCert.Thumbprint) -and $t1 -and $t2 -and $t3 -and $t4 -and $w -eq 'whatif' -and $fake2.Calls.Count -eq 0
    }
    Check 'INST-CLIENT Untrust (fake store): removes only the certificate with that fingerprint; absent and -WhatIf change nothing' {
        $other = New-TestCert 'CN=Some Other Root' $true $true
        $fake = New-FakeRootStore @($other, $caCert); $sf = { $fake }.GetNewClosure()
        $w = Remove-HccTrustedRoot -Sha256 $caSha -StoreFactory $sf -WhatIf
        $noRemoveYet = @($fake.Calls | Where-Object { $_ -like 'remove:*' }).Count -eq 0
        $r1 = Remove-HccTrustedRoot -Sha256 $caSha -StoreFactory $sf
        $r2 = Remove-HccTrustedRoot -Sha256 $caSha -StoreFactory $sf
        $removes = @($fake.Calls | Where-Object { $_ -like 'remove:*' })
        $w -eq 'whatif' -and $noRemoveYet -and @($fake.Calls | Where-Object { $_ -eq 'open:ReadWrite' }).Count -eq 1 -and $r1 -eq 'removed' -and $r2 -eq 'absent' -and $removes.Count -eq 1 -and $removes[0] -eq ('remove:' + $caCert.Thumbprint) -and $fake.Certs.Count -eq 1 -and (Throws { Remove-HccTrustedRoot -Sha256 'zz' -StoreFactory $sf })
    }

    # --- Name Constraints content (item 1) ---------------------------------------------------------------------------------------------
    function New-NcCert([byte[]]$Der, [bool]$Critical, [bool]$BcCritical = $true, [bool]$KeyCertSign = $true) {
        $rsa = [Security.Cryptography.RSA]::Create(2048)
        $req = New-Object Security.Cryptography.X509Certificates.CertificateRequest('CN=HCC Hotel CA', $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $req.CertificateExtensions.Add((New-Object Security.Cryptography.X509Certificates.X509BasicConstraintsExtension($true, $true, 0, $BcCritical)))
        $flags = [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature
        if ($KeyCertSign) { $flags = $flags -bor [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign }
        $req.CertificateExtensions.Add((New-Object Security.Cryptography.X509Certificates.X509KeyUsageExtension($flags, $true)))
        if ($Der) { $req.CertificateExtensions.Add((New-Object Security.Cryptography.X509Certificates.X509Extension('2.5.29.30', $Der, $Critical))) }
        return $req.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddDays(30))
    }
    function Get-NcInfo([byte[]]$Der, [bool]$Critical = $true) { $f = Save-Cer (New-NcCert $Der $Critical) ('nc-' + [guid]::NewGuid().ToString('N') + '.cer'); return Get-HccCaFileInfo $f }
    $plan = Get-HccCertificatePlan -ServerName 'HOTEL-PC' -DnsName @() -IpAddress @('192.168.1.10')
    $realExt = New-HccNameConstraintsExtension -DnsNames $plan.DnsNames -IpNetworks (@($plan.IpAddresses) + @('192.168.1.0/24'))   # exactly as New-HccCertificate.ps1 builds the CA
    $realCa = Get-NcInfo $realExt.RawData $realExt.Critical
    $realFile = Save-Cer $realCa.Cert 'real-style-ca.cer'
    Check 'INST-CLIENT name constraints: a CA built exactly as New-HccCertificate.ps1 builds it is accepted, its permitted names are listed, the host is checked against them' {
        $okHost = Test-HccClientInputs -CerPath $realFile -ServerUrl 'https://hotel-pc:5443/'
        $okSub = Test-HccClientInputs -CerPath $realFile -ServerUrl 'https://pms.hotel-pc:5443/'
        $okIp = Test-HccClientInputs -CerPath $realFile -ServerUrl 'https://192.168.1.77:5443/'
        $badName = Test-HccClientInputs -CerPath $realFile -ServerUrl 'https://bank.example:5443/'
        $badIp = Test-HccClientInputs -CerPath $realFile -ServerUrl 'https://10.9.9.9:5443/'
        $realCa.Ok -and $realExt.Critical -and $realCa.Permitted -match 'hotel-pc' -and $realCa.Permitted -match 'localhost' -and $realCa.Permitted -match '192\.168\.1\.10' -and $realCa.Permitted -match '192\.168\.1\.0/24' -and
        $okHost.Ok -and $okHost.Permitted -match 'hotel-pc' -and $okSub.Ok -and $okIp.Ok -and
        -not $badName.Ok -and $badName.Error -match 'not one of the names' -and -not $badIp.Ok -and $badIp.Error -match 'not one of the addresses'
    }
    Check 'INST-CLIENT name constraints: refused when not critical, empty, excluded-only, a bare top-level domain, 0.0.0.0/0, a public address, a leading-dot or wildcard name' {
        $good = Get-HccNameConstraintsDer -DnsNames @('hotel-pc') -IpNetworks @('192.168.1.10')
        $nonCrit = Get-NcInfo $good $false
        $empty = Get-NcInfo ([byte[]](ConvertTo-HccDer 0x30 ([byte[]]@()))) $true
        $exclOnly = Get-NcInfo ([byte[]](ConvertTo-HccDer 0x30 (ConvertTo-HccDer 0xA1 (ConvertTo-HccDer 0x30 (ConvertTo-HccDer 0x82 ([Text.Encoding]::ASCII.GetBytes('evil'))))))) $true
        $tld = Get-NcInfo (Get-HccNameConstraintsDer -DnsNames @('com', 'net', 'in') -IpNetworks @('0.0.0.0/0')) $true
        $in = Get-NcInfo (Get-HccNameConstraintsDer -DnsNames @('in') -IpNetworks @('192.168.1.10')) $true
        $coin = Get-NcInfo (Get-HccNameConstraintsDer -DnsNames @('co.in') -IpNetworks @('192.168.1.10')) $true
        $allIp = Get-NcInfo (Get-HccNameConstraintsDer -DnsNames @('hotel-pc') -IpNetworks @('0.0.0.0/0')) $true
        $pubIp = Get-NcInfo (Get-HccNameConstraintsDer -DnsNames @('hotel-pc') -IpNetworks @('8.8.8.8')) $true
        $wide = Get-NcInfo (Get-HccNameConstraintsDer -DnsNames @('hotel-pc') -IpNetworks @('172.0.0.0/8')) $true
        $dot = Get-NcInfo (Get-HccNameConstraintsDer -DnsNames @('.hotel') -IpNetworks @('192.168.1.10')) $true
        $star = Get-NcInfo (Get-HccNameConstraintsDer -DnsNames @('*.hotel') -IpNetworks @('192.168.1.10')) $true
        $okAgain = Get-NcInfo $good $true
        -not $nonCrit.Ok -and $nonCrit.Error -match 'critical' -and -not $empty.Ok -and $empty.Error -match 'permits no host names' -and -not $exclOnly.Ok -and $exclOnly.Error -match 'permits no host names' -and
        -not $tld.Ok -and $tld.Error -match 'top-level' -and -not $in.Ok -and -not $coin.Ok -and -not $allIp.Ok -and $allIp.Error -match 'private' -and -not $pubIp.Ok -and -not $wide.Ok -and
        -not $dot.Ok -and -not $star.Ok -and $okAgain.Ok
    }
    Check 'INST-CLIENT name constraints: the kit refuses a ServerName the CA does not cover; kit prints the fingerprint prominently and lists the permitted names in READ-ME' {
        $o = Join-Path $ct 'kit-uncovered'
        $r = RunKit @('-ServerName', 'other-pc', '-HotelName', 'H', '-CaCer', $caFile, '-OutDir', $o, '-ClientSetupExe', $stub)
        $k2 = Join-Path $ct 'kit-ok'
        $g = RunKit @('-ServerName', 'hotel-pc', '-HotelName', 'H', '-CaCer', $caFile, '-OutDir', $k2, '-ClientSetupExe', $stub)
        $readme = [IO.File]::ReadAllText((Join-Path $k2 'READ-ME.txt'), [Text.Encoding]::UTF8)
        $r.Exit -ne 0 -and $r.Out -match 'cannot be used with this CA' -and -not (Test-Path -LiteralPath $o) -and $g.Exit -eq 0 -and $g.Out.Contains((Format-HccFingerprint $caSha)) -and $g.Out -match 'FINGERPRINT' -and
        $readme -match 'can vouch only for: .*hotel-pc' -and $readme -match 'SERVER PC' -and $readme -notmatch 'the same as the one below'
    }

    # --- file type, private key, CA flags (item 7), hotel name (item 8), empty fingerprint (item 2) ------------------------------------------
    Check 'INST-CLIENT certificate file: a PFX, a PEM with a private key, a non-critical CA flag and a CA without keyCertSign are refused' {
        $pfx = Join-Path $ct 'key.pfx'; [IO.File]::WriteAllBytes($pfx, $caCert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx))
        $pemKey = Join-Path $ct 'withkey.pem'
        [IO.File]::WriteAllText($pemKey, ([IO.File]::ReadAllText($pemFile)) + "-----BEGIN PRIVATE KEY-----`nQUJDRA==`n-----END PRIVATE KEY-----`n")
        $good = Get-HccNameConstraintsDer -DnsNames @('hotel-pc') -IpNetworks @('192.168.1.10')
        $bcNc = Save-Cer (New-NcCert $good $true $false $true) 'bc-noncritical.cer'
        $noSign = Save-Cer (New-NcCert $good $true $true $false) 'no-certsign.cer'
        $p = Get-HccCaFileInfo $pfx; $k = Get-HccCaFileInfo $pemKey
        -not $p.Ok -and $p.Error -match 'not a single plain certificate' -and -not $k.Ok -and $k.Error -match 'private key' -and
        -not (Get-HccCaFileInfo $bcNc).Ok -and (Get-HccCaFileInfo $bcNc).Error -match 'critical' -and -not (Get-HccCaFileInfo $noSign).Ok -and (Get-HccCaFileInfo $noSign).Error -match 'sign certificates'
    }
    Check 'INST-CLIENT hotel name from the json: 1-100 characters, no control / format / line-separator characters (shown as "name not shown" otherwise)' {
        $long = 'A' * 101; $ls = "Hotel" + [char]0x2028 + "Fingerprint verified"; $ctl = "Hotel" + [char]7; $fmt = "Hotel" + [char]0x200B
        $cfgLong = Write-TestCfgName 'long.json' $long
        $cfgLs = Write-TestCfgName 'ls.json' $ls
        $cfgOk = Write-TestCfgName 'name-ok.json' ("Hotel Parth " + [char]0x0939)
        (Test-HccHotelName 'Hotel Parth') -and (Test-HccHotelName ('A' * 100)) -and -not (Test-HccHotelName $long) -and -not (Test-HccHotelName $ls) -and -not (Test-HccHotelName $ctl) -and -not (Test-HccHotelName $fmt) -and -not (Test-HccHotelName '') -and -not (Test-HccHotelName '   ') -and
        (Test-HccClientInputs -CerPath $caFile -ConfigPath $cfgLong).HotelName -eq '(name not shown: not valid)' -and (Test-HccClientInputs -CerPath $caFile -ConfigPath $cfgLs).HotelName -eq '(name not shown: not valid)' -and
        (Test-HccClientInputs -CerPath $caFile -ConfigPath $cfgOk).HotelName -eq ("Hotel Parth " + [char]0x0939) -and (Test-HccClientInputs -CerPath $caFile -ConfigPath $cfgLong).Ok
    }
    Check 'INST-CLIENT an empty or null caSha256 counts as "no fingerprint" (never a match): Inspect says expectedKnown=0, matches=0' {
        $e1 = Test-HccClientInputs -CerPath $caFile -ConfigPath (Write-TestCfg 'empty-sha.json' 'https://hotel-pc:5443/' '')
        $nullCfg = Join-Path $ct 'null-sha.json'; [IO.File]::WriteAllText($nullCfg, '{ "serverUrl": "https://hotel-pc:5443/", "hotelName": "H", "caSha256": null }')
        $e2 = Test-HccClientInputs -CerPath $caFile -ConfigPath $nullCfg
        $script = Join-Path $root 'Hcc-ClientTrust.ps1'; $outE = Join-Path $ct 'inspect-empty.txt'
        $ErrorActionPreference = 'Continue'
        & $psExe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $script -Action Inspect -CerPath $caFile -ConfigPath (Join-Path $ct 'empty-sha.json') -OutFile $outE | Out-Null
        $txt = [IO.File]::ReadAllText($outE, [Text.Encoding]::Unicode)
        $e1.Ok -and -not $e1.ExpectedKnown -and -not $e1.FingerprintMatches -and $e2.Ok -and -not $e2.ExpectedKnown -and $txt -match '(?m)^expectedKnown=0' -and $txt -match '(?m)^matches=0'
    }

    # --- the .iss ---------------------------------------------------------------------------------------------------------------------
    $iss = Join-Path $root 'installer\HccClient.iss'
    $issText = Get-Content -LiteralPath $iss -Raw
    Check 'INST-CLIENT HccClient.iss layout: admin, own AppId/folder, no server files, root store only through the trust script, edge --app launcher' {
        $issText -match 'PrivilegesRequired=admin' -and $issText -match 'DefaultDirName=\{autopf\}\\HCC Client' -and $issText -notmatch 'B6A3E3A2-6C54-4F0E-9B1A-2D8F4C7A0C11' -and
        $issText -match '\{commondesktop\}\\HCC' -and $issText -match '\{commonprograms\}\\HCC' -and $issText -match '#ifndef AppVersion' -and $issText -match 'OutputDir=\{#OutputDir\}' -and
        $issText -notmatch 'Hcc\.Api' -and $issText -notmatch 'Server\\' -and
        (Get-Content -LiteralPath (Join-Path $root 'installer\Launch-HCC.vbs') -Raw) -match '--app=' -and (Get-Content -LiteralPath (Join-Path $root 'installer\Launch-HCC.vbs') -Raw) -match 'msedge\.exe' -and
        (Get-Content -LiteralPath (Join-Path $root 'installer\Launch-HCC.vbs') -Raw) -match '--user-data-dir'
    }
    Check 'INST-CLIENT HccClient.iss gating text: tick box and silent rule decided in NextButtonClick / PrepareToInstall, trusted list, wording points at the server PC' {
        $issText -match 'CurPageID = ConfirmPage\.ID\) and not WizardSilent' -and $issText -match 'InspectOk and ConfirmCheck\.Checked' -and $issText -match 'if WizardSilent then' -and $issText -match 'not ExpectedKnown' -and
        $issText -match 'ca-trusted\.txt' -and $issText -match '"added"|= ''added''' -and $issText -match 'SERVER PC' -and $issText -notmatch 'READ-ME\.txt of the kit'
    }

    # --- second audit round: allow-list for DNS names, duplicate extension, untrust guard, files swapped after the check ----------------
    Check 'INST-CLIENT permitted DNS names are an allow-list: single-label names and private-use suffixes only; a real public name is refused and named' {
        $okNames = foreach ($n in 'hotelpc', 'localhost', 'hotel.lan', 'pms.home.arpa', 'srv.local', 'a.internal', 'x.localdomain', 'x.intranet', 'x.corp', 'x.home') { (Get-NcInfo (Get-HccNameConstraintsDer -DnsNames @($n) -IpNetworks @('192.168.1.10')) $true).Ok }
        $bad = foreach ($n in 'sbi.co.in', 'github.io', 'hotel.com', 'home.arpa', 'www.example.org', 'a.b.c.d') { Get-NcInfo (Get-HccNameConstraintsDer -DnsNames @($n) -IpNetworks @('192.168.1.10')) $true }
        $sbi = Get-NcInfo (Get-HccNameConstraintsDer -DnsNames @('sbi.co.in') -IpNetworks @('192.168.1.10')) $true
        -not ($okNames -contains $false) -and -not (@($bad | Where-Object { $_.Ok }).Count) -and $sbi.Error -match 'sbi\.co\.in' -and $sbi.Error -match 'real internet name' -and $realCa.Ok
    }
    Check 'INST-CLIENT the kit refuses early, naming the offending permitted name, when the CA permits a real public name' {
        $f = Save-Cer (New-NcCert (Get-HccNameConstraintsDer -DnsNames @('hotel-pc', 'sbi.co.in') -IpNetworks @('192.168.1.10')) $true) 'public-name-ca.cer'
        $o = Join-Path $ct 'kit-public'
        $r = RunKit @('-ServerName', 'hotel-pc', '-HotelName', 'H', '-CaCer', $f, '-OutDir', $o, '-ClientSetupExe', $stub)
        $r.Exit -ne 0 -and $r.Out -match 'sbi\.co\.in' -and -not (Test-Path -LiteralPath $o)
    }
    Check 'INST-CLIENT a certificate with two Name Constraints extensions is refused' {
        $e1 = New-Object Security.Cryptography.X509Certificates.X509Extension('2.5.29.30', (Get-HccNameConstraintsDer -DnsNames @('hotel-pc') -IpNetworks @('192.168.1.10')), $true)
        $e2 = New-Object Security.Cryptography.X509Certificates.X509Extension('2.5.29.30', (Get-HccNameConstraintsDer -DnsNames @('hotel-pc') -IpNetworks @('192.168.1.10')), $true)
        $two = Get-HccNameConstraintInfo ([pscustomobject]@{ Extensions = @($e1, $e2) })
        $one = Get-HccNameConstraintInfo ([pscustomobject]@{ Extensions = @($e1) })
        -not $two.Ok -and $two.Error -match 'more than one' -and $one.Ok
    }
    Check 'INST-CLIENT Untrust (fake store) refuses a public-style root that a list names: not removed, store never opened read-write' {
        $pub = New-TestCert 'CN=Some Public Root CA' $true $false
        $pubSha = Get-HccCertificateSha256 $pub
        $fake = New-FakeRootStore @($pub); $sf = { $fake }.GetNewClosure()
        $r = Remove-HccTrustedRoot -Sha256 $pubSha -StoreFactory $sf
        $leaf = New-TestCert 'CN=Leaf Server' $false $false
        $fake2 = New-FakeRootStore @($leaf); $sf2 = { $fake2 }.GetNewClosure()
        $r2 = Remove-HccTrustedRoot -Sha256 (Get-HccCertificateSha256 $leaf) -StoreFactory $sf2
        $r -match '^refused' -and $fake.Certs.Count -eq 1 -and @($fake.Calls | Where-Object { $_ -like 'remove:*' -or $_ -eq 'open:ReadWrite' }).Count -eq 0 -and $r2 -match '^refused' -and $fake2.Certs.Count -eq 1
    }
    Check 'INST-CLIENT iss: PrepareToInstall stops when the files changed after the confirmation; uninstall keeps the trusted list while a certificate stays' {
        $issText -match 'ShownSha := ApprovedSha' -and $issText -match 'ApprovedSha <> ShownSha' -and $issText -match 'ApprovedUrl <> ShownUrl' -and $issText -match 'The files changed after you checked them' -and
        $issText -notmatch 'Name: "\{app\}\\ca-trusted\.txt"' -and $issText -match "DeleteFile\(ExpandConstant\('\{app\}\\ca-trusted\.txt'\)\)"
    }
    $iscc = Get-Command ISCC -ErrorAction SilentlyContinue
    if (-not $iscc) { foreach ($c in 'E:\Tools\Inno Setup 6\ISCC.exe', "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") { if (Test-Path $c) { $iscc = Get-Item $c; break } } }
    if ($iscc) {
        $isccPath = if ($iscc.PSObject.Properties['Source']) { $iscc.Source } else { $iscc.FullName }
        $isccOut = Join-Path $ct 'iscc'
        Check 'INST-CLIENT ISCC compiles HccClient.iss and produces HCC-Client-Setup-<ver>.exe' {
            $ErrorActionPreference = 'Continue'
            & $isccPath /Q /DAppVersion=0.0.1.0 "/DOutputDir=$isccOut" $iss 2>&1 | Out-Null
            $LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath (Join-Path $isccOut 'HCC-Client-Setup-0.0.1.0.exe'))
        }
        Check 'INST-CLIENT ISCC refuses to compile without /DAppVersion' {
            $ErrorActionPreference = 'Continue'
            & $isccPath /Q "/DOutputDir=$isccOut" $iss 2>&1 | Out-Null
            $LASTEXITCODE -ne 0
        }
    }
    else { Write-Host 'SKIP INST-CLIENT ISCC compile checks (Inno Setup 6 not found)' }
    Check 'INST-CLIENT nothing was added to the Root store by these tests' {
        (@(Get-ChildItem Cert:\LocalMachine\Root | ForEach-Object { $_.Thumbprint }) -join ',') -eq ($storeBefore -join ',')
    }
}
finally { Remove-Item -LiteralPath $ct -Recurse -Force -ErrorAction SilentlyContinue }
# /INST-CLIENT
# INST-SERVER ---------------------------------------------------------------------------------------------------------
# Server installer lane (wave INST): installer\Hcc.iss layout + ISCC compile with stub inputs, and Install-HccServer.ps1 with every
# adapter mocked. Nothing here installs, starts or changes anything on this PC: the orchestrator is dot-sourced, its adapters
# (Invoke-HccOps, Invoke-HccNative, services, HTTP, certificate store, ACLs) are replaced by recorders, and all files live in a temp folder.
$instRoot = Join-Path ([IO.Path]::GetTempPath()) ('hcc-inst-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $instRoot -Force | Out-Null
$instMarker = 'MARKER-Pw-9f3a!zz'            # fictional password: must never reach a log, state or summary file
$instCaMarker = 'CA-MARKER-Pw-77qq#xx'
$instHotelOdd = 'O''Brien & "Sons"; %PATH% ' + [string][char]0x0939 + [string][char]0x0949 + [string][char]0x091F + '\x'
$iss = Get-Content -LiteralPath (Join-Path $root 'installer\Hcc.iss') -Raw
$orch = Get-Content -LiteralPath (Join-Path $root 'Install-HccServer.ps1') -Raw

# --- Hcc.iss layout -------------------------------------------------------------------------------------------------------
Check 'INST-SERVER iss: setup logging, version info, locked install folder, admin, x64, downgrade refusal' {
    $iss -match '(?m)^SetupLogging=yes' -and $iss -match '(?m)^VersionInfoVersion=\{#AppVersion\}' -and $iss -match '(?m)^DisableDirPage=yes' -and $iss -match '(?m)^DefaultDirName=\{autopf\}\\HCC' -and
    $iss -match '(?m)^PrivilegesRequired=admin' -and $iss -match 'ArchitecturesInstallIn64BitMode=x64compatible' -and $iss -match 'function IsNewerVersion' -and $iss -match 'Setup will not put an older release over a newer one'
}
Check 'INST-SERVER iss: the /D defines of INST-CONTRACT 4.4 are required or optional exactly as documented' {
    $iss -match '(?m)^#ifndef AppVersion' -and $iss -match '(?m)^#ifndef PublishDir' -and $iss -match '(?m)^#ifndef OpsDir' -and $iss -match '(?m)^#ifndef TemplatesDir' -and $iss -match '(?m)^#ifndef ClientSetupExe' -and
    $iss -match '(?m)^#ifdef SqlMediaDir' -and $iss -match '(?m)^#ifndef OutputDir'
}
Check 'INST-SERVER iss: ships publish output, scripts to Operations, import templates, client setup exe to ClientKit, SQL media only when chosen and deleted after use' {
    $iss -match 'Source: "\{#PublishDir\}\\\*"; DestDir: "\{app\}\\Server"' -and $iss -match 'Source: "\{#OpsDir\}\\\*\.ps1"; DestDir: "\{app\}\\Operations"' -and $iss -match 'Source: "\{#TemplatesDir\}\\\*"; DestDir: "\{app\}\\ImportTemplates"' -and
    $iss -match 'Source: "\{#ClientSetupExe\}"; DestDir: "\{app\}\\ClientKit"' -and $iss -match 'Check: WantsBundledSql; Flags: .*deleteafterinstall' -and $iss -match 'msodbcsql17\.msi"; DestDir: "\{app\}\\SqlSetupMedia"; Check: WantsSqlMedia' -and $iss -match 'MsSqlCmdLnUtils\.msi"; DestDir: "\{app\}\\SqlSetupMedia"; Check: WantsSqlMedia' -and $iss -match 'Name: "\{app\}\\Server\\web"'
}
Check 'INST-SERVER iss: settings files are created once, never replaced and never removed (onlyifdoesntexist + uninsneveruninstall)' {
    ([regex]::Matches($iss, 'DestName: "appsettings\.Production\.json"; Flags: onlyifdoesntexist uninsneveruninstall')).Count -eq 1 -and ([regex]::Matches($iss, 'DestName: "hcc-ops\.json"; Flags: onlyifdoesntexist uninsneveruninstall')).Count -eq 1 -and
    $iss -notmatch '(?m)^Type: .*Name: "[^"*]*\.json"'
}
Check 'INST-SERVER iss: upgrade stops the service first and asks nothing; the post-install step is mandatory and a failure ends with an exit code and the log named' {
    $iss -match 'function PrepareToInstall' -and $iss -match 'stop \{#ServiceName\} /y' -and $iss -match 'UpgradeRun then Exit' -and $iss -match 'SetupFailedExitCode = 1603' -and $iss -match 'RestartNeededExitCode = 3010' -and
    $iss -match 'ExitProcess\(SetupFailedExitCode\)' -and $iss -match 'The log says which step failed' -and $iss -match '-Upgrade' -and $iss -match '-Resume'
}
Check 'INST-SERVER iss: uninstall removes service/rules/tasks via the orchestrator and says on screen that SQL, databases, backups, keys and settings stay' {
    $iss -match '\[UninstallRun\][^\[]*Install-HccServer\.ps1""? -Uninstall' -and $iss -match 'It does NOT remove, and never will' -and $iss -match 'the HCC databases \(Hcc and HccDocuments\)' -and $iss -match 'the backups' -and $iss -match 'the keys' -and
    $iss -match 'your settings files' -and $iss -notmatch 'DROP DATABASE' -and $iss -notmatch 'Type: filesandordirs; Name: "\{commonappdata\}'
}
Check 'INST-SERVER iss: wizard pages and the TRIAL wording exist (mode, hotel, backup, Owner twice, SQL, CA password twice, USB, summary)' {
    $iss -match 'Kind of installation' -and $iss -match 'Your hotel and this PC' -and $iss -match 'Backup folder' -and $iss -match 'The first Owner' -and $iss -match 'Type the password again' -and $iss -match 'Database \(SQL Server\)' -and
    $iss -match 'Hotel certificate password \(Production\)' -and $iss -match 'USB stick for the keys' -and $iss -match 'function UpdateReadyMemo' -and $iss -match 'TRIAL install - not for real guest data' -and
    $iss -match 'HCC - TRIAL \(not for real guest data\)' -and $iss -match 'GetComputerNameString' -and $iss -match 'DetectSqlExpress' -and $iss -match '12 to 128 characters'
}
Check 'INST-SERVER iss: passwords never go on a command line, into the registry or into a log' {
    $execLines = @($iss -split "`n" | Where-Object { $_ -match 'Exec\(' -or $_ -match 'Params := ' -or $_ -match 'Parameters:' })
    @($execLines | Where-Object { $_ -match 'Password|ownerPassword|Values\[' }).Count -eq 0 -and $iss -notmatch 'RegWrite' -and $iss -notmatch 'Log\([^)]*(Password|Values\[1\])' -and
    $iss -match 'function CreatePrivateFolder' -and $iss -match 'SetOwner\(\$admins\); \[void\]\[IO\.Directory\]::CreateDirectory\(\$Path, \$sec\)' -and $iss -match 'SaveStringsToUTF8File\(AnswerFileName' -and $iss.IndexOf('PrepareSetupFolder') -lt $iss.IndexOf('SaveStringsToUTF8File(AnswerFileName') -and
    $iss -match 'procedure RemoveAnswerFile' -and $iss -match 'procedure DeinitializeSetup\(\);\s*begin\s*RemoveAnswerFile'
}
Check 'INST-SERVER iss: the answer JSON escapes quotes, backslashes and control characters (hotel name with quote, ampersand, percent, Unicode)' {
    $iss.Contains('function JsonEscape') -and $iss.Contains('if C = ''\'' then R := R + ''\\''') -and $iss.Contains('else if C = ''"'' then R := R + ''\"''') -and $iss.Contains('Ord(C) < 32')
}

$isccPath = $null
foreach ($c in @((Get-Command ISCC.exe -ErrorAction SilentlyContinue | ForEach-Object Source), 'E:\Tools\Inno Setup 6\ISCC.exe', "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe")) { if ($c -and (Test-Path -LiteralPath $c)) { $isccPath = $c; break } }
if (-not $isccPath) { Write-Host 'SKIP INST-SERVER iss compile (ISCC.exe not found; install Inno Setup 6)' -ForegroundColor Yellow }
else {
    $stub = Join-Path $instRoot 'stub'
    foreach ($d in @('pub\web', 'tpl', 'sql', 'out')) { New-Item -ItemType Directory -Path (Join-Path $stub $d) -Force | Out-Null }
    Set-Content -LiteralPath (Join-Path $stub 'pub\Hcc.Api.exe') -Value 'stub'; Set-Content -LiteralPath (Join-Path $stub 'pub\web\index.html') -Value '<html><body></body></html>'
    Set-Content -LiteralPath (Join-Path $stub 'tpl\a.csv') -Value 'x'; Set-Content -LiteralPath (Join-Path $stub 'sql\SQLEXPR_x64_ENU.exe') -Value 'stub'; Set-Content -LiteralPath (Join-Path $stub 'sql\msodbcsql17.msi') -Value 'stub'; Set-Content -LiteralPath (Join-Path $stub 'sql\MsSqlCmdLnUtils.msi') -Value 'stub'; Set-Content -LiteralPath (Join-Path $stub 'HCC-Client-Setup-9.9.9.exe') -Value 'stub'
    function Invoke-InstIscc([string[]]$Defines, [string]$OutName) {
        $a = (@('/Q') + $Defines + @("`"/DOutputDir=$stub\$OutName`"", "`"$(Join-Path $root 'installer\Hcc.iss')`"")) -join ' '
        $p = Start-Process -FilePath $isccPath -ArgumentList $a -NoNewWindow -Wait -PassThru -RedirectStandardOutput (Join-Path $stub "$OutName.txt") -RedirectStandardError (Join-Path $stub "$OutName.err")
        return [pscustomobject]@{ Exit = $p.ExitCode; Out = ((Get-Content -LiteralPath (Join-Path $stub "$OutName.txt") -Raw) + (Get-Content -LiteralPath (Join-Path $stub "$OutName.err") -Raw)) }
    }
    $defs = @('/DAppVersion=9.9.9', "`"/DPublishDir=$stub\pub`"", "`"/DOpsDir=$root`"", "`"/DTemplatesDir=$stub\tpl`"", "`"/DClientSetupExe=$stub\HCC-Client-Setup-9.9.9.exe`"")
    Check 'INST-SERVER ISCC compiles Hcc.iss with stub inputs (with and without SQL media) and writes HCC-Server-Setup-<ver>-x64.exe' {
        $stubOps = Join-Path $stub 'ops'; New-Item -ItemType Directory -Path $stubOps -Force | Out-Null
        foreach ($f in @('*.ps1', '*.json', '*.md')) { Copy-Item -Path (Join-Path $root $f) -Destination $stubOps -Force }
        Set-Content -LiteralPath (Join-Path $stubOps 'sql-media.manifest.json') -Value '{}'   # the sql lane ships the real one
        $defsSql = @($defs | Where-Object { $_ -notlike '*OpsDir*' }) + "`"/DOpsDir=$stubOps`""
        $with = Invoke-InstIscc ($defsSql + "`"/DSqlMediaDir=$stub\sql`"") 'out1'
        $without = Invoke-InstIscc $defs 'out2'
        $with.Exit -eq 0 -and $without.Exit -eq 0 -and (Test-Path -LiteralPath (Join-Path $stub 'out1\HCC-Server-Setup-9.9.9-x64.exe')) -and (Test-Path -LiteralPath (Join-Path $stub 'out2\HCC-Server-Setup-9.9.9-x64.exe'))
    }
    Check 'INST-SERVER ISCC refuses a compile without AppVersion or ClientSetupExe (no silent default)' {
        $a = Invoke-InstIscc @($defs | Where-Object { $_ -notlike '*AppVersion*' }) 'out3'
        $c = Invoke-InstIscc @($defs | Where-Object { $_ -notlike '*ClientSetupExe*' }) 'out4'
        $a.Exit -ne 0 -and $a.Out -match 'AppVersion is not defined' -and $c.Exit -ne 0 -and $c.Out -match 'ClientSetupExe is not defined'
    }
}

# --- orchestrator: parse, interface, plan ------------------------------------------------------------------------------------
Check 'INST-SERVER Install-HccServer.ps1 parses and follows the PowerShell rules (5.1 and 7: no &&, no ternary, no ??; strict mode; ShouldProcess)' {
    $errs = $null; $null = [Management.Automation.Language.Parser]::ParseInput($orch, [ref]$null, [ref]$errs)
    $errs.Count -eq 0 -and $orch -match '\[CmdletBinding\(SupportsShouldProcess\)\]' -and $orch -match 'Set-StrictMode -Version Latest' -and $orch -notmatch '&&' -and $orch -notmatch '\?\?' -and $orch -notmatch '\s\?\s.*\s:\s' -and
    $orch -notmatch '[^\x00-\x7f]' -and $orch -match 'param\(\s*\[string\]\$AnswerFile,\s*\[switch\]\$Resume,\s*\[switch\]\$PlanOnly'
}
. (Join-Path $root 'Install-HccServer.ps1')   # dot-sourced: functions only
$instRealOps = ${function:Invoke-HccOps}
# A page shaped like the production build: CSP meta first, the real <title> and <body> before the inline script, and <title> / <body>
# strings INSIDE the script (the audit's blank-page case).
function New-InstPageHtml([string]$ScriptText = 'var t="<title>x</title><body>"; var a = 1; document.title = "<title>"+a;') {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $h = 'sha256-' + [Convert]::ToBase64String($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($ScriptText))) } finally { $sha.Dispose() }
    return '<!DOCTYPE html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src ''none''; script-src ''' + $h + '''"><title>HCC</title></head><body><div id="root"></div><script>' + $ScriptText + '</script></body></html>'
}
function Initialize-InstFixture([string]$Name) {
    $d = Join-Path $instRoot $Name
    $app = Join-Path $d 'app'; $data = Join-Path $d 'data'
    foreach ($x in @("$app\Server\web", "$app\Operations", "$data\Setup")) { New-Item -ItemType Directory -Path $x -Force | Out-Null }
    Copy-Item -LiteralPath (Join-Path $root 'appsettings.Production.sample.json') -Destination (Join-Path $app 'Server\appsettings.Production.sample.json')
    Copy-Item -LiteralPath (Join-Path $root 'appsettings.Production.sample.json') -Destination (Join-Path $app 'Server\appsettings.Production.json')
    Copy-Item -LiteralPath (Join-Path $root 'hcc-ops.sample.json') -Destination (Join-Path $app 'Operations\hcc-ops.sample.json')
    [IO.File]::WriteAllText((Join-Path $app 'Server\web\index.html'), (New-InstPageHtml), (New-Object Text.UTF8Encoding($false)))
    Set-Content -LiteralPath (Join-Path $app 'Server\Hcc.Api.exe') -Value 'stub'
    return [pscustomobject]@{ Dir = $d; App = $app; Data = $data; Ops = (Join-Path $app 'Operations'); Answers = (Join-Path $data 'Setup\answers.json') }
}
function New-InstAnswerFile($Fx, [hashtable]$Over = @{}) {
    $a = [ordered]@{ mode = 'Trial'; hotelName = 'Hotel Test'; serverName = 'hotelpc'; serverIps = @('192.168.1.10'); httpsPort = 5443; backupDirectory = 'Z:\HccBackups'; ownerUserName = 'owner'
        ownerPassword = $instMarker; sql = 'UseExisting'; caPfxPassword = ''; offlineCopyDirectory = '' }
    foreach ($k in $Over.Keys) { $a[$k] = $Over[$k] }
    [IO.File]::WriteAllText($Fx.Answers, ($a | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    return $Fx.Answers
}
function Get-InstTreeText($Fx) { return ((Get-ChildItem -LiteralPath $Fx.Dir -Recurse -File | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n") }

Check 'INST-SERVER every INST-CONTRACT 2 step is present, in the contract order, each calling its script' {
    $fx = Initialize-InstFixture 'order'
    Initialize-HccInstallContext -AppDirectory $fx.App -DataDirectory $fx.Data -OperationsDirectory $fx.Ops
    $steps = Get-HccInstallSteps -A @{ mode = 'Trial'; sql = 'UseExisting'; backupDirectory = 'D:\b'; offlineCopyDirectory = ''; httpsPort = 5443; serverName = 'x'; hotelName = 'h'; ownerUserName = 'o' }
    $ids = ($steps | ForEach-Object { $_.Id }) -join ','
    $plan = ($steps | ForEach-Object { $_.Plan }) -join "`n"
    $order = @('Bootstrap-HccSql.ps1', 'Install-HccService.ps1', 'Initialize-HccDatabase.ps1', 'New-HccCertificate.ps1', 'appsettings.Production.json', 'New-HccGuestIdKey.ps1', 'New-HccBackupKey.ps1', 'Hcc.Api.exe migrate', 'start HccServer', 'Initialize-HccProperty.ps1', 'Register-HccBackupTasks.ps1', 'Set-HccEgress.ps1 -State Install', 'New-HccClientKit.ps1', 'answers')
    $pos = @($order | ForEach-Object { $plan.IndexOf($_) })
    $sorted = $true; for ($i = 1; $i -lt $pos.Count - 1; $i++) { if ($pos[$i] -lt 0 -or $pos[$i] -le $pos[$i - 1]) { $sorted = $false } }
    $ids -eq 'sql,service-register,database,certificate,settings,keys,migrate,service-start,property,backup-tasks,egress,client-kit,health' -and $sorted -and
    $orch.IndexOf('function Step-Migrate') -gt 0 -and $plan -match 'schema current' -and $plan.IndexOf('Install-HccService.ps1') -lt $plan.IndexOf('Initialize-HccDatabase.ps1') -and $plan -match 'NOT started'
}
$instRealGetIp = ${function:Get-HccLocalIpv4}
$instRealPath = ${function:Update-HccProcessPath}
$instRealRepair = ${function:Repair-HccPrivateDirectory}
$instMyIp = @(& $instRealGetIp | Where-Object { (ConvertTo-HccIpv4 $_) -and (Test-HccPrivateIpv4 $_) }) | Select-Object -First 1
if (-not $instMyIp) { Write-Host 'SKIP INST-SERVER -PlanOnly process check (this PC has no private IPv4 address)' -ForegroundColor Yellow }
else { Check 'INST-SERVER -PlanOnly and -WhatIf print the 13 steps and change nothing (no folder, no answer-file deletion, no state)' {
    $fx = Initialize-InstFixture 'plan'
    $f = New-InstAnswerFile $fx @{ serverIps = @($instMyIp) }
    $extraApp = Join-Path $fx.Dir 'noapp'; $extraData = Join-Path $fx.Dir 'nodata'
    $out = & $psExe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root 'Install-HccServer.ps1') -AnswerFile $f -PlanOnly -AppDirectory $extraApp -DataDirectory $extraData 2>&1 | Out-String
    $code = $LASTEXITCODE
    $w = DryRun 'Install-HccServer.ps1' @('-AnswerFile', $f, '-AppDirectory', $extraApp, '-DataDirectory', $extraData)
    $up = & $psExe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root 'Install-HccServer.ps1') -Upgrade -PlanOnly -AppDirectory $extraApp -DataDirectory $extraData 2>&1 | Out-String
    $un = & $psExe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root 'Install-HccServer.ps1') -Uninstall -PlanOnly -AppDirectory $extraApp -DataDirectory $extraData 2>&1 | Out-String
    $code -eq 0 -and $out -match ' 1\. Preparing SQL Server' -and $out -match '13\. Checking that HCC answers' -and $out -match 'PLAN ONLY: nothing was changed' -and $out -notmatch [regex]::Escape($instMarker) -and $out -match 'password hidden' -and
    $w.Exit -eq 0 -and $w.Out -match 'PLAN ONLY' -and (Test-Path -LiteralPath $f) -and -not (Test-Path -LiteralPath $extraApp) -and -not (Test-Path -LiteralPath $extraData) -and
    $up -match 'upgrade' -and $up -match 'Hcc.Api.exe migrate' -and $up -match 'PLAN ONLY' -and $un -match 'KEPT: SQL Server, the databases' -and $un -match 'PLAN ONLY'
} }

# --- ACL / owner checks against REAL temp folders and files (nothing mocked; the temp items are only read, and one file's own ACL is tightened) ---
Check 'INST-SERVER real checks: an ordinary temp folder / file is reported as not private (inheritance on, other accounts), the owner SID is read, and the owner rule follows the real owner' {
    $d = Join-Path $instRoot 'real-folder'; New-Item -ItemType Directory -Path $d -Force | Out-Null
    $f = Join-Path $d 'answers.json'; Set-Content -LiteralPath $f -Value 'x'
    $owner = Get-HccOwnerSid -Path $f
    $fileP = @(Get-HccFileProblems -Path $f); $dirP = @(Get-HccFolderProblems -Path $d)
    $ownerIsOurs = @('S-1-5-18', 'S-1-5-32-544') -contains $owner
    $owner -match '^S-1-5-' -and $dirP.Count -gt 0 -and ($dirP -join ';') -match 'inherits|has access' -and $fileP.Count -gt 0 -and ((@($fileP | Where-Object { $_ -match 'is owned by' }).Count -eq 0) -eq $ownerIsOurs) -and
    ((@($dirP | Where-Object { $_ -match 'is owned by' }).Count -eq 0) -eq (@('S-1-5-18', 'S-1-5-32-544') -contains (Get-HccOwnerSid -Path $d)))
}
Check 'INST-SERVER real checks: a link is reported; a settings-file style ACL (protected, only the allowed accounts) passes and one extra account is reported' {
    $d = Join-Path $instRoot 'real-link'; New-Item -ItemType Directory -Path $d -Force | Out-Null
    $target = Join-Path $d 'target'; New-Item -ItemType Directory -Path $target -Force | Out-Null
    $link = Join-Path $d 'link'; $linkMade = $false
    try { New-Item -ItemType Junction -Path $link -Target $target -ErrorAction Stop | Out-Null; $linkMade = $true } catch { }
    $linkOk = if ($linkMade) { (@(Get-HccFolderProblems -Path $link) -join ';') -match 'is a link' } else { $true }
    if ($linkMade) { (Get-Item -LiteralPath $link -Force).Delete() }   # removes the junction only
    $f = Join-Path $d 'settings.json'; Set-Content -LiteralPath $f -Value '{}'
    $me = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl = Get-Acl -LiteralPath $f
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($r in @($acl.Access)) { [void]$acl.RemoveAccessRule($r) }
    foreach ($sid in @($me, (New-Object Security.Principal.SecurityIdentifier('S-1-5-18')), (New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))) { $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'Allow'))) }
    Set-Acl -LiteralPath $f -AclObject $acl
    $myName = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $clean = @(Test-HccProtectedDirectory -Path $f -AllowedAccounts @($myName))
    $extra = @(Test-HccProtectedDirectory -Path $f)
    $linkOk -and $clean.Count -eq 0 -and $extra.Count -gt 0 -and ($extra -join ';') -match 'has access'
}

# --- orchestrator with every adapter mocked ----------------------------------------------------------------------------------------
$instCalls = New-Object System.Collections.Generic.List[string]
$instParams = @{}
$instFail = ''
$instSqlExit = 0
$instServiceInstalled = $false
$instFolderPrivate = $true
function Protect-HccFolder { param([string]$Path, [string[]]$ReadAccounts = @()) if (-not (Test-Path -LiteralPath $Path)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }; $script:instCalls.Add("protect-folder:$(Split-Path -Leaf $Path)") }
function Protect-HccFile { param([string]$Path) $script:instCalls.Add('protect-file') }
function Assert-HccAnswerFolderPrivate { param([string]$Path) if (-not $script:instFolderPrivate) { throw 'folder not private (mock)' } }
function Test-HccElevated { return $true }
function Update-HccProcessPath { }
function Get-HccFolderProblems { param([string]$Path) return @($script:instFolderProblems) }
$instFolderProblems = @()
$instHasWarningsParam = $false
$instSqlWarnings = $null
function Test-HccScriptParameter { param([string]$Script, [string]$Name) return ($script:instHasWarningsParam -and $Name -eq 'WarningsFile') }
function Get-HccInstallDriveRoot { return 'Z:\' }
$instDriveType = 'Removable'; $instAclProblems = @(); $instSetupRequired = $true; $instPageOverride = $null; $instGrantFail = $false
function Get-HccLocalIpv4 { return @('192.168.1.10') }
function Get-HccDriveType { param([string]$Path) return $script:instDriveType }
function Get-HccSettingsAclProblems { return @($script:instAclProblems) }
function Restart-HccServerService { $script:instCalls.Add('restart-service') }
function Remove-HccEgressObjects { $script:instCalls.Add('remove-egress') }
function Repair-HccPrivateDirectory { param([string]$Path) $script:instCalls.Add('repair:' + (Split-Path -Leaf $Path)) }
function Install-HccTrustedCa { param([string]$CerPath) $script:instCalls.Add('trust-ca') }
function Test-HccServerCertificatePresent { param([string]$Thumbprint) return [bool]$Thumbprint }
function Remove-HccLeftoverCaKey { }
function Grant-HccCertificateKeyAccess { param([string]$Thumbprint, [string]$Account) if ($script:instGrantFail) { throw 'cannot grant (mock)' }; $script:instCalls.Add('grant-key') }
function Test-HccServiceInstalled { return $script:instServiceInstalled }
function Start-HccServerService { $script:instCalls.Add('start-service') }
function Stop-HccServerService { $script:instCalls.Add('stop-service') }
function Invoke-HccHttp { param([string]$Url, [int]$TimeoutSec = 15)
    $script:instCalls.Add("http:$Url")
    if ($Url -like '*/setup/status') { return [pscustomobject]@{ Status = 200; Body = ('{"setupRequired":' + ([string]$script:instSetupRequired).ToLowerInvariant() + '}'); Error = '' } }
    if ($Url -like '*/system/health') { return [pscustomobject]@{ Status = 200; Body = '{"status":"ok"}'; Error = '' } }
    $page = if ($null -ne $script:instPageOverride) { $script:instPageOverride } else { [IO.File]::ReadAllText((Join-Path $script:instFx.App 'Server\web\index.html'), [Text.Encoding]::UTF8) }
    return [pscustomobject]@{ Status = 200; Body = $page; Error = '' } }
function Invoke-HccNative { param([string]$FilePath, [string]$Arguments, [string]$WorkingDirectory, [hashtable]$Environment = @{})
    $script:instCalls.Add("native:$(Split-Path -Leaf $FilePath) $Arguments env=$($Environment['ASPNETCORE_ENVIRONMENT'])")
    return [pscustomobject]@{ ExitCode = $(if ($script:instFail -eq 'migrate') { 5 } else { 0 }); Output = "migrate output that wrongly echoes $script:instMarker" } }
function Set-InstSettings($Fx, [scriptblock]$Edit) {
    $p = Join-Path $Fx.App 'Server\appsettings.Production.json'
    $j = Get-Content -LiteralPath $p -Raw | ConvertFrom-Json; & $Edit $j; [IO.File]::WriteAllText($p, ($j | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
}
function Invoke-HccOps { param([string]$Script, [hashtable]$Params = @{}, [switch]$UseExitCode)
    $script:instCalls.Add($Script); $script:instParams[$Script] = $Params
    if ($script:instFail -eq $Script) { throw "simulated failure in $Script (and it echoes $script:instMarker)" }
    $fx = $script:instFx
    switch ($Script) {
        'Install-HccService.ps1' { if (-not $Params.ContainsKey('Uninstall')) { $script:instServiceInstalled = $true } }
        'Bootstrap-HccSql.ps1' { if ($Params.ContainsKey('WarningsFile') -and $script:instSqlWarnings) { Set-Content -LiteralPath $Params.WarningsFile -Value $script:instSqlWarnings -Encoding UTF8 }; return $script:instSqlExit }
        'New-HccCertificate.ps1' {
            New-Item -ItemType Directory -Path (Split-Path -Parent $Params.CaPfxPath) -Force | Out-Null
            Set-Content -LiteralPath $Params.CaPfxPath -Value 'pfx'; Set-Content -LiteralPath (Join-Path $Params.OutputDirectory 'HCC-Hotel-CA.cer') -Value 'cer'
            Set-InstSettings $fx { param($j) $j.Hcc.Hosting.CertificateThumbprint = 'AABBCCDD' }
        }
        'New-HccGuestIdKey.ps1' { Set-InstSettings $fx { param($j) $j.Hcc | Add-Member -NotePropertyName GuestIdKeys -NotePropertyValue ([pscustomobject]@{ Current = '2026-11' }) -Force } ; Set-Content -LiteralPath $Params.OfflineCopyPath -Value 'copy' }
        'New-HccBackupKey.ps1' { New-Item -ItemType Directory -Path (Join-Path $fx.Data 'Keys') -Force | Out-Null; Set-Content -LiteralPath (Join-Path $fx.Data 'Keys\hcc-backup-key.txt') -Value 'k'; Set-Content -LiteralPath $Params.OfflineCopyPath -Value 'copy' }
        'Initialize-HccProperty.ps1' {
            Write-InstallLog "child script echoes the Owner password $script:instMarker and the CA one $script:instCaMarker"
            Set-InstSettings $fx { param($j) $j.Hcc.Hosting.LoopbackHttpPort = 0 }
        }
        'New-HccClientKit.ps1' { New-Item -ItemType Directory -Path $Params.OutDir -Force | Out-Null; Set-Content -LiteralPath (Join-Path $Params.OutDir 'hcc-client.json') -Value '{}'; Set-Content -LiteralPath (Join-Path $Params.OutDir 'HCC-Hotel-CA.cer') -Value 'cer' }
        default { }
    }
    return 0
}
function Invoke-InstRun([string]$Name, [hashtable]$Over = @{}, [switch]$Resume, [switch]$Upgrade, [scriptblock]$Before, $Fx) {
    if (-not $Fx) { $Fx = Initialize-InstFixture $Name; $script:instServiceInstalled = $false }
    $script:instFx = $Fx; $script:instCalls.Clear(); $script:instParams.Clear()
    if ($Before) { & $Before $Fx }
    $f = $null
    if (-not $Upgrade) { $f = New-InstAnswerFile $Fx $Over }
    $code = Start-HccInstall -AnswerFile $f -Resume:$Resume -Upgrade:$Upgrade -AppDirectory $Fx.App -DataDirectory $Fx.Data -OperationsDirectory $Fx.Ops
    return [pscustomobject]@{ Code = [int]($code | Select-Object -Last 1); Fx = $Fx; Answers = $Fx.Answers
        State = $(if (Test-Path -LiteralPath (Join-Path $Fx.Data 'Setup\install-state.json')) { [IO.File]::ReadAllText((Join-Path $Fx.Data 'Setup\install-state.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json } else { $null })
        Log = $(@(Get-ChildItem -LiteralPath (Join-Path $Fx.Data 'Logs') -Filter 'install-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n") }
}

Check 'INST-SERVER trial run (mocked): exit 0, every step ran in contract order, answer file deleted, state lists 12 steps, TRIAL banner added, settings written' {
    $r = Invoke-InstRun 'trial'
    $names = @($instCalls | Where-Object { $_ -match '\.ps1$|^native:' })
    $want = @('Bootstrap-HccSql.ps1', 'Install-HccService.ps1', 'Initialize-HccDatabase.ps1', 'New-HccCertificate.ps1', 'New-HccGuestIdKey.ps1', 'New-HccBackupKey.ps1', 'native:Hcc.Api.exe migrate env=Production', 'Initialize-HccProperty.ps1', 'Backup-HccDatabase.ps1', 'Register-HccBackupTasks.ps1', 'Set-HccEgress.ps1', 'New-HccClientKit.ps1')
    $html = Get-Content -LiteralPath (Join-Path $r.Fx.App 'Server\web\index.html') -Raw
    $set = Get-Content -LiteralPath (Join-Path $r.Fx.App 'Server\appsettings.Production.json') -Raw | ConvertFrom-Json
    $ops = Get-Content -LiteralPath (Join-Path $r.Fx.Ops 'hcc-ops.json') -Raw | ConvertFrom-Json
    $r.Code -eq 0 -and ($names -join '|') -eq ($want -join '|') -and -not (Test-Path -LiteralPath $r.Answers) -and @($r.State.completed).Count -eq 13 -and $r.State.status -eq 'Done' -and
    $html.Contains('<!--HCC-TRIAL-BANNER-->') -and $html -match '<title>TRIAL - HCC</title>' -and $html -match 'TRIAL - not for real guest data' -and $set.Hcc.Hosting.ServerName -eq 'hotelpc' -and $ops.BackupDirectory -eq 'Z:\HccBackups' -and $set.Hcc.Install.Mode -eq 'Trial' -and
    $ops.LogDirectory -eq 'Z:\HccBackups\Logs' -and $set.Hcc.Sql.BackupDirectory -eq 'Z:\HccBackups' -and (Test-Path -LiteralPath (Join-Path $r.Fx.Data 'Keys\HCC-Hotel-CA-password.txt')) -and ($instCalls -contains 'trust-ca') -and ($instCalls -contains 'http:https://hotelpc:5443/')
}
Check 'INST-SERVER trial mark: only the first <title> and the first <body> before the script are touched, the script stays byte for byte, the page still matches its own CSP, idempotent (fixture has <title> and <body> INSIDE its script)' {
    $fx = Initialize-InstFixture 'banner'; $p = Join-Path $fx.App 'Server\web\index.html'
    $before = [IO.File]::ReadAllText($p, [Text.Encoding]::UTF8)
    Set-HccTrialPage -IndexHtml $p; Set-HccTrialPage -IndexHtml $p
    $after = [IO.File]::ReadAllText($p, [Text.Encoding]::UTF8)
    $scriptBefore = [regex]::Match($before, '<script>(.*?)</script>').Groups[1].Value
    $scriptAfter = [regex]::Match($after, '<script>(.*?)</script>').Groups[1].Value
    $added = [regex]::Match($after, '<!--HCC-TRIAL-BANNER-->.*?</table>').Value
    ([regex]::Matches($after, 'HCC-TRIAL-BANNER')).Count -eq 1 -and $scriptAfter -ceq $scriptBefore -and $scriptAfter.Contains('<title>x</title><body>') -and ([regex]::Matches($after, '<title>TRIAL - HCC</title>')).Count -eq 1 -and
    $added -notmatch '<script' -and $added -notmatch '<style' -and $added -notmatch 'style\s*=' -and $after.IndexOf('<!--HCC-TRIAL-BANNER-->') -gt $after.IndexOf('<body>') -and $after.IndexOf('<!--HCC-TRIAL-BANNER-->') -lt $after.IndexOf('<script>') -and
    @(Test-HccPageCsp $after).Count -eq 0 -and @(Test-HccPageCsp $before).Count -eq 0 -and (Throws { Set-HccTrialPage -IndexHtml (Join-Path $fx.Dir 'missing.html') })
}
Check 'INST-SERVER trial mark refuses a page that does not have the expected shape (title after the script, body after the script, no title, no CSP meta, stale hash); nothing is written' {
    $fx = Initialize-InstFixture 'shape'; $p = Join-Path $fx.App 'Server\web\index.html'
    $good = New-InstPageHtml
    $cases = @(
        $good.Replace('<title>HCC</title></head>', '</head>'),                                           # no title before the script
        $good.Replace('</head><body>', '</head>').Replace('</script></body>', '</script><body></body>'),   # body only after the script
        [regex]::Replace($good, '<meta http-equiv="Content-Security-Policy"[^>]*>', ''),                  # no CSP meta
        $good.Replace('script-src ''sha256-', 'script-src ''sha256-AAAA'))                                 # stale hash
    $allRefused = $true
    foreach ($c in $cases) {
        [IO.File]::WriteAllText($p, $c, (New-Object Text.UTF8Encoding($false)))
        if (-not (Throws { Set-HccTrialPage -IndexHtml $p }) -or ([IO.File]::ReadAllText($p, [Text.Encoding]::UTF8) -cne $c)) { $allRefused = $false }
    }
    $allRefused
}
Check 'INST-SERVER the old blind <title> replace would have broken the page (regression guard): replacing every <title> changes the script and Test-HccPageCsp says so' {
    $good = New-InstPageHtml
    $broken = [regex]::Replace($good, '<title>', '<title>TRIAL - ', 'IgnoreCase')
    @(Test-HccPageCsp $good).Count -eq 0 -and @(Test-HccPageCsp $broken).Count -gt 0 -and $orch -notmatch "Replace\(\`$html, '<title>'"
}
Check 'INST-SERVER secrets: the passwords are SecureStrings in the script calls, and appear in no log, state, summary, progress or settings file (a child echoing them is scrubbed)' {
    $r = Invoke-InstRun 'secrets' @{ caPfxPassword = $instCaMarker }
    $all = Get-InstTreeText $r.Fx
    $p = $instParams['Initialize-HccProperty.ps1']
    $r.Code -eq 0 -and $p.OwnerPassword -is [securestring] -and $instParams['New-HccCertificate.ps1'].CaPfxPassword -is [securestring] -and $r.Log -match 'child script echoes the Owner password \[hidden\]' -and
    $all -notmatch [regex]::Escape($instMarker) -and $all -notmatch [regex]::Escape($instCaMarker) -and $r.Log -match 'migrate output that wrongly echoes \[hidden\]' -and
    (@($instParams.Values | ForEach-Object { $_.Values } | Where-Object { $_ -is [string] -and ($_ -match [regex]::Escape($instMarker)) }).Count -eq 0)
}
Check 'INST-SERVER the generated TRIAL CA password is a SecureString for the script, scrubbed from the log, and stored only in the ACL-protected Keys folder' {
    $r = Invoke-InstRun 'trialpw'
    $pwFile = Join-Path $r.Fx.Data 'Keys\HCC-Hotel-CA-password.txt'
    $gen = ((Get-Content -LiteralPath $pwFile) | Select-Object -Last 1)
    $r.Code -eq 0 -and $gen.Length -ge 32 -and $r.Log -notmatch [regex]::Escape($gen) -and $instParams['New-HccCertificate.ps1'].CaPfxPassword -is [securestring] -and ($instCalls -contains 'protect-folder:Keys') -and
    ($instParams['New-HccCertificate.ps1'].CaPfxPath -eq (Join-Path $r.Fx.Data 'Keys\HCC-Hotel-CA.pfx')) -and ($instParams['New-HccGuestIdKey.ps1'].OfflineCopyPath -like "$($r.Fx.Data)\TrialKeyCopies\*") -and ($instCalls -contains 'protect-folder:TrialKeyCopies')
}
Check 'INST-SERVER hotel name with quote, ampersand, percent, semicolon, backslash and Unicode reaches Initialize-HccProperty and the client kit unchanged (no command line is built from it)' {
    $r = Invoke-InstRun 'odd' @{ hotelName = $instHotelOdd }
    $r.Code -eq 0 -and $instParams['Initialize-HccProperty.ps1'].HotelName -ceq $instHotelOdd -and $instParams['New-HccClientKit.ps1'].HotelName -ceq $instHotelOdd -and $r.State.hotelName -ceq $instHotelOdd -and
    $orch -notmatch 'Invoke-Expression' -and $orch -notmatch 'iex ' -and $orch -notmatch 'cmd /c'
}
Check 'INST-SERVER answer file is deleted on failure too, a failed step stops the install with exit 1, the log names the step, nothing after it ran' {
    $script:instFail = 'Initialize-HccDatabase.ps1'
    $r = Invoke-InstRun 'fail'
    $script:instFail = ''
    $r.Code -eq 1 -and -not (Test-Path -LiteralPath $r.Answers) -and $r.State.status -eq 'Failed' -and $r.State.failedStep -eq 'database' -and (@($r.State.completed) -join ',') -eq 'sql,service-register' -and $r.Log -match 'Step 3 of 13 \(database\) FAILED' -and
    $r.Log -notmatch [regex]::Escape($instMarker) -and ($instCalls -notcontains 'New-HccCertificate.ps1') -and (Get-Content -LiteralPath (Join-Path $r.Fx.Data 'Setup\install-summary.txt') -Raw) -match 'status=Failed'
}
Check 'INST-SERVER a failing migrate (exit code 5) fails the install; it is never reported as success' {
    $script:instFail = 'migrate'
    $r = Invoke-InstRun 'migfail'
    $script:instFail = ''
    $r.Code -eq 1 -and $r.State.failedStep -eq 'migrate' -and $r.Log -match 'migrate failed with code 5' -and ($instCalls -notcontains 'Initialize-HccProperty.ps1') -and ($instCalls -notcontains 'start-service') -and (Get-Content -LiteralPath (Join-Path $r.Fx.Data 'Setup\progress.txt') -Raw) -match 'status=failed'
}
Check 'INST-SERVER -Resume continues at the failed step: finished steps are not run again' {
    $script:instFail = 'New-HccCertificate.ps1'
    $r1 = Invoke-InstRun 'resume'
    $script:instFail = ''
    $firstCalls = @($instCalls)
    $r2 = Invoke-InstRun 'resume' -Resume -Fx $r1.Fx
    $r1.Code -eq 1 -and $r1.State.failedStep -eq 'certificate' -and (@($r1.State.completed) -join ',') -eq 'sql,service-register,database' -and $r2.Code -eq 0 -and @($r2.State.completed).Count -eq 13 -and ($instCalls -notcontains 'Bootstrap-HccSql.ps1') -and
    ($instCalls -notcontains 'Install-HccService.ps1') -and ($instCalls -notcontains 'Initialize-HccDatabase.ps1') -and ($instCalls -contains 'New-HccCertificate.ps1') -and
    ($instCalls -contains 'native:Hcc.Api.exe migrate env=Production') -and ($instCalls -contains 'New-HccClientKit.ps1') -and -not (Test-Path -LiteralPath $r2.Answers) -and ($firstCalls -notcontains 'native:Hcc.Api.exe migrate env=Production')
}
Check 'INST-SERVER SQL bootstrap exit 2 returns exit 2 (restart Windows), records it, and -Resume then continues at the SQL step' {
    $script:instSqlExit = 2
    $r1 = Invoke-InstRun 'restart'
    $script:instSqlExit = 0
    $r2 = Invoke-InstRun 'restart' -Resume -Fx $r1.Fx
    $r1.Code -eq 2 -and $r1.State.status -eq 'RestartNeeded' -and $r1.State.failedStep -eq 'sql' -and @($r1.State.completed).Count -eq 0 -and -not (Test-Path -LiteralPath $r1.Answers) -and $r1.Log -match 'Windows must restart' -and
    $r2.Code -eq 0 -and $r2.State.status -eq 'Done' -and ($instCalls -contains 'Bootstrap-HccSql.ps1')
}
Check 'INST-SERVER bad answers are refused before anything is done (weak password, password with the login, bad port, bad name, no IP, bad backup path, bad mode)' {
    $bad = @(@{ ownerPassword = 'short' }, @{ ownerPassword = 'owner-owner-owner' }, @{ ownerPassword = 'aaaaaaaaaaaaaaaa' }, @{ httpsPort = 80 }, @{ serverName = 'bad name;calc' }, @{ serverIps = @() }, @{ serverIps = @('999.1.1.1') }, @{ backupDirectory = 'backups' },
        @{ mode = 'Maybe' }, @{ ownerUserName = 'a b' }, @{ sql = 'Other' }, @{ hotelName = '' })
    $allRefused = $true; $n = 0
    foreach ($b in $bad) {
        $n++; $r = Invoke-InstRun "bad$n" $b
        if ($r.Code -ne 1 -or @($instCalls | Where-Object { $_ -notlike 'protect-folder:*' }).Count -ne 0 -or (Test-Path -LiteralPath $r.Answers)) { $allRefused = $false; Write-Host "  not refused: $($b.Keys -join ',')" }
    }
    $allRefused
}
Check 'INST-SERVER Production answers need the CA password (12+) and an existing USB folder that is not on the install drive' {
    $usb = Join-Path $instRoot 'usb'; New-Item -ItemType Directory -Path $usb -Force | Out-Null
    $r0 = Invoke-InstRun 'prod0' @{ mode = 'Production' }
    $r1 = Invoke-InstRun 'prod1' @{ mode = 'Production'; caPfxPassword = 'short'; offlineCopyDirectory = $usb }
    $r2 = Invoke-InstRun 'prod2' @{ mode = 'Production'; caPfxPassword = $instCaMarker; offlineCopyDirectory = (Join-Path $instRoot 'no-such-usb') }
    $r3 = Invoke-InstRun 'prod3' @{ mode = 'Production'; caPfxPassword = $instCaMarker; offlineCopyDirectory = $usb }
    $script:instDriveType = 'Fixed'
    $r4 = Invoke-InstRun 'prod4' @{ mode = 'Production'; caPfxPassword = $instCaMarker; offlineCopyDirectory = $usb }
    $script:instDriveType = 'Removable'
    $r0.Code -eq 1 -and $r1.Code -eq 1 -and $r2.Code -eq 1 -and $r3.Code -eq 0 -and $r4.Code -eq 1 -and $r4.Log -match 'removable USB stick'
}
Check 'INST-SERVER Production run: CA key and key copies go to the USB folder (timestamped names), CA password passed as SecureString, no TRIAL banner, nothing in TrialKeyCopies' {
    $usb = Join-Path $instRoot 'usb2'; New-Item -ItemType Directory -Path $usb -Force | Out-Null
    $r = Invoke-InstRun 'prod' @{ mode = 'Production'; caPfxPassword = $instCaMarker; offlineCopyDirectory = $usb }
    $html = Get-Content -LiteralPath (Join-Path $r.Fx.App 'Server\web\index.html') -Raw
    $r.Code -eq 0 -and $instParams['New-HccCertificate.ps1'].CaPfxPath -like "$usb\HCC-Hotel-CA-*.pfx" -and $instParams['New-HccGuestIdKey.ps1'].OfflineCopyPath -like "$usb\HCC-guest-id-key-*.txt" -and
    $instParams['New-HccBackupKey.ps1'].OfflineCopyPath -like "$usb\HCC-backup-key-*.txt" -and $instParams['New-HccCertificate.ps1'].CaPfxPassword -is [securestring] -and -not $html.Contains('HCC-TRIAL-BANNER') -and
    -not (Test-Path -LiteralPath (Join-Path $r.Fx.Data 'TrialKeyCopies')) -and -not (Test-Path -LiteralPath (Join-Path $r.Fx.Data 'Keys\HCC-Hotel-CA-password.txt')) -and (Get-InstTreeText $r.Fx) -notmatch [regex]::Escape($instCaMarker)
}
Check 'INST-SERVER a trial install cannot be continued or re-run as Production (and the other way round)' {
    $fx = Initialize-InstFixture 'modeswitch'
    $r1 = Invoke-InstRun 'modeswitch' -Fx $fx
    $usb = Join-Path $instRoot 'usb3'; New-Item -ItemType Directory -Path $usb -Force | Out-Null
    $r2 = Invoke-InstRun 'modeswitch' @{ mode = 'Production'; caPfxPassword = $instCaMarker; offlineCopyDirectory = $usb } -Fx $fx
    $r1.Code -eq 0 -and $r2.Code -eq 1 -and $r2.Log -match 'cannot be turned into a production install' -and @($instCalls | Where-Object { $_ -match '\.ps1$' }).Count -eq 0
}
Check 'INST-SERVER an answer folder that is not private makes the install refuse to read the answers (and the file is still deleted)' {
    $script:instFolderPrivate = $false
    $r = Invoke-InstRun 'notprivate'
    $script:instFolderPrivate = $true
    $r.Code -eq 1 -and @($instCalls | Where-Object { $_ -notlike 'protect-folder:*' }).Count -eq 0 -and -not (Test-Path -LiteralPath $r.Answers) -and $r.Log -match 'not private'
}
Check 'INST-SERVER running the install twice is idempotent: existing keys, service and certificate are kept, nothing is created again' {
    $fx = Initialize-InstFixture 'twice'
    $r1 = Invoke-InstRun 'twice' -Fx $fx
    $script:instServiceInstalled = $true
    $r2 = Invoke-InstRun 'twice' -Fx $fx
    $script:instServiceInstalled = $false
    $r1.Code -eq 0 -and $r2.Code -eq 0 -and ($instCalls -notcontains 'New-HccCertificate.ps1') -and ($instCalls -notcontains 'New-HccGuestIdKey.ps1') -and ($instCalls -notcontains 'New-HccBackupKey.ps1') -and ($instCalls -notcontains 'Install-HccService.ps1') -and ($instCalls -notcontains 'Initialize-HccProperty.ps1') -and
    $r2.Log -match 'already exists; it is kept'
}

# --- settings merge and upgrade -----------------------------------------------------------------------------------------------
Check 'INST-SERVER Merge-HccJsonMissing adds new settings, never changes an existing value (nested, arrays, empty strings, false), skips _notes' {
    $existing = '{"A":{"x":1,"list":["keep"],"empty":"","flag":false},"B":"mine"}' | ConvertFrom-Json
    $sample = '{"A":{"x":99,"list":["sample"],"empty":"default","flag":true,"new":5},"B":"sample","C":{"d":1},"_notes":["n"]}' | ConvertFrom-Json
    $n = Merge-HccJsonMissing -Existing $existing -Sample $sample
    $n -eq 2 -and $existing.A.x -eq 1 -and $existing.A.list[0] -eq 'keep' -and $existing.A.empty -eq '' -and $existing.A.flag -eq $false -and $existing.A.new -eq 5 -and $existing.B -eq 'mine' -and $existing.C.d -eq 1 -and -not $existing.PSObject.Properties['_notes']
}
Check 'INST-SERVER upgrade: no questions, settings merged without overwriting, files kept, stop -> migrate -> start -> client kit -> health; no database, certificate, key or Owner step' {
    $fx = Initialize-InstFixture 'upgrade'
    # an installed system: customised settings, an older sample without a setting the new sample has
    Set-InstSettings $fx { param($j) $j.Hcc.Hosting.CertificateThumbprint = 'KEEPME'; $j.Hcc.Hosting.LoopbackHttpPort = 0; $j.Hcc.Sql.BackupDirectory = 'E:\Mine'; $j.Hcc.Hosting.PSObject.Properties.Remove('PublicPort') }
    Write-HccTextFile (Join-Path $fx.Data 'Setup\install-state.json') (@{ mode = 'Production'; hotelName = 'H'; serverName = 'hotelpc'; httpsPort = 5443; status = 'Done'; completed = @('sql') } | ConvertTo-Json)
    New-Item -ItemType Directory -Path (Join-Path $fx.Data 'certs') -Force | Out-Null; Set-Content -LiteralPath (Join-Path $fx.Data 'certs\HCC-Hotel-CA.cer') -Value 'cer'
    Copy-Item -LiteralPath (Join-Path $root 'hcc-ops.sample.json') -Destination (Join-Path $fx.Ops 'hcc-ops.json')
    $opsText = Get-Content -LiteralPath (Join-Path $fx.Ops 'hcc-ops.json') -Raw
    $keysBefore = @(Get-ChildItem -LiteralPath $fx.Data -Recurse -File | ForEach-Object FullName)
    $r = Invoke-InstRun 'upgrade' -Upgrade -Fx $fx
    $set = Get-Content -LiteralPath (Join-Path $fx.App 'Server\appsettings.Production.json') -Raw | ConvertFrom-Json
    $seq = ($instCalls | Where-Object { $_ -match '^(stop-service|native:|start-service|New-HccClientKit|http:)' }) -join '|'
    $r.Code -eq 0 -and $set.Hcc.Hosting.CertificateThumbprint -eq 'KEEPME' -and $set.Hcc.Sql.BackupDirectory -eq 'E:\Mine' -and $set.Hcc.Hosting.LoopbackHttpPort -eq 0 -and $set.Hcc.Hosting.PublicPort -eq 0 -and (Test-Path -LiteralPath (Join-Path $fx.Data 'Setup\appsettings.Production.json.before-upgrade')) -and -not (Test-Path -LiteralPath (Join-Path $fx.App 'Server\appsettings.Production.json.before-upgrade')) -and
    (Get-Content -LiteralPath (Join-Path $fx.Ops 'hcc-ops.json') -Raw) -eq $opsText -and
    $seq -match '^stop-service\|native:Hcc.Api.exe migrate env=Production\|start-service\|New-HccClientKit.ps1\|http:' -and ($instCalls -notcontains 'Initialize-HccDatabase.ps1') -and ($instCalls -notcontains 'New-HccCertificate.ps1') -and ($instCalls -notcontains 'New-HccGuestIdKey.ps1') -and
    ($instCalls -notcontains 'New-HccBackupKey.ps1') -and ($instCalls -notcontains 'Initialize-HccProperty.ps1') -and ($instCalls -notcontains 'Bootstrap-HccSql.ps1') -and ($instCalls -notcontains 'Install-HccService.ps1') -and
    (@($keysBefore | Where-Object { -not (Test-Path -LiteralPath $_) }).Count -eq 0) -and (Get-Content -LiteralPath (Join-Path $fx.Data 'Setup\install-summary.txt') -Raw) -match 'status=Done'
}
Check 'INST-SERVER upgrade stops at a failing migrate with exit 1 and never starts the service afterwards' {
    $fx = Initialize-InstFixture 'upgfail'
    Write-HccTextFile (Join-Path $fx.Data 'Setup\install-state.json') (@{ mode = 'Production'; serverName = 'hotelpc'; httpsPort = 5443; status = 'Done'; completed = @('sql') } | ConvertTo-Json)
    $script:instFail = 'migrate'
    $r = Invoke-InstRun 'upgfail' -Upgrade -Fx $fx
    $script:instFail = ''
    $r.Code -eq 1 -and ($instCalls -notcontains 'start-service') -and ($instCalls -contains 'stop-service')
}

# --- uninstall ------------------------------------------------------------------------------------------------------------------
Check 'INST-SERVER uninstall removes only the service, firewall rules and scheduled tasks; it never touches SQL, databases, backups, keys, certificates or settings' {
    $fx = Initialize-InstFixture 'uninst'
    Initialize-HccInstallContext -AppDirectory $fx.App -DataDirectory $fx.Data -OperationsDirectory $fx.Ops
    $script:instCalls.Clear(); $script:instFx = $fx
    $code = Start-HccInstall -Uninstall -AppDirectory $fx.App -DataDirectory $fx.Data -OperationsDirectory $fx.Ops
    $body = (Get-Command Invoke-HccUninstall).ScriptBlock.ToString()
    [int]($code | Select-Object -Last 1) -eq 0 -and ($instCalls -contains 'Install-HccService.ps1') -and ($instCalls -contains 'Register-HccBackupTasks.ps1') -and $instParams['Install-HccService.ps1'].Uninstall -eq $true -and $instParams['Install-HccService.ps1'].HttpsPort -eq 5443 -and $instParams['Register-HccBackupTasks.ps1'].Unregister -eq $true -and
    $body -notmatch 'DROP |sqlcmd|Invoke-HccSql|Remove-Item|Remove-Certificate|Set-Content' -and (Test-Path -LiteralPath (Join-Path $fx.App 'Server\appsettings.Production.json')) -and (Get-Content -LiteralPath (Join-Path $fx.Data 'Setup\install-summary.txt') -Raw -ErrorAction SilentlyContinue) -notmatch 'status=Done'
}

Check 'INST-SERVER hotel CA SHA-256 fingerprint: written to the log and to the summary file the finish page reads, equal to the hash of the .cer' {
    $r = Invoke-InstRun 'fp'
    $hex = (Get-FileHash -LiteralPath (Join-Path $r.Fx.Data 'certs\HCC-Hotel-CA.cer') -Algorithm SHA256).Hash
    $grouped = (($hex -split '(?<=\G.{2})(?=.)') -join ':')
    $sum = Get-Content -LiteralPath (Join-Path $r.Fx.Data 'Setup\install-summary.txt') -Raw
    $r.Code -eq 0 -and $sum -match "caSha256=$hex" -and $r.Log.Contains("Hotel CA SHA-256 fingerprint: $grouped") -and $r.Log -match 'compare this with what the client setup shows' -and
    $iss.Contains("ReadSetting(SetupDir + '\install-summary.txt', 'caSha256')") -and $iss.Contains('SHA-256 fingerprint') -and $iss.Contains('WizardForm.FinishedLabel.Caption')
}
Check 'INST-SERVER SQL warnings: read from the bootstrap''s warnings file (strings or objects), logged as WARN, written to warnings.txt for the wizard; the parameter is passed only when the script declares it' {
    $script:instSqlWarnings = '{ "warnings": [ { "code": "MixedMode", "text": "Mixed-mode logins are enabled." }, { "code": "TcpEnabled", "text": "TCP/IP is on." } ] }'
    $script:instHasWarningsParam = $false
    $r0 = Invoke-InstRun 'warn0'
    $p0 = $instParams['Bootstrap-HccSql.ps1']
    $script:instHasWarningsParam = $true
    $r1 = Invoke-InstRun 'warn1'
    $p1 = $instParams['Bootstrap-HccSql.ps1']
    $script:instHasWarningsParam = $false; $script:instSqlWarnings = $null
    $w = Get-Content -LiteralPath (Join-Path $r1.Fx.Data 'Setup\warnings.txt') -Raw
    $r2 = Invoke-InstRun 'warn2'
    $r0.Code -eq 0 -and -not $p0.ContainsKey('WarningsFile') -and $p1.ContainsKey('WarningsFile') -and $r1.Log -match 'WARN SQL Server: Mixed-mode logins are enabled' -and $r1.Log -match 'WARN SQL Server: TCP/IP is on' -and
    $w -match 'SQL Server: Mixed-mode' -and $w -match 'SQL Server: TCP/IP is on' -and -not (Test-Path -LiteralPath (Join-Path $r2.Fx.Data 'Setup\warnings.txt')) -and
    $iss.Contains("warnings.txt") -and $iss.Contains('Please read (also in the log)') -and $iss.Contains('FilenameLabel.Caption := Trim(WarningsText)')
}
Check 'INST-SERVER the SQL media folder always gets -MediaDir (the two client MSIs are shipped for "use existing" too); the log file is handed to the bootstrap' {
    $r = Invoke-InstRun 'media' @{ sql = 'UseExisting' }
    $p = $instParams['Bootstrap-HccSql.ps1']
    $r.Code -eq 0 -and $p.Mode -eq 'UseExisting' -and $p.MediaDir -eq (Join-Path $r.Fx.App 'SqlSetupMedia') -and $p.LogFile -like '*install-*.log'
}
Check 'INST-SERVER private folders are created with their permissions in one call (no create-then-ACL): orchestrator and wizard' {
    $fn = (Get-Command New-HccPrivateDirectory).ScriptBlock.ToString() + (Get-Command Get-HccPrivateSecurity).ScriptBlock.ToString()
    $pf = [regex]::Match($orch, 'function Protect-HccFolder \{[\s\S]*?\n\}').Value   # the real one (the tests replace the function by a recorder)
    $fn -match 'DirectorySecurity' -and $fn -match 'SetAccessRuleProtection\(\$true, \$false\)' -and $fn -match 'S-1-5-32-544' -and $fn -match 'S-1-5-18' -and $fn -match 'SetOwner' -and $fn -match '\[System\.IO\.Directory\]::CreateDirectory\(\$Path, \$sec\)' -and
    $fn -match 'FileSystemAclExtensions\]::Create' -and $fn -notmatch 'icacls|New-Item -ItemType Directory -Path \$Path' -and $pf -match 'New-HccPrivateDirectory' -and
    $iss -match 'procedure CurStepChanged[\s\S]*CreatePrivateFolder\(ExpandConstant\(''\{app\}\\SqlSetupMedia''\)\)' -and $iss -match 'function PrepareSetupFolder: Boolean;\s*begin\s*Result := CreatePrivateFolder\(SetupDir\)' -and $iss -notmatch 'ForceDirectories\(SetupDir\)' -and $iss -notmatch 'Exec\(ExpandConstant\(''\{sys\}\\icacls'
}
Check 'INST-SERVER Read-HccSqlWarnings understands { "warnings": [ { code, text } ] } and, defensively, a plain array of strings' {
    $f1 = Join-Path $instRoot 'w1.json'; $f2 = Join-Path $instRoot 'w2.json'
    Set-Content -LiteralPath $f1 -Value '{ "warnings": [ { "code": "TcpEnabled", "text": "TCP is on." } ] }'
    Set-Content -LiteralPath $f2 -Value '["plain one", "plain two"]'
    $a = @(Read-HccSqlWarnings -Path $f1); $b = @(Read-HccSqlWarnings -Path $f2); $c = @(Read-HccSqlWarnings -Path (Join-Path $instRoot 'none.json'))
    $a.Count -eq 1 -and $a[0] -eq 'TCP is on.' -and $b.Count -eq 2 -and $c.Count -eq 0
}
Check 'INST-SERVER data folders are the first action: created private (not created then locked), a link is refused, a folder others can write is repaired or the run is refused; all paths passed to scripts lie inside them' {
    $fx = Initialize-InstFixture 'folders'
    $dd = Join-Path $fx.Dir 'newdata'
    Initialize-HccInstallContext -AppDirectory $fx.App -DataDirectory $dd -OperationsDirectory $fx.Ops
    $script:instCalls.Clear()
    Initialize-HccDataFolders
    $created = @($instCalls | Where-Object { $_ -like 'protect-folder:*' })
    # an existing but unsafe folder is repaired; a link is refused; an unrepairable one is refused
    $script:instRepairs = 0
    function Repair-HccPrivateDirectory { param([string]$Path) $script:instRepairs++; $script:instFolderProblems = @() }
    $script:instFolderProblems = @('Users can change x')
    Initialize-HccInstallContext -AppDirectory $fx.App -DataDirectory $fx.Data -OperationsDirectory $fx.Ops
    Initialize-HccDataFolders
    $repaired = $script:instRepairs -ge 1
    $script:instFolderProblems = @('C:\x is a link')
    $link = Throws { Initialize-HccDataFolders }
    $script:instFolderProblems = @()
    $r = Invoke-InstRun 'folders2'
    $logIn = $instParams['Bootstrap-HccSql.ps1'].LogFile -like "$($r.Fx.Data)\Logs\*"
    $created.Count -eq 3 -and $created[0] -eq 'protect-folder:newdata' -and $created[1] -eq 'protect-folder:Setup' -and $created[2] -eq 'protect-folder:Logs' -and $repaired -and $link -and $r.Code -eq 0 -and $logIn -and
    $iss -match 'if CurStep = ssInstall then\s*begin[^;]*\r?\n\s*if not CreatePrivateFolder\(DataDir\)' -and $iss -notmatch 'Name: "\{commonappdata\}\\HCC"'
}

Check 'INST-SERVER the service is registered BEFORE the database step (its account must exist for SQL Server): the database step refuses without it, and the call order is service then database' {
    $r = Invoke-InstRun 'svcfirst'
    $iSvc = $instCalls.IndexOf('Install-HccService.ps1'); $iDb = $instCalls.IndexOf('Initialize-HccDatabase.ps1'); $iMig = $instCalls.IndexOf('native:Hcc.Api.exe migrate env=Production'); $iStart = $instCalls.IndexOf('start-service')
    $script:instServiceInstalled = $false
    $refused = Throws { Step-Database @{ backupDirectory = 'Z:\b' } }
    $r.Code -eq 0 -and $iSvc -ge 0 -and $iSvc -lt $iDb -and $iDb -lt $iMig -and $iMig -lt $iStart -and $refused
}
Check 'INST-SERVER a failed grant of the certificate key to the service fails the install at the start step (no warning-only), before the hotel is set up' {
    $script:instGrantFail = $true
    $r = Invoke-InstRun 'grantfail'
    $script:instGrantFail = $false
    $r.Code -eq 1 -and $r.State.failedStep -eq 'service-start' -and $r.Log -match 'cannot be given access to its certificate key' -and ($instCalls -notcontains 'start-service') -and ($instCalls -notcontains 'Initialize-HccProperty.ps1')
}
Check 'INST-SERVER a settings file readable by others stops the install after the settings step (the check does not rely on any other folder''s permissions)' {
    $script:instAclProblems = @('Users has access to appsettings.Production.json')
    $r = Invoke-InstRun 'aclfail'
    $script:instAclProblems = @()
    $r.Code -eq 1 -and $r.State.failedStep -eq 'service-register' -and $r.Log -match 'settings file is readable by others' -and ($instCalls -notcontains 'Initialize-HccDatabase.ps1') -and
    $orch -match 'function Assert-HccSettingsAcl' -and $orch -match 'Test-HccProtectedDirectory -Path \$script:SettingsFile -AllowedAccounts @\(\$script:ServiceAccountName\)'
}
Check 'INST-SERVER a JSON error in the answer file does not put the file (passwords) into the log' {
    $fx = Initialize-InstFixture 'badjson'
    [IO.File]::WriteAllText($fx.Answers, '{ "ownerPassword": "' + $instMarker + '", "mode": }', (New-Object Text.UTF8Encoding($false)))
    $script:instFx = $fx; $script:instCalls.Clear()
    $code = Start-HccInstall -AnswerFile $fx.Answers -AppDirectory $fx.App -DataDirectory $fx.Data -OperationsDirectory $fx.Ops
    $log = @(Get-ChildItem -LiteralPath (Join-Path $fx.Data 'Logs') -Filter 'install-*.log' | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
    [int]($code | Select-Object -Last 1) -eq 1 -and $log -match 'not valid JSON' -and (Get-InstTreeText $fx) -notmatch [regex]::Escape($instMarker) -and -not (Test-Path -LiteralPath $fx.Answers)
}
Check 'INST-SERVER hotel setup is idempotent: if the hotel already exists only the setup listener is closed and the service restarted; the loopback port is used for the call' {
    $script:instSetupRequired = $false
    $r1 = Invoke-InstRun 'prop1'
    $c1 = @($instCalls)
    $script:instSetupRequired = $true
    $r2 = Invoke-InstRun 'prop2' -Before { param($fx) Set-InstSettings $fx { param($j) $j.Hcc.Hosting.LoopbackHttpPort = 5090 } }
    $p2 = $instParams['Initialize-HccProperty.ps1']
    $set1 = Get-Content -LiteralPath (Join-Path $r1.Fx.App 'Server\appsettings.Production.json') -Raw | ConvertFrom-Json
    $script:instSetupRequired = $true
    $r1.Code -eq 0 -and ($c1 -notcontains 'Initialize-HccProperty.ps1') -and ($c1 -contains 'restart-service') -and $r1.Log -match 'already exist' -and $set1.Hcc.Hosting.LoopbackHttpPort -eq 0 -and
    $r2.Code -eq 0 -and $p2.ServerUrl -eq 'http://127.0.0.1:5090' -and ($instCalls -contains 'http:http://127.0.0.1:5090/api/v1/setup/status')
}
Check 'INST-SERVER Trial health check fails when the served page no longer matches its CSP (a broken page can never pass); Production does not need the check' {
    $good = New-InstPageHtml
    $bad = $good.Replace('var a = 1;', 'var a = 2;')
    $fx = Initialize-InstFixture 'health'; Initialize-HccInstallContext -AppDirectory $fx.App -DataDirectory $fx.Data -OperationsDirectory $fx.Ops; $script:instFx = $fx
    $A = @{ mode = 'Trial'; serverName = 'hotelpc'; httpsPort = 5443 }
    $script:instPageOverride = $good; $ok = -not (Throws { Step-Health $A })
    $script:instPageOverride = $bad; $broken = Throws { Step-Health $A }
    $A.mode = 'Production'; $prodOk = -not (Throws { Step-Health $A })
    $script:instPageOverride = $null
    $ok -and $broken -and $prodOk
}
Check 'INST-SERVER merge never fills placeholders for AllowedHosts / LoopbackHttpPort / thumbprint / server name / backup folder / guest ID keys from the sample, and says which are missing' {
    $existing = '{"Hcc":{"Hosting":{"HttpsPort":5443},"Sql":{"CommandTimeoutSeconds":30}}}' | ConvertFrom-Json
    $sample = Get-Content -LiteralPath (Join-Path $root 'appsettings.Production.sample.json') -Raw | ConvertFrom-Json
    $missing = New-Object System.Collections.Generic.List[string]
    $n = Merge-HccJsonMissing -Existing $existing -Sample $sample -Deny $script:HccNoSampleKeys -Missing $missing
    $existing.PSObject.Properties['AllowedHosts'] -eq $null -and $existing.Hcc.Hosting.PSObject.Properties['LoopbackHttpPort'] -eq $null -and $existing.Hcc.Hosting.PSObject.Properties['CertificateThumbprint'] -eq $null -and $existing.Hcc.Hosting.PSObject.Properties['ServerName'] -eq $null -and
    $existing.Hcc.Sql.PSObject.Properties['BackupDirectory'] -eq $null -and $missing -contains 'AllowedHosts' -and $missing -contains 'Hcc.Hosting.LoopbackHttpPort' -and $missing -contains 'Hcc.Sql.BackupDirectory' -and $existing.Hcc.Hosting.PublicPort -eq 0 -and $n -gt 0
}
Check 'INST-SERVER upgrade mode: taken from the record, then Hcc:Install:Mode, then the Trial files; with none of them the upgrade stops and asks instead of assuming Production' {
    $fx = Initialize-InstFixture 'modeup'
    $r0 = Invoke-InstRun 'modeup' -Upgrade -Fx $fx
    $log0 = $r0.Log; $calls0 = @($instCalls)
    $sum0 = Get-Content -LiteralPath (Join-Path $fx.Data 'Setup\install-summary.txt') -Raw
    Set-InstSettings $fx { param($j) $j.Hcc | Add-Member -NotePropertyName Install -NotePropertyValue ([pscustomobject]@{ Mode = 'Trial' }) -Force }
    $r1 = Invoke-InstRun 'modeup' -Upgrade -Fx $fx
    $html = Get-Content -LiteralPath (Join-Path $fx.App 'Server\web\index.html') -Raw
    $fx2 = Initialize-InstFixture 'modeup2'; New-Item -ItemType Directory -Path (Join-Path $fx2.Data 'TrialKeyCopies') -Force | Out-Null
    $r2 = Invoke-InstRun 'modeup2' -Upgrade -Fx $fx2
    $fx3 = Initialize-InstFixture 'modeup3'; Set-InstSettings $fx3 { param($j) $j.Hcc | Add-Member -NotePropertyName Install -NotePropertyValue ([pscustomobject]@{ Mode = 'Production' }) -Force }
    $r3 = Invoke-InstRun 'modeup3' -Upgrade -Fx $fx3
    $html3 = Get-Content -LiteralPath (Join-Path $fx3.App 'Server\web\index.html') -Raw
    $r0.Code -eq 1 -and $log0 -match 'Cannot tell whether this is a Trial or a Production install' -and ($calls0 -notcontains 'stop-service') -and $sum0 -match 'status=UpgradeFailed' -and
    $r1.Code -eq 0 -and $html.Contains('HCC-TRIAL-BANNER') -and $r2.Code -eq 0 -and (Get-Content -LiteralPath (Join-Path $fx2.App 'Server\web\index.html') -Raw).Contains('HCC-TRIAL-BANNER') -and $r3.Code -eq 0 -and -not $html3.Contains('HCC-TRIAL-BANNER')
}
Check 'INST-SERVER a failed upgrade writes status UpgradeFailed with the CURRENT log and the backup folder (the wizard keeps treating the next run as an upgrade)' {
    $fx = Initialize-InstFixture 'upgsum'
    Write-HccTextFile (Join-Path $fx.Data 'Setup\install-state.json') (@{ mode = 'Production'; hotelName = 'H'; serverName = 'hotelpc'; httpsPort = 5443; status = 'Done'; backupDirectory = 'Z:\HccBackups'; completed = @('sql') } | ConvertTo-Json)
    Write-HccTextFile (Join-Path $fx.Data 'Setup\install-summary.txt') "status=Done`r`nlogFile=C:\old\install-OLD.log`r`n"
    $script:instFail = 'migrate'
    $r = Invoke-InstRun 'upgsum' -Upgrade -Fx $fx
    $script:instFail = ''
    $sum = Get-Content -LiteralPath (Join-Path $fx.Data 'Setup\install-summary.txt') -Raw
    $r.Code -eq 1 -and $sum -match 'status=UpgradeFailed' -and $sum -match 'backupDirectory=Z:\\HccBackups' -and $sum -match 'logFile=.*install-\d{8}-\d{6}\.log' -and $sum -notmatch 'OLD' -and
    $iss.Contains("(Status = 'UpgradeFailed')") -and $iss.Contains('HCC could not be updated and is STOPPED') -and $iss.Contains("ReadSetting(SetupDir + '\install-summary.txt', 'backupDirectory')")
}
Check 'INST-SERVER resume with different answers than the finished steps used is refused and names them' {
    $script:instFail = 'New-HccCertificate.ps1'
    $r1 = Invoke-InstRun 'resumechg'
    $script:instFail = ''
    $r2 = Invoke-InstRun 'resumechg' @{ hotelName = 'Another Hotel'; backupDirectory = 'Z:\Other' } -Resume -Fx $r1.Fx
    $r1.Code -eq 1 -and $r2.Code -eq 1 -and $r2.Log -match 'differ from the earlier attempt' -and $r2.Log -match 'hotelName' -and $r2.Log -match 'backupDirectory' -and @($instCalls | Where-Object { $_ -match '\.ps1$' }).Count -eq 0
}
Check 'INST-SERVER a stray answers.json of a crashed run is removed at the start of an upgrade and of an uninstall (after the folder check); the uninstall does not depend on healthy data folders and passes the HTTPS port' {
    $fx = Initialize-InstFixture 'stray'
    Write-HccTextFile (Join-Path $fx.Data 'Setup\install-state.json') (@{ mode = 'Production'; serverName = 'hotelpc'; httpsPort = 5555; status = 'Done'; completed = @('sql') } | ConvertTo-Json)
    Set-Content -LiteralPath $fx.Answers -Value "secret $instMarker"
    $r = Invoke-InstRun 'stray' -Upgrade -Fx $fx
    $goneUp = -not (Test-Path -LiteralPath $fx.Answers)
    # unsafe folders: the uninstall still runs (and does not trust the folder to delete from)
    Set-Content -LiteralPath $fx.Answers -Value "secret $instMarker"
    $script:instFolderProblems = @('Users can change the Logs folder')
    $script:instCalls.Clear()
    $code1 = Start-HccInstall -Uninstall -AppDirectory $fx.App -DataDirectory $fx.Data -OperationsDirectory $fx.Ops
    $port1 = $instParams['Install-HccService.ps1'].HttpsPort
    $calls1 = @($instCalls)
    # safe folders: the stray file is removed
    $script:instFolderProblems = @()
    $script:instCalls.Clear()
    $code2 = Start-HccInstall -Uninstall -AppDirectory $fx.App -DataDirectory $fx.Data -OperationsDirectory $fx.Ops
    $goneUn = -not (Test-Path -LiteralPath $fx.Answers)
    $r.Code -eq 0 -and $goneUp -and [int]($code1 | Select-Object -Last 1) -eq 0 -and $port1 -eq 5555 -and ($calls1 -contains 'Install-HccService.ps1') -and ($calls1 -notcontains 'repair:Setup') -and [int]($code2 | Select-Object -Last 1) -eq 0 -and $goneUn
}
Check 'INST-SERVER an uninstall step that fails is reported as UninstallPartial with a note, never as a clean removal' {
    $fx = Initialize-InstFixture 'unipart'
    Write-HccTextFile (Join-Path $fx.Data 'Setup\install-state.json') (@{ mode = 'Production'; serverName = 'hotelpc'; httpsPort = 5443; status = 'Done'; completed = @('sql') } | ConvertTo-Json)
    $script:instFail = 'Register-HccBackupTasks.ps1'
    $script:instCalls.Clear(); $script:instFx = $fx
    $code = Start-HccInstall -Uninstall -AppDirectory $fx.App -DataDirectory $fx.Data -OperationsDirectory $fx.Ops
    $script:instFail = ''
    $sum = Get-Content -LiteralPath (Join-Path $fx.Data 'Setup\install-summary.txt') -Raw
    [int]($code | Select-Object -Last 1) -eq 1 -and $sum -match 'status=UninstallPartial' -and $sum -match 'note=NOT removed: the backup tasks' -and $iss.Contains("'UninstallPartial'") -and $iss.Contains('but NOT everything else')
}

# --- checks of the answers (addresses, port, hotel name, backup folder) -------------------------------------------------------------
Check 'INST-SERVER network addresses: only four-part private IPv4 addresses that this PC has (public, 0.0.0.0, loopback, short forms, leading zeros, scope id, IPv6 are refused)' {
    $good = @('192.168.1.10', '10.0.0.5', '172.16.4.9', '172.31.255.1') | ForEach-Object { (ConvertTo-HccIpv4 $_) -and (Test-HccPrivateIpv4 $_) }
    $syntax = @('10.1', '10.0.1', '192.168.001.5', '192.168.1.256', 'fe80::1%12', '::1', '192.168.1.10/24', ' 192.168.1.10', '1.2.3.4.5', 'abc') | ForEach-Object { $null -eq (ConvertTo-HccIpv4 $_) }
    $notPrivate = @('0.0.0.0', '127.0.0.1', '255.255.255.255', '8.8.8.8', '172.15.0.1', '172.32.0.1', '224.0.0.1', '169.254.1.1', '192.169.1.1') | ForEach-Object { -not (Test-HccPrivateIpv4 $_) }
    $fx = Initialize-InstFixture 'ips'; Initialize-HccInstallContext -AppDirectory $fx.App -DataDirectory $fx.Data -OperationsDirectory $fx.Ops
    $base = @{ mode = 'Trial'; hotelName = 'H'; serverName = 'hotelpc'; serverIps = @('192.168.1.10'); httpsPort = 5443; backupDirectory = 'Z:\HccBackups'; ownerUserName = 'owner'; ownerPassword = $instMarker; sql = 'UseExisting'; caPfxPassword = ''; offlineCopyDirectory = '' }
    $a0 = @(Test-HccAnswers $base)
    $b = $base.Clone(); $b.serverIps = @('192.168.9.9'); $a1 = @(Test-HccAnswers $b)
    $b = $base.Clone(); $b.serverIps = @('8.8.8.8'); $a2 = @(Test-HccAnswers $b)
    $b = $base.Clone(); $b.serverIps = @('10.1'); $a3 = @(Test-HccAnswers $b)
    @($good | Where-Object { -not $_ }).Count -eq 0 -and @($syntax | Where-Object { -not $_ }).Count -eq 0 -and @($notPrivate | Where-Object { -not $_ }).Count -eq 0 -and $a0.Count -eq 0 -and
    $a1.Count -eq 1 -and $a1[0] -match 'not an address of this PC' -and $a2.Count -eq 1 -and $a2[0] -match 'not a private network address' -and $a3.Count -eq 1 -and $a3[0] -match 'four numbers'
}
Check 'INST-SERVER port 5080 (the setup listener) is refused as the HTTPS port; hotel name rule is the client kit''s (no control / format / separator characters, 1-100)' {
    $fx = Initialize-InstFixture 'port'; Initialize-HccInstallContext -AppDirectory $fx.App -DataDirectory $fx.Data -OperationsDirectory $fx.Ops
    $base = @{ mode = 'Trial'; hotelName = 'H'; serverName = 'hotelpc'; serverIps = @('192.168.1.10'); httpsPort = 5443; backupDirectory = 'Z:\HccBackups'; ownerUserName = 'owner'; ownerPassword = $instMarker; sql = 'UseExisting'; caPfxPassword = ''; offlineCopyDirectory = '' }
    $b = $base.Clone(); $b.httpsPort = 5080; $p = @(Test-HccAnswers $b)
    $names = @{ ok = 'Hotel ' + [string][char]0x0939; zw = 'Hotel' + [string][char]0x200B + 'X'; sep = 'A' + [string][char]0x2028 + 'B'; ctl = "A`tB"; long = ('x' * 101); empty = '   ' }
    $r = @{}; foreach ($k in $names.Keys) { $r[$k] = Test-HccHotelNameOk $names[$k] }
    $p.Count -eq 1 -and $p[0] -match '5080' -and $r.ok -and -not $r.zw -and -not $r.sep -and -not $r.ctl -and -not $r.long -and -not $r.empty -and (Get-Content -LiteralPath (Join-Path $root 'Hcc-ClientTrust.ps1') -Raw) -match '\\p\{Cc\}\\p\{Cf\}\\p\{Zl\}\\p\{Zp\}'
}
Check 'INST-SERVER backup folder: refused on a drive top, under Users, Windows, Program Files, ProgramData or the HCC folders, and when it already holds other files; accepted when new, empty or holding only HCC backups' {
    $fx = Initialize-InstFixture 'bk'; Initialize-HccInstallContext -AppDirectory $fx.App -DataDirectory $fx.Data -OperationsDirectory $fx.Ops
    $loc = @('D:\', 'C:\Users\Someone\Documents\Backups', 'C:\Users', "$env:SystemRoot\x", "$env:ProgramFiles\x", "$env:ProgramData\HCC\Backups", (Join-Path $fx.App 'Backups'), 'backups', '\\server\share\b') | ForEach-Object { [bool](Get-HccBackupLocationProblem $_) }
    $locOk = @('Z:\HccBackups', 'D:\HccBackups', 'E:\Backups\HCC') | ForEach-Object { -not (Get-HccBackupLocationProblem $_) }
    $empty = Join-Path $instRoot 'bk-empty'; New-Item -ItemType Directory -Path $empty -Force | Out-Null
    $own = Join-Path $instRoot 'bk-own'; New-Item -ItemType Directory -Path (Join-Path $own 'Logs') -Force | Out-Null; Set-Content -LiteralPath (Join-Path $own 'Hcc_FULL_20261025T120000Z.bak.hccenc') -Value 'x'
    $foreign = Join-Path $instRoot 'bk-foreign'; New-Item -ItemType Directory -Path $foreign -Force | Out-Null; Set-Content -LiteralPath (Join-Path $foreign 'holiday-photos.zip') -Value 'x'
    @($loc | Where-Object { -not $_ }).Count -eq 0 -and @($locOk | Where-Object { -not $_ }).Count -eq 0 -and -not (Get-HccBackupContentsProblem $empty) -and -not (Get-HccBackupContentsProblem $own) -and -not (Get-HccBackupContentsProblem (Join-Path $instRoot 'bk-new')) -and
    (Get-HccBackupContentsProblem $foreign) -match 'already holds other files' -and (Get-HccBackupContentsProblem (Join-Path $foreign 'holiday-photos.zip')) -match 'is a file'
}

# --- the setup wizard (.iss) rules that mirror the orchestrator --------------------------------------------------------------------
Check 'INST-SERVER iss wizard: private IPv4 only, port 5080 refused, backup folder rules, removable USB, parsed port in the Start-menu address, answer file recreated, execution-policy hint, NOT finished text, upgrade failure text' {
    $iss.Contains('function Ipv4Problem') -and $iss.Contains('is not a private network address') -and $iss.Contains('Port = 5080') -and $iss.Contains('function BackupFolderProblem') -and $iss.Contains('inside a user profile') -and $iss.Contains('already holds other files') -and
    $iss.Contains("GetDriveTypeW(Copy(UsbPage.Values[0], 1, 3)) <> 2") -and $iss.Contains("IntToStr(StrToIntDef(Trim(HotelPage.Values[3]), 5443))") -and $iss -match 'RemoveAnswerFile;\s*if FileExists\(AnswerFileName\) then' -and
    $iss.Contains('execution policy AllSigned') -and $iss.Contains('HCC setup is NOT finished. Windows must restart first.') -and $iss.Contains('not asked here: set them inside HCC') -and $iss.Contains("/setowner ''*S-1-5-32-544''") -and $iss.Contains('the folder is a link')
}
Check 'INST-SERVER data folder repair replaces the DACL (never merges), sets the owner to Administrators and refuses links; the checks cover owner, links and extra accounts (folder and answer file)' {
    $rp = [regex]::Match($orch, 'function Repair-HccPrivateDirectory \{[\s\S]*?\n\}').Value
    $fp = [regex]::Match($orch, 'function Get-HccFolderProblems \{[\s\S]*?\n\}').Value
    $fl = [regex]::Match($orch, 'function Get-HccFileProblems \{[\s\S]*?\n\}').Value
    $rp -match 'is a link' -and $rp -match '/setowner' -and $rp -match 'Set-Acl -LiteralPath \$Path -AclObject \(Get-HccPrivateSecurity\)' -and $rp -notmatch 'Set-HccProtectedDirectory' -and $fp -match 'ReparsePoint' -and $fp -match 'Get-HccOwnerSid' -and $fp -match 'Test-HccProtectedDirectory' -and
    $fl -match 'ReparsePoint' -and $fl -match 'Get-HccOwnerSid' -and $fl -match 'GetAccessRules' -and $orch -match 'Assert-HccAnswerFolderPrivate -Path \(Split-Path -Parent \$AnswerFile\) -FilePath \$AnswerFile'
}
Check 'INST-SERVER PATH: the refresh keeps what the bootstrap put in front, adds registry entries that are missing, and puts the protected sqlcmd folder first when it exists (also on a resume run)' {
    $save = $env:Path
    try {
        $env:Path = 'C:\hcc-test-front;' + $save
        & $instRealPath
        $list = @($env:Path -split ';' | Where-Object { $_ })
        $reg = @(([Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')) -split ';' | Where-Object { $_ })
        $sq = Get-HccProtectedSqlcmdFolder
        $frontKept = $list -contains 'C:\hcc-test-front'
        $firstOk = if ($sq) { $list[0] -eq $sq } else { $list[0] -eq 'C:\hcc-test-front' }
        $allReg = @($reg | Where-Object { $list -notcontains $_ }).Count -eq 0
    }
    finally { $env:Path = $save }
    $frontKept -and $firstOk -and $allReg -and $orch -match "Update-HccProcessPath\s+# the bootstrap" -and $orch -match 'function Get-HccProtectedSqlcmdFolder' -and $orch -match 'Client SDK\\ODBC\\\$v\\Tools\\Binn'
}
# --- the real Invoke-HccOps adapter (not mocked): output goes through the scrubber, a throwing script fails, a nonzero exit code is returned for Bootstrap ----------
${function:Invoke-HccOps} = $instRealOps
Check 'INST-SERVER real Invoke-HccOps: runs a script in-process, passes a SecureString, scrubs secrets from its output, returns its exit code, and a throwing script throws' {
    $fx = Initialize-InstFixture 'realops'
    Initialize-HccInstallContext -AppDirectory $fx.App -DataDirectory $fx.Data -OperationsDirectory $fx.Ops
    $script:LogFile = Join-Path $fx.Dir 'ops.log'; $script:Secrets.Clear(); Add-HccSecret 'REALOPS-SECRET-123'
    Set-Content -LiteralPath (Join-Path $fx.Ops 'Echo-Test.ps1') -Value 'param([securestring]$P, [string]$Name) Write-Host "hello $Name REALOPS-SECRET-123"; Write-Warning "warn REALOPS-SECRET-123"; exit 7'
    Set-Content -LiteralPath (Join-Path $fx.Ops 'Throw-Test.ps1') -Value 'param() throw "boom REALOPS-SECRET-123"'
    $code = Invoke-HccOps -Script 'Echo-Test.ps1' -Params @{ P = (ConvertTo-HccSecureString 'x1y2z3w4'); Name = 'n' } -UseExitCode
    $threw = $false; try { Invoke-HccOps -Script 'Throw-Test.ps1' | Out-Null } catch { $threw = $true }
    $missing = $false; try { Invoke-HccOps -Script 'Nope.ps1' | Out-Null } catch { $missing = $true }
    $log = Get-Content -LiteralPath $script:LogFile -Raw
    $script:Secrets.Clear(); $script:LogFile = $null
    $code -eq 7 -and $threw -and $missing -and $log -match 'hello n \[hidden\]' -and $log -match 'warn \[hidden\]' -and $log -notmatch 'REALOPS-SECRET-123'
}
Check 'INST-SERVER scripts that gained parameters keep their old behaviour: New-HccCertificate -CaPfxPassword and Initialize-HccProperty -OwnerPassword are optional securestring parameters, the prompts remain the default' {
    $c = Get-Command (Join-Path $root 'New-HccCertificate.ps1'); $p = Get-Command (Join-Path $root 'Initialize-HccProperty.ps1')
    $c.Parameters['CaPfxPassword'].ParameterType -eq [securestring] -and -not ($c.Parameters['CaPfxPassword'].Attributes | Where-Object { $_ -is [Management.Automation.ParameterAttribute] -and $_.Mandatory }) -and
    $p.Parameters['OwnerPassword'].ParameterType -eq [securestring] -and -not ($p.Parameters['OwnerPassword'].Attributes | Where-Object { $_ -is [Management.Automation.ParameterAttribute] -and $_.Mandatory }) -and
    (Get-Content -LiteralPath (Join-Path $root 'New-HccCertificate.ps1') -Raw) -match "Read-Host 'Password to protect the CA backup file" -and (Get-Content -LiteralPath (Join-Path $root 'Initialize-HccProperty.ps1') -Raw) -match 'Read-Secret "Owner password for'
}
Remove-Item -LiteralPath $instRoot -Recurse -Force -ErrorAction SilentlyContinue
# /INST-SERVER ---------------------------------------------------------------------------------------------------------
Write-Host ''
Write-Host "Passed: $script:passed  Failed: $script:failed"
if ($script:failed -gt 0) { exit 1 }
exit 0
