# Restore Hotel Control Centre from the migration package and verify it (see MIGRATION.md).
# Usage: powershell -ExecutionPolicy Bypass -File restore-on-new-pc.ps1 -Package "D:\HCC-Migration-2026-10-25" -Target "V:\Codex\Hotel Control Centre"
param(
  [Parameter(Mandatory=$true)][string]$Package,
  [Parameter(Mandatory=$true)][string]$Target,
  [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
function Step($m){ Write-Host "`n== $m" -ForegroundColor Cyan }

Step 'Checking package checksums'
$sums = Join-Path $Package 'SHA256SUMS.txt'
if(-not (Test-Path $sums)){ throw "SHA256SUMS.txt not found in $Package" }
$bad = 0
foreach($line in Get-Content $sums){
  if(-not $line.Trim()){ continue }
  $hash,$rel = $line -split '\s+\*?',2
  $file = Join-Path $Package $rel
  if(-not (Test-Path $file)){ Write-Host "MISSING $rel" -ForegroundColor Red; $bad++; continue }
  if((Get-FileHash $file -Algorithm SHA256).Hash -ne $hash.ToUpper()){ Write-Host "CHANGED $rel" -ForegroundColor Red; $bad++ }
}
if($bad){ throw "$bad package file(s) missing or changed - copy the package again." }
Write-Host 'All package files match.'

Step 'Checking tools'
foreach($t in 'git','node','npm'){ if(-not (Get-Command $t -ErrorAction SilentlyContinue)){ throw "$t is not installed (see MIGRATION.md section 2)." } }
Write-Host ("git: " + (git --version) + " | node: " + (node -v))

Step "Cloning repository into $Target"
if(Test-Path (Join-Path $Target '.git')){ throw "$Target already contains a git repository - choose an empty folder." }
git -c core.autocrlf=false clone --branch main (Join-Path $Package 'HCC.bundle') $Target   # keep files byte-identical to the old PC
Set-Location $Target
git config core.autocrlf false
git remote remove origin 2>$null
foreach($ref in (git ls-remote (Join-Path $Package 'HCC.bundle') 'refs/heads/*' | ForEach-Object { ($_ -split '\s+')[1] })){
  $name = $ref -replace '^refs/heads/',''
  if($name -ne 'main'){ git fetch -q (Join-Path $Package 'HCC.bundle') "$($ref):refs/heads/$name" }
}
git branch -a

Step 'Copying the APK (not stored in git)'
$apkDir = Join-Path $Target '05 Delivery Evidence\APK'
New-Item -ItemType Directory -Force $apkDir | Out-Null
Copy-Item (Join-Path $Package 'HCC-0.1.0-proto-debug-v8.apk') (Join-Path $apkDir 'HCC-0.1.0-proto-debug.apk') -Force

Step 'Installing packages (npm ci)'
Push-Location (Join-Path $Target '03 Prototype'); npm ci --no-audit --no-fund; Pop-Location
Push-Location (Join-Path $Target '04 Android'); npm ci --no-audit --no-fund; Pop-Location

if(-not $SkipTests){
  Step 'Verifying: typecheck, tests, build'
  Push-Location (Join-Path $Target '03 Prototype')
  npx tsc --noEmit; if($LASTEXITCODE){ throw 'Typecheck failed' } else { Write-Host 'Typecheck clean' }
  $out = npm test 2>&1; $out | Select-String -Pattern '^FAIL|tests passed' | ForEach-Object { $_.Line }
  npm run build 2>&1 | Select-Object -Last 1
  Pop-Location
}
Step 'Done'
Write-Host "Next: copy claude-memory and reference-inputs as described in MIGRATION.md section 3, then tell Claude 'resume after migration'."
