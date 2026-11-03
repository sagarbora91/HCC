# HCC Module 27 — Hall booking with quotations and T&C (spec for owner approval)

Status: **DRAFT for owner approval (OP-17 / A1)** · 3 Nov 2026 · wave F6 (`claude/f6`) · migrations 4300–4349 · SCHEMA 13 → 14.
Sources: Wave Plan F1–Fn §F6 and Appendix D (A26), Pending Inventory §3.2, Build Plan v2 W5 (DEC-151), DEC-142 / 144 / 063 / 197 / 198, F4 tariff-card rulings DEC-219…222, Accounting Rulebook chart.
Technical detail for the build lanes is in `02 Planning/agents/F6-CONTRACT.md`. This file is the plain-English version you approve.

---

## 1. What this module does (in one paragraph)

Today hall business runs on paper. HCC will keep it in one place:
a customer **asks** (enquiry) → we send a numbered **quotation** with prices, GST, an advance plan and our T&C →
we **revise** it if they bargain (v1, v2 …) → they **accept** and pay an **advance** → the event goes on the **calendar** →
on the day we bill **actual plates and extras** → HCC prints a **tax invoice** in the hotel series `HPE/…` with the advance taken off →
the entries go to the **books** automatically. If they cancel, the cancellation rules in the T&C decide what is kept and what is refunded.

## 2. Scope

| In scope | Not in this wave |
|---|---|
| Halls master (halls, lawn, capacities, setups, rates) | Online booking of halls on the website |
| Enquiries with follow-up dates and lost reasons | Payment links (F9, after a payment provider is chosen) |
| Quotations: numbered, versioned, validity, T&C, advance plan | Real e-mail / WhatsApp sending (held in the outbox until F9) |
| Menus per plate from the POS menu master | Kitchen stock deduction per plate (stock module, later) |
| Acceptance → event, optional room block, advance receipt | Decorator / DJ vendor management |
| Calendar; no double booking of a hall | PDF from the report engine (F7) — HTML print until then |
| Event-day bill → `HPE` invoice → accounting | Split billing of one event to two payers (SF wave) |
| Cancellations, refunds, credit notes | |
| Phone screens, reports, print (quotation, function sheet) | |

## 3. Words used

| Word | Meaning |
|---|---|
| **Hall** | A bookable space: hall, lawn, terrace. "Venue" in the code. |
| **Slot** | Part of a day: Morning, Afternoon, Evening, Full day. Each hall sets its slot times. |
| **Enquiry** | A customer asking. No price, no number yet. |
| **Quotation** | A numbered price offer `QT/2627/0001`. Has versions v1, v2 … |
| **Event** | An accepted quotation. Holds the hall on the calendar. |
| **Advance plan** | When the customer pays: default 25 % / 50 % / balance. |
| **T&C template** | Numbered clauses per event type, printed on every quotation. |
| **Function sheet (BEO)** | One page for kitchen, housekeeping and decor: timings, menu, setup. |

## 4. Halls master (Owner)

Lives in Master configuration with the other masters (same CSV import / export as every master).

| Field | Example | Notes |
|---|---|---|
| Name | Sagar Hall | Fictional in the prototype |
| Kind | Hall / Lawn / Terrace | |
| Capacity per setup | Theatre 200 · Round tables 150 · U-shape 60 · Floating 250 | Quotation warns above capacity; Manager can override with a reason |
| Slots and times | Morning 08–13 · Evening 18–23 · Full day | Set per hall |
| Rate per slot | ₹15,000 evening · ₹25,000 full day | Prices **before** GST on this master; the tariff card (F4) shows them in its Halls tab |
| Minimum plates | 100 for the evening slot | Optional; quotation warns below it |
| GST class | "Hall rent" | A class name, not a rate; the rate comes from the dated GST master (§9) |
| Combined with | Sagar Hall + Lawn | Optional combined booking; booking the combination blocks both |
| Active | yes / no | Inactive halls cannot be quoted; history stays |

