# Wave 0.5 — where SCC and ETP features go in the HCC UI (proposal)

24 Oct 2026 · Status: **APPROVED by the owner 24 Oct 2026 (APR-006, DEC-055…058)** · Sources: `HCC_FirstParty_Reuse_ETP_SCC_v1.md` §3a, current navigation `03 Prototype/src/app/navmap.ts` (10 categories), SCC modules `payroll`, `leave`, `tax`, `expense`. Mockups were shown in chat (24 Oct). Keeps the current UI: Modules Home tiles → category page → page with tabs on desktop, `ChipFilter` sections and cards on phone, Maroon & Sand theme, 48 px phone targets.

## 1. Placement

| Feature (source) | Where it lives | New page or tab | Who sees it (existing permissions) |
|---|---|---|---|
| Payroll with PF / ESIC / PT, lock pay-run, bank CSV, payslips (PDF, WhatsApp simulated), full and final (SCC payroll) | People and payroll › **Staff and payroll** › Payroll tab (upgraded) | Existing tab; "Rates" tab renamed **Statutory rates** (dated rates, CA to confirm) | Owner, Accountant (`hrCanPay`) — unchanged |
| Leave: types, entitlements, half-day, weekly off, max staff off per department per day, approvals, leave calendar (SCC leave) | People and payroll › Staff and payroll › **Leave** tab (upgraded); **My shifts and pay** shows balance + apply | Existing tab / page | Managers approve (`approve`); every staff member sees their own |
| Payslip with deduction breakdown | People and payroll › **My shifts and pay** | Existing page, new payslip card | Each employee — own payslip only; housekeeping/maintenance see only their own pay, no hotel money |
| Compliance calendar: TDS, GSTR-1/3B/9, EPF/ESIC, PT due dates; mark filed / not applicable; send CA pack (simulated); manage obligations (SCC tax) | Accounts and reports › **Compliance calendar** | **New page** `compliance` (category `accounts`) | Owner, Accountant (`reports` + `approve` for managing obligations) |
| "Due this week" reminders | **Dashboard** card + badge on the Accounts and reports tile + Management assistant answer | New card | Owner, Accountant |
| Cash book: opening balance carried forward, front-desk and restaurant receipts, expenses, banking, closing; day locked by a checker; owner reopen (SCC expense ledger + ETP cash book) | Cash and billing › **Cash book** (next to Cashier shift); linked from Day closing | **New page** `cashbook` (category `cash`) | Cashier, Manager, Owner, Accountant (`payments` or `reports`) |
| Excel export (ETP OpenXML style) | Reports, Cash book, Payroll register | Button next to CSV | Same as the page |
| Daily report pack PDF (ETP) | Day closing → after close: "Day pack PDF" + share | Button | Manager, Owner |
| Tally export preview, marked "Simulated" (ETP) | Accounting › **Tally export** tab | New tab | Owner, Accountant |

Navigation changes: `PAGE_CATEGORY` gains `compliance:'accounts'` and `cashbook:'cash'`, with blurbs and search keywords ("GST", "TDS", "PF", "ESIC", "due date", "cash book", "day book", "rokad"). The navmap test makes this mandatory.

## 2. Phone
- Compliance: cards sorted by due date, colour + text status (Overdue / Due in N days / Filed), one "Mark filed" button per card.
- Cash book: one card per day with opening / in / out / closing, then entries as cards.
- My shifts and pay: payslip card + "Payslip PDF" (native share).
- Payroll run and statutory rates: desktop/tablet only. On a phone they show a read-only summary with a note to use the tablet or desktop, as access-matrix editing does today.

## 3. Rules kept
Fictional staff and figures only; statutory rates are dated settings, never constants; WhatsApp/e-mail/Tally are simulated in the prototype; no hotel money for housekeeping/maintenance; every new page is registered for desktop and phone; existing figures and tests unchanged unless a rule change requires it (recomputed with derivation, never weakened).

## 4. Open questions for the owner
1. Cash book under **Cash and billing** (daily use by the cashier) — or under Accounts and reports?
2. Compliance calendar visible to **Owner + Accountant** only, or also the Manager?
3. Payslips: PDF via share sheet only, or also a simulated WhatsApp send as in SCC?

## 5. Master configuration panel
See `HCC_Master_Configuration_Design_v1.md`: one settings hub in Admin and setup (12 groups, search, live summaries, list/details on desktop and phone, modules on/off, dated values, approvals and history). The new payroll, leave, compliance and cash-book settings are registered there rather than on their own pages.
