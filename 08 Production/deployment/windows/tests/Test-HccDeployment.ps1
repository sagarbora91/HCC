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
    (($order | Sort-Object) -join ',') -eq ($templates -join ',') -and $order.Count -eq 39 -and
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
    $r.Exit -eq 0 -and $lines.Count -eq 39 -and $lines[0] -match ' 1\. room-types\.csv' -and $lines[1] -match 'rooms\.csv' -and $lines[14] -match 'stock-items\.csv' -and
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
    $d -eq 'HccDocuments_FULL_20261025T120000Z.bak' -and (ConvertFrom-HccBackupFileName ($d + '.hccenc')).Database -eq 'HccDocuments' -and
    @(Select-HccBackupsToDelete -Names @($d, (Nm Full 200), (Nm Full 201), (Nm Full 202), (Nm Full 203)) -Database HccDocuments -UtcNow $now -RetentionDays 30 -KeepMinFulls 3).Count -eq 0 -and
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

Write-Host ''
Write-Host "Passed: $script:passed  Failed: $script:failed"
if ($script:failed -gt 0) { exit 1 }
exit 0
