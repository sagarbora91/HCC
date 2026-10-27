# HCC Accounting Rulebook v1 — the books before the build

Written 27 Oct 2026 for the owner, the CA and the agents who will build the accounting module. Nothing in this document is built yet. The module is built **to this document**, and the worked month in §8 becomes its golden test.

**Status of this version:** draft for the owner's reading. Owner rulings already taken: DEC-135 (ITC allowed / disallowed per expense), DEC-137 (consumables expensed on purchase), DEC-138 (depreciation per the Income-tax Act), DEC-139 (opening balances entered by hand), **DEC-140 (HCC feeds Tally: ledgers, trial balance and P&L here; balance sheet, year-end close and GST returns in Tally at the CA)**, **DEC-141…150 (the open points OP-2…OP-12, answered 27 Oct 2026 evening; only OP-10 stays with the CA)**. **The owner has answered every open point that was theirs; the build may start on the owner's word.** Open points are numbered OP-1… in §9; nothing is built on an open point.

## 0. How to read the Source column

Every rule carries one of four tags. Only the first is law.

| Tag | Meaning |
|---|---|
| **LAW** | A dated primary source: an Act section, a CBIC / CBDT notification or circular, an ICAI pronouncement. Listed in §11 as S-nn. |
| **OWNER** | A ruling in `02 Planning/HCC_Decision_Register.md` (DEC-nnn). |
| **PRACTICE** | Standard bookkeeping practice or the hotel's own way of working. The owner may change it. |
| **VERIFY** | The rule is settled but the **section number or figure** comes from secondary sources that disagree. The build does not depend on the number; the CA confirms it once. |

Money is integer paise throughout; every rule below is written in rupees for reading. Dates are the hotel's **business date** (simulated clock in the prototype).

## 1. What the module is

**Tally is the final book (DEC-140).** HCC produces what Tally needs and what the owner wants to see without waiting for the CA: every rupee that moves is a balanced voucher in HCC, every party has a ledger, the **ledgers, trial balance and profit & loss** are HCC reports, and Tally imports HCC's vouchers and ledger masters. The **balance sheet, depreciation entries, year-end close and the GST returns are prepared in Tally at the CA's office**; HCC keeps a GST summary only so the CA can reconcile. Sections marked *(Tally)* below describe entries HCC does not post itself; they stay in the rulebook so the export and the CA's work line up.

Principles kept from the existing module 20 (`02 Planning/modules/HCC_Module_20_Accounting_Spec.md`):

1. **Derived first.** Operational records (folio lines, receipts, POS bills, expenses, pay runs, cash movements) create their vouchers automatically. Nobody keys a sale twice.
2. **Manual second.** What operations cannot produce (capital, loans, bank charges, depreciation, corrections) is entered as a manual voucher by the Accountant and **approved by the Owner** (maker-checker, DEC-058 / DEC-063 for money).
3. **Balanced always.** A voucher whose debits ≠ credits cannot be saved. A trial balance that does not balance is a defect, never a figure to explain.
4. **Locked days stay locked.** Day lock, month lock and the financial-year close freeze vouchers; the Owner reopens with a reason (DEC-077), and issued invoice numbers and their GST are never altered.
5. **Party-wise from day one.** Every company, Udhaar customer, OTA, vendor, bank account and staff member is its own ledger under a control group, so a statement per party is a report, not a project.

## 2. The entity and its statutory frame

| # | Rule | Source |
|---|---|---|
| 2.1 | The firm profile carries an **entity-type drop-down** (proprietorship, partnership firm, LLP, private limited company, HUF, trust / society); the capital section of the chart follows it (one capital + drawings for a proprietorship; capital, current and drawings per partner plus remuneration and interest for a partnership / LLP; share capital and reserves for a company). GST and TDS rules do not change with the type. The demo hotel is a proprietorship. | OWNER DEC-141 |
| 2.2 | **Financial year 1 April – 31 March**, the same FY the compliance calendar follows on the business date (DEC-125). Books are kept on the **mercantile (accrual) basis**: revenue when the night is consumed / the bill is issued, expense when the bill is received. | LAW S-20 (IT Act 2025 s.62 books; method of accounting — VERIFY section); OWNER DEC-125 |
| 2.3 | The hotel is a **Level IV non-company entity** for ICAI Accounting Standards (turnover ≤ ₹10 crore and borrowings ≤ ₹2 crore in the preceding year), so AS 3 (cash flow), AS 17, AS 18, AS 24, AS 28 and the actuarial parts of AS 15 do not apply. | LAW S-21 (ICAI announcement, March 2021, Annexure 2) |
| 2.4 | Financial statements follow ICAI's **Guidance Note on Financial Statements of Non-Corporate Entities** (vertical balance sheet with current / non-current split, "Owner's funds" instead of shareholders' funds). Phase II makes it apply to **all** such entities for periods beginning on or after **1 April 2026** — the hotel's first HCC year. | LAW S-22 (ICAI Council 451st meeting, 31 Mar 2026 — VERIFY the turnover-test wording on icai.org) |
| 2.5 | Books of account are compulsory under the **Income-tax Act 2025 s.62** (replaces 1961 s.44AA); the hotel is far above the ₹10 lakh turnover test. **Tax audit s.63** (replaces 44AB) applies above ₹1 crore turnover, or ₹10 crore when cash receipts and payments are each ≤ 5 % — HCC's cash-ratio report (§7.9) exists for this test. | LAW S-20, S-23 |
| 2.6 | GST: **regular scheme**, one GSTIN for the property (DEC-134; a second registration for the restaurant is a future item). Returns GSTR-1 and GSTR-3B monthly, or **QRMP** (quarterly returns, monthly payment) while aggregate turnover ≤ ₹5 crore — a firm-profile switch, not a rule in the books. | LAW S-10 (QRMP); OWNER DEC-134 |
| 2.7 | **E-invoicing does not apply** below ₹5 crore aggregate turnover (any year from 2017-18). The module records the threshold and warns the Owner when the FY's turnover crosses ₹4.5 crore. | LAW S-11 (Notification 10/2023-CT, from 1 Aug 2023) |
| 2.8 | GST records to keep (s.35 + Rule 56): stock register (quantities — the Stock module), output tax and ITC registers, supplier and customer registers with addresses, invoice / credit-note / receipt-voucher / refund-voucher series. **Retention 72 months** from the due date of the annual return (s.36). HCC never deletes a locked voucher or an issued document. | LAW S-12 |
| 2.9 | Under the Income-tax Act 2025 the TDS credit statement is **Form 168** (earlier 26AS) and the salary certificate Form 130 (earlier 16); the TDS deduction sections are consolidated in **s.393**. | LAW S-13; the compliance catalogue already carries the renumbering |

## 3. Chart of accounts

### 3.1 Groups — Tally's tree, so the export maps one to one

Tally's 28 predefined groups (15 primary, 13 sub-groups) become HCC's group master. Primary groups cannot be deleted or re-parented; the Owner may add sub-groups. **Source:** S-24 (Tally help, default groups).

| Balance sheet (capital nature) | P&L (revenue nature) |
|---|---|
| Capital Account › Reserves & Surplus | Sales Accounts |
| Current Assets › Bank Accounts · Cash-in-Hand · Deposits (Asset) · Loans & Advances (Asset) · Stock-in-Hand · Sundry Debtors | Purchase Accounts |
| Current Liabilities › Duties & Taxes · Provisions · Sundry Creditors | Direct Incomes |
| Fixed Assets | Direct Expenses |
| Investments | Indirect Incomes |
| Loans (Liability) › Bank OD A/c · Secured Loans · Unsecured Loans | Indirect Expenses |
| Misc. Expenses (Asset) · Suspense A/c · Branch / Divisions | |

### 3.2 Ledgers — the 35 that exist, re-grouped, plus the ones the module adds

Codes stay four digits so every existing mapping key keeps working. **Existing** = already in `src/domain/streams/accounting.ts` and `Hcc.Domain.Accounting.ChartOfAccounts`. Ledgers marked **control** hold party sub-ledgers (§3.3) and are never posted to directly.

