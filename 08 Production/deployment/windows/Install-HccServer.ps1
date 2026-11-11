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
# only SYSTEM and Administrators, inheritance off) - never created first and locked afterwards. A pre-existing folder gets its DACL
# REPLACED (not merged) and its owner set to Administrators; a link is refused.
function Get-HccPrivateSecurity {
    $admins = New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')
    $system = New-Object Security.Principal.SecurityIdentifier('S-1-5-18')
    $sec = New-Object Security.AccessControl.DirectorySecurity
    $sec.SetAccessRuleProtection($true, $false)
    foreach ($sid in @($system, $admins)) {
        $sec.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    }
    return $sec
}

function New-HccPrivateDirectory {
    param([string]$Path)
    $sec = Get-HccPrivateSecurity
    $sec.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    if ($PSVersionTable.PSVersion.Major -ge 6) { [void][System.IO.FileSystemAclExtensions]::Create((New-Object System.IO.DirectoryInfo($Path)), $sec) }
    else { [void][System.IO.Directory]::CreateDirectory($Path, $sec) }
}

function Repair-HccPrivateDirectory {
    param([string]$Path)
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Path is a link" }
    & icacls.exe $Path /setowner '*S-1-5-32-544' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not make Administrators the owner of $Path." }
    Set-Acl -LiteralPath $Path -AclObject (Get-HccPrivateSecurity)
}

function Get-HccOwnerSid {
    param([string]$Path)
    return (Get-Acl -LiteralPath $Path).GetOwner([Security.Principal.SecurityIdentifier]).Value
}

# Problems that make a data folder unsafe for files written/executed with administrator rights: a link, an owner other than SYSTEM /
# Administrators, or any account other than SYSTEM / Administrators in its permissions (or inheritance still on).
function Get-HccFolderProblems {
    param([string]$Path)
    $problems = @()
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $problems += "$Path is a link" }
    $owner = Get-HccOwnerSid -Path $Path
    if (@('S-1-5-18', 'S-1-5-32-544') -notcontains $owner) { $problems += ('{0} is owned by {1}' -f $Path, (Get-HccAccountName $owner)) }
    $problems += @(Test-HccProtectedDirectory -Path $Path)
    return @($problems)
}

# Same for one file (the answer file): not a link, owner SYSTEM / Administrators, no other account allowed.
function Get-HccFileProblems {
    param([string]$Path)
    $problems = @()
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $problems += "$Path is a link" }
    $owner = Get-HccOwnerSid -Path $Path
    if (@('S-1-5-18', 'S-1-5-32-544') -notcontains $owner) { $problems += ('{0} is owned by {1}' -f $Path, (Get-HccAccountName $owner)) }
    $acl = Get-Acl -LiteralPath $Path
    foreach ($r in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($r.AccessControlType -ne 'Allow') { continue }
        if (($r.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -ne 0) { continue }
        if (@('S-1-5-18', 'S-1-5-32-544') -notcontains $r.IdentityReference.Value) { $problems += ('{0} has access to {1}' -f (Get-HccAccountName $r.IdentityReference.Value), $Path) }
    }
    return @($problems)
}

