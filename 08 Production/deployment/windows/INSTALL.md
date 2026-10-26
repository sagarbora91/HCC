# Installing the Hotel Control Centre server (owner's guide)

This puts the HCC server on the hotel's Windows PC. Staff then open HCC in a browser at `https://<server>:5443/` (the server itself serves the web app, section 6c; no separate web server) or use the Android app, over the hotel Wi-Fi/LAN.
Nothing here is sent outside the hotel. Do the steps in order; each one says how to check it worked.
**Status:** Wave 1 scripts, hardened by the security review of 26 Oct 2026 (S3). They have been dry-run tested only (`-WhatIf`), not yet run on the hotel PC. Do the first install on the real PC with a spare hour, and keep a note of anything that differs.

**Words used:** *PowerShell (Admin)* = Start menu, type PowerShell, right-click, "Run as administrator". *The scripts folder* = `C:\Program Files\HCC\Operations` (the installer puts them there; only administrators can change files there, which the backup tasks rely on; from the source tree they are in `08 Production\deployment\windows`).

## 0. Before you start
- A Windows 10/11 PC that stays on, with a fixed address on the hotel network (ask your router/IT person to give it a "reservation" so it always gets the same number, for example `192.168.1.20`). Write the PC name and that number down.
- A second drive or folder for backups, ideally **not** the same disk as the data (for example `D:\HccBackups`, or a USB/NAS disk). Turn on **BitLocker** for that drive (Settings > Privacy & security > Device encryption / BitLocker; Windows Pro) so a stolen disk cannot be read.
- Two USB sticks for the safe: one for the **keys** (CA backup, guest ID key, backup key) and, separately, the weekly **backup copy** disk. Never keep the keys on the backup disk.
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
Add `-WhatIf` first to see what it will do. It creates the empty `Hcc` database and:
- lets the HCC service **read, write and run** in that database only. The service is **not** the database owner: it cannot change tables or switch off the audit trail. Building and upgrading the tables (`Hcc.Api.exe migrate`, step 5) is done by **you**, the administrator, with your own Windows login, only at install and upgrade.
- lets the backup tasks (they run as the Windows SYSTEM account) take backups and run the weekly restore test.
- creates the backup folder as a **private** folder: only SYSTEM, Administrators, SQL Server and the backup account can open it (no other Windows user of this PC can copy the backups).
(If you prefer a dedicated Windows user such as `HOTELPC\svc-hcc`, create that user first, give its name here and in step 3. A dedicated backup user instead of SYSTEM: `-BackupAccount HOTELPC\svc-backup`, then use the same name in section 7.)

## 3. Install the server files and the service
1. Run the installer `HCC-Server-Setup-<version>.exe` (or copy the published server files to `C:\Program Files\HCC\Server`). It creates `appsettings.Production.json` there from a sample and never overwrites it on upgrade.
2. Open `C:\Program Files\HCC\Server\appsettings.Production.json` in Notepad and check: `BackupDirectory` is your backup folder; `ServerName` is how you want the server to be called (for example `Hotel PC`). Leave the connection string as it is. `LoopbackHttpPort` is `5080` for now (needed for step 6 only; it is switched off automatically afterwards). **There are no passwords in this file, and there must never be.** (`TrustServerCertificate=True` is only for a developer's laptop; the server refuses to start in production if it is present.)
3. PowerShell (Admin), scripts folder:
```
.\Install-HccService.ps1            # add -WhatIf first to preview; add -ServiceAccount HOTELPC\svc-hcc for a dedicated user
```
It creates the Windows service "Hotel Control Centre server" (starts automatically, restarts itself after a crash), opens port 5443 for the hotel network only (Private/Domain networks), and makes `appsettings.Production.json` readable only by SYSTEM, Administrators and the HCC service. Do not start it yet.

## 4. Secure connection (HTTPS certificate)
Browsers and Android refuse plain HTTP logins on a network, so the server uses HTTPS with the hotel's own certificate authority (CA). Plug in the keys USB stick (here `E:`):
```
.\New-HccCertificate.ps1 -ServerName "HOTELPC" -DnsName "hotelpc.local" -IpAddress "192.168.1.20" -CaPfxPath "E:\HCC-Hotel-CA.pfx" -UpdateConfig "C:\Program Files\HCC\Server\appsettings.Production.json"
```
Use your real PC name and IP address (add `-WhatIf` first). What it does:
- The hotel CA can **only** vouch for this server's names and addresses (a "name constraint"): even if someone stole it, it could not fake a bank or Gmail in Chrome or Windows on your devices (install it only on the hotel's own devices). If the PC's address may change within your network, add `-PermittedNetwork 192.168.1.0/24`.
- It asks for a password for the CA backup file and writes the CA's private key **only** to `E:\HCC-Hotel-CA.pfx`; the key is then **removed from this PC**. Put the stick and the password in the safe: you need them once a year to renew (4b). If the export fails, the script undoes everything.
- The server certificate is valid for **397 days** and its key cannot be copied off the PC.
- It writes the certificate thumbprint and `AllowedHosts` (the names and addresses the server answers to) into the settings file.
You now have `certs\HCC-Hotel-CA.cer`: this is the file you install on every phone, tablet and PC (section "Phones and tablets" below). If the hotel already owns a proper certificate use `-ImportPfx file.pfx` instead.

