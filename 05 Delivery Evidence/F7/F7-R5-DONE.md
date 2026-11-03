# F7-R5 DONE — tax (batch 2)

Lane R5 tax · Sonnet · branch `claude/f7-r5` (worktree `/home/user/wt-f7-r5`) · base = `claude/f7` with engine R1, hub R2 and R3 merged.

## Reports (prototype AND server, same ids, column keys / order, flags)
| Id | Prototype | Server | Figures from |
|---|---|---|---|
| `rpt.tax.sac-summary` (Table 12) | `03 Prototype/src/domain/reports/tax/sac-summary.ts` | `Reports/Tax/SacSummaryReport.cs` | `gstr1Summary(...).sac` / `InvoiceQueries.Gstr1UncheckedAsync`; quantity = invoices carrying the SAC |
| `rpt.tax.documents-issued` (Table 13) | `documents-issued.ts` | `DocumentsIssuedReport.cs` | `gstr1Summary(...).documents`; the legacy LGC row first (prototype `getLegacyBills`, server `lgc.bill`); cancelled = real cancelled numbers |
| `rpt.tax.gst-by-bill` | `gst-by-bill.ts` | `GstByBillReport.cs` | stored GST of each document; level bill / line, zero-tax include / exclude; credit notes as negative rows so totals tie to GSTR-1 |
| `rpt.tax.invoice-register` | `invoice-register.ts` | `InvoiceRegisterReport.cs` | same documents; tariff / other / discount, GST, net, payment split, status issued / cancelled / corrected |
| `rpt.tax.bill-corrections` | `bill-corrections.ts` | `BillCorrectionsReport.cs` | `blcState` / `ivc.bill_correction`; gate PERM-RPT-002 |
| `rpt.tax.gst-summary` (ACC-3 wired) | `gst-summary.ts` | `GstSummaryReport.cs` | `accGstSummary` / `GstSummaryQueries.SummaryAsync`, ACC-3 figures unchanged, ties + differences in words |

Shared: `tax/shared.ts` (prototype), `Reports/Tax/TaxSql.cs` (server). `gstr1.ts` was refactored in an `F7-R5` comment block: the collection step of `gstr1Summary` is now `gstr1Documents` (header facts added: date, buyer, GSTIN, room); `gstr1Summary` output is unchanged (existing GSTR-1 tests green).
Migrations: **none** (4428–4431 unused).

## Tests
- Prototype `tests/stream-rpt-tax.mjs` **10 tests**: registry/flags, ties to `gstr1Summary` / `accGstSummary` (SAC rows, totals, table 13 series, GST by bill rate columns, register net = taxable + GST), legacy LGC row (May 2026), cancelled RST, corrections on LF-PARITY, roles, export right, date edges, provisional / legacy flag, CSV formula; goldens `tests/rpt-golden/goldens/rpt.tax.*.json` (6 files, 12 cases: FY26 + LF-PARITY) with screen = Excel = print on every case.
- Server `tests/Hcc.Reporting.Tests/Tax/TaxSqlTests.cs` **7 SQL tests** on a disposable `HccTest_*` DB (invoices, credit notes, a bill correction, a counter bill, legacy bills, folio receipts): every result goes through `RendererTests.ThreeWay`. `Hcc.Reporting.Tests` 35 -> **42/42**. Catalogue cross-check (`catalogue.json` regenerated, both copies) green.

Counts: prototype `npm test` 2614 -> **2624/2624** (tsc clean; two earlier tests assumed the Cashier sees no report, and the Cashier holds PERM-INV-001, so R3's `room status roles` test now ignores tax reports and R2's empty-state test uses Tech admin only); `dotnet build Hcc.slnx` 0 warnings, 0 errors.

## Integration needed (f7-int)
- `HccComposition.cs`: an `F7-R5` block inside `F7-RPT` — one `services.AddSingleton(sp => AccountingAgeingEndpoints.GstQueriesOf(sp))` (ACC-3 `GstSummaryQueries` is otherwise built per request) + six `AddReport<...>()` lines.
- `03 Prototype/src/domain/reports/mask.ts`: `RPT_V2_GRANTS` block (`F7-R5`) — the prototype has no matrix row for PERM-INV-001 / PERM-ACC-001 (server permissions); granted as the server seeds them (INV-001: Owner, Manager, Reception, Cashier, Accountant; ACC-001: Owner, Manager, Accountant). R6 (acc) needs the same entry for PERM-ACC-001: keep one copy when merging.
- `catalogue.json` (both copies): regenerate with `node tools/rpt-catalogue.mjs` after merging with R4 / R6.
- `EngineTests.cs` `ServerCatalogue()` spreads `F7R5Reports()` (an `// F7-R5` block).
- Catalogue §2: `sac-summary`, `documents-issued`, `gst-by-bill`, `invoice-register`, `bill-corrections` are `built` in code (were `planned`); `gst-summary` stays `ACC-3`. `rpt.tax.gstr1`, `gst-month`, `tds-deductee` are not in this lane (gstr1 built page stays; gst-month / tds belong to R6).
- Entry points not switched (Compliance GSTR-1 modal unchanged).

## Decisions / open questions for f7-int
1. **Permission vs "Who may see it"**: the catalogue text says Owner + Accountant; the permission PERM-INV-001 is held by Owner, Manager, Reception, Cashier, Accountant (server seed 2401). Built to the permission (the engine enforces codes). If the owner wants Owner + Accountant only, the report needs a role-restricted permission.
2. **Month parameter**: the catalogue lists "From, To" with class D-M; the engine's D-M binds a `month`, so these reports take `month` (whole month, capped at the business date).
3. **Credit notes in GST by bill**: listed as negative rows (catalogue says "each tax invoice"); chosen so the totals tie to the GSTR-1 working summary.
4. **Round-off is always 0**: HCC bills carry exact paise.
5. **Payment split** (invoice register): guest folio receipts count only on the stay's current invoice (a superseded invoice shows none); restaurant and counter bills from their settlement; group invoices show none. Credit = company / OTA / guest credit and Udhaar.
6. **Room** (invoice register, GST by bill): prototype = the stay's rooms (all segments); server = the reservation's current room. Restaurant bills have no room.
7. **Bill corrections date filter** = the date the Owner approved (prototype `decidedAt`, server the re-issued invoice date). Issued corrections only.
8. **Rates other than 0 / 5 / 12 / 18 / 28 %** count in Net amount and the tax columns only (GST by bill); there is no column for them.
9. **Server POS (RST) payment / cancelled paths** are coded (pos.bill tender / status) but not covered by a SQL test (needs POS seed); prototype RST path is covered via cancelled-number test.
10. `rpt.tax.gst-summary` on FY26 does not tie (no books in the generated history) — the ACC-3 behaviour, shown in words.

## HANDOFF bullet
F7-R5 tax done (claude/f7-r5): SAC summary (Table 12), documents issued (Table 13 + legacy LGC row), GST by bill, invoice register, bill corrections and GST summary (ACC-3) on the report engine both sides; 10 prototype + 7 SQL tests, 6 goldens; no migrations.
