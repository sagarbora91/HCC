# Shared helpers for Run-Gate.ps1 / Start-Gate.ps1 / Rerun-Gate.ps1 (dot-source this file). HCC, ACC-1 tools lane.
# Ported in spirit from ETP's GateCommon.ps1 (E:\ETP\Migration 2026-10-04): every step writes to files (never through a pipe, a piped
# `dotnet test` once left an ETP gate idle for 30 minutes), has a hard timeout, and the whole log is kept.
# Nothing here deletes anything. Safe under `pwsh -NoProfile`.


$script:HccToolsDir = 'E:\Projects\HCC\worktrees\_tools'

function Format-Duration([double]$Seconds) {
    $s = [int][Math]::Round($Seconds)
    if ($s -ge 3600) { return ('{0}h {1:00}m {2:00}s' -f [int][Math]::Floor($s / 3600), [int][Math]::Floor(($s % 3600) / 60), ($s % 60)) }
    if ($s -ge 60) { return ('{0}m {1:00}s' -f [int][Math]::Floor($s / 60), ($s % 60)) }
    return "${s}s"
}

function Get-GateGit([string]$Repo, [string[]]$GitArgs) {
    # stdout lines of `git -C <repo> ...`; throws when git fails.
    $out = & git -C $Repo @GitArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($GitArgs -join ' ') failed in ${Repo}: $out" }
    return ,@($out | ForEach-Object { "$_" })
}

# Paths that never count as "dirty": scratch logs, and gate reports themselves (a report is written into the gated worktree).
$script:GateIgnoredPrefixes = @('.scratch/', '05 Delivery Evidence/Gates/')

function Get-DirtyPaths([string]$Repo) {
    $lines = @(& git -C $Repo status --porcelain=v1 --untracked-files=all 2>&1 | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ne 0) { throw "git status failed in ${Repo}: $lines" }
    $dirty = @()
    foreach ($l in $lines) {
        if ($l.Length -lt 4) { continue }
        $p = $l.Substring(3).Trim('"')
        if ($p -like '* -> *') { $p = ($p -split ' -> ')[-1].Trim('"') }
        $ignored = $false
        foreach ($pre in $script:GateIgnoredPrefixes) { if ($p.StartsWith($pre)) { $ignored = $true; break } }
        if (-not $ignored) { $dirty += $l }
    }
    return $dirty
}

# ---------------------------------------------------------------------------------------------------------------- one gated step
function Invoke-GateStep {
    <#
      Runs one heavy command through Invoke-Serialized.ps1 (label gate-<wave>). The command text goes into a step script in the log
      folder; stdout+stderr of the real command go to <name>.log; the step script records its own run time in <name>.secs.
      Exit 75 (lock busy / lane builds paused) is retried. A hard timeout kills the whole process tree.
    #>
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$Body,           # PowerShell lines; the LAST native command's exit code is the step's
        [Parameter(Mandatory)][string]$LogDir,
        [int]$WaitMinutes = 90,
        [int]$RunMinutes = 30,
        [int]$BusyRetries = 6
    )
    $scriptPath = Join-Path $LogDir "step-$Name.ps1"
    $logPath = Join-Path $LogDir "$Name.log"
    $secsPath = Join-Path $LogDir "$Name.secs"
    $wrapPath = Join-Path $LogDir "$Name.serializer.log"
    $script = @"
