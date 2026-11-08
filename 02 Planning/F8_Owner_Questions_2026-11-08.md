# F8 owner questions — split folio, P2 reports, quick wins (8 November 2026)

**For:** Sagar (owner). **From:** F8 coordinator (Opus). **Why:** wave F8 needs these answers for money and GST rules that are not yet decided. **Nothing was guessed:** every question has a default, the lanes build the default, and your later answer replaces it (the contract row it changes is named). Sources: `02 Planning/agents/SF-CONTRACT.md` (rules), `02 Planning/agents/F8_Briefs.md` (lanes), DEC-152 (split folio), DEC-076 (company credit limits, audit D7), DEC-063 (approvals), DEC-069 (GST master). Rates and GST treatment are illustrative until the CA confirms. Answer by question id ("SFQ-3: yes"), or "accept all defaults".

**Already settled, applied without asking again:** A17 (the DEC-096 … DEC-110 rows stay as built; CRM un-merge revisited later) and A20 (cash out of the drawer needs a second person above ₹20,000; past attendance: the Manager corrects, the Owner approves; company credit limits are Owner-only).

## A. Split folio (SFQ)

| Id | Question | Default (built unless you say otherwise) | Contract |
|---|---|---|---|
| SFQ-1 | How many bills (folios) may one stay have? | Up to **4** (A = the guest, B–D for others). | §2.2 |
| SFQ-2 | Which charge groups can be sent to another payer? | Two groups, the same as the group-booking module: **room** (room nights, meal plan, room discounts) and **extras** (laundry, food to the room, late check-out, other services). No "company pays up to ₹X a night, guest pays the rest" split in F8 (that would cut one night into two lines). | §3, §8 R5 |
| SFQ-3 | Meal plan with the room: if the company pays the room, does it also pay the breakfast / meal plan? | **Yes, always.** A bundled plan is one supply with the room for GST; a "separate" plan follows the room too. **CA to confirm** for separate plans. | §3 |
| SFQ-4 | Who may set up a split bill at check-in or later? | Anyone who may post charges today (**Reception, Manager, Owner**); no approval. A company bill needs a company account with enough credit. No new permission. | §4 |
| SFQ-5 | Can a charge already posted be moved to another bill? | **No, not in F8.** The split applies to charges posted after it is set; set it at check-in. A wrong posting is fixed by the existing correction (Owner approves) and posted again. | Deviation 6 |
| SFQ-6 | The night audit posts a room night to the company's bill and the company goes **over its credit limit**. What happens? | The night **is posted** (the day close must never stop), the high-balance report shows it red, and at check-out company credit is **refused** until the Owner raises the limit or the guest pays. | §7.3 |
| SFQ-7 | A desk user posts an extra (e.g. laundry) to the company's bill and it would go over the limit. | **Refused** with the credit-limit message; the desk may post it to the guest's bill instead. | §7.3 |
| SFQ-8 | A bill ends at ₹0 (e.g. the guest had no extras). Does it get a tax invoice? | **No invoice for a ₹0 bill**; its lines stay on the stay statement. | §5.2 |
| SFQ-9 | On a split stay, whose name and GSTIN go on the guest's extras invoice? | The **guest's name, no GSTIN** (B2C). The company GSTIN is only on the company's invoice. If the guest is a registered business, the desk may enter his own GSTIN for his bill. | §2.2, §5.5 |
| SFQ-10 | Group master as a payer (a wedding party where the organiser pays the rooms of every guest). | **Not in F8.** Group bookings keep today's group module (master folio, routing, transfers). A split bill is refused on a group member. | Deviation 5 |
| SFQ-11 | Bill correction with a new invoice number (LF A2) on a split stay. | **Refused** on a split stay; use a credit note on the bill concerned. | §3 |
| SFQ-12 | Where is the refundable deposit kept on a split stay? | Always on the **guest's bill (A)**; it is returned to the guest at check-out. | §3 |
| SFQ-13 | The Manager may approve company credit up to ₹10,000 per stay. On a split stay is that per stay or per company bill? | **Per company bill** — the same thing, because a stay can have only one company bill. Above ₹10,000 the Owner approves (or the limit you set in the Administration panel). | §7.4 |
| SFQ-14 | Can two different companies pay parts of one stay? | **No: one company bill per stay.** If the booking is already linked to a company account, the company bill must be that company. | §2.2 |
| SFQ-15 | Invoice numbers on a split stay. | Allocated at check-out from the **one hotel series**, consecutively: the guest's bill first, then the company's (e.g. HPE/2627/0101 guest, HPE/2627/0102 company). | §5 |
| SFQ-16 | A third person (a local customer known to the hotel) pays part of the stay on **Udhaar** credit. | Allowed: that bill goes to the customer's Udhaar account at check-out, within the customer's credit limit (the existing credit-guest rules). DEC-243 (Udhaar account key) is still pending. | §2.2 |
| SFQ-17 | Can a split be undone? | A bill can be removed while **nothing** has been posted or paid on it. | §2.2 |
| SFQ-18 | After check-out, a credit note on the company's bill (company credit already given): should the credit note reduce what the company owes (its receivable) automatically? | **Same as today's single-bill company stays** (no new rule in F8); the integrator writes down exactly what happens today. **CA to confirm** the treatment. | §6 |

