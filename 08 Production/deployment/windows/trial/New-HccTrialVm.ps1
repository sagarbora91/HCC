#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Creates the throw-away Hyper-V machine "HCC-Trial" for the first trial run of the HCC setups (INST-CONTRACT rule 2).

.DESCRIPTION
    Run by the owner in an elevated PowerShell window. Creates a new, empty generation 2 machine with its own disk and attaches the
    Windows 11 evaluation ISO. It never touches an existing machine: if "HCC-Trial" or its folder already exists it stops.
    Windows itself is installed by hand in the machine's window; this script does not install HCC and runs no HCC setup.

    Nothing on the host changes except: one new Hyper-V machine and the folder given by -Root.

.EXAMPLE
    pwsh -File New-HccTrialVm.ps1
    pwsh -File New-HccTrialVm.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $Name = 'HCC-Trial',
    [string] $Root = 'E:\Hyper-V\HCC-Trial',
    [string] $Iso = 'E:\Hyper-V\ETP-Acceptance-186\Windows11-Enterprise-Evaluation.iso',
    [string] $Switch = 'Default Switch',
    [int] $MemoryGB = 8,
    [int] $Processors = 4,
    [int] $DiskGB = 100
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module Hyper-V

if (Get-VM -Name $Name -ErrorAction SilentlyContinue) { throw "A machine named '$Name' already exists. Nothing was changed." }
if (Test-Path -LiteralPath $Root) { throw "The folder '$Root' already exists. Nothing was changed." }
if (-not (Test-Path -LiteralPath $Iso -PathType Leaf)) { throw "Windows ISO not found: $Iso" }
if (-not (Get-VMSwitch -Name $Switch -ErrorAction SilentlyContinue)) { throw "Virtual switch not found: $Switch" }

$drive = Get-PSDrive -Name ([IO.Path]::GetPathRoot($Root).Substring(0, 1))
if ($drive.Free -lt 60GB) { throw "Less than 60 GB free on $($drive.Name): - choose another -Root." }

$vhd = Join-Path $Root "Disks\$Name.vhdx"

if (-not $PSCmdlet.ShouldProcess($Name, "Create Hyper-V machine in $Root")) { return }

New-Item -ItemType Directory -Path (Join-Path $Root 'Disks') -Force | Out-Null
$vm = New-VM -Name $Name -Generation 2 -MemoryStartupBytes ($MemoryGB * 1GB) -Path $Root -NewVHDPath $vhd -NewVHDSizeBytes ($DiskGB * 1GB) -SwitchName $Switch

Set-VM -VM $vm -ProcessorCount $Processors -AutomaticCheckpointsEnabled $false -CheckpointType Standard -AutomaticStartAction Nothing -AutomaticStopAction ShutDown
Set-VMMemory -VM $vm -DynamicMemoryEnabled $false

# Windows 11 needs a TPM and Secure Boot.
Set-VMKeyProtector -VM $vm -NewLocalKeyProtector
Enable-VMTPM -VM $vm
Set-VMFirmware -VM $vm -EnableSecureBoot On -SecureBootTemplate MicrosoftWindows

$dvd = Add-VMDvdDrive -VM $vm -Path $Iso -Passthru
Set-VMFirmware -VM $vm -FirstBootDevice $dvd

# Lets files be copied in from the host with Copy-VMFile once Windows is installed.
Enable-VMIntegrationService -VM $vm -Name 'Guest Service Interface'

Write-Host ''
Write-Host "Created '$Name' (not started)." -ForegroundColor Green
Write-Host "  Folder : $Root"
Write-Host "  Disk   : $vhd ($DiskGB GB, grows as used)"
Write-Host "  Memory : $MemoryGB GB, processors: $Processors, switch: $Switch"
Write-Host ''
Write-Host 'Next (see READ-ME.md beside this script):'
Write-Host "  1. Start-VM '$Name'; vmconnect.exe localhost '$Name'   (press a key at 'Press any key to boot from CD or DVD')"
Write-Host '  2. Install Windows with a local account.'
Write-Host "  3. Shut it down, then: Checkpoint-VM -Name '$Name' -SnapshotName 'clean-windows'"