`$ErrorActionPreference = 'Continue'
`$sw = [System.Diagnostics.Stopwatch]::StartNew()
Set-Location -LiteralPath '$WorkingDirectory'
$Body
`$code = `$LASTEXITCODE
if (`$null -eq `$code) { `$code = 0 }
Set-Content -LiteralPath '$secsPath' -Value ([int]`$sw.Elapsed.TotalSeconds)
exit `$code
"@
    Set-Content -LiteralPath $scriptPath -Value $script -Encoding UTF8
    $serializer = Join-Path $PSScriptRoot 'Invoke-Serialized.ps1'
    $pwsh = (Get-Process -Id $PID).Path
    $command = "& '$scriptPath'; exit `$LASTEXITCODE"
    $argText = "-NoProfile -File `"$serializer`" -WorkingDirectory `"$WorkingDirectory`" -Label $Label -WaitMinutes $WaitMinutes -Command `"$command`""
    $timeoutMs = [int](($WaitMinutes + $RunMinutes + 3) * 60 * 1000)
    $started = Get-Date
    $exit = 1; $timedOut = $false; $attempt = 0
    while ($true) {
        $attempt++
        $p = Start-Process -FilePath $pwsh -ArgumentList $argText -NoNewWindow -PassThru -RedirectStandardOutput $wrapPath -RedirectStandardError ($wrapPath + '.err')
        $null = $p.Handle
        if (-not $p.WaitForExit($timeoutMs)) {
            $timedOut = $true
            & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null
            $exit = 124
            break
        }
        $p.WaitForExit()
        $exit = $p.ExitCode
        if ($exit -eq 75 -and $attempt -le $BusyRetries) { Start-Sleep -Seconds 30; continue }
        break
    }
    $secs = $null
    if (Test-Path -LiteralPath $secsPath) { try { $secs = [int](Get-Content -LiteralPath $secsPath -Raw).Trim() } catch { $secs = $null } }
    if ($null -eq $secs) { $secs = [int]((Get-Date) - $started).TotalSeconds }
    return [pscustomobject]@{
        Name = $Name; ExitCode = $exit; TimedOut = $timedOut; Seconds = $secs
        WallSeconds = [int]((Get-Date) - $started).TotalSeconds
        Busy = ($exit -eq 75); LogPath = $logPath; Attempts = $attempt
    }
}

# ---------------------------------------------------------------------------------------------------------------- log parsers
function Get-TscErrors([string]$LogPath) {
    if (-not (Test-Path -LiteralPath $LogPath)) { return @() }
    return @(Get-Content -LiteralPath $LogPath | Where-Object { $_ -match '\berror TS\d+' } | ForEach-Object { $_.Trim() })
}

function Get-DotnetBuildErrors([string]$LogPath) {
    if (-not (Test-Path -LiteralPath $LogPath)) { return @() }
    $seen = New-Object 'System.Collections.Generic.HashSet[string]'
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($l in Get-Content -LiteralPath $LogPath) {
        if ($l -match ': (error|warning as error) [A-Za-z]*\d+' -or $l -match '^\s*error [A-Za-z]*\d+') {
            $t = ($l -replace '\s+\[[^\]]+\]\s*$', '').Trim()
            if ($seen.Add($t)) { $out.Add($t) }
        }
    }
    return ,$out.ToArray()
}

function Read-NpmTestResults {
    # Prefers evidence/domain-tests.json (names + errors separately) when it is newer than $Since; falls back to the FAIL lines of the log.
    param([Parameter(Mandatory)][string]$LogPath, [string]$EvidenceJson, [datetime]$Since = [datetime]::MinValue)
    $r = [ordered]@{ total = $null; passed = $null; failed = @(); source = 'none' }
    if ($EvidenceJson -and (Test-Path -LiteralPath $EvidenceJson) -and ((Get-Item -LiteralPath $EvidenceJson).LastWriteTime -ge $Since)) {
        try {
            $j = Get-Content -LiteralPath $EvidenceJson -Raw | ConvertFrom-Json
            $r.total = [int]$j.total
            $r.passed = [int]$j.passed
            $r.failed = @($j.results | Where-Object { $_.status -ne 'passed' } | ForEach-Object { [ordered]@{ name = [string]$_.name; error = [string]$_.error } })
            $r.source = 'evidence/domain-tests.json'
        } catch { }
    }
    if ($r.source -eq 'none' -and (Test-Path -LiteralPath $LogPath)) {
        $lines = @(Get-Content -LiteralPath $LogPath)
        $r.failed = @($lines | Where-Object { $_ -match '^FAIL ' } | ForEach-Object { [ordered]@{ name = ($_ -replace '^FAIL ', ''); error = '' } })
        $pass = @($lines | Where-Object { $_ -match '^PASS ' }).Count
        $r.passed = $pass
        $r.total = $pass + $r.failed.Count
        $r.source = 'log (PASS / FAIL lines)'
        $sum = $lines | Where-Object { $_ -match '^(\d+)/(\d+) tests passed' } | Select-Object -Last 1
        if ($sum -and $sum -match '^(\d+)/(\d+)') { $r.passed = [int]$Matches[1]; $r.total = [int]$Matches[2] }
    }
    return $r
}

