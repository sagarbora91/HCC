<#
  Lodge ID document patch - migration helper (DESIGN DRAFT, NOT EXECUTED ANYWHERE; written 2 Nov 2026)
  Modes
    Scan   : read IDProof and Photo folders (read-only), write scan.csv (SourceFolder,FileName,SizeBytes,ModifiedAt,Sha256Hex)
    Copy   : read plan.csv (exported from dbo.IdMigScanFile where MatchStatus='MATCHED'), COPY each file to the new store,
             re-hash the copy, write copy-result.csv (ScanId,CopyOk,NewSha256Hex,NewSizeBytes,Message). Never moves, never deletes, never overwrites.
    Verify : read the register export (IdDocument rows) and check every file exists with the stored size and hash; report missing or changed.
  Safety : source folders are only read. -WhatIf style dry run is the default; pass -Commit to write files.
  No guest names or ID numbers are printed; only file names, sizes and hashes.
  Example: powershell -NoProfile -File scan_idproof_folder.ps1 -Mode Scan -SourceRoot "D:\LodgeLive" -OutDir "D:\IdMigration"
#>
param(
    [Parameter(Mandatory=$true)][ValidateSet('Scan','Copy','Verify')][string]$Mode,
    [string]$SourceRoot,                 # folder that contains IDProof\ and Photo\  (the Lodge EXE folder, or a copy of it)
    [string]$StoreRoot,                  # new protected store (UNC), required for Copy and Verify
    [string]$Plan,                       # plan.csv (Copy) or register.csv (Verify)
    [string]$OutDir = (Get-Location).Path,
    [switch]$Commit
)
$ErrorActionPreference = 'Stop'
function Get-Sha256([string]$path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLower() }

if ($Mode -eq 'Scan') {
    if (-not $SourceRoot) { throw 'SourceRoot is required for Scan' }
    $rows = New-Object System.Collections.ArrayList
    foreach ($folder in 'IDProof','Photo') {
        $dir = Join-Path $SourceRoot $folder
        if (-not (Test-Path -LiteralPath $dir)) { Write-Warning "missing folder $dir"; continue }
        Get-ChildItem -LiteralPath $dir -File | ForEach-Object {
            $h = if ($_.Length -gt 0) { Get-Sha256 $_.FullName } else { '' }
            [void]$rows.Add([pscustomobject]@{ SourceFolder=$folder; FileName=$_.Name; SizeBytes=$_.Length;
                ModifiedAt=$_.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'); Sha256Hex=$h })
        }
    }
    $out = Join-Path $OutDir 'scan.csv'
    $rows | Export-Csv -LiteralPath $out -NoTypeInformation -Encoding ASCII
    "{0} files scanned, {1:N0} bytes, written to {2}" -f $rows.Count, ($rows | Measure-Object SizeBytes -Sum).Sum, $out
}

if ($Mode -eq 'Copy') {
    if (-not ($SourceRoot -and $StoreRoot -and $Plan)) { throw 'SourceRoot, StoreRoot and Plan are required for Copy' }
    $res = New-Object System.Collections.ArrayList
    foreach ($r in (Import-Csv -LiteralPath $Plan)) {      # columns used: ScanId, SourceFolder, FileName, Sha256Hex, NewFolderRel, NewFileName
        $src = Join-Path (Join-Path $SourceRoot $r.SourceFolder) $r.FileName
        $dstDir = Join-Path $StoreRoot $r.NewFolderRel
        $dst = Join-Path $dstDir $r.NewFileName
        $msg = ''; $ok = $false; $newHash = ''; $newSize = 0
        try {
            if (-not (Test-Path -LiteralPath $src)) { throw 'source missing' }
            if ((Get-Sha256 $src) -ne $r.Sha256Hex) { throw 'source changed since scan' }
            if (Test-Path -LiteralPath $dst) { throw 'target already exists (not overwritten)' }
            if ($Commit) {
                if (-not (Test-Path -LiteralPath $dstDir)) { [void](New-Item -ItemType Directory -Path $dstDir) }
                Copy-Item -LiteralPath $src -Destination $dst
                $newHash = Get-Sha256 $dst; $newSize = (Get-Item -LiteralPath $dst).Length
                if ($newHash -ne $r.Sha256Hex) { Remove-Item -LiteralPath $dst; throw 'copy hash differs, copy removed' }
                $ok = $true; $msg = 'copied'
            } else { $ok = $false; $msg = 'dry run (no -Commit)' }
        } catch { $msg = $_.Exception.Message }
        [void]$res.Add([pscustomobject]@{ ScanId=$r.ScanId; CopyOk=[int]$ok; NewSha256Hex=$newHash; NewSizeBytes=$newSize; Message=$msg })
    }
    $out = Join-Path $OutDir 'copy-result.csv'
    $res | Export-Csv -LiteralPath $out -NoTypeInformation -Encoding ASCII
    "{0} planned, {1} copied ok, {2} failed or skipped. Result: {3}" -f $res.Count, ($res | ? CopyOk -eq 1).Count, ($res | ? CopyOk -ne 1).Count, $out
}

if ($Mode -eq 'Verify') {
    if (-not ($StoreRoot -and $Plan)) { throw 'StoreRoot and Plan (register.csv: IdDocumentId,FolderRel,FileName,SizeBytes,Sha256Hex) are required for Verify' }
    $bad = New-Object System.Collections.ArrayList; $n = 0
    foreach ($r in (Import-Csv -LiteralPath $Plan)) {
        $n++; $p = Join-Path (Join-Path $StoreRoot $r.FolderRel) $r.FileName
        if (-not (Test-Path -LiteralPath $p)) { [void]$bad.Add([pscustomobject]@{ IdDocumentId=$r.IdDocumentId; Problem='MISSING' }); continue }
        $f = Get-Item -LiteralPath $p
        if ([string]$f.Length -ne [string]$r.SizeBytes) { [void]$bad.Add([pscustomobject]@{ IdDocumentId=$r.IdDocumentId; Problem='SIZE' }); continue }
        if ($r.Sha256Hex -and (Get-Sha256 $p) -ne $r.Sha256Hex) { [void]$bad.Add([pscustomobject]@{ IdDocumentId=$r.IdDocumentId; Problem='HASH' }) }
    }
    # stray files: on disk but not in the register
    $known = @{}; foreach ($r in (Import-Csv -LiteralPath $Plan)) { $known[(Join-Path $r.FolderRel $r.FileName).ToLower()] = 1 }
    $root = (Resolve-Path -LiteralPath $StoreRoot).Path.TrimEnd('\')
    Get-ChildItem -LiteralPath $StoreRoot -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($root.Length + 1).ToLower()
        if (-not $known.ContainsKey($rel)) { [void]$bad.Add([pscustomobject]@{ IdDocumentId=''; Problem="STRAY:$rel" }) }
    }
    $out = Join-Path $OutDir 'verify-problems.csv'
    $bad | Export-Csv -LiteralPath $out -NoTypeInformation -Encoding ASCII
    "{0} register rows checked, {1} problems. {2}" -f $n, $bad.Count, $out
}
