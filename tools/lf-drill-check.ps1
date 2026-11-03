<#
.SYNOPSIS
  LF documents-database drill check (wave F3, LF-docdb). NOT EXECUTED in the LF build environment (Linux, no Windows PowerShell / SQL Express): written for the hotel PC.
.DESCRIPTION
  Runs, in order, the plan-only (-WhatIf) forms of the three database scripts so the owner can read what they will do to BOTH databases (Hcc and HccDocuments), then the
  deployment self-test, and finally (only with -Live) the real restore drill. Nothing is changed without -Live. Exit code 0 = every step passed, 1 = a step failed.
    1. Initialize-HccDatabase.ps1 -WhatIf   (creates Hcc and, with -DocumentsDatabase, HccDocuments)
    2. Backup-HccDatabase.ps1     -WhatIf   (both databases, one backup file and one plat.backup_run row each)
    3. Restore-HccDrill.ps1       -WhatIf   (scratch restore of both, CHECKDB, row counts, guest-ID check, reference check)
    4. tests\Test-HccDeployment.ps1         (the deployment self-test)
    5. with -Live: Restore-HccDrill.ps1 for real; the drill rows must show a pass for 'main' and 'documents' and 0 dangling / 0 orphan objects.
  Run it from the repository root:  powershell -File tools\lf-drill-check.ps1 [-SqlInstance .\SQLEXPRESS] [-Live]
#>
[CmdletBinding()]
param(
  [string]$SqlInstance = '.\SQLEXPRESS',
  [string]$Database = 'Hcc',
  [string]$DocumentsDatabase = 'HccDocuments',
  [string]$ServiceAccount = 'NT SERVICE\HccServer',
  [string]$BackupDirectory = 'D:\HccBackups',   # plan-only steps never create it; with -Live use the real backup folder
  [switch]$Live
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$win = Join-Path $root '08 Production\deployment\windows'
$results = New-Object System.Collections.Generic.List[object]

function Step([string]$name, [scriptblock]$body) {
  Write-Host "== $name"
  $ok = $true
  try { & $body | Out-Host; if ($LASTEXITCODE -ne $null -and $LASTEXITCODE -ne 0) { $ok = $false } }
  catch { Write-Host "   FAILED: $($_.Exception.Message)"; $ok = $false }
  $results.Add([pscustomobject]@{ Step = $name; Passed = $ok })
}

Step 'Initialize-HccDatabase -WhatIf (both databases)' { & (Join-Path $win 'Initialize-HccDatabase.ps1') -SqlInstance $SqlInstance -Database $Database -DocumentsDatabase $DocumentsDatabase -ServiceAccount $ServiceAccount -BackupDirectory $BackupDirectory -WhatIf }
Step 'Backup-HccDatabase -WhatIf (both databases)'     { & (Join-Path $win 'Backup-HccDatabase.ps1') -Type Full -SqlInstance $SqlInstance -BackupDirectory $BackupDirectory -WhatIf }   # no -Database = both databases
Step 'Restore-HccDrill -WhatIf (both databases)'       { & (Join-Path $win 'Restore-HccDrill.ps1') -SqlInstance $SqlInstance -Database $Database -DocumentsDatabase $DocumentsDatabase -BackupDirectory $BackupDirectory -WhatIf }
Step 'Test-HccDeployment'                              { & (Join-Path $win 'tests\Test-HccDeployment.ps1') }
if ($Live) {
  Step 'Restore-HccDrill (live: scratch restore of both databases)' { & (Join-Path $win 'Restore-HccDrill.ps1') -SqlInstance $SqlInstance -Database $Database -DocumentsDatabase $DocumentsDatabase -BackupDirectory $BackupDirectory }
  # The drill script writes one plat.backup_run row per database and exits 1 on a CHECKDB / row-count / reference-check failure, so its exit code is the verdict;
  # read the rows on System health (databases block) or in plat.backup_run for the details.
}
$results | Format-Table -AutoSize | Out-Host
if (($results | Where-Object { -not $_.Passed }).Count -gt 0) { Write-Host 'LF drill check: FAILED'; exit 1 }
Write-Host 'LF drill check: passed'
exit 0
