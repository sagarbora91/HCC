# Lodge ID storage: analysis and patch design

**Date:** 2 Nov 2026
**Purpose:** Find out exactly how the old VB6 "Lodge" software stores guest ID proofs and photos, then design a small, reversible patch (new SQL Server 2008 R2 tables, VB6 changes, one-off migration, rollback, security and tests) so the ID store stops depending on a single PC folder. Also fix the table shape so HCC can take Lodge's ID data later without rework.
**Status:** analysis is read-only. Nothing was changed in the originals. Scripts were written but **not executed anywhere** and not syntax-checked on a server.

**Sources read (all under `E:\Projects\HCC\Existing Software`):** `Forms\Forms` (139 `.frm`, 8 `.bas`, 1 `.cls`, `Hotel.vbp`, `DataModi.txt` = the vendor's change log of database edits, `DatabaseType.dll` = a text file), `DB-PROFILE\README.md` and `schema_latest.csv`, `LG-EXTRACT\20261101-0531\report.md`, `Lodge 22-6-26-org` (live-install copy), `SQL SERVER 2008 R2 prasad sir`, `pakage for 64 bit`, the `Menu*.jpeg` screenshots. HCC side: `Hotel Control Centre\08 Production\database\migrations\2100_documents.sql` and `4605_legacy_capture_kind.sql`, `02 Planning\HCC_Decision_Register.md` (DEC-068, DEC-071, DEC-186).

**Legend.** **[M] Measured** = counted or read directly from the files named. **[G] Guessed** = my estimate or inference; a reason is given. If a sentence has no tag it is a design choice, not a fact.

---

## 1. Bottom line

1. **[M] Lodge stores ID proofs and photos as loose JPEG files in two folders next to the EXE (`App.Path\IDProof\`, `App.Path\Photo\`). The database holds only the file name** in `CustomerInfo.IDProofPath` and `CustomerInfo.Photpath` (a bare name such as `1234.jpg`, or `-` for none). There is no path, no size, no hash, no date, no user, no audit.
2. **[M] The binary (OLE) columns exist but are empty.** `RoomBooking.CPhoto/ITProff`, `CheckInReport.CustomerPhoto/IDProff`, `PhotoAddresProof.CustomerPhoto/IDProff` are NULL in every year (DB-PROFILE README section 3). The newer table `CustomerIdProofDetail` (several IDs per guest) and `TransactionDataPhotoDetail` (per stay) are **empty in every year**. So the real data is: 524 photo names and 5,053 ID-proof names in `CustomerInfo` (README), plus whatever files sit on the live PC.
3. **[M] The live install runs in Access mode, not SQL Server.** `DatabaseType.dll` line 1 is `acc` (lines 2-3 hold a server name and the `sa` login, unused in this mode). Lodge's code does have a SQL Server branch (`SQLOLEDB.1`, 769 `DataBaseType` checks), and a SQL Express 2008 R2 x86 installer sits in `SQL SERVER 2008 R2 prasad sir`, but the customer data is in yearly `.mdb` files. **The T-SQL scripts here apply only if the site moves to SQL mode; an Access equivalent is supplied** (`lodge-id-patch\access\20_jet_ddl_LodgeIdDocs.sql`).
4. **[M] The live ID folder was not in the material supplied.** The `IDProof` and `Photo` folders in `Lodge 22-6-26-org` are empty. Only dev samples exist (38 + 10 files in `Forms\Forms`, 19 + 74 in `SupportFile`). The DB profile README says the same: "collect them separately". **Open question 1: copy the live folders from the Hotel Parth PC.**
5. **Recommended patch:** keep files on disk (do not put images back into Access/SQL), move them to one protected server folder, and add a small permanent database `LodgeIdDocs` (register + audit). Total effort **[G] 80-140 h full, 45-70 h minimal** (section 12).

---

## 2. What is stored where (today)

| Thing | Where it is | Detail | Basis |
|---|---|---|---|
| ID proof image (1 per guest) | Disk: `<EXE folder>\IDProof\<GuestID>.jpg` | overwritten on every re-upload, so **no history** | [M] frmCustomerMaster.SaveRecord, frmRoomBooking.cmdSave_Click |
| Extra ID images (srno 2, 3 ...) | Disk: `IDProof\<GuestID>-<srno>.jpg`, or `IDProof\<srno>.jpg` if the guest had no id yet | made on the "multiple photo and ID proof" screen | [M] frmMultipalePhotoAndIDProff.cmdAddIDProff_Click lines 731-737 |
| Stay copy of the above | Disk: `IDProof\<TransactionNo>-<name>.jpg` (and `Photo\...`) | made when a stay is saved with the multiple screen | [M] frmRoomBooking.StoreMultipalePhoto lines 9511-9517 |
| Guest photo | Disk: `Photo\<GuestID>.jpg` | webcam or file | [M] same procedures |
| The name of the file | Access/SQL: `CustomerInfo.IDProofPath`, `CustomerInfo.Photpath` (Text 50) | `-` means none. Copied into every new yearly file (GuestID carried forward) | [M] schema_latest.csv |
| List of extra documents | Access/SQL: `CustomerIdProofDetail(GuestID, SrNo, BookingId, Type, Path, CarryImage)` and `TransactionDataPhotoDetail(TransactionID, srno, Type, Path)` | `Type` is `Photo` or `IDProof`; **0 rows in all ten years** | [M] DB-PROFILE rowcounts |
| What type of ID | `CustomerInfo.IDCode` -> `IdentityType`, `IDNumber` (Text 15) and the same on `RoomBooking` | the number itself is stored in the database in clear text | [M] schema; HCC DEC-068 records the same risk |
| Picture inside the report | Binary columns `CheckInReport.CustomerPhoto/IDProff/IDProof2ndPart`, `PhotoAddresProof.PhotoAndID` | filled **at print time** from the disk file (`AppendChunk`) so Crystal can print it, then the row stays (1 row per table: it is a scratch table) | [M] frmSalesBillReport.filldata lines 616-682, filldata_Work 714-1075; frmRoomBooking.filldata 9413-9465 |
| Database password and connection | Module1.bas line ~944 (not printed here) | not part of this task | [M] |
| Backup of the pictures | Only when someone runs Administrator > Back Up: `CopyFiles App.Path\Photo` and `\IDProof` to the chosen folder | no scheduled backup | [M] frmBackup.cmdok_Click lines 333-334 |

**Where the folder lives. [M]** Always `App.Path` (the folder the EXE runs from), never the data folder `SoftwareLanPath` that holds the `.mdb` (Module1.DatabaseLanPath defaults `SoftwareLanPath` to `App.Path` only when its text file is empty). **[G]** If the hotel runs the EXE from each reception PC against a shared `.mdb`, every PC has its own `IDProof` folder and a guest photographed on PC A has no picture on PC B. The supplied copy cannot prove which setup the live site uses; ask (open question 2).

**Capture methods. [M]** (a) File picker (`CommonDialog` filter bmp, ico, jpg, jpeg, gif) then `FileCopy` to the fixed staging name `CustomerIDProof.jpg` / `CustomerPhoto.jpg`. (b) Webcam through `avicap32` (`WebCamp.bas`, `capCreateCaptureWindowA`) saved by `SAVEJPEG` (`modJPG.bas`, GDI) as `Photo\CustomerPhoto.jpg`; used for **photos only**. The ID proof is always a file (so an ID scanner works only if its software saves a file first). **No TWAIN or WIA code exists** (searched all forms). Webcam use is switched by a SoftwareSetting row "WebCamera Y/N" (DataModi.txt line 167).

**How a picture is shown back. [M]** `LoadPicture(App.Path & "\Photo\" & txtPath)` into a picture box for the photo; for the ID proof a **View** button (`cmdView_Click`, frmRoomBooking line 4347 and frmCustomerMaster line 2225) calls `ShellExecute` on the file, so it opens in whatever image viewer Windows has. The multiple screen does the same (`cmdIDProffView_Click`). Printing: see the Crystal row above.

---

## 3. Evidence from the files

| Fact | Value | Basis |
|---|---|---|
| Guests with a photo name / ID-proof name (names, not files) | 524 / 5,053 (README: bare file names such as `1234.jpg`) | [M] DB-PROFILE README line 280 |
| `CustomerIdProofDetail` rows, all years | 0 | [M] rowcounts_matrix.csv |
| OLE image columns that hold data | none | [M] README line 280 |
| Dev sample `IDProof`: 37 jpg | 31 are the **same 17,186-byte placeholder picture**; 3 are 0 bytes; 3 real (max 178 KB) | [M] hashed |
| Sample `SupportFile\IDProof` (17 jpg) / `SupportFile\Photo` (72 jpg) | 1 of 17 and **43 of 72 are 0 bytes**; real files average 336 KB (ID) / 225 KB (photo) | [M] |
| Likely real size of the live ID folder | 5,053 x about 300 KB = about 1.5 GB | [G] from the sample average; could be 0.5-3 GB |
| Largest yearly database | Lodge26-27.mdb 1,482 MB uncompacted (Access hard cap 2 GB) | [M] README table 1 |

**Why so many 0-byte files [G].** `cmdGetImage_Click` wraps the webcam save in `On Error Resume Next` (frmRoomBooking line 4208, frmMultipalePhotoAndIDProff line 1132), so a failed `SAVEJPEG` is silent and the later `FileCopy` copies an empty file. 60 % empty photos in the sample fits that.

### Bugs and risks found in the current ID code (all [M] unless tagged)

1. **No history.** A re-upload overwrites `<GuestID>.jpg`. The old image is gone.
2. **Shared staging file.** Every PC writes to `CustomerIDProof.jpg` and then copies it. Two receptionists at once on a shared EXE folder can save each other's picture onto the wrong guest.
3. **Stale variable.** frmRoomBooking.cmdSave_Click lines 5244-5257: when no ID is attached, the code sets `Photpath = "-"` (wrong variable) and then writes `IDProofPath = idProof`, which can still hold the previous guest's name.
4. **`cmdRefereshIDProof_Click`** (frmCustomerMaster lines 2102-2120) rebuilds `CustomerIdProofDetail` from `CustomerInfo.Photpath` (the **photo** column) and labels it `IDProof`. It is an admin button; do not run it.
5. **Path trick surface.** The path is built from DB text with no validation; a crafted `IDProofPath` could point outside the folder. Low risk (desk users cannot edit it) but the new module validates.
6. **No access control or audit.** Any Windows user who can open the EXE folder can copy every ID. No record of who opened an ID. `frmBackup` copies the folders in clear to any chosen drive.
7. **Hard-coded SMS account name and password in Module1.bas lines 294-295** (values not repeated here). Unrelated to IDs but the same hygiene problem; rotate before any source is shared.

---

## 4. Design of the patch

### 4.1 Principles

1. **Files stay files**, in one protected server folder (UNC), not inside Access or SQL. Reasons: Access has a 2 GB cap and already bloats (96 % dead space measured by the profile); SQL 2008 R2 Express caps a database at 10 GB **[G: from memory of the edition limits]**; images through ADO `AppendChunk` are what made the old OLE columns unusable.
2. **A small permanent database `LodgeIdDocs`** holds the register (one row per file) and an append-only audit. It is separate from the per-year Lodge database because Lodge makes a new database each financial year while a guest's ID outlives the year. Key = `GuestID` (carried forward across all yearly files, README section 2) plus optional `FYCode` + `BookingID` for a stay.
3. **Never overwrite, never hard-delete.** A new upload makes the old row `IsCurrent = 0`; delete sets `IsDeleted = 1`; files are only removed by a manual purge outside the app (HCC DEC-071 rule: keep indefinitely, manual purge only).
4. **Switch-off safe.** If `IdDocs.cfg` is absent the new module is off and every form behaves as today. The old columns (`IDProofPath`, `Photpath`) keep being written during the first weeks so a rollback loses nothing.
5. **2008 R2 syntax only** (no THROW, IIF, FORMAT, CONCAT, SEQUENCE, OFFSET, DROP IF EXISTS).

### 4.2 Tables (script `01_create_database_and_tables.sql`)

| Table | Purpose | Key columns |
|---|---|---|
| `IdDocType` | document types; mirrors Lodge `IdentityType` and HCC `doc_kind` | `DocTypeCode` (AADHAAR, DL, VOTER, PAN, PASSPORT, VISA, OFFICE-ID, OTHER, LEGACY, PHOTO), `LegacyIdentityCode`, `HccDocKind` |
| `IdDocument` | **the register**, one row per stored file | `IdDocumentId`, `GuestID`, `FYCode`, `BookingID`, `TransactionID`, `DocTypeCode`, `SrNo`, `SideNo`, `FolderRel`, `FileName`, `OriginalName`, `FileExt`, `SizeBytes`, `Sha256Hex`, `CapturedVia` (WEBCAM, FILE, SCAN, MIGRATED), `CapturedAt`, `CapturedBy`, `Workstation`, `IsCurrent`, `IsDeleted` + `DeletedAt/By/Reason`, `QualityFlag`, `MigratedFrom`, `RowVer` |
| `IdDocumentAudit` | append-only | `Action` (ADD, REPLACE, VIEW, PRINT, DELETE, RESTORE, MIGRATE, EXPORT, DENIED, ERROR), `ActionAt`, `ActionBy`, `Workstation`, `Detail` (never an ID number) |
| `IdDocSetting` | `RootPath` (UNC), `MaxBytes` (8 MB), `AllowedExt`, `Retention` (KEEP), `FolderFan` (1000) | |

Other points: a filtered unique index allows only one current, undeleted row per `GuestID + DocTypeCode + SrNo + SideNo`; `FolderRel` is always relative so the whole store can be moved or re-pointed by changing one setting; hash is SHA-256 hex.

**Folder and file naming (new files):** `<RootPath>\<GuestID div 1000>\<GuestID>\<GuestID>-<TYPE>-<sr>-<version>.<ext>`, for example `14\14803\14803-AADHAAR-1-2.jpg` (a made-up example). A new upload gets the next version number, so nothing is overwritten.

### 4.3 Security objects (script `02_triggers_and_security.sql`)
`INSTEAD OF DELETE` trigger blocks hard delete; an `AFTER UPDATE` trigger makes guest, folder, file name, hash, size, captured at/by write-once; the audit table is append-only; three roles (`LodgeIdViewer`, `LodgeIdEditor`, `LodgeIdAdmin`) have **no table rights at all** (explicit DENY) and only `EXECUTE` on procedures. Logins are created by the DBA, not in the script (no passwords in scripts). Lodge today uses the shared `sa` login in SQL mode: the patch needs its own login (open question 5).

### 4.4 Procedures (script `03_procedures.sql`)
`usp_IdDocument_Add` (validates size, extension, relative path; replaces the current row and writes ADD or REPLACE audit in one transaction), `usp_IdDocument_List`, `usp_IdDocument_Open` (returns the file location and writes a VIEW audit row), `usp_IdDocument_LogPrint`, `usp_IdDocument_SoftDelete` (reason of 5+ characters, Admin only), `usp_IdDocument_Restore`.

### 4.5 Write order (so no file exists without a row)
1. VB6 copies the picked file to `<name>.part` in the store, checks the length, hashes it.
2. Renames `.part` to the final name (a half-written file never has the final name).
3. Calls `usp_IdDocument_Add`. If that fails VB6 deletes the new file and shows the reason.
4. The monthly `Verify` run (`tools\scan_idproof_folder.ps1 -Mode Verify`) finds the opposite case: a row with no file, a changed file (hash), or a stray file.

---

## 5. Files delivered (all under `E:\Projects\HCC\02 Planning\lodge-id-patch\`, none executed)

| File | What it is |
|---|---|
| `01_create_database_and_tables.sql` | database, tables, seed types and settings |
| `02_triggers_and_security.sql` | triggers, roles, permissions |
| `03_procedures.sql` | stored procedures |
| `04_migration_staging_and_match.sql` | staging, name classification, matching rules, exception reports, register step (last step commented out until the owner approves) |
| `05_verify_and_tests.sql` | database tests T-DB-01 to 07 (run on a copy) and monthly reconciliation queries |
| `99_rollback.sql` | export of post-go-live rows, then removal of procedures, triggers and staging; keeps the register and audit |
| `access\20_jet_ddl_LodgeIdDocs.sql` | the same tables for Access mode (the live site today) |
| `vb6\modIdDocs.bas` | **draft** new VB6 module: open, save, find, view, soft delete, hash (CryptoAPI SHA-256), per-PC temp name. Not compiled |
| `tools\scan_idproof_folder.ps1` | read-only scan, safe copy (dry run unless `-Commit`), verify |

---

## 6. VB6 code changes (exact places)

Add `modIdDocs.bas` to `Hotel.vbp` and call `IdDocsOpen` once in `Module1.Main` after `Call DatabaseConnection`. Then, by form (line numbers are from the supplied source; all forms below are in `Hotel.vbp`):

| Form | Procedure(s) | Change |
|---|---|---|
| **Module1.bas** | `Main` (line ~909-950) | `Call IdDocsOpen` after the database connects |
| **frmCustomerMaster** | `cmdItProof_Click` (2072-2098), `cmdSelect_Click` (2150-2175, photo), `cmdGetImage_Click` (3805-3814, webcam) | stage to `TempWork("ID")` / `TempWork("PH")` instead of `App.Path\IDProof\CustomerIDProof.jpg` and `App.Path\Photo\CustomerPhoto.jpg` |
| | `SaveRecord` (3244-3329) | after the existing `rssave.Update`, when `IdDocsEnabled`, call `IdDocSave(GuestID, 0, 0, <type from txtIDType.Tag>, 1, TempWork("ID"), "FILE")` and the same for the photo with type `PHOTO`; keep the old `FileCopy` and `IDProofPath` writes until the switch-over is signed off. Fix the stale variable (item 3 above) at the same time |
| | `display` (2876-2896), `cmdView_Click` (2225-2228) | resolve the picture through `IdDocPath(GuestID, type, 1, "CHECKIN")`; fall back to the old path while the guest has no register row |
| | `cmdRefereshIDProof_Click` (2102-2120) | disable the button (item 4 above) |
| **frmRoomBooking** | `cmdItProof_Click` (4570-4582), `cmdSelect_Click` (6100-6140), `cmdGetImage_Click` (4200-4230) | same staging change |
| | `cmdSave_Click` (4908-5949, the three save branches at 5231-5260, 5655-5675, 5931-5950) | same hook as `SaveRecord`, pass the new `BookingID` and `TransactionID` so the stay is linked; fix stale variable at 5244-5257 |
| | `msfgAdvance_DblClick` (7126-7183), `cmdView_Click` (4347-4350) | resolve through `IdDocPath` / `IdDocShow` |
| | `filldata` (9319-9465) and `StoreMultipalePhoto` (9492-9517) | `Open App.Path & "\IDProof\" & ...` becomes `Open IdDocPath(..., "PRINT")`; the TransactionNo-prefixed copy is no longer needed (the register links stay and file) |
| **frmAdvanceBooking** | `cmdSave_Click` (2022-2044), `lstcustomer_DblClick` (2490-2615), `msfgAdvance_DblClick` (2927-2977), `cmdSelect_Click` (4799), `cmdGetImage_Click` (4672) | same pattern |
| **frmMultipalePhotoAndIDProff** | `cmdAddIDProff_Click` (704-737), `cmdAddPhoto_Click` (767-798), `cmdItProof_Click` (843), `cmdSelect_Click` (906), `cmdGetImage_Click` (1129), `displayPhoto` (1017), `displayIDProof` (1068), `cmdIDProffView_Click` (923), `cmdPhotoView_Click` (930) | save through `IdDocSave` with the real `SrNo`; the unsafe `<srno>.jpg` naming disappears; list from `usp_IdDocument_List` |
| **frmSalesBill** | `cmdEditCheckin_Click` (8539-8613) | `LoadPicture(App.Path & "\Photo\" ...)` and the IDProof path assignments become `IdDocPath` calls |
| **frmSalesBillReport** | `filldata` (616-682), `filldata_Work` (714-1075; 49 sites) | the `Open ... For Binary` source path comes from `IdDocPath(..., "PRINT")`; this is the bill / registration card / PhotoAddressProof printing |
| **frmBackup** | `cmdok_Click` (333-334) | stop copying `IDProof` and `Photo` to the chosen folder; show a message that the ID store is backed up by the server job (section 9) |

Measured size of the edit: **46** `App.Path & "\IDProof` strings and **64** `"\Photo` strings across 7 compiled forms (counted), about 14 procedures to rewrite, plus the new module and one small "documents of this guest" list on the check-in screen. Forms **not** in `Hotel.vbp` (frmFunctionProspectus123, frmTempCustomerInfo and others) are dead code and are left alone.

---

## 7. One-off migration of the existing files

1. **Collect.** Copy the live `IDProof` and `Photo` folders (read-only copy) to a work PC, plus an export of `CustomerInfo` columns **only** `GuestID, IDProofPath, Photpath, IDCode, deleteflag` for each yearly file. No names, phones or ID numbers are exported (not needed to match).
2. **Scan.** `scan_idproof_folder.ps1 -Mode Scan` writes `scan.csv` with name, size, date and SHA-256.
3. **Classify and match** (script 04). Rules, taken from the file names the code actually generates:

| Pattern | Reading | Result |
|---|---|---|
| `<GuestID>.jpg` | the guest's one ID/photo | MATCHED if some year's `CustomerInfo` points at that file name; NO-GUEST if the guest exists but nothing points at it; NOT-IN-DB otherwise |
| `<GuestID>-<n>.jpg`, n 1-20 | extra document of that guest | MATCHED if the guest exists |
| `<A>-<B>.jpg` where A is not a guest and B is | `<TransactionNo>-<GuestID>` | AMBIGUOUS (owner confirms) |
| `<A>-<B>-<C>.jpg` | `<TransactionNo>-<GuestID>-<srno>` | MATCHED if B is a guest |
| `<n>.jpg` only, anything else | saved before the guest had an id | AMBIGUOUS (never auto-matched) |
| `CustomerIDProof.jpg`, `CustomerPhoto.jpg`, `CustomerMultipale*.jpg` | per-PC work files | WORKFILE, ignored |
| 0 bytes | failed copy | EMPTY, not migrated, listed |
| same hash on more than 5 guests | stock picture (the 17,186-byte file) | PLACEHOLDER, not migrated, listed |

   Document type comes from the guest's `IDCode` (needs the owner to say which Lodge code is which type, open question 4); unknown becomes `LEGACY`.
4. **Reports to the owner** (script 04 section 5): (a) summary by folder and status; (b) **names in the database with no file** (the expected big list, since 5,053 names will exceed the files collected); (c) **files nothing points at** and the ambiguous ones.
5. **Copy.** `-Mode Copy` (dry run first, then `-Commit`) copies, never moves; re-hashes the copy; writes `copy-result.csv`. Old folders stay read-only for 90 days after sign-off.
6. **Register.** Load `copy-result.csv`, run the commented INSERT (script 04 section 6) so only verified copies get a row, `CapturedVia = 'MIGRATED'`, `MigratedFrom` = old relative path, original date as `CapturedAt`.
7. **Reconcile:** counts per status equal the scan; `Verify` run reports 0 missing, 0 changed, 0 stray; owner samples 20 guests on screen.

---

## 8. Security, backup and privacy (DPDP-style care)

ID images are sensitive personal data (Aadhaar, passport, DL). HCC already carries the risk in writing: DEC-068 (full ID numbers kept, owner against advice, lawyer to confirm Aadhaar Act position) and DEC-071 (keep ID images indefinitely, manual purge only, DPDP Act 2023 expects erasure once the purpose is served, lawyer to confirm). The patch follows DEC-071 and makes the risk smaller without changing the owner's rulings.

| Control | Design | Basis |
|---|---|---|
| Where | One folder on the server PC, shared as a hidden share; **no** copy in the EXE folder or on reception PCs | design |
| Who can open | NTFS: only the Lodge service/user group and Administrators; reception users get **no direct file access**, only through the app | design |
| Encryption at rest | BitLocker on the server data volume (Windows Pro, minimum), or EFS on the folder. App-level encryption (HCC seals each image with AES-256-GCM) is **not** added in the patch, because Crystal and the viewer need plain files | design |
| Access record | VIEW / PRINT / ADD / REPLACE / DELETE audit with user and PC; owner's monthly query R4/R5 in script 05 | design |
| Who may delete | Admin role only, with a reason; soft delete, file kept | design |
| Backup | Nightly copy of the store and `LodgeIdDocs` to an encrypted external disk kept off site; keep 30 daily + 12 monthly; restore test once a quarter. `robocopy /MIR` is **not** acceptable (it mirrors deletions); use `/E` without `/PURGE` | design |
| Retention | KEEP (DEC-071); keep the manual purge as an Owner-only job that writes an audit row | HCC register |
| Hard-coded secrets | Rotate the SMS account in Module1.bas; give the ID database its own login, not `sa` | [M] source |
| Export / print | Each print of an ID inside a bill or card is logged (PRINT). No bulk export button is added | design |
| Staff leaving | Disable their Lodge user; ID access is by Lodge login | design |
| Lawyer | Ask the lawyer (already needed for DEC-068 / DEC-071) to confirm: retention period, purpose notice on the check-in form, breach procedure | open question 6 |

---

## 9. Rollback

* **Switch off:** delete or rename `IdDocs.cfg` on a PC. `IdDocsEnabled` becomes False and the forms use the old code path. Old columns were kept up to date during the overlap, so nothing is lost.
* **Old build:** reinstall the previous EXE; the old `IDProof` and `Photo` folders were copied, not moved, so they still exist (read-only for 90 days).
* **Files added only after go-live** exist only in the new store: script `99_rollback.sql` part 99a lists them; `tools\scan_idproof_folder.ps1` can copy them back under the old names `GuestID.jpg`.
* **Database objects:** script 99b (refuses to run while post-go-live rows exist unless `@Force = 1`) drops procedures, triggers and staging. The register and audit tables are intentionally kept; dropping them is a separate owner decision after an offline `.bak`.

---

## 10. Test plan

| Id | Test | Pass when |
|---|---|---|
| T-DB-01..07 | Script 05 on a copy: add, replace, hard delete refused, write-once, validation, view audit, soft delete and restore | all print PASS |
| T-DB-08 | Access mode: the same add/replace/delete flows through `modIdDocs` against `LodgeIdDocs.mdb` | same results; audit rows present |
| T-FN-01 | New guest, attach Aadhaar by file picker, save | file in the store, row in register, old `IDProofPath` still written |
| T-FN-02 | Same guest, attach a new picture | old row `IsCurrent = 0`, old file still on disk, new version number |
| T-FN-03 | Webcam photo with the camera unplugged | clear message, **no** 0-byte file, no row |
| T-FN-04 | File of 0 bytes, 20 MB file, `.exe` renamed `.jpg` header test, path with `..` | each refused with a reason |
| T-FN-05 | Two reception PCs save two different guests at the same second | each guest gets their own picture (no shared staging file) |
| T-FN-06 | Check-in, then print registration card and bill with ID and photo | pictures appear; PRINT audit rows written |
| T-FN-07 | Open an old guest whose files were migrated, and one with no file | picture shows / "No ID picture on file" |
| T-FN-08 | Store folder unreachable (server off) | check-in still works, message says ID store off, no crash; saving without ID allowed |
| T-FN-09 | `IdDocs.cfg` removed | behaves as today (regression: full check-in, bill, backup) |
| T-FN-10 | Backup menu | no ID files copied; message shown |
| T-SEC-01 | Reception Windows user opens the share in Explorer | access denied |
| T-SEC-02 | Lodge login runs `SELECT * FROM IdDocument` in SSMS | permission denied |
| T-SEC-03 | Viewer role tries soft delete | permission denied |
| T-MIG-01 | Scan then Copy dry run then Commit on 200 sample files | 0 hash mismatches; reports non-empty; originals untouched |
| T-MIG-02 | `Verify` after break-in test (rename a file, edit a file, drop a stray) | MISSING, HASH, STRAY each reported |
| T-REG-01 | Full Lodge regression on a copy of the live DB: guest master, check-in, advance booking, bill, day close, backup | no new errors |

---

## 11. How HCC should store IDs later (same shape)

HCC already has `doc.id_capture` (guest, stay, `doc_kind`, content type, size, status Captured/Confirmed/Rejected/Purged, captured date/by) with the image sealed in `doc.id_capture_image`, and migration 4605 added `doc_kind = 'legacy'` for imported old pictures (DEC-186). The Lodge table is deliberately shaped to import into it:

| Lodge `IdDocument` | HCC `doc.id_capture` |
|---|---|
| `GuestID` | `legacy_guest_id` on the guest (DEC-193 party ids), then `guest_id` |
| `DocTypeCode` -> `IdDocType.HccDocKind` | `doc_kind` (`aadhaar, passport, dl, voter, pan, other`; unknown = `legacy`) |
| `FileExt` -> content type | `content_type` (jpeg, png, webp; **pdf and bmp/gif need converting** because HCC allows only image/jpeg, png, webp) |
| `SizeBytes` | `size_bytes` (HCC cap 8 MB, same as the patch) |
| `CapturedAt`, `CapturedBy` | `captured_date`, `captured_utc`, `captured_by` (user id must be mapped) |
| `IsDeleted` | `status = 'Purged'` (do not import deleted rows' images) |
| `Sha256Hex` | integrity check before sealing; not stored by HCC today |
| `FYCode + BookingID` | `stay_id` through the legacy stay map |
| file | read from the store, seal with the guest-ID key, write to `doc.id_capture_image` |

So the Lodge patch and the HCC importer share one reconciliation key (GuestID) and one type list. If Lodge keeps running for a while, HCC's legacy importer can read `IdDocument` instead of the loose folders, which gives it dates and hashes for free.

---

## 12. Effort for the ID patch

**[G]** All hours are estimates. I have not compiled VB6 on this PC; the VB6 IDE, the old OCXs and Crystal 8.5 must be installed to build (folder `VB 6new` is the installer set). First-time build risk is included.

| Block | Low h | High h | Reason |
|---|---:|---:|---|
| VB6 environment on a Win 11 work PC, open and compile `Hotel.vbp` clean, make a test copy of a live DB | 4 | 8 | 10 OCX/DLL references, Crystal 8.5, 32-bit |
| Database: tables, triggers, procedures, Access twin, test on SQL 2008 R2 and Access | 10 | 16 | scripts are drafted; mostly install, fix, test two engines |
| `modIdDocs` finish and unit-test (hash, store, retry, offline) | 12 | 20 | draft exists; CryptoAPI and network-path edge cases |
| Edit 7 forms, about 14 procedures, 110 path strings | 16 | 28 | rewriting plus the big `SaveRecord` and `cmdSave_Click` branches |
| "Documents of this guest" list screen and viewer buttons | 6 | 10 | one small form |
| Print path (frmSalesBillReport, 60 sites) re-point and test with Crystal | 3 | 5 | only the file source changes |
| Migration tool, matching, reports, owner review rounds | 8 | 14 | scripts drafted; time is mostly data surprises |
| Security set-up (share ACL, BitLocker, backup job, logins) | 4 | 8 | |
| Test plan section 10 | 14 | 24 | two engines, three PCs |
| Packaging, install on site, hand-over | 4 | 8 | |
| **Total (full patch)** | **81** | **141** | about 2-3.5 working weeks for one person |
| **Minimal variant** (only: root-path function + register hook after save + migration + audit; no new list screen, keep old viewers) | **45** | **70** | |

---

## 13. Open owner questions

1. Can the live `IDProof` and `Photo` folders (and the live Lodge exe folder) be copied from the Hotel Parth PC? Their size and count decide the migration timing.
2. Does each reception PC run its own copy of the EXE, or do all run one EXE from a server share? (decides whether pictures are split across PCs today)
3. Access or SQL Server: confirm the live site is in Access mode (`DatabaseType.dll` says `acc`). If SQL Server is wanted, that is a separate project (the SQL branch is untested).
4. Which Lodge `IdentityType` code is Aadhaar, DL, voter, PAN, passport (for the type mapping)?
5. Who is the server PC and who may hold the `LodgeIdAdmin` role (suggest owner only)?
6. Lawyer questions already open under DEC-068 / DEC-071: retention length, consent notice at check-in, breach procedure.
7. Is a "minimal" patch enough, or is the guest-documents screen wanted?
