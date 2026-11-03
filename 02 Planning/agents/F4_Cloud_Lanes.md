# F4 cloud run — lane plan (2 Nov 2026)

This cloud run of wave F4 (`F4-CONTRACT.md`) folds the plan's eight build lanes into **3 build lanes + the integrator**, with **at most 3 agents alive** at once. Base = the setup commit on `claude/f4` ("F4 setup: contract, cloud lanes, types, append blocks"). Each lane works on its own branch `claude/f4-<lane>` cut from that commit; never push; the integrator (f4-int, Opus) merges T1 → T2 → T3.

**Setup commit gives every lane:** `SCHEMA` unchanged at 13 (contract header explains why); the F4 types at the end of `src/domain/types.ts` (`TRC*`, `RCT*`, `DGS*`, `FBK*`), the optional slices `tariffCardX / digestX / feedbackX`, the optional `days` / `cardVersion` fields on `State.tariffs[]`. **Import these; never redeclare.** Empty append blocks `// F4-T1` … `// F4-T1 end` (same for T2 / T3; C# the same; `Hcc.slnx` `<!-- F4-T<n> <range> -->`).

| Lane | Model | Branch | Builds (plan rows) | Migrations | Starts |
|---|---|---|---|---|---|
| **T1 tariff** | **Opus** | `claude/f4-t1` | tc-money + tc-ui + tc-print — contract §1 | 4380–4389 | now |
| **T2 revenue + digest** | Sonnet | `claude/f4-t2` | rev-report + digest — contract §2, §3 | 4370–4379 | now |
| **T3 quick wins** | Sonnet | `claude/f4-t3` | qw-audit + qw-feedback + qw-alerts — contract §4 | 4390–4399 | now |
| f4-int | Opus | `claude/f4` | merge, stubs out, full suites, `F4-DONE.md`, verify publish writes only masters and nothing reads the card for prices | 4789 reserve | after all DONE |

## Owned files (a lane edits only its rows plus its own append blocks)

- **T1** — `src/domain/streams/tariffcard.ts`, `src/services/mock/handlers/tariffcard.ts`, `src/features/tariffcard/*`, `src/styles/m-f4.css`, `features/reports/rate-sheet*` (+ the existing rate-sheet section of `Reports.tsx`, marked block), `tests/stream-tariffcard*.mjs`, `tests/stream-rate-sheet.mjs`, `tests/fixtures/f4/rate-card/*`; the tariff resolver change (contract §1.3: wherever `s.tariffs` is resolved per night — mark the edit `// F4-T1`). Server `Hcc.Application/Tariff/TariffCard*`, `Hcc.Infrastructure.SqlServer/.../SqlTariffCardStore.cs`, endpoints, `TariffResolver` day-set, migrations 4380–4389, `Hcc.Masters.Tests/TariffCardTests.cs`. Catalogue line for the rate sheet (append).
- **T2** — `src/domain/streams/rpt-revcat.ts`, `src/domain/streams/digest.ts`, `src/services/mock/handlers/digest.ts`, `src/features/reports/RevenueByCategory.tsx`, `src/features/digest/*`, `tests/stream-rpt-revcat.mjs`, `tests/stream-digest.mjs`; the day-close hook line (closing handler, marked `// F4-T2`). Server `RevenueByCategoryQueries.cs`, digest draft store / endpoints, migrations 4370–4379, tests. Catalogue line for `rpt.money.revenue-by-category-tender` (append). Stubs for T3 names (`qwReorderAlerts`, `fbkOverdue`, `fbkSummary`) in `src/features/digest/_contract-stubs-f4.ts` only.
- **T3** — `src/features/admin/AuditLog*` (or the existing audit page's marked block), `src/domain/streams/feedback.ts`, `src/services/mock/handlers/feedback.ts`, `src/features/guest-services/Feedback*` (check-out sheet mount = one marked line in the check-out flow), `src/features/stock/Reorder*`, `src/app/LanHealth.tsx`, printer settings UI block, `tests/stream-audit-viewer.mjs`, `stream-feedback.mjs`, `stream-qw-alerts.mjs`. Server feedback store / endpoints, printer test, migrations 4390–4399.

## Append blocks (setup commit; one lane per block)

| File | Blocks |
|---|---|
| `src/services/mock/handlers/index.ts` | `F4-T1` (tariffcard), `F4-T2` (digest), `F4-T3` (feedback) inside `const F4` |
| `src/app/registry.ts` | import + registration blocks `F4-T1`, `F4-T2`, `F4-T3` |
| `src/app/navmap.ts` | `PAGE_CATEGORY` blocks `F4-T1` (`'tariff-card':'revenue'`), `F4-T2` (`'owner-digest':'accounts'`, report keywords), `F4-T3` |
| `src/app/subscreens.ts` | `F4-T1` (tariff-card tabs), `F4-T2` |
| `src/app/App.tsx` | `F4-T3` (LanHealth mount) |
| `src/features/overview/Overview.tsx` | `F4-T2` (Yesterday card) |
| `src/domain/streams/syshealth.ts` | `F4-T3` (printers block) |
| `08 Production/src/Hcc.Api/HccComposition.cs` | `F4-T1`, `F4-T2`, `F4-T3` |
| `08 Production/Hcc.slnx` | `<!-- F4-T1 4380-4389 -->`, `<!-- F4-T2 4370-4379 -->`, `<!-- F4-T3 4390-4399 -->` |

Run `node tools/migrations-lock.mjs --add` for each new migration (lock file conflicts: the integrator re-runs it after merging).

## Every lane

- Contract beats guesswork; a Sonnet lane that hits a money / GST / posting / permission rule not settled in the contract stops on that item and writes it for f4-int.
- Cloud: run `npx tsc --noEmit`, your own `node tests/<file>.mjs`, `dotnet build Hcc.slnx` from the worktree root (0 warnings) and only the test projects you touch (`-maxcpucount:1`), one heavy job at a time; never commit `global.json`; never stage the `node_modules` symlink. Windows-only checks (fit-check at five viewports, Tab S7) go in the DONE file as "to run on the hotel PC".
- DONE file: `05 Delivery Evidence/F4/F4-T1-DONE.md`, `F4-T2-DONE.md`, `F4-T3-DONE.md` — template `tools/README-agents.md` §4; counts before / after; "Integration needed" with exact edits; stub names to remove.
- Commit trailer lines as the session's attribution reminder says; commits are auto-pushed — never `git push`.
