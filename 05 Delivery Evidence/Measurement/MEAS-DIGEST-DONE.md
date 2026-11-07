# MEAS-DIGEST DONE

Lane meas-digest / Sonnet / branch claude/meas-digest / worktree E:\Projects\HCC\worktrees\meas-digest / base b017cf13. No SQL, no migration, SCHEMA stays 15.

## Files
- `03 Prototype/src/domain/streams/measure-digest.ts` (new): `MeasRedRow {key; reportId; text; severity}`, `registerMeasRedRows(key, fn)`, stub providers `measRegretRows` (rpt.guests.regrets-denials: >3 price regrets on a night under 60 % occupancy) and `measHighBalanceRows` (rpt.money.high-balance: walk-in above `meas.target.highBalance`, company balance above deposit + limit, balance older than 2 nights), `measDigestBlock(s,date)`, `measDigestSubscriptions`, `measDigestEmailLines`, `measDigestWhatsAppLines` (count only).
- `03 Prototype/src/features/overview/DigestMeasurement.tsx` (new): block + subscription checkboxes (48 px rows), not yet mounted.
- `03 Prototype/tests/stream-meas-digest.mjs` (new, 8 tests; bundles the module itself).
- `08 Production/src/Hcc.Domain/Reporting/MeasurementDigest.cs` (new) and `tests/Hcc.Closing.Tests/Unit/MeasurementDigestTests.cs` (3 tests): pure server twin `Compose(date, rowsByKey, isOn, subscribed)`.

## Behaviour
Off item (module / pack / item via `measOn`) absent; all packs off or nothing checkable = no block; subscription = `configX.values['meas.digest.line.<item key>'] === false` hides the item (default shown). A provider can only report on its own switch. WhatsApp = count only (DEC-202 #7).

## Tests
tsc --noEmit clean; `HCC_TEST_ONLY=meas-digest npm test` 64/64 (8 new); full `npm test` see commit log / below; `dotnet test tests/Hcc.Closing.Tests --filter MeasurementDigestTests` 3/3.

## Integration needed (files outside my row)
1. `src/domain/streams/digest.ts`: `import './measure-digest'`; in `dgsEmailBody` append `...measDigestEmailLines(measDigestBlock(s,c.date))` (needs `s`, so add a `meas` field to `DGSCard` built in `dgsCard`) and in `dgsWhatsAppBody` `...measDigestWhatsAppLines(...)`.
2. `src/features/digest/OwnerDigest.tsx`: render `<DigestMeasurement date={c.date} onOpenReport={id=>…open report hub id}/>` below the figures.
3. `register.ts` F12-DIGEST block: nothing needed.
4. Subscription setting: `meas.digest.line.<key>` is a raw `SETTING_SET`; meas-settings should register it (boolean, Owner, default true) or SETTING_SET may refuse unknown keys.
5. Wire each Phase B lane's pure exception function: `registerMeasRedRows('meas.daily.rate-variance', …)` and so on for every key with a red-row rule (daily 9, revenue 7, guest, hk, people; study entries have none). Stubs cover only `meas.daily.regrets` and `meas.daily.high-balance`.
6. Server: `DigestBodies.Email/WhatsApp` take a `MeasDigestBlock?` (add a `DigestFacts.Measurement` field) and append `MeasurementDigest.EmailLines/WhatsAppLines`; `DigestDraftProjector` builds the rows per key via the report lanes' exception methods and `IMeasurementGate.IsOnAsync` for `isOn`.