### 4b. Renewing the server certificate (once a year)
Put a reminder in the calendar about 11 months after step 4 (the script prints the expiry date). With the keys stick plugged in, PowerShell (Admin), scripts folder:
```
.\New-HccCertificate.ps1 -RenewServer -CaPfx "E:\HCC-Hotel-CA.pfx" -ServerName "HOTELPC" -DnsName "hotelpc.local" -IpAddress "192.168.1.20" -UpdateConfig "C:\Program Files\HCC\Server\appsettings.Production.json"
Restart-Service HccServer
```
It loads the CA (its key cannot be copied while it is on the PC), issues a new server certificate, removes the CA from the PC again and updates the thumbprint. Phones, tablets and the APK need **nothing**: they trust the CA, not the server certificate. Afterwards delete the old server certificate in `certlm.msc` > Personal > Certificates (the one with the earlier expiry date).

## 5. Build the tables and start the server
PowerShell (Admin) - this is the "migration" step and must be run by you (the administrator), not by the service:
```
cd "C:\Program Files\HCC\Server"
$env:ASPNETCORE_ENVIRONMENT="Production"
.\Hcc.Api.exe migrate
Start-Service HccServer
```
`migrate` builds the tables (on an upgrade it first takes a safety backup through the backup script and refuses to continue if that fails).
**Check:** open a browser on the server PC: `http://127.0.0.1:5080/api/v1/system/health` should show `"status":"ok"`, the database name `Hcc` and a schema version (this plain address only works until step 6 is done). From another device on the Wi-Fi: `https://192.168.1.20:5443/api/v1/system/health` (works without a warning only after the CA certificate is installed, see below).
If it does not start: Event Viewer > Windows Logs > Application, source "HccServer" / ".NET Runtime": the message lists exactly which setting is wrong.

## 6. Set up the hotel and the first Owner (once)
HCC needs to know your hotel and needs its first Owner account. One script does both, in one step. It works **only on the server PC itself** and **only on a new, empty installation**: once the hotel exists, it refuses to run again (nothing is changed).
PowerShell (Admin), scripts folder (replace the values in quotes with yours):
```
.\Initialize-HccProperty.ps1 -HotelName "Your Hotel Name" -Address "Street, City, PIN" -Gstin "27ABCDE1234F1Z5" -OwnerLogin "owner" -OwnerDisplayName "Your Name"
```
- It asks for the Owner password twice (12 or more characters; it is not shown on screen and not saved in the PowerShell history).
- You may leave out `-Gstin` to finish the setup, but **a valid hotel GSTIN (with its correct check character) is required before the first tax invoice**: the server refuses to issue invoices without it. Set or correct it later in Master configuration > Property and branding > GSTIN on documents. A Maharashtra GSTIN starts with 27; the script refuses one from another state.
- Already filled in for you (add the option only to change it): state `-State "Maharashtra"`, time zone `-TimeZone "Asia/Kolkata"`, check-in `-CheckInTime "14:00"`, checkout `-CheckOutTime "11:00"`, business date `-BusinessDate` = today (write it as `2026-11-01` if you start on another day).
- Add `-WhatIf` first to see what it will send (the password is shown as `********`, nothing is sent).
- When it succeeds it **switches off the plain-HTTP setup address** (`LoopbackHttpPort` = 0 in the settings file) and restarts the service. From then on the server PC uses `https://localhost:5443/` like everyone else. (The setup address never accepted sign-ins; it served only setup, health and discovery.)

It answers "Hotel '...' is set up. Sign in as 'owner' ...". Now sign in from the app as `owner` with that password, then add staff in Admin > Users. The hotel name, address, GSTIN and check-in/checkout times are also in Master configuration, where the Owner can change them later. Rooms, rates, staff, opening balances and the other master data are loaded in step 6d.
**Check:** `https://localhost:5443/api/v1/system/health` in a browser on the server PC shows ok, and `appsettings.Production.json` shows `"LoopbackHttpPort": 0`.
(No SQL commands are needed any more. On an older installation where the hotel row was already typed in by hand but no Owner exists, the earlier `auth/bootstrap-owner` call still works from the server PC itself; it is refused through any proxy.)

