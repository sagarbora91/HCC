# MEAS-FORECAST DONE

Lane meas-forecast / Opus / branch `claude/meas-forecast` / worktree `E:\Projects\HCC\worktrees\meas-forecast` / base tag `meas-setup` (3040fa7f).

## Commits
See `git log --oneline meas-setup..claude/meas-forecast` (listed at the end of this file).

## Files
Prototype (`03 Prototype`):
- created `src/domain/forecast.ts` (pure: real forecast curve, back-test, OTB rows, Mondays, displacement; exports `fc*` / `FC*`)
- created `src/services/mock/handlers/meas-forecast.ts` (MEAS_EVENT_SAVE / _DELETE, MEAS_OTB_SNAPSHOT, MEAS_COMPETITOR_SAVE, MEAS_RATESHOP_ADD)
- created `src/fixtures/ext/meas-forecast.ts` (fictional events / competitors / rate-shop rows; seeded legacy-shaped sample history for the back-test; NOT wired into BASE / FY26, so no pinned hash moves)
- created `src/features/yield/EventsPage.tsx`, `RateShopPage.tsx`, `DisplacementPage.tsx` (FitPage components, not registered: see Integration needed)
- created `tests/stream-meas-forecast.mjs` (14 `[F12-FORECAST]` tests)
- appended `// F12-FORECAST` block at the end of `src/domain/streams/yield.ts` (nothing above it changed)
- appended inside the F12-FORECAST blocks of `src/services/mock/handlers/index.ts` (import + spread), `src/services/mock/index.ts` (exports), `src/clients/sync/registry.ts` (area `meas-forecast`, commands listed as unmapped)

