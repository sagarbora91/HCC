# fx-rpt-d AUDIT (wave FX, lane rpt-d, Opus auditor)

Branch `claude/fx-rpt-d`, head faa32540 (pushed, origin = faa32540), base 9c5abf01. Read: FX-CONTRACT §1, §2, §3.2, §3.3; `fx-rpt-d-DONE.md`; `git diff 9c5abf01..claude/fx-rpt-d`; `cu-rpt-b-AUDIT.md`; the prototype definitions `03 Prototype/src/domain/reports/built/stock.ts` and their inputs (`streams/inventory.ts` invAlerts / invCountValue / invOccupied / invDates / invConsumption / invPar* / invMissing / invFmt, `money/common.ts` userName, `reports/mask.ts` RPT_V2_R8, `features/reports/hub-logic.ts` hubDefs, mock handler and fixture move timestamps); server inputs `SqlStockReadStore` (ItemsAsync, RoomsByTypeAsync, SettingsAsync, BatchesAsync, CountsAsync, LossesAsync, MovesAsync, OccupancyAsync, ConsumptionAsync), `StockRules`, `StockQueries`, `ReportRunner`, `ReportService`, `MoneyMasker`, `SqlAccessPolicy`, migration 3700 grants, `Hcc.Stock.Tests/Sql/StockSqlTests.cs`.
Light checks run: `node tools/rpt-catalogue.mjs --check` (up to date, 124 reports); `git diff --stat` = `--ignore-cr-at-eol` (22 files, +1130 / -21); 24 `[Fact]` counted (4+3+3+4+4+3+3). No heavy job was needed.

## Verdict: PASS WITH NOTES

The seven reports match the prototype in ids, columns, column order, totals, filters and figures. The lane wrote no new SQL. Every report reads through the existing `IStockReadStore` queries, which are parameterised and property-scoped. Money is integer paise throughout, and the percentages are exact integers rounded half up. The one real difference from the demo is a permission gap for the Housekeeper (note 1). It sits in the shared report engine, not in this lane's files, and it narrows access: no money can leak through it. The DONE file describes it wrongly. The orchestrator must decide it and record it before merge.

## Notes

1. **Housekeeper parity gap (engine level, orchestrator decision).** In the demo the Housekeeper holds `PERM-STK-001` (`03 Prototype/src/domain/reports/mask.ts` `RPT_V2_R8`; `tests/stream-rpt-built.mjs` line 86 runs stock reports as `housekeeper`). The demo hub then lists the three no-money reports (consumption, par, movements) for that role (`hub-logic.ts` `money||!d.moneyFlag`). On the server, migration 3700 line 111 grants `HKP PERM-STK-001` at scope `A` (assigned). `ReportRunner.HoldsAsync` (`08 Production/src/Hcc.Reporting/ReportRunner.cs` line 30) asks for property scope, so `SqlAccessPolicy` denies it. Result: HKP gets FORBIDDEN on the server and the three reports disappear from that role's production catalogue. Yet the server's own stock page (`StockQueries.RequireReadAsync`, `AccessScopes.Any`) lets HKP read the same moves and consumption.
   - The lane's tests pin this behaviour as "roles without the stock view right": `StockConsumptionReportTests.cs` line 91, `StockParReportTests.cs` line 87, `StockMovementsReportTests.cs` line 77 (the four money reports at lines 70 / 84 / 84 / 88 are refused at the hub anyway, as in the demo). The DONE file says the same ("REC, CSH, HKP refused"). That wording is wrong: HKP does hold the view right, and the engine's scope rule refuses it. The lane did not list this under "Defaults used".
   - The decision, either:
     - (a) the runner accepts a non-own-only scope-`A` view grant, as the prototype and the stock page do, and the three test lines then expect HKP rows with no money column; or
     - (b) the narrower server rule is accepted and recorded as a decision, with the DONE wording corrected.
   - It cannot be fixed inside `Reports/FxD/` without a hack (for example misusing `OwnShift`), so it is not a lane FIX item.
