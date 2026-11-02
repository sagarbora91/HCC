# Lodge rebuild sizing: patch, .NET port, or finish HCC

**Date:** 2 Nov 2026
**Purpose:** Give the owner concrete size facts about the old VB6 Lodge software and honest hour ranges for three choices: (i) patch Lodge for ID storage, (ii) port Lodge line by line to .NET, (iii) finish HCC instead. Each number says whether it was measured or guessed.
**Status:** read-only analysis. No source was changed or compiled. The VB6 IDE was not run on this PC.

**Sources:** `Existing Software\Forms\Forms` (`Hotel.vbp`, `*.frm`, `*.bas`, `*.cls`, `DataModi*.txt`), `DB-PROFILE\README.md`, `rowcounts_matrix.csv`, `schema_latest.csv`, `pakage for 64 bit`, `SQL SERVER 2008 R2 prasad sir`, `Updated Version` (git), the report map `HCC_Lodge_Crystal_Report_Usage_Map.md`, the ID analysis `HCC_Lodge_ID_Storage_Analysis_and_Patch_Design.md`. HCC side: line counts from `git ls-files` in `Hotel Control Centre` (2 Nov 2026) and the project state notes (1 Nov 2026: test counts, waves still to do), `HCC_Report_Catalogue_v1.md`, `HCC_Build_Plan_v2_2026-10-27.md`.

**Legend.** **[M] Measured** = counted or read from files. **[G] Guessed** = my estimate; the reason is given. Hour figures are always [G] because productivity is not measured on this team.

---

## 1. Bottom line

| Option | Hours (range) | One-line verdict |
|---|---:|---|
| **(i-a) Stage 0, no code**: put the EXE/ID folders on one server share, set folder permissions, nightly backup | 4-8 | [G] Removes the worst risks (per-PC folders, no backup, open access) without touching VB6 |
| **(i-b) ID patch, minimal** (root-path function + register + audit + migration) | 45-70 | [G] Worth doing only if Lodge will run more than about 6 months |
| **(i-c) ID patch, full** (above + guest documents screen + print path + security + test plan) | 81-141 | [G] Best Lodge-only fix; designs and scripts are drafted |
| **(ii) Line-by-line .NET port of everything compiled** | 2,500-5,300 | [G] Not recommended: it recreates a product that HCC already replaces |
| **(ii-b) Port only what the hotel actually uses** | 1,500-3,300 | [G] Still 2-3 times the cost of finishing HCC |
| **(iii) Finish HCC** (FIT, LF, APK v19, hall booking, split folio, report module, go-live, installer) | 670-1,330 | [G] about one third of (ii-b); HCC is already larger than Lodge in measured code lines |

Owner time (decisions, UAT, training) is extra: about 60-120 h for (iii), 30-60 h for (i-c), 400+ h for (ii) [G].

---

## 2. Measured sizing facts about Lodge

### 2.1 Code

| Fact | Value | Basis |
|---|---:|---|
| Forms on disk | 139 `.frm` (133 `.frx` binary companions) | [M] |
| Forms in the project `Hotel.vbp` | 124 | [M] |
| Forms reachable from the menu or from another reachable form | 113 (one of them, `frmImageCapture`, is not in the project); 26 are unreachable: 12 compiled-in dead forms and 14 not in the project | [M] reachability walk |
| Modules / classes | 8 `.bas` (7 in the project), 1 `.cls` | [M] |
| All lines in `.frm/.bas/.cls` | 259,469 | [M] |
| of which designer section (control layout) | 94,808 | [M] |
| of which code (not blank, not comment, after the designer part) | **126,732** | [M] |
| of which comments / blank | 20,505 / 16,716 | [M] |
| Code in the 124 project forms + 7 modules/class | about **113,500** (107,069 + 6,473) | [M] |
| Code in forms reachable from the menu | about 101,400 | [M] |
| Procedures | 6,755 | [M] |
| Controls declared (`Begin` lines in designers, menus included) | 5,739 | [M] |
| SQL statements embedded in code (lines with SELECT / INSERT / UPDATE / DELETE in a string) | 6,263 | [M] |
| `DataBaseType` branches (Access vs SQL Server dialect) | 769 | [M] |
| Win32 API `Declare` lines | 58 | [M] |
| Largest units (code lines) | `frmreport` 8,370; `frmSalesBill` 7,816; `Module1` 5,474; `frmRoomBooking` 5,010; `frmBillUpdation` 3,504; 9 forms above 3,000; median form 512 | [M] |
| Automated tests | none found | [M] |
| Version control | `Updated Version` has a git repo with 1 commit; `Forms\Forms` carries an old VSS `.SCC` marker | [M] |
| Secrets in source | database password and an SMS account name and password are literals in `Module1.bas` (values not copied here) | [M] |

