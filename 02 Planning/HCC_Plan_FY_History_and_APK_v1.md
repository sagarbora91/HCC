# HCC — Build plan for Claude Code (Sonnet): FY history data (1 Apr 2026 → today) + Android APK

Version 1.0 · 24 Oct 2026 · Owner: Sagar · Executor: Claude Code with the Sonnet model, single agent, one phase per session
Workspace: `V:\Codex\Hotel Control Centre` (prototype in `03 Prototype`) · Read with: `CLAUDE.md`, `HANDOFF.md`, `02 Planning/HCC_Mobile_Version_Plan_v1.md` §5, §6, §12

---

## 0. How to run this plan (read first, Claude Code)

**Paste this to start each session:**
> Read CLAUDE.md, HANDOFF.md and 02 Planning\HCC_Plan_FY_History_and_APK_v1.md. Do the next phase that is not ticked in §9 only. Follow every rule in §1. After each step run `npx tsc --noEmit`, `npm test` and `npm run build` in `03 Prototype` (all must pass). Before stopping, tick the phase in §9, update HANDOFF.md and give me full Windows paths of changed files and a 5-line summary.

Rules for the executor:
- **One phase per session**. Each phase ends green (tsc + all tests + build) and with HANDOFF.md updated. If usage runs low, stop at the last green step and write what is left in HANDOFF.md.
- Don't re-ask approved decisions (Decision Register, mobile plan §11, this plan §2). Ask the owner only for the items marked **OWNER** in §8.
- Work directly on the Windows PC (PowerShell). Paths contain spaces — always quote them.
- Never weaken or delete existing tests. Today there are **388 tests**; every phase adds tests.
- If a step fails twice the same way, stop and report the exact error rather than guessing.

## 1. Fixed constraints (do not break)

1. All business rules stay in the mock service: mutations via `execute(state, uid, cmd)`; pure selectors in `src/domain/**`; fixtures in `src/fixtures/**`. Money is integer paise; time comes from the simulated clock `s.clock`/`s.date` (never `Date.now()` in domain code); data is fictional; tax is the synthetic 10%.
2. The prototype stays **one self-contained HTML** (`dist/index.html` = `Hotel Control Centre.html`). **No new npm packages in `03 Prototype`**. Capacitor packages live only in the separate `04 Android` folder (Phase D).
3. **BASE stays exactly as it is.** The existing scenarios and all 388 tests depend on its figures (occupancy 8, collections ₹21,500, drawer ₹10,200, the S0 ₹4,400 journey). History is a **new scenario**; BASE is not edited.
4. **Business date stays 24 Oct 2026** (open day). History covers **1 Apr 2026 – 23 Oct 2026** (Indian FY 2026-27, 206 closed business days).
5. Theme (Maroon & Sand), WCAG AA, the phone rules (48 px targets, 16 px text, no sideways scroll at 360 px) and the display toggle apply to anything new.
6. Room 308 stays out of service. Group holds (module 14) block rooms. Day-closed rules hold: no posting into closed dates except through existing correction commands.

## 2. Decisions taken in this plan (defaults; change only if the owner says so)

| ID | Decision |
|---|---|
| H-01 | New scenario **`FY26` "FY 2026-27 history (Apr–Oct)"** = BASE for 24 Oct **plus** generated history 1 Apr–23 Oct. Listed first in the Scenario picker. |
| H-02 | **Startup default becomes FY26** for the app (a constant `DEFAULT_SCENARIO` in `fixtures/scenarios.ts`). `seed()`/`baseFixture()` used by tests stays BASE. The Reset button reloads the current scenario as today. |
| H-03 | History is produced by a **deterministic generator** (seeded PRNG, fixed seed `20260401`). The same build always gives identical data (tests check a hash). No `Math.random()`. |
| H-04 | History is generated **as data** (direct construction, not by replaying thousands of `execute()` calls, which would be O(n²) because of `structuredClone`) and then **proved correct by invariant tests** (§5). Only the last 7 days' small samples may be produced through `execute()` if that is simpler. |
| H-05 | Storage moves to **IndexedDB** (with a localStorage fallback for small states), because FY26 will exceed the ~5 MB localStorage limit. This still works in Chrome and inside the Capacitor WebView. |
| H-06 | History audit log = **one summary entry per closed day** plus every approval/void/override event. It does **not** log one entry per night posting (to keep size down). |
| H-07 | Size budget: FY26 state JSON **≤ 12 MB**, generation **≤ 1.5 s** on the PC, first screen usable **≤ 3 s** on a mid-range phone/emulator. If the budgets fail, apply §4.6 before adding more data. |
| H-08 | APK = **debug build only**, package id `in.hcc.prototype`, app name "Hotel Control Centre", for usability testing on the owner's phone (DEC-051). Not a Play Store release. |

