# A09 — Production readiness and ETP stand-ins

Agent A9 (Opus), 26 Oct 2026, branch `claude/audit-fable-2026-10-26`, audit-only. No product file was changed. Raw outputs: `05 Delivery Evidence\Audit\evidence\A09\`; probes: `05 Delivery Evidence\Audit\probes\A09_*`.
**No admin rights on this PC**: the Windows service, Task Scheduler registration, LocalMachine certificate store and Program Files writes were reviewed on paper only (plus their `-WhatIf` self-tests). SQL work ran under `locks\dotnet-test.lock` (taken 15:15 after A1 released it) on `HccTest_perf_*` / `HccTest_drill_*` databases only; all of them were dropped at the end (see §k). The current Windows login is **not** sysadmin on `.\SQLEXPRESS` (`IS_SRVROLEMEMBER('sysadmin') = 0`, `evidence\A09\sqlcmd-encrypt-check.txt`), so server-wide DMVs were not readable; statement costs come from the perf database's own Query Store.

**In one paragraph.** The deployment scripts are careful and well hardened on paper (43/43 self-tests pass), but they have only ever run with `-WhatIf`. The first real check against SQL Server shows the main problem. With the current `sqlcmd` (ODBC Driver 18), **every script that talks to SQL Server fails to connect** unless `TrustServerCertificate` is on, and neither INSTALL.md nor `hcc-ops.sample.json` turns it on. That covers database set-up, every backup, the restore drill and the pre-upgrade backup (A09-01, High). Other go-live gaps:
- the .NET 10 runtime is not listed as a prerequisite;
- the installer has never been compiled;
- the nightly full and differential backups are scheduled for the same minute;
- there is effectively no log for the operator to read;
- the offline read-only copy disappears after 30 minutes of outage;
- a replacement PC cannot reopen guest ID numbers without a developer.

Performance at one year's scale is not a go-live risk, with one exception: the room board query (avg 76 ms, worst 2.4 s, ~10,500 page reads per call for 24 rooms). Of the 38 ETP stand-ins, 1 blocks go-live outright (the backup script, because of A09-01). 2 more block only in some cases: POS printing if the restaurant goes live on HCC, and invoices if the CA says e-invoicing applies. 13 are needed in the first month and 22 can wait. ETP code **is** on this PC now. It can serve about 15 rows (PDF, Excel, Tally, backup, installer, migrations), but none of the day-one blockers (KOT printing, messaging, OCR, payments, channel manager).

## (a) INSTALL.md walk (clean hotel PC, step by step)

Legend — *Script*: yes = a script does the step; doc = only described. *Tested*: what `deployment\windows\tests\Test-HccDeployment.ps1` covers (43/43 PASS today, `evidence\A09\deployment-tests.txt`, 28 s; every script is run **only with `-WhatIf`** or as a pure function, never against SQL Server, a service or a certificate store), plus what other evidence exercised it. *Admin*: needed? / stated in INSTALL.md?

| § | Step | Script | Tested | Admin needed / stated | Hard-coded path | Notes |
|---|---|---|---|---|---|---|
| 0 | Fixed IP, backup drive + BitLocker, two USB sticks | doc | — | yes / yes ("You are a Windows administrator") | — | **Missing prerequisite: the .NET 10 ASP.NET Core runtime.** `Build-Installer.ps1:16,26` publishes `--self-contained false`, the installer has no prerequisite check (`Hcc.iss`), and INSTALL.md never says to install the runtime → step 5 fails on a clean PC (A09-03). |
| 1 | SQL Server 2025 Express + `sqlcmd` | doc | — | yes / implied | `.\SQLEXPRESS` default everywhere (parameter) | The check command correctly uses `sqlcmd -E -C`; the **scripts do not pass `-C`** (A09-01). |
| 2 | `Initialize-HccDatabase.ps1` (DB, runtime login without db_owner, backup login, private backup folder) | yes | 3 `-WhatIf`/refusal checks; runtime grants proven by `Hcc.Platform.Tests` `RuntimePermissionTests` (reads, writes, sequences, TVPs, applocks work; DDL and trigger disable refused) | yes / yes | none (parameters) | As written in INSTALL.md (no `-TrustServerCertificate`) it **fails on a fresh PC with ODBC 18 sqlcmd** — same root cause as A09-01. No elevation check inside the script (it needs sysadmin on SQL + icacls on the folder). |
| 3.1 | Installer `HCC-Server-Setup-<v>.exe` | outline (`installer\Hcc.iss`, `Build-Installer.ps1`) | only "skips when Inno Setup is absent" | yes / yes (`PrivilegesRequired=admin`) | `{autopf}\HCC` with `DisableDirPage=auto` | **Never compiled** (ETP_Reuse #16). Does not ship `web\index.html` (only creates the empty folder, `Hcc.iss:48-50`). The "or copy the published files" alternative has no script (no publish/copy helper; operator must also copy the `.ps1` files into `Operations` with admin-only ACLs — `Register-HccBackupTasks.ps1 -InstallScripts` does that part). |
| 3.2 | Edit `appsettings.Production.json` | doc | validator unit tests (`HccConfigValidator`) | yes (file in Program Files) / **not stated** | sample: `D:\HccBackups`, `C:\Program Files\HCC\Operations\Backup-HccDatabase.ps1` | Validator refuses TrustServerCertificate, SQL logins, `AllowedHosts=*`, missing thumbprint/backup folder (`HccHosting.cs:57-102`) — good. `Encrypt=Optional` allowed only for a local instance. |
| 3.3 | `Install-HccService.ps1` (service, recovery, firewall Private/Domain, settings ACL) | yes | `-WhatIf` + `-Uninstall -WhatIf` | yes / yes (script checks elevation, line 44) | `C:\Program Files\HCC\Server` default | Not run (no admin). Delayed-auto start; restart 5 s/30 s/60 s; creates `logs\` that nothing writes to (A09-05). No Event Log source registration (A09-05). |
| 4 | `New-HccCertificate.ps1` (name-constrained CA, 397-day server cert, CA key exported then removed) | yes | `-WhatIf`, plan + name-constraint encoders | yes / yes (checks elevation, line 151) | output `<scripts>\certs` | Not run (LocalMachine store needs elevation; the APK v12 run used a PFX via `Kestrel__Endpoints__Https__*` env vars instead — `APK_v12_Emulator_Report.md` "Server set-up"). The `LocalMachine\My` thumbprint path (`HccHosting.cs:219-228`) is therefore **untested anywhere**. |
| 4b | Yearly renewal `-RenewServer -CaPfx` | yes | `-WhatIf` | yes / yes | — | Old cert deletion in `certlm.msc` is manual. Devices need nothing (they trust the CA). |
| 5 | `Hcc.Api.exe migrate` then `Start-Service` | exe (no wrapper) | migrations 0 → 2702 (45) applied in W4_E2E2, APK v12 and this probe (`evidence\A09\perf-migrate.txt`) | yes / yes | content root = current folder (INSTALL says `cd` first) | In Production `migrate` runs the full config validator first (`Program.cs:13` before line 17), so step 4 must be done before step 5 (it is, in INSTALL order). On an **upgrade** the backup gate calls `Backup-HccDatabase.ps1` without `-TrustServerCertificate` (`ScriptBackupGate.cs:79-90`) → refuses (A09-01). |
| 6 | `Initialize-HccProperty.ps1` (hotel + first Owner, then setup listener off) | yes | `-WhatIf`, refuses non-loopback | yes / yes | `C:\Program Files\HCC\Server\appsettings.Production.json` default | Real setup flow exercised through the API (`SetupProperty`) in W4_E2E2 and this probe, not through the `.ps1`. |
| 6b | `New-HccGuestIdKey.ps1` (DPAPI machine key, offline copy, ACL, Required/AllowMachineScope) | yes | real run against temp files incl. refuse-second and `-Rotate` | yes / yes | settings path default as above | N9 (`StartsWith` without separator) is **fixed** (`New-HccGuestIdKey.ps1:53` now appends the separator). **No import of the offline copy on a replacement PC** — INSTALL.md:97 says "ask your developer" (A09-07). |
| 6c | Copy `dist\production\index.html` to `Server\web\` | doc | W4_E2E2 H1 (served by the API) | yes (Program Files) / **not stated** | `C:\Program Files\HCC\Server\web` | Needs a developer machine with Node 25 to build; not in the installer (A09-04). |
| 7 | `New-HccBackupKey.ps1` | yes | real run: create, refuse second, import offline copy | yes / yes | `C:\ProgramData\HCC\Keys\hcc-backup-key.txt` | Exercised for real in this audit's drill (§b). |
| 7 | `Copy-Item hcc-ops.sample.json hcc-ops.json` | doc | — | yes / yes | sample `D:\HccBackups` | The installer already created `hcc-ops.json` (`Hcc.iss:46`); the documented `Copy-Item` silently **overwrites** it (and any later edits if re-run) (A09-14). `TrustServerCertificate: false` in the sample is what breaks every backup (A09-01). |
| 7 | `Backup-HccDatabase.ps1 -Type Full` | yes | `-WhatIf`, naming/retention/chain/encryption pure tests | yes / yes | — | Real run in §b: **fails as shipped**, works with `-TrustServerCertificate`. |
| 7 | `Register-HccBackupTasks.ps1` (full 02:30, diff every 4 h from 06:30, drill Sunday 04:30, as SYSTEM, admin-only script folder) | yes | `-WhatIf`, refusal of a user-writable folder | yes / yes | `$env:ProgramFiles\HCC\Operations` | Not run (no admin). The 4-hour differential lands on **02:30**, the same minute as the full (A09-02). |
| 7 | `Restore-HccDrill.ps1` | yes | `-WhatIf`, refusal of non-scratch names | yes (as SYSTEM via task) / yes | scratch default `<backup>\DrillScratch` | Accepts `-Database` and `-ScratchName`, so it was dry-run for real in §b. |
| 7 | Weekly offline copy to USB | doc | — | — | — | Manual; no reminder or check. |
| 8 | CA on Android / Windows; APK server address | doc (`ANDROID_SERVER_ADDRESS.md`) | APK v12 emulator run (test CA) | Windows "Local Machine" store needs admin / **not stated** | — | The server APK **bundles the hotel CA at build time** (`res/raw/hcc_hotel_ca.pem`), so each hotel's APK must be rebuilt by a developer after step 4 (and again for a new CA). |
| 9 | Final checklist | doc | — | — | — | Good list; nothing checks it automatically. |
| 10 | Booking website behind a reverse proxy | doc + validator | validator tests | yes / implied | — | Off by default; proxy/CAPTCHA not supplied (ETP_Reuse #35). |
| Up | Upgrade with the installer | outline | — | yes / yes | — | Installer **starts the service before** the admin runs `migrate` (`Hcc.iss:52-54`) and the server has no schema-version guard (A09-04). For pre-26-Oct installs INSTALL says `migrate` first, `New-HccBackupKey` later → the encrypted pre-migration backup refuses for want of a key (A09-15). Existing `appsettings.Production.json` is kept but new keys are never merged (A09-16). |
| Un | `Install-HccService.ps1 -Uninstall`; uninstaller keeps DB and backups | yes | `-WhatIf` | yes / yes | — | Fine. |

`TrustServerCertificate` audit: nothing sets it silently. The API refuses it in Production (`HccHosting.cs:73`); `SqlConnectionFactory.LocalDevConnectionString` (the only `TrustServerCertificate = true` in `src`) is used by tests only. The scripts use it only when `-TrustServerCertificate` or `hcc-ops.json` says so — which is exactly why they fail out of the box on ODBC 18 (A09-01). The safe fix is `-C` (or `-No`) for a **local** instance only, mirroring the API's own "Encrypt=Optional only for a local instance" rule.

## (b) Backups and restore

**What was run.** The lead asked for the SQL lane back at 15:44, because the lock had been held past the 30-minute cap. That was before the parameterised drill (`probes\A09_drill.ps1`) could start. The probe is ready to run against a disposable `HccTest_*` database: real `New-HccBackupKey` → `Backup-HccDatabase` full and differential → concurrent full+diff → `Restore-HccDrill` into `HccTest_drill_*` → restores with a wrong key and with an imported key. So the drill is **reviewed on paper**, plus one live check of its precondition:
- `evidence\A09\sqlcmd-encrypt-check.txt`: `sqlcmd -S .\SQLEXPRESS -E -b -Q "SELECT @@VERSION"` fails with *"SSL Provider: The certificate chain was issued by an authority that is not trusted"*, exit 1. The same command with `-C` works (SQL Server 2025 Express RTM-CU9, `E:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE`). `Invoke-HccSql` adds `-C` only when asked (`Hcc-Common.ps1:55-56`), `hcc-ops.sample.json:10` says `false`, and the documented commands never pass `-TrustServerCertificate` (INSTALL.md:23, 114-116). **So, as shipped, `Initialize-HccDatabase`, `Backup-HccDatabase`, `Restore-HccDrill` and the `migrate` backup gate cannot connect** on a PC like this one (A09-01). The deployment self-tests never caught it because no test opens a SQL connection.
- The scripts **do** accept a target database (`-Database`, limited to `^Hcc…` names, `Hcc-Common.ps1:9-13`) and a scratch name (`-ScratchName`, limited to `HccDrill_`/`HccTest_`, lines 16-20). So the drill can be rehearsed on a test database exactly as the brief wanted, and the probe script is there for the lead or V1.

**Paper walk — `Backup-HccDatabase.ps1`.**
- Order of work: `BACKUP DATABASE … WITH CHECKSUM, INIT` (+ `DIFFERENTIAL` / `COPY_ONLY`), then `RESTORE VERIFYONLY … WITH CHECKSUM`, then encrypt, delete the plain `.bak`, then apply retention (lines 51-103).
- The key is read **before** the backup, so no plain file is left behind when the key is missing (line 72).
- A differential with no full in the folder becomes a full (lines 75-82).
- The folder ACL is checked, and a WARN is logged if the folder is no longer private (lines 70-71).
- Failures are logged and re-thrown, so Task Scheduler shows exit ≠ 0 as the "last run result". Nothing notifies anyone (ETP #12).

**Encryption (F-H03 fix) — holds.** See `Hcc-Common.ps1:217-352`.
- File format: `HCCBAK1\0 | 8-byte key fingerprint | 16-byte IV | AES-256-CBC/PKCS7 | HMAC-SHA256` (encrypt-then-MAC).
- Separate encryption, MAC and id sub-keys are derived by HMAC with fixed labels.
- The tag covers header + ciphertext. It is checked over the whole file **before** any plaintext is written, with a constant-time compare (lines 319-326).
- A wrong key gives "different backup key (fingerprint …)" and writes nothing.
- Self-tests cover multi-block and odd sizes, tampering and the wrong key (`Test-HccDeployment.ps1:107`).
- The plain `.bak` sits in the private folder between backup and encryption, for seconds to minutes. The drill writes a decrypted copy into `DrillScratch` and deletes it in `finally`. Both are acceptable given the folder ACL.

**Retention arithmetic** (`Select-HccBackupsToDelete`, `Hcc-Common.ps1:92-106`; defaults: 30 days, ≥ 3 newest fulls, pre-migration backups 90 days):
- **Schedule** (`Register-HccBackupTasks.ps1:23,34-35`): full daily at 02:30. Differential `-Once -At 06:30 -RepetitionInterval 4h`, which gives 06:30, 10:30, 14:30, 18:30, 22:30 and **02:30**: 6 a day, one colliding with the full (A09-02).
- **Kept at steady state**: 30–31 fulls (a full taken exactly 30 days earlier stays or goes depending on its seconds), every differential newer than the oldest kept full (≈ 30 × 6 = ~180), and every pre-migration backup of the last 90 days.
- **Size**: after 274 business days of FY26-scale activity, the probe database had **208 MB allocated** (`evidence\A09\perf-row-counts.txt`, data + log files; the data actually used is smaller). Taking ~200 MB per full after a year as an upper bound: 31 × 200 MB ≈ 6.2 GB of fulls. Differentials grow from near zero after 02:30 to one day's changes by 22:30 (≈ 1–3 MB a day here), about 0.2–0.5 GB in total. That is **≈ 7 GB per year of data**, growing roughly in line with database size.
- **Disk sizing**: a 2-TB backup drive is no constraint. A 32-GB USB stick for the weekly offline copy is enough for ~4 years if only the newest full and the differentials after it are copied. INSTALL says to copy "the `.bak.hccenc` files", i.e. all ~210; it should say "the newest full and the differentials after it".
- **Recovery point**: up to 4 hours, because there are no log backups (the database is created with the instance's default recovery model, SIMPLE on Express). That is fine for this hotel if the owner accepts re-entering up to half a shift, but it needs an explicit owner yes (see decisions).
- **Edge case**: retention is not filtered by database. `Backup-HccDatabase.ps1:97` uses `-like "${Database}_*"`, which for database `Hcc` also matches `Hcc_X_…`, so two `Hcc…` databases sharing one folder would count each other's fulls (A09-10).

**Paper walk — `Restore-HccDrill.ps1`.**
- Picks the newest FULL plus the newest later DIFF (`Select-HccRestoreChain`, pre-migration backups excluded).
- Decrypts after the HMAC check, then runs `RESTORE FILELISTONLY` → `RESTORE … WITH MOVE … NORECOVERY/RECOVERY, CHECKSUM` → `DBCC CHECKDB … NO_INFOMSGS, ALL_ERRORMSGS`.
- Compares per-table row counts with the live database. It fails only on a missing table, or on an empty restore of a non-empty live database.
- Drops the scratch database, appends to `drill-history.jsonl`, and exits 1 on failure. Sound.
- Needs ≈ 2× the database size free on the backup drive (decrypted copy + restored files). Running as SYSTEM, it needs `dbcreator` (`Initialize-HccDatabase.ps1 -GrantDrillRights`).
- Weakness: if a differential's base full is not in the folder (e.g. a manual SSMS full), the drill fails. That is correct, but the SQL error is one the owner will not understand.

**Restoring on a different machine (DPAPI scope).**
- *Backup key*: DPAPI **LocalMachine** + entropy "HCC backup key v1" (`Hcc-Common.ps1:356-382`). A copied key file is useless on another PC. The backups open there only after `New-HccBackupKey.ps1 -ImportOfflineCopy <USB file>`, which is scripted and covered by the self-test "imports on another PC". Good.
- *Guest-ID key*: DPAPI LocalMachine (`dpapi-machine:`), stored in `appsettings.Production.json` and by design **never in the backup**. On a replacement PC the restored database is complete, but every sealed ID number stays unreadable (only the last 4 digits show) until someone re-wraps the raw key from the offline copy. There is **no script** for that; INSTALL.md:97 says "ask your developer … `ConvertTo-HccWrappedGuestIdKey`" (A09-07).
- *Server certificate*: also needed on the new PC. Its key cannot be exported, so it must be re-issued with the CA PFX from the safe. Devices keep working because they trust the CA, as long as the new PC keeps a name/IP inside the CA's name constraint.

## (c) Masters import

- **Templates vs code**: all 11 `templates\*.csv` headers equal the `ImportMasters.cs:37-113` column lists (room-types, rooms, tariffs, seasons, staff, ledgers, opening-balances, till-cash, guests, pos-outlets, pos-menu); asserted by `Hcc.Import.Tests` `Shipped_templates_match_the_column_specs_and_every_sample_parses`. Forbidden columns (password, PIN, Aadhaar, PAN, passport, …) are refused (`ImportMasters.cs:131-132`).
- **Wrong row**: the planner reports row + column + message (README example "Row 7 · rate · '42OO' is not an amount in rupees"); tests `Row_level_errors_name_row_column_and_message`, `Row_errors_and_references_to_other_masters_block_the_commit`. Commit is all-or-nothing; dry run executes and rolls back (`Dry_run_executes_everything_then_rolls_back`).
- **Opening balances**: must balance and are accepted only before the first day close (`Opening_trial_balance_must_balance_and_needs_setup_state`, `Opening_balances_and_till_cash_are_refused_once_the_first_business_day_is_closed`). E2E2 defect N5 (opening balances missing from the cumulative trial balance) now has a covering test `Imported_opening_balances_are_in_the_cumulative_trial_balance_and_it_balances_at_16_20_000`; I did not re-run whether the production trial balance now shows them (A3b owns the trial balance).
- **Idempotency**: `Re_commit_is_idempotent_and_reimporting_the_same_files_changes_nothing`, `Existing_users_seasons_and_used_tills_are_not_changed_by_import`.
- **In this probe** all 7 masters used (24 rooms, 3 types, tariffs, ledgers, opening balances, till, 1,155 fictional guests) uploaded and committed with 0 errors in about 3 s (`evidence\A09\perf-seed.log` lines 3-9).
- A9 did **not** re-run `Hcc.Import.Tests` (the lock went back to the lead); A1's full-suite output in `evidence\A01\` covers it.
- **Gaps for a real go-live** (also E2E2 N1–N4): there is no import for **stock items**, **departments** (neither has a UI either), **companies / corporate accounts** (the UI save `GRP_COMPANY_SAVE` is now mapped, `03 Prototype/src/clients/sync/money.ts:354`, but there is no import) or **suppliers**, and staff joining dates cannot be set. A hotel can only create these through raw API calls today (A09-09).

## (d) Read-only / offline behaviour

- **Nothing is queued**: `runtime.ts:5` and `cache.ts:3-4`; every command while unreachable returns `READ_ONLY_REFUSAL` "Server unavailable — read-only. Nothing was sent and nothing is saved on this device" (`runtime.ts:28,306-307`). Confirmed.
- **Banner** (`clients/react.tsx:112`): "Server unavailable — read-only. Bookings and payments are disabled until the hotel server is back. Nothing is saved on this device." + Retry; under it "Showing data as of dd/mm hh:mm (last loaded from the server)" (`sync/react.tsx:224`). APK12-5 (banner says bookings are disabled but "New booking" stays enabled) and APK12-6 (no automatic recovery; Retry needed) are still open.
- **Cache expiry**: the encrypted copy is deleted on sign-out, on 401, on another user's sign-in, after 5 wrong offline unlocks, and when the lock record expires: **idle 30 min since the last answered server request** or the 12 h absolute session end (`cache.ts:89-92`, `runtime.ts:336,376-378`, `AccessPorts.cs:10-12`). Consequence: during a server outage longer than ~30 minutes every device signs out and loses the read-only copy, so the front desk cannot even see who is in house (A09-06).

## (e) Performance with a year of data

**Probe**: `probes\A09_perf_run.ps1` + `probes\A09_perf_seed.mjs`, run against the disposable database `HccTest_perf_20261026095553`.
- **Set-up**: migrated 0 → 2702. A Node 25 driver (`evidence\A09\node-version.txt`) then drove the real API (Development, 127.0.0.1:5291, the built `Hcc.Api.dll` copied to a temp folder) through simulated business days from 1 Oct 2025.
- **Masters imported**: 24 rooms in 3 types, tariffs, ledgers, opening balances, a till and 1,155 fictional guests.
- **Each simulated day**: open the cashier shift; settle and check out departures (cash/UPI/card) and issue the tax invoice; run the housekeeping checkout clean to Ready; book 3–7 walk-in arrivals (quote → book → register guest → check in); submit an exact cash count; run `RunBusinessDayClose`.

**What the seed reached.** 274 closed business days (1 Oct 2025 → 1 Jul 2026) in 18 min wall clock (15:25:53–15:43:58). At that point the lead asked for the lock back, so the read-timing phase **did not run** at full scale.
- Volumes (`evidence\A09\perf-row-counts.txt`): 1,399 reservations/stays/folios (FY26 has 1,279), 2,554 guest profiles, 3,404 room nights (~52 % of 24 × 274), 3,390 charges, 1,381 tax invoices / 3,302 invoice lines, 1,391 receipts, 276 shifts, 274 closing snapshots, 16,342 accounting source entries, 26,270 audit events, 9,784 outbox events. Data + log: 208 MB allocated.
- Ten invoices were correctly refused ("An invoice of ₹50,000 or more to an unregistered guest needs the recipient's name and address"; the probe gave no address). A good rule, noted for A3a.
- A first attempt (`evidence\A09\perf-seed-attempt1-stalled.log`) stalled. Open **stayover** housekeeping tasks are never closed or expired, and they pushed the checkout tasks off the 100-row task page (316 open stayover tasks after 3 months) — A09-19.

**Statement costs measured over the seeded period.** Source: the perf database's Query Store (`evidence\A09\perf-top-statements.txt`). These are averages over every execution during the seed, i.e. from an empty database up to 9 months of data, on a PC shared with a running ETP test session.

| Read (API) | Statement | Execs | Avg ms | Max ms | Avg logical reads | Comment |
|---|---|---|---|---|---|---|
| **Room board** `GET /rooms/board` | `SELECT r.room_id, r.room_no … cur … nxt …` (`SqlHousekeepingStore.cs` ~l.250-284) | 545 | **76.1** | **2,398** | **10,547** | Heaviest by far: ~440 page reads per room per call. The `cur`/`nxt` OUTER APPLYs over `inv.reservation`/stays plus `openTasks` grow with history. A09-18. |
| Quote / availability tariff | `SELECT MAX(v) … inv.room_type_tariff` | 822 | 2.6 | 20.7 | 1,500 | fine |
| Outbox poll (4 workers, every 2 s) | `SELECT TOP (@n) … plat.outbox WHERE event_type IN (…) AND NOT EXISTS …` | 2,572 | 35.7 | 913 | 226 | Runs ~170,000 times a day. No index on `event_type`, and the outbox is never purged (`SqlRetentionPurge` deletes only idempotency and session rows), so cost grows linearly. A09-20. |
| Folio `GET /folios/{id}` | `SELECT charge_id … FROM bill.charge WHERE folio_id = @f` | 4,173 | 0.8 | 4.8 | 30 | fine |
| Day-close night/charge reads | `SELECT c.charge_id … @from,@to` and deposits sum | 274 each | 1.4 / 2.0 | 9.6 / 8.5 | 122 / 372 | fine; the day close itself took 45–310 ms per day in the seed log |
| Cash book / receipts by range | `SELECT x.receipt_id … @from,@to` | 274 | 0.35 | 2.4 | 32 | fine |

**API timings at small scale.** From the earlier 6-day smoke run of the same probe; medians of 10 warm calls. That run's log was superseded by the full run.

| Read | Median ms |
|---|---|
| Room board | 8.5 |
| Bookings list | 12.5 |
| Folio | 4.7 |
| Cash book range | 4.7 |
| Trial balance (cumulative) | 11.1 |
| GSTR-1 | 4.9 |
| Customers search | 12.5 |
| Day pack (year range) | 15.9 |
| Cashier folios | 16.4 |

**Estimate at one year (FY26 scale) for the 8 heavy reads.** Based on the statement costs above and the query shapes; not measured end to end; confidence Medium.

| Read | Estimate at one year | Basis |
|---|---|---|
| Room board | ~80–150 ms typical, spikes of 1–2 s | measured statement cost |
| Bookings list (paged 200, `IX_reservation_room`) | < 50 ms | query shape |
| Folio | < 10 ms | measured statement cost |
| Cash book range for a year | < 50 ms | query shape |
| Trial balance cumulative (16k source entries) | < 100 ms | query shape |
| GSTR-1 | < 50 ms for a month, < 200 ms for a year (3.3k lines) | query shape |
| Customers search | ~50–150 ms | `SqlCustomerStore.cs:66-80` loads every guest, reservation and room charge of the property into memory on every call and filters in C# (≈ 7,300 rows now, ×5 after five years) |
| Day pack, one day | < 50 ms | query shape |

None of these threatens the plan's proposed target (p95 < 1 s, `HCC_Claude_Execution_Plan_v1.md:523`) at one year, apart from the board spikes. Customers and the board are the two reads that grow with history; both need an index or paging fix before year 3.

## (f) ETP stand-ins: 38 rows classified

Kind: **R** = real HCC code that works and only awaits ETP alignment; **S** = simulated behaviour (nothing leaves the server / fake result); **O** = output not built (JSON or text only, no print/PDF/Excel/e-mail).

| # | Spot | Kind | Class | Why |
|---|---|---|---|---|
| 1 | Directory.Build.props | R | Later | Builds are deterministic; signing/SourceLink is polish. |
| 2 | SqlConnectionFactory policy | R | Later | Works; production validator enforces integrated auth. |
| 3 | IBackupGate contract | R | Later | Works. |
| 4 | SqlBackupGate | R | Later | Not used in production when `Hcc:Operations:BackupScript` is set (sample sets it). |
| 5 | MigrationRunner | R | Later | Journal, checksum drift, newer-DB refusal, applock: sound. |
| 6 | `migrate` CLI | R | Later | Works (45 migrations). |
| 7 | PBKDF2 hasher | R | Later | ETP has no credential hashing (§g); keep HCC's. |
| 8 | Housekeeping projector worker | R | Later | Works; logging/back-off gap is A09-05, not ETP. |
| 9 | Folio JSON only | O | First month | Guests ask for a folio print; the tax invoice HTML (#38) covers the legal document on day one. |
| 10 | Collections → Tally receipts | R | Later | Follows #21. |
| 11 | Shift count, no printed sheet | O | Later | Count is stored and audited; paper sheet optional. |
| 12 | Backup script | R | **Blocks go-live** | Not because of ETP: as shipped it cannot connect (A09-01). After that fix: First month (failure e-mail). |
| 13 | Restore drill | R | First month | Works after A09-01 (§b); result e-mail missing. |
| 14 | Task Scheduler registration | R | First month | Works on paper; 02:30 overlap (A09-02); no failure notification. |
| 15 | Cert / service / DB provisioning scripts | R | Later | Present; first real run on the hotel PC will tell (INSTALL "Status"). |
| 16 | Inno Setup installer | R (uncompiled) | First month | Day one can use a manual copy; the first **upgrade** needs it (A09-04). |
| 17 | ScriptBackupGate | R | Later | Affected by A09-01 on upgrades. |
| 18 | HccHosting (service, validator, Kestrel cert) | R | Later | Sound; LocalMachine cert path untested (§a 4). |
| 19 | Day pack JSON only | O | First month | Owner sees it on screen; Excel/PDF/e-mail to owner expected soon. |
| 20 | Expense receipt = file name | O | Later | Paper receipts can be filed. |
| 21 | Tally XML simulated | S | First month | Month-end posting to Tally; XML is generated but never confirmed by Tally. |
| 22 | Voucher CSV | O | Later | CSV opens in Excel. |
| 23 | Accounting projector worker | R | Later | As #8. |
| 24 | CA pack simulated | S | First month | GSTR-1 is due monthly; pack lines exist, nothing is sent/rendered. |
| 25 | POS KOT / bill printing not built | O | **Blocks go-live if the restaurant or room service runs on HCC from day one**; otherwise First month | No KOT to the kitchen and no printed restaurant bill from the server. |
| 26 | Technician directory | R | Later | Works. |
| 27 | Stock ledger (GRN simulated receipt) | S | Later | Plus the item-master gap (A09-09). |
| 28 | Stock rules | R | Later | — |
| 29 | Messaging gateway fake | S | First month | No WhatsApp/SMS/e-mail leaves; screens may show "sent" (A09-08). |
| 30 | Handover photo = file name | O | Later | — |
| 31 | Yield forecast simulated | S | Later | Manual rates work. |
| 32 | Payroll files CSV text, payslip JSON | O | First month | First pay run: bank upload layout, PF ECR / ESIC / PT formats, payslip PDF (CA P17). |
| 33 | Payslip WhatsApp simulated | S | Later | Hand out payslips. |
| 34 | OCR simulated | S | Later | Type ID numbers; ID capture is unmapped in the client (A09-08 latent risk). |
| 35 | Web booking deposit/confirmation simulated | S | Later | Off by default; owner decision. |
| 36 | OTA channel manager simulated | S | First month | No ARI push / reservation pull; keep extranets manual or risk overbooking. |
| 37 | Group statement JSON only | O | Later | Groups are occasional; statement on screen. |
| 38 | Invoice/credit note as HTML/text | O | **First month**, or **Blocks go-live if the CA says e-invoicing (IRN) applies** | HTML prints from a browser (APK printing untested, APK v12 "Not tested"); no PDF, logo, QR, IRN. |

Counts: **19 R**, **9 S** (#21, 24, 27, 29, 31, 33, 34, 35, 36), **10 O** (#9, 11, 19, 20, 22, 25, 30, 32, 37, 38). Classes: **Blocks go-live 1 unconditional** (#12, really A09-01) **+ 2 conditional** (#25 if POS is used, #38 if e-invoicing applies); **First month 13** (#9, 13, 14, 16, 19, 21, 24, 29, 32, 36, plus #25/#38 when their condition is false, plus #12 after the fix); **Later 22**. By the brief's categories: messaging 4 (#24 e-mail, #29, #33, #35 confirmation), OCR 1 (#34), payment 2 (#35 deposit, #37 group-payment reconciliation), channel manager 1 (#36), printing/documents 8 (#9, 11, 19, 25, 30, 32, 37, 38).

**On day one the hotel cannot:**
1. print a KOT or a restaurant/room-service bill from HCC (#25);
2. print a PDF invoice with logo/QR, or issue an e-invoice IRN — it can print the HTML tax invoice from a browser (#38);
3. send any WhatsApp/SMS/e-mail: booking confirmations, payslips, the owner's day pack, the CA pack (#24, #29, #33, #35) — and the messaging screens may record them as sent;
4. push rates/availability to OTAs or receive OTA bookings automatically (#36);
5. take an online deposit (#35);
6. read an ID card by camera/OCR (#34);
7. send vouchers to Tally (the XML is produced but unconfirmed, #21);
8. get Excel/PDF of the day pack, voucher register, payroll register, payslips, group statement (#19, #22, #32, #37);
9. attach expense receipts or handover photos (#20, #30);
10. be told by e-mail that a backup or restore drill failed (#12–14) — and, until A09-01 is fixed, it has **no working backups at all**.

## (g) ETP on this PC (premise changed; not audited)

`E:\ETP\Code\SaagarCC-V6-ETP-Source-and-Report-Engine-605002f` (git HEAD `8d65d83f`, 17 Sep 2026) plus `E:\ETP\Code\Worktrees` (≥10 worktrees, one used by the parallel ETP test session) **exist**, so the "ETP is not on this PC" premise of `ETP_Reuse_Pending.md:3` and CLAUDE.md is out of date. Read-only directory/grep survey (no build, no tests, no DB):

| Platform piece the stand-ins wait for | Present in ETP? | Where (ETP paths) | Rows it could serve |
|---|---|---|---|
| PDF exporters | yes (PDFsharp 6.2.4) | `src\Etp.Reporting.Reporting\SimplePdfReportExporter.cs`, `DailySalesReportPdfExporter.cs`, `VisualReportPdfDocument.cs` | 9, 11, 19, 32, 37, 38 (A4 layouts; no thermal) |
| Excel exporter | yes (DocumentFormat.OpenXml 3.3.0) | `OpenXmlReportExporter.cs`, `ReportPackExporters.cs` | 19, 22, 32, 37 |
| Tally export | yes | `src\Etp.Reporting.Infrastructure.SqlServer\SqlServerAccountingService.cs`, `AccountingAndSharingServices.cs` | 10, 21 |
| Backup, recovery drill, scheduled tasks | yes | `scripts\backup-etp-database.ps1`, `invoke-etp-recovery-drill.ps1`, `install-daily-backup-task.ps1`, `install-monthly-recovery-drill-task.ps1`, `remove-etp-scheduled-tasks.ps1` | 12, 13, 14 |
| Installer | yes | `installer\EtpReportingEngine.iss`, `scripts\build-windows-installer.ps1`, `test-installer-lifecycle.ps1` | 16 |
| Migration runner | yes | `src\Etp.Reporting.Infrastructure.SqlServer\Migrations.cs`, `DatabaseBootstrapper.cs` | 3, 5, 6 |
| Deterministic build props | yes | `Directory.Build.props` | 1 |
| E-mail sending | **no** (SMTP settings fields only, `OperationsAdministrationContracts.cs:176-179`; no SmtpClient/MailKit) | — | 24, 29, 33 stay open |
| WhatsApp/SMS gateway | **no** (desktop share-link launcher only, `ArchiveShareLauncher.cs`) | — | 29, 33, 35 |
| OCR, payment gateway, channel manager, KOT/thermal print | **no** | — | 25, 34, 35, 36 |
| Background-job / Windows-service hosting, credential hashing, DPAPI | **no** (ETP is a desktop app, `Etp.Reporting.Desktop`) | — | 7, 8, 18, 23 keep HCC's own code |
| Stores/inventory ledger | no (only retail `StockReconciliationService.cs`) | — | 27, 28 |

So roughly 15 of the 38 rows now have an ETP counterpart to compare against; the rows that block day-one operations (#25, #29, #34, #36 and e-invoicing for #38) have **no** ETP counterpart and need new work or a vendor either way.

## (h) Logging, monitoring, upgrade path

- **Where logs go**: default ASP.NET Core providers only (`Program.cs`, `appsettings.json` "Logging"); no file logger, so the `logs\` folder that `Install-HccService.ps1:57-60` creates stays empty. As a service, `UseWindowsService` (`HccHosting.cs:176`) adds the Event Log provider with source = application name (`Hcc.Api`), which **no script registers**; the virtual account cannot create a source, so the framework disables app logging to the Event Log after its first failed write. INSTALL.md:70 tells the owner to look for source "HccServer" / ".NET Runtime": a start-up configuration error does reach ".NET Runtime" (unhandled exception), but run-time warnings (failed projector polls, retention purge failures, `HccErrorMiddleware` errors) most likely reach nobody. Event Log provider default level is Warning. Confidence Medium (framework behaviour; the service was not installed here). **Rotation**: none needed today because nothing is written; backup logs are monthly files `backup_yyyyMM.log` and per-drill files + `drill-history.jsonl`, never pruned.
- **When a worker dies**: the four outbox workers and `RetentionWorker` catch every exception and retry every 2 s / 6 h (`HousekeepingEndpoints.cs:66-104`, `RetentionWorker.cs`), so they never die; a poison event or a permanent SQL error produces a Warning every 2 s, forever, with no back-off and no visible symptom except "checkout tasks don't appear" / "vouchers missing". `/system/health` (`HccComposition.cs:88-96`) reports only "ok", database and schema version — no outbox backlog, worker last-success, last backup or last drill age.
- **Service crash**: SCM restarts after 5 s, 30 s, 60 s (reset 1 day) (`Install-HccService.ps1:55`).
- **Upgrade**: installer stops the service, replaces programs, never overwrites `appsettings.Production.json` / `hcc-ops.json` (`Hcc.iss:38,45-46`), starts the service again **before** `migrate` is run; `migrate` takes a COPY_ONLY encrypted backup through the script (fails today, A09-01), refuses an older build against a newer DB and checksum drift (`MigrationRunner.cs:186-193`). The running server does not check that the DB schema matches the build (A09-04). New settings keys must be added by hand (A09-16).

## (i) Findings

| ID | Sev | Area | File:line | Evidence | Impact | Fix | Hours | Conf |
|----|-----|------|-----------|----------|--------|-----|-------|------|
| A09-01 | High | Backups / install | 08 Production/deployment/windows/Hcc-Common.ps1:55-56; hcc-ops.sample.json:10; INSTALL.md:23,114-116; src/Hcc.Operations/ScriptBackupGate.cs:79-90 | evidence/A09/sqlcmd-encrypt-check.txt: sqlcmd (ODBC 18) without -C → "certificate chain … not trusted", exit 1; with -C OK. Scripts add -C only on -TrustServerCertificate / hcc-ops TrustServerCertificate=true; neither INSTALL nor the sample sets it | On a fresh hotel PC: Initialize-HccDatabase (step 2), every scheduled backup, the restore drill and the pre-upgrade backup gate fail → no backups, no upgrades | For a local instance (`.`, `(local)`, localhost, machine name) pass `-C` (or `-No`) by default, like the API's "Encrypt=Optional only locally" rule; set the sample accordingly; add one non-WhatIf test that runs Backup/Restore against `.\SQLEXPRESS` with a HccTest_ DB; run `probes/A09_drill.ps1` | 3 | High |
| A09-02 | Medium | Backups | deployment/windows/Register-HccBackupTasks.ps1:23,34-35 | 06:30 + n×4 h = 02:30, same minute as the daily full | Every night the full and a differential of the same database start together. SQL Server does not run a full and a differential of one database at the same time (not reproduced here: drill not run), so one of them fails or waits every night and Task Scheduler shows a failure | Start differentials at 04:30 (or skip a diff within 1 h of a full inside the script) | 0.5 | Medium |
| A09-03 | Medium | Install | deployment/windows/installer/Build-Installer.ps1:16,26; INSTALL.md §0–1; installer/Hcc.iss (no prerequisite check) | `--self-contained false`; INSTALL never mentions the .NET 10 / ASP.NET Core runtime | Step 5 (`Hcc.Api.exe migrate`) fails on a clean PC | Publish self-contained (simplest) or add the runtime to §0 and a check in the installer | 1 | High |
| A09-04 | Medium | Install / upgrade | installer/Hcc.iss:48-54; src/Hcc.Api/Program.cs:12-32 (no schema check); src/Hcc.Api/HccComposition.cs:88-96 | Installer never compiled; ships no web/index.html; on upgrade `[Run] sc start` runs before the admin's `migrate`; server serves whatever schema it finds | After an upgrade the new code runs against the old schema (errors on new tables) until someone runs migrate; a first install needs a developer to copy files and build the web app | Server refuses to serve (health "schema behind, run migrate") when DB version < the build's newest migration; installer runs migrate (elevated) before starting, or does not start the service; bundle `web/index.html`; compile and test the installer once | 4 | High |
| A09-05 | Medium | Monitoring | src/Hcc.Api/Hosting/HccHosting.cs:176; deployment/windows/Install-HccService.ps1:57-60; INSTALL.md:70; Endpoints/HousekeepingEndpoints.cs:66-100 | No file logger; Event Log source = app name, never registered; INSTALL points at source "HccServer"; workers log a Warning every 2 s forever; health has no backlog/worker/backup age | When a projector, the retention purge or a backup fails, the operator sees nothing (no file, probably no Event Log entries, no e-mail); symptoms show up as "tasks/vouchers missing" | Register source "HccServer" in Install-HccService and set `EventLogSettings.SourceName`; add a rolling file log in `logs\`; back off and log one Error after N failures; add outbox lag, worker last-success and last backup/drill age to admin health and an Owner home tile | 4 | Medium |
| A09-06 | Medium | Offline | 03 Prototype/src/clients/sync/cache.ts:89-92; runtime.ts:336,376-378; sync/react.tsx:45-48; 08 Production/src/Hcc.Application/Access/AccessPorts.cs:10-12 | Read-only copy expires 30 min after the last answered request; then sign-out + wipe | A server outage over ~30 min leaves every device blank: no in-house list, no arrivals, no balances | Owner decision on the trade-off (F-H01); at minimum a written downtime procedure and a printable daily "in-house / arrivals / balances" sheet; optionally allow PIN unlock up to the absolute lifetime while offline | 3 | High |
| A09-07 | Medium | Disaster recovery | deployment/windows/New-HccGuestIdKey.ps1 (no import); INSTALL.md:97 | Backup key has `-ImportOfflineCopy`; guest-ID key has none ("ask your developer") | After PC loss, all stored ID numbers stay unreadable until a developer re-wraps the key | Add `-ImportOfflineCopy` to New-HccGuestIdKey.ps1 (same wrap as `ConvertTo-HccWrappedGuestIdKey`); document a replacement-PC drill | 2 | High |
| A09-08 | Medium | Simulated adapters in production | src/Hcc.Api/Endpoints/DocumentsEndpoints.cs:23; src/Hcc.Application/Documents/DocumentPorts.cs:53-90; src/Hcc.Api/Endpoints/MessagingEndpoints.cs:18 | `SimulatedIdOcrEngine` and `FakeMessageGateway` are registered unconditionally, also in Production. The OCR returns a fictional but checksum-valid Aadhaar ("1…") for any real image, and `ConfirmIdCapture` with `AcceptOcr` stores it | Latent today (ID capture is unmapped in the client, channels.ts:12-14), but any client wiring would store fake ID numbers as real ones; messaging marks messages as sent although nothing leaves | In Production register "not configured" adapters that refuse with a clear message; add a validator flag | 1 | High |
| A09-09 | Medium | Masters import | deployment/import-templates/* (11 templates) | No template/import for stock items, departments, companies or suppliers; no joining date (E2E2 N3, N4) | A real hotel cannot load these at go-live except via raw API calls | Add 4 templates + importers (or UI) | 8 | High |
| A09-10 | Low | Backups | deployment/windows/Backup-HccDatabase.ps1:97; Hcc-Common.ps1:92-106 | `-like "${Database}_*"` also matches `Hcc_X_…`; selection ignores the database | Two Hcc databases in one folder would share retention | Filter by the parsed `Database` | 0.5 | High |
| A09-11 | Low | Install docs | INSTALL.md §3.2, §6c, §8 | Editing a file in Program Files, copying index.html there and installing a CA into Local Machine all need elevation; INSTALL.md does not say so | Owner gets "access denied" / UAC confusion | Say so | 0.5 | High |
| A09-12 | Low | Install | installer/Hcc.iss (`DisableDirPage=auto`); scripts' defaults `C:\Program Files\HCC\…`; appsettings sample `Operations:BackupScript` | Owner may pick another folder; INSTALL commands then point at the wrong files | Wrong-folder errors | `DisableDirPage=yes` | 0.5 | High |
| A09-13 | Low | Backups | deployment/windows/Hcc-Common.ps1 (plain .bak before encryption); Restore-HccDrill.ps1 (decrypted copy in DrillScratch) | Plaintext exists briefly in the private folder; the drill needs ~2× DB size free | Low risk given the ACLs; a full disk fails the drill | Document the free-space need; accept the brief plaintext window (Express cannot encrypt during BACKUP) | 0.5 | Medium |
| A09-14 | Low | Install docs | INSTALL.md:113 vs installer/Hcc.iss:46 | `Copy-Item hcc-ops.sample.json hcc-ops.json` overwrites the installer's copy and any later edits | Owner's folder settings lost on re-run | "only if hcc-ops.json does not exist" | 0.25 | High |
| A09-15 | Low | Upgrade docs | INSTALL.md:146 | Upgrade from pre-26-Oct: `migrate` comes first and `New-HccBackupKey` later, so the encrypted pre-migration backup refuses (no key) | Upgrade stops (safely) | Put New-HccBackupKey before migrate | 0.25 | High |
| A09-16 | Low | Upgrade | installer/Hcc.iss:45-46 (onlyifdoesntexist) | New settings keys are never merged into an existing appsettings.Production.json | A future required key must be added by hand (the validator lists it) | Upgrade note per release, or a settings-merge step | 1 | High |
| A09-17 | Info | ETP premise | 08 Production/ETP_Reuse_Pending.md:3; CLAUDE.md "ETP (V:\ETP) was not moved" | `E:\ETP\Code\SaagarCC-V6-ETP-Source-and-Report-Engine-605002f` (HEAD 8d65d83f) exists | ~15 stand-ins now have an ETP counterpart to compare (§g) | Update both documents; schedule the ETP comparison | 0.5 | High |
| A09-18 | Medium | Performance | src/Hcc.Infrastructure.SqlServer/Housekeeping/SqlHousekeepingStore.cs (board query ~l.250-284) | Query Store: 545 calls, avg 76 ms, max 2,398 ms, avg 10,547 logical reads, 24 rooms, 9 months of data | The most-used screen gets slower every month; spikes of seconds already | Index for the current/next stay lookups (e.g. stays by room+status, reservations by room+arrival with a status filter) and limit `openTasks` to open tasks of today; measure with the probe | 3 | Medium |
| A09-19 | Low | Housekeeping | ops.hk_task (stayover tasks); SqlHousekeepingStore.cs:306 ordering | Stayover tasks are generated daily and never expire; 316 open after 3 months in the probe; the list is paged at 100 | Housekeepers' list fills with stale tasks; checkout cleans get pushed off the first page | Auto-cancel stayover tasks at day close (or when the stay departs) | 1 | High |
| A09-20 | Low | Performance | plat.outbox poll (Query Store); src/Hcc.Infrastructure.SqlServer/Access/SqlRetentionPurge.cs:23-25 | Poll avg 36 ms / max 913 ms, ~170k polls a day; outbox (9.8k rows in 9 months) and consumer_dedup never purged; no index on event_type | Constant background load that grows linearly | Index (event_type, outbox_id) or a per-consumer high-water mark; purge delivered events after N days | 2 | Medium |
| A09-21 | Info | Backups | Hcc-Common.ps1:217-352; Test-HccDeployment.ps1:107,133,291 | Encrypt-then-MAC AES-256-CBC + HMAC-SHA256, MAC checked before any output, DPAPI LocalMachine key with ACL, offline-copy import | F-H03 fix holds (on paper and in self-tests) | — | 0 | High |
| A09-22 | Info | Deployment tests | deployment/windows/tests/Test-HccDeployment.ps1 | 43/43 PASS in 28 s (evidence/A09/deployment-tests.txt) | Good unit coverage, but every script runs only with -WhatIf: no SQL, service, task or certificate path is exercised, which is how A09-01 escaped | Add a SQL-backed lane (A09-01 fix) | — | High |
| A09-23 | Info | DB permissions | tests/Hcc.Platform.Tests/Sql/RuntimePermissionTests.cs | Runtime grants allow reads/writes/sequences/TVPs/applocks and refuse DDL and audit-trigger disable | F-L08 holds | — | 0 | High |

## (j) Closing lists

### Claims I could not confirm
- That a full and a differential backup started in the same minute actually fail (A09-02). This is reasoning from SQL Server behaviour; `probes/A09_drill.ps1` step 4 would show it but was not run.
- The end-to-end backup → encrypt → restore drill on this PC. The probe is ready but was not run, because the lock went back to the lead at 15:44.
- API response times of the 8 heavy reads at one-year scale. The seed stopped at 274 days, before the timing phase; §e gives measured statement costs and an estimate.
- Event Log behaviour of the service (A09-05). Based on how `UseWindowsService` sets the Event Log source; the service was not installed here.
- Anything that needs elevation: `Install-HccService`, `New-HccCertificate` (LocalMachine\My thumbprint hosting — untested anywhere, including APK v12), `Register-HccBackupTasks`, and the installer (never compiled).
- Whether the production web build prints the HTML tax invoice and a folio cleanly on the hotel's printer, and whether the server APK can print at all (APK v12 "Not tested").
- E2E2 N1 ("New company" not available on the server). The lead/A2b report that `GRP_COMPANY_SAVE` is mapped (`03 Prototype/src/clients/sync/money.ts:354`), and reading that line confirms it; I did not re-test the UI flow.

### Open owner/CA decisions I hit (and the code that depends on each)
- **Recovery point**: accept losing up to 4 hours of work after a disk failure (differentials every 4 h, no log backups)? Code: `Register-HccBackupTasks.ps1:23` (`DifferentialEveryHours`) and the recovery model of the `Hcc` database (`Initialize-HccDatabase.ps1:45`).
- **Offline window** (A09-06): keep "the copy is gone after 30 min idle" (security), or allow a longer read-only window behind the PIN (continuity)? Code: `cache.ts:89-92`, `AccessOptions.IdleTimeout` (`AccessPorts.cs:10`).
- **POS on day one**: will the restaurant/room service run on HCC from day one? If yes, KOT/bill printing (ETP #25) blocks go-live. Code: `src/Hcc.Application/Pos`.
- **CA: is e-invoicing (IRN) required** (aggregate turnover above ₹5 crore)? If yes, ETP #38 blocks go-live (PERM-INV-006 reserved). Code: `SimpleInvoiceRenderer.cs`, `IInvoiceDocumentRenderer`.
- **OTAs**: keep updating the OTA extranets by hand until a channel-manager vendor is chosen (ETP #36)? Code: `OtaChannelManagerProjector.cs`, `OtaService.cs`.
- **Messaging provider** (WhatsApp/SMS/e-mail): none exists in HCC or ETP (#24, #29, #33, #35). Which one? Code: `MessagingEndpoints.cs:18`.
- **CA P17**: bank upload layout and PF ECR / ESIC / PT formats for the payroll files (#32). Code: `PayrollFiles.cs`.
- **Weekly offline copy**: who does it, and what is copied (all ~210 files, or the newest full + later diffs)? INSTALL.md:124.
- **ETP code is now on this PC** (DEC-054): compare the ~15 rows in §g before go-live, or after?

### Questions for the lead
- May V1 or the lead run `probes/A09_drill.ps1 -Database <a migrated HccTest_ DB>` under the lock (≈ 3–5 min)? It turns A09-01, A09-02 and the restore drill from paper into evidence.
- Should A09-01 be graded Critical (it means "no backups at all" in normal use, i.e. a data-loss risk) rather than High? I kept High because it fails loudly at install time and has a one-line workaround.
- The probe database seeded 274 days. A full 365-day read-timing run (`probes/A09_perf_run.ps1 -Days 365 -SeedMinutes 28`) needs ~25 min of the lock plus ~1 min of reads. Is it wanted?

## (k) Clean-up record
- Lock `locks\dotnet-test.lock` taken 15:15:19 (after A1 released it) and released 15:44:17 (`evidence\A09\cleanup.txt`). The lead asked for it back at ~15:43; the seed was stopped at 15:43:57.
- Databases created: three short smoke `HccTest_perf_*` databases and `HccTest_perf_20261026095553`. All were dropped by `probes\A09_cleanup.ps1`, which only ever touches `HccTest_perf_*` / `HccTest_drill_*` names. Remaining after clean-up: `EtpReporting`, `EtpScratch_20261020` and the system databases (`evidence\A09\cleanup.txt`). No `HccTest_drill_*` database was created. `EtpReporting`, `EtpScratch_*` and `Hcc` were not touched.
- The temporary API copy `%TEMP%\a09-perf-api` is deleted, and no API or node process is left. The probe's credentials were generated in memory and never written. `E:\HccTest_A09_drill` was never created (drill not run).
- No product file changed. Files written: this report, `evidence\A09\*`, `probes\A09_*`.
