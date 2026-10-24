# What HCC can reuse from the ETP Reporting Engine and Saagar Control Centre

24 Oct 2026 · read-only review (nothing copied or changed in either project) · Sources: `V:\ETP\Code\Worktrees\main` (ETP Reporting Engine 1.9.x, `github.com/sagarbora91/etpreportengine`) and `V:\Co work\Projects\Retail\saagar-control-centre-phase1-clone` (SCC V5/V6 offline Android app). Both are the owner's own code, so there is no third-party licence barrier. Their bundled libraries keep their own licences: OpenXML, PDFsharp, MailKit and SqlClient are MIT; jsPDF, JSZip and sql.js need checking per file.

## 1. The short answer
**ETP has already solved most of the hard, boring production work HCC still has to build:** installing SQL Server Express, safe schema upgrades, backups with proof, restore drills, scheduled jobs, Excel/PDF reports, e-mail sharing, a cash book, and an accounting (Tally) export. It runs on **this PC** against the **same `.\SQLEXPRESS` instance** HCC will use.

SCC adds proven ideas for the **Android side**: old-WebView compatibility (API 23), automatic phone backups, re-authentication by PIN, and WhatsApp sharing.

The catch is the language. ETP is **.NET 10 (C#)**, and HCC's production plan says **NestJS (TypeScript)**. You only get ETP's code as code if the HCC server is also .NET. That is the one decision below.

## 2. ETP Reporting Engine — reuse candidates

| ETP piece (file / script) | What it does | HCC need it covers | Reuse level |
|---|---|---|---|
| `Infrastructure.SqlServer/DatabaseBootstrapper.cs`, `Migrations.cs`, `database/migrations/` (37 scripts) | Versioned SQL migrations with checksums in `schema_migrations`. Before upgrading it takes a verified `COPY_ONLY, CHECKSUM` backup with a receipt (path, length, SHA-256), then runs `DBCC CHECKDB`. A failed upgrade keeps the database and backup. | AS-13 recovery, A4 "migrations tool" (open item in the contracts package) | **High** — same SQL Server; the pattern is exactly what HCC needs |
| `LocalSqlConnectionPolicy.cs`, `SqlAdapterConnection.cs`, `ConnectionHealth.cs`, `DatabaseOperationalHealth.cs` | Windows-integrated local connections (no stored SQL passwords), health checks | Server connection handling, the "server unavailable" banner | High |
| `scripts/backup-etp-database.ps1`, `restore-etp-database.ps1`, `install-daily-backup-task.ps1`, `install-monthly-recovery-drill-task.ps1`, `invoke-etp-recovery-drill.ps1`, `BackupCertificateService.cs` | Daily backups through Task Scheduler (Express has no SQL Agent), a monthly **restore drill** into a separate DB, and backup certificates | Instructions §7 rule 9 (backup + separate restore before pilot), Wave 1 P7 | **High** — parameterise the DB name |
| `installer/EtpReportingEngine.iss`, `bootstrap-etp-prerequisites.ps1`, `build-windows-installer.ps1`, `invoke-release-rollback.ps1`, `sign-etp-artifacts.ps1` | Inno Setup installer that installs SQL Server 2025 Express offline, plus release signing and rollback | HCC Windows server install and upgrades (A7) | High as a template |
| `OperationalAuditRepository.cs`, `RestatementApprovalPersistence.cs` | Append-only audit; approval-bound restatement (corrections, not silent deletes) | HCC audit log, DEC-028 closed-date corrections, DEC-041 approval binding | Medium — same principle, different tables |
| `CashBookRepository.cs`, `CashBalanceReconciliationService.cs`, `ControlReconciliationService.cs` | Cash book and cash-balance reconciliation for the shops | HCC cashier shift, day close, shared till (DEC-052) | Medium — reference the reconciliation rules |
| `ProductisationRepository.Accounting.cs`, `AccountingAndSharingServices.cs` (Phase 5: Tally XML preparation/export receipts) | Accounting export with receipts; the Phase 7 note says saving XML ≠ Tally imported it | HCC module 20 Accounting — a **real Tally export instead of the simulated one** | **High value**, later wave |
| `Reporting/OpenXmlReportExporter.cs`, `ReportWorksheetWriter.cs`, `SimplePdfReportExporter.cs`, `VisualReportPdfDocument.cs`, `ReportPackageService.cs` (DocumentFormat.OpenXml 3.3, PDFsharp 6.2) | One result model rendered on screen, to Excel and to PDF; daily report packs | HCC Excel export (listed as **not built** in the prototype), owner reports and closing packs as PDF | **High** |
| `ReportEmailTransport.cs` (MailKit), `SharingDeliveryRepository.cs` | E-mail reports with delivery history | Owner daily report by e-mail | Medium |
| `AutomatedOperationsService.cs`, `AutomationSessionLease.cs`, `install-etp-automation-task.ps1` | Scheduled automatic jobs with a lease, so two jobs can't run together | Scheduled night audit/closing reminders, report packs | Medium |
| `new-etp-support-package.ps1`, `invoke-security-scan.ps1`, privacy-safe diagnostics rule | Support bundle with no customer data; security scan | HCC support and diagnostics | Medium |
| SQL integration test rule: tests create and drop their own uniquely named DB and never touch the shop DB | Same rule as the HCC SQL spike | AS-06/AS-08 SQL tests | Already aligned |

Not reusable: the ETP workbook import/parsing (`Etp.Reporting.Import`, profiles, mapping register) — HCC has no workbook import — and the WPF desktop screens (HCC keeps its React UI).

## 3. Saagar Control Centre — reuse candidates
SCC is a single offline WebView app (vanilla JS, Capacitor, SQLite in the WebView). HCC's prototype already has its own React versions of payroll, leave, expenses and checklists, so SCC's value is in the **Android/phone know-how**.

| SCC piece | What it does | HCC need | Reuse level |
|---|---|---|---|
| `scripts/capture-api23-compatibility.mjs`, `prepare-api23-assets.mjs`, `stage-etp-api23-evaluation.mjs`, `android-emulator.ps1` (API 23 AVD `saagar_api23_evidence`) | Makes the app run on **old Android WebViews** and records evidence | **Directly fixes defect AC-01** (HCC fails on WebView 83: `structuredClone` missing). Decide HCC's minimum Android/WebView, then either polyfill or show a "please update Android System WebView" screen | **High, now** |
| `auto-backup.js`, `portable-backup.js`, `offdevice-backup.js`, `restore-engine.js`, `persistence-acceptance.js` | Dated JSON backups to `Documents/…`, restore after reinstall | Prototype APK demo data (IndexedDB) is lost on uninstall today. Production phones hold no authoritative data, so this matters for the prototype/pilot only | Medium (prototype) |
| `reauth-policy.js`, `module-pin-policy.js` | PIN re-authentication and per-module PIN locks | Contracts package open decision 4 (password vs PIN on shared tablets) — a ready, tested policy to copy the rules from | Medium–High |
| `whatsapp-share.js`, `export-control.js` | Share reports/PDFs to WhatsApp, with controlled exports | Owner wants reports on the phone; HCC native share already exists, so add WhatsApp as a target | Medium |
| `legal/R1-LEGAL-MINIMUM-CONTROL-PACK.md`, `legal-control.js` | Data-retention / legal-minimum controls (draft for CA/counsel) | HCC guest data, ID documents and payroll privacy | Reference |
| `app-i18n.js` | Language switching framework | Hindi/Marathi staff screens later | Reference |
| `storage-capacity-policy.js`, `storage-recovery-policy.js`, `sqlite-store.js` | Storage limits and recovery on the phone | Not needed — HCC production is server-authoritative (no offline writes by design) | Low |

### 3a. SCC business modules to adopt (owner, 24 Oct 2026): payroll, leave, tax compliance, cash book
HCC's prototype has payroll, leave and expenses, but with **illustrative** rates (`HR_RATES_SET`, "illustrative payroll rates"). It has no compliance calendar and no separate cash book. SCC's modules carry the **real Indian statutory rules** the owner already uses at Saagar Traders. A first scan of `www/modules/*/index.html` found:

| SCC module | What it contains (scan) | HCC area it replaces or extends | How to reuse |
|---|---|---|---|
| `payroll` (266 KB) | PF, ESIC (100+ references), Maharashtra Professional Tax, overtime, half-day | Module 17 Staff & payroll — replace the illustrative rates with the real calculation | Port the rules to C# (`Hcc.Domain/Payroll`); SCC's worked examples become golden tests |
| `leave` (164 KB) | Staff Leave Manager: leave types, half-day leave | Module 17 leave requests/approvals | Port the rules (entitlements, half-day, approvals); keep HCC's approval-binding rules |
| `tax` (229 KB) | Compliance calendar: TDS, GSTR-1, GSTR-3B, GSTR-9, EPF/ESIC, Professional Tax due dates | **New for HCC** — due-date reminders on the owner dashboard / assistant | Port the calendar rules + reminders; add hotel-specific items |
| `expense` (161 KB) "Central Ledger" + ETP `CashBookRepository.cs` / `CashBalanceReconciliationService.cs` | Opening balance, carry-forward, GST rates on expenses; ETP has the C# cash book and reconciliation | Cash book for day close + shared till (DEC-052) and expenses | Use **ETP's C# cash book** as the base (same language); SCC's ledger rules as reference |

Before go-live:
- **Hotel ≠ retail shop.** Minimum-wage schedules, service charge/tips, meals and accommodation given to staff, and the GST rates on room tariffs and restaurant bills all differ. They need checking by your CA. SCC's rates are a starting point, not proof.
- **Effective dates:** keep statutory rates as dated settings, not constants.
- **No real data:** no real employee or salary data is copied. Prototype and test data stay fictional.

Timing: the prototype's illustrative payroll can stay until acceptance. The real rules land in the .NET server (Wave 1/2), so this work isn't done twice.

## 4. The server-language decision — DECIDED: Option A, .NET 10 (DEC-054, 24 Oct 2026)

| | **Option A — .NET 10 server (ASP.NET Core)** | **Option B — NestJS (current plan)** |
|---|---|---|
| Reuse from ETP | Migrations/backup/restore/installer/ops/Excel/PDF/e-mail/Tally code reused, and later a shared `Saagar.Platform` library used by **both** ETP and HCC — "don't code the same thing twice" | Patterns only; every ETP piece is rewritten in TypeScript |
| Reuse from the HCC prototype | Business rules (money, folio, restrictions, closing) re-written in C#. The 490 prototype tests become the specification (golden figures) | Prototype TypeScript rules can be lifted more directly into the server |
| Your PC | One runtime (.NET 10 + SQL Express 2025) already installed and operated for ETP | Adds Node.js as a production service next to .NET |
| Front end / APK | Unchanged — React web + Capacitor APK call the API | Unchanged |
| Risk | C# port of rules must reproduce the prototype figures exactly (tests prove it) | Recovery/installer/ops are new, unproven code — the riskiest production area |

**Recommendation: Option A (.NET 10 server).** The parts that hurt most in production — installation, upgrades, backups, restores and scheduled jobs — are already working in ETP on this machine. The hotel rules are already pinned down by the prototype's tests, which can be turned into golden-figure tests for the C# code. The React screens and the APK stay as they are.

If you choose A, update `HCC_Production_Contracts_and_Schema_v1.md` §5 (driver = Microsoft.Data.SqlClient, migrations = ETP pattern) and the multi-agent plan's Wave 1 P0. The SQL spike results stay valid because they are plain T-SQL.

## 5. Use now, whatever the decision
1. **AC-01 old-WebView defect:** apply SCC's API-23 approach — set a minimum WebView in the APK with a friendly "update Android System WebView" screen, and/or a `structuredClone` polyfill. Small change in `03 Prototype` + `04 Android`.
2. **Excel export** (prototype gap): mirror ETP's report → Excel/PDF model when production reports are built; in the prototype, CSV stays.
3. **Backup/restore drill** and **migration-with-backup-gate**: adopt ETP's rules as written into the HCC production design now (contracts package §4 rule 11, AS-13).

## 6. Not done here
No code was copied, no build or test was run in ETP or SCC, and nothing in either project was changed. Exact files to extract would be chosen and recorded with provenance (like `07 Integration Lab/evidence/source-reuse.json`) once the server decision is made.
