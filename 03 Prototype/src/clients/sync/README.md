# Server sync layer (Wave 4) — how to connect an area

The production build keeps every prototype screen and swaps what is under it (design: `02 Planning/agents/W4_Briefs.md`).
Screens still read `ctx.s` synchronously and still call `ctx.run(cmd)`. In the production build:

- **State** = an empty skeleton (`empty.ts`: the State shape with every list empty, no demo data) + the slices your **loaders** return + the signed-in user (from `/me/capabilities`). Nothing else.
- **`ctx.run(cmd)`** looks `cmd.type` up in the registry:
  - **mapped:** your mapper sends one or more `POST /api/v1/commands/<Name>` with a stable Idempotency-Key (and `expectedVersion` where the prototype command carries `version`). The runtime then reloads the slices you named, and the screen gets the usual busy state, toast, `close` and `after`.
  - **unmapped:** the command is refused with `Not available on the server yet: <TYPE>`.
  - **server unreachable:** every command is refused (read-only banner). Nothing changes locally and nothing is queued.
- **Loads** run after sign-in, after each command (only its reload slices), on window focus / page visible (at most every 30 s) and on the **Refresh** button. The strip under the header shows "Server data as of …". The oldest loaded slice decides that time.
- **Read cache:** the last loaded slices are kept in IndexedDB database `hcc-server-cache`, separate from the prototype's demo store `hcc`. They are shown with their "as of" time while the server is unreachable. The cache is cleared on sign-out, when the session ends, and when another user signs in.

The prototype build is unchanged: it keeps the mock `execute()` and demo data. It never contains this folder: `package.json` here sets `"sideEffects": false`, and the app reaches the folder only from `process.env.HCC_BUILD==='production'` branches. `tests/stream-clients.mjs` checks this. Every `'/path'` literal and `api.command('Name', …)` name written in a sync file must be absent from the prototype bundle.

## Files

| File | What |
|---|---|
| `types.ts` | The contract: `SyncArea`, `SliceLoader`, `CommandMapper`, `LoaderCtx`, `CommandCtx`, `SyncApi`, `SyncMe` (worked example in the header) |
| `registry.ts` | `AREAS` — one pre-allocated slot per W4 agent (uncomment your two lines) |
| `map.ts` | Mapping helpers: `protoId` / `serverId`, `paise`, `isoDate`, `utcToClock` / `clockToUtc` / `utcToDate`, `IdDirectory` (room number ↔ room id, record versions) |
| `runtime.ts` | `createSyncRuntime` (framework-free): boot, sign-in/out, loader batches, `run`, key derivation, read-only, cache, focus refresh; `validateAreas` |
| `capabilities.ts` | `PERMISSION_MAP` (server PERM ids → prototype `can()` permissions), `ROLE_MAP`, `CAPABILITY_GAPS`, `overlayMe` |
| `auth.ts` | Sign-in seam: `AuthAdapter`, `cookieAuth()` (browser, HttpOnly cookie). W4-12 adds the APK token adapter here |
| `cache.ts` | Read cache (IndexedDB `hcc-server-cache`, memory fallback) |
| `empty.ts` | Production skeleton state |
| `core.ts` | `clock` and a provisional `date` from `GET /system/health` |
| `reservations.ts` | **Reference area**: `bookings` loader, `BOOK` mapper (availability → QuoteStay → CreateReservation) |
| `instance.ts`, `react.tsx` | The app's runtime singleton, `ProductionApp`, server sign-in screen, `SyncStatus` strip |
| `../../../tests/_sync-harness.mjs` | Fake server + runtime + page smoke render for your tests |

## Add an area (W4-1 … W4-11)

1. Create **`src/clients/sync/<area>.ts`** that exports one `SyncArea` as a plain object literal:

   ```ts
   import type {SyncArea} from './types';
   import {serverId,utcToClock} from './map';
   export const rooms:SyncArea={id:'rooms',
     loaders:[{slice:'rooms',async load(c){const r=await c.api.get<SrvBoard>('/rooms/board',{pageSize:500});
       for(const x of r.items)c.ids.learn('room',x.roomNo,x.roomId);return r.items.map(roomFromServer);}}],
     commands:[{type:'ROOM_BLOCK',reload:['rooms'],precheck:cmd=>cmd.reason?undefined:'Give a reason.',
       async send(c){await c.api.command('ApplyRoomBlock',{roomId:c.ids.server('room',c.cmd.room),reason:c.cmd.reason});}}],
     unmapped:{ROOM_SWAP:'no server command yet (backlog BL-…)'}};
   ```