## 3. What the history must contain (realistic, fictional, Latur-style 24-room hotel)

**Demand model** (per night, before randomness):
- Base occupancy by month: Apr 62%, May 55%, Jun 48%, Jul 45%, Aug 50%, Sep 58% (Ganesh festival week ~ 14–24 Sep +12 pts), Oct 66% (Navratri/Dussehra peak ~ 11–20 Oct +10 pts; Diwali is in November and outside the window).
- Day of week: Fri +6 pts, Sat +10 pts, Sun −8 pts. Weekday corporate share higher; weekend leisure/family share higher.
- Stay length: 1 night 45%, 2 nights 30%, 3 nights 12%, 4–6 nights 10%, 7+ nights 3% (long-stay plan).
- Lead time: walk-in 15%, same week 45%, 1–4 weeks 30%, >4 weeks 10%.
- Cancellations 6% (with or without a deposit), no-shows 2% (charged one night per policy where a deposit exists), early departures 2%, extensions 3%, room moves 1%.
- Sources: Direct/phone 35%, OTA via the module-21 channels (BookNest, StayHub, TripKart) 30%, Corporate/company accounts (module 14) 20%, Walk-in 10%, Website (module 22) 5%. OTA bookings carry commission in the channels slice.
- Room types: Classic ₹2,000, Deluxe ₹3,000, Suite ₹4,500 rack. ADR varies with module-13 seasons and day-of-week rules; corporate −12%; weekend plan; occasional discounts (≤ 5% of stays, larger ones with approval records).
- 3–4 group blocks across the period (wedding party in May, corporate training in Aug, festival group in Sep) through the module-14 structures, with pickup, release and members linked to real bookings.

**Per booking:** guest (generated from fictional first/last name lists, Indian context, phone `+91 9xxxx xxxxx` fictional pattern, `@example.test` emails); room assignment that **never overlaps** (use `availableRaw` on the history being built); registration; folio lines: one room night per night with the tax snapshot, plus service lines (laundry, minibar, room service from module 23 for ~15% of stays); receipts: advance/deposit where relevant, settlement at checkout with a realistic tender mix (Cash 35%, UPI 45%, Card 15%, Bank/company 5%), deposit returns; company-credit stays settled later by bank transfer. Departed folios balance to **zero** except ≤ 6 deliberate open receivables (company credit within limit) that show up in reports.

