# F6 · lane H1 hb-domain + billing — DONE (3 Nov 2026)

Branch `claude/f6` (worktree `/home/user/wt-f6`), from 705e13bd. Spec OP-17 approved with all defaults (A26).

## Commits
1. `F6 setup: SCHEMA 14, hall types, append blocks` — SCHEMA 13 → 14; optional `s.hallX` (absent = seed, `hallSlice`); `Booking.hallEventId?`; every F6 type in `src/domain/streams/hall.ts`; 7 new `HccError` codes; `// F6-H1/H2/H3` blocks in `types.ts`, `handlers/index.ts` (F6 object), `mock/index.ts`, `handlers/hall.ts`; accounting source kind `hall`. Hashes re-pinned (SCHEMA only): BASE `f8788cff… → d6df4a46…`, FY26 `14d8a8f7… → 961b04c2…`.
2. `F6-H1: prototype hall domain, billing, handlers, accounting sources + tests`.
3. `F6-H1: server HallBilling / HallRules + Hcc.Hall.Tests (HB-GOLDEN, same truth table)`.
4. `F6-H1: server migrations 4300/4320/4740, HallBillingCommands, SqlHallBillingStore, endpoints, HallPosted accounting + SQL golden`.

## Files
Prototype (`03 Prototype/`): `src/domain/streams/hall.ts` (types, slice, clash incl. combined halls, soft holds, calendar without money, enquiry auto-lost, quote expiry reminders, roles), `src/domain/streams/hall-billing.ts` (one truth table: pricing, GST per line by class from the dated master, discount limits, advance plan, allocation, bill lines max(guaranteed, actual), cancellation slabs, inclusive GST split, refund approver, `hallAccSources`), `src/services/mock/handlers/hall.ts`, `src/fixtures/ext/hall.ts` (2 halls + 1 lawn + combined Sagar Hall + Lawn, 3 menus on POS items, 6 lost reasons, A26 settings), `src/domain/streams/accounting.ts` (F6-H1 block), `src/domain/errors.ts`, `src/domain/types.ts`, `tests/_hall.mjs`, `tests/stream-hall.mjs`, `tests/stream-hall-billing.mjs`, SCHEMA / hash pins in 10 existing tests.
Server (`08 Production/`): `src/Hcc.Domain/Hall/HallBilling.cs`, `HallRules.cs`; `src/Hcc.Application/Hall/HallBillingCommands.cs` (commands, `IHallBillingStore`, `HallInvoiceSource`); `src/Hcc.Infrastructure.SqlServer/Hall/SqlHallBillingStore.cs`; `src/Hcc.Api/Endpoints/HallEndpoints.cs` + `HccComposition.cs` F6 blocks; `HccErrorCode` / `HccErrorCatalog` (7 codes); `AccountingEvents.HallPosted` + `HallPostedEvent`; `SourceKinds.Hall`; projector case; `tests/Hcc.Hall.Tests` (new project in `Hcc.slnx`).

## Commands (prototype `execute`) / endpoints (server)
`HALL_VENUE_UPSERT` (Owner), `HALL_MENU_UPSERT` (Owner/Manager, POS ids checked), `HALL_ENQUIRY_ADD/_UPDATE/_LOST/_SOFT_HOLD` (soft hold Manager+, 1–3 days, warns), `HALL_NIGHT_RUN` (auto-lost 30 days after follow-up), `QUO_ACCEPT`, `HALL_ADVANCE_RECEIVE`, `HALL_EVENT_CLOSE`, `HALL_EVENT_CANCEL`, `HALL_REFUND`.
Server: `POST /api/v1/hall/quotations/{id}/accept`, `POST /api/v1/hall/events/{id}/advance|close|cancel|refund` (+ `/commands/{AcceptQuotation|ReceiveHallAdvance|CloseHallEvent|CancelHallEvent|ApproveHallRefund}`).

