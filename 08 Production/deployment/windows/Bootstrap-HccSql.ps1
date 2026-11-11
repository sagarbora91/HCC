<#
.SYNOPSIS
  Makes sure SQL Server Express (instance .\SQLEXPRESS) and sqlcmd are ready on this PC for the HCC installer (INST-CONTRACT 2, lane sql).
.DESCRIPTION
  -Mode UseExisting : the instance must already be on this PC (for example ETP's). It is ADOPTED: only read and (if its service is stopped) started.
                      Nothing of it is reconfigured, restarted or upgraded, and no database or login is touched.
                      NOTE: if this PC has no sqlcmd, -MediaDir must hold at least msodbcsql17.msi and MsSqlCmdLnUtils.msi (hash-checked);
                      without them the script stops with exit 1 and a plain message. The installer must therefore ship those two files
                      also when the owner chooses "use the SQL Server already on this PC".
  -Mode InstallBundled : if the instance is already there it is adopted exactly as above (a second HCC or ETP install never disturbs it);
                      otherwise SQL Server Express is installed silently from the offline media folder -MediaDir (Windows authentication only,
                      TCP/IP and named pipes off, Administrators as sysadmin), then ODBC Driver 17 and sqlcmd when the PC has no sqlcmd.
  Every media file is checked against sql-media.manifest.json (next to this script: names, sizes and SHA-256, computed from the ETP payload
  folder) before anything is run, and each file again immediately before it runs. A missing file, an unknown name, a wrong size or hash stops the
  script with exit 1 and nothing installed. The media folder must be owned and writable by SYSTEM / Administrators / TrustedInstaller only and must
  not be a link (the files run with the administrator's rights).
  sqlcmd is only taken from the SQL Server client folders under Program Files (ODBC 17, then 18) and only when those folders are admin-only.
  Exit codes: 0 the instance is ready and reachable with Windows authentication by this administrator; 2 Windows must be restarted first (nothing
  was installed) or an installer asked for a restart and the instance could not be used yet (run again afterwards; it is then adopted); 1 failed.
  -PlanOnly (and -WhatIf) print what would happen and change nothing: no log file, no folder, no process, no service start. The media hashes
  ARE read (read only) so that the plan can say whether the media would be accepted.
  -LogFile: default C:\ProgramData\HCC\Logs\install-<yyyyMMdd-HHmmss>.log. No secret is ever handled here.
  -WarningsFile: optional path. At the end of a real run (exit 0, 2 or 1) the script writes the warnings it logged as JSON the orchestrator can show on
  screen: { "warnings": [ { "code": "TcpAndMixedMode", "text": "..." }, ... ] }. Codes: StartTypeNotAutomatic, MixedMode, TcpEnabled,
  TcpAndMixedMode, NamedPipesEnabled, OtherSysadmins, ServiceDisabledRunning, RestartRequested. Nothing is written in -PlanOnly.
  The new process environment gets sqlcmd's folder in front of PATH; a calling script that starts further child processes should refresh its own PATH.
  Microsoft licence: running this in InstallBundled mode when the instance is absent accepts the SQL Server and ODBC/sqlcmd licence terms (the
  installer wizard must say so before it gets here: see INSTALL.md).
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('InstallBundled', 'UseExisting')][string]$Mode,
    [string]$MediaDir,
    [switch]$PlanOnly,
    [string]$LogFile,
    [string]$ManifestPath,
    [string]$WarningsFile
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Hcc-Common.ps1')
Set-StrictMode -Version Latest   # Hcc-Common.ps1 relaxes it to 2.0 for itself

$script:HccSqlInstanceName = 'SQLEXPRESS'
$script:HccSqlServiceName = 'MSSQL$SQLEXPRESS'
$script:HccSqlInstance = '.\SQLEXPRESS'
$script:HccSqlMinMajorVersion = 16   # SQL Server 2022 or newer, the same floor as ETP
$script:HccSqlWarnings = New-Object System.Collections.Generic.List[object]

# --- media manifest -----------------------------------------------------------------------------------------------------------
function Get-HccSqlMediaManifest {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "The media manifest was not found: $Path" }
    $m = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    $files = @($m.files)
    if ($files.Count -eq 0) { throw 'The media manifest lists no files.' }
    foreach ($f in $files) {
        if ($f.name -notmatch '^[A-Za-z0-9._-]{1,80}$') { throw "The media manifest has an unusable file name '$($f.name)'." }
        if ($f.sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw "The media manifest has no valid SHA-256 for '$($f.name)'." }
    }
    return $files
}

