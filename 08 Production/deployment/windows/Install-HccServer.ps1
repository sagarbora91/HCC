<#
.SYNOPSIS
  Installs (or upgrades, or removes) the HCC server on the hotel PC: the orchestrator behind HCC-Server-Setup-<version>.exe.
.DESCRIPTION
  INST-CONTRACT section 2. Reads the answer file the setup wizard wrote (C:\ProgramData\HCC\Setup\answers.json), then runs the existing
  scripts in the order INSTALL.md prescribes and records every finished step in C:\ProgramData\HCC\Setup\install-state.json, so a
  failed install can be run again with -Resume and continues at the failed step:
    1 sql            Bootstrap-HccSql.ps1            7 service        Install-HccService.ps1 and start
    2 database       Initialize-HccDatabase.ps1      8 property       Initialize-HccProperty.ps1 (hotel + first Owner)
    3 certificate    New-HccCertificate.ps1          9 backup-tasks   first backup + Register-HccBackupTasks.ps1
    4 settings       appsettings.Production.json,    10 egress        Set-HccEgress.ps1 -State Install
                     hcc-ops.json (merge, trial page) 11 client-kit   New-HccClientKit.ps1
    5 keys           guest ID key, backup key        12 health        https://<server>:<port>/ answers, schema current
    6 migrate        Hcc.Api.exe migrate
  Exit codes: 0 done; 2 a restart of Windows is needed (run again with -Resume); 1 failed (the log names the step).
  -PlanOnly (and -WhatIf) print the steps and change nothing: no file, no service, no certificate, no answer-file deletion.
  -Upgrade: no questions. The installer has stopped the service and replaced the files; this merges new settings into the existing
  settings files (an existing value is never overwritten), runs migrate, starts the service, refreshes the client kit and checks health.
  -Uninstall: removes the service, its firewall rules and the scheduled tasks. It NEVER removes SQL Server, the databases, the backups,
  the keys, the certificates or the settings files (A21 defaults).
  Secrets (INST-CONTRACT rule 5): the Owner password and the CA password arrive only in the answer file (a folder only SYSTEM and
  Administrators can open, checked before it is read), are held as SecureString objects, are handed to the scripts in the same process
  (never on a command line), are removed from every log line, and the answer file is overwritten and deleted in a finally block.
  Windows PowerShell 5.1 and PowerShell 7.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$AnswerFile,
    [switch]$Resume,
    [switch]$PlanOnly,
    [switch]$Upgrade,
    [switch]$Uninstall,
    [string]$AppDirectory = (Join-Path $env:ProgramFiles 'HCC'),
    [string]$DataDirectory = (Join-Path $env:ProgramData 'HCC'),
    [string]$OperationsDirectory
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Hcc-Common.ps1')

$script:ServiceName = 'HccServer'
$script:ServiceAccountName = 'NT SERVICE\HccServer'
$script:Secrets = New-Object System.Collections.Generic.List[string]
$script:LogFile = $null
$script:PlanMode = $false
$script:StepOutcome = ''

# ======================================================================================================================
# Small helpers
# ======================================================================================================================
function Get-HccProp {
    param($Object, [string]$Name, $Default = $null)
    if ($null -eq $Object) { return $Default }
    $p = $Object.PSObject.Properties[$Name]
    if ($null -eq $p -or $null -eq $p.Value) { return $Default }
    return $p.Value
}

function Write-HccTextFile {
    param([string]$Path, [string]$Text)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

function Read-HccTextFile { param([string]$Path) return [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8) }

# ----- logging: every line passes through Protect-HccText, so a secret never reaches the log or the console --------------
function Add-HccSecret {
    param([string]$Value)
    if (-not $Value -or $Value.Length -lt 4) { return }
    foreach ($v in @($Value, (($Value | ConvertTo-Json -Compress).Trim('"')))) {
        if ($v -and -not $script:Secrets.Contains($v)) { $script:Secrets.Add($v) }
    }
}

function Protect-HccText {
    param([string]$Text)
    if ($null -eq $Text) { return '' }
    foreach ($s in $script:Secrets) { if ($s) { $Text = $Text.Replace($s, '[hidden]') } }
    return $Text
}

function Write-InstallLog {
    param([string]$Message, [ValidateSet('INFO', 'WARN', 'ERROR')][string]$Level = 'INFO')
    foreach ($m in @(([string]$Message) -split "`r?`n")) {
        if ($m.Trim().Length -eq 0) { continue }
        $line = '{0} {1} {2}' -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'), $Level, (Protect-HccText $m)
        Write-Host $line
        if ($script:LogFile) {
            $dir = Split-Path -Parent $script:LogFile
            if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
            Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8
        }
    }
}

function Test-HccElevated {
    return ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# ----- adapters: every change to the PC goes through these (the tests replace them with recorders) ------------------------
# A folder that holds secrets or things run as administrator is created WITH its permissions in one call (owner Administrators,
# only SYSTEM and Administrators, inheritance off) - never created first and locked afterwards.
function New-HccPrivateDirectory {
    param([string]$Path)
    $admins = New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')
    $system = New-Object Security.Principal.SecurityIdentifier('S-1-5-18')
    $sec = New-Object Security.AccessControl.DirectorySecurity
    $sec.SetAccessRuleProtection($true, $false)
    foreach ($sid in @($system, $admins)) {
        $sec.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    }
    $sec.SetOwner($admins)
    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    if ($PSVersionTable.PSVersion.Major -ge 6) { [void][System.IO.FileSystemAclExtensions]::Create((New-Object System.IO.DirectoryInfo($Path)), $sec) }
    else { [void][System.IO.Directory]::CreateDirectory($Path, $sec) }
}

# Problems that make a data folder unsafe for files written/executed with administrator rights: a link, or writable by non-administrators.
function Get-HccFolderProblems {
    param([string]$Path)
    $problems = @()
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $problems += "$Path is a link" }
    $problems += @(Test-HccAdminOnlyWritable -Path $Path)
    return @($problems)
}

# First action of every run: C:\ProgramData\HCC, Setup and Logs exist, are not links and can be written only by SYSTEM and Administrators
# (created with that security; a pre-existing folder is repaired, or the run is refused). Only then are log / warnings / answer paths used.
function Initialize-HccDataFolders {
    foreach ($f in @($script:DataDir, $script:SetupDir, $script:LogsDir)) {
        if (-not (Test-Path -LiteralPath $f)) { Protect-HccFolder -Path $f; continue }
        $problems = @(Get-HccFolderProblems -Path $f)
        if ($problems.Count -gt 0) {
            if ($problems -match 'is a link') { throw ("Refusing to use ${f}: " + ($problems -join '; ')) }
            Set-HccProtectedDirectory -Path $f
            $problems = @(Get-HccFolderProblems -Path $f)
            if ($problems.Count -gt 0) { throw ("The folder $f is not safe and could not be repaired: " + ($problems -join '; ')) }
        }
    }
}

function Protect-HccFolder {
    param([string]$Path, [string[]]$ReadAccounts = @())
    if (-not (Test-Path -LiteralPath $Path)) { New-HccPrivateDirectory -Path $Path; if (-not $ReadAccounts) { return } }
    Set-HccProtectedDirectory -Path $Path -ReadAccounts $ReadAccounts
}

function Protect-HccFile {
    param([string]$Path)
    & icacls.exe $Path /inheritance:r /grant:r '*S-1-5-18:(F)' '*S-1-5-32-544:(F)' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not restrict the permissions of $Path." }
}

function Get-HccInstallDriveRoot { return [IO.Path]::GetPathRoot($script:AppDir) }

function Assert-HccAnswerFolderPrivate {
    param([string]$Path)
    $problems = @(Test-HccProtectedDirectory -Path $Path)
    if ($problems.Count -gt 0) { throw ("The folder that holds the answers is not private, so the answers (they contain passwords) are not read: " + ($problems -join '; ')) }
}

function Invoke-HccOps {
    param([string]$Script, [hashtable]$Params = @{}, [switch]$UseExitCode)
    $path = Join-Path $script:OpsDir $Script
    if (-not (Test-Path -LiteralPath $path)) { throw "The setup script $Script is missing ($path). The installation is incomplete." }
    Set-StrictMode -Off
    $global:LASTEXITCODE = 0
    & $path @Params *>&1 | ForEach-Object { Write-InstallLog ([string]$_) }
    if ($UseExitCode) { return [int]$global:LASTEXITCODE }
    return 0
}

function Invoke-HccNative {
    param([string]$FilePath, [string]$Arguments, [string]$WorkingDirectory, [hashtable]$Environment = @{})
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = $Arguments
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    foreach ($k in $Environment.Keys) { $psi.EnvironmentVariables[[string]$k] = [string]$Environment[$k] }
    $p = [System.Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEndAsync()
    $err = $p.StandardError.ReadToEnd()
    $p.WaitForExit()
    return [pscustomobject]@{ ExitCode = $p.ExitCode; Output = ($out.Result + $err) }
}

function Test-HccServiceInstalled { return [bool](Get-Service -Name $script:ServiceName -ErrorAction SilentlyContinue) }

function Start-HccServerService {
    $svc = Get-Service -Name $script:ServiceName
    if ($svc.Status -ne 'Running') { Start-Service -Name $script:ServiceName }
    (Get-Service -Name $script:ServiceName).WaitForStatus('Running', (New-TimeSpan -Seconds 90))
}

function Stop-HccServerService {
    $svc = Get-Service -Name $script:ServiceName -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne 'Stopped') {
        Stop-Service -Name $script:ServiceName -Force
        (Get-Service -Name $script:ServiceName).WaitForStatus('Stopped', (New-TimeSpan -Seconds 90))
    }
}

function Invoke-HccHttp {
    param([string]$Url, [int]$TimeoutSec = 15)
    try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }
    try {
        $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec $TimeoutSec -ErrorAction Stop
        return [pscustomobject]@{ Status = [int]$r.StatusCode; Body = [string]$r.Content; Error = '' }
    }
    catch {
        $status = 0
        $resp = $_.Exception.PSObject.Properties['Response']
        if ($resp -and $resp.Value) { $status = [int]$resp.Value.StatusCode }
        return [pscustomobject]@{ Status = $status; Body = ''; Error = $_.Exception.Message }
    }
}

