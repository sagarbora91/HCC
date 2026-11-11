<#
.SYNOPSIS
  Builds the two HCC Windows installers (wave INST, lane build):
    HCC-Client-Setup-<ver>.exe   the small client setup (trusts the hotel certificate, puts the HCC icon on the desktop)
    HCC-Server-Setup-<ver>-x64.exe  the server installer (self-contained server, web page, scripts, optional SQL Server media)
  plus SHA256SUMS.txt, all in -OutDir (default E:\Projects\HCC\Installers, outside the repository, never committed).

.DESCRIPTION
  Order: find ISCC (a missing Inno Setup is an ERROR) -> check the SQL media -> production web page (npm run build:production in
  03 Prototype) -> self-contained win-x64 publish of Hcc.Api -> copy the web page into <publish>\web\index.html -> compile the CLIENT
  setup -> compile the SERVER installer (it embeds the client setup) -> optional signing -> SHA256SUMS.txt.
  Any failing step stops the build with exit code 1 and a plain "BUILD FAILED" message. Nothing is installed or changed on this PC:
  the script only writes into -OutDir and the work folder.

  /D defines passed to ISCC (the .iss files of lanes server and client use exactly these names):
    AppVersion      x.y.z
    PublishDir      self-contained publish output, including web\index.html
    OpsDir          deployment\windows (the .ps1 scripts, samples, INSTALL.md)
    TemplatesDir    08 Production\deployment\import-templates
    SqlMediaDir     the SQL Server Express media folder; NOT defined when the SQL payload is skipped (use #ifdef SqlMediaDir)
    ClientSetupExe  full path of the client setup exe just built (the server installer ships it inside the client kit)
    OutputDir       where the compiled setup is written (-OutDir)

  Unsigned: no code-signing certificate exists yet. A clear note is printed; -SignThumbprint signs both exes with signtool.

.PARAMETER Version        x.y.z; default: <Version> in 08 Production\Directory.Build.props.
.PARAMETER SqlPayloadDir  Folder with SQLEXPR*_x64_*.exe, MsSqlCmdLnUtils.msi and msodbcsql17.msi. Default: the ETP payload folder
                          (E:\ETP\Installers\SQL payload for the ETP installer (2025 + ODBC 17)) when it exists.
.PARAMETER SkipSqlPayload Build a server installer without SQL media (it then cannot install SQL Server itself).
.PARAMETER OutDir         Output folder. Default E:\Projects\HCC\Installers.
.PARAMETER DryRun         Print every step and the ISCC command lines; build nothing, create nothing, change nothing.
.PARAMETER SignThumbprint Certificate thumbprint for signtool (optional).
.PARAMETER TimestampUrl   RFC 3161 timestamp server used with -SignThumbprint.
.PARAMETER InnoSetupCompiler  ISCC.exe or its folder; default: PATH, registry, default folders.
.PARAMETER ServerIss      Default installer\Hcc.iss next to this script.
.PARAMETER ClientIss      Default installer\HccClient.iss next to this script.
.PARAMETER WorkDir        Scratch folder for the publish output. Default %TEMP%\HccInstallerBuild.
.PARAMETER PublishDir     Use an already built publish folder (needs Hcc.Api.exe, hostfxr.dll, web\index.html); skips web build and publish.
.PARAMETER LockScript     Path of Invoke-Serialized.ps1: npm and dotnet then run through the heavy-job lock (exit 75 = busy = build fails).
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$SqlPayloadDir,
    [switch]$SkipSqlPayload,
    [string]$OutDir = 'E:\Projects\HCC\Installers',
    [switch]$DryRun,
    [string]$SignThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
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

# ---- helpers (ISCC lookup ported from ETP Resolve-EtpInnoSetupCompiler) ---------------------------------------------------------
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

# Runs a heavy command (npm, dotnet) in a folder: through the heavy-job lock when -LockScript is given, else directly.
function Invoke-HccHeavy {
    param([string]$Directory, [string]$Command, [string]$What)
    Write-Host "  > ($Directory) $Command"
    if ($LockScript) {
        $pwsh = Get-Command -Name 'pwsh' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $pwsh) { throw 'pwsh (PowerShell 7) is needed to call the heavy-job lock but was not found on PATH.' }
        & $pwsh.Source -NoProfile -File $LockScript -WorkingDirectory $Directory -Label 'inst-build' -Command $Command
        $code = $LASTEXITCODE
        if ($code -eq 75) { throw "$What did not run: the heavy-job lock is busy (exit 75). Run the build again later." }
    } else {
        Push-Location -LiteralPath $Directory
        try {
            $global:LASTEXITCODE = 0
            Invoke-Expression $Command
            $code = $LASTEXITCODE
        } finally { Pop-Location }
    }
    if ($code -ne 0) { throw "$What failed (exit code $code)." }
}

function Invoke-HccIscc {
    param([string]$Iscc, [string]$Script, [string[]]$Defines, [string]$What)
    $argList = @($Defines) + @($Script)
    Write-Host ("  > ISCC {0}" -f ($argList -join ' '))
    $global:LASTEXITCODE = 0
    & $Iscc @argList
    if ($LASTEXITCODE -ne 0) { throw "$What failed: Inno Setup compile exit code $LASTEXITCODE." }
}

