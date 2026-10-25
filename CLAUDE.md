# Hotel Control Centre (HCC) — project instructions for Claude

Internal hotel-operations app for the owner's hotel business in India (owner: Sagar). Local-network first. Windows desktop + Android phone.
**Current stage:** interactive prototype — React 19 + TypeScript, single self-contained HTML, fictional data, in-browser mock service — **accepted by the owner 25 Oct 2026 (APR-011)**. Production (`08 Production\`: .NET 10 ASP.NET Core + SQL Server 2025/Express, reusing ETP platform code, DEC-054) is built on branch `claude/wave1-integration`, not merged to main.
**Status (25 Oct 2026, evening):** `main` = all 26 modules + **Wave 0.5** (cash book, Excel/day-pack/Tally exports, compliance calendar, SCC payroll/leave/payslips, Master configuration hub, FY26 guest profiles, RBAC rulings DEC-063), desktop + phone views, Maroon & Sand theme, FY26 history, **694 tests**, **SCHEMA 8**, **APK v9** (`05 Delivery Evidence\APK\HCC-0.1.0-proto-debug.apk`, signed with this PC's debug key, DEC-064). W0-D acceptance evidence: `05 Delivery Evidence\Acceptance\W0-D_Acceptance_Report.md` (Tab S7 items accepted unverified). **Wave 1** (production server P0–P5: access with DEC-062 sign-in, inventory/reservations, stays/housekeeping, billing/cashier, web clients) is on branch `claude/wave1-integration` together with **Wave 2** (12 modules: Master configuration, payroll/leave, accounting/Tally, expenses, compliance, POS, maintenance, stock, guest services, messaging, handover, yield), P6 closing/reports, P7 deployment, the server GST master, encrypted guest IDs and W3-a/W3-b (**645 .NET tests** on `.\SQLEXPRESS`, run serially: `dotnet test Hcc.slnx -maxcpucount:1`); not yet merged to main. Plan: finish Wave 3 (`02 Planning/agents/W3_Briefs.md`) → merge `08 Production` into main → APK v11 → emulator tests → push. Main = prototype + Wave 1 web-client layer (`npm run build:production`), **781 tests**. Open items: `02 Planning/HCC_Implementation_Backlog.md`. Details: `HANDOFF.md` (Sessions 10-11), `MIGRATION.md`.

**Location (work PC since 25 Oct 2026):** `E:\Projects\HCC\Hotel Control Centre`. Older documents say `V:\Codex\Hotel Control Centre` (old PC) — read that as this folder. Use Node 25 (`E:\Tools\nodejs`; the `E:\Program Files\nodejs` v24 comes first on PATH). SCC rule sources: `E:\Projects\HCC\Migration data on 25 oct\HCC-Migration-2026-10-25\reference-inputs\SCC-modules`. ETP (`V:\ETP`) was not moved.

## Read first
1. `HANDOFF.md` (this folder) — current status and next steps
2. `02 Planning/HCC_Plan_FY_History_and_APK_v1.md` — CURRENT plan (phases A–D, §9 progress ticks)
3. `02 Planning/HCC_Mobile_Version_Plan_v1.md` — mobile design rules, decisions DEC-044…051; module specs 13–26 in `02 Planning/modules/`
4. `02 Planning/HCC_Decision_Register.md`, `HCC_Prototype_Spec_v1.md`, `HCC_RBAC_Matrix_v1.md`, `HCC_Business_Workflow_Spec_v1.md`, `HCC_Fixture_and_Scenario_Catalog.md`, `HCC_Acceptance_Matrix.md`, `HCC_Implementation_Backlog.md`
5. `03 Prototype/README.md` — commands, code layout, phone-over-Wi-Fi

## Layout
- `01 Research Stage/` original conversation + execution plan · `02 Planning/` specs (+ `modules/`) · `03 Prototype/` app · `04 Android/` Capacitor wrapper (README there: build env incl. Norton trust-store workaround) · `05 Delivery Evidence/` summaries/tests/evidence

## Commands (run in `03 Prototype`)
`npm install` · `npx tsc --noEmit` · `npm test` (781 tests must pass on main) · `npm run build` (→ `dist/index.html` + `Hotel Control Centre.html`) · `npm start` (http://127.0.0.1:4173; `-- --lan` for phones)

## Architecture rules
- All mutations via `execute(state, uid, cmd)` (`src/services/mock/commands.ts`) + stream handlers in `src/services/mock/handlers/*.ts`; typed `HccError` codes; idempotency keys.
- Pure helpers in `src/domain/` and `src/domain/streams/*.ts`; fixtures in `src/fixtures/` (+ `ext/`); UI per area in `src/features/*` registered via `register.tsx` → `src/app/registry.ts`.
- Money = integer paise; simulated clock `s.clock`, never real time; fictional data only; GST from the dated GST master (DEC-069, `src/domain/gst.ts`, Master configuration › Money and tax, Owner-only): rooms per unit per night ≤ ₹7,500 5 % (12 % before 22 Sep 2025) / above 18 %, restaurant 5 % (18 % if specified premises), other services 18 %; CGST + SGST per line, half away from zero; posted lines keep their taxBps snapshot; rates illustrative — CA to confirm.
- New state fields optional; bump `SCHEMA` (now 8) in `src/domain/types.ts` when state shape changes.
- History: `src/fixtures/history/` (seeded generator `buildHistory` = stays in `generate.ts` + daily operations/closings in `operations.ts`; module 13–26 history in `modules*.ts` (group blocks and maintenance downtime are reserved in a pre-pass; room-service POS checks are built with the stays); closing records come from the real `closingSummary`; long lists use `usePaged`/`ShowMore` (desktop 100 rows, phone 50); BASE is never edited — FY26 = BASE + history). `DEFAULT_SCENARIO='FY26'` is used by the app only (`restoreState(raw,legacy,fallback)` defaults to BASE so tests keep the small fixture). Persistence = IndexedDB via `src/app/storage.ts`: one record per state slice + a manifest (`hcc-slices-1`), each save writes only changed slices in one transaction; `execute` is copy-on-write per slice (never mutate state outside a command); FY26 is generated in an inline Web Worker with a main-thread fallback; App shows a loading screen with progress.
- Navigation: `src/app/navmap.ts` = the 10 module categories, `PAGE_CATEGORY` (every nav page must be listed — a test enforces it), keywords and the search index; `SearchPalette.tsx` (Ctrl+K, top-bar button, phone app-bar icon; pages + quick actions only, role-filtered; booking-ID search belongs to Arrivals & check-in). Customers 360 (`features/customers`) opens from the Customers tile (see HANDOFF Session 8).
- Native hooks live in `src/app/native.ts` (back button, status bar, `printDocument`, `saveCsv` — all no-ops/fallbacks outside the APK); use them instead of `window.print()` / download links.
- Keep the build a single self-contained HTML (Capacitor-ready: no CDN/external fonts, relative paths). No new npm packages in `03 Prototype` (Capacitor packages only in `04 Android`).
- Phone versions: `FeatureRegistration.mobile.{pages,modals}` (registry.ts) replace desktop page/modal IDs when the mobile shell is shown; shared phone components in `src/app/mobile/components.tsx`; phone rules = 48 px targets, 16 px text, cards not tables, no sideways scroll at 360 px.
- Modules 13–26: one folder per module (`src/features/<m>`, `src/domain/streams/<m>.ts`, `src/services/mock/handlers/<m>.ts`, `src/fixtures/ext/<m>.ts`, `tests/stream-<m>.mjs`), state slice `s.<m>X`, prefixed names/commands.
- Availability: `available()` = `availableRaw()` minus room holds (group blocks via `registerRoomHold`); use `withHoldExempt(blockId, …)` for a block's own pickup.
- Permissions: `can()` / `NavItem.p` accept one permission or an any-of array. Housekeeping/maintenance never see hotel money (they may see only their own pay on "My shifts & pay", DEC-061).
- Theme Maroon & Sand: primary #7A1F2B, background #FAF6F0, text #2B1D16, muted #6B5547; WCAG AA; status colours always with text labels.

## Working preferences
- Always give full Windows file paths for files created/saved.
- Parallel multi-agent work is welcome on the work PC (up to ~16 agents), one emulator at a time; Sonnet for checklist/testing/mechanical agents, Opus for settings/money/statutory rules and for merging/verification. No 7-minute checkpoint job on the work PC (lead commits at sensible points). Run builds/tests locally; report concisely; save progress (update HANDOFF.md, tick the plan's §9) before stopping.
- Ask only for genuine decisions; don't re-ask approved ones (see Decision Register + mobile plan §11).