function Get-HccSqlMediaEntry {
    param([Parameter(Mandatory)][object[]]$Manifest, [Parameter(Mandatory)][string]$Role)
    return (@($Manifest | Where-Object { $_.role -eq $Role }) | Select-Object -First 1)
}

# Checks one media file against its manifest entry (link, size when the manifest has one, SHA-256). Returns '' when fine, else the problem.
function Test-HccSqlMediaFile {
    param([Parameter(Mandatory)][string]$MediaDir, [Parameter(Mandatory)][object]$Entry)
    $file = Join-Path $MediaDir $Entry.name
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { return "Missing media file: $($Entry.name)." }
    $item = Get-Item -LiteralPath $file -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { return "Media file $($Entry.name) is a link and is refused." }
    if ($Entry.PSObject.Properties['bytes'] -and $Entry.bytes -and [int64]$Entry.bytes -ne $item.Length) { return "Media file $($Entry.name) has the wrong size ($($item.Length) bytes, expected $($Entry.bytes)). It was not run." }
    $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    if ($actual -ne ([string]$Entry.sha256).ToUpperInvariant()) { return "Media file $($Entry.name) is not the expected file (SHA-256 differs). It was not run." }
    return ''
}

# Checks the named roles' files in $MediaDir against the manifest. Returns the list of problems (empty = all good). Read only.
function Test-HccSqlMedia {
    param([Parameter(Mandatory)][string]$MediaDir, [Parameter(Mandatory)][object[]]$Manifest, [Parameter(Mandatory)][string[]]$Roles)
    $problems = @()
    if (-not (Test-Path -LiteralPath $MediaDir -PathType Container)) { return @("The SQL Server media folder was not found: $MediaDir") }
    foreach ($role in $Roles) {
        $entry = Get-HccSqlMediaEntry -Manifest $Manifest -Role $role
        if (-not $entry) { $problems += "The media manifest has no entry for '$role'."; continue }
        $p = Test-HccSqlMediaFile -MediaDir $MediaDir -Entry $entry
        if ($p) { $problems += $p }
    }
    return $problems
}

# Hashes the file again right before it runs (closes the gap between the first check and the use); throws on any difference.
function Assert-HccSqlMediaFileUnchanged {
    param([Parameter(Mandatory)][string]$MediaDir, [Parameter(Mandatory)][object[]]$Manifest, [Parameter(Mandatory)][string]$Role)
    $entry = Get-HccSqlMediaEntry -Manifest $Manifest -Role $Role
    if (-not $entry) { throw "The media manifest has no entry for '$Role'." }
    $p = Test-HccSqlMediaFile -MediaDir $MediaDir -Entry $entry
    if ($p) { throw $p }
    return (Join-Path $MediaDir $entry.name)
}

# --- what to do (pure; unit-tested) -------------------------------------------------------------------------------------------
# Outcome: Adopt (instance there), Install (instance absent, bundled), Refuse (UseExisting but absent), RestartNeeded (install wanted, Windows restart pending).
function Get-HccSqlBootstrapPlan {
    param([Parameter(Mandatory)][ValidateSet('InstallBundled', 'UseExisting')][string]$Mode, [bool]$ServiceExists, [bool]$SqlcmdPresent, [bool]$Odbc17Present, [string[]]$RestartReasons = @())
    $steps = New-Object System.Collections.Generic.List[string]
    $roles = New-Object System.Collections.Generic.List[string]
    $outcome = 'Adopt'; $reason = ''
    if ($ServiceExists) {
        $steps.Add('Adopt the existing SQL Server instance .\SQLEXPRESS (read only; start its service only if it is stopped; no setting, login or database is changed)')
    }
    elseif ($Mode -eq 'UseExisting') {
        $outcome = 'Refuse'
        $reason = 'There is no SQL Server instance named SQLEXPRESS on this PC. Choose "install the bundled SQL Server", or install SQL Server Express first.'
        $steps.Add('Stop: no SQLEXPRESS instance to use')
    }
    elseif (@($RestartReasons).Count -gt 0) {
        $outcome = 'RestartNeeded'
        $reason = 'Windows has a restart pending (' + (@($RestartReasons) -join ', ') + '). Restart the PC, then run the setup again. Nothing was installed.'
        $steps.Add('Stop: restart Windows first')
    }
    else {
        $outcome = 'Install'
        $roles.Add('engine')
        $steps.Add('Check SQLServer Express media (SQLEXPR_x64_ENU.exe) against the manifest')
        $steps.Add('Extract the media to a protected folder and run SQL Server setup silently: instance SQLEXPRESS, Windows authentication only, TCP/IP and named pipes off, Administrators as sysadmin')
    }
    if ($outcome -in @('Adopt', 'Install') -and -not $SqlcmdPresent) {
        if (-not $Odbc17Present) { $roles.Add('odbc17'); $steps.Add('Install Microsoft ODBC Driver 17 for SQL Server (msodbcsql17.msi)') }
        $roles.Add('sqlcmd'); $steps.Add('Install Microsoft Command Line Utilities for SQL Server (MsSqlCmdLnUtils.msi)')
    }
    if ($outcome -in @('Adopt', 'Install')) { $steps.Add('Check that .\SQLEXPRESS answers to Windows authentication and this administrator is a SQL administrator') }
    return [pscustomobject]@{ Outcome = $outcome; Reason = $reason; Steps = @($steps); Roles = @($roles | Select-Object -Unique) }
}