# First action of every install / upgrade run: C:\ProgramData\HCC, Setup and Logs exist, are not links, are owned by Administrators and
# can be used only by SYSTEM and Administrators (created with that security; a pre-existing folder has its DACL replaced, or the run is
# refused with a sentence that says what to delete). Only then are log / warnings / answer paths used.
function Initialize-HccDataFolders {
    foreach ($f in @($script:DataDir, $script:SetupDir, $script:LogsDir)) {
        if (-not (Test-Path -LiteralPath $f)) { Protect-HccFolder -Path $f; continue }
        $problems = @(Get-HccFolderProblems -Path $f)
        if ($problems.Count -eq 0) { continue }
        if (@($problems | Where-Object { $_ -like '* is a link' }).Count -gt 0) { throw ("Refusing to use ${f}: it is a link. Delete the link $f and run the setup again.") }
        try { Repair-HccPrivateDirectory -Path $f } catch { throw ("The folder $f is not safe and could not be repaired ($($_.Exception.Message)). Delete the folder $f (it holds only HCC setup files) and run the setup again.") }
        $problems = @(Get-HccFolderProblems -Path $f)
        if ($problems.Count -gt 0) { throw ("The folder $f is not safe and could not be repaired: " + ($problems -join '; ') + ". Delete the folder $f and run the setup again.") }
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

# The folder AND the file are checked before the answers (they hold passwords) are read.
function Assert-HccAnswerFolderPrivate {
    param([string]$Path, [string]$FilePath)
    $problems = @(Get-HccFolderProblems -Path $Path)
    if ($FilePath -and (Test-Path -LiteralPath $FilePath)) { $problems += @(Get-HccFileProblems -Path $FilePath) }
    if ($problems.Count -gt 0) { throw ("The folder or file that holds the answers is not private, so the answers (they contain passwords) are not read: " + ($problems -join '; ')) }
}

function Get-HccLocalIpv4 {
    try { return @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop | ForEach-Object { [string]$_.IPAddress }) } catch { return @() }
}

# 'a.b.c.d' written as four plain decimal numbers 0-255 (no short forms, no leading zeros, no scope id); returns the text or $null.
function ConvertTo-HccIpv4 {
    param([string]$Text)
    if ($null -eq $Text -or $Text -notmatch '^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$') { return $null }
    $parts = @($Matches[1], $Matches[2], $Matches[3], $Matches[4])
    foreach ($p in $parts) { if ($p.Length -gt 1 -and $p.StartsWith('0')) { return $null }; if ([int]$p -gt 255) { return $null } }
    return ($parts -join '.')
}

function Test-HccPrivateIpv4 {
    param([string]$Ip)
    $o = @($Ip.Split('.') | ForEach-Object { [int]$_ })
    return ($o[0] -eq 10) -or ($o[0] -eq 172 -and $o[1] -ge 16 -and $o[1] -le 31) -or ($o[0] -eq 192 -and $o[1] -eq 168)
}

# Same rule as the client kit (Test-HccHotelName in Hcc-ClientTrust.ps1): 1-100 characters, no control / format / line-separator characters.
function Test-HccHotelNameOk {
    param([string]$Name)
    if ($null -eq $Name) { return $false }
    $n = $Name.Trim()
    return ($n.Length -ge 1 -and $n.Length -le 100 -and $n -notmatch '[\p{Cc}\p{Cf}\p{Zl}\p{Zp}]')
}

# Where the backups may go: not a drive top, not under the HCC folders, Windows, Program Files, ProgramData or any user profile.
function Get-HccBackupLocationProblem {
    param([string]$Path)
    if ($Path -notmatch '^[A-Za-z]:\\.+') { return 'The backup folder must be a full folder path on a drive, for example D:\HccBackups.' }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ($full -match '^[A-Za-z]:$') { return 'The backup folder must not be the top of a drive: choose a folder, for example D:\HccBackups.' }
    $blocked = @($script:AppDir, $env:SystemRoot, $env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:ProgramData) | Where-Object { $_ }
    foreach ($b in $blocked) {
        $bb = [IO.Path]::GetFullPath($b).TrimEnd('\')
        if ($full -ieq $bb -or $full.StartsWith($bb + '\', [StringComparison]::OrdinalIgnoreCase)) { return "The backup folder must not be inside $bb." }
    }
    if ($full -match '^[A-Za-z]:\\Users(\\|$)') { return 'The backup folder must not be inside a user profile (C:\Users): the setup makes it private to SQL Server and administrators.' }
    return ''
}

# A new or empty folder, or one that already holds only HCC backups (or already has the HCC backup permissions). Anything else is refused,
# because the setup removes inherited permissions from the backup folder and everything in it.
function Get-HccBackupContentsProblem {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return "$Path is a file, not a folder." }
    $items = @(Get-ChildItem -LiteralPath $Path -Force)
    if ($items.Count -eq 0) { return '' }
    $foreign = @($items | Where-Object {
            if ($_.PSIsContainer) { @('Logs', 'DrillScratch') -notcontains $_.Name }
            else { -not (ConvertFrom-HccBackupFileName $_.Name) }
        })
    if ($foreign.Count -eq 0) { return '' }
    $acl = @(Test-HccProtectedDirectory -Path $Path -AllowedAccounts @((Get-HccSqlServiceAccount), 'NT AUTHORITY\SYSTEM'))
    if ($acl.Count -eq 0) { return '' }
    return "The backup folder $Path already holds other files ($($foreign[0].Name) ...). Choose a new or empty folder: the setup makes the backup folder private and that would lock other users out of their files."
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

function Format-HccCaFingerprint {
    param([string]$Hex)
    if (-not $Hex) { return '' }
    return (($Hex -split '(?<=\G.{2})(?=.)') -join ':')
}

function Write-HccSummary {
    param($State, [string]$Status, [string]$Note = '')
    $backup = [string](Get-HccProp $State 'backupDirectory' '')
    if (-not $backup -and $script:SettingsFile -and (Test-Path -LiteralPath $script:SettingsFile)) {
        try { $backup = [string](Get-HccProp (Get-HccProp (Get-HccProp ((Read-HccTextFile $script:SettingsFile) | ConvertFrom-Json) 'Hcc') 'Sql') 'BackupDirectory' '') } catch { $backup = '' }
    }
    $lines = @(
        'status=' + $Status,
        'mode=' + [string](Get-HccProp $State 'mode' ''),
        'version=' + [string](Get-HccProp $State 'version' ''),
        'serverName=' + [string](Get-HccProp $State 'serverName' ''),
        'httpsPort=' + [string](Get-HccProp $State 'httpsPort' ''),
        'serverUrl=' + [string](Get-HccProp $State 'serverUrl' ''),
        'backupDirectory=' + $backup,
        'caSha256=' + (Get-HccCaFingerprint),
        'logFile=' + [string]$script:LogFile,
        'note=' + (($Note -replace '[\r\n]+', ' ').Trim()))
    Write-HccTextFile $script:SummaryFile (($lines -join "`r`n") + "`r`n")
}

# ======================================================================================================================
# Answers
# ======================================================================================================================
function ConvertTo-HccAnswers {
    param($Json)
    $ips = @(Get-HccProp $Json 'serverIps' @() | ForEach-Object { $t = ([string]$_).Trim(); $n = ConvertTo-HccIpv4 $t; if ($n) { $n } else { $t } } | Where-Object { $_ })
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
    if (-not (Test-HccHotelNameOk $A.hotelName)) { $p.Add('The hotel name must be 1 to 100 characters, without control, invisible or line-break characters.') }
    if ($A.serverName -notmatch '^[a-z0-9]([a-z0-9.-]{0,61}[a-z0-9])?$') { $p.Add('This PC''s name may contain only letters, digits, dots and dashes.') }
    if ($A.serverIps.Count -lt 1) { $p.Add('Give at least one network address of this PC (for example 192.168.1.10).') }
    $local = $null
    foreach ($ip in $A.serverIps) {
        $n = ConvertTo-HccIpv4 $ip
        if (-not $n) { $p.Add("'$ip' must be written as four numbers separated by dots, for example 192.168.1.10."); continue }
        if (-not (Test-HccPrivateIpv4 $n)) { $p.Add("$n is not a private network address. Use an address of the hotel network (10.x.x.x, 172.16-31.x.x or 192.168.x.x)."); continue }
        if ($null -eq $local) { $local = @(Get-HccLocalIpv4) }
        if ($local -notcontains $n) { $p.Add("$n is not an address of this PC's network adapters. Use the address this PC has on the hotel network.") }
    }
    if ($A.httpsPort -lt 1024 -or $A.httpsPort -gt 65535) { $p.Add('The port must be between 1024 and 65535.') }
    elseif ($A.httpsPort -eq 5080) { $p.Add('Port 5080 is used by the setup listener of HCC; choose another port (the usual one is 5443).') }
    $loc = Get-HccBackupLocationProblem $A.backupDirectory
    if ($loc) { $p.Add($loc) }
    else { $con = Get-HccBackupContentsProblem $A.backupDirectory; if ($con) { $p.Add($con) } }
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
            elseif ((Get-HccDriveType $A.offlineCopyDirectory) -ne 'Removable') { $p.Add('The key copies must go to a removable USB stick (this drive is not one), so that they leave this PC.') }
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

# Adds what the sample has and the existing file lacks, recursively. An existing value is never changed. Keys that start with '_' (notes) are
# skipped. -Deny lists dotted key paths that are never taken from the sample (their sample value is a placeholder); a denied key that is
# missing is added to -Missing so the caller can warn.
function Merge-HccJsonMissing {
    param($Existing, $Sample, [string]$Prefix = '', [string[]]$Deny = @(), $Missing = $null)
    $added = 0
    foreach ($prop in $Sample.PSObject.Properties) {
        if ($prop.Name.StartsWith('_')) { continue }
        $path = if ($Prefix) { $Prefix + '.' + $prop.Name } else { $prop.Name }
        $have = $Existing.PSObject.Properties[$prop.Name]
        if (-not $have) {
            if ($Deny -contains $path) { if ($null -ne $Missing) { $Missing.Add($path) }; continue }
            $Existing | Add-Member -NotePropertyName $prop.Name -NotePropertyValue $prop.Value; $added++; continue
        }
        if ($prop.Value -is [pscustomobject] -and $have.Value -is [pscustomobject]) { $added += Merge-HccJsonMissing -Existing $have.Value -Sample $prop.Value -Prefix $path -Deny $Deny -Missing $Missing }
    }
    return $added
}

$script:HccNoSampleKeys = @('AllowedHosts', 'Hcc.Hosting.LoopbackHttpPort', 'Hcc.Hosting.CertificateThumbprint', 'Hcc.Hosting.ServerName', 'Hcc.Sql.BackupDirectory', 'Hcc.GuestIdKeys')

function Save-HccJsonInPlace {
    # Overwrites the file's content (its protective permissions stay) after a safety copy made in the private Setup folder.
    param([string]$Path, $Object, [string]$Tag)
    $safety = Join-Path $script:SetupDir ((Split-Path -Leaf $Path) + ".before-$Tag")
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
    Set-HccJsonValue $json 'Hcc.Install.Mode' $A.mode   # ignored by the server; lets an upgrade know this is a Trial install
    Write-HccTextFile $script:SettingsFile ($json | ConvertTo-Json -Depth 12)
    Write-InstallLog "Settings written to $script:SettingsFile (server name, port, backup folder)."

    if (-not (Test-Path -LiteralPath $script:OpsConfigFile)) {
        if (-not (Test-Path -LiteralPath $script:OpsConfigSample)) { throw "Neither hcc-ops.json nor its sample exists in $script:OpsDir." }
        Copy-Item -LiteralPath $script:OpsConfigSample -Destination $script:OpsConfigFile
    }
    $ops = (Read-HccTextFile $script:OpsConfigFile) | ConvertFrom-Json
    Set-HccJsonValue $ops 'BackupDirectory' $A.backupDirectory
    Set-HccJsonValue $ops 'LogDirectory' ([IO.Path]::Combine($A.backupDirectory, 'Logs'))
    Set-HccJsonValue $ops 'ScratchDirectory' ([IO.Path]::Combine($A.backupDirectory, 'DrillScratch'))
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
        $missing = New-Object System.Collections.Generic.List[string]
        $n = Merge-HccJsonMissing -Existing $existing -Sample $sample -Deny $script:HccNoSampleKeys -Missing $missing
        if ($n -gt 0) { Save-HccJsonInPlace -Path $script:SettingsFile -Object $existing -Tag 'upgrade' }
        Write-InstallLog "Settings: $n new setting(s) added to appsettings.Production.json; existing values were not changed."
        foreach ($m in $missing) { Write-InstallLog "Settings: '$m' is missing in appsettings.Production.json and was NOT filled from the sample (its sample value is only a placeholder). Check it." 'WARN' }
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

# The inline <script>/<style> blocks must still match the hashes in the page's own Content-Security-Policy meta (A10-05); the blocks are
# found the way WebAppHosting.cs finds them. Returns the problems (empty = the page will run).
function Test-HccPageCsp {
    param([string]$Html)
    $metas = [regex]::Matches($Html, '<meta\s+http-equiv="Content-Security-Policy"\s+content="([^"]*)"\s*/?>', 'IgnoreCase')
    if ($metas.Count -ne 1) { return @('the page has no single Content-Security-Policy meta') }
    $csp = $metas[0].Groups[1].Value
    $problems = @()
    foreach ($m in [regex]::Matches($Html, '<(script|style)(?<attrs>[^>]*)>(?<body>.*?)</\1>', 'IgnoreCase,Singleline')) {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $h = 'sha256-' + [Convert]::ToBase64String($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($m.Groups['body'].Value))) } finally { $sha.Dispose() }
        if (-not $csp.Contains("'$h'")) { $problems += ("the inline <{0}> does not match the hash in the page's Content-Security-Policy" -f $m.Groups[1].Value.ToLowerInvariant()) }
    }
    return @($problems)
}

function Set-HccTrialPage {
    # TRIAL - not for real guest data: a banner at the top of the sign-in page and "TRIAL - " in the browser-tab title. Only the first
    # <title> that lies before the first <script>, and a banner after the first <body> that lies before it, are touched; the inline
    # script and style stay byte for byte, so the page's Content-Security-Policy hashes stay valid. Anything else is refused (a failed
    # step is better than a blank page), and the result is checked against the page's CSP before it is written.
    param([string]$IndexHtml)
    if (-not (Test-Path -LiteralPath $IndexHtml)) { throw "The web page $IndexHtml is missing, so the TRIAL mark cannot be added. The installation is incomplete." }
    $html = Read-HccTextFile $IndexHtml
    if ($html.Contains($script:TrialMarker)) { return }
    $firstScript = [regex]::Match($html, '<script', 'IgnoreCase')
    $title = [regex]::Match($html, '<title>', 'IgnoreCase')
    $body = [regex]::Match($html, '<body[^>]*>', 'IgnoreCase')
    $limit = if ($firstScript.Success) { $firstScript.Index } else { $html.Length }
    if (-not $title.Success -or $title.Index -gt $limit) { throw "The web page $IndexHtml does not have the expected shape (no <title> before its first script), so the TRIAL mark is not added." }
    if (-not $body.Success -or $body.Index -gt $limit) { throw "The web page $IndexHtml does not have the expected shape (no <body> before its first script), so the TRIAL mark is not added." }
    if ($title.Index -gt $body.Index) { throw "The web page $IndexHtml does not have the expected shape (<title> after <body>), so the TRIAL mark is not added." }
    $banner = $script:TrialMarker + '<table width="100%" bgcolor="#b00020" cellpadding="6" cellspacing="0" border="0"><tr><td align="center"><font color="#ffffff" face="Arial" size="3"><b>TRIAL - not for real guest data</b></font></td></tr></table>'
    $new = $html.Insert($body.Index + $body.Length, $banner).Insert($title.Index + $title.Length, 'TRIAL - ')   # later position first
    $problems = @(Test-HccPageCsp $new)
    if ($problems.Count -gt 0) { throw ("The TRIAL mark would break the web page ($($problems[0])); it was not added.") }
    Write-HccTextFile $IndexHtml $new
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

# The protected sqlcmd folder (where Bootstrap-HccSql.ps1 finds or installs it).
function Get-HccProtectedSqlcmdFolder {
    foreach ($v in @('180', '170')) {
        $d = Join-Path $env:ProgramFiles "Microsoft SQL Server\Client SDK\ODBC\$v\Tools\Binn"
        if (Test-Path -LiteralPath (Join-Path $d 'sqlcmd.exe')) { return $d }
    }
    return ''
}

# Adds what the registry PATH has and this process lacks (a tool installed by an earlier step), keeps what the bootstrap put in front,
# and puts the protected sqlcmd folder first so no other sqlcmd.exe is used.
function Update-HccProcessPath {
    $current = @(([string]$env:Path) -split ';' | Where-Object { $_ })
    $registry = @(([Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')) -split ';' | Where-Object { $_ })
    $merged = @($current) + @($registry | Where-Object { $current -notcontains $_ })
    $sq = Get-HccProtectedSqlcmdFolder
    if ($sq) { $merged = @($sq) + @($merged | Where-Object { $_ -ne $sq }) }
    $env:Path = $merged -join ';'
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

function Assert-HccSettingsAcl {
    # Only SYSTEM, Administrators and the service may be able to use the settings file (it holds the guest ID key); same rule as the server's
    # start-up check. Never relies on a side effect of another folder's permissions.
    $problems = @(Get-HccSettingsAclProblems)
    if ($problems.Count -gt 0) { throw ('The settings file is readable by others: ' + ($problems -join '; ')) }
}

function Get-HccSettingsAclProblems {
    return @(Test-HccProtectedDirectory -Path $script:SettingsFile -AllowedAccounts @($script:ServiceAccountName))
}

function Step-ServiceRegister {
    param([hashtable]$A)
    Confirm-HccSettingsFile
    if (Test-HccServiceInstalled) { Write-InstallLog 'The service HccServer already exists; it is kept.' }
    else { Invoke-HccOps -Script 'Install-HccService.ps1' -Params @{ InstallDirectory = $script:ServerDir; HttpsPort = $A.httpsPort } | Out-Null }
    if (-not (Test-HccServiceInstalled)) { throw 'The HCC service was not registered, so SQL Server cannot be given its account.' }
    Assert-HccSettingsAcl
    Write-InstallLog 'The HCC service is registered (not started yet).'
}

function Step-Database {
    param([hashtable]$A)
    if (-not (Test-HccServiceInstalled)) { throw 'The HCC service must be registered before the database step (its Windows account must exist).' }
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
    Assert-HccSettingsAcl
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
    Assert-HccSettingsAcl   # the guest ID key is now in the file: migrate and the server refuse a file others can read
}

function Step-Migrate {
    param([hashtable]$A)
    $exe = Join-Path $script:ServerDir 'Hcc.Api.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "Hcc.Api.exe was not found in $script:ServerDir." }
    $r = Invoke-HccNative -FilePath $exe -Arguments 'migrate' -WorkingDirectory $script:ServerDir -Environment @{ ASPNETCORE_ENVIRONMENT = 'Production' }
    Write-InstallLog ([string]$r.Output)
    if ($r.ExitCode -ne 0) { throw "Hcc.Api.exe migrate failed with code $($r.ExitCode) (see the lines above)." }
}

function Step-ServiceStart {
    param([hashtable]$A)
    if (-not (Test-HccServiceInstalled)) { throw 'The HCC service is not registered.' }
    $thumb = Get-HccThumbprint
    try { Grant-HccCertificateKeyAccess -Thumbprint $thumb -Account $script:ServiceAccountName }
    catch { throw ("The HCC service cannot be given access to its certificate key, so it could not start: " + $_.Exception.Message) }
    Start-HccServerService
    Write-InstallLog 'The HCC service is running.'
}

function Restart-HccServerService {
    Stop-HccServerService
    Start-HccServerService
}

# The server says "already set up" only once: if the hotel exists but the setup listener is still on (an earlier run stopped half way),
# only close the listener and restart.
function Close-HccSetupListener {
    $json = (Read-HccTextFile $script:SettingsFile) | ConvertFrom-Json
    Set-HccJsonValue $json 'Hcc.Hosting.LoopbackHttpPort' 0
    Write-HccTextFile $script:SettingsFile ($json | ConvertTo-Json -Depth 12)
    Restart-HccServerService
}

function Step-Property {
    param([hashtable]$A)
    $port = Get-HccLoopbackPort
    if ($port -eq 0) { Write-InstallLog 'The hotel and the first Owner are already set up (the setup listener is off).'; return }
    $status = Wait-HccUrl -Url ("http://127.0.0.1:$port/api/v1/setup/status") -Seconds 120
    if ($status.Status -ne 200) { throw 'The HCC server did not answer on this PC in time, so the hotel could not be set up. Check the Windows Event Log (source HCC) and run the setup again.' }
    $required = $true
    try { $required = [bool](ConvertFrom-Json $status.Body).setupRequired } catch { $required = $true }
    if (-not $required) {
        Write-InstallLog 'The hotel and the first Owner already exist (an earlier run stopped before closing the setup listener); closing it now.'
        Close-HccSetupListener
        return
    }
    $params = @{
        HotelName = $A.hotelName; Address = '(enter the address in Master configuration after signing in)'; OwnerLogin = $A.ownerUserName
        OwnerDisplayName = $A.ownerUserName; OwnerPassword = (ConvertTo-HccSecureString $A.ownerPassword); SettingsFile = $script:SettingsFile
        ServerUrl = "http://127.0.0.1:$port"
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
    if ($A.mode -eq 'Trial' -and $page.Status -eq 200) {
        $csp = @(Test-HccPageCsp $page.Body)
        if ($csp.Count -gt 0) { throw ("The web page served by HCC would not run in a browser ($($csp[0])). The TRIAL mark must not touch the page's scripts.") }
    }
    Write-InstallLog "Health check passed: $base/ answers and the database is current."
    $fp = Get-HccCaFingerprint
    if ($fp) { Write-InstallLog ('Hotel CA SHA-256 fingerprint: ' + (Format-HccCaFingerprint $fp) + ' - compare this with what the client setup shows on each PC.') }
}

function Get-HccInstallSteps {
    param([hashtable]$A)
    $mode = [string]$A.mode
    return @(
        @{ Id = 'sql'; Title = 'Preparing SQL Server'; Run = { param($A) Step-Sql $A }; Plan = "Bootstrap-HccSql.ps1 -Mode $($A.sql) -MediaDir <app>\SqlSetupMedia (exit 2 = restart Windows)" }
        @{ Id = 'service-register'; Title = 'Registering the HCC service'; Run = { param($A) Step-ServiceRegister $A }; Plan = "Install-HccService.ps1 -HttpsPort $($A.httpsPort) (registers HccServer, NOT started: its account must exist before SQL Server and the key files are given to it)" }
        @{ Id = 'database'; Title = 'Creating the HCC databases'; Run = { param($A) Step-Database $A }; Plan = "Initialize-HccDatabase.ps1 -ServiceAccount 'NT SERVICE\HccServer' -BackupDirectory '$($A.backupDirectory)' -GrantDrillRights" }
        @{ Id = 'certificate'; Title = 'Creating the hotel certificate'; Run = { param($A) Step-Certificate $A }; Plan = $(if ($mode -eq 'Trial') { 'New-HccCertificate.ps1 -UpdateConfig (TRIAL: CA key kept in an ACL''d PFX in ProgramData\HCC\Keys, generated password beside it); trust the CA on this PC' } else { "New-HccCertificate.ps1 -UpdateConfig; CA key written to the USB folder '$($A.offlineCopyDirectory)' and removed from this PC; trust the CA on this PC" }) }
        @{ Id = 'settings'; Title = 'Writing the settings'; Run = { param($A) Step-Settings $A }; Plan = $(if ($mode -eq 'Trial') { 'Write appsettings.Production.json (also Hcc:Install:Mode) and hcc-ops.json; add the TRIAL mark to the sign-in page; check the settings file permissions' } else { 'Write appsettings.Production.json (also Hcc:Install:Mode) and hcc-ops.json; check the settings file permissions' }) }
        @{ Id = 'keys'; Title = 'Creating the guest ID key and the backup key'; Run = { param($A) Step-Keys $A }; Plan = $(if ($mode -eq 'Trial') { 'New-HccGuestIdKey.ps1, New-HccBackupKey.ps1 (TRIAL: copies stay in ProgramData\HCC\TrialKeyCopies); check the settings file permissions' } else { "New-HccGuestIdKey.ps1, New-HccBackupKey.ps1 (key copies to '$($A.offlineCopyDirectory)'); check the settings file permissions" }) }
        @{ Id = 'migrate'; Title = 'Building the database tables'; Run = { param($A) Step-Migrate $A }; Plan = 'Hcc.Api.exe migrate (ASPNETCORE_ENVIRONMENT=Production, as the administrator)' }
        @{ Id = 'service-start'; Title = 'Starting the HCC service'; Run = { param($A) Step-ServiceStart $A }; Plan = 'Give the service access to the certificate key (the step fails if that fails); start HccServer' }
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
        @{ Id = 'settings'; Title = 'Merging new settings'; Run = { param($A) Update-HccSettingsMerge | Out-Null; Set-HccInstallModeSetting $A.mode; if ($A.mode -eq 'Trial') { Set-HccTrialPage -IndexHtml (Join-Path $script:ServerDir 'web\index.html') }; Assert-HccSettingsAcl }; Plan = 'Add new settings to appsettings.Production.json and hcc-ops.json (existing values are never changed; placeholders for AllowedHosts, thumbprint, port 5080 etc. are never taken from the sample)' }
        @{ Id = 'migrate'; Title = 'Updating the database tables'; Run = { param($A) Stop-HccServerService; Step-Migrate $A }; Plan = 'Hcc.Api.exe migrate (the server is stopped first)' }
        @{ Id = 'service-start'; Title = 'Starting the HCC service'; Run = { param($A) Start-HccServerService }; Plan = 'Start HccServer' }
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

function Remove-HccEgressObjects {
    Get-NetFirewallRule -DisplayName 'HCC Egress Block' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    Unregister-ScheduledTask -TaskName 'ToggleEgress' -TaskPath '\HCC\' -Confirm:$false -ErrorAction SilentlyContinue
}

function Get-HccConfiguredHttpsPort {
    param([int]$Default = 5443)
    $st = Read-HccState
    $port = [int](Get-HccProp $st 'httpsPort' 0)
    if ($port -gt 0) { return $port }
    if (Test-Path -LiteralPath $script:SettingsFile) {
        try { return [int](Get-HccProp (Get-HccProp (Get-HccProp ((Read-HccTextFile $script:SettingsFile) | ConvertFrom-Json) 'Hcc') 'Hosting') 'HttpsPort' $Default) } catch { }
    }
    return $Default
}

function Invoke-HccUninstall {
    param([switch]$Plan)
    $actions = @(
        'Stop and remove the service HccServer and its firewall rule (Install-HccService.ps1 -Uninstall)',
        'Remove the scheduled tasks \HCC\ (Register-HccBackupTasks.ps1 -Unregister, ToggleEgress)',
        'Remove the firewall rule "HCC Egress Block"',
        'KEPT: SQL Server, the databases Hcc and HccDocuments, the backups, the keys, the certificates, the settings files (appsettings.Production.json, hcc-ops.json)')
    if ($Plan) { foreach ($a in $actions) { Write-Host "  $a" }; Write-Host 'PLAN ONLY: nothing was changed.'; return 0 }
    $setupSafe = (Test-Path -LiteralPath $script:SetupDir) -and (@(Get-HccFolderProblems -Path $script:SetupDir).Count -eq 0)
    if ($setupSafe) { Remove-HccAnswerFile (Join-Path $script:SetupDir 'answers.json') }
    $port = Get-HccConfiguredHttpsPort
    $failed = New-Object System.Collections.Generic.List[string]
    try { Invoke-HccOps -Script 'Install-HccService.ps1' -Params @{ Uninstall = $true; HttpsPort = $port } | Out-Null } catch { $failed.Add('the service and its firewall rule'); Write-InstallLog ('Service removal: ' + $_.Exception.Message) 'WARN' }
    try { Invoke-HccOps -Script 'Register-HccBackupTasks.ps1' -Params @{ Unregister = $true } | Out-Null } catch { $failed.Add('the backup tasks'); Write-InstallLog ('Backup tasks removal: ' + $_.Exception.Message) 'WARN' }
    try { Remove-HccEgressObjects }
    catch { $failed.Add('the internet-block rule and its task'); Write-InstallLog ('Egress removal: ' + $_.Exception.Message) 'WARN' }
    $st = if ($setupSafe) { Read-HccState } else { $null }
    if ($failed.Count -gt 0) {
        $note = 'NOT removed: ' + ($failed -join ', ') + '. The program files are removed anyway.'
        Write-InstallLog "$note Run Install-HccService.ps1 -Uninstall by hand if the service is still listed." 'WARN'
        if ($setupSafe) { Write-HccSummary $st 'UninstallPartial' $note }
        return 1
    }
    if ($setupSafe) { Write-HccSummary $st 'Uninstalled' }
    Write-InstallLog 'HCC service, firewall rules and scheduled tasks removed. SQL Server, the databases, the backups, the keys, the certificates and the settings files were not touched.'
    return 0
}

# Trial or Production: the setup record first, then Hcc:Install:Mode in the settings file, then the Trial files; '' = unknown (never guessed).
function Get-HccInstallMode {
    param($State)
    $m = [string](Get-HccProp $State 'mode' '')
    if ($m -in @('Trial', 'Production')) { return $m }
    if (Test-Path -LiteralPath $script:SettingsFile) {
        try {
            $m = [string](Get-HccProp (Get-HccProp (Get-HccProp ((Read-HccTextFile $script:SettingsFile) | ConvertFrom-Json) 'Hcc') 'Install') 'Mode' '')
            if ($m -in @('Trial', 'Production')) { return $m }
        }
        catch { }
    }
    if ((Test-Path -LiteralPath $script:TrialCopiesDir) -or (Test-Path -LiteralPath (Join-Path $script:KeysDir 'HCC-Hotel-CA-password.txt'))) { return 'Trial' }
    return ''
}

function Set-HccInstallModeSetting {
    param([string]$Mode)
    if (-not $Mode -or -not (Test-Path -LiteralPath $script:SettingsFile)) { return }
    $json = (Read-HccTextFile $script:SettingsFile) | ConvertFrom-Json
    if ([string](Get-HccProp (Get-HccProp (Get-HccProp $json 'Hcc') 'Install') 'Mode' '')) { return }
    Set-HccJsonValue $json 'Hcc.Install.Mode' $Mode
    Write-HccTextFile $script:SettingsFile ($json | ConvertTo-Json -Depth 12)
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
    try {
        if (-not $plan) {
            if (-not (Test-HccElevated)) { Write-Host 'Run this from an administrator window.'; return 1 }
            if ($Uninstall) {
                # the uninstall must not depend on the data folders being healthy: it logs only if the Logs folder is safe
                if ((Test-Path -LiteralPath $script:LogsDir) -and @(Get-HccFolderProblems -Path $script:LogsDir).Count -eq 0) { $script:LogFile = Join-Path $script:LogsDir ('install-{0}.log' -f (Get-Date -Format 'yyyyMMdd-HHmmss')) }
            }
            else {
                Initialize-HccDataFolders
                $script:LogFile = Join-Path $script:LogsDir ('install-{0}.log' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
                Update-HccProcessPath
                if ($Upgrade) { Remove-HccAnswerFile (Join-Path $script:SetupDir 'answers.json') }   # a stray answer file of a crashed run
            }
        }
        if ($Uninstall) { return (Invoke-HccUninstall -Plan:$plan) }

        # ----- upgrade: no questions ------------------------------------------------------------------------------------------
        if ($Upgrade) {
            $old = Read-HccState
            $mode = Get-HccInstallMode $old
            if (-not $mode) {
                if ($plan) { $mode = 'Production' }
                else {
                    $note = 'Cannot tell whether this is a Trial or a Production install (no setup record and no Hcc:Install:Mode in appsettings.Production.json). Nothing was changed.'
                    Write-InstallLog ($note + ' Add "Install": { "Mode": "Production" } (or "Trial") under "Hcc" in appsettings.Production.json and run the setup again.') 'ERROR'
                    Write-HccSummary $old 'UpgradeFailed' $note
                    return 1
                }
            }
            $A = @{ mode = $mode; hotelName = [string](Get-HccProp $old 'hotelName' ''); serverName = [string](Get-HccProp $old 'serverName' ''); httpsPort = [int](Get-HccProp $old 'httpsPort' 5443) }
            if (-not $A.serverName) {
                $A.serverName = $env:COMPUTERNAME.ToLowerInvariant()
                if (Test-Path -LiteralPath $script:SettingsFile) {
                    $cfg = (Read-HccTextFile $script:SettingsFile) | ConvertFrom-Json
                    $A.httpsPort = [int](Get-HccProp (Get-HccProp (Get-HccProp $cfg 'Hcc') 'Hosting') 'HttpsPort' $A.httpsPort)
                }
            }
            $steps = Get-HccUpgradeSteps -A $A
            if ($plan) { Show-HccPlan $steps 'HCC server upgrade:'; return 0 }
            Write-InstallLog "HCC server upgrade started (PowerShell $($PSVersionTable.PSVersion))."
            $state = if ($old) { $old } else { [pscustomobject]@{ mode = $A.mode; hotelName = $A.hotelName; serverName = $A.serverName; httpsPort = $A.httpsPort; serverUrl = ('https://{0}:{1}/' -f $A.serverName, $A.httpsPort); completed = @(); status = 'Done' } }
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
            else {
                Write-HccSummary $state 'UpgradeFailed' ("Failed step: " + [string]$up.failedStep)
                Write-InstallLog "The upgrade did not finish (step $($up.failedStep)). The log is $script:LogFile. Run the setup again to retry." 'ERROR'
            }
            return $code
        }

        # ----- install ------------------------------------------------------------------------------------------------------
        if (-not $AnswerFile) { Write-Host 'Give -AnswerFile <path to answers.json>.'; return 1 }
        if (-not (Test-Path -LiteralPath $AnswerFile)) { Write-Host "The answer file $AnswerFile was not found."; return 1 }
        if (-not $plan) { Assert-HccAnswerFolderPrivate -Path (Split-Path -Parent $AnswerFile) -FilePath $AnswerFile }
        # the parse error of Windows PowerShell 5.1 quotes the whole text (it holds the passwords): never log the exception here
        try { $answerJson = (Read-HccTextFile $AnswerFile) | ConvertFrom-Json }
        catch { Write-InstallLog 'The answer file is not valid JSON, so nothing was installed.' 'ERROR'; return 1 }
        $A = ConvertTo-HccAnswers -Json $answerJson
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

        $old = Read-HccState
        $completed = @()
        if ($Resume -and $old) {
            $oldMode = [string](Get-HccProp $old 'mode' '')
            if ($oldMode -and $oldMode -ne $A.mode) { Write-InstallLog "The earlier attempt was a $oldMode install; it cannot continue as $($A.mode). Run the setup again with the same kind." 'ERROR'; return 1 }
            $completed = @(Get-HccProp $old 'completed' @())
            if ($completed.Count -gt 0) {
                # the finished steps used the earlier values: refuse changed answers instead of silently keeping the old ones
                $changed = @()
                foreach ($f in @('serverName', 'httpsPort', 'hotelName', 'backupDirectory', 'ownerUserName', 'sql')) {
                    $ov = [string](Get-HccProp $old $f ''); if ($ov -and $ov -ne [string]$A[$f]) { $changed += $f }
                }
                $oldIps = (@(Get-HccProp $old 'serverIps' @()) -join ','); if ($oldIps -and $oldIps -ne ($A.serverIps -join ',')) { $changed += 'serverIps' }
                if ($changed.Count -gt 0) { Write-InstallLog ('These answers differ from the earlier attempt, whose finished steps used the old values: ' + ($changed -join ', ') + '. Type the same values to continue.') 'ERROR'; return 1 }
            }
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
