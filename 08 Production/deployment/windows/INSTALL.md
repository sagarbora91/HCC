# Installing the Hotel Control Centre server (owner's guide)

This puts the HCC server on the hotel's Windows PC. Staff then use HCC from a browser or the Android app over the hotel Wi-Fi/LAN.
Nothing here is sent outside the hotel. Do the steps in order; each one says how to check it worked.
**Status:** Wave 1 scripts. They have been dry-run tested only (`-WhatIf`), not yet run on the hotel PC. Do the first install on the real PC with a spare hour, and keep a note of anything that differs.

**Words used:** *PowerShell (Admin)* = Start menu, type PowerShell, right-click, "Run as administrator". *The scripts folder* = `C:\Program Files\HCC\Operations` (the installer puts them there; from the source tree they are in `08 Production\deployment\windows`).

## 0. Before you start
- A Windows 10/11 PC that stays on, with a fixed address on the hotel network (ask your router/IT person to give it a "reservation" so it always gets the same number, for example `192.168.1.20`). Write the PC name and that number down.
- A second drive or folder for backups, ideally **not** the same disk as the data (for example `D:\HccBackups`, or a USB/NAS disk).
- You are a Windows administrator on the PC.

## 1. Install SQL Server Express (the database)
1. Install **SQL Server 2025 Express** (free) with default settings, instance name `SQLEXPRESS`, Windows authentication. If ETP is already on this PC, the instance is already there: reuse it, HCC uses its own database called `Hcc` and never touches ETP's.
2. Install **SQL Server command-line utilities** (`sqlcmd`) if it is not there (the scripts use it).
3. Check: PowerShell: `sqlcmd -S .\SQLEXPRESS -E -C -Q "SELECT @@VERSION"` prints the version.

## 2. Windows account for HCC and the database
Simplest: let the service run as its built-in "virtual account" `NT SERVICE\HccServer` (no password to manage). Then, in PowerShell (Admin), in the scripts folder:
```
.\Initialize-HccDatabase.ps1 -ServiceAccount "NT SERVICE\HccServer" -BackupDirectory "D:\HccBackups" -GrantDrillRights
```
Add `-WhatIf` first to see what it will do. It creates the empty `Hcc` database, lets the HCC service use it (and only it), creates the backup folder and lets SQL Server write backups there.
(If you prefer a dedicated Windows user such as `HOTELPC\svc-hcc`, create that user first, give its name here and in step 3.)

