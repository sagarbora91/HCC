# F5 cloud run — lane plan (2 Nov 2026)

Wave F5 (ACC-4 + egress) folds the plan's lanes into **3 build lanes + the integrator**, **at most 3 agents alive**. Contracts: `ACC4-CONTRACT.md`, `EGRESS-CONTRACT.md`; F-rules and the Wave Plan §F5 still apply. Base = `claude/f5` (from main 0c16d931, F1–F3; **F4 is merged in by the integrator before lanes branch** — lanes start from the commit after that merge). Each lane works on `claude/f5-<lane>`; never push; the integrator (f5-int, Opus) merges.

| Lane | Model | Branch | Builds | Migrations | Starts |
|---|---|---|---|---|---|
| **E1 egress-server + egress-proof** | Opus | `claude/f5-egress` | EGRESS §1–§4: `Hcc.Infrastructure.Egress`, settings + fail-closed, `NET_OFFLINE`, outbox held + replay, audit, `Hcc.Egress.Tests`, `Hcc.Architecture.Tests`, `tools/egress-proof.ps1`, `Set-HccEgress.ps1` + task; System health **server** read; prototype `CFG_NET_SET` + mock outbox held | 4730–4739 | now |
| **A1 acc4-year + acc4-bank** | Opus | `claude/f5-acc4` | ACC4 §1–§3 both sides (domain, handlers, server stores / endpoints, carry-overs C1–C6), domain tests | 4280–4294 | now |
| **U1 acc4-ui + acc4-tests + egress-ui** | Sonnet | `claude/f5-ui` | Accounting tabs Year end / Bank reconciliation / Clearing (+ phone cards); golden4 + `RulebookYearTests.cs` + permission rows + export tests; bank fixture `bank-april-2026.csv`; Internet config group, System health Egress block (client), banners, held badge, APK allowNavigation | — | after A1 and E1 have committed their public names (types, commands, endpoints) — branches from A1 merged with E1 |
| f5-int | Opus | `claude/f5` | merge E1 → A1 → U1; F4 merge; fallout 4295–4299; full suites; `ACC4-DONE.md`; gate `-Wave f5` + egress proof | 4295–4299 | after all DONE |

## Owned files (a lane edits only these plus its append blocks)
- **E1:** `08 Production/src/Hcc.Infrastructure.Egress/**`, outbox worker held/replay hook, `HccError.NET_OFFLINE`, settings seed / provider `net.*`, `Health/Egress*` (server), migrations 4730–4739, `tests/Hcc.Egress.Tests/**`, `tests/Hcc.Architecture.Tests/**`, `tools/egress-proof.ps1`, `deployment/windows/Set-HccEgress.ps1` (+ installer task stub); prototype `src/domain/streams/egress.ts`, `handlers/egress.ts`, `tests/stream-egress.mjs`.
- **A1:** `src/domain/streams/accounting-year.ts`, `accounting-bank.ts`, `handlers/accounting-year.ts`, `handlers/accounting-bank.ts`, `fixtures/ext/bank-layouts.ts`; server `Hcc.Application/Accounting/YearLock*`, `BankRecon*`, `GstMonthService` fix, `AgeingQueries` source, vendor-return allocation, TDS dated-rate lookup, stores, endpoints, RBAC seeds, migrations 4280–4294; `deployment/import-templates/bank-statement-layouts.csv`; tests `stream-acc4-year.mjs`, `stream-acc4-bank.mjs`, `Hcc.Accounting.Tests/YearLock/`, `/Bank/`, carry-over tests.
- **U1:** `features/accounting/acc4-views.ts` + Year end / Bank / Clearing tab files, phone cards, `features/admin/Internet*`, banner components, Messages held badge, `04 Android/capacitor.config.*`; fixtures `bank-april-2026.csv` (fictional), tests `stream-accounting-ui4.mjs`, `stream-accounting-golden4.mjs`, `stream-egress-ui.mjs`, `RulebookYearTests.cs`, permission-matrix rows.

## Append blocks `// F5-<LANE>` … `// F5-<LANE> end` (C# the same; `Hcc.slnx` `<!-- F5-<LANE> range -->`)
- `F5-E1`: `HccComposition.cs`, `handlers/index.ts`, `services/mock/index.ts`, `config.ts` (net keys), `syshealth.ts`, `Hcc.slnx`.
- `F5-A1`: `HccComposition.cs`, `handlers/index.ts`, `services/mock/index.ts`, `rbac.ts`, `clients/sync/registry.ts`, `Hcc.slnx`.
- `F5-U1`: `subscreens.ts`, `navmap.ts` (if a page is added), `app/registry.ts`, `config.ts` Internet group UI block, `features/admin/SystemHealth.tsx` Egress block, `admin.ts` labels.

## Every lane
- Cloud: run `npx tsc --noEmit`, `npm test`, `dotnet build Hcc.slnx` (0 warnings) and only touched test projects (`-maxcpucount:1`), one heavy job at a time; never commit `global.json`. Windows-only steps (`egress-proof.ps1`, firewall task, fit-check at five viewports, gate) go under "to run on the hotel PC" in DONE.
- Every migration: `node tools/migrations-lock.mjs --add` + `Hcc.slnx` line.
- DONE file: `05 Delivery Evidence/F5/F5-<lane>-DONE.md` (`F5-E1-DONE.md`, `F5-A1-DONE.md`, `F5-U1-DONE.md`) — template `tools/README-agents.md` §4; counts before / after; "Integration needed" with exact edits.
- Contract beats guesswork; U1 (Sonnet) stops on any money / posting / permission rule and writes it for f5-int.