## 6b. Guest ID key (once, before the first check-in)
HCC stores guest ID numbers (Aadhaar, passport, ...) encrypted. The key is made on this PC and kept only in the settings file (never in git, never in the database or its backups). **Without this step check-in with an ID number is refused** with "ID storage is not set up on the server" (names, phones and the last 4 digits still work, and the rest of the app keeps working).
Plug in the keys USB stick and, in PowerShell (Admin), scripts folder:
```
.\New-HccGuestIdKey.ps1 -OfflineCopyPath "E:\HCC-guest-id-key-2026-10.txt"        # add -WhatIf first to preview; E: is the USB stick
Restart-Service HccServer
```
- It makes a random key, wraps it with Windows DPAPI for this PC (`dpapi-machine:`), writes it into `appsettings.Production.json` (`Hcc:GuestIdKeys`) and limits that file to Administrators, SYSTEM and the HCC service. The key is never printed. It refuses to run again once a key exists (replacing it would make stored numbers unreadable); the yearly change is `-Rotate -KeyId 2027-10` (steps in `src\Hcc.Application\GuestIds\README.md`, "Key rotation").
- **What the server checks at every start (Production):** a plain (unencrypted) key or `AllowPlainKeys` is refused; a machine-wide key is accepted only because the script also writes `AllowMachineScope: true`, and only while the settings file stays readable by SYSTEM, Administrators and the service alone (if someone widens the file's permissions, the service refuses to start and the event log shows the `icacls` command that fixes it); `Required: true` means the service also refuses to start if the key is ever removed. With a dedicated service user you can instead make a key bound to that user: run the script as that user (`runas /user:HOTELPC\svc-hcc powershell`) with `-Scope User`.
- **The USB file is the offline copy: put it in the safe, never e-mail it, never leave it on this PC.** A DPAPI key only opens on this PC and this Windows installation. If the PC is replaced or Windows reinstalled, or the settings file is lost, every stored ID number is unreadable (last 4 digits stay) unless you have that copy. To restore, ask your developer to wrap the raw key from the file for the new PC (same wrapping as the script, `ConvertTo-HccWrappedGuestIdKey`). After the first successful check-in delete `appsettings.Production.json.before-guestid-key` (the script's safety copy).
- Optional: instead of the settings file you may put the same values in the service account's environment (`Hcc__GuestIdKeys__Current`, `Hcc__GuestIdKeys__Keys__2026-10`); use a `dpapi-user:` key there (the service's environment is not protected like the settings file).
**Check:** sign in, check a guest in with an ID number: it saves and shows `XXXX-XXXX-1234`. If the app says "ID storage is not set up on the server", this step was missed or the service was not restarted.

## 6c. Web app (browsers on the hotel network)
The server also serves the HCC web app itself, on the same address as the API, so staff just open `https://<server>:5443/` in a browser (no separate web server). The web app is one file, `index.html`, built from the source (`03 Prototype`): `npm run build:production` makes `03 Prototype\dist\production\index.html`.
1. Copy that file to **`C:\Program Files\HCC\Server\web\index.html`** on the server PC (create the `web` folder if it is not there; another place can be set with `Hcc:Hosting:WebRoot` in `appsettings.Production.json`).
2. No restart is needed, also not when you replace it later with a newer build: browsers fetch the new page the next time they open it (the page is never reused without asking the server first).
3. **Check:** `https://<server>:5443/` shows the HCC sign-in screen. If it says "The HCC web app is not installed on this server", the file is not in the `web` folder.
The page is sent with a strict content security policy (only its own script, only this server as a data source, no framing); every answer over HTTPS also tells the browser to use HTTPS only (HSTS), and API answers are never cached. The phones' APK has the web app built in and only needs the server address.

## 6d. Load your masters (rooms, rates, staff, opening balances, ...)
Before the first guest, HCC needs your rooms, room types and rates, the till float and your opening balances; staff, ledgers, guests, restaurant menu, departments, company clients, suppliers and stock items are loaded the same way. You fill CSV files once and one script loads them. Full column lists and rules: [`import-templates\README.md`](../import-templates/README.md) (installed in `C:\Program Files\HCC\ImportTemplates`).
1. **Fill the templates.** Copy the files you need from `C:\Program Files\HCC\ImportTemplates\templates` (source tree: `08 Production\deployment\import-templates\templates`) to a folder of your own, for example `D:\HccMasters`, keep the file names, and fill them in Excel (one item per row, first row unchanged; save as "CSV UTF-8"). The `samples` folder next to it shows filled-in fictional examples; never import the samples. No passwords, PINs or ID numbers in any file.
2. **Dry run.** PowerShell, scripts folder:
```
.\Import-HccMasters.ps1 -Folder "D:\HccMasters" -OpeningBalancesDate 2026-10-31 -DryRun
```
   It asks for the Owner sign-in name and password (not shown, not saved), uploads every file in the right order (room types before rooms, ledgers before opening balances, ...) and lets the server check every row; nothing is kept. `-OpeningBalancesDate` is the last day before HCC starts (the date of your trial balance); leave it out if you have no `opening-balances.csv`.
3. **Read the report.** For each file it prints the rows that are **REJECTED** with the row number, the column and what to fix, the warnings, and what will be created or changed. Fix the files in Excel and repeat step 2 until no row is rejected. On a new hotel a row marked "expected in a dry run" only waits for an earlier file of the same run (rooms wait for the room types) and goes through in step 4.
4. **Commit.** The same command without `-DryRun`:
```
.\Import-HccMasters.ps1 -Folder "D:\HccMasters" -OpeningBalancesDate 2026-10-31
```
   Each file is saved completely or not at all, in order; the script stops at the first file with a rejected row (fix it and run the script again: files already loaded show "unchanged" and are skipped). Opening balances and till cash are accepted only before the first business day is closed. Staff it creates cannot sign in yet: the script lists them, and you set each person's first password or PIN in Access control > Set credential.
5. **Verify.** The script ends with "Finished: ... committed, 0 file(s) with rejected rows". Sign in to the web app as the Owner on the server PC: the room grid shows every room (Ready) and Admin > Users lists the new users. In the same browser, `https://localhost:5443/api/v1/import/batches` lists every file as `Committed`, and `https://localhost:5443/api/v1/import/opening-balances` shows `"balanced":true` with your totals in paise. The first cashier shift on each till opens with the imported float. Stock items start at zero: count the store and floors in Stock > Count.
(`-WhatIf` lists the files and sends nothing. The script talks to `https://localhost:5443` on the server PC; from another PC use `-ServerUrl https://<server>:5443` (that PC needs the hotel CA, section 8). The Technical administrator may run the dry run; only the Owner can commit.)

## 7. Backups (do not skip)
Backups are **encrypted**: SQL Server Express cannot do that itself, so the backup script encrypts every checked backup with the hotel's backup key (AES-256, built into Windows, no extra software) and deletes the unencrypted file. A copied backup is useless without the key.
PowerShell (Admin), scripts folder, keys USB stick plugged in:
```
.\New-HccBackupKey.ps1 -OfflineCopyPath "E:\HCC-backup-key.txt"      # once: makes the backup key; the USB file is its offline copy (safe!)
Copy-Item hcc-ops.sample.json hcc-ops.json      # then open hcc-ops.json and check the folder names
.\Backup-HccDatabase.ps1 -Type Full              # first full backup, now
.\Register-HccBackupTasks.ps1                    # schedules: full 02:30 daily, changes every 4 h, restore test every Sunday 04:30
```
(`-WhatIf` on any of them shows the plan without doing it.)
- The backup key lives in `C:\ProgramData\HCC\Keys` (only SYSTEM and Administrators can read it). With a dedicated backup user (section 2), add `-ReaderAccount HOTELPC\svc-backup` to `New-HccBackupKey.ps1` and `-RunAsUser HOTELPC\svc-backup` to `Register-HccBackupTasks.ps1`, and put `"BackupAccount": "HOTELPC\\svc-backup"` in `hcc-ops.json`.
- The scheduled tasks run the scripts **only** from `C:\Program Files\HCC\Operations`, and `Register-HccBackupTasks.ps1` refuses to register them if anyone other than administrators can change the scripts there (a task running as SYSTEM must never run a file a normal user could edit). If you ran the installer, the scripts are already there; otherwise add `-InstallScripts` (it copies them there and sets the permissions).
- Backups are kept 30 days (never fewer than the 3 newest full ones). **Check:** `.bak.hccenc` files appear in the backup folder (no plain `.bak` files stay there); Task Scheduler > HCC folder lists three tasks.

**Run a restore drill now:** `.\Restore-HccDrill.ps1`. It checks and decrypts the newest backup into a throw-away copy, checks it for damage, compares record counts and deletes the copy (and the decrypted file), then says `DRILL PASSED` (log in the backup folder under `Logs`). A backup that was never restored is not a backup: the Sunday task repeats this every week; look at `Logs\drill-history.jsonl` or the latest `restore-drill_*.log` once a month.

**Offline copy (weekly):** copy the backup folder's `.bak.hccenc` files to the backup USB disk (or another PC) and take it off site; a fire or theft takes the PC and the folder together. The files are encrypted, so the disk may travel, but keep the **keys** stick (backup key, guest ID key, CA) in the safe, never with the backup disk. **Restoring on a replacement PC:** install as above, then `.\New-HccBackupKey.ps1 -ImportOfflineCopy "E:\HCC-backup-key.txt"` before restoring; without the offline copy of the backup key the backups cannot be opened.

## 8. Phones and tablets
See `ANDROID_SERVER_ADDRESS.md` (installing the certificate on Android and typing the server address in the app). For a browser on a Windows PC: double-click `HCC-Hotel-CA.cer` > Install Certificate > Local Machine > "Trusted Root Certification Authorities". Because of its name constraint the hotel CA is trusted only for the HCC server's own names and addresses.

## 9. Final check list
- `https://<server>:5443/api/v1/system/health` shows ok, from a tablet, without a certificate warning.
- `https://<server>:5443/api/v1/system/discovery` shows the server name and version (this is also what the app uses to confirm it found HCC).
- `https://<server>:5443/` opens the sign-in screen (section 6c), and a check-in with an ID number saves (section 6b).
- `http://127.0.0.1:5080/` no longer answers (setup listener off, section 6).
- The backup folder holds `.bak.hccenc` files and the restore drill passed; `icacls D:\HccBackups` lists only SYSTEM, Administrators, `NT SERVICE\MSSQL$SQLEXPRESS` (and your backup user).
- The keys stick (CA `.pfx`, guest ID key, backup key) is in the safe; `certlm.msc` shows no "HCC Hotel CA" with a private key; a calendar reminder for 4b exists.
- Restart the PC: the service comes back by itself (give it two minutes).
- Masters loaded (section 6d): rooms, room types, rates, till float and opening balances committed with `Import-HccMasters.ps1`, no file with rejected rows.

## 10. Booking website on the internet (optional; only when the owner decides)
The booking routes (`/api/v1/public/booking/...`) are off until `Hcc:WebBooking:Enabled` is `true`. Even then, **never port-forward 5443** (that would put staff sign-in and the whole staff API on the internet). The safe recipe:
1. In `appsettings.Production.json`: `"WebBooking": { "Enabled": true }` and, under `Hosting`, `"PublicPort": 5444`, `"PublicBindAddress": "127.0.0.1"`, `"KnownProxies": [ "127.0.0.1" ]`, and add the public site name (for example `book.yourhotel.in`) to `AllowedHosts`. Restart the service. Port 5444 then serves **only** the booking routes; everything else (sign-in, setup, staff API, the web app) answers "not found" there.
2. Run a reverse proxy on the same PC that terminates HTTPS with a real certificate for the public name (for example Caddy, which gets a free certificate itself) and forwards to `http://127.0.0.1:5444`, sending `X-Forwarded-For` and `X-Forwarded-Proto` (Caddy does by default). Put a CAPTCHA / rate limit in the proxy too. Only the proxy's port 443 is opened on the router.
3. How HCC treats it: forwarded headers are believed only on port 5444 and only from the addresses in `KnownProxies`, so the booking rate limit counts each real visitor separately; a request that did not arrive over HTTPS is refused; first-time setup and the first-Owner call are refused for any request carrying forwarding headers, on every port.
4. The server refuses to start if `PublicPort` is set on loopback without `KnownProxies`. A proxy on another machine: set `PublicBindAddress` to the PC's address (the listener then uses HTTPS with the server certificate) and list the proxy's address.

## Upgrading, uninstalling
Run the new installer: it stops the service, replaces the programs, keeps your settings, starts the service again. Start `Hcc.Api.exe migrate` once first when told (new tables; as the administrator, see step 5); the safety backup is automatic. After upgrading from a version before 26 Oct 2026, run `Initialize-HccDatabase.ps1` again (same options as step 2: it removes the old database-owner right of the service and protects the backup folder), then `New-HccBackupKey.ps1` and `Register-HccBackupTasks.ps1` (section 7), and set `AllowedHosts` (`New-HccCertificate.ps1 -RenewServer ... -UpdateConfig`). Uninstalling never deletes the database or the backups. To remove the service itself: `.\Install-HccService.ps1 -Uninstall`.
