# F8D-B1 DONE (lane f8d-b1, Opus 5.5), 8 Nov 2026

Branch `claude/f8d-b1` (from claude/f8d-tests-b 5c96ca5a). Head hash: `git log -1` of the branch (the commit that adds this file).

## Bug B1 (fixed)
`08 Production/src/Hcc.Reporting/Documents/DocumentSql.cs` `FolioHeaderAsync` built `FolioHeader` from five reservation columns and never set `InvoiceNo`, `BillTo`, `BuyerGstin`, so every folio print showed Bill to = the stay's guest, an empty Buyer GSTIN and "Not yet issued".

## What changed
- `DocumentSql.cs:18-31` `HeaderSql` now also reads the folio's own tax invoice (`OUTER APPLY` on `ivc.invoice`, `source_kind = 'folio'`, `source_id = folio_id`, latest `revision`, the same pick as `SqlInvoiceStore.InvoiceIdForSourceAsync`) and the folio payer (`bill.folio.payer_name`, `payer_gstin`).
  - Issued: Invoice no = that invoice's number; Bill to = the invoice's `buyer_name` (falls back to `payer_name`); Buyer GSTIN = the invoice's `buyer_gstin`. The print therefore never disagrees with the statutory invoice.
  - Not issued: Bill to = `payer_name`, Buyer GSTIN = `payer_gstin` (company folio: the company name + GSTIN copied at OpenFolio; guest folio with own GSTIN, SFQ-9: that GSTIN). `DocumentBuilders.Folio` (unchanged) still falls back to the guest when there is no payer name, so folio A and every single-folio stay print exactly as before.
- `DocumentSql.cs:33-46` `Text()` helper (null / blank → null, CHAR(15) GSTIN trimmed) and the three new `FolioHeader` arguments.
- `08 Production/tests/Hcc.Integration.Tests/F8dTestsBTests.cs`:
  - `F8D_B_per_folio_print_company_folio_names_the_company_as_payer` un-skipped; also checks B says "Not yet issued" and folio A names the guest with no company / GSTIN.
  - new `F8D_B1_per_folio_print_shows_each_folio_issued_invoice_number`: split stay checked out (A cash ₹590, B company credit ₹9,450), two invoices; print A shows invoice A only, no GSTIN; print B shows invoice B only, Deccan Agro + GSTIN; neither says "Not yet issued".
  - new `F8D_B1_single_folio_print_is_unchanged`: single-folio stay prints Guest and Bill to = the guest, "Not yet issued", no GSTIN.

## Prototype twin
`03 Prototype/src/domain/reports/documents/folio.ts:26-33` draws the per-folio print from `sfView(b,f)` (`src/domain/streams/folio-split.ts:56-70`), which sets that folio's own `invoiceNo` and its payer as `company` (company: name + GSTIN; guest with GSTIN: same), and Bill to falls back to the folio payer name (Udhaar) then the guest. **Defect not present; no prototype change.** Existing test `[F8-SF-UI] one document per folio` (`tests/stream-split-ui.mjs:120`) already asserts B's Bill to = Deccan.

## Tests run (Invoke-Serialized.ps1, label f8d-b1, filtered)
- `dotnet test tests/Hcc.Integration.Tests --filter FullyQualifiedName~F8dTestsBTests`: **12 passed, 0 skipped, 0 failed** (12; was 9 passed + 1 skipped).
- Prototype `HCC_TEST_ONLY=stream-split-ui npm test`: **64/64 passed** (core tests + the 8 stream-split-ui tests, incl. "one document per folio").

## Not run
Full server suite, full prototype suite, the gate. Receipt print (`DocumentBuilders.Receipt`, "Received from" = guest) not changed: outside B1.

## Defaults / contract relied on
SF-CONTRACT §2.2 (invoice buyer = folio payer: company with GSTIN; Udhaar = the customer's name; guest = the payer name, B2B only with a GSTIN), §5 (one invoice per non-zero folio, letter order), SFQ-8 (₹0 folio: no invoice, so it keeps "Not yet issued"), SFQ-9 (guest extras folio: guest name, no GSTIN).

## Observation (not fixed, other lane)
Udhaar folio: `SplitFolioService.OpenAsync` (`Hcc.Application/Billing/SplitFolioService.cs:94-98`) stores `payer_name` only when the request carries `PayerName`; it does not look up the credit customer's name from `CustomerKey`. If the desk sends no name, both the issued invoice (`SqlInvoiceStore.FolioPartyAsync`, `COALESCE(f.payer_name, r.guest_name)`) and this print fall back to the stay's guest, against §2.2 ("the customer's name"). The print follows whatever the invoice carries; the fix belongs in OpenFolio (billing / sf-domain).
