# ETP/SCC implementation map for HCC .NET

25 October 2026. Read-only preparation for DEC-054; no production code copied, donor tests run, operational scripts executed or databases opened. This map refines the earlier reuse proposal; it does not authorize production construction.

## Exact snapshot and path convention

- **ETP root:** `V:\ETP\Code\Worktrees\main`; HEAD `92a14da1ca0b21c4cdb34bedf5a9fc7da9f95708`; working tree clean at inspection.
- **SCC root:** `V:\Co work\Projects\Retail\saagar-control-centre-phase1-clone`; HEAD `b4734766d49c43d834ac2f5ef81fc88a00123580`; five working-tree entries were present, in audit/temporary/generated locations. Do not discard them or assume HEAD alone identifies the entire checkout.
- Paths below are relative to these roots. `HCC_FirstParty_Source_Map_2026-10-25.json` records absolute paths and SHA-256 hashes for 49 implementation, dependency and test files. Recheck hashes before extraction. Test references identify candidate evidence to inspect/run, not proof that those tests currently pass.

## ETP extraction packages

### E01 — migration runner and bootstrap: adapt the framework, not the retail schema
- Source: `src/Etp.Reporting.Infrastructure.SqlServer/DatabaseBootstrapper.cs`, `Migrations.cs`.
- Observed boundaries: bootstrap validates connection/database target and opens master for database creation; migrations expose `IMigrationSource`, `IMigrationStore`, script checksums and async cancellation. These are operational privileges, not routine request privileges.
- Dependencies: Microsoft.Data.SqlClient, connection policy, SQL migration store/source implementations in the migration file and their referenced helpers. Enumerate those references in the extraction spike; copying two files is not a compilation guarantee.
- Destination: `Hcc.Infrastructure.SqlServer/Migrations` plus an explicitly invoked HCC setup tool. Keep HCC's own migration scripts and schema. Do not import ETP's dbo tables or migration history.
- Candidate tests: `tests-dotnet/Etp.Reporting.SqlServer.Tests/MigrationTests.cs`; `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/CrossPhaseMigrationTests.cs`.
- HCC acceptance: disposable HCC-only DB; first install, repeat run, checksum mismatch, interrupted migration, serialized competing upgrades, verified pre-upgrade backup, failed-upgrade recovery. Never test these against the existing ETP database.

### E02 — connection policy: adapt to HCC service identity
- Source: `src/Etp.Reporting.Infrastructure.SqlServer/LocalSqlConnectionPolicy.cs`.
- Observed: rejects stored SQL credentials, non-local database servers and attached-file/user-instance options. SQL stays local to the service; browser/tablet clients use HTTPS to HCC, not SQL directly.
- Destination: `Hcc.Infrastructure.SqlServer/ConnectionPolicy`; retain integrated authentication, explicit database selection and current TLS validation. Configure the actual Windows service identity and least privileges separately.
- Candidate test: `tests-dotnet/Etp.Reporting.SqlServer.Tests/AdapterIntegratedSecurityRegressionTests.cs`.
- Acceptance: valid service account, denied remote/credential-bearing string, wrong catalog, Unicode/decimal/rowversion handling, cancellation and unavailable SQL. A local admin success does not prove service-account access.

### E03 — backup/restore operations: adapt with explicit HCC targets
- Sources: `scripts/backup-etp-database.ps1`, `restore-etp-database.ps1`, `invoke-etp-recovery-drill.ps1`, `install-daily-backup-task.ps1`, `install-monthly-recovery-drill-task.ps1`.
- Observed backup defaults identify ETP database/directories; restore script describes replacing the live ETP database and protected staging. **These are not safe HCC commands as downloaded.**
- Destination: `Hcc.Operations` / HCC deployment scripts. Replace product configuration, task names, paths, DB allowlists and service identity; separate drill destinations from live HCC. Preserve receipt/hash verification and distinguish protected pre-migration backups from rotatable daily backups.
- Candidate tests: `tests-dotnet/Etp.Reporting.Desktop.Tests/RestoreDatabaseScriptTests.cs`, `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/DatabaseRecoveryHealthTests.cs`.
- Acceptance: synthetic backup; tampered receipt rejected; inaccessible staging path; separate restore and consistency check; retained recovery copy; no ETP catalog/task/path touched. Do not schedule tasks during a source-qualification run.

### E04 — recovery-certificate service: qualify only if HCC needs the corresponding encryption model
- Source: `src/Etp.Reporting.Infrastructure.SqlServer/BackupCertificateService.cs`.
- Observed: certificate/key handling, password and directory validation, platform dependencies. Do not assume this is necessary for every backup or that it works with an unselected SQL encryption feature.
- Destination: conditional `Hcc.Operations/RecoveryKeys`; keep secrets outside logs/source, bind export to authorized operations.
- Candidate test: `tests-dotnet/Etp.Reporting.SqlServer.Tests/BackupCertificateServiceTests.cs`.
- Acceptance: validate chosen SQL edition/features first; isolated key export/recovery drill and access controls. Defer if the production encryption design does not need this service.