# SQL Server's silent setup arguments. Windows authentication only (no SECURITYMODE), no network protocols, the installing
# administrator reaches the instance through BUILTIN\Administrators (the same choice as ETP, so a shared instance looks alike).
function Get-HccSqlSetupArguments {
    return @('/ACTION=Install', '/QUIET', '/IACCEPTSQLSERVERLICENSETERMS', '/FEATURES=SQLENGINE', '/INSTANCENAME=SQLEXPRESS',
        '/SQLSYSADMINACCOUNTS=BUILTIN\Administrators', '/ADDCURRENTUSERASSQLADMIN=False', '/SQLCOLLATION=Latin1_General_CI_AS',
        '/TCPENABLED=0', '/NPENABLED=0', '/UPDATEENABLED=0')
}

# The probe query (read only) and its parser. The parser is pure: text in, object out; every match is read through its own [regex] object,
# so one match can never overwrite another's groups.
function Get-HccSqlProbeQuery {
    return "SET NOCOUNT ON; SELECT N'HCC_PROBE:'+CONVERT(nvarchar(5),COALESCE(IS_SRVROLEMEMBER('sysadmin'),0))+N'|'+CONVERT(nvarchar(10),COALESCE(SERVERPROPERTY('ProductMajorVersion'),0))+N'|'+CONVERT(nvarchar(5),COALESCE(SERVERPROPERTY('IsIntegratedSecurityOnly'),0)); " +
        "IF COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)=1 BEGIN " +
        "SELECT N'HCC_TCP:'+CONVERT(nvarchar(5),value_data) FROM sys.dm_server_registry WHERE value_name=N'Enabled' AND registry_key LIKE N'%\SuperSocketNetLib\Tcp'; " +
        "SELECT N'HCC_NP:'+CONVERT(nvarchar(5),value_data) FROM sys.dm_server_registry WHERE value_name=N'Enabled' AND registry_key LIKE N'%\SuperSocketNetLib\Np'; " +
        "SELECT N'HCC_ADMIN:'+m.name FROM sys.server_role_members rm JOIN sys.server_principals r ON r.principal_id=rm.role_principal_id JOIN sys.server_principals m ON m.principal_id=rm.member_principal_id WHERE r.name=N'sysadmin' AND m.type IN ('U','G') AND m.is_disabled=0; END;"
}

function ConvertFrom-HccSqlProbeOutput {
    param([AllowNull()][string]$Text)
    if (-not $Text) { return $null }
    $probe = [regex]::Match($Text, 'HCC_PROBE:(\d+)\|(\d+)\|(\d+)')
    if (-not $probe.Success) { return $null }
    $isSysadmin = ($probe.Groups[1].Value -eq '1')
    $major = [int]$probe.Groups[2].Value
    $windowsOnly = ($probe.Groups[3].Value -eq '1')
    $tcp = [regex]::Match($Text, 'HCC_TCP:(\d+)')
    $np = [regex]::Match($Text, 'HCC_NP:(\d+)')
    $admins = @()
    foreach ($a in [regex]::Matches($Text, '(?m)^\s*HCC_ADMIN:(.+?)\s*$')) { $admins += $a.Groups[1].Value }
    return [pscustomobject]@{
        IsSysadmin = $isSysadmin; MajorVersion = $major; WindowsOnly = $windowsOnly
        TcpEnabled = $(if ($tcp.Success) { $tcp.Groups[1].Value -ne '0' } else { $null })
        NamedPipesEnabled = $(if ($np.Success) { $np.Groups[1].Value -ne '0' } else { $null })
        Admins = @($admins)
    }
}