**Daily operations (each closed day 1 Apr–23 Oct):**
- Night posting for all in-house stays; a **closing record** in `s.closings[date]` made with the same `closingSummary` logic (generate by setting a working state's date/clock to that day and calling the selector, so figures are identical to what the app would compute); `closedDates` includes every history day.
- Cashier shift per day: opening float ₹5,000; expected cash from receipts and cash expenses; counted cash equals expected on ~92% of days, otherwise small differences ±₹10–₹200 with a reason and an approval record.
- Expenses: daily small items (supplies, vegetables for the kitchen outlet, petty cash), weekly laundry vendor (Fresh Linen Services), monthly electricity/water/internet/AMC bills (non-cash), occasional repairs linked to maintenance work orders; a few rejected/reversed ones.
- Housekeeping: a checkout clean task per departure and stayover tasks, all **Done** in history (keep only the last 30 days of task rows; older days are counted in a compact daily summary to save space).
- Module data, consistent with the bookings:
  - 15 Maintenance: corrective work orders (~1 per 3 days), PM schedule executions, a few room blocks with downtime that the calendar shows (rooms really unavailable on those nights).
  - 16 Linen & stock: weekly GRNs, daily issues, laundry batches and returns, a monthly physical count, consumption per occupied room-night that matches occupancy.
  - 17 Staff & payroll: attendance for every day, leave records, **locked payroll runs for Apr–Sep**, October still open.
  - 18 Guest services: requests (~0.3 per occupied room-night), complaints (~1 per 40 stays) with recovery, lost & found items with some claimed and some disposed.
  - 19 Checklists: runs for the last 30 days only (older ones summarised).
  - 20 Accounting: vouchers are derived; **lock every month Apr–Sep** (snapshots) so the period-lock flow is visible.
  - 21–22 Channels: OTA bookings arriving as accepted inbound messages; commission report Apr–Oct; website requests (some confirmed, some declined/expired).
  - 23 POS: restaurant checks daily (cash/UPI/room-post), room service posted to folios, a few voids with approval.
  - 24–25 Messages/documents: outbox entries for confirmations/receipts for the last 60 days; a few document captures.
  - 13 Yield: season rules for Apr–Oct and a publish history consistent with the rates used in bookings.
- 23 Oct (yesterday) must join up with BASE's 24 Oct: BASE's in-house guests arrived 23 Oct (rooms 101, 102, 104, 201…). The generator must **reserve those rooms/nights first** and skip them, so there are no overlaps.

**Targets to sanity-check (±10%):** FY-to-date occupancy ~ 56%; ADR ~ ₹2,650; ~2,650 occupied room-nights (23 sellable rooms × 206 nights × 56%); room revenue Apr–Oct ~ ₹65–75 lakh; ~1,200–1,400 stays. Show these on a "History summary" card (see P-B5).

## 4. Phases A–C: history data

### Phase A — Generator foundation and storage (session 1)
A1. Create `src/fixtures/history/` with:
- `prng.ts`: mulberry32 (or similar) + helpers (`pick`, `weighted`, `int`, `chance`) that take an explicit generator; no global state.
- `calendar.ts`: date helpers for the window, monthly/day-of-week/festival demand factors (§3) as data tables.
- `names.ts`: fictional name/company lists (no real people, no real company names; companies end in "(fictional)" or use made-up names).
- `generate.ts`: `export function buildHistory(base:State, opts={seed:20260401, from:'2026-04-01', to:'2026-10-23'}):State` — returns a new state (BASE + history). Phase A builds **bookings/folios/receipts and room allocations only**.
A2. Register scenario `FY26` in `fixtures/scenarios.ts` (builder calls `buildHistory`), `DEFAULT_SCENARIO='FY26'` used by `restoreState` when there is no saved state (H-02). Keep `seed()`/`baseFixture()` = BASE.
A3. IndexedDB persistence (H-05): `src/app/storage.ts` gets an async adapter (`idbLoad/idbSave`, database `hcc`, store `state`, key `current`), write debounce 500 ms, fallback to localStorage for states < 4 MB, and the existing reset messages. `App.tsx` shows a short "Loading demo data…" screen until the first load resolves. Bump `SCHEMA` to 5. Keep `restoreState` pure and tested.
A4. Tests (`tests/stream-history-a.mjs`): determinism (two builds → identical JSON hash); no room overlaps (for every room, no two live bookings overlap; blocked nights empty); every booking has nights = posted room lines for departed stays; departed folios balanced except the listed receivables; BASE figures untouched for 24 Oct (occupancy 8 etc. **in FY26 too** for today's in-house set); size and time budgets (H-07) measured and printed.
Done when: FY26 loads in the app (desktop + phone) with the Room board/Calendar/Reservations showing history, all tests green.

### Phase B — Daily operations, closings and core reports (session 2)
B1. Extend the generator: night posting, cashier shifts, expenses, deposits/returns, discounts + approvals, cancellations/no-shows/extensions/moves (§3).
B2. Closing records for every history day computed with the real `closingSummary`/`reportPack` selectors (so desktop and phone reports equal the stored records); `closedDates` filled; `openingCash` of 24 Oct = 23 Oct closing cash, which must equal BASE's ₹5,000 float logic — if it can't, adjust history so 23 Oct closes with the BASE opening float.
B3. Compact housekeeping/audit history (H-06): last 30 days in full, older days as daily summary rows in a `historyX` slice (`{date, departures, cleans, stayovers, audits}`), read by the reports.
B4. Reports: make sure Reports date ranges, CSV export and drill-downs work over months (pagination/virtual lists for long lists on desktop and phone; phone lists show 50 rows with "Show more").
B5. Overview/Reports "History summary" card: FY-to-date occupancy, ADR, RevPAR, room revenue, stays, cancellations, by month (simple bar list; follow the dataviz rules already in the app).
B6. Tests (`stream-history-b.mjs`): for every closed day, the stored closing record equals the recomputed `closingSummary`; drawer continuity (day N closing cash = day N+1 opening float + rules); monthly totals equal the sum of daily records; cash differences have approvals; no posting on closed days; accounting module trial balance balanced for every day (`acc*` selectors).
Done when: a manager can open Reports for "Apr 2026" or "FY to date" on desktop and phone and the totals match the drill-downs.

### Phase C — Module 13–26 history (session 3)
C1. Generate module slices per §3 (yield, groups, maintenance, inventory, staff/payroll, guest services, checklists, accounting locks, channels, POS, messaging/documents), always **consistent with Phase A/B data** (e.g. POS room posts appear as folio lines; OTA bookings exist as bookings; group members are real bookings; maintenance blocks are real room blocks).
C2. Payroll runs Apr–Sep locked using the module-17 calculation functions (not hand-typed numbers); accounting month locks Apr–Sep using the module-20 lock snapshot function.
C3. Tests (`stream-history-c.mjs`): every module invariant (POS folio posts = folio lines; OTA commission totals = channel report; payroll locked runs = recalculation; accounting vouchers balanced and locked snapshots = recomputed vouchers; inventory consumption ≈ standard × occupied room-nights; maintenance downtime nights = blocked nights; group pickup = linked bookings).
C4. Performance pass (§4.6) and the full Playwright smoke: 9 roles × every page × desktop 1366 and phone 360 on FY26, no console errors, no sideways scroll, first screen time recorded (`05 Delivery Evidence/History/`).

### 4.6 Performance rules (apply when budgets fail)
- Selectors that scan all bookings (`available`, calendar, `grpHolds` fingerprint, report packs) get **indexes memoised per state object** (WeakMap keyed by state): bookings by room, by arrival/departure date, live bookings for a date range. Today `grpHolds` builds a fingerprint string of all bookings on each call; replace it with a version counter or an index.
- Report packs for ranges > 31 days use the stored daily closing records instead of recomputing from folios.
- Phone lists render at most 50 cards with "Show more"; desktop tables page by 100 rows.
- Don't keep huge derived arrays in React state; compute with `useMemo` keyed on `s`.

## 5. Phase D — Android APK with Capacitor (session 4; plus session 5 if needed)

Prerequisites on the PC (from HANDOFF.md): Android SDK `C:\Users\Sagar\AppData\Local\Android\Sdk`; Android Studio installed; AVD `codex_api35_google`; Node v25; **Java on PATH is 1.8, which is too old**.

D0. In PowerShell for every Android session:
```powershell
$env:JAVA_HOME="C:\Program Files\Android\Android Studio\jbr"; $env:Path="$env:JAVA_HOME\bin;$env:Path"
$env:ANDROID_HOME="C:\Users\Sagar\AppData\Local\Android\Sdk"; $env:Path="$env:ANDROID_HOME\platform-tools;$env:ANDROID_HOME\emulator;$env:Path"
java -version   # must show 17 or 21
```
If Gradle fails because of the space in the path, build from a mirror folder `C:\hcc-android` (copy `04 Android` there with robocopy) and copy the APK back.

D1. Prototype changes first (in `03 Prototype`, no new packages — use the native bridge `window.Capacitor` when present):
- `display.tsx` already maps Auto→Mobile in the app (`isNativeApp`). Keep.
- **Android back button:** in `App.tsx`, when `window.Capacitor?.Plugins?.App` exists, add a `backButton` listener: close sheet → wizard step back (where a sheet exposes one) → `history.back()` for pages → at the landing page show "Exit Hotel Control Centre?" confirm sheet → `App.exitApp()`.
- **Status bar:** maroon `#7a1f2b`, light icons (`StatusBar` plugin) when native.
- **Printing (DEC-047):** `window.print()` does nothing in the Android WebView. Implement a `printDocument(html, title)` helper used by every print button. In the browser it calls `window.print()` as today. In the APK it uses the print plugin chosen in D2; if no plugin passes D2, it writes the printable HTML to a file (Filesystem) and opens the Android share sheet (Share) so the user can print from Chrome or a printer app.
- **CSV export:** same pattern — browser download as today; in the APK write to `Documents/HCC/<name>.csv` (Filesystem) and offer Share.
- Unit tests for the pure parts (back-step decision function, file names) — native calls are mocked.
D2. Create `V:\Codex\Hotel Control Centre\04 Android` (own `package.json`):
```powershell
npm init -y
npm i @capacitor/core @capacitor/app @capacitor/status-bar @capacitor/splash-screen @capacitor/filesystem @capacitor/share
npm i -D @capacitor/cli @capacitor/android @capacitor/assets
npx cap init "Hotel Control Centre" in.hcc.prototype --web-dir www
```
- Check the current stable Capacitor major (`npm view @capacitor/core version`) and use one major consistently across all `@capacitor/*` packages.
- Print plugin: search npm for a maintained Capacitor printer plugin that supports the same Capacitor major (look at the last publish date, Android support and an HTML-print API). Test it on the emulator with a sample invoice. If none works, use the Filesystem + Share fallback from D1 and record the decision in HANDOFF.md.
- `scripts/copy-web.mjs`: copy `..\03 Prototype\dist\index.html` → `www\index.html` (fail if the build is older than the sources).
- `capacitor.config.ts`: `appId`, `appName`, `webDir:'www'`, `android.backgroundColor:'#faf6f0'`, SplashScreen sand background with a maroon logo, `server.androidScheme:'https'` (default).
D3. `npx cap add android`; icons/splash: generate from a 1024 px maroon "H" icon (same design as the web manifest icon) with `npx capacitor-assets generate --android`.
D4. Build:
```powershell
cd "V:\Codex\Hotel Control Centre\03 Prototype"; npm run build
cd "V:\Codex\Hotel Control Centre\04 Android"; node scripts\copy-web.mjs; npx cap sync android
cd android; .\gradlew.bat assembleDebug
# APK: 04 Android\android\app\build\outputs\apk\debug\app-debug.apk
```
Set `versionName` "0.1.0-proto" and increase `versionCode` for each build (`android/app/build.gradle`). Copy the APK to `V:\Codex\Hotel Control Centre\05 Delivery Evidence\APK\HCC-0.1.0-proto-debug.apk`.
D5. Emulator test (record as **emulated**, DEC-048):
```powershell
emulator -avd codex_api35_google   # in a separate window; wait for boot
adb install -r "05 Delivery Evidence\APK\HCC-0.1.0-proto-debug.apk"
adb shell am start -n in.hcc.prototype/.MainActivity
```
Checklist (screenshot each with `adb exec-out screencap -p > file.png` into `05 Delivery Evidence\APK\`):
- App opens in Mobile mode with FY26 data in < 3 s.
- Back button: sheet → page → exit confirm.
- Each of the 9 roles: tabs and More are correct.
- AC-M05: booking → check-in.
- AC-M06: ₹3,400 split payment, deposit return, shift close.
- AC-M07: housekeeper cleans Room 105.
- AC-M08: manager closes the day.
- Print an invoice (plugin or share), export a CSV.
- Data survives app restart (IndexedDB); Reset works.
- Rotate to landscape: still usable.
- No horizontal scroll.
D6. Owner's phone: enable Developer options → USB debugging → `adb install -r …apk`, or copy the APK to the phone and allow "Install unknown apps". Note the phone model in HANDOFF.md. The owner runs the same checklist and sends screenshots — store them as **physical device** evidence, separate from the emulated evidence.
D7. Write `04 Android\README.md`: prerequisites, the D0 environment lines, the build commands, where the APK lands, how to install, and known limits (debug only, demo data per phone, no backend).

## 6. Files Claude Code is expected to create or change
- New: `03 Prototype\src\fixtures\history\{prng,calendar,names,generate,modules}.ts`, `tests\stream-history-{a,b,c}.mjs`, `tests\stream-native.mjs`, `04 Android\**`, `05 Delivery Evidence\History\*`, `05 Delivery Evidence\APK\*`.
- Changed: `src\fixtures\scenarios.ts`, `src\app\storage.ts`, `src\app\App.tsx` (loading state, native back button), `src\domain\types.ts` (SCHEMA 5, `historyX` slice), print/CSV helpers in `src\components\ui.tsx` (or a new `src\app\native.ts`), the print/CSV buttons to call the helpers, `HANDOFF.md`, `CLAUDE.md` (new test count, Android commands).

## 7. Acceptance (owner can check)
| ID | Check |
|---|---|
| H-AC1 | Scenario picker shows "FY 2026-27 history"; the app opens on it by default; Reset reloads it. |
| H-AC2 | Reports "FY to date" shows ~56% occupancy, ADR ~₹2,650, ~1,200–1,400 stays, and every total equals its drill-down (desktop and phone). |
| H-AC3 | The calendar for any past week shows realistic stays, maintenance blocks and group stays without overlaps. |
| H-AC4 | Accounting: months Apr–Sep are locked and balanced. Payroll: Apr–Sep runs are locked. POS, channels, guest services and inventory screens show months of history. |
| H-AC5 | 24 Oct (today) behaves exactly like BASE (the S0 walkthrough still works when S0 is loaded). |
| H-AC6 | Performance budgets of H-07 are met and recorded. |
| A-AC1 | `app-debug.apk` installs on the emulator and on the owner's phone and opens in Mobile mode. |
| A-AC2 | Android back button, printing (plugin or share), CSV export and data persistence work in the APK. |
| A-AC3 | AC-M05…M08 pass in the APK on the emulator; results stored as emulated evidence, and the phone results separately. |

## 8. Owner decisions (ask only these)
- **OWNER-1:** Confirm FY26 as the startup default (H-02). Default: yes.
- **OWNER-2:** If no print plugin works, is the share-sheet route OK for printing? Default: yes.
- **OWNER-3:** Phone model for physical testing (for the record).

## 9. Progress (Claude Code ticks these)
- [x] Phase A — generator foundation, FY26 scenario, IndexedDB (24 Oct 2026: 403 tests, tsc + build green; FY26 = 1,286 bookings, 56.5 % occupancy, ADR ₹2,685, ₹71.8 lakh room revenue, 1.38 MB, generated in ~20 ms)
- [x] Phase B — daily operations, closings, reports, history summary (24 Oct 2026: 420 tests, SCHEMA 6; 206 closing records, cash joins up with BASE, FY report pack ~250 ms, accounting balanced for every day)
- [x] Phase C — module 13–26 history, performance, full smoke (24 Oct 2026: 435 tests, SCHEMA 7; evidence in 05 Delivery Evidence\History\HCC_History_Smoke_Phase_C.md)
- [x] Phase D1 — native hooks in the prototype (back button, print/CSV helpers) (24 Oct 2026: 445 tests; src\app\native.ts)
- [x] Phase D2–D5 — Capacitor project, debug APK, emulator evidence (DONE 25 Oct 2026 on APK v8, emulator API 35: Part 2 role/Back/Reset/CSV checks 21/21, Part 3 AC-M05…M08 + BL-01 17/17; evidence `05 Delivery Evidence\Acceptance\D5_Roles.md`, `D5_AC.md`. Open defects: AC-03 (S4), AC-04 (S3), R-01 (S4, owner decision) — fixes in progress)
- [x] APK versions — v1 24 Oct … v7 … **v8** (25 Oct; web bundle = `03 Prototype\dist\index.html`, SHA-256 starts `76519ceb`; 502 tests). v9 follows the Wave 0.5 merge.
- [ ] Phase D6 — owner's Samsung Galaxy Tab S7 (PARTLY DONE 25 Oct: 17 checks run on the physical tablet, `05 Delivery Evidence\Acceptance\D6_TabS7.md`; open owner decisions T-01 (portrait top bar ~25 % of height) and T-02 (32–38 dp controls); physical print/touch/long-session Back checks still outstanding)
- [x] Phase D7 — Android README written
- [ ] Owner acceptance of W0-D (browser 15/15, Tab S7 17, emulator 21/21 + 17/17 complete; awaiting owner)