2. **Consumption over 93 days refused (Default 3): accepted.** The prototype `invDates` stops at 93 dates for room-nights but sums the used quantity over the whole range, so its figures for a longer range are wrong. The server refuses with VALIDATION, as the server stock page already does (`StockQueries` `MaxConsumptionDays`). For a range of 93 days or less, both sides give the same figures. Optional follow-up: make the prototype refuse too.
3. **Room-night denominator: the live stays are included.** `SqlStockReadStore.OccupancyAsync` reads `inv.room_night` joined to `inv.reservation` with status `InHouse` / `Departed`, scoped by `inv.room.property_id`, plus `stk.occupancy_history`. `StockRules.Occupied` applies the prototype rule: none after the business date, live nights when there are any, else history. The lane's tests seed only history. The live branch and the after-business-date zero are covered on the same store call by `Hcc.Stock.Tests` (`Live_stays_override_the_imported_history_for_a_night`, and the 24 Oct = 0 assertion in `Consumption_per_occupied_room_night_uses_history_and_flags_shampoo`). Variance is `StockRules.VariancePercent`, exact integer arithmetic rounded half up, the same as JS `Math.round` except at float-noise ties. `perRN` / `std` text uses `Fmt`; .NET `F2` and JS `toFixed(2)` agree on these values.
4. **Item order by code (Default 1): harmless.** In production mode the prototype gets its items from the server in code order anyway. One visible effect, in counts: the store returns count lines ordered by item code, while the demo keeps entry order. The variance value of a count can therefore sit on a different item's row (same amount, same count). Reorder uses `InvariantCulture` against `localeCompare`; the two differ only in exotic collation.
5. **Count variance at current unit cost (Default 5): same as the prototype** (`invCountValue` uses `invItem(...).cost`). Integer paise, no rounding.
6. **Par (Default 4): same rows in practice.** The prototype includes any linen item with a truthy `par` object; the server includes one with at least one `stk.item_par` row. Only an empty `{}` par in the demo could differ, and the server cannot store one. Coverage is `(total*200 + req) / (2*req)` = half up, and 100 when nothing is required, as `invParCoverage`.
7. **Movements (Default 6): harmless, one edge.**
   - `MovesAsync` takes `TOP (50 001) ... ORDER BY move_id DESC` before the item filter and the re-sort by `at_utc`. Above 50 000 moves in a range, a paged (non-`all`) screen run would filter and show a truncated set without refusing. The hub viewer and every file run use `all` and are refused (`RPT_TOO_LARGE`). Not reachable for a hotel of this size.
   - "When" = `IstText` (`yyyy-MM-ddTHH:mm` IST), the same format as the demo's `at` (`s.clock` / fixture `2026-10-20T10:15`).
   - Supplier and `used` labels pass through unchanged, as `invLocLabel`.
8. **Names (Default 7): harmless.** An unknown actor shows "user N"; the prototype shows the raw id. Shared `MoneyRules.NameOf`, the same as CuA.
9. **Batch / count references `LB-<id>` / `CNT-<id>` (Default 2): harmless.** The ids differ between the two sides by design. Ordering: sent date or count date descending, then id descending, on both sides.
10. **Permissions and DEC-061: correct apart from note 1.**
    - All seven views need `PERM-STK-001` and export needs `PERM-RPT-003`.
    - Reorder, laundry, counts and losses have `MoneyFlag = true` with gate `PERM-STK-004`. The engine removes unit cost, charge, variance value and loss value without the gate (rule M).
    - `ReportService.GuardAsync` refuses SUP / HKP / MNT at the hub for those four, the same as the prototype `hubDefs` (NO_MONEY_ROLES includes Supervisor).
    - Consumption, par and movements have no money column and no flag. The claim "Supervisor is allowed consumption and movements without money columns" is true, and true for par too: par is tested at runner level only, but it carries no money flag.
11. **Registration and routing: clean.**
    - `HccComposition.cs`: seven `AddReport` lines inside `// FX-RPT-D … // /FX-RPT-D`, after `// /CU-RPT-B`.
    - `EngineTests.cs`: the seven constructors inside the marked block before `]).All;`. No existing line was touched.
    - `stock.ts`: `serverMirror:false` removed from exactly these seven; none is left in the file, and the stock sheet was already server-run.
    - Both `catalogue.json` copies differ only in seven `"side": "prototype"` → `"both"` lines, and `--check` confirms they are the tool's output.
    - DI: `IStockReadStore` (StockEndpoints) and `IMoneyReportSource` (HccComposition line 109) are singletons.
12. **Tests: real.**
    - They use seeded SQL rows and assert figures to the paise: counts -837.50 = 5 × 12.50 - 2 × 450.00, losses total 1,625.00, laundry charge 1,234.56.
    - They assert ordering and tie-breaks, blank cells while a batch is out, the no-variance line, room-nights 24 against a decoy 10 Oct history, a linen "use" move excluded from consumption, coverage rounding (188.9 → 189, 366.7 → 367), the IST conversion, a trimmed and case-insensitive filter, and the empty property.
    - They check money-column removal and refusals through the real access policy, plus the hub-level DEC-061 refusal. CuB audit item 12(h) is honoured here.
    - A wrong query, sort, filter or label would fail them.
    - Gaps, acceptable because they are covered at store level: live stays, the business-date cutoff, a second property for the six non-reorder reports, and MNT not exercised.
13. **Scope and §1: clean.** Only lane files plus the marked blocks changed. Line endings are kept, there is no migration, no SCHEMA / APK bump, no edit to HANDOFF, CLAUDE.md or memory, and no existing test was weakened. The commit trailer is present and the branch is pushed.

## For the orchestrator
- Decide note 1 (engine scope rule for the HKP view grant) and record it. If you choose (a), lane follow-up: flip the three HKP assertions listed in note 1.
- Merge with rpt-c: keep both marked blocks in `HccComposition.cs` and `EngineTests.cs`, then regenerate both `catalogue.json` copies with `node tools/rpt-catalogue.mjs` (do not hand-merge them), then run `--check`.
