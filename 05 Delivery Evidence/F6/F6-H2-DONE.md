# F6-H2 — Hall quotations + T&C (lane H2, Opus) — DONE

Branch `claude/f6-h2` (from `claude/f6` with the setup commit ee4fa350 and H1's commits up to 6340840d). Commits prefixed `F6-H2:`.
Contract: `02 Planning/agents/F6-CONTRACT.md`; spec `02 Planning/modules/HCC_Module_27_Hall_Booking_Spec.md` §6–§8, §18, §21.

## Prototype (`03 Prototype`)
- `src/domain/streams/hall-quote.ts` — numbering `QT/<fy>/0001` (FY code via `fdmFyCode`, restarts 1 April, consecutive over all quotations, nothing deleted → no gaps), T&C template in force (event type, else General; latest effective version), snapshot (deep copy) + placeholder flag, default lines from hall slot + menu × guaranteed plates, master list rates, discount approval check, **print model `hallQuotePrint`** (lines priced per line by H1's `hallPrice`, totals, advance amounts, T&C, validity, watermark DRAFT / NOT FINAL / SUPERSEDED / EXPIRED, signature block), quotation register, CSV for `halls.csv` / `tnc-templates.csv` (RFC 4180, formula cells refused).
- `src/fixtures/ext/hall-seed.ts` — templates General / Wedding / Corporate, ten headings, every text `OWNER TO REPLACE`. `hallTemplates(x)` returns the seed while `s.hallX.templates` is empty (H1's `hallSeed()` untouched); the first template save materialises them.
- `src/services/mock/handlers/hall-quote.ts` (spread in the F6-H2 block of `handlers/index.ts`; exports in `mock/index.ts` F6-H2 block):
  `QUO_CREATE`, `QUO_EDIT` (draft only), `QUO_REVISE` (v+1, SENT → SUPERSEDED, EXPIRED stays), `QUO_SEND` (discount limit → `HCC_DISCOUNT_LIMIT` unless Owner-approved; placeholder → `HCC_TNC_PLACEHOLDER` unless overridden; snapshot re-taken and locked; validity 15 days; enquiry → QUOTED; outbox held), `QUO_PLACEHOLDER_OVERRIDE` (Owner, reason ≥ 5, once per quotation, carried to later versions), `QUO_DISCOUNT_APPROVE` (Owner, not the author; cleared when lines / header change), `QUO_LOST` (listed reason, Other needs a note; enquiry → LOST), `QUO_CANCEL` (draft; number kept), `QUO_EXPIRE` (night run, Manager / Owner), `HALL_TNC_TEMPLATE_SAVE` (Owner only → `HCC_TNC_OWNER_ONLY`; new version, snapshots never touched). Advance plan: Manager / Owner may set rows; Reception the default (`[]` → H1's 25 / 50 / balance at accept).
- `QUO_ACCEPT` is H1's (already on the branch); the H2 golden test drives create → revise → 5 % → send → accept and checks the event + plan.
- Tests: `tests/stream-hall-quote.mjs` (12: numbering + FY reset + cancel keeps number, default lines / master list rate, discount limits 0 / 10 / 11 % / Owner any / no self-approval / approval cleared on edit, placeholder guard + override + once, template Owner-only + snapshot unchanged, revise keeps number + NOT_LATEST, expiry + reminder, lost / cancel, golden accept + print = screen, advance plan), `tests/stream-hall-seed.mjs` (4: seed shape, both CSVs = seed and round-trip, import errors).
- `08 Production/deployment/import-templates/halls.csv`, `tnc-templates.csv` (top level, not in `templates/` — the [MIO] header-sync test lists that folder).

## Server (`08 Production`)
- `Hcc.Domain/Hall/HallQuoteRules.cs` — FY code, format, roles, status machine, expiry / reminder, template in force + snapshot, placeholder, clause validation, default lost reasons and placeholder General template.
- `Hcc.Application/Hall/HallQuoteCommands.cs` + port `IHallQuoteStore` — Create / Edit / Revise / Send / OverrideTncPlaceholder / ApproveQuotationDiscount / LoseQuotation / CancelQuotation / ExpireQuotations / SaveTncTemplate; money via H1's `HallBilling` (Price, DiscountWithin / DiscountBps / DiscountLimitBps); audit on every command.
- `Hcc.Infrastructure.SqlServer/Hall/SqlHallQuoteStore.cs` — gap-free QUO number under `sp_getapplock` (prefix from the `QUO` row of `bill.number_series`, else `QT`; frozen per FY in `hall.quote_series`).
- `Hcc.Api/Endpoints/HallQuoteEndpoints.cs` — `POST /hall/quotations`, `/hall/quotations/{id}/edit|revise|send|override|approve-discount|lost|cancel`, `/hall/quotations/expire`, `/hall/templates` + `/commands/{op}`; registered in the F6-H2 blocks of `HccComposition.cs`.
- Migrations (locked with `--add --accept-out-of-order`, listed in `Hcc.slnx` F6-H2 block): 4330 QUO series (number-series CHECK + `hall.quote_series`), 4331 T&C templates / versions / clauses, 4332 version snapshot table + tnc / approval / lost columns (approval ≠ author CHECK), 4333 menu packages (+ items), 4334 lost reasons, 4349 seed (per property with no halls: 2 halls + 1 lawn with distinct fictional names, lost reasons, placeholder templates, 3 menus).
- Tests (`Hcc.Hall.Tests/Quote/`): `QuoteRulesTests` (11) + `HallQuoteSqlTests` (3, SQL: numbering + FY reset + revise + cancel, discounts + approval + placeholder guard / override + audit, snapshot unchanged after template save + Owner-only templates + accept → event + plan + expiry + lost). Whole project 23/23 green; Platform migration tests 16/16; build 0 warnings.

## Notes for the coordinator
- **Number series master (prototype):** `QUO` is not added to `FDM_DOCS` / `SEED_NUMBER_SERIES` (shared masters with row-count tests); the prototype numbers from the quotations themselves (`hallQuoNext`). Server reads a `QUO` row of `bill.number_series` (4330 adds one where the list is stored); the server's front-desk masters code does not yet offer `QUO` in its document list.
- **Discount approval** is a version flag (`approvalId` `QDA|by|at|bps` in the prototype; `approval_by/_utc/_bps` on the server), not the folio APPROVAL queue.
- Removed a draft constraint "SENT ⇒ sent_utc" from 4332 because H1's SQL fixture inserts SENT versions directly.
- Seed menus in 4349 are package headers only (POS item links are H1's master command).
- Not done (other lanes): reads / reports / RBAC seed 4345–4348 (H3), UI.

## Full prototype run (`npm test`, 3 Nov 2026)
`tsc --noEmit` clean. All H2 tests pass (`stream-hall-quote` 12, `stream-hall-seed` 4) and H1's `stream-hall` / `stream-hall-billing` still pass.
Four failures, none in H2 files — for H1 / the coordinator:
- `[MNY]` and `[MFX]` "no code reads the legacy payment constants": `handlers/hall.ts` (H1) has its own `TENDERS=['Cash','UPI','Card','Bank transfer']` list — should read the payment-methods master.
- `[MFX]` FY26 whole-state hash and `[HIST-A]` "loads with the current schema" (14 !== 13): expectations not yet moved to SCHEMA 14 by the setup commit.
