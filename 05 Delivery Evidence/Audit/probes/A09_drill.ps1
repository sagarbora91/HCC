# A09 backup / restore dry run (audit only, no admin rights). Needs the dotnet-test lock. Uses the real deployment scripts against a
# disposable HccTest_* database (-Database), a throw-away backup folder and a throw-away backup key, then deletes all of them.
# Steps: (1) Backup as shipped (TrustServerCertificate false, as in hcc-ops.sample.json); (2) with -TrustServerCertificate;
# (3) differential; (4) full + differential started together (the 02:30 overlap of the scheduled tasks); (5) restore drill into
# HccTest_drill_*; (6) the drill with a different backup key (expects refusal). Output: evidence\A09\drill-*.txt
param([Parameter(Mandatory)][string]$Database)
$ErrorActionPreference = 'Continue'
$repo = 'E:\Projects\HCC\Hotel Control Centre'
$ops = Join-Path $repo '08 Production\deployment\windows'
$ev = Join-Path $repo '05 Delivery Evidence\Audit\evidence\A09'
$root = 'E:\HccTest_A09_drill'          # outside the repo; removed at the end
$bk = Join-Path $root 'backups'; $keys = Join-Path $root 'keys'; $off = Join-Path $root 'offline'; $scr = Join-Path $root 'scratch'; $keys2 = Join-Path $root 'keys2'; $off2 = Join-Path $root 'offline2'
$out = Join-Path $ev 'drill-output.txt'
function Note($t) { "`n==== $t ====" | Tee-Object -FilePath $out -Append }
if (Test-Path $root) { Remove-Item -Recurse -Force $root }
New-Item -ItemType Directory $root, $bk, $keys, $off, $scr, $keys2, $off2 | Out-Null
$sqlSvc = 'NT SERVICE\MSSQL$SQLEXPRESS'
foreach ($d in @($bk, $scr)) { & icacls.exe $d /grant "${sqlSvc}:(OI)(CI)M" | Out-Null }
$keyFile = Join-Path $keys 'hcc-backup-key.txt'
$me = [Security.Principal.WindowsIdentity]::GetCurrent().Name
Note "whoami / elevated"
"$me elevated=$(([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))" | Tee-Object -FilePath $out -Append
Note "sqlcmd version"
(sqlcmd -? 2>&1 | Select-Object -First 3) | Tee-Object -FilePath $out -Append
Note "New-HccBackupKey (key file in a throw-away folder, reader = current user)"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ops 'New-HccBackupKey.ps1') -OfflineCopyPath (Join-Path $off 'key-offline.txt') -KeyFile $keyFile -ReaderAccount $me *>&1 | Tee-Object -FilePath $out -Append
Note "1) Backup-HccDatabase -Type Full as shipped (TrustServerCertificate=false)"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ops 'Backup-HccDatabase.ps1') -Type Full -Database $Database -BackupDirectory $bk -BackupKeyFile $keyFile *>&1 | Tee-Object -FilePath $out -Append
"exit=$LASTEXITCODE" | Tee-Object -FilePath $out -Append
Note "2) Backup-HccDatabase -Type Full -TrustServerCertificate"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ops 'Backup-HccDatabase.ps1') -Type Full -Database $Database -BackupDirectory $bk -BackupKeyFile $keyFile -TrustServerCertificate *>&1 | Tee-Object -FilePath $out -Append
"exit=$LASTEXITCODE" | Tee-Object -FilePath $out -Append
Note "3) Backup-HccDatabase -Type Differential -TrustServerCertificate"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ops 'Backup-HccDatabase.ps1') -Type Differential -Database $Database -BackupDirectory $bk -BackupKeyFile $keyFile -TrustServerCertificate *>&1 | Tee-Object -FilePath $out -Append
"exit=$LASTEXITCODE" | Tee-Object -FilePath $out -Append
Note "4) Full and Differential started at the same moment (scheduled 02:30 overlap)"
$a = Start-Process powershell.exe -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$(Join-Path $ops 'Backup-HccDatabase.ps1')`"", '-Type', 'Full', '-Database', $Database, '-BackupDirectory', $bk, '-BackupKeyFile', $keyFile, '-TrustServerCertificate') -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $root 'c-full.txt') -RedirectStandardError (Join-Path $root 'c-full-err.txt')
$b = Start-Process powershell.exe -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$(Join-Path $ops 'Backup-HccDatabase.ps1')`"", '-Type', 'Differential', '-Database', $Database, '-BackupDirectory', $bk, '-BackupKeyFile', $keyFile, '-TrustServerCertificate') -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $root 'c-diff.txt') -RedirectStandardError (Join-Path $root 'c-diff-err.txt')
$a.WaitForExit(); $b.WaitForExit()
"full exit=$($a.ExitCode)" | Tee-Object -FilePath $out -Append; Get-Content (Join-Path $root 'c-full.txt'), (Join-Path $root 'c-full-err.txt') | Tee-Object -FilePath $out -Append
"diff exit=$($b.ExitCode)" | Tee-Object -FilePath $out -Append; Get-Content (Join-Path $root 'c-diff.txt'), (Join-Path $root 'c-diff-err.txt') | Tee-Object -FilePath $out -Append
Note "backup folder after step 4"
Get-ChildItem $bk -Recurse | Select-Object Name, Length | Format-Table -AutoSize | Out-String | Tee-Object -FilePath $out -Append
Note "5) Restore-HccDrill into HccTest_drill_* (TrustServerCertificate)"
$scratchName = 'HccTest_drill_' + (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ops 'Restore-HccDrill.ps1') -Database $Database -BackupDirectory $bk -ScratchDirectory $scr -ScratchName $scratchName -BackupKeyFile $keyFile -TrustServerCertificate *>&1 | Tee-Object -FilePath $out -Append
"exit=$LASTEXITCODE" | Tee-Object -FilePath $out -Append
Note "drill-history.jsonl"
Get-Content (Join-Path $bk 'Logs\drill-history.jsonl') -ErrorAction SilentlyContinue | Tee-Object -FilePath $out -Append
Note "6) Restore drill with a DIFFERENT backup key (replacement PC without the offline copy)"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ops 'New-HccBackupKey.ps1') -OfflineCopyPath (Join-Path $off2 'key2.txt') -KeyFile (Join-Path $keys2 'k2.txt') -ReaderAccount $me *>&1 | Out-Null
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ops 'Restore-HccDrill.ps1') -Database $Database -BackupDirectory $bk -ScratchDirectory $scr -ScratchName ($scratchName + 'b') -BackupKeyFile (Join-Path $keys2 'k2.txt') -TrustServerCertificate *>&1 | Tee-Object -FilePath $out -Append
"exit=$LASTEXITCODE" | Tee-Object -FilePath $out -Append
Note "7) Import the offline copy of the right key into a new key file (replacement PC path), then drill again"
$keys3 = Join-Path $root 'keys3'; New-Item -ItemType Directory $keys3 | Out-Null
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ops 'New-HccBackupKey.ps1') -ImportOfflineCopy (Join-Path $off 'key-offline.txt') -KeyFile (Join-Path $keys3 'k3.txt') -ReaderAccount $me *>&1 | Tee-Object -FilePath $out -Append
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ops 'Restore-HccDrill.ps1') -Database $Database -BackupDirectory $bk -ScratchDirectory $scr -ScratchName ($scratchName + 'c') -BackupKeyFile (Join-Path $keys3 'k3.txt') -TrustServerCertificate *>&1 | Tee-Object -FilePath $out -Append
"exit=$LASTEXITCODE" | Tee-Object -FilePath $out -Append
Note "backup log"
Get-Content (Join-Path $bk 'Logs\*.log') -ErrorAction SilentlyContinue | Tee-Object -FilePath $out -Append
Note "remaining HccTest databases"
sqlcmd -S .\SQLEXPRESS -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT name FROM sys.databases WHERE name LIKE 'HccTest%'" | Tee-Object -FilePath $out -Append
Remove-Item -Recurse -Force $root
"throw-away folder $root removed: $(-not (Test-Path $root))" | Tee-Object -FilePath $out -Append
