# ACC-1 · UI smoke — accounting tabs (desktop click-through + phone at 360 px)

Integrator, 27 Oct 2026, branch `claude/acc1` after the seven lane merges and the integration checklist (stubs deleted, real selectors).
Build: `npm run build` → `03 Prototype/dist/index.html` (3310 KB), served locally with `PORT=4391 node serve.mjs`, opened in the Claude
browser pane. Scenario **FY26** (default), signed in as **Sagar · Owner**. Fictional data only; everything below ran in the demo's own
browser storage (nothing left the machine).

## Desktop (1366 × 900) — every new tab clicked

| tab | what was checked | result |
|---|---|---|
| Vouchers | 13 vouchers of 24 Oct, Dr = Cr ₹24,539 | OK |
| Trial balance | six columns (opening / period / closing Dr · Cr), "This month" 1–24 Oct, groups shown by Tally name (Cash-in-Hand, Bank Accounts, Sundry Debtors, Duties & Taxes, Sales Accounts…), "Debits = credits in every column" pill, "Opening balances pending" note; **With parties** shows `1000/TILL · Cash in hand · Front-desk cash drawer`, `1030/BANK-1 · Bank accounts · Hotel current account` under their controls | OK |
| Statements | row 1110 in the TB opens the statement of 1110 (all parties): opening —, five company-credit journals 7–14 Oct with counter ledger "Guest ledger (in-house folios)", running balance in Dr (₹18,088.25 Dr), CSV button; voucher link opens the `acc-voucher` dialog (JRN/20261007/001, balanced, source record) | OK |
| Day book | 960 vouchers 1–24 Oct, debits = credits ₹26,47,107.70, one row per entry with ledger names incl. parties, `date, no` order | OK |
| Chart of accounts | 28 Tally groups as a tree, 70 built-in ledgers (Active 70), control badges on 1000 / 1030 …; **Owner add** 5130 "Guest amenities (fictional)" under Indirect Expenses → Active 71, toast "Ledger saved"; **retire** 5130 → Retired (1), toast "Ledger retired"; **reactivate** from the Retired filter → Active 71, toast "Ledger reactivated" (payloads: `create:true`, RETIRE `{code}`, UPSERT `{code, active:true}`) | OK |
| Parties | grouped by control with balances (1000 · Cash in hand: 1000/TILL Front-desk cash drawer ₹23,05,141.80 Dr; 1030 · Bank accounts: 1030/BANK-1 …; 1110 …), Statement buttons | OK |
| Opening balances | before: "Not entered" + "Pending — locks not gated until an entry exists" (ruling 5); added 1500 Buildings Dr ₹1,00,000 → **Save draft**: 1400 Opening-balance difference Cr ₹1,00,000 added automatically, pill "Pending — period locks from 1 Apr refused"; **Approve** (Owner) → "Approved" + still pending (1400 ≠ 0) | OK |
| Console | no errors | OK |

## Phone (360 × 780, mobile emulation) — every view chip

Measured on each view with a script: page `scrollWidth` vs viewport, elements past the right edge outside a horizontal scroller, targets
under 44 px, inputs under 16 px, `<table>` count.

| view | scrollWidth | overflow | tables | small targets | inputs < 16 px |
|---|---|---|---|---|---|
| Day | 360 | none | 0 | none | 0 |
| Trial balance | 360 | none | 0 | **Control ledgers / With parties 44 px** → fixed (below) → 48 px | 0 |
| Statements | 360 | none | 0 | search input 44 px inside a 48 px label (the label is the target) | 0 |
| Day book | 360 | none | 0 | none | 0 |
| Chart | 360 | none | 0 | search input 44 px in a 48 px label; checkbox 24 px inside a 48 px label row | 0 |
| Parties | 360 | none | 0 | search input 44 px in a 48 px label | 0 |
| Opening (pending) | 360 | none | 0 | none | 0 |
| Tally export (Simulated) | 360 | none | 0 | none | 0 |

Sheets: **Add ledger** opens full width (0–360 px), every field 16 px, sticky Cancel / Save ledger; **Reopen** (opening entry) textarea
16 px, Cancel / Reopen 48 px. Cards, not tables; one primary action in the sticky footer (Add ledger, Reopen). No console errors.

**Fix made:** `src/styles/m-accounting2.css` — `.acc-m .acc-subbar .filters button{min-height:48px;font-size:16px}` (the trial-balance
detail toggle measured 44 px); re-measured 48 px after `npm run build`. Tokens only, no colour change (themes.css untouched).

Not covered here: the production build against a live server (the sync tests and `Hcc.Integration.Tests` cover the wire), a physical phone.
