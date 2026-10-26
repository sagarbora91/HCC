<#
.SYNOPSIS
  Loads the hotel's masters (rooms, rates, staff, ledgers, opening balances, till cash, guests, POS, departments, companies,
  suppliers, stock items) from the filled CSV templates into HCC: sign in, upload each file, print the row report, commit.
.DESCRIPTION
  Run on the server PC (or any PC that trusts the hotel CA), after step 6 of INSTALL.md (hotel and first Owner set up).
  - -Folder: the folder with your filled files, named exactly like the templates in deployment\import-templates\templates
    (room-types.csv, rooms.csv, ...; see import-templates\README.md). Files you do not use can be left out.
  - Signs in as the Owner: the sign-in name and password are asked for when not given (the password is read with
    Read-Host -AsSecureString: never shown, never written to a file, a log or the PowerShell history). The session is signed out
    at the end.
  - Server address: -ServerUrl, else "ServerUrl" in hcc-ops.json, else https://localhost:5443. HTTPS with the hotel certificate
    (INSTALL.md section 4 and 8); plain http:// is refused for any other PC.
  - Uploads the files one by one in the order the server needs (room types before rooms, ledgers before opening balances, outlets
    before the menu, ...) with UploadImportBatch and prints each file's report: rejected rows (row, column, what to fix),
    warnings, and what will be created, changed or left unchanged.
  - Without -DryRun each valid file is committed (CommitImportBatch) before the next one is uploaded. A file with a rejected row
    stops the run (nothing more is committed; fix the file and run again: committed files then show "unchanged").
    -ContinueOnErrors skips only the rejected file.
  - -DryRun: uploads and reports every file and lets the server run each valid file's import and roll it back; nothing is kept.
    Rows that need a file of the same run first (for example rooms that need the room types) are marked "expected in a dry run".
  - -OpeningBalancesDate yyyy-MM-dd is required when opening-balances.csv is in the folder (the last day before HCC starts).
  - -Only room-types,rooms imports just those files. -WhatIf lists the files and the requests and sends nothing.
  Exit code 0 = every file went through; 1 = a file was rejected or the server refused a request.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Folder,
    [string]$ServerUrl,
    [string]$UserName,
    [SecureString]$Password,
    [string]$OpeningBalancesDate,
    [switch]$DryRun,
    [switch]$ContinueOnErrors,
    [string[]]$Only = @(),
    [string]$ConfigPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Hcc-Common.ps1')

if (-not $Folder) { throw '-Folder is required: the folder with your filled CSV files, for example -Folder "D:\HccMasters".' }
if (-not (Test-Path -LiteralPath $Folder -PathType Container)) { throw "The folder '$Folder' was not found." }
if ($OpeningBalancesDate -and $OpeningBalancesDate -notmatch '^\d{4}-\d{2}-\d{2}$') { throw 'OpeningBalancesDate must be yyyy-MM-dd, for example 2026-10-31.' }
$api = Get-HccImportApiBase (Get-HccImportServerUrl -ServerUrl $ServerUrl -ConfigPath $ConfigPath)
$found = Select-HccImportFiles -FileNames @(Get-ChildItem -LiteralPath $Folder -File -Filter '*.csv' | ForEach-Object { $_.FullName }) -Only $Only
foreach ($u in $found.Unknown) { Write-Warning "$u is not named like an import template (see import-templates\README.md); it is skipped." }
if ($found.Files.Count -eq 0) { throw "No import files in '$Folder'. Name them like the templates: $((Get-HccImportOrder) -join '.csv, ').csv." }
if (($found.Files | Where-Object Master -eq 'opening-balances') -and -not $OpeningBalancesDate) {
    throw 'opening-balances.csv needs -OpeningBalancesDate yyyy-MM-dd: the last day before HCC starts (the date of your trial balance).'
}

$what = if ($DryRun) { 'check (dry run, nothing is kept)' } else { 'import and commit' }
if (-not $PSCmdlet.ShouldProcess($api, "$what $($found.Files.Count) file(s) from $Folder")) {
    Write-Host "DRY RUN: POST $api/auth/sign-in (sign-in name and password asked; password ********, never shown or saved)"
    $n = 0
    foreach ($f in $found.Files) {
        $n++
        $asOf = if ($f.Master -eq 'opening-balances') { " as at $OpeningBalancesDate" } else { '' }
        $next = if ($DryRun) { 'CommitImportBatch dryRun=true (rolled back)' } else { 'CommitImportBatch' }
        Write-Host ("DRY RUN: {0,2}. {1} -> POST {2}/commands/UploadImportBatch (master {3}{4}), print the report, then {5} unless a row is rejected" -f $n, $f.FileName, $api, $f.Master, $asOf, $next)
    }
    Write-Host "DRY RUN: POST $api/auth/sign-out"
    return
}

