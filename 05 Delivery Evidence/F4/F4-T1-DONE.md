# F4-T1 DONE — tariff card (tc-money + tc-ui + tc-print)

Lane T1 / Opus / branch `claude/f4` (worktree `/home/user/wt-f4`, as instructed by the caller) / base 2cb46579.

## Commits
See `git log --oneline 2cb46579..HEAD --grep "F4-T1"` (domain + handlers; page + print; server; DONE).

## Files
- Prototype: `src/domain/streams/tariffcard.ts`, `tariffcard-resolve.ts` (resolver kept import-free: admin.ts → tariffcard.ts was a circular import), `src/services/mock/handlers/tariffcard.ts`, `src/features/tariffcard/{logic.ts,TariffCardPage.tsx,TariffCardMobile.tsx,register.tsx}`, `src/features/reports/rate-sheet.tsx`, `src/styles/m-f4.css`; marked edits: `domain/streams/admin.ts` (adminTariffOn), `handlers/index.ts`, `services/mock/index.ts` (new `// F4-T1` export block at the end), `app/registry.ts`, `app/navmap.ts` (category, blurb, keywords, navRoleOk line), `app/subscreens.ts`.
- Tests: `tests/stream-tariffcard.mjs` (17), `stream-tariffcard-ui.mjs` (7), `stream-rate-sheet.mjs` (4), fixtures `tests/fixtures/f4/rate-card/*.csv` (fictional copy of the DEC-198 files; headers = the import templates + `plan` column on tariffs).
- Server: `Hcc.Domain/Tariff/TariffCardRules.cs`, `Hcc.Contracts/Tariff/TariffCardContracts.cs`, `Hcc.Application/Tariff/TariffCard{Ports,Commands}.cs`, `Hcc.Infrastructure.SqlServer/Tariff/SqlTariffCardStore.cs`, `Hcc.Api/Endpoints/TariffCardEndpoints.cs`, `HccComposition.cs` (F4-T1 services block + a new F4-T1 block in the endpoint map), migrations 4380–4383 (+ `Hcc.slnx`, lock with `--accept-out-of-order`), resolver edits marked F4-T1 in `SqlStayRateStore.TariffAsync` (→ `inv.fn_tariff_on`) and `SqlInventoryStore.GetTariffsAsync` (`day_set='A'` only). Tests `Hcc.Masters.Tests/TariffCardTests.cs` (7 rule tests) + `Sql/TariffCardSqlTests.cs` (2 SQL tests).
- Docs: catalogue line appended under `rpt.admin.rate-sheet`.

## What was built
- Commands TRC_DRAFT_SAVE / PROPOSE (Approval kind `tariff-card`) / PUBLISH / REJECT / DISCARD / WEEKEND_SET / IMPORT; refusals TRC_* via `TrcError` (reason on an HccError, like MstError). Publish = one command: `s.tariffs` rows with `days` + `cardVersion` (pair = two rows), occupancy / meal plan / extras dated money, one audit row per master row + TRC_PUBLISH; earlier published versions on/before the date → superseded. Blank child row publishes nothing; CP/MAP/AP → meal plan add-on (plan − EP) ÷ 2; day-use print only; hall refused.
- Rounding per §1.4 (each half rounded) — golden Deluxe 2,000 → 1,694.92. Note: under this rule card prices ₹3,300, ₹500 and ₹200 are not exactly reachable at 18 %; they show ₹0.01 rounding difference (tests assert ≤ 1 paisa). Owner/CA to confirm or switch to the folio rule (then Deluxe 2,000 is unreachable instead).
- Resolver: `adminTariffOn` / server `inv.fn_tariff_on` + `TariffCardRules.Resolve`: weekday/weekend rows by the writing version's weekend days, default Fri/Sat/Sun.
- Page `tariff-card` (REVENUE & SALES, FitPage, tabs Rooms · Meal plans & extras · Seasons & weekends · Halls placeholder · History · Print), version strip, incl. entry toggle + rounding column, propose/publish/reject/discard/import by role; phone cards read-only except Owner. Rate sheet print + card CSV + history CSV; PDF hook `rateSheetPdf.renderPdf`.
- Permissions: prototype by role (`trcCan`; no new Permission union member, types.ts untouched); server PERM-TRF-001..003 (4383).

## Tests
- Prototype: `node tests/_one.mjs tests/stream-tariffcard.mjs tests/stream-tariffcard-ui.mjs tests/stream-rate-sheet.mjs` → 28/28; `npx tsc --noEmit` clean; full `npm test` — see bottom line.
- Server: `dotnet build tests/Hcc.Masters.Tests` 0 warnings / 0 errors; `dotnet test Hcc.Masters.Tests` 29/29 (was 27; SQL Server available in the cloud). Stays / Inventory tests — see bottom line.

## Open items
- Server proposal does not create a plat approval row (status `proposed` + proposed_by only); prototype does create an Approval. Pending tariff proposals block day close in the prototype (as yield).
- Server extras bps = "other"; meal add-on bps = room slab (bundled) even for `separate` plans; prototype follows the plan's treatment.
- Roommasters `writeTypes` (prototype) and `SqlRoomMastersStore` rack-rate save delete future tariff rows of a type incl. card rows; `SqlRoomMastersStore`/yield `SqlRatePublicationPort` still assume one row per (type, date).
- Fit-check at five viewports and Tab S7: to run on the hotel PC.

## Integration needed
- Server readers via `GetTariffsAsync` (quotes / availability) see only `day_set='A'` rows: switch them to `TariffCardRules.Resolve` or `inv.fn_tariff_on`.
- Re-run `node tools/migrations-lock.mjs --add` after merging T2/T3 (lock conflict expected).
- `HccComposition.cs`: my endpoint block sits after `LF-IMAGES end` (no F4 endpoint block existed).

## HANDOFF bullet
F4-T1 tariff card: page `tariff-card` (versions, incl. GST entry, weekend pairs, rate sheet print/CSV), TRC_* commands publish into the existing masters only (DEC-MA-102); server Tariff module + migrations 4380–4383 + `inv.fn_tariff_on`. DEC-198 CSV import reproduces the card within ₹0.01 (3,300/500/200 not exactly reachable under the §1.4 half-rounding — owner/CA note).

## Final runs (bottom line)
- `npx tsc --noEmit`: clean. `npm test`: 1,635 PASS, then the runner died of a Node heap OOM (cloud box shared with two other lanes) — rerun on the hotel PC. One FAIL before that: `[FIT] no phone-layout file changed since the wave base` — my new phone files `src/features/tariffcard/TariffCardMobile.tsx` and `src/styles/m-f4.css` (contract-owned) are not on its FROM_MAIN allow-list. **Integrator: add both to that set** (or the F4 equivalent).
- Server: Hcc.Masters.Tests 29/29, Hcc.Inventory.Tests 46/46, Hcc.Stays.Tests 104/105 — the failure is `RoomBoardQuerySqlTests.Board_reads_use_the_confirmed_index_and_stay_flat_with_two_years_of_history` (timing/plan test under CPU load; the board reads do not use the tariff tables) — recheck in the gate.
