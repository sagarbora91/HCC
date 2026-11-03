# F6 cloud lanes — hall booking

Start only after the owner approves the spec (OP-17). **At most 3 agents alive.** Each lane works in its own worktree off `claude/f6`, commits small, never pushes, never touches `main`. No builds/tests in cloud lanes unless the coordinator says so; heavy jobs run on the hotel PC through `tools/Invoke-Serialized.ps1`.
Read first: `02 Planning/modules/HCC_Module_27_Hall_Booking_Spec.md`, `02 Planning/agents/F6-CONTRACT.md`, CLAUDE.md architecture rules.

| Lane | Model | Branch | Owns | Migrations | Tests | DONE file |
|---|---|---|---|---|---|---|
| **H1 hb-domain + billing** | Opus | `claude/f6-h1` | setup commit (SCHEMA 14, `s.hallX`, `Booking.hallEventId?`); `src/domain/streams/hall.ts`, `hall-billing.ts`, `handlers/hall.ts`, `fixtures/ext/hall.ts`; `Hcc.Domain/Hall/HallRules.cs`, `HallBilling.cs`, `Hcc.Application/Hall/HallBillingCommands.cs`; accounting sources `hall:*`, ledger 4400 | 4300–4329 | `stream-hall.mjs`, `stream-hall-billing.mjs`, `Hcc.Hall.Tests/BillingTests.cs` (HB-GOLDEN) | `05 Delivery Evidence/F6/H1-DONE.md` |
| **H2 quotation + T&C** | Opus (numbering, guard and snapshot touch money documents; plan lists quote-ui / tnc-seed as Sonnet — Sonnet acceptable if H1 has landed the totals) | `claude/f6-h2` | `QUO_*` commands + `HALL_TNC_TEMPLATE_SAVE` in handlers (append section), QUO number series, placeholder guard + override, T&C snapshot; `fixtures/ext/hall-seed.ts`, `deployment/import-templates/halls.csv`, `tnc-templates.csv` | 4330–4344, 4349 | `stream-hall-quote.mjs`, `stream-hall-seed.mjs` | `05 Delivery Evidence/F6/H2-DONE.md` |
| **H3 hb-ui + server + tests** | Sonnet | `claude/f6-h3` | `src/features/hall/*` (page `hall-booking`, tabs Enquiries · Quotations · Calendar · Events · Menus · Templates; quotation editor; T&C editor; prints incl. BEO; `Mobile.tsx`); navmap / registry / subscreens lines; `Hcc.Application/Hall/*Commands.cs` (non-money), `SqlHallStore`, endpoints, RBAC seed; `Hcc.Hall.Tests` project; `HallGoldenTests.cs` | 4345–4348 | `stream-hall-ui.mjs`, `stream-hall-print.mjs`, `check-phone-hall.mjs`, `StoreTests.cs`, endpoint tests | `05 Delivery Evidence/F6/H3-DONE.md` |

**Order.** H1 starts first and lands the setup commit + types (≈ first hour). H2 and H3 start from H1's first commit (H3 may stub names until then). The coordinator (Opus) merges H1 → H2 → H3 into `claude/f6`, wires the tariff-card Halls tab, re-pins `migrations.lock.json`, writes `05 Delivery Evidence/F6/F6-DONE.md`; the hotel PC runs the suites and the gate `-Wave hb`.

**DONE file contents.** Files changed, migrations added (and lock entries), tests added with expected counts, deviations from the contract, open questions.

**Exit.** HB-GOLDEN green both sides; printed totals = screen; Reception cannot edit a template; placeholder template blocks Send; HK / maintenance see no prices.
