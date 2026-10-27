# SCC port wave — questions for the owner (27 Oct 2026)

Leave, the compliance (tax) calendar and expenses now follow SCC in full (DEC-116, DEC-119, DEC-120). The three agents raised 54 points. Everything is **built as SCC does it** today, except where a line says HCC keeps a safeguard. Nothing below changes unless the owner says so.

Detail: `SCC_Leave_Port_2026-10-27.md` (L), `SCC_Tax_Port_2026-10-27.md` (T), `SCC_Expense_Port_2026-10-27.md` (E), in this folder.

## A. Needs the owner's decision (recommendation in brackets)

| # | Question | As built | Recommendation |
|---|---|---|---|
| A1 (L-Q1) | Fixed week-off days for leave. SCC skips Saturday and Sunday when leave is requested. | No day ticked (a 7-day hotel; each person's own weekly off is used for the roster only) | Keep "none" |
| A2 (L-Q4) | Several pending leave requests can together go over a person's quota, and all can be approved (SCC re-checks nothing at approval). | As SCC | Keep SCC, or show the quota warning again at approval |
| A3 (L-Q13) | SCC's calendar cannot go back before May 2026, so April 2026 is hidden on the calendar (the report and pay still have it). | As SCC | Allow April 2026 |
| A4 (L-Q18) | Who approves leave: the Owner and Manager-role users. | Owner + Managers | Confirm |
| A5 (T-Q5) | The compliance FY never moves by itself: on 1 Apr 2027 it still shows 2026–27 until someone changes it. | As SCC | Switch to the new FY automatically on 1 April |
| A6 (T-Q9) | Estimated PF / ESIC due includes the employer's share (that is what is deposited); GST due = GST on invoices issued, no input credit taken off. | As described | Keep |
| A7 (E-Q1) | Salaries are not copied into expenses (payroll already books them; SCC would count them twice). | Kept out | Keep out |
| A8 (E-Q3) | Totals, budgets and petty-cash balance count an expense only after it is approved (SCC counts pending ones at once). | Approved only | Keep |
| A9 (E-Q5) | Who sets monthly budgets and recurring expenses. | Manager, Accountant, Owner | Owner only (money settings are Owner-only, DEC-058) |
| A10 (E-Q7) | Udhaar (credit) cash collected from a customer never reaches the drawer, so a cashier who puts it in the drawer counts over. | As SCC (recorded against the credit only) | Take Udhaar cash into the drawer as a desk receipt |
| A11 (E-Q9) | A locked expense month cannot be unlocked (SCC has no unlock). | No unlock; the Owner can still post into it with a reason | Add an Owner unlock with a reason |
| A12 (E-Q13) | A new vendor name typed on an expense joins the vendor list automatically, though the vendor list was Owner-only. | Auto-add (SCC); the Owner can retire names | Keep auto-add |

## B. For the CA

- T-Q11: SCC's compliance texts are written for Maharashtra retail (ESIC half-yearly return, Shops & Establishment, PT). Which apply to a hotel, and are hotel licences missing?
- T-Q14: one firm (one GSTIN, one PAN) per property — enough?
- E-Q14: input GST on expense bills is not split into a GST-input ledger in Accounting.
- Earlier item G9 (inter-state company guests: CGST + SGST, per-line rounding) is still open.

## C. Built as SCC — tell me only if you want a change

Leave: SCC's 4-a-shift limit never applies because the 3-a-day limit comes first (L-Q2); store holidays show a "discouraged" warning (L-Q3); the over-quota warning can count dates that were later skipped (L-Q5); the import refuses AM + PM of one person on one date (L-Q8); the backup leaves out caps and blackouts (L-Q9); the 7-day strip counts records, not people (L-Q11); a request across 31 March is checked against the first year only (L-Q12); SCC's "decide within 7 days" note is not shown (L-Q14); an empty entitlement saves 0 (L-Q15); SCC's "store" is HCC's department, caps only warn (L-Q17).

Leave — HCC safeguards kept (DEC-119): nobody approves their own leave (L-Q6); removing leave cancels it and keeps a record, never a hard delete (L-Q7); staff are matched by ID, not name (L-Q10); inactive staff are refused; attendance and approved leave cannot overlap; past days go through attendance corrections.

Compliance calendar: no QRMP Q4 cards and no PMT-06 card (T-Q1, only matters under QRMP); two EPF/ESIC and LWF names archive separately (T-Q2); a custom due day 29–31 rolls into the next month in short months, with a warning (T-Q3); the 2025 Income-tax Act renames also change last year's texts (T-Q4); the "Escalate" tag also shows on filed cards (T-Q6); risk level comes from words in the name (T-Q7); GSTR-4 sits on April's page though due 30 June (T-Q8, composition dealers only); overdue days in the CSV start with an apostrophe (T-Q10); editing a custom obligation changes every month (T-Q13); the Shops & Establishment text mentions moving a built-in card, which is not possible (T-Q15); nothing can be recorded until the Owner fills the firm profile (T-Q16); old FY 2025–26 filings are kept read-only (T-Q17); SCC's past-date view, reports feed and reminders are not in HCC (T-Q18). HCC extras kept (T-Q12): "Due this week" card, CA pack by e-mail, file-size limits.

Expenses: HCC's cashier rules (denominations, ₹1 tolerance) unchanged (E-Q2); exactly 100 % of a budget shows "near limit" in the table but an "over" alert (E-Q4); a petty-cash payout may go past the float (E-Q6); write-off by Manager, Accountant or Owner (E-Q8); expenses only on the business date or an Owner-reopened day, never a future date (E-Q10); only Owner, Manager or Accountant void a pending expense, the maker their own draft (E-Q11); whoever edits someone else's expense cannot approve it (E-Q12); a recurring expense is posted as a draft to submit (E-Q15); the CSV adds HCC's Ref and Status columns (E-Q16); a bill photo over 4 MB is refused on the form (E-Q17); SCC's manual income entries, firms, store tags and stock/service feeds are not added — HCC books income from folios and the restaurant (E-Q18).