function Read-TrxResults {
    # Every *.trx under $TrxDir: totals and every non-passing test (name, project file, first lines of the message). Never truncates names.
    param([Parameter(Mandatory)][string]$TrxDir)
    $files = @()
    if (Test-Path -LiteralPath $TrxDir) { $files = @(Get-ChildItem -LiteralPath $TrxDir -Filter *.trx -File -Recurse) }
    $total = 0; $passed = 0; $failedCount = 0; $skipped = 0
    $failed = New-Object System.Collections.Generic.List[object]
    $perFile = New-Object System.Collections.Generic.List[object]
    foreach ($f in $files) {
        try { $xml = [xml](Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8) } catch {
            $failed.Add([ordered]@{ name = "(unreadable trx) $($f.Name)"; project = $f.Name; error = $_.Exception.Message }); continue
        }
        $results = @($xml.SelectNodes("//*[local-name()='UnitTestResult']"))
        $fTotal = 0; $fPassed = 0; $fFailed = 0; $fSkipped = 0
        foreach ($u in $results) {
            $fTotal++
            $o = [string]$u.outcome
            if ($o -eq 'Passed') { $fPassed++ }
            elseif ($o -in @('NotExecuted', 'Skipped')) { $fSkipped++ }
            elseif ($o -in @('Failed', 'Error', 'Timeout', 'Aborted', 'Disconnected')) {
                $fFailed++
                $msg = ''
                $mn = $u.SelectSingleNode(".//*[local-name()='ErrorInfo']/*[local-name()='Message']")
                if ($mn) { $msg = ([string]$mn.InnerText -split "`r?`n" | Select-Object -First 6) -join ' | ' }
                $proj = ''
                $tn = $u.SelectSingleNode("//*[local-name()='UnitTest'][@id='$($u.testId)']/*[local-name()='TestMethod']")
                if ($tn) { $proj = [string]$tn.className }
                $failed.Add([ordered]@{ name = [string]$u.testName; project = $f.Name; className = $proj; outcome = $o; error = $msg })
            }
        }
        $total += $fTotal; $passed += $fPassed; $failedCount += $fFailed; $skipped += $fSkipped
        $perFile.Add([ordered]@{ file = $f.Name; total = $fTotal; passed = $fPassed; failed = $fFailed; skipped = $fSkipped })
    }
    return [ordered]@{ files = $files.Count; total = $total; passed = $passed; failedCount = $failedCount; skipped = $skipped; failed = $failed.ToArray(); perFile = $perFile.ToArray() }
}

function Get-DotnetFailedLines([string]$LogPath) {
    # Supplement for a run with no (or partial) trx: console lines "  Failed <name> [..]" and run-level failure lines.
    if (-not (Test-Path -LiteralPath $LogPath)) { return @() }
    return @(Get-Content -LiteralPath $LogPath | Where-Object { $_ -match '^\s*Failed\s+\S' -or $_ -match 'Test Run Failed|test run was aborted|Test host process crashed|Process terminated|MSB\d+: ' } | ForEach-Object { $_.Trim() })
}

function Get-ExpectedTestProjects([string]$ProductionDir) {
    $slnx = Join-Path $ProductionDir 'Hcc.slnx'
    if (-not (Test-Path -LiteralPath $slnx)) { return @() }
    $x = [xml](Get-Content -LiteralPath $slnx -Raw)
    return @($x.SelectNodes('//Project/@Path') | ForEach-Object { $_.Value } | Where-Object { $_ -like 'tests/*Tests.csproj' })
}