Prototype seed: **2 halls + 1 lawn**, fictional names and prices.

## 5. Enquiry

Anyone at the desk (Reception, Manager, Owner) can record one in under a minute.

| Field | Required |
|---|---|
| Customer name, phone | yes (phone is checked against Customers 360; existing customer is linked) |
| Company (optional) | links to a party account if it exists |
| Event type | Wedding, Reception, Birthday, Engagement, Corporate meeting, Conference, Religious, Other |
| Date(s), slot, expected pax | yes |
| Hall preference | optional |
| Source | Walk-in, Phone, Reference, Website, Repeat |
| Follow-up date and note | yes — shows in "Follow-ups today" |

**Status:** Open → Quoted → Won / Lost. Lost needs a **lost reason** from a list (A26 default: yes):
Price too high · Date not available · Chose another venue · Event cancelled · No response · Other (note needed).
No reply for 30 days after the follow-up date → the enquiry moves itself to Lost / "No response" (the reminder warns 3 days earlier).

## 6. Quotation

### 6.1 Numbering
- Series **`QT/2627/0001`**, a new doc type `QUO` in Master configuration › Number series.
- Restarts every **1 April** (`QT/2728/0001` next year). Gaps are not allowed; a cancelled quotation keeps its number.
- **Versions keep the same number**: `QT/2627/0007 v2`. Only the latest version can be sent or accepted. Older versions are read-only and printable.

### 6.2 What is on a quotation

| Part | Content |
|---|---|
| Header | Customer, company, event type, date(s), slot, hall, setup, expected pax, guaranteed pax |
| Lines | One line per item: hall rent, menu (per plate × plates), decor, sound, extra hours, rooms, other |
| Each line | Kind · description · quantity · unit · rate · discount · taxable value · GST class · GST % · CGST · SGST · total |
| Totals | Taxable, CGST, SGST, grand total; rounding shown |
| Advance plan | Rows with % or amount and due date (§6.4) |
| T&C | The template for the event type, printed as numbered clauses (§7) |
| Validity | **15 days** by default; the expiry date is printed |
| Signature block | "Accepted by" name, date, signature |

Rates come from: the hall master (hall rent), the menu package (per plate), the extras master (decor, sound), the tariff card (rooms).
The person may change a rate; a lower rate counts as a **discount** (§8).

### 6.3 Status

```
Draft → Sent → Accepted → (event created)
  │       │  ↘ Revised (new version, old one Superseded)
  │       ├→ Expired (validity passed; can be revised into a new version)
  │       └→ Lost (reason from the list)
  └→ Cancelled (before sending)
```
- **Send** = print, or "Send" to the outbox (held until F9). Sending locks that version.
- **Revise** makes v(n+1) as a copy; you edit it; v(n) becomes Superseded.
- **Expiry:** 2 days before expiry a reminder appears for the person who sent it; on expiry the status is Expired.
- **Accept** is allowed only on the latest Sent version that has not expired (Manager may accept an expired one with a reason).

### 6.4 Advance plan (A26 default)

| Row | Default | Due |
|---|---|---|
| 1 | **25 %** | on acceptance (booking) |
| 2 | **50 %** | 30 days before the event (or at once if the event is nearer) |
| 3 | **Balance** | on the event day, with the final bill |

- Percentages are of the quotation **grand total**. The plan is editable per quotation (Manager and Owner; Reception only with the default).
- Advances are **not income** and carry **no GST** (DEC-142: Dr cash/bank · Cr 2000 Advance deposits).
- A reminder shows instalments due in the next 7 days and overdue ones.

## 7. T&C templates (Owner)

