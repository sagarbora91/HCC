# MEAS2 lane C (settings): M9 + L7 catalogue side, done 7 Nov 2026

Branch `claude/meas2-c`, cut from `claude/meas` 79f7abe7. Owner decisions 7 Nov 2026: M9 overbooking allowance maximum 10 rooms; L7 Owner-changeable upsell-per-arrival target.

## Sizing report: what I checked in the code
- M9: confirmed. The server read `"yld.overbook." + typeId` (numeric id), the registry had no `yld.overbook.*`, the prototype keyed by `Room.type` with max 20, and `yield.ts` clamped at 10.
- One correction: the report says "TypesAsync already reads it (the code)". That is wrong. `TypesAsync` read `COALESCE(t.name, t.code)` as the name only, so the code had to be added as a new column.
- In the prototype, `Room.type` is the type key: `'Classic'` / `'Deluxe'` / `'Suite'` in the fixture, the server type code in production (`roommasters.ts:13`). The fixture keys are mixed case, so the suffix pattern is `[A-Za-z0-9_-]{1,20}`, not the report's `[A-Z0-9-]{1,20}`. `inv.room_type.code` is `VARCHAR(20)`.
- L7: confirmed. `upsell.ts` read `meas.target.upsellPerArrival`, but the key was in neither target table.

## Items fixed
- **M9, registry key family.** File: `08 Production/src/Hcc.Application/Configuration/ConfigurationPorts.cs`, class `SettingsRegistry` (now `partial`).
  - A definition with the key `<prefix>*` registers a family. The prefix must be on the explicit list `FamilyPrefixes = ["yld.overbook."]`; any other family throws at startup.
  - `Find(key)` checks the exact key first. If there is none, it resolves the family when the suffix matches `^[A-Za-z0-9_-]{1,20}$` and the whole key passes `KeyPattern`. The result is a `RegisteredSetting` for the concrete key, labelled "Overbooking allowance · <code>".
  - Families are not listed in `All`, and `Families` exposes them.
- **M9, catalogue.** File: `MeasurementCatalogue.cs` `Definitions()`.
  - Adds family `yld.overbook.*`: Integer, default 0, 0 to `OverbookMax` = 10, suffix "rooms", Owner, money, immediate.
  - Adds constants `OverbookPrefix`, `OverbookMax`, `OverbookKey(code)`.
- **M9, report reads by code.**
  - `MeasRevenueData.cs`: `RType` gains `Code`, and `TypesAsync` selects `t.code`.
  - `OverbookAllowedAsync(reader, propertyId, string typeCode, ct)` reads `yld.overbook.<code>` and clamps at `OverbookMax`. An empty code or no reader gives 0.
  - `OverbookingWashReport.cs`: new public `AllowedAsync(settings, propertyId, codes, ct)`, and `ComputeAsync` passes `t.Code`.
- **M9, prototype.**
  - `measure.ts`: `MEAS_OVERBOOK_MAX = 10`, and the per-type setting max went from 20 to 10.
  - `yield.ts` `yldOverbookAllowed` clamps at `MEAS_OVERBOOK_MAX`.
- **L7, catalogue side.** Both target tables now have `meas.target.upsellPerArrival`, "Upsell revenue per arrival", money, default 5000 paise (Rs 50):
  - `MeasurementCatalogue.Targets`, plus the constant `UpsellTargetKey`.
  - Prototype `MEAS_TARGETS`.
  - Lane B reads it through `TargetAsync`. The prototype `upsell.ts` already reads it through `measTarget`.

## Tests added or changed
- New file `08 Production/tests/Hcc.Meas.Tests/MeasSettingsFamilyTests.cs`, 4 tests:
  - Family resolution: DLX resolves, Classic resolves, `yld.other.DLX` is null, empty, dotted, too-long and spaced suffixes are null, and the family is not listed in `All`.
  - A family outside the prefix list is refused.
  - The Overbooking "allowed" figure reads a stored 2 for DLX through a real `SettingsReader`. An unset type gives 0, a stored 15 is clamped to 10, and an invalid or empty code gives 0.
  - The upsell target is an Owner money setting, default 5000, and a stored 6000 is read back.
- `MeasurementGateTests.cs`: target count 13 to 14, definition count `6+56+13+5` to `6+56+14+6`, and the key-pattern check now accepts the one family key.
- `03 Prototype/tests/stream-meas-settings.mjs`, 2 tests appended, both prefixed `[F12-MEAS2-C]`:
  - M9 test: keys `yld.overbook.<type>` with min 0, max 10 and default 0; a value above the maximum is clamped and refused; a value saved in the hub is read back. It also pins C# `OverbookMax = 10` and the C# prefix.
  - L7 test: the upsell target is in the table and the group with default 5000; Tech admin is refused; the Owner's 6000 is stored; the C# literal is pinned.
- The existing parity tests (TS to C# targets, both directions) pass with the new key.

## Commands actually run (all through tools/Invoke-Serialized.ps1, label meas2-c)
- Prototype tests, 119/119 passed:
  - `03 Prototype`: `$env:HCC_TEST_ONLY="stream-meas-(settings|forecast|rpt-revenue|rpt-guest)"; node --max-old-space-size=8192 tests/run.mjs`
- Prototype typecheck, exit 0:
  - `03 Prototype`: `npx tsc --noEmit`
- Server tests, `08 Production`:
  - `dotnet test tests/Hcc.Meas.Tests/Hcc.Meas.Tests.csproj -maxcpucount:1`: 101/101 passed.
  - `dotnet test tests/Hcc.Configuration.Tests/Hcc.Configuration.Tests.csproj -maxcpucount:1 --filter "FullyQualifiedName~RegistryAndRulesTests"`: 13/13 passed.
  - `dotnet test tests/Hcc.Reporting.Tests/Hcc.Reporting.Tests.csproj -maxcpucount:1 --filter "FullyQualifiedName~MeasRevenue"`: 7/7 passed.

## Not done or unverified
- The `TypesAsync` SQL change (`, t.code`) has not been run against SQL Server. No MeasRevenue SQL fixture exists; that is lane F.
- The full prototype suite and the full server solution were not run, only the projects and files listed above.
- The server settings hub does not list the per-type `yld.overbook.<code>` rows. `ConfigurationQueries` builds groups from `All`, which has no access to room types. The keys can be set (`ConfigurationService` uses `Find`) and read, but a hub listing that expands the family from `inv.room_type` is an integration item outside this lane.
- A room-type code containing a character outside `[A-Za-z0-9_-]`, such as a space or a dot, has no allowance setting and reads 0, the same as before.
- The comment in `upsell.ts:13` ("The contract lists no upsell target") is out of date. That file belongs to lane B.
