# ACC-3 UI smoke (integrator, 1 Nov 2026)

Build: `03 Prototype/dist/index.html` from `claude/acc3` (integration head), served locally on 127.0.0.1, scenario FY26 (business date 24 Oct 2026), signed in as Sagar · Owner (fictional demo). Checked in the Browser pane with scripted tab clicks.

## Desktop (pane width ~1000 px)
| Tab | Renders | Sideways page scroll | Notes |
|---|---|---|---|
| P&L | yes (ledger view + Tally-group toggle, Export CSV / Excel, provisional note) | no | |
| Balance sheet | yes ("Both sides equal", not-the-statutory-statement note) | no | |
| Outstanding | yes (receivable / payable toggle, party rows with buckets) | no | |
| GST summary | yes (month picker, GSTR-1 / GSTR-3B ties, set-off payable / challan rows) | no | |
| Cash ratio | yes (FY picker, 30.97 % / 36.66 %, flagged) | no | basis points shown as % after the integration fix |
| Exports | yes (range, one CSV / Excel per report, Download all zip) | no | |
| Tally masters panel | yes (FY picker, "0 groups and 119 ledgers (49 parties)", download) | no | above the voucher export |

**Defect found and fixed:** with 20 accounting tabs the tab strip was wider than its card and a tab click scrolled the whole card sideways (the Exports panel opened cut off on the left). Fix: `.acc-tabs{overflow-x:auto;max-width:100%}` appended to `src/styles/m-accounting2.css` (m-accounting.css is frozen by the CT-B round-trip test). After the fix the card never scrolls sideways on any tab.

## Phone (360 × 780 emulated, mobile shell)
Every chip (P&L, Balance sheet, Outstanding, GST summary, Cash ratio, Exports, Tally, Assets, TDS, GST month): no sideways scroll, no tables (cards only), no control under 44 px, no error.

## Carry-over dialogs
| Dialog | Result |
|---|---|
| GST on advances (dated, Owner) — GST month | opens on desktop (560 px) and phone (full 360 px width, no control under 44 px) |
| Asset put to use / disposal — Assets | not reachable in FY26: the scenario has no fixed asset (buttons appear on register rows); covered by the render tests `[ACC3-UI]` carry dialogs in `stream-accounting-ui3.mjs` |
| Vendor payment re-allocation — Expenses › vendors | not opened in the browser (needs a vendor payment with open bills); covered by `[ACC3-UI]` render tests |
| Form 168 figures — TDS remittances | not reachable in FY26: no TDS remittance yet; covered by `[ACC3-UI]` render tests |

Not done here: Android APK and the physical tablet (APK v19 after FIT, per HANDOFF 13m).
