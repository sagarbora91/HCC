<#
.SYNOPSIS
  Reruns only the failed parts named in the last gate report for the wave (05 Delivery Evidence\Gates\<Wave>-gate-*.md), after a fix.
  Port of ETP's Rerun-Gate. Writes a new report; steps that passed before are CARRIED only when HEAD is still the reported commit.
  If HEAD moved (a fix commit), the earlier passes are STALE: the result is PARTIAL (exit 2) and the full Start-Gate.ps1 is still required.
  Exit 0 = PASS (same commit, everything green), 1 = FAIL, 2 = PARTIAL.

.EXAMPLE
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\acc1\tools\Rerun-Gate.ps1" -Repo "E:\Projects\HCC\worktrees\acc1" -Wave acc1
  Add -NoPause to run without pausing lane builds (Start-Gate.ps1 is used otherwise).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Repo,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]*$')][string]$Wave,
    [string]$ExpectedBranch = '',
    [string]$BaseRef = 'main',
    [string]$LogRoot = '',
    [int]$WaitMinutes = 90,
    [string]$Report = '',
    [switch]$NoPause
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GateCommon.ps1')
$Repo = (Resolve-Path -LiteralPath $Repo).Path.TrimEnd('\')
if ($Report) { $reportPath = (Resolve-Path -LiteralPath $Report).Path }
else {
    $latest = Get-LatestGateReport $Repo $Wave
    if (-not $latest) { Write-Host "No gate report for wave '$Wave' in $Repo\05 Delivery Evidence\Gates. Run Start-Gate.ps1 first."; exit 1 }
    $reportPath = $latest.FullName
}
$prev = Read-GateReportState $reportPath
$failed = @($prev.steps.PSObject.Properties | Where-Object { $_.Value.status -in @('FAIL', 'SKIPPED', 'NOT-RUN', 'STALE') } | ForEach-Object { $_.Name })
Write-Host "Last report: $reportPath  (result $($prev.result), commit $($prev.commit.Substring(0, 8)))"
if (-not $failed.Count) { Write-Host 'No failed step in that report; nothing to rerun. Run the full gate if HEAD changed.'; exit $(if ($prev.result -eq 'PASS') { 0 } else { 1 }) }
Write-Host "Rerunning only: $($failed -join ', ')"
$params = @{ Repo = $Repo; Wave = $Wave; BaseRef = $BaseRef; WaitMinutes = $WaitMinutes; Only = $failed; CarryFrom = $reportPath }
if ($ExpectedBranch) { $params.ExpectedBranch = $ExpectedBranch }
if ($LogRoot) { $params.LogRoot = $LogRoot }
$target = if ($NoPause) { Join-Path $PSScriptRoot 'Run-Gate.ps1' } else { Join-Path $PSScriptRoot 'Start-Gate.ps1' }
& $target @params
exit $LASTEXITCODE
