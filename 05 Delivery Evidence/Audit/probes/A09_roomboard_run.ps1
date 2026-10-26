# A09-18 room board probe (Wave C4, 26 Oct 2026). Reuses the audit's A09_perf_seed.mjs unchanged (a copy with the samples path pointed
# at this checkout is run from the scratch folder). Needs the SQL lane lock (E:\Projects\HCC\worktrees\wave-c-sql.lock) for every phase.
#   seed    create HccTest_PERF_<utc>, migrate it with -ApiBin (the build BEFORE the fix), Query Store on, start that API on 127.0.0.1:<Port>,
#           run the seed for -Days business days (stops at -SeedMinutes), capture Query Store top statements + row counts, stop the API.
#   before  A09_roomboard_enrich.sql (future bookings, holds, blocks written directly; fictional) then A09_roomboard_measure.ps1.
#   after   migrate with -ApiBin (the build WITH the fix), A09_roomboard_measure.ps1 (includes the old/new equivalence check), then
#           the stress set (A09_roomboard_stress.sql: 288 more future bookings) and A09_roomboard_measure.ps1 again.
#   drop    drop the database (only an HccTest_PERF_* name).
# Raw output goes to 05 Delivery Evidence\Audit\evidence\A09\roomboard-*.txt. The seed's own log/results go there too (roomboard-seed*.*).
param(
    [ValidateSet('seed', 'before', 'after', 'drop')][string]$Phase,
    [string]$Db,
    [string]$ApiBin,
    [string]$Start = '2025-10-01',
    [int]$Days = 240,
    [int]$SeedMinutes = 18,
    [int]$Port = 5293,
    [string]$Scratch = (Join-Path $env:TEMP 'a09-roomboard'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSCommandPath)))
$probes = Join-Path $repo '05 Delivery Evidence\Audit\probes'
$ev = Join-Path $repo '05 Delivery Evidence\Audit\evidence\A09'
New-Item -ItemType Directory -Force $Scratch | Out-Null

function Assert-Db([string]$name) { if ($name -notmatch '^HccTest_PERF_\d{14}$') { throw "Refusing database name '$name' (HccTest_PERF_<14 digits> only)." } }
function Invoke-Sql([string]$file, [string]$out) {
    & sqlcmd -S .\SQLEXPRESS -E -C -b -I -y 0 -v DB=$Db -i (Join-Path $probes $file) *>&1 | Out-File -FilePath $out -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw "$file failed (see $out)" }
}
function Invoke-Migrate {
    $env:Hcc__Sql__ConnectionString = "Data Source=.\SQLEXPRESS;Initial Catalog=$Db;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Application Name=HCC-A09-roomboard"
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:Hcc__Sql__BackupDirectory = 'NUL'   # throwaway database: no pre-migration .bak in the SQL Server backup folder (Development only)
    Push-Location $ApiBin
    try { & dotnet .\Hcc.Api.dll migrate *>&1 | Out-String } finally { Pop-Location }
}

