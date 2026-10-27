<#
.SYNOPSIS
  Starts the HCC wave gate for the coordinator: writes E:\Projects\HCC\worktrees\_tools\PAUSE-LANE-BUILDS (one line, the gate label
  gate-<Wave>) so every other agent's Invoke-Serialized.ps1 call waits (exit 75 = busy, retry) instead of taking the lock, runs
  Run-Gate.ps1, and removes the pause file in a finally. It deletes nothing else. Port of ETP's Start-Gate.ps1 (DEC-154).

.EXAMPLE
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\acc1\tools\Start-Gate.ps1" -Repo "E:\Projects\HCC\worktrees\acc1" -Wave acc1
  Exit code = Run-Gate.ps1's (0 PASS, 1 FAIL, 2 partial rerun). A running lane step finishes first; the gate takes the lock next.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Repo,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]*$')][string]$Wave,
    [string]$ExpectedBranch = '',
    [string]$BaseRef = 'main',
    [string]$LogRoot = '',
    [int]$WaitMinutes = 90,
    [string[]]$Only = @(),
    [string]$CarryFrom = ''
)
$ErrorActionPreference = 'Stop'
$toolsDir = 'E:\Projects\HCC\worktrees\_tools'
$pause = Join-Path $toolsDir 'PAUSE-LANE-BUILDS'
$label = "gate-$Wave"
if (-not (Test-Path -LiteralPath $toolsDir)) { New-Item -ItemType Directory -Path $toolsDir | Out-Null }
if (Test-Path -LiteralPath $pause) {
    $holder = (@(Get-Content -LiteralPath $pause -ErrorAction SilentlyContinue) -join ' ').Trim()
    if ($holder -ne $label) { Write-Host "Another priority job holds $pause ($holder). Not starting the gate."; exit 1 }
}
$params = @{ Repo = $Repo; Wave = $Wave; BaseRef = $BaseRef; WaitMinutes = $WaitMinutes }
if ($ExpectedBranch) { $params.ExpectedBranch = $ExpectedBranch }
if ($LogRoot) { $params.LogRoot = $LogRoot }
if ($Only.Count) { $params.Only = $Only }
if ($CarryFrom) { $params.CarryFrom = $CarryFrom }
$code = 1
try {
    Set-Content -LiteralPath $pause -Value $label -Encoding ASCII
    Write-Host "Lane builds paused ($pause = $label)."
    & (Join-Path $PSScriptRoot 'Run-Gate.ps1') @params
    $code = $LASTEXITCODE
}
finally {
    # Only our own pause file, and only if it is still ours.
    if ((Test-Path -LiteralPath $pause) -and ((@(Get-Content -LiteralPath $pause -ErrorAction SilentlyContinue) -join ' ').Trim() -eq $label)) {
        Remove-Item -LiteralPath $pause -ErrorAction SilentlyContinue
        Write-Host 'Lane builds resumed (pause file removed).'
    }
}
exit $code