| Code | Ledger | Group | Status | Note |
|---|---|---|---|---|
| 1000 | Cash in hand | Cash-in-Hand | existing | One sub-ledger per **till** when more than one till exists |
| 1005 | Petty cash float | Cash-in-Hand | new | SCC petty-cash float (expenses wave) |
| 1010 | UPI clearing | Bank Accounts | existing | Settles to a bank ledger (§5 B8) |
| 1020 | Card clearing | Bank Accounts | existing | Settles net of MDR (§5 B8) |
| 1030 | Bank accounts | Bank Accounts | existing → **control** | One sub-ledger per row of the **Bank accounts master** |
| 1100 | Guest ledger (in-house folios) | Sundry Debtors | existing | Open folio balances; zero after check-out |
| 1110 | Company receivables (city ledger) | Sundry Debtors | existing → **control** | One sub-ledger per company account (Groups & corporate) |
| 1115 | OTA receivables | Sundry Debtors | new → **control** | One sub-ledger per OTA channel (Goibibo, MakeMyTrip…) |
| 1120 | Udhaar receivables (customer credit) | Sundry Debtors | existing → **control** | One sub-ledger per Udhaar customer |
| 1200 | Staff advances | Loans & Advances (Asset) | existing → **control** | One sub-ledger per staff member |
| 1210 | Recoverable from leavers (F&F) | Loans & Advances (Asset) | existing key `payroll:fnf-recoverable` | |
| 1300 | GST input credit | Duties & Taxes | existing → **becomes a group of three** | See 1310–1330; the law needs the split (§5 E1) |
| 1310 | Input CGST | Duties & Taxes | new | ITC marked allowed, CGST half |
| 1320 | Input SGST | Duties & Taxes | new | ITC marked allowed, SGST half |
| 1330 | Input IGST | Duties & Taxes | new | Bills from vendors outside Maharashtra (IGST on the bill) |
| 1340 | GST electronic cash ledger | Duties & Taxes | new | Challan deposits and TCS credits with the government |
| 1350 | GST paid on advances (adjustable) | Duties & Taxes | new | §5 A6; nets to zero when the advance is invoiced |
| 1360 | TDS receivable | Loans & Advances (Asset) | new | TDS deducted by companies / OTAs; reconciled to Form 168 |
| 1370 | Prepaid expenses | Loans & Advances (Asset) | new | Optional, §5 C11 |
| 1400 | Opening-balance difference | Suspense A/c | new | DEC-139: parks an unbalanced opening entry until corrected; must be zero before the first period lock |
| 1500 | Buildings | Fixed Assets | new | Income-tax block, 10 % (hotels are **not** residential buildings) |
| 1510 | Furniture, fittings & electrical fittings | Fixed Assets | new | Block 10 % |
| 1520 | Plant & machinery (kitchen, AC, laundry, generator) | Fixed Assets | new | Block 15 % |
| 1530 | Computers, software & POS hardware | Fixed Assets | new | Block 40 % |
| 1540 | Vehicles | Fixed Assets | new | Block 15 %; ITC blocked (§5 C10) |
| 2000 | Advance deposits (guest and group) | Current Liabilities | existing (+ `receipt:group-deposit`) | Refundable; never income |
| 2100 | Synthetic tax payable (legacy) | Duties & Taxes | existing | Locked snapshots before DEC-069 only |
| 2110 | Output CGST | Duties & Taxes | existing | |
| 2120 | Output SGST | Duties & Taxes | existing | |
| 2130 | Output IGST | Duties & Taxes | new, normally zero | Kept so a wrong inter-state posting is visible; DEC-136 refuses it at source |
| 2140 | GST payable under reverse charge | Duties & Taxes | new | §5 C8; paid in cash, never by ITC |
| 2150 | TDS payable (hotel as deductor) | Duties & Taxes | new | §5 C9, only if the hotel must deduct |
| 2200 | Vendor payables | Sundry Creditors | existing → **control** | One sub-ledger per expense vendor (DEC-132: a typed vendor joins the list and gets its ledger) |
| 2300 | Salaries payable (net) | Current Liabilities | existing | |
| 2310 | PF payable | Duties & Taxes | existing | |
| 2320 | ESIC payable | Duties & Taxes | existing | |
| 2330 | Professional tax payable | Duties & Taxes | existing | |
| 2340 | Other payroll deductions | Current Liabilities | existing | |
| 2400 | Bank loans (secured) | Secured Loans | new | One sub-ledger per loan |
| 2410 | Unsecured loans | Unsecured Loans | new | s.185 / s.188 of the IT Act 2025: ≥ ₹20,000 only by bank (VERIFY numbers) |
| 3000 | Owner's drawings | Capital Account | existing | Closed to 3100 at year end |
| 3100 | Owner's capital | Capital Account | new | Opening capital + profit − drawings |
| 4000 | Room revenue | Sales Accounts | existing | Incl. meal plans bundled with the room (composite supply, DEC-MA-11f) |
| 4100 | Other services revenue (laundry, banquet, extras) | Sales Accounts | existing | |
| 4200 | Food & beverage revenue | Sales Accounts | new | `pos:sales` and folio F&B lines move here from 4100 so the P&L shows rooms vs F&B |
| 4300 | Cancellation, no-show & attrition charges | Sales Accounts | new | `line:attrition` moves here from 4000 (answers CA G7) |
| 4900 | Discounts allowed | Sales Accounts (contra) | existing | Pre-supply discounts shown on the invoice |
| 4910 | Refunds, reversals & credit notes | Sales Accounts (contra) | existing | |
| 4950 | Cash over | Indirect Incomes | new | Was netted into 5900; income and expense are shown separately |
| 4960 | Interest & other income | Indirect Incomes | new | |
| 5000 | Housekeeping & guest supplies | Direct Expenses | existing "Supplies" | DEC-137: expensed on purchase |
| 5010 | Linen & laundry | Direct Expenses | existing "Laundry" | |
| 5015 | Kitchen provisions & beverages | Direct Expenses | new | Restaurant purchases, DEC-137 |
| 5020 | Repairs & maintenance | Indirect Expenses | existing | |
| 5030 | Electricity, water & fuel | Direct Expenses | existing "Utilities" | |
| 5040 | Petty cash & sundry | Indirect Expenses | existing | |
| 5050 | General & administrative expenses | Indirect Expenses | existing | Printing, stationery, telephone, internet… via categories |
| 5060 | Rent & licence fees | Indirect Expenses | new | FSSAI, lodging licence, PT enrolment (DEC-133 cards) |
| 5070 | OTA commission | Indirect Expenses | new | §5 A13 |
| 5080 | Bank & card charges | Indirect Expenses | new | MDR, bank charges |
| 5090 | Interest on loans | Indirect Expenses | new | Finance cost line of the P&L |
| 5100 | Salaries & wages | Indirect Expenses | existing | |
| 5110 | Employer PF & ESIC | Indirect Expenses | existing | |
| 5120 | Staff welfare | Indirect Expenses | new | Staff meals, uniforms; ITC blocked (s.17(5)(b)) |
| 5200 | Depreciation | Indirect Expenses | new | DEC-138 |
| 5300 | Bad debts written off | Indirect Expenses | new | §5 A18 |
| 5400 | GST / TDS interest & late fees | Indirect Expenses | new | Interest is a cost; penalties flagged "not deductible" |
| 5900 | Cash short | Indirect Expenses | existing "Cash over / short" | Over goes to 4950 |
| 9999 | Suspense (unmapped) | Suspense A/c | existing | Never a mapping target; blocks locking |

### 3.3 Party sub-ledgers (the "party-wise" and "customer-wise" ask)

| # | Rule | Source |
|---|---|---|
| 3.3.1 | A **control ledger** (1030, 1110, 1115, 1120, 1200, 2200, 2400) is a parent. Every master row that money can be owed to or by creates a sub-ledger automatically: `control-code/party-id` (e.g. `1110/CMP-0007 Deccan Agro Ltd`). The sub-ledger is created when the master row is created and retired with it (a retired party with a non-zero balance cannot be retired). | PRACTICE |
| 3.3.2 | Vouchers post to the **sub-ledger**, never the control. The trial balance and Tally export show either the control total (summary) or every party (detail); both tie. | PRACTICE; Tally practice S-24 |
| 3.3.3 | A **statement per party** (opening, every voucher, running balance, closing) is a standard report for any date range, exportable (§7.2). Outstanding by party with ageing 0–30 / 31–60 / 61–90 / 90+ days (§7.6). | PRACTICE |
| 3.3.4 | A **vendor typed on an expense** that is not in the list joins the vendor list and gets a sub-ledger (DEC-132); a company account gets one when Groups & corporate creates it; an Udhaar customer when the first credit is raised; an OTA when the channel is configured; a staff member when the first advance is given. | OWNER DEC-132; PRACTICE |

### 3.4 Chart of accounts as a master

| # | Rule | Source |
|---|---|---|
| 3.4.1 | The chart is an **Owner-only master** (money list, DEC-058 / DEC-MA-3.1). Built-in ledgers (every code above) are **system ledgers**: renamable, re-groupable within the same nature, never deletable, because mapping keys point at them. The Owner may add ledgers under any group. | OWNER DEC-058 |
| 3.4.2 | Every ledger has: code, name, group, nature (Dr / Cr normal), opening balance (DEC-139), GST flag (none / input / output), Tally name (for the XML masters), active. | PRACTICE |
| 3.4.3 | Masters feed the mapping, as today: a payment method's ledger (tender), an expense category's ledger, a bank account's sub-ledger. The mapping table keeps the Owner override with history. | existing module 20 §4; DEC-MA-11a |

## 4. GST rules the postings depend on