### E05 — Excel report and report-pack exporters: strongest bounded C# reuse candidate
- Sources: `src/Etp.Reporting.Reporting/OpenXmlReportExporter.cs`, `ReportPackExporters.cs`.
- Observed contracts: `ExcelReportMetadata`, `ExcelReportData`, `ExcelReportColumn`, report-pack tables; dependency on DocumentFormat.OpenXml and associated report writer/helper types.
- Destination: `Hcc.Reporting/Excel`; adapt metadata to property, business date, scope, currency and rule version. Feed authorized HCC DTOs rather than donor query results.
- Candidate tests: `tests-dotnet/Etp.Reporting.Reporting.Tests/OpenXmlReportExporterTests.cs`, `ReportPackExporterTests.cs`.
- Acceptance: integer-paise conversion, dates, formula-like guest input treated as text, non-ASCII names, totals/row counts, multiple sheets and restricted fields absent. Capture every referenced writer/helper in the import manifest before copying.

### E06 — PDF exporter: reuse the rendering chain, not just its wrapper
- Sources: `src/Etp.Reporting.Reporting/SimplePdfReportExporter.cs`, `VisualReportPdfDocument.cs`, `VisualReporting.cs`.
- Observed: SimplePdfReportExporter delegates to VisualReportComposer and VisualReportPdfDocument; it is not standalone. Reporting project references PDFsharp.
- Destination: `Hcc.Reporting/Pdf`; preserve hotel invoice/report distinction and HCC document permissions. Specify A4 defaults, repeated headers, fonts, page breaks and complete rows.
- Candidate tests: `tests-dotnet/Etp.Reporting.Reporting.Tests/SimplePdfReportExporterTests.cs`, `VisualPdfExportRegressionTests.cs`.
- Acceptance: one-page and long reports, INR/Unicode, all rows and exact totals, no blank filler, document escaping and rendering inspection. Existing browser/Android printing remains until the server path is verified.

### E07 — cash reconciliation: adapt pure calculations; rewrite the repository
- Sources: `src/Etp.Reporting.Reporting/CashBalanceReconciliationService.cs`, `CashBookDocument.cs`; `src/Etp.Reporting.Infrastructure.SqlServer/CashBookRepository.cs`.
- Observed: pure reconciliation distinguishes missing inputs from zero. **CashDeposit means cash removed/banked**, not a guest's refundable deposit. Repository is a partial OperationalReportRepository and queries retail manual inputs/tender data; it cannot be transplanted as an HCC repository.
- Destination: `Hcc.Domain/Cashier` for the adapted calculation; new HCC repository over authoritative guest/POS/expense cash movements.
- Map: opening float + guest receipts + guest deposits + POS cash sales − guest refunds − deposit returns − POS cash voids − cash expenses − banking, with approved adjustments. Never map guest deposits to ETP's subtractive CashDeposit field. UPI/card and room-folio transfer are not drawer cash.
- Candidate test: `tests-dotnet/Etp.Reporting.Reporting.Tests/CashBalanceReconciliationServiceTests.cs` (read examples include cash/noncash separation and missing-versus-zero).
- HCC golden cases: BASE ₹10,750; cash sale once; approved void once; pending void no effect; folio transfer no receipt; multi-day closing/restore; separate immutable source IDs. Preserve HCC's paise/rounding contract when adapting ETP decimals.

### E08 — email transport: later optional adapter, not automatic sending
- Sources: `src/Etp.Reporting.Infrastructure.SqlServer/SqlServerReportEmailService.cs`, `ReportEmailTransport.cs`.
- Observed service is a partial distribution service and depends on access/document/settings gateways; transport uses MailKit from the infrastructure project.
- Destination: optional `Hcc.Infrastructure/Distribution`; reuse transport boundaries after HCC sessions, recipient authorization, delivery policy and secrets storage exist. No SMTP config or recipients copied from ETP.
- Candidate tests: `tests-dotnet/Etp.Reporting.SqlServer.Tests/ReportEmailServiceTests.cs`, `MailKitReportEmailTransportTests.cs`.
- Acceptance: fake transport, masked logs, retry/idempotency and denied-recipient cases. Source qualification never sends mail. Defer until communication scope is authorized.

## SCC rules to port, not HTML applications to embed

Shared dependencies visible in module HTML include `www/shared/module-bridge.js`, `module-runtime.js`, `mah4-runtime.js`, `www/c1-operations-policy.js`, `c1-control-desk.js` and `app-i18n.js`; the modules also depend on DOM, localStorage and cross-module messages. Extract rule inputs/outputs into C#; do not copy this runtime into the server.

