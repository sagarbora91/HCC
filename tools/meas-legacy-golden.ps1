<#
F12 MEAS · legacy golden test (MEAS-CONTRACT §9.5; brief meas-study-pack item 3). THIS PC ONLY: the legacy workbooks and the imported lgc
data never enter the repository; outputs go to <repo>\.scratch\golden\<stamp>\ (not committed).

Real mode (the integrator, once, after Phase B2 is merged): runs every ported study report on the hotel server's imported lgc data for each
FY 2017-18 … 2026-27 through the report API, reads the same headline totals from the workbooks in -Workbooks with Python (openpyxl,
read-only), and compares them: billed per FY, room-nights, occupancy, ADR, bad-debt total, zero-rate counts, repeat %, hall events.
Tolerances (tools/legacy/meas_golden_map.json): 0.1 % money, 5 room-nights, 0.5 points percentages, counts exact. Exit 1 on any mismatch
or missing metric.

Dry mode (-Dry, required plumbing check, fictional data only): runs the registered study reports and the management pack on the prototype's
FY26 fixture (FY 2026-27, April to the business date), recomputes the pack's executive-summary headlines independently in Python from the fixture's raw room lines, and
compares them with the same tolerances. Study reports not merged yet are listed as skipped.

  pwsh -NoProfile -File tools\meas-legacy-golden.ps1 -Dry
  pwsh -NoProfile -File tools\meas-legacy-golden.ps1 -ApiBase http://127.0.0.1:5080/api -Token $env:HCC_GOLDEN_TOKEN
Heavy step (node bundling) — run it through tools\Invoke-Serialized.ps1 like any other job.
#>
[CmdletBinding()]
param(
    [switch]$Dry,
    [string]$ApiBase,
    [string]$Token = $env:HCC_GOLDEN_TOKEN,
    [string]$Workbooks = 'E:\Projects\HCC\Existing Software\Reports',
    [string[]]$Fys = @('2017-18', '2018-19', '2019-20', '2020-21', '2021-22', '2022-23', '2023-24', '2024-25', '2025-26', '2026-27'),
    [string]$DryFy = '2026-27',
    [string]$Out
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$legacy = Join-Path $PSScriptRoot 'legacy'
$map = Join-Path $legacy 'meas_golden_map.json'
$py = Join-Path $legacy 'meas_golden.py'
if (-not $Out) { $Out = Join-Path $repo ('.scratch\golden\' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Force $Out | Out-Null
$results = Join-Path $Out 'hcc-results.json'; $hcc = Join-Path $Out 'hcc.json'; $ref = Join-Path $Out 'ref.json'
function Invoke-Step([string]$what, [scriptblock]$block) { Write-Host "== $what"; & $block; if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" } }

if ($Dry) {
    $facts = Join-Path $Out 'fixture-facts.json'
    Invoke-Step 'HCC side (prototype FY26 fixture)' { node (Join-Path $legacy 'meas_golden_hcc.mjs') --fy $DryFy --results $results --facts $facts }
    Invoke-Step 'extract headlines' { python $py extract $results $map $hcc --dry }
    Invoke-Step 'reference (recomputed from the fixture facts)' { python $py ref-dry $facts $ref }
    $skipped = (Get-Content $results -Raw | ConvertFrom-Json).reports | Where-Object { $_.error } | ForEach-Object { $_.id }
    if ($skipped) { Write-Host ("skipped (error): " + ($skipped -join ', ')) }
    python $py compare $hcc $ref $map --dry
    $code = $LASTEXITCODE
}
else {
    if (-not $ApiBase) { throw 'Real mode needs -ApiBase (the hotel server report API, e.g. http://127.0.0.1:5080/api) and -Token (an Owner session token).' }
    if (-not (Test-Path $Workbooks)) { throw "Workbooks folder not found: $Workbooks" }
    $ids = (Get-Content $map -Raw | ConvertFrom-Json).metrics | ForEach-Object { $_.hcc.report } | Sort-Object -Unique
    $headers = @{ Authorization = "Bearer $Token" }
    $reports = foreach ($id in $ids) {
        $secs = @()
        $err = $null
        foreach ($fy in $Fys) {
            try {
                $r = Invoke-RestMethod -Uri "$ApiBase/reports/$id`?fy=$fy&all=true" -Headers $headers -Method Get
                $tables = if ($r.sections) { $r.sections } else { @(@{ key = 'main'; title = $r.title; columns = $r.columns; rows = $r.rows }) }
                foreach ($t in $tables) { $secs += @{ key = "$fy/$($t.key)"; title = $t.title; columns = $t.columns; rows = $t.rows } }
            }
            catch { $err = "$fy`: $($_.Exception.Message)" }
        }
        if ($secs.Count) { @{ id = $id; sections = $secs } } else { @{ id = $id; error = $err } }
    }
    @{ source = $ApiBase; reports = @($reports) } | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 $results
    Invoke-Step 'extract headlines (HCC)' { python $py extract $results $map $hcc }
    Invoke-Step 'reference (legacy workbooks)' { python $py ref-workbooks $Workbooks $map $ref }
    python $py compare $hcc $ref $map
    $code = $LASTEXITCODE
}
Write-Host "Outputs: $Out"
exit $code