| # | Rule | Source |
|---|---|---|
| 4.1 | **Room accommodation (SAC 996311):** value of supply per unit per day ≤ ₹7,500 → **5 % without ITC**; above ₹7,500 → **18 % with ITC**. Effective **22 Sep 2025** (before: 12 % with ITC up to ₹7,500; the ₹1,000 exemption ended 18 Jul 2022). Tested on the **actual price charged**, per unit per night. Already in the dated GST master (DEC-069). | LAW S-1, S-2 (Notification 11/2017-CT(R) as amended by 15/2025-CT(R)) |
| 4.2 | **Restaurant and room service (SAC 996331):** 5 % without ITC; **18 % with ITC** only when the hotel is **"specified premises"** for the FY — any unit above ₹7,500 per day in the **preceding** FY, or an opt-in declaration filed in the window (15 Jan – 31 Mar). Already derived in `gst.ts`. | LAW S-3 (Notification 05/2025-CT(R), from 1 Apr 2025) |
| 4.3 | **Place of supply** of accommodation and restaurant service is where the property is: **CGST + SGST always**, whatever the guest's state or GSTIN. | LAW S-4 (IGST Act s.12(3)(b), s.12(4)); OWNER DEC-136 |
| 4.4 | **The 5 % condition blocks the hotel's own ITC** on goods and services used to supply 5 % rooms and meals. The hotel **has 18 % supplies** (DEC-143), so ITC exists for inputs used for them. Policy: the per-expense switch (DEC-135) is marked **allowed only on a bill directly for an 18 % supply**; bills for 5 % rooms / restaurant and **shared inputs** (electricity, housekeeping, repairs) stay **disallowed**. Rule 42 apportionment of common credit, if the CA wants it, is done in Tally from HCC's bill-wise ITC report. | LAW S-1 (condition in the rate entry), S-5 (CGST s.17(2), Rule 42); OWNER DEC-143 |
| 4.5 | **Blocked credits (s.17(5))** never become ITC whatever the output rate: food and beverages, outdoor catering, health services, club / fitness membership, **motor vehicles ≤ 13 seats**, works contract and construction of immovable property (except plant and machinery), goods or services for personal consumption, goods lost / stolen / gifted / free samples. The expense-category master carries a "blocked under 17(5)" flag so the ITC switch cannot be turned on for them. | LAW S-5 |
| 4.6 | **Advances for services are taxed when received.** Notification 66/2017-CT spared only **goods**; a hotel booking advance is a service advance: a **receipt voucher** (s.31(3)(d), Rule 50) is issued and GST paid in that month at the rate of the booked room (5 % / 18 %), adjusted when the invoice is raised; a refund of an unused advance needs a **refund voucher** (s.31(3)(e), Rule 51). Reported in GSTR-1 Table 11A (received) and 11B (adjusted). **Owner ruling DEC-142: HCC posts no GST on booking advances** (risk recorded in the register; CA to confirm). The lawful entry stays in the rulebook (A6 *(switch)*) behind a firm-profile switch "GST on advances", default **off**, so it can be turned on without a rebuild. | LAW S-6 (CGST s.13(2), s.31(3)(d)/(e), Rules 50–51; Not. 66/2017-CT); OWNER DEC-142 (against advice) |
| 4.7 | **Discounts:** a discount shown on the invoice at the time of supply reduces the taxable value (s.15(3)(a)); a post-supply discount reduces GST only through a **credit note** under s.34 and only if agreed before supply. A goodwill reduction after the invoice is a credit note **without** GST reduction when the recipient has kept the ITC. | LAW S-7 (CGST s.15(3), s.34) |
| 4.8 | **Credit notes** (s.34): issued against a specific invoice; GST reduced only by declaring the note in a return not later than **30 November following the FY** of the supply (or the annual return, if earlier). Numbered in their own series, never a negative invoice. Existing rule DEC-077 (issued numbers never altered). | LAW S-12 |
| 4.9 | **Cancellation, no-show and retention charges** are consideration for the booking: taxable at the **rate of the principal supply** (the room), not 18 %; exempt only if the room would have been. Posted to 4300. | LAW S-8 (Circular 178/10/2022-GST) |
| 4.10 | **Composite supply:** a meal plan bundled with the room takes the room's rate; the room night's slab is tested on room + bundled meals (DEC-MA-11f). | LAW S-9 (CGST s.2(30), s.8(a)); OWNER DEC-MA-11f |
| 4.11 | **Walk-in restaurant bills** are tax invoices in the `RST/<FY>/<7 digits>` series (DEC-072; serial ≤ 16 characters, Rule 46); bills under ₹200 to unregistered guests may be consolidated daily (s.31(3)(b)) — HCC issues every bill anyway. | LAW S-12 (Rule 46); OWNER DEC-072 |
| 4.12 | **ITC utilisation order** (s.49(5), 49A, 49B, Rule 88A): IGST credit first (against IGST, then CGST / SGST in any order); CGST credit → CGST then IGST; SGST credit → SGST then IGST; **CGST and SGST credit never cross**. Hence three input ledgers (1310–1330). The CA's "one ITC ledger" question is answered by the law. | LAW S-14 |
| 4.13 | **Reverse charge (s.9(3))** on services a hotel typically buys from unregistered or notified suppliers: goods transport agency, security services from a non-body-corporate, legal services from an advocate, renting of a motor vehicle with operator from a non-body-corporate. The hotel pays the GST in **cash** (not by ITC), and may take it as ITC only if §4.4 allows. Which categories the hotel actually buys: OP-6. | LAW S-15 (CGST s.9(3), Notification 13/2017-CT(R) as amended) |
| 4.14 | **OTA bookings:** the hotel invoices the guest for the full room value; the OTA collects **TCS under s.52** (0.5 % of net taxable value, as 0.25 % CGST + 0.25 % SGST) which lands in the hotel's **electronic cash ledger** (1340); the OTA's commission invoice carries 18 % GST (ITC per §4.4). | LAW S-16 (CGST s.52; TCS rate 0.5 % from 10 Jul 2024) |
| 4.15 | **GST interest** on late payment (s.50) is a cost (5400); **penalties and late fees** are posted to the same ledger with a "not deductible for income tax" flag so the CA adds them back. | LAW S-12; PRACTICE |

## 5. Transaction rules — every event and its entry

Dr / Cr lines name the ledger codes of §3.2; `[party]` means the party's sub-ledger. "Exists" = HCC posts this today (`Hcc.Domain.Accounting.AccountingSource.SourceRules` and `accLiveVouchers`); "new" = the module adds it.

### A. Front desk, folios and sales