| Package | Exact source / entry points observed | Destination and adaptation | Existing candidate tests; new proof required |
|---|---|---|---|
| S01 Payroll | `www/modules/payroll/index.html`: `calcAttendance` (~1710), `calcGM` (~1821) | `Hcc.Domain/Payroll`: isolate attendance, salary basis, deductions, employer contributions and dated configuration. Attendance source includes business-specific late/no-thumb/extra-day rules; those are not automatically HCC policy. Use employee IDs and exact money arithmetic. | `tests/financial-golden.test.mjs`: payroll gross/net and PT-boundary cases; `tests/phase6b-payroll-control-identities.test.mjs` is UI identity evidence, not arithmetic proof. Add HCC monthly/daily wage, leap month, partial attendance, advances, negative/overflow and locked-run tests. |
| S02 Leave | `www/modules/leave/index.html`: `leaveStatus`, `isActiveLeave`, `isCountedLeave`, `leaveDaysOf` (~1012 onward) | `Hcc.Domain/Leave`: pending+approved capacity versus approved-only balance; half days and weekly offs; employee-ID entitlements and approval history. Source falls back to approved for missing/unknown status: do not import that permissive fallback into production. | `tests/financial-golden.test.mjs`: weighted FY days and weekly-off cases; `phase6b-leave-control-identities.test.mjs`. Add invalid status denial, overlap, cancellation/refund, concurrent approval and payroll linkage. |
| S03 Compliance calendar | `www/modules/tax/index.html`: `dueDateIso`, `materializeDueDates` (~1138/~1168), evidence completeness | `Hcc.Domain/Compliance`: dated rule catalog and deterministic calendar, filing evidence, owner/accountant scope. Keep legal dates configurable with source/effective date; no claim that retail rules are correct/current for a hotel. | `tests/financial-golden.test.mjs` GST-feed case covers aggregation, not legal due-date completeness; `phase6b-tax-dsr-qms-generated-control-identities.test.mjs` covers UI identity. New leap-year, month rollover, extensions, holiday policy, missing-rule and duplicate-reminder tests required. |
| S04 Cash/expense ledger | `www/modules/expense/index.html`: `computeDay` (~607), `computeDayStore` (~645) | Original HCC ledger adapter with E07 calculations. Use carry-forward/closed-day scenarios, not SCC localStorage as authority. Hotel expense approvals, returns and shared POS till remain authoritative. | `tests/financial-golden.test.mjs`: carry-forward across date gaps and GST-feed examples; `phase6b-expense-control-identities.test.mjs`. Add HCC refunds/deposits/voids, bank drops, reversals and locked dates. |

`tests/financial-golden.test.mjs` uses Node test/assert, VM, storage/DOM stubs and `tests/lib/module-bundle.mjs`; those are useful for extracting synthetic golden vectors. They are not a production service or proof of current statutory compliance. Verify payroll/tax applicability with current authoritative requirements and the owner's adviser before live use; no real staff data belongs in extracted tests.

## Observed dependency versions, not upgrade recommendations
ETP infrastructure csproj: `net10.0`, Microsoft.Data.SqlClient **6.1.1**, MailKit **4.18.0**; project references Application, Import, Domain and Reporting. Reporting csproj: DocumentFormat.OpenXml **3.3.0**, PDFsharp **6.2.4**. Do not import the whole infrastructure project merely to obtain a small helper. Record exact transitive dependencies/notices during qualification and validate current support/security separately before production.

## Coding handoff and order
1. Recheck this manifest against local files; record source-to-destination mapping, dependencies, notices and modifications. Retain owner changes in both sources.
2. First bounded experiments: E05 exporter and E07 pure calculation with synthetic DTOs; S01/S02 golden-vector extraction. These can be evaluated without a live DB or copied credentials.
3. Once production construction is authorized: E02 policy and E01 migration framework in an isolated HCC test database; repeat the prior T-SQL concurrency suite through the actual C# adapter. Do not run ETP tests blindly against configured live stores.
4. Add E03 operational recovery only after HCC paths/catalog/service identities exist. An HCC-only separate restore drill is required before claiming recovery readiness.
5. Port S01–S04 with explicit approved hotel policies; compare C# outputs to selected, synthetic, reviewed golden inputs. Document every intended divergence rather than forcing parity with retail-specific behavior.
6. E06 follows authorized report contracts; E08 and E04 remain conditional. Installer/Tally/audit candidates from the earlier broad proposal need their own exact dependency review before extraction; they were not silently qualified by this 12-package map.

Completion evidence per package: retained notices + source hashes, dependency graph, compilable isolated unit, tests actually run, unrun tests, HCC semantic differences, and a rollback path. No source has been imported by this preparation task.
