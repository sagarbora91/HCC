# A0 / A1 (+ A2, A3 design) result — 24 Oct 2026

Single implementation stream, local only. Plan: `02 Planning/HCC_Component_Selection_and_Assembly_Plan_v1.md`.

## A0 — baseline (before any change)
| Check | Command (in `03 Prototype`) | Result |
|---|---|---|
| Typecheck | `npx tsc --noEmit` | exit 0 |
| Tests | `npm test` | **446/446** passed (18.8 s) |
| Build | `npm run build` | exit 0, `dist/index.html` 1,822 KB, SHA-256 `a7fb5eca…9948` (= `Hotel Control Centre.html`) |
| APK | file check | `05 Delivery Evidence/APK/HCC-0.1.0-proto-debug.apk` SHA-256 `974f4132…c48` (versionCode 4). Not rebuilt or re-tested — no web/native input for the APK was changed by this work. D5/D6 items (9 roles, AC-M05…M08, tablet CSV/print, Reset) remain **open**. |
| Donor snapshots | `git rev-parse HEAD` / `git status --porcelain` | all six at pinned commits, 0 changed files — before and after all lab runs |
| Evidence hashes | re-hash of `HCC_Component_Source_Evidence_v1.json` | 50 donor + 12 HCC files, **0 mismatches** |

Concurrent work noticed: another session (Session 8, navigation redesign) added `src/app/navmap.ts`, `SearchPalette.tsx`, `tests/stream-navmap.mjs` (+9 tests) during this session. No file overlap with this work.

## A1 — qualification (lab: `07 Integration Lab/`, run `node "07 Integration Lab/run.mjs"`)
| Candidate | Decision | Upstream spec (unchanged, via shim) | HCC tests |
|---|---|---|---|
| H01 reservation transitions | REFERENCE — original HCC map, conformance-tested against the mock | 37/37 | 46/46 |
| H02 LOS/occupancy pricing | ADAPT, lab only (paise/bps, half-open bands, single rounding) | 4/4 | 11/11 |
| H03 refund remainder | REFERENCE — HCC already stricter | not run (decimal.js/drizzle missing) | 8/8 |
| H04 timezone calendar date | ADAPT, lab only (invalid zone = error) | 2/2 | 7/7 |
| O01 check-in night picker | REJECT for now (no gap) | 1/1 | 2/2 |
| O02 checkout release date | REFERENCE / DEFER | 1/1 | 3/3 |
| **Lab total** | | **45/45** | **77/77** → **122/122**, 1 suite not run |

The shim was checked to fail correctly (5/5 deliberate failures detected). Donor code imported into HCC: **none**. Attributed adaptations exist only in the lab: H02 `rate-adjustments.ts`, H04 `calendar-date.ts` (Apache-2.0 LICENSE copied beside each, change notice in headers, records in `07 Integration Lab/evidence/source-reuse.json`). H05–H07, O03–O05, K01–K02, Q01, D01, M01: decisions in `02 Planning/HCC_Component_Qualification_Register.md` (reference/keep/defer/reject; nothing copied).

## A2 — integration gaps
- **A2.1 Yield restrictions enforced (done, prototype).** `yldStayViolations()` (`src/domain/streams/yield.ts`); new error code `RESTRICTED`; enforced in `BOOK` and `CHANGE_DATES` (`handlers/reservations.ts`), which also serve group pickup (`GRP_ROOMING` passes `restrictionOverride`), OTA accept and web booking requests. Override: approve-permission holders with a reason (recorded in booking notes + audit) — **prototype default, owner to confirm**. In-house MOVE not restricted. 7 tests in `tests/stream-a2.mjs`. Limitation: no override field in the booking UI yet (command-level only); the error message tells staff to ask an approver.
- **A2.2 Permission coverage (evidence, no code change).** `node "07 Integration Lab/tools/perm-coverage.mjs"` → `evidence/permission-coverage.md/.json`: 165 live commands, **all guarded** — 137 coarse `req()`, 8 fine-grained `authorize()`, 18 custom gates (assigned scope, payroll, handover), 2 open by design (AI_ASK/AI_CLEAR); 19 dead built-in cases shadowed by stream handlers; 28/165 carry their intended PERM ID. Reads/exports are filtered in the UI only (whole state on the client) — must be server-enforced in production. Fine-grained PERM enforcement is designed into A4 rather than retrofitted into the mock.
- **A2.3 Cashier/POS (decision pack, no code change).** `02 Planning/HCC_A2_Till_Decision_Pack.md`: worked example for shared vs separate tills; recommendation A (shared) for a small hotel. **Owner question pending.**

## A3 — contracts/schema package
`02 Planning/HCC_Production_Contracts_and_Schema_v1.md` (identity/session, API conventions, error→HTTP map, 14 contracts, schema families, 10 data rules, open decisions). **Real SQL spike**: `node "07 Integration Lab/sql-spike/run-spike.mjs"` → **15/15** on SQL Server 2025 Express 17.0.1000.7 (disposable DB created and dropped): Unicode, overlap rollback, adjacency, rowversion, paise checks, refund remainder, idempotent night posting, idempotency key/payload, outbox + consumer dedup, date types, and 4 multi-connection races (blocked-then-rejected booking, rollback-then-winner, concurrent refunds, 12-way rush → 1 winner, 0 deadlocks). Two design rules came out of failures on the first run (explicit rollback in CATCH; no catch-duplicate-key inside a transaction).

## After all changes
`npx tsc --noEmit` exit 0 · `npm test` **462/462** (446 + 7 A2 + 9 from the concurrent navigation session) · `npm run build` 1,838 KB (includes the other session's navigation work) · donor repos clean.

## Not done / limitations
No production code (`08 Production/` not created — A4 not authorized). No Node SQL driver chosen. APK not rebuilt (unchanged inputs from this work; the navigation session will rebuild). D5/D6 device acceptance still open. Till model and override authority await the owner.

## Next concrete step
Owner answers: (1) till model A/B, (2) restriction-override authority, (3) production go-ahead after D5/D6. Then A2.3 implementation (≈1 session) and A4 platform start in `08 Production/` per the multi-agent plan (`02 Planning/HCC_Multi_Agent_Plan_v1.md`).
