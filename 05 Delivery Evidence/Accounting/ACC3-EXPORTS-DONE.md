# ACC3-EXPORTS DONE (lane acc3-exports, branch claude/acc3-exports, 1 Nov 2026)

Contract section: ACC3-REPORTS-CONTRACT §5 (also §4 `saveText`).

## Commits
f9bc4cee (prototype), 541db385 (server), final commit = this file's commit (see reply).

## Files
Prototype (03 Prototype): `src/app/xlsx.ts` (cell kinds `{amt}` plain `#,##0.00`, `{b}` bold; sheet option `freeze`; styles 6 and 7), new `src/app/zip.ts` (store-only writer over xlsx.ts `zipStored`, unique safe names, reader `unzipStored` with CRC check), `src/app/native.ts` (`saveText(name, text, mime)`, no BOM), new `src/domain/streams/accounting-exports.ts`, new `src/domain/streams/_acc3-stubs.ts`, `tests/stream-accounting-exports.mjs`.
Server (08 Production): `Hcc.Infrastructure.SqlServer/Reporting/OpenXmlExporter.cs`, `Reporting/StatementExports.cs`, `Hcc.Api/Endpoints/AccountingExportEndpoints.cs`, one registration line in `HccComposition.cs`, `third-party/source-reuse.json` entry ACC3-EXPORTS-OPENXML, `tests/Hcc.Accounting.Tests/Exports/ExportsTests.cs`.

## What was built
- One row model per report (title block with range and "PROVISIONAL", bold header, body) rendered as Excel sheet (money numbers `#,##0.00`, dates as date serials, frozen header) and as formula-guarded CSV (csvMinimal / `VoucherExport.CsvCell` = `CsvFormula.Neutralise`). Covers ACC-1 statement, trial balance, day book and the contract §2-§3 shapes: P&L (+ Tally group sheet), balance sheet, cash ratio, ageing (+ bills sheet), GST summary.
- File names `<property code>-<report>-<from>-<to>.<ext>` (`accExportName`, `StatementExports.FileName`).
- Bulk: prototype `accStatementsZip(s,{from,to,parties,format})`; server `GET /accounting/export/statements.zip?from&to&parties=all|receivable|payable&format=csv|xlsx` (PERM-ACC-005; parties with neither opening nor movement are skipped; zip is store-only).

## Rulings / deviations
1. **No `DocumentFormat.OpenXml` package.** The package is not in the local cache and Directory.Packages.props is kept minimal, so ETP's OpenXmlReportExporter layout and styles are adapted onto System.IO.Compression + XmlWriter. No package or lock-file change. Provenance entry says "adapted".
2. **Local stubs**: the report lanes had not landed. `_acc3-stubs.ts` holds the contract §2-§3 types; cash ratio and GST summary field types are read from the contract text (`setoff` typed as a name to paise map).
3. Server side has no P&L / ageing / GST view records yet (acc3-pl / acc3-ageing own them), so the server workbook covers the ACC-1 statements and bulk zip; `OpenXmlExporter.Build(XlsxSheet[])` is generic for the integrator to feed PlView etc.
4. `saveText(name, text, mime)`: the brief says `saveText(name, mime)`; content has to be a parameter.
5. Property code: the prototype has none (`settings.propertyCode` else `HCC`); the server uses the letters/digits of the property name (max 12) else `HCC`. Owner may want a real configured code.
6. `AccountingReportEndpoints.Queries` is private to another lane's file, so its composition is duplicated in `AccountingExportEndpoints.cs` (integrator may unify).
7. Bulk server export runs one statement query per party (each loads the vouchers); fine for the party counts here, may need batching at scale.

## Tests (before -> after)
- Prototype `npm test`: 2095 -> 2103 (8 new `[ACC3-EXPORTS]`), 0 failed; `tsc --noEmit` clean.
- Server `Hcc.Accounting.Tests`: 236 -> 245 (9 new in `Exports/`), 0 failed. `dotnet build Hcc.slnx` built earlier except the test fix; the test run rebuilt the touched projects with no errors.
- Not run: `npm run build`, the other server test projects (not touched).

## Open items
- Excel files not opened in desktop Excel (owner step at the gate).
- Integrator: re-point imports of accounting-exports.ts to the real report modules and delete `_acc3-stubs.ts`; add server report sheets for P&L / ageing / GST.
- UI lane wires the buttons (`accTablesXlsx`, `saveBinaryFile`, `saveCsv`, `saveText`).

## Integration needed
1. Delete `src/domain/streams/_acc3-stubs.ts`; change its import in `accounting-exports.ts` to `accounting-pl` / `accounting-ageing` / `accounting-gst-summary` (type-only).
2. `source-reuse.json` and `HccComposition.cs` will conflict textually with other lanes' ACC-3 lines; keep both sides.

## HANDOFF bullet
acc3-exports: Excel/CSV/bulk-zip exports for ACC-1 statements and ACC-3 reports (prototype xlsx.ts + zip.ts + accounting-exports.ts; server OpenXmlExporter adapted from ETP without the OpenXml package, `/accounting/export/statements.zip`); prototype 2103, Accounting.Tests 245.