### 2.2 Data

| Fact | Value | Basis |
|---|---:|---|
| Tables in the latest yearly database | 282 (291 distinct across years); 131 never hold a row in any year | [M] DB-PROFILE |
| Yearly databases | 10 (FY 17-18 to 26-27), Access `.mdb`, **6,455 MB uncompacted, 251 MB compacted**; the biggest, 26-27, is 1,482 MB against the 2 GB Access limit | [M] |
| Guests / bookings / bills (10 yearly files) | 103,137 guest rows (cumulative, repeated each year; 17,512 distinct in the latest) / 24,622 / 24,581 | [M] |
| Heaviest table | `BillUpdationOLDDetail` 1.16 million rows | [M] |
| Customer pictures | loose files outside the database | [M] |

### 2.3 Reports

| Fact | Value | Basis |
|---|---:|---|
| `.rpt` files / distinct names | 715 / 203 | [M] |
| Used by reachable code / carried by the HCC catalogue | 129 / 80 | [M] |
| Crystal runtime | Crystal Reports 8.5 RDC + `Crystl32.OCX`; a Crystal 9 runtime set sits in the 64-bit package | [M] |
| Data access in reports | ODBC DSN `Lodge`, scratch tables filled by VB6 before each run | [M] |

### 2.4 Third-party and platform dependencies

| Dependency | Detail | Basis |
|---|---|---|
| ActiveX controls (10) | MSCOMCTL, MSCOMCT2, MSDATGRD, MSADODC, MSFLXGRD, comdlg32, tabctl32, Crystl32, MSCOMM32 (serial), RICHTX32 | [M] `Hotel.vbp` |
| COM references (13) | ADO 2.8, DAO 3.51, Jet/Replication 2.6, Data Report Designer 6.0, Data Formatting, Data Binding, Excel 12.0, Scripting Runtime, Crystal RDC 8.5, CDO, MSXML 3, OLE Automation, stdole | [M] |
| Extra programs | `Email Sending Project` (separate VB6 EXE `EmailSend.vbp`), a `SelfWebCheckIn` folder, SMS sending through an online API (`modSMSSending.bas`) | [M] |
| Hardware code | Webcam through the 1990s Video-for-Windows API (`avicap32`); serial port (control present, use not traced) | [M] / [G] |
| Room key cards | a different product (BIS Hotel v5.7, folder `Be-Tech`); Lodge code does not drive the lock | [M] searched; HCC has its own KeyAgent spec |

### 2.5 64-bit, Windows 11 and SQL Server 2008 R2 problems

| Problem | Why it matters | Basis |
|---|---|---|
| VB6 is 32-bit only | runs under Windows' 32-bit layer (WOW64) on 64-bit Windows; every OCX must be registered from the 32-bit system folder with admin rights; the IDE is unsupported on Windows 11 though it usually starts | [M] project type; [G] IDE behaviour |
| Two Access engines mixed | main connection uses `Microsoft.Jet.OLEDB.4.0` (32-bit only); backup/convert code uses `Microsoft.ACE.OLEDB.12.0`. A 64-bit Office or a 64-bit ACE install can break the 32-bit one | [M] Module1 lines 877 and 6324; [G] conflict |
| ODBC DSN created from VB | reports need a 32-bit user DSN named `Lodge`; the app creates it at start-up | [M] |
| Crystal 8.5 | out of vendor support; its runtime needs manual redistribution (the `pakage for 64 bit` folder is one earlier attempt: a 36 MB VB6 setup cabinet carrying Crystal 9 DLLs, DAO and Office files) | [M] contents |
| Access 2 GB limit | the newest yearly file is at 1.48 GB before compaction; compaction at start-up exists in code but the profile found the files were never shrunk | [M] |
| SQL Server 2008 R2 | the SQL branch exists but live runs Access; the installer in `prasad sir` is Express x86. SQL 2008 R2 left extended support in July 2019 and may not install on current Windows | [M] files; [G] support dates and install behaviour |
| Legacy SQL provider | `SQLOLEDB.1` is the old OLE DB provider; new SQL Server versions and TLS 1.2 rules push towards newer drivers | [G] |
| Shared SQL login | SQL mode uses `sa` | [M] `DatabaseType.dll` |
| Single point of failure | one EXE folder holds code, pictures, e-mail spool; backup is a manual menu action | [M] |

