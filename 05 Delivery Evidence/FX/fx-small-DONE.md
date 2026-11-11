# FX lane `small` - DONE (12 Nov 2026)

Branch `claude/fx-small` (base 9c5abf01).

## What changed
1. **Hall terms sync (production).** Cause: `hallFromServer` hard-coded `templates:[]` and an empty T&C snapshot on every quotation version; the snapshot never carried the Owner's saved terms and the loader never called `GET /hall/templates` (which already existed on the server).
   - `03 Prototype/src/clients/sync/hall.ts`: loader also reads `GET /hall/templates` (a failure, e.g. a role without the view, falls back to the seeded terms); `hallFromServer(r, srvTemplates)` maps them to `s.hallX.templates` (id = server code, e.g. `T-WEDDING`); each quotation version gets its T&C snapshot: the exact locked (code, version) for a sent version, else the version in force on the sent / created date.
   - Server read model: `HallQuotationVersionView` gains optional `TncTemplateCode`, `TncV` (filled from `hall.quotation_version.tnc_template_code / tnc_v`, already stored on SEND). Files: `Hcc.Application/Hall/HallStorePorts.cs`, `Hcc.Infrastructure.SqlServer/Hall/SqlHallStore.cs`. No migration.
   - Save path (`HALL_TNC_TEMPLATE_SAVE` -> `SaveTncTemplate`) was already correct and is unchanged.
2. **D25-1.** `fmtCell` in `03 Prototype/src/features/reports/ReportView.tsx` formatted any string in a date column; "Total" / "Range total" gave "Invalid Date". Now only `YYYY-MM-DD` is formatted, anything else prints as is. The Excel path (`xlsxCell`) already had this guard; print / CSV use other code and are unaffected. All report views (demo and server) use `fmtCell`.
3. **Server cashier expected cash.** Checked: the shift page (`BillingQueries.GetShiftAsync`), the cash desk (`GetCashDeskAsync`) and the close command (`CashierService.ExpectedAsync` in `SubmitShiftCountAsync` and the approval stale check) all read the same `pay.cash_movement` rows (`MovementSql`) through `CashDrawer.Expected`; vendor payments, expenses, counter bills, hall and POS all post to that table through `ICashDrawerPort`. They already agree, no production change. Added tests that prove it to the paisa.

## Tests (exact)
- Prototype: `HCC_TEST_ONLY='sync-hall|stream-fx-small' npm test` -> 63/63 passed (new: 2 `[HSYNC] FX-SMALL`, 2 `[FXS]`). `npx tsc --noEmit` clean.
- `dotnet test tests/Hcc.Hall.Tests --filter FullyQualifiedName~HallQuoteSqlTests` -> 3/3 passed (extended `Snapshot_unchanged_after_template_save...` with the new view fields and the template list).
- `dotnet test tests/Hcc.Expenses.Tests --filter FullyQualifiedName~PurchaseSqlTests` -> 11/11 passed (2 new: shift page = cash desk = close command with a cash vendor payment and cash expense; exact count closes with no difference).

New / changed tests: `tests/sync-hall.mjs`, `tests/stream-fx-small.mjs` (new), `Hcc.Hall.Tests/Quote/HallQuoteSqlTests.cs`, `Hcc.Expenses.Tests/Sql/PurchaseSqlTests.cs`.

## Not verified
- Full `npm test` and full server suites not run (targeted only, per contract). No browser / emulator run of the hall confirmation print against a live server; the round trip is proved by the mapper / loader test with a fake API and by the SQL read-model test.
- The `[FIT]` test was not run: no phone-layout file changed.

## Defaults used
- A role that cannot read `/hall/templates` silently keeps the seeded terms (better than failing the whole hall load).
- A draft quotation version shows the version in force on its created date (the server only locks the version on SEND).

## Open questions / shared files
None. Shared files touched: none of the marked-block files. No migration, no SCHEMA or APK bump needed.
