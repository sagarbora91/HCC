# HCC multi-agent plan

Version 1.0 · 24 Oct 2026 · Status: **proposed — owner go-ahead needed per wave** · Builds on: A0/A1 result (`05 Delivery Evidence/Assembly/A0_A1_Result.md`), contracts/schema package (`HCC_Production_Contracts_and_Schema_v1.md`), the rules the 16 agents followed in session 3 (`HCC_Parallel_Agent_Brief.md`).

## 1. Why waves, and how many agents
- Two gates decide what can run in parallel: **prototype acceptance (D5/D6 + BL-023)** and **production authorization (A4)**. Wave 0 needs neither; Wave 1 needs both.
- Each agent costs roughly a full session of usage and must re-read context. Session 3 showed 16 agents work when file ownership is strict, but merge and verification then become the bottleneck. **Recommended: 4 agents in Wave 0, 5–6 in Wave 1**, plus one lead (this session type) that merges and verifies. CLAUDE.md's "prefer single-agent" default is overridden only for the waves the owner approves.
- One writer per file. Shared files (`src/app/registry.ts`, `src/domain/types.ts`, `src/domain/errors.ts`, `src/services/mock/index.ts`, `package.json`) are changed only by the lead between waves; agents report "shared change needed" instead.

## 2. Prerequisite (lead, before any wave) — version control
The workspace is **not a git repository**, so isolated worktrees are impossible today. Lead step (≈ 15 min, owner approval):
1. `git init` at `V:\Codex\Hotel Control Centre`, `.gitignore`: `node_modules/`, `06 Reference Repositories/` (own clones), `03 Prototype/dist/`, `03 Prototype/tests/.generated/`, `03 Prototype/evidence/`, `04 Android/.build/` (trust store), `04 Android/android/app/build/`, `07 Integration Lab/.generated/`, `*.apk`, `*.jks`, `*.keystore`.
2. Baseline commit = today's state (462 tests). Each agent then runs in its own worktree/branch (`isolation: worktree`), `node_modules` linked, never `npm install` in `03 Prototype`.
3. The navigation session (Session 8) is currently editing the same folder without git — finish or pause its step before `git init`, then it continues as agent W0-A on a branch.

## 3. Wave 0 — prototype completion (no production authorization needed)

| Agent | Scope | Owns (writes only these) | Depends on | Done = |
|---|---|---|---|---|
| **W0-A Navigation** (already running as Session 8) | Steps 2–4: staff-picker sign-in, Modules Home + category pages + breadcrumb, hamburger drawer replacing sidebar/More, Customers tile (customer history), booking-ID search on Arrivals | `src/app/App.tsx`, `src/app/mobile/MobileShell.tsx`, `src/app/navmap.ts`, `SearchPalette.tsx`, new `src/features/customers/*`, `src/styles/search.css` + new nav CSS, `tests/stream-navmap.mjs`, new `tests/stream-customers.mjs` | — | navmap tests + every page reachable for every role at 1366 px and 360 px |
| **W0-B Cash & till** | Implement the owner's till choice (A2.3 pack): POS cash into drawer (A) or per-till counts + `CASH_TRANSFER` (B); open POS checks block day close; FY26 history regenerated coherently | `src/domain/selectors.ts` (`drawer` only), `src/domain/streams/{closing,pos}.ts`, `services/mock/handlers/{closing,pos}.ts`, `src/features/{closing,pos}/*`, `src/mobile/money/*` cash pages, `src/fixtures/history/operations.ts`, new `tests/stream-a2-till.mjs` | **Owner answer A/B** | scenario tests (cash, UPI, room charge, void, refund, transfer, replay, close blocked); closing records recomputed with `closingSummary`; no assertion weakened |
| **W0-C Booking UX for A2.1** | Show stay restrictions in the booking/amend forms before submit (desktop modal + phone wizard step), approver override field with reason, `RESTRICTED` message styling | `src/features/reservations/*` (booking + change-dates modals), `src/mobile/frontdesk/*` booking wizard, `src/features/groups/*` rooming form (override field only), `tests/stream-a2-ui.mjs` | Owner confirms override authority (default: approve permission) | forms warn before submit; override works for Manager/Owner only; phone wizard passes 360 px checks |
| **W0-D Acceptance & evidence** | BL-023 desktop walkthrough (24→26 Oct), D5 emulator 9-role check + AC-M05…M08, D6 Tab S7 checklist (CSV, print, Reset, touch comfort), acceptance matrix + M3 delivery summary, APK rebuild (versionCode +1) | `05 Delivery Evidence/**`, `02 Planning/HCC_Acceptance_Matrix.md`, `04 Android` version bump only | **Runs last**, on the merged W0-A/B/C build; Tab S7 connected by USB | every AC row Passed/Failed with evidence (emulated vs physical labelled); open defects listed |

