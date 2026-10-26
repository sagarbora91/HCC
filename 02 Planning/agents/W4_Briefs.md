# Wave 4 — connect everything (26 Oct 2026)

Owner: "Now let's finish wave 4". Scope (from the wave plan): every screen works against the production server, the APK connects to the server over HTTPS with token sign-in, read-only mode when the server is unreachable, import of real masters and opening balances. Base: `main` (prototype 800 tests + server 849 .NET tests). Integration branch: `claude/wave4-integration` (from main). Nothing is pushed by agents.

## Design (decided by the lead)
Only booking uses typed clients today (`src/clients/*`, Wave 1 P5). Rewriting every screen (≈300 `ctx.run` call sites, synchronous `ctx.s` reads) would be slow and conflict-heavy. Instead the **production build keeps the screens and swaps what is under them**:
- **Server sync layer** `03 Prototype/src/clients/sync/`: one file per area `sync/<area>.ts` that exports (a) **loaders** — server GETs mapped into the prototype state slices the area's screens read (`s.rooms`, `s.folios`, `s.posX` …), and (b) **command mappers** — prototype command type (`cmd.type`) → server command(s) (`POST /commands/<Name>` with Idempotency-Key, expectedVersion) + which slices to reload afterwards. Areas register in `sync/registry.ts` (one line each).
- In the production build `ctx.run(cmd)` goes through the registry: mapped → HTTP command → reload affected slices → same toast/busy behaviour as today; unmapped → refused with a clear "Not available on the server yet" (never a silent local change). Server state is the truth; the browser holds only a read cache.
- **Read-only mode**: when the server is unreachable every command is refused (existing `ReadOnlyBanner`), screens show the last loaded data (cached in IndexedDB per slice, with "as of" time); nothing is queued on the device.
- Prototype build is unchanged (mock `execute()`); `npm test` must stay green and the prototype bundle must not contain the sync layer (build-time flag `HCC_BUILD`, as today).
- Mapping rules: ids (server numeric ↔ prototype string ids), money paise unchanged, dates `YYYY-MM-DD`, server permission ids → prototype `can()` via `/me/capabilities` (extend the existing capabilities mapping), masked fields stay masked.

## Agents
| # | Area (prototype features → server modules) | Model | Notes |
|---|---|---|---|
| W4-0 | **Foundation**: sync registry, production `run()` routing, slice loader scheduling (after sign-in, after commands, on focus, manual refresh), read cache + read-only mode, capabilities → `can()`, error mapping, a test harness (fake fetch) and the reservations area as the reference implementation | Opus | First; everyone else starts after it is merged |
| W4-1 | Front desk: reservations, guests (stays, registration, check-in/out, room move, extend/shorten), customers | Sonnet | |
| W4-2 | Rooms, housekeeping, room blocks (inventory) | Sonnet | |
| W4-3 | Money: folio, cashier/payments, cash book, invoices & credit notes, groups billing | Opus | |
| W4-4 | Closing, reports, exports, overview | Sonnet | |
| W4-5 | Admin: users/roles/PIN+device sign-in admin, Master configuration, first-time setup | Opus | |
| W4-6 | Staff / payroll / leave | Opus | |
| W4-7 | Accounting, expenses, compliance | Opus | |
| W4-8 | POS | Sonnet | |
| W4-9 | Maintenance, linen & stock | Sonnet | |
| W4-10 | Guest services, messaging, shift handover | Sonnet | |
| W4-11 | Yield, OTA channels, booking website, ID document capture | Sonnet | |
| W4-12 | APK ↔ server: HTTPS (hotel CA trust via Android network security config), server URL setup screen, token sign-in (`ReturnToken`) + PIN + device binding (DEC-062), sign-out/expiry | Opus | After W4-0 |
| W4-13 | Masters & opening balances import (server): CSV templates + validate/preview/commit commands for rooms, room types, tariffs, staff/users, ledgers + opening balances, guests (optional); migrations 2700–2799 | Opus | Starts with W4-0 (server only) |

Every area agent: own files only (`src/clients/sync/<area>.ts`, `tests/sync-<area>.mjs`, server read endpoints in its own module folders if a screen needs data the server does not expose yet — in-block migration numbers only if unavoidable); one registry line; tests with the fake-fetch harness reproducing the prototype figures; prototype `npm test` green, `npx tsc --noEmit` clean, `npm run build` + `npm run build:production` OK; server `dotnet build` 0 warnings and its own test project serially. Shared rules otherwise as in `W2_Briefs.md` "Every agent". Final answer ≤ 15 lines: commands mapped / unmapped (with reason), server endpoints added, tests, decisions.

## Then (lead)
Merge into `claude/wave4-integration` → full prototype + server tests → end-to-end smoke (server on `.\SQLEXPRESS` with a disposable DB + production web build in the browser) → merge into main → APK v12 → phone + tablet emulator against the server → push when the owner says.