function Invoke-HccSign {
    param([string]$SignTool, [string]$File)
    Write-Host "  > signtool sign $File"
    $global:LASTEXITCODE = 0
    & $SignTool sign /sha1 $SignThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $File
    if ($LASTEXITCODE -ne 0) { throw "Signing failed for $File (signtool exit code $LASTEXITCODE)." }
}

function Step { param([string]$Text) Write-Host ''; Write-Host "== $Text" }

# ---- main -----------------------------------------------------------------------------------------------------------------------
try {
    $winDir = Split-Path -Parent $PSScriptRoot                              # ...\deployment\windows
    $prod = [IO.Path]::GetFullPath((Join-Path $winDir '..\..'))             # ...\08 Production
    $repo = Split-Path -Parent $prod
    $proto = Join-Path $repo '03 Prototype'
    $templates = [IO.Path]::GetFullPath((Join-Path $winDir '..\import-templates'))
    if (-not $ServerIss) { $ServerIss = Join-Path $PSScriptRoot 'Hcc.iss' }
    if (-not $ClientIss) { $ClientIss = Join-Path $PSScriptRoot 'HccClient.iss' }
    if (-not $WorkDir) { $WorkDir = Join-Path ([IO.Path]::GetTempPath()) 'HccInstallerBuild' }
    $OutDir = [IO.Path]::GetFullPath($OutDir)
    $WorkDir = [IO.Path]::GetFullPath($WorkDir)

    Step 'Version'
    if (-not $Version) {
        $propsFile = Join-Path $prod 'Directory.Build.props'
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
    Write-Host "ISCC: $iscc"

    Step 'SQL Server media'
    $sqlMedia = $null
    if ($SkipSqlPayload) {
        Write-Warning 'SQL payload skipped: the server installer will NOT be able to install SQL Server by itself.'
    } else {
        $dir = if ($SqlPayloadDir) { $SqlPayloadDir } else { $DefaultSqlPayload }
        if (-not (Test-Path -LiteralPath $dir -PathType Container)) {
            throw "SQL media folder not found: $dir. Pass -SqlPayloadDir <folder> or -SkipSqlPayload."
        }
        $sqlMedia = [IO.Path]::GetFullPath($dir)
        if (-not (Get-ChildItem -LiteralPath $sqlMedia -Filter 'SQLEXPR*_x64_*.exe' -File -ErrorAction SilentlyContinue)) { throw "No SQL Server Express package (SQLEXPR*_x64_*.exe) in: $sqlMedia" }
        foreach ($n in @('MsSqlCmdLnUtils.msi', 'msodbcsql17.msi')) {
            if (-not (Test-Path -LiteralPath (Join-Path $sqlMedia $n) -PathType Leaf)) { throw "$n is missing in the SQL media folder: $sqlMedia" }
        }
        Write-Host "SQL media: $sqlMedia"
    }

    Step 'Signing'
    $signTool = $null
    if ($SignThumbprint) {
        $signTool = Resolve-HccSignTool
        Write-Host "Will sign both setup programs with certificate $SignThumbprint using $signTool"
    } else {
        Write-Warning 'UNSIGNED build: no code-signing certificate was given (-SignThumbprint). Windows SmartScreen will warn when the setup is run; this is expected in this wave.'
    }

    $publishOut = if ($PublishDir) { [IO.Path]::GetFullPath($PublishDir) } else { Join-Path $WorkDir 'publish' }
    $clientExe = Join-Path $OutDir ("HCC-Client-Setup-$Version.exe")
    $serverExe = Join-Path $OutDir ("HCC-Server-Setup-$Version-x64.exe")
    $sumsFile = Join-Path $OutDir 'SHA256SUMS.txt'
    $apiProject = Join-Path $prod 'src\Hcc.Api\Hcc.Api.csproj'
    $webSource = Join-Path $proto 'dist\production\index.html'

    $common = @("/DAppVersion=$Version", "/DOutputDir=$OutDir", "/O$OutDir")
    $clientDefines = $common + @("/DOpsDir=$winDir")
    $serverDefines = $common + @("/DPublishDir=$publishOut", "/DOpsDir=$winDir", "/DTemplatesDir=$templates", "/DClientSetupExe=$clientExe")
    if ($sqlMedia) { $serverDefines += "/DSqlMediaDir=$sqlMedia" }

    if ($DryRun) {
        Step 'DRY RUN (nothing is built, created or changed)'
        Write-Host "Output folder : $OutDir"
        Write-Host "Work folder   : $WorkDir"
        Write-Host "Client script : $ClientIss  $(if (Test-Path -LiteralPath $ClientIss) { '(present)' } else { '(NOT PRESENT YET: a real build fails here)' })"
        Write-Host "Server script : $ServerIss  $(if (Test-Path -LiteralPath $ServerIss) { '(present)' } else { '(NOT PRESENT YET: a real build fails here)' })"
        if ($PublishDir) {
            Write-Host "1. use the existing publish folder $publishOut (no web build, no publish)"
        } else {
            Write-Host "1. ($proto) npm run build:production   -> $webSource"
            Write-Host "2. ($prod) dotnet publish `"$apiProject`" -c Release -r win-x64 --self-contained true -p:Version=$Version -o `"$publishOut`""
            Write-Host "3. copy $webSource -> $(Join-Path $publishOut 'web\index.html')"
        }
        Write-Host '4. check the publish folder: Hcc.Api.exe, hostfxr.dll (self-contained), web\index.html'
        Write-Host "5. ISCC (client first) : $iscc $(($clientDefines + @($ClientIss)) -join ' ')"
        if ($signTool) { Write-Host "   sign $clientExe" }
        Write-Host "6. ISCC (server)       : $iscc $(($serverDefines + @($ServerIss)) -join ' ')"
        if ($signTool) { Write-Host "   sign $serverExe" }
        Write-Host "7. write $sumsFile (SHA-256 of both setup programs)"
        Write-Host 'DRY RUN finished: nothing was changed.'
        exit 0
    }

    foreach ($f in @($ClientIss, $ServerIss)) {
        if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { throw "Installer script not found: $f" }
    }
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
    $env:HCC_RELEASE = 'true'

    if ($PublishDir) {
        Step "Using existing publish folder $publishOut"
    } else {
        Step 'Production web page (npm run build:production)'
        if (Test-Path -LiteralPath $webSource) { Remove-Item -LiteralPath $webSource -Force }
        Invoke-HccHeavy -Directory $proto -Command 'npm run build:production' -What 'The production web build'
        if (-not (Test-Path -LiteralPath $webSource -PathType Leaf)) { throw "The web build did not produce $webSource." }

        Step 'Self-contained server publish (win-x64)'
        if (Test-Path -LiteralPath $publishOut) {
            if (-not $publishOut.StartsWith($WorkDir, [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to clean $publishOut (outside the work folder)." }
            Remove-Item -LiteralPath $publishOut -Recurse -Force
        }
        New-Item -ItemType Directory -Path $publishOut -Force | Out-Null
        Invoke-HccHeavy -Directory $prod -Command ("dotnet publish `"$apiProject`" -c Release -r win-x64 --self-contained true -p:Version=$Version -o `"$publishOut`"") -What 'dotnet publish'
        $webDest = Join-Path $publishOut 'web'
        New-Item -ItemType Directory -Path $webDest -Force | Out-Null
        Copy-Item -LiteralPath $webSource -Destination (Join-Path $webDest 'index.html') -Force
    }

    Step 'Checking the publish folder'
    foreach ($n in @('Hcc.Api.exe', 'hostfxr.dll', 'web\index.html')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishOut $n) -PathType Leaf)) { throw "The publish folder lacks $n ($publishOut). hostfxr.dll proves a self-contained publish (Install-HccService.ps1 then needs no .NET install)." }
    }
    Write-Host 'Hcc.Api.exe, hostfxr.dll (self-contained) and web\index.html are present.'

    Step 'Client setup'
    if (Test-Path -LiteralPath $clientExe) { Remove-Item -LiteralPath $clientExe -Force }
    Invoke-HccIscc -Iscc $iscc -Script $ClientIss -Defines $clientDefines -What 'The client setup'
    if (-not (Test-Path -LiteralPath $clientExe -PathType Leaf)) { throw "ISCC finished but $clientExe does not exist (the .iss must write OutputBaseFilename=HCC-Client-Setup-{#AppVersion})." }
    if ($signTool) { Invoke-HccSign -SignTool $signTool -File $clientExe }

    Step 'Server installer'
    if (Test-Path -LiteralPath $serverExe) { Remove-Item -LiteralPath $serverExe -Force }
    Invoke-HccIscc -Iscc $iscc -Script $ServerIss -Defines $serverDefines -What 'The server installer'
    if (-not (Test-Path -LiteralPath $serverExe -PathType Leaf)) { throw "ISCC finished but $serverExe does not exist (the .iss must write OutputBaseFilename=HCC-Server-Setup-{#AppVersion}-x64)." }
    if ($signTool) { Invoke-HccSign -SignTool $signTool -File $serverExe }

    Step 'SHA256SUMS.txt'
    $lines = foreach ($f in @($serverExe, $clientExe)) {
        $h = (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash.ToLowerInvariant()
        '{0}  {1}' -f $h, (Split-Path -Leaf $f)
    }
    Set-Content -LiteralPath $sumsFile -Value $lines -Encoding ASCII
    $lines | ForEach-Object { Write-Host $_ }

    Write-Host ''
    Write-Host 'BUILD OK'
    Write-Host "  $serverExe"
    Write-Host "  $clientExe"
    Write-Host "  $sumsFile"
    if (-not $SignThumbprint) { Write-Warning 'These setup programs are UNSIGNED.' }
    exit 0
} catch {
    [Console]::Error.WriteLine('')
    [Console]::Error.WriteLine("BUILD FAILED: $($_.Exception.Message)")
    exit 1
}
