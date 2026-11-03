# F4-T2 done: revenue by category and tender, owner digest (branch claude/f4-t2)

Built
- Prototype `src/domain/streams/rpt-revcat.ts` (`rctReport`, `rctReportFor`, `rctCsv`), page `revenue-category` (`src/features/reports/RevenueByCategory.tsx`, Excel `rctXlsx.ts`). Blocks A / B / C over `reportLines` / `reportReceipts`; basis toggle business-date or stay (Block A only; the bridge stays on business dates). Roles Owner / Accountant / Manager; others FORBIDDEN.
- Digest `src/domain/streams/digest.ts` (`dgsCard`, held drafts written by DAY_CLOSE through one `// F4-T2` line, `DIGEST_REDRAFT`), page `owner-digest` and the "Yesterday" card on the Dashboard (Owner only). Two drafts per day (Email with money, WhatsApp counts only), status held, reason names F9 and F5, idempotent per date and channel.
- T3 names are stubbed in `src/features/digest/_contract-stubs-f4.ts`; the integrator swaps the import for `qwReorderAlerts`, `fbkOverdue`, `fbkSummary`.
- Server: `RevenueByCategoryCalculator` (Domain), `RevenueByCategoryQueries` + `GET /api/v1/reports/revenue-by-category-tender`, `DigestDraftQueries` + `GET /api/v1/digest/drafts`; migrations 4370 (indexes), 4375 / 4376 (digest.draft), 4377 (PERM-DGS-001 Owner), all in `migrations.lock.json` (accepted out of order, reason recorded) and `Hcc.slnx`.

Tests: `tests/stream-rpt-revcat.mjs`, `tests/stream-digest.mjs`, RBAC matrix entries; server `tests/Hcc.Closing.Tests/Unit/RevenueByCategoryTests.cs` (4 pass).

Second pass (coordinator gaps)
1. Legacy FY: prototype `rctLegacyReport` (lgc-shaped bills and receipts, header "Legacy data (old Lodge software)", unmapped room kept as `unassigned`); server `RevenueByCategoryCalculator.ComputeLegacy` fed by `SqlRevenueCategorySource.LegacyAsync` (lgc.bill / lgc.receipt, room type from the rooms master by name); when the range holds legacy bills the endpoint returns that report. Tests assert Σ components, GST and receipts equal the inputs (the LG §6 sums); the real-data per-FY comparison against `totals.json` was not run here (no legacy files in this environment).
2. Block C vs trial balance: prototype `tb.rows` (7 ledger rows: 1100 opening / closing due, 2000 advances start / end, credits raised on 1110 / 1115 / 1120) asserted on ACC-RULEBOOK April 2026 (all tie, difference 0); server bridge has opening / closing due and advances (`FolioBalances` via SQL) and `Ledgers` rows from the six-column TB (`AccountingReportQueries`). Server test uses the golden-month TB figures as data (no database in this run), plus a broken-ledger case.
3. Server day close: `DigestDraftProjector` consumes the `BusinessDayClosed` outbox event (same pattern as the OTA channel-manager projector, hosted worker `DigestDraftWorker`) and writes the two held drafts into digest.draft in the consumer transaction; nothing is sent. Pending approvals count covers folio approvals only; reorder / complaint counts are 0 until T3.
4. Nav gates: `navRoleOk` hides `owner-digest` from everyone but the Owner and `revenue-category` from everyone but Owner, Manager, Accountant; RBAC matrix updated.

Still open: the production page reads the prototype selector rather than the new endpoint; `subscreens.ts` F4-T2 block empty; the real-data legacy tie-out per FY.