## 3. Install the server files and the service
1. Run the installer `HCC-Server-Setup-<version>.exe` (or copy the published server files to `C:\Program Files\HCC\Server`). It creates `appsettings.Production.json` there from a sample and never overwrites it on upgrade.
2. Open `C:\Program Files\HCC\Server\appsettings.Production.json` in Notepad and check: `BackupDirectory` is your backup folder; `ServerName` is how you want the server to be called (for example `Hotel PC`). Leave the connection string as it is. **There are no passwords in this file, and there must never be.** (`TrustServerCertificate=True` is only for a developer's laptop; the server refuses to start in production if it is present.)
3. PowerShell (Admin), scripts folder:
```
.\Install-HccService.ps1            # add -WhatIf first to preview; add -ServiceAccount HOTELPC\svc-hcc for a dedicated user
```
It creates the Windows service "Hotel Control Centre server" (starts automatically, restarts itself after a crash) and opens port 5443 for the hotel network only. Do not start it yet.

## 4. Secure connection (HTTPS certificate)
Browsers and Android refuse plain HTTP logins on a network, so the server uses HTTPS with the hotel's own certificate authority.
```
.\New-HccCertificate.ps1 -ServerName "HOTELPC" -DnsName "hotelpc.local" -IpAddress "192.168.1.20" -ExportCaPfx -UpdateConfig "C:\Program Files\HCC\Server\appsettings.Production.json"
```
Use your real PC name and IP address. It asks for a password for the CA backup file: put the file `certs\HCC-Hotel-CA.pfx` and the password in the safe (needed once, in two years, to renew). It writes the certificate's thumbprint into the settings file.
You now have `certs\HCC-Hotel-CA.cer`: this is the file you install on every phone, tablet and PC (section "Phones and tablets" below). If the hotel already owns a proper certificate use `-ImportPfx file.pfx` instead.

## 5. Build the tables and start the server
PowerShell (Admin):
```
cd "C:\Program Files\HCC\Server"
$env:ASPNETCORE_ENVIRONMENT="Production"
.\Hcc.Api.exe migrate
Start-Service HccServer
```
`migrate` builds the tables (on an upgrade it first takes a safety backup through the backup script and refuses to continue if that fails).
**Check:** open a browser on the server PC: `http://127.0.0.1:5080/api/v1/system/health` should show `"status":"ok"`, the database name `Hcc` and a schema version. From another device on the Wi-Fi: `https://192.168.1.20:5443/api/v1/system/health` (works without a warning only after the CA certificate is installed, see below).
If it does not start: Event Viewer > Windows Logs > Application, source "HccServer" / ".NET Runtime": the message lists exactly which setting is wrong.

## 6. First Owner (once)
HCC needs a property record and the first Owner account. This is only possible **on the server PC itself** (the address `127.0.0.1`) and only while no user exists.
1. Property (name/time zone are yours; code is a short word). PowerShell:
```
sqlcmd -S .\SQLEXPRESS -E -C -d Hcc -Q "INSERT INTO inv.property(code,name,time_zone,business_date) VALUES ('MAIN', N'Your Hotel Name', 'Asia/Kolkata', CAST(SYSDATETIME() AS date)); SELECT property_id FROM inv.property;"
```
Note the number it prints (normally 1). Rooms, rates and other master data are loaded by the later setup steps of HCC (not part of this installer).
2. First Owner (replace the values; pick a long password, 12+ characters):
```
curl.exe -X POST http://127.0.0.1:5080/api/v1/auth/bootstrap-owner -H "Content-Type: application/json" -d "{\"propertyId\":1,\"loginName\":\"owner\",\"displayName\":\"Your Name\",\"password\":\"YOUR-LONG-PASSWORD\"}"
```
It answers with the new user. Now sign in from the app as `owner`. Then add staff in Admin > Users. (The command is recorded in the PowerShell history: close the window after, and change the password in the app if you like.)

## 7. Backups (do not skip)
PowerShell (Admin), scripts folder:
```
Copy-Item hcc-ops.sample.json hcc-ops.json      # then open hcc-ops.json and check the folder names
.\Backup-HccDatabase.ps1 -Type Full              # first full backup, now
.\Register-HccBackupTasks.ps1                    # schedules: full 02:30 daily, changes every 4 h, restore test every Sunday 04:30
```
(`-WhatIf` on either shows the plan without doing it.) Backups are kept 30 days (never fewer than the 3 newest full ones). **Check:** the `.bak` files appear in the backup folder; Task Scheduler > HCC folder lists three tasks.
**Run a restore drill now:** `.\Restore-HccDrill.ps1`. It restores the newest backup into a throw-away copy, checks it for damage, compares record counts and deletes the copy, then says `DRILL PASSED` (log in the backup folder under `Logs`). A backup that was never restored is not a backup: the Sunday task repeats this every week; look at `Logs\drill-history.jsonl` or the latest `restore-drill_*.log` once a month.
Also copy the backup folder to a USB disk or another PC weekly; a fire or theft takes the PC and the folder together.

## 8. Phones and tablets
See `ANDROID_SERVER_ADDRESS.md` (installing the certificate on Android and typing the server address in the app). For a browser on a Windows PC: double-click `HCC-Hotel-CA.cer` > Install Certificate > Local Machine > "Trusted Root Certification Authorities".

## 9. Final check list
- `https://<server>:5443/api/v1/system/health` shows ok, from a tablet, without a certificate warning.
- `https://<server>:5443/api/v1/system/discovery` shows the server name and version (this is also what the app uses to confirm it found HCC).
- A backup file exists and the restore drill passed.
- Restart the PC: the service comes back by itself (give it two minutes).

## Upgrading, uninstalling
Run the new installer: it stops the service, replaces the programs, keeps your settings, starts the service again. Start `Hcc.Api.exe migrate` once first when told (new tables); the safety backup is automatic. Uninstalling never deletes the database or the backups. To remove the service itself: `.\Install-HccService.ps1 -Uninstall`.