# Warnings about an adopted instance (nothing is changed): returns objects { Code; Text }. Pure.
function Get-HccSqlAdoptionWarnings {
    param([Parameter(Mandatory)][object]$Probe, [string]$StartMode = '', [string]$ServiceStatus = '')
    $w = @()
    if ($StartMode -and $StartMode -ne 'Auto') {
        $w += [pscustomobject]@{ Code = 'StartTypeNotAutomatic'; Text = "SQL Server's service is set to start '$StartMode', not automatically. After a restart of this PC HCC would lose its database until someone starts it. HCC did not change this; ask whoever set up SQL Server to set it to Automatic." }
    }
    $mixed = (-not $Probe.WindowsOnly); $tcp = ($Probe.TcpEnabled -eq $true)
    if ($mixed -and $tcp) {
        $w += [pscustomobject]@{ Code = 'TcpAndMixedMode'; Text = 'This SQL Server accepts SQL logins (for example sa) AND can be reached over the network (TCP/IP is on). Anyone on the hotel network could try to sign in to the database that will hold guest ID numbers. HCC does not use SQL logins and did not change this; turn off TCP/IP in SQL Server Configuration Manager or switch to Windows authentication only.' }
    }
    elseif ($mixed) { $w += [pscustomobject]@{ Code = 'MixedMode'; Text = 'This SQL Server also accepts SQL logins, not only Windows accounts. HCC does not use them and did not change this.' } }
    elseif ($tcp) { $w += [pscustomobject]@{ Code = 'TcpEnabled'; Text = 'TCP/IP is on for this SQL Server, so it can be reached over the network. HCC does not need that and did not change it; consider turning it off in SQL Server Configuration Manager.' } }
    if ($Probe.NamedPipesEnabled -eq $true) { $w += [pscustomobject]@{ Code = 'NamedPipesEnabled'; Text = 'Named pipes are on for this SQL Server. HCC does not need them and did not change this.' } }
    $others = @($Probe.Admins | Where-Object { $_ -and $_ -ne 'BUILTIN\Administrators' -and $_ -ne 'NT AUTHORITY\SYSTEM' -and $_ -notlike 'NT SERVICE\*' })
    if ($others.Count -gt 0) { $w += [pscustomobject]@{ Code = 'OtherSysadmins'; Text = ('Besides the Administrators of this PC, these Windows accounts are SQL Server administrators and can read the HCC databases: ' + ($others -join ', ') + '.') } }
    return $w
}