function Wait-HccUrl {
    param([string]$Url, [int[]]$AcceptStatus = @(200), [int]$Seconds = 90)
    $until = (Get-Date).AddSeconds($Seconds)
    $last = $null
    do {
        $last = Invoke-HccHttp -Url $Url
        if ($AcceptStatus -contains $last.Status) { return $last }
        Start-Sleep -Seconds 3
    } while ((Get-Date) -lt $until)
    return $last
}

function Get-HccFileSha256 {
    param([string]$Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($Path))) -replace '-', '') } finally { $sha.Dispose() }
}

function Get-HccDriveType {
    param([string]$Path)
    try { return [string](New-Object IO.DriveInfo([IO.Path]::GetPathRoot($Path))).DriveType } catch { return 'Unknown' }
}

function Install-HccTrustedCa {
    param([string]$CerPath)
    $cert = New-Object Security.Cryptography.X509Certificates.X509Certificate2($CerPath)
    $existing = Get-ChildItem -Path 'Cert:\LocalMachine\Root' | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
    if (-not $existing) { Import-Certificate -FilePath $CerPath -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null }
}

function Test-HccServerCertificatePresent {
    param([string]$Thumbprint)
    if (-not $Thumbprint) { return $false }
    return [bool](Get-ChildItem -Path 'Cert:\LocalMachine\My' -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $Thumbprint })
}

function Remove-HccLeftoverCaKey {
    # New-HccCertificate removes the CA (and its key) from this PC after exporting it; make sure nothing is left (DEC-087).
    $left = @(Get-ChildItem -Path 'Cert:\LocalMachine\My' -ErrorAction SilentlyContinue | Where-Object { $_.Subject -eq 'CN=HCC Hotel CA' })
    foreach ($c in $left) {
        Write-InstallLog "The hotel CA was still in this PC's certificate store (thumbprint $($c.Thumbprint)); removing it and its private key." 'WARN'
        Remove-Item -LiteralPath ('Cert:\LocalMachine\My\' + $c.Thumbprint) -DeleteKey -Force -ErrorAction SilentlyContinue
    }
}

function Grant-HccCertificateKeyAccess {
    # The service did not exist yet when the certificate was made, so make sure its account can read the key.
    param([string]$Thumbprint, [string]$Account)
    $cert = Get-ChildItem -Path 'Cert:\LocalMachine\My' | Where-Object { $_.Thumbprint -eq $Thumbprint } | Select-Object -First 1
    if (-not $cert) { throw "The server certificate $Thumbprint is not in this PC's certificate store." }
    $rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
    $keyName = if ($rsa -is [Security.Cryptography.RSACng]) { $rsa.Key.UniqueName } else { $rsa.CspKeyContainerInfo.UniqueKeyContainerName }
    $keyFile = @('Microsoft\Crypto\Keys', 'Microsoft\Crypto\RSA\MachineKeys') | ForEach-Object { Join-Path $env:ProgramData $_ } |
        ForEach-Object { Get-ChildItem -LiteralPath $_ -Filter $keyName -ErrorAction SilentlyContinue } | Select-Object -First 1
    if (-not $keyFile) { throw "The key file $keyName of the server certificate was not found." }
    $acl = Get-Acl $keyFile.FullName
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($Account, 'Read', 'Allow')))
    Set-Acl -Path $keyFile.FullName -AclObject $acl
}

function New-HccRandomPassword {
    $b = New-Object byte[] 24
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($b) } finally { $rng.Dispose() }
    return (([Convert]::ToBase64String($b) -replace '[+/=]', 'x') + 'Aa1')
}

function ConvertTo-HccSecureString {
    param([string]$Text)
    $s = New-Object System.Security.SecureString
    foreach ($ch in $Text.ToCharArray()) { $s.AppendChar($ch) }
    $s.MakeReadOnly()
    return $s
}

# ======================================================================================================================
# Paths, state, progress
# ======================================================================================================================
function Initialize-HccInstallContext {
    param([string]$AppDirectory, [string]$DataDirectory, [string]$OperationsDirectory)
    $script:AppDir = $AppDirectory
    $script:DataDir = $DataDirectory
    $script:OpsDir = $OperationsDirectory
    $script:ServerDir = Join-Path $AppDirectory 'Server'
    $script:ClientKitDir = Join-Path $AppDirectory 'ClientKit'
    $script:SqlMediaDir = Join-Path $AppDirectory 'SqlSetupMedia'
    $script:SettingsFile = Join-Path $script:ServerDir 'appsettings.Production.json'
    $script:SettingsSample = Join-Path $script:ServerDir 'appsettings.Production.sample.json'
    $script:OpsConfigFile = Join-Path $OperationsDirectory 'hcc-ops.json'
    $script:OpsConfigSample = Join-Path $OperationsDirectory 'hcc-ops.sample.json'
    $script:SetupDir = Join-Path $DataDirectory 'Setup'
    $script:LogsDir = Join-Path $DataDirectory 'Logs'
    $script:KeysDir = Join-Path $DataDirectory 'Keys'
    $script:CertsDir = Join-Path $DataDirectory 'certs'
    $script:TrialCopiesDir = Join-Path $DataDirectory 'TrialKeyCopies'
    $script:StateFile = Join-Path $script:SetupDir 'install-state.json'
    $script:ProgressFile = Join-Path $script:SetupDir 'progress.txt'
    $script:SummaryFile = Join-Path $script:SetupDir 'install-summary.txt'
    $script:CaCer = Join-Path $script:CertsDir 'HCC-Hotel-CA.cer'
}

function Read-HccState {
    if (-not (Test-Path -LiteralPath $script:StateFile)) { return $null }
    try { return (Read-HccTextFile $script:StateFile) | ConvertFrom-Json } catch { return $null }
}