W0-A, W0-B and W0-C run **in parallel** (no shared files; W0-C touches `features/reservations` which W0-A only links to). W0-D runs after the lead merges them. Lead merge order: B → C → A (A touches the shell and is most likely to conflict on CSS).

**Gate after Wave 0:** owner accepts the prototype (D5/D6 + walkthrough) and authorizes A4 production construction (DEC-17).

## 4. Wave 1 — production platform and first guest slice (`08 Production/`, after authorization)
Module layout per contracts package §3–§4 and instructions §5. All agents code against the contracts in `HCC_Production_Contracts_and_Schema_v1.md`; the lead freezes `packages/contracts` before agents start.

| Agent | Scope | Owns | Depends on |
|---|---|---|---|
| **P0 Foundation** (lead or first agent, sequential) | Workspace (NestJS app, TypeScript, lint), Node SQL driver choice spike (BIGINT/Unicode/TLS/rowversion), migrations tool, money/date kernel (promote lab H02/H04 with provenance into `third-party/source-reuse.json`), error types, idempotency + outbox + audit infrastructure, test DB harness on `.\SQLEXPRESS` | `apps/api` bootstrap, `packages/contracts`, `modules/platform/*`, `database/migrations/0001…`, `tests/harness`, `third-party/*` | Authorization |
| **P1 Identity & access** | Users, roles, grants, sessions, policy engine (DEC-040), capabilities query, PERM coverage from `permission-coverage.json` | `modules/access/*`, its migrations, `tests/access/*` | P0 |
| **P2 Inventory & reservations** | room_night allocation, holds, group-block capacity (explicit block context, no global exemption), quotes, restrictions (`RESTRICTED`), amend/cancel | `modules/inventory/*` | P0 |
| **P3 Stays & housekeeping** | registration, check-in, segments, checkout (dirty + outbox), housekeeping tasks/inspection, maintenance blocks | `modules/stays/*`, `modules/housekeeping/*` | P0; P2 interfaces |
| **P4 Billing & cashier** | folio, room-night posting, receipts, refunds (row-locked remainder), approvals binding (DEC-041), tills per W0-B decision | `modules/billing/*`, `modules/payments/*` | P0 |
| **P5 Web client boundary** | typed feature clients, mock adapter over today's `execute()`, HTTP adapter for availability → quote → book → booking details; production build excludes demo controls | `apps/web/src/clients/*`, `apps/web/src/features/reservations` wiring | P0 contracts |
| **P6 Closing & reports** (second half) | resumable business-day close, late-write barrier, closing snapshot, owner reports from the same transactions | `modules/closing/*`, `modules/reporting/*` | P2–P4 merged |
| **P7 Deployment & recovery** (second half) | Windows service, LAN TLS certificate, backup via Task Scheduler (Express has no Agent), restore into a separate DB, APK server-address/session handling | `deployment/windows/*`, `04 Android` connection settings | P0 |

Run P1–P5 in parallel after P0; then P6–P7. The lead then runs the **first real vertical slice** (instructions §9, 10 steps) with two independent clients against a disposable SQL database — the Wave 1 exit gate. Remaining MVP modules (expenses, masters, corrections) become Wave 2 with the same pattern.

## 5. Rules every agent brief must contain
1. Read: `CLAUDE.md`, this plan, its row, the contracts package, the A1 register; for Wave 0 also `HCC_Parallel_Agent_Brief.md` (architecture, phone rules, theme, Playwright checks).
2. Write only owned files; list any needed shared change in the final answer (exact file + diff) — do not make it.
3. No donor code unless the A1 register marks it ADAPT and its provenance record is copied; never edit `06 Reference Repositories`.
4. Money integer paise, simulated clock in the prototype, injected clock in production, fictional data only, no new npm packages in `03 Prototype`.
5. Tests before done: `npx tsc --noEmit`, full `npm test` (prototype) or the module's unit + SQL integration tests (production); never weaken an assertion; label emulated vs physical evidence.
6. Commit on the agent's branch only; never push; final answer ≤ 25 lines (built, tests, results, shared changes needed, gaps).

## 6. Lead responsibilities
Write the per-agent briefs from §3/§4 rows, launch, answer blocking questions, merge in the stated order, run full regression (prototype tests, lab, SQL spike/integration, build, Playwright smoke, APK when web inputs changed), update HANDOFF/acceptance matrix, and stop the wave if a critical invariant fails.

## 7. Decisions needed to start
1. **Approve `git init` + Wave 0 with 4 agents** (W0-A continues the running navigation session).
2. Till model A (shared) or B (separate) — unblocks W0-B.
3. Stay-restriction override authority (approve permission vs Owner only) — unblocks W0-C.
4. After Wave 0 acceptance: production go-ahead and release scope (first 12 MVP areas) — unblocks Wave 1.
