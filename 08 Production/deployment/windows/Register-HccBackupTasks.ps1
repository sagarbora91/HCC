<#
.SYNOPSIS
  Creates the Task Scheduler jobs for HCC backups (SQL Server Express has no SQL Agent): nightly FULL, DIFFERENTIAL every
  few hours, weekly restore drill. Run once from an elevated PowerShell. -WhatIf lists the tasks and changes nothing.
.DESCRIPTION
  Tasks (folder \HCC): "HCC Backup Full" 02:30 daily, "HCC Backup Differential" every 4 hours from 06:30, "HCC Restore Drill"
  Sunday 04:30. They run as -RunAsUser (default SYSTEM, which Initialize-HccDatabase.ps1 gives db_backupoperator, db_datareader and,
  with -GrantDrillRights, dbcreator; or a dedicated Windows account given to Initialize-HccDatabase.ps1 -BackupAccount).
  F-M06: a task that runs as SYSTEM must never run a script that a normal user can change. So the scripts are run ONLY from
  -ScriptDirectory (default C:\Program Files\HCC\Operations, where the installer puts them), and registration is REFUSED when that
  folder, or any .ps1/.json in it, can be changed by anyone other than SYSTEM, Administrators or TrustedInstaller. -InstallScripts
  copies the scripts (and hcc-ops.json) there from this folder first and sets that folder's permissions (owner Administrators,
  inheritance removed, SYSTEM/Administrators full, Users read).
  Re-running replaces the tasks. -Unregister removes them.
# ETP-REUSE: replace/align with ETP's Task Scheduler registration script when V:\ETP is available.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ScriptDirectory,
    [switch]$InstallScripts,
    [string]$RunAsUser = 'SYSTEM',
    [securestring]$RunAsPassword,
    [string]$FullTime = '02:30', [int]$DifferentialEveryHours = 4, [string]$DifferentialStart = '06:30',
    [string]$DrillDay = 'Sunday', [string]$DrillTime = '04:30',
    [switch]$Unregister
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Hcc-Common.ps1"
if (-not $ScriptDirectory) { $ScriptDirectory = Join-Path $env:ProgramFiles 'HCC\Operations' }   # never the folder the installer was run from
$ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$folder = '\HCC\'
$opsFiles = @('Hcc-Common.ps1', 'Backup-HccDatabase.ps1', 'Restore-HccDrill.ps1', 'New-HccBackupKey.ps1')
$defs = @(
    @{ Name = 'HCC Backup Full'; Script = 'Backup-HccDatabase.ps1'; Args = '-Type Full'; Trigger = { New-ScheduledTaskTrigger -Daily -At $FullTime } },
    @{ Name = 'HCC Backup Differential'; Script = 'Backup-HccDatabase.ps1'; Args = '-Type Differential'; Trigger = { New-ScheduledTaskTrigger -Once -At $DifferentialStart -RepetitionInterval (New-TimeSpan -Hours $DifferentialEveryHours) } },
    @{ Name = 'HCC Restore Drill'; Script = 'Restore-HccDrill.ps1'; Args = ''; Trigger = { New-ScheduledTaskTrigger -Weekly -DaysOfWeek $DrillDay -At $DrillTime } }
)

if ($Unregister) {
    foreach ($d in $defs) { if ($PSCmdlet.ShouldProcess("$folder$($d.Name)", 'Unregister scheduled task')) { Unregister-ScheduledTask -TaskName $d.Name -TaskPath $folder -Confirm:$false -ErrorAction SilentlyContinue } }
    return
}

$sameFolder = [IO.Path]::GetFullPath($ScriptDirectory).TrimEnd('\') -eq [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
if ($InstallScripts -and -not $sameFolder) {
    if ($PSCmdlet.ShouldProcess($ScriptDirectory, "Copy $($opsFiles -join ', ') (+ hcc-ops.json) and make the folder admin-only")) {
        if (-not (Test-Path -LiteralPath $ScriptDirectory)) { New-Item -ItemType Directory -Path $ScriptDirectory -Force | Out-Null }
        foreach ($f in $opsFiles + @('hcc-ops.json')) { $src = Join-Path $PSScriptRoot $f; if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination $ScriptDirectory -Force } }
        & icacls.exe $ScriptDirectory /setowner "*$script:HccSidAdministrators" /T /C | Out-Null
        Set-HccProtectedDirectory -Path $ScriptDirectory -ReadAccounts @('*S-1-5-32-545')
    }
    else { Write-Host "DRY RUN: copy $($opsFiles -join ', ') and hcc-ops.json to $ScriptDirectory; owner Administrators; inheritance removed; SYSTEM/Administrators full, Users read" }
}

$problems = @(Test-HccAdminOnlyWritable -Path $ScriptDirectory)
foreach ($d in $defs) { if (-not (Test-Path -LiteralPath (Join-Path $ScriptDirectory $d.Script))) { $problems += "Script not found: $(Join-Path $ScriptDirectory $d.Script)" } }
$problems = @($problems | Select-Object -Unique)

foreach ($d in $defs) {
    $scriptPath = Join-Path $ScriptDirectory $d.Script
    $arg = ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{0}" {1}' -f $scriptPath, $d.Args).Trim()
    if ($PSCmdlet.ShouldProcess("$folder$($d.Name)", "Register: powershell.exe $arg (as $RunAsUser)")) {
        if ($problems.Count -gt 0) { throw ("Refusing to register the backup tasks (they would run as $RunAsUser a script that others can change): " + ($problems -join '; ') + ". Use the default folder C:\Program Files\HCC\Operations or -InstallScripts.") }
        $action = New-ScheduledTaskAction -Execute $ps -Argument $arg -WorkingDirectory $ScriptDirectory
        $trigger = & $d.Trigger
        $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 3) -MultipleInstances IgnoreNew
        if ($RunAsUser -eq 'SYSTEM') { $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest }
        else { $principal = New-ScheduledTaskPrincipal -UserId $RunAsUser -LogonType Password -RunLevel Limited }
        $task = New-ScheduledTask -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description 'Hotel Control Centre (managed by Register-HccBackupTasks.ps1)'
        if ($RunAsUser -eq 'SYSTEM') { Register-ScheduledTask -TaskName $d.Name -TaskPath $folder -InputObject $task -Force | Out-Null }
        else {
            if (-not $RunAsPassword) { $RunAsPassword = Read-Host "Password for $RunAsUser" -AsSecureString }
            $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($RunAsPassword)
            try { Register-ScheduledTask -TaskName $d.Name -TaskPath $folder -InputObject $task -User $RunAsUser -Password ([Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)) -Force | Out-Null }
            finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
        }
        Write-Host "Registered $folder$($d.Name)"
    }
}
if ($WhatIfPreference) {
    if ($problems.Count -gt 0) { Write-Host ("DRY RUN: folder check: registration would be REFUSED: " + ($problems -join '; ')) }
    else { Write-Host "DRY RUN: folder check: $ScriptDirectory is writable only by SYSTEM, Administrators and TrustedInstaller" }
}