---

## 3. Option (i): the ID patch

Details, scripts and tests are in `HCC_Lodge_ID_Storage_Analysis_and_Patch_Design.md`. Estimate by block:

| Block | Low h | High h | Why |
|---|---:|---:|---|
| VB6 build environment working on a Win 11 PC, compile `Hotel.vbp`, test copy of live data | 4 | 8 | 10 OCXs, Crystal 8.5; first build is the main unknown |
| Database scripts for SQL 2008 R2 **and** the Access twin; test both | 10 | 16 | drafted; install, fix, retest |
| New VB6 module: store, hash, retry, offline behaviour | 12 | 20 | draft exists, uncompiled |
| Edit 7 forms, about 14 procedures, 110 path strings | 16 | 28 | counted sites; the big save procedures are fragile |
| Guest documents list and viewer buttons | 6 | 10 | |
| Print path re-point and Crystal test | 3 | 5 | |
| Migration tool, rules, owner review | 8 | 14 | data surprises dominate |
| Security: share ACL, BitLocker, backup job, logins | 4 | 8 | |
| Tests (two engines, three PCs) | 14 | 24 | |
| Install and hand-over | 4 | 8 | |
| **Full** | **81** | **141** | |
| **Minimal** | **45** | **70** | skips the screen and the extended tests |
| **Stage 0 (no VB6 change)** | **4** | **8** | server share + permissions + nightly backup; the EXE already reads one folder if all PCs run the same EXE folder **[G]: needs the owner's answer on how PCs run it** |

**Not covered:** the same 32-bit/Crystal risks remain; the patch does not make Lodge supportable for 5 more years.

---

## 4. Option (ii): line-by-line port to .NET

**What a line-by-line port means here:** keep Lodge's screens, rules and reports, rewrite in C# WinForms (or WPF) on SQL Server, reproduce every compiled form, and rebuild the reports.

**Measured size to port:** about 113,500 code lines in 124 forms and 8 modules; 6,263 embedded SQL statements in two dialects; 129 reports; 160 tables with data; no tests (so no executable specification).

