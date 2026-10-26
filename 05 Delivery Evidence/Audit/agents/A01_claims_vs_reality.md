# A01 — Claims vs reality (26 Oct 2026)

Agent A1 (Sonnet). Repo `E:\Projects\HCC\Hotel Control Centre`, branch `claude/audit-fable-2026-10-26` (HEAD `c0c477d`), working tree clean apart from the Audit folder and the untracked plan file. Read-only on product code; all evidence is in `05 Delivery Evidence\Audit\evidence\A01\`, probes in `05 Delivery Evidence\Audit\probes\A01_*.mjs`. I held `locks\dotnet-test.lock` from 14:56:50 to 15:15:01 IST and released it before the prototype checks. Severity is only Info/Low here; the lead grades.

## 1. Result in one paragraph

Every headline number in HANDOFF 12b and CLAUDE.md reproduces: server **962 tests in 28 projects, 0 warnings**, prototype **1008/1008**, `tsc` clean, both web builds succeed, secret scan clean, no APK/keystore/key tracked, versionCode 12 in `build.gradle` and in both v12 APKs, the two v12 APK SHA-256 values in the report match the files on disk, and a fresh build of today's source is **byte-identical** to the web bundle inside both v12 APKs. Two honest caveats: (a) one server test (`Hcc.Operations.Tests.DeploymentScriptTests`) fails when `dotnet test` is started from a PowerShell 7 session (inherited `PSModulePath`), so the first run was 961/962; with a clean `PSModulePath` it is 962/962 (A01-01); (b) small documentation/report inaccuracies: "45 migrations" is 46, a v12 table does not add up, CLAUDE.md/READMEs have stale lines, and the "E2E run 2 all pass" reading overstates what the report says (A01-02 to A01-06).

## 2. Environment and commands

| Item | Value | Evidence |
|---|---|---|
| Host | Windows 11, `.\SQLEXPRESS`, E: free 287.6 GB before / 286.9 GB after (HANDOFF said 86 GB at the time of the full disk; not comparable) | `disk-before.txt`, `disk-after.txt` |
| .NET SDK | 10.0.401 | `dotnet-version.txt` |
| Node | v25.9.0 from `E:\Tools\nodejs`, npm 11.12.1 | `node-version.txt` |
| Other load | ETP session ran `dotnet test` on `Etp.Reporting.StressGen.Tests` and used the same SQL instance during my run | `Get-CimInstance Win32_Process` at 14:56 |
| `dotnet build Hcc.slnx` | **Build succeeded, 0 Warning(s), 0 Error(s)**, 1 min 01 s | `dotnet-build.txt` |
| `dotnet test Hcc.slnx -maxcpucount:1 --no-build` | 28 projects, **962 total, 961 passed, 1 failed**, 0 skipped, wall 15 min 09 s (under ETP CPU contention) | `dotnet-test.txt` |
| Operations project re-run with `PSModulePath` reset to the Machine value | **39/39 passed** → 962/962 | `dotnet-test-operations-rerun.txt` |
| HccTest_* databases after the run | **none** (sys.databases = EtpReporting, EtpScratch_20261020, system DBs). Later in the session, other agents' DBs appeared (`HccTest_A08outbox_…` at 15:14 while I held the lock, `HccTest_perf_…` at 15:17+): not mine, not touched | `sql-hcctest-after-tests.txt`, `sql-hcctest-final.txt` |
| `npx tsc --noEmit` | exit 0, no output, 25 s | `tsc.txt` |
| `npm test` | **1008/1008 tests passed**, 1008 PASS lines, 0 FAIL lines, 1 min 44 s | `npm-test.txt` |
| `npm run build` | `dist/index.html` 2,312 KB (2,367,400 bytes), exit 0, no warnings | `npm-build.txt` |
| `npm run build:production` | `dist/production/index.html` 2,500 KB (2,559,813 bytes), exit 0, no warnings | `npm-build-production.txt` |

Per-project server results (all from `dotnet-test.txt`, duration in s as printed):

| Project | Passed | Project | Passed | Project | Passed |
|---|---|---|---|---|---|
| Access | 83 | Handover | 28 | Ota | 39 |
| Accounting | 36 | Import | 38 | Payroll | 32 |
| Billing | 36 | Integration | 14 | Platform | 56 |
| Closing | 42 | Inventory | 38 | Pos | 38 |
| Compliance | 22 | Invoices | 29 | Stays | 57 |
| Configuration | 30 | Maintenance | 18 | Stock | 33 |
| Customers | 27 | Messaging | 21 | WebBooking | 21 |
| Documents | 22 | **Operations** | **38 of 39 (1 failed first run; 39/39 after env fix)** | Yield | 36 |
| Expenses | 18 | GstIds | 38 | Groups | 32 |
| GuestServices | 39 | | | | |

Sum 962. HANDOFF does not give per-project numbers; only the total and "28 projects" (confirmed: 28 test csproj + 6 src csproj = 34 `<Project>` entries in `Hcc.slnx`, `static-facts.txt`).

## 3. Findings

| ID | Sev | Area | File:line | Evidence | Impact | Fix | Hours | Conf |
|----|-----|------|-----------|----------|--------|-----|-------|------|
| A01-01 | Low | Test reproducibility | `08 Production/tests/Hcc.Operations.Tests/DeploymentScriptTests.cs:29-31` (starts `powershell.exe`) | First full run: `Failed Hcc.Operations.Tests.DeploymentScriptTests.Deployment_scripts_pass_their_dry_run_tests`; inner script reports 4 FAIL, all "The 'Get-Acl' command was found in the module 'Microsoft.PowerShell.Security', but the module could not be loaded" (`dotnet-test.txt` lines ~80-125). The shell I ran from is PowerShell 7 whose `PSModulePath` includes `e:\tools\powershell\7\Modules`; Windows PowerShell 5.1 inherits it and cannot load its Security module. With `PSModulePath` set to the Machine value the same project passes 39/39 (`dotnet-test-operations-rerun.txt`) | HANDOFF/CLAUDE.md say 962/962 without mentioning this; anyone running `dotnet test` from PowerShell 7 (the usual shell here) gets one red test and the 4 ACL checks of the deployment scripts (the F-H03/F-M06 fixes) are silently not exercised | Make the test clear/replace `PSModulePath` in `ProcessStartInfo.Environment`, or document "run from Windows PowerShell / reset PSModulePath" in `08 Production/README.md` | 0.5 | High |
| A01-02 | Low | Docs accuracy | `05 Delivery Evidence/Acceptance/APK_v12_Emulator_Report.md` ("migrated 0 -> 2702, 45 migrations"); same wording in `W4_E2E2_Report.md:14` ("0 -> 2702, 45 migrations"), `W4_E2E_Report.md:14` ("0 -> 2701, 43 migrations") | `database/migrations/*.sql` = **46** files (`static-facts.txt`). Fresh disposable DB `dotnet run --project src/Hcc.Api -- migrate`: "Schema version 0 -> 2702; applied [1, 2, 100, … 2702]", journal `COUNT(*)` = **46**, MIN 1, MAX 2702 (`migrate-fresh-db.txt`). The 46th is `2202_groups_company.sql`, merged in `5f0f885` (11:54) after E2E2 | The v12 report copied a stale count; harmless, but "45" is wrong for the code the APK was tested against | Correct the count (or say "migrations up to 2702") | 0.1 | High |
| A01-03 | Low | Docs accuracy | `APK_v12_Emulator_Report.md`, section "Server APK, phone AVD": tally "Pass 12, Fail 2, Partial 1, Not completed 1" | The table has **17** rows S1-S17. Counting its own Result column: Pass S1-S6, S8, S10-S14 = 12, plus S16 "Pass with note" = 13, Fail S7 and S17 = 2, Partial S15 = 1, Not completed S9 = 1 → 17. The report's tally sums to 16. HANDOFF line 343 repeats "12 pass / 2 fail / 1 partial / 1 not completed" | A test count in the acceptance evidence is off by one | Fix the tally (13 pass incl. S16) | 0.1 | High |
| A01-04 | Low | Repo state vs briefing | `git rev-parse main` = `f0a5a29`; `git branch --contains c0c477d` = audit branch only | The instruction text says `main` moved to `c0c477d` and HANDOFF has the v12 paragraph. In the repo `main` is `f0a5a29` (APK v12 report + screenshots); `c0c477d` ("HANDOFF: APK v12 emulator results, next-session audit prompt", parent `f0a5a29`) exists only on the audit branch. So the HANDOFF v12 paragraph and the audit prompt are not on `main`. Also the plan's §0 row 13 is stale: the `Acceptance/v12/` folder (107 PNG: a 33, s 51, t 23) and the v12 report are **tracked** in `f0a5a29`, `git status` shows nothing untracked there | If the audit branch is discarded, `main` loses the HANDOFF entry; the audit commit must not carry a clean `v12\` exclusion | Lead to decide whether `c0c477d` should be moved onto `main`; adjust the plan's commit step | 0.2 | High |
| A01-05 | Low | Docs accuracy | `CLAUDE.md:5` ("APK v12 (versionCode 12) = next build/test"); `CLAUDE.md:7` (Node 24 first on PATH); `08 Production/README.md:1-3` ("not merged into main", migrations listed only up to 0601); `03 Prototype/README.md` (Node 22, `tests/ run.mjs + m1.mjs`, 88 test files exist); `04 Android/README.md:` SDK path `C:\Users\Sagar\AppData\Local\Android\Sdk` | v12 is built and tested (`aapt dump badging` versionCode 12 on both APKs, `apk-aapt.txt`); Machine PATH now lists `E:\Tools\nodejs` (v25.9.0) at entry #0 and `E:\Program Files\nodejs` (v24.13.1) at #13, `Get-Command node -All` lists Node 25 first (`node-path-order.txt`); the Android SDK is at `E:\Android\Sdk` (build-tools 35, 36) | Misleading for the next session; A11 owns the full stale-statement table | Edit the five lines | 0.5 | High |
| A01-06 | Low | Evidence wording | `HANDOFF.md:337` ("run 2 all D1–D7 fixed + payroll/accounting/B2B/refund/credit note/groups/OTA/stock pass"); briefing wording "E2E run 2 all pass" | `W4_E2E2_Report.md:56` totals: "all D1-D6 fixed; 1 area FAIL (company account save), 5 PASS with an API workaround"; D8 and D10 PARTIAL; 11 new defects N1-N11 (N1-N5 are S2). HANDOFF 12b says N1-N3 were fixed afterwards (`5f0f885` etc.), but I found **no third E2E run**: the N1-N3 fixes are covered by unit/mapper tests only | "Run 2 all pass" is true only for the D-list; the end-to-end fixes for company accounts, OTA channel add, stock item master, departments/joining date, opening TB, CSP and display names were not re-walked against the real server | Re-run the E2E walk (or the relevant parts) before go-live | 4 | Medium |
| A01-07 | Info | Confirmed good | see claim table | 962/962 (after env fix), 1008/1008, 0 warnings, tsc clean, builds OK, **byte-identical** reproduction of both v12 APK web bundles from current source (`apk-web-bundle-compare.txt`: demo `B3CC915F…FE24` = `dist/index.html`; server `F0D8B928…1463` = `dist/production/index.html`; the pre-existing `dist\` files had the same hashes as my rebuild, `dist-before.txt` / `dist-after.txt`) | The v12 test evidence belongs to the code in the tree | none | 0 | High |
| A01-08 | Info | Confirmed good | `secret-scan.txt`, `apk-and-tracked-files.txt` | `git ls-files` has no `.apk .aab .jks .keystore .pfx .p12 .pem .key .crt .cer .der .env .snk`; `git grep` for private-key headers, `Pwd=`, `User Id=`, AWS/GitHub/OpenAI key shapes, `Server=…Password` finds nothing but a unit test string and fictional/dummy test values (list below) | HANDOFF "secret scan clean" holds | none | 0 | High |
| A01-09 | Info | Coordination | lock file, `sql-hcctest-final.txt` | `HccTest_A08outbox_20261026094452` (UTC 09:44 = 15:14 IST) existed at 15:15 while A1 held `dotnet-test.lock`; `HccTest_perf_20261026094934` (A9) at the end. Neither is mine; they were not there when my test run ended (15:13) | The "no leftover DBs" check can only be asserted at the moments I looked: before the run (none) and right after it (none) | Lead: final leftover check after all agents finish | 0.1 | High |

Secret-scan hits that are not secrets (all reviewed): the test string `-----BEGIN PRIVATE KEY-----\nAAAA…` in `04 Android/scripts/build-production.test.mjs:17` (checks that a PEM with a private key is refused); `PASSWORD='Sand-Residency-2026!'` and PIN constants in `03 Prototype/tests/sync-*.mjs`; `Password=x` and `Password=hunter2` in `08 Production/tests/Hcc.Operations.Tests/HostingTests.cs:144,214` (tests that a bad connection string is refused / not echoed); `OwnerPassword = "Different#Secret77"` in `Hcc.Integration.Tests/SetupTests.cs:122`; docs/gradle references to `storePassword` (property names, no values) and `trustStorePassword=changeit` in `04 Android/README.md:17` (the JDK default, build-time trust store).

## 4. Claim table (HANDOFF Sessions 10–12b and CLAUDE.md status lines)

How-checked codes: R = re-ran it; F = file/count/hash check; G = git; S = static read; N = not checkable.

| # | Claim | Where stated | How checked | Verdict |
|---|---|---|---|---|
| 1 | Server tests 962/962, 28 projects, serial `dotnet test Hcc.slnx -maxcpucount:1` on `.\SQLEXPRESS` | HANDOFF:338; CLAUDE.md:5 | R: 962 total, 961 pass on first run, 962 after PSModulePath fix; 28 test csproj (`static-facts.txt`) | **Confirmed** (with the PS7 caveat, A01-01) |
| 2 | Prototype tests 1008/1008 | HANDOFF:338; CLAUDE.md:5,20 | R: `npm test` → "1008/1008 tests passed" | **Confirmed** |
| 3 | `dotnet build` 0 warnings | HANDOFF:338; CLAUDE.md:5 | R: 0 Warning(s), 0 Error(s) | **Confirmed** |
| 4 | Prototype `tsc` clean | CLAUDE.md commands; HANDOFF 11 | R: exit 0, no output | **Confirmed** |
| 5 | "Both web builds" OK (`npm run build`, `npm run build:production`) | HANDOFF:338; CLAUDE.md:5 | R: both exit 0, no warnings | **Confirmed** |
| 6 | 0 leftover HccTest databases | HANDOFF:318,323,327 | R: sys.databases before and right after the run | **Confirmed** (see A01-09 for later DBs of other agents) |
| 7 | Web build is a single self-contained HTML (no CDN/external fonts) | CLAUDE.md architecture rules | F: probe `A01_selfcontained.mjs`: 0 `<script src>`, 0 external `<link href>` (3 `data:` links), 0 `src/href="http…"`, 0 `url(http…)`, 0 font/CDN hosts. Only `http(s)://` literals are XML/SVG namespaces, `react.dev/errors/`, `https://demo.invalid/…`, and (production) the server-address placeholders `https://192.168.1.20:5443`, `https://hcc-server.local:8443`; one `@import url('data:text/css,')` (empty data URL) | **Confirmed** |
| 8 | Production build has no demo controls / mock adapter | HANDOFF:306,336; plan | R/F: the assertion is test `[P5] Production bundle: no demo toolbar, scenario switcher, simulated clock chip, Reset or mock adapter; HTTP adapter present` (`03 Prototype/tests/stream-clients.mjs:249`, list at :248) which passed in `npm test`; probe confirms "Demo controls" absent in `dist/production/index.html`, present in `dist/index.html` | **Confirmed** (note: "Preview as" text remains in production, which the v12 report lists as APK12-4) |
| 9 | `SCHEMA` is 8 | CLAUDE.md:24+ | F: `03 Prototype/src/domain/types.ts:266 export const SCHEMA=8` | **Confirmed** |
| 10 | Migration numbering: gapped blocks 0001…2702, specific files named (0202, 0203, 0301, 0600/0601, 1201, 1900, 2000/2001, 2100/2101, 2200-2202, 2300/2301, 2400/2401, 2500/2501, 2700-2702) | HANDOFF:307-327,336 | F: all named files exist; 46 files; fresh-DB migrate applied 46, 0 → 2702 | **Confirmed** (count is 46, see claim 11) |
| 11 | "migrated 0 → 2702, 45 migrations" | APK_v12 report; W4_E2E2:14 | R: fresh DB journal COUNT = 46 | **Not confirmed** (A01-02) |
| 12 | `0101` seeds 45 matrix PERMs; module PERM ids per module (HR-001…009, WEB-001, DOC-001/002, CRM-001/002, GRP-001…005, INV-001/002/003/005, OTA-001…003) | HANDOFF:314,321,327 | F: `grep` of the seed files: 0101 has 45 distinct PERM ids; 0901 PERM-HR-001…009; 2001 WEB-001; 2101 DOC-001/002; 2301 CRM-001/002; 2201 GRP-001…005; 2401 INV-001/002/003/005 (+ referenced FOL-004, INV-006); 2501 OTA-001…003 | **Confirmed** |
| 13 | Golden figures: room 105 ₹4,265 incl. ₹265 GST; BASE drawer ₹10,725; S0 drawers ₹10,725/₹11,225/₹11,225/₹12,725/₹12,425 | HANDOFF:298,299,310; 08 README | F: asserted in prototype tests (`m1.mjs:50-53`, `stream-f.mjs:180`) and server tests (`Hcc.Closing.Tests/Sql/CloseJourneyTests.cs:110,156`: folio 400000 + 26500 = 426500; drawers `[1072500,1122500,1122500,1272500,1242500]`); all those suites pass. Independent recomputation is A3a/A3b's job | **Confirmed** (as pinned by tests) |
| 14 | Payroll golden: Aug ₹2,12,101.66 and Sep ₹2,08,889.00 | HANDOFF:264,313 | F: `Hcc.Payroll.Tests/Domain/PayrollRulesTests.cs:274` (21,210,166) and `:282` (20,888,900), golden json; suite passes | **Confirmed** (as pinned by tests) |
| 15 | Customers golden: FY26 1,279 bookings / 1,155 customers | HANDOFF:321 | F: `Hcc.Customers.Tests/Golden/customers-golden.json`: bookingCount 1279, customers 1155 | **Confirmed** |
| 16 | Room 105 "₹4,200 incl ₹200 GST" in the integration test | 08 README (Integration row) vs ₹4,265 elsewhere | F: 2 nights × ₹2,000 at 5 % in a different scenario than S0 (no laundry/discount); not a contradiction | **Confirmed** (different scenario) |
| 17 | Invoice number format `INV/2627/0000001` | HANDOFF:327 | F: appears in `W4_E2E2_Report.md` (INV/2627/0000001, …0003) | **Confirmed** (A3a checks the rule) |
| 18 | Security review: 22 findings (4 H / 7 M / 11 L) + 6 Info | HANDOFF:336; plan | F: `Security_Review_2026-10-26.md` summary table 4/7/11/6; ids F-H01…H04, M01…M07, L01…L11, I01…I06 present | **Confirmed** (count); fixes are A6's job |
| 19 | "All 22 findings fixed except F-M03 forced change after admin reset" | HANDOFF:336 | S: no `must_change`/`force_change` anywhere in `08 Production` or `03 Prototype/src` (`git grep`); the Owner-only part of F-M03 is described in `08 Production/README.md` ("Money-role accounts (F-M03)") | **Confirmed** for the open part (forced change is absent); whether the other 21 hold is A6 |
| 20 | Security S1/S2/S3 fix commits `4b70254`, `779c79d`, `08fd127`, `621cb6a` | HANDOFF/plan | G: all four exist; messages match the claimed content | **Confirmed** (existence/message) |
| 21 | Commits named in Sessions 10-12b exist (7ac3e2f, 3ba9559, bbee681, 0803e14, f0a5a29, b629b5e, 26f1abf, 5f1de52, 4f46af8, 28a7475, a5decb3, 6b3e040, bf86f07, 83d7d9e, b04f596, 619e0f8, e0bcfa7, 865e76a, 1212aab, 6346a79, 549235e, 4bfe0d2, b64b360, efafbd7, fa90145, ee46ee6, 61e1369, b12c6eb) | HANDOFF | G: `git cat-file`/`git log -1` on every commit id named (`git-facts.txt`); 10 spot-checked are ancestors of `main` | **Confirmed** |
| 22 | `main` = Waves 1–4, Wave 4 merged at `7ac3e2f` | HANDOFF:334; CLAUDE.md:5 | G: `7ac3e2f` is an ancestor of `main` (`f0a5a29`) | **Confirmed** |
| 23 | Pushed to GitHub: `main` at `b04f596` and `claude/wave1-integration` at `a5decb3`; nothing pushed since; "173+ commits ahead" | HANDOFF:290-291,332 | G: `git ls-remote --heads origin` → `main b04f596…`, `claude/wave1-integration a5decb3…`; `git rev-list --count b04f596..main` = **271** (local `origin/main..main` = 270). "173+" is a lower bound that has since grown | **Confirmed** (figure outdated) |
| 24 | Repo is public GitHub `sagarbora91/HCC` | HANDOFF:290; audit prompt | F: `git remote -v`, `ls-remote` answered unauthenticated | **Confirmed** |
| 25 | `core.autocrlf=false` | HANDOFF:255 | F: `git config core.autocrlf` = false | **Confirmed** |
| 26 | Secret scan of `main` clean; no keys/keystores/APKs tracked; connection strings use Integrated Security; only dummy test passwords | HANDOFF:332,291 | R: see A01-08; `appsettings.json:11` uses `Integrated Security=True` | **Confirmed** |
| 27 | APKs are git-ignored (`*.apk`) | HANDOFF:285; v12 report | F: `git check-ignore -v` → `.gitignore:21:*.apk` for both v12 files; `.gitignore` also has `*.jks`, `*.keystore`, `04 Android/.gitignore` has `keystore.properties` | **Confirmed** |
| 28 | `versionCode 12` (APK v12) at commit `b629b5e` | HANDOFF:343; CLAUDE.md:5 | F/G: `build.gradle:30 versionCode 12`; `git log -- build.gradle` top entry `b629b5e Android: versionCode 12`; `aapt dump badging` on both v12 APKs: `versionCode='12'` | **Confirmed** |
| 29 | Earlier versionCodes: 7 (`28a7475`), 8, 9 (`652adce`), 10 (`26f1abf`), 11 (`bbee681`) | HANDOFF | G: all in `git log -- build.gradle` | **Confirmed** |
| 30 | v12 report SHA-256 prefixes: demo APK `39C6C9F7846C94A1…`, server APK `3635D571F794861E…`; v11 kept as `-v11.apk` | APK_v12 report | F: full hashes computed: `39C6C9F7846C94A136B4…`, `3635D571F794861EB152…`; `HCC-0.1.0-proto-debug-v11.apk` present | **Confirmed** |
| 31 | Older APK hashes: v8 `2BA3FE68…52C3`, v9 `600ABA41…2369`, v10 `1CA1616A…2846`, v11 `D5D2F9DD…` | HANDOFF:285,299,331 | F: files on disk hash to exactly these prefixes/suffixes | **Confirmed** |
| 32 | v7 APK SHA `E87C692B…73EF`, "web bundle byte-identical to dist" for v7/v8/v9 | HANDOFF:216,255,285 | v7 file no longer exists; older dist not reproducible from today's tree | **Could not check** |
| 33 | v12 APK web bundles equal the built web app ("web bundle byte-identical") | implicit in v12 report | F: rebuilt `dist/index.html` = `assets/public/index.html` of the demo APK (`B3CC915F…`), rebuilt `dist/production/index.html` = server APK (`F0D8B928…`); server APK also carries `res/raw/hcc_hotel_ca.pem` and `network_security_config.xml` | **Confirmed** |
| 34 | Server APK is HTTPS-only with test CA, debuggable ("productionDebug") | v12 report | F: `aapt`: `usesCleartextTraffic=0x0`, `networkSecurityConfig` present, `application-debuggable` | **Confirmed** |
| 35 | Demo APK package `in.hcc.prototype`, server `in.hcc.prototype.server`, release `in.hcc.server` | v12 report | F: aapt shows the first two; `build.gradle` `applicationId.set('in.hcc.server')` for productionRelease | **Confirmed** |
| 36 | `productionRelease` verified not debuggable, `allowBackup=0`, no cleartext (throwaway keystore, APK not kept) | v12 report; HANDOFF:343 | S: build.gradle `productionRelease { debuggable false … }`; `src/productionRelease/AndroidManifest.xml`: `allowBackup="false"`, `usesCleartextTraffic="false"`; signing config refuses a keystore inside the repo or unset (`hccSigningProblem`). The throwaway release APK itself is gone | **Confirmed by configuration; the built release APK could not be checked** |
| 37 | No APK in `05 Delivery Evidence\APK` is a release build | plan A10 | F: both current APKs are debuggable (`application-debuggable`); the older 4 are `proto-debug` by name | **Confirmed** for v12 files (older four by name only) |
| 38 | `Acceptance\APK_v12_Emulator_Report.md` exists and v12 screenshots are committed | HANDOFF:343 (plan said absent) | F/G: report present; `Acceptance/v12/` 107 files tracked at `f0a5a29`; `git status` shows nothing untracked there | **Confirmed** (plan §0 row 13 is stale, A01-04) |
| 39 | v12 phone/tablet emulator results (demo 10/10; server phone 12 pass / 2 fail / 1 partial / 1 not completed; tablet 7/7; "No S1/S2") | HANDOFF:343; report | S: counted the report's own tables: demo A1-A10 = 10 pass (A11 n/a); tablet T1-T7 = 7 pass; server phone 17 rows = 13 pass (incl. S16 with note) + 2 fail + 1 partial + 1 not completed; defect table APK12-1…10 has S3 ×2 (APK12-2, -3), rest S4. No emulator re-run | **Not confirmed as stated for the phone tally** (A01-03); rest consistent with the report. Emulator results themselves **could not be checked** (no emulator by rule) |
| 40 | v12 report: disposable DB dropped, only Etp DBs and system DBs remain; throwaway keys deleted | v12 report | N: cannot verify the past; current sys.databases has no APK12 DB | **Could not check** (current state consistent) |
| 41 | v11: phone 15 pass / 1 partial / 0 fail; tablet 11 pass / 0 fail; defects V11-1…V11-7 | HANDOFF:331 | S: `APK_v11_Emulator_Report.md` states "Pass 15, Partial 1 (P8), Fail 0, Not run 2 (P3, P19)"; tablet "Pass 11, Fail 0"; V11-1…7 listed. HANDOFF omits "2 not run" | **Confirmed** (HANDOFF omits the 2 not-run items) |
| 42 | E2E run 1: 31/37 | HANDOFF:337 | S: `W4_E2E_Report.md:64` "37 checks: 31 PASS, 3 FAIL, 1 unclear, 2 PASS with workaround" | **Confirmed** |
| 43 | E2E run 2: "all D1–D7 fixed + payroll/accounting/B2B/refund/credit note/groups/OTA/stock pass"; briefing: "E2E run 2 all pass" | HANDOFF:337 | S: `W4_E2E2_Report.md:56`: D1-D6 fixed, 1 area FAIL (N1), 5 pass with API workaround, D8/D10 partial, N1-N11 new defects | **Not confirmed as "all pass"** (A01-06); the D-list claim is accurate |
| 44 | Later fixes N1/N2/N3 (company accounts, add OTA channel, stock item master, departments, joining date, opening TB, names, wording, CSP) | HANDOFF:336 | G: commits `5f0f885` (N1/N2/N4) etc. exist; no end-to-end re-run found | **Could not check** end-to-end (only unit tests) |
| 45 | Disk: "E: filled up (0 GB) mid-run; … 86 GB free" | HANDOFF:339 | F: 287.6 GB free now (not the same moment) | **Could not check** (historic); current free space ample |
| 46 | "~60 merged worktrees removed (branches kept)" | HANDOFF:339 | G: `git worktree list` shows 9 + main; 94 local `claude/*` branches (97 branches in all) kept | **Plausible, not countable** (the removed count is not recoverable); branches are kept as stated |
| 47 | "S2 left junctions in `scratchpad\art` — remove with `rmdir`" | HANDOFF:339 | N: the scratchpad of that session is outside the repo and not found under `E:\Projects\HCC` | **Could not check** |
| 48 | Node 25 note: `E:\Program Files\nodejs` v24 comes first on PATH; use Node 25 | CLAUDE.md:7; HANDOFF:255 | F: on this machine Machine PATH #0 is `E:\Tools\nodejs` (v25.9.0), v24 is #13 | **Not confirmed as written** (now Node 25 is first; A01-05) |
| 49 | Prototype commands in CLAUDE.md/README exist: `npm install`, `typecheck`, `test`, `build`, `build:production`, `start` | CLAUDE.md; 03 README | F: `package.json` scripts: build, build:production, test, typecheck, start | **Confirmed** |
| 50 | 38 `ETP-REUSE` stand-ins | plan §0 (ETP_Reuse_Pending.md) | F: 38 numbered rows in `08 Production/ETP_Reuse_Pending.md` | **Confirmed** (A9 classifies them) |
| 51 | 291 `MapGet/MapPost` calls in 31 endpoint files (plan) vs 373 routes (security review) | plan §0; review | F: `git grep` of `.Map(Get|Post|Put|Delete|Patch)(` in `src/Hcc.Api` = 291 calls in 32 files (31 `Endpoints/*` files + composition); 373 vs 291 reconciliation is A5's job | **Confirmed** (291); 373 not reconciled here |
| 52 | Knowledge graph "4,433 nodes, 15,414 edges, 258 communities"; Obsidian vault "4,685 notes" | HANDOFF:318 | F: today `graphify-out/GRAPH_REPORT.md`: 29,036 nodes / 81,567 edges / 1,423 communities; vault has 30,384 `.md` files (regenerated 12:28 after Wave 4) | **Superseded** (earlier numbers no longer apply; not an error then) |
| 53 | Historic intermediate totals (501, 502, 519, 561, 584, 661, 690, 694, 757, 773, 781, 800 prototype; 54, 212, 233, 259+1, 302, 635, 794, 849 server) | HANDOFF Sessions 10-12 | N: those states are earlier commits; only the final 1008 / 962 are reproducible on the current tree | **Could not check** (superseded) |
| 54 | Paths named in backticks exist | HANDOFF 211-344, CLAUDE.md | F: probe `A01_paths.mjs`, `paths-check.txt`: 218 backticked tokens, 154 resolve, 50 are non-paths/external/pattern, 14 flagged missing = 13 are not repo paths (branch-less names such as `.\SQLEXPRESS`, `Workpc\Sagar`, URLs, `INV/2627/0000001`, `/graphify --update`, the package name `@capgo/capacitor-printer`) or relative to a sub-folder that exists (`appsettings.json` → `08 Production/src/Hcc.Api/appsettings.json`, `register.tsx` per feature, `ext/`), 1 is outside the repo (`scratchpad\art`) | **Confirmed** (no missing repo file found; full docs-vs-code table is A11's) |
| 55 | Old-PC paths (`V:\Codex\…`, `V:\ETP`) | CLAUDE.md | N: `V:` not present; HANDOFF itself says read as this folder | **Not applicable** |
| 56 | Prototype test count 1008 covers the production sync layer (W4 tests) | HANDOFF:335-338 | R: the PASS list includes `[W4-10]`, `[W4-6 staff]`, `[P5]`, `[W4-0]` groups | **Confirmed** |
| 57 | "Cash-book lock blocks until Owner reopen (DEC-065)", "Reception cash deposit needs own open shift" etc. (behavioural) | HANDOFF / v12 | Not in A1's scope (behaviour) | **Could not check** here (A3b/A5) |

## 5. What evidence exists for APK v12 (replaces the plan's "v12 report absent")

* Report `05 Delivery Evidence\Acceptance\APK_v12_Emulator_Report.md` and 107 screenshots in `Acceptance\v12\` are tracked in `f0a5a29` (the plan's "untracked v12 folder" no longer applies).
* `build.gradle:30 versionCode 12`, commit `b629b5e` (12:27 on 26 Oct), `aapt dump badging` on the two APKs in `05 Delivery Evidence\APK\` shows versionCode 12 and the expected package names; SHA-256 of both files match the report.
* The web bundles inside both APKs are byte-identical to a fresh build of the current source.
* Not independently verifiable without an emulator: every pass/fail line in the report, the throwaway release build, the throwaway CA, the DB drop.
* The report itself lists what was **not** tested: physical devices, a release-signed APK on a device, `LocalMachine\My` thumbprint hosting / Windows service path, real LAN address, refunds/credit notes/day close/payroll on the APK, printing from the server APK, session expiry, device-registration removal.

## 6. Reproduce

```
cd "08 Production"           # Windows PowerShell, or PSModulePath reset
dotnet build Hcc.slnx
dotnet test Hcc.slnx -maxcpucount:1 --no-build
cd "..\03 Prototype"         # Node 25 first
npx tsc --noEmit; npm test; npm run build; npm run build:production
node "..\05 Delivery Evidence\Audit\probes\A01_selfcontained.mjs"
cd ..; node "05 Delivery Evidence\Audit\probes\A01_paths.mjs"
```
Throwaway migrate check (done in `HccTest_A01_…`, dropped): `Hcc__Sql__ConnectionString=<test DB>`, `ASPNETCORE_ENVIRONMENT=Development`, `dotnet run --project src\Hcc.Api --no-build -- migrate` (without Development the host refuses to start without a full Production config; my first attempt hit that and left no database behind).

## 7. Closing lists

### Claims I could not confirm
1. Everything the emulator reports state (v11 and v12 pass/fail lines, APK12-1…10, "phone emulator killed", "DB dropped"): no emulator allowed; only counts, hashes, versionCode and configuration were checked.
2. v7 APK SHA `E87C692B…73EF` and the "web bundle byte-identical to dist" statements for v7-v9: the files/builds no longer exist.
3. Built `productionRelease` APK "not debuggable / allowBackup=0 / apksigner OK": the APK was not kept; only the Gradle/manifest configuration confirms it.
4. "86 GB free" and the ~60 removed worktrees, the `scratchpad\art` junctions: historic or outside the repo.
5. End-to-end behaviour of the N1-N3 fixes after run 2 (no third E2E run).
6. Intermediate test totals in Sessions 10-12 (superseded).
7. The 373-route versus 291-map-call reconciliation (A5).

### Open owner/CA decisions I hit (and the code that depends on each)
None specific to A1. Recorded only so A11 can merge them: the HANDOFF 12b "owner/CA items added" (release keystore + backup → `build.gradle` `hccSigningProblem` / `productionRelease` signing; hotel CA PFX and keys on a separate USB → `deployment\windows\New-HccCertificate.ps1`, `New-HccGuestIdKey.ps1`; BitLocker backup drive → `Backup-HccDatabase.ps1`; booking-history window 180 days; early-departure fee; attrition rule → Groups module; role matrix v2 D1-D7 → `capabilities.ts`/seeds; CA invoice questions G9-G12 → Invoices module; `V:\ETP` for 38 ETP-REUSE rows). F-M03 forced credential change is open and needs a schema change plus a screen (no code present).

### Questions for the lead
1. `main` is `f0a5a29`; `c0c477d` (HANDOFF v12 paragraph + audit prompt) is only on the audit branch. Should the final audit commit (or a cherry-pick) bring it onto `main`, and should the audit commit include the plan file that is currently untracked?
2. A01-01: do you want the Operations test fixed (clear `PSModulePath` in the test) as a post-audit fix, and should the readiness gate "builds and tests reproduce HANDOFF" count 962/962 (env-fixed) or 961/962 (as-run from PowerShell 7)? I suggest 1 (reproduces) with the caveat noted.
3. A08 created `HccTest_A08outbox_…` while I held the lock, and A9 has `HccTest_perf_…`; please run the final leftover-DB check after all agents finish and remove any that remain.
4. Should A10/A11 use my observation that Machine PATH now puts Node 25 first (CLAUDE.md note stale), or is the `Machine` ordering expected to revert?
5. I did not re-walk E2E; do you want a third E2E run (4 h, Sonnet) as a pre-go-live task, since run 2 left 1 FAIL and 5 workaround areas whose fixes are only unit-tested?
