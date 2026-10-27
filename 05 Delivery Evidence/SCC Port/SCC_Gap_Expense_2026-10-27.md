# SCC → HCC gap review: EXPENSE module (read-only review, 27 Oct 2026)

Owner ruling (27 Oct 2026): every SCC module HCC took from must carry SCC's **entire** rule set, exactly as SCC computes it.

## Sources and how to read the references

- **SCC** = `C:\Users\Sagar\AppData\Local\Temp\claude\E--Projects-HCC\896b948a-fdcc-4c6c-baa0-d5e84f6980a5\scratchpad\scc\expense.js` (1678 lines). It is byte-identical (apart from CR/LF) to the one inline `<script>` of the original `E:\Projects\HCC\Migration data on 25 oct\HCC-Migration-2026-10-25\reference-inputs\SCC-modules\expense\index.html`: **original line = expense.js line + 384** (e.g. `addEntry` expense.js:513 = index.html:897). The other script blocks of index.html (lines 4–12, 329–332, 2068–2074) only boot the shared SCC shell runtime (`SaagarModuleRuntime.run('storage'|'safety'|'mobile'|'back'|'employees'|'audit'|'access', {moduleId:'expense', nextSteps:[{id:'tax'}]})`); they contain **no expense rules, no embedded JSON seed and no config**. The shell files they load (`shared/module-runtime.js`, `c1-operations-policy.js`…) are **not** in the reference inputs, so nothing about them can be checked. index.html lines 345–350 hold the role picker (Cashier (maker) / Accounts (checker) / Owner (approver)) and lines 353–363 the 10 tabs (Dashboard, Ledger, Udhaar, Cash Statement, Petty Cash, Vendors, Budgets, Cross-module, Month & Tax, Audit).
- **P** = `E:\Projects\HCC\worktrees\waved\03 Prototype\src\`  ·  **S** = `E:\Projects\HCC\worktrees\waved\08 Production\`

## 1. Was HCC's expense module derived from SCC? — **No.**

Evidence:
1. `02 Planning\HCC_FirstParty_Reuse_ETP_SCC_v1.md:32` — "HCC's prototype already has its own React versions of payroll, leave, **expenses** and checklists"; §3a row `expense` (line 52): "Use **ETP's C# cash book** as the base … SCC's ledger rules as **reference**".
2. `02 Planning\HCC_FirstParty_Implementation_Map_2026-10-25.md` package S04 "Cash/expense ledger": "**Original HCC ledger adapter** with E07 calculations … Document every intended divergence rather than forcing parity with retail-specific behavior."
3. `S\database\migrations\0800_expenses.sql:4` — "**Original HCC design; no donor code.**" Every server expense file cites only the prototype (`closing.ts` EXPENSE/EXPENSE_SUBMIT/EXPENSE_APPROVE/EXPENSE_REVERSE) and the HCC RBAC matrix; `grep -i scc` over `08 Production\src` finds no expense or cash-book file.
4. HCC expense rules come from HCC's own spec: `02 Planning\HCC_Master_PRD_v1.md:197-201` (REQ-EXP-001..005), `HCC_Business_Workflow_Spec_v1.md:113-123` (WF-EXP-001), `HCC_RBAC_Matrix_v1.md:81-83` (PERM-EXP-001..003), DEC-063 G4/G5, DEC-065, DEC-MA-3/3.1/3.3/6/11a.
5. The **only** SCC-expense material HCC took is the **cash book** idea (W05-D, `02 Planning\agents\W05_Briefs.md:27`: "Rules referenced from SCC `expense` ledger and ETP `CashBookRepository.cs` … (read-only)"; `P\domain\streams\cashbook.ts:4-5`: "running balance, locked days"). Even that was re-designed (lock by a checker who is a different *person* from the counter, Owner reopen) rather than ported; the server cash book (`S\src\Hcc.Domain\Closing\CashBookCalculator.cs:18-27`) says "ported from the prototype".

So under the 27 Oct ruling HCC carries **almost none** of SCC's expense rule set. SCC's "Expense Manager" is really a **central financial ledger** (income + expense, cash statement, petty cash, vendors, budgets, recurring, cross-module feed, month/tax lock, udhaar receivables, audit). HCC spreads the few equivalents across Expenses (SCR-13), Cashier shift, Cash book, Day closing, Accounting and Groups/company credit.

## 2. Verdict counts

| Verdict | Rows |
|---|---|
| SAME | 9 |
| DIFFERENT | 42 |
| MISSING in HCC | 36 |
| HCC-ONLY | 10 |
| **Total rules compared** | **97** |

## 3. Rule-by-rule table

Legend: SCC lines are expense.js lines. "P" = prototype, "S" = server. Money in SCC is rupees as JS floats (`num()` = parseFloat, expense.js:41); HCC is integer paise.

### 3A. Ledger entry (add)

| # | Rule | SCC behaviour (line) | HCC prototype | HCC server | Verdict | What to change |
|---|---|---|---|---|---|---|
| L01 | One ledger for income **and** expense | `gm_expenses` holds both; `type` = 'income' else 'expense' (5, 110); Add form has ＋Income / －Expense toggle (371) | Expenses only (`P\domain\types.ts:159`); income comes from folio/POS lines | `ops.expense` expense only (`S\database\migrations\0800_expenses.sql:11-35`) | DIFFERENT | Owner to decide if manual income entries are wanted (see L03). If yes add `type` + income rows that never touch folio revenue. |
| L02 | Expense categories | Fixed 10: Inventory, Rent, Salary, Utilities, Marketing, Bank Charges, Transport, Repairs, Statutory, Miscellaneous (29) | Owner-edited master seeded Supplies, Laundry, Maintenance, Utilities, Petty cash (`P\fixtures\ext\masters-seed.ts:124-130`; `P\domain\streams\closing.ts:27`) | Master `ops.expense_category` (seed `S\database\migrations\2830_money_masters.sql:146-151`) but the expense command accepts **any free text ≤60** and `/expenses/options` returns the hard-coded 5 (`S\src\Hcc.Domain\Expenses\ExpenseRules.cs:31,58-60`; `S\src\Hcc.Application\Expenses\ExpenseCommands.cs:282-288`) | DIFFERENT | Seed SCC's 10 heads (with ledgers) in the category master on both sides; ask whether HCC's 5 stay as extra heads. Server must validate against the master (see §4). |
| L03 | Income categories | Retail Sale, Service Income, Other Income (30); default 'Other Income' (118) | none | none | MISSING | Only if L01 = yes. |
| L04 | Payment modes | Cash, Bank, UPI, Card (31) | Expense funding = Payment-methods master usable at 'expenses': Cash, UPI, Bank transfer, Vendor credit; **Card not usable for expenses** (`P\fixtures\ext\masters-seed.ts:116-122`) | Same via `PaymentMethodBook.Pick` (`ExpenseRules.cs:62`); method column NVARCHAR(60) (`2890_payment_method_records.sql:15-19`) | DIFFERENT | Add `expenses` to Card's `usableAt` (seed + migration); map SCC "Bank" = "Bank transfer". Vendor credit is HCC-ONLY (A14). |
| L05 | Legacy normalisation | Unknown mode → regex: /cash/→Cash, /upi/→UPI, /card/→Card, else Bank (109); missing category → Miscellaneous/Other Income (118); `desc`/`d` aliases (127) | none | none | MISSING | Needed only if SCC data is ever imported; then implement exactly in the importer. |
| L06 | Only Cash moves the cash count | `isCash = mode==='Cash'`; Bank/UPI/Card go to bankIn/bankOut (228-230) | Drawer = method that settles to a till (`P\domain\streams\closing.ts:76`, `mnyIsDrawer`) | `PaymentMethodBook.SettlesToTill` (`ExpenseCommands.cs:108`) | SAME | — (same outcome with the seeded methods). |
| L07 | Firm (legal entity) on every entry | Firm select from org master, fallback list of four firm names (377, 405; names omitted here); CSV 'Unassigned' when blank (660) | none (one property) | `property_id` only | MISSING | Ask owner: does the hotel book expenses for more than one firm? If yes add `firm` field + per-firm P&L (M01). |
| L08 | Store / cash-drawer tag | Optional; whitelist **WLMHW / HEMW only** (124, 586, 691, 808), else Unassigned; options from org master (247-257) | Single drawer | Cash expense needs `TillId` on approval (`ExpenseCommands.cs:113-118`) | DIFFERENT | Map store → till; let the submitter pick the till (optional, "Unassigned") as SCC does, not only the approver. |
| L09 | Vendor / payee | **Optional** free text with datalist (379) | **Required** ("Enter a vendor.", `P\services\mock\handlers\closing.ts:111`) | Required, ≤120 (`ExpenseRules.cs:55-57`) | DIFFERENT | Make vendor optional on both sides (SCC). |
| L10 | Description | **Required** ("Description is required", 385, 519) | No description field; Notes optional ≤300 (`P\services\mock\handlers\closing.ts:126`) | `notes` NVARCHAR(300) NULL (`0800_expenses.sql:22`) | DIFFERENT | Add required `description` (P + S; migration column), keep Notes optional. |
| L11 | Notes | Optional (386) | Optional, ≤300 | Optional, ≤300 | SAME | — |
| L12 | Entry date | User picks any date (default = viewed day, 373); refused only if that day is closed (521) or month tax-locked (523-524); **no future-date check** | Always the business date; approval re-dates to the approval day (`handlers\closing.ts:125,137,149`) | Same (`ExpenseCommands.cs:48-51,74,104,124`) | DIFFERENT | Add a `date` field: allow back-dating to any open, non-cash-book-locked date; do not re-date on approval. Ask owner about future dates (bug B5). |
| L13 | Amount | > 0 (518), step 0.01, **no maximum** | Integer paise > 0, ≤ ₹10,00,000 (`P\services\mock\commands.ts:28`; form max 1000000, `P\features\expenses\ExpenseModal.tsx:26`) | > 0, ≤ ₹10 crore (`S\src\Hcc.Domain\Money\Paise.cs:35-39`) | DIFFERENT | Lift the prototype cap to the server's ₹10 crore (SCC has none); keep paise. |
| L14 | Bill photo | Optional `capture="environment"` (380); file > 4 MB → toast "Photo too large (max 4MB)" and saved **without** photo (458); scaled to max width 1000 px, JPEG quality 0.7 (463-466); stored as `@photo:<pid>` via photo store else inline base64 (538) | Receipt = file-name text only, nothing uploaded (`ExpenseModal.tsx:27`; `types.ts:172-173`) | `receipt NVARCHAR(120)` text only (`0800_expenses.sql:23`) | MISSING | Add bill-photo upload with SCC's exact limits (4 MB, 1000 px, JPEG 0.7) using the server document store; prototype keeps a data URL. |
| L15 | View / replace photo | 📎 button opens the photo (434, 495-512); edit may replace it, editLog records "(changed)" not bytes (565, 589) | none | none | MISSING | With L14 and E01. |
| L16 | GST on the expense bill | Expense-only optional "GST rate (%)" and "GST on bill (₹)" (381-384, 517); stored only when > 0 (112, 125-126) | none | none | MISSING | Add `gstRateBps`, `gstPaise` to Expense (P type + S columns), expense rows only. |
| L17 | GST prefill | When amount > 0, rate > 0 and GST box empty: GST = round(amount × r ÷ (100 + r), 2 dp) (445-448) — GST-inclusive bill | none | none | MISSING | Port the formula exactly (in paise: round half-up of amount×r/(100+r)). |
| L18 | GST > amount | confirm "GST amount (…) is more than the entry amount (…). Save anyway?" — warn, not block (528; edit 573) | none | none | MISSING | Warning + confirm on add and edit. |
| L19 | Vendor GSTIN hint | GSTIN of the typed vendor shown under the payee box (450-455) | Shown under the vendor box (`P\features\money\masters.tsx:47`) | n/a (UI) | SAME | — |
| L20 | Duplicate check | Non-void entry with **same date + abs(amount difference) < 0.01 + same description (case-insensitive)** → `confirm` "A similar entry already exists… Save anyway?"; Cancel aborts (526-527) | Same **vendor** + amount + date, excluding rejected and reversal records; warning only, never blocks (`P\domain\streams\closing.ts:125-127`; `ExpenseModal.tsx:30`) | Same as P, returned as `DuplicateOf` (`ExpenseRules.cs:86-94`) | DIFFERENT | Switch the key to date + amount + description and make it a confirm (Cancel stops the save). |
| L21 | Vendor list auto-add | A new payee is **added to the vendor list automatically** on save (case-insensitive) (533; recurring 878) | Kept as typed; only the Owner can add it with one tap (`P\features\money\masters.tsx:41-48`; DEC-MA-3.3) | No vendor lookup at all | DIFFERENT | Conflicts with DEC-MA-3.1 (vendor list Owner-only). Ask owner. |
| L22 | Closed day refuses entries | "That day's cash statement is locked. Owner must reopen it first." (521) | `open()` DAY_CLOSED (`P\services\mock\commands.ts:29`) | `EnsureBusinessDayOpenAsync` (`ExpenseCommands.cs:49`) | SAME | Keep once back-dating (L12) exists: check the chosen date. |
| L23 | Past-date read-only guard | `isPast()` = viewed day < real today → every write refused/hidden (55-57; 520, 545, 613, 771, 830, 1095, 1225, 1487…) | No view-date rail; writes only on the open business date | Same | DIFFERENT | Equivalent outcome for past days; becomes relevant once back-dating is added (writes then gated by day-closed, not by view date). |
| L24 | Voucher numbering | None: ids `e_<base36 time>_<5 random>` (39) | Sequential `EXP-<n>` (`P\services\mock\commands.ts:26`) | `EXP-<identity>` (`ExpenseCommands.cs:221`) | DIFFERENT | Keep HCC numbering (SCC has no rule to port). |
| L25 | Who may add | Any role (`can('entry')` = true, 86; addEntry has no role check) | Reception, Cashier, Manager, Owner, Accountant (`P\domain\streams\rbac.ts:46`; `P\fixtures\base.ts:41-45`) | PERM-EXP-001 OWN/MGR/REC/CSH/ACC (`0101_access_rbac_seed.sql:167-171`) | DIFFERENT | SCC has only 3 roles, all may add; HCC's set covers them. Ask whether HK/maintenance should add (DEC-063 G6 says no). |

### 3B. Approval and status

| # | Rule | SCC behaviour (line) | HCC prototype | HCC server | Verdict | What to change |
|---|---|---|---|---|---|---|
| A01 | New entry status | Manual and recurring entries saved `status:'pending'` (531, 876) | 'Pending' (or 'Draft') (`handlers\closing.ts:125`) | Same (`ExpenseCommands.cs:50`) | SAME | — |
| A02 | **What counts** | Every **non-void** row counts at once — pending or approved — in cash statement, dashboard, budgets, month P&L and tax feed (`computeDay` 225; 317; 1274; 1415; 1492) | Only Posted/Reversed count; drawer cut once **on approval** (`P\domain\streams\closing.ts:123`; REQ-EXP-003, AC-015) | Same; drawer movement written in `DecideAsync` (`ExpenseCommands.cs:113-118`) | DIFFERENT | Core conflict with REQ-EXP-003 / AC-015. Owner must choose (bug B1). If SCC wins: count Pending in drawer/reports from submission and make approval a lock only. |
| A03 | Who approves an entry | **Super Admin only** (shell owner session, `isSuperAdmin()` 83, 650); no amount threshold; no maker≠checker rule; `approvedBy` = role (653) | Owner, Accountant unlimited; Manager ≤ ₹5,000 or category limit; never your own (`handlers\closing.ts:140-151`; `P\domain\streams\closing.ts:17-20`) | PERM-EXP-002 OWN/ACC A, MGR L 500000 (`0101_access_rbac_seed.sql:172-174`); own refused (`ExpenseRules.cs:137-141`; `CK_expense_sod`) | DIFFERENT | Under the ruling: Owner-only approval, no limit. Conflicts with DEC-063 and PERM-SOD-001. Ask. |
| A04 | Approval effect | Approval **locks** the entry (no edit/void) and nothing else (424-427, 547, 616); allowed on a closed day or tax-locked month (no check, 649-656) | Approval **posts** (status Posted) and moves the drawer; refused if the date's cash book is locked (`handlers\closing.ts:144`) | Same + till + cash-book check (`ExpenseCommands.cs:105-110`) | DIFFERENT | Follows A02. |
| A05 | Entries that skip approval | Petty disbursements and cross-module postings are created **without status** → never pending, never approval-locked (1236, 1397; legacy rule 133-135) | Every expense needs approval | Same | DIFFERENT | Port: petty / feed rows post directly (see P03, F01). Ask owner (bug B3). |
| A06 | Draft | none | Draft → own submit (`handlers\closing.ts:131-138`) | `SubmitDraftAsync` (`ExpenseCommands.cs:59-79`) | HCC-ONLY | Keep or drop — owner. |
| A07 | Reject | none (only void) | Reject with optional reason (`handlers\closing.ts:149-150`) | `DecideAsync` approve=false (`ExpenseCommands.cs:81-153`) | HCC-ONLY | Keep (no SCC rule against it) or replace by void. |
| A08 | No self-approval | none | `e.actor!==uid` (`handlers\closing.ts:145`) | `ExpenseRules.cs:140`, `CK_expense_sod` (`0800_expenses.sql:34`) | HCC-ONLY | Owner to confirm it may stay (SCC has none). |
| A09 | Manager limit / category limit | none | ₹5,000 hotel limit, category `approvalLimit` override (`P\domain\streams\money-master.ts:139-140`) | Policy limit only (500000); category override **not** applied | HCC-ONLY | Owner. |
| A10 | Bill number required per category | none | `needsBill` (`handlers\closing.ts:124,136`; `money-master-specs.ts:97`) | Master stores it; expense command does **not** enforce it | HCC-ONLY | Owner. |
| A11 | Cash approval needs open shift / till | none | `assert(s.shiftOpen)` for drawer methods (`handlers\closing.ts:148`) | `TillId` required (`ExpenseCommands.cs:115`) | HCC-ONLY | — |
| A12 | Day close blocked by pending expenses | Not blocked (closeDay 1101-1124 ignores status) | Blocked (`handlers\closing.ts:175`) | Blocked (`S\src\Hcc.Domain\Closing\CloseRules.cs:61-75`) | HCC-ONLY | Under A02-SCC this block becomes unnecessary. Owner. |
| A13 | Accounting voucher per expense | none (only monthly tax feed) | Dr category ledger / Cr funding ledger (`P\domain\streams\accounting.ts:145-148`) | Outbox `ExpenseEvents.Posted/Reversed` (`ExpenseCommands.cs:134-148,192-211`) | HCC-ONLY | Keep; add GST input entry once L16 exists. |
| A14 | Vendor credit funding | none | 'Vendor credit' = no drawer effect (`masters-seed.ts:121`) | Same | HCC-ONLY | Keep (no SCC equivalent). |

### 3C. Edit, void, month lock

| # | Rule | SCC behaviour (line) | HCC prototype | HCC server | Verdict | What to change |
|---|---|---|---|---|---|---|
| E01 | Edit an entry | Edit modal: amount (must be > 0 and finite), category, mode, store, GST rate/amount (expense), description, bill photo; **reason required**; `editLog` push {at, by(role), old, new, reason} (old/new = amount, category, mode, store, description [+gst on expense, + photo marker]) (544-611). Vendor, firm, notes, date and type are **not** editable. No role check. Budget alert after (598) | **No edit** at all (commands: EXPENSE, EXPENSE_SUBMIT, EXPENSE_APPROVE, EXPENSE_REVERSE only) | No edit command | MISSING | Add `EXPENSE_EDIT` / `EditExpense` with SCC's field set, reason, edit log table, same gates (E02, E06). |
| E02 | Edit/void blocked when… | approved ("Approved entry is locked", 547, 616), day closed ("Day locked — reopen first", 548, 617), past view (545, 613) | Reverse only Posted, blocked when the date's cash book is locked (`handlers\closing.ts:157-158`) | `EnsureCanReverse` + `EnsureCashBookOpenAsync(e.BusinessDate)` (`ExpenseCommands.cs:167-172`) | DIFFERENT | Port SCC gates for edit/void. |
| E03 | Void | Soft void: `void=true`, reason kept, row **removed from every total on its own date** ("Entry voided (kept for audit)") (612-646) | Reverse: original stays counted on its date (status Reversed) + a **negative Posted record on today's date** (`handlers\closing.ts:154-165`) | Same (`ExpenseCommands.cs:155-214`) | DIFFERENT | Add true void for un-approved entries (SCC); keep reversal only if owner wants it for posted rows. |
| E04 | Void / mismatch / reopen reasons | Canned list: 'Cash shortage correction', 'Duplicate entry', 'Missed receipt', 'Wrong payment mode', 'Voucher correction', 'Other' + optional note; stored "reason — note" (33, 625, 641) | Free text, required (`ExpenseModal.tsx:43`) | Free text ≤300 required (`ExpenseRules.cs:77-83`) | DIFFERENT | Use SCC's six reasons + note. |
| E05 | Who may void | cashier, accounts, owner = **all three roles** (87) | Reverse: Owner, Manager, Accountant; **not Cashier** (DEC-063 G5; `rbac.ts:48`) | PERM-EXP-003 OWN/MGR/ACC (`0101_access_rbac_seed.sql:175-177`) | DIFFERENT | SCC lets the cashier void. Conflicts with DEC-063 G5. Ask. |
| E06 | Month tax-lock gate + Owner override | Month locked when `gm_tax_feed[YYYY-MM]` exists or any statement day of it has `monthLocked` (188-197). Add/edit/void/petty/recurring into a locked month: non-Owner refused "Month … is tax-locked … Owner override required."; Owner must give a reason (void: the free-text note is required), pass step-up re-auth, then audit `locked-month-override` and mark that month's feed stale (207-218, 522-542, 552-553, 575, 620-643, 841-859, 1232-1242). Re-checked at commit (TOCTOU, 597, 631). Fails **open** on storage error (196) | No month lock (accounting locks are per date, snapshot + drift warning; `P\domain\streams\accounting.ts:103-105,232-237`) | `LockAccountingPeriod`/`UnlockAccountingPeriod` (`S\src\Hcc.Application\Accounting\AccountingService.cs:73-74`) — does not gate expense writes | MISSING | Add a month tax-feed lock (M04) that gates expense writes exactly as SCC, with Owner override reason, re-auth, audit and stale flag. |

### 3D. Daily cash statement (HCC: cashier shift, cash book, day close)

| # | Rule | SCC behaviour (line) | HCC prototype | HCC server | Verdict | What to change |
|---|---|---|---|---|---|---|
| C01 | Expected closing | opening + cash income − cash expense (238) | opening + receipts − payments (`P\domain\streams\cashbook.ts:59-60`; `closing.ts:113`) | `CashBookCalculator` (`S\src\Hcc.Domain\Closing\CashBookCalculator.cs:18-27`) | SAME | — (HCC has more cash sources: deposits, POS, refunds). |
| C02 | Opening carry-forward | Walk back **up to 90 days** to the latest **closed** day; opening = **max(physical count − bank-deposit count, 0)**; if none: that day's stored `openingBalance`, else ₹0 (235-237) | Opening = counted cash at the last close (`handlers\closing.ts:191`); cash book carried = counted − banked (`cashbook.ts:65`); no 90-day cap, no floor at 0 | Per till from the last count (`CashBookCalculator.cs`) | DIFFERENT | Port the 0-floor and 90-day rule if the owner wants them (bug B12). |
| C03 | "Previous day not closed" warning | Banner when no closed day found and opening = 0 (926, 974) | Cash book shows "Opening differs from previous carry" gaps (`cashbook.ts:84,131`) | — | DIFFERENT | Add SCC's banner text/condition. |
| C04 | Denominations | ₹2000, 500, 200, 100, 50, 20, 10, 5, 2, 1 — count of each, for "Physical cash counted" **and** "Of which deposited to bank" (32, 1079-1085) | ₹500, 200, 100, 50, 20, 10 + coins as an amount (`P\domain\streams\closing.ts:22,117-118`) | Same (`S\src\Hcc.Domain\Payments\CashDrawer.cs:94-104`) | DIFFERENT | Use SCC's 10 denominations; add the bank-deposit grid (HCC records banking as a movement instead). |
| C05 | Balanced tolerance | Balanced when abs(counted − expected) **< ₹1** (326, 914, 1106) | Exact to the paisa (`handlers\closing.ts:73-79`) | Exact (`S\src\Hcc.Application\Payments\CashierService.cs:147`) | DIFFERENT | Ask owner (₹1 tolerance vs exact). |
| C06 | Close the day | Roles cashier or owner (88); count = 0 → confirm "No physical cash counted. Close anyway?" (1105); mismatch ≥ ₹1 → reason (C04 list + detail) and close goes through — **no approval of the difference** (1106-1117); stores openingBalance, closedAt/By, approved=false (1119-1124) | Count by 'payments' roles; any difference → reason **and** a cash-difference approval (Manager ≤ ₹200, Owner/Accountant above; not the counter) (`handlers\closing.ts:61-107`; `closing.ts:16`); then DAY_CLOSE by 'close' roles (`handlers\closing.ts:167-186`) | Same (`CashierService.cs:133-160,220-253`) | DIFFERENT | Owner to choose: SCC (reason only, then checker approves the whole day) vs HCC (difference approval). |
| C07 | Closed day is locked | No add/edit/void dated on a closed day until Owner reopens (521, 548, 617, 834) | Closed business date refuses writes (`commands.ts:29`); cash-book lock refuses decisions/reversals on that date (`cashbook.ts:116`) | `EnsureBusinessDayOpenAsync`, `EnsureCashBookOpenAsync` | SAME | — |
| C08 | Day approval (checker) | accounts or owner; **"Cashier cannot approve own day"** is a *role* check — the owner may close and approve the same day (1125-1131) | Cash-book lock by Owner/Manager/Accountant who is a **different person** from whoever counted/closed (`cashbook.ts:17-18,103-112`) | `LockCashBookDay` (`S\src\Hcc.Application\Closing\ClosingService.cs:34`) | DIFFERENT | Owner (bug B2). |
| C09 | Days awaiting approval | Dashboard banner "N closed day(s) awaiting checker approval" (first 5 dates) (351); Month close banner (1441); list = all closed && !approved days (1523-1525) | No such banner | none | MISSING | Add banner (fix the month filter — bug B7). |
| C10 | Reopen a day | Owner only; reason (C04 list) + detail; resets closed **and** approved (1132-1143) | Owner reopens cash-book day with reason (`cashbook.ts:19,113`); DAY_REOPEN Owner (`handlers\closing.ts:198-200`) | `ReopenCashBookDay` (`ClosingService.cs:35`) | SAME | Use SCC reason list (E04). |
| C11 | Per-store drawers | Soft per-store overlay: own count, close, approve, reopen per store; does **not** lock the business day; untagged rows only in the business view (244-281, 957-1004, 1145-1200) | Single drawer | Per-till shifts and counts | DIFFERENT | Map store = till; add the "untagged rows only in business view" rule. |
| C12 | Print / WhatsApp statement | Cash rows only; empty state "No cash statement for this date."; carry line = counted − deposit (1005-1072) | Cash book CSV (`cashbook.ts:125-141`), day-pack PDF | Cash book queries | DIFFERENT | Add SCC-layout print. |
| C13 | Non-cash excluded | Banner "Non-cash today — Bank in … · Bank/UPI/Card out …" (937) | Report pack non-cash metrics (`P\domain\streams\closing.ts:383-384`) | `ExpenseTotals` (`ExpenseRules.cs:97-113`) | SAME | — |

### 3E. Petty cash

| # | Rule | SCC behaviour (line) | HCC prototype | HCC server | Verdict | What to change |
|---|---|---|---|---|---|---|
| P01 | Float (imprest) | "Set / top-up float": amount > 0 else "Enter amount"; float += amount; history {at, by, add}; any role; audit `petty.float` (1215-1216, 1225) | none ("Petty cash" is only a category, `masters-seed.ts:129`) | none (category seed only) | MISSING | New petty slice/table + command. |
| P02 | Balance and status | Disbursed = Σ amount of **all-time** non-void rows with `source:'petty'`; Balance = float − disbursed; status "Replenish soon" when balance < 15 % of float and float > 0, else "Healthy" (1206, 1210-1213) | none | none | MISSING | Port exactly. |
| P03 | Disbursement | Amount > 0; category from the 10 heads; posts a **Cash expense dated today**, description "Petty: <For, or 'sundry'>", `source:'petty'`, `sourceRef:'pty_…'`, **no status** (no approval); month-lock override; audit `petty.out` (1218-1223, 1226-1243). **No closed-day check** (bug B3) | none | none | MISSING | Port; owner decides on approval and the closed-day check. |

### 3F. Vendors

| # | Rule | SCC behaviour (line) | HCC prototype | HCC server | Verdict | What to change |
|---|---|---|---|---|---|---|
| V01 | Vendor list | {name, gstin}; add by any role; duplicate name (case-insensitive) → "Already exists"; GSTIN upper-cased; soft check `^[0-9]{2}[A-Z0-9]{13}$` → warn "GSTIN looks unusual (expected 15 characters) — saved anyway" (44, 1252, 1260) | Owner-only master; strict GSTIN `^\d{2}[A-Z]{5}\d{4}[A-Z][1-9A-Z]Z[0-9A-Z]$` **blocks**; PAN-in-GSTIN cross-check (`P\domain\streams\money-master-specs.ts:27-28,104-127`) | Same strict rules (`S\src\Hcc.Domain\MoneyMasters\MoneyMastersRules.cs:248-252`) | DIFFERENT | Ruling says SCC's soft check; conflicts with DEC-MA-3.1/DEC-068. Ask. |
| V02 | Inline GSTIN edit | Per-row input, any role, audit `vendor.gstin` (1256, 1262-1268) | Master edit, Owner only | PERM-MST-002 | DIFFERENT | Role conflict (DEC-MA-3.1). |
| V03 | Remove vendor | Any role, no in-use check; ledger rows keep the text (1269) | Retire/delete Owner only with usage check (`money-master.ts:181-183`) | Same | DIFFERENT | Owner. |
| V04 | Vendor spend report | Per vendor: total spend (expense rows only, non-void) and entry count (1247-1257) | Usage count only | none | MISSING | Add spend total + count to the vendor list. |
| V05 | PAN, TDS flag, type, contact, payment terms | none | `MNY_EV_SPEC` (`money-master-specs.ts:104-127`) | `MoneyMastersRules.cs:87` | HCC-ONLY | Keep. |

### 3G. Budgets

| # | Rule | SCC behaviour (line) | HCC prototype | HCC server | Verdict | What to change |
|---|---|---|---|---|---|---|
| B01 | Monthly budget per head | One cap per expense head per month; written to the **current real month**; any role; empty input = 0 = "no budget"; audit `budget.set` (1272-1291) | none | none | MISSING | New `budgets` slice/table; owner decides role (DEC-058 suggests Owner). |
| B02 | Budget status | Actual = Σ non-void expense rows of the head in the month (pending included); Used % = round(actual ÷ budget × 100); "over ₹(actual−budget)" when % > 100, "near limit" when % ≥ 85, else "ok"; no budget → "no budget" (1274-1287) | none | none | MISSING | Port exactly. |
| B03 | Entry-time alert | After add, edit (expense) and recurring post: only for the current real month and budget > 0; % = round(actual ÷ budget × 100); < 90 silent; ≥ 90 warning, ≥ 100 error; text "<head> now <pct>% of <Mon> budget"; shown 1.6 s after the save toast (1297-1320, 537, 598, 882) | none | none | MISSING | Return the alert in the command result; UI shows it. |
| B04 | Copy last month | Copies only heads with a value > 0 last month **and** no value > 0 this month; "No budgets found for <prev>" / "Every head already has a budget"; confirm; audit `budget.copy` (1321-1340) | none | none | MISSING | Port exactly. |

### 3H. Recurring expenses

| # | Rule | SCC behaviour (line) | HCC prototype | HCC server | Verdict | What to change |
|---|---|---|---|---|---|---|
| R01 | Templates | name (required), typical amount (> 0), due day 1–31 (invalid → 1), category (unknown → Miscellaneous; form default Rent), mode (unknown → Cash), vendor, firm, store, active; add/edit/activate/delete by accounts or owner; same-name confirm "A template named … already exists. Add another?" (680-704, 754-828, 92) | none | none | MISSING | New template table + CRUD commands. |
| R02 | Due rule | Due when active **and** no non-void row with `sourceRef = 'rec:<id>:<YYYY-MM>'` this real month **and** today's day ≥ min(due day, days in month) (day-31 rent is due on 28/29 Feb) (705-725) | none | none | MISSING | Port exactly. |
| R03 | Dashboard nudge | "N recurring expense(s) not yet posted this month: <first 4 names>…" + Post now; hidden in past view (726-733) | none | none | MISSING | Add to Expenses/Overview. |
| R04 | Post | Any role; date = **today**; amount editable (> 0, finite); if already posted this month shows count and sum and "Confirming will post ANOTHER entry"; row: expense, category/mode/vendor/firm/store from template, description "<name> — <YYYY-MM>", `source:'recurring'`, `status:'pending'`; vendor auto-add; budget alert; day-closed and month-lock gates (re-checked at commit) (829-884) | none | none | MISSING | Port exactly. |
| R05 | Delete template | Confirm "…Ledger entries already posted from it are kept." (821-828) | none | none | MISSING | Port. |

### 3I. Cross-module feed

| # | Rule | SCC behaviour (line) | HCC prototype | HCC server | Verdict | What to change |
|---|---|---|---|---|---|---|
| F01 | Payroll → expense | Per firm Σ (gross, else salaryAmount); **Salary** expense, mode **Bank**, dated **YYYY-MM-28**, ref `pay_<year>_<month>_<firm>` (firm default 'GM') (1350-1355) | Payroll goes to accounting, not to expenses | Pay-run journal Dr salaries / employer PF / ESIC… (`S\src\Hcc.Domain\Accounting\AccountingSource.cs:144-150`) | DIFFERENT | If ported into the expense register, stop the accounting double count (salary would be both a payroll journal and an expense voucher). Ask. |
| F02 | Stock GRN → expense | GRN value per store/day → **Inventory** expense, mode **Bank**, ref `grn_<store>_<date>` (1356-1360) | `INV_GRN` stores unit cost, posts no expense (`P\services\mock\handlers\inventory.ts:19-30`) | none | MISSING | Add GRN-value feed (category Inventory) or a vendor-payable journal. |
| F03 | WSC / QMS → income | Closed service cases → Service Income; QMS purchases → Retail Sale income (1346-1349, 1361-1366) | none (no such modules) | none | MISSING | Not applicable to the hotel unless the owner names an equivalent. |
| F04 | Feed posting rules | Pending items listed per source; "Post n to ledger" any role (not past view); de-duplicated by `sourceRef`; rows `createdBy 'sync:<role>'`, **no status**, **no closed-day check**; posting into a tax-locked month marks that feed stale + audit `feed.stale` (1343-1410) | n/a | Outbox events, idempotent to accounting | DIFFERENT | Port with F01/F02. |

### 3J. Month close and tax feed

| # | Rule | SCC behaviour (line) | HCC prototype | HCC server | Verdict | What to change |
|---|---|---|---|---|---|---|
| M01 | Month summary | Income, expense, net P&L, entry count; heads "＋ <income cat>" / "－ <expense cat>"; **per-firm P&L** table (1414-1454) | Reports pack by funding source (`P\domain\streams\closing.ts:383-388`); category totals only in the assistant (`P\domain\streams\assistant.ts:154`) and accounting ledgers | Reporting + accounting | DIFFERENT | Add month view with per-head and per-firm totals. |
| M02 | GST estimate on income | Σ income per category × rate ÷ 100; rate = per-category rate (`gm_gst_rates`) else default `gm_settings.gstRate` (0 after migration, 156); junk → default; blank clears; explicit 0 = 0 %; only Owner edits (1419-1450, 1473-1485) | Exact per-line GST from the dated GST master (`P\domain\gst.ts`, DEC-069) | `GstMaster.cs` | DIFFERENT | Conflict: HCC's statutory engine is exact. Ask owner before porting an estimate. |
| M03 | GST paid on purchases | "GST paid on purchases (from entries — input-credit hint)" = Σ `gstAmount` of the month's non-void expense rows (1430, 1451, 1503) | none | none | MISSING | Needs L16; show in month close and feed. |
| M04 | Generate & lock tax feed | Owner only; refused for a future month or bad month "Pick the current or a past month — a future month cannot be locked"; stores {month, income, expense, net, gstRate, gstEstimate, byCategory, gstRates, gstEstimateByCat, gstPaidOnPurchases, generatedAt, by}; stamps every existing statement day of the month `monthLocked`; **no unlock** (1486-1516) | Per-date accounting lock by Owner/Manager/Accountant after the day is closed; unlock exists (`accounting.ts:103-105,232-237`) | `LockAccountingPeriod` / `UnlockAccountingPeriod` | DIFFERENT | Add the month feed + lock (drives E06). |
| M05 | Stale feed | Stale when stored `stale` flag set (only `markFeedStale` writes it, 198-206) **or** any raw non-void row of the month has createdAt > generatedAt; banner "Ledger changed after the tax feed was generated…"; regenerate clears it (1459-1470, 1506-1508) | "Live sources differ from the locked snapshot" drift warning (`accounting.ts` exceptions) | Accounting drift | DIFFERENT | Port the two-part stale rule for the month feed. |
| M06 | Month CSV | Columns Date, Firm, Type, Category, Mode, Vendor, Description, Amount, Source, GSTAmt, GSTRate% — non-void rows of the month, date ascending, file `expense_month_<YYYY-MM>.csv` (1517-1522) | none | none | MISSING | Add. |

### 3K. Exports, audit, screens

| # | Rule | SCC behaviour (line) | HCC prototype | HCC server | Verdict | What to change |
|---|---|---|---|---|---|---|
| X01 | Ledger CSV | Columns Date, Firm, Type, Category, Mode, Vendor, Description, Amount, Source ('manual' when none), Void ('Y'), VoidReason, CreatedBy, Store (name or 'Unassigned'), GSTAmt, GSTRate% — **all** rows incl. void, date ascending, `ledger_<today>.csv` (657-663) | No CSV on Expenses; report drill CSV = Date, Reference, Booking, Guest, Room, Detail, Method, Amount (`P\domain\streams\closing.ts:426-428`) | Reporting endpoints | MISSING | Add SCC ledger CSV with these columns. |
| X02 | Ledger filters | Type, Mode, Month (default viewed month), Search over description + vendor + category; newest first; shows at most 200 rows with a note (359-417) | Status tabs only; 100-row pages (`P\features\expenses\Expenses.tsx:9,16-17`) | `status/from/to` filters (`ExpenseCommands.cs:261-271`) | DIFFERENT | Add SCC filters and search. |
| X03 | Share one entry | "↗ Send" builds "Expense · date / Category / Amount / Mode / Vendor / Note / (VOID — reason)" and shares (62-78, 437) | none | n/a | MISSING | Add via native share. |
| X04 | Audit trail | Every create/edit/void/approve/close/reopen/petty/vendor/budget/recurring/sync/gst/month-lock/udhaar action; `by` = role; newest 800 kept; 300 shown; **Super-Admin only**; CSV When, Role, Action, Detail (97-103, 1527-1544) | Every command audited (`P\services\mock\commands.ts:193`); visible to Owner, Manager, Accountant (DEC-063 G10) | `tx.AuditAsync` per command (`ExpenseCommands.cs:54,76,130,190`) | DIFFERENT | Owner: visibility (Super-Admin vs Owner/Manager/Accountant) and the 800 cap. |
| X05 | Dashboard | Day income / expense / net, expected cash in hand, month income / expense / P&L, top expense head, cash-position card, recent 8 entries, banners (pending feed, unapproved days, recurring) (314-355) | Expenses page: posted today (net), drawer cash today, pending, drafts (`Expenses.tsx:38`) | — | DIFFERENT | Add SCC KPIs. |
| X06 | Step-up re-auth | Owner override actions call `stReauth` (fails open on an old shell) (24-26, 215, 575, 634, 859) | none | none | MISSING | Add re-auth for month-lock overrides. |
| X07 | Roles | Cashier (maker), Accounts (checker), Owner (approver) picked in the header; `can()` matrix (79-95; index.html 345-350) | Real users + grants (`P\fixtures\base.ts:41-45`) | Role grants | DIFFERENT | Map: Cashier → Cashier/Reception, Accounts → Accountant/Manager, Owner → Owner. |

### 3L. Udhaar (credit sales register)

| # | Rule | SCC behaviour (line) | HCC prototype | HCC server | Verdict | What to change |
|---|---|---|---|---|---|---|
| U01 | Record credit | Customer name required; mobile digits only, kept only if ≥ 10 digits (last 10); amount > 0; due date default today + 30; note; date today; **never posts to cash or income** (1564-1575, 1618) | Only company-credit receivables of companies (`P\domain\streams\groups.ts:164-176`, DEC-076) | Same (company credit) | MISSING | Ask whether the hotel needs a guest/walk-in udhaar register. |
| U02 | Record payment | Amount > 0; if > outstanding + ₹0.50 → "More than outstanding — capped" to outstanding; mode; recorded against the credit only (1576-1591) | Company payments never more than owed (`groups.ts:25`) | Same | MISSING | Port with U01. |
| U03 | Write off / remove | Optional reason; hidden from outstanding, kept for audit (1592-1600) | none | none | MISSING | Port. |
| U04 | Outstanding and ageing | Outstanding = amount − Σ settlements, floored at 0; buckets by days since the credit date: ≤ 30 "0–30", ≤ 60 "31–60", else "60+"; overdue when today > due date; oldest first; tab pill = open count (1556-1560, 1610-1651) | none | none | MISSING | Port. |
| U05 | Reminder | Message "Namaste <name>,\nA gentle reminder from <firm name, town> regarding your pending balance of <₹> (due <date>).\nKindly clear it at your convenience. Thank you." shared (1601-1609) | none | none | MISSING | Port with hotel name. |

## 4. HCC's own prototype ↔ server gaps found on the way (not SCC rules)

1. Server accepts any category text ≤ 60 and offers a hard-coded list (`ExpenseRules.cs:31,58-60`; `ExpenseCommands.cs:282-288`); the prototype requires a master category and refuses retired ones (`handlers\closing.ts:119-121`).
2. Server ignores the category `needsBill` flag and the category `approvalLimit` override; prototype enforces both (`handlers\closing.ts:124,136,146`).
3. Server never checks the expense-vendor master (retired vendor accepted); prototype refuses retired vendors (`handlers\closing.ts:122-123`).
4. Prototype amount cap ₹10,00,000 vs server ₹10 crore.

## 5. Port plan (concrete changes)

Order: owner answers §6 first (A02, A03, E05, C05, C06, C08, V01-V03, M02, F01 decide the shape), then build.

**Prototype (`03 Prototype`)**
1. `src\domain\types.ts`: extend `Expense` with `description` (required), `date` chosen by the user, `firm?`, `till?/store?`, `gstRateBps?`, `gstPaise?` (only when > 0), `billPhoto?`, `source?` ('manual'|'recurring'|'petty'|'payroll'|'stock'), `sourceRef?`, `void?`, `voidReason?`, `editLog?[]`, `approvedBy?/approvedAt?`; new optional slice `expensesX` {budgets, recurring, petty {float, history}, udhaar {receivables}, monthFeeds}; bump `SCHEMA` to 10.
2. `src\fixtures\ext\masters-seed.ts` + `src\domain\streams\accounting.ts`: seed SCC's 10 expense heads with ledgers; add `expenses` to Card's `usableAt`.
3. `src\services\mock\handlers\closing.ts` `EXPENSE`: vendor optional, description required, date ≤ business date and open/not cash-book-locked/not month-locked (Owner override + reason), SCC duplicate key (date + amount + description, confirm flag in the command), GST fields with the prefill formula and GST > amount confirm, budget alert in the result, amount cap ₹10 crore.
4. New handlers (new file `handlers\expenses-scc.ts`, prefix `EXP_`): `EXP_EDIT` (SCC fields, reason, editLog), `EXP_VOID` (six reasons + note; locked month needs the note), `EXP_BUDGET_SET`, `EXP_BUDGET_COPY`, `EXP_REC_ADD/EDIT/TOGGLE/DELETE/POST`, `EXP_PETTY_FLOAT`, `EXP_PETTY_OUT`, `EXP_FEED_POST` (payroll, GRN), `EXP_MONTH_FEED` (generate & lock, Owner, not future), `UD_ADD/UD_PAY/UD_VOID`.
5. Pure helpers in `src\domain\streams\expenses-scc.ts`: `budgetStatus` (round %, > 100 over, ≥ 85 near), `budgetAlert` (≥ 90 warn, ≥ 100 error, current month only), `recurringDue` (min(dueDay, days in month)), `pettyBalance` (replenish < 15 %), `udOutstanding/udBucket/udOverdue`, `monthLocked`, `feedStale`, `gstPrefill`.
6. Cash: SCC denominations (2000…1) incl. bank-deposit grid; tolerance and close/approve flow per owner answer (C05, C06, C08); 90-day carry walk-back and 0 floor; "awaiting approval" banners.
7. UI `src\features\expenses\*` and `src\mobile\money\Expenses.tsx`: Ledger filters + search + 200 cap, Edit/Void actions, bill photo (4 MB, 1000 px, JPEG 0.7), GST inputs, Recurring card + nudge, Petty tab, Budgets tab, Vendor spend, Month & tax tab with per-firm P&L, GST paid on purchases, stale banner, ledger CSV and month CSV with SCC columns, share entry, Udhaar tab.
8. Tests `tests\stream-expenses-scc.mjs`: golden vectors taken from SCC (budget 85/90/100 edges, day-31 recurring in February, petty 15 % edge, udhaar ₹0.50 cap and 30/60-day buckets, GST prefill 1180 @ 18 % → 180.00, duplicate key, month-lock override audit).

**Server (`08 Production`)**
1. Migration (next free block, add to `database\migrations.lock.json` with `tools/migrations-lock.mjs --add`): `ops.expense` add `description NVARCHAR(300)`, `expense_date` (or allow back-dated `business_date`), `firm`, `store_till_id`, `gst_rate_bps`, `gst_paise`, `bill_document_id`, `source`, `source_ref` (filtered unique), `is_void`, `void_reason`, `approved_by`, `approved_utc`; relax `CK_expense_sod` if A08 is dropped; new tables `ops.expense_edit_log`, `ops.expense_budget`, `ops.expense_recurring`, `ops.petty_float` + `ops.petty_float_history`, `ops.udhaar_receivable` + `ops.udhaar_settlement`, `ops.expense_month_feed` (with `stale`, `stale_utc`); seed the 10 heads into `ops.expense_category`; Card usable for expenses; PERM rows for edit/void/budget/recurring/petty/udhaar/month-lock.
2. `src\Hcc.Domain\Expenses\ExpenseRules.cs`: description rule, SCC duplicate key, GST prefill/limit, budget/recurring/petty/udhaar/month-lock/stale pure functions (golden-tested against the same vectors as the prototype).
3. `src\Hcc.Application\Expenses\ExpenseCommands.cs`: date-aware submit with day/cash-book/month-lock gates and Owner override (reason + re-auth + audit `locked-month-override` + stale), `EditExpense`, `VoidExpense`, budget/recurring/petty/feed/month-feed/udhaar commands; validate category, vendor, `needsBill`, category limit against the masters (§4).
4. Contracts + endpoints in `src\Hcc.Contracts\Expenses\ExpensesContracts.cs` and `src\Hcc.Api\Endpoints\ExpensesEndpoints.cs`; ledger CSV and month CSV exports with SCC columns.
5. Accounting: GST input ledger entry on expenses with `gst_paise`; if payroll/GRN feed rows enter the expense register, exclude them from expense vouchers (or drop the payroll journal) so nothing is counted twice.
6. Cash: SCC denominations in `src\Hcc.Domain\Payments\CashDrawer.cs`, tolerance/approval per owner answer in `CashierService.cs`.

## 6. SCC behaviour that looks like a bug — ask the owner

| # | SCC behaviour | Line | Question |
|---|---|---|---|
| B1 | Unapproved (pending) entries already count in cash, P&L, budgets and the tax feed; approval only locks the row. | 225, 317, 1415, 1492 | Should HCC copy this (drawer cut on submission) or keep "posted only after approval" (REQ-EXP-003)? |
| B2 | "Cashier cannot approve own day" checks the role, not the person: the Owner can close and approve the same day; entry approval has no maker ≠ checker rule. | 1128, 1185, 649-656 | Keep HCC's different-person rule? |
| B3 | Petty disbursements have **no closed-day check** (can post into a locked day) and, like cross-module postings, carry no status, so they never need approval. | 1226-1243, 1397 | Bug or intended? |
| B4 | Cross-module "Post to ledger" ignores closed and approved days (only the tax feed is marked stale). | 1391-1410 | Should feed rows respect the day lock? |
| B5 | Add entry has no future-date check (entries can be dated next month; the budget alert then stays silent). | 513-543, 1299 | Block future dates? |
| B6 | Edit has no role check and void is allowed for all three roles, so anyone can edit or void any un-approved entry. | 544, 87 | Who may edit/void in HCC? |
| B7 | Month & Tax banner says "day(s) in this period … not approved" but lists **all** unapproved days, not the selected month. | 1441, 1523-1525 | Fix to the month when porting? |
| B8 | Store tag whitelist is hard-coded to WLMHW / HEMW although the store list comes from the org master: a third store's tag is silently dropped to "Unassigned". | 124, 586, 691, 808 vs 247-255 | Port as "any active till"? |
| B9 | Budget tab marks "over" only when the rounded % > 100, but the alert is an error at ≥ 100: exactly 100 % shows "near limit" on the tab and an error toast. Budgets can be set by any role. | 1287, 1308, 1291 | Align thresholds? Owner-only budgets? |
| B10 | Udhaar payment up to outstanding + ₹0.50 is accepted as typed (over-payment ≤ 50 paise); larger amounts are silently capped. | 1587 | Keep the ₹0.50 tolerance? |
| B11 | Mismatch, void and reopen "reason required" is always met by the default first option of the select; only the locked-month void forces a typed note. | 1109-1113, 625, 632, 1137 | Require a typed detail? |
| B12 | Opening cash is floored at ₹0 and the carry-forward search stops after 90 days (then falls back to a stored opening or ₹0). Month lock fails open on a storage error and has no unlock. | 236-237, 196, 1489 | Port these as is? |
| B13 | `approveEntry` saves the ledger read before the click (no fresh re-read, unlike edit/void) — can overwrite rows added meanwhile. | 651-654 | (Fix in port.) |
| B14 | Payroll feed always books Salary by Bank on the 28th, firm default 'GM', even for cash salaries. | 1352-1354 | Correct for the hotel? |
| B15 | Amounts are JS floats in rupees (parseFloat), totals are float sums, display rounds to whole rupees. | 41, 40 | HCC keeps integer paise — confirm. |