# Windows PowerShell 5.1 may not offer TLS 1.2 by default; the HCC server (Kestrel) needs TLS 1.2 or 1.3.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Invoke-HccApi {
    param([string]$Path, $Body, [string]$Token)
    $headers = @{ 'Idempotency-Key' = (New-HccIdempotencyKey 'import') }
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $json = if ($null -ne $Body) { $Body | ConvertTo-Json -Depth 8 -Compress } else { '{}' }
    try {
        return Invoke-RestMethod -Method Post -Uri ($api + $Path) -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json))
    }
    catch {
        $detail = if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $_.ErrorDetails.Message } else { $null }
        if (-not $detail) {
            # Windows PowerShell 5.1 does not always copy the reply into ErrorDetails: read the server's JSON error from the response.
            try {
                $stream = $_.Exception.Response.GetResponseStream()
                if ($stream.CanSeek) { $stream.Position = 0 }
                $detail = (New-Object IO.StreamReader($stream, [Text.Encoding]::UTF8)).ReadToEnd()
            }
            catch { $detail = $null }
        }
        throw (ConvertFrom-HccApiError -Body $detail -Fallback $_.Exception.Message)
    }
    finally { $json = $null }
}

if (-not $UserName) {
    $UserName = Read-Host -Prompt 'Owner sign-in name [owner]'
    if (-not $UserName) { $UserName = 'owner' }
}
if (-not $Password) { $Password = Read-Host -Prompt "Password for '$UserName' (not shown)" -AsSecureString }
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
$signIn = [ordered]@{ loginName = $UserName; password = $null; returnToken = $true }
try {
    $signIn.password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    $session = Invoke-HccApi -Path '/auth/sign-in' -Body $signIn
}
catch { throw "Sign-in as '$UserName' was refused: $($_.Exception.Message)" }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr); $signIn.password = $null }
$token = Get-HccField $session 'sessionToken'
if (-not $token) { throw 'The server did not return a session for this script. Is this an HCC server of 26 Oct 2026 or later?' }
Write-Host "Signed in as $($session.displayName) at $api. Mode: $what."

$rejected = @(); $committed = 0; $earlier = @(); $stopped = $false
try {
    foreach ($f in $found.Files) {
        Write-Host ''
        $body = New-HccImportUploadBody -Master $f.Master -FileName $f.FileName -CsvText (Read-HccImportCsv $f.Path) -AsOfDate $OpeningBalancesDate
        $report = Invoke-HccApi -Path '/commands/UploadImportBatch' -Body $body -Token $token
        $body = $null
        $split = Split-HccImportErrors -Master $f.Master -Errors @(Get-HccField $report 'errors') -DryRun:$DryRun -EarlierInRun $earlier
        foreach ($line in (Format-HccImportReport -Report $report -Blocking $split.Blocking -Expected $split.Expected)) { Write-Host $line }
        $earlier += $f.Master
        if ($split.Blocking.Count -gt 0) {
            $rejected += $f.FileName
            if ($ContinueOnErrors) { Write-Host "  -> NOT committed (upload $($report.batchId)); going on with the next file (-ContinueOnErrors)." -ForegroundColor Yellow; continue }
            Write-Host "  -> STOPPED: fix the rejected rows in $($f.FileName) and run the script again. The files after it were not sent." -ForegroundColor Red
            $stopped = $true
            break
        }
        if ($split.Expected.Count -gt 0) { Write-Host "  -> checked against what HCC holds now; the marked rows need $($split.Needs).csv committed first (run without -DryRun)."; continue }
        $c = $report.counts
        if (([int]$c.create + [int]$c.update + [int]$c.remove) -eq 0) { Write-Host '  -> nothing to change: every row is already in HCC.'; continue }
        $result = Invoke-HccApi -Path '/commands/CommitImportBatch' -Body (New-HccImportCommitBody -BatchId ([long]$report.batchId) -DryRun:$DryRun) -Token $token
        foreach ($line in (Format-HccImportCommit -Result $result)) { Write-Host $line }
        if (-not $DryRun) { $committed++ }
    }
}
finally {
    try { Invoke-HccApi -Path '/auth/sign-out' -Token $token | Out-Null } catch { Write-Warning "Sign-out failed: $($_.Exception.Message)" }
    $token = $null
}

Write-Host ''
if ($DryRun) { Write-Host "Dry run finished: nothing was kept. $(@($rejected).Count) file(s) with rejected rows." }
else { Write-Host "Finished: $committed file(s) committed, $(@($rejected).Count) file(s) with rejected rows." }
if ($stopped -or @($rejected).Count -gt 0) { exit 1 }
exit 0
