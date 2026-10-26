# A11 — Documentation and decisions vs code

Auditor: A11 (Sonnet), 26 Oct 2026, branch `claude/audit-fable-2026-10-26`. Read-only on the repository; no test suite and no build was run.
Node: `E:\Tools\nodejs` first on PATH, `node -v` = v25.9.0 (`evidence/A11/node-version.txt`).

**Documents audited (11):** `HANDOFF.md` (344 lines, includes the APK v12 paragraph from `c0c477d`), `CLAUDE.md`, `MIGRATION.md`, `08 Production/README.md`, `03 Prototype/README.md`, `03 Prototype/src/clients/sync/README.md`, `04 Android/README.md`, `08 Production/deployment/windows/INSTALL.md`, `02 Planning/HCC_Decision_Register.md` (plus the DEC-044…051 rows that live in `HCC_Mobile_Version_Plan_v1.md` §11), `HCC_Implementation_Backlog.md`, `RBAC_Mismatch_Rulings_Proposed.md`. For the open-items register I also read `05 Delivery Evidence/CA_Question_Pack_2026-10-25.md`, `HCC_RBAC_Matrix_v2_Proposal.md` D1–D7 and `08 Production/ETP_Reuse_Pending.md`.

**Probes (all in `05 Delivery Evidence/Audit/probes/`, outputs in `evidence/A11/`):**

