# SCC "tax" module vs HCC compliance calendar — gap report

27 Oct 2026 · read-only review · owner ruling (27 Oct 2026): every SCC module HCC took from must carry SCC's **entire** rule set, computed exactly as SCC does.

## What SCC "tax" is

SCC's tax module is a **statutory compliance calendar and control layer**, called "Compliance Operating System". It does **not** compute tax. It has:

- a fixed catalogue of **113 dated obligation cards**, one set per FY page April–March, covering **30 distinct obligations** across Income Tax, GST, Composition GST and Labour Law;
- firm profiles (multi-firm) that decide which cards apply: GST registration type, QRMP, whether the firm has employees;
- done / not-applicable recording per firm per FY, with filed-on date, notes, evidence reference, reviewer and attached files;
- a control layer per card: risk, cash-flow class, responsible person, reviewer, internal T-5 / T-3 deadline, escalation, checklist, workflow and evidence list;
- custom obligations, archiving (per obligation or whole category), an Action Center (30-day window), a quarterly dashboard, a CSV register, a month print, an evidence ZIP and a CA pack sent through the shell's WhatsApp composer;
- a **read-only "≈₹ payable"** display. It reads a feed the SCC shell builds from locked payroll (PF / ESIC / PT) and from the Expense module's GST estimate.

Penalties, interest and late fees appear **only as text**. SCC computes none of them.

**Sources read.**
- `scratchpad\scc\tax.js`, 2,532 lines. It is byte-identical to the main `<script>` of `E:\Projects\HCC\Migration data on 25 oct\HCC-Migration-2026-10-25\reference-inputs\SCC-modules\tax\index.html`, lines 863–3397: **index.html line = tax.js line + 861**.
- The other script blocks in index.html are only shell runtime bootstraps (`SaagarModuleRuntime.run(...)`, lines 4–11 and 3399–3403) and carry no rules. There is **no embedded JSON seed**.
- The markup at index.html lines 622–861 gives the form options: entity, GST type, QRMP, has-employees, custom-obligation fields, frequency list and due-day list.
- Also read: SCC `expense/index.html` `genTaxFeed` (line 1870), the source of the GST estimate.

**Not available.** The SCC **shell** is not in the reference inputs, and SCC's full source is not on this PC. It holds:
- the bridge that writes `saagar_tax_payable`;
- `computeTaxStatus` / `buildTodayBrief` (Reports buckets);
- the deadline notifications that use `record.buffer`;
- the WhatsApp `ca_pack` template.

These rules cannot be ported until that source is supplied.

## Did HCC derive its module from SCC?

**Yes, but partially.** HCC took **7 due-date rules only** ("rules only, not code"). It replaced everything else with HCC's own design. Evidence:

- `03 Prototype\src\domain\streams\compliance.ts:2-4`: "Due-date rules ported (rules only, not code) from SCC www/modules/tax/index.html … TDS … GSTR-1 … GSTR-3B … EPF … ESIC … Professional Tax … GSTR-9".
- `03 Prototype\src\fixtures\ext\compliance.ts:1,13`: "Due rules from SCC tax module", and the TDS note "(SCC)".
- `08 Production\src\Hcc.Domain\Compliance\ComplianceRules.cs:6-9,306`: "default SCC-derived obligations".
- `08 Production\database\migrations\1100_compliance.sql:1-2`: "rules from the SCC tax module … Original HCC design; no donor code".
- `02 Planning\agents\W05_Briefs.md:22-23`: "W05-C · Compliance calendar from SCC", with the scope limited to TDS, GSTR-1, GSTR-3B, GSTR-9, EPF, ESIC and PT.
- `02 Planning\HCC_FirstParty_Reuse_ETP_SCC_v1.md` §3a, `tax` row: "Port the calendar rules + reminders".
- `02 Planning\HCC_Wave05_UI_Placement_v1.md:12`.

