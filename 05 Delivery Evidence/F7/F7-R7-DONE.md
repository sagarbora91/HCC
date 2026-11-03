# F7-R7 DONE - documents (batch 3)

Lane R7 documents · Sonnet · branch `claude/f7-r7` (worktree `/home/user/wt-f7-r7`) · base `57e2c4bb`.

## Design
A document = one **DocumentModel** (header facts, tables, notes, signature captions, `money` flag, optional plain-string QR text - never an IRN) built once by a pure builder.
Lodge look + HCC letterhead (A16): `docHtml` / `DocumentHtml` (same CSS as R3's `LODGE_CSS`). Screen, Excel/CSV and PDF/print come from the **engine's own renderers**: `docResult` /
`DocumentResults.ToReport` turn the model into a sectioned `RptResult` / `ReportResult`, so the three-way check (screen = Excel = PDF text = print) holds by construction and is tested.
Documents are a **separate registry** (`DOCUMENTS`), not table definitions: no change to the catalogue, `catalogue.json` or the table-report goldens.

## Documents built (prototype AND server)
| Id | Prototype `src/domain/reports/documents/` | Server `Hcc.Reporting/Documents/DocumentBuilders.cs` | Roles / money |
|---|---|---|---|
| `doc.registration-card` (R3 builder, wired) | `index.ts` -> `registrationCardLayout` | `FromLodge` over `LodgeLayouts.RegistrationCardAsync` | PERM-STY-001 or PERM-RPT-001; ID masked |
| `doc.day-end` (R3 builder, wired) | `index.ts` -> `dayEndLayout` | `FromLodge` over `LodgeLayouts.DayEnd` | PERM-RPT-001; money block only with PERM-RPT-002 |
| `doc.folio`, `doc.invoice` | `folio.ts` | `Folio`, `Invoice` | folio rights (billing / payments); QR text, no IRN; number only when issued |
| `doc.credit-note` | `folio.ts` | `CreditNote` | same; GST at the original line rate |
| `doc.receipt` | `folio.ts` | `Receipt` | same; amount in words |
| `doc.quotation` | `folio.ts` | `Quotation` (input record) | reservations / billing |
| `doc.rate-card` | `ratecard.ts` | `RateCard` | tariff-card view right; no hotel money |
| `rpt.people.payslip` | `payslip.ts` | `Payslip` | payroll roles any line; staff only their own Locked line (as the payslip dialog) |
| `doc.kot` | `pos.ts` | `Kot` | no money column at all; restaurant / billing |
| `doc.outlet-bill` | `pos.ts` | `OutletBill` | billing / payments; RST tax invoice with CGST / SGST |
| `doc.handover-pack` | `handover.ts` | `HandoverPack` | cash float column only for roles that see handover cash (`hovSeesCash`) |

Housekeeping, supervisor, maintenance and stock roles never get a money document (own payslip excepted, DEC-061). Permissions follow the existing screen / read rule; nothing widened (R5 ruling).

## Wiring (prototype, `{/* F7-R7 */}` blocks, small inserts)
`features/reports/documents/DocButton.tsx` (`DocPrintButton`: builds the model, prints through `printPage` / a hidden frame in the browser, writes the print-log row). Buttons added in:
GuestProfile (registration card), ClosingReport (day end), folio InvoiceModal (folio / invoice + each credit note) and the receipt list (receipt), Confirmation (quotation),
HRPayslipModal, TariffCardPage print tab (letterhead card), HandoverPage dashboard (hand-over pack), PosPrintModal (KOT / outlet bill).
`handlers/reports.ts` `RPT_LOG` (F7-R7 block): a printed document writes one `exportLog` row (rule V.4), refused for a caller who may not print it.

Server: `DocumentService` (render html / pdf + `rpt.export_log` row), `Hcc.Api/Endpoints/DocumentEndpoints.cs`:
`GET /documents/invoice/{id}`, `/credit-note/{id}`, `/payslip/{runId}/{staffId}`, `/bill/{orderId}`, `/kot/{orderId}/{kotId}` (`?format=html|pdf`); the permission is that of the underlying query.

## Tests
- Prototype `tests/stream-rpt-docs.mjs` **10 tests** (+ `rpt-docs-entry.ts`): registry, totals = folio / sales totals, GST split, QR no IRN, credit note, receipt words, quotation, payslip (own vs colleague vs payroll), KOT without money, bill, rate card, hand-over cash rule, role matrix, print log, CSV neutralisation, three-way on every document, goldens in `tests/rpt-golden/doc-goldens/` (`DOC_GOLDEN_ACCEPT=1` to accept).
- Server `tests/Hcc.Reporting.Tests/Documents/DocumentTests.cs` **8 tests** (DTO fixtures; three-way via `RendererTests.ThreeWay`, PDF text, CSV neutralisation).
- Counts: prototype `npm test` 2652/2652 (tsc clean); `dotnet build Hcc.slnx` 0 warnings, 0 errors; `Hcc.Reporting.Tests` DocumentTests 8/8. Migrations: **none** (4434-4435 unused).

## Integration needed (f7-int)
- `HccComposition.cs`: `api.MapDocumentEndpoints()` and `services.AddSingleton<DocumentService>()` are already added in `// F7-R7` blocks.
- Catalogue: documents are not table entries; `rpt.people.payslip` stays `built` (now with a Lodge-look document). Nothing to regenerate.
- Folio / receipt / quotation / rate card / hand-over pack **endpoints** are not added on the server (the builders exist and are tested): they need the reservation header (guest, room, dates) joined to the folio, or the handover / tariff query services; add routes the same way as the five above.
- Server registration card / day end: `LodgeLayouts.RegistrationCardAsync` exists; an endpoint wrapping `DocumentBuilders.FromLodge` is the missing line.
- Windows PC only: print preview on the Tab, one paper print of the invoice and registration card, fit-check.

## Open questions
1. Documents are print-only (`print`, `pdf`); no Excel export button (they are legal layouts, not data sheets). `docResult` does allow Excel / CSV if the owner wants it.
2. QR text is a plain `HCC|hotel|number|date|total` string (contract: "QR text, no IRN"); a UPI / e-invoice QR needs an owner decision.
3. Quotation = the booking confirmation figures (nights x agreed rate + GST of the dated master + advance); there is no separate quotation record in HCC.
4. Prototype KOT permission = billing / payments / Restaurant / Manager (the POS page's right); the matrix has no PERM-POS row in v1.

## HANDOFF bullet
F7-R7 documents done (claude/f7-r7): folio, tax invoice, credit note, receipt, quotation, rate card, payslip, KOT, outlet bill and hand-over pack as Lodge-look documents on the report engine (prototype + server), R3's registration card and day-end builders wired to their buttons, print-log rows, 10 prototype + 8 server tests, doc goldens; no migrations.
