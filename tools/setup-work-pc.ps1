<#
  Hotel Control Centre - work-PC setup.
  1. Checks every essential first and prints a table (Installed / Missing / Wrong version).
  2. Installs ONLY what is missing, ALWAYS under E:\ (tools, SDKs, caches, emulators, SQL data).
  3. Optionally restores the project from the migration package (-Package).

  Run in Windows PowerShell 5.1 or PowerShell 7, as Administrator (SQL Server and some installers need it):
    powershell -ExecutionPolicy Bypass -File setup-work-pc.ps1 -CheckOnly            # just report
    powershell -ExecutionPolicy Bypass -File setup-work-pc.ps1                       # install what is missing
    powershell -ExecutionPolicy Bypass -File setup-work-pc.ps1 -Package "E:\HCC-Migration-2026-10-25"
  Optional parts: -SkipAndroid  -SkipSql  -SkipDotnet  -SqlSetupPath "<folder with SQL 2025 Express setup.exe>"
#>
param(
  [string]$Root = 'E:\',
  [switch]$CheckOnly,
  [switch]$SkipAndroid,
  [switch]$SkipSql,
  [switch]$SkipDotnet,
  [string]$SqlSetupPath = '',
  [string]$Package = '',
  [string]$ProjectDir = 'E:\Codex\Hotel Control Centre'
)
# Continue (not Stop): in Windows PowerShell 5.1, Stop turns any stderr line of winget/npm/sdkmanager into a fatal error.
# Native commands are checked via exit codes; downloads/unzips/moves below use -ErrorAction Stop.
$ErrorActionPreference = 'Continue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ---------- locations (everything on E:) ----------
$Tools     = Join-Path $Root 'Tools'
$Cache     = Join-Path $Root 'Cache'
$Downloads = Join-Path $Root 'HCC-Setup\downloads'
$LogDir    = Join-Path $Root 'HCC-Setup'
$Paths = @{
  Git        = Join-Path $Tools 'Git'
  Node       = Join-Path $Tools 'nodejs'
  NpmGlobal  = Join-Path $Tools 'npm-global'
  NpmCache   = Join-Path $Cache 'npm'
  Dotnet     = Join-Path $Tools 'dotnet'
  NugetCache = Join-Path $Cache 'nuget'
  Studio     = Join-Path $Root 'Android\Studio'
  Sdk        = Join-Path $Root 'Android\Sdk'
  Avd        = Join-Path $Root 'Android\avd'
  Gradle     = Join-Path $Cache 'gradle'
  Jdk        = Join-Path $Tools 'jdk-21'
  SqlInstall = Join-Path $Root 'SQLServer'
  SqlData    = Join-Path $Root 'SQLServer\Data'
  SqlBackup  = Join-Path $Root 'SQLServer\Backup'
}
$NodeMajor = 25          # matches the old PC (v25.9.0)
$DotnetChannel = '10.0'
$AndroidPackages = @('platform-tools','emulator','platforms;android-36','build-tools;36.0.0','system-images;android-35;google_apis;x86_64')

