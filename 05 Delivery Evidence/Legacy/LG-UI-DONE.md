# LG-UI DONE

Lane lg-ui / Fable / branch `claude/lg-ui` / worktree `E:\Projects\HCC\worktrees\lg-ui` / base `ced9861e`; `claude/lg-schema` (25d1f8b5) merged in.

## Files (all under `03 Prototype`)
- Created: `src/fixtures/ext/legacy-sample.ts`, `src/services/mock/handlers/customers-legacy.ts`, `src/features/customers/legacy-source.ts`, `src/clients/sync/customers-legacy.ts`, `src/clients/sync/legacy-import.ts`, `src/features/admin/legacyImportModel.ts`, `src/features/admin/LegacyImport.tsx`, `src/mobile/admin/LegacyImport.tsx`, `tests/stream-customers-legacy.mjs`.
- Edited: `src/domain/streams/customers.ts`, `src/features/customers/Customers.tsx`, `src/features/admin/register.tsx`, `src/features/admin/config/panels.tsx` (Open Legacy import box, Owner), `src/mobile/admin/register.tsx` (phone page), `src/app/navmap.ts` (PAGE_CATEGORY, blurb, keywords), `tests/stream-rbac-matrix.mjs` (documents the page).
- No `_contract-stubs.ts` was needed: the mapper was written straight against the merged server names. No State slice, no SCHEMA bump.

## What was built
- Contract 9: `LegacyStay`, `Customer.legacyStays/legacyCount`; nights, lifetime, firstStay (and lastStay) include non-cancelled legacy stays; `stays`, `completed`, avgRate stay live-only. `customerIndex(s, lookup?)`.
- Customers 360 (desktop and phone, same component): list shows `live + legacy` stays with "(n legacy)" muted; stats count legacy; "Earlier stays - old Lodge app" section with a Legacy badge, FY, nights, rooms, old bill no., net amount only for roles with billing, no folio link (not a button).
- Source of legacy rows: prototype = mock read of the six fictional stays (Ananya Rao, Vikram Shah); production = `legacyStaysFromProfile` maps the server's `LegacyStays`; `setLegacyFetcher` (legacy-source.ts) is the one hook the production composition wires.
- Owner page "Legacy import" (`legacy-import`, Administration group, linked from Master configuration > Data and system): folder, parts, totals.json file, dry run, Run; result as tables with Match / Mismatch pills (colour + text) and a differences table; runs list; phone = read-only runs cards. Page opens for role Owner only. Prototype uses a labelled demo client; `createLegacyImportClient(api)` calls only `legacy.import` and `GET /legacy/runs`.

## Tests
`npx tsc --noEmit` 0 errors. `npm test`: before 1948, after the first run 1949 passed / 11 failed (the RBAC matrix did not list the new page; fixed in `stream-rbac-matrix.mjs`); final counts below. `npm run build` OK before the merge.
FINAL after merging claude/lg-schema: tsc 0 errors; npm test 1960/1960 (was 1948; +12 [LG-UI]); npm run build OK before the merge (not re-run after it).

## Open items
- Legacy-only customers (no live booking) are not in the list (index is built from bookings), as the contract says.
- Menu entry is visible to Tech admin (nav uses `admin`); the page shows a lock note. Hide with a `navRoleOk` line if wanted (not my file).
- Production wiring: call `setLegacyFetcher` (fetch `GET /customers/profile`, `legacyStaysFromProfile`) and `setLegacyImportClient(createLegacyImportClient(api))` in the production composition. List rows for customers not yet opened show live counts only until the profile is opened.

## Integration needed
1. Production composition: the two wiring calls above.
2. `tests/_role-reads.json` needed no change (the page uses the existing `admin` gate plus an Owner role check); `RbacV2MatrixTests.cs` `LaterPermissions` line is lg-schema's item 2.

## HANDOFF bullet
- **W1a LG lg-ui (1 Nov 2026):** Customers 360 shows old Lodge stays (Legacy badge, old bill no., money masked like live, rollups include them); Owner page Legacy import (dry run / run / result tables / runs list, phone read-only); fictional sample for two guests; no State slice, no SCHEMA bump.