**Not derived from SCC tax (HCC-only):**
- `03 Prototype\src\domain\gst.ts` (DEC-069 GST rate master);
- `03 Prototype\src\domain\gstr1.ts` and `features\compliance\Gstr1.tsx` (DEC-072 GSTR-1 working summary, a port of the server's `Hcc.Domain\Invoices\Gstr1Summary.cs`).

SCC tax has no GST rate logic and no GSTR-1 computation.

**Abbreviations used in the table**
- **SCC Lnnn** = `scratchpad\scc\tax.js` line (index.html = +861)
- **P-dom** = `03 Prototype\src\domain\streams\compliance.ts`
- **P-hnd** = `03 Prototype\src\services\mock\handlers\compliance.ts`
- **P-fix** = `03 Prototype\src\fixtures\ext\compliance.ts`
- **P-ui/X** = `03 Prototype\src\features\compliance\X`
- **S-dom** = `08 Production\src\Hcc.Domain\Compliance\ComplianceRules.cs`
- **S-app** = `08 Production\src\Hcc.Application\Compliance\ComplianceCommands.cs`
- **S-port** = `…\Hcc.Application\Compliance\CompliancePorts.cs`
- **S-sql** = `08 Production\database\migrations\1100_compliance.sql`
- "Due" strings are quoted exactly from SCC's catalogue `D` (SCC L391–1131).
- **FY rule** (SCC `parseDueDate` L345–369): a date in Jan–Mar falls in FY-start-year + 1; Apr–Dec fall in the FY-start year.

---

## 1. Obligation catalogue (SCC `D`, L391–1131: 113 cards, 30 obligation keys)

| # | Rule | SCC behaviour (line) | HCC prototype | HCC server | Verdict | What to change |
|---|---|---|---|---|---|---|
| 1.1 | TDS deposit (monthly) | 12 cards `cat:'it'`: "TDS Deposit – <M> Deductions", due **7th of the next month**. **March deductions due "30 Apr"** (ap1 L396, `urgent:true`, freq "Monthly (Special Date)"). Others: my1 L449 "7 May", jn1 L502, jl1 L561, ag1 L638, sp1 L685, oc1 L738, nv1 L809, dc1 L862, jn_1 L927, fb1 L1004, mr1 L1063. Form "Challan 281". | P-fix:13 `{type:'day',day:7,special:{month:3,day:30}}` → same dates (test `tests\stream-compliance.mjs:16`) | S-dom:313 same rule; test `ComplianceRulesTests.cs:17-20` | **SAME** (dates) | Keep dates. Add the SCC card fields (urgent on ap1, freq label, who/what/why/penalty): see §4 and §6. |
| 1.2 | GSTR-1 monthly | 12 cards "GSTR-1 – <M> (Monthly Filers)", due **11th**: ap2 L408 "11 Apr", my3 L461 … mr5 L1087. | P-fix:14 day 11, note "Monthly filer assumed (not QRMP)" | S-dom:315 | **SAME** | Add QRMP gating/advisory (row 3.4). |
| 1.3 | GSTR-1 QRMP quarterly | jl5 L585 "13 Jul" (Q1), oc6 L768 "13 Oct" (Q2), jn_5 L951 "13 Jan" (Q3). **No Q4 card** (13 Apr). | absent (no QRMP; kinds P-dom:10) | absent (kind CHECK S-sql:13) | **MISSING** | Add the 3 cards exactly. Ask about the missing Q4 card (§8 Q1). |
| 1.4 | GSTR-3B monthly | 12 cards, due **20th**: ap3 L414 "20 Apr" … mr6 L1093. | P-fix:15 day 20 | S-dom:316 | **SAME** | — |
| 1.5 | GSTR-3B QRMP quarterly | jl7 L597 "22 Jul", oc7 L774 "22 Oct", jn_6 L957 "22 Jan". **No Q4 card** (22 Apr). | absent | absent | **MISSING** | Add the 3 cards (§8 Q1). |
| 1.6 | CMP-08 (composition quarterly) | `cat:'comp'`, urgent: ap_comp1 L402 "18 Apr" (Q4 Jan–Mar), jl8 L603 "18 Jul", oc8 L780 "18 Oct", jn_7 L963 "18 Jan". | absent | absent | **MISSING** | Add the cards, shown only for composition / both firms (row 3.2). |
| 1.7 | GSTR-4 annual (composition) | ap4 L420, **on the April page but due "30 Jun"**, urgent, Annual, "(FY 2023–24)" rebased to the previous FY. | absent | absent | **MISSING** | Add it. Ask whether it belongs on the April page (§8 Q9). |
| 1.8 | EPF contribution (monthly) | 12 cards due **15th**. **The April card is named "EPF Contribution Deposit – March Wages"** (ap5 L426); others are "EPF Contribution – <M> Wages" (my5 L473 … mr8 L1105). | P-fix:17 day 15 | S-dom:318 | **SAME** (dates) | Port the card names exactly; they drive the archive keys (row 3.5). |
| 1.9 | ESIC contribution (monthly) | 12 cards due **15th**. April card is "ESIC Contribution Deposit – March Wages" (ap6 L432); others my6 L479 … mr9 L1111. | P-fix:18 | S-dom:319 | **SAME** (dates) | As 1.8. |
| 1.10 | Professional Tax monthly | 12 cards "Professional Tax – <M> Deduction & Deposit", due "Last day of <next month>": ap7 L438 "Last day of Apr" … mr10 L1117. Text quotes MH slabs ≤₹7,500 nil; ₹7,501–10,000 ₹175; >₹10,000 ₹200 (₹300 Feb) (L440, L1048, L1119). | P-fix:19 `{type:'month-end'}` | S-dom:320 | **SAME** (dates) | Port the texts. |
| 1.11 | TDS return 24Q/26Q (quarterly) | urgent: my2 L455 "31 May" (Q4), jl2 L567 "31 Jul" (Q1), oc2 L744 "31 Oct" (Q2), jn_2 L933 "31 Jan" (Q3). | absent | absent | **MISSING** | Add the 4 cards. |
| 1.12 | ESIC half-yearly return | urgent: my7 L485 "11 May" (Oct–Mar), nv7 L845 "11 Nov" (Apr–Sep); "Fine up to ₹5,000 under Section 85". | absent | absent | **MISSING** | Add them. CA to confirm still required (§8 Q11). |
| 1.13 | Advance tax instalments | urgent, Challan 280: jn2 L508 "15 Jun" (≥15%), sp2 L691 "15 Sep" (cumulative ≥45%), dc2 L868 "15 Dec" (≥75%), mr2 L1069 "15 Mar" (100%, freq "Annual Final"). | absent | absent | **MISSING** | Add the 4 cards, with the % in the name. |
| 1.14 | Form 16 (salary TDS certificate) | jn3 L514 "15 Jun", urgent, Annual, prior FY. | absent | absent | **MISSING** | Add it. |
| 1.15 | Form 16A (quarterly) | jn4 L520 "15 Jun" (Q4), ag2 L644 "15 Aug" (Q1, urgent), nv2 L815 "15 Nov" (Q2, urgent), fb2 L1010 "15 Feb" (Q3, urgent). | absent | absent | **MISSING** | Add the 4 cards. |
| 1.16 | ITR non-audit | jl3 L573 "31 Jul", urgent, ITR-3 / ITR-5. | absent | absent | **MISSING** | Add it. |
| 1.17 | Labour Welfare Fund half-yearly | jl11 L621 "15 Jul" (Jan–Jun), name "Labour Welfare Fund (LWF) – …"; jn_10 L981 "15 Jan" (Jul–Dec), name "Labour Welfare Fund – …". MH ₹6 employee + ₹12 employer per month (text). | absent | absent | **MISSING** | Add both, with the two names exactly (row 3.5 key quirk). |
| 1.18 | Tax audit report 3CA/3CB/3CD | sp3 L697 "30 Sep", urgent. | absent | absent | **MISSING** | Add it. |
| 1.19 | ITR audit cases | oc3 L750 "31 Oct", urgent. | absent | absent | **MISSING** | Add it. |
| 1.20 | GSTR-9 annual | dc5 L886 "31 Dec", urgent, "(FY 2023–24)" rebased: the previous FY appears on the December page of the current FY. | P-fix:16 `{type:'fixed',month:12,day:31}`, owner "Owner" | S-dom:317 | **SAME** (date) | — |
| 1.21 | GSTR-9C | dc6 L892 "31 Dec", Annual. | absent | absent | **MISSING** | Add it. |
| 1.22 | Shops & Establishment renewal | dc_shops L916 "31 Dec", business-level (stays when no employees, L1608). | absent | absent | **MISSING** | Add it. |
| 1.23 | Annual returns, Factories Act / S&E | jn_11 L987 "15 Jan", business-level. | absent | absent | **MISSING** | Add it. |
| 1.24 | Employee investment proofs | fb3 L1016 "By end of Feb", Annual. | absent | absent | **MISSING** | Add it. |
| 1.25 | Annual return, Shops & Commercial Establishments | fb9 L1052 "1 Feb", business-level. | absent | absent | **MISSING** | Add it. |
| 1.26 | Finalise salary TDS / Form 16 data | mr3 L1075 "31 Mar". | absent | absent | **MISSING** | Add it. |
| 1.27 | ITR-U (2 years prior) | mr4 L1081 "31 Mar", "Annual (if applicable)". | absent | absent | **MISSING** | Add it. |
| 1.28 | Year-end GST reconciliation | mr7 L1099 "31 Mar". | absent | absent | **MISSING** | Add it. |
| 1.29 | Annual PT return + employer enrolment renewal | mr11 L1123 "31 Mar", business-level; employer's own PT "₹2,500/year in Maharashtra". | absent | absent | **MISSING** | Add it. |

## 2. Due-date engine, periods and FY

| # | Rule | SCC behaviour (line) | HCC prototype | HCC server | Verdict | What to change |
|---|---|---|---|---|---|---|
| 2.1 | "D Mon" parse | `^(\d{1,2})\s+([A-Za-z]+)$` → `new Date(y, mi, day)`, y = FY start + 1 if Jan–Mar, else FY start. The month in the due string, **not** the page month, decides the date (so ap4 on April = 30 Jun) (L359–367). | Period + "day of next month" rule (P-dom:71-78). Same result for the 7 shared obligations. | S-dom:174-191 | **SAME** (for the 7) | Port `parseDueDate` for the full catalogue so every card resolves exactly as SCC. |
| 2.2 | "Last day of / By end of Mon" | `^(?:last day of\|by end of)\s+(.+)$`, 3-letter month lookup → day 0 of the next month (L349–358). | `month-end` rule (P-dom:76) | S-dom:187 | **SAME** | — |
| 2.3 | Holiday / Sunday shifting | **None.** Due dates never move (L345–369). | Due date on a paid holiday moves to the next day that is neither a holiday nor a Sunday, up to 14 tries (P-dom:64-70, used P-dom:91,95) | S-dom:160-172; S-app:54,76; holidays via `SqlComplianceStore.cs:149` | **HCC-ONLY** (DEC-MA-61; inert today because DEC-114 says no holidays) | Ruling says no substitutions: remove the shift, or get an owner exception (§8 Q12). |
| 2.4 | Custom due day | Due day `0` = "Last day of <Mon>", `1–31` = "<d> <Mon>" **in the ticked month itself** (L1668–1671; dropdown L1276–1288). No clamping: day 29–31 in a shorter month rolls over (JS Date). | "Day of the next month" **1–28**, or month-end of the next month (P-hnd:24; P-ui/modals.tsx:90); clamped (P-dom:78) | S-dom:207-226; S-sql:38 (`due_day BETWEEN 1 AND 28`) | **DIFFERENT** | Add SCC's model: due day 0–31 in the selected month(s). Rollover bug: §8 Q3. |
| 2.5 | Editing a due date | Built-ins are **immutable** (`D` never mutated, L1631–1632); they can only be archived and re-added as custom. Custom edits replace the object, so they apply **retroactively** to every month (L1406–1408). | Owner edits any obligation's rule with an **effective-from ≥ today**; earlier periods keep the old rule (P-hnd:62-71) | S-app:126-137; table `cmp.obligation_rule` S-sql:27-39 | **DIFFERENT** | Make built-ins read-only. Custom edits are retroactive as in SCC unless the owner keeps dated rules (§8 Q13). |
| 2.6 | "Today" for days-left | Device **real date** `new Date()` (L374, L2258), even when the shell as-of date is in the past. | Business date `s.date` (P-dom:95) | business date (S-app:264) | **DIFFERENT** | Keep the business date (HCC architecture rule); record it as an owner-approved difference (§8 Q5). |
| 2.7 | Which cards exist | The whole selected FY (12 month pages) is shown in advance, including future items (L2435, L2481–2483). | A period appears only **after its month (or FY) has ended**, from `from` onward: fixture 2026-04; a new obligation tracks from the previous month (P-dom:54-58; P-hnd:77-79) | S-dom:119-142 | **DIFFERENT** | Show the full FY catalogue ahead, as SCC does. |
| 2.8 | FY selection | Selector FY 2026–27 … 2032–33; `fyStartYear` minimum 2026 (L25, L220–239). Records are bucketed per FY (L241–246). The FY does **not** follow today: it is persisted until the user switches. | No FY selector; continuous periods; FY derived (P-dom:47-52) | same (S-dom:100-116) | **DIFFERENT** | Add the FY selector and FY buckets (2026–2032). Auto-advance question: §8 Q5. |
| 2.9 | Quarters | Q1 Apr–Jun, Q2 Jul–Sep, Q3 Oct–Dec, Q4 Jan–Mar, with period text (L1136–1141). | none | none | **MISSING** | Add them (needed by the dashboard, row 5.4). |

## 3. Applicability (firm profile, gating, archive)

| # | Rule | SCC behaviour (line) | HCC prototype | HCC server | Verdict | What to change |
|---|---|---|---|---|---|---|
| 3.1 | Firm / entity profile | Multi-firm. Fields: name (required), PAN, GSTIN (upper-cased, no format check), entity (proprietorship / partnership / llp / company), GST type (regular / composition / both / unregistered), `hasEmployees` (default yes), `qrmp`, notes (L1187–1216; markup index.html lines 683–728). Records are keyed firm → FY → item (L9). | none (one hotel; no profile) | none (per `property_id` only) | **MISSING** | Add a compliance profile for the property: GST type, QRMP, has employees, entity. Multi-firm: §8 Q14. |
| 3.2 | GST-type gating | `it` and `lab` always shown. `gst` only when type is regular or both. `comp` only when type is composition or both. No firm selected = 'both', so everything shows. Unregistered sees no GST/comp cards (L1613–1628; `\|\| type==='unregistered' && false` at L1624 is dead code). | none | none | **MISSING** | Port `getVisibleItems` exactly. |
| 3.3 | Has-employees gating | `hasEmployees===false` (explicit only) hides payroll cards = `cat:'lab'`, not custom, not in `BUSINESS_LAB_IDS = ['dc_shops','jn_11','fb9','mr11']` (L1604–1611, L1616, L1622). | none | none | **MISSING** | Port it. |
| 3.4 | QRMP | `qrmp` can be set only for regular / both (L1200, L1263–1268). **Advisory only, nothing hidden** (L1800–1833). For a QRMP firm: "(Monthly Filers)" GST cards show the amber note "QRMP firm: you file this quarterly, not monthly. File the quarterly GSTR-1 / GSTR-3B and pay monthly via PMT-06 challan (GSTR-1 IFF upload is optional)." (not for type 'both'), and QRMP cards show the green note "Applies to your firm (QRMP quarterly filer)." For a monthly firm, QRMP cards show the grey note "Only for QRMP-scheme firms — your firm files monthly …". A QRMP badge appears in the header and firm list. | fixture note "Monthly filer assumed (not QRMP)" (P-fix:14) | none | **MISSING** | Port the flag, badge and advisory texts. |
| 3.5 | Archive an obligation | Toggle a **key** in the global `STATE.archived`. Applies to all firms. Built-in key = name up to the first en-dash or pipe; custom key = id (L1465–1473, L1643–1653). Archived cards vanish from calendar, dashboard, Action Center, CSV, print and materialisation. **Quirk:** April EPF/ESIC keys "EPF/ESIC Contribution Deposit" differ from the other months' "EPF/ESIC Contribution"; LWF has two keys (verified: 30 keys, 113 cards). | Switch obligation off/on (Owner); history kept; inactive rows hidden unless `includeInactive` (P-hnd:83-86; P-dom:93) | S-app:158-173; `active` column S-sql:19 | **DIFFERENT** | Port archive-by-key including the key derivation. Ask about the split keys (§8 Q2). |
| 3.6 | Archive a whole category | `STATE.archivedCats` (it / gst / comp / lab). Hides every built-in and custom card in it. Resets the category filter if it was active (L1477–1486, L1620). | none | none | **MISSING** | Add it. |
| 3.7 | Category model and filter | Categories it / gst / comp / lab, labelled "Income Tax", "GST", "Composition GST", "Labour Law" (L2140–2145). Filter buttons All / Income Tax / GST / Composition / Labour / **NA (count)** (L2451). | 7 "kinds" filter + status tabs Open / Overdue / Filed-NA / All (P-ui/shared.tsx:16-23; CompliancePage.tsx:44-46) | kinds S-dom:11-16 | **DIFFERENT** | Add the categories and the NA filter. The kind filter can stay as an extra. |

## 4. Status, recording, guards

| # | Rule | SCC behaviour (line) | HCC prototype | HCC server | Verdict | What to change |
|---|---|---|---|---|---|---|
| 4.1 | Status set | Pending / Done / N/A per firm × FY × card (L97–99, L1985). | Overdue / Due today / Due soon / Upcoming / Filed / Not applicable (P-dom:85,96) | S-dom:23-26,230 | **DIFFERENT** | Store SCC's three states; derive the time bands as in row 4.2. |
| 4.2 | Days-left bands | Done → "✓ Filed". <0 → "⚠ Nd overdue". 0 → "🔴 Due today". **≤7** → "⚡ Nd left". **≤15** → "⏰ Nd left". Otherwise "Nd left" (L378–386). Rounding: `Math.round((due−today)/86400000)` (L375). | Due soon = days ≤ `dueSoonDays` (default 7, 1–30), otherwise Upcoming; **no 15-day band** (P-dom:27,96) | S-dom:59,230 | **DIFFERENT** | Add the ≤15 band and the exact labels. |
| 4.3 | Mark done | Capture row: **Filed on** (date input, default today, any date), Notes ("Ack no. or filing note"), Evidence ref, Reviewer, Files (multiple). **All optional, no validation.** The record is `{done:true, filedOn, notes, evidenceRef, reviewerNote, dueDate}` and **replaces** any earlier record (L2402–2425; UI L2469). A firm is required (L2404). | Reference **required**, 4–40 characters `[\w\-/. ]`. Time = system clock (no user date). No notes, evidence or reviewer (P-hnd:31-38; P-ui/modals.tsx:13-21) | S-dom:275-281; S-app:42-61; `cmp.filing` S-sql:41-50 | **DIFFERENT** | Add filed-on date, notes, evidence ref, reviewer and files, and make the reference optional (§8 Q6). |
| 4.4 | Mark NA | Toggle; **no reason**; additive `na:true` flag, other fields untouched (L2179–2195). An NA card is read-only and cannot be marked done (L2161, L2406). | Reason **required** (5–120); closes the period (P-hnd:40-46) | S-app:63-79; S-dom:283-288 | **DIFFERENT** | Allow NA without a reason (§8 Q6). |
| 4.5 | Undo done | Clicking ✓ on a done card **deletes the whole record** (filed date, notes, evidence ref, reviewer). No confirmation, no reason, no role check (L2159–2172). | Owner-only "Reopen" with a reason (5–120); the original is kept in `reopened` (max 200) (P-hnd:48-54) | S-app:81-98; `cmp.filing_reopened` S-sql:53-66 | **DIFFERENT** | §8 Q6: SCC semantics vs HCC's kept trail. |
| 4.6 | Un-NA | Toggle removes only the `na` flag; the record is deleted if it becomes empty (L2183–2186). | via Reopen (Owner) | via Reopen | **DIFFERENT** | As 4.5. |
| 4.7 | When a card can be recorded | Any card, any time, including cards whose due date is in the future (no period check). | Only for periods that have ended and are on the calendar, and only on active obligations (P-hnd:15-16,33) | S-app:252-268 | **DIFFERENT** | Allow recording any card of the selected FY. |
| 4.8 | Read-only past view | If the shell as-of date (`window.__stAsOf`) is before today, `isPastAsOf()` blocks: materialisation (L308), done/undo (L2160), NA (L2180), evidence delete (L2289), evidence attach (L2296) and confirm (L2403). Each shows an alert "🔒 Viewing a past date … read-only". | No past-date view exists | Closed business day deliberately not checked (S-app:39-40) | **MISSING** | Only applies if HCC adds as-of browsing. Then refuse every compliance write while viewing a past date. |
| 4.9 | Firm must be selected to record | L2163, L2181, L2404, L2297 | n/a (single property) | n/a | **N/A** | Not needed with one property, unless multi-firm is adopted. |
| 4.10 | Late marker | none stored | Audit text "N day(s) late" (P-hnd:38) | `daysLate` in audit and outbox event `ComplianceFiled` (S-app:55-58) | **HCC-ONLY** | Keep (additive) or drop (§8 Q12). With the SCC user-entered filed-on date, compute lateness from that date. |

## 5. Penalties, texts, control layer, custom obligations, reminders, views

| # | Rule | SCC behaviour (line) | HCC prototype | HCC server | Verdict | What to change |
|---|---|---|---|---|---|---|
| 5.1 | Penalties / interest / late fees | **Text only, never computed.** Every card carries a `penalty` string, e.g. TDS "Interest @ 1.5% per month … u/s 201(1A) … 30% … disallowed u/s 40(a)(ia)" (L400); GST "₹50/day (₹20/day for Nil returns). Maximum late fee: ₹10,000" (L412); 3B interest 18% p.a. (L418); 234E "₹200/day … capped at TDS amount" + 271H ₹10,000–₹1,00,000 (L459); EPF damages 5% / 10% / 15% / 25% + 12% p.a. (L430); ESIC 12% + damages up to 25% (L436); PT MH 3× + 1.25%/month (L442); 234F ₹5,000 (₹1,000 if income ≤ ₹5 lakh) (L577); 271B ½% or ₹1.5 lakh (L701); 272A ₹100/day (L518); GSTR-4 ₹200/day max ₹5,000 (L424); GSTR-9 ₹200/day max 0.25% (L890). Shown under "⚠ Penalty for Non-Compliance" (L2469). | none (no computation, no text) | none | **MISSING** (text). Computation: SAME (neither computes). | Carry every penalty string verbatim. |
| 5.2 | Who / what / why texts + 12 month control notes | Per card `who`, `what`, `why` (L396–1127). Per month `note`, shown as "⚡ Control Note" (e.g. L394, L447, L500, L559, …, L1061; render L2448). | Obligation `note` only (P-fix:13-19) | `note` (S-sql:20) | **MISSING** | Port all texts verbatim. |
| 5.3 | FY text rebasing + Income-tax Act 2025 renames | `renderFYText` (L190–219) rebases baked "FY/AY YYYY–YY" ranges by `fyStart − 2024`, and "<Month> YYYY" only inside the authored window (FY 2023–2024). `renderNewActForms` (L165–189, `NEW_ACT_FORMS=true`): Form 16A→131, 16→130, 24Q→138, 26Q→140, 26AS→168; Section 200→392, 192→392, 194I/194J→393; u/s 234B→424, 234C→425, 234E→427; 80C→123, 80D→126; u/s 201(1A), 271H and 40(a)(ia) → "under the Income-tax Act, 2025"; "Assessment Year"/"Previous Year" → "Tax Year". Applied to name, form, texts, CSV, print and Action Center. | none | none | **MISSING** | Port both functions. Bug question: §8 Q4. |
| 5.4 | Quarterly dashboard | Per quarter, excluding NA: total, done, pending, overdue, highRisk, **score = round(done/total×100)** ('—' when 0). Monthly rows (score, done/total, overdue). Control summary: high-risk count, cash exposure H / M / L, evidence readiness = done count. **Top 8 focus items** = not done, sorted by risk (High, Medium, Low) then days left. Owner load by responsible. "Pending and Escalated" list (L2478–2511). | none | none | **MISSING** | Add the dashboard tab. |
| 5.5 | Risk class | High if the name contains "gstr-3b", "cmp-08", "tds", "epf", "esic", "professional tax" or "advance tax". Otherwise Medium if cat is lab or gst, else Low. A custom override wins (L2245–2251). Result per key, e.g. ITR / Tax audit / ITR-U / GSTR-4 = **Low**; "Employee Investment Proofs … (for Salary TDS)" = High. | none | none | **MISSING** | Port verbatim (§8 Q7). |
| 5.6 | Cash-flow class | High if GST payment, advance tax or TDS. Medium if payroll or cat gst. Else Low (L2252). | none | none | **MISSING** | Port. |
| 5.7 | Responsible | Custom value, otherwise: payroll → "Payroll / HR Executive"; gst/comp → "GST Executive"; else "Tax Executive" (L2253). | `owner` free text, default "Accountant"; GSTR-9 "Owner" (P-fix:13-19; P-hnd:58) | S-app:108 | **DIFFERENT** | Port the SCC defaults and the override. |
| 5.8 | Reviewer | Custom value, otherwise: advance tax → "Partner / CFO"; payroll → "HR & Finance Manager"; else "Compliance Manager" (L2254). | none | none | **MISSING** | Port. |
| 5.9 | Internal deadline (T-buffer) | Buffer = custom value (0–30, clamped L1399), otherwise **5 days if High risk, else 3**. Internal date = due − buffer (L2255–2257). Shown as "Internal due dd Mon" and "T-n". | none | none | **MISSING** | Port. |
| 5.10 | Escalation | On internal days left: **≤1 "Critical"**, **≤3 "Escalate"**, **≤7 "Reminder"**, else "Monitor", each with fixed note text (L2258–2262). Computed even for done cards (pill shown). | none | none | **MISSING** | Port, including the pill on done cards. |
| 5.11 | Checklist / workflow / evidence defaults | GST payment: ["Sales register and purchase register closed", "ITC matched with GSTR-2B", "Net GST payable and challan amount approved"]. TDS: ["Deduction working with section-wise mapping", "PAN and deductee details validated", "Challan or return control total checked"]. Payroll: ["Payroll register frozen", "Employee contribution workings validated", "Portal upload file and payment reference prepared"]. Other: ["Books closed for the period", "Working papers reviewed", "Final computation approved for filing"]. Workflow ["Prepare data pack", "Reviewer check and approval", "File and store evidence"]. Evidence: GST ["GST challan", "Filed return PDF", "Working reconciliation"]; TDS ["OLTAS challan", "Filing acknowledgement", "Deduction working"]; else ["Portal challan", "Return / ECR copy", "Payroll or support working"]. Custom overrides (L2263–2275). | none | none | **MISSING** | Port verbatim. |
| 5.12 | Urgent flag | 24 cards with `urgent:true` get a red due tag (L2469): ap1, ap_comp1, ap4, my2, my7, jn2, jn3, jl2, jl3, jl8, ag2, sp2, sp3, oc2, oc3, oc8, nv2, nv7, dc2, dc5, jn_2, jn_7, fb2, mr2. Custom cards are never urgent (L1403, L1700). | none | none | **MISSING** | Port. |
| 5.13 | Custom obligation: add / edit | Name required; ≥1 month required; category; firm scope (''= all firms; defaults to the active firm); frequency label Monthly / Quarterly / Annual / One-time (label only); due day 0–31; form; who / what / why / penalty; responsible, reviewer, risk, buffer 0–30, checklist / workflow / evidence lists. No count limit, no duplicate-name check. Card id = `<id>-<Mon>`. User text is HTML-escaped at expansion (L1376–1416, L1658–1704). | Kind from 7 types (frequency derived: GSTR-9 annual, else monthly); name 3–60, unique (case-insensitive); form ≤60; owner ≤30; note; rule; **max 30 obligations** (P-hnd:56-81) | S-app:100-156 (note ≤300) | **DIFFERENT** | Port SCC's custom model; keep HCC validation only where it is safe (§8 Q13). |
| 5.14 | Custom obligation: delete | Confirm, then remove the obligation and purge its evidence files for all firms / FYs / months (L1454–1462). Status records stay orphaned. | none (switch off only) | none | **DIFFERENT** | Add delete (Owner) with the evidence purge. |
| 5.15 | Obligation cap and unique name | none | 30 max, unique name (P-hnd:66,74-75) | S-app:133,146-147; `UQ_cmp_obligation_name` S-sql:24 | **HCC-ONLY** | Drop the cap, or keep it with the owner's OK. |
| 5.16 | Action Center | Across **all firms** × all 12 months of the **selected FY**. Cards not done / NA with **days ≤ 30** (`AC_SOON_DAYS`), including overdue, sorted by days ascending. Groups "Overdue (n)" and "Due soon (n)"; header badge with a "99+" cap; a row jumps to the firm, month and card. Read-only: the active firm is restored in `finally` (L1897–1964). | "Due this week": overdue + due within `dueSoonDays` (default 7); dashboard card top 5 (P-ui/DueCard.tsx:9-19); Accounts tile badge (`app\ModulesHome.tsx:35`); phone home (`mobile\frontdesk\Home.tsx:35-38`) | `/compliance/due-this-week`, `/compliance/badge` (S-app:297-309; S-port:114-115) | **DIFFERENT** | Add the Action Center with the **30-day window**. Keep the 7-day card as an extra if the owner agrees. |
| 5.17 | Month navigation + overdue dot | A dot on a month tab when any visible, non-NA, not-done card there is overdue (L2120–2135, L2518–2527). The calendar opens on the as-of month (L1718–1724). | List sorted by due date (P-dom:99) | — | **MISSING** | Add a month view with dots. |
| 5.18 | Month header stats | Total (non-NA), Completed, Pending, High Risk, Not Applicable, Overdue, Evidence (done-with-evidence / done) (L2450). | Overdue / Due this week / Filed this year / Not applicable (P-ui/CompliancePage.tsx:35-40) | counts S-dom:250-269 | **DIFFERENT** | Port SCC's 7 stats. |
| 5.19 | Feed to shell Reports / notifications | `materializeDueDates` writes `{dueDate, done:false, name, buffer}` for every visible card of the active firm and FY. It never overwrites done or notes; past view is a no-op (L298–337). `confirmDone` stamps `dueDate` (L2412–2421). Used by the shell's `computeTaxStatus` / `buildTodayBrief` (overdue / dueWeek / dueMonth) and by deadline notifications at T-buffer (shell code not supplied). | Computed on read. Assistant intent `compliance-due` (`domain\streams\assistant.ts:84,315-319`). No "due this month" bucket, no notifications. | due-this-week / badge | **DIFFERENT** | Add a due-this-month bucket and buffer-based reminders once the SCC shell rules are known. |
| 5.20 | Deadline notifications at T-buffer | The shell schedules them from `record.buffer` (comment L319–322; shell not supplied). | none | none | **MISSING** | Needs the SCC shell source. |

## 6. Amounts, outputs, access

| # | Rule | SCC behaviour (line) | HCC prototype | HCC server | Verdict | What to change |
|---|---|---|---|---|---|---|
| 6.1 | "≈₹ payable" chip on cards | Reads the `saagar_tax_payable` feed (5-second cache, L1758–1766). Field by card name: name contains "gstr-3b" **and** "monthly filers" → `gstEstimate`; "epf"+"contribution" → `pf`; "esic"+"contribution" → `esic`; "professional tax"+"deduction" → `pt`; the QRMP 3B, ESIC half-yearly and PT annual cards are excluded (L1768–1775). Key = **YYYY-MM of the month before the calendar page** (L1776–1786). Rounded; shown only if > 0: "≈₹X payable · estimate — verify with CA" (L1789–1793). Also on Action Center rows (L1795–1798). | none | none | **MISSING** | Compute from HCC data (row 6.4) and show it on the same cards. |
| 6.2 | Statutory Payable panel (dashboard) | Key = **current as-of month** (not P−1), else the newest key; only `^\d{4}-\d{2}$` keys (L1847–1861). Rows EPF / ESIC / Professional Tax / GST (estimate) / "Total statutory payable"; Sources; "Synced dd Mon yyyy"; feed `_note`, or "Estimate — verify with CA before filing." Empty state: "No statutory payable computed yet. Lock this month's Payroll and enter Expense GST, then run cross-module sync from the Home screen." (L1862–1883) | none | none | **MISSING** | Port (§8 Q10 about the month key). |
| 6.3 | Current-month strip | Only on the page of the current month, when that month has a feed entry and total > 0: "₹T statutory payable this month · PF · ESIC · PT · GST · estimate — verify with CA" (L1884–1895). | none | none | **MISSING** | Port. |
| 6.4 | Where the amounts come from | SCC shell bridge (not supplied): PF / ESIC / PT from **locked payroll rows**. GST estimate from Expense `gm_tax_feed[month].gstEstimate` = Σ income by category × (category rate, or the flat `gstRate` if unset) / 100 (`expense\index.html` L1870–1893). | Payroll lines already hold `pf, pfEr, esic, esicEr, pt` (`domain\streams\staff.ts:102`); GST output tax exists in invoices / GSTR-1 (`domain\gstr1.ts`); not linked to compliance | Payroll in `Hcc.Domain\Payroll\*`; `Hcc.Domain\Invoices\Gstr1Summary.cs` | **MISSING** | Build `cmpStatutoryPayable(month)` from the **locked** pay run. Employee-only or employee + employer is unknown: get the SCC bridge (§8 Q10). GST: SCC's income × rate estimate, or HCC's actual output GST (owner to choose). |
| 6.5 | CSV register | Active firm, full selected FY, visible cards. Columns **exactly**: `Firm, FY, Month, Due, Obligation, Form/Portal, Category, Frequency, Responsible, Risk, Status, Filed On, Days Left`. Status N/A / Done / Pending; Filed On = `filedOn \|\| filedRef`. Formula guard prefixes `'` when a cell starts (after whitespace) with `= + - @`. BOM. File `Compliance_<firm>_<FY>.csv` (L1971–1999). | none | none | **MISSING** | Add via `saveCsv` (native hook). |
| 6.6 | Month print | A4 landscape; heading "Compliance Calendar" plus month, year and firm; meta Firm (type) / FY / Month / Obligations / "Done d / (n − NA)" / NA. Columns Due Date, Obligation, Form / Portal, Frequency, Responsible, Status ("N/A", "✓ Done", "Pending"). Footer "n obligations · d done · p pending · k N/A" (L59–136). | none (GSTR-1 print only) | none | **MISSING** | Add via `printDocument`. |
| 6.7 | Evidence files | Attach, view and delete files per firm\|FY\|card in the shell store. Count badge "📎 n" (L2281–2316). | none | none | **MISSING** | Use HCC's documents store or a new attachment table. |
| 6.8 | Evidence completeness | Done non-NA cards are satisfied by a non-empty `evidenceRef` **or** ≥1 file. pct = round(satisfied/done×100). Missing list with "Open →" links. FY meter on the dashboard, month chip "s/d" (L2318–2400). | none | none | **MISSING** | Port. |
| 6.9 | Evidence ZIP | Active firm + FY. Folder `<Month>_<Category>_<Name>` (≤60) + `__<itemId>` (≤24); duplicate names become "name (n).ext"; `index.csv` manifest `Obligation, Original filename, Stored path, Added-on`. File `Evidence_<firm>_<FY>.zip` (L2001–2098). | none | none | **MISSING** | Port (server or client zip). |
| 6.10 | CA pack | Button "💬 Send CA pack (WhatsApp)", only when a firm is selected. It opens the shell WhatsApp composer `templateId:'ca_pack'`; content is defined in the shell, not supplied (L2105–2115, L2508). | **Simulated e-mail** to CA name / e-mail. Lines: position; every OPEN row; FILED in the last 35 days; ≤60 lines; last 20 packs kept (P-dom:117-124; P-hnd:95-101) | S-app:199-220; S-dom:291-301; `cmp.ca_pack` S-sql:78-90 | **DIFFERENT** | Switch to a simulated WhatsApp send (DEC-056 pattern) with SCC's template once obtained. |
| 6.11 | CA settings + due-soon window | none | caName, caEmail, dueSoonDays 1–30 (P-dom:21,27,128-136; P-hnd:88-93) | S-app:175-197; `cmp.setting` S-sql:68-75 | **HCC-ONLY** | Keep only if the e-mail pack stays (§8 Q12). |
| 6.12 | Access control | None inside the module. Any user who can open it can mark, undo, NA, archive, add custom and delete firms (shell runtime only). | Owner + Accountant (view, file, NA, pack); Owner (manage, reopen, settings) (P-dom:36-37; `app\navmap.ts:199`) | PERM-CMP-001 / 002 (S-port:7-14; migration 1101) | **HCC-ONLY** | Keep (DEC-055 / 058 security); record as an owner-approved exception (§8 Q6). |
| 6.13 | Audit trail | Not in module code (shell 'audit' runtime, index.html L3403). | Audit text per command (P-hnd returns) | `tx.AuditAsync` per command | **HCC-ONLY** | Keep (additive). |
| 6.14 | GSTR-1 working summary | not in SCC tax | P-ui/Gstr1.tsx; `domain\gstr1.ts` | `Hcc.Domain\Invoices\Gstr1Summary.cs`, `GET /invoices/gstr1` | **HCC-ONLY** (DEC-072) | Keep (not an SCC substitution). |
| 6.15 | GST rate master | not in SCC tax | `domain\gst.ts` (DEC-069) | `IGstRateResolver` | **HCC-ONLY** | Keep. |
| 6.16 | Settings hub / assistant / search | none | `cmpDefs` (`domain\streams\config.ts:557-567`); assistant intent; nav keywords (`app\navmap.ts:114`) | `BuiltinSettings.cs:113` pointer | **HCC-ONLY** | Keep. |
| 6.17 | Delete firm | Confirm; delete the firm, its compliance buckets and its evidence (L1235–1247). | n/a | n/a | **N/A** | Only if multi-firm is adopted. |
| 6.18 | Legacy storage migration | Wrap a flat firm bucket into the FY bucket (L28–37). | n/a | n/a | **N/A** | SCC storage housekeeping; not a business rule. |

### Verdict counts (93 rows)

| Verdict | Count | Rows |
|---|---|---|
| SAME | 9 | 1.1, 1.2, 1.4, 1.8, 1.9, 1.10, 1.20: the 7 due rules (TDS deposit incl. 30 Apr for March, GSTR-1 monthly 11th, GSTR-3B monthly 20th, EPF 15th, ESIC 15th, PT last day of next month, GSTR-9 31 Dec). 2.1, 2.2: the two due-string forms. |
| DIFFERENT | 21 | 2.4–2.8, 3.5, 3.7, 4.1–4.7, 5.7, 5.13, 5.14, 5.16, 5.18, 5.19, 6.10 |
| MISSING | 51 | 22 catalogue rows (1.3, 1.5–1.7, 1.11–1.19, 1.21–1.29). These cover **21 of SCC's 30 obligation keys**, plus the QRMP variants of GSTR-1/3B: **40 of SCC's 113 cards** have no HCC equivalent. QRMP GSTR-1/3B, CMP-08, GSTR-4, TDS returns, ESIC half-yearly, advance tax, Form 16 / 16A, both ITRs, LWF, tax audit, GSTR-9C, 3 S&E items, investment proofs, salary-TDS finalisation, ITR-U, GST reconciliation, PT annual. Plus 2.9, 3.1–3.4, 3.6, 4.8, 5.1–5.6, 5.8–5.12, 5.17, 5.20, 6.1–6.9. |
| HCC-ONLY | 9 | 2.3 holiday shift, 4.10 late marker, 5.15 cap / unique name, 6.11 CA settings, 6.12 roles, 6.13 audit, 6.14 GSTR-1 summary, 6.15 GST master, 6.16 hub / assistant / search. Dated rule versions (2.5) and the reopen trail (4.5) are also HCC-only, but are counted under DIFFERENT. |
| N/A | 3 | 4.9, 6.17, 6.18 |

---

## 7. Port plan (concrete changes)

**Principle.** Port SCC's catalogue **as data** and its functions **verbatim**: `parseDueDate`, `getVisibleItems`, `obligationKey`, `getControlProfile`, `daysLeftTag`, `taxPayableField`, `renderFYText`, `renderNewActForms`. Do not re-model them on HCC's period engine. Use golden tests to prove the C# and TS ports reproduce SCC for FY 2026–27 and FY 2027–28.

### Prototype (`03 Prototype`)

1. **New `src\domain\streams\compliance-catalogue.ts`.** Holds SCC's `D` (12 months × 113 cards) with every field verbatim: `id, cat, name, due, urgent, freq, form, who, what, why, penalty`, the month `note`, `QUARTERS`, `BUSINESS_LAB_IDS`, `FY_BASELINE_START=2024` and `NEW_ACT_FORMS=true`. Export `cmpParseDue(due, fyStart)` (= SCC L345–369), `cmpRenderFYText`, `cmpRenderNewActForms`, `cmpObligationKey`, `cmpIsPayrollItem`, `cmpGstFilingMode` and `cmpControlProfile(item, due, today)` (= L2244–2276, with `today` = business date).
2. **Rewrite `src\domain\streams\compliance.ts`.**
   - State `complianceX` becomes:
     - `profile {gstType: 'regular'|'composition'|'both'|'unregistered', qrmp, hasEmployees, entity, pan, gstin}` (pan/gstin read from the property if present);
     - `fyStart`;
     - `records {[fy]: {[itemId]: {done?, na?, filedOn?, notes?, evidenceRef?, reviewerNote?, dueDate?}}}`;
     - `custom[]` (SCC custom shape, L1382–1404);
     - `archived[]`; `archivedCats[]`;
     - `packs`.
   - Selectors: `cmpVisibleItems` (= L1613–1628), `cmpMonthItems` (= built-in + `expandCustom` L1658–1704), `cmpDaysLeft`, `cmpDaysTag` (bands 0 / 7 / 15), `cmpActionCenter` (30-day window, L1897–1917), `cmpQuarterStats` (L2481–2507), `cmpMonthStats` (L2436–2450), `cmpHasOverdueInMonth`, `cmpEvidenceCompleteness` and `cmpCsvRows` (13 columns).
   - `cmpStatutoryPayable(ym)` from the locked pay run for `ym` (staff.ts lines `pf/pfEr/esic/esicEr/pt`) plus a GST figure. Then `cmpPayableForCard(name, page)` = L1768–1786 and `cmpPayablePanel` = L1847–1883.
   - Bump `SCHEMA` 9 → 10.
   - **Migration of existing filings** (each HCC filing becomes a done record):
     - Map an HCC `(obligation, period P)` to the SCC card on page month(P+1) of the FY that contains that page.
     - TDS → ap1/my1/jn1/jl1/ag1/sp1/oc1/nv1/dc1/jn_1/fb1/mr1 for pages Apr…Mar.
     - GSTR-1 → ap2/my3/jn5/jl4/ag3/sp4/oc4/nv3/dc3/jn_3/fb4/mr5.
     - GSTR-3B → ap3/my4/jn6/jl6/ag4/sp5/oc5/nv4/dc4/jn_4/fb5/mr6.
     - EPF → ap5/my5/jn8/jl9/ag5/sp6/oc9/nv5/dc7/jn_8/fb6/mr8.
     - ESIC → ap6/my6/jn9/jl10/ag6/sp7/oc10/nv6/dc8/jn_9/fb7/mr9.
     - PT → ap7/my8/jn10/jl12/ag7/sp8/oc11/nv8/dc9/jn_12/fb8/mr10.
     - GSTR-9 for FY y → dc5 in FY y+1.
     - Filed: `filedOn` = `at` date, `evidenceRef` = reference. NA: `na:true`, reason → notes.
3. **`src\services\mock\handlers\compliance.ts`: replace the commands.**

   | Command | What it does | SCC source |
   |---|---|---|
   | `CMP_DONE {fy, itemId, filedOn, notes, evidenceRef, reviewerNote}` | all fields optional; filedOn defaults to the business date | L2402–2425 |
   | `CMP_UNDONE` | deletes the record | L2159–2172 |
   | `CMP_NA_TOGGLE` | toggles the NA flag | L2179–2195 |
   | `CMP_ARCHIVE_KEY`, `CMP_ARCHIVE_CAT` | archive by key / by category | L1465–1486 |
   | `CMP_CUSTOM_SAVE` / `CMP_CUSTOM_DELETE` | add, edit, delete custom obligations | L1376–1462 |
   | `CMP_PROFILE_SET` | qrmp only when regular / both | L1187–1216 |
   | `CMP_FY_SET` | 2026–2032 | L234–239 |
   | `CMP_CA_PACK` | simulated WhatsApp | L2105–2115 |

   Keep the role checks (Owner + Accountant; Owner for archive, custom and profile) and the audit text, pending §8 Q6. Keep a past-date refusal hook for when as-of browsing exists (L292–296).
4. **`src\features\compliance\*`.**
   - Month tabs April–March with overdue dots, opening on the business-date month.
   - The 7 stats and the "⚡ Control Note".
   - Category filter including NA.
   - Cards with: due tag (urgent red), days tag, freq, form, ≈₹ chip, QRMP advisory, filed line, control strip (risk, cash flow, escalation, internal due); expanders Ownership / Deadline-Escalation-Evidence / Input Checklist / Workflow / Reference & Penalty; a done-capture row (filed-on, notes, evidence, reviewer, files); NA toggle.
   - Dashboard tab Q1–Q4 (score, monthly rows, control summary, top-8 focus, owner load, pending list, Statutory Payable panel, evidence meter).
   - Action Center modal and badge.
   - Manage Compliance modal: categories, custom list, built-in keys with Archive / Restore.
   - Firm (property) profile modal.
   - FY selector.
   - CSV (`saveCsv`), print (`printDocument`, landscape), evidence ZIP.
   - Phone versions as cards (48 px, no sideways scroll).
   - Update the `DueCard.tsx`, `ModulesHome.tsx` badge and `mobile\frontdesk\Home.tsx` callers to the Action Center logic.
5. **Fixtures `src\fixtures\ext\compliance.ts`.** A fictional profile (regular, not QRMP, has employees); done records for FY 2026–27 pages Apr–Sep, built from the same reference pattern; keep 2–3 open items.
6. **Tests `tests\stream-compliance.mjs`.**
   - Golden table: the 113 SCC due dates for fyStart 2026 and 2027, generated once by running SCC's own `parseDueDate` on SCC's `D`.
   - Visibility matrix: 4 GST types × qrmp × hasEmployees.
   - Control-profile table for the 30 keys (risk / cash / responsible / reviewer / buffer as listed in this review).
   - Days-tag bands; Action Center 30-day window; quarter score; CSV header and formula guard; New-Act renames; archive keys (incl. the April EPF/ESIC split); custom due-day expansion (0 = last day).
7. **`src\clients\sync\accounting.ts:220,261-282`.** Remap the `CMP_*` senders to the new server commands.

### Server (`08 Production`)

1. **Migration**: next number via `tools\migrations-lock.mjs --add`.
   - `cmp.profile(property_id PK, gst_type CHECK IN ('regular','composition','both','unregistered'), qrmp BIT, has_employees BIT, entity, row_ver)`, with a CHECK that qrmp=1 only when gst_type is regular or both.
   - `cmp.record(property_id, fy_start SMALLINT, item_id VARCHAR(40), done BIT, na BIT, filed_on DATE NULL, notes NVARCHAR(200), evidence_ref NVARCHAR(120), reviewer_note NVARCHAR(80), due_date DATE NULL, PK(property_id, fy_start, item_id))`.
   - `cmp.custom_obligation(id, property_id, cat CHECK IN ('it','gst','comp','lab'), name, freq_label, due_day TINYINT 0–31, months_mask SMALLINT, form, who, what, why, penalty, responsible, reviewer, risk NULL, buffer TINYINT NULL CHECK 0–30, checklist_json, workflow_json, evidence_json)`.
   - `cmp.archive(property_id, archive_key NVARCHAR(80))`; `cmp.archive_category(property_id, cat)`.
   - `cmp.setting` + `fy_start`.
   - `cmp.evidence(...)`, or link to the documents store.
   - Data migration of `cmp.filing` → `cmp.record` using the mapping in prototype step 2. Keep `cmp.filing*` read-only for history, or drop it (owner).
2. **New `Hcc.Domain\Compliance\SccCatalogue.cs`.** Static data identical to `D` (113 cards + 12 notes + texts).
   **`ComplianceRules.cs`.**
   - Add: `ParseSccDue(string due, int fyStart)`, `ObligationKey`, `IsPayrollItem`, `Visible(profile, archived, archivedCats, item)`, `DaysTag`, `ControlProfile`, `Escalation`, `RenderFyText`, `RenderNewActForms`, `PayableField`, `QuarterStats`.
   - Remove: the holiday shift and the 1–28 limit, unless the owner keeps them (§8 Q12).
3. **`Hcc.Application\Compliance`.**
   - Commands: `MarkComplianceDone` / `UndoComplianceDone` / `ToggleComplianceNa` / `ArchiveComplianceKey` / `ArchiveComplianceCategory` / `SaveCustomObligation` / `DeleteCustomObligation` / `SaveComplianceProfile` / `GenerateCaPack` (WhatsApp, simulated).
   - Queries: `GET /compliance/month?fy&month`, `/compliance/dashboard?fy&q`, `/compliance/action-center` (30 days), `/compliance/register.csv`, `/compliance/print?fy&month`, `/compliance/payable?ym`.
   - The payable query reads the **locked** payroll run (`Hcc.Domain\Payroll\*`) and output GST from `Gstr1Summary` or the SCC estimate.
   - Keep the PERM-CMP-001/002 checks and the audit.
   - Update `Hcc.Api\Endpoints\ComplianceEndpoints.cs`.
   - Wire `SeedComplianceDefaults` into set-up, or drop it: SCC's catalogue needs no seed (closes A02b-09).
4. **Tests (`tests\Hcc.Compliance.Tests`).** The same golden tables as the prototype, shared as JSON; visibility matrix; archive keys; CSV header; payable from a locked run.

---

## 8. SCC behaviour that looks like a bug or needs an owner decision

1. **QRMP Q4 cards missing.** SCC has QRMP GSTR-1 (13th) and GSTR-3B (22nd) only for Q1–Q3 (jl5/jl7, oc6/oc7, jn_5/jn_6). The April page has no "Q4 (Jan–Mar)" QRMP cards (13 Apr / 22 Apr). QRMP firms also get no PMT-06 monthly-payment card; PMT-06 is only mentioned in the texts (L588, L1825). Add the Q4 and PMT-06 cards, or port as is?
2. **Archive key split.** Archiving "EPF Contribution" or "ESIC Contribution" leaves **April's** card visible, because its name "EPF/ESIC Contribution Deposit – March Wages" (L426, L432) gives a different key. LWF also has two keys, "Labour Welfare Fund (LWF)" (L621) and "Labour Welfare Fund" (L981). Port the quirk exactly, or align the names?
3. **Custom due day 29–31 overflows.** `new Date(y, mi, day)` (L366) with day 31 in a 30-day month gives the 1st of the next month; Feb 30/31 gives 1–3 Mar. The UI offers 1–31 (L1282). Clamp to the month end?
4. **Income-tax Act 2025 renames applied to prior-year returns.** The April note (L394) says "Returns filed during this year for the prior year continue on the earlier forms". But `renderNewActForms` renames every text, so:
   - the Q4 TDS return due 31 May 2026 (for Jan–Mar 2026, 1961 Act) shows "Form 138 / Form 140";
   - Form 16 for FY 2025–26 (15 Jun 2026) shows "Form 130", and Q4 Form 16A shows "Form 131";
   - the April note itself is mangled: "(replacing Previous Year / Assessment Year)" (L394) renders as "(replacing Tax Year / Tax Year)", because the "Assessment Year" / "Previous Year" → "Tax Year" rule (L188) also runs on the note.

   Apply the renames only to FY 2026–27 periods and spare the explanatory note?
5. **FY does not follow the date.** `fyStartYear` is stored and never auto-advanced (L25, L234–239). On 1 Apr 2027 the calendar still shows FY 2026–27. The Action Center and badges cover only the selected FY, so prior-FY overdue items disappear once the user switches. Days left use the device date, not the as-of date (L374). HCC uses the business date. Accept HCC's business date, and auto-select the FY of the business date?
6. **Recording strictness (SCC vs HCC).** In SCC, reference, reason and confirmation are not required, and any user can undo a done item, which **erases** the record (L2159–2172). HCC requires a 4–40-character reference, a 5–120-character NA reason, and an Owner reopen with reason that keeps the history. HCC also limits the module to Owner + Accountant. Under the ruling, these are "HCC differently" — keep HCC's controls as an approved exception (recommended for audit), or go back to SCC's looser rules?
7. **Risk classes by name substring.** ITR (both), Tax Audit, ITR-U and GSTR-4 are **Low** risk; GSTR-1 / GSTR-9 / GSTR-9C are Medium; "Employee Investment Proofs … (for Salary TDS)" and "Finalize Salary TDS …" are **High**, only because the name contains "TDS" (L2245–2250). Intended?
8. **Escalation shows on done cards.** The Critical / Escalate pill is computed without checking done (L2259–2262, rendered L2469). Port as is?
9. **GSTR-4 sits on the April page but is due 30 Jun** (ap4 L420). It counts in April's stats and print and puts the overdue dot on April. Move it to June?
10. **Statutory-payable month keys disagree.** The panel and strip use the **current** month's feed (wages accrued this month, deposited next month) and label it "statutory payable this month" (L1847–1894). The card chips use the **previous** month (the deposit due on this page) (L1776–1786). Also confirm from the SCC shell bridge whether PF/ESIC include the **employer** share. Please send the SCC shell (`computeTaxStatus`, `buildTodayBrief`, notifications, the bridge, the `ca_pack` template) so these rules can be ported fully.
11. **Content to confirm with the CA:**
    - ESIC half-yearly returns (11 May / 11 Nov, L485, L845): still required?
    - The ESIC / EPF / LWF / PT texts are Maharashtra-retail; a hotel may differ (e.g. Shops & Establishment vs hotel licences).
12. **HCC-only items: keep or remove?**
    - holiday shift (DEC-MA-61; inert under DEC-114);
    - dated due-rule versions;
    - 30-obligation cap;
    - simulated **e-mail** CA pack plus CA name / e-mail settings (SCC uses WhatsApp);
    - "due this week" 7-day card next to SCC's 30-day Action Center;
    - late-days in the audit text.
13. **Custom edits are retroactive in SCC** (no effective date). HCC keeps dated rules. Which one?
14. **Multi-firm.** SCC tracks several firms, each with its own GST type, QRMP and employees setting. The hotel is one property: is one profile enough, or are several GSTINs / entities needed? Note also that SCC's archive is global across firms while custom obligations can be firm-scoped.
15. **"Shops & Establishment renewal" text** says "Use the ⚙ Manage Compliance tool to move the month or day" (L918). Built-ins cannot be moved; they can only be archived and re-added as custom. Port the text as is?
