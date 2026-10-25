# RBAC mismatch rulings: proposed for owner decision

25 Oct 2026 · Status: **Proposed** (needs the owner's answer) · Source: `05 Delivery Evidence\Assembly\RBAC_Matrix_Check_2026-10-25.md` (integration worktree), test `03 Prototype\tests\stream-rbac-matrix.mjs` (KNOWN table)
Evidence: `HCC_RBAC_Matrix_v1.md` (M0, "Proposed"), `HCC_Prototype_Spec_v1.md` §1.1, `HCC_Business_Workflow_Spec_v1.md`, Decision Register (DEC-027, 031, 055, 058, 061).

**How to answer:** reply "accept all", or "accept all except G2, G6…" and give your choice for those groups.
"Doc wins" = the app gets fixed to match the access document. "App wins" = the app stays as is and the document is corrected. "New rule" = neither one; a new rule is written into both.

## Status only (no decision needed)

| ID | State |
|---|---|
| W05-1 | **Done.** Compliance is hidden from Manager and Cashier on integration (`navFilterModules(s,items,uid)` → `navRoleOk` → `cmpHasAccess`, used by App, drawer, Modules Home and search; `[PF] W05-1` test passes). The RBAC test still logs KNOWN-FAIL only because its own `surfaces()` calls `navFilterModules(s,X.navItems)` without `uid`. Test-only fix: pass `uid`, then remove W05-1 from KNOWN (S). |
| F1 | Being fixed on another branch (store who counted the day in the closing record, read it in `cbCounters`). Rule already agreed: the person who counted a day never locks it. |

## Summary (answer these)

| Group | IDs | Question | Recommendation |
|---|---|---|---|
| G1 | 12, 22, 23 | Who may give a discount, and up to how much? | **Doc wins:** Cashier gives none; Reception ≤ ₹250, Manager ≤ ₹1,000, Owner no limit; Manager approves only up to ₹1,000 |
| G2 | 14 | May Reception pay out a refund that someone else has approved? | **New rule:** yes, from their own shift, never one they asked for |
| G3 | 13 | Who may ask for a bill correction or credit note? | **New rule:** Reception, Cashier, Accountant, Manager may *ask*; Owner approves |
| G4 | 3, 15, 16 (Acct), 17, 4b (Acct) | Should the Accountant check bills, expenses and the day close (read-only) and decide cash/expense items? | **Doc wins** |
| G5 | 16 (Cashier) | May the Cashier cancel (reverse) an expense in their own shift? | **App wins:** no; Manager, Accountant or Owner reverses |
| G6 | 4, 4b (Sup, Maint) | Do the supervisor and maintenance staff enter their own purchase bills? | **App wins:** no; they hand the bill to Reception/Cashier, who enter it |
| G7 | 1, 2 | Do Supervisor, Maintenance, Cashier and Accountant need the Room board / Reservations / Calendar? | **App wins:** no; they use the Housekeeping board, folios and reports |
| G8 | 5, 6, 7 | Should Reception see the Housekeeping board, Day-closing checklist and Reports (and the supervisor Reports)? | **App wins:** no; Room board already shows clean/dirty, reports stay with Owner/Manager/Accountant/Cashier |
| G9 | 8, 9, 9b, 11 | Should the Manager see Access control, request setting changes, and see Backup? | **New rule:** no Access control page, no setting requests (DEC-058); Manager gets a "last backup" warning card |
| G10 | 10 | Who opens Approvals & audit? | **Doc wins:** Owner, Manager, Accountant; not the Cashier. Tech admin view later (M4) |
| G11 | 18, 19 | May the Tech admin change ledger mapping or company credit limits? | **Doc wins:** no, Owner only |
| G12 | 20, 21 | Money leaks into maintenance: AMC contracts by Reception/Cashier, part prices sent to Maintenance | **Doc wins:** close both |
| G13 | 24, 25 | Tally export tab and Excel/CSV report export: who? | **Doc wins:** Tally tab Owner + Accountant; Cashier cannot export |

Totals: 13 groups · **Doc wins 6** (G1, G4, G10, G11, G12, G13) · **App wins 4** (G5, G6, G7, G8) · **New rule 3** (G2, G3, G9).
Code effort if all accepted: 9 small fixes, 1 medium (G4 read-only views); App-wins groups are document edits only.

---

## Details

Paths: code = `E:\Projects\HCC\worktrees\integration\03 Prototype\src\…`; matrix = `02 Planning\HCC_RBAC_Matrix_v1.md`; spec = `02 Planning\HCC_Prototype_Spec_v1.md`. The app has 12 broad permissions (Cashier = billing, payments, expenses, reports; Accountant = reports only; `fixtures\base.ts`), so most fixes are an extra role check inside a command, not a new permission.

### G1 · Discounts (RBAC-12, 22, 23)
- **Doc says:** FOL-003 Cashier = no; §6 limits Reception ₹250 / Manager ₹1,000 / Owner unlimited; APR-001 Manager approves up to ₹1,000, above → Owner.
- **App does:** anyone with `billing` (incl. Cashier, and complaint recovery `GSV_COMPLAINT_RECOVERY`) can discount; one ₹250 threshold for everyone, so even the Owner's discount waits for approval; Manager approves any amount.
- **Ruling:** Doc wins. **Why:** the person who takes the cash should not also reduce the bill; the Owner should not need permission from staff.
- **Risk if left:** cash-and-discount by one person is the classic counter leakage; Manager can approve big write-offs alone.
- **Change (code, S):** `services\mock\handlers\folio.ts` DISCOUNT: also require `checkin` or `approve` (Owner/Manager/Reception, not Cashier); limit = `adminLimitFor(s,role,'PERM-FOL-003')` falling back to `settings.discountLimit`. APPROVAL: refuse a Manager above the Manager limit (as company credit already does). Matrix: no edit.

### G2 · Refund paid out by Reception (RBAC-14) · *owner judgement*
- **Doc says:** PAY-004 Reception = no (only Owner, Manager, Cashier execute).
- **App does:** Reception can execute an approved refund (has `payments`); the requester can never execute and the approver is a different person (DEC-031) — both already enforced.
- **Ruling:** New rule: Reception may pay out an approved refund **from their own shift's drawer** (A·S), never one they requested. **Why:** in a small hotel Reception holds the drawer most of the day and at night; three people are still involved (asker, approver, payer).
- **Risk if left as doc:** guest waits for a refund at night when no Cashier is on duty. Risk of the new rule: low, approval binding stays.
- **Change (doc, S):** matrix §5 PAY-004 REC `—` → `A·S`; §6 "Refund execution" Reception → "after approval, own shift". Code: none. Test: REFUND_EXECUTE roles + REC, remove RBAC-14.

### G3 · Bill corrections and credit notes (RBAC-13)
- **Doc says:** FOL-005 Owner A; Manager, Reception, Accountant request; Cashier no.
- **App does:** Cashier may request; Accountant refused (has no `billing`).
- **Ruling:** New rule: Manager, Reception, **Cashier** and Accountant may *request*; Owner approves. **Why:** the Cashier is often the one who spots the wrong charge at payment; a request alone moves no money. The Accountant needs credit notes for tax returns.
- **Risk if left:** Accountant must ask front desk to raise credit notes; no money risk either way.
- **Change:** doc (S) matrix FOL-005 CSH `—` → `R`. Code (S): `handlers\folio.ts` CORRECTION_REQUEST and CREDIT_NOTE accept `billing` **or** Accountant role.

### G4 · Accountant's back-office checks (RBAC-3, 15, 16-Acct, 17, 4b-Acct)
- **Doc says:** Spec §1.1 Accountant sees "Reports, Folios (read), Expenses, Day closing (read)"; FOL-001 V, CLS-001 V, EXP-001 own, EXP-002/003 A, SHF-002 A, CLS-003 A.
- **App does:** Accountant has only `reports` (an M1 shortcut): cannot open Folios, Expenses, Day closing or Customers; cannot accept a cash difference, approve/reverse expenses, enter own expense or request a closed-date correction.
- **Ruling:** Doc wins for Folios (read), Expenses, Day closing (read) and all five actions. Customers page: not needed (App wins; matrix GST-001 ACC `M` → `—`). **Why:** the Accountant is the independent checker of cash; that is the whole point of having one.
- **Risk if left:** Owner or Manager must do every check; cash differences and expenses are approved by the same people who run the counter.
- **Change (code, M):** Accountant role check (not a new broad grant, `approve` would also let them approve discounts) in `handlers\closing.ts` SHIFT_DIFFERENCE_DECIDE and CORRECTION_ADJUST, `commands.ts` EXPENSE / EXPENSE_APPROVE / EXPENSE_REVERSE (also `EXPENSE_SUBMIT` handler). Nav: any-of / role-aware entries for `folios`, `expenses`, `closing` with a view-only mode (no post/discount/close buttons). Own-shift rule (SOD-004) stays.

### G5 · Cashier reversing expenses (RBAC-16, Cashier part)
- **Doc says:** EXP-003 Cashier = allow, own shift.
- **App does:** Cashier refused (`approve` needed).
- **Ruling:** App wins. **Why:** the Cashier pays expenses out of the drawer; letting the same person undo them makes cash easy to hide. A wrong entry is reversed by Manager, Accountant or Owner.
- **Risk if left as app:** slightly slower fix of a wrong entry. **Change (doc, S):** matrix EXP-003 CSH `A·S` → `—`; test: drop CSH from EXPENSE_REVERSE.

### G6 · Staff purchase bills (RBAC-4, 4b for Supervisor/Maintenance) · *owner judgement*
- **Doc says:** EXP-001 Supervisor and Maintenance submit their own expenses.
- **App does:** they cannot open Expenses or submit (no `expenses`).
- **Ruling:** App wins: they hand the shop bill to Reception/Cashier, who enter it against the drawer. **Why:** matches how small hotels run petty cash; keeps housekeeping/maintenance completely free of money screens (only own pay, DEC-061).
- **Alternative if you prefer self-entry:** a "My bills" card on My shifts & pay showing only their own bills (code M).
- **Risk if left:** none for money; bills depend on the front desk entering them. **Change (doc, S):** matrix EXP-001 SUP and MNT `A·O` → `—` with note "submitted through Reception/Cashier".

### G7 · Room board and reservations for non-front-desk roles (RBAC-1, 2)
- **Doc says:** Spec §1.1 Supervisor "Room board (read, no guest finance)", Maintenance "Room board (blocks)"; RES-001 Cashier V, Accountant V, Supervisor masked.
- **App does:** all hidden; Spec §1.1 itself does not list reservations for Cashier/Accountant.
- **Ruling:** App wins. **Why:** the Housekeeping board already shows room status and blocks; Cashier works from folios; Accountant from reports. Fewer people see guest names.
- **Risk if left:** none. **Change (doc, S):** matrix RES-001 CSH/ACC `V` → `—`, SUP `M` → `—`; spec §1.1 Supervisor "Housekeeping (incl. room status)", Maintenance "Housekeeping (repairs, blocks), Maintenance". Test PAGES unchanged; remove RBAC-1/2.

### G8 · Reception and supervisor views (RBAC-5, 6, 7) · *owner judgement*
- **Doc says:** HK-001 Reception view; CLS-001 Reception/Cashier view the day-close checklist; RPT-001 operations reports Reception and Supervisor view.
- **App does:** all hidden.
- **Ruling:** App wins. **Why:** Room board already shows clean/dirty/inspected for Reception; day close is done by Owner/Manager and the Cashier sees drawer blockers on the Cashier page; the Reports page mixes occupancy with revenue, so keeping it to Owner/Manager/Accountant/Cashier protects business figures.
- **Risk if left:** Reception asks the Manager for occupancy figures. **Change (doc, S):** matrix HK-001 REC, CLS-001 REC/CSH, RPT-001 REC/SUP → `—` (revisit a money-free operations report at M4).

### G9 · Manager and administration pages (RBAC-8, 9, 9b, 11)
- **Doc says:** Spec §1.1 Manager views Access control; ADM-003 Manager *requests* config changes, §7 route "TAD / MGR → OWN"; ADM-005 Manager views Backup.
- **App does:** Access control, Master configuration and Backup hidden; SETTING_SET refused for Manager.
- **Ruling:** New rule: (a) no Access control page (who-works-here is on the Staff page; §9 "Staff list: MGR view" is met); (b) no config requests — DEC-058 (agreed after the matrix) names only Owner and Tech admin; Manager tells the Owner; (c) Manager and Owner get a Dashboard warning "Last backup N hours ago / failed", no Backup page. **Why:** the Manager runs the hotel when the Owner is away and must know if backups stop, but does not need system pages.
- **Risk if left:** a silent backup failure goes unnoticed while the Owner is away.
- **Change:** doc (S) spec §1.1 Manager "All except Administration (backup status on Overview)"; matrix ADM-003 MGR `R` → `—`, ADM-005 MGR `V` → "status card", §7 route "TAD → OWN". Code (S): backup-status card on Overview (`features\dashboard`, reads the backup slice). Test: drop MGR from SETTING_SET; remove RBAC-8/9/9b/11.

### G10 · Approvals & audit page (RBAC-10)
- **Doc says:** ADM-004 audit Owner, Manager, Accountant; Tech admin view; Cashier no. §9: audit before/after values Owner/Manager/Accountant only.
- **App does:** Cashier opens it (nav `p:'reports'`); Tech admin cannot.
- **Ruling:** Doc wins for the Cashier now; Tech admin view (action + time only, no amounts) deferred to M4. **Why:** the Cashier should not browse other people's approvals and money changes.
- **Risk if left:** Cashier sees every discount, refund and correction with amounts.
- **Change (code, S):** `features\admin\register.tsx` approvals nav: role-aware filter in `app\navmap.ts` `navRoleOk` (Owner/Manager/Accountant), like Compliance. Doc: matrix ADM-004 TAD note "M4".

### G11 · Tech admin and money (RBAC-18, 19)
- **Doc says:** Tech admin holds no financial action (matrix note, plan §12; DEC-058 money Owner-only); Module 14: Owner sets credit limits.
- **App does:** Tech admin can change ledger mapping (ACC_MAP_SET/RESET, `req('admin')`) and set a company credit limit directly (GRP_LIMIT_SET).
- **Ruling:** Doc wins. **Why:** changing where money is booked or how much credit a company gets is a money decision.
- **Risk if left:** a technical person can move revenue to other ledgers or extend credit without the Owner.
- **Change (code, S):** `handlers\accounting.ts` ACC_MAP_SET/RESET require `admin` + `reports` (Owner); `handlers\groups.ts` GRP_LIMIT_SET: direct set only for `admin` + `reports`, Tech admin refused. Doc: Module 20 §8 `admin` → Owner.

### G12 · Money leaking around maintenance (RBAC-20, 21)
- **Doc says:** Module 15: AMC contracts by maintenance roles with money (Owner/Manager); maintenance staff never get money fields.
- **App does:** Reception/Cashier can save AMC contracts (only `expenses` checked) without the Maintenance page; `mntVisibleOrders` sends part unit costs to the Maintenance user (page hides them).
- **Ruling:** Doc wins. **Why:** a vendor contract is a manager decision; hidden-on-screen is not hidden in the data (matters for the future server API).
- **Risk if left:** contracts signed from the wrong desk; prices readable on the maintenance phone.
- **Change (code, S):** `handlers\maintenance.ts` MNT_AMC_SAVE also `req('inspect')`; `domain\streams\maintenance.ts` `mntVisibleOrders`: drop `unitCost` (and other money fields) when `!mntCanSeeCost(s,uid)`.

### G13 · Tally tab and report export (RBAC-24, 25)
- **Doc says:** Tally export tab Owner + Accountant (Wave 0.5 placement, DEC-055); RPT-003 export Owner, Manager, Accountant; matrix §10: Cashier only own-shift finance reports.
- **App does:** Tally tab visible to anyone who opens Accounting (Manager, Cashier); Cashier sees Excel/CSV export on Reports.
- **Ruling:** Doc wins (Manager keeps Accounting but not the Tally tab). **Why:** exports carry whole-hotel revenue and guest lists out of the building; the Tally file goes to the CA via the Accountant.
- **Risk if left:** full data export by counter staff.
- **Change (code, S):** `features\accounting\Accounting.tsx` (+ `Mobile.tsx`) hide the `tally` tab unless Owner/Accountant; `features\reports\Reports.tsx` hide Excel/CSV (`exportExcel`, `downloadCsv`) for Cashier; print of own-shift cash report stays. Narrowing the Cashier's reports to own shift (RPT-002 V·S) stays on BL-024.

---

## After the owner answers
1. Doc wins / New rule code items → one small fix branch (G1, G3, G4, G9c, G10–G13); each fix flips its KNOWN entry to XPASS → delete from `KNOWN` in `tests\stream-rbac-matrix.mjs` and from the check report.
2. App wins / New rule doc items → edit `HCC_RBAC_Matrix_v1.md` §5–§7 and `HCC_Prototype_Spec_v1.md` §1.1, update the test's `PAGES`/`CMDS` to the new rule, log one DEC entry ("RBAC rulings, 25 Oct 2026") in the Decision Register.
3. W05-1: pass `uid` in the test's `surfaces()` and remove it from KNOWN.
