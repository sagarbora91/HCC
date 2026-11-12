# HCC trial machine (Hyper-V)

The HCC setups have never been run. Their first run is on the throw-away machine **HCC-Trial**, never on the development PC or the hotel PC
(INST-CONTRACT rule 2). Owner's choice, 12 Nov 2026: a fresh Hyper-V machine with clean Windows.

Status of this folder: `New-HccTrialVm.ps1` was written without being run (it needs an administrator window, which the agent does not have).
Run it with `-WhatIf` first.

## Why not the ETP machines

This PC has two Hyper-V machines, both ETP's: `ETP-Acceptance-186` (26 checkpoints, in use on 12 Nov 2026) and `ETP-Stress`. They already
hold SQL Server and ETP, so they cannot show what the HCC setup does on a PC that has neither, and a mistake with a checkpoint would cost ETP
its test history. HCC-Trial only borrows their Windows 11 evaluation ISO (read-only).

## Steps (owner, in an administrator PowerShell window)

1. Create the machine (about a minute):
   `pwsh -File "E:\Projects\HCC\worktrees\inst\08 Production\deployment\windows\trial\New-HccTrialVm.ps1"`
2. Start it and open its window: `Start-VM HCC-Trial; vmconnect.exe localhost HCC-Trial`. Press a key when it says "Press any key to boot
   from CD or DVD".
3. Install Windows 11 (about 20 minutes). Use a local account. Name the PC `HCC-TRIAL`.
4. Shut the machine down and take the clean checkpoint: `Checkpoint-VM -Name HCC-Trial -SnapshotName clean-windows`.
   Every trial starts from this checkpoint: `Restore-VMSnapshot -VMName HCC-Trial -Name clean-windows -Confirm:$false`.
5. Start it again and copy the two setups in:
   `Copy-VMFile HCC-Trial -SourcePath "E:\Projects\HCC\Installers\HCC-Server-Setup-0.26.0-x64.exe" -DestinationPath "C:\HCC-Trial\" -CreateFullPath -FileSource Host`
   (same for `HCC-Client-Setup-0.26.0.exe` and `SHA256SUMS.txt`).
6. Inside the machine: check the SHA-256 of both files against `SHA256SUMS.txt`, then run the server setup in **Trial** mode and follow the
   checklists in `05 Delivery Evidence/INST/inst-server-DONE.md`, `inst-client-DONE.md` and `inst-sql-AUDIT.md` (S7).

## Limits

- The evaluation Windows runs for 90 days from its install.
- The machine uses 8 GB of memory while it runs. With `ETP-Acceptance-186` running too, check free memory first.
- Remove everything afterwards with `Remove-VM HCC-Trial -Force` and by deleting `E:\Hyper-V\HCC-Trial`.