# --- system seams (small, so the tests can replace them) --------------------------------------------------------------------------
function Test-HccAdministrator {
    $p = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-HccSqlServiceState {
    $s = Get-Service -Name $script:HccSqlServiceName -ErrorAction SilentlyContinue
    if (-not $s) { return $null }
    $mode = ''
    try { $c = Get-CimInstance -ClassName Win32_Service -Filter ("Name='" + $script:HccSqlServiceName.Replace("'", "''") + "'") -ErrorAction Stop; if ($c) { $mode = [string]$c.StartMode } } catch { $mode = '' }
    return [pscustomobject]@{ Name = $s.Name; Status = [string]$s.Status; StartMode = $mode }
}

function Get-HccRestartPendingReasons {
    $r = @()
    if (Test-Path -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') { $r += 'Windows servicing' }
    if (Test-Path -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') { $r += 'Windows Update' }
    $pf = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name PendingFileRenameOperations -ErrorAction SilentlyContinue
    if ($pf -and $pf.PSObject.Properties['PendingFileRenameOperations'] -and @($pf.PendingFileRenameOperations | Where-Object { $_ }).Count -gt 0) { $r += 'files waiting to be replaced' }
    return $r
}

function Get-HccInstalledOdbcDrivers {
    $key = 'HKLM:\SOFTWARE\ODBC\ODBCINST.INI\ODBC Drivers'
    if (-not (Test-Path -LiteralPath $key)) { return }
    return (Get-Item -LiteralPath $key).Property
}

# The places sqlcmd is taken from: the SQL Server client folders under Program Files only, ODBC 17 first (HCC always passes -C, so 18 connects too).
# There is NO fallback to PATH: a sqlcmd.exe in some user-writable PATH folder would run with the administrator's rights.
function Get-HccSqlcmdCandidates {
    $roots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { $_ } | Select-Object -Unique
    $list = @()
    foreach ($v in '170', '180') { foreach ($r in $roots) { $list += (Join-Path $r "Microsoft SQL Server\Client SDK\ODBC\$v\Tools\Binn\SQLCMD.EXE") } }
    return $list
}

function Get-HccSqlcmdPath {
    foreach ($c in (Get-HccSqlcmdCandidates)) {
        if (-not (Test-Path -LiteralPath $c -PathType Leaf)) { continue }
        $problems = @(Test-HccAdminOnlyWritable -Path (Split-Path -Parent $c) -Extensions @('.exe'))
        if ($problems.Count -eq 0) { return $c }
    }
    return $null
}

# Problems with the media folder (empty = safe): non-administrators can change it or a file in it (.exe/.msi), its owner is not SYSTEM /
# Administrators / TrustedInstaller, or it is a link. The files run with the administrator's rights, so all of this must hold.
function Get-HccMediaFolderProblems {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return @("The SQL Server media folder was not found: $Path") }
    $problems = @()
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { $problems += "The media folder $Path is a link and is refused." }
    $problems += @(Test-HccAdminOnlyWritable -Path $Path -Extensions @('.exe', '.msi'))
    try {
        $owner = (Get-Acl -LiteralPath $Path).GetOwner([Security.Principal.SecurityIdentifier]).Value
        if (@($script:HccSidSystem, $script:HccSidAdministrators, $script:HccSidTrustedInstaller) -notcontains $owner) { $problems += "The media folder $Path is owned by an account that is not an administrator ($owner)." }
    }
    catch { $problems += "The owner of the media folder $Path could not be read." }
    if ($problems.Count -gt 0) { $problems += 'Copy the media to a folder only administrators can write (for example under C:\Program Files\HCC).' }
    return $problems
}

# The security a scratch folder gets AT CREATION: owner Administrators, no inheritance, SYSTEM + Administrators full control only. Pure.
function Get-HccScratchDirectorySecurity {
    $sec = New-Object Security.AccessControl.DirectorySecurity
    $sec.SetOwner((New-Object Security.Principal.SecurityIdentifier($script:HccSidAdministrators)))
    $sec.SetAccessRuleProtection($true, $false)
    foreach ($sid in $script:HccSidSystem, $script:HccSidAdministrators) {
        $id = New-Object Security.Principal.SecurityIdentifier($sid)
        $sec.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($id, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    }
    return $sec
}

# Creates <BasePath>\HccSqlSetup-<guid> in one step WITH its security (no create-then-protect gap), refuses an existing folder, checks the result.
# The parent is %ProgramData% itself (admin-controlled; no HCC\... parents that Users could have created). No spaces: /X: is not quoted.
function New-HccProtectedScratchDirectory {
    param([string]$BasePath = $env:ProgramData)
    $dir = Join-Path $BasePath ('HccSqlSetup-' + [guid]::NewGuid().ToString('N'))
    if (Test-Path -LiteralPath $dir) { throw "The scratch folder $dir already exists and is refused." }
    $sec = Get-HccScratchDirectorySecurity
    if ($PSVersionTable.PSVersion.Major -ge 6) { [void][IO.FileSystemAclExtensions]::Create((New-Object IO.DirectoryInfo($dir)), $sec) }
    else { [void][IO.Directory]::CreateDirectory($dir, $sec) }
    try {
        $acl = Get-Acl -LiteralPath $dir
        $okSids = @($script:HccSidSystem, $script:HccSidAdministrators)
        $owner = $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
        $bad = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) | Where-Object { $okSids -notcontains $_.IdentityReference.Value })
        if (-not $acl.AreAccessRulesProtected -or $bad.Count -gt 0 -or $okSids -notcontains $owner) { throw 'The scratch folder was not created with the expected protection.' }
    }
    catch { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue; throw }
    return $dir
}

# Runs one installer process and returns its exit code. Arguments are quoted here (Start-Process never quotes).
function Start-HccSetupProcess {
    param([Parameter(Mandatory)][string]$FilePath, [string[]]$Arguments = @())
    $q = @($Arguments | ForEach-Object { if ($_ -match '\s' -and $_ -notmatch '^".*"$') { '"' + $_ + '"' } else { $_ } })
    if ($q.Count -gt 0) { $p = Start-Process -FilePath $FilePath -ArgumentList $q -Wait -PassThru -NoNewWindow }
    else { $p = Start-Process -FilePath $FilePath -Wait -PassThru -NoNewWindow }
    return [int]$p.ExitCode
}

function Start-HccSqlService {
    Start-Service -Name $script:HccSqlServiceName
    (Get-Service -Name $script:HccSqlServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(90))
}

# Read-only look at the instance with Windows authentication (see Get-HccSqlProbeQuery / ConvertFrom-HccSqlProbeOutput). Throws when it cannot connect.
function Invoke-HccSqlProbe {
    param([Parameter(Mandatory)][string]$Sqlcmd, [int]$Attempts = 8, [int]$DelaySeconds = 5)
    $q = Get-HccSqlProbeQuery
    $last = ''
    for ($i = 1; $i -le $Attempts; $i++) {
        $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        try { $out = & $Sqlcmd -S $script:HccSqlInstance -E -C -b -l 30 -h -1 -W -Q $q 2>&1; $code = $LASTEXITCODE } finally { $ErrorActionPreference = $prev }
        $text = ($out | Out-String)
        $parsed = $null; if ($code -eq 0) { $parsed = ConvertFrom-HccSqlProbeOutput -Text $text }
        if ($parsed) { return $parsed }
        $last = $text.Trim()
        if ($i -lt $Attempts) { Start-Sleep -Seconds $DelaySeconds }
    }
    throw "sqlcmd could not reach $($script:HccSqlInstance) with Windows authentication. Last answer: $last"
}

function Add-HccSqlcmdToPath {
    param([string]$Sqlcmd)
    if (-not $Sqlcmd) { return }
    $dir = Split-Path -Parent $Sqlcmd
    if (($env:Path -split ';') -notcontains $dir) { $env:Path = $dir + ';' + $env:Path }
}

function Add-HccSqlWarning {
    param([string]$Code, [string]$Text, [string]$LogFile, [string]$Level = 'WARN')
    $script:HccSqlWarnings.Add([pscustomobject]@{ code = $Code; text = $Text })
    Write-HccLog $Text $LogFile $Level
}

# --- installation steps ---------------------------------------------------------------------------------------------------------
# Returns $true when the step asked for a Windows restart (exit 3010), throws on any other failure.
function Install-HccSqlEngineFromMedia {
    param([Parameter(Mandatory)][string]$MediaDir, [Parameter(Mandatory)][object[]]$Manifest, [string]$LogFile)
    $extract = $null
    try {
        $engine = Assert-HccSqlMediaFileUnchanged -MediaDir $MediaDir -Manifest $Manifest -Role engine
        $extract = New-HccProtectedScratchDirectory
        Write-HccLog "Extracting the SQL Server Express media to a protected folder." $LogFile
        $c = Start-HccSetupProcess -FilePath $engine -Arguments @('/Q', "/X:$extract")
        if ($c -ne 0) { throw "Could not extract the SQL Server media (exit code $c)." }
        $setup = Join-Path $extract 'setup.exe'
        if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) {
            $found = @(Get-ChildItem -LiteralPath $extract -Filter 'setup.exe' -File -Recurse -Depth 1)
            if ($found.Count -ne 1) { throw 'The SQL Server media did not extract a setup program.' }
            $setup = $found[0].FullName
        }
        Write-HccLog "Running SQL Server setup silently (instance SQLEXPRESS, Windows authentication only, no network protocols). This takes several minutes." $LogFile
        $c = Start-HccSetupProcess -FilePath $setup -Arguments (Get-HccSqlSetupArguments)
        if ($c -eq 3010) { Write-HccLog 'SQL Server setup finished and asks for a restart of Windows.' $LogFile 'WARN'; $restart = $true }
        elseif ($c -ne 0) {
            $logRoot = Join-Path $env:ProgramFiles 'Microsoft SQL Server'
            $summary = @(Get-ChildItem -LiteralPath $logRoot -Filter 'Summary.txt' -Recurse -Depth 5 -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like '*Setup Bootstrap\Log*' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1)
            $where = if ($summary.Count -gt 0) { $summary[0].FullName } else { "$logRoot\<version>\Setup Bootstrap\Log\Summary.txt" }
            throw "SQL Server setup failed (exit code $c). Its own log: $where"
        }
        else { $restart = $false }
    }
    finally { if ($extract -and (Test-Path -LiteralPath $extract)) { Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue } }
    if (-not (Get-HccSqlServiceState)) { throw 'SQL Server setup finished but the service MSSQL$SQLEXPRESS did not appear.' }
    return $restart
}

