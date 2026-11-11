<#
.SYNOPSIS
  Makes sure SQL Server Express (instance .\SQLEXPRESS) and sqlcmd are ready on this PC for the HCC installer (INST-CONTRACT 2, lane sql).
.DESCRIPTION
  -Mode UseExisting : the instance must already be on this PC (for example ETP's). It is ADOPTED: only read and (if its service is stopped) started.
                      Nothing of it is reconfigured, restarted or upgraded, and no database or login is touched. sqlcmd is installed from the media
                      only when the PC has none.
  -Mode InstallBundled : if the instance is already there it is adopted exactly as above (a second HCC or ETP install never disturbs it);
                      otherwise SQL Server Express is installed silently from the offline media folder -MediaDir (Windows authentication only,
                      TCP/IP and named pipes off, Administrators as sysadmin), then ODBC Driver 17 and sqlcmd when the PC has no sqlcmd.
  Every media file is checked against sql-media.manifest.json (next to this script: names and SHA-256, computed from the ETP payload folder)
  before anything is run. A missing file, an unknown name or a wrong hash stops the script with exit 1 and nothing installed. The media folder
  must be writable by administrators only (the files run with the administrator's rights).
  Exit codes: 0 the instance is ready and reachable with Windows authentication by this administrator; 2 Windows must be restarted first (nothing
  was installed) or SQL Server asked for a restart after installing (run again afterwards; the instance is then adopted); 1 failed (the log says why).
  -PlanOnly (and -WhatIf) print what would happen and change nothing: no log file, no folder, no process, no service start. The media hashes
  ARE read (read only) so that the plan can say whether the media would be accepted.
  Log: -LogFile (default C:\ProgramData\HCC\Logs\install-<yyyyMMdd-HHmmss>.log). No secret is ever handled here.
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
    [string]$ManifestPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Hcc-Common.ps1')
Set-StrictMode -Version Latest   # Hcc-Common.ps1 relaxes it to 2.0 for itself

$script:HccSqlInstanceName = 'SQLEXPRESS'
$script:HccSqlServiceName = 'MSSQL$SQLEXPRESS'
$script:HccSqlInstance = '.\SQLEXPRESS'
$script:HccSqlMinMajorVersion = 16   # SQL Server 2022 or newer, the same floor as ETP

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

# Checks the named roles' files in $MediaDir against the manifest. Returns the list of problems (empty = all good). Read only.
function Test-HccSqlMedia {
    param([Parameter(Mandatory)][string]$MediaDir, [Parameter(Mandatory)][object[]]$Manifest, [Parameter(Mandatory)][string[]]$Roles)
    $problems = @()
    if (-not (Test-Path -LiteralPath $MediaDir -PathType Container)) { return @("The SQL Server media folder was not found: $MediaDir") }
    foreach ($role in $Roles) {
        $entry = @($Manifest | Where-Object { $_.role -eq $role }) | Select-Object -First 1
        if (-not $entry) { $problems += "The media manifest has no entry for '$role'."; continue }
        $file = Join-Path $MediaDir $entry.name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { $problems += "Missing media file: $($entry.name)."; continue }
        $item = Get-Item -LiteralPath $file -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { $problems += "Media file $($entry.name) is a link and is refused."; continue }
        $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        if ($actual -ne ([string]$entry.sha256).ToUpperInvariant()) { $problems += "Media file $($entry.name) is not the expected file (SHA-256 differs). It was not run." }
    }
    return $problems
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

# --- system seams (small, so the tests can replace them) --------------------------------------------------------------------------
function Test-HccAdministrator {
    $p = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-HccSqlServiceState {
    $s = Get-Service -Name $script:HccSqlServiceName -ErrorAction SilentlyContinue
    if (-not $s) { return $null }
    return [pscustomobject]@{ Name = $s.Name; Status = [string]$s.Status }
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

# The sqlcmd to use: the ODBC 17 one first, then ODBC 18 (HCC always passes -C, so either connects), then whatever PATH has.
function Get-HccSqlcmdPath {
    $roots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { $_ } | Select-Object -Unique
    foreach ($v in '170', '180') {
        foreach ($r in $roots) {
            $c = Join-Path $r "Microsoft SQL Server\Client SDK\ODBC\$v\Tools\Binn\SQLCMD.EXE"
            if (Test-Path -LiteralPath $c -PathType Leaf) { return $c }
        }
    }
    $g = Get-Command sqlcmd.exe -ErrorAction SilentlyContinue
    if ($g) { return $g.Source }
    return $null
}

# A folder whose ACL lets only SYSTEM / Administrators / TrustedInstaller / the owner-placeholder write (so non-administrators cannot swap media).
function Test-HccPathAdminOnly {
    param([Parameter(Mandatory)][string]$Path)
    $ok = @('S-1-5-18', 'S-1-5-32-544', 'S-1-3-0')
    $write = [Security.AccessControl.FileSystemRights]::WriteData -bor [Security.AccessControl.FileSystemRights]::AppendData -bor [Security.AccessControl.FileSystemRights]::Delete -bor
        [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor [Security.AccessControl.FileSystemRights]::ChangePermissions -bor [Security.AccessControl.FileSystemRights]::TakeOwnership
    $acl = Get-Acl -LiteralPath $Path
    foreach ($rule in $acl.Access) {
        if ($rule.AccessControlType -ne 'Allow') { continue }
        if (-not ($rule.FileSystemRights -band $write)) { continue }
        try { $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } catch { return $false }
        if ($ok -contains $sid -or $sid.StartsWith('S-1-5-80-')) { continue }   # S-1-5-80-* = service SIDs such as TrustedInstaller
        return $false
    }
    return $true
}

function New-HccProtectedScratchDirectory {
    $base = Join-Path $env:ProgramData 'HCC\SqlSetup'
    $dir = Join-Path $base ([guid]::NewGuid().ToString('N'))   # no spaces: the extractor's /X: argument is not quoted
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $sec = New-Object Security.AccessControl.DirectorySecurity
    $sec.SetAccessRuleProtection($true, $false)
    foreach ($sid in 'S-1-5-18', 'S-1-5-32-544') {
        $id = New-Object Security.Principal.SecurityIdentifier($sid)
        $sec.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($id, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    }
    Set-Acl -LiteralPath $dir -AclObject $sec
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

# Read-only look at the instance with Windows authentication. Returns IsSysadmin / MajorVersion / WindowsOnly / TcpEnabled, or throws.
function Invoke-HccSqlProbe {
    param([Parameter(Mandatory)][string]$Sqlcmd, [int]$Attempts = 8, [int]$DelaySeconds = 5)
    $q = "SET NOCOUNT ON; SELECT N'HCC_PROBE:'+CONVERT(nvarchar(5),COALESCE(IS_SRVROLEMEMBER('sysadmin'),0))+N'|'+CONVERT(nvarchar(10),COALESCE(SERVERPROPERTY('ProductMajorVersion'),0))+N'|'+CONVERT(nvarchar(5),COALESCE(SERVERPROPERTY('IsIntegratedSecurityOnly'),0)); " +
        "IF COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)=1 SELECT N'HCC_TCP:'+CONVERT(nvarchar(5),value_data) FROM sys.dm_server_registry WHERE value_name=N'Enabled' AND registry_key LIKE N'%\SuperSocketNetLib\Tcp';"
    $last = ''
    for ($i = 1; $i -le $Attempts; $i++) {
        $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        try { $out = & $Sqlcmd -S $script:HccSqlInstance -E -C -b -l 30 -h -1 -W -Q $q 2>&1; $code = $LASTEXITCODE } finally { $ErrorActionPreference = $prev }
        $text = ($out | Out-String)
        if ($code -eq 0 -and $text -match 'HCC_PROBE:(\d+)\|(\d+)\|(\d+)') {
            $tcp = $null; if ($text -match 'HCC_TCP:(\d+)') { $tcp = ($Matches[1] -ne '0') }
            return [pscustomobject]@{ IsSysadmin = ($Matches[1] -eq '1'); MajorVersion = [int]$Matches[2]; WindowsOnly = ($Matches[3] -eq '1'); TcpEnabled = $tcp }
        }
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

# --- installation steps ---------------------------------------------------------------------------------------------------------
# Returns $true when the step asked for a Windows restart (exit 3010), throws on any other failure.
function Install-HccSqlEngineFromMedia {
    param([Parameter(Mandatory)][string]$MediaDir, [Parameter(Mandatory)][object[]]$Manifest, [string]$LogFile)
    $engine = Join-Path $MediaDir (@($Manifest | Where-Object { $_.role -eq 'engine' })[0].name)
    $extract = $null
    try {
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
        elseif ($c -ne 0) { throw "SQL Server setup failed (exit code $c). Its own log: C:\Program Files\Microsoft SQL Server\<version>\Setup Bootstrap\Log\Summary.txt" }
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
        $file = Join-Path $MediaDir (@($Manifest | Where-Object { $_.role -eq $item[0] })[0].name)
        Write-HccLog "Installing $($item[2])." $LogFile
        $c = Start-HccSetupProcess -FilePath $msiexec -Arguments @('/i', $file, '/qn', '/norestart', 'ADDLOCAL=ALL', $item[1])
        if ($c -eq 3010) { $restart = $true } elseif ($c -ne 0) { throw "Could not install $($item[2]) (exit code $c)." }
    }
    return $restart
}

# --- main ---------------------------------------------------------------------------------------------------------------------
function Invoke-HccSqlBootstrap {
    [CmdletBinding()]
    param([string]$Mode, [string]$MediaDir, [bool]$PlanOnly, [string]$LogFile, [string]$ManifestPath)
    $log = $LogFile
    if (-not $PlanOnly -and -not $log) { $log = Join-Path $env:ProgramData ('HCC\Logs\install-{0}.log' -f (Get-Date).ToString('yyyyMMdd-HHmmss')) }
    if ($PlanOnly) { $log = '' }   # a plan writes nothing, not even a log
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
        if ($plan.Outcome -eq 'RestartNeeded') { Write-HccLog $plan.Reason $log 'WARN'; return 2 }

        # media: verified before the first installer runs, so a bad folder stops us with nothing half-installed
        $manifest = $null
        if ($plan.Roles.Count -gt 0) {
            if (-not $MediaDir) { throw 'The SQL Server media folder (-MediaDir) is needed for this step.' }
            $manifest = @(Get-HccSqlMediaManifest -Path $ManifestPath)
            $problems = @(Test-HccSqlMedia -MediaDir $MediaDir -Manifest $manifest -Roles $plan.Roles)
            if ($problems.Count -eq 0 -and -not (Test-HccPathAdminOnly -Path $MediaDir)) { $problems += "The media folder $MediaDir can be changed by non-administrators and is refused. Copy it to a folder only administrators can write." }
            if ($problems.Count -gt 0) { foreach ($p in $problems) { Write-HccLog $p $log 'ERROR' }; throw 'The SQL Server media was refused; nothing was installed.' }
            Write-HccLog ("Media checked against the manifest: {0} file(s) match." -f $plan.Roles.Count) $log
        }
        if ($PlanOnly) { Write-HccLog 'Plan finished. Nothing was changed.' $log; return 0 }

        $needRestart = $false
        if ($plan.Outcome -eq 'Install') { if (Install-HccSqlEngineFromMedia -MediaDir $MediaDir -Manifest $manifest -LogFile $log) { $needRestart = $true } }
        elseif ($svc.Status -ne 'Running') {
            Write-HccLog "The SQL Server service is $($svc.Status); starting it (it is not restarted or reconfigured)." $log
            Start-HccSqlService
        }
        else { Write-HccLog 'Existing SQL Server instance .\SQLEXPRESS adopted (service running). It is not changed.' $log }

        if ($plan.Roles -contains 'sqlcmd') {
            # after a fresh engine install SQL Server's own tools may already provide a usable sqlcmd: only install what is still missing
            $sqlcmd = Get-HccSqlcmdPath
            if (-not $sqlcmd) { if (Install-HccSqlClientFromMedia -MediaDir $MediaDir -Manifest $manifest -NeedOdbc17 ($plan.Roles -contains 'odbc17') -LogFile $log) { $needRestart = $true } }
        }
        $sqlcmd = Get-HccSqlcmdPath
        if (-not $sqlcmd) { throw 'sqlcmd was not found after the installation.' }
        Add-HccSqlcmdToPath -Sqlcmd $sqlcmd
        Write-HccLog "sqlcmd: $sqlcmd" $log

        $probe = Invoke-HccSqlProbe -Sqlcmd $sqlcmd
        if ($probe.MajorVersion -lt $script:HccSqlMinMajorVersion) { throw "SQL Server $($probe.MajorVersion) is too old for HCC (SQL Server 2022 or newer is needed). The instance was not changed." }
        if (-not $probe.IsSysadmin) { throw 'This administrator is not a SQL Server administrator on .\SQLEXPRESS, so HCC cannot create its databases there. Ask whoever set up that instance to add this account as a sysadmin. The instance was not changed.' }
        if (-not $probe.WindowsOnly) { Write-HccLog 'Note: this instance also accepts SQL Server logins (not only Windows accounts). HCC does not use them; it was not changed.' $log 'WARN' }
        if ($probe.TcpEnabled) { Write-HccLog 'Note: TCP/IP is on for this instance, so it can be reached over the network. HCC does not need that; it was not changed. Consider turning it off in SQL Server Configuration Manager.' $log 'WARN' }
        Write-HccLog ("SQL Server {0} on .\SQLEXPRESS is ready (Windows authentication, administrator access confirmed)." -f $probe.MajorVersion) $log
        if ($needRestart) { Write-HccLog 'A restart of Windows was requested by an installer. Restart the PC, then run the setup again; this step will then find the instance and finish.' $log 'WARN'; return 2 }
        return 0
    }
    catch {
        Write-HccLog ("SQL bootstrap failed: " + $_.Exception.Message) $log 'ERROR'
        return 1
    }
}

if ($MyInvocation.InvocationName -eq '.') { return }   # dot-sourced by the tests: functions only
if ($WhatIfPreference) { $PlanOnly = [switch]$true }
exit (Invoke-HccSqlBootstrap -Mode $Mode -MediaDir $MediaDir -PlanOnly ([bool]$PlanOnly) -LogFile $LogFile -ManifestPath $ManifestPath)
