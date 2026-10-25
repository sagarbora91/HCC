<#
.SYNOPSIS
  Creates the Task Scheduler jobs for HCC backups (SQL Server Express has no SQL Agent): nightly FULL, DIFFERENTIAL every
  few hours, weekly restore drill. Run once from an elevated PowerShell. -WhatIf lists the tasks and changes nothing.
.DESCRIPTION
  Tasks (folder \HCC): "HCC Backup Full" 02:30 daily, "HCC Backup Differential" every 4 hours from 06:30, "HCC Restore Drill"
  Sunday 04:30. They run as -RunAsUser (default SYSTEM: the machine account logs in to SQL Server through BUILTIN\Administrators
  on a typical single-PC install; use a dedicated account with db_backupoperator + dbcreator if you prefer). Re-running replaces
  the tasks. -Unregister removes them.
# ETP-REUSE: replace/align with ETP's Task Scheduler registration script when V:\ETP is available.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ScriptDirectory,
    [string]$RunAsUser = 'SYSTEM',
    [securestring]$RunAsPassword,
    [string]$FullTime = '02:30', [int]$DifferentialEveryHours = 4, [string]$DifferentialStart = '06:30',
    [string]$DrillDay = 'Sunday', [string]$DrillTime = '04:30',
    [switch]$Unregister
)
$ErrorActionPreference = 'Stop'
if (-not $ScriptDirectory) { $ScriptDirectory = $PSScriptRoot }   # $PSScriptRoot is empty in parameter defaults on Windows PowerShell 5.1
$ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$folder = '\HCC\'
$defs = @(
    @{ Name = 'HCC Backup Full'; Script = 'Backup-HccDatabase.ps1'; Args = '-Type Full'; Trigger = { New-ScheduledTaskTrigger -Daily -At $FullTime } },
    @{ Name = 'HCC Backup Differential'; Script = 'Backup-HccDatabase.ps1'; Args = '-Type Differential'; Trigger = { New-ScheduledTaskTrigger -Once -At $DifferentialStart -RepetitionInterval (New-TimeSpan -Hours $DifferentialEveryHours) } },
    @{ Name = 'HCC Restore Drill'; Script = 'Restore-HccDrill.ps1'; Args = ''; Trigger = { New-ScheduledTaskTrigger -Weekly -DaysOfWeek $DrillDay -At $DrillTime } }
)
foreach ($d in $defs) {
    $scriptPath = Join-Path $ScriptDirectory $d.Script
    $arg = ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{0}" {1}' -f $scriptPath, $d.Args).Trim()
    if ($Unregister) {
        if ($PSCmdlet.ShouldProcess("$folder$($d.Name)", 'Unregister scheduled task')) { Unregister-ScheduledTask -TaskName $d.Name -TaskPath $folder -Confirm:$false -ErrorAction SilentlyContinue }
        continue
    }
    if ($PSCmdlet.ShouldProcess("$folder$($d.Name)", "Register: powershell.exe $arg (as $RunAsUser)")) {
        if (-not (Test-Path -LiteralPath $scriptPath)) { throw "Script not found: $scriptPath" }
        $action = New-ScheduledTaskAction -Execute $ps -Argument $arg -WorkingDirectory $ScriptDirectory
        $trigger = & $d.Trigger
        $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 3) -MultipleInstances IgnoreNew
        if ($RunAsUser -eq 'SYSTEM') { $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest }
        else { $principal = New-ScheduledTaskPrincipal -UserId $RunAsUser -LogonType Password -RunLevel Highest }
        $task = New-ScheduledTask -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description 'Hotel Control Centre (managed by Register-HccBackupTasks.ps1)'
        if ($RunAsUser -eq 'SYSTEM') { Register-ScheduledTask -TaskName $d.Name -TaskPath $folder -InputObject $task -Force | Out-Null }
        else {
            if (-not $RunAsPassword) { $RunAsPassword = Read-Host "Password for $RunAsUser" -AsSecureString }
            $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($RunAsPassword))
            Register-ScheduledTask -TaskName $d.Name -TaskPath $folder -InputObject $task -User $RunAsUser -Password $plain -Force | Out-Null
        }
        Write-Host "Registered $folder$($d.Name)"
    }
}
