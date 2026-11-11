# fx-viewer DONE

Lane `viewer` / branch `claude/fx-viewer` / base 9c5abf01. Paging and a PDF button in the report viewer when it shows a server report (production mode).

## What changed
- `03 Prototype/src/features/reports/ReportView.tsx`
  - **Paging.** Finding: the production viewer already pages locally (`usePaged` / `ShowMore`, 100 desktop / 50 phone) over the all-rows answer (`GET /reports/{id}?all=true`); the hub contract carries no page parameters, so server-side paging was not built (default below). One real fault fixed: the closing total / subtotal rows sat at the end of the paged list, so on a long report the total was hidden until "Show more" was pressed to the end. New `splitTail()`: paging covers the detail rows only, the trailing total rows are always shown under the page (table and phone cards). Totals remain the server's totals of the whole report (nothing is recomputed).
  - **PDF button.** New "Export PDF" button in `ExportBar`, production only, shown only with the server export right (`canExport`) and when the report's formats include pdf. It uses the existing `fetchHubFile(...,'pdf',...)` (`GET /reports/{id}/export?format=pdf&encoding=base64`) and `saveBinaryFile` / `saveAndReport` (native share sheet or download) like Excel / CSV; the server writes the `rpt.export_log` row. `ResultTable` exported for the test.
  - Finding: the demo viewer has NO PDF button and the prototype has no PDF writer (only a placeholder in `rate-sheet.tsx`); the demo build is unchanged (button is under `process.env.HCC_BUILD==='production'`).
- New tests: `03 Prototype/tests/stream-fx-viewer.mjs`, `03 Prototype/tests/fx-viewer-entry.tsx`.
- No server file changed, so no server tests. No phone file, no CSS, no migration, no shared registry touched (FIT allow-list not needed).

## Tests
- `tests/stream-fx-viewer.mjs`: 8 / 8 pass (splitTail; 250 rows -> 100 + total row + "Showing 100 of 250", total = whole-report figure; short report no Show more; phone 50 cards + total card; PDF button present / absent without export right / absent without pdf format / disabled with no result; demo build has no PDF button and paging unchanged; click wiring; `exportHubFile` pdf over a fake server: query, mime, file name, no POST).
- `npx tsc --noEmit`: clean.
- Full `npm test` through the lock: see the lane reply for the final count.

## Not verified
- Real click in a browser / APK (save dialog), and a real server PDF byte stream: only the loader against a fake server and static render were tested. Server PDF export of each report not re-checked (existing af2 server tests).
- Phone layout in a browser.

## Defaults used
- Local paging over the all-rows answer instead of server-side paging (contract has no page parameters; <= 50,000 rows cap stays).
- PDF button production-only (no client PDF writer exists).
- Trailing total/subtotal rows pinned under the page; mid-list subtotals stay inside the paged rows.

## Shared files touched
None of the marked-block shared files.

## Open questions
None.