function Save-HccState {
    param($State)
    $tmp = $script:StateFile + '.tmp'
    Write-HccTextFile $tmp ($State | ConvertTo-Json -Depth 6)
    Move-Item -LiteralPath $tmp -Destination $script:StateFile -Force
}

function Write-HccProgress {
    param([int]$Index, [int]$Count, [string]$Title, [string]$Status = 'running')
    $text = "pid=$PID`r`nstatus=$Status`r`nstep=$Index`r`nof=$Count`r`ntitle=$Title`r`n"
    Write-HccTextFile $script:ProgressFile $text
}

# SHA-256 of the hotel CA certificate (plain upper-case hex, '' when there is none); the client setup shows the same value on each PC.
function Get-HccCaFingerprint {
    if ($script:CaCer -and (Test-Path -LiteralPath $script:CaCer)) { try { return Get-HccFileSha256 -Path $script:CaCer } catch { return '' } }
    return ''
}

function Format-HccFingerprint {
    param([string]$Hex)
    if (-not $Hex) { return '' }
    return (($Hex -split '(?<=\G.{2})(?=.)') -join ':')
}

function Write-HccSummary {
    param($State, [string]$Status)
    $lines = @(
        'status=' + $Status,
        'mode=' + [string](Get-HccProp $State 'mode' ''),
        'version=' + [string](Get-HccProp $State 'version' ''),
        'serverName=' + [string](Get-HccProp $State 'serverName' ''),
        'httpsPort=' + [string](Get-HccProp $State 'httpsPort' ''),
        'serverUrl=' + [string](Get-HccProp $State 'serverUrl' ''),
        'caSha256=' + (Get-HccCaFingerprint),
        'logFile=' + [string]$script:LogFile)
    Write-HccTextFile $script:SummaryFile (($lines -join "`r`n") + "`r`n")
}

# ======================================================================================================================
# Answers
# ======================================================================================================================
function ConvertTo-HccAnswers {
    param($Json)
    $ips = @(Get-HccProp $Json 'serverIps' @() | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ })
    return @{
        mode = ([string](Get-HccProp $Json 'mode' '')).Trim()
        hotelName = ([string](Get-HccProp $Json 'hotelName' '')).Trim()
        serverName = ([string](Get-HccProp $Json 'serverName' '')).Trim().ToLowerInvariant()
        serverIps = $ips
        httpsPort = [int](Get-HccProp $Json 'httpsPort' 5443)
        backupDirectory = ([string](Get-HccProp $Json 'backupDirectory' '')).Trim()
        ownerUserName = ([string](Get-HccProp $Json 'ownerUserName' '')).Trim().ToLowerInvariant()
        ownerPassword = [string](Get-HccProp $Json 'ownerPassword' '')
        sql = ([string](Get-HccProp $Json 'sql' '')).Trim()
        caPfxPassword = [string](Get-HccProp $Json 'caPfxPassword' '')
        offlineCopyDirectory = ([string](Get-HccProp $Json 'offlineCopyDirectory' '')).Trim()
    }
}

