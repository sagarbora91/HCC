<#
.SYNOPSIS
  Cut-over dry run on THIS PC (run by the coordinator, never by a lane): extractor over the ten compacted files -> importer dry run ->
  05 Delivery Evidence\Legacy\LG-DryRun-<date>.md (section 6 comparison + 20-guest review list with names and phones MASKED).
.NOTES
  Importer call: POST /api/v1/commands/legacy.import (dryRun true) against a local server only; start it first and pass -Token (Owner bearer token).
  The result JSON is saved in the run folder. Only the masked report is written into the repository.
  The Access password is never handled here (extract.py reads Module1.bas).
#>
[CmdletBinding()]
param(
    [string]$Repo = 'E:\Projects\HCC\worktrees\lg',
    [string]$MdbDir = 'E:\Projects\HCC\Existing Software\DB-PROFILE\compacted',
    [string]$OutRoot = 'E:\Projects\HCC\Existing Software\LG-EXTRACT',
    [string]$RateCard = '',
    [string]$ApiBase = 'http://127.0.0.1:5080',
    [string]$Token = '',
    [string[]]$Parts = @('guests', 'history', 'cutover')
)
$ErrorActionPreference = 'Stop'
$stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$run = Join-Path $OutRoot $stamp
$evidence = Join-Path $Repo '05 Delivery Evidence\Legacy'
$report = Join-Path $evidence ('LG-DryRun-{0}.md' -f (Get-Date -Format 'yyyy-MM-dd'))
if ((Get-ChildItem -LiteralPath $MdbDir -Filter 'Lodge*.mdb' | Measure-Object).Count -ne 10) { throw "Expected ten Lodge*.mdb files in $MdbDir" }
$py = (Get-Command python -ErrorAction Stop).Source

function Mask-Name([string]$n) {
    ($n -split '\s+' | Where-Object { $_ } | ForEach-Object { if ($_.Length -le 1) { $_ } else { $_.Substring(0, 1) + ('*' * ($_.Length - 1)) } }) -join ' '
}
function Mask-Phone([string]$p) {
    $d = ($p -replace '\D', '')
    if ($d.Length -lt 6) { return ('*' * $d.Length) }
    $d.Substring(0, 2) + ('*' * ($d.Length - 5)) + $d.Substring($d.Length - 3)
}

$resultFile = Join-Path $run 'dryrun-result.json'
Push-Location $Repo
try {
    # 1. Extractor (repository root, absolute paths, python -I)
    $exArgs = @('-I', (Join-Path $Repo 'tools\legacy\extract.py'), '--mdb-dir', $MdbDir, '--out', $run)
    if ($RateCard) { $exArgs += @('--rate-card', $RateCard) }
    & $py @exArgs
    if ($LASTEXITCODE -ne 0) { throw "extract.py exited $LASTEXITCODE (2 = reconciliation self-check failed)" }

    # 2. Importer dry run: HTTP command against a LOCAL server only (there is no host command)
    if ($ApiBase -notmatch '^https?://(127\.0\.0\.1|localhost|\[::1\])(:\d+)?$') { throw "ApiBase must be a local server, got $ApiBase" }
    if (-not $Token) { throw 'Needs -Token (Owner bearer token) and a running local server at -ApiBase (dotnet run --project src/Hcc.Api).' }
    $body = @{ folder = $run; parts = $Parts; dryRun = $true; expectedTotals = (Get-Content -Raw -LiteralPath (Join-Path $run 'totals.json')) } | ConvertTo-Json -Depth 6
    Invoke-RestMethod -Method Post -Uri "$ApiBase/api/v1/commands/legacy.import" -Headers @{ Authorization = "Bearer $Token"; 'Idempotency-Key' = "lg-dryrun-$stamp" } -ContentType 'application/json' -Body $body |
        ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $resultFile -Encoding UTF8
}
finally { Pop-Location }

# 3. Report (masked)
$res = Get-Content -Raw -LiteralPath $resultFile | ConvertFrom-Json
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# LG dry run $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
[void]$sb.AppendLine()
[void]$sb.AppendLine("Run folder (outside git): ``$run``. Importer: dry run, parts $($Parts -join ', '). Names and phones below are masked.")
[void]$sb.AppendLine()
[void]$sb.AppendLine('## Section 6 comparison per financial year')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| FY | stays | bills | receipts | dup dropped | out of FY | bills n | net paise | room GST | F&B | receipts n | receipts paise | refunds n | refunds paise | result |')
[void]$sb.AppendLine('|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|')
foreach ($f in $res.history.perFy) {
    $c = $f.reconciliation
    $verdict = if ($c.match) { 'MATCH' } else { 'MISMATCH' }
    [void]$sb.AppendLine("| $($f.fy) | $($f.stays) | $($f.bills) | $($f.receipts) | $($f.duplicatesDropped) | $($f.outOfFy) | $($c.billsCount) | $($c.netPaise) | $($c.roomGstPaise) | $($c.fbPaise) | $($c.receiptsCount) | $($c.receiptsPaise) | $($c.refundsCount) | $($c.refundsPaise) | $verdict |")
}
foreach ($f in $res.history.perFy) {
    foreach ($d in @($f.reconciliation.differences)) { if ($d) { [void]$sb.AppendLine("- $($f.fy) difference: $($d | ConvertTo-Json -Compress)") } }
}
[void]$sb.AppendLine()
[void]$sb.AppendLine('## Guests')
[void]$sb.AppendLine()
[void]$sb.AppendLine(($res.guests | ConvertTo-Json -Compress))
[void]$sb.AppendLine()
[void]$sb.AppendLine('## Cut-over')
[void]$sb.AppendLine()
[void]$sb.AppendLine(($res.cutover | ConvertTo-Json -Compress))
[void]$sb.AppendLine()
[void]$sb.AppendLine('## Warnings')
foreach ($w in @($res.warnings)) { [void]$sb.AppendLine("- $w") }
[void]$sb.AppendLine()
[void]$sb.AppendLine('## 20-guest review list (masked)')
[void]$sb.AppendLine()
[void]$sb.AppendLine('Every Nth row of guests.csv (even spread). Check on this PC against the old software that the person is plausible; real values stay in the run folder.')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| # | Name | Phone | Company | Notes (digits masked) |')
[void]$sb.AppendLine('|---:|---|---|---|---|')
$g = @(Import-Csv -LiteralPath (Join-Path $run 'guests.csv') -Encoding UTF8)
if ($g.Count -gt 0) {
    $step = [math]::Max(1, [math]::Floor($g.Count / 20)); $i = 0; $n = 0
    while ($i -lt $g.Count -and $n -lt 20) {
        $r = $g[$i]; $n++
        $note = ($r.notes -replace '\d{4,}', '****')
        $co = if ($r.company) { Mask-Name $r.company } else { '' }
        [void]$sb.AppendLine("| $n | $(Mask-Name $r.name) | $(Mask-Phone $r.phone) | $co | $note |")
        $i += $step
    }
}
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
Set-Content -LiteralPath $report -Value $sb.ToString() -Encoding UTF8
Write-Host "Report: $report"
$bad = @($res.history.perFy | Where-Object { -not $_.reconciliation.match }).Count
if ($bad) { Write-Host "$bad FY mismatch(es)."; exit 1 }
exit 0