# ---------- helpers ----------
if(-not (Test-Path $Root)){ throw "Drive $Root not found. Connect/format E: first, or pass -Root." }
New-Item -ItemType Directory -Force $LogDir,$Downloads | Out-Null
$Log = Join-Path $LogDir ('setup-' + (Get-Date -Format 'yyyyMMdd-HHmm') + '.log')
function Say($m,$c='Gray'){ Write-Host $m -ForegroundColor $c; Add-Content $Log ("{0:HH:mm:ss} {1}" -f (Get-Date),$m) }
function Step($m){ Say "`n== $m" 'Cyan' }
function IsAdmin(){ ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) }
function Cmd($name){ Get-Command $name -ErrorAction SilentlyContinue | Select-Object -First 1 }
function RefreshPath(){ $env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User') }
function AddUserPath($dir){
  $cur = [Environment]::GetEnvironmentVariable('Path','User'); if(-not $cur){ $cur = '' }
  $parts = $cur.Split(';') | Where-Object { $_ -and ($_.TrimEnd('\') -ne $dir.TrimEnd('\')) }
  [Environment]::SetEnvironmentVariable('Path', (@($dir) + $parts) -join ';', 'User')   # new tool goes first
  $env:Path = $dir + ';' + $env:Path
}
function SetUserEnv($n,$v){ [Environment]::SetEnvironmentVariable($n,$v,'User'); Set-Item -Path "Env:$n" -Value $v }
# Installed-program lookup in the Windows uninstall registry (finds programs wherever their installer put them).
function FindInstallDir($namePattern){
  $keys = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
  foreach($k in $keys){
    foreach($e in (Get-ItemProperty $k -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like $namePattern })){
      if($e.InstallLocation -and (Test-Path $e.InstallLocation)){ return $e.InstallLocation.TrimEnd('\') }
      if($e.UninstallString){ $d = Split-Path ($e.UninstallString.Trim('"') -replace '"','') -ErrorAction SilentlyContinue; if($d -and (Test-Path $d)){ return $d } }
    }
  }
  return $null
}
function FindStudioDir(){
  foreach($d in @($Paths.Studio,'C:\Program Files\Android\Android Studio',(FindInstallDir 'Android Studio*'))){ if($d -and (Test-Path (Join-Path $d 'bin\studio64.exe'))){ return $d } }
  return $null
}
function FindJavaHome(){
  $cands = @()
  $s = FindStudioDir; if($s){ $cands += (Join-Path $s 'jbr') }
  $cands += $Paths.Jdk
  $cands += (Get-ChildItem 'C:\Program Files\Microsoft','E:\Program Files\Microsoft' -Directory -Filter 'jdk-21*' -ErrorAction SilentlyContinue | ForEach-Object FullName)
  $j = FindInstallDir 'Microsoft Build of OpenJDK*21*'; if($j){ $cands += $j }
  foreach($c in $cands){ if($c -and (Test-Path (Join-Path $c 'bin\java.exe'))){ return $c } }
  return $null
}
function OnE($dir){ $dir -and ($dir.ToUpper().StartsWith($Root.ToUpper().TrimEnd('\'))) }
function Download($url,$file){
  if(Test-Path $file){ Say "  using cached $file"; return }
  Say "  downloading $url"
  Invoke-WebRequest -Uri $url -OutFile "$file.part" -UseBasicParsing -ErrorAction Stop
  Move-Item "$file.part" $file -Force -ErrorAction Stop
}
function HasWinget(){ [bool](Cmd 'winget') }
function WingetInstall($id,$location){
  if(-not (HasWinget)){ throw "winget is not available. Install 'App Installer' from the Microsoft Store, or install $id manually into $location." }
  Say "  winget install $id -> $location"
  winget install --id $id -e --location $location --silent --accept-package-agreements --accept-source-agreements --disable-interactivity | Out-Host
  if($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne -1978335189){ throw "winget failed for $id (exit $LASTEXITCODE)" }   # -1978335189 = already installed
}

# ---------- 1. checks ----------
Step 'Checking essentials'
RefreshPath
$results = New-Object System.Collections.ArrayList
function Check($name,$status,$detail,$need){ [void]$results.Add([pscustomobject]@{Component=$name;Status=$status;Detail=$detail;Action=$need}) }

$git = Cmd 'git'
if($git){ Check 'Git' 'Installed' ((git --version) + ' @ ' + $git.Source) 'none' } else { Check 'Git' 'Missing' '' 'install' }

$node = Cmd 'node'
if($node){
  $v = (node -v).TrimStart('v'); $maj = [int]($v.Split('.')[0])
  if($maj -eq $NodeMajor){ Check 'Node.js' 'Installed' "v$v @ $($node.Source)" 'none' }
  else { Check 'Node.js' 'Wrong version' "v$v found, need $NodeMajor.x" 'install (side by side, first in PATH)' }
} else { Check 'Node.js' 'Missing' '' 'install' }

if(-not $SkipDotnet){
  $dn = Cmd 'dotnet'; $has10 = $false
  if($dn){ $has10 = [bool]((dotnet --list-sdks 2>$null) -match '^10\.') }
  if($has10){ Check '.NET 10 SDK' 'Installed' ((dotnet --list-sdks | Select-String '^10\.' | Select-Object -First 1).ToString()) 'none' }
  else { Check '.NET 10 SDK' 'Missing' '' 'install' }
}

if(-not $SkipAndroid){
  $studio = FindStudioDir
  if($studio){ Check 'Android Studio' 'Installed' $studio $(if(OnE $studio){'none'}else{'installed outside E: (see MIGRATION.md to move)'}) } else { Check 'Android Studio' 'Missing' '' 'install' }
  $sdk = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, "$env:LOCALAPPDATA\Android\Sdk", $Paths.Sdk) | Where-Object { $_ -and (Test-Path (Join-Path $_ 'platform-tools\adb.exe')) } | Select-Object -First 1
  if($sdk){ Check 'Android SDK' 'Installed' $sdk 'check packages' } else { Check 'Android SDK' 'Missing' '' 'install' }
  $java = FindJavaHome
  if($java){ Check 'Java 21 (JBR/JDK)' 'Installed' $java 'none' } else { Check 'Java 21 (JBR/JDK)' 'Missing' '' 'comes with Android Studio' }
  if($env:JAVA_HOME -and -not (Test-Path (Join-Path $env:JAVA_HOME 'bin\java.exe'))){ Check 'JAVA_HOME' 'Invalid' $env:JAVA_HOME 'will be reset to the real Java' }
  if($env:ANDROID_HOME -and $env:ANDROID_SDK_ROOT -and ($env:ANDROID_HOME -ne $env:ANDROID_SDK_ROOT)){ Check 'ANDROID_HOME = ANDROID_SDK_ROOT' 'Mismatch' "$env:ANDROID_HOME vs $env:ANDROID_SDK_ROOT" 'set both to the same SDK' }
}

if(-not $SkipSql){
  $svc = Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue
  if($svc){ Check 'SQL Server Express' 'Installed' ("instance SQLEXPRESS, " + $svc.Status) 'none' } else { Check 'SQL Server Express' 'Missing' '' $(if($SqlSetupPath){'install from media'}else{'needs -SqlSetupPath'}) }
  if(Cmd 'sqlcmd'){ Check 'sqlcmd' 'Installed' (Cmd 'sqlcmd').Source 'none' } else { Check 'sqlcmd' 'Missing' '' 'install' }
}

$wg = HasWinget
Check 'winget (installer helper)' $(if($wg){'Installed'}else{'Missing'}) '' $(if($wg){'none'}else{'install App Installer from Microsoft Store'})
$results | Format-Table -AutoSize | Out-String -Width 200 | ForEach-Object { Say $_ }
if($CheckOnly){ Say "Check only - nothing installed. Log: $Log" 'Green'; return }
if(-not (IsAdmin)){ Say 'Not running as Administrator: SQL Server and some installers may fail. Re-run from an elevated PowerShell if they do.' 'Yellow' }

# ---------- 2. installs (only the missing ones) ----------
New-Item -ItemType Directory -Force $Tools,$Cache | Out-Null

if(-not (Cmd 'git')){
  Step 'Installing Git'
  WingetInstall 'Git.Git' $Paths.Git
  RefreshPath; if(-not (Cmd 'git')){ AddUserPath (Join-Path $Paths.Git 'cmd') }
}

$needNode = -not (Cmd 'node') -or ([int]((node -v).TrimStart('v').Split('.')[0]) -ne $NodeMajor)
if($needNode){
  Step "Installing Node.js $NodeMajor.x (portable, $($Paths.Node))"
  $base = "https://nodejs.org/dist/latest-v$NodeMajor.x"
  $sums = (Invoke-WebRequest "$base/SHASUMS256.txt" -UseBasicParsing -ErrorAction Stop).Content
  $line = ($sums -split "`n") | Where-Object { $_ -match 'node-v[\d.]+-win-x64\.zip$' } | Select-Object -First 1
  $hash,$zipName = $line.Trim() -split '\s+'
  $zip = Join-Path $Downloads $zipName
  Download "$base/$zipName" $zip
  if((Get-FileHash $zip -Algorithm SHA256).Hash -ne $hash.ToUpper()){ Remove-Item $zip; throw 'Node.js download checksum mismatch - run again.' }
  $tmp = Join-Path $Downloads 'node-extract'; if(Test-Path $tmp){ Remove-Item $tmp -Recurse -Force }
  Expand-Archive $zip $tmp -ErrorAction Stop
  if(Test-Path $Paths.Node){ Remove-Item $Paths.Node -Recurse -Force }
  Move-Item (Get-ChildItem $tmp -Directory | Select-Object -First 1).FullName $Paths.Node -ErrorAction Stop
  AddUserPath $Paths.Node
  # Windows puts the machine PATH before the user PATH: an older system-wide Node would still win in new windows.
  $winner = (Get-Command node -All | Where-Object { [Environment]::GetEnvironmentVariable('Path','Machine') -split ';' -contains (Split-Path $_.Source) } | Select-Object -First 1)
  if($winner -and ((Split-Path $winner.Source) -ne $Paths.Node)){
    if(IsAdmin){ $m=[Environment]::GetEnvironmentVariable('Path','Machine'); [Environment]::SetEnvironmentVariable('Path',($Paths.Node+';'+$m),'Machine'); Say "  put $($Paths.Node) first in the system PATH (older Node at $($winner.Source))" 'Yellow' }
    else { Say "  WARNING: an older Node at $($winner.Source) is in the system PATH and will win in new windows. Re-run as Administrator, or remove it." 'Yellow' }
  }
  Say ("  node " + (& (Join-Path $Paths.Node 'node.exe') -v))
}
# npm cache and global packages on E: (always safe to set)
New-Item -ItemType Directory -Force $Paths.NpmGlobal,$Paths.NpmCache | Out-Null
npm config set cache $Paths.NpmCache | Out-Null
npm config set prefix $Paths.NpmGlobal | Out-Null
AddUserPath $Paths.NpmGlobal

if(-not $SkipDotnet -and -not ((Cmd 'dotnet') -and ((dotnet --list-sdks 2>$null) -match '^10\.'))){
  Step ".NET $DotnetChannel SDK ($($Paths.Dotnet))"
  $ps1 = Join-Path $Downloads 'dotnet-install.ps1'
  Download 'https://dot.net/v1/dotnet-install.ps1' $ps1
  & $ps1 -Channel $DotnetChannel -InstallDir $Paths.Dotnet -NoPath -ErrorAction Stop
  SetUserEnv 'DOTNET_ROOT' $Paths.Dotnet
  AddUserPath $Paths.Dotnet
}
if(-not $SkipDotnet){ SetUserEnv 'NUGET_PACKAGES' $Paths.NugetCache; SetUserEnv 'DOTNET_CLI_TELEMETRY_OPTOUT' '1' }

if(-not $SkipAndroid){
  Step 'Android (Studio, SDK, emulator)'
  # Reuse an SDK that already exists (with adb); only a missing SDK is created on E:.
  $existingSdk = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, "$env:LOCALAPPDATA\Android\Sdk") | Where-Object { $_ -and (Test-Path (Join-Path $_ 'platform-tools\adb.exe')) } | Select-Object -First 1
  if($existingSdk){ $Paths.Sdk = $existingSdk; Say "  using existing Android SDK: $existingSdk" } else { Say "  new Android SDK location: $($Paths.Sdk)" }
  SetUserEnv 'ANDROID_HOME' $Paths.Sdk; SetUserEnv 'ANDROID_SDK_ROOT' $Paths.Sdk   # both must point to the same SDK (old-PC lesson)
  if($env:ANDROID_AVD_HOME -and (Test-Path $env:ANDROID_AVD_HOME)){ $Paths.Avd = $env:ANDROID_AVD_HOME } else { SetUserEnv 'ANDROID_AVD_HOME' $Paths.Avd }
  if($env:GRADLE_USER_HOME){ $Paths.Gradle = $env:GRADLE_USER_HOME } else { SetUserEnv 'GRADLE_USER_HOME' $Paths.Gradle }
  New-Item -ItemType Directory -Force $Paths.Sdk,$Paths.Avd,$Paths.Gradle | Out-Null
  $studioDir = FindStudioDir
  if(-not $studioDir){
    # winget's --location is ignored by this NSIS installer: pass /D= ourselves (must be last, unquoted).
    if(-not (HasWinget)){ throw 'winget is not available to install Android Studio.' }
    Say "  winget install Google.AndroidStudio -> $($Paths.Studio)"
    winget install --id Google.AndroidStudio -e --override "/S /D=$($Paths.Studio)" --accept-package-agreements --accept-source-agreements --disable-interactivity | Out-Host
    $studioDir = FindStudioDir
    if(-not $studioDir){ throw 'Android Studio installer finished but studio64.exe was not found.' }
  }
  Say "  Android Studio: $studioDir"
  if(-not (OnE $studioDir)){ Say "  NOTE: Android Studio is outside $Root. It works; to move it, uninstall it (Settings > Apps) and run this script again." 'Yellow' }
  $jbr = FindJavaHome
  if(-not $jbr){
    Say '  No Java 21 found - installing Microsoft OpenJDK 21'
    if(-not (HasWinget)){ throw 'winget is not available to install OpenJDK.' }
    winget install --id Microsoft.OpenJDK.21 -e --override "/quiet /norestart INSTALLDIR=`"$($Paths.Jdk)`"" --accept-package-agreements --accept-source-agreements --disable-interactivity | Out-Host
    $jbr = FindJavaHome
    if(-not $jbr){ throw 'OpenJDK installed but java.exe was not found.' }
  }
  Say "  Java (JAVA_HOME): $jbr"
  SetUserEnv 'JAVA_HOME' $jbr
  $sdkm = Join-Path $Paths.Sdk 'cmdline-tools\latest\bin\sdkmanager.bat'
  if(-not (Test-Path $sdkm)){ $alt = Get-ChildItem (Join-Path $Paths.Sdk 'cmdline-tools') -Filter sdkmanager.bat -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1; if($alt){ $sdkm = $alt.FullName } }
  if(-not (Test-Path $sdkm)){
    Say '  installing Android command-line tools'
    $zip = Join-Path $Downloads 'commandlinetools-win.zip'
    Download 'https://dl.google.com/android/repository/commandlinetools-win-11076708_latest.zip' $zip
    $tmp = Join-Path $Downloads 'clt'; if(Test-Path $tmp){ Remove-Item $tmp -Recurse -Force }
    Expand-Archive $zip $tmp -ErrorAction Stop
    New-Item -ItemType Directory -Force (Join-Path $Paths.Sdk 'cmdline-tools') | Out-Null
    Move-Item (Join-Path $tmp 'cmdline-tools') (Join-Path $Paths.Sdk 'cmdline-tools\latest') -ErrorAction Stop
  }
  $have = (& $sdkm --sdk_root=$($Paths.Sdk) --list_installed 2>$null) -join "`n"
  $missing = $AndroidPackages | Where-Object { $have -notmatch [regex]::Escape($_) }
  if($missing){
    Say ('  SDK packages to install: ' + ($missing -join ', '))
    $yes = ('y' + [Environment]::NewLine) * 30
    $yes | & $sdkm --sdk_root=$($Paths.Sdk) --licenses | Out-Null
    & $sdkm --sdk_root=$($Paths.Sdk) @missing | Out-Host
    if($LASTEXITCODE -ne 0){ throw "sdkmanager failed (exit $LASTEXITCODE). JAVA_HOME=$env:JAVA_HOME" }
  } else { Say '  SDK packages already present' }
  AddUserPath (Join-Path $Paths.Sdk 'platform-tools'); AddUserPath (Join-Path $Paths.Sdk 'emulator')
  $avdm = Join-Path (Split-Path $sdkm) 'avdmanager.bat'
  if(-not (Test-Path (Join-Path $Paths.Avd 'codex_api35_google.avd'))){
    Say '  creating AVD codex_api35_google (Android 15) in E:\Android\avd'
    'no' | & $avdm create avd -n codex_api35_google -k 'system-images;android-35;google_apis;x86_64' -d pixel_6 | Out-Host
    if($LASTEXITCODE -ne 0){ Say "  AVD creation failed (exit $LASTEXITCODE) - create it later in Android Studio Device Manager." 'Yellow' }
  } else { Say '  AVD codex_api35_google already exists' }
  Say '  Old-WebView test AVD (codex_apk_review): create it in Android Studio Device Manager if needed (any API 28-30 image with the stock WebView).' 'Yellow'
}

if(-not $SkipSql){
  if(-not (Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue)){
    if($SqlSetupPath -and (Test-Path (Join-Path $SqlSetupPath 'setup.exe'))){
      Step 'SQL Server 2025 Express (instance SQLEXPRESS) on E:'
      if(-not (IsAdmin)){ throw 'SQL Server setup needs an elevated (Administrator) PowerShell.' }
      New-Item -ItemType Directory -Force $Paths.SqlInstall,$Paths.SqlData,$Paths.SqlBackup | Out-Null
      $me = [Security.Principal.WindowsIdentity]::GetCurrent().Name
      $sqlArgs = @('/Q','/ACTION=Install','/FEATURES=SQLEngine','/INSTANCENAME=SQLEXPRESS','/IACCEPTSQLSERVERLICENSETERMS',
        "/INSTANCEDIR=$($Paths.SqlInstall)","/INSTALLSHAREDDIR=$($Paths.SqlInstall)\Shared","/INSTALLSHAREDWOWDIR=$($Paths.SqlInstall)\SharedWOW",
        "/INSTALLSQLDATADIR=$($Paths.SqlData)","/SQLUSERDBDIR=$($Paths.SqlData)","/SQLUSERDBLOGDIR=$($Paths.SqlData)","/SQLTEMPDBDIR=$($Paths.SqlData)",
        "/SQLBACKUPDIR=$($Paths.SqlBackup)","/SQLSYSADMINACCOUNTS=`"$me`"",'/TCPENABLED=0','/UPDATEENABLED=0')
      $p = Start-Process (Join-Path $SqlSetupPath 'setup.exe') -ArgumentList $sqlArgs -Wait -PassThru
      if($p.ExitCode -ne 0){ throw "SQL Server setup failed (exit $($p.ExitCode)). See C:\Program Files\Microsoft SQL Server\*\Setup Bootstrap\Log." }
      Say '  SQL Server Express installed (Windows authentication, TCP off, data on E:).' 'Green'
    } else {
      Say 'SQL Server Express not installed: pass -SqlSetupPath with the SQL Server 2025 Express media (setup.exe). The old PC has offline media in V:\ETP\Installers. Not needed for the prototype; needed for the SQL spike and production work.' 'Yellow'
    }
  }
  if(-not (Cmd 'sqlcmd')){ Step 'sqlcmd'; try { WingetInstall 'Microsoft.Sqlcmd' (Join-Path $Tools 'sqlcmd') } catch { Say "  sqlcmd not installed: $($_.Exception.Message)" 'Yellow' } }
}

# ---------- 3. optional project restore ----------
if($Package){
  Step "Restoring the project from $Package into $ProjectDir"
  $restore = Join-Path $Package 'restore-on-new-pc.ps1'
  if(-not (Test-Path $restore)){ throw "restore-on-new-pc.ps1 not found in $Package" }
  if(Test-Path (Join-Path $ProjectDir '.git')){ Say "  $ProjectDir already has the project - restore skipped." 'Yellow' }
  else { & $restore -Package $Package -Target $ProjectDir }
}

# ---------- 4. final report ----------
Step 'Final check'
RefreshPath
foreach($t in 'git','node','npm','dotnet','adb','sqlcmd'){ $c = Cmd $t; Say ("  {0,-7} {1}" -f $t, $(if($c){$c.Source}else{'not found (open a NEW PowerShell window if just installed)'})) }
Say "`nDone. Open a NEW PowerShell window so the updated PATH and environment variables apply. Log: $Log" 'Green'
Say 'Manual steps: install the Claude desktop app (and Codex) from their websites; copy claude-memory as described in MIGRATION.md; on the Tab S7 accept "Allow USB debugging" for this PC.'
