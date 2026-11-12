# HCC trial install (Hyper-V)

The HCC setups have never been run. Their first run is on a throw-away machine, never on the development PC or the hotel PC
(INST-CONTRACT rule 2).

**Owner's decision, 12 Nov 2026: the first trial runs on ETP's machine `ETP-Acceptance-186`, from a checkpoint.**
Nothing in this folder has been run: the agent has no administrator window, so the owner types the commands below.

## What this trial proves, and what it does not

- `ETP-Acceptance-186` already has SQL Server (`.\SQLEXPRESS`) and ETP. The HCC setup therefore takes its **"use the existing SQL Server"**
  path: it adopts the instance read-only and adds its own two databases, `Hcc` and `HccDocuments`. It must not touch ETP's databases.
- It proves: the wizard, the 13 steps, the service account, HTTPS and the certificate, the Trial banner, the client setup, uninstall.
- It does **not** prove: installing the bundled SQL Server on a PC that has none, and the restart-and-resume path. That needs a clean
  machine later: `New-HccTrialVm.ps1` in this folder creates one ("HCC-Trial"; also never run).

## Rules that protect ETP

1. Make sure no ETP test is using the machine. On 12 Nov 2026 a virtual machine was running on this PC and this machine's disk had changed
   that day.
2. Take the checkpoint `before-hcc-trial` **before** copying anything in. Never restore, rename or remove any other checkpoint.
3. After the trial, go back to `before-hcc-trial` and remove only that one checkpoint. The machine is then exactly as ETP left it.
4. Free space: `E:` had 132 GB free on 12 Nov 2026; a checkpoint plus the trial needs about 15 GB.

## Steps (owner, PowerShell as administrator, on this PC)

1. Look: `Get-VM ETP-Acceptance-186 | Format-List Name,State,Uptime` and `Get-VMSnapshot ETP-Acceptance-186 | Select-Object Name,CreationTime`.
2. Checkpoint: `Checkpoint-VM -Name ETP-Acceptance-186 -SnapshotName before-hcc-trial`
3. Start it if it is off and open its window: `Start-VM ETP-Acceptance-186; vmconnect.exe localhost ETP-Acceptance-186`
4. Copy the three files in (run once per file; change the file name):
   `Copy-VMFile ETP-Acceptance-186 -SourcePath "E:\Projects\HCC\Installers\HCC-Server-Setup-0.26.0-x64.exe" -DestinationPath "C:\HCC-Trial\" -CreateFullPath -FileSource Host`
   Files: `HCC-Server-Setup-0.26.0-x64.exe`, `HCC-Client-Setup-0.26.0.exe`, `SHA256SUMS.txt`.
   If it says the guest service is not enabled: `Enable-VMIntegrationService -VMName ETP-Acceptance-186 -Name "Guest Service Interface"`, then repeat.

## Steps inside the machine

5. Check the files: `Get-FileHash C:\HCC-Trial\*.exe` and compare with `SHA256SUMS.txt`.
6. Before installing, note what ETP uses, so a clash is seen and not guessed:
   `Get-NetTCPConnection -State Listen -LocalPort 5443,5080 -ErrorAction SilentlyContinue` (should print nothing: HCC needs both ports) and
   `sqlcmd -S .\SQLEXPRESS -E -C -Q "SELECT name FROM sys.databases"` (keep the list).
7. Run `HCC-Server-Setup-0.26.0-x64.exe`. Choose **Trial** and **use the existing SQL Server**. Windows will warn "unknown publisher":
   the setup is unsigned.
8. Go through the checklist in `05 Delivery Evidence/INST/inst-server-DONE.md` ("Only the trial machine can prove"), then the client
   setup with the kit from the Start menu ("client kit") and `inst-client-DONE.md` ("Trial-machine checklist", items 1-13), and
   `inst-sql-AUDIT.md` (S7).
9. ETP check: run the database list from step 6 again. Only `Hcc` and `HccDocuments` may be new; ETP's service must still be running.
10. Bring the evidence out before going back: the install logs (Start menu, "HCC install logs") and screenshots. From the host:
    copy them through the machine's window (enhanced session, copy and paste) into `E:\Projects\HCC\Builds\<date>\trial\`.

## Afterwards (owner, on this PC)

11. `Restore-VMSnapshot -VMName ETP-Acceptance-186 -Name before-hcc-trial -Confirm:$false`
12. `Remove-VMSnapshot -VMName ETP-Acceptance-186 -Name before-hcc-trial`
13. Tell the agent what happened at each step, or give it the log folder; it writes the trial report.
