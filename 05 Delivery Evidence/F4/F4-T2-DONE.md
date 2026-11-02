# F4-T2 done: revenue by category and tender, owner digest (branch claude/f4-t2)

Built
- Prototype `src/domain/streams/rpt-revcat.ts` (`rctReport`, `rctReportFor`, `rctCsv`), page `revenue-category` (`src/features/reports/RevenueByCategory.tsx`, Excel `rctXlsx.ts`). Blocks A / B / C over `reportLines` / `reportReceipts`; basis toggle business-date or stay (Block A only; the bridge stays on business dates). Roles Owner / Accountant / Manager; others FORBIDDEN.
- Digest `src/domain/streams/digest.ts` (`dgsCard`, held drafts written by DAY_CLOSE through one `// F4-T2` line, `DIGEST_REDRAFT`), page `owner-digest` and the "Yesterday" card on the Dashboard (Owner only). Two drafts per day (Email with money, WhatsApp counts only), status held, reason names F9 and F5, idempotent per date and channel.
- T3 names are stubbed in `src/features/digest/_contract-stubs-f4.ts`; the integrator swaps the import for `qwReorderAlerts`, `fbkOverdue`, `fbkSummary`.
- Server: `RevenueByCategoryCalculator` (Domain), `RevenueByCategoryQueries` + `GET /api/v1/reports/revenue-by-category-tender`, `DigestDraftQueries` + `GET /api/v1/digest/drafts`; migrations 4370 (indexes), 4375 / 4376 (digest.draft), 4377 (PERM-DGS-001 Owner), all in `migrations.lock.json` (accepted out of order, reason recorded) and `Hcc.slnx`.

Tests: `tests/stream-rpt-revcat.mjs`, `tests/stream-digest.mjs`, RBAC matrix entries; server `tests/Hcc.Closing.Tests/Unit/RevenueByCategoryTests.cs` (4 pass).

Not done / for the integrator
- Legacy FY header and the LG bill and receipt tie-out (rule 4): `legacy` is always false; `lgc` data is not read yet.
- Block C against the trial balance: the prototype bridge is built from folio data (opening and closing due, advances) and reports whether the trial balance balances; the ledger-by-ledger comparison on the April golden month is not asserted. The server bridge has no opening / closing due (needs full history).
- Server: the digest drafts are not yet written by the server day close (store, query and endpoint exist); production page wiring reads the prototype selector, not the new endpoint.
- Nav gate: `owner-digest` uses the coarse `admin` permission (Owner and Tech admin see it; the page refuses Tech admin); `revenue-category` uses `reports` (Cashier sees the entry, the page refuses). Documented in the RBAC matrix test.
- `subscreens.ts` F4-T2 block left empty.