# ---------------------------------------------------------------------------------------------------------------- secret scan
function Read-SecretAllowlist([string]$Path) {
    $rules = New-Object System.Collections.Generic.List[object]
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    foreach ($l in Get-Content -LiteralPath $Path) {
        $t = $l.Trim()
        if ($t -eq '' -or $t.StartsWith('#')) { continue }
        $parts = $t -split '\|', 3
        if ($parts.Count -lt 2) { continue }
        $rules.Add([pscustomobject]@{
            Rule = $parts[0].Trim()
            Path = [regex]::new($parts[1].Trim(), 'IgnoreCase')
            Text = $(if ($parts.Count -ge 3 -and $parts[2].Trim() -ne '') { [regex]::new($parts[2].Trim(), 'IgnoreCase') } else { $null })
        })
    }
    return ,@($rules.ToArray())
}

$script:SecretRules = @(
    @{ Name = 'private-key';       Pattern = '-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----' },
    @{ Name = 'connection-string'; Pattern = '(?i)\b(?:server|data source|host|user id|uid|initial catalog|database)\s*=[^"\r\n]*?;\s*[^"\r\n]*?\b(?:password|pwd)\s*=\s*(?<v>[^;"''\s{}<>%$]{3,})' },
    @{ Name = 'connection-string'; Pattern = '(?i)\b(?:password|pwd)\s*=\s*(?<v>[^;"''\s{}<>%$]{3,})\s*;[^"\r\n]*?\b(?:server|data source|host|user id|uid)\s*=' },
    @{ Name = 'credential-literal'; Pattern = '(?i)\b(?:password|passwd|pwd|secret|api[_-]?key|access[_-]?key|client[_-]?secret|auth[_-]?token|private[_-]?key)["'']?\s*[:=]\s*["''](?<v>[^"''\s${}<>%]{8,})["'']' },
    @{ Name = 'token';             Pattern = '\b(?<v>gh[pousr]_[A-Za-z0-9]{36,})\b' },
    @{ Name = 'token';             Pattern = '\b(?<v>github_pat_[A-Za-z0-9_]{50,})\b' },
    @{ Name = 'token';             Pattern = '\b(?<v>AKIA[0-9A-Z]{16})\b' },
    @{ Name = 'token';             Pattern = '\b(?<v>xox[abposr]-[A-Za-z0-9-]{10,})\b' },
    @{ Name = 'token';             Pattern = '\b(?<v>sk-ant-[A-Za-z0-9_-]{20,})\b' },
    @{ Name = 'token';             Pattern = '\b(?<v>sk-[A-Za-z0-9]{32,})\b' },
    @{ Name = 'token';             Pattern = '\b(?<v>AIza[0-9A-Za-z_-]{35})\b' },
    @{ Name = 'token';             Pattern = '\b(?<v>eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,})\b' },
    @{ Name = 'token';             Pattern = '(?i)\bBearer\s+(?<v>[A-Za-z0-9._~+/-]{24,}=*)' }
)