## B. P2 reports (RPQ)

| Id | Question | Default | Contract |
|---|---|---|---|
| RPQ-1 | F8 builds 24 P2 reports in three batches (list in the contract §12). Twelve other P2 entries wait (partials that already have a screen, three housekeeping task reports — one of them, room cleaning time, is extended by the measurement report "Attendant productivity and turnaround" — and the drill-down), plus 14 P2 reports that work today as screens but are not yet on the new report engine. Do you want a fourth batch in F8? | **No fourth batch**; the waiting list goes to the F9 / F10 planning. | §12 |
| RPQ-2 | `rpt.rooms.adr-trend` (day-by-day ADR and occupancy): drop it, because `rpt.rooms.occupancy` (per day) and the study report `rpt.study.revenue-kpi` (per month) cover it? | **Keep it in the catalogue, not built in F8**; decide after you have used the two existing ones. | §12 |

## C. Quick wins (QWQ)

| Id | Question | Default | Contract |
|---|---|---|---|
| QWQ-1 | Night-audit checklist: the new lines (key cards not cancelled for departed rooms, backup not OK, POS checks still open) — block **Close day**, or show only? | **Show only.** Only what blocks today keeps blocking (pending arrivals, unposted nights, open shift …). Measurement lines block only if you switch on "Block the close on measurement exceptions" (off). | §13 |
| QWQ-2 | Overbooking: may a room type be sold beyond its rooms (to the OTAs)? | **+0 for every type** (no overbooking), the measurement setting you already have; F8 only makes the channel push honour it. | §13 |
| QWQ-3 | Kitchen display: when does a KOT turn amber and red? | **Amber after 10 minutes, red after 20 minutes**, same for every outlet (you can change them later). | §13 |
| QWQ-4 | Booking against a minimum-stay / closed-to-arrival rule: today the system **refuses** it (an approver may override with a reason), while the measurement setting says "advisory". Which do you want? | **Keep today's refusal with override**; the setting stays as it is. | §13 |
| QWQ-5 | "Advances held" view: which advances are shown red? | Red when the booking is **cancelled, no-show or departed** and money is still held (a refund is owed), or the advance is **older than 90 days**. | §13 |
| QWQ-6 | CCTV and DG (generator) shift checklist: which checks? | Morning / evening / night: CCTV recorder running and all cameras visible, DG diesel level, DG test run (weekly), battery / UPS status, fire panel normal. Added only when the Manager presses "Add standard CCTV / DG checklist". | §13 |
| QWQ-7 | No-show: after the cut-off time (today 23:00), should the system mark no-shows by itself? | **No.** After the cut-off the desk sees a "Due no-shows" list and marks each one with one click; the no-show charge of the booking's policy is **shown, not posted** (as decided for the masters, DEC-MA-5b). | §13 |

**Count:** 18 SFQ + 2 RPQ + 7 QWQ = **27 questions**, all with defaults.

## D. Raised by the lanes (f8-int, 8 Nov 2026)

| Id | Question | Default | Contract |
|---|---|---|---|
| RPQ-3 | Report "Payment corrections" (`rpt.money.payment-corrections`): HCC has no command that changes a receipt's payment type after entry, so there is nothing to list. Add such a command (entered as / corrected to / by / reason / time)? | **Not built**; the report stays `planned` until a correction command exists (a wrong receipt is refunded and entered again, as today). | §12 |
| RPQ-4 | Asset register: per-asset depreciation and written-down value columns. HCC keeps no depreciation rate per asset; DEC-138 / DEC-140 leave depreciation to the CA in Tally. | **Columns not built**; the register shows cost (bill less ITC, net of returns). The CA gives the rates (block-wise, Income-tax Act) if the owner wants them in HCC. | §12 |
| RPQ-5 | Rate sheet (`rpt.admin.rate-sheet`) needs permission PERM-YLD-001, which the prototype matrix does not list: F8 grants it to **Owner and Manager** (as the server seed 3700). Confirm? | **Owner + Manager.** | §12 |
| RPQ-6 | Licence expiry and Company credit reports follow their permission codes (PERM-CMP-001 = Owner + Accountant, so a Manager is refused licence expiry; PERM-GRP-001 also admits Reception and Cashier to company credit), not the role text in the catalogue ("Owner, Manager, Accountant"). Which wins? | **The permission codes** (as built); the catalogue text is corrected at close. | §12 |
| SFQ-20 | A discount or credit note on a split-stay folio that has **no room night** and lines at **different GST rates** (e.g. the guest's folio with laundry at 18 % and food at 5 %): which GST rate? (Before F8 every folio had room nights, so the room rate was used.) | **Refused** with a plain message (credit the folio that holds the nights, or the Owner decides); a folio whose lines share one rate uses that rate. **CA to confirm** an apportionment rule (per line or pro rata). | §6 |
| SFQ-19 | Split stay checked out on the server when the hotel GSTIN is not yet set: the folio invoices cannot be numbered. | Check-out goes ahead; the invoices are issued afterwards per folio with **Issue invoice** (numbers then follow the order they are issued). | §5 |

**Count after f8-int:** 20 SFQ + 6 RPQ + 7 QWQ = **33 questions**, all with defaults.