function Install-HccSqlClientFromMedia {
    param([Parameter(Mandatory)][string]$MediaDir, [Parameter(Mandatory)][object[]]$Manifest, [bool]$NeedOdbc17, [string]$LogFile)
    $restart = $false
    $list = @()
    if ($NeedOdbc17) { $list += , @('odbc17', 'IACCEPTMSODBCSQLLICENSETERMS=YES', 'ODBC Driver 17 for SQL Server') }
    $list += , @('sqlcmd', 'IACCEPTMSSQLCMDLNUTILSLICENSETERMS=YES', 'sqlcmd')
    $msiexec = Join-Path $env:SystemRoot 'System32\msiexec.exe'
    foreach ($item in $list) {
        $file = Assert-HccSqlMediaFileUnchanged -MediaDir $MediaDir -Manifest $Manifest -Role $item[0]   # hashed again right before it runs
        Write-HccLog "Installing $($item[2])." $LogFile
        $c = Start-HccSetupProcess -FilePath $msiexec -Arguments @('/i', $file, '/qn', '/norestart', 'ADDLOCAL=ALL', $item[1])
        if ($c -eq 3010) { $restart = $true } elseif ($c -ne 0) { throw "Could not install $($item[2]) (exit code $c)." }
    }
    return $restart
}