function Invoke-SecretScan {
    <#
      Scans the lines ADDED by $BaseRef..HEAD (git diff, unified=0). A line is skipped when it carries `secret-scan:allow` or matches a
      rule in the allowlist file (format: rule | path-regex | optional line-regex; `*` = any rule). Findings never contain the secret:
      the matched value is shown as its first 3 characters + ***. Returns @{ ok; base; findings[]; scannedLines; scannedFiles; allowed }.
    #>
    param([Parameter(Mandatory)][string]$Repo, [string]$BaseRef = 'main', [Parameter(Mandatory)][string]$AllowlistPath, [Parameter(Mandatory)][string]$WorkDir)
    $baseSha = (Get-GateGit $Repo @('merge-base', $BaseRef, 'HEAD'))[0].Trim()
    $diffFile = Join-Path $WorkDir 'secret-scan.diff'
    [System.IO.File]::WriteAllText($diffFile, '')   # an empty diff must not leave a stale file from an earlier scan
    & git -C $Repo diff -U0 --no-color --no-ext-diff --diff-filter=ACMR --no-renames $baseSha HEAD 2>$null | Set-Content -LiteralPath $diffFile -Encoding UTF8
    if ($LASTEXITCODE -ne 0) { throw "git diff $baseSha HEAD failed" }
    $allow = Read-SecretAllowlist $AllowlistPath
    $rules = $script:SecretRules | ForEach-Object { @{ Name = $_.Name; Re = [regex]::new($_.Pattern, 'Compiled') } }
    $skipFile = [regex]::new('(^|/)(package-lock\.json|migrations\.lock\.json|packages\.lock\.json)$|\.(png|jpg|jpeg|gif|ico|woff2?|ttf|pdf|zip|apk|keystore|jks)$', 'IgnoreCase')
    $findings = New-Object System.Collections.Generic.List[object]
    $file = ''; $lineNo = 0; $scannedLines = 0; $scannedFiles = New-Object 'System.Collections.Generic.HashSet[string]'; $allowed = 0; $skipThis = $false
    foreach ($l in [System.IO.File]::ReadLines($diffFile)) {
        if ($l.StartsWith('+++ ')) {
            $file = $l.Substring(4); if ($file.StartsWith('b/')) { $file = $file.Substring(2) }; $file = $file.Trim('"')
            $skipThis = $skipFile.IsMatch($file) -or $file -eq '/dev/null'
            if (-not $skipThis) { $null = $scannedFiles.Add($file) }
            continue
        }
        if ($l.StartsWith('--- ') -or $l.StartsWith('diff ') -or $l.StartsWith('index ')) { continue }
        if ($l.StartsWith('@@')) { if ($l -match '\+(\d+)') { $lineNo = [int]$Matches[1] - 1 }; continue }
        if (-not $l.StartsWith('+') -or $skipThis) { continue }
        $lineNo++
        $text = $l.Substring(1)
        $scannedLines++
        if ($text.Length -gt 4000) { $text = $text.Substring(0, 4000) }
        if ($text.Contains('secret-scan:allow')) { continue }
        foreach ($r in $rules) {
            $m = $r.Re.Match($text)
            if (-not $m.Success) { continue }
            $hit = $false
            foreach ($a in $allow) {
                if (($a.Rule -eq '*' -or $a.Rule -eq $r.Name) -and $a.Path.IsMatch($file) -and ($null -eq $a.Text -or $a.Text.IsMatch($text))) { $hit = $true; break }
            }
            if ($hit) { $allowed++; break }
            $val = if ($m.Groups['v'].Success) { $m.Groups['v'].Value } else { $m.Value }
            $masked = $val.Substring(0, [Math]::Min(3, $val.Length)) + '***'
            $ctx = $text.Replace($val, $masked).Trim()
            if ($ctx.Length -gt 160) { $ctx = $ctx.Substring(0, 160) + '...' }
            $findings.Add([ordered]@{ rule = $r.Name; file = $file; line = $lineNo; text = $ctx })
            break
        }
    }
    return [ordered]@{ ok = ($findings.Count -eq 0); base = $baseSha; findings = $findings.ToArray(); scannedLines = $scannedLines; scannedFiles = $scannedFiles.Count; allowed = $allowed }
}

# ---------------------------------------------------------------------------------------------------------------- report state
function Get-LatestGateReport([string]$Repo, [string]$Wave) {
    $dir = Join-Path $Repo '05 Delivery Evidence\Gates'
    if (-not (Test-Path -LiteralPath $dir)) { return $null }
    return Get-ChildItem -LiteralPath $dir -Filter "$Wave-gate-*.md" -File | Sort-Object Name | Select-Object -Last 1
}

function Read-GateReportState([string]$ReportPath) {
    $text = Get-Content -LiteralPath $ReportPath -Raw
    if ($text -notmatch '(?s)```json gate-state\r?\n(.*?)\r?\n```') { throw "No gate-state block in $ReportPath (not a report written by Run-Gate.ps1)." }
    return ($Matches[1] | ConvertFrom-Json)
}