| Probe | What it does | Output |
|---|---|---|
| `A11_paths.mjs` | every backtick-quoted path in the 11 documents, resolved against 28 roots and then by suffix over the whole tree | `paths.json`, `paths.txt` — 458 path-like tokens, 405 resolve, 53 do not (all classified in table (a)) |
| `A11_counts.mjs` | static counts: `test(` registrations, `[Fact]/[Theory]`, migrations, solution projects, `MapGet/MapPost`, PERM ids, `SCHEMA` | `counts.json` |
| `A11_install_check.mjs` | INSTALL.md / READMEs: every `.\Script.ps1 -Flag` against the script, config keys against `src`, HTTP routes against `Hcc.Api` | `install_check.json` — 17 script refs, 27 flags: all exist; routes/keys all exist (the 5 "misses" were my own probe list: `ApplyRoomBlock` is README's own wrong example, the other four exist under different spelling, checked by hand) |
| `A11_perms.mjs` | every `PERM-xxx-nnn` in the documents against the seed SQL | `perms_in_docs.json` — 31 references, 0 unseeded |
| `A11_dec_trace.mjs` | where code, tests and migrations cite each DEC/APR id (a **lead only**: I opened the cited lines and judged them in table (b)) | `dec_trace.txt/json` |
| (manual) | `apk_hashes.txt` (SHA-256 prefixes of the APKs in `05 Delivery Evidence/APK`), `proto_loop_sites.txt` | |

Test and build counts below are **static** (registrations counted in source). Every HANDOFF number is marked **to be confirmed by A1**.

---

## (a) Stale-statement table

Severity of the staleness: **MISLEADING** = a reader following the document would do the wrong thing or hold a wrong belief about go-live; **minor** = harmless historic or cosmetic.

### a1. Statements checked and found stale or wrong

| # | Document : line | Statement | What the code / repo shows | Staleness |
|---|---|---|---|---|
| 1 | `CLAUDE.md:7`, `08 Production/ETP_Reuse_Pending.md:3`, `HANDOFF.md:341`, `MIGRATION.md:26` | "ETP (`V:\ETP`) was not moved" / "ETP is not on this PC" / "V:\ETP for ETP-REUSE stand-ins" | `E:\ETP\Code` exists on this PC (`SaagarCC-V6-ETP-Source-and-Report-Engine-605002f`, `Worktrees`); `V:\ETP` does not. ETP was **not audited**. The 38 `ETP-REUSE` stand-ins (25 `ETP-REUSE` markers in `08 Production/src`) can be aligned now, not "when ETP is available" | **MISLEADING** (Wave 5 planning: the dependency the docs call blocked is not blocked) |
| 2 | `CLAUDE.md:5` | "APK v12 (versionCode 12) = next build/test" | APK v12 is built and tested (`f0a5a29`, report `05 Delivery Evidence/Acceptance/APK_v12_Emulator_Report.md`, tracked; `04 Android/android/app/build.gradle:30` `versionCode 12`; SHA-256 of `HCC-0.1.0-proto-debug.apk` = `39C6C9F7…` and `…server-debug-TESTCA.apk` = `3635D571…` match HANDOFF:343). `CLAUDE.md` last changed in `8b84b83`, before v12 | minor |
| 3 | `CLAUDE.md:7` | "Use Node 25 (`E:\Tools\nodejs`; the `E:\Program Files\nodejs` v24 comes first on PATH)" | On this PC Machine PATH lists `E:\Tools\nodejs` **before** `E:\Program Files\nodejs`, User PATH also has it; `where node` returns `E:\Tools\nodejs\node.exe` first, `node -v` = v25.9.0 (v24.13.1 is still installed). The precaution is harmless, the statement is no longer true | minor |
| 4 | `CLAUDE.md:11` (Read first #2) | `HCC_Plan_FY_History_and_APK_v1.md` is the "CURRENT plan" | That plan is finished (Phases A–D done; its §9 still shows two unticked lines, D6 and "owner acceptance of W0-D", although APR-011 accepted). The live plans are `agents/W4_Briefs.md`, `Audit_*`, HANDOFF 12b "Wave 5". Read-first list has **nothing** about the server: no `08 Production/README.md`, `WAVE1_PARALLEL_RULES.md`, `HCC_Production_Contracts_and_Schema_v1.md`, INSTALL.md, security review, CA pack | minor (but the list sends a new agent to the prototype only) |
| 5 | `CLAUDE.md:16-17` (Layout) | Lists folders 01–05 only | `06 Reference Repositories` is **absent** on this PC (see row 27), `07 Integration Lab`, `08 Production`, `tools`, `graphify-out`, `worktrees` exist and are unlisted — `08 Production` is the main deliverable | minor |
| 6 | `HANDOFF.md:1-61` (title and "Status" block) and `CLAUDE.md:10` ("HANDOFF.md — current status and next steps") | Title "updated 24 Oct 2026, session 2"; Status says 155/155 then 158/158 tests, "Not yet done (desktop)", "NEXT: MB-05…09", "Next session — start here (expect 382/382)" (L52) | HANDOFF is an append-only log; the current state is at lines 320-344 (Session 12/12b). Anyone reading from the top (as CLAUDE.md tells them to) gets the 24 Oct picture | **MISLEADING** (onboarding) |
| 7 | `HANDOFF.md:180,220,246` vs `:262`, `CLAUDE.md:39` | "restart `bash tools/checkpoint.sh`" (three places) | L262 (and CLAUDE.md) say the 7-minute checkpoint job does **not** apply on the work PC. `tools/checkpoint.sh` still exists | minor |
| 8 | `main` vs audit branch | HANDOFF's APK v12 paragraph and "Next session: independent audit" (L343-344) | Commit `c0c477d` is **only on `claude/audit-fable-2026-10-26`** (`git branch --contains c0c477d`); `main` is `f0a5a29` and still says "Running at handoff: APK v12 build" (L340) | minor (lead should know before the final merge decision) |
| 9 | `HANDOFF.md:332` (Session 12) and audit plan §0 | "main is 173+ commits ahead of origin" | `git rev-list --count origin/main..main` = **270** (437 commits on main). Statement is true as "173+", but stale | minor |
| 10 | `08 Production/README.md:3` | "Branches `claude/wave1-p0` → `claude/wave1-integration` … not merged into `main` until the owner says so"; title "Wave 1 · P0 foundation + P1–P5 integrated" | The server is on `main` since `3ba9559` (HANDOFF:328), and the tree has Waves 1–4 | minor |
| 11 | `08 Production/README.md:14` | Migration list ends at `0600/0601`; "(The 0200–0400 header comments still say `database/modules`…)" | `database/migrations` holds **46** files up to `2702_import_pos.sql` (`counts.json`); the audit plan says 47 — the folder has 46 | minor |
| 12 | `08 Production/README.md:15-25` (tests table) | Test projects described: Platform, Access/Inventory/Stays/Billing, Closing, GstIds, Integration | 28 test directories exist (`Hcc.slnx` lists 28 `tests/*.csproj`); 20 are undocumented in the README | minor |
| 13 | `08 Production/README.md:29-30` | `dotnet test Hcc.slnx` ("all tests") | CLAUDE.md:5 and HANDOFF (L312, L323, L338) require `dotnet test Hcc.slnx -maxcpucount:1`; `xunit.runner.json` serialises inside a project only, parallel projects share one SQL instance and time out (HANDOFF:312). The README command is the one that will flake | **MISLEADING** (anyone re-running the suite) |
| 14 | `08 Production/README.md:42-43` | `Workpc\Sagar` lacks `dbcreator`; owner must grant | Granted on 25 Oct (HANDOFF:271) | minor |
| 15 | `08 Production/deployment/windows/INSTALL.md:84` and `:129-136` (final checklist) | "Rooms, rates and other master data are loaded by the later setup steps of HCC (not part of this installer)" | The masters import exists since W4-13 (`ImportEndpoints.cs`, `2700_import.sql`, templates and samples in `deployment/import-templates/`, README there). INSTALL.md never points to it and its checklist has no "rooms/tariffs/staff/opening balances loaded" line — the first-day step the owner cannot skip | **MISLEADING for go-live** (undocumented step; workaround = read `import-templates/README.md`) |
| 16 | `04 Android/README.md:10-12` | `$env:JAVA_HOME="C:\Program Files\Android\Android Studio\jbr"` … "`java -version` must show 21" | That JBR is **Java 25.0.3** on this PC (`java -version`); HANDOFF:285 says it fails and Microsoft JDK 21 (`C:\Program Files\Microsoft\jdk-21.0.12.101-hotspot`, used in the Production section L56) is needed. `ANDROID_HOME` here is `E:\Android\Sdk`; the README's `C:\Users\Sagar\AppData\Local\Android\Sdk` does not exist | **MISLEADING** (anyone rebuilding the APK, incl. the release APK that goes to hotel tablets) |
| 17 | `04 Android/README.md:4,79` | "Debug build only … no backend. Not a Play Store release"; "Known limits: debug build only · no backend" | The same file documents `productionDebug` and `productionRelease` (L31-69); `build.gradle` has `productionRelease` `debuggable false` (L71). Also L23-24 use `V:\Codex\…` paths and `npm install` (HANDOFF:343 says `npm ci`), L3 `versionCode 2` (now 12) | minor |
| 18 | `03 Prototype/README.md:9,56,77,82-88` | "Node 20 or later (tested on Node 22)"; saved in "local storage under `hcc-prototype-v2`"; tests = "run.mjs + m1.mjs"; "Known limitations (M1)": 12 permissions per role, calendar/guest profile/check-in/cashier/approvals/backup "not built", "No physical Android test" | State is IndexedDB (`src/app/storage.ts:15`, manifest `hcc-slices-1` L16; the README's own L57 says so — it contradicts L56); 83 test files are loaded by `tests/run.mjs`; all those screens exist (PAGE_CATEGORY); physical Tab S7 tests were run (HANDOFF:108, D6 report). README also omits `npm run build:production` | minor |
| 19 | `03 Prototype/README.md:44` | "S4 and S5 show as 'not yet built' until M2/M3" | Still literally true: `src/fixtures/scenarios.ts:17-18` `available:false` — although company credit (BL-020) and close-resume (BL-030) were delivered. Two scenarios were never built | minor |
| 20 | `03 Prototype/src/clients/sync/README.md:85-89` ("Known gaps (W4-0)") | "No reservation list endpoint"; "`date` is provisional … W4-4 patches it"; "`s.policy` … not derived yet (W4-5)" | `GET /reservations` exists (`ReservationsEndpoints.cs:38`) and `reservations.ts:5` loads it (180-day window, `reservations.ts:34`); `closing.ts:45` patches `date` from the server business date; `admin.ts:84` `policyFromServer` derives `s.policy` | minor |
| 21 | `03 Prototype/src/clients/sync/README.md:47` | Worked example sends `ApplyRoomBlock` | No such command; the server command is `BlockRoom` (`HousekeepingEndpoints.cs:42`) | minor |
| 22 | `03 Prototype/src/clients/sync/capabilities.ts:32` (comment quoted from README) | "Module PERM ids … PERM-HOV-*, PERM-MNT-*, PERM-POS* do not change can()" | No `PERM-HOV-nnn`, `PERM-MNT-nnn`, `PERM-POS-nnn` exists in any migration; they are only **proposed** in `HCC_RBAC_Matrix_v2_Proposal.md:83-87` (no migration 2600) | minor |
| 23 | `02 Planning/HCC_Implementation_Backlog.md:59-100` | "502 tests, APK v8", "Wave 0.5 … not yet on main; Wave 1 P0 started"; open-defects table: R-01, T-01, T-02 "OPEN — owner decision", AC-03/04, W05-1…8, F1…F6 "Fix in progress" | All closed: DEC-059/060/061 (R-01, T-01, T-02), AC-03/04 and W05-x fixed (HANDOFF:265-276), F1 pinned by `tests/stream-cashbook.mjs:74-89`. Anyone using the backlog as "what is open" gets a wrong list | **MISLEADING** (for the open-items register) |
| 24 | `02 Planning/HCC_Implementation_Backlog.md:55` | BL-035 output `05 Delivery Evidence/M3/` | Folder is `05 Delivery Evidence/M2-M3` | minor |
| 25 | `02 Planning/RBAC_Mismatch_Rulings_Proposed.md:12-14` | W05-1 "RBAC test still logs KNOWN-FAIL"; F1 "being fixed on another branch" | `tests/stream-rbac-matrix.mjs:29` `const KNOWN=[]` (empty); F1 fixed (above) | minor |
| 26 | `MIGRATION.md` (whole file) | Package `V:\HCC-Migration-…`, target `E:\Codex\Hotel Control Centre`, "expect 502/502 … 1,884 KB", `HCC.bundle`, `claude-memory\`, `SHA256SUMS.txt`, `Norton trust store` | One-time document for the old PC. Real location `E:\Projects\HCC\Hotel Control Centre`; the package files are not in the repo (only `tools/restore-on-new-pc.ps1`, `tools/setup-work-pc.ps1` are). Historic | minor |
| 27 | `HANDOFF.md:101-102` | "Saved full Git clones under `06 Reference Repositories/`" | Folder not present on this PC (not migrated) | minor |
| 28 | `CA_Question_Pack_2026-10-25.md:5,89` | "Every GST, payroll and due-date rate or rule it applies is a configurable, dated setting … **no software change is needed** for rates, thresholds or due dates" | True for the GST master (`0600_tax_gst_master.sql:71-82`), payroll rate versions (`PayrollModel.cs:153-163`, incl. `OtBps`, `LateBlock`, `EsicPeriod`) and compliance due rules (`ComplianceRules.cs:299`). **Not** true for: B2CL threshold `Gstr1Summary.cs:28` (`static readonly`), SAC codes `GstIdentity.cs:101`, chart of accounts `ChartOfAccounts.cs:22` / `1000_accounting.sql:6` ("fixed in code"), F&F caps `LeaveAndFnf.cs:45-50`, leave span `LeaveAndFnf.cs:12`, IGST/place-of-supply logic `InvoiceRules.cs:99-100`, PF base = basic only (structure), ESIC wage definition. A3a/A4 classify each (see register (c)) | **MISLEADING** (the CA is told a change is a setting change when some answers need code) |

### a2. Path probe — the 53 unresolved path tokens (`evidence/A11/paths.txt`)

| Group | Tokens | Verdict |
|---|---|---|
| Installed or generated on the hotel PC, or git-ignored per PC: `appsettings.Production.json`, `certs\HCC-Hotel-CA.cer`, `.cer`/`.pfx` fragments, `hcc-ops.json`, `C:\Program Files\HCC\Server\…`, `E:\HCC-Hotel-CA.pfx`, `/index.html`, `hcc-release.jks`, `keystore.properties`, `Documents/HCC/`, `res\raw\hcc_hotel_ca.pem`, `04 Android\.build\hcc-cacerts.jks` (3 documents) | 30 | fine (samples exist: `appsettings.Production.sample.json`, `hcc-ops.sample.json`; the Norton trust store is not needed on this PC) |
| Old PC / migration package: `V:\HCC-Migration…`, `V:\ETP\…`, `V:\Co work\…`, `HCC.bundle`, `claude-memory\`, `reference-inputs\…`, `SHA256SUMS.txt`, `06 Reference Repositories\…`, a lone `\` | 14 | historic (rows 1, 26, 27) |
| Deleted on purpose: `03 Prototype/dist/*.tgz`, `zz-contrast.css` | 6 | fine (HANDOFF says "can be deleted") |
| APK name fragments `-v8.apk`, `-v9.apk` (the files exist as `HCC-0.1.0-proto-debug-v8.apk` …) | 2 | fine |
| Wrong folder name: `05 Delivery Evidence/M3/` | 1 | row 24 |

**Everything else resolves**: all 405 other documented file/folder paths in the 11 documents exist in the repo (`src/mobile/frontdesk|money|ops|admin`, every `Hcc.*` project and README named, `tests/Hcc.Platform.Tests/Golden/base-fixture.golden.json`, `tools/golden/export-golden.mjs`, the `W4`/`W3` briefs, INSTALL.md scripts and `installer/Hcc.iss`+`Build-Installer.ps1`, `04 Android/scripts/*`). One note: INSTALL §3 offers `HCC-Server-Setup-<version>.exe`; the repo has the Inno template and the build script but **no evidence an installer was ever built** (A9).

### a3. Statements checked and found TRUE (so the lead can skip them)

| Statement | Evidence |
|---|---|
| INSTALL.md: every script, every `-Flag`, `Hcc.Api.exe migrate`, ports 5443/5080, service name `HccServer`, backup times 02:30 / every 4 h / Sunday 04:30, 397-day certificate, 30-day retention, firewall profile Private+Domain, TrustServerCertificate refusal in Production, `AllowedHosts` ≠ `*`, web root `<server>\web\index.html`, `Hcc:WebBooking:Enabled`, `PublicPort`, `KnownProxies` | `install_check.json`; `Register-HccBackupTasks.ps1:6,23-24`; `New-HccCertificate.ps1:44`; `Backup-HccDatabase.ps1`/`Hcc-Common.ps1:30`; `Install-HccService.ps1:66`; `HccHosting.cs:73,97`; `WebAppHosting.cs:21-25` |
| `CLAUDE.md`: `SCHEMA` = 8 | `03 Prototype/src/domain/types.ts` (`SCHEMA=8`) |
| `CLAUDE.md` Commands: `npx tsc --noEmit`, `npm test`, `npm run build` (→ `dist/index.html` + `Hotel Control Centre.html`), `npm start` on 127.0.0.1:4173, `-- --lan` | `package.json` scripts; `build.mjs:17-18`; `serve.mjs:1-6`. `build:production` exists and exits before writing `dist/index.html` (`build.mjs:17`) |
| CLAUDE.md architecture rules: all mutations through `execute`, handlers in `handlers/*.ts`, money in paise, simulated clock (no `Date.now()`/`new Date()` outside `clients/` except one idempotency-key suffix `features/guests/GuestId.tsx:26`), no new npm packages (deps = react, react-dom, lucide-react), `restoreState(...,fallback='BASE')` (`persistence.ts:39`), `hcc-slices-1` manifest, `PAGE_CATEGORY` (`navmap.ts:30`), `withHoldExempt` (`selectors.ts:50`), `native.ts` helpers, module folders `features|streams|handlers|fixtures/ext|tests` for modules 13–26 (documents lives inside `messaging`; `config`, `cashbook`, `customers` lack their own `fixtures/ext` or handler file) | grep results, `evidence/A11` notes |
| `08 README`: commands `dotnet run … -- migrate` (`Program.cs:17`), `Category!=Sql` filter (83 files carry `[Trait("Category","Sql")]`), session idle 30 min / absolute 12 h (`AccessPorts.cs:10-12`), first-time setup routes `/setup/status`, `/setup/SetupProperty` | grep |
| HANDOFF migration blocks: OTA 2500/2501, web 2000/2001, documents 2100/2101, CRM 2300/2301, stays 0301, room-block 0202, groups 2200/2201, invoices 2400/2401, setup 1900, import 2700/2701 | `ls database/migrations` (also present, undocumented in HANDOFF: 0203, 2202, 2702) |
| HANDOFF "master folio takes no cash yet" | `GroupCommands.cs:298-299` refuses Cash |
| HANDOFF "Accounting does not book GroupReceiptRecorded/GroupAttritionPosted" | published at `GroupCommands.cs:343,417`; no consumer anywhere in `src` (grep) |
| HANDOFF "cash book has no export" (server) | no export/csv in `ClosingEndpoints.cs` (prototype has Excel/CSV, W05-8) |
| HANDOFF "ETP-REUSE 38 rows" | `ETP_Reuse_Pending.md` has 38 numbered rows |
| HANDOFF "28 projects" | `Hcc.slnx`: 34 `<Project>` entries = 6 src + 28 tests |

### a4. Test-count claims (static; **to be confirmed by A1**)

| Claim | Static finding |
|---|---|
| Prototype **1008** (CLAUDE.md:5, HANDOFF:338) | `tests/run.mjs` loads `run.mjs`, `m1.mjs` and 81 `stream-*/sync-*.mjs` = 83 files holding **963** literal `test(…)` calls; 1008 − 963 = 45 must come from loop sites with template names: `stream-rbac-matrix.mjs:195,234` (10 demo users each), `sync-roles.mjs:175` (9 roles), `stream-perf-reports.mjs:31`. Plausible, not provable statically. A results file written by someone's run at 2026-10-26T09:35:44Z (`03 Prototype/evidence/domain-tests.json`, git-ignored) lists 1008 passed / 0 failed with 1008 unique names. |
| Server **962** in 28 projects (serial) | 28 test directories; **798** `[Fact]` + **30** `[Theory]` (with **140** `[InlineData]` rows and 3 `MemberData/ClassData` attributes) → 938 if every InlineData row is one case, plus the MemberData expansions. Compatible with 962, not provable statically. 0 `Skip=`. |
| "47 migrations" (audit plan §0) | 46 `.sql` files |
| "291 MapGet/MapPost in 31 files" (audit plan §0) | `Hcc.Api/Endpoints`: 156 MapGet + 133 MapPost = 289 in 31 files; whole `Hcc.Api`: 158 MapGet + 2 MapMethods + 133 MapPost = 293 in 32 files (A5 to reconcile with the review's 373) |
| "43 PERM ids in contracts" (plan) | 85 distinct PERM ids appear in migration SQL (82 seeded rows: `0101` = exactly 45; module seeds 9+6+2+1+2+5+2+4+3+3; the rest are comment mentions), 86 in `src/**/*.cs`; in `src` but not seeded: `PERM-CLS-005` (`ClosingPorts.cs:19` "confirm or add"), `PERM-SOD-003/004` (rule ids); seeded but not in `src`: `PERM-INV-006` (`2401…sql:3` "not seeded"), `PERM-RATE-001` |

---

## (b) Decision Register trace

### b1. DEC / APR → code

Verdict: **T** = implemented, code found · **N** = no code effect by nature · **S** = superseded or changed in practice (register not updated) · **P** = partly implemented / deviates · **G** = no trace in the repo. "proto" = `03 Prototype/src`, "srv" = `08 Production/src`. A line citation is the line I opened and judged; the DEC id appearing in a comment alone was never accepted as proof.

| ID | Decision (short) | Code that implements it | Verdict |
|---|---|---|---|
| DEC-01, 11, 13, 17, 20 | audience; UI inspiration only; workflows first; prototype first; schedule estimate | — (scope / process / estimate) | N |
| DEC-02 | 26 areas, first 12 = MVP | proto `features/*` (26 areas; `features/housekeeping/Roadmap.tsx:8`) | T |
| DEC-03 | SQL Server Express is the store | `database/migrations/0001_platform.sql`; srv `Hcc.Infrastructure.SqlServer/Migrations/MigrationRunner.cs:65` | T |
| DEC-04 | modular monolith, DDD, internal events | `Hcc.slnx` (6 src + 28 test projects), `08 Production/WAVE1_PARALLEL_RULES.md`; boundary rules are A8's | T (A8 verifies) |
| DEC-05 | NestJS backend | superseded by DEC-054; none in repo | S |
| DEC-06 | React + TypeScript | `03 Prototype/package.json` (react ^19), `src/main.tsx` | T |
| DEC-07 | Windows-hosted server + browser frontend | `Hcc.Api/Hosting/HccHosting.cs` (Windows service, LAN HTTPS), `Hosting/WebAppHosting.cs:21-25` | T |
| DEC-08 | Android = responsive browser/PWA | replaced by the Capacitor APK (DEC-051) and the web app served by the API; no service worker (`03 Prototype/README.md:97`). Register row never updated | S |
| DEC-09 | SQLite never the master DB | negative: no SQLite reference in `08 Production/src`, `Directory.Packages.props`, `03 Prototype/src` (grep) | T (negative) |
| DEC-10 | master Access Control panel | proto `features/admin/AccessControl.tsx`; srv `Hcc.Domain/Access/AccessModel.cs`, `0100_access_identity.sql` | T |
| DEC-12 | selective licence-checked reuse | `08 Production/third-party/source-reuse.json` (H02 → `Hcc.Domain/Pricing/RateAdjustments.cs`, "not yet called by any use case"; H04 date helper) | T (limited) |
| DEC-14 | charges, receipts, deposits, refunds, tax separate | srv `Hcc.Application/Billing/BillingService.cs` (charge L82-90, receipt L138-155, refund L218) | T |
| DEC-15 | separate configuration panel | proto `domain/streams/config.ts:1`; srv `Hcc.Application/Configuration/ConfigurationService.cs` | T |
| DEC-16 | module-owned tables, no cross-module writes | `WAVE1_PARALLEL_RULES.md`; platform migration/FK test; **A8 verifies** | T (A8 verifies) |
| DEC-18, DEC-021 | React + mock; single esbuild HTML | `03 Prototype/build.mjs:12-18` | T |
| DEC-19 | local-network first, no SaaS for core | `HccHosting.cs` LAN HTTPS 5443, `INSTALL.md`; external calls = A9 | T (A9 verifies) |
| DEC-022 | persistence = `localStorage` `hcc-prototype-v1`, Reset | now IndexedDB: proto `app/storage.ts:9-16` (`hcc`/`state`; legacy v2 key only migrated); Register text not updated | S |
| DEC-023 | integer paise; per-line tax "half-up" | proto `domain/money.ts:1`, `domain/gst.ts:73`; srv `Hcc.Domain/Money/Paise.cs:8`, `MoneyMath.cs:24-28`. **Code rounds half away from zero**; the register says half-up (differs only for negative lines) | T (text differs) |
| DEC-024 | arrival-inclusive, departure-exclusive | proto `domain/selectors.ts:36-39`; srv `Hcc.Domain/Dates/StayDates.cs:25`, `Infrastructure.SqlServer/Inventory/SqlInventoryStore.cs:48` | T |
| DEC-025 | no checkout with balance or deposit unless company credit | proto `services/mock/handlers/folio.ts:236-237`; srv `Billing/BillingIntegration.cs:25`, `Stays/StaysCommands.cs:204`, `BillingService.cs:368-381` | T |
| DEC-026 | inspection before Ready, default ON | proto `fixtures/base.ts:46`, `handlers/housekeeping.ts:27`; srv `Housekeeping/HousekeepingCommands.cs:66-67` (Master configuration) | T |
| DEC-027 | discounts > ₹250 need approval | proto `fixtures/base.ts:46` (`discountLimit:25000`); srv `0101_access_rbac_seed.sql:107` (REC limit 25000). Replaced as a rule by DEC-063 G1 (MGR 100000 at `:106`); register §4 does not say so | S |
| DEC-028 | closed-date corrections: adjustment-only **or** controlled reopen (production) | proto `handlers/closing.ts:171-177` (adjustment); srv adjustment path `BillingService.cs:242,338`; only cash-book days can be reopened (`ReopenCashBookDay`, `ClosingEndpoints.cs:15`). Register status still "Needs owner decision" | P (open decision) |
| DEC-029 | night posted once; option B approved APR-003 | proto `services/mock/handlers/closing.ts` (`NIGHT_POST`; legacy `POST_NIGHTS` kept, not wired to any screen: `commands.ts:77`); srv `PostRoomNights` `ClosingEndpoints.cs:48`. Register status cell still says "Needs owner decision" although APR-003 approved it | T (status text stale) |
| DEC-030 | deterministic clock | proto `domain/types.ts:195`, `services/mock/commands.ts:189`; srv injected `TimeProvider` (`08 README`: events `DATETIME2(3)` UTC) | T |
| DEC-031 | refund executed by someone other than requester | proto `handlers/folio.ts:110`; srv `BillingService.cs:423` | T |
| DEC-032 | add Accountant, Tech admin, Maintenance | srv `AccessModel.cs:3`, `0101_access_rbac_seed.sql:10-14`; proto `ROLE_MAP` `capabilities.ts` | T |
| DEC-033 | last **active** owner protected | srv `Infrastructure.SqlServer/Access/SqlUserAdministration.cs:640-653`; proto `handlers/admin.ts:114` | T |
| DEC-034 | advance is a receipt allocated at receipt time | srv `BillingService.cs:120` (advance before check-in opens the folio), `:218`; proto `domain/selectors.ts` `totals` | T |
| DEC-035 | prototype one shift/day; production multi-shift | proto `handlers/closing.ts:164`; srv `ClosingService.cs:297`, `PaymentsEndpoints.cs:7` (`CreateTill, OpenShift, SubmitShiftCount`) | T |
| DEC-036 | `--lan` option | `03 Prototype/serve.mjs:1-6` | T |
| DEC-037 | demo name "HCC Demo Residency" | proto `fixtures/base.ts:46` | T |
| DEC-038 | hold expiry 2 h | proto `domain/types.ts:73`; srv `Hcc.Domain/Inventory/StayRules.cs:55` (120 min) | T |
| DEC-039 | synthetic 10 % tax | replaced by DEC-069: srv `Billing/BillingPorts.cs:51`, `Tax/TaxPorts.cs:17` ("replaces the DEC-039 `ITaxRateSource`"); register §4 does not list it | S |
| DEC-040 | policy evaluation order | srv `Hcc.Domain/Access/AccessModel.cs:120`, `SqlAccessPolicy.cs:72`, `0100_access_identity.sql:38`; proto `domain/streams/admin.ts:114` | T |
| DEC-041 | approval bound to action+target+amount+version | srv `BillingService.cs:369,451` ("folio changed after this approval"), `BillingPorts.cs:125`; proto `handlers/folio.ts` (stale approval test in `run.mjs`) | T |
| DEC-042 | business date moves only at day close | srv `ClosingService.cs:297`, `BillingService.cs:45`, `AccountingService.cs:38`; proto `types.ts:193` | T |
| DEC-043 | no third-party code in prototype | negative: `03 Prototype/package.json` has no donor package; donor-derived source lives only in `08 Production` (`source-reuse.json`) and the patched printer plugin in `04 Android` (MPL-2.0, README L84). Register row sits after DEC-069 (mis-ordered) | T (negative) |
| DEC-044 | one codebase, mobile shell, display toggle | proto `app/display.tsx:1`. **Not in the Decision Register**: lives in `HCC_Mobile_Version_Plan_v1.md:122,139` | T (register gap) |
| DEC-045 | all 9 roles on phone; access-matrix editing desktop-only | proto `mobile/admin/Access.tsx:2,62` | T |
| DEC-046 | phone calendar = agenda | proto `mobile/frontdesk/Calendar.tsx:1` | T |
| DEC-047 | printing stays on phone (changed from "hidden") | proto `app/native.ts:146` `printDocument`; `mobile/money/register.tsx:18` | T |
| DEC-048 | validate on emulator + Playwright, record as emulated | process (reports in `Acceptance/`) | N |
| DEC-049 | Maroon & Sand (two rows: "Open" then "Approved") | proto `styles.css:2` (#2b1d16 / #faf6f0), theme-color `build.mjs:16`. Mobile plan keeps both the "Open" row (L127) and the approved row (L144) | T (register gap) |
| DEC-050 | mobile is part of the same single HTML | `build.mjs` one output; `mobile/` shell | T |
| DEC-051 | later wrap in Capacitor APK | `04 Android/` | T |
| DEC-052 | shared till; POS cash in front-desk drawer; open POS checks block day close | proto `domain/selectors.ts:64` `drawerOutletCash`, `domain/streams/closing.ts:63`; srv `Billing/BillingPorts.cs:54` (`ICashDrawerPort`), `PosEndpoints.cs:25`, `ClosingService.cs:172-173` (POS checks/voids in preflight) | T |
| DEC-053 | stay-restriction override = approve permission + reason | proto `handlers/reservations.ts:25-30`; srv `Reservations/ReservationService.cs:269`, `Domain/Inventory/StayRules.cs:13` | T |
| DEC-054 | .NET 10 + SQL Server, ETP reuse | whole `08 Production`; `MigrationRunner.cs:65`; ETP-REUSE: 38 stand-ins pending | T (reuse = A9) |
| DEC-055 | cash book page; compliance Owner + Accountant | proto `app/navmap.ts:185`, `domain/streams/compliance.ts:33`; srv `ComplianceEndpoints.cs:11`, `1101_compliance_rbac_seed.sql` | T |
| DEC-056 | payslip share sheet + simulated WhatsApp | proto `domain/streams/staff.ts:492`, `handlers/staff.ts:304`; srv `PayrollService.Pay.cs:244`, `0900_payroll.sql:187` | T |
| DEC-057 | Master configuration hub, dated values, **module switch-off** | hub: proto `domain/streams/config.ts:1,58-75`; srv `Configuration/*`. Switch-off: client hides pages/commands (`config.ts:58-75`, production client reads `module.*` in `clients/sync/admin.ts:120`); server `IModuleGate.EnsureOnAsync` (`ConfigurationPorts.cs:171-211`) and `RequireHccModule` (`ConfigurationEndpoints.cs:31`) exist but have **no production call site** (only `tests/Hcc.Configuration.Tests/Sql/ConfigurationHttpTests.cs:41`) | **P** → finding A11-01 |
| DEC-058 | Owner changes all; Tech admin non-money only | proto `domain/gst.ts:12`, `streams/admin.ts:221,252`; srv `ConfigurationService.cs:207`, `ConfigurationPorts.cs:11,214` | T |
| DEC-059 | keep compact tablet header | no change required; proto CSS as built | N |
| DEC-060 | touch targets 44/48 px on coarse pointers | proto `styles/z-tablet.css:1,23` | T |
| DEC-061 | HK/MNT see own pay only | srv `Payroll/PayrollQueries.cs:12,35,97`, `PayrollPorts.cs:19`; proto `clients/sync/staff.ts:4` | T |
| DEC-062 | PIN + device for staff; password for Owner/Accountant/Tech admin | srv `Domain/Access/CredentialRules.cs:5-38`, `AccessModel.cs:19`, `AccessContracts.cs:12,54` (`X-HCC-Device`) | T |
| DEC-063 | 13 RBAC rulings G1–G13 | sampled: G1 limits `0101_access_rbac_seed.sql:105-107` (MGR 100000, REC 25000 paise); G2 `BillingService.cs:423`; proto `domain/streams/admin.ts:223,232`, `app/nav.ts:1`; srv seeds. Full route-by-route match = **A5** | T (A5 verifies) |
| DEC-064 | work-PC debug keystore from APK v9 | none in repo by design (`C:\Users\Sagar\.android\debug.keystore` exists on this PC; Gradle uses the default debug signing); decision is operational | G (by design) |
| DEC-065 | cash-book lock blocks changes until Owner reopens | proto `domain/streams/cashbook.ts:111`, guards in `handlers/closing.ts:89,127,140,177,196`, `folio.ts:47,93,113`, `pos.ts:126,160`; srv `Closing/ClosingPorts.cs:44` `EnsureCashBookOpenAsync` called from `BillingService.cs:242,338`, `ExpenseCommands.cs:100,162`, `GroupCommands.cs:313`, `PosService.cs:275,473`. Server dates every cash receipt/refund at the current business date (`BillingService.cs:147`), so the prototype's "refund of a receipt on a locked day" case has no server twin | T (A3b checks the lock/reopen audit) |
| DEC-066 | F&F: ≤ ₹2,00,000 per adjustment, > ₹1,00,000 additions need Owner, maker-checker | srv `Domain/Payroll/LeaveAndFnf.cs:45,48,50`, `PayrollService.Pay.cs:289,311,333` | T |
| DEC-067 | checkout cleaning tasks created **unassigned** | srv `Infrastructure.SqlServer/Housekeeping/SqlHousekeepingStore.cs:134` (no assignee), `StayoverCommands.cs:27`. **Prototype still assigns `'housekeeper'`**: `services/mock/handlers/folio.ts:244`, `handlers/guests.ts:100`, `commands.ts:72,124` | **P** → finding A11-03 |
| DEC-068 | store full ID number incl. Aadhaar, masked, encrypted | srv `Domain/GuestIds/GuestIdRules.cs:8`, `0601_guest_id_documents.sql:2`, `Api/Endpoints/GuestIdEndpoints.cs:9`, `Application/Documents/DocumentService.cs:19`; proto `domain/streams/guests.ts:17-18`. Leak analysis = **A7** | T (A7 verifies) |
| DEC-069 | dated GST master | srv `Domain/Tax/GstMaster.cs:78-93`, `0600_tax_gst_master.sql:71-82`, `Tax/GstMasterService.cs`; proto `domain/gst.ts:1-83`. Figures = **A3a** | T (A3a verifies) |
| APR-001…004 | approvals of plan / M0 package / M2-M3 parallel build | no code effect (APR-003 Q1/Q2 → DEC-029/037) | N |
| APR-005 | Wave 0 | → DEC-052, DEC-053 | N (via DEC) |
| APR-006 | Wave 0.5 placement and master configuration design | → DEC-055…058 | N (via DEC) |
| APR-007 | start Wave 0.5 now | process | N |
| APR-008 | T-01, T-02, R-01 | → DEC-059, 060, 061 | N (via DEC) |
| APR-009 | production sign-in | → DEC-062 | N (via DEC) |
| APR-010 | all 13 RBAC rulings | → DEC-063 | N (via DEC) |
| APR-011 | prototype accepted | process; `HCC_Acceptance_Matrix.md` | N |
| APR-012 | open-decision list | → DEC-065…069 | N (via DEC) |

(The `dec_trace` probe also shows "APR-001" 69 times — those are `PERM-APR-001`, the approval permission, not the approval log.)

**Tally (81 ids: 69 DEC + 12 APR):** T = 53 (7 of them marked "A-verifies": DEC-04, 16, 19, 054, 063, 068, 069); N = 7 DEC + 12 APR; S = 5 (DEC-05, 08, 022, 027, 039); P = 3 (DEC-028 open, DEC-057, DEC-067); G = 1 (DEC-064, by design). 6 of the 53 T rows have register text that differs from the code or is missing from the register (DEC-023, 029, 044, 047, 049, 043 ordering).

### b2. Owner decisions raised in HANDOFF (and agent-chosen production rules) with **no DEC id**

"Owner approval recorded?" = a DEC/APR in the register or an explicit owner answer in HANDOFF. For every row below the answer is **no, or only "raised for confirmation"**.

| # | HANDOFF line | Rule / decision | Where it lives in code | Owner approval recorded? |
|---|---|---|---|---|
| 1 | 69 | OWNER-1: FY26 history is the default scenario | proto `fixtures/scenarios.ts:9` | "yes" in chat, no DEC |
| 2 | 69 | OWNER-2: share-sheet fallback for printing is acceptable | proto `app/native.ts:146` | "yes" in chat, no DEC |
| 3 | 83 | OWNER-3: Tab S7 (SM-T875) is the test device | process | n/a |
| 4 | 107 | Phones keep the phone layout, tablets get the desktop layout inside the APK | proto `app/native.ts:30` `nativePhoneLayout` (short side < 600) | owner decision, **no DEC id** (the T-01/T-02 brief wrongly cites it as DEC-044) |
| 5 | 122 | Navigation redesign: staff-picker sign-in without PIN, ten categories, search finds pages/actions only, booking-ID search only on Arrivals, Guests profile moves under Customers | proto `app/navmap.ts:30`, `ModulesHome.tsx:61`, `SearchPalette.tsx` | owner feedback + "decisions" in text, **no DEC id** |
| 6 | 270 | Wave 1 P0 "9 design choices for confirmation" (tax rounding half away from zero vs prototype `Math.round`, audit row on every command, IANA time zones, …) | srv `MoneyMath.cs:6`, `SqlCommandRunner` (mandatory audit) | **no** — the list was never answered in the register |
| 7 | 278/287 | "own shift" = the shift you opened; Cashier refused the daily-collections report; folio created at check-in | srv `Infrastructure.SqlServer/Billing/SqlShiftScopeResolver.cs:10` ("Owner to confirm"), `PaymentsEndpoints.cs:20`, `StaysCommands.cs` check-in | **no** |
| 8 | 278/287 | ESIC contribution-period rule + daily-wage ×26 basis | srv `PayrollRules.cs:208,234`, `PayrollModel.cs:158,162` | **no** (CA P5) |
| 9 | 278/287 | SCC "extra days" rule not ported | srv `PayrollModel.cs:11` | **no** (CA P10; "owner decision pending" in the code comment) |
| 10 | 278/287 | Bank-detail changes have no second approver | srv `PayrollService.cs:202,246` | **no** |
| 11 | 310 | Where the production ID-encryption key lives and who keeps the offline copy | `New-HccGuestIdKey.ps1`, INSTALL §6b (DPAPI machine key + USB copy) | **no** — INSTALL chose the answer; owner never recorded it |
| 12 | 326 | Early-departure fee: none | srv `Stays/StayChangeCommands.cs:29` | **no** |
| 13 | 326 | Downgrade-rate loss needs the approval permission | srv `Domain/Stays/StayChangeRules.cs:65,100`, `StayChangeCommands.cs:222` | **no** ("added as production rule") |
| 14 | 326 | OTA party-size change: modify refused, front desk amends | srv `Ota/OtaService.cs:435`, `OtaQueries.cs:142` | **no** |
| 15 | 326 | Web booking needs reverse proxy/CAPTCHA before internet exposure; staff mark deposits paid | `HccHosting`/`HccListeners`, INSTALL §10; srv `WebBooking/WebBookingService.cs:235` | **no** |
| 16 | 326 | ID-image retention 90 days and "nightly purge?" | srv `Domain/Documents/DocumentRules.cs:18`, `DocumentService.cs:205-225` (manual command only) | **no** → A11-02 |
| 17 | 326 | CRM un-merge not built | srv `Customers/*` (no un-merge) | **no** |
| 18 | 326 | Pre-change released room blocks backfilled as released on creation date | `database/migrations/0202_inventory_block_history.sql:2-11` | **no** |
| 19 | 326 | Cash book has no export (server) | `ClosingEndpoints.cs` | **no** |
| 20 | 329 | Attrition: unpicked rooms beyond allowance × group % × block rate, GST per room-night, Manager ≤ ₹10,000 | srv `Domain/Groups/GroupRules.cs:172-184`, `GroupCommands.cs:417` | **no** (CA flag in code comment L182) |
| 21 | 329 | Group master folio takes no cash; group transfer before checkout manual; invoices are issued by a call after checkout; hotel GSTIN needed before first invoice; invoice prefix ≤ 6 chars | srv `GroupCommands.cs:298-299`, `Invoices/InvoiceService.cs:57`, `InvoicePorts.cs:24` | **no** |
| 22 | 341 | Booking-history window 180 days | proto `clients/sync/reservations.ts:34` | **no** |
| 23 | 341 / `Matrix_v2:285-291` | Role matrix v2 D1–D7 (RST role, dedicated PERMs, migration 2600, Cashier loses stock/maintenance money, …) | none implemented (no `2600_*`); `capabilities.ts:32` | proposal only; no DEC/APR |
| 24 | `ClosingPorts.cs:19` | Correction-request permission: reuse `close.correction.request` or add `PERM-CLS-005` | srv `Closing/ClosingPorts.cs:19` | **no** (code comment: "lead/owner decision to confirm or add") |
| 25 | `InventoryService.cs:21` | Quote lifetime default | srv `Inventory/InventoryService.cs:21` | **no** ("owner decision listed in the P2 report") |

---

## (c) De-duplicated open owner / CA register (skeleton for A3a / A4 / A9 — **lead to merge**)

Sources merged: HANDOFF Sessions 10–12b, Backlog, CA pack, Matrix v2 D1–D7, `ETP_Reuse_Pending.md`, security review checklist, and the "owner decision / CA to confirm" comments in code (57 hits in `08 Production/src` + `database`). Status is what the documents say as of 26 Oct 2026; **no CA reply and no owner answer to any row below is recorded anywhere in the repo**. The last column is empty for A3a/A4/A9/A3b/A6/A7 to fill. Rows marked (dup) are the same item raised in more than one place.

### c1. Owner decisions and actions

| # | Item | Raised where | Status | Code path waiting on it | A3a / A4 / A9 note (fill) |
|---|---|---|---|---|---|
| O-01 | Create the **release keystore** and back it up (every later APK update needs the same key) | HANDOFF:341; `04 Android/README.md:54-65` | Open; v12 release verified only with a throwaway keystore | `android/app/build.gradle:39-75` (`hccRelease`, `checkHccReleaseSigning`, `hccSigningProblem`); `.gitignore` for `*.jks` | |
| O-02 | Hotel **CA** (PFX on USB, `HCC-Hotel-CA.cer` to the APK build, yearly renewal reminder) | HANDOFF:341; INSTALL §4, 4b | Open; scripts only dry-run (`-WhatIf`) tested, never run on the hotel PC (INSTALL:5) | `New-HccCertificate.ps1`; `04 Android/scripts/build-production.mjs --ca`; `network_security_config.xml` | |
| O-03 | Keys USB: guest-ID key + offline copy holder, backup key, BitLocker on the backup drive (dup of HANDOFF:310) | HANDOFF:310,341; INSTALL §0, 6b, 7 | Open | `New-HccGuestIdKey.ps1`, `New-HccBackupKey.ps1`; restore on a new PC needs a developer step (INSTALL:97 `ConvertTo-HccWrappedGuestIdKey`) | |
| O-04 | **DEC-068 / CA L1**: full Aadhaar numbers stored against UIDAI guidance — CA/lawyer to confirm before production | Register DEC-068; CA pack L1 | Open, legal | `Hcc.Domain/GuestIds/GuestIdRules.cs:8`; `0601_guest_id_documents.sql:2`; `GuestIdProtector`; `DocumentService.cs:19` | |
| O-05 | ID-image retention (90 d default) and a **scheduled purge**; CA L2 retention/guest register | HANDOFF:326; CA L2 | Open; purge is a manual API command with no screen and no job; two retention settings exist (A11-02) | `DocumentRules.cs:18`; `DocumentService.cs:205-225`; `BuiltinSettings.cs:132`; `RetentionWorker.cs`/`SqlRetentionPurge.cs:23-25` | |
| O-06 | **DEC-028**: closed-date corrections — adjustment-only or controlled reopen (production) | Register DEC-028 ("Needs owner decision") | Open; server is adjustment-only today | `BillingService.cs:242,338`; `ClosingEndpoints.cs:15` (`ReopenCashBookDay` = cash-book days only) | |
| O-07 | Early-departure fee (none now) | HANDOFF:326 | Open | `StayChangeCommands.cs:29` | |
| O-08 | Group **attrition** rule (+ GST on retention charges) | HANDOFF:329; code comment | Open (owner + CA) | `GroupRules.cs:172-184`; `GroupCommands.cs:417`; no Accounting consumer of `GroupAttritionPosted` | |
| O-09 | Downgrade-rate loss approval rule | HANDOFF:326 | Open (confirm) | `StayChangeRules.cs:65,100`; `StayChangeCommands.cs:222` | |
| O-10 | OTA party-size change refused | HANDOFF:326 | Open (confirm) | `OtaService.cs:435` | |
| O-11 | Booking website on the internet: reverse proxy + CAPTCHA first; staff mark deposits paid (security review F-H04) | HANDOFF:326; INSTALL §10; security review | Open; off by default | `Hcc:WebBooking:Enabled`; `HccHosting.cs`/`Hosting/HccListeners.cs`; `WebBookingService.cs:235` | |
| O-12 | Booking-history window (180 days) | HANDOFF:341 | Open (confirm) | `clients/sync/reservations.ts:34` | |
| O-13 | Role matrix **v2 D1–D7** (RST role, per-module PERMs, migration 2600, Cashier loses stock/maintenance money, POS prices Owner-only, OTA/web grants) + "Tech admin view-only cells not exposed by /me/capabilities" | `HCC_RBAC_Matrix_v2_Proposal.md:285-291`; HANDOFF:341 | Proposal; recommended answer "Yes" to D1–D5, D7 and "No" to D6; not recorded as decided | `0101_access_rbac_seed.sql`; no `2600_*` migration; `capabilities.ts:32` | |
| O-14 | `PERM-CLS-005` (close-correction request): confirm or add | `ClosingPorts.cs:19` | Open | `ClosingPorts.cs:19`; `0101` seed (`close.correction.request`) | |
| O-15 | Quote lifetime default; "own shift = opened it"; Cashier refused daily-collections report; folio created at check-in (dup of b2 #7) | `InventoryService.cs:21`; `SqlShiftScopeResolver.cs:10`; HANDOFF:287 | Open (confirm) | those files | |
| O-16 | Bank-detail change without a second approver | HANDOFF:278,287 | Open | `PayrollService.cs:202,246` | |
| O-17 | SCC "extra days"/5.5-day rule not ported (= CA P10) | `PayrollModel.cs:11` | Open (owner + CA) | `PayrollModel.cs:11`; `PayrollRules.cs` attendance | |
| O-18 | CRM un-merge not built; released-block backfill rule; cash book (server) export not built | HANDOFF:326 | Open | `Customers/*`; `0202_inventory_block_history.sql:2-11`; `ClosingEndpoints.cs` | |
| O-19 | Group master folio takes **no cash**; **Accounting does not book** `GroupReceiptRecorded` / `GroupAttritionPosted`; group transfer before checkout manual; invoices issued by a call after checkout | HANDOFF:329 | Confirmed true in code (a3); build gaps rather than decisions | `GroupCommands.cs:298,343,417`; `Invoices/InvoiceService.cs`; no consumer in `Hcc.Application/Accounting` | |
| O-20 | Hotel **GSTIN** must be recorded (with valid check character) before the first tax invoice; invoice prefix ≤ 6 chars | HANDOFF:329; INSTALL:79 | Owner action at install | `InvoiceService.cs:57`; `InvoicePorts.cs:24`; `Initialize-HccProperty.ps1 -Gstin` | |
| O-21 | Forced credential change after admin reset (F-M03 remainder; needs schema + screen); Tech admin can still reset non-money accounts | Security review F-M03, checklist L91; HANDOFF:336 | Known open | `SqlUserAdministration.cs` (SetCredential); `Hcc.Contracts/Access/AccessContracts.cs:54` | |
| O-22 | **Wave 5**: ETP code (38 stand-ins; ETP now at `E:\ETP\Code`), hotel-PC install rehearsal, speed test, staff trial | HANDOFF:342; `ETP_Reuse_Pending.md` | Open | `ETP_Reuse_Pending.md` (38 rows); `deployment/windows/*`; INSTALL "dry-run tested only" | |
| O-23 | Tab S7 items T-01…T-05 and long-session Back accepted **unverified** (APR-011); physical-device checks outstanding | Register APR-011; HANDOFF:284 | Open (accepted risk) | `app/native.ts` `decideBack`; `styles/z-tablet.css` | |
| O-24 | APK v12 defects: APK12-2 (429 throttle shows generic "Sign-in failed"), APK12-3 (Reception offered "Record payment" without own open shift), five S4 items — not owner decisions, listed so they are not lost | HANDOFF:343 | Open (fix after audit) | `clients/sync/auth.ts`; money mapper `clients/sync/money.ts` | |
| O-25 | Push to GitHub (270 commits ahead) waits for the owner's word, after a secret scan | CLAUDE.md:5; HANDOFF:332 | Owner | process | |
| O-26 | Install-guide gap: masters import (rooms, tariffs, staff, opening balances) is not a step in INSTALL.md (A11 row 15) | this audit | New | `ImportEndpoints.cs`; `deployment/import-templates/README.md` | |

### c2. CA questions (CA pack of 25 Oct 2026 — 39 questions; reply not recorded for any)

"Setting or code?" is my first reading from the file named; A3a (G), A4 (P, C, T), A7 (L) confirm and fill.

| CA id | Question (short) | Code path waiting | Setting or code? (first reading) | A3a / A4 / A9 note (fill) |
|---|---|---|---|---|
| G1 | Room slab, rates, ₹7,500, 22 Sep 2025 | `Hcc.Domain/Tax/GstMaster.cs:78-83`; `0600_tax_gst_master.sql:71-76`; proto `domain/gst.ts` | dated setting | |
| G2 | Slab basis = price charged per night | `Tax/GstMasterService.cs`; `TaxPorts.cs:17` | code (rule) | |
| G3 | <₹1,000 exemption before 22 Sep 2025 | not modelled | code | |
| G4 | Restaurant 5 % / 18 % | `GstMaster.cs:85-88` | dated setting | |
| G5 | "Specified premises" test + opt-in | `GstMaster.cs:12`; `GstMasterService.cs` | code (derivation) + opt-in setting | |
| G6 | Minibar treated as restaurant | `BillingService.cs:82` (category); proto `gst.ts` charge categories | code (description → category) | |
| G7 | No-show / cancellation charge rate | no rule | code | |
| G8 | Laundry, banquet, other 18 % | `GstMaster.cs:92-93`; `0600…sql:80-82` | dated setting | |
| G9 | CGST/SGST split, rounding, IGST | `Money/MoneyMath.cs:24-32`; `Invoices/InvoiceRules.cs:34,99-100`; `CreditNoteRules.cs:81-82` | code | |
| G10 | Discount apportioned across rates | `BillingService.cs:515`; `CreditNoteRules.cs:36` | code | |
| G11 | Discount vs ₹7,500 slab | `GstMasterService.cs` (price charged) | code | |
| G12 | Credit-note treatment and timing | `CreditNoteRules.cs:36,82` | code | |
| P1 | PF wage base, pay structure after Labour Codes | `Payroll/PayrollModel.cs:153-163`; `PayrollRules.cs` | structure = code, rates = setting | |
| P2 | Employer PF split (3.67/8.33/0.5/0.5) | `PayrollModel.cs:162` (`PfErBps` 1300 single) | code | |
| P3 | PF default for basic-only / daily staff | staff master flag | per-person setting | |
| P4 | ESIC rate, ceiling, wage definition | `PayrollModel.cs:157,162`; `PayrollRules.cs:208` | rates setting, wage definition code | |
| P5 | ESIC contribution period, daily ×26 | `PayrollRules.cs:208,234`; `PayrollModel.cs:158` (`EsicPeriod`) | switch + code | |
| P6 | Rounding PF/ESIC | `PayrollModel.cs:159` (`StatutoryRounding`) | setting | |
| P7 | Maharashtra PT slabs/base/part month | `PayrollRules.cs:154-158`; `PayrollModel.cs:155-156` | slabs setting, base code | |
| P8 | Overtime 1× vs 2×, weekly-off rule | `PayrollModel.cs:159` (`OtBps` 10_000); `PayrollRules.cs:186,192` | multiplier setting, weekly-off rule code | |
| P9 | Late marks, half-days, absence | `PayrollModel.cs:159` (`LateBlock`); `:184` | partly setting | |
| P10 | Absence-forfeiture rule not used | `PayrollModel.cs:11` | not ported (code if wanted) | |
| P11 | Advance recovery limit | `PayrollRules`/`PayrollService` advance recovery | code | |
| P12 | Leave entitlements | `PayrollModel.cs:220` `LeavePolicy.Entitled` | policy setting | |
| P13 | Leave encashment formula | `LeaveAndFnf.cs` `FnfFigures` | code | |
| P14 | F&F, gratuity/notice/bonus not calculated | `LeaveAndFnf.cs:42-50`; `PayrollService.Pay.cs:267-333` | caps = constants (DEC-066), rest absent | |
| P15 | Gratuity provision | absent | code | |
| P16 | Labour Codes changes | absent | code | |
| P17 | Layout of ECR / ESIC / PT files | `Payroll/PayrollFiles.cs` | code | |
| C1-C6 | GSTR-1 11th, GSTR-3B 20th, GSTR-9 31 Dec, EPF 15th, ESIC 15th, PT last day | `Compliance/ComplianceRules.cs:299…` (`DueRule`), `1100_compliance.sql` | dated setting | |
| C7 | Other obligations to track | `ComplianceRules.cs:13-15` (7 kinds fixed) | code | |
| T1 | TDS date and TDS calculation (not built) | `ComplianceRules.cs` (`Tds` obligation only) | code | |
| L1 | Full Aadhaar storage | see O-04 | — | |
| L2 | Retention, guest register, **Form C** (foreign guests) | see O-05; no "Form C" anywhere in `src` | code | |
| CA-X1 | SAC codes on invoice lines | `Invoices/GstIdentity.cs:101` | code | |
| CA-X2 | B2CL threshold ₹1 lakh; B2C ≥ ₹50,000 name/address | `Invoices/Gstr1Summary.cs:27-28` (`static readonly`) | code | |
| CA-X3 | Credit-note time limits; e-invoicing (IRN) | `CreditNoteRules.cs`; `PERM-INV-006` not seeded (`2401_invoices_rbac_seed.sql:3`) | code | |
| CA-X4 | Chart of accounts ledgers 5100/5110, 2300–2340, 1200, 2110/2120 | `Accounting/ChartOfAccounts.cs:22,39-44`; `1000_accounting.sql:6` ("fixed in code") | code | |

---

## (d) CLAUDE.md "Read first" and "Commands" truth check

### d1. Read first

| # | Line | Entry | Exists? | True as described? |
|---|---|---|---|---|
| 1 | 10 | `HANDOFF.md` — current status and next steps | yes | **No at the top**: first 61 lines are Session 2; the current state is lines 320-344 (row 6 in (a1)). On `main` it lacks the v12 paragraph (row 8) |
| 2 | 11 | `02 Planning/HCC_Plan_FY_History_and_APK_v1.md` — CURRENT plan | yes | No: plan complete; §9 has two unticked lines (D6 partial, W0-D acceptance) that APR-011 closed |
| 3 | 12 | `HCC_Mobile_Version_Plan_v1.md` — decisions DEC-044…051; module specs 13–26 in `02 Planning/modules/` | yes, yes | Yes. DEC-044…051 are **not** in the Decision Register; DEC-049 has two rows ("Open", "Approved") |
| 4 | 13 | Decision Register, Prototype Spec, RBAC Matrix v1, Workflow Spec, Fixture catalogue, Acceptance Matrix, Backlog | yes (all 7) | Register: statuses stale (see (b), A11-11). **RBAC Matrix v1 is superseded in part** by DEC-063 (revised, per RBAC rulings) and the v2 proposal; Backlog open list stale (row 23) |
| 5 | 14 | `03 Prototype/README.md` — commands, code layout, phone-over-Wi-Fi | yes | Commands table yes (verified); "Known limitations" and persistence text stale (row 18) |
| — | — | **Missing from Read first** | `08 Production/README.md`, `WAVE1_PARALLEL_RULES.md`, `HCC_Production_Contracts_and_Schema_v1.md`, `deployment/windows/INSTALL.md`, `05 Delivery Evidence/Security/Security_Review_2026-10-26.md`, `CA_Question_Pack_2026-10-25.md`, `02 Planning/agents/W4_Briefs.md` | — |

### d2. Commands (run in `03 Prototype`) and other checkable claims

| Claim | Check | Result |
|---|---|---|
| `npm install` | `package.json` has no `engines`; a lock file exists | works; `npm ci` is what `04 Android` needs |
| `npx tsc --noEmit` | `typecheck` alias = `tsc --noEmit` | true (run: A1) |
| `npm test` — "1008 tests must pass on main" | `tests/run.mjs` loads 83 files; 963 literal calls + loop expansions; recorded run file says 1008/1008 at 2026-10-26T09:35Z | consistent; **to be confirmed by A1** |
| `npm run build` → `dist/index.html` + `Hotel Control Centre.html` | `build.mjs:18` | true |
| `npm start` → http://127.0.0.1:4173, `-- --lan` for phones | `serve.mjs:5-6` (port 4173, host 127.0.0.1 / 0.0.0.0) | true |
| `npm run build:production` (named in the status line, **not in the Commands list**) | `package.json`; writes `dist/production/index.html` and exits | true, but undocumented in Commands |
| "SCHEMA (now 8)" | `types.ts` `SCHEMA=8` | true |
| "Status: server 962 (28 projects, serial `dotnet test Hcc.slnx -maxcpucount:1` on `.\SQLEXPRESS`), 0 warnings" | 28 projects; 798 Facts + 30 Theories (140 InlineData) static | **to be confirmed by A1** |
| Server commands for a developer | none in CLAUDE.md; `08 Production/README.md:29` omits `-maxcpucount:1` (row 13) | gap |
| "Node 25 first on PATH" | row 3 | already true by default on this PC; keep as a precaution |
| "Money = paise … GST per DEC-069 (≤ ₹7,500 5 % (12 % before 22 Sep 2025) / above 18 %, restaurant 5 % (18 % specified premises), other 18 %), CGST+SGST per line, half away from zero, posted lines keep taxBps" | seeds `0600…sql:71-82`, `gst.ts:73-83`, `MoneyMath.cs:24` | rules match as written; **figures = A3a** |
| "Housekeeping/maintenance never see hotel money (own pay only, DEC-061)" | `PayrollQueries.cs:35` | traced; **route-by-route = A5** |
| "New state fields optional; bump SCHEMA" | `stream-schema8.mjs` exists | not re-verified |

---

## (e) Findings

IDs `A11-nn`. Severity per the audit scale; "staleness" classes are in (a).

| ID | Sev | Area | File:line | Evidence | Impact | Fix | Hours | Conf |
|----|-----|------|-----------|----------|--------|-----|-------|------|
| A11-01 | Medium | Decision DEC-057 (module switch-off) | `08 Production/src/Hcc.Api/Endpoints/ConfigurationEndpoints.cs:31`; `Hcc.Application/Configuration/ConfigurationPorts.cs:171-211` | `RequireHccModule(` and `IModuleGate.EnsureOnAsync` have **no call site** in `src` (grep across `src` and `tests`: only the definition, doc comments and `tests/Hcc.Configuration.Tests/Sql/ConfigurationHttpTests.cs:41`, a test route). Register DEC-057 and `ConfigurationPorts.cs:168` say each switchable module "calls EnsureOn…". Client hides pages/commands (`03 Prototype/src/domain/streams/config.ts:58-75`; `clients/sync/admin.ts:120`) | Owner switches Payroll / POS / OTA / web booking / messaging / documents / stock / maintenance / assistant off in Master configuration: the screens vanish but the server still executes every command from any client (API, old APK, other device) whose role holds the grant. "Off" is cosmetic on the server | Add `.RequireHccModule("<id>")` to the command routes of the 9 switchable modules + one refusal test per module | 6 | High |
| A11-02 | Medium | Data protection / configuration | `Hcc.Application/Configuration/BuiltinSettings.cs:132` vs `Hcc.Application/Documents/DocumentPorts.cs:24`; `DocumentService.cs:205-225`; `Hcc.Infrastructure.SqlServer/Access/SqlRetentionPurge.cs:23-25` | Two retention settings in group "Data and system": `doc.retentionDays` (default **365**, help "Captured images are discarded after this") and `documents.retentionDays` (default **90**, `DocumentRules.cs:18`). Only the second is read by the purge. The purge is the POST command `PurgeIdCaptures` (`DocumentsEndpoints.cs:51`); nothing in the prototype (`grep PurgeIdCaptures` = 0) or any background job calls it (`RetentionWorker` purges idempotency rows and sessions only). HANDOFF:326 itself asks "nightly purge?" | The Owner can change the visible "Captured document retention" setting and nothing happens; ID images (sealed Aadhaar scans) stay until someone calls the API by hand. Adds to the DEC-068 legal exposure (O-04) | Delete the `doc.retentionDays` duplicate (server catalogue + prototype `config.ts:504`), add a purge screen or schedule `PurgeIdCaptures` in `RetentionWorker` with an audit row | 5 | High (duplicate keys, no caller); Medium (that no other trigger exists outside `src`) |
| A11-03 | Low | Decision DEC-067 (prototype) | `03 Prototype/src/services/mock/handlers/folio.ts:244`; `handlers/guests.ts:100`; `services/mock/commands.ts:72,124` | Checkout and room-move cleaning tasks are pushed with `assignee:'housekeeper'`; DEC-067 (APR-012) says created unassigned, supervisor assigns. Server does it right: `SqlHousekeepingStore.cs:134`, `StayoverCommands.cs:27` | Accepted prototype and production disagree; production is correct. Only matters if the prototype is used for training | Set `assignee:undefined` at the 4 sites and update the 2–3 tests that expect it | 2 | High |
| A11-04 | Low | Docs — ETP availability | `CLAUDE.md:7`; `ETP_Reuse_Pending.md:3`; `HANDOFF.md:341`; `MIGRATION.md:26` | `E:\ETP\Code` exists; `V:\ETP` does not (a1 row 1) | Wave 5 ETP alignment is planned as blocked when it is not | Correct the four statements; give the new path | 0.5 | High |
| A11-05 | Low | Docs — entry point | `HANDOFF.md:1-61`; `CLAUDE.md:5,10,11,16` | (a1) rows 2, 4–8 | New agent/developer reads Session 2 first, then "CURRENT plan" that is finished, and a layout list without the server | Move a 10-line "Current status" to the top of HANDOFF; refresh CLAUDE.md status, Read first and Layout; reconcile `main` with `c0c477d` | 1.5 | High |
| A11-06 | Medium | Docs — go-live guide | `08 Production/deployment/windows/INSTALL.md:84,129-136` | Masters import (W4-13: `ImportEndpoints.cs`, `2700_import.sql`, `deployment/import-templates/`) is not a step; checklist has no "rooms, tariffs, staff, opening balances loaded". INSTALL says master data is "not part of this installer" without a pointer | An owner following only INSTALL.md ends at a signed-in empty hotel and no instructions for what the first day needs | Add §6d "Load your masters" (order, templates, opening balances, re-import) + checklist line; `Test-HccDeployment.ps1` stays as is | 1.5 | High |
| A11-07 | Low | Docs — server README | `08 Production/README.md:3,14,15-25,29,42-43` | (a1) rows 10–14 | Wrong merge status, 28 of 46 migrations/projects not described, and a test command without `-maxcpucount:1` that times out on shared SQL | Refresh the README; add the serial flag | 1.5 | High |
| A11-08 | Low | Docs — Android README | `04 Android/README.md:3-4,10-12,23-24,79` | (a1) rows 16, 17: `java -version` of the named JBR is 25.0.3; SDK path wrong; `V:\` paths; "debug only, no backend" | Rebuilding the hotel tablets' release APK from this README fails at step 1 | Use the JDK 21 path already in the Production section, `E:\Android\Sdk`, `npm ci`; drop the stale header/limits | 1 | High |
| A11-09 | Low | Docs — prototype & sync READMEs | `03 Prototype/README.md:9,44,56,77,82-88`; `src/clients/sync/README.md:47,85-89` | (a1) rows 18–21 | Wrong "known limitations"; sync how-to example uses a command that does not exist | Rewrite the two blocks | 1 | High |
| A11-10 | Low | Docs — backlog | `02 Planning/HCC_Implementation_Backlog.md:59-100` | (a1) rows 23–24 | The only file titled "backlog" shows closed defects as open | Replace the status/open tables by a pointer to register (c) once the lead merges it | 1 | High |
| A11-11 | Medium | Decision Register governance | `02 Planning/HCC_Decision_Register.md` (header L3; rows DEC-022/023/027/029/039/043/008) and `HCC_Mobile_Version_Plan_v1.md:122-146`; (b2) | Statuses stale or contradicted (DEC-029 "Needs owner decision" yet approved; DEC-027, 039, 022, 008 changed without a §4 entry; DEC-023 "half-up" vs half-away; DEC-043 after DEC-069; "Updated 24 Oct"); DEC-044…051 absent (DEC-049 twice); **25 agent-chosen production rules with no owner approval on record** ((b2): early-departure fee, downgrade approval, OTA party change, attrition, 180-day window, own-shift definition, bank-detail approver, …) | The register is the document the owner relies on to know what he approved; it under-reports both | Update statuses/§4, add DEC-044…051, give each (b2) item a DEC id as "Proposed — owner to confirm" so the owner can answer yes/no | 3 | High |
| A11-12 | Medium | CA pack promise | `05 Delivery Evidence/CA_Question_Pack_2026-10-25.md:5,89` vs `Invoices/Gstr1Summary.cs:28`, `GstIdentity.cs:101`, `Accounting/ChartOfAccounts.cs:22`, `LeaveAndFnf.cs:12,45-50`, `InvoiceRules.cs:99-100` | "no software change is needed for rates, thresholds or due dates" — true for GST rates, payroll rate versions, due dates; false for the items listed in (a1) row 28 | The CA answers G3, G7, G10–G12, P2, P11, P15, P16, C7, T1, L2 and CA-X1…X4 will need code, not a setting; the owner has been told otherwise | Reword the pack (two columns: "setting" / "needs code") from table (c2) once A3a/A4 fill it | 1 | Medium (classification is first reading; A3a/A4 confirm) |
| A11-13 | Low | Prototype comment | `03 Prototype/src/clients/sync/capabilities.ts:32` | Cites `PERM-HOV-*`, `PERM-MNT-*`, `PERM-POS*` as existing; none is in any migration (proposal only, `Matrix_v2:83-87`) | Misleads the next person who maps module permissions | Fix the comment | 0.2 | High |
| A11-14 | Info | Static counts | `evidence/A11/counts.json` | Prototype: 963 literal `test(` + 4 loop sites vs claim 1008; a results file on disk shows 1008/1008 (09:35Z today). Server: 798 `[Fact]` + 30 `[Theory]` (140 InlineData) vs claim 962; 46 migrations; 28 test projects; `SCHEMA=8`; 85 seeded PERM ids | Nothing contradicts the HANDOFF numbers; nothing proves them either | A1 runs the suites | — | High |
| A11-15 | Info | INSTALL.md ↔ scripts | `evidence/A11/install_check.json` | 17 script references, 27 `-Flags`, config keys, routes and `Program.cs migrate` all exist and match; documented times/limits match script defaults | The owner's install guide is accurate to the scripts (what it has never had is a run on a real PC; INSTALL:5) | — | — | High |
| A11-16 | Info | Plan facts (for the lead) | `Audit_Multi_Agent_Plan_2026-10-26.md` §0 | `APK_v12_Emulator_Report.md` exists and is tracked (`f0a5a29`; `Acceptance/v12/` = 107 tracked files), so fact 13 and §9.4 are obsolete; 46 migrations (not 47); 293 map calls in 32 files (plan: 291/31); `c0c477d` is on the audit branch only | Plan statements need the correction before the report cites them | Lead edit | 0.2 | High |

---

## (f) Closing lists

### Claims I could not confirm

1. **Test and build numbers** (prototype 1008, server 962, "28 projects serial", "0 warnings", "0 leftover HccTest DBs", dist/bundle sizes, SHA-256 of `dist`): counted statically only. A results file for 1008/1008 exists on disk (git-ignored) but I did not run anything. **To be confirmed by A1.**
2. "All 22 security findings fixed except F-M03" and the `F-M03` partial fix described in `08 README` (A6).
3. DEC-063's 13 rulings route by route and DEC-061 own-pay on every route (A5); I sampled G1, G2, G4 only.
4. DEC-068 behaviour (AAD binding, no plaintext in logs/audit/outbox/idempotency/exports) — traced to the code that claims it, not tested (A7).
5. Golden figures (room 105 ₹4,265, drawer ₹10,725 / ₹12,425, GB-1/GB-2/CO-1/CO-2) quoted in README/HANDOFF (A3a/A3b).
6. That INSTALL.md steps work on a clean PC: scripts and flags exist and match; nothing was executed (A9). No evidence that `HCC-Server-Setup-<version>.exe` was ever built.
7. "Secret scan clean", E2E run results, APK v12 emulator results — documents only (A1, A10). I did verify the two v12 APK hashes match HANDOFF:343.
8. Whether the DEC-057 switch-off is enforced elsewhere than by `RequireHccModule`/`IModuleGate` (e.g. inside a service by reading `module.<id>` directly): grep for `SettingKey(`/`IsOnAsync(` finds only the gate itself and the configuration queries. High confidence, but A8 should confirm with a test that switches a module off over HTTP.
9. `E:\ETP\Code`: its existence is confirmed; its contents, version and fitness for the 38 stand-ins were **not** looked at (out of scope).
10. The prototype's behaviour for DEC-065 at every locked-day entry point beyond the call sites listed; the server twin for "refund of a receipt dated on a locked day" does not exist because server receipts are dated at the current business date (A3b).

### Open owner / CA decisions I hit (and the code that depends on each)

The full de-duplicated register is table (c). The ones that gate code or go-live most directly:

| Decision | Code that depends on it |
|---|---|
| DEC-068 / L1 Aadhaar full-number storage (CA/lawyer) and retention/purge (O-04, O-05) | `GuestIdRules.cs:8`, `0601_guest_id_documents.sql`, `DocumentService.cs:205-225`, `BuiltinSettings.cs:132` |
| DEC-028 closed-date correction model (O-06) | `BillingService.cs:242,338`, `ClosingEndpoints.cs:15` |
| Release keystore, hotel CA, key USB custody (O-01–O-03) | `04 Android/android/app/build.gradle:39-75`, `New-HccCertificate.ps1`, `New-HccGuestIdKey.ps1`, `New-HccBackupKey.ps1` |
| Role matrix v2 D1–D7 and `PERM-CLS-005` (O-13, O-14) | `0101_access_rbac_seed.sql`, `ClosingPorts.cs:19`, `capabilities.ts:32` |
| 25 agent-chosen production rules awaiting owner yes/no (b2): attrition, early-departure fee, downgrade approval, OTA party change, booking window, own-shift scope, bank-detail approver, quote lifetime | files in table (b2) |
| CA G1–G12 (GST), P1–P17 (payroll), C1–C7, T1, L1–L2, CA-X1–X4 | table (c2) |
| Web booking on the internet (reverse proxy, CAPTCHA) | `HccHosting.cs`, INSTALL §10, `WebBookingService.cs:235` |

### Questions for the lead

1. `c0c477d` (HANDOFF v12 paragraph, "next session: audit") is on `claude/audit-fable-2026-10-26` only; `main` = `f0a5a29`. Is that intended? At merge time `main`'s HANDOFF will lack the v12 results unless this commit is carried over.
2. Plan §0 fact 13 and §9.4 are obsolete (v12 report exists and is tracked); 46 migrations not 47; 293 route maps in 32 files (not 291/31). Do you want A5 to reconcile the route count with the review's 373?
3. A11-01 (module switch-off not enforced on the server): please have V1 or A8 reproduce it with a switched-off `pos` module and a `POST /commands/…` from a role with the grant. I only did a grep.
4. A11-02 (two retention settings, no scheduled purge) overlaps A7 — merge it into A7's data-protection section and let A7 own severity (I would not go below Medium given DEC-068).
5. For the report to the owner, shall the 25 rows of (b2) be phrased as yes/no questions? I have given each its code location so the question can state "today the system does X".
6. Should I (or another agent) file the corrections for the docs in (a1) as a fix list for after the audit? I have not changed any document (audit-only).
7. HANDOFF says ETP is not on this PC; `E:\ETP\Code` is. Confirm A9 should treat the 38 `ETP-REUSE` rows as "alignable now" in the go-live plan but not read ETP itself.