# --- main ---------------------------------------------------------------------------------------------------------------------
function Write-HccSqlWarningsFile {
    param([string]$Path)
    if (-not $Path) { return }
    try {
        $dir = Split-Path -Parent $Path
        if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        $items = @($script:HccSqlWarnings.ToArray() | ForEach-Object { [pscustomobject]@{ code = [string]$_.code; text = [string]$_.text } })
        $json = '{ "warnings": ' + $(if ($items.Count -eq 0) { '[]' } else { ConvertTo-Json -InputObject $items -Depth 3 }) + ' }'
        Set-Content -LiteralPath $Path -Value $json -Encoding UTF8
    }
    catch { }   # the warnings are also in the log; a file that cannot be written must not change the exit code
}

function Invoke-HccSqlBootstrap {
    [CmdletBinding()]
    param([string]$Mode, [string]$MediaDir, [bool]$PlanOnly, [string]$LogFile, [string]$ManifestPath, [string]$WarningsFile)
    $script:HccSqlWarnings = New-Object System.Collections.Generic.List[object]
    $log = $LogFile
    if (-not $PlanOnly -and -not $log) { $log = Join-Path $env:ProgramData ('HCC\Logs\install-{0}.log' -f (Get-Date).ToString('yyyyMMdd-HHmmss')) }
    if ($PlanOnly) { $log = ''; $WarningsFile = '' }   # a plan writes nothing, not even a log
    $code = 1
    try {
        if (-not $Mode) { throw 'Give -Mode InstallBundled or -Mode UseExisting.' }
        if (-not $ManifestPath) { $ManifestPath = Join-Path $PSScriptRoot 'sql-media.manifest.json' }
        Write-HccLog ("SQL bootstrap: mode {0}{1}." -f $Mode, $(if ($PlanOnly) { ' (plan only, nothing is changed)' } else { '' })) $log
        if (-not $PlanOnly -and -not (Test-HccAdministrator)) { throw 'This step needs an administrator (run from an elevated PowerShell or the setup program).' }

        $svc = Get-HccSqlServiceState
        $sqlcmd = Get-HccSqlcmdPath
        $odbc17 = @(Get-HccInstalledOdbcDrivers) -contains 'ODBC Driver 17 for SQL Server'
        $restartReasons = @(); if (-not $svc -and $Mode -eq 'InstallBundled') { $restartReasons = @(Get-HccRestartPendingReasons) }
        $plan = Get-HccSqlBootstrapPlan -Mode $Mode -ServiceExists ([bool]$svc) -SqlcmdPresent ([bool]$sqlcmd) -Odbc17Present $odbc17 -RestartReasons $restartReasons
        foreach ($s in $plan.Steps) { Write-HccLog ("PLAN: " + $s) $log }

        if ($plan.Outcome -eq 'Refuse') { throw $plan.Reason }
        if ($plan.Outcome -eq 'RestartNeeded') { Write-HccLog $plan.Reason $log 'WARN'; $code = 2; return $code }

        # media: verified before the first installer runs, so a bad folder stops us with nothing half-installed
        $manifest = $null
        if ($plan.Roles.Count -gt 0) {
            if (-not $MediaDir) { throw 'The SQL Server media folder (-MediaDir) is needed for this step.' }
            $manifest = @(Get-HccSqlMediaManifest -Path $ManifestPath)
            $problems = @(Test-HccSqlMedia -MediaDir $MediaDir -Manifest $manifest -Roles $plan.Roles)
            if ($problems.Count -eq 0) { $problems = @(Get-HccMediaFolderProblems -Path $MediaDir) }
            if ($problems.Count -gt 0) { foreach ($p in $problems) { Write-HccLog $p $log 'ERROR' }; throw 'The SQL Server media was refused; nothing was installed.' }
            Write-HccLog ("Media checked against the manifest: {0} file(s) match." -f $plan.Roles.Count) $log
        }
        if ($PlanOnly) { Write-HccLog 'Plan finished. Nothing was changed.' $log; $code = 0; return $code }

        $needRestart = $false
        if ($plan.Outcome -eq 'Install') { if (Install-HccSqlEngineFromMedia -MediaDir $MediaDir -Manifest $manifest -LogFile $log) { $needRestart = $true } }
        else {
            if ($svc.StartMode -eq 'Disabled' -and $svc.Status -ne 'Running') { throw 'SQL Server is switched off on this PC (its service is Disabled). Ask whoever set it up to enable it; HCC does not change it.' }
            if ($svc.Status -ne 'Running') {
                Write-HccLog "The SQL Server service is $($svc.Status); starting it (it is not restarted or reconfigured)." $log
                Start-HccSqlService
            }
            else { Write-HccLog 'Existing SQL Server instance .\SQLEXPRESS adopted (service running). It is not changed.' $log }
        }

        # From here a failure after an installer asked for a restart means "restart, then run again" (2), not a failure (1).
        try {
            if ($plan.Roles -contains 'sqlcmd') {
                # after a fresh engine install SQL Server's own tools may already provide a usable sqlcmd: only install what is still missing
                $sqlcmd = Get-HccSqlcmdPath
                if (-not $sqlcmd) { if (Install-HccSqlClientFromMedia -MediaDir $MediaDir -Manifest $manifest -NeedOdbc17 ($plan.Roles -contains 'odbc17') -LogFile $log) { $needRestart = $true } }
            }
            $sqlcmd = Get-HccSqlcmdPath
            if (-not $sqlcmd) { throw 'sqlcmd was not found in a protected SQL Server client folder after the installation.' }
            Add-HccSqlcmdToPath -Sqlcmd $sqlcmd
            Write-HccLog "sqlcmd: $sqlcmd" $log
            $probe = Invoke-HccSqlProbe -Sqlcmd $sqlcmd
        }
        catch {
            if ($needRestart) {
                Add-HccSqlWarning 'RestartRequested' ('An installer asked for a restart of Windows and SQL Server cannot be used yet (' + $_.Exception.Message + '). Restart the PC, then run the setup again; this step will then find the instance and finish.') $log
                $code = 2; return $code
            }
            throw
        }
        if ($probe.MajorVersion -lt $script:HccSqlMinMajorVersion) { throw "SQL Server $($probe.MajorVersion) is too old for HCC (SQL Server 2022 or newer is needed). The instance was not changed." }
        if (-not $probe.IsSysadmin) { throw 'This administrator is not a SQL Server administrator on .\SQLEXPRESS, so HCC cannot create its databases there. Ask whoever set up that instance to add this account as a sysadmin. The instance was not changed.' }
        if ($plan.Outcome -eq 'Adopt') {
            $mode = ''; $status = ''; if ($svc) { $mode = [string]$svc.StartMode; $status = [string]$svc.Status }
            foreach ($w in (Get-HccSqlAdoptionWarnings -Probe $probe -StartMode $mode -ServiceStatus $status)) { Add-HccSqlWarning $w.Code $w.Text $log }
        }
        Write-HccLog ("SQL Server {0} on .\SQLEXPRESS is ready (Windows authentication, administrator access confirmed)." -f $probe.MajorVersion) $log
        if ($needRestart) { Add-HccSqlWarning 'RestartRequested' 'A restart of Windows was requested by an installer. Restart the PC, then run the setup again; this step will then find the instance and finish.' $log; $code = 2; return $code }
        $code = 0; return $code
    }
    catch {
        Write-HccLog ("SQL bootstrap failed: " + $_.Exception.Message) $log 'ERROR'
        $code = 1; return $code
    }
    finally { Write-HccSqlWarningsFile -Path $WarningsFile }
}

if ($MyInvocation.InvocationName -eq '.') { return }   # dot-sourced by the tests: functions only
if ($WhatIfPreference) { $PlanOnly = [switch]$true }
exit (Invoke-HccSqlBootstrap -Mode $Mode -MediaDir $MediaDir -PlanOnly ([bool]$PlanOnly) -LogFile $LogFile -ManifestPath $ManifestPath -WarningsFile $WarningsFile)