| # | Event | Voucher | Entries | GST / TDS | Status | Source |
|---|---|---|---|---|---|---|
| A1 | Room night posted to a folio (incl. bundled meal plan) | Sales | Dr 1100 Guest ledger (gross) · Cr 4000 Room revenue (taxable) · Cr 2110 CGST · Cr 2120 SGST | Slab per unit per night on the actual price, §4.1, §4.10 | exists | LAW S-1; existing |
| A2 | Extra / service line (laundry, banquet, misc.) | Sales | Dr 1100 · Cr 4100 · Cr 2110 · Cr 2120 | Rate from the GST master per class (18 % seed, CA to confirm) | exists | existing |
| A3 | Restaurant / room-service bill charged to a folio | Sales | Dr 1100 · Cr **4200** F&B revenue · Cr 2110 · Cr 2120 | 5 % / 18 % per §4.2 | exists, ledger moves to 4200 | existing |
| A4 | Discount on the invoice (before supply) | Sales (same voucher) | Dr 4900 Discounts allowed · Cr 1100 | Reduces taxable value, §4.7; GST computed on the net line | exists | LAW S-7 |
| A5 | Credit note / correction after invoice | Sales (negative) | Dr 4910 Refunds, reversals & credit notes · Dr 2110 · Dr 2120 (only when GST is reduced) · Cr 1100 or Cr [party] | §4.8: own series, by 30 Nov next FY; otherwise no GST lines | exists (adjustment lines) | LAW S-12 |
| A6 | **Advance deposit received** for a future stay | Receipt | Dr tender (1000 / 1010 / 1020 / 1030[bank]) · Cr 2000 Advance deposits. **Switch off (DEC-142): no GST line.** With the switch on: **and** Dr 1350 GST paid on advances · Cr 2110 · Cr 2120 (GST at the booked room's rate, cum-tax on the amount received; receipt voucher; GSTR-1 11A) | §4.6 | exists (switch *new*, off) | OWNER DEC-142; LAW S-6 |
| A6a | Advance applied to the folio at check-out | Journal | Dr 2000 · Cr 1100. *(Switch on: **and** Dr 2110 · Dr 2120 · Cr 1350, the advance GST reversed; GSTR-1 11B)* | | exists | OWNER DEC-142; LAW S-6 |
| A6b | Advance refunded, no stay | Payment | Dr 2000 · Cr tender. *(Switch on: **and** Dr 2110 · Dr 2120 · Cr 1350; refund voucher)* | | exists | OWNER DEC-142; LAW S-6 |
| A7 | Guest pays at check-out (cash / UPI / card / bank) | Receipt | Dr tender · Cr 1100 | — | exists | existing |
| A8 | Folio settled by **company credit** | Journal | Dr 1110/[company] · Cr 1100 | Invoice to the company with its GSTIN (B2B in GSTR-1) | exists (control) → party | existing; DEC-076 |
| A9 | **Company pays**, often net of TDS | Receipt | Dr 1030/[bank] (net) · Dr 1360 TDS receivable (TDS) · Cr 1110/[company] (gross) | Company deducts under IT Act 2025 s.393 (old 194-I) at 10 % on the **taxable value excluding GST** when it has a regular arrangement / rate contract and pays the hotel above **₹50,000 in a month**; catering may be 2 % (old 194C). The hotel records whatever was deducted and reconciles to Form 168 at year end | **new** (TDS line) | LAW S-13, S-17 (Circular 715/1995 Q20, 5/2002; Circular 23/2017 GST excluded) — VERIFY threshold wording |
| A10 | Refund to a guest | Payment | Dr 1100 (or 4910 if revenue is reversed) · Cr tender | Credit note if an invoice was issued | exists | existing |
| A11 | **No-show / cancellation / retention charge** kept | Sales | Dr 1100 or 1110/[party] · Cr **4300** · Cr 2110 · Cr 2120 | Rate of the room, §4.9 | partly (attrition exists → 4000) | LAW S-8 |
| A12 | Complimentary room (no consideration, unrelated person) | — | No voucher; the night is recorded at ₹0 with the reason | No GST without consideration (Schedule I only for related persons) | exists (₹0 lines skipped) | LAW CGST s.7, Sch. I |
| A13 | **OTA booking** — stay invoiced to the guest, money comes from the OTA | Sales; then Receipt | Dr 1115/[OTA] (gross) · Cr 4000 · Cr 2110 · Cr 2120. Payout **from the OTA's statement** (every OTA pays net, DEC-150): Dr 1030/[bank] (net) · Dr 5070 OTA commission (commission + its 18 % GST, ITC per §4.4) · Dr 1340 GST cash ledger (TCS) · Cr 1115/[OTA] (gross) | §4.14 | **new** | OWNER DEC-150; LAW S-16 |
| A14 | Walk-in restaurant bill settled at the outlet | Sales | Dr tender · Cr 4200 · Cr 2110 · Cr 2120 | RST series, §4.11 | exists (→ 4200) | OWNER DEC-072 |
| A15 | POS bill void | Sales (negative) | Reverse of A14 | Same day only; otherwise a credit note | exists | existing |
| A16 | Group master folio: shares, attrition, deposits, settlement | Sales / Receipt / Journal | As built (B3 wave): master receivable = 1110/[company]; attrition → **4300** | | exists | OWNER DEC-074 |
| A17 | **Udhaar credit raised** (a sale on credit to a local customer, restaurant bill **or** room folio — the screen asks, DEC-144) | Sales | Dr 1120/[customer] (gross) · Cr 4200 or 4000 (· 4100) · Cr 2110 · Cr 2120 | A tax invoice is issued at the time of the credit; due date = date + 30 days (SCC) | **new** — answers the 13j question: the credit **is** booked when raised | OWNER DEC-144; PRACTICE |
| A17a | Udhaar paid (desk receipt into the open shift) | Receipt | Dr tender · Cr 1120/[customer] | ₹0.50 overpay tolerance (SCC) | exists → party | OWNER DEC-130 |
| A17b | Mistaken Udhaar payment reversed | Receipt (negative) | Reverse of A17a, same day, Owner approval, reason | | **new** — answers the 13j question | PRACTICE |
| A18 | **Bad debt** (company / Udhaar balance given up) | Journal (manual, Owner-approved) | Dr 5300 Bad debts · Cr 1120/[customer] or 1110/[company] | No GST relief (a bad debt is not a credit note) | new | LAW S-12; PRACTICE |

### B. Cash, bank and the owner

| # | Event | Voucher | Entries | Note | Status | Source |
|---|---|---|---|---|---|---|
| B1 | Shift / day cash difference accepted | Journal | Short: Dr 5900 · Cr 1000. Over: Dr 1000 · Cr **4950** | Over and short shown separately | exists (over → 4950) | OWNER DEC-065 |
| B2 | Cash banked (slip + photo, second-person confirm) | Contra | Dr 1030/[bank] · Cr 1000 | Rejected deposit reverses | exists → bank sub-ledger | OWNER DEC-073 |
| B3 | Till transfer | — | Nothing (cash stays in 1000; per-till sub-ledgers move it) | | exists | OWNER DEC-073 |
| B4 | Owner takes cash | Payment | Dr 3000 Drawings · Cr 1000 | Capital, never an expense | exists | OWNER DEC-073 |
| B5 | **Owner introduces money** | Receipt (manual) | Dr 1030/[bank] or 1000 · Cr 3100 Capital | | new | PRACTICE |
| B6 | **Owner's personal tax / insurance paid from the hotel bank** (advance tax, self-assessment tax, LIC…) | Payment (manual) | Dr 3000 Drawings · Cr 1030/[bank] | A proprietor's income tax is **never** a business expense | new | LAW (IT Act: tax on the proprietor is not a deduction); PRACTICE |
| B7 | Bank charges, interest credited (from the statement) | Payment / Receipt (manual) | Dr 5080 · Cr 1030/[bank]; Dr 1030/[bank] · Cr 4960 | GST on bank charges: ITC per §4.4 | new | PRACTICE |
| B8 | **UPI / card settlement** reaches the bank | Contra (manual or statement import) | Dr 1030/[bank] (net) · Dr 5080 (MDR incl. its GST unless ITC allowed) · Cr 1010 / 1020 (gross) | Clearing ledgers must reach zero in bank reconciliation | **new** | PRACTICE |
| B9 | **Cash receipt limit** — ₹2,00,000 or more in cash from one person in a day / for one transaction / one event | — | HCC **refuses** the receipt and says why | IT Act 2025 **s.186** (old 269ST; penalty old 271DA → s.451) | new (guard) | LAW S-18 — VERIFY section numbers |
| B10 | **Cash payment limit** — more than ₹10,000 to one person in a day other than by account-payee / electronic mode | — | HCC **warns** on the expense and marks it "deduction lost" in the P&L notes (₹35,000 for goods transport) | Old 40A(3); new section reported as 36 or 41 | new (guard) | LAW S-19 — VERIFY section number |
| B11 | **Loan taken / repaid** | Receipt / Payment (manual) | Dr 1030/[bank] · Cr 2400/[loan]; EMI: Dr 2400/[loan] (principal) · Dr 5090 Interest (interest) · Cr 1030/[bank] | Loans ≥ ₹20,000 only through the bank (old 269SS / 269T → s.185 / s.188) | new | LAW S-18 — VERIFY |
| B12 | **Petty cash float** issued and topped up (SCC) | Contra | Dr 1005 · Cr 1000; expenses from the float credit 1005 | | exists in expenses (needs its ledger) | OWNER DEC-116 (SCC port) |

### C. Purchases and expenses

| # | Event | Voucher | Entries | Note | Status | Source |
|---|---|---|---|---|---|---|
| C1 | **Vendor bill on credit** (consumables, services) | Purchase | Dr expense ledger (bill value **incl.** GST when ITC disallowed; excl. GST when allowed) · Dr 1310 / 1320 (or 1330 for IGST) when ITC allowed · Cr 2200/[vendor] (bill total) | ITC default **disallowed** (§4.4, §4.5); a bill photo and the vendor's GSTIN are kept for any allowed ITC (s.16 conditions: invoice, receipt, vendor filed) | exists (single 1300, control vendor) → split + party | OWNER DEC-135, DEC-137; LAW S-5 |
| C2 | Expense paid at once (cash / UPI / bank) | Payment | Dr expense ledger (+ 1310 / 1320 if allowed) · Cr tender | B10 cash guard | exists | existing |
| C3 | **Vendor payment** against bills | Payment | Dr 2200/[vendor] · Cr 1030/[bank] or 1000 | Allocated to bills (oldest first) for ageing | **new** | PRACTICE |
| C4 | Advance to a vendor | Payment | Dr 2200/[vendor] (goes Dr) · Cr tender | Shown as "advance" in the creditor's statement | new | PRACTICE |
| C5 | Goods returned / vendor credit note | Purchase (negative) | Dr 2200/[vendor] · Cr expense ledger (· Cr 1310/1320 if ITC had been taken → ITC reversed) | | new | LAW S-12 |
| C6 | Recurring template posts a bill | Purchase | As C1 | Owner-only templates | exists | OWNER DEC-129 |
| C7 | Expense edited / voided with reason | reversal | Exact negative of the original, same keys | | exists | existing |
| C8 | **Reverse-charge bill** — any expense category the Owner flags "reverse charge" in the category master (DEC-145; GTA, security, advocate, vehicle hire — §4.13) | Purchase + Journal | Dr expense · Cr 2200/[vendor] (bill, no GST); **and** Dr expense (or 1310 / 1320 if ITC allowed) · Cr 2140 RCM payable (GST at the service's rate) | 2140 is paid by challan, never set off | **new** | OWNER DEC-145; LAW S-15 |
| C9 | **Hotel deducts TDS** on its own payments (rent of premises, contractors, professional fees) — **switch on (DEC-146)** | Payment | Dr expense · Cr 2150 TDS payable · Cr 2200/[vendor] / bank (net) | A dated **TDS-rate table** (section row of s.393, threshold, rate; Owner-editable, seeded "CA to confirm": rent 10 %, contractor 1 % individual / 2 % others, professional 10 %, commission 2 %); HCC holds the amount in 2150 and gives the CA a deductee-wise report; **the CA deposits and files** | **new** | OWNER DEC-146; LAW S-13 — VERIFY rows |
| C10 | **Fixed asset bought** (furniture, AC, kitchen machine, computer, vehicle, building work) | Purchase | Dr 15x0 asset block · (Dr 1310 / 1320 only if ITC is allowed **and** the asset is not blocked: vehicles ≤ 13 seats and construction are blocked) · Cr 2200/[vendor] / bank | Added to the fixed-asset register with date put to use (§F) | **new** | LAW S-5; OWNER DEC-138 |
| C11 | Insurance / AMC / licence paid for more than 12 months | Payment | **Dr expense when paid** (DEC-147); no prepaid ledger (1370 kept in the chart, unused, for the CA's year-end journal in Tally) | | exists | OWNER DEC-147 |
| C12 | Stock received (GRN) | — | **No entry** — quantities only | | exists (nothing posted) | OWNER DEC-137 |

### D. Payroll (SCC rules in full, DEC-115)

| # | Event | Voucher | Entries | Status | Source |
|---|---|---|---|---|---|
| D1 | Pay run posted | Journal | Dr 5100 Salaries (gross) · Dr 5110 Employer PF · Dr 5110 Employer ESIC · Cr 2300 Net pay · Cr 2310 PF (employee + employer) · Cr 2320 ESIC (employee + employer) · Cr 2330 PT · Cr 1200/[staff] (advance recovered) · Cr 2340 other deductions | exists → staff sub-ledgers | existing; OWNER DEC-115 |
| D2 | Net pay paid | Payment | Dr 2300 · Cr 1030/[bank] / 1000 | exists | existing |
| D3 | Salary advance given | Payment | Dr 1200/[staff] · Cr tender | exists → party | OWNER DEC-074 |
| D4 | F&F settled and paid | Journal + Payment | As built (mirrors D1 / D2); negative net → 1210 | exists | OWNER DEC-066, DEC-074 |
| D5 | **Statutory remittance** (PF by the 15th, ESIC by the 15th, PT by the end of month) | Payment (manual or from the compliance card) | Dr 2310 / 2320 / 2330 · Cr 1030/[bank] | **new** | LAW (EPF Act s.6, ESI Act, Maharashtra PT Act — rates as ported from SCC: PF 12 % + 12 % on wages ≤ ₹15,000, ESIC 0.75 % + 3.25 % on wages ≤ ₹21,000, PT ₹200 / ₹300 Feb; women up to ₹25,000 a month exempt, DEC-149) |
| D6 | Employer's own PT enrolment (₹2,500 / year) | Payment | Dr 5060 · Cr bank | new | PRACTICE |
| D7 | Staff meals, uniforms | Purchase | Dr 5120 Staff welfare (GST included; ITC blocked) · Cr vendor / cash | new ledger | LAW S-5 (s.17(5)(b)) |

### E. Tax settlement

| # | Event | Voucher | Entries | Status | Source |
|---|---|---|---|---|---|
| E1 | **Month-end GST set-off** (§4.12) | Journal (generated, Accountant confirms) | Dr 2110 Output CGST · Cr 1310 Input CGST (up to the smaller); Dr 2120 · Cr 1320; IGST credit first against 2130 then 2110 / 2120; remaining output → Dr 2110 / 2120 · Cr 1340 Electronic cash ledger | **new** | LAW S-14 |
| E2 | GST challan paid (cash ledger deposit), TCS credit from OTAs | Payment / Journal | Dr 1340 · Cr 1030/[bank]; TCS: in A13 | new | LAW S-16 |
| E3 | RCM paid (C8) | Payment | Dr 2140 · Cr 1340 (via challan) | new | LAW S-15 |
| E4 | GST interest / late fee | Payment | Dr 5400 · Cr 1340 / bank; penalties flagged not deductible | new | LAW S-12 |
| E5 | TDS deposited (C9) | Payment | Dr 2150 · Cr 1030/[bank] by the 7th (30 April for March) | new | LAW S-13 |
| E6 | **TDS receivable reconciled** to Form 168 at year end; shortfall chased with the company, excess written to 3000 Drawings (the proprietor's tax credit) | Journal (manual) | Dr 3000 · Cr 1360 at year end (credit belongs to the proprietor's return) | new | PRACTICE |

### F. Fixed assets and depreciation (DEC-138) — *(Tally)*: the depreciation entry is normally passed by the CA in Tally; HCC keeps the register and the rate table, and posts F3 only if the owner wants the P&L after depreciation in HCC

| # | Rule | Source |
|---|---|---|
| F1 | A **fixed-asset register**: asset, block, cost, date put to use, vendor, bill, ITC taken, disposal. One ledger per block (1500–1540). | OWNER DEC-138 |
| F2 | **Written-down value per block** at the Income-tax rates: buildings (hotel, non-residential) **10 %**, furniture & electrical fittings **10 %**, plant & machinery **15 %**, computers & software **40 %**, vehicles **15 %**. **Half the rate** when an asset is put to use for fewer than 180 days in the year of purchase. Rates are a dated master seeded with their source, editable by the Owner. | LAW S-25 (Income-tax Rules 2026, Rule 25 and Appendix I — the Rules were published 5 May 2026; VERIFY that the notified text matches the draft rates) |
| F3 | Year-end journal per block: Dr 5200 Depreciation · Cr 15x0 block (WDV method credits the asset directly, as Tally does). Generated by the module, Owner-approved, dated 31 March. | OWNER DEC-138; PRACTICE |
| F4 | Sale / scrapping: Dr tender · Cr block (sale value); a block that goes negative or empty raises a flag "short-term capital gain / loss — CA" rather than an automatic entry. | LAW (block-of-assets rule, IT Act 2025 s.33/34 — VERIFY); PRACTICE |

### G. Periods, locks and the year end

| # | Rule | Source |
|---|---|---|
| G1 | **Day lock** as today (closed day, no exceptions, Owner / Manager / Accountant). **Month lock** (new, Accountant proposes, Owner locks) after the GST set-off E1 is confirmed and the statutory payables D5 are posted; the expense month lock of the SCC port becomes part of it. | existing module 20; OWNER DEC-131 |
| G2 | **Opening balances** (DEC-139 / DEC-148): one Journal dated the FY start, one line per ledger and per party, **entered by hand by the Manager once the whole software is functional** (or imported), **approved by the Owner**; any difference parks in 1400 and the entry can be saved unbalanced only with 1400 carrying the gap; the first period lock refuses while 1400 ≠ 0 or no opening entry exists (the trial balance says "opening balances pending" until then). Editable until that lock; afterwards only by an Owner-approved journal. | OWNER DEC-139, DEC-148 |
| G3 | **Year-end close** (31 March) — *(Tally)*, HCC only locks the year and carries balance-sheet ledgers forward as next year's opening (step 5): (1) depreciation F3; (2) GST set-off for March; (3) TDS reconciliation E6; (4) **closing entry**: every Sales / Direct / Indirect income and expense ledger is closed to **Profit & Loss**, whose balance goes to **3100 Owner's capital**; 3000 Drawings is closed to 3100; (5) the balance-sheet ledgers' closing balances become **next year's opening** (generated, not typed); (6) the year is locked. | PRACTICE (standard close); ICAI formats S-22 |
| G4 | After the year lock, a correction is a journal in the **new** year referring to the old one; the Owner may reopen a year with a reason, which marks every later export as provisional (DEC-077 extended). | OWNER DEC-077 |
| G5 | **Correcting a locked day** never alters an issued invoice or credit note; it adds a dated reversing voucher (as DEC-077). | OWNER DEC-077 |

## 6. Permissions

| Action | Owner | Accountant | Manager | Cashier | Others |
|---|---|---|---|---|---|
| View ledgers, statements, reports | yes | yes | yes (no payroll detail) | collections report only (DEC-079) | no |
| Enter a manual voucher | yes | yes (pending) | request only | no | no |
| Approve a manual voucher | yes | no | no | no | no |
| Chart of accounts, mapping | yes | propose | no | no | no |
| Opening balances (DEC-148) | approve | enter | enter | no | no |
| Day / month lock | yes | yes | yes (day only) | no | no |
| Year close, reopen | yes | no | no | no | no |
| Export (CSV / Excel / Tally) | yes | yes | no | no | no |

Sources: DEC-058 (money settings Owner-only), DEC-063 (maker-checker, Accountant read-only money), DEC-079 (Cashier collections report, Tally export Owner + Accountant), DEC-061 (housekeeping / maintenance see no money).

## 7. Reports and their formulas

All reports read one posting table (voucher → entries → ledger, date, amount). Each has a date range, a party / ledger filter where relevant, and **CSV + Excel export**; party and ledger statements also export in bulk (one file per party, zipped) and as Tally XML (ledger masters + vouchers).

| # | Report | Formula / layout | Source |
|---|---|---|---|
| 7.1 | **Day book** | Every voucher of a date range in posting order: date, no., type, narration, ledger, Dr, Cr | PRACTICE |
| 7.2 | **Ledger / party statement** | Opening balance (sum of entries before the range, incl. opening journal) → each entry with running balance → closing. Dr balance shown as "Dr", Cr as "Cr"; never negative numbers | PRACTICE |
| 7.3 | **Trial balance (six columns)** | Per ledger: opening Dr/Cr, period Dr, period Cr, closing Dr/Cr; control ledgers summarise or expand parties; Σ Dr = Σ Cr in every column pair or the report is red | existing `accTrialBalance` extended |
| 7.4 | **Profit & loss** (ICAI NCE vertical format) | **Revenue from operations**: 4000 Rooms + 4200 F&B + 4100 Other services + 4300 Cancellation charges − 4900 Discounts − 4910 Credit notes. **Other income**: 4950, 4960. **Expenses**: consumption (5000, 5010, 5015), employee benefits (5100, 5110, 5120), finance costs (5090, 5080), depreciation (5200), other expenses (5020–5070, 5300, 5400, 5900). **Profit before tax** = revenue + other income − expenses. No tax line (proprietor). Sub-totals by Tally group as the second view | LAW S-22 |
| 7.5 | **Balance sheet** (ICAI NCE vertical) — *(Tally)*; an optional HCC view, not the statutory statement | **Owner's funds** = 3100 + current-year profit − 3000. **Non-current liabilities** = 2400 / 2410. **Current liabilities** = Sundry Creditors (2200 parties) + Duties & Taxes (2110–2150, 2310–2330 net of 1310–1350 shown as net payable or as a current asset when net Dr) + 2000 Advance deposits + 2300 / 2340. **Non-current assets** = fixed-asset blocks at WDV. **Current assets** = Cash-in-Hand (1000, 1005) + Bank Accounts (1030 parties, 1010, 1020) + Sundry Debtors (1100, 1110, 1115, 1120 parties) + Loans & Advances (1200, 1210, 1360, 1370). Must equal; 1400 and 9999 must be zero or the sheet is marked provisional | LAW S-22 |
| 7.6 | **Outstanding — receivables / payables with ageing** | Per party: open balance split 0–30 / 31–60 / 61–90 / 90+ days from invoice date (bills allocated oldest-first by payments) | PRACTICE |
| 7.7 | **GST summary** (ties to the GSTR-1 working summary that exists and to GSTR-3B) | Output by rate (5 %, 18 %) and section (B2B, B2CS, advances 11A / 11B, credit notes), ITC by head, RCM, set-off per §4.12, cash paid, TCS credit, balance in 1340 | LAW S-14; existing `gstr1Summary` |
| 7.8 | **Cash book / bank book** | 7.2 for 1000 / 1005 and for each bank sub-ledger; bank book shows the reconciliation columns (cleared date, statement balance) | existing cash book + new |
| 7.9 | **Cash-ratio report** (tax-audit test) | Cash receipts ÷ total receipts and cash payments ÷ total payments for the FY; flags above 5 % | LAW S-23 |
| 7.10 | **Depreciation schedule** | Per block: opening WDV, additions (≥ 180 days / < 180 days), disposals, rate, depreciation, closing WDV | LAW S-25 |
| 7.11 | **Reconciliation to operations** | Existing `accReconcile`: ledger movement = daily reports for revenue, collections, deposits, expenses, cash — unchanged and extended to F&B, OTA and Udhaar | existing module 20 §7 |

## 8. The worked month — April 2026 (fictional figures, the golden test)

Every figure below is a test expectation. The build is wrong if any ledger, the trial balance, the P&L or the balance sheet differs by a paisa. In this month every supply is at 5 % (no unit above ₹7,500, no banquet or laundry, not specified premises) and no bill is for an 18 % supply, so **no ITC is allowed on any bill** (§4.4); the advance carries no GST (DEC-142).

### 8.1 Opening balances, 1 April 2026 (DEC-139 journal)

| Ledger | Dr | Cr |
|---|---:|---:|
| 1000 Cash in hand | 20,000 | |
| 1030/HDFC Bank account | 3,00,000 | |
| 1500 Buildings | 60,00,000 | |
| 1510 Furniture & fittings | 8,00,000 | |
| 1520 Plant & machinery | 5,00,000 | |
| 1530 Computers | 80,000 | |
| 1110/Deccan Agro Ltd (company) | 40,000 | |
| 2200/Morning Supplies (vendor) | | 25,000 |
| 2000 Advance deposits | | 10,000 |
| 2400/HDFC term loan | | 20,00,000 |
| 3100 Owner's capital (balancing) | | 57,05,000 |
| **Total** | **77,40,000** | **77,40,000** |

### 8.2 Transactions

| # | Date | Event | Entries (₹) |
|---|---|---|---|
| T1 | April | Room nights on folios: gross ₹4,10,000, invoice discounts ₹10,000, taxable ₹4,00,000, GST 5 % ₹20,000 | Dr 1100 4,20,000 · Dr 4900 10,000 · Cr 4000 4,10,000 · Cr 2110 10,000 · Cr 2120 10,000 |
| T2 | April | Restaurant on folios: taxable ₹1,00,000, GST ₹5,000 | Dr 1100 1,05,000 · Cr 4200 1,00,000 · Cr 2110 2,500 · Cr 2120 2,500 |
| T3 | April | Walk-in restaurant bills (RST series): taxable ₹60,000, GST ₹3,000; half cash, half UPI | Dr 1000 31,500 · Dr 1010 31,500 · Cr 4200 60,000 · Cr 2110 1,500 · Cr 2120 1,500 |
| T4 | April | OTA stay invoiced to the guest, payable by Goibibo: taxable ₹20,000, GST ₹1,000 | Dr 1115/Goibibo 21,000 · Cr 4000 20,000 · Cr 2110 500 · Cr 2120 500 |
| T5a | check-outs | Opening advance ₹10,000 applied (booked before HCC: no advance-GST line) | Dr 2000 10,000 · Cr 1100 10,000 |
| T5b | check-outs | Deccan Agro company credit: taxable ₹60,000 + GST ₹3,000 | Dr 1110/Deccan 63,000 · Cr 1100 63,000 |
| T5c | check-outs | Guests pay the rest ₹4,52,000: cash 1,52,000, UPI 1,50,000, card 1,50,000 | Dr 1000 1,52,000 · Dr 1010 1,50,000 · Dr 1020 1,50,000 · Cr 1100 4,52,000 |
| T6 | 20 Apr | Advance by UPI for a May stay ₹31,500 (no GST, DEC-142) | Dr 1010 31,500 · Cr 2000 31,500 |
| T7a | 10 Apr | Deccan Agro pays the opening ₹40,000 (below the ₹50,000 month threshold: no TDS) | Dr 1030/HDFC 40,000 · Cr 1110/Deccan 40,000 |
| T7b | 30 Apr | Deccan Agro pays the April bill net of TDS 10 % on ₹60,000 = ₹6,000 | Dr 1030/HDFC 57,000 · Dr 1360 6,000 · Cr 1110/Deccan 63,000 |
| T8 | 5 Apr | Morning Supplies bill: consumables ₹50,000 + GST 18 % ₹9,000; ITC disallowed | Dr 5000 59,000 · Cr 2200/Morning 59,000 |
| T9 | 12 Apr | Kaveri Textiles bill: linen ₹40,000 + GST 5 % ₹2,000; ITC disallowed | Dr 5010 42,000 · Cr 2200/Kaveri 42,000 |
| T10 | 15 Apr | Electricity (exempt) paid by bank | Dr 5030 45,000 · Cr 1030/HDFC 45,000 |
| T11 | 18 Apr | Paid Morning Supplies ₹60,000 by bank (oldest bills first) | Dr 2200/Morning 60,000 · Cr 1030/HDFC 60,000 |
| T12 | April | Sundry cash expenses ₹8,000 (no GST) | Dr 5040 8,000 · Cr 1000 8,000 |
| T13a | 8 Apr | Salary advance to a staff member ₹5,000 cash | Dr 1200/Staff-07 5,000 · Cr 1000 5,000 |
| T13b | 30 Apr | Pay run: gross 2,40,000; PF wages 1,50,000 (12 % + 12 %), ESIC wages 2,00,000 (0.75 % + 3.25 %), PT ₹2,000, advance recovered ₹5,000 | Dr 5100 2,40,000 · Dr 5110 18,000 · Dr 5110 6,500 · Cr 2300 2,13,500 · Cr 2310 36,000 · Cr 2320 8,000 · Cr 2330 2,000 · Cr 1200/Staff-07 5,000 |
| T13c | 30 Apr | Net pay paid by bank | Dr 2300 2,13,500 · Cr 1030/HDFC 2,13,500 |
| T14a | April | UPI settlements reach the bank, no charge: 31,500 + 1,50,000 + 31,500 | Dr 1030/HDFC 2,13,000 · Cr 1010 2,13,000 |
| T14b | April | Card settlement ₹1,50,000 less MDR 1 % ₹1,500 + GST 18 % ₹270 | Dr 1030/HDFC 1,48,230 · Dr 5080 1,770 · Cr 1020 1,50,000 |
| T15a | 25 Apr | Cash banked ₹1,50,000 | Dr 1030/HDFC 1,50,000 · Cr 1000 1,50,000 |
| T15b | 26 Apr | Shift cash short ₹200 accepted | Dr 5900 200 · Cr 1000 200 |
| T16 | 28 Apr | Owner draws ₹50,000 from the bank | Dr 3000 50,000 · Cr 1030/HDFC 50,000 |
| T17 | 5 Apr | Loan EMI ₹55,000: principal 40,000, interest 15,000 | Dr 2400/HDFC loan 40,000 · Dr 5090 15,000 · Cr 1030/HDFC 55,000 |
| T18 | 30 Apr | Bank charges ₹500 + GST ₹90 | Dr 5080 590 · Cr 1030/HDFC 590 |
| T19 | 29 Apr | Goibibo payout: ₹21,000 less commission 15 % of 20,000 = 3,000 + GST 540, less TCS 0.5 % of 20,000 = 100 | Dr 1030/HDFC 17,360 · Dr 5070 3,540 · Dr 1340 100 · Cr 1115/Goibibo 21,000 |

### 8.3 Closing trial balance, 30 April 2026

| Ledger | Dr | Cr |
|---|---:|---:|
| 1000 Cash in hand | 40,300 | |
| 1030/HDFC Bank | 5,01,500 | |
| 1010 UPI clearing · 1020 Card clearing · 1100 Guest ledger · 1110/Deccan · 1115/Goibibo · 1200/Staff-07 · 2300 Net pay | 0 | 0 |
| 1360 TDS receivable | 6,000 | |
| 1340 GST electronic cash ledger | 100 | |
| 1500–1530 Fixed assets | 73,80,000 | |
| 3000 Owner's drawings | 50,000 | |
| 4900 Discounts allowed | 10,000 | |
| 5000 Housekeeping supplies 59,000 · 5010 Linen 42,000 · 5030 Electricity 45,000 · 5040 Sundry 8,000 · 5100 Salaries 2,40,000 · 5110 Employer PF & ESIC 24,500 · 5080 Bank & card charges 2,360 · 5900 Cash short 200 · 5090 Interest 15,000 · 5070 OTA commission 3,540 | 4,39,600 | |
| 2200/Morning Supplies | | 24,000 |
| 2200/Kaveri Textiles | | 42,000 |
| 2000 Advance deposits | | 31,500 |
| 2110 Output CGST | | 14,500 |
| 2120 Output SGST | | 14,500 |
| 2310 PF payable | | 36,000 |
| 2320 ESIC payable | | 8,000 |
| 2330 PT payable | | 2,000 |
| 2400/HDFC term loan | | 19,60,000 |
| 3100 Owner's capital | | 57,05,000 |
| 4000 Room revenue | | 4,30,000 |
| 4200 F&B revenue | | 1,60,000 |
| **Total** | **84,27,500** | **84,27,500** |

### 8.4 Profit & loss, April 2026

| | ₹ |
|---|---:|
| Revenue from operations: rooms 4,30,000 + F&B 1,60,000 − discounts 10,000 | 5,80,000 |
| Consumption: housekeeping 59,000 + linen 42,000 | 1,01,000 |
| Employee benefits: salaries 2,40,000 + employer PF & ESIC 24,500 | 2,64,500 |
| Finance costs: interest 15,000 + bank & card charges 2,360 | 17,360 |
| Other expenses: electricity 45,000 + sundry 8,000 + OTA commission 3,540 + cash short 200 | 56,740 |
| **Profit for the month** | **1,40,400** |

### 8.5 Balance sheet, 30 April 2026

| Owner's funds and liabilities | ₹ | Assets | ₹ |
|---|---:|---|---:|
| Owner's capital 57,05,000 + profit 1,40,400 − drawings 50,000 | 57,95,400 | Fixed assets (WDV; depreciation at year end) | 73,80,000 |
| Secured loan | 19,60,000 | Cash in hand | 40,300 |
| Sundry creditors (Morning 24,000 + Kaveri 42,000) | 66,000 | Bank | 5,01,500 |
| Duties & taxes: output GST 29,000 + PF / ESIC / PT 46,000 | 75,000 | TDS receivable | 6,000 |
| Advance deposits | 31,500 | GST electronic cash ledger (TCS credit) | 100 |
| **Total** | **79,27,900** | **Total** | **79,27,900** |

### 8.6 GST for April (E1, paid 20 May)

Output CGST 14,500 + SGST 14,500 = 29,000. ITC nil (§4.4). TCS credit ₹100 already in the cash ledger → challan **₹28,900**. Entries in May: Dr 2110 14,500 · Dr 2120 14,500 · Cr 1340 29,000; Dr 1340 28,900 · Cr 1030/HDFC 28,900. GSTR-1: B2B Deccan Agro ₹60,000 taxable; B2CS the rest; the May advance is not reported (DEC-142).

### 8.7 Year-end preview (31 March 2027, same assets, nothing bought)

Depreciation F2: buildings 10 % × 60,00,000 = 6,00,000; furniture 10 % × 8,00,000 = 80,000; plant 15 % × 5,00,000 = 75,000; computers 40 % × 80,000 = 32,000 → **₹7,87,000** (Dr 5200 · Cr each block). Then the closing entry G3.

## 9. Open points — all the owner's points are answered (APR-018); OP-10 stays with the CA and blocks nothing

| # | Point | Recommendation | Who |
|---|---|---|---|
| OP-1 | HCC as the book of record, Tally a copy | **Answered — DEC-140:** Tally is the final book; HCC prepares ledgers, TB and P&L and feeds Tally | Owner |
| OP-2 | Entity type | **Answered — DEC-141:** drop-down of entity types; the capital section follows it | Owner |
| OP-3 | **GST on advances** | **Answered — DEC-142:** no GST on booking advances (owner's ruling against the lead's advice; risk recorded; switch kept, default off); CA to confirm | Owner + CA |
| OP-4 | **ITC policy** | **Answered — DEC-143:** the hotel has 18 % supplies; ITC allowed only on bills directly for them, shared inputs disallowed; Rule 42 in Tally if the CA wants | Owner + CA |
| OP-5 | **Udhaar** scope | **Answered — DEC-144:** both; the screen asks | Owner |
| OP-6 | **Reverse charge** | **Answered — DEC-145:** a reverse-charge flag on the expense-category master; the Owner marks the categories | Owner |
| OP-7 | **Hotel as TDS deductor** | **Answered — DEC-146:** HCC holds back TDS at a dated rate table, the CA deposits and files | Owner + CA |
| OP-8 | Prepaid expenses | **Answered — DEC-147:** expense when paid | Owner |
| OP-9 | **Opening balances** | **Answered — DEC-148:** the option stays open; the Manager enters them by hand once the whole software is functional, the Owner approves; the first period lock waits for them | Owner |
| OP-10 | Section numbers under the **Income-tax Act 2025** marked VERIFY (cash limits s.186 / s.36 or 41 / s.185 / s.188; depreciation s.33–34, Rule 25; TDS rows in s.393) and the final text of the **Income-tax Rules 2026** Appendix I | Confirm once; the build stores them as text in the guard messages, not as logic | CA |
| OP-11 | Women's PT exemption slab | **Answered — DEC-149:** ₹25,000 a month | Owner (CA confirms) |
| OP-12 | OTA flows | **Answered — DEC-150:** every OTA pays net of commission and TCS with a statement | Owner |

## 10. Build plan — waves (the owner's go starts ACC-1)

Each wave is prototype **and** server, with the §8 figures as golden tests (`tests/stream-accounting-rulebook.mjs`, `Hcc.Accounting.Tests/RulebookMonthTests.cs`), an independent oracle (the same month posted in **ERPNext** or **GNUKhata**, statements compared), both full suites green, a report in `05 Delivery Evidence/Accounting/`, Opus agents (money / statutory), one heavy job at a time under a lock file.

| Wave | Scope | Depends on |
|---|---|---|
| **ACC-1 Chart and parties** | Group master (Tally tree), chart as a master with system ledgers, party sub-ledgers created from masters, posting table, ledger / party statement, six-column trial balance, opening-balance screen (DEC-139), migration of existing vouchers to sub-ledgers, Udhaar credit raised (A17), control-vs-detail Tally export | — |
| **ACC-2 Purchases, manual vouchers, GST** | Purchase voucher with ITC split (C1, C3–C5, C8, C10), manual Journal / Contra / Payment / Receipt with maker-checker, GST on advances (A6), OTA flow (A13), TDS receivable (A9), cash guards (B9–B11), month-end set-off (E1–E5), month lock | ACC-1 |
| **ACC-3 Statements and Tally export** | P&L (ICAI NCE format), ageing, GST summary tied to GSTR-1 / 3B as a reconciliation aid, day book, cash-ratio report, bulk exports (CSV / Excel per ledger and per party, zip), **Tally XML with ledger masters + groups + vouchers that imports cleanly into a fresh Tally company** (the DEC-140 deliverable; ETP's exporter at `E:\ETP\Code` as the base), optional balance-sheet view | ACC-2 |
| **ACC-4 Year lock and bank** (reduced by DEC-140) | Year lock with opening carry-forward (G3 step 5), bank reconciliation with statement import (B8), reopen rules (G4); fixed-asset register and depreciation journal only if the owner asks | ACC-3 |

Reference material for the agents (design only, never code — GPL / AGPL licences and a different stack): ERPNext's Indian chart of accounts and GST reports; GNUKhata's Tally-style groups and statements; Tally's group tree (S-24); ETP's Tally export at `E:\ETP\Code` for the XML.

## 11. Sources

Primary texts are cited by section; where only secondary summaries were reachable the entry says so and the rule is tagged VERIFY in the tables.

| # | Source |
|---|---|
| S-1 | Notification 11/2017-Central Tax (Rate), Sl. 7 (SAC 9963), as amended by **Notification 15/2025-CT(R), 17 Sep 2025, effective 22 Sep 2025** (56th GST Council): accommodation ≤ ₹7,500 per unit per day 5 % with the condition that credit of input tax on goods and services used in supplying the service has not been taken; above ₹7,500 18 %. Summarised by [TaxGuru](https://taxguru.in/goods-and-service-tax/gst-rate-hotels-restaurants-effective-22nd-september-2025.html) and [gstlearn](https://gstlearn.com/2025/09/17/cgst-r-nn-15-2025-seeks-to-amend-notification-no-11-2017-central-tax-rate-dated-28th-june-2017-to-implement-the-recommendations-of-the-56th-gst-council/); gazette text on cbic-gst.gov.in |
| S-2 | Notification 03/2022-CT(R), 13 Jul 2022 (withdrawal of the ≤ ₹1,000 exemption from 18 Jul 2022); summarised by [TaxGuru](https://taxguru.in/goods-and-service-tax/gst-hotel-guest-house-accommodation-current-legal-position.html) |
| S-3 | **Notification 05/2025-CT(R), 16 Jan 2025**, "specified premises" from 1 Apr 2025, Annexures VII–IX (opt-in / opt-out); CBIC FAQ reproduced by [Karnataka GST](https://gst.karnataka.gov.in/latestupdates/GSTFAQ28325.pdf) and [TaxGuru](https://taxguru.in/goods-and-service-tax/faqs-restaurant-service-supplied-specified-premises.html) |
| S-4 | IGST Act 2017 s.12(3)(b) (accommodation: location of the immovable property), s.12(4) (restaurant: where performed) |
| S-5 | CGST Act 2017 s.16 (conditions for ITC), s.17(2) and Rules 42–43 (apportionment), **s.17(5)** (blocked credits); clause guide at [TaxGuru](https://taxguru.in/goods-and-service-tax/gst-hospitality-industry-2026-hotel-accommodation-restaurant-gst-ratesitc-rule-42-reversal.html) |
| S-6 | CGST Act s.13(2) (time of supply of services), s.31(3)(d)–(e), Rules 50–51; **Notification 66/2017-Central Tax, 15 Nov 2017** (advance relief for goods only) — [GST Council](https://gstcouncil.gov.in/node/3993); Circular 38 (advance treatment) via [CEWACOR](https://cewacor.nic.in/Docs/GST-Circular/28OCTGSTCircular_38.pdf) |
| S-7 | CGST Act s.15(3) (discounts) |
| S-8 | **Circular 178/10/2022-GST, 3 Aug 2022** — [CBIC PDF](https://cbic-gst.gov.in/pdf/cir-178-08-2022-cgst.pdf) |
| S-9 | CGST Act s.2(30), s.8(a) (composite supply) |
| S-10 | QRMP scheme (Notification 84/2020-CT; Circular 143/13/2020) — summarised by [ClearTax](https://cleartax.in/s/quarterly-return-monthly-payment-qrmp-scheme-gst) |
| S-11 | **Notification 10/2023-Central Tax, 10 May 2023** (e-invoicing above ₹5 crore from 1 Aug 2023) — [EY alert](https://www.ey.com/en_in/technical/alerts-hub/2023/05/cbic-lowers-turnover-threshold-for-e-invoicing-to-inr5-crores-with-effect-from-1-august-2023) |
| S-12 | CGST Act s.34 (credit / debit notes), s.35 and Rule 56 (records), **s.36** (retention 72 months), s.50 (interest), Rule 46 (invoice) — [TaxGuru](https://taxguru.in/goods-and-service-tax/accounts-other-records-gst-retention-period.html) |
| S-13 | **Income-tax Act 2025** (in force 1 Apr 2026): s.393 TDS table (old 194-I rent rows Sl. 2(ii); old 194C), Form 168 / Form 130 renumbering — [tdsman](https://blog.tdsman.com/2026/06/tds-on-rent-section-3931-section-194i-a-b/), [Wikipedia](https://en.wikipedia.org/wiki/Income-tax_Act,_2025); rent threshold ₹50,000 per month or part (Finance Act 2025) — VERIFY against the Act's table |
| S-14 | CGST Act **s.49(5), s.49A, s.49B**, Rule 88A (Notification 16/2019-CT), Circular 98/17/2019 — [TaxGuru](https://taxguru.in/goods-and-service-tax/itc-utilization-rule-88a.html) |
| S-15 | CGST Act s.9(3); Notification 13/2017-CT(R) as amended (RCM services) |
| S-16 | CGST Act s.52 (TCS by e-commerce operators; rate 0.5 % from 10 Jul 2024, Notification 15/2024-CT) |
| S-17 | CBDT Circular 715 (8 Aug 1995) Q.20; Circular 5/2002 (regular arrangement); Circular 23/2017 (TDS on the amount excluding GST on services) — summarised by [CA Club](https://www.caclubindia.com/articles/tds-on-hotel-accommodation-35223.asp) |
| S-18 | Income-tax Act 2025 **s.186** (cash receipt ≥ ₹2 lakh; old 269ST; penalty s.451), s.185 / s.188 (loans in cash; old 269SS / 269T) — [TaxTMI](https://www.taxtmi.com/tmi_notes?id=1989), [ebizfiling](https://ebizfiling.com/blog/section-186-of-the-income-tax-act/) — VERIFY numbers |
| S-19 | Cash expenditure above ₹10,000 (old s.40A(3), Rule 6DD): new section reported as 36 by [taxupdate](https://taxupdate.in/income-tax/899/section-40a3-cash-payment-disallowance-tax-audit-2026-rule-6dd-section-36-income-tax-act-2025/) and as 41 by [itact2025.org](https://www.itact2025.org/circulars/c0037-2016-cash-payment-restrictions-us-40a3/) — VERIFY |
| S-20 | Income-tax Act 2025 **s.62** (books of account) — [Income Tax India](https://www.incometaxindia.gov.in/w/section-62-134) |
| S-21 | ICAI, *Applicability of Accounting Standards to Non-company Entities*, March 2021 (Level IV: turnover ≤ ₹10 crore, borrowings ≤ ₹2 crore; Annexure 2 exemptions) — [EIRC presentation](https://eirc-icai.org/uploads/background_materials/Presentation%20on%20AS%20for%20NCEs_EIRC,%20ICAI_1657526538.pdf) |
| S-22 | ICAI, *Guidance Note on Financial Statements of Non-Corporate Entities* (Aug 2023); phased applicability announced 31 Mar 2026 (Phase II: all entities, periods from 1 Apr 2026) — [Taxscan](https://www.taxscan.in/top-stories/icai-asb-announces-phased-applicability-of-guidance-notes-on-financial-statements-for-non-corporate-entities-llps-1444435), [TaxGuru](https://taxguru.in/chartered-accountant/icai-mandates-guidance-notes-llps-non-corporates-two-phases.html) |
| S-23 | Income-tax Act 2025 s.63 (tax audit: ₹1 crore; ₹10 crore when cash ≤ 5 %) — [incorpx](https://www.incorpx.io/guide/how-to-file-tax-audit-report-new-it-act-2025) — VERIFY |
| S-24 | Tally Solutions help, *Default groups* (28 predefined groups) — [help.tallysolutions.com](https://help.tallysolutions.com/docs/te9rel66/Creating_Masters/Accounts_Info/Default_Groups.htm) |
| S-25 | **Income-tax Rules 2026, Rule 25 and Appendix I** (WDV rates; published 5 May 2026) — [Income Tax India Appendix I](https://www.incometaxindia.gov.in/w/appendix-i-1); rates summarised by [TaxGuru](https://taxguru.in/income-tax/depreciation-income-tax-act-2025-section-34-rates-provisions.html) — VERIFY against the notified text |
| S-26 | Maharashtra State Tax on Professions Act 1975 slabs (₹175 / ₹200 / ₹300 Feb) — [BankBazaar](https://money.bankbazaar.com/tax/maharashtra-professional-tax-slab-rate.html); EPF Act 1952 s.6 (12 % + 12 %, wage ceiling ₹15,000); ESI Act (0.75 % + 3.25 %, ceiling ₹21,000) — as ported from SCC (DEC-115) |
| S-27 | HCC's own rules: `02 Planning/modules/HCC_Module_20_Accounting_Spec.md`; `08 Production/src/Hcc.Domain/Accounting/AccountingSource.cs`; `03 Prototype/src/domain/gst.ts`; Decision Register DEC-058, 061, 063, 065, 069, 072–074, 076–077, 079, 112, 115–116, 125, 129–139 |