## Accounting (both sides, same keys)
`hall:advance` Receipt Dr `method:<tender>` · Cr `receipt:deposit` (2000), no GST (DEC-142) · `hall:invoice` Sales Dr `folio:guest` (1100) · Cr `ledger:4400` (hall / decor / services) · Cr `line:food` (4200; `settings.foodLedger='4400'` = Q6 switch) · Cr `line:room` · Cr 2110 / 2120 · `hall:advance-apply` Journal Dr 2000 · Cr 1100 · `hall:balance` Dr tender | `receipt:company-credit` (1110/<company>) | `receipt:udhaar` · Cr 1100 · `hall:cancel` Journal Dr 2000 · Cr `line:attrition` (4300) + CGST/SGST (kept is GST-inclusive at the hall-rent rate) · `hall:refund` Payment Dr 2000 · Cr tender.

## Migrations (lock appended with `--accept-out-of-order`, reason recorded)
- `4300_hall_schema.sql` — schema `hall`: venue, venue_slot, enquiry, quotation, quotation_version, quotation_line, event, event_line, calendar_hold (unique filtered index per physical hall × date × part of day; FULL_DAY = 3 parts; combined venue = its halls).
- `4320_hall_billing.sql` — quotation_advance, event_advance, event_receipt, event bill / invoice / cancellation columns + checks (refund approver ≠ canceller).
- `4740_hall_source_kinds.sql` — **deviation**: `ivc.invoice` and `acc.source` source_kind gain `hall`. Numbered above the contract range because 4687 (LF) drops and re-creates both constraints; a widening below 4687 is undone on a fresh database. Any later migration re-creating them must keep `hall`.

## Tests
- Prototype: `stream-hall.mjs` 8, `stream-hall-billing.mjs` 8 (HB-GOLDEN: v2 total ₹1,37,322.50; bill 160 plates ₹1,44,609; advance ₹20,000 applied; balance ₹1,24,609 → 1110/CO-1; 2000 nets to 0; 4400 Cr ₹23,750; 4200 Cr ₹98,800; CGST = SGST ₹11,029.50). Full `npm test`: 2587 tests, the 16 first-run failures fixed (FY26 hash `415be48f… → e64651dd…` in perf-store / masters-fixture, SCHEMA 14 in history-a, hall commands in the RBAC matrix CMDS, tenders from the money masters) and re-run green on the affected suites (144/144).
- Server: `Hcc.Hall.Tests` 10 (8 BillingTests incl. HB-GOLDEN, 2 SQL: golden through the real accounting consumer + clash / cancel 40 days / refund). `Hcc.Platform.Tests` 80/80 (error-code table updated). `Hcc.Accounting.Tests` 320/320. Solution build 0 warnings.

## Deviations from the contract
- Quotations are created by H2 (`QUO_CREATE` / `QUO_SEND`); H1 tests place a SENT quotation directly in state / SQL.
- No new mapping keys: hall revenue posts as `ledger:4400` (both sides already resolve `ledger:<code>`), food via `line:food`, cancellation via `line:attrition`, advances via `receipt:deposit`; the Q6 switch is the hall setting `foodLedger`, not a mapping key (keeps 4400 retirable and the mapping lists unchanged).
- Prototype invoice number comes from the INV series (`DEMO/2026-27/…` in the seed, `HPE/2627/…` on the server, DEC-197).
- Roles are checked by role (prototype `HALL_ROLES`, server `HallRules` via `plat.user_role`); `policy.RequireAsync(PERM-HAL-00x)` waits for H3's RBAC seed 4345.
- Server bill discount: only new extras below list are held to the role limit (accepted discount stands); no approval queue for it yet (refused `HCC_DISCOUNT_LIMIT`).
- Not done in H1: room block on acceptance (`Booking.hallEventId` declared), maintenance downtime on a hall, hall cash in the front-desk drawer (prototype `drawer()` / server till movement), credit notes on a hall invoice (`HallInvoiceSource` refuses), server enquiry / venue / menu commands (H3 non-money), reads.

## Open questions
- Hall cash advances should count in the shift drawer (prototype `drawer`, server till) — coordinator / H3 to wire.
- Credit-note flow for a printed hall invoice (spec §13) — later wave.
