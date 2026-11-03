# F7 cloud run — lane plan (3 Nov 2026)

Wave F7 (report module) in the cloud: **at most 3 agents alive** (2 lanes + the coordinator / integrator). Contracts: `RPT-ARCHITECTURE.md`, `F7-CONTRACT.md`; F-rules and Wave Plan §F7 still apply. Base = `claude/f7` (main e14c0ead + setup commit). Owner defaults: OP-13 → hook; A16 → Lodge look + HCC letterhead; A15 → skip.

| Phase | Lane | Model | Branch | Builds | Migrations | Starts |
|---|---|---|---|---|---|---|
| 1 | **R1 rpt-engine** | Opus | `claude/f7-r1` | engine both sides (contract §2), `Hcc.Reporting` + `Hcc.Reporting.Tests`, `MinimalPdfWriter`, endpoints, schedules / runs / export log, golden harness `tests/rpt-golden/` + `tools/pdf-drift.mjs`, demo report `revenue-by-category-tender` | 4400–4419 | now; commits interfaces within 2 h |
| 1 | **R2 rpt-hub** | Sonnet | `claude/f7-r2` | Reports hub on the catalogue: search, favourites, parameters pane, preview (`ReportView`), Export Excel / PDF / Print, schedule editor, "my subscriptions", phone cards | — | after R1's interface commit |
| 2a | **R3 guests+rooms** | Sonnet | `claude/f7-r3` | batch 1 guests + rooms (contract §4) | 4420–4423 | R1 merged |
| 2a | **R4 money-a** | Sonnet | `claude/f7-r4` | batch 1 money | 4424–4427 | R1 merged |
| 2b | **R5 tax** | Sonnet | `claude/f7-r5` | batch 2 tax | 4428–4431 | 2a merged |
| 2b | **R6 acc** | Sonnet | `claude/f7-r6` | batch 2 accounting | 4432–4433 | 2a merged |
| 3 | **R7 documents** | Sonnet | `claude/f7-r7` | batch 3 documents | 4434–4435 | 2b merged |
| 3 | **R8 built moves** | Sonnet | `claude/f7-r8` | batch 3 built reports + export-log report | 4436–4437 | 2b merged |
| end | f7-int | Opus | `claude/f7` | merge per phase; entry-point switches; catalogue statuses from code; full suites; `RPT-DONE.md`; gate `-Wave f7` on the PC | 4445–4449 | after each phase |

## Owned files (a lane edits only these plus its append blocks)
- **R1:** `src/domain/reports/{engine,catalogue,params,mask,computed,print}.ts`, `handlers/reports.ts`, `tests/stream-rpt-engine.mjs`, `tests/rpt-golden/**`, `tools/pdf-drift.mjs`, `tools/rpt-catalogue.mjs`; server `src/Hcc.Reporting/**`, `Hcc.Api/Endpoints/ReportEndpoints.cs`, `Hcc.Application/Reporting/Rpt/**` (may extend), `tests/Hcc.Reporting.Tests/**`, migrations 4400–4419.
- **R2:** `features/reports/Hub*.tsx`, `ReportView*.tsx`, `Schedule*.tsx`, phone cards in `features/reports/mobile*`, `tests/stream-rpt-hub.mjs`.
- **R3…R8:** `src/domain/reports/<area>/**`, `features/reports/<area>/**`, server `src/Hcc.Reporting/Reports/<Area>/**`, their tests and goldens, their migrations.

## Rules
- ≤ 3 agents alive at any time; report lanes run **two at a time**.
- Every lane: `. /tmp/claude-0/envs.sh`; `npx tsc --noEmit`, `npm test`, `dotnet build Hcc.slnx` from the worktree root (0 warnings), only touched test projects `-maxcpucount:1`; one heavy job at a time; never edit `global.json`; never stage the `node_modules` symlink; no new npm / NuGet packages.
- Every migration: `node tools/migrations-lock.mjs --add` + its line in the contract table.
- Sonnet lanes stop on any money / permission / masking rule not stated in the catalogue entry and write the question into their DONE for f7-int.
- **DONE file:** `05 Delivery Evidence/F7/F7-<lane>-DONE.md` (`F7-R1-DONE.md` … `F7-R8-DONE.md`), template `tools/README-agents.md` §4: counts before / after, reports moved, goldens added, "Integration needed" with exact edits.
- Windows-only steps (gate, fit-check, emulator) are left to the owner's PC and listed in the DONE.