| Block | Low h | High h | Reasoning [G] |
|---|---:|---:|---|
| Forms and modules, 113,500 lines at 35-70 lines per hour all-in (read, rewrite, wire the UI, fix, test) | 1,620 | 3,240 | VB6 forms mix UI, SQL and rules in one procedure; no shared layers to reuse. 35-70 lines/h is my assumption for careful manual porting; automated converters give code that still has to be reviewed |
| Re-do the 129 used reports (3-8 h each: data query, layout, check against Crystal output) | 390 | 1,030 | the Crystal designer files cannot be converted automatically |
| Data layer: Access to SQL Server, dialect fixes in the 6,263 SQL strings and 769 branches, schema clean-up, yearly-file consolidation | 150 | 300 | partly inside the line rate above; this is the extra for design and verification |
| Data migration and reconciliation (10 yearly files, 103,137 guest rows; already prototyped by the LG wave for HCC) | 100 | 200 | reuse of HCC's importer lowers this |
| Test and acceptance (no tests exist; the hotel's daily bills, GST and day-close must match to the rupee) | 250 | 500 | parallel run of at least one month |
| Installer, printers, hardware (webcam, serial), 64-bit issues | 60 | 120 | |
| **Total, everything compiled** | **2,570** | **5,390** | about 15-30 working months for one person |
| **Total, only what the hotel uses** | **1,500** | **3,300** | drops laundry, store, restaurant purchase, HR, owner ledger, finalize, function prospectus, tanker, commission, accounts menu; I estimate 55-65 % of the code is left **[G]; not measured per form** |

An AI-assisted port could lower the writing hours but not the verification hours; I would not plan below 60 % of these figures. The result would still be a desktop program with no phone app, no web access, no role matrix, no audit trail, no GST workings and no tests, which HCC already has.

---

## 5. Option (iii): finish HCC

### 5.1 What HCC already is (measured, 2 Nov 2026, `git ls-files`)

| Part | Files | Lines |
|---|---:|---:|
| Web app (React/TypeScript prototype that is also production UI) `03 Prototype/src` | 470 | 50,514 |
| .NET server `08 Production/src` | 491 | 89,495 |
| SQL migrations `08 Production/database` | 133 | 7,338 |
| Server tests `08 Production/tests` | 324 | 52,974 |
| Prototype tests `03 Prototype/tests` | 191 | 26,243 |
| **Total** | **1,609** | **226,564** |

Test counts on main at the 1 Nov 2026 hand-over: 2,176 prototype + 3,697 server [from the project state notes; not re-run for this report]. Report catalogue: 130 entries, of which 62 built, 13 partial, 7 ACC-3, 48 planned [M, catalogue section 6]. Lodge's customer history (10 FYs) is already imported in a dry run with every FY matching [project notes].

So HCC is already about 1.8 times Lodge's code lines including tests (1.3 times without tests: 147,347 against 126,732) and carries about 5,900 automated tests that Lodge lacks.

### 5.2 What is left (from the project's own list) and my estimate

| Block | Low h | High h | Reasoning [G] |
|---|---:|---:|---|
| W2a FIT: fit-to-screen layout on desktop and tablet | 40 | 80 | phase A done; layout rules decided |
| W2b LF: legacy parity items and the second documents database | 60 | 120 | planned on its own branch |
| APK v19 build, emulator tests, tablet run | 20 | 40 | |
| Hall booking module | 80 | 150 | new module; design exists |
| Split folio | 40 | 80 | touches bill and tax rules; high test cost |
| Report module (W7): engine (OpenXML + PDFsharp) 80-160 h; remaining 61 reports (48 planned + 13 partial) at 4-8 h = 250-500 h | 330 | 660 | the engine covers every report; each report is a query plus a template |
| Go-live: data load from Lodge, cut-over, parallel run, training (training pipeline exists) | 60 | 120 | owner sessions on top |
| Installer pipeline (server PC, backup, update) | 40 | 80 | |
| **Total (human-equivalent hours)** | **670** | **1,330** | about 17-33 working weeks for one person |

The project's own plan (27 Oct 2026) said "about three to four working weeks of agent time if nothing waits on a decision" for waves W0-W9; several of those waves have since landed, so 4-8 weeks of calendar time for what is left is a fair guess **[G]**, with the owner's decisions and a physical-device test as the usual blockers.

### 5.3 Risks
* HCC does not run the hotel yet; Lodge keeps running meanwhile (that is the reason for any patch).
* Report parity: 57 catalogue entries are still planned; the day-end and registration-card layouts need owner approval (see the report map, section 5.3).
* Legal items already open: ID retention and Aadhaar storage (DEC-068, DEC-071).

---

## 6. How to choose

| If the owner wants ... | Then |
|---|---|
| ID pictures safe this month | Stage 0 now (4-8 h), decide on the patch after the answers to the questions below |
| Lodge to keep running for more than about 6 months | Do (i-c) full patch; keep the design table-compatible with HCC (done) |
| HCC live within about 3 months | Do Stage 0 and at most the minimal patch; finish HCC |
| A new .NET Lodge | Not recommended; compare 1,500-3,300 h to HCC's remaining 670-1,330 h |

---

## 7. Open questions

1. How do the reception PCs run Lodge (own EXE copy each, or one EXE on a server share)? This decides Stage 0.
2. Is the target date for HCC go-live fixed? That decides whether the patch is minimal or full.
3. Can the live IDProof/Photo folders and one live yearly file be copied for the migration test?
4. Is anyone able to run a 30-day report-use log (report map, section 8) so the "used" list rests on facts?
5. Will the owner accept the 2 GB Access limit risk for FY 26-27 (1.48 GB uncompacted), or should compaction be run now?
