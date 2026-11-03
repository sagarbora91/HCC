# F7-R8 DONE — built moves (batch 3, prototype side)

Lane R8 · Sonnet · branch `claude/f7-r8` (from `claude/f7` d60996ad). **Prototype only**: no server reports, no migrations (4436–4437 unused).

## Reports moved onto the engine (22, all `built`, one definition each, adapters over the old selectors, figures unchanged)
| Id | File (`03 Prototype/src/domain/reports/built/`) | Wraps | Notes |
|---|---|---|---|
| `rpt.guests.daily-ops` | pack.ts | `reportPack` tab daily | total row = tab tiles |
| `rpt.rooms.occupancy` | pack.ts | tab occupancy | total recomputes ratios; ADR / RevPAR as the old text |
| `rpt.money.receipts-deposits` | pack.ts | tab receipts | one column per tender (dynamic); second block: company credit, deposits held |
| `rpt.money.expenses-cash` | pack.ts | tab expenses | second block: the seven tiles |
| `rpt.money.cash-book` | money.ts | `cbRange` | second block: entries with running balance |
| `rpt.money.closing-records` | money.ts | `s.closings` | PERM-CLS-001, money gate RPT-002; Corrections = number of reopens |
| `rpt.money.cash-out-register` | money.ts | `cxSlice` records | totals per kind exclude Rejected |
| `rpt.money.expense-month` | money.ts | `expMonthSummary` | GST / ITC on the total row only |
| `rpt.rest.day-summary` | rest.ts | `posDaySummary` | 4 blocks |
| `rpt.rest.closed-bills` | rest.ts | settled POS orders | Cashier own settlements (ownShift) |
| `rpt.stock.*` x8 (stock-sheet, reorder, laundry, counts, losses, consumption, par, movements) | stock.ts | inventory selectors | PERM-STK-001 view, STK-004 value (removed without it) |
| `rpt.admin.audit-log` | admin.ts | `avFilter` | detail rule + money mask kept |
| `rpt.admin.export-log` (new) | admin.ts | `s.rptX.exportLog` | Owner and Manager only (checked in the selector, PERM-ADM-004 alone would admit the Accountant) |
| `rpt.admin.report-dictionary`, `rpt.admin.config-history` | admin.ts | `reportDictionary`, `s.configHistory` | |

Permission plumbing: `mask.ts` `// F7-R8` block adds the server grants (migration 3700 seed) of PERM-STK-001/004, POS-004 (Cashier scoped S), CFG-001 that the prototype v1 matrix lacks. Follows the seed, never wider (CFG-001 = Owner, Tech admin; Maintenance has no stock view in the seed).

## Tests
`tests/stream-rpt-built.mjs` 12 tests (ties to old selectors for both ACC-RULEBOOK April and FY26 October, roles, money removal, export-log via `RPT_LOG`, CSV neutralisation, three-way for all 22). 22 golden files added in `tests/rpt-golden/goldens/` (other lanes' goldens unchanged).

## Not moved (stay on the old path) — listed for the next pass
- **Closing-pack renderers**: `rpt.money.revenue-tax`, `daily-collections` (R4's), `closing-report`, `day-pack` (document layouts; need R7-style layouts).
- **Guests**: arrivals-list, inhouse-list (`stayListRows`, page-permission desk rule), groups-pickup, group-master-folio.
- **Rooms**: maintenance-report, handover-dashboard. **Money**: settles-to, udhaar-recurring-petty, vendor-ageing, ota-commission, ota-payouts, plans-extras.
- **People (all 9)**: payroll-register, statutory, bank-file, payslip, roster, staff-list, advances-fnf, statutory-rates, my-hr (HR permission codes have no prototype grants yet; need the same v2 block).
- **Admin**: system-health, backup-status, legacy-import (server-only data). **Tax**: compliance-register, compliance-dashboard, gstr1, gst-month, tds-deductee (R5/R6 area).
- **Server side (Hcc.Reporting)** for all of the above: not done.

## Integration needed (f7-int)
- Regenerate `catalogue.json` (both copies) with `node tools/rpt-catalogue.mjs`; register nothing else (register.ts R8 block has 5 import lines).
- Catalogue §2 statuses: the 22 ids are `built` (export-log was `planned`).
- Entry-point switches: the old Reports page (pack tabs), Cash book page, Audit log tab, POS summary keep their own UI and exports; switching them to `openReport(id)` is f7-int.
- Cashier is not included on cash-book (catalogue lists it): narrower on purpose, the range is property-wide.
- Day-summary: Cashier own-shift not included (whole-outlet figures).
- Occupancy/daily-ops money-free variants (PERM-RPT-001) not built; reports are finance-gated.
