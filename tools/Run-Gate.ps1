<#
.SYNOPSIS
  The HCC wave gate (ported from ETP's Run-Gate / Start-Gate family, DEC-154). Runs both suites and the checks on ONE worktree, in order,
  every heavy step through tools\Invoke-Serialized.ps1 with label gate-<Wave>, and writes
  05 Delivery Evidence\Gates\<Wave>-gate-<yyyyMMdd-HHmm>.md (PASS / FAIL, every failing test name, durations, the commit).
  Exit 0 = PASS, 1 = FAIL, 2 = partial (a -Only rerun passed but the earlier passes belong to another commit: run the full gate).

.EXAMPLE
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\acc1\tools\Start-Gate.ps1" -Repo "E:\Projects\HCC\worktrees\acc1" -Wave acc1
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\acc1-tools\tools\Run-Gate.ps1" -Repo "E:\Projects\HCC\worktrees\acc1-tools" -Wave acc1-dryrun -ExpectedBranch claude/acc1-tools

  Steps (names for -Only): tsc, npm-test, npm-build, dotnet-build, dotnet-test, migrations-lock, secret-scan.
  Logs: <Repo>\.scratch\gate\<Wave>-<timestamp>\ (uncommitted; override with -LogRoot). Nothing is deleted anywhere.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Repo,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]*$')][string]$Wave,
    [string]$ExpectedBranch = '',                 # default claude/<Wave>
    [string]$BaseRef = 'main',                    # the secret scan covers merge-base(BaseRef, HEAD)..HEAD
    [string]$LogRoot = '',                        # default <Repo>\.scratch\gate
    [int]$WaitMinutes = 90,                       # lock wait per step
    [string[]]$Only = @(),                        # rerun only these steps (Rerun-Gate.ps1 sets this)
    [string]$CarryFrom = ''                       # report whose PASSED steps are carried when -Only is used
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GateCommon.ps1')