switch ($Phase) {
    'seed' {
        $Db = 'HccTest_PERF_' + (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
        Assert-Db $Db
        Set-Content -Path (Join-Path $ev 'roomboard-db-name.txt') -Value $Db
        & sqlcmd -S .\SQLEXPRESS -E -C -b -Q "CREATE DATABASE [$Db];" | Out-Null
        $t0 = Get-Date
        (Invoke-Migrate) + "`nmigrate wall: $(((Get-Date) - $t0).TotalSeconds) s" | Set-Content (Join-Path $ev 'roomboard-before-migrate.txt')
        & sqlcmd -S .\SQLEXPRESS -E -C -b -Q "ALTER DATABASE [$Db] SET QUERY_STORE = ON (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = ALL, INTERVAL_LENGTH_MINUTES = 1);" | Out-Null
        $seedDir = Join-Path $Scratch 'seed'
        New-Item -ItemType Directory -Force $seedDir | Out-Null
        Get-ChildItem $seedDir -File | Remove-Item
        $samples = (Join-Path $repo '08 Production\deployment\import-templates\samples\').Replace('\', '/')
        $src = Get-Content -Raw (Join-Path $probes 'A09_perf_seed.mjs')
        $src = $src.Replace('E:/Projects/HCC/Hotel Control Centre/08 Production/deployment/import-templates/samples/', $samples)
        $seedJs = Join-Path $Scratch 'A09_perf_seed.mjs'
        [IO.File]::WriteAllText($seedJs, $src)
        $env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
        $api = Start-Process dotnet -ArgumentList '.\Hcc.Api.dll' -WorkingDirectory $ApiBin -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput (Join-Path $Scratch 'api-out.txt') -RedirectStandardError (Join-Path $Scratch 'api-err.txt')
        try {
            for ($i = 0; $i -lt 60; $i++) { try { Invoke-RestMethod "http://127.0.0.1:$Port/api/v1/system/health" | Out-Null; break } catch { Start-Sleep -Milliseconds 500 } }
            $env:PATH = "E:\Tools\nodejs;$env:PATH"
            "start $((Get-Date).ToString('o'))" | Set-Content (Join-Path $ev 'roomboard-seed-wallclock.txt')
            & node $seedJs "http://127.0.0.1:$Port/api/v1" $seedDir $Start $Days $SeedMinutes
            "end $((Get-Date).ToString('o'))" | Add-Content (Join-Path $ev 'roomboard-seed-wallclock.txt')
        }
        finally { if ($api -and -not $api.HasExited) { Stop-Process -Id $api.Id -Force } }
        Copy-Item (Join-Path $seedDir 'perf-seed.log') (Join-Path $ev 'roomboard-seed.log')
        if (Test-Path (Join-Path $seedDir 'perf-results.json')) { Copy-Item (Join-Path $seedDir 'perf-results.json') (Join-Path $ev 'roomboard-seed-results.json') }
        Invoke-Sql 'A09_roomboard_querystore.sql' (Join-Path $ev 'roomboard-before-querystore.txt')
        $Db
    }
    'before' {
        Assert-Db $Db
        Invoke-Sql 'A09_roomboard_enrich.sql' (Join-Path $ev 'roomboard-enrich.txt')
        & (Join-Path $probes 'A09_roomboard_measure.ps1') -Db $Db -Label before -Out (Join-Path $ev 'roomboard-before-measure.txt') -PlanDir (Join-Path $ev 'roomboard-plans')
    }
    'after' {
        Assert-Db $Db
        Invoke-Migrate | Set-Content (Join-Path $ev 'roomboard-after-migrate.txt')
        & (Join-Path $probes 'A09_roomboard_measure.ps1') -Db $Db -Label after -Out (Join-Path $ev 'roomboard-after-measure.txt') -PlanDir (Join-Path $ev 'roomboard-plans')
        Invoke-Sql 'A09_roomboard_stress.sql' (Join-Path $ev 'roomboard-stress-enrich.txt')
        & (Join-Path $probes 'A09_roomboard_measure.ps1') -Db $Db -Label stress -Out (Join-Path $ev 'roomboard-stress-measure.txt') -PlanDir (Join-Path $ev 'roomboard-plans')
    }
    'drop' {
        Assert-Db $Db
        & sqlcmd -S .\SQLEXPRESS -E -C -b -Q "IF DB_ID(N'$Db') IS NOT NULL BEGIN ALTER DATABASE [$Db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Db]; END" | Out-Null
        & sqlcmd -S .\SQLEXPRESS -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) AS perf_dbs_left FROM sys.databases WHERE name LIKE 'HccTest[_]PERF[_]%'" | Set-Content (Join-Path $ev 'roomboard-cleanup.txt')
        Get-Content (Join-Path $ev 'roomboard-cleanup.txt')
    }
}