Server (`08 Production`):
- created `database/migrations/4830_meas_forecast_tables.sql` (+ lock entry via `migrations-lock.mjs --add --accept-out-of-order`, + `Hcc.slnx` File line inside `<!-- F12-FORECAST -->`)
- created `src/Hcc.Domain/Yield/MeasForecast.cs` (C# port of forecast.ts: NightCounts, OnBooks, EventPct, ForecastNight, Backtest, Displacement)
- created `tests/Hcc.Yield.Tests/Domain/MeasForecastTests.cs` (5 tests, same worked numbers as the prototype)

## What was built
**2.2 Real forecast curve** (`fcForecastNight`, `fcCurve`, server `MeasForecast.ForecastNight`):
- baseline % = same night one and two years back aligned on the weekday (date − 364 / − 728) plus the same weekday ±1 and ±2 weeks (5 samples a year), year 1 weight 2, year 2 weight 1; unknown nights (before the history start or on / after the as-of date) are skipped. No such history → `trailing` = same weekday in the 8 weeks before the as-of date. Nothing → `none` (0).
- pickup (future night, last year's aligned night known) = last year's final rooms − last year's rooms on the books at the same lead time (≥ 0); blended rooms = (on the books now + pickup + baseline rooms) ÷ 2. Pace factor (bps) = (on books + 1) ÷ (last year on books + 1) × 10000, shown only.
- event uplift = sum of `impactPct` of the MEAS events covering the night, clamped −50 … +100, applied to the blended rooms.
- result = max(on the books, Math.round(blend × (100 + uplift) ÷ 100)), capped at the sellable rooms; pct = Math.round(rooms ÷ sellable × 100). Occupancy = distinct rooms slept ÷ rooms in service (contract §9.2). C# rounds with floor(x + 0.5) = JS Math.round.
- Inputs: prototype bookings (`fcStaysFromState`; Booking has no createdAt, so booked on = earliest receipt date on or before arrival, else arrival − 14 days; cancelled = arrival − 1 when unknown) + legacy `lgc` stays (`fcStaysFromAn`, booked on = arrival − 14).
- Owner override kept: the yield module's manual rate override (YLD_OVERRIDE) still wins over the suggested rate; the curve only feeds occupancy.
- Behind `meas.rev.forecast`: `yldOccReal(s,date,legacy?)` / `yldRealCurve(s,n,legacy?)` in the yield F12 block. **`yldOcc` itself is unchanged** so the yield guards and tiers behave exactly as before (see Open items).

**2.1 Events master**: `MEAS_EVENT_SAVE` (name 3–80, from ≤ to, kind wedding-season / exam / fair / corporate-visit / holiday / other, impact whole % −50 … +100, upsert by id, unchanged repeat is a no-op), `MEAS_EVENT_DELETE` (repeat-safe). Gate `meas.rev.demand-calendar`; Manager + Owner (`approve`).

**2.3 OTB snapshot**: `MEAS_OTB_SNAPSHOT {takenOn?, backfillFrom?}` writes one row per night with rooms on the books (rooms + revenue = the booking's nightly rate, paise) for 120 nights as known on the snapshot day; the same day again replaces its rows; `backfillFrom` writes one snapshot per Monday (`fcMondays`); the newest 60 snapshots are kept. Gate `meas.rev.pickup`; Manager + Owner (the scheduler runs it as the Owner on Monday 06:00 — see Integration needed).

**2.4 Displacement** (`fcDisplacement`, server `MeasForecast.Displacement`): per night free = max(0, sellable − forecast rooms); displaced = max(0, group rooms − free); displaced revenue = displaced × transient ADR (the night's suggested BAR on the page); group revenue = rooms × (group rate + other spend per room-night); net = group − displaced; accept when net ≥ 0, else counter at break-even = ceil(ceil((displaced revenue − other spend × room-nights) ÷ group room-nights) ÷ 5000) × 5000 paise (₹50 up). Worked example (tests): 2 nights, sellable 30, forecast 25 / 28, 10 rooms, ADR ₹2,000: at ₹1,500 net +₹4,000 accept; at ₹1,000 net −₹6,000, counter at ₹1,300.

**2.5 / 2.6 / 2.7 helpers**: `yldOverbookAllowed(s,type)` reads `yld.overbook.<type>` (default 0, integer 0…10); `yldOversold(s,type,date)` (red when oversold > allowed); `yldRestrictionsEnforced(s)` reads `yld.restrictionsEnforce` (default off); `yldRestrictionCount(measX,night,kind,'refused'|'reshaped')` (pure counter) and `yldRestrictionNights(s,from,to)` (2.6 report input). `MEAS_COMPETITOR_SAVE` (unique name, ≤ 8 active) and `MEAS_RATESHOP_ADD` (idempotent on `clientId`, active competitor, known room type, whole paise ≤ ₹1,00,000). Gate `meas.rev.rate-shop`.

Every command refuses first with `MEAS_OFF` when `measOn` is false (local guard; `measOn` is the setup stub until meas-settings lands), uses the simulated clock and returns its audit text.

**Server tables** (4830, schema `meas` created if absent): `meas.event`, `meas.otb_snapshot`, `meas.competitor`, `meas.rate_shop`, `meas.restriction_counter` (property_id + client id keys, money BIGINT paise, check constraints mirroring the command validation).

## Tests
All through `E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1 -Label meas-forecast` (scripts in `.scratch\`; several exit-75 retries while other lanes held the lock; logs `.scratch\*.log`).
- `npx tsc --noEmit`: exit 0 (after the pages were added).
- Focused `npm test` (`HCC_TEST_ONLY='meas-forecast|stream-yield'`): **91 / 91** (14 new `[F12-FORECAST]` + the 77 existing `[YLD]` tests unchanged: yield guards unchanged at defaults). Both DoD back-tests pass: FY 2025-26 on the legacy-shaped sample (actual within 2 points of the study's 31 %, forecast 7 days ahead within 2 points of the actual) and the FY26 fixture (1 Jun 2026 to the day before the business date, trailing curve + pickup, within 2 points).
- Full `npm test`: **2886 / 2897** (before this lane: 2883 tests on the base, all passing). The **11 failures are all `tests/stream-rbac-matrix.mjs`**: its `CMDS` table must document every command and the five new MEAS_* commands are not in it (one failure + the ten per-role runs that read the missing entry). That file is outside this lane's row → Integration needed 0. No other test failed.
- `dotnet test tests/Hcc.Yield.Tests -maxcpucount:1 --logger trx`: **41 / 41** (36 before + 5 new `MeasForecastTests`; the SQL tests apply every migration, so 4830 runs cleanly on a fresh `HccTest_*` database). One test expectation was wrong on the first run (3 rooms on the books, not 4) and was fixed.

## Open items
1. **Contract vs code — restrictions.** The plan says yield restrictions are advisory today; on `main` the house restrictions (MIN_STAY / CTA) are already **enforced** by BOOK / CHANGE_DATES (A2.1, `reservations.ts restrictions()`, approver override with a reason). `yld.restrictionsEnforce` off can therefore not mean "advisory" without changing the guard at defaults (forbidden). The helper reads the setting; what "off" should do is an owner / integrator decision (suggest: off = today's behaviour, on = rate-plan rules also enforced).
2. **Real curve not wired into `yldOcc`.** `meas.rev.forecast` defaults on; replacing `yldOcc`'s simulated forecast would change the yield tiers and the pinned `[YLD]` figures at defaults. The real curve is exposed as `yldOccReal` / `yldRealCurve` (used by the pages and for the 2.1 / 2.4 reports). Swapping `yldOcc` is the integrator's call after the owner's go.
3. **No server service / endpoints / schedule job yet.** Tables (4830) and the pure C# port exist; the store, `MeasForecastService`, endpoints (`/commands/MeasEventSave` …) and the F7 schedule-worker job (Monday 06:00) are not built (cap reached). Sync area lists the five commands as unmapped with that reason. Migrations 4831–4834 are still free for this.
4. **Back-test on real `lgc` data** is not run here (legacy data stays on the hotel PC; contract §9.5 golden test). On the legacy-shaped sample the FY 2025-26 back-test matches (see Tests); the real run needs `fcStaysFromAn` on the imported `lgc` stays.
5. Booking has no `createdAt` / `cancelledAt` (SCHEMA 15): booking dates come from the earliest receipt, else arrival − 14; cancellation date = arrival − 1. A side table `measX.bookingDates` (or the server's reservation timestamps) would make pickup exact.

## Integration needed
0. **`03 Prototype/tests/stream-rbac-matrix.mjs` `CMDS`** (blocks the full suite): add next to the YLD rows
   `...Object.fromEntries(['MEAS_EVENT_SAVE','MEAS_EVENT_DELETE','MEAS_OTB_SNAPSHOT','MEAS_COMPETITOR_SAVE','MEAS_RATESHOP_ADD'].map(c=>[c,C('OWN MGR','F12 meas-forecast (approve)')])),`
   (all five use `k.req('approve')`, like YLD_PROPOSE = 'OWN MGR'). If the per-role run needs a runnable command, MEAS_RATESHOP_ADD needs a competitor first (`MEAS_COMPETITOR_SAVE {id:'CMP-1',name:'Hotel Sample Residency'}`) and a unique `clientId`.
1. `src/features/yield/register.tsx` (owner: yield module / integrator): register the three pages, e.g. pages `meas-events` → `MeasEventsPage` (Manager + Owner), `meas-rateshop` → `MeasRateShopPage` (Manager + Owner), `meas-displacement` → `MeasDisplacementPage` (PERM-RPT-002); add the ids to `PAGE_CATEGORY` (revenue) in `src/app/navmap.ts` and keywords in its F12-KEYWORDS block; gate each with `measOn` (`meas.rev.demand-calendar`, `meas.rev.rate-shop`, `meas.rev.displacement`). Then run `node tools/fit-screen-check.mjs --pages meas-events,meas-rateshop,meas-displacement` (PORT 4196).
2. `src/services/mock/handlers/reservations.ts restrictions()`: when a stay is refused (`k.fail('RESTRICTED',…)` path) or overridden, store `k.s.measX={...(k.s.measX??{}),restrictions:yldRestrictionCount(k.s.measX,arrival,v[0].code,'refused'|'reshaped')}` — note a refusal throws and rolls the state back, so the refused counter needs a log-on-failure hook (or the server's audit row); the override path ("reshaped") can be stored directly.
3. Availability / overbooking: if the owner wants `yld.overbook.<type>` honoured at BOOK, `available()` (selectors) needs to allow `yldOverbookAllowed(s,type)` extra rooms per type. At the default 0 nothing changes.
4. Replace the local `guard()` in `handlers/meas-forecast.ts` with meas-settings' `measGuard(k,key)` once merged, and add `'MEAS_OFF'` to `ErrorCode` (it is cast today).
5. F7 schedule worker: a weekly job "MEAS OTB snapshot" Monday 06:00 → command `MEAS_OTB_SNAPSHOT` (prototype) / the server service (open item 3).

## HANDOFF bullet
- F12 meas-forecast (Opus): real forecast curve (last two years by weekday + pickup + event uplift, behind `meas.rev.forecast`), events / OTB snapshot / competitor / rate-shop commands, displacement calculator (paise, break-even rounded to ₹50), yield F12 helpers for `yld.overbook.<type>` and `yld.restrictionsEnforce`, migration 4830 (`meas.event`, `otb_snapshot`, `competitor`, `rate_shop`, `restriction_counter`), C# port. Yield guards unchanged at defaults. Open: server service / endpoints / Monday job, page registration, restrictions "advisory" vs enforced on main.