$Repo = (Resolve-Path -LiteralPath $Repo).Path.TrimEnd('\')
if (-not $ExpectedBranch) { $ExpectedBranch = "claude/$Wave" }
$label = "gate-$Wave"
$stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$gateStart = Get-Date
if (-not $LogRoot) { $LogRoot = Join-Path $Repo '.scratch\gate' }
$logDir = Join-Path $LogRoot "$Wave-$stamp"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$proto = Join-Path $Repo '03 Prototype'
$prod = Join-Path $Repo '08 Production'
$reportDir = Join-Path $Repo '05 Delivery Evidence\Gates'
$reportPath = Join-Path $reportDir "$Wave-gate-$stamp.md"
$allSteps = @('tsc', 'npm-test', 'npm-build', 'dotnet-build', 'dotnet-test', 'migrations-lock', 'secret-scan')
foreach ($o in $Only) { if ($o -notin $allSteps) { throw "Unknown step '$o'. Steps: $($allSteps -join ', ')" } }
$selected = if ($Only.Count) { $allSteps | Where-Object { $_ -in $Only } } else { $allSteps }

$state = [ordered]@{
    wave = $Wave; branch = ''; commit = ''; commitShort = ''; subject = ''; base = $BaseRef; baseCommit = ''; started = $gateStart.ToString('s')
    mode = $(if ($Only.Count) { "rerun of: $($Only -join ', ')" } else { 'full' }); carriedFrom = ''; logDir = $logDir
    result = 'FAIL'; note = @(); steps = [ordered]@{}; npm = $null; dotnet = $null; secretScan = $null
}
function Set-Step($name, $status, $secs, $exit, $command, $note = '') {
    $state.steps[$name] = [ordered]@{ status = $status; seconds = $secs; exit = $exit; command = $command; note = $note }
}

# ------------------------------------------------------------------------------------------------ 0. worktree checks
$stopped = $null
try {
    if (-not (Test-Path -LiteralPath (Join-Path $Repo '.git'))) { throw "$Repo is not a git worktree." }
    $state.branch = (Get-GateGit $Repo @('rev-parse', '--abbrev-ref', 'HEAD'))[0].Trim()
    $state.commit = (Get-GateGit $Repo @('rev-parse', 'HEAD'))[0].Trim()
    $state.commitShort = $state.commit.Substring(0, 8)
    $state.subject = (Get-GateGit $Repo @('log', '-1', '--format=%s'))[0].Trim()
    if ($state.branch -ne $ExpectedBranch) { throw "Worktree is on branch '$($state.branch)', expected '$ExpectedBranch'." }
    $dirty = Get-DirtyPaths $Repo
    if ($dirty.Count) { throw "Worktree is not clean ($($dirty.Count) path(s)): $($dirty -join ' ; ')" }
    $state.baseCommit = (Get-GateGit $Repo @('merge-base', $BaseRef, 'HEAD'))[0].Trim()
    Write-Host "== Gate $Wave on $($state.branch) @ $($state.commitShort): $($state.subject)"
    Write-Host "== Logs: $logDir"
}
catch { $stopped = $_.Exception.Message; $state.note += "STOPPED before any step: $stopped"; Write-Host "STOPPED: $stopped" }

# carried steps from an earlier report (rerun)
$carry = $null; $staleCarry = $false
if (-not $stopped -and $Only.Count) {
    if (-not $CarryFrom) { $state.note += 'Partial run: -Only without -CarryFrom, the other steps are NOT-RUN, so this report can never be PASS.' }
    else {
        $carry = Read-GateReportState $CarryFrom
        $state.carriedFrom = $CarryFrom
        if ($carry.commit -ne $state.commit) { $staleCarry = $true; $state.note += "HEAD is $($state.commitShort), the report being rerun was for $($carry.commit.Substring(0, 8)): steps not rerun are NOT valid for this commit; run the full gate before merging." }
    }
}

# ------------------------------------------------------------------------------------------------ steps
function Run-Heavy($name, $command, $dir, $body, $runMinutes) {
    Write-Host "-- $name : $command"
    $s = Invoke-GateStep -Name $name -Label $label -WorkingDirectory $dir -Body $body -LogDir $logDir -WaitMinutes $WaitMinutes -RunMinutes $runMinutes
    $status = if ($s.ExitCode -eq 0) { 'PASS' } else { 'FAIL' }
    $note = ''
    if ($s.TimedOut) { $note = "timed out (killed the process tree); lock wait + $runMinutes min cap" }
    elseif ($s.Busy) { $note = 'heavy-job lock still busy after the retries (exit 75); nothing ran' }
    Set-Step $name $status $s.Seconds $s.ExitCode $command $note
    Write-Host ("   {0}  exit {1}  {2} (wall {3})" -f $status, $s.ExitCode, (Format-Duration $s.Seconds), (Format-Duration $s.WallSeconds))
    return $s
}

if (-not $stopped) {
  try {
    foreach ($name in $allSteps) {
        if ($name -notin $selected) {
            # not rerun: carry a PASS from the report (valid only for the same commit), keep a FAIL as FAIL
            $prev = $carry.steps.$name
            if ($prev -and $prev.status -in @('PASS', 'CARRIED') -and -not $staleCarry) { Set-Step $name 'CARRIED' $prev.seconds $prev.exit $prev.command "passed in $(Split-Path $CarryFrom -Leaf), same commit" }
            elseif ($prev -and $prev.status -in @('PASS', 'CARRIED')) { Set-Step $name 'STALE' $prev.seconds $prev.exit $prev.command "passed for $($carry.commit.Substring(0, 8)), not rerun for this commit" }
            elseif ($prev) { Set-Step $name $prev.status $prev.seconds $prev.exit $prev.command "not rerun; $($prev.note)" }
            else { Set-Step $name 'NOT-RUN' 0 $null '' 'not rerun' }
            continue
        }
        switch ($name) {
            'tsc' {
                $s = Run-Heavy 'tsc' 'npx tsc --noEmit' $proto "npx tsc --noEmit *> '$logDir\tsc.log'" 15
                $errs = Get-TscErrors $s.LogPath
                $state.steps['tsc']['errors'] = $errs
                if ($s.ExitCode -eq 0 -and $errs.Count) { $state.steps['tsc'].status = 'FAIL' }
            }
            'npm-test' {
                $started = Get-Date
                $s = Run-Heavy 'npm-test' 'npm test' $proto "npm test *> '$logDir\npm-test.log'`n`$ec = `$LASTEXITCODE`nif (Test-Path 'evidence\domain-tests.json') { Copy-Item 'evidence\domain-tests.json' '$logDir\domain-tests.json' -Force }`nexit `$ec" 40
                $r = Read-NpmTestResults -LogPath $s.LogPath -EvidenceJson (Join-Path $logDir 'domain-tests.json') -Since $started.AddSeconds(-5)
                $state.npm = $r
                if ($s.ExitCode -eq 0 -and $r.failed.Count) { $state.steps['npm-test'].status = 'FAIL' }
                if ($s.ExitCode -ne 0 -and -not $r.failed.Count -and -not $s.Busy -and -not $s.TimedOut) { $state.steps['npm-test'].note = 'non-zero exit and no FAIL line found: the harness crashed or timed out; read npm-test.log' }
                if ($s.ExitCode -eq 0 -and $null -ne $r.total) { Write-Host "   npm tests: $($r.passed)/$($r.total)" }
            }
            'npm-build' { $null = Run-Heavy 'npm-build' 'npm run build' $proto "npm run build *> '$logDir\npm-build.log'" 20 }
            'dotnet-build' {
                $s = Run-Heavy 'dotnet-build' 'dotnet build Hcc.slnx' $prod "dotnet build Hcc.slnx *> '$logDir\dotnet-build.log'" 40
                $state.steps['dotnet-build']['errors'] = @(Get-DotnetBuildErrors $s.LogPath)
            }
            'dotnet-test' {
                if ($state.steps['dotnet-build'].status -in @('FAIL', 'SKIPPED')) {
                    Set-Step 'dotnet-test' 'SKIPPED' 0 $null 'dotnet test Hcc.slnx -maxcpucount:1 --logger trx' 'skipped because dotnet build did not pass (counts as FAIL)'
                    Write-Host '-- dotnet-test : SKIPPED (dotnet build did not pass)'
                    break
                }
                $trx = Join-Path $logDir 'trx'
                New-Item -ItemType Directory -Force -Path $trx | Out-Null
                $s = Run-Heavy 'dotnet-test' 'dotnet test Hcc.slnx -maxcpucount:1 --logger trx' $prod "dotnet test Hcc.slnx -maxcpucount:1 --logger trx --results-directory '$trx' *> '$logDir\dotnet-test.log'" 240
                $t = Read-TrxResults -TrxDir $trx
                $expected = @(Get-ExpectedTestProjects $prod)
                $t['expectedProjects'] = $expected.Count
                $t['consoleFailedLines'] = @(Get-DotnetFailedLines $s.LogPath)
                $state.dotnet = $t
                $st = $state.steps['dotnet-test']
                if ($s.ExitCode -eq 0 -and $t.failedCount -gt 0) { $st.status = 'FAIL' }
                if ($s.ExitCode -eq 0 -and $t.files -lt $expected.Count) { $st.status = 'FAIL'; $st.note = "only $($t.files) trx file(s) for $($expected.Count) test projects: some project produced no results" }
                if ($s.ExitCode -ne 0 -and $t.failedCount -eq 0 -and -not $s.Busy -and -not $s.TimedOut) { $st.note = 'non-zero exit and no failing test in the trx files: build/host crash or run aborted; read dotnet-test.log' }
                Write-Host "   server tests: $($t.passed)/$($t.total) passed, $($t.failedCount) failed, $($t.skipped) skipped, $($t.files) trx file(s) of $($expected.Count) projects"
            }
            'migrations-lock' {
                $s = Run-Heavy 'migrations-lock' 'node tools/migrations-lock.mjs' $prod "node tools/migrations-lock.mjs *> '$logDir\migrations-lock.log'" 5
                $state.steps['migrations-lock']['output'] = @(if (Test-Path $s.LogPath) { Get-Content -LiteralPath $s.LogPath })
            }
            'secret-scan' {
                Write-Host '-- secret-scan : diff since merge-base with main'
                $sw = [Diagnostics.Stopwatch]::StartNew()
                try {
                    $ss = Invoke-SecretScan -Repo $Repo -BaseRef $BaseRef -AllowlistPath (Join-Path $PSScriptRoot 'secret-scan-allow.txt') -WorkDir $logDir
                    $state.secretScan = $ss
                    Set-Step 'secret-scan' $(if ($ss.ok) { 'PASS' } else { 'FAIL' }) ([int]$sw.Elapsed.TotalSeconds) $(if ($ss.ok) { 0 } else { 1 }) "secret scan of $($ss.base.Substring(0, 8))..HEAD" "$($ss.scannedLines) added lines in $($ss.scannedFiles) files, $($ss.allowed) allowlisted, $($ss.findings.Count) finding(s)"
                } catch { Set-Step 'secret-scan' 'FAIL' ([int]$sw.Elapsed.TotalSeconds) 1 'secret scan' "scan error: $($_.Exception.Message)" }
                Write-Host "   $($state.steps['secret-scan'].status): $($state.steps['secret-scan'].note)"
            }
        }
    }
  }
  catch { $stopped = $_.Exception.Message; $state.note += "STOPPED: $stopped"; Write-Host "STOPPED: $stopped" }
}

# ------------------------------------------------------------------------------------------------ verdict
$partial = $false
if ($stopped) { $state.result = 'FAIL' }
else {
    $bad = @($state.steps.Keys | Where-Object { $state.steps[$_].status -in @('FAIL', 'SKIPPED', 'NOT-RUN') })
    $stale = @($state.steps.Keys | Where-Object { $state.steps[$_].status -eq 'STALE' })
    if ($bad.Count) { $state.result = 'FAIL' }
    elseif ($stale.Count) { $state.result = 'PARTIAL'; $partial = $true }
    else { $state.result = 'PASS' }
    try {
        $headAfter = (Get-GateGit $Repo @('rev-parse', 'HEAD'))[0].Trim()
        if ($headAfter -ne $state.commit) { $state.result = 'FAIL'; $state.note += "HEAD moved during the gate ($($state.commitShort) -> $($headAfter.Substring(0, 8))): the result belongs to no single commit." }
        $after = Get-DirtyPaths $Repo
        if ($after.Count) { $state.note += "Worktree dirty after the run (warning, not a failure): $($after -join ' ; ')" }
    } catch { $state.note += "Post-run git check failed: $($_.Exception.Message)" }
}
$gateEnd = Get-Date
$state.finished = $gateEnd.ToString('s')
$state.totalSeconds = [int]($gateEnd - $gateStart).TotalSeconds

# ------------------------------------------------------------------------------------------------ report
$md = New-Object System.Collections.Generic.List[string]
function L([string]$t = '') { $md.Add($t) }
$headline = switch ($state.result) { 'PASS' { 'PASS' } 'PARTIAL' { 'PARTIAL (the failed parts pass now; the full gate is still required)' } default { 'FAIL' } }
L "# Gate report: $Wave"
L ''
L "**Result: $headline**"
L ''
L '| | |'
L '|---|---|'
L "| Wave | $Wave |"
L "| Branch | $($state.branch) |"
L "| Commit | ``$($state.commit)`` ($($state.subject)) |"
L "| Base for the secret scan | $BaseRef (merge-base ``$($state.baseCommit)``) |"
L "| Mode | $($state.mode) |"
if ($state.carriedFrom) { L "| Carried steps from | $(Split-Path $state.carriedFrom -Leaf) |" }
L "| Started / finished | $($state.started) / $($state.finished) |"
L "| Total duration | $(Format-Duration $state.totalSeconds) |"
L "| Machine | $env:COMPUTERNAME |"
L "| Logs (uncommitted) | ``$logDir`` |"
L ''
foreach ($n in $state.note) { L "> $n"; L '' }
L '## Steps'
L ''
L '| Step | Command | Result | Duration | Note |'
L '|---|---|---|---|---|'
foreach ($n in $allSteps) {
    $st = $state.steps[$n]
    if ($null -eq $st) { L "| $n | | not reached | | |"; continue }
    L "| $n | ``$($st.command)`` | **$($st.status)** | $(Format-Duration $st.seconds) | $($st.note) |"
}
L ''
L '## Test counts'
L ''
if ($state.npm) { L "- Prototype (npm test): $($state.npm.passed) / $($state.npm.total) passed, $(@($state.npm.failed).Count) failed (source: $($state.npm.source))" } else { L '- Prototype (npm test): not run in this report' }
if ($state.dotnet) { L "- Server (dotnet test): $($state.dotnet.passed) / $($state.dotnet.total) passed, $($state.dotnet.failedCount) failed, $($state.dotnet.skipped) skipped; $($state.dotnet.files) trx file(s) for $($state.dotnet.expectedProjects) test projects" } else { L '- Server (dotnet test): not run in this report' }
L ''

$anyFail = $false
function Section([string]$title) { L "## $title"; L '' }
foreach ($n in $allSteps) {
    $st = $state.steps[$n]
    if ($null -eq $st -or $st.status -notin @('FAIL', 'SKIPPED')) { continue }
    $anyFail = $true
    Section "Failure: $n"
    L "Command: ``$($st.command)``  exit $($st.exit)  $($st.note)"
    L ''
    switch ($n) {
        'tsc' { if ($st.errors) { L '```'; $st.errors | ForEach-Object { L $_ }; L '```' } else { L "No ``error TS`` line found; read ``tsc.log``." } }
        'npm-test' {
            if ($state.npm -and $state.npm.failed.Count) {
                L "Failing prototype tests ($($state.npm.failed.Count)):"; L ''
                foreach ($f in $state.npm.failed) { L "- ``$($f.name)``"; if ($f.error) { L "  - $($f.error -replace '\r?\n', ' | ')" } }
            } else { L 'No failing test name could be parsed; read ``npm-test.log``.' }
        }
        'dotnet-build' { if ($st.errors) { L '```'; $st.errors | ForEach-Object { L $_ }; L '```' } else { L 'No error line parsed; read ``dotnet-build.log``.' } }
        'dotnet-test' {
            if ($state.dotnet -and $state.dotnet.failed.Count) {
                L "Failing server tests ($($state.dotnet.failed.Count)):"; L ''
                foreach ($f in $state.dotnet.failed) { L "- ``$($f.name)``  ($($f.project); $($f.outcome))"; if ($f.error) { L "  - $($f.error)" } }
                L ''
            }
            if ($state.dotnet -and $state.dotnet.consoleFailedLines.Count) {
                L 'Failure lines from the console log (names the trx may lack):'; L ''; L '```'; $state.dotnet.consoleFailedLines | ForEach-Object { L $_ }; L '```'
            }
        }
        'migrations-lock' { L '```'; $st.output | ForEach-Object { L $_ }; L '```' }
        'secret-scan' {
            if ($state.secretScan -and $state.secretScan.findings.Count) {
                L '| Rule | File | Line | Text (value masked) |'; L '|---|---|---|---|'
                foreach ($f in $state.secretScan.findings) { L "| $($f.rule) | ``$($f.file)`` | $($f.line) | ``$(($f.text -replace '\|', '\|'))`` |" }
                L ''; L 'Real secret: remove it (and rotate it). Fixture / test value: add a line to ``tools\secret-scan-allow.txt`` or the marker ``secret-scan:allow`` on the line.'
            }
        }
    }
    L ''
}
if (-not $anyFail -and $state.result -ne 'PASS' -and -not $partial) { Section 'Why not PASS'; L ($state.note -join ' '); L '' }
if ($state.dotnet -and $state.dotnet.perFile.Count) {
    Section 'Server test files (trx)'
    L '| trx | total | passed | failed | skipped |'; L '|---|---|---|---|---|'
    foreach ($f in $state.dotnet.perFile) { L "| $($f.file) | $($f.total) | $($f.passed) | $($f.failed) | $($f.skipped) |" }
    L ''
}
Section 'Machine-readable state (used by Rerun-Gate.ps1)'
L '```json gate-state'
L (($state | ConvertTo-Json -Depth 8 -Compress:$false))
L '```'

New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
Set-Content -LiteralPath $reportPath -Value ($md -join "`r`n") -Encoding UTF8
Set-Content -LiteralPath (Join-Path $logDir 'report-path.txt') -Value $reportPath -Encoding UTF8
Write-Host ''
Write-Host "== GATE $($state.result)  $Wave @ $($state.commitShort)  $(Format-Duration $state.totalSeconds)"
Write-Host "== Report: $reportPath"
Write-Output "gate: $Wave $($state.result) commit=$($state.commitShort) report=$reportPath"
if ($state.result -eq 'PASS') { exit 0 } elseif ($state.result -eq 'PARTIAL') { exit 2 } else { exit 1 }
