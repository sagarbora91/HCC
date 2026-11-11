# fx-viewer AUDIT (Opus)

Lane `viewer` / branch `claude/fx-viewer` / head 5a787292 / base 9c5abf01. Diff read: `git diff 9c5abf01..claude/fx-viewer` (4 files, +123 / -7).

## Verdict: PASS WITH NOTES

## Checks

| Claim / rule | Result |
|---|---|
| (a) Viewer already paged locally; trailing totals hidden until the last page; `splitTail()` pins them, totals unchanged | Confirmed. Base `ResultTable` / `ResultCards` ran `usePaged(result.rows, …)` over all rows, the total row included, so on 250 rows it was row 251 and sat behind "Show more". `splitTail` (ReportView.tsx:26) only moves the trailing non-`row` rows out of the paged list; the total's cells are the server's figures and are not recomputed. Test asserts ₹31,375 = sum of all 250 rows. |
| (b) Export PDF, production only, needs catalogue `canExport` and `pdf` in formats, through `fetchHubFile(...,'pdf',...)`; demo unchanged | Confirmed (ReportView.tsx:62, 69, 75). Button is gated by `process.env.HCC_BUILD==='production'`; `serverFile` is undefined in the demo build so `pdf()` is a no-op there anyway. Demo bundle test shows no button. The server endpoint `GET /reports/{id}/export` already parses `pdf` (ReportService.ParseFormat) and writes `rpt.export_log` in `ExportAsync`, so "logged like the other formats" holds without a client POST. Claim that the demo has no PDF button / no client PDF writer is correct (only `printDocument` "Print / save PDF" in modals). |
| (c) No FIT-watched phone file changed | Confirmed. The watch regex in `tests/stream-fit.mjs:94` matches `src/mobile/**`, `src/app/mobile/**`, `*Mobile.tsx`, `styles/mobile*.css` / `m-*.css`. `src/features/reports/ReportView.tsx` matches none, although it renders the phone cards. No allow-list line needed; none added. |
| (d) DEC-061: no money export for a no-money role through the new button | Holds, on the server. `ReportService.ExportAsync` calls `GuardAsync` (403 for a money report when every role is SUP/HKP/MNT/STK), then `runner.RunAsync(... Export: true, ExportFormat)` (CanExport + format offered), then `StripMoney`. Format-independent, so pdf is covered by the same path as xlsx/csv. Client side, the catalogue already drops money reports for such callers and `canExport` is the server's flag. |
| (e) Tests fail without the change | Mostly yes (by reading; not run against the base to avoid touching the lane tree): tests 1–5 and 7 fail on base (no `splitTail` / `ResultTable` export, total hidden at row 251, no PDF button, no `serverFile('pdf'…)` text). Tests 6 (demo) and 8 (`exportHubFile` pdf over a fake server) pass on base too: they are guards, not regressions of this change. |
| Light runs | `node tests/_one.mjs tests/stream-fx-viewer.mjs`: 8 passed, 0 failed. `npx tsc --noEmit`: clean. Full `npm test` not run (gate will; executor reports it unconfirmed — honest). |
| Scope / ownership / §1 | Only ReportView.tsx + two new test files + DONE. No registry, routing, CSS, migration, server, phone file. Date cell (lane `small`) untouched. Line endings: `--stat` equals `--stat --ignore-cr-at-eol`. Commits carry the required co-author line. `tests/.generated` not committed. |

## Notes (no fix required)

1. `05 Delivery Evidence/FX/fx-viewer-DONE.md` line 7: "the hub contract carries no page parameters" is not accurate. `GET /reports/{id}` takes `page` / `pageSize` (`08 Production/src/Hcc.Api/Endpoints/ReportEndpoints.cs:47`, `ReportRunner.RunAsync` slices rows at line 90-94). The decision to page locally is still the right one: the server slice is taken after totals are computed, so the total row would arrive only on the last page — the very fault this lane fixes — and the brief made server paging optional. The orchestrator should record the reason correctly in HANDOFF ("server paging exists but slices the total row with the details").
2. `ReportView.tsx:26` `splitTail` pins every trailing non-`row` row. A report ending `…rows, subtotal (last group), total` shows that last group's subtotal under page 1, away from its group's rows. Acceptable (figures are right), but worth a glance when reports with group subtotals are reviewed on a long range.
3. The demo build's paging also changed (total pinned) — `splitTail` is not production-gated. That is an improvement and matches the brief's intent; the DONE's "demo build is unchanged" refers to the PDF button only.
4. Test 7 (`stream-fx-viewer.mjs`) checks the click wiring by regex over the source, not by a click. The loader itself is tested over the fake server; an end-to-end click in a browser/APK and a real server PDF byte stream remain unverified, as the DONE says.
5. Merge note: lane `small` will edit `fmtCell` in the same file (D25-1); this lane's `splitTail` is inserted right after `fmtCell` (line 25-26). Expect an adjacent-hunk conflict at merge; resolve by keeping both.
