# CU f8e-open DONE: F8E leftovers (11 Nov 2026)

Branch `claude/cu-f8e-open` (worktree `E:\Projects\HCC\worktrees\cu-f8e-open`), base c45bbce3. Head: the commit that adds this file (`git log -1 claude/cu-f8e-open`).

## What changed
1. **P2-B company-credit statement opening balance (server).** `08 Production/src/Hcc.Reporting/Reports/P2B/P2BReports.cs`: `CompanyCreditReport` gets an optional constructor parameter `IOpeningEntryStore? openings = null` (existing `new CompanyCreditReport(null!, null!, null!)` calls still compile) and `Statement(facts, long opening = 0)`. The opening is `OpeningOf(entry, companyId)`: the net debit on the party ledger `1110/CMP-<id>` of the APPROVED opening entry (ACC-1 hand-entered opening balances), never below 0, a draft never counts. This is the server twin of the prototype's `openingOutstanding` (which the prototype itself fills from the same opening entry in the accounting fixtures). The statement then runs from that opening to the paise, closing included.
2. **Company-credit counter-bill tests.** New `08 Production/tests/Hcc.Counter.Tests/Sql/CuF8eOpenCounterCompanyCreditTests.cs` (service: stored with company id and mode `company`, no till movement, open counter bills add up against the limit, over-limit refused with "Credit limit exceeded ... this counter bill" and zero writes, limit 125999 refuses and 126000 allows to the paise). New fact `Counter_bills_report_shows_a_company_credit_bill_and_its_credited_state_with_exact_paise` in `08 Production/tests/Hcc.Reporting.Tests/Sql/F8ESqlGapTests.cs` (report tender text "Company credit", credited variant, totals).
3. **Per-cashier filter in sales by item.** New optional parameter `cashier` (type `user`) on `rpt.rest.sales-by-item`: prototype `03 Prototype/src/domain/reports/p2-c/sales-by-item.ts` (`st.by === cashier`), server `Reports/P2C/P2CReports.cs` (`SaleLineFact.SettledBy` as string). Filters intersect with the existing own-shift rule (a Cashier asking for someone else sees nothing). `catalogue.json` (both copies) regenerated with `node tools/rpt-catalogue.mjs`; only the `cashier` param line changed.
4. **Dark / high-contrast check of the F8C/F8D/F8E folio CSS.** No change needed. `folio.css` and the folio-dup rules contain no hard-coded colour; every `--c-<hex>` they use is aliased to a role or has a generated dark and high-contrast value in `themes.css`. Contrast measured on all text/background pairs (dark >= 7.2, high contrast >= 10.9, switcher small text at opacity .85 >= 5.7 dark / 8.6 hc, dup-tick border 3.18 dark / 10.09 hc). No token changed, so `themes.css` was not regenerated. Pinned by a new test.

## Files
- `03 Prototype/src/domain/reports/p2-c/sales-by-item.ts`, `src/domain/reports/catalogue.json` (generated)
- `08 Production/src/Hcc.Reporting/Reports/P2B/P2BReports.cs`, `.../P2C/P2CReports.cs`, `tests/Hcc.Reporting.Tests/catalogue.json` (generated)
- New tests: `03 Prototype/tests/stream-cu-f8e-open.mjs` (3 tests), `08 Production/tests/Hcc.Reporting.Tests/P2B/CuF8eOpenCompanyOpeningTests.cs` (2), `08 Production/tests/Hcc.Counter.Tests/Sql/CuF8eOpenCounterCompanyCreditTests.cs` (1), fact in `F8ESqlGapTests.cs` (1)

## Tests run (all through Invoke-Serialized.ps1, label cu-f8e-open)
- Prototype `HCC_TEST_ONLY='stream-cu-f8e-open|stream-rpt-p2-c|stream-rpt-p2-b|stream-rpt-engine' npm test`: 104/104 passed (before the theme test was added).
- Prototype `HCC_TEST_ONLY='stream-cu-f8e-open|stream-themes|stream-rpt-engine' npm test`: 98/98 passed (includes the 3 CU tests, the catalogue-up-to-date test and the themes tests).
- `npx tsc --noEmit`: clean.
- `dotnet test tests/Hcc.Counter.Tests --filter CuF8eOpenCounterCompanyCreditTests`: 1/1 passed.
- `dotnet test tests/Hcc.Reporting.Tests --filter "CuF8eOpen|F8ESqlGapTests|P2CReportsTests|P2BReportsTests|EngineTests"`: 46/46 passed (includes the 2 opening-balance tests, the new company-credit counter-bill report fact and the catalogue-equals-registry test).

## NOT verified
- Full `npm test`, builds and the full server suite (the gate runs them).
- The server opening balance was tested at unit level (`OpeningOf`, `Statement`), not through HTTP with a real approved opening entry in SQL.
- Only the filtered Reporting run above was done, not the whole Reporting project.

## Defaults used
- The statement has no period parameter, so "entries dated before the period start" does not apply. The default is the approved opening entry's `1110/CMP-<id>` line (hand-entered opening balance, ACC-1). A company with no opening line, or only a draft, opens at 0 (same as before).
- Cashier filter value is the user / principal id (as `rpt.money.shift` does); the prototype id and the server principal id are each side's own key.

## Open questions / observations (for the orchestrator, not blocking)
1. Server "Used" / "Remaining" and the payment guard (`before.Receivable`) do not include the opening outstanding, while the prototype receivable (`grpReceivable`) does. The statement now shows the opening, but a payment against the opening alone is still refused by the server as "owes nothing". Needs an owner/CA ruling (and a place on the company row) before the exposure changes; left unchanged on purpose (money rule, not in this brief).
2. The prototype wire layer (`clients/sync/money/wire.ts`) maps server companies with `openingOutstanding: 0`, so a production-synced prototype shows 0 opening in its own copy of the report; the server report is the one with the figure.
3. `catalogue.json` is a generated shared file: `rpt-a` / `rpt-b` regenerate it too; on merge, re-run `node tools/rpt-catalogue.mjs` instead of resolving by hand.

## Shared files touched
`03 Prototype/src/domain/reports/catalogue.json` and `08 Production/tests/Hcc.Reporting.Tests/catalogue.json` (generated, one param line). No registry, `Hcc.slnx`, migration, `v22-fixes.css`, `themes.css` or `stream-fit.mjs` change. No SCHEMA or APK bump.