# Plain-words problems with the answers (the wizard checks the same things; this is the second check). Empty = fine.
function Test-HccAnswers {
    param([hashtable]$A)
    $p = New-Object System.Collections.Generic.List[string]
    if ($A.mode -notin @('Trial', 'Production')) { $p.Add('The kind of installation must be Trial or Production.') }
    if ($A.hotelName.Length -lt 1 -or $A.hotelName.Length -gt 100) { $p.Add('The hotel name must be 1 to 100 characters.') }
    if ($A.hotelName -match '[\x00-\x1f]') { $p.Add('The hotel name must not contain control characters.') }
    if ($A.serverName -notmatch '^[a-z0-9]([a-z0-9.-]{0,61}[a-z0-9])?$') { $p.Add('This PC''s name may contain only letters, digits, dots and dashes.') }
    if ($A.serverIps.Count -lt 1) { $p.Add('Give at least one network address of this PC (for example 192.168.1.10).') }
    foreach ($ip in $A.serverIps) {
        $parsed = $null
        if (-not [Net.IPAddress]::TryParse($ip, [ref]$parsed)) { $p.Add("'$ip' is not a valid network address.") }
    }
    if ($A.httpsPort -lt 1024 -or $A.httpsPort -gt 65535) { $p.Add('The port must be between 1024 and 65535.') }
    if ($A.backupDirectory -notmatch '^[A-Za-z]:\\.+') { $p.Add('The backup folder must be a full folder path on a drive, for example D:\HccBackups.') }
    elseif ($A.backupDirectory.StartsWith($script:AppDir, [StringComparison]::OrdinalIgnoreCase)) { $p.Add('The backup folder must not be inside the HCC program folder.') }
    if ($A.ownerUserName -notmatch '^[a-z0-9._-]{3,40}$') { $p.Add('The Owner''s sign-in name must be 3 to 40 characters: letters, digits, dot, dash or underscore.') }
    $pw = $A.ownerPassword
    if ($pw.Length -lt 12 -or $pw.Length -gt 128) { $p.Add('The Owner''s password must be 12 to 128 characters.') }
    elseif ($A.ownerUserName -and $pw.IndexOf($A.ownerUserName, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $p.Add('The Owner''s password must not contain the sign-in name.') }
    elseif (@($pw.ToCharArray() | Select-Object -Unique).Count -lt 4) { $p.Add('The Owner''s password is too simple.') }
    if ($A.sql -notin @('InstallBundled', 'UseExisting')) { $p.Add('SQL Server must be InstallBundled or UseExisting.') }
    if ($A.mode -eq 'Production') {
        if ($A.caPfxPassword.Length -lt 12) { $p.Add('The certificate password must be at least 12 characters.') }
        if (-not $A.offlineCopyDirectory) { $p.Add('Production needs the folder on the USB stick where the key copies are saved.') }
        elseif (-not (Test-Path -LiteralPath $A.offlineCopyDirectory -PathType Container)) { $p.Add("The USB folder $($A.offlineCopyDirectory) was not found. Plug in the stick.") }
        else {
            $sysRoot = Get-HccInstallDriveRoot
            if ([IO.Path]::GetPathRoot($A.offlineCopyDirectory) -ieq $sysRoot) { $p.Add('The key copies must go to a USB stick, not to the drive HCC is installed on (they would stay on this PC).') }
            if ($A.offlineCopyDirectory.StartsWith($script:KeysDir, [StringComparison]::OrdinalIgnoreCase) -or $A.offlineCopyDirectory.StartsWith($script:AppDir, [StringComparison]::OrdinalIgnoreCase)) { $p.Add('The USB folder must not be inside the HCC folders.') }
        }
    }
    return @($p.ToArray())
}

# ======================================================================================================================
# Settings files
# ======================================================================================================================
function Set-HccJsonValue {
    # Sets Parent.Name = Value, creating nested objects as needed. Path is dotted: 'Hcc.Hosting.ServerName'.
    param($Root, [string]$Path, $Value)
    $parts = $Path -split '\.'
    $node = $Root
    for ($i = 0; $i -lt $parts.Count - 1; $i++) {
        if (-not $node.PSObject.Properties[$parts[$i]]) { $node | Add-Member -NotePropertyName $parts[$i] -NotePropertyValue ([pscustomobject]@{}) }
        $node = $node.($parts[$i])
    }
    $node | Add-Member -NotePropertyName $parts[-1] -NotePropertyValue $Value -Force
}

# Adds what the sample has and the existing file lacks, recursively. An existing value is never changed. Keys that start with '_' (notes) are skipped.
function Merge-HccJsonMissing {
    param($Existing, $Sample)
    $added = 0
    foreach ($prop in $Sample.PSObject.Properties) {
        if ($prop.Name.StartsWith('_')) { continue }
        $have = $Existing.PSObject.Properties[$prop.Name]
        if (-not $have) { $Existing | Add-Member -NotePropertyName $prop.Name -NotePropertyValue $prop.Value; $added++; continue }
        if ($prop.Value -is [pscustomobject] -and $have.Value -is [pscustomobject]) { $added += Merge-HccJsonMissing -Existing $have.Value -Sample $prop.Value }
    }
    return $added
}

function Save-HccJsonInPlace {
    # Overwrites the file's content (its protective permissions stay) after a private safety copy.
    param([string]$Path, $Object, [string]$Tag)
    $safety = "$Path.before-$Tag"
    Copy-Item -LiteralPath $Path -Destination $safety -Force
    Protect-HccFile -Path $safety
    Write-HccTextFile $Path ($Object | ConvertTo-Json -Depth 12)
}

function Confirm-HccSettingsFile {
    if (-not (Test-Path -LiteralPath $script:SettingsFile)) {
        if (-not (Test-Path -LiteralPath $script:SettingsSample)) { throw "Neither appsettings.Production.json nor its sample exists in $script:ServerDir." }
        Copy-Item -LiteralPath $script:SettingsSample -Destination $script:SettingsFile
    }
}

function Update-HccSettingsFresh {
    param([hashtable]$A)
    Confirm-HccSettingsFile
    $json = (Read-HccTextFile $script:SettingsFile) | ConvertFrom-Json
    Set-HccJsonValue $json 'Hcc.Sql.BackupDirectory' $A.backupDirectory
    Set-HccJsonValue $json 'Hcc.Hosting.ServerName' $A.serverName
    Set-HccJsonValue $json 'Hcc.Hosting.HttpsPort' $A.httpsPort
    Set-HccJsonValue $json 'Hcc.Operations.BackupScript' (Join-Path $script:OpsDir 'Backup-HccDatabase.ps1')
    Set-HccJsonValue $json 'Hcc.Operations.SqlInstance' '.\SQLEXPRESS'
    Write-HccTextFile $script:SettingsFile ($json | ConvertTo-Json -Depth 12)
    Write-InstallLog "Settings written to $script:SettingsFile (server name, port, backup folder)."

    if (-not (Test-Path -LiteralPath $script:OpsConfigFile)) {
        if (-not (Test-Path -LiteralPath $script:OpsConfigSample)) { throw "Neither hcc-ops.json nor its sample exists in $script:OpsDir." }
        Copy-Item -LiteralPath $script:OpsConfigSample -Destination $script:OpsConfigFile
    }
    $ops = (Read-HccTextFile $script:OpsConfigFile) | ConvertFrom-Json
    Set-HccJsonValue $ops 'BackupDirectory' $A.backupDirectory
    Set-HccJsonValue $ops 'LogDirectory' (Join-Path $A.backupDirectory 'Logs')
    Set-HccJsonValue $ops 'ScratchDirectory' (Join-Path $A.backupDirectory 'DrillScratch')
    Set-HccJsonValue $ops 'ServerDirectory' $script:ServerDir
    Set-HccJsonValue $ops 'BackupKeyFile' (Join-Path $script:KeysDir 'hcc-backup-key.txt')
    Write-HccTextFile $script:OpsConfigFile ($ops | ConvertTo-Json -Depth 6)
    Write-InstallLog "Backup settings written to $script:OpsConfigFile."
}

function Update-HccSettingsMerge {
    $total = 0
    if ((Test-Path -LiteralPath $script:SettingsFile) -and (Test-Path -LiteralPath $script:SettingsSample)) {
        $existing = (Read-HccTextFile $script:SettingsFile) | ConvertFrom-Json
        $sample = (Read-HccTextFile $script:SettingsSample) | ConvertFrom-Json
        $n = Merge-HccJsonMissing -Existing $existing -Sample $sample
        if ($n -gt 0) { Save-HccJsonInPlace -Path $script:SettingsFile -Object $existing -Tag 'upgrade' }
        Write-InstallLog "Settings: $n new setting(s) added to appsettings.Production.json; existing values were not changed."
        $total += $n
    }
    else { Write-InstallLog 'Settings: no appsettings.Production.json or sample to merge.' 'WARN' }
    if ((Test-Path -LiteralPath $script:OpsConfigFile) -and (Test-Path -LiteralPath $script:OpsConfigSample)) {
        $existing = (Read-HccTextFile $script:OpsConfigFile) | ConvertFrom-Json
        $sample = (Read-HccTextFile $script:OpsConfigSample) | ConvertFrom-Json
        $n = Merge-HccJsonMissing -Existing $existing -Sample $sample
        if ($n -gt 0) { Save-HccJsonInPlace -Path $script:OpsConfigFile -Object $existing -Tag 'upgrade' }
        Write-InstallLog "Settings: $n new setting(s) added to hcc-ops.json; existing values were not changed."
        $total += $n
    }
    return $total
}

$script:TrialMarker = '<!--HCC-TRIAL-BANNER-->'
function Set-HccTrialPage {
    # TRIAL - not for real guest data: the browser-tab title and a banner at the top of the sign-in page. Only presentational HTML
    # (no script, no style block, no style attribute), so the page's Content-Security-Policy hashes stay valid.
    param([string]$IndexHtml)
    if (-not (Test-Path -LiteralPath $IndexHtml)) { throw "The web page $IndexHtml is missing, so the TRIAL banner cannot be added. The installation is incomplete." }
    $html = Read-HccTextFile $IndexHtml
    if ($html.Contains($script:TrialMarker)) { return }
    $banner = $script:TrialMarker + '<table width="100%" bgcolor="#b00020" cellpadding="6" cellspacing="0" border="0"><tr><td align="center"><font color="#ffffff" face="Arial" size="3"><b>TRIAL - not for real guest data</b></font></td></tr></table>'
    $body = [regex]::Match($html, '<body[^>]*>', 'IgnoreCase')
    if (-not $body.Success) { throw "The web page $IndexHtml has no <body> tag; the TRIAL banner cannot be added." }
    $html = $html.Insert($body.Index + $body.Length, $banner)
    $html = [regex]::Replace($html, '<title>', '<title>TRIAL - ', 'IgnoreCase')
    Write-HccTextFile $IndexHtml $html
    Write-InstallLog 'TRIAL banner added to the sign-in page.'
}

# ======================================================================================================================
# Steps
# ======================================================================================================================
function Get-HccThumbprint { $s = (Read-HccTextFile $script:SettingsFile) | ConvertFrom-Json; return [string](Get-HccProp (Get-HccProp (Get-HccProp $s 'Hcc') 'Hosting') 'CertificateThumbprint' '') }
function Get-HccLoopbackPort { $s = (Read-HccTextFile $script:SettingsFile) | ConvertFrom-Json; return [int](Get-HccProp (Get-HccProp (Get-HccProp $s 'Hcc') 'Hosting') 'LoopbackHttpPort' 5080) }
function Test-HccGuestIdKeyConfigured {
    if (-not (Test-Path -LiteralPath $script:SettingsFile)) { return $false }
    $s = (Read-HccTextFile $script:SettingsFile) | ConvertFrom-Json
    return [bool](Get-HccProp (Get-HccProp (Get-HccProp $s 'Hcc') 'GuestIdKeys') 'Current' '')
}

function Update-HccProcessPath {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = (@($machine, $user) | Where-Object { $_ }) -join ';'
}

function Test-HccScriptParameter {
    param([string]$Script, [string]$Name)
    try { return (Get-Command (Join-Path $script:OpsDir $Script) -ErrorAction Stop).Parameters.ContainsKey($Name) } catch { return $false }
}

# Warnings about an adopted SQL instance (mixed-mode logins, TCP/IP on, service not automatic): shown in the log AND on the wizard.
function Read-HccSqlWarnings {
    param([string]$Path)
    $out = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    try {
        $j = (Read-HccTextFile $Path) | ConvertFrom-Json
        $items = $j
        if ($j -and $j.PSObject.Properties['warnings']) { $items = $j.warnings }   # { "warnings": [ { "code", "text" } ] }
        foreach ($w in @($items)) {
            if ($null -eq $w) { continue }
            if ($w -is [string]) { $out.Add($w) }
            else {
                foreach ($n in @('text', 'message', 'warning', 'detail')) { if ($w.PSObject.Properties[$n] -and $w.$n) { $out.Add([string]$w.$n); break } }
            }
        }
    }
    catch { $out.Add('SQL Server warnings file could not be read: ' + $Path) }
    return $out.ToArray()
}

function Step-Sql {
    param([hashtable]$A)
    $sqlParams = @{ Mode = $A.sql; MediaDir = $script:SqlMediaDir }
    if ($script:LogFile) { $sqlParams.LogFile = $script:LogFile }
    $warnFile = Join-Path $script:SetupDir 'sql-warnings.json'
    Remove-Item -LiteralPath $warnFile -Force -ErrorAction SilentlyContinue
    if (Test-HccScriptParameter -Script 'Bootstrap-HccSql.ps1' -Name 'WarningsFile') { $sqlParams.WarningsFile = $warnFile }
    $code = Invoke-HccOps -Script 'Bootstrap-HccSql.ps1' -Params $sqlParams -UseExitCode
    if ($code -eq 2) { $script:StepOutcome = 'restart'; Write-InstallLog 'SQL Server needs Windows to restart before it can finish.' 'WARN'; return }
    if ($code -ne 0) { throw "The SQL Server step ended with code $code (see the lines above)." }
    Update-HccProcessPath   # the bootstrap can only change its own PATH; later steps need sqlcmd
    $warnings = @(Read-HccSqlWarnings -Path $warnFile)
    foreach ($w in $warnings) { Write-InstallLog ('SQL Server: ' + $w) 'WARN' }
    if ($warnings.Count -gt 0) { Write-HccTextFile (Join-Path $script:SetupDir 'warnings.txt') ((@($warnings | ForEach-Object { 'SQL Server: ' + $_ }) -join "`r`n") + "`r`n") }
    if (Test-Path -LiteralPath $script:SqlMediaDir) { Remove-Item -LiteralPath $script:SqlMediaDir -Recurse -Force -ErrorAction SilentlyContinue }
}

function Step-Database {
    param([hashtable]$A)
    Invoke-HccOps -Script 'Initialize-HccDatabase.ps1' -Params @{ ServiceAccount = $script:ServiceAccountName; BackupDirectory = $A.backupDirectory; GrantDrillRights = $true } | Out-Null
}

function Step-Certificate {
    param([hashtable]$A)
    Confirm-HccSettingsFile
    $thumb = Get-HccThumbprint
    if ($thumb -and (Test-HccServerCertificatePresent $thumb) -and (Test-Path -LiteralPath $script:CaCer)) {
        Write-InstallLog "A server certificate ($thumb) already exists on this PC; it is kept."
        Install-HccTrustedCa -CerPath $script:CaCer
        return
    }
    if (-not (Test-Path -LiteralPath $script:CertsDir)) { New-Item -ItemType Directory -Path $script:CertsDir -Force | Out-Null }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $trialFiles = @()
    if ($A.mode -eq 'Trial') {
        Protect-HccFolder -Path $script:KeysDir
        $pfx = Join-Path $script:KeysDir 'HCC-Hotel-CA.pfx'
        $pwFile = Join-Path $script:KeysDir 'HCC-Hotel-CA-password.txt'
        $caPassword = New-HccRandomPassword
        Add-HccSecret $caPassword
        Write-HccTextFile $pwFile ("TRIAL install: password of HCC-Hotel-CA.pfx (kept on this PC, only Administrators and SYSTEM can read it).`r`n" + $caPassword + "`r`n")
        $trialFiles = @($pfx, $pwFile)
        Write-InstallLog "TRIAL: the hotel CA key is kept in $pfx; its generated password is beside it. Not for real guest data."
    }
    else {
        $caPassword = $A.caPfxPassword
        $pfx = Join-Path $A.offlineCopyDirectory ("HCC-Hotel-CA-$stamp.pfx")
        Write-InstallLog "The hotel CA key will be written to $pfx (keep the stick and the password in the safe)."
    }
    $params = @{
        ServerName = $A.serverName; DnsName = @($A.serverName); IpAddress = @($A.serverIps); CaPfxPath = $pfx
        CaPfxPassword = (ConvertTo-HccSecureString $caPassword); OutputDirectory = $script:CertsDir; UpdateConfig = $script:SettingsFile
    }
    try { Invoke-HccOps -Script 'New-HccCertificate.ps1' -Params $params | Out-Null }
    catch {
        foreach ($f in $trialFiles) { Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue }
        throw
    }
    if (-not (Test-Path -LiteralPath $pfx)) { throw "The hotel CA backup file $pfx was not written." }
    if (-not (Test-Path -LiteralPath $script:CaCer)) { throw "The hotel CA certificate $script:CaCer was not written." }
    Remove-HccLeftoverCaKey
    Install-HccTrustedCa -CerPath $script:CaCer
    Write-InstallLog "The hotel CA is trusted on this PC. CA certificate (public): $script:CaCer"
}

function Step-Settings {
    param([hashtable]$A)
    Update-HccSettingsFresh -A $A
    if ($A.mode -eq 'Trial') { Set-HccTrialPage -IndexHtml (Join-Path $script:ServerDir 'web\index.html') }
}

function Step-Keys {
    param([hashtable]$A)
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    if ($A.mode -eq 'Trial') {
        Protect-HccFolder -Path $script:TrialCopiesDir
        $copyDir = $script:TrialCopiesDir
        Write-InstallLog 'TRIAL: key copies stay on this PC (folder TrialKeyCopies, Administrators only); no USB stick is used.'
    }
    else { $copyDir = $A.offlineCopyDirectory }
    if (Test-HccGuestIdKeyConfigured) { Write-InstallLog 'The guest ID key already exists; it is kept.' }
    else {
        $dest = Join-Path $copyDir ("HCC-guest-id-key-$stamp.txt")
        Invoke-HccOps -Script 'New-HccGuestIdKey.ps1' -Params @{ SettingsFile = $script:SettingsFile; OfflineCopyPath = $dest } | Out-Null
    }
    $backupKey = Join-Path $script:KeysDir 'hcc-backup-key.txt'
    if (Test-Path -LiteralPath $backupKey) { Write-InstallLog 'The backup key already exists; it is kept.' }
    else {
        $dest = Join-Path $copyDir ("HCC-backup-key-$stamp.txt")
        Invoke-HccOps -Script 'New-HccBackupKey.ps1' -Params @{ OfflineCopyPath = $dest } | Out-Null
    }
}

function Step-Migrate {
    param([hashtable]$A)
    $exe = Join-Path $script:ServerDir 'Hcc.Api.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "Hcc.Api.exe was not found in $script:ServerDir." }
    $r = Invoke-HccNative -FilePath $exe -Arguments 'migrate' -WorkingDirectory $script:ServerDir -Environment @{ ASPNETCORE_ENVIRONMENT = 'Production' }
    Write-InstallLog ([string]$r.Output)
    if ($r.ExitCode -ne 0) { throw "Hcc.Api.exe migrate failed with code $($r.ExitCode) (see the lines above)." }
}

function Step-Service {
    param([hashtable]$A)
    if (Test-HccServiceInstalled) { Write-InstallLog 'The service HccServer already exists; it is kept.' }
    else { Invoke-HccOps -Script 'Install-HccService.ps1' -Params @{ InstallDirectory = $script:ServerDir; HttpsPort = $A.httpsPort } | Out-Null }
    $thumb = Get-HccThumbprint
    try { Grant-HccCertificateKeyAccess -Thumbprint $thumb -Account $script:ServiceAccountName }
    catch { Write-InstallLog ("Could not confirm the service's access to the certificate key: " + $_.Exception.Message) 'WARN' }
    Start-HccServerService
    Write-InstallLog 'The HCC service is running.'
}

function Step-Property {
    param([hashtable]$A)
    if ((Get-HccLoopbackPort) -eq 0) { Write-InstallLog 'The hotel and the first Owner are already set up (the setup listener is off).'; return }
    $status = Wait-HccUrl -Url ('http://127.0.0.1:' + (Get-HccLoopbackPort) + '/api/v1/setup/status') -Seconds 120
    if ($status.Status -ne 200) { throw 'The HCC server did not answer on this PC in time, so the hotel could not be set up. Check the Windows Event Log (source HCC) and run the setup again.' }
    $params = @{
        HotelName = $A.hotelName; Address = '(enter the address in Master configuration after signing in)'; OwnerLogin = $A.ownerUserName
        OwnerDisplayName = $A.ownerUserName; OwnerPassword = (ConvertTo-HccSecureString $A.ownerPassword); SettingsFile = $script:SettingsFile
    }
    Invoke-HccOps -Script 'Initialize-HccProperty.ps1' -Params $params | Out-Null
}

function Step-BackupTasks {
    param([hashtable]$A)
    Invoke-HccOps -Script 'Backup-HccDatabase.ps1' -Params @{ Type = 'Full' } | Out-Null
    Invoke-HccOps -Script 'Register-HccBackupTasks.ps1' -Params @{ ScriptDirectory = $script:OpsDir } | Out-Null
}

function Step-Egress {
    param([hashtable]$A)
    Invoke-HccOps -Script 'Set-HccEgress.ps1' -Params @{ State = 'Install'; ServiceExe = (Join-Path $script:ServerDir 'Hcc.Api.exe') } | Out-Null
}

function Step-ClientKit {
    param([hashtable]$A)
    Invoke-HccOps -Script 'New-HccClientKit.ps1' -Params @{
        ServerName = $A.serverName; Port = $A.httpsPort; HotelName = $A.hotelName; CaCer = $script:CaCer; OutDir = $script:ClientKitDir
    } | Out-Null
    foreach ($f in @('hcc-client.json', 'HCC-Hotel-CA.cer')) {
        if (-not (Test-Path -LiteralPath (Join-Path $script:ClientKitDir $f))) { throw "The client kit is incomplete: $f is missing in $script:ClientKitDir." }
    }
}

function Step-Health {
    param([hashtable]$A)
    $base = 'https://{0}:{1}' -f $A.serverName, $A.httpsPort
    $health = Wait-HccUrl -Url "$base/api/v1/system/health" -Seconds 120
    if ($health.Status -ne 200) {
        $why = if ($health.Status -eq 503) { 'the database is behind this version (migrate did not finish)' } elseif ($health.Status -eq 0) { $health.Error } else { 'HTTP ' + $health.Status }
        $local = Invoke-HccHttp -Url ('https://localhost:{0}/api/v1/system/health' -f $A.httpsPort)
        if ($local.Status -eq 200) { Write-InstallLog "The server answers on https://localhost:$($A.httpsPort)/ but not on $base/ ($why). Check the PC name and the network." 'WARN' }
        else { throw "The HCC server does not answer at $base/api/v1/system/health ($why)." }
    }
    else {
        $ok = $false
        try { $ok = ((ConvertFrom-Json $health.Body).status -eq 'ok') } catch { $ok = $false }
        if (-not $ok) { throw "The HCC server answered at $base/api/v1/system/health but did not say ok." }
    }
    $page = Invoke-HccHttp -Url "$base/"
    if ($page.Status -ne 200) {
        $local = Invoke-HccHttp -Url ('https://localhost:{0}/' -f $A.httpsPort)
        if ($local.Status -ne 200) { throw "The HCC web page does not open at $base/ (HTTP $($page.Status)). Is Server\web\index.html present?" }
        Write-InstallLog "The web page opens on https://localhost:$($A.httpsPort)/ but not on $base/." 'WARN'
    }
    Write-InstallLog "Health check passed: $base/ answers and the database is current."
    $fp = Get-HccCaFingerprint
    if ($fp) { Write-InstallLog ('Hotel CA SHA-256 fingerprint: ' + (Format-HccFingerprint $fp) + ' - compare this with what the client setup shows on each PC.') }
}

function Get-HccInstallSteps {
    param([hashtable]$A)
    $mode = [string]$A.mode
    return @(
        @{ Id = 'sql'; Title = 'Preparing SQL Server'; Run = { param($A) Step-Sql $A }; Plan = "Bootstrap-HccSql.ps1 -Mode $($A.sql) -MediaDir <app>\SqlSetupMedia (exit 2 = restart Windows)" }
        @{ Id = 'database'; Title = 'Creating the HCC databases'; Run = { param($A) Step-Database $A }; Plan = "Initialize-HccDatabase.ps1 -ServiceAccount 'NT SERVICE\HccServer' -BackupDirectory '$($A.backupDirectory)' -GrantDrillRights" }
        @{ Id = 'certificate'; Title = 'Creating the hotel certificate'; Run = { param($A) Step-Certificate $A }; Plan = $(if ($mode -eq 'Trial') { 'New-HccCertificate.ps1 -UpdateConfig (TRIAL: CA key kept in an ACL''d PFX in ProgramData\HCC\Keys, generated password beside it); trust the CA on this PC' } else { "New-HccCertificate.ps1 -UpdateConfig; CA key written to the USB folder '$($A.offlineCopyDirectory)' and removed from this PC; trust the CA on this PC" }) }
        @{ Id = 'settings'; Title = 'Writing the settings'; Run = { param($A) Step-Settings $A }; Plan = $(if ($mode -eq 'Trial') { 'Write appsettings.Production.json and hcc-ops.json; add the TRIAL banner to the sign-in page' } else { 'Write appsettings.Production.json and hcc-ops.json' }) }
        @{ Id = 'keys'; Title = 'Creating the guest ID key and the backup key'; Run = { param($A) Step-Keys $A }; Plan = $(if ($mode -eq 'Trial') { 'New-HccGuestIdKey.ps1, New-HccBackupKey.ps1 (TRIAL: copies stay in ProgramData\HCC\TrialKeyCopies)' } else { "New-HccGuestIdKey.ps1, New-HccBackupKey.ps1 (key copies to '$($A.offlineCopyDirectory)')" }) }
        @{ Id = 'migrate'; Title = 'Building the database tables'; Run = { param($A) Step-Migrate $A }; Plan = 'Hcc.Api.exe migrate (ASPNETCORE_ENVIRONMENT=Production, as the administrator)' }
        @{ Id = 'service'; Title = 'Installing and starting the HCC service'; Run = { param($A) Step-Service $A }; Plan = "Install-HccService.ps1 -HttpsPort $($A.httpsPort); start HccServer" }
        @{ Id = 'property'; Title = 'Setting up the hotel and the first Owner'; Run = { param($A) Step-Property $A }; Plan = "Initialize-HccProperty.ps1 (hotel '$($A.hotelName)', Owner '$($A.ownerUserName)', password hidden); then the setup listener is closed" }
        @{ Id = 'backup-tasks'; Title = 'Scheduling the backups'; Run = { param($A) Step-BackupTasks $A }; Plan = 'Backup-HccDatabase.ps1 -Type Full; Register-HccBackupTasks.ps1' }
        @{ Id = 'egress'; Title = 'Blocking internet access for HCC'; Run = { param($A) Step-Egress $A }; Plan = 'Set-HccEgress.ps1 -State Install' }
        @{ Id = 'client-kit'; Title = 'Preparing the client kit'; Run = { param($A) Step-ClientKit $A }; Plan = 'New-HccClientKit.ps1 -OutDir <app>\ClientKit' }
        @{ Id = 'health'; Title = 'Checking that HCC answers'; Run = { param($A) Step-Health $A }; Plan = "https://$($A.serverName):$($A.httpsPort)/ answers and the API reports the schema current" }
    )
}

function Get-HccUpgradeSteps {
    param([hashtable]$A)
    return @(
        @{ Id = 'settings'; Title = 'Merging new settings'; Run = { param($A) Update-HccSettingsMerge | Out-Null; if ($A.mode -eq 'Trial') { Set-HccTrialPage -IndexHtml (Join-Path $script:ServerDir 'web\index.html') } }; Plan = 'Add new settings to appsettings.Production.json and hcc-ops.json (existing values are never changed)' }
        @{ Id = 'migrate'; Title = 'Updating the database tables'; Run = { param($A) Stop-HccServerService; Step-Migrate $A }; Plan = 'Hcc.Api.exe migrate (the server is stopped first)' }
        @{ Id = 'service'; Title = 'Starting the HCC service'; Run = { param($A) Start-HccServerService }; Plan = 'Start HccServer' }
        @{ Id = 'client-kit'; Title = 'Refreshing the client kit'; Run = { param($A) if ($A.hotelName -and (Test-Path -LiteralPath $script:CaCer)) { Step-ClientKit $A } else { Write-InstallLog 'No client kit to refresh (no earlier setup information).' } }; Plan = 'New-HccClientKit.ps1 -OutDir <app>\ClientKit' }
        @{ Id = 'health'; Title = 'Checking that HCC answers'; Run = { param($A) Step-Health $A }; Plan = "https://$($A.serverName):$($A.httpsPort)/ answers" }
    )
}

# ======================================================================================================================
# Runner
# ======================================================================================================================
function Invoke-HccSteps {
    # Returns 0 done, 2 restart needed, 1 failed. $State is the object saved in install-state.json.
    param($Steps, $State, $Answers)
    $count = @($Steps).Count
    $i = 0
    foreach ($s in $Steps) {
        $i++
        $done = @($State.completed)
        if ($done -contains $s.Id) { Write-InstallLog "Step $i of ${count}: $($s.Title) - already done, skipped."; continue }
        Write-InstallLog "Step $i of ${count}: $($s.Title)"
        Write-HccProgress -Index $i -Count $count -Title $s.Title
        $State.currentStep = $s.Id
        Save-HccState $State
        $script:StepOutcome = ''
        try { & $s.Run $Answers }
        catch {
            Write-InstallLog ("Step $i of $count ($($s.Id)) FAILED: " + $_.Exception.Message) 'ERROR'
            $State.status = 'Failed'; $State.failedStep = $s.Id
            Save-HccState $State
            Write-HccProgress -Index $i -Count $count -Title $s.Title -Status 'failed'
            return 1
        }
        if ($script:StepOutcome -eq 'restart') {
            Write-InstallLog "Windows must restart before step $i ($($s.Id)) can finish. Restart the PC, then run the HCC setup again (it continues from this step)." 'WARN'
            $State.status = 'RestartNeeded'; $State.failedStep = $s.Id
            Save-HccState $State
            Write-HccProgress -Index $i -Count $count -Title $s.Title -Status 'restart'
            return 2
        }
        $State.completed = @($State.completed) + $s.Id
        $State.failedStep = ''
        Save-HccState $State
    }
    return 0
}

function Remove-HccAnswerFile {
    param([string]$Path)
    if (-not $Path -or -not (Test-Path -LiteralPath $Path)) { return }
    try {
        $len = (Get-Item -LiteralPath $Path).Length
        [IO.File]::WriteAllBytes($Path, (New-Object byte[] $len))
    }
    catch { }
    Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
}

function Show-HccPlan {
    param($Steps, [string]$Title)
    Write-Host $Title
    $i = 0
    foreach ($s in $Steps) { $i++; Write-Host ('  {0,2}. {1} - {2}' -f $i, $s.Title, $s.Plan) }
    Write-Host 'PLAN ONLY: nothing was changed (no file, service, certificate, firewall rule, scheduled task or SQL object).'
}

function Invoke-HccUninstall {
    param([switch]$Plan)
    $actions = @(
        'Stop and remove the service HccServer and its firewall rule (Install-HccService.ps1 -Uninstall)',
        'Remove the scheduled tasks \HCC\ (Register-HccBackupTasks.ps1 -Unregister, ToggleEgress)',
        'Remove the firewall rule "HCC Egress Block"',
        'KEPT: SQL Server, the databases Hcc and HccDocuments, the backups, the keys, the certificates, the settings files (appsettings.Production.json, hcc-ops.json)')
    if ($Plan) { foreach ($a in $actions) { Write-Host "  $a" }; Write-Host 'PLAN ONLY: nothing was changed.'; return 0 }
    $failed = 0
    try { Invoke-HccOps -Script 'Install-HccService.ps1' -Params @{ Uninstall = $true } | Out-Null } catch { $failed++; Write-InstallLog ('Service removal: ' + $_.Exception.Message) 'WARN' }
    try { Invoke-HccOps -Script 'Register-HccBackupTasks.ps1' -Params @{ Unregister = $true } | Out-Null } catch { $failed++; Write-InstallLog ('Backup tasks removal: ' + $_.Exception.Message) 'WARN' }
    try {
        Get-NetFirewallRule -DisplayName 'HCC Egress Block' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
        Unregister-ScheduledTask -TaskName 'ToggleEgress' -TaskPath '\HCC\' -Confirm:$false -ErrorAction SilentlyContinue
    }
    catch { $failed++; Write-InstallLog ('Egress removal: ' + $_.Exception.Message) 'WARN' }
    if ($failed -gt 0) { Write-InstallLog "$failed removal step(s) did not finish; the program files are removed anyway. Run Install-HccService.ps1 -Uninstall by hand if the service is still listed." 'WARN'; return 1 }
    if (Test-Path -LiteralPath $script:SetupDir) { Write-HccSummary (Read-HccState) 'Uninstalled' }
    Write-InstallLog 'HCC service, firewall rules and scheduled tasks removed. SQL Server, the databases, the backups, the keys, the certificates and the settings files were not touched.'
    return 0
}

function Start-HccInstall {
    param([string]$AnswerFile, [switch]$Resume, [switch]$PlanOnly, [switch]$Upgrade, [switch]$Uninstall, [string]$AppDirectory, [string]$DataDirectory, [string]$OperationsDirectory, [bool]$WhatIfMode = $false)
    Set-StrictMode -Version Latest
    $ErrorActionPreference = 'Stop'
    Initialize-HccInstallContext -AppDirectory $AppDirectory -DataDirectory $DataDirectory -OperationsDirectory $OperationsDirectory
    $plan = $PlanOnly -or $WhatIfMode
    $script:PlanMode = $plan
    $script:Secrets.Clear()
    $script:LogFile = $null
    $version = ''
    try { $version = (Read-HccTextFile (Join-Path $script:AppDir 'VERSION.txt')).Trim() } catch { $version = '' }
    if (-not $plan) {
        if (-not (Test-HccElevated)) { Write-Host 'Run this from an administrator window.'; return 1 }
        try { Initialize-HccDataFolders } catch { Write-Host ('The installation stopped: ' + $_.Exception.Message); return 1 }
        $script:LogFile = Join-Path $script:LogsDir ('install-{0}.log' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    }
    try {
        if ($Uninstall) {
            if (-not $plan -and -not (Test-HccElevated)) { Write-Host 'Run this from an administrator window.'; return 1 }
            return (Invoke-HccUninstall -Plan:$plan)
        }

        # ----- upgrade: no questions ------------------------------------------------------------------------------------------
        if ($Upgrade) {
            $old = Read-HccState
            $A = @{ mode = [string](Get-HccProp $old 'mode' 'Production'); hotelName = [string](Get-HccProp $old 'hotelName' ''); serverName = [string](Get-HccProp $old 'serverName' ''); httpsPort = [int](Get-HccProp $old 'httpsPort' 5443) }
            if (-not $A.serverName) {
                $A.serverName = $env:COMPUTERNAME.ToLowerInvariant()
                if (Test-Path -LiteralPath $script:SettingsFile) {
                    $cfg = (Read-HccTextFile $script:SettingsFile) | ConvertFrom-Json
                    $A.httpsPort = [int](Get-HccProp (Get-HccProp (Get-HccProp $cfg 'Hcc') 'Hosting') 'HttpsPort' $A.httpsPort)
                }
            }
            $steps = Get-HccUpgradeSteps -A $A
            if ($plan) { Show-HccPlan $steps 'HCC server upgrade:'; return 0 }
            if (-not (Test-HccElevated)) { Write-Host 'Run this from an administrator window.'; return 1 }
            Write-InstallLog "HCC server upgrade started (PowerShell $($PSVersionTable.PSVersion))."
            $state = if ($old) { $old } else { [pscustomobject]@{ mode = $A.mode; hotelName = $A.hotelName; serverName = $A.serverName; httpsPort = $A.httpsPort; completed = @(); status = 'Done' } }
            # the upgrade keeps its own progress; the original install record stays as it was
            $up = [pscustomobject]@{ completed = @(); currentStep = ''; failedStep = ''; status = 'InProgress' }
            $backup = $script:StateFile
            $script:StateFile = Join-Path $script:SetupDir 'upgrade-state.json'
            try { $code = Invoke-HccSteps -Steps $steps -State $up -Answers $A } finally { $script:StateFile = $backup }
            if ($code -eq 0) {
                if ($old) { $old | Add-Member -NotePropertyName lastUpgradeUtc -NotePropertyValue ((Get-Date).ToUniversalTime().ToString('o')) -Force; $old | Add-Member -NotePropertyName version -NotePropertyValue $version -Force; Save-HccState $old }
                Write-HccSummary $state 'Done'
                Write-HccProgress -Index @($steps).Count -Count @($steps).Count -Title 'Done' -Status 'done'
                Write-InstallLog 'HCC server upgrade finished.'
            }
            else { Write-InstallLog "The upgrade did not finish. The log is $script:LogFile. Run the setup again to continue." 'ERROR' }
            return $code
        }

        # ----- install ------------------------------------------------------------------------------------------------------
        if (-not $AnswerFile) { Write-Host 'Give -AnswerFile <path to answers.json>.'; return 1 }
        if (-not (Test-Path -LiteralPath $AnswerFile)) { Write-Host "The answer file $AnswerFile was not found."; return 1 }
        if (-not $plan) { Assert-HccAnswerFolderPrivate -Path (Split-Path -Parent $AnswerFile) }
        $A = ConvertTo-HccAnswers -Json ((Read-HccTextFile $AnswerFile) | ConvertFrom-Json)
        Add-HccSecret $A.ownerPassword
        Add-HccSecret $A.caPfxPassword
        $problems = @(Test-HccAnswers -A $A)
        if ($problems.Count -gt 0) {
            foreach ($m in $problems) { Write-InstallLog $m 'ERROR' }
            Write-InstallLog 'The answers are not usable, so nothing was installed.' 'ERROR'
            return 1
        }
        $steps = Get-HccInstallSteps -A $A
        if ($plan) { Show-HccPlan $steps ("HCC server install ({0} mode) for '{1}' on {2}:" -f $A.mode, $A.hotelName, $A.serverName); return 0 }
        if (-not (Test-HccElevated)) { Write-Host 'Run this from an administrator window.'; return 1 }

        $old = Read-HccState
        $completed = @()
        if ($Resume -and $old) {
            $oldMode = [string](Get-HccProp $old 'mode' '')
            if ($oldMode -and $oldMode -ne $A.mode) { Write-InstallLog "The earlier attempt was a $oldMode install; it cannot continue as $($A.mode). Run the setup again with the same kind." 'ERROR'; return 1 }
            $oldName = [string](Get-HccProp $old 'serverName' '')
            if ($oldName -and ($oldName -ne $A.serverName -or [int](Get-HccProp $old 'httpsPort' 0) -ne $A.httpsPort)) { Write-InstallLog 'The PC name or port differs from the earlier attempt; use the same values to continue.' 'ERROR'; return 1 }
            $completed = @(Get-HccProp $old 'completed' @())
        }
        elseif ($old -and [string](Get-HccProp $old 'status' '') -eq 'Done' -and [string](Get-HccProp $old 'mode' '') -ne $A.mode) {
            Write-InstallLog "This PC already has a finished $(Get-HccProp $old 'mode' '') install. A trial cannot be turned into a production install by this setup (the keys session does that)." 'ERROR'; return 1
        }
        $state = [pscustomobject]@{
            schema = 1; version = $version; mode = $A.mode; hotelName = $A.hotelName; serverName = $A.serverName; serverIps = @($A.serverIps); httpsPort = $A.httpsPort
            serverUrl = ('https://{0}:{1}/' -f $A.serverName, $A.httpsPort); backupDirectory = $A.backupDirectory; ownerUserName = $A.ownerUserName; sql = $A.sql
            startedUtc = (Get-Date).ToUniversalTime().ToString('o'); completed = @($completed); currentStep = ''; failedStep = ''; status = 'InProgress'
        }
        Save-HccState $state
        Remove-Item -LiteralPath (Join-Path $script:SetupDir 'warnings.txt') -Force -ErrorAction SilentlyContinue
        Write-HccSummary $state 'InProgress'
        Write-InstallLog "HCC server install started: $($A.mode) mode, server $($A.serverName):$($A.httpsPort), PowerShell $($PSVersionTable.PSVersion)$(if ($Resume) { ', resuming' })."
        $code = Invoke-HccSteps -Steps $steps -State $state -Answers $A
        if ($code -eq 0) {
            $state.status = 'Done'; $state.currentStep = ''
            $state | Add-Member -NotePropertyName finishedUtc -NotePropertyValue ((Get-Date).ToUniversalTime().ToString('o')) -Force
            Save-HccState $state
            Write-HccSummary $state 'Done'
            Write-HccProgress -Index @($steps).Count -Count @($steps).Count -Title 'Done' -Status 'done'
            Write-InstallLog "HCC is installed. Open https://$($A.serverName):$($A.httpsPort)/ and sign in as '$($A.ownerUserName)'."
            if ($A.mode -eq 'Trial') { Write-InstallLog 'TRIAL install - not for real guest data.' 'WARN' }
        }
        else {
            Write-HccSummary $state ([string]$state.status)
            Write-InstallLog "The install did not finish. The log is $script:LogFile. $(if ($code -eq 2) { 'Restart Windows, then run the setup again.' } else { 'Run the setup again to continue at the failed step.' })" 'ERROR'
        }
        return $code
    }
    catch {
        Write-InstallLog ('The installation stopped: ' + $_.Exception.Message) 'ERROR'
        return 1
    }
    finally {
        if ($AnswerFile -and -not $plan) { Remove-HccAnswerFile $AnswerFile }   # on success and on failure
        $script:Secrets.Clear()
    }
}

if ($MyInvocation.InvocationName -eq '.') { return }   # dot-sourced by the tests: functions only

function Set-HccProgressStatus {
    # Guarantees the installer sees a final status whatever way the run ended.
    param([string]$Status)
    $lines = @()
    if (Test-Path -LiteralPath $script:ProgressFile) { $lines = @(Get-Content -LiteralPath $script:ProgressFile | Where-Object { $_ -notlike 'status=*' }) }
    if (-not ($lines | Where-Object { $_ -like 'pid=*' })) { $lines = @("pid=$PID") + $lines }
    Write-HccTextFile $script:ProgressFile ((@($lines) + "status=$Status") -join "`r`n")
}

if (-not $OperationsDirectory) { $OperationsDirectory = $PSScriptRoot }   # not in the param default: empty there under powershell -File (5.1)
$exitCode = Start-HccInstall -AnswerFile $AnswerFile -Resume:$Resume -PlanOnly:$PlanOnly -Upgrade:$Upgrade -Uninstall:$Uninstall -AppDirectory $AppDirectory -DataDirectory $DataDirectory -OperationsDirectory $OperationsDirectory -WhatIfMode:([bool]$WhatIfPreference)
if (-not $PlanOnly -and -not $WhatIfPreference -and -not $Uninstall -and (Test-Path -LiteralPath $script:SetupDir)) {
    try { Set-HccProgressStatus $(if ($exitCode -eq 0) { 'done' } elseif ($exitCode -eq 2) { 'restart' } else { 'failed' }) } catch { }
}
exit $exitCode
