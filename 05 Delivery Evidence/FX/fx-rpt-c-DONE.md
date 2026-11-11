# fx-rpt-c DONE

Lane rpt-c / branch claude/fx-rpt-c / base 9c5abf01. All seven non-stock demo-only reports now run on the server report hub.

## Reports now on the server
| id | source on the server | view / gate |
|---|---|---|
| rpt.admin.config-history | cfg.setting_change + the Owner's GST versions (tax.gst_setting_version, source owner) | PERM-CFG-001 (Owner, Tech admin); money masked without PERM-RPT-002 |
| rpt.admin.export-log | rpt.export_log | PERM-ADM-004 opens it, the report then refuses all but Owner / Manager (as the prototype) |
| rpt.admin.report-dictionary | static (12 rows) | PERM-RPT-001 / 002 |
| rpt.money.closing-records | cls.closing_snapshot (current record) + cls.day_reopen | PERM-CLS-001, gate PERM-RPT-002 |
| rpt.money.cash-out-register | pay.cash_transfer | PERM-RPT-002 |
| rpt.money.expense-month | ops.expense via IExpenseStore.LedgerAsync + ExpenseScc.Summary | PERM-RPT-002 |
| rpt.rest.closed-bills | pos.bill / pos.check_order / ivc.invoice | PERM-POS-004, own-shift Cashier sees own bills |

No report was left demo-only: every one has server data. No migration.

## Files
New: `08 Production/src/Hcc.Reporting/Reports/FxC/` (FxCSupport + 7 reports); tests `08 Production/tests/Hcc.Reporting.Tests/FxC/` (7 classes, 21 tests).
Shared files, appended in marked blocks: `src/Hcc.Api/HccComposition.cs` (`// FX-RPT-C` ... `// /FX-RPT-C`, after `// /CU-RPT-A`, before `return services;`), `tests/Hcc.Reporting.Tests/EngineTests.cs` (same markers, end of ServerCatalogue). Prototype: `serverMirror:false` removed from the 7 definitions (`built/admin.ts`, `money.ts`, `rest.ts`); both `catalogue.json` copies regenerated with `node tools/rpt-catalogue.mjs` (only the 7 `"side"` lines change). The production client routes by the catalogue flag, so no client code changed. Merge note: rpt-d appends at the same two places; keep both blocks and regenerate catalogue.json with the tool, never by hand.

## Tests (through the lock)
- `dotnet test tests/Hcc.Reporting.Tests --filter "Tests.FxC|EngineTests"`: 37 passed, 0 failed, 0 skipped (22 FxC + 15 EngineTests incl. the catalogue.json cross-check of all 7) after the audit fix round.
- `dotnet test tests/Hcc.Integration.Tests --filter ReportHubHttpTests`: 2 passed, 0 failed (resolves every selector on the real composition).
- Prototype: `npx tsc --noEmit` clean; `node --test stream-rpt-engine, stream-rpt-built, stream-rpt-hub, sync-hub-server, stream-rpt-money-a, sync-reports-hub, stream-rpt-p2-c`: all pass; `rpt-catalogue.mjs --check` up to date.
- Each report: SQL-seeded rows, figures to the paise, screen = Excel = PDF = print check (RendererTests.ThreeWay), refusals by role through the real access policy; money reports also checked at the hub (Housekeeping refused, DEC-061).

## Not verified
Full server suite, full npm test, emulator (not in scope). No HTTP call of the 7 ids end to end beyond selector resolution. Phone layout of the new rows is the generic viewer.

## Defaults used
1. report-dictionary: four glyphs the PDF writer cannot round-trip are folded to ASCII (arrow to "->", sigma to "Sum of", minus sign to "-", curly apostrophe to "'"); otherwise word for word.
2. config-history: lists cfg.setting_change only (masters keep their audit trail in the audit log); every stored change incl. a later withdrawn one is a row; actor shown by display name.
3. closing-records: only the current record of a date (a record replaced by a re-close is history); cash difference = counted - expected, 0 when no count; corrections = number of cls.day_reopen rows of the date; closed-at as "yyyy-MM-ddTHH:mm" hotel time.
4. export-log: report title from the registry (id if unregistered); `user` filter is the numeric user id; time in hotel time with a space; parameters as "k=v, ..." without `legacy`.
5. cash-out-register: kind filter validated against the three labels; totals per kind leave out Rejected; ordered by date then recorded time.
6. expense-month: capital purchases are included (as the prototype's expMonthSummary); heads in ordinal order; reversal pairs both count.
7. closed-bills: invoice number = latest revision of the pos invoice, else the bill number; a voided bill is excluded; items exclude voided lines.
8. FY filter of closing-records follows the engine (current FY ends at the business date).

## Open questions
None blocking. Ask the owner whether the dictionary glyph folding is acceptable (default yes).

## Audit fix round (audit commit 0f7812fb)
1. closing-records: the cash difference now comes from the stored record (`JSON_VALUE(record_json, '$.figures.drawer.differencePaise')`, the sum over every closed shift, as the production client shows), not from the two snapshot columns. Tests seed real canonical `ClosingRecord` JSON: 22 Oct two shifts (morning 100.00 short, evening balanced) gives -100.00 (the columns would give -50.00); 21 Oct two tills, one counted 25.00 short and one uncounted gives -25.00 (the columns would give 0).
2. config-history: the Owner's GST rate changes are listed, merged by time with the setting changes, with the client's text (`gstHistoryFromServer` / `gstShow`): "—" before the first version, otherwise the value in force before; " (withdrawn)" after a withdrawn version; seed versions not listed. A withdrawn setting change also carries " (withdrawn)". Test: one Owner version, one withdrawn Owner version and a seed version in range (seed not listed); format unit test for percent / money (Indian grouping) / boolean / opt-in.
3. Gap tests added: a second property's rows never show (config history setting + GST, export log); a closed bill with two invoice revisions shows the latest number once.

### Defaults added
9. config-history masks rupee amounts for a reader without PERM-RPT-002, so the Tech admin sees "₹ ••" on the server (stricter than the demo).
10. expense-month heads are sorted ordinal (the prototype uses locale order; only differs for lower-case or accented head names).