2. Register it: in `registry.ts`, uncomment **your** import line and **your** `AREAS` line, nothing else. Each agent has its own slot, so branches merge cleanly.
3. Write **`tests/sync-<area>.mjs`**, modelled on `tests/sync-reservations.mjs`. `npm test` runs every `tests/sync-*.mjs`. Use `loadSync`, `fakeServer`, `startRuntime`, `reply`, `netDown`, `capabilities` and `loadPages` from `_sync-harness.mjs`:
   - fake routes shaped exactly like the .NET DTOs (`08 Production/src/Hcc.Contracts/**`);
   - assert that your loaders fill the slices with the **prototype's own figures**: run the same scenario through `M.execute` and compare;
   - assert each mapped command's path, body, `Idempotency-Key` (`srv.commands('Name')[i].key`) and `expectedVersion`, and that the right slices reload afterwards;
   - assert that `loadPages().renderPages(rt.get().state, uid)` and `renderPages(…, true)` return `errors: {}`, so every desktop and phone page renders on your server data;
   - list what stays unmapped in `unmapped` (with the reason) and check it in a test.
4. If a screen needs data the server does not expose, add a **read endpoint in your own server module folder** (`08 Production/src/Hcc.Api/Endpoints/<Module>Endpoints.cs` + queries). Use migrations only if unavoidable, and only in your block. Run `dotnet build` (0 warnings) and your module's test project serially (`-maxcpucount:1`, `HccTest_*` databases only).

## Rules

- **Own your slices whole.** Exactly one area owns a slice (`kind:'own'`, the default) and returns its complete value. Do not merge in skeleton or demo data. To add fields to another area's slice, use `kind:'patch'`: it runs after the owner in the same batch and gets the fresh value through `c.current(slice)` (example: money adds `lines`/`receipts` to `bookings`). `validateAreas` refuses two owners of a slice and two mappers of one command type.
- **`needs`:** a loader that reads another slice through `c.current()` lists it in `needs`.
- **`requires`:** set the prototype permission(s) without which the slice is pointless. The GET is then skipped, so no 403 noise. A 403 anyway leaves the slice empty (skeleton). Any other error keeps the last value and appears as "N areas could not load".
- **Ids:** prototype id = `String(server id)` (`protoId` / `serverId`). Exceptions: rooms use the room number and room types the type code. Learn those pairs with `c.ids.learn('room', roomNo, roomId)` and resolve them with `c.ids.server('room', roomNo)`. User ids: `protoUserId(userId)`. The signed-in user's record is added by the runtime.
- **Versions:** in loaders, `c.ids.setVersion(kind, protoId, serverVersion)`. In mappers, `{expectedVersion: c.expectedVersion(kind, cmd.id)}`, which is only set when the prototype command carries `version`. Keep prototype `version` numbers neutral (e.g. 1).
- **Money** stays integer paise (`paise()` for response fields you compute with). **Dates** stay `YYYY-MM-DD`. Server UTC instants become the prototype clock format with `utcToClock`.
- **Masked fields stay masked:** show what the server sends and never reconstruct a value (see `c.me.fieldMasks`).
- **Idempotency:** never build keys yourself. Use `c.api.command(...)`: the first call gets the action key, later calls get `<key>:2`, `<key>:3`, …, or `<key>:<step>`. Typed clients take `{idempotencyKey: c.key}`. The key comes from the dialog's `cmd.key` when given; otherwise it is derived, stays the same on retry, and changes after a success.
- **Prechecks** are for cheap client-side refusals with the prototype's wording. The server still decides.
- **No local changes:** a mapper only sends commands. The screen sees the effect when the reload returns. If you need the new record's id, learn it in `c.ids` (as `BOOK` does) or return it as `result`.
- **No top-level calls** in area files: only object literals and function declarations (tree-shaking).
- **Do not touch** shared files (`runtime.ts`, `types.ts`, `map.ts`, `capabilities.ts`, `App.tsx`, …). If the foundation lacks something, write the gap in your final report.

## Checklist before you hand back

- [ ] `src/clients/sync/<area>.ts` + your two registry lines + `tests/sync-<area>.mjs`, and nothing else in shared code
- [ ] every prototype command of your screens is either mapped or listed in `unmapped` with a reason
- [ ] loaders reproduce the prototype figures; pages render (`renderPages`) on desktop and phone
- [ ] mapped commands: path, body, key, `expectedVersion`, reloads asserted
- [ ] `npx tsc --noEmit` clean · `npm test` green · `npm run build` and `npm run build:production` OK (bundle check included)
- [ ] server: `dotnet build` with 0 warnings and your test project run serially (only if you added endpoints)

## Known gaps (W4-0)

- **No reservation list endpoint.** `bookings` reloads the bookings this device knows (created here or already loaded). W4-1 adds a list read.
- **`date` is provisional:** it is the server's local calendar date. W4-4 patches it with the real business date.
- **Capabilities:** see `CAPABILITY_GAPS` in `capabilities.ts`. The Accountant gets "expenses". "approve" follows PERM-APR-001 only. Module PERM ids do not change `can()`. `s.policy` (fine-grained) is not derived yet (W4-5).
- **PIN sign-in** needs a registered device token (`X-HCC-Device`, read from localStorage `hcc-device-token`). Device registration screens and the APK token sign-in belong to W4-5 / W4-12 (`auth.ts` seam).