- One template **per event type** (one "General" template is used where none exists).
- Numbered clauses; each template has versions with an effective date. The quotation keeps a **copy (snapshot)** of the clauses it was sent with — later edits never change a sent quotation.
- **Only the Owner edits templates.** Reception and Manager can only pick and preview.
- Clause headings seeded: advance and payment; cancellation slabs; guaranteed pax and extra plates; timings and overtime; damages; outside caterers and decorators; music and noise limits; liquor and licences (customer's responsibility); GST note; force majeure.
- **Until your paper T&C arrive**, every clause text is **"OWNER TO REPLACE"**.
  **Guard:** a quotation whose T&C still contain a placeholder **cannot be sent or printed as final**. The Owner may override once per quotation with a reason (logged). Drafts can still be printed, marked "DRAFT — NOT AN OFFER".

## 8. Discounts

| Role | Hall discount limit (A26 default) |
|---|---|
| Reception | **0 %** — can quote list rates only |
| Manager | up to **10 %** of the quotation before GST |
| Owner | any |

- Above the limit the quotation goes to an **approval** (Owner). No self-approval (as everywhere in HCC).
- Discount is shown on each line and reduces the taxable value (Accounting 4900 shows pre-supply discounts as today).
- The same limits apply on the event-day bill.

## 9. GST (CA to confirm)

- **Itemised per line** (A26 default), not one package rate. Each line has a **GST class**; the rate comes from the **dated GST master** (DEC-069), so a CA answer is a master edit, not a code change.
- Starting classes (illustrative, **CA to confirm**):

| Class | Starting rate |
|---|---|
| Hall rent | 18 % |
| Banquet catering (food in the hall) | 18 % (CA to confirm 5 % vs 18 % for outdoor/event catering) |
| Decor, sound, other services | 18 % |
| Rooms on the event | room rate per the room master (room GST 5 % ≤ ₹7,500 per the CA, DEC-239; banquet food / hall 18 %) |

- CGST + SGST on every line, rounded half away from zero (always; DEC-136, place of supply = the hotel).
- The rate is taken at the **invoice date**; the quotation shows the rate on its own date and says "GST at the rate on the invoice date".

## 10. Acceptance → event

On **Accept** HCC does, in one step:
1. Creates the **event** (copy of the accepted lines, pax, timings, menu, T&C snapshot).
2. Holds the hall on the **calendar** for the date and slot. **A clash is refused** (§11).
3. Optionally blocks **rooms** for guests (a group block on the room board; same rules as group bookings).
4. Records the **first advance** (any tender: cash, UPI, card, bank; or company credit) — or leaves it due if not paid yet.
5. Prints an advance receipt (no GST on it, DEC-142).

## 11. Calendar and availability

- Month / week / day views per hall; colour plus text label (Enquiry hold · Confirmed · Event day · Closed).
- **A hall cannot be double-booked** for an overlapping slot. A combined booking blocks every hall in it.
- An enquiry may place a **soft hold** (Manager, up to 3 days); a soft hold warns but does not block. Acceptance turns it hard.
- Maintenance downtime on a hall blocks it like an event.
- The calendar links to the room board for the same dates.

## 12. Menus

- Menus are **packages per plate** built from the **POS menu master** items (A26 default), e.g. "Silver Veg ₹650 / plate: 2 starters, 4 mains, 2 desserts".
- The package price is set on the package; items are the list for the kitchen.
- A quotation may use a package and add or remove items (price changes are a discount or an extra line).
- The function sheet prints the final menu for the kitchen.

## 13. Event day and the bill

- The **event-day screen** shows the function sheet, the advance paid and the balance.
- Staff enter **actual plates** (bill = the **higher of guaranteed and actual** pax), extra hours, extra items, damages.
- **Close the event** → HCC makes the **tax invoice** in the hotel series **`HPE/2627/…`** (DEC-197), lists every line with GST, then **advances received** and **balance due**.
- Balance is paid by any tender, by **company credit** (party account 1110/[company]), or as **Udhaar** (DEC-144).
- The invoice cannot be edited after printing; corrections are credit notes (as for room invoices).

## 14. Accounting

New ledger **4400 Banquet & events revenue** (Sales Accounts) is added to the chart.

| Event | Entry |
|---|---|
| Advance received | Dr tender (1000 / 1010 / 1020 / 1030 bank) · Cr **2000** Advance deposits — no GST (DEC-142) |
| Invoice on the event day | Dr 1100 (or 1110/[company]) · Cr **4400** (hall, decor, services) · Cr **4200** F&B (food) · Cr 2110 CGST · Cr 2120 SGST |
| Advance applied | Dr **2000** · Cr 1100 |
| Balance paid | Dr tender · Cr 1100 (or 1110/[company]) |
| Discount on the invoice | shown on the line (4900 as today) |
| Cancellation charge kept | Dr 2000 · Cr **4300** · Cr 2110 · Cr 2120 (GST at the rate of the main supply, Rulebook §4.9) |
| Refund | Dr 2000 · Cr tender |

Food in the hall goes to 4200 so the P&L keeps food apart; if you prefer all hall income in 4400, it is one mapping switch (question Q6).

## 15. Cancellations and refunds

Cancellation slabs come from the T&C template (placeholder until your paper). Default slabs used by the guard and the calculator:

| Cancelled | Kept from advances |
|---|---|
| more than 60 days before | 0 % (full refund) |
| 30–60 days before | 25 % of the quotation total |
| 7–29 days before | 50 % |
| under 7 days / no-show | 100 % of advances paid |

- The kept amount becomes a **cancellation charge** (4300, with GST). The rest is refunded (Manager approves; Owner above ₹50,000).
- The hall is released from the calendar at once; blocked rooms are released.
- Reducing guaranteed pax after acceptance: allowed up to 10 % without charge, below that the T&C attrition clause applies.

## 16. Who can do what

| Action | Owner | Manager | Reception | Accountant | Cashier | HK / Maint. |
|---|---|---|---|---|---|---|
| See hall calendar (no money) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ (event list and setup only, **no prices**) |
| Enquiries: add, follow up, mark lost | ✓ | ✓ | ✓ | — | — | — |
| Quotation: create, revise, send | ✓ | ✓ | ✓ (list rates, default advance plan) | view | — | — |
| Discount | any | ≤ 10 % | 0 % | — | — | — |
| Accept quotation, take advance | ✓ | ✓ | ✓ | — | advance only | — |
| Soft hold on a hall | ✓ | ✓ | — | — | — | — |
| Event-day bill and invoice | ✓ | ✓ | ✓ | view | settle | — |
| Cancel event, approve refund | ✓ | ✓ (≤ ₹50,000) | request | view | — | — |
| Halls master, menus | ✓ | menus | — | — | — | — |
| T&C templates, placeholder override | ✓ | — | — | — | — | — |
| Hall reports | ✓ | ✓ | counts only | ✓ | — | — |

Housekeeping and maintenance see the **function sheet without prices** (DEC-061).
Permissions `PERM-HAL-001…006` (contract §4). The module can be switched off on the server (Wave C) if you approve with reservations.

## 17. Screens

Page **Hall booking** under REVENUE & SALES, tabs:
**Enquiries · Quotations · Calendar · Events · Menus · Templates (Owner)**.
- Enquiries: board by status, "Follow-ups today" on top.
- Quotation editor: header, lines, totals, advance plan, T&C preview, version strip v1 v2 …, status pill; buttons Send / Revise / Accept / Mark lost / Print.
- Event day: function sheet, actual pax, extras, Close & bill.

**Phone** (48 px buttons, cards, no sideways scroll): enquiry list + add sheet, quotation summary with Send / Accept, calendar day list, event-day card. Editing quotation lines is desktop-only.

## 18. Prints and reports

| Print / report | Who | Notes |
|---|---|---|
| Quotation (A4) | desk | letterhead, lines, GST summary, advance plan, T&C, validity, signature block; **printed totals = screen totals** |
| Advance receipt | desk | no GST |
| Function sheet (BEO) | desk, kitchen, HK | one page, no prices for HK / kitchen |
| Tax invoice `HPE/…` | desk | same layout as room invoices |
| Quotation register | Owner, Manager, Accountant | number, version, customer, value, status |
| Enquiry funnel | Owner, Manager | enquiries → quoted → won / lost, lost reasons |
| Hall events | Owner, Manager, Accountant | by date, revenue, advances, balance |
| Hall occupancy | Owner, Manager | slots booked per hall per month |
| Advances due / overdue | Owner, Manager, Accountant | |

HTML print now; the report engine (F7) adds Excel / PDF with the same figures.

## 19. Data kept (summary)

Halls · enquiries · quotations (one header per number) · quotation versions · lines · advance rows · T&C snapshot per version · T&C templates and versions · menu packages · events · event bills → invoices · lost reasons · print log. Everything has an audit trail; nothing is deleted (cancel, never delete).

## 20. Server tables and migrations

| Migrations | What |
|---|---|
| 4300–4319 | hall schema: venues, enquiries, quotations, versions, lines, events, calendar holds |
| 4320–4329 | billing: advance rows, event bills, accounting sources, ledger 4400 |
| 4330–4344 | QT number series, T&C templates and versions, quotation T&C snapshot, menu packages, lost reasons |
| 4345–4347 | permissions seed, indexes |
| 4348 | print log |
| 4349 | seed (halls, templates with placeholders, lost reasons) |

Each new migration is added to `migrations.lock.json` with `tools/migrations-lock.mjs --add`.

## 21. Tests (what proves it works)

**Golden event (both prototype and server):**
enquiry → quotation v1 (Sagar Hall evening + 150 plates Silver Veg + decor) → v2 with 5 % Manager discount → send → accept → ₹20,000 UPI advance → event day 160 actual plates → invoice `HPE/2627/…` with advance taken off → balance by company credit → ledgers: 2000 Dr ₹20,000, 4400 / 4200 Cr, CGST + SGST, 1110/[company] Dr balance.

Other checks:
- Reception cannot give any discount; Manager 10 % passes, 11 % goes to approval; no self-approval.
- A placeholder T&C blocks Send; Owner override works and is logged.
- Reception and Manager cannot edit a template; a sent quotation keeps its old clauses after an edit.
- Double booking of a hall (and of a combined hall) is refused.
- Expiry after 15 days, reminder 2 days before; revising makes v2 and supersedes v1.
- Numbering restarts on 1 April; no gaps.
- Cancellation at 40 days keeps 25 % → 4300 with GST; the rest refunded.
- Housekeeping / maintenance never see a price (DEC-061 walk).
- Printed totals equal screen totals; phone pages pass the 360 px check.

## 22. Owner questions with defaults (A26)

If you do not answer, HCC is built with the default.

| # | Question | Default |
|---|---|---|
| Q1 | Your paper T&C text | Placeholders "OWNER TO REPLACE"; quotations cannot be sent until replaced (Owner override) |
| Q2 | Advance pattern | 25 % on booking / 50 % at 30 days / balance on the day |
| Q3 | GST package rate or itemised | Itemised per line; CA to confirm the banquet catering rate |
| Q4 | Menus | Packages per plate on the POS menu master |
| Q5 | Quotation numbering | `QT/2627/0001`, restarts every 1 April |
| Q6 | Food in the hall to 4200 F&B or all to 4400 | Food to 4200, hall and services to 4400 |
| Q7 | Hall discount limits | Reception 0 / Manager 10 % / Owner any |
| Q8 | Lost-reason list | Yes, the six reasons in §5 |
| Q9 | Quotation validity | 15 days |
| Q10 | Cancellation slabs until your T&C | §15 table |
| Q11 | Bill on guaranteed or actual plates | The higher of the two |

**CA:** banquet catering GST slab; package vs itemised (CA question 3).

**To approve:** reply "OP-17 approved" (with any changes to Q1–Q11).
