# ACC-2 · UI smoke — accounting tabs (desktop click-through + phone at 360 px)

Integrator, 1 Nov 2026, branch `claude/acc2` after the eight lane merges, the integration checklist and the merge of `main`.
Build: `npm run build` → `03 Prototype/dist/index.html` (3537 KB), served locally with `PORT=4392 node serve.mjs`, opened in the Claude
browser pane. Scenario **FY26** (default), preview **Sagar · Owner**. Fictional data only; everything ran in the demo's own browser storage.

## Desktop (1024 × 768) — every new tab, real handlers (no stubs left)

| tab / action | what was checked | result |
|---|---|---|
| Manual vouchers | template list (Contra, Capital introduced, Owner drawing, Loan received, Loan EMI, Bank charges, Interest income, Bad debt), status filters; **Bank charges** sheet: bank account picker (1030/BANK-1), ₹590, narration → **Post voucher** → toast "Voucher posted (approved — you are the checker)", row MV-1101 24 Oct Payment ₹590 Approved with Void… | OK |
| GST month | September 2026: CGST ₹35,131.75 + SGST ₹35,131.65 output, set-off table by head, blocker "Post the GST set-off for 2026-09 first (rulebook E1)"; **Post set-off** → toast "Set-off posted", voucher dated 2026-10-24; the lock blocker then reads "The pay run for 2026-09 must be paid first (generate its bank file)" (**ruling 6** live) | OK |
| TDS | rate table (rent, contractor, professional, commission; individual / other columns), text "the rate column follows each vendor's TDS payee type … rounded to the nearest rupee" (**ruling 9**) | OK |
| Assets | empty register text "Created automatically when a capital purchase posts", CSV button | OK |
| Chart of accounts | 28 Tally groups, 70 built-ins, **Add loan** button, controls incl. 1210 | OK |
| Parties | grouped by control with balances (1000/TILL ₹23,05,141.80 Dr, 1030/BANK-1 …) | OK |
| Trial balance / Day book | six columns, "Debits = credits in every column"; day book 960 vouchers, ₹26,47,107.70 both sides | OK |
| Console | no errors | OK |

## Phone (360 × 780, mobile emulation) — every view chip

Measured by script on each view: page `scrollWidth`, elements past the right edge outside a horizontal scroller, `<table>` in main,
targets under 44 px, inputs under 16 px.

| view | scrollWidth | overflow | tables | small targets | inputs < 16 px |
|---|---|---|---|---|---|
| Day | 360 | none | 0 | none | 0 |
| Manual vouchers | 360 | none | 0 | none | 0 |
| GST month | 360 | none | 0 | none | 0 |
| TDS | 360 | none | 0 | none | 0 |
| Assets | 360 | none | 0 | none | 0 |
| Trial balance | 360 | none | 0 | none | 0 |
| Statements | 360 | none | 0 | search input 44 px inside a 48 px label (the label is the target, as ACC-1) | 0 |
| Day book | 360 | none | 0 | none | 0 |
| Chart | 360 | none | 0 | search input 44 px in a 48 px label; checkbox 24 px in a 48 px label row | 0 |
| Parties | 360 | none | 0 | search input 44 px in a 48 px label | 0 |
| Opening (pending) | 360 | none | 0 | none | 0 |

No fixes needed (no CSS or colour change; themes.css untouched). No console errors.

Not covered here: the Expenses (vendor payment / return), Channels (OTA payout) and check-out (OTA credit, deposit apply) sheets were
covered by the UI lane's phone smoke and by the stream / sync tests (`stream-accounting-ui2.mjs`, `sync-*.mjs`), not clicked again; the
production build against a live server (sync tests + `Hcc.Integration.Tests` cover the wire); a physical phone.
