<#
.SYNOPSIS
  Builds the two HCC Windows installers (wave INST, lane build):
    HCC-Client-Setup-<ver>.exe      the small client setup (trusts the hotel certificate, puts the HCC icon on the desktop)
    HCC-Server-Setup-<ver>-x64.exe  the server installer (self-contained server, web page, scripts, optional SQL Server media)
  plus SHA256SUMS.txt and BUILDINFO.txt, all in -OutDir (default E:\Projects\HCC\Installers, outside the repository, never committed).

.DESCRIPTION
  Provenance: the build refuses a dirty git tree (-AllowDirty overrides, loudly). By default it builds from a throw-away detached
  git worktree of the current commit under the work folder, so the eight packages.lock.json files of the real tree are never
  rewritten and only committed files can ship. The worktree is removed in a finally.
  Order: find ISCC (a missing Inno Setup is an ERROR) -> stage + hash the SQL media -> worktree -> production web page
  (npm run build:production in 03 Prototype) -> self-contained win-x64 publish -> web page into <publish>\web\index.html ->
  sanity check -> compile the CLIENT setup -> compile the SERVER installer (embeds the client setup) -> optional signing ->
  SHA256SUMS.txt + BUILDINFO.txt -> only then move everything into -OutDir. All compiling, signing and hashing happens in
  <WorkDir>\out: a failed build leaves -OutDir without a finished-looking file or a stale sums file.
  Any failing step stops the build with exit code 1 and "BUILD FAILED". Nothing is installed or changed on this PC.

  /D defines passed to ISCC (the .iss files of lanes server and client use exactly these names):
    AppVersion      x.y.z (both installers)
    OutputDir       <WorkDir>\out (also given as /O) (both)
    OpsDir          deployment\windows of the built commit (both)
    PublishDir      self-contained publish output, including web\index.html (server)
    TemplatesDir    08 Production\deployment\import-templates (server)
    SqlMediaDir     staged folder with exactly SQLEXPR_x64_ENU.exe, MsSqlCmdLnUtils.msi, msodbcsql17.msi, hashes checked against
                    sql-media.manifest.json; NOT defined with -SkipSqlPayload (use #ifdef SqlMediaDir) (server)
    ClientSetupExe  full path of the client setup exe just built (server)

  Unsigned: no code-signing certificate exists yet. A clear note is printed; -SignThumbprint signs both exes with signtool.

.PARAMETER Version        x.y.z; default: <Version> in 08 Production\Directory.Build.props.
.PARAMETER SqlPayloadDir  Folder with SQLEXPR_x64_ENU.exe, MsSqlCmdLnUtils.msi and msodbcsql17.msi. Default: the ETP payload folder.
.PARAMETER SkipSqlPayload Build a server installer without SQL media (it then cannot install SQL Server itself).
.PARAMETER OutDir         Output folder. Default E:\Projects\HCC\Installers.
.PARAMETER DryRun         Print every step and the ISCC command lines; build nothing, create nothing, change nothing.
.PARAMETER AllowDirty     Build although the git tree has uncommitted changes, in place (no worktree). Loud warning; the real tree's lock files get rewritten.
.PARAMETER SignThumbprint Certificate thumbprint for signtool (optional).
.PARAMETER TimestampUrl   RFC 3161 timestamp server used with -SignThumbprint.
.PARAMETER SignToolPath   signtool.exe to use (default: PATH, then Windows Kits).
.PARAMETER InnoSetupCompiler  ISCC.exe or its folder; default: PATH, registry, default folders.
.PARAMETER ServerIss      Default installer\Hcc.iss of the built commit.
.PARAMETER ClientIss      Default installer\HccClient.iss of the built commit.
.PARAMETER WorkDir        Scratch folder. Default %TEMP%\HccInstallerBuild. Its publish, sqlmedia, out, tmp and src folders are emptied at the start.
.PARAMETER PublishDir     Use an already built publish folder (copied through a filter; skips web build and publish). For tests.
.PARAMETER LockScript     Path of Invoke-Serialized.ps1: npm and dotnet then run through the heavy-job lock (exit 75 = busy = build fails).
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$SqlPayloadDir,
    [switch]$SkipSqlPayload,
    [string]$OutDir = 'E:\Projects\HCC\Installers',
    [switch]$DryRun,
    [switch]$AllowDirty,
    [string]$SignThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [string]$SignToolPath,
    [string]$InnoSetupCompiler,
    [string]$ServerIss,
    [string]$ClientIss,
    [string]$WorkDir,
    [string]$PublishDir,
    [string]$LockScript
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$DefaultSqlPayload = 'E:\ETP\Installers\SQL payload for the ETP installer (2025 + ODBC 17)'
$script:HeavyCounter = 0

# ---- helpers ----------------------------------------------------------------------------------------------------------------------
function Step { param([string]$Text) Write-Host ''; Write-Host "== $Text" }

function Get-HccFullPath {
    param([string]$Path)
    $p = [IO.Path]::GetFullPath($Path)
    if ($p.Length -gt 3) { $p = $p.TrimEnd('\') }
    return $p
}

# Runs a native command with stderr tolerated (Windows PowerShell 5.1 turns native stderr into errors under 'Stop'); returns code + lines.
function Invoke-HccNative {
    param([string]$File, [string[]]$Arguments, [switch]$Show)
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $global:LASTEXITCODE = 0
        $lines = @()
        & $File @Arguments 2>&1 | ForEach-Object { $t = "$_"; $lines += $t; if ($Show) { Write-Host $t } }
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $old }
    return [pscustomobject]@{ Code = $code; Lines = $lines }
}

function Format-HccArg { param([string]$A) if ($A -match '\s') { return '"' + $A + '"' } return $A }

# Deletes a folder inside the work folder only (never anything else); unlinks junctions instead of following them.
function Remove-HccWorkTree {
    param([string]$Path, [string]$Root)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $full = Get-HccFullPath $Path
    if (-not $full.StartsWith($Root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to delete $full (outside the work folder $Root)." }
    Get-ChildItem -LiteralPath $full -Recurse -Force -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } |
        ForEach-Object { try { [IO.Directory]::Delete($_.FullName, $false) } catch { Write-Verbose "could not unlink $($_.FullName)" } }
    Remove-Item -LiteralPath $full -Recurse -Force
}

# ISCC lookup ported from ETP Resolve-EtpInnoSetupCompiler.
function Resolve-HccInnoSetupCompiler {
    param([string]$Requested)
    if ($Requested) {
        $candidate = [IO.Path]::GetFullPath($Requested)
        if (Test-Path -LiteralPath $candidate -PathType Container) { $candidate = Join-Path $candidate 'ISCC.exe' }
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "The Inno Setup compiler was not found at: $candidate" }
        return $candidate
    }
    $searched = New-Object System.Collections.Generic.List[string]
    $searched.Add('PATH (ISCC.exe)')
    $onPath = Get-Command -Name 'ISCC.exe' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($onPath) { return $onPath.Source }
    $folders = New-Object System.Collections.Generic.List[string]
    foreach ($key in @(
            'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1')) {
        $item = Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
        if ($item -and $item.PSObject.Properties['InstallLocation'] -and $item.InstallLocation) { $folders.Add([string]$item.InstallLocation) }
    }
    if ($env:LOCALAPPDATA) { $folders.Add((Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6')) }
    foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) { if ($base) { $folders.Add((Join-Path $base 'Inno Setup 6')) } }
    foreach ($folder in $folders) {
        $candidate = Join-Path $folder 'ISCC.exe'
        $searched.Add($candidate)
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw ('Inno Setup 6 is required and was not found. Pass -InnoSetupCompiler with the path of ISCC.exe or its folder, put that folder on PATH, or install it with: winget install JRSoftware.InnoSetup. Searched: ' + ($searched -join '; '))
}

function Resolve-HccSignTool {
    if ($SignToolPath) {
        if (-not (Test-Path -LiteralPath $SignToolPath -PathType Leaf)) { throw "signtool not found at $SignToolPath" }
        return (Get-HccFullPath $SignToolPath)
    }
    $onPath = Get-Command -Name 'signtool.exe' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($onPath) { return $onPath.Source }
    foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if (-not $base) { continue }
        $kits = Join-Path $base 'Windows Kits\10\bin'
        if (Test-Path -LiteralPath $kits) {
            $hit = Get-ChildItem -LiteralPath $kits -Filter 'signtool.exe' -Recurse -File -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
            if ($hit) { return $hit.FullName }
        }
    }
    throw 'signtool.exe was not found (install the Windows SDK) but -SignThumbprint was given. Nothing was signed.'
}

# Runs a heavy command (npm, dotnet): written to a temporary .ps1 (quotes survive Windows PowerShell 5.1), then run directly or
# through the heavy-job lock when -LockScript is given.
function Invoke-HccHeavy {
    param([string]$Directory, [string]$Command, [string]$What)
    Write-Host "  > ($Directory) $Command"
    $script:HeavyCounter++
    $tmp = Join-Path $script:TmpDir ("heavy-{0}.ps1" -f $script:HeavyCounter)
    $body = "`$ErrorActionPreference = 'Continue'`r`nSet-Location -LiteralPath '$($Directory.Replace("'", "''"))'`r`n$Command`r`nexit `$LASTEXITCODE`r`n"
    Set-Content -LiteralPath $tmp -Value $body -Encoding UTF8
    if ($LockScript) {
        $pwsh = Get-Command -Name 'pwsh' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $pwsh) { throw 'pwsh (PowerShell 7) is needed to call the heavy-job lock but was not found on PATH.' }
        $lockCmd = "& '" + $tmp.Replace("'", "''") + "'; exit `$LASTEXITCODE"
        $global:LASTEXITCODE = 0
        & $pwsh.Source -NoProfile -File $LockScript -WorkingDirectory $Directory -Label 'inst-build' -Command $lockCmd
        $code = $LASTEXITCODE
        if ($code -eq 75) { throw "$What did not run: the heavy-job lock is busy (exit 75). Run the build again later." }
    } else {
        $global:LASTEXITCODE = 0
        & $tmp
        $code = $LASTEXITCODE
    }
    if ($code -ne 0) { throw "$What failed (exit code $code)." }
}

function Invoke-HccIscc {
    param([string]$Iscc, [string]$Script, [string[]]$Defines, [string]$What)
    $argList = @($Defines) + @($Script)
    Write-Host ("  > ISCC {0}" -f (($argList | ForEach-Object { Format-HccArg $_ }) -join ' '))
    $r = Invoke-HccNative -File $Iscc -Arguments $argList -Show
    if ($r.Code -ne 0) { throw "$What failed: Inno Setup compile exit code $($r.Code)." }
}

function Invoke-HccSign {
    param([string]$SignTool, [string]$File)
    Write-Host "  > signtool sign $File"
    $r = Invoke-HccNative -File $SignTool -Arguments @('sign', '/sha1', $SignThumbprint, '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256', $File) -Show
    if ($r.Code -ne 0) { throw "Signing failed for $File (signtool exit code $($r.Code)). Nothing was moved to the output folder." }
}

# Copies a caller-given publish folder into the fresh work publish folder, dropping everything that must never ship.
function Copy-HccPublishFiltered {
    param([string]$Source, [string]$Dest)
    $srcFull = Get-HccFullPath $Source
    $dropped = New-Object System.Collections.Generic.List[string]
    $blockedDirs = @('logs', 'certs', 'keys')
    foreach ($f in Get-ChildItem -LiteralPath $srcFull -Recurse -File -Force) {
        $rel = $f.FullName.Substring($srcFull.Length).TrimStart('\')
        $segments = $rel.Split('\')
        $drop = $false
        for ($i = 0; $i -lt $segments.Count - 1; $i++) { if ($blockedDirs -contains $segments[$i].ToLowerInvariant()) { $drop = $true } }
        $n = $f.Name.ToLowerInvariant()
        if ($n -like '*.pfx' -or $n -like '*.p12' -or $n -like '*.key' -or $n -like '*.pem' -or $n -like '*.log' -or $n -eq 'hcc-ops.json') { $drop = $true }
        if ($n -like 'appsettings.*.json') { $drop = $true }
        if ($drop) { $dropped.Add($rel); continue }
        $target = Join-Path $Dest $rel
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $f.FullName -Destination $target -Force
    }
    if ($dropped.Count) { Write-Warning ("Dropped {0} file(s) that must never ship from the given publish folder, e.g. {1}" -f $dropped.Count, (($dropped | Select-Object -First 5) -join ', ')) }
}

function Remove-HccGitWorktree {
    param([string]$Repo, [string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    # junction first (node_modules), never follow it
    $nm = Join-Path $Path '03 Prototype\node_modules'
    if (Test-Path -LiteralPath $nm) {
        $item = Get-Item -LiteralPath $nm -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { [IO.Directory]::Delete($nm, $false) }
    }
    Invoke-HccNative -File $script:Git -Arguments @('-C', $Repo, 'worktree', 'remove', '--force', $Path) | Out-Null
    if (Test-Path -LiteralPath $Path) { Remove-HccWorkTree -Path $Path -Root $script:WorkRoot }
    Invoke-HccNative -File $script:Git -Arguments @('-C', $Repo, 'worktree', 'prune') | Out-Null
}

# ---- main -----------------------------------------------------------------------------------------------------------------------
$savedRelease = $env:HCC_RELEASE
$worktreePath = $null
$repo = $null
$exitCode = 1
try {
    $realWin = Split-Path -Parent $PSScriptRoot                                  # ...\deployment\windows
    $realProd = [IO.Path]::GetFullPath((Join-Path $realWin '..\..'))             # ...\08 Production
    $repo = Split-Path -Parent $realProd
    if (-not $WorkDir) { $WorkDir = Join-Path ([IO.Path]::GetTempPath()) 'HccInstallerBuild' }
    $OutDir = Get-HccFullPath $OutDir
    $WorkDir = Get-HccFullPath $WorkDir
    $script:WorkRoot = $WorkDir
    $script:TmpDir = Join-Path $WorkDir 'tmp'
    $stageOut = Join-Path $WorkDir 'out'
    $publishOut = Join-Path $WorkDir 'publish'
    $sqlStage = Join-Path $WorkDir 'sqlmedia'
    $srcRoot = Join-Path $WorkDir 'src'

    Step 'Source control'
    $gitCmd = Get-Command -Name 'git' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $gitCmd) { throw 'git was not found on PATH (needed to record and check the commit being built).' }
    $script:Git = $gitCmd.Source
    $r = Invoke-HccNative -File $script:Git -Arguments @('-C', $repo, 'rev-parse', 'HEAD')
    if ($r.Code -ne 0) { throw "git rev-parse failed in $repo." }
    $commit = ($r.Lines | Select-Object -First 1).Trim()
    $r = Invoke-HccNative -File $script:Git -Arguments @('-C', $repo, 'branch', '--show-current')
    $branch = if ($r.Code -eq 0 -and $r.Lines.Count -and $r.Lines[0].Trim()) { $r.Lines[0].Trim() } else { '(detached)' }
    $r = Invoke-HccNative -File $script:Git -Arguments @('-C', $repo, 'status', '--porcelain')
    if ($r.Code -ne 0) { throw 'git status failed.' }
    $dirtyLines = @($r.Lines | Where-Object { $_.Trim() })
    $dirty = $dirtyLines.Count -gt 0
    Write-Host "Commit $commit on $branch; working tree $(if ($dirty) { 'DIRTY (' + $dirtyLines.Count + ' entries)' } else { 'clean' })"
    if ($dirty) {
        if ($AllowDirty) {
            Write-Warning 'DIRTY TREE: -AllowDirty was given. The build runs IN PLACE, uncommitted files can ship, and the real packages.lock.json files will be rewritten. Not a gated build.'
        } elseif ($DryRun) {
            Write-Warning 'The git tree is dirty: a real build would be REFUSED (commit the changes or pass -AllowDirty).'
        } else {
            throw "The git tree has uncommitted changes ($($dirtyLines.Count) entries, e.g. $($dirtyLines[0].Trim())). Commit them, or pass -AllowDirty (not a gated build)."
        }
    }
    $useWorktree = (-not $AllowDirty) -and (-not $DryRun)
    $buildRoot = if ($useWorktree) { $srcRoot } else { $repo }
    $prod = Join-Path $buildRoot '08 Production'
    $winDir = Join-Path $prod 'deployment\windows'
    $proto = Join-Path $buildRoot '03 Prototype'
    $templates = Join-Path $prod 'deployment\import-templates'
    if (-not $ServerIss) { $ServerIss = Join-Path $winDir 'installer\Hcc.iss' }
    if (-not $ClientIss) { $ClientIss = Join-Path $winDir 'installer\HccClient.iss' }
    $apiProject = Join-Path $prod 'src\Hcc.Api\Hcc.Api.csproj'
    $webSource = Join-Path $proto 'dist\production\index.html'

    Step 'Version'
    if (-not $Version) {
        $propsFile = Join-Path $realProd 'Directory.Build.props'
        if (-not (Test-Path -LiteralPath $propsFile)) { throw "Directory.Build.props not found: $propsFile" }
        [xml]$props = Get-Content -LiteralPath $propsFile
        $node = $props.SelectSingleNode('/Project/PropertyGroup/Version')
        if (-not $node -or -not $node.InnerText) { throw "No <Version> in $propsFile; pass -Version x.y.z." }
        $Version = $node.InnerText.Trim()
    }
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$Version' is not x.y.z." }
    Write-Host "Version $Version"

    Step 'Inno Setup compiler'
    $iscc = Resolve-HccInnoSetupCompiler -Requested $InnoSetupCompiler
    $isccVersion = (Get-Item -LiteralPath $iscc).VersionInfo.ProductVersion
    if (-not $isccVersion -or $isccVersion -eq '0.0.0.0') {
        $uninst = Join-Path (Split-Path -Parent $iscc) 'unins000.exe'
        $isccVersion = if (Test-Path -LiteralPath $uninst) { 'Inno Setup ' + (Get-Item -LiteralPath $uninst).VersionInfo.ProductVersion.Trim() } else { 'unknown' }
    }
    $isccSig = (Get-AuthenticodeSignature -LiteralPath $iscc).Status
    Write-Host "ISCC: $iscc  (version $isccVersion, signature $isccSig)"

    Step 'Signing'
    $signTool = $null
    if ($SignThumbprint) {
        $signTool = Resolve-HccSignTool
        Write-Host "Will sign both setup programs with certificate $SignThumbprint using $signTool"
    } else {
        Write-Warning 'UNSIGNED build: no code-signing certificate was given (-SignThumbprint). Windows SmartScreen will warn when the setup is run; this is expected in this wave.'
    }

    $clientExeName = "HCC-Client-Setup-$Version.exe"
    $serverExeName = "HCC-Server-Setup-$Version-x64.exe"
    $clientExe = Join-Path $stageOut $clientExeName
    $serverExe = Join-Path $stageOut $serverExeName
    $manifestFile = Join-Path $winDir 'sql-media.manifest.json'
    $sqlFiles = @('SQLEXPR_x64_ENU.exe', 'MsSqlCmdLnUtils.msi', 'msodbcsql17.msi')
    $sqlSource = $null
    if (-not $SkipSqlPayload) {
        $dir = if ($SqlPayloadDir) { $SqlPayloadDir } else { $DefaultSqlPayload }
        if (-not (Test-Path -LiteralPath $dir -PathType Container)) { throw "SQL media folder not found: $dir. Pass -SqlPayloadDir <folder> or -SkipSqlPayload." }
        $sqlSource = Get-HccFullPath $dir
    }

    $common = @("/DAppVersion=$Version", "/DOutputDir=$stageOut", "/O$stageOut", "/DOpsDir=$winDir")
    $clientDefines = $common
    $serverDefines = $common + @("/DPublishDir=$publishOut", "/DTemplatesDir=$templates", "/DClientSetupExe=$clientExe")
    if ($sqlSource) { $serverDefines += "/DSqlMediaDir=$sqlStage" }

    if ($DryRun) {
        Step 'DRY RUN (nothing is built, created or changed)'
        Write-Host "Output folder : $OutDir  (files are moved there only after everything succeeded)"
        Write-Host "Work folder   : $WorkDir  (publish, sqlmedia, out, tmp, src are emptied first)"
        Write-Host "Client script : $ClientIss  $(if (Test-Path -LiteralPath $ClientIss) { '(present)' } else { '(NOT PRESENT)' })"
        Write-Host "Server script : $ServerIss  $(if (Test-Path -LiteralPath $ServerIss) { '(present)' } else { '(NOT PRESENT)' })"
        if ($sqlSource) {
            Write-Host "0. stage the three SQL files from $sqlSource into $sqlStage and verify the staged copies against $manifestFile (missing manifest = error)"
            foreach ($n in $sqlFiles) { if (-not (Test-Path -LiteralPath (Join-Path $sqlSource $n))) { Write-Warning "SQL media file missing: $n" } }
            if (-not (Test-Path -LiteralPath $manifestFile)) { Write-Warning 'sql-media.manifest.json is missing: a real build fails.' }
        } else { Write-Warning 'SQL payload skipped: the server installer will not install SQL Server.' }
        if ($PublishDir) {
            Write-Host "1. copy the existing publish folder $PublishDir through the filter into $publishOut (no web build, no publish)"
        } else {
            Write-Host "1. git worktree add --detach `"$srcRoot`" $commit   (throw-away, removed in a finally); node_modules: npm ci, else a junction to the real one"
            Write-Host "2. ($proto) npm run build:production   -> $webSource"
            Write-Host "3. ($prod) dotnet publish `"$apiProject`" -c Release -r win-x64 --self-contained true -p:Version=$Version -o `"$publishOut`""
            Write-Host "4. copy the web page -> $(Join-Path $publishOut 'web\index.html')"
        }
        Write-Host '5. check the publish folder (Hcc.Api.exe, hostfxr.dll, web\index.html, service + Event Log DLLs) and the exe version'
        Write-Host ("6. ISCC (client first) : {0} {1}" -f (Format-HccArg $iscc), ((($clientDefines + @($ClientIss)) | ForEach-Object { Format-HccArg $_ }) -join ' '))
        if ($signTool) { Write-Host "   sign $clientExe" }
        Write-Host ("7. ISCC (server)       : {0} {1}" -f (Format-HccArg $iscc), ((($serverDefines + @($ServerIss)) | ForEach-Object { Format-HccArg $_ }) -join ' '))
        if ($signTool) { Write-Host "   sign $serverExe" }
        Write-Host "8. write SHA256SUMS.txt and BUILDINFO.txt in $stageOut, then move the 4 files into $OutDir"
        Write-Host 'DRY RUN finished: nothing was changed.'
        $exitCode = 0
        return
    }

    foreach ($f in @($ClientIss, $ServerIss)) {
        if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { if ($useWorktree) { } else { throw "Installer script not found: $f" } }
    }

    Step 'Preparing the work folder'
    New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null
    Remove-HccGitWorktree -Repo $repo -Path $srcRoot
    foreach ($p in @($stageOut, $publishOut, $sqlStage, $script:TmpDir)) { Remove-HccWorkTree -Path $p -Root $WorkDir }
    foreach ($p in @($stageOut, $script:TmpDir)) { New-Item -ItemType Directory -Path $p -Force | Out-Null }
    # a build that starts must not leave an old sums file or same-version exes in the output folder
    foreach ($n in @('SHA256SUMS.txt', 'BUILDINFO.txt', $clientExeName, $serverExeName)) {
        $old = Join-Path $OutDir $n
        if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force; Write-Host "Removed stale $old" }
    }
    $env:HCC_RELEASE = 'true'

    $nodeModulesMode = 'n/a (existing publish folder)'
    if ($useWorktree) {
        Step 'Throw-away worktree of the current commit'
        $r = Invoke-HccNative -File $script:Git -Arguments @('-C', $repo, 'worktree', 'add', '--detach', $srcRoot, $commit) -Show
        if ($r.Code -ne 0) { throw "git worktree add failed (exit $($r.Code))." }
        $worktreePath = $srcRoot
        foreach ($f in @($ClientIss, $ServerIss)) { if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { throw "Installer script not found in the built commit: $f" } }
    }

    $sqlIncluded = $false
    if ($sqlSource) {
        Step 'SQL Server media'
        foreach ($n in $sqlFiles) {
            if (-not (Test-Path -LiteralPath (Join-Path $sqlSource $n) -PathType Leaf)) { throw "$n is missing in the SQL media folder: $sqlSource" }
        }
        if (-not (Test-Path -LiteralPath $manifestFile -PathType Leaf)) { throw "sql-media.manifest.json is missing ($manifestFile): the installer's SQL step could not verify the media. Use -SkipSqlPayload to build without SQL media." }
        $manifest = Get-Content -Raw -LiteralPath $manifestFile | ConvertFrom-Json
        New-Item -ItemType Directory -Path $sqlStage -Force | Out-Null
        foreach ($n in $sqlFiles) { Copy-Item -LiteralPath (Join-Path $sqlSource $n) -Destination (Join-Path $sqlStage $n) -Force }
        foreach ($n in $sqlFiles) {
            $entry = @($manifest.files | Where-Object { $_.name -eq $n }) | Select-Object -First 1
            if (-not $entry) { throw "sql-media.manifest.json has no entry for $n." }
            $actual = (Get-FileHash -LiteralPath (Join-Path $sqlStage $n) -Algorithm SHA256).Hash
            if ($actual -ne ([string]$entry.sha256).ToUpperInvariant()) { throw "SQL media file $n does not match sql-media.manifest.json (SHA-256 of the staged copy differs). Wrong or damaged media: $sqlSource" }
        }
        Write-Host "The three staged SQL media files match sql-media.manifest.json ($sqlStage)."
        $sqlIncluded = $true
    } else {
        Write-Warning 'SQL payload skipped: the server installer will NOT be able to install SQL Server by itself.'
    }

    if ($PublishDir) {
        Step "Copying the given publish folder (filtered) to $publishOut"
        New-Item -ItemType Directory -Path $publishOut -Force | Out-Null
        Copy-HccPublishFiltered -Source $PublishDir -Dest $publishOut
    } else {
        Step 'Production web page (npm run build:production)'
        if ($useWorktree) {
            $nm = Join-Path $proto 'node_modules'
            try {
                Invoke-HccHeavy -Directory $proto -Command 'npm ci --prefer-offline --no-audit --no-fund' -What 'npm ci'
                $nodeModulesMode = 'npm ci in the throw-away worktree'
            } catch {
                if ($_.Exception.Message -match 'lock is busy') { throw }
                Write-Warning "npm ci did not work here ($($_.Exception.Message)); reusing the existing node_modules of the real tree through a junction."
                if (Test-Path -LiteralPath $nm) {
                    $item = Get-Item -LiteralPath $nm -Force
                    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { [IO.Directory]::Delete($nm, $false) } else { Remove-Item -LiteralPath $nm -Recurse -Force }
                }
                $realNm = Join-Path $repo '03 Prototype\node_modules'
                if (-not (Test-Path -LiteralPath $realNm)) { throw "No node_modules in the real tree ($realNm); run npm ci there first." }
                New-Item -ItemType Junction -Path $nm -Target $realNm | Out-Null
                $nodeModulesMode = 'junction to the real tree node_modules (npm ci not possible offline)'
            }
        } else { $nodeModulesMode = 'existing node_modules of the real tree (in place, -AllowDirty)' }
        if (Test-Path -LiteralPath $webSource) { Remove-Item -LiteralPath $webSource -Force }
        Invoke-HccHeavy -Directory $proto -Command 'npm run build:production' -What 'The production web build'
        if (-not (Test-Path -LiteralPath $webSource -PathType Leaf)) { throw "The web build did not produce $webSource." }

        Step 'Self-contained server publish (win-x64)'
        New-Item -ItemType Directory -Path $publishOut -Force | Out-Null
        Invoke-HccHeavy -Directory $prod -Command ("dotnet publish `"$apiProject`" -c Release -r win-x64 --self-contained true -p:Version=$Version -o `"$publishOut`"") -What 'dotnet publish'
        $webDest = Join-Path $publishOut 'web'
        New-Item -ItemType Directory -Path $webDest -Force | Out-Null
        Copy-Item -LiteralPath $webSource -Destination (Join-Path $webDest 'index.html') -Force
    }

    Step 'Checking the publish folder'
    foreach ($n in @('Hcc.Api.exe', 'hostfxr.dll', 'web\index.html', 'System.ServiceProcess.ServiceController.dll', 'System.Diagnostics.EventLog.dll', 'Microsoft.Extensions.Hosting.WindowsServices.dll', 'Microsoft.Extensions.Logging.EventLog.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishOut $n) -PathType Leaf)) { throw "The publish folder lacks $n ($publishOut). hostfxr.dll proves a self-contained publish (Install-HccService.ps1 then needs no .NET install); the service and Event Log DLLs are needed to run as a Windows service and write alerts." }
    }
    Write-Host 'Hcc.Api.exe, hostfxr.dll, web\index.html and the Windows-service / Event Log DLLs are present.'
    $exeInfo = (Get-Item -LiteralPath (Join-Path $publishOut 'Hcc.Api.exe')).VersionInfo
    if ($exeInfo.ProductVersion) {
        if (-not $exeInfo.ProductVersion.StartsWith($Version)) { throw "Hcc.Api.exe reports version $($exeInfo.ProductVersion), expected $Version." }
        Write-Host "Hcc.Api.exe product version: $($exeInfo.ProductVersion)"
    } else { Write-Host 'Hcc.Api.exe has no version resource (stub?).' }
    Write-Host 'Smoke start of Hcc.Api.exe skipped: the exe has no verb that exits without touching the system (only "migrate", which opens the database); the version resource check above is the smoke check.'
    $runtimes = 'not found'
    $rcFile = Join-Path $publishOut 'Hcc.Api.runtimeconfig.json'
    if (Test-Path -LiteralPath $rcFile) {
        $rc = Get-Content -Raw -LiteralPath $rcFile | ConvertFrom-Json
        $inc = $null
        if ($rc.PSObject.Properties['runtimeOptions'] -and $rc.runtimeOptions.PSObject.Properties['includedFrameworks']) { $inc = $rc.runtimeOptions.includedFrameworks }
        if ($inc) { $runtimes = (@($inc) | ForEach-Object { "$($_.name) $($_.version)" }) -join '; ' }
    }

    Step 'Client setup'
    Invoke-HccIscc -Iscc $iscc -Script $ClientIss -Defines $clientDefines -What 'The client setup'
    if (-not (Test-Path -LiteralPath $clientExe -PathType Leaf)) { throw "ISCC finished but $clientExe does not exist (the .iss must write OutputBaseFilename=HCC-Client-Setup-{#AppVersion})." }
    if ($signTool) { Invoke-HccSign -SignTool $signTool -File $clientExe }

    Step 'Server installer'
    Invoke-HccIscc -Iscc $iscc -Script $ServerIss -Defines $serverDefines -What 'The server installer'
    if (-not (Test-Path -LiteralPath $serverExe -PathType Leaf)) { throw "ISCC finished but $serverExe does not exist (the .iss must write OutputBaseFilename=HCC-Server-Setup-{#AppVersion}-x64)." }
    if ($signTool) { Invoke-HccSign -SignTool $signTool -File $serverExe }

    Step 'SHA256SUMS.txt and BUILDINFO.txt'
    $lines = foreach ($f in @($serverExe, $clientExe)) {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path -Leaf $f)
    }
    Set-Content -LiteralPath (Join-Path $stageOut 'SHA256SUMS.txt') -Value $lines -Encoding ASCII
    $lines | ForEach-Object { Write-Host $_ }
    $dotnetV = (Invoke-HccNative -File 'dotnet' -Arguments @('--version')).Lines | Select-Object -First 1
    $nodeV = (Invoke-HccNative -File 'node' -Arguments @('-v')).Lines | Select-Object -First 1
    $info = @(
        "HCC installers build information",
        "Version        : $Version",
        "Commit         : $commit",
        "Branch         : $branch",
        "Dirty          : $(if ($dirty) { 'yes (-AllowDirty)' } else { 'no' })",
        "Built from     : $(if ($useWorktree) { 'throw-away detached git worktree of the commit' } else { 'the working tree in place' })",
        "dotnet SDK     : $dotnetV",
        "Bundled runtime: $runtimes",
        "Node           : $nodeV",
        "node_modules   : $nodeModulesMode",
        "ISCC           : $iscc (version $isccVersion, signature $isccSig)",
        "SQL media      : $(if ($sqlIncluded) { 'included (hashes match sql-media.manifest.json)' } else { 'NOT included' })",
        "Signed         : $(if ($SignThumbprint) { 'yes, certificate ' + $SignThumbprint } else { 'unsigned' })",
        "Built (UTC)    : $([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss'))"
    )
    Set-Content -LiteralPath (Join-Path $stageOut 'BUILDINFO.txt') -Value $info -Encoding ASCII

    Step "Moving the finished files into $OutDir"
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
    foreach ($n in @($serverExeName, $clientExeName, 'SHA256SUMS.txt', 'BUILDINFO.txt')) {
        Move-Item -LiteralPath (Join-Path $stageOut $n) -Destination (Join-Path $OutDir $n) -Force
    }
    Write-Host ''
    Write-Host 'BUILD OK'
    Write-Host "  $(Join-Path $OutDir $serverExeName)"
    Write-Host "  $(Join-Path $OutDir $clientExeName)"
    Write-Host "  $(Join-Path $OutDir 'SHA256SUMS.txt')"
    Write-Host "  $(Join-Path $OutDir 'BUILDINFO.txt')"
    if (-not $SignThumbprint) { Write-Warning 'These setup programs are UNSIGNED.' }
    $exitCode = 0
} catch {
    [Console]::Error.WriteLine('')
    [Console]::Error.WriteLine("BUILD FAILED: $($_.Exception.Message)")
    $exitCode = 1
} finally {
    $env:HCC_RELEASE = $savedRelease
    if ($savedRelease -eq $null) { Remove-Item Env:\HCC_RELEASE -ErrorAction SilentlyContinue }
    if ($worktreePath -and $repo) {
        try { Remove-HccGitWorktree -Repo $repo -Path $worktreePath } catch { [Console]::Error.WriteLine("Warning: could not remove the throw-away worktree $worktreePath : $($_.Exception.Message)") }
    }
}
exit $exitCode
