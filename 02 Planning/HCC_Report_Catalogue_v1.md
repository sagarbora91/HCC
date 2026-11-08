# HCC Report Catalogue v1 (wave W7 RPT)

Date: 1 November 2026. Status: draft for the W7 agents. Docs only: no code, no build.

**Purpose.** One list of every report HCC will have: what exists, what is partly built, what ACC-3 delivers and what is new. The W7 agents (rpt-engine, rpt-catalogue, rpt-ui, rpt-schedule, rpt-tests) build straight from it. Each entry gives the id, parameters, columns in order, grouping, who may see it, formats, data source, old-software equivalents, dependencies, status and priority.

**Sources.**
- `scratchpad\rpt\hcc-reports.md` - HCC's existing reports (inventory of 1 Nov 2026, sections A-L).
- `scratchpad\rpt\legacy-reports.md` - the old Lodge software: Part A (61 menu reports), Part B (57 dormant .rpt files), Part C (49 documents and registers opened by other forms).
- `02 Planning\HCC_Build_Plan_v2_2026-10-27.md` section W7; `02 Planning\HCC_Legacy_Feature_Gap_v1.md` section E.
- `worktrees\lf\02 Planning\agents\LF-CONTRACT.md` section 13 (data LF leaves for W7: rate changes A1, bill corrections A2, counter bills A7, credit guests B2, Form C A5, desk fields A3 and A4).
- `02 Planning\agents\ACC3-REPORTS-CONTRACT.md` (P&L, balance sheet, cash ratio, ageing, GST summary, Tally, exports; being built now).
- `CLAUDE.md` (money in integer paise, roles, DEC-061).

**Legacy ids.** `A12` = item 12 of Part A of `legacy-reports.md`. `C2.TodaysCreditDetail` = the row of Part C section 2 with that report name. Part B files are named in full in section 4.

## 1. Rules for every report

### 1.1 Naming and words
- Id = `rpt.<area>.<slug>`. Areas: `guests` (guests and stays), `rooms` (rooms and housekeeping), `money` (money and cash), `tax`, `acc` (accounting), `people`, `stock`, `rest` (restaurant), `admin` (admin and audit), `revenue` (revenue management, MEAS) and `study` (analytics and study, MEAS). Slug is lower-case with hyphens. An id never changes once built.
- **Status:** `built` (works in the prototype or server), `partial` (works but lacks a server side, a date range or an export), `ACC-3` (the ACC-3 wave is building it now), `planned` (new).
- **Priority:** P1 = needed at go-live (used daily or required by law or tax), P2 = useful, P3 = later.
- Money = integer paise everywhere; screens show it through `money()` / `rupees()`; Excel gets numbers with format `#,##0.00`, never text.

### 1.2 Money flag (rule M)
1. Every report has a **money flag** (y if any column shows hotel money). Every column has its own **money y/n** flag. Both are in each entry. The flag is stored in the catalogue, not worked out at run time.
2. The money gate of a report is named in its entry (`PERM-RPT-002` unless a module code applies: STK-004, MNT-004, HND-004, GSV-004, POS-004, OTA-004, CRM-004, GRP-004, HR-005, ACC-001).
3. A user without the money gate gets the report **without** its money columns (columns removed, not blanked) or, if every column is money, a refusal. Totals and ratios that depend on a removed column are removed too.
4. **Housekeeping (Supervisor, Housekeeper), Maintenance and Stock-only roles never see a money column** (DEC-061). They hold no money gate. The only exception is their own pay on `rpt.people.my-hr`. No report may use PERM-RPT-002 as the 'may see money' switch for these roles: the catalogue checks the flag, not the role name.
5. A report whose money flag is n may be opened by those roles if it carries a non-money permission (for example `rpt.rooms.room-status` with PERM-HK-001).

### 1.3 Permissions
**Roles used in entries:** Owner, Manager, Accountant, Cashier, Reception, Supervisor, Housekeeper, Maintenance, Tech admin, Restaurant.

**Report codes, enforced on the server exactly as declared** (today `ReportOperations` and `ReportExport` are declared but unused):
| Code | Meaning | Roles |
|---|---|---|
| PERM-RPT-001 | Operations reports (no hotel money) | Owner, Manager, Accountant |
| PERM-RPT-002 | Finance reports (money) | Owner A, Manager A, Accountant A, Cashier V.S (own shift) |
| PERM-RPT-003 | Export (Excel, PDF, CSV file) | Owner, Manager, Accountant |

**Accounting codes (this table replaces the wrong codes in the ACC-3 contract, sections 2 and 9):**
| Code | Use |
|---|---|
| PERM-ACC-001 | View books and every accounting report (the ACC-3 contract wrongly says ACC-003) |
| PERM-ACC-002 | Lock a date or month |
| PERM-ACC-003 | Unlock (Owner only) |
| PERM-ACC-004 | Change mapping |
| PERM-ACC-005 | Voucher CSV export |
| PERM-ACC-006 | Tally XML export (the ACC-3 contract wrongly says ACC-005) |

Other accounting codes stay as built: 007 chart edit, 008 / 009 opening enter / approve, 010 / 011 manual enter / approve, 012 month lock, 013 accounting.tax (Owner, Accountant).

**Print and export follow view (rule V).**
1. Nobody can print or export a report they cannot view. The print button and the export buttons appear only with the report's view permission.
2. Excel, PDF and CSV files also need PERM-RPT-003. So the Cashier may view and print own-shift reports but not export (open question 3).
3. File products with their own code keep it: voucher CSV needs ACC-005, Tally XML needs ACC-006.
4. Every print and every export is recorded (`rpt.admin.export-log`), as expense CSV and Tally already are.
5. Desk lists (arrivals, in-house, Form C, guest register) can be opened with the desk page permission in addition to PERM-RPT-001, because Reception does not hold RPT-001.

### 1.4 One date-range rule
Every date parameter uses one of these classes. Each entry names its class. The old 31-day server cap and the 62 / 366-day prototype cap are replaced by this table. Long ranges are paged on the server (100 rows a page, phone 50). Excel and PDF use the same maximum as the screen.
| Class | Default | Maximum |
|---|---|---|
| D-DAY | one business date; default today | max today; any earlier date with data, incl. legacy FYs |
| D-S | range; default last 7 days | max 31 days |
| D-B | range; default month start to today | max 92 days |
| D-F | range; default FY start to today | max 366 days; FY history allowed |
| D-M | month; default current month | any month with data |
| D-FY | financial year; default current FY | any FY with data, incl. legacy FYs |
| D-ASOF | one date; default today | max today; any earlier date with data |
| D-NOW | no date: shows the state now; no history |  |
| D-FWD | range; default today to +7 days | max 92 days; may look 90 days ahead |

- From must not be after To. A future date is refused except in class D-FWD.
- **FY history:** where data exists, a report may reach into earlier FYs, including the ten legacy FYs (17-18 to 26-27) loaded by the legacy import. Such rows carry a 'legacy' marker in the header. Reports on vouchers, payroll, expenses and stock have no legacy data; the picker simply stops at their first date.
- Tally XML vouchers keep their own 31-day file limit (a limit of the file, not of the report).

### 1.5 One renderer, three outputs
1. The server runs one query per report and gets one result set (rows, totals, parameters, flags). Screen, Excel and PDF are drawn from that same result. The figures are identical by construction; the test pack compares them.
2. Each entry lists extra formats (CSV, ZIP, XML). CSV cells that start with `=`, `+`, `-` or `@` are neutralised (existing `CsvFormula.Neutralise`).
3. Excel: one sheet per report plus an 'About' sheet (parameters, date basis, run time, user, 'provisional' when set). Header bold, money as numbers, dates as dates, totals as values (formulas only in the computed-column hook).
4. PDF: A4, landscape when more than 8 columns. Header: property name, report name, parameters, run time (IST), user. Footer: page x of y and the date-basis note. Documents with a legal layout (invoice, credit note, payslip) keep their own layouts.
5. File name: `<property code>-<report id>-<from>-<to>.<ext>`.
6. Proposed limits: screen pages of 100 rows (phone 50); Excel and PDF up to 50,000 rows; above that the user is asked to narrow the range.
7. Computed-column hook: a column may be an expression over other columns. The Excel-like calculation engine (OP-13, about 3 Nov) plugs in here. Until then, computed columns are fixed code.

### 1.6 Masking
- Guest names show as `J•••` to roles that hold neither billing nor reservations. Entries state which roles see names (open question 2 for the Accountant).
- ID numbers, passport numbers and visa numbers show masked (`XXXX-XXXX-1234`) in every format. Reveal needs PERM-GST-003 and is audited (`GUEST_ID_VIEW`). A file never holds a full ID number. Photos never appear in a report.
- Bank account numbers show masked on screen; the bank file holds the full number.

### 1.7 Phone
`yes` = full report as cards, no sideways scroll at 360 px, 48 px targets. `summary` = tiles and the top rows only, with 'open on desktop' for the full table. `no` = not offered on the phone. Excel and PDF can still be requested from the phone; the file is made on the server.

### 1.8 Date basis, provisional marker, drill-down
- Every entry shows its date basis in the header: charges by service date, receipts by receipt date, expenses by business date, GST by departure date, cash by shift date. The report dictionary (`rpt.admin.report-dictionary`) holds the definitions.
- A report whose range includes an open or unlocked period is marked **provisional** on every format.
- Every figure opens its source rows (`rpt.money.source-rows`), on desktop and server alike.

### 1.9 Scheduled runs
A schedule runs as its owner's role. It cannot widen what that role may see. Money reports are delivered only to Owner, Manager and Accountant addresses (open question 7). Runs and failures are listed in `rpt.admin.scheduled-runs`.

### 1.10 Legacy and demo columns
Columns of new entries are what HCC should show, from HCC's real data. The HTML demo's invented columns (for example passport and visa on the old NRI report, F&B and laundry on the room-wise report, the 5 % deposit on the salary sheet) are not copied as old-software columns. Where the demo and the old software differ, the entry says so.

## 2. Summary table

| Id | Name | Area | Status | Priority | Legacy ids | Money |
|---|---|---|---|---|---|---|
| rpt.guests.daily-ops | Daily operations | Guests and stays | built | P1 | - | y |
| rpt.guests.movement-register | Arrivals and departures register | Guests and stays | planned | P1 | A30 | y |
| rpt.guests.arrivals-list | Arrivals list (printable) | Guests and stays | built | P1 | - | y |
| rpt.guests.inhouse-list | In-house list (printable) | Guests and stays | built | P1 | - | y |
| rpt.guests.reservations-register | Reservations register | Guests and stays | planned | P2 | - | y |
| rpt.guests.history-summary | History summary (FY to date) | Guests and stays | partial | P2 | - | y |
| rpt.guests.source-mix | Bookings by source | Guests and stays | partial | P2 | - | y |
| rpt.guests.customer-history | Customer history (Customer 360) | Guests and stays | partial | P2 | C8.DuplicateCustomer | y |
| rpt.guests.guest-directory | Guest directory | Guests and stays | planned | P3 | A55 | n |
| rpt.guests.guest-register | Guest register (statutory) | Guests and stays | planned | P1 | C1.GuestRegister, C4.CheckInReport, C4.PhotoAddressProof | n |
| rpt.guests.form-c-register | Form C register (foreign guests) | Guests and stays | planned | P1 | A13, C4.CFormReport | n |
| rpt.guests.rate-changes | Rate change detail | Guests and stays | planned | P1 | A36 | y |
| rpt.guests.guest-tariff | Guest tariff detail | Guests and stays | planned | P2 | A42 | y |
| rpt.guests.company-business | Business from company | Guests and stays | planned | P2 | A12 | y |
| rpt.guests.userwise-activity | User-wise check-in and check-out | Guests and stays | planned | P2 | A33 | n |
| rpt.guests.service-lines | Room service and extras lines | Guests and stays | planned | P2 | A5, A6, A43, C4.RoomServedItemDetail | y |
| rpt.guests.groups-pickup | Group pickup report | Guests and stays | built | P2 | - | n |
| rpt.guests.group-master-folio | Group master folio | Guests and stays | built | P2 | - | y |
| rpt.guests.hall-enquiry-funnel | Hall enquiry funnel | Guests and stays | planned | P3 | - | y |
| rpt.guests.hall-events | Hall events | Guests and stays | planned | P3 | - | y |
| rpt.guests.hall-occupancy | Hall occupancy | Guests and stays | planned | P3 | - | y |
| rpt.rooms.occupancy | Occupancy, ADR and RevPAR | Rooms and housekeeping | built | P1 | A35 | y |
| rpt.rooms.adr-trend | ADR and occupancy trend | Rooms and housekeeping | planned | P2 | - | y |
| rpt.rooms.room-status | Room status today | Rooms and housekeeping | planned | P1 | A26 | n |
| rpt.rooms.allocation-grid | Room allocation grid | Rooms and housekeeping | planned | P2 | A31, A56 | n |
| rpt.rooms.rooms-left | Rooms left by type | Rooms and housekeeping | planned | P2 | A7 | n |
| rpt.rooms.room-wise-revenue | Room-wise and room-type revenue | Rooms and housekeeping | planned | P2 | A11, A44, C1.TariffSalesDetail | y |
| rpt.rooms.hk-tasks | Housekeeping tasks | Rooms and housekeeping | planned | P2 | - | n |
| rpt.rooms.hk-task-sla | Housekeeping task SLAs | Rooms and housekeeping | planned | P2 | - | n |
| rpt.rooms.hk-turnaround | Room cleaning time | Rooms and housekeeping | planned | P2 | A40 | n |
| rpt.rooms.room-status-history | Room status history | Rooms and housekeeping | planned | P3 | - | n |
| rpt.rooms.maintenance-report | Maintenance report | Rooms and housekeeping | built | P2 | - | y |
| rpt.rooms.work-orders | Work orders and registers | Rooms and housekeeping | partial | P2 | - | y |
| rpt.rooms.handover-dashboard | Checklists and handover dashboard | Rooms and housekeeping | built | P2 | - | y |
| rpt.rooms.guest-services | Guest services report | Rooms and housekeeping | partial | P2 | - | y |
| rpt.money.daily-collections | Daily collections | Money and cash | built | P1 | A10 | y |
| rpt.money.revenue-tax | Revenue and tax | Money and cash | built | P1 | - | y |
| rpt.money.receipts-deposits | Receipts and deposits | Money and cash | built | P1 | C2.PreviousAdvance | y |
| rpt.money.expenses-cash | Expenses and cash | Money and cash | built | P1 | - | y |
| rpt.money.source-rows | Source rows (drill-down) | Money and cash | partial | P2 | - | y |
| rpt.money.closing-report | Closing report (day end) | Money and cash | built | P1 | A30, C2.CounterCashierReport, C3.DayEndReportABC | y |
| rpt.money.day-pack | Day pack (A4) | Money and cash | built | P1 | A30, C1.DayEndDetail, C2.CounterCashierDetail | y |
| rpt.money.closing-records | Earlier closing records | Money and cash | built | P2 | - | y |
| rpt.money.cash-book | Cash book | Money and cash | built | P1 | - | y |
| rpt.money.settles-to | Cash book by account | Money and cash | built | P2 | - | y |
| rpt.money.cash-out-register | Banking, transfers and drawings | Money and cash | built | P2 | - | y |
| rpt.money.shift-report | Shift report (X and Z) and cash hand-over | Money and cash | planned | P1 | A38 | y |
| rpt.money.cash-count-sheet | Cash count sheet | Money and cash | planned | P2 | - | y |
| rpt.money.collection-detail | Collection detail (receipts register) | Money and cash | planned | P1 | A10, C1.BillWisePaymentMode, C1.BillSummaryAccount, C1.BillSummaryAccountDetail, C2.TodaysCustomerDeposit, C2.DepositDetail, C8.CusomerDepositReport | y |
| rpt.money.payment-corrections | Payment type corrections | Money and cash | planned | P2 | C7.WrongPaymentEntry | y |
| rpt.money.credit-bills | Credit bills of the day | Money and cash | planned | P1 | C2.TodaysCreditDetail | y |
| rpt.money.counter-bills | Counter bills | Money and cash | planned | P2 | A39, C2.DirectBillSummary, C4.DirectBillDoc | y |
| rpt.money.refunds | Refunds | Money and cash | planned | P2 | A41 | y |
| rpt.money.discount-log | Discounts given | Money and cash | planned | P2 | C2.OtherDiscount | y |
| rpt.money.expense-register | Expense register | Money and cash | partial | P1 | A50, C2.TodaysExpenses | y |
| rpt.money.expenses-by-head | Expenses by head | Money and cash | planned | P1 | A50 | y |
| rpt.money.expense-month | Expenses month and tax feed | Money and cash | built | P2 | - | y |
| rpt.money.expense-budgets | Expense budgets | Money and cash | partial | P3 | - | y |
| rpt.money.udhaar-recurring-petty | Udhaar, recurring expenses and petty cash | Money and cash | built | P3 | - | y |
| rpt.money.vendor-ageing | Vendor ageing and payments | Money and cash | built | P2 | - | y |
| rpt.money.company-credit | Company credit and statement | Money and cash | partial | P2 | - | y |
| rpt.money.ota-commission | OTA commission | Money and cash | built | P2 | - | y |
| rpt.money.ota-payouts | OTA payouts and reconciliation | Money and cash | built | P2 | - | y |
| rpt.money.plans-extras | Plans and extras | Money and cash | built | P3 | - | y |
| rpt.money.bank-book | Bank book | Money and cash | planned | P2 | - | y |
| rpt.tax.gstr1 | GSTR-1 working summary | Tax | built | P1 | A1 | y |
| rpt.tax.sac-summary | SAC summary (Table 12) | Tax | planned | P1 | A2 | y |
| rpt.tax.documents-issued | Documents issued (Table 13) | Tax | planned | P1 | A3 | n |
| rpt.tax.gst-by-bill | GST by bill | Tax | planned | P1 | A18, A19, A20 | y |
| rpt.tax.invoice-register | Invoice register | Tax | planned | P1 | A45, C1.BillSummary, C1.BillSummerShort, C4.SalesBill, C4.LodgingBillReceipt | y |
| rpt.tax.bill-corrections | Bill corrections | Tax | planned | P1 | A4 | y |
| rpt.tax.gst-month | GST month (set-off working) | Tax | built | P1 | - | y |
| rpt.tax.gst-summary | GST summary (ties to GSTR-1 and GSTR-3B) | Tax | ACC-3 | P1 | - | y |
| rpt.tax.tds-deductee | TDS deductee report and Form 168 | Tax | built | P1 | - | y |
| rpt.tax.compliance-register | Compliance register and calendar | Tax | built | P1 | - | n |
| rpt.tax.compliance-dashboard | Compliance dashboard | Tax | built | P2 | - | y |
| rpt.tax.licence-expiry | Licence expiry | Tax | planned | P2 | - | n |
| rpt.acc.vouchers | Vouchers (one day) | Accounting | built | P1 | - | y |
| rpt.acc.trial-balance | Trial balance (six columns) | Accounting | built | P1 | - | y |
| rpt.acc.statement | Ledger or party statement | Accounting | built | P1 | A27 | y |
| rpt.acc.day-book | Day book | Accounting | built | P2 | - | y |
| rpt.acc.chart-parties-opening | Chart, parties and opening balances | Accounting | built | P2 | - | y |
| rpt.acc.exceptions-recon-mapping | Exceptions, reconciliation and mapping | Accounting | built | P2 | - | y |
| rpt.acc.manual-register | Manual voucher register | Accounting | built | P2 | - | y |
| rpt.acc.asset-register | Fixed-asset register | Accounting | partial | P2 | - | y |
| rpt.acc.lock-history | Period lock history | Accounting | built | P3 | - | n |
| rpt.acc.tally-xml | Tally export (masters and vouchers) | Accounting | ACC-3 | P1 | - | n |
| rpt.acc.pl | Profit and loss | Accounting | ACC-3 | P2 | - | y |
| rpt.acc.balance-sheet | Balance sheet | Accounting | ACC-3 | P3 | - | y |
| rpt.acc.cash-ratio | Cash ratio | Accounting | ACC-3 | P2 | - | y |
| rpt.acc.ageing | Outstanding with ageing | Accounting | ACC-3 | P1 | A22 | y |
| rpt.acc.statements-bulk | Bulk party statements (zip) | Accounting | ACC-3 | P3 | - | n |
| rpt.people.payroll-register | Monthly payroll register | People | built | P1 | - | y |
| rpt.people.statutory | Statutory statement (PF, ESIC, PT) | People | built | P1 | - | y |
| rpt.people.bank-file | Bank transfer file | People | built | P1 | - | y |
| rpt.people.payslip | Payslip | People | built | P1 | - | y |
| rpt.people.leave-report | Leave report | People | partial | P2 | - | n |
| rpt.people.attendance | Attendance report | People | partial | P2 | - | n |
| rpt.people.roster | Weekly roster | People | built | P3 | - | n |
| rpt.people.staff-list | Staff list | People | built | P3 | - | y |
| rpt.people.advances-fnf | Salary advances and full-and-final | People | built | P2 | - | y |
| rpt.people.statutory-rates | Statutory rates | People | built | P3 | - | y |
| rpt.people.leave-balances | Leave balances | People | planned | P2 | - | n |
| rpt.people.pf-esic-files | PF ECR and ESIC upload files | People | planned | P3 | - | y |
| rpt.people.my-hr | My shifts and pay | People | built | P3 | - | y |
| rpt.stock.stock-sheet | Stock by location | Stock | built | P1 | - | y |
| rpt.stock.reorder | Reorder alerts | Stock | built | P2 | - | y |
| rpt.stock.laundry | Laundry batches | Stock | built | P3 | - | y |
| rpt.stock.counts | Stock counts | Stock | built | P3 | - | y |
| rpt.stock.losses | Losses | Stock | built | P3 | - | y |
| rpt.stock.consumption | Consumption per occupied room-night | Stock | built | P2 | - | n |
| rpt.stock.par | Par and minimums | Stock | built | P3 | - | n |
| rpt.stock.movements | Stock movements | Stock | built | P3 | - | n |
| rpt.stock.grn-register | Goods received (GRN) register | Stock | planned | P2 | - | y |
| rpt.rest.day-summary | Outlet day summary | Restaurant | built | P1 | - | y |
| rpt.rest.closed-bills | Closed bills | Restaurant | built | P2 | - | y |
| rpt.rest.sales-by-item | Sales by item | Restaurant | planned | P2 | - | y |
| rpt.rest.sales-by-outlet-hour | Sales by outlet and hour | Restaurant | planned | P3 | - | y |
| rpt.rest.voids | POS voids | Restaurant | planned | P2 | - | y |
| rpt.admin.audit-log | Audit log (filters: actor, entity type, entity id, date; before/after view; CSV; money masked for roles without money, DEC-061) - F4-T3 | Admin and audit | built | P1 | - | n |
| rpt.admin.export-log | Export and print log | Admin and audit | planned | P2 | - | n |
| rpt.admin.scheduled-runs | Scheduled runs | Admin and audit | planned | P2 | - | n |
| rpt.admin.system-health | System health | Admin and audit | built | P2 | - | n |
| rpt.admin.backup-status | Backup status | Admin and audit | built | P3 | - | n |
| rpt.admin.legacy-import | Legacy import runs and reconciliation | Admin and audit | built | P3 | - | y |
| rpt.admin.config-history | Configuration and masters history | Admin and audit | built | P2 | - | n |
| rpt.admin.rate-sheet | Rate sheet, publish log and change history | Admin and audit | partial | P2 | C7.RoomTariffCard | y |
| rpt.admin.report-dictionary | Report dictionary | Admin and audit | built | P2 | - | n |
| rpt.admin.cancelled-lines | Cancelled folio lines | Admin and audit | planned | P2 | C1.CancelItem | y |
| rpt.admin.desk-reminders | Desk reminders and wake-up calls | Admin and audit | planned | P3 | C7.AlertsGuestInfo | n |
| rpt.guests.manager-flash | Manager's flash | Guests and stays | built (MEAS) | P1 | - | y |
| rpt.guests.regrets-denials | Regrets and denials | Guests and stays | built (MEAS) | P1 | - | n |
| rpt.guests.rate-variance | Rate variance / rate check | Guests and stays | built (MEAS) | P1 | - | y |
| rpt.rooms.room-discrepancy | Room discrepancy | Rooms and housekeeping | built (MEAS) | P1 | - | n |
| rpt.money.high-balance | High-balance and credit-limit | Money and cash | built (MEAS) | P1 | - | y |
| rpt.money.allowances-voids | Allowances, rebates, voids and corrections | Money and cash | built (MEAS) | P1 | - | y |
| rpt.guests.no-show-cancel | No-show, cancellation and early departure | Guests and stays | built (MEAS) | P2 | - | y |
| rpt.rooms.inventory-exceptions | Complimentary, house-use and out-of-order | Rooms and housekeeping | built (MEAS) | P1 | - | n |
| rpt.money.cashier-variance | Cashier and shift variance | Money and cash | built (MEAS) | P1 | - | y |
| rpt.revenue.demand-calendar | Demand calendar | Revenue management | built (MEAS) | P1 | - | y |
| rpt.revenue.pickup-pace | Pickup and pace | Revenue management | built (MEAS) | P2 | - | y |
| rpt.revenue.displacement | Displacement calculator | Revenue management | built (MEAS) | P2 | - | y |
| rpt.revenue.overbooking-wash | Overbooking and wash | Revenue management | built (MEAS) | P2 | - | y |
| rpt.revenue.restriction-effect | Length-of-stay and minimum-stay controls | Revenue management | built (MEAS) | P2 | - | y |
| rpt.revenue.rate-parity | Rate shop and parity | Revenue management | built (MEAS) | P2 | - | y |
| rpt.guests.satisfaction | Guest satisfaction and NPS | Guests and stays | built (MEAS) | P1 | - | n |
| rpt.guests.reputation-index | Online reputation | Guests and stays | built (MEAS) | P2 | - | n |
| rpt.guests.service-recovery | Service recovery | Guests and stays | built (MEAS) | P2 | - | y |
| rpt.guests.desk-timing | Arrival and departure experience | Guests and stays | built (MEAS) | P2 | - | n |
| rpt.guests.upsell | Upsell and upgrade | Guests and stays | built (MEAS) | P2 | - | y |
| rpt.guests.repeat-loyalty | Repeat guests and loyalty base | Guests and stays | built (MEAS) | P2 | - | y |
| rpt.rooms.hk-productivity | Attendant productivity and turnaround | Rooms and housekeeping | built (MEAS) | P1 | - | n |
| rpt.rooms.ooo-days | Out-of-order and lost inventory | Rooms and housekeeping | built (MEAS) | P2 | - | y |
| rpt.rooms.ppm-compliance | Preventive maintenance compliance | Rooms and housekeeping | built (MEAS) | P2 | - | n |
| rpt.rooms.utilities-per-room | Energy, water and LPG per occupied room-night | Rooms and housekeeping | built (MEAS) | P2 | - | y |
| rpt.stock.linen-cost | Linen, laundry and guest-supply cost per occupied room | Stock | built (MEAS) | P2 | - | y |
| rpt.acc.asset-life | Asset register, FF&E reserve and renovation cycle | Accounting | built (MEAS) | P2 | - | y |
| rpt.people.staff-productivity | Staff per occupied room, overtime, absenteeism | People | built (MEAS) | P2 | - | n |
| rpt.people.attrition | Attrition and tenure | People | built (MEAS) | P2 | - | n |
| rpt.people.agent-scorecard | Performance by desk user | People | built (MEAS) | P2 | - | y |
| rpt.people.training | Training hours and certifications | People | built (MEAS) | P2 | - | n |
| rpt.people.staff-pulse | Staff pulse (optional) | People | built (MEAS) | P3 | - | n |
| rpt.study.revenue-monthly | Monthly revenue and cash (FY history) | Analytics and study | built (MEAS) | P2 | - | y |
| rpt.study.year-comparison | Year comparison | Analytics and study | built (MEAS) | P2 | - | y |
| rpt.study.index-vs-base | Index vs a base year | Analytics and study | built (MEAS) | P2 | - | y |
| rpt.study.fy-deep-dive | FY deep dive (price-volume, segments, top bills) | Analytics and study | built (MEAS) | P2 | - | y |
| rpt.study.occupancy-history | Occupancy history (three denominators) | Analytics and study | built (MEAS) | P2 | - | n |
| rpt.study.operations-patterns | Operations patterns | Analytics and study | built (MEAS) | P2 | - | n |
| rpt.study.rooms-and-rates | Rooms, rates and discounts | Analytics and study | built (MEAS) | P2 | - | y |
| rpt.study.zero-rate-complimentary | Zero-rate and complimentary stays | Analytics and study | built (MEAS) | P2 | - | y |
| rpt.study.receivables-bad-debt | Receivables and bad debt (legacy + live ageing) | Analytics and study | built (MEAS) | P2 | - | y |
| rpt.study.revenue-kpi | Occupancy, ADR, RevPAR by month, FY, type, channel | Analytics and study | built (MEAS) | P2 | - | y |
| rpt.study.channel-economics | Channel economics and net ADR | Analytics and study | built (MEAS) | P2 | - | y |
| rpt.study.pricing-shape | Day-of-week, length-of-stay, segment mix, rate ladder, upgrades | Analytics and study | built (MEAS) | P2 | - | y |
| rpt.study.guest-repeat-value | Repeat guests, frequency, lifetime value, repeat customer list | Analytics and study | built (MEAS) | P2 | - | y |
| rpt.study.guest-origin-plan | Guest origin, nationality, meal plan, company guests | Analytics and study | built (MEAS) | P2 | - | n |
| rpt.study.housekeeping-operations | Turnaround, idle streaks, check-in/out vs standard, daily load | Analytics and study | built (MEAS) | P2 | - | n |
| rpt.study.fnb-halls-ancillary | F&B on bills, breakfast covers, hall utilisation, ancillary | Analytics and study | built (MEAS) | P2 | - | y |
| rpt.study.finance-cost | USALI P&L, break-even, paid-outs, cash & bank, GST by slab | Analytics and study | built (MEAS) | P2 | - | y |
| rpt.study.corporate-accounts | Corporate accounts, win-back list, account base | Analytics and study | built (MEAS) | P2 | - | y |
| rpt.study.compliance-risk | Capture rates, audit-trail counts, risk register, licence calendar | Analytics and study | built (MEAS) | P2 | - | n |
| rpt.study.forecast-scenarios | Seasonality indices, forecast (three methods), scenarios, budget vs actual, comp-set index | Analytics and study | built (MEAS) | P2 | - | y |
| rpt.study.owner-dashboard | Owner 12-KPI monthly dashboard | Analytics and study | built (MEAS) | P1 | - | y |
| rpt.study.management-pack | Management study pack (PDF + Excel) | Analytics and study | built (MEAS) | P1 | - | y |

## 3. Entries by area

### 3.1 Guests and stays (21 entries)

#### rpt.guests.daily-ops - Daily operations
- **Purpose:** One row per business date: arrivals, departures, occupied room-nights, net revenue, net receipts, expenses.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) | Server today caps at 31 days |
| Month | month | D-M (month; default current month; any month with data) | shortcut that fills From/To |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Business date | date | n |  |
| 2 | Day status | enum | n | Closed / Open / Future |
| 3 | Arrivals | int | n | checked in |
| 4 | Departures | int | n | checked out |
| 5 | Occupied room-nights | int | n |  |
| 6 | Net revenue | money | y | excl. tax |
| 7 | Net receipts | money | y | payments minus refunds |
| 8 | Expenses | money | y | posted |

- **Grouping, sort, totals:** One row per date, oldest first. Total row = tile totals. Click a tile or row to open source rows (rpt.money.source-rows).
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: selector `reportPack` (reportLines / reportReceipts / reportExpenses / reportStaysOn); server `GET /reports/daily`, `IReportSource`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Date basis: charges by service date, receipts by receipt date, expenses by business date. Print stamp today says DEMONSTRATION; the server version must not. Ops-only (money-free) variant: counts columns only, PERM-RPT-001, for roles that hold RPT-001 but not RPT-002 (none today; kept for the matrix).

#### rpt.guests.movement-register - Arrivals and departures register
- **Purpose:** Date-range register of arrivals and departures with check-out basis, late charges and desk fields.
- **Status:** planned. **Priority:** P1. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Type | enum | both | arrivals / departures / both |
| Period | date range | D-FWD (range; default today to +7 days; max 92 days; may look 90 days ahead) | arrivals may look ahead |
| Status | enum | all | booked / checked in / checked out / cancelled / no-show |
| Room type | master | all |  |
| Source | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n | arrival or departure date |
| 2 | Booking | text | n |  |
| 3 | Guest | text (masked) | n |  |
| 4 | Room | text | n |  |
| 5 | Room type | text | n |  |
| 6 | Pax | int | n | adults + children |
| 7 | Nights | int | n |  |
| 8 | Source | text | n |  |
| 9 | Status | enum | n |  |
| 10 | Check-in time | datetime | n |  |
| 11 | Check-out time | datetime | n |  |
| 12 | Check-out basis | enum | n | 24-hour or fixed time (LF A6) |
| 13 | Late check-out charge | money | y | LF A6 |
| 14 | Purpose of visit | enum | n | LF A3 |
| 15 | Vehicle no | text | n | LF A4 |
| 16 | Balance due | money | y |  |

- **Grouping, sort, totals:** Sorted by date, then room. Sub-total per date (count of rooms, pax). Total row.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-001. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** summary.
- **Data source:** New: stays and folio tables (existing); desk fields from LF §8.
- **Legacy equivalents:** A30
- **Depends on:** LF A3, A4, A6
- **Notes:** Covers the arrivals / departures blocks of the old Day End Report A. Printable list for the desk stays as rpt.guests.arrivals-list. Balance due and late charge removed for roles without money view.

#### rpt.guests.arrivals-list - Arrivals list (printable)
- **Purpose:** Paper list of today's expected arrivals for the desk; works while the server is down.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Business date | date | D-DAY (one business date; default today; max today; any earlier date with data, incl. legacy FYs) | also +1 day for tomorrow |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Room | text | n |  |
| 2 | Guest | text (masked) | n |  |
| 3 | Booking | text | n |  |
| 4 | Guests | int | n |  |
| 5 | Stay | text | n | arrival - departure |
| 6 | Registered | flag | n |  |
| 7 | Phone | text | n |  |
| 8 | Advance paid | money | y | billing only |
| 9 | Notes | text | n | blank column for pen |

- **Grouping, sort, totals:** Sorted by room. No totals.
- **Who may see it:** Owner, Manager, Reception. **Permission:** PERM-RPT-001 or page permission `checkin`. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Print (PDF), Screen. No Excel (paper aid).. **Phone:** yes.
- **Data source:** Existing: selector `stayListRows`, modal `front-list` (DEC-081); server derived from reservation reads.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Advance column only with `billing`.

#### rpt.guests.inhouse-list - In-house list (printable)
- **Purpose:** Paper list of every occupied room with guest and balance due.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| (none) | - | D-NOW (no date: shows the state now; no history) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Room | text | n |  |
| 2 | Guest | text (masked) | n |  |
| 3 | Booking | text | n |  |
| 4 | Guests | int | n |  |
| 5 | Stay | text | n |  |
| 6 | Phone | text | n |  |
| 7 | Balance due | money | y | billing only |
| 8 | Notes | text | n | blank column for pen |

- **Grouping, sort, totals:** Sorted by room. Total balance due (money roles only).
- **Who may see it:** Owner, Manager, Reception. **Permission:** PERM-RPT-001 or page permission `inhouse`. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Print (PDF), Screen, Excel. **Phone:** yes.
- **Data source:** Existing: selector `stayListRows`, modal `front-list`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.guests.reservations-register - Reservations register
- **Purpose:** Search-and-export list of reservations by arrival date, status and source.
- **Status:** planned. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Period | date range | D-FWD (range; default today to +7 days; max 92 days; may look 90 days ahead) | by arrival date |
| Status | enum | all |  |
| Source | master | all |  |
| Search | text |  | name, mobile, booking id |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Booking | text | n |  |
| 2 | Created | date | n |  |
| 3 | Guest | text (masked) | n |  |
| 4 | Arrival | date | n |  |
| 5 | Departure | date | n |  |
| 6 | Room type | text | n |  |
| 7 | Rooms | int | n |  |
| 8 | Plan | text | n |  |
| 9 | Source | text | n |  |
| 10 | Status | enum | n |  |
| 11 | Advance | money | y |  |
| 12 | Quoted total | money | y |  |

- **Grouping, sort, totals:** Sorted by arrival date. Count per status in the footer.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-001. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing screen: `GET /reservations`, `GET /availability` (calendar). New report form wraps it.
- **Legacy equivalents:** A37 (purpose only)
- **Depends on:** nothing outside W7
- **Notes:** The old Advance Booking Detail was not used; this is the HCC equivalent for all bookings.

#### rpt.guests.history-summary - History summary (FY to date)
- **Purpose:** Monthly occupancy, ADR, RevPAR, stays and cancellations for the year so far.
- **Status:** partial. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Financial year | FY | D-FY (financial year; default current FY; any FY with data, incl. legacy FYs) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Month | month | n |  |
| 2 | Occupancy | pct | n |  |
| 3 | ADR | money | y |  |
| 4 | RevPAR | money | y |  |
| 5 | Room revenue | money | y |  |
| 6 | Stays | int | n |  |
| 7 | Cancelled / no-show | int | n |  |

- **Grouping, sort, totals:** One row per month. FY total row.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `features/reports/HistorySummary.tsx`; server `ServerHistorySummary` loops `/reports/daily` per month (thin). New: one server query.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Server version has fewer fields today. FY history includes legacy FYs from the legacy import.

#### rpt.guests.source-mix - Bookings by source
- **Purpose:** Bookings, nights, room revenue and commission by booking source and segment.
- **Status:** partial. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Source | text | n |  |
| 2 | Segment | enum | n |  |
| 3 | Bookings | int | n |  |
| 4 | Cancelled / no-show | int | n |  |
| 5 | Nights | int | n |  |
| 6 | Room charges | money | y |  |
| 7 | Commission | money | y | masked server-side without money view |

- **Grouping, sort, totals:** Group by segment, then source. Footer totals. Segment strip at the top.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** summary.
- **Data source:** Existing prototype only: `fdmSourceReport`; server has the booking-sources master but no endpoint (new query).
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.guests.customer-history - Customer history (Customer 360)
- **Purpose:** Stays, nights, lifetime spend and average tariff per guest, with imported legacy stays; duplicate list.
- **Status:** partial. **Priority:** P2. **Money flag:** y (gate PERM-CRM-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Search | text |  | name or mobile |
| Tag | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Guest | text (masked) | n |  |
| 2 | Mobile | text | n |  |
| 3 | Stays | int | n |  |
| 4 | Nights | int | n |  |
| 5 | Lifetime spend | money | y | PERM-CRM-004 |
| 6 | Average tariff | money | y | PERM-CRM-004 |
| 7 | Last stay | date | n |  |
| 8 | Tags | text | n |  |
| 9 | Legacy stays | int | n | from legacy import |

- **Grouping, sort, totals:** Sorted by lifetime spend or last stay. No totals.
- **Who may see it:** Owner, Manager, Reception. **Permission:** PERM-CRM-001. **Masking:** money by PERM-CRM-004. Money columns need PERM-CRM-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `customerIndex`, `customerSearch`; server `GET /customers`, `/customers/profile`, `/customers/duplicates`, `/customers/merges`.
- **Legacy equivalents:** C8.DuplicateCustomer
- **Depends on:** Legacy import (LG)
- **Notes:** Screen only today; add export and print. Duplicate check list is a second tab.

#### rpt.guests.guest-directory - Guest directory
- **Purpose:** Master list of guests with contact details, for mailing and follow-up.
- **Status:** planned. **Priority:** P3. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Address group | master | all |  |
| City | text | all |  |
| Has stayed since | date | none |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Guest | text (masked) | n |  |
| 2 | Gender | enum | n |  |
| 3 | Phone | text | n |  |
| 4 | Email | text | n |  |
| 5 | City | text | n |  |
| 6 | Address group | text | n |  |
| 7 | Vehicle | text | n |  |
| 8 | Last stay | date | n |  |

- **Grouping, sort, totals:** Sorted by name.
- **Who may see it:** Owner, Manager. **Permission:** PERM-CRM-001. **Masking:** phone and email masked for others. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** New: `gst.guest` (existing) and legacy guests.
- **Legacy equivalents:** A55
- **Depends on:** nothing outside W7
- **Notes:** Personal data. No ID numbers or photos in any column.

#### rpt.guests.guest-register - Guest register (statutory)
- **Purpose:** Register of guests staying, with ID, purpose, vehicle and next destination, for police and licence checks.
- **Status:** planned. **Priority:** P1. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Status | enum | all | in house / checked out |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Registration no | text | n |  |
| 2 | Check-in | datetime | n |  |
| 3 | Guest | text (masked) | n |  |
| 4 | Nationality | text | n |  |
| 5 | City | text | n |  |
| 6 | Phone | text | n |  |
| 7 | ID type | text | n |  |
| 8 | ID number | text (masked) | n | XXXX-XXXX-1234 |
| 9 | Room | text | n |  |
| 10 | Adults | int | n |  |
| 11 | Children | int | n |  |
| 12 | Purpose of visit | enum | n | LF A3 |
| 13 | Vehicle no | text | n | LF A4 |
| 14 | Coming from | text | n |  |
| 15 | Going to | text | n |  |
| 16 | Check-out | datetime | n |  |
| 17 | Bill no | text | n |  |

- **Grouping, sort, totals:** Sorted by registration number. No totals; count in footer.
- **Who may see it:** Owner, Manager, Reception. **Permission:** PERM-RPT-001 or page permission `checkin`; ID reveal PERM-GST-003. **Masking:** ID masked. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** New: `gst.guest`, `gst.guest_party`, stays; legacy stays from `lgc`.
- **Legacy equivalents:** C1.GuestRegister, C4.CheckInReport, C4.PhotoAddressProof
- **Depends on:** LF A3, A4; DEC-193 party IDs
- **Notes:** The registration card, GRC and photo-proof pages are documents made by the stays module; this register lists them and reprints on click. Privacy: no photos, no full ID in Excel or PDF.

#### rpt.guests.form-c-register - Form C register (foreign guests)
- **Purpose:** Register of foreign guests with passport, visa and Form C submission status.
- **Status:** planned. **Priority:** P1. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Submitted | enum | all | yes / no (to submit) |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Check-in | date | n |  |
| 2 | Guest | text (masked) | n |  |
| 3 | Nationality | text | n |  |
| 4 | Passport no | text (masked) | n |  |
| 5 | Passport issue date and place | text | n |  |
| 6 | Visa no and type | text | n |  |
| 7 | Visa expiry | date | n |  |
| 8 | Arrived in India on | date | n |  |
| 9 | Arrived from | text | n |  |
| 10 | Next destination | text | n |  |
| 11 | Purpose | text | n |  |
| 12 | Room | text | n |  |
| 13 | Check-out | date | n |  |
| 14 | Form C submitted | flag | n |  |
| 15 | Submitted on | date | n |  |

- **Grouping, sort, totals:** Sorted by check-in. Count of not-yet-submitted rows shown in red at the top.
- **Who may see it:** Owner, Manager, Reception. **Permission:** PERM-RPT-001 or page permission `checkin`. **Masking:** passport and visa masked except for PERM-GST-003. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** summary.
- **Data source:** New: `gst.stay_form_c` (LF A5). Field names follow LF A5; if they differ, LF wins.
- **Legacy equivalents:** A13, C4.CFormReport
- **Depends on:** LF A5
- **Notes:** Do not copy the demo's passport, visa and arrived-from columns as old-software columns: the old report showed business amounts per country. These columns are what HCC should show.

#### rpt.guests.rate-changes - Rate change detail
- **Purpose:** Every change of room rate or guest count during a stay, with reason and who allowed it.
- **Status:** planned. **Priority:** P1. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Room | master | all |  |
| Changed by | user | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Changed at | datetime | n |  |
| 2 | Room | text | n |  |
| 3 | Booking | text | n |  |
| 4 | Guest | text (masked) | n |  |
| 5 | Old rate | money | y |  |
| 6 | New rate | money | y |  |
| 7 | Old guests | int | n |  |
| 8 | New guests | int | n |  |
| 9 | Reason | text | n |  |
| 10 | Authorised by | user | n |  |
| 11 | Changed by | user | n |  |

- **Grouping, sort, totals:** Sorted by time. Count and net change in rate in the footer.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** New: `gst.stay_rate_change` (LF A1).
- **Legacy equivalents:** A36
- **Depends on:** LF A1
- **Notes:** Control report: shows staff who change tariffs. Legacy 1,609 rows a year were written for it.

#### rpt.guests.guest-tariff - Guest tariff detail
- **Purpose:** What a guest or company paid per stay and whether an agreed rate applied.
- **Status:** planned. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Guest or company | search | required |  |
| Period | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) | FY history allowed (replaces the old FY combo) |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Stay | text | n |  |
| 2 | Room | text | n |  |
| 3 | Room type | text | n |  |
| 4 | Plan | text | n |  |
| 5 | Standard rate | money | y |  |
| 6 | Rate source | enum | n | standard / agreed / manual |
| 7 | Agreed rate | money | y | LF B1 |
| 8 | Extra charges | money | y |  |
| 9 | Discount | money | y |  |
| 10 | Net tariff | money | y |  |

- **Grouping, sort, totals:** Per stay, newest first. Total tariff, extras and discount.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** New: `gst.guest_agreed_rate`, stays with `rate_source = 'agreed'` (LF B1).
- **Legacy equivalents:** A42
- **Depends on:** LF B1

#### rpt.guests.company-business - Business from company
- **Purpose:** Room-nights and billed amount by company, for companies billed instead of the guest.
- **Status:** planned. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) |  |
| Company | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Company | text | n |  |
| 2 | Guests | int | n |  |
| 3 | Room-nights | int | n |  |
| 4 | Tariff | money | y |  |
| 5 | Other charges | money | y |  |
| 6 | Total business | money | y |  |

- **Grouping, sort, totals:** Group by company; drill to the bills. Grand total.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** summary.
- **Data source:** New: stays and invoices where the billed-to party differs from the guest.
- **Legacy equivalents:** A12
- **Depends on:** nothing outside W7

#### rpt.guests.userwise-activity - User-wise check-in and check-out
- **Purpose:** Who checked guests in and out, by user.
- **Status:** planned. **Priority:** P2. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| User | user | all |  |
| Action | enum | both | check-in / check-out / both |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | User | text | n |  |
| 2 | Date and time | datetime | n |  |
| 3 | Action | enum | n |  |
| 4 | Booking | text | n |  |
| 5 | Room | text | n |  |
| 6 | Guest | text (masked) | n |  |
| 7 | Bill no | text | n |  |

- **Grouping, sort, totals:** Group by user with counts of check-ins and check-outs.
- **Who may see it:** Owner, Manager. **Permission:** PERM-ADM-004. **Masking:** guest names shown. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** New: audit trail (existing, `GET /admin/audit`).
- **Legacy equivalents:** A33
- **Depends on:** nothing outside W7
- **Notes:** The old report's date filter did not bind the check-out branch (precedence bug). The date range here binds both.

#### rpt.guests.service-lines - Room service and extras lines
- **Purpose:** Food, laundry and other service lines posted to guest folios.
- **Status:** planned. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Group | enum | all | food / laundry / other / extras |
| Item | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n |  |
| 2 | Room | text | n |  |
| 3 | Guest | text (masked) | n |  |
| 4 | Item | text | n |  |
| 5 | Group | enum | n |  |
| 6 | Qty | int | n |  |
| 7 | Rate | money | y |  |
| 8 | Amount | money | y |  |
| 9 | Tax | money | y |  |
| 10 | Source | enum | n | folio / room-service POS |
| 11 | Bill ref | text | n |  |

- **Grouping, sort, totals:** Sorted by date, room. Subtotal per group, grand total.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** no.
- **Data source:** New: folio lines (existing); catalogue lines `GET /catalogue/links` for the plan split.
- **Legacy equivalents:** A5, A6, A43, C4.RoomServedItemDetail
- **Depends on:** nothing outside W7
- **Notes:** The laundry module itself was not used by the old hotel; only lines posted to bills existed.

#### rpt.guests.groups-pickup - Group pickup report
- **Purpose:** Rooms held, picked up and released for each group block.
- **Status:** built. **Priority:** P2. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| (none) | - | D-NOW (no date: shows the state now; no history) |  |
| Block | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Block | text | n |  |
| 2 | Dates and cut-off | text | n |  |
| 3 | Type | enum | n |  |
| 4 | Held | int | n |  |
| 5 | Picked | int | n |  |
| 6 | Pickup | pct | n |  |
| 7 | Released | int | n |  |
| 8 | Remaining | int | n |  |
| 9 | At risk | flag | n |  |
| 10 | Status | enum | n |  |

- **Grouping, sort, totals:** One row per block. No totals.
- **Who may see it:** Owner, Manager, Reception. **Permission:** PERM-RPT-001 or page permission `reservations`. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing prototype: `grpPickupReport`; server `GET /groups`. Server report form is new.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Print only today.

#### rpt.guests.group-master-folio - Group master folio
- **Purpose:** All charges of one group block with tax and totals.
- **Status:** built. **Priority:** P2. **Money flag:** y (gate PERM-GRP-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Block | master | required |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Booking | text | n |  |
| 2 | Guest | text (masked) | n |  |
| 3 | Room | text | n |  |
| 4 | Status | enum | n |  |
| 5 | Rate | money | y |  |
| 6 | Posted net | money | y |  |
| 7 | Tax | money | y |  |
| 8 | Total | money | y |  |

- **Grouping, sort, totals:** One row per booking in the block. Totals.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002; money also PERM-GRP-004. **Masking:** guest names shown. Money columns need PERM-GRP-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `grpMasterFolio`; server `GET /groups`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Print only today.

#### rpt.guests.hall-enquiry-funnel - Hall enquiry funnel
- **Purpose:** Enquiries to quotes to confirmed events.
- **Status:** planned. **Priority:** P3. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Stage | enum | n |  |
| 2 | Enquiries | int | n |  |
| 3 | Converted | pct | n |  |
| 4 | Quoted value | money | y |  |

- **Grouping, sort, totals:** One row per stage.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** summary.
- **Data source:** New: hall booking module (HB, not built).
- **Legacy equivalents:** none (HCC only)
- **Depends on:** HB hall booking wave

#### rpt.guests.hall-events - Hall events
- **Purpose:** Events held or booked, by hall and date.
- **Status:** planned. **Priority:** P3. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) | Forward dates allowed. |
| Hall | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n |  |
| 2 | Hall | text | n |  |
| 3 | Event | text | n |  |
| 4 | Client | text | n |  |
| 5 | Pax | int | n |  |
| 6 | Status | enum | n |  |
| 7 | Billed | money | y |  |

- **Grouping, sort, totals:** Sorted by date.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** summary.
- **Data source:** New: hall booking module.
- **Legacy equivalents:** C8.FunctionProspectus (appendix)
- **Depends on:** HB hall booking wave

#### rpt.guests.hall-occupancy - Hall occupancy
- **Purpose:** Days each hall was used.
- **Status:** planned. **Priority:** P3. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Hall | text | n |  |
| 2 | Days available | int | n |  |
| 3 | Days used | int | n |  |
| 4 | Utilisation | pct | n |  |
| 5 | Revenue | money | y |  |

- **Grouping, sort, totals:** One row per hall.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** summary.
- **Data source:** New: hall booking module.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** HB hall booking wave

### 3.2 Rooms and housekeeping (14 entries)

#### rpt.rooms.occupancy - Occupancy, ADR and RevPAR
- **Purpose:** Sellable and occupied room-nights with occupancy, ADR and RevPAR per day.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Business date | date | n |  |
| 2 | Sellable | int | n | rooms available |
| 3 | Occupied | int | n |  |
| 4 | Occupancy | pct | n |  |
| 5 | Room revenue | money | y |  |
| 6 | ADR | money | y |  |
| 7 | RevPAR | money | y |  |

- **Grouping, sort, totals:** One row per date. Total row recomputes ratios from totals (not an average of ratios).
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: tab `occupancy` of Reports; server `GET /reports/daily`.
- **Legacy equivalents:** A35
- **Depends on:** LF A6 (due-out, late charges)
- **Notes:** Occupancy needs a money-free view for Reception and Supervisor: columns Sellable, Occupied, Occupancy only, PERM-RPT-001. Formulas shown in the footer.

#### rpt.rooms.adr-trend - ADR and occupancy trend
- **Purpose:** Month-by-month ADR, RevPAR and occupancy across years, including legacy years.
- **Status:** planned. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From year | FY | D-FY (financial year; default current FY; any FY with data, incl. legacy FYs) |  |
| To year | FY | D-FY (financial year; default current FY; any FY with data, incl. legacy FYs) | max 10 years |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Month | month | n |  |
| 2 | Occupancy | pct | n |  |
| 3 | ADR | money | y |  |
| 4 | RevPAR | money | y |  |
| 5 | Room revenue | money | y |  |
| 6 | Same month last year | pct | n | change in ADR |

- **Grouping, sort, totals:** One row per month. Chart on screen and PDF.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** summary.
- **Data source:** New: daily pack and legacy history (`lgc`).
- **Legacy equivalents:** none (HCC only)
- **Depends on:** Legacy import (LG)

#### rpt.rooms.room-status - Room status today
- **Purpose:** Every room with its stay and housekeeping state at this moment.
- **Status:** planned. **Priority:** P1. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| (none) | - | D-NOW (no date: shows the state now; no history) |  |
| Filter | enum | all | vacant / occupied / dirty / out of order / due out |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Room | text | n |  |
| 2 | Type | text | n |  |
| 3 | Floor | text | n |  |
| 4 | Status | enum | n |  |
| 5 | Housekeeping status | enum | n |  |
| 6 | Guest | text (masked) | n | hidden for Housekeeping |
| 7 | Pax | int | n |  |
| 8 | Check-in | date | n |  |
| 9 | Expected check-out | date | n |  |
| 10 | Next arrival | date | n |  |
| 11 | Notes | text | n |  |

- **Grouping, sort, totals:** Sorted by room sequence. Counts by status in the header (check-ins, check-outs, rooms).
- **Who may see it:** Owner, Manager, Accountant, Reception, Supervisor, Housekeeper, Maintenance. **Permission:** PERM-RPT-001 or PERM-HK-001. **Masking:** guest names only for Owner, Manager, Reception. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** New report form over `GET /rooms/board`, `/rooms/status` (screens exist).
- **Legacy equivalents:** A26, A29 (appendix)
- **Depends on:** nothing outside W7
- **Notes:** No money column at all, so every housekeeping role may open it.

#### rpt.rooms.allocation-grid - Room allocation grid
- **Purpose:** Rooms against dates showing who is in which room.
- **Status:** planned. **Priority:** P2. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-S (range; default last 7 days; max 31 days) | Max 31 days (grid width). |
| Room type | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Room | text | n |  |
| 2 | Room type | text | n |  |
| 3 | One column per date | text | n | guest or status code |
| 4 | Nights occupied | int | n |  |

- **Grouping, sort, totals:** By room type then room. Row total = nights occupied.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-001. **Masking:** guest names shown. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** New: stays and availability (existing `GET /availability`).
- **Legacy equivalents:** A31, A56
- **Depends on:** nothing outside W7
- **Notes:** Old layout was a 31-day grid (A31) and a 14-day grid (A56); HCC uses one grid, 31 days.

#### rpt.rooms.rooms-left - Rooms left by type
- **Purpose:** Rooms still free per room type per date.
- **Status:** planned. **Priority:** P2. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-S (range; default last 7 days; max 31 days) | Forward dates allowed (D-FWD max). |
| Room type | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n |  |
| 2 | Room type | text | n |  |
| 3 | Total rooms | int | n |  |
| 4 | Occupied | int | n |  |
| 5 | Held (group blocks) | int | n |  |
| 6 | Out of order | int | n |  |
| 7 | Free | int | n |  |

- **Grouping, sort, totals:** By date then type.
- **Who may see it:** Owner, Manager, Accountant, Reception. **Permission:** PERM-RPT-001 or page permission `reservations`. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `available()` and `GET /availability`; new report form.
- **Legacy equivalents:** A7
- **Depends on:** nothing outside W7

#### rpt.rooms.room-wise-revenue - Room-wise and room-type revenue
- **Purpose:** Nights sold and tariff earned per room and per room type.
- **Status:** planned. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) |  |
| Group by | enum | room | room / room type / floor |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Room or type | text | n |  |
| 2 | Nights sold | int | n |  |
| 3 | Tariff | money | y |  |
| 4 | Extra person | money | y |  |
| 5 | Other room charges | money | y |  |
| 6 | Total | money | y |  |
| 7 | Average rate | money | y |  |

- **Grouping, sort, totals:** Grouped as chosen. Grand total.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** summary.
- **Data source:** New: folio room lines by service date.
- **Legacy equivalents:** A11, A44, C1.TariffSalesDetail
- **Depends on:** nothing outside W7
- **Notes:** The old report held room-type columns (AC double, non-AC single). HCC groups by master room type instead.

#### rpt.rooms.hk-tasks - Housekeeping tasks
- **Purpose:** Tasks by room, type, status and time.
- **Status:** planned. **Priority:** P2. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Status | enum | all |  |
| Task type | master | all |  |
| Assigned to | user | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Task | text | n |  |
| 2 | Room | text | n |  |
| 3 | Type | text | n |  |
| 4 | Assigned to | user | n |  |
| 5 | Created | datetime | n |  |
| 6 | Started | datetime | n |  |
| 7 | Finished | datetime | n |  |
| 8 | Status | enum | n |  |
| 9 | Inspection | enum | n | pass / fail / none |

- **Grouping, sort, totals:** Sorted by created time. Counts by status.
- **Who may see it:** Owner, Manager, Supervisor. **Permission:** PERM-HK-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing screen: `GET /housekeeping/tasks`; no report form today.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** No money column.

#### rpt.rooms.hk-task-sla - Housekeeping task SLAs
- **Purpose:** Time to finish tasks against target, and inspection pass rate.
- **Status:** planned. **Priority:** P2. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Task type | master | all |  |
| Floor | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Task type | text | n |  |
| 2 | Tasks | int | n |  |
| 3 | Average minutes | int | n |  |
| 4 | Within target | pct | n |  |
| 5 | Overdue | int | n |  |
| 6 | Inspection pass rate | pct | n |  |

- **Grouping, sort, totals:** By task type. Total row.
- **Who may see it:** Owner, Manager, Supervisor. **Permission:** PERM-HK-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** summary.
- **Data source:** New: task timestamps and inspection results (existing).
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** No money column.

#### rpt.rooms.hk-turnaround - Room cleaning time
- **Purpose:** Time spent on each room between start and finish, by day.
- **Status:** planned. **Priority:** P2. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Room | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Room | text | n |  |
| 2 | Date | date | n |  |
| 3 | From | datetime | n |  |
| 4 | To | datetime | n |  |
| 5 | Minutes | int | n |  |
| 6 | Done by | user | n |  |

- **Grouping, sort, totals:** By room then date. Total minutes per room.
- **Who may see it:** Owner, Manager, Supervisor. **Permission:** PERM-HK-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** New: housekeeping task timestamps (replaces the old table `RoomHouseKeepingPeriod`).
- **Legacy equivalents:** A40
- **Depends on:** nothing outside W7
- **Notes:** No money column.

#### rpt.rooms.room-status-history - Room status history
- **Purpose:** Status changes of a room over time.
- **Status:** planned. **Priority:** P3. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Room | master | required |  |
| Period | date range | D-B (range; default month start to today; max 92 days) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Changed at | datetime | n |  |
| 2 | Room | text | n |  |
| 3 | From status | enum | n |  |
| 4 | To status | enum | n |  |
| 5 | By | user | n |  |
| 6 | Reason | text | n |  |

- **Grouping, sort, totals:** Sorted by time.
- **Who may see it:** Owner, Manager, Supervisor. **Permission:** PERM-HK-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** New: room status events (audit trail).
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** No money column.

#### rpt.rooms.maintenance-report - Maintenance report
- **Purpose:** Mean time to repair, PM compliance, SLA breaches, room downtime, work orders by age.
- **Status:** built. **Priority:** P2. **Money flag:** y (gate PERM-MNT-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| (none) | - | D-NOW (no date: shows the state now; no history) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Work order | text | n |  |
| 2 | Priority | enum | n |  |
| 3 | Status | enum | n |  |
| 4 | Age (days) | int | n |  |
| 5 | Room | text | n |  |
| 6 | Downtime | int | n |  |
| 7 | Parts cost | money | y | PERM-MNT-004 |

- **Grouping, sort, totals:** Tiles first, then tables: open by age, downtime by room, by asset type, SLA breaches.
- **Who may see it:** Owner, Manager, Maintenance. **Permission:** PERM-MNT-003. **Masking:** parts cost PERM-MNT-004. Money columns need PERM-MNT-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `mntReportData`; server `GET /maintenance/report`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Print only today. Maintenance role never sees the parts-cost column.

#### rpt.rooms.work-orders - Work orders and registers
- **Purpose:** Open work orders, PM schedule, assets, AMC contracts, vendors, spares, repair requests.
- **Status:** partial. **Priority:** P2. **Money flag:** y (gate PERM-MNT-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| List | enum | open work orders | open work orders / PM schedule / assets / AMC / vendors / spares / requests |
| Status | enum | open |  |
| Priority | enum | all |  |
| Period | date range | D-B (range; default month start to today; max 92 days) | for closed work orders |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Work order or asset | text | n |  |
| 2 | Type | text | n |  |
| 3 | Priority | enum | n |  |
| 4 | Status | enum | n |  |
| 5 | Due | date | n |  |
| 6 | Room | text | n |  |
| 7 | Part cost | money | y | PERM-MNT-004 |
| 8 | AMC value | money | y | PERM-MNT-004 or MNT-005 |

- **Grouping, sort, totals:** Sorted by priority then due date.
- **Who may see it:** Owner, Manager, Maintenance. **Permission:** PERM-MNT-003. **Masking:** money by PERM-MNT-004. Money columns need PERM-MNT-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: `GET /maintenance/work-orders`, `/registers`, `/amcs`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Screen only today; add range and export.

#### rpt.rooms.handover-dashboard - Checklists and handover dashboard
- **Purpose:** Completion of shift checklists and unacknowledged notes by role or desk.
- **Status:** built. **Priority:** P2. **Money flag:** y (gate PERM-HND-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Business date | date | D-DAY (one business date; default today; max today; any earlier date with data, incl. legacy FYs) |  |
| Group by | enum | role | role / desk |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Role or desk | text | n |  |
| 2 | Checklists | int | n |  |
| 3 | Submitted | int | n |  |
| 4 | Not started | int | n |  |
| 5 | Items done | int | n |  |
| 6 | Completion | pct | n |  |
| 7 | Overdue items | int | n |  |
| 8 | Past shift end | int | n |  |
| 9 | Cash on handover | money | y | PERM-HND-004 |

- **Grouping, sort, totals:** By role or desk. Overdue items listed below.
- **Who may see it:** Owner, Manager. **Permission:** PERM-HND-003. **Masking:** cash PERM-HND-004. Money columns need PERM-HND-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `hovDashboard`; server `GET /handover/dashboard?date&by`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Print only today.

#### rpt.rooms.guest-services - Guest services report
- **Purpose:** Response times, complaints and lost and found.
- **Status:** partial. **Priority:** P2. **Money flag:** y (gate PERM-GSV-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) | Today there is no date filter |
| By | enum | type | type / department |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Group | text | n |  |
| 2 | Requests | int | n |  |
| 3 | Done | int | n |  |
| 4 | Average response (min) | int | n |  |
| 5 | Within SLA | pct | n |  |
| 6 | Open | int | n |  |
| 7 | Overdue | int | n |  |
| 8 | Complaints | int | n |  |
| 9 | Recovery cost | money | y | PERM-GSV-004 |

- **Grouping, sort, totals:** By request type or department. Complaints and lost-found as separate sections.
- **Who may see it:** Owner, Manager, Reception. **Permission:** PERM-GSV-001 or PERM-RPT-001. **Masking:** recovery cost PERM-GSV-004. Money columns need PERM-GSV-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `GET /guest-services/reports/response`, `/complaints`, `/lost-found`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** No date range today; print only.

### 3.3 Money and cash (30 entries)

#### rpt.money.daily-collections - Daily collections
- **Purpose:** Net collections for one business date by tender, with deposits.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Business date | date | D-DAY (one business date; default today; max today; any earlier date with data, incl. legacy FYs) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Tender | text | n |  |
| 2 | Collected | money | y | payments minus refunds |
| 3 | Deposits received | money | y |  |
| 4 | Deposits returned | money | y |  |

- **Grouping, sort, totals:** One row per tender. Totals: net collections, deposits received, deposits returned.
- **Who may see it:** Owner, Manager, Accountant; Cashier own shift only. **Permission:** PERM-RPT-002 (Cashier: view and print own shift, no export). **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `GET /cashier/collections?date` (production only).
- **Legacy equivalents:** A10 (summary block)
- **Depends on:** nothing outside W7
- **Notes:** The Cashier's whole Reports page (DEC-079). Own shift only for the Cashier.

#### rpt.money.revenue-tax - Revenue and tax
- **Purpose:** Room, service, discounts, adjustments, net revenue and GST per business date.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Business date | date | n |  |
| 2 | Room | money | y |  |
| 3 | Service | money | y | incl. F&B charged to folios |
| 4 | Discounts | money | y |  |
| 5 | Adjustments and corrections | money | y |  |
| 6 | Net revenue | money | y | excl. tax |
| 7 | Tax (CGST + SGST) | money | y |  |
| 8 | Charges incl. tax | money | y |  |

- **Grouping, sort, totals:** One row per date. Server adds GST by category rows (category, tax rate, lines, taxable, CGST, SGST, tax).
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: Reports tab `revenue`; server `GET /reports/daily` (`GstCategoryRow`).
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Illustrative, not the statutory GST report (that is rpt.tax.gst-by-bill and rpt.tax.gstr1).

#### rpt.money.receipts-deposits - Receipts and deposits
- **Purpose:** Receipts by tender, deposits in and out, company credit raised.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Receipt date | date | n |  |
| 2 | One column per tender | money | y | payments minus refunds |
| 3 | Net receipts | money | y |  |
| 4 | Deposits in | money | y |  |
| 5 | Deposits out | money | y |  |
| 6 | Company credit (city ledger) | money | y |  |
| 7 | Deposits held now | money | y | point in time |

- **Grouping, sort, totals:** One row per date. Totals per tender.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: Reports tab `receipts`.
- **Legacy equivalents:** C2.PreviousAdvance
- **Depends on:** nothing outside W7

#### rpt.money.expenses-cash - Expenses and cash
- **Purpose:** Drawer, UPI and bank, vendor-credit expenses, cash in and out of the drawer, expected cash against counted.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Business date | date | n |  |
| 2 | Day | enum | n |  |
| 3 | Opening float | money | y |  |
| 4 | Expected cash | money | y |  |
| 5 | Counted | money | y |  |
| 6 | Difference | money | y |  |
| 7 | Cash expenses | money | y |  |

- **Grouping, sort, totals:** One row per date. Tiles: drawer, UPI / bank, vendor-credit, reversals, posted, cash in, cash out.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: Reports tab `expcash`; `DrawerFigures` per till on the server.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.money.source-rows - Source rows (drill-down)
- **Purpose:** The rows behind any figure of any report; total equals the figure.
- **Status:** partial. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Report and figure | ref | required | passed from the report |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n |  |
| 2 | Reference | text | n |  |
| 3 | Booking or guest | text (masked) | n |  |
| 4 | Detail | text | n |  |
| 5 | Method | text | n |  |
| 6 | Amount | money | y |  |

- **Grouping, sort, totals:** Sorted by date. Footer total equals the tile.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, CSV. **Phone:** yes.
- **Data source:** Existing prototype `reportDrillCsv`; production shows 'details on the server PC' today. New: one server drill endpoint for every catalogue report.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Booking link only for roles with billing or reservations.

#### rpt.money.closing-report - Closing report (day end)
- **Purpose:** The frozen record of one closed day: revenue, receipts, outlets, deposits, expenses, occupancy, cash, exceptions.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Business date | date | D-DAY (one business date; default today; max today; any earlier date with data, incl. legacy FYs) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Block | text | n | Revenue / Receipts / Restaurant / Deposits / Expenses / Occupancy / Cash / Exceptions |
| 2 | Line | text | n |  |
| 3 | Amount | money | y |  |
| 4 | Counted vs expected | money | y |  |
| 5 | Accepted by | user | n |  |

- **Grouping, sort, totals:** Fixed block order. Earlier replaced records shown (DEC-077). An open day shows a live preview marked 'not closed'.
- **Who may see it:** Owner, Manager; Accountant read-only; not Cashier. **Permission:** PERM-CLS-001. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `closingSummary`, `s.closings`; server `GET /closing/snapshots/{date}`, `/closing/runs/{date}`.
- **Legacy equivalents:** A30, C2.CounterCashierReport, C3.DayEndReportABC
- **Depends on:** nothing outside W7
- **Notes:** Replaces the old Day End Report A and B and the counter cashier sheet. Print only today.

#### rpt.money.day-pack - Day pack (A4)
- **Purpose:** One A4 pack of the day: summary, cash, receipts list, expenses list.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Business date | date | D-DAY (one business date; default today; max today; any earlier date with data, incl. legacy FYs) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Section | text | n | Summary / Cash / Receipts / Expenses |
| 2 | Reference | text | n |  |
| 3 | Booking or guest | text (masked) | n |  |
| 4 | Detail | text | n |  |
| 5 | Method | text | n |  |
| 6 | Amount | money | y |  |

- **Grouping, sort, totals:** Four sections, each with a total.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-CLS-001 (or `close`). **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** PDF (A4), Screen, Excel. **Phone:** yes.
- **Data source:** Existing: `features/exports/DayPack.tsx`; server day-pack exporter is ETP spot 19 (open).
- **Legacy equivalents:** A30, A34 (appendix), C1.DayEndDetail, C2.CounterCashierDetail
- **Depends on:** nothing outside W7

#### rpt.money.closing-records - Earlier closing records
- **Purpose:** List of every closed date with its immutable record and corrections.
- **Status:** built. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Financial year | FY | D-FY (financial year; default current FY; any FY with data, incl. legacy FYs) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Business date | date | n |  |
| 2 | Closed by | user | n |  |
| 3 | Closed at | datetime | n |  |
| 4 | Net revenue | money | y |  |
| 5 | Net receipts | money | y |  |
| 6 | Cash difference | money | y |  |
| 7 | Corrections | int | n |  |

- **Grouping, sort, totals:** One row per date, newest first.
- **Who may see it:** Owner, Manager, Accountant read-only. **Permission:** PERM-CLS-001. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: server `GET /closing/records?from&to`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.money.cash-book - Cash book
- **Purpose:** Range and day view of opening, receipts, payments, closing, counted and over / short.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-S (range; default last 7 days; max 31 days) | Cash book today defaults to the last 14 days |
| Day | date | click | a row opens the day view |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Business date | date | n |  |
| 2 | Status | enum | n | Open / Closed / Locked |
| 3 | Opening | money | y |  |
| 4 | Receipts | money | y |  |
| 5 | Payments | money | y |  |
| 6 | Closing (book) | money | y |  |
| 7 | Counted | money | y |  |
| 8 | Over / short | money | y |  |

- **Grouping, sort, totals:** Range: one row per date. Day view: entries with running balance, closing row, count difference, carried forward, banking and drawings, lock history.
- **Who may see it:** Owner, Manager, Accountant, Cashier. **Permission:** PERM-RPT-002. **Masking:** Cashier sees own shift's entries. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: `cbRange`, `cbDay`; server `GET /cashbook`, `/cashbook/{date}`, `/cashbook/{date}/book`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** No print button today.

#### rpt.money.settles-to - Cash book by account
- **Purpose:** In, out and net per till, bank and credit account.
- **Status:** built. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-S (range; default last 7 days; max 31 days) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Account | text | n |  |
| 2 | Method | text | n |  |
| 3 | In | money | y |  |
| 4 | Out | money | y |  |
| 5 | Net | money | y |  |

- **Grouping, sort, totals:** Cards per account with method lines.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `mnySettlement`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.money.cash-out-register - Banking, transfers and drawings
- **Purpose:** Bank deposits, till transfers and owner drawings with approval status.
- **Status:** built. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Kind | enum | all | bank deposit / till transfer / owner drawing |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n |  |
| 2 | Kind | enum | n |  |
| 3 | Amount | money | y |  |
| 4 | Bank and slip | text | n |  |
| 5 | From till | text | n |  |
| 6 | To till | text | n |  |
| 7 | Reason | text | n |  |
| 8 | Status | enum | n |  |
| 9 | Decided by | user | n |  |

- **Grouping, sort, totals:** Sorted by date. Totals per kind.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002 (slip photo PERM-DRW-003). Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `GET /cashier/transfers`.
- **Legacy equivalents:** A17 (appendix)
- **Depends on:** nothing outside W7
- **Notes:** No export today.

#### rpt.money.shift-report - Shift report (X and Z) and cash hand-over
- **Purpose:** Collections and cash by cashier and shift, with who handed cash to whom.
- **Status:** planned. **Priority:** P1. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) | Cashier sees own shifts. |
| Cashier | user | all |  |
| Shift | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n |  |
| 2 | Shift | text | n |  |
| 3 | Cashier | user | n |  |
| 4 | Opening float | money | y |  |
| 5 | Receipts | money | y |  |
| 6 | Payments | money | y |  |
| 7 | Expected cash | money | y |  |
| 8 | Counted | money | y |  |
| 9 | Difference | money | y |  |
| 10 | Handed over to | user | n |  |
| 11 | Hand-over time | datetime | n |  |

- **Grouping, sort, totals:** By date then shift. Totals per cashier.
- **Who may see it:** Owner, Manager, Accountant; Cashier own shift only. **Permission:** PERM-RPT-002 (Cashier: view and print own shift, no export). **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Shift page `GET /cashier/desk`, `/shifts/{shiftId}` (existing); hand-over log (existing). Report form is new.
- **Legacy equivalents:** A38
- **Depends on:** nothing outside W7
- **Notes:** The old hand-over log was the busiest table (7,000+ rows a year): the hotel uses it daily. ETP spot 24.

#### rpt.money.cash-count-sheet - Cash count sheet
- **Purpose:** Printable denomination count sheet for the shift or day.
- **Status:** planned. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Shift or date | ref | D-DAY (one business date; default today; max today; any earlier date with data, incl. legacy FYs) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Denomination | text | n |  |
| 2 | Notes or coins | int | n |  |
| 3 | Value | money | y |  |
| 4 | Expected | money | y |  |
| 5 | Difference | money | y |  |

- **Grouping, sort, totals:** Rows by denomination. Signature lines.
- **Who may see it:** Owner, Manager, Accountant; Cashier own shift only. **Permission:** PERM-RPT-002 (Cashier: view and print own shift, no export). **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** PDF, Screen. **Phone:** yes.
- **Data source:** New (ETP spot 24).
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.money.collection-detail - Collection detail (receipts register)
- **Purpose:** Every receipt with guest, bill, tender, TDS and commission fields; per-bill payment mode.
- **Status:** planned. **Priority:** P1. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Tender | master | all |  |
| Group by | enum | tender | tender / bill / user |
| Include | enum | receipts | receipts / advances / corrections |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n |  |
| 2 | Receipt no | text | n |  |
| 3 | Guest | text (masked) | n |  |
| 4 | Room | text | n |  |
| 5 | Bill no | text | n |  |
| 6 | Tender | text | n |  |
| 7 | Amount | money | y |  |
| 8 | TDS | money | y |  |
| 9 | Entered by | user | n |  |
| 10 | Source | enum | n | stay / counter bill / credit settlement (LF A7) |

- **Grouping, sort, totals:** Subtotal per tender (or bill, user), grand total. Summary block by tender.
- **Who may see it:** Owner, Manager, Accountant; Cashier own shift only. **Permission:** PERM-RPT-002 (Cashier: view and print own shift, no export). **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** New: receipts (existing) joined to `ctr.bill`, `ctr.bill_line` (LF A7).
- **Legacy equivalents:** A10, C1.BillWisePaymentMode, C1.BillSummaryAccount, C1.BillSummaryAccountDetail, C2.TodaysCustomerDeposit, C2.DepositDetail, C8.CusomerDepositReport
- **Depends on:** LF A7
- **Notes:** A receipt reprint (the old deposit receipt) opens from a row. The demo's 'Entered As / Corrected To' columns belong to rpt.money.payment-corrections.

#### rpt.money.payment-corrections - Payment type corrections
- **Purpose:** Receipts whose payment type was changed after entry.
- **Status:** planned. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n |  |
| 2 | Receipt no | text | n |  |
| 3 | Booking | text | n |  |
| 4 | Guest | text (masked) | n |  |
| 5 | Entered as | text | n |  |
| 6 | Corrected to | text | n |  |
| 7 | Amount | money | y |  |
| 8 | Corrected by | user | n |  |
| 9 | Reason | text | n |  |

- **Grouping, sort, totals:** Sorted by correction time.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** New: audit trail of receipt changes (existing).
- **Legacy equivalents:** C7.WrongPaymentEntry
- **Depends on:** nothing outside W7
- **Notes:** Control report.

#### rpt.money.credit-bills - Credit bills of the day
- **Purpose:** Bills charged to a company or credit guest and not paid on the spot.
- **Status:** planned. **Priority:** P1. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Party | search | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n |  |
| 2 | Bill no | text | n |  |
| 3 | Company or guest | text | n |  |
| 4 | Room | text | n |  |
| 5 | Credit amount | money | y |  |
| 6 | Credit limit | money | y |  |
| 7 | Used after this bill | money | y |  |

- **Grouping, sort, totals:** Sorted by date. Total credit raised.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** New: invoices and credit accounts `gst.guest_credit_account`, company credit (existing); ledger 1120 parties.
- **Legacy equivalents:** C2.TodaysCreditDetail
- **Depends on:** LF B2
- **Notes:** Outstanding and ageing are in rpt.acc.ageing.

#### rpt.money.counter-bills - Counter bills
- **Purpose:** Walk-in bills without a room (food, laundry, other).
- **Status:** planned. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n |  |
| 2 | Bill no | text | n |  |
| 3 | Customer | text | n |  |
| 4 | Item | text | n |  |
| 5 | Qty | int | n |  |
| 6 | Rate | money | y |  |
| 7 | Amount | money | y |  |
| 8 | Tax | money | y |  |
| 9 | Tender | text | n |  |

- **Grouping, sort, totals:** Sorted by date. Subtotal per bill, grand total.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** no.
- **Data source:** New: `ctr.bill`, `ctr.bill_line` (LF A7).
- **Legacy equivalents:** A39, C2.DirectBillSummary, C4.DirectBillDoc
- **Depends on:** LF A7
- **Notes:** The demo read 'direct bill' as company credit; in the old software it was the walk-in bill. HCC uses the walk-in meaning.

#### rpt.money.refunds - Refunds
- **Purpose:** Refunds paid to guests.
- **Status:** planned. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n |  |
| 2 | Refund no | text | n |  |
| 3 | Guest | text (masked) | n |  |
| 4 | Bill no | text | n |  |
| 5 | Amount | money | y |  |
| 6 | Tender | text | n |  |
| 7 | Reason | text | n |  |
| 8 | Approved by | user | n |  |

- **Grouping, sort, totals:** Sorted by date. Total.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** New: refund payments (existing).
- **Legacy equivalents:** A41
- **Depends on:** nothing outside W7
- **Notes:** Rare: 9 in a year in the old software.

#### rpt.money.discount-log - Discounts given
- **Purpose:** Every discount with reason and approver.
- **Status:** planned. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Reason | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n |  |
| 2 | Booking or bill | text | n |  |
| 3 | Guest | text (masked) | n |  |
| 4 | Reason | text | n |  |
| 5 | Amount | money | y |  |
| 6 | Approved by | user | n |  |

- **Grouping, sort, totals:** Sorted by date. Subtotal per reason.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** New: discount lines; summary by reason exists in rpt.money.plans-extras.
- **Legacy equivalents:** C2.OtherDiscount
- **Depends on:** nothing outside W7

#### rpt.money.expense-register - Expense register
- **Purpose:** Expenses with category, funding, status and GST; ledger CSV.
- **Status:** partial. **Priority:** P1. **Money flag:** y (gate PERM-EXP-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Status | enum | all |  |
| Category | master | all |  |
| Funding | enum | all |  |
| Search | text |  |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n |  |
| 2 | Description or reference | text | n |  |
| 3 | Category | text | n |  |
| 4 | Funding | enum | n |  |
| 5 | Amount | money | y |  |
| 6 | GST amount | money | y |  |
| 7 | GST rate | pct | n |  |
| 8 | ITC | enum | n | allowed / disallowed |
| 9 | Status | enum | n |  |
| 10 | Vendor | text | n |  |
| 11 | Created by | user | n |  |

- **Grouping, sort, totals:** Sorted by date. Totals per category and grand total.
- **Who may see it:** Owner, Manager, Accountant; Cashier submits only. **Permission:** PERM-EXP-001. **Masking:** export needs PERM-RPT-003. Money columns need PERM-EXP-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: `expLedgerFilter`; server `GET /expenses`, `/expenses/export/ledger`.
- **Legacy equivalents:** A50, C2.TodaysExpenses
- **Depends on:** nothing outside W7
- **Notes:** CSV today only; no Excel or print (ETP spot 24). Every export is recorded (`EXP_CSV`).

#### rpt.money.expenses-by-head - Expenses by head
- **Purpose:** Expense totals by group and head over a range.
- **Status:** planned. **Priority:** P1. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) |  |
| Group | master | all |  |
| Head | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Group | text | n |  |
| 2 | Head | text | n |  |
| 3 | Entries | int | n |  |
| 4 | Amount | money | y |  |
| 5 | Share | pct | n |  |

- **Grouping, sort, totals:** Group then head, subtotals, grand total.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** summary.
- **Data source:** New: approved expenses (existing).
- **Legacy equivalents:** A50 (summary option)
- **Depends on:** nothing outside W7

#### rpt.money.expense-month - Expenses month and tax feed
- **Purpose:** Approved expenses, GST paid on purchases, ITC allowed and disallowed for a month.
- **Status:** built. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Month | month | D-M (month; default current month; any month with data) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Head | text | n |  |
| 2 | Amount | money | y |  |
| 3 | Entries | int | n |  |
| 4 | GST paid | money | y |  |
| 5 | ITC allowed | money | y |  |
| 6 | ITC disallowed | money | y |  |

- **Grouping, sort, totals:** By head. Lock state of the tax feed shown.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: `GET /expenses/summary`, `/expenses/export/month`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.money.expense-budgets - Expense budgets
- **Purpose:** Budget against actual by category, with ok, near and over status.
- **Status:** partial. **Priority:** P3. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Month | month | D-M (month; default current month; any month with data) | Today only the current month |
| To month | month | same | optional |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Category | text | n |  |
| 2 | Budget | money | y |  |
| 3 | Actual | money | y |  |
| 4 | Used | pct | n |  |
| 5 | Status | enum | n | ok / near 85 % / over |

- **Grouping, sort, totals:** By category. Total.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `expBudgetRows`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.money.udhaar-recurring-petty - Udhaar, recurring expenses and petty cash
- **Purpose:** Credit register by age, due recurring items, petty-cash float.
- **Status:** built. **Priority:** P3. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| As on | date | D-ASOF (one date; default today; max today; any earlier date with data) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Customer or item | text | n |  |
| 2 | Age bucket | enum | n |  |
| 3 | Amount | money | y |  |
| 4 | Due date | date | n |  |
| 5 | Float balance | money | y |  |

- **Grouping, sort, totals:** Three sections.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `expUdOpen`, `expRecStatus`, `expPetty`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Customer receivable ageing moves to rpt.acc.ageing (ACC-3).

#### rpt.money.vendor-ageing - Vendor ageing and payments
- **Purpose:** What is owed to vendors by age; open bills; payments.
- **Status:** built. **Priority:** P2. **Money flag:** y (gate PERM-EXP-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| As on | date | D-ASOF (one date; default today; max today; any earlier date with data) |  |
| Vendor | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Vendor | text | n |  |
| 2 | 0-30 days | money | y |  |
| 3 | 31-60 days | money | y |  |
| 4 | 61-90 days | money | y |  |
| 5 | 90+ days | money | y |  |
| 6 | Open total | money | y |  |
| 7 | Advance | money | y |  |

- **Grouping, sort, totals:** By vendor. Bill and payment lists on drill.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-EXP-004 or PERM-ACC-001. Money columns need PERM-EXP-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: `GET /expenses/vendor-ageing`, `/expenses/vendor-payments`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** ACC-3 ageing (payable side) will supersede
- **Notes:** When rpt.acc.ageing (payable) ships, this entry becomes a link to it.

#### rpt.money.company-credit - Company credit and statement
- **Purpose:** Credit limit, used and remaining per company; statement of one company.
- **Status:** partial. **Priority:** P2. **Money flag:** y (gate PERM-GRP-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| (none) | - | D-NOW (no date: shows the state now; no history) |  |
| Company | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Company | text | n |  |
| 2 | Contact | text | n |  |
| 3 | Negotiated rates | text | n |  |
| 4 | Credit limit | money | y |  |
| 5 | Used | money | y |  |
| 6 | Remaining | money | y |  |
| 7 | Credit status | enum | n |  |

- **Grouping, sort, totals:** One row per company. Statement: lines with running balance.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-GRP-001; money PERM-GRP-004. Money columns need PERM-GRP-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `GET /groups/companies`, `/groups/{id}/statement` (JSON only).
- **Legacy equivalents:** none (HCC only)
- **Depends on:** ACC-3 ageing
- **Notes:** No statement print or CSV today.

#### rpt.money.ota-commission - OTA commission
- **Purpose:** Bookings, room-nights, revenue, commission and net to the hotel by channel.
- **Status:** built. **Priority:** P2. **Money flag:** y (gate PERM-OTA-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) | Prototype has no dates |
| Channel | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Channel | text | n |  |
| 2 | Rate | pct | n |  |
| 3 | Bookings | int | n |  |
| 4 | Room-nights | int | n |  |
| 5 | Room revenue | money | y |  |
| 6 | Commission | money | y |  |
| 7 | Net to hotel | money | y |  |

- **Grouping, sort, totals:** By channel; booking list below. Total row.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-OTA-001; money PERM-OTA-004. Money columns need PERM-OTA-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `chnCommissionReport`; server `GET /ota/commission`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Print only today.

#### rpt.money.ota-payouts - OTA payouts and reconciliation
- **Purpose:** Payout statements against bookings: gross, commission, GST, TCS, net.
- **Status:** built. **Priority:** P2. **Money flag:** y (gate PERM-OTA-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Channel | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Payout | text | n |  |
| 2 | Channel and statement | text | n |  |
| 3 | Gross | money | y |  |
| 4 | Commission | money | y |  |
| 5 | GST on commission | money | y |  |
| 6 | TCS | money | y |  |
| 7 | Net received | money | y |  |
| 8 | Bookings | int | n |  |

- **Grouping, sort, totals:** By payout. Reconciliation differences listed.
- **Who may see it:** Owner, Manager, Accountant (view). **Permission:** PERM-OTA-004. Money columns need PERM-OTA-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** Existing: `GET /ota/payouts`, `/ota/reconciliation`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** No export today.

#### rpt.money.plans-extras - Plans and extras
- **Purpose:** Catalogue against free-text service charges, stays by meal plan, discounts by reason.
- **Status:** built. **Priority:** P3. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) | Default today is the current month. |
| Section | enum | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Section | text | n |  |
| 2 | Item | text | n |  |
| 3 | Bookings | int | n |  |
| 4 | Room-nights | int | n |  |
| 5 | Room revenue | money | y |  |
| 6 | Plan revenue | money | y |  |
| 7 | Count | int | n |  |
| 8 | Amount before GST | money | y |  |

- **Grouping, sort, totals:** Three sections.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: `catChargeSplit`, `catPlanReport`, `catDiscountReport`; server `GET /catalogue/links`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.money.bank-book - Bank book
- **Purpose:** Statement of one bank ledger with running balance.
- **Status:** planned. **Priority:** P2. **Money flag:** y (gate PERM-ACC-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Bank ledger | ledger | required |  |
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n |  |
| 2 | Voucher | text | n |  |
| 3 | Narration | text | n |  |
| 4 | Counter ledger | text | n |  |
| 5 | Debit | money | y |  |
| 6 | Credit | money | y |  |
| 7 | Balance | money | y |  |

- **Grouping, sort, totals:** Chronological. Opening and closing rows.
- **Who may see it:** Owner, Manager, Accountant (not Cashier). **Permission:** PERM-ACC-001. **Masking:** guest names shown. Money columns need PERM-ACC-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** New form over the ledger statement `GET /accounting/statement` (ACC-1).
- **Legacy equivalents:** A17 (appendix)
- **Depends on:** nothing outside W7
- **Notes:** A fixed view of rpt.acc.statement for the bank ledgers.

### 3.4 Tax (12 entries)

#### rpt.tax.gstr1 - GSTR-1 working summary
- **Purpose:** Outward supplies by rate and section, SAC summary (Table 12) and documents issued (Table 13).
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-INV-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Period | month | D-M (month; default current month; any month with data) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Section | enum | n | B2B / B2CL / B2CS / CDNR / CDNUR |
| 2 | Rate | pct | n |  |
| 3 | Taxable value | money | y |  |
| 4 | CGST | money | y |  |
| 5 | SGST | money | y |  |
| 6 | IGST | money | y | always 0 (DEC-136) |
| 7 | Documents | int | n |  |

- **Grouping, sort, totals:** Rate-wise table; then SAC table; then documents-issued table. Totals per table.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-INV-001. **Masking:** customer names shown. Money columns need PERM-INV-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV (CA working file). No portal JSON.. **Phone:** yes.
- **Data source:** Existing: `gstr1Summary`; server `GET /invoices/gstr1?from&to`.
- **Legacy equivalents:** A1
- **Depends on:** LF B7 (legacy series row LGC)
- **Notes:** HCC prepares, the CA files. Export is new (print only today).

#### rpt.tax.sac-summary - SAC summary (Table 12)
- **Purpose:** Taxable value and tax by SAC code and rate.
- **Status:** planned. **Priority:** P1. **Money flag:** y (gate PERM-INV-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-M (month; default current month; any month with data) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | SAC | text | n |  |
| 2 | Description | text | n |  |
| 3 | UQC | text | n | NOS |
| 4 | Total quantity | int | n |  |
| 5 | Total value | money | y |  |
| 6 | Taxable value | money | y |  |
| 7 | CGST | money | y |  |
| 8 | SGST | money | y |  |
| 9 | Rate | pct | n |  |

- **Grouping, sort, totals:** One row per SAC and rate. Total row.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-INV-001. Money columns need PERM-INV-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** no.
- **Data source:** Existing section of `gstr1Summary`; Table-12 export from LF B7.
- **Legacy equivalents:** A2
- **Depends on:** LF B7
- **Notes:** Becomes a stand-alone report with its own Excel. Old report needed Excel installed; HCC does not.

#### rpt.tax.documents-issued - Documents issued (Table 13)
- **Purpose:** Invoice, credit note and receipt series: from, to, total, cancelled.
- **Status:** planned. **Priority:** P1. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Month | month | D-M (month; default current month; any month with data) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Nature of document | text | n |  |
| 2 | Series | text | n | INV / RST / BOS / CRN / LGC |
| 3 | From no | text | n |  |
| 4 | To no | text | n |  |
| 5 | Total | int | n |  |
| 6 | Cancelled | int | n |  |
| 7 | Net issued | int | n |  |

- **Grouping, sort, totals:** One row per series. Cancelled is counted from real cancelled numbers (the old report hard-coded 0).
- **Who may see it:** Owner, Accountant. **Permission:** PERM-INV-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** no.
- **Data source:** Existing section of `gstr1Summary`; legacy series row from LF B7.
- **Legacy equivalents:** A3
- **Depends on:** LF B7

#### rpt.tax.gst-by-bill - GST by bill
- **Purpose:** Each tax invoice with taxable value and tax by rate, for the month's GST working.
- **Status:** planned. **Priority:** P1. **Money flag:** y (gate PERM-INV-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-M (month; default current month; any month with data) |  |
| Level | enum | bill | bill / line |
| Zero-tax bills | enum | include | include / exclude |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Bill no | text | n |  |
| 2 | Bill date | date | n | departure date |
| 3 | Guest or company | text (masked) | n |  |
| 4 | GSTIN | text | n |  |
| 5 | Room | text | n |  |
| 6 | Net amount | money | y |  |
| 7 | Taxable at 0 % | money | y |  |
| 8 | Taxable at 5 % | money | y |  |
| 9 | Taxable at 12 % | money | y |  |
| 10 | Taxable at 18 % | money | y |  |
| 11 | Taxable at 28 % | money | y |  |
| 12 | CGST | money | y |  |
| 13 | SGST | money | y |  |

- **Grouping, sort, totals:** Sorted by bill date. Totals per rate and grand total. Line level lists item, qty, rate, amount, rate, tax.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-INV-001. **Masking:** guest names shown. Money columns need PERM-INV-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** no.
- **Data source:** New over invoices (existing). Reads the stored GST of each bill; never recomputes.
- **Legacy equivalents:** A18, A19, A20
- **Depends on:** nothing outside W7
- **Notes:** The old report ran regularly (3,214 rows in one year). Service-tax and calculate-tax variants are not carried over: use the bill's stored tax.

#### rpt.tax.invoice-register - Invoice register
- **Purpose:** All tax invoices and credit notes in a range, with reprint and bulk print.
- **Status:** planned. **Priority:** P1. **Money flag:** y (gate PERM-INV-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Series | enum | all |  |
| Bill type | enum | all | room / counter / credit note |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Invoice no | text | n |  |
| 2 | Date | date | n |  |
| 3 | Guest or company | text (masked) | n |  |
| 4 | Room | text | n |  |
| 5 | Tariff | money | y |  |
| 6 | Food, laundry, other | money | y |  |
| 7 | Discount | money | y |  |
| 8 | GST | money | y |  |
| 9 | Round-off | money | y |  |
| 10 | Net bill | money | y |  |
| 11 | Cash | money | y |  |
| 12 | Card, UPI, cheque | money | y |  |
| 13 | Credit | money | y |  |
| 14 | Status | enum | n | issued / cancelled / corrected |

- **Grouping, sort, totals:** Sorted by number. Totals. A row opens the invoice PDF; 'Print all' makes one PDF with one page per invoice.
- **Who may see it:** Owner, Manager, Accountant, Reception (reprint). **Permission:** PERM-INV-001. **Masking:** guest names shown. Money columns need PERM-INV-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** Existing: invoices and credit notes (server `GET /invoices`); invoice PDF is ETP spot 9.
- **Legacy equivalents:** A45, C1.BillSummary, C1.BillSummerShort, C4.SalesBill, C4.LodgingBillReceipt
- **Depends on:** nothing outside W7
- **Notes:** The legal invoice itself is a document of the invoice module; layout must match. This is its register.

#### rpt.tax.bill-corrections - Bill corrections
- **Purpose:** Bills corrected after check-out: old and new invoice, amounts, who and why.
- **Status:** planned. **Priority:** P1. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Corrected by | user | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Corrected at | datetime | n |  |
| 2 | Old invoice no | text | n |  |
| 3 | New invoice no | text | n |  |
| 4 | Guest | text (masked) | n |  |
| 5 | Room | text | n |  |
| 6 | Old amount | money | y |  |
| 7 | New amount | money | y |  |
| 8 | Difference | money | y |  |
| 9 | Old GST | money | y |  |
| 10 | New GST | money | y |  |
| 11 | Reason | text | n |  |
| 12 | Corrected by | user | n |  |
| 13 | Approved by | user | n |  |

- **Grouping, sort, totals:** Sorted by time. Totals of differences.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** New: `ivc.bill_correction` and its changes (LF A2).
- **Legacy equivalents:** A4
- **Depends on:** LF A2
- **Notes:** Replaces the old Bill Updation Report and all Bill Finalize reports (see appendix).

#### rpt.tax.gst-month - GST month (set-off working)
- **Purpose:** Output GST, input credit, set-off by head in section 49 order, cash ledger, month lock.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-ACC-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Month | month | D-M (month; default current month; any month with data) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Head | text | n | CGST / SGST / IGST |
| 2 | Output tax | money | y |  |
| 3 | Input credit | money | y |  |
| 4 | Set off against this head | money | y |  |
| 5 | Credit used elsewhere | money | y |  |
| 6 | To pay in cash | money | y |  |

- **Grouping, sort, totals:** By head; blockers and remittances listed below.
- **Who may see it:** Owner, Accountant (enter); view books roles. **Permission:** PERM-ACC-001. Money columns need PERM-ACC-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `accGstMonthView`; server `GET /accounting/gst/month`, `/accounting/gst/locks`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Screen only today.

#### rpt.tax.gst-summary - GST summary (ties to GSTR-1 and GSTR-3B)
- **Purpose:** Output by rate and type, ITC, reverse charge, set-off, cash ledger, with tie checks.
- **Status:** ACC-3. **Priority:** P1. **Money flag:** y (gate PERM-ACC-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Month | month | D-M (month; default current month; any month with data) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Line | text | n | rate 5, rate 18, B2B, B2CS, advances, credit notes, ITC, RCM, set-off |
| 2 | Amount | money | y |  |
| 3 | Ties to GSTR-1 | flag | n |  |
| 4 | Ties to GSTR-3B | flag | n |  |

- **Grouping, sort, totals:** Sections as listed; differences shown as text.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-ACC-001. Money columns need PERM-ACC-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** ACC-3 contract: `GET /accounting/gst/summary?month`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** ACC-3

#### rpt.tax.tds-deductee - TDS deductee report and Form 168
- **Purpose:** TDS deducted and remitted per vendor, rate table, Form 168 reconciliation.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-ACC-013).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Financial year | FY | D-FY (financial year; default current FY; any FY with data, incl. legacy FYs) | or a month |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Vendor | text | n |  |
| 2 | PAN | text (masked) | n |  |
| 3 | Section | text | n |  |
| 4 | Bills | int | n |  |
| 5 | Taxable value | money | y |  |
| 6 | TDS deducted | money | y |  |
| 7 | Remitted | money | y |  |

- **Grouping, sort, totals:** By vendor. Totals.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-ACC-013. Money columns need PERM-ACC-013 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: `tdsDeducteeRows`; server `GET /accounting/tds/deductees`, `/accounting/tds/receivable`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.tax.compliance-register - Compliance register and calendar
- **Purpose:** Obligations by month with due date, form, owner, status and filed date; month print; evidence ZIP; CA pack.
- **Status:** built. **Priority:** P1. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Financial year | FY | D-FY (financial year; default current FY; any FY with data, incl. legacy FYs) |  |
| Month | month | all |  |
| Category | enum | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Due | date | n |  |
| 2 | Obligation | text | n |  |
| 3 | Form or portal | text | n |  |
| 4 | Category | enum | n |  |
| 5 | Frequency | enum | n |  |
| 6 | Responsible | text | n |  |
| 7 | Risk | enum | n |  |
| 8 | Status | enum | n |  |
| 9 | Filed on | date | n |  |
| 10 | Days left | int | n |  |

- **Grouping, sort, totals:** By due date.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-CMP-001. Print and export: rule V.
- **Formats:** Screen, Excel (replaces CSV), PDF, CSV, ZIP (evidence). **Phone:** yes.
- **Data source:** Existing: `GET /compliance/register`, `/compliance/print`, `/compliance/evidence-zip`, `/compliance/ca-packs`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.tax.compliance-dashboard - Compliance dashboard
- **Purpose:** Monthly performance, control summary, ownership load, statutory payable, evidence completeness.
- **Status:** built. **Priority:** P2. **Money flag:** y (gate PERM-CMP-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Financial year | FY | D-FY (financial year; default current FY; any FY with data, incl. legacy FYs) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Measure | text | n |  |
| 2 | Value | int | n |  |
| 3 | Statutory payable | money | y |  |

- **Grouping, sort, totals:** Panels.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-CMP-001. Money columns need PERM-CMP-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `GET /compliance/dashboard`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.tax.licence-expiry - Licence expiry
- **Purpose:** Licences and permits with expiry dates and days left.
- **Status:** planned. **Priority:** P2. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| (none) | - | D-NOW (no date: shows the state now; no history) |  |
| Within days | int | 90 |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Licence | text | n |  |
| 2 | Number | text | n |  |
| 3 | Issued | date | n |  |
| 4 | Expires | date | n |  |
| 5 | Days left | int | n |  |
| 6 | Renewal card | flag | n |  |

- **Grouping, sort, totals:** Sorted by expiry.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-CMP-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Data in `compliance-licences.ts` and firm profile expiry dates (FSSAI, lodging licence).
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** No money column.

### 3.5 Accounting (15 entries)

#### rpt.acc.vouchers - Vouchers (one day)
- **Purpose:** Vouchers of one business date with a balance check; voucher CSV.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-ACC-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Business date | date | D-DAY (one business date; default today; max today; any earlier date with data, incl. legacy FYs) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Voucher no | text | n |  |
| 2 | Type | enum | n |  |
| 3 | Narration | text | n |  |
| 4 | Source id | text | n |  |
| 5 | Amount | money | y |  |
| 6 | Check | enum | n | Balanced / Suspense |

- **Grouping, sort, totals:** By voucher no. Totals: debits, credits, exceptions. Voucher dialog lists ledger, debit, credit.
- **Who may see it:** Owner, Manager, Accountant (not Cashier). **Permission:** PERM-ACC-001. **Masking:** guest names shown. Money columns need PERM-ACC-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV (voucher CSV needs PERM-ACC-005). **Phone:** yes.
- **Data source:** Existing: server `GET /accounting/days/{date}`, `/accounting/days?from&to`, `/accounting/export/csv?from&to`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Voucher CSV columns: Date, Voucher No, Voucher Type, Ledger, Debit, Credit, Narration, Source ID.

#### rpt.acc.trial-balance - Trial balance (six columns)
- **Purpose:** Opening, period and closing debit and credit per ledger.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-ACC-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) | Presets today, last 7 days, this month, FY. |
| View | enum | control ledgers | control ledgers / with parties |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Code | text | n |  |
| 2 | Ledger | text | n |  |
| 3 | Group | text | n |  |
| 4 | Opening Dr | money | y |  |
| 5 | Opening Cr | money | y |  |
| 6 | Period Dr | money | y |  |
| 7 | Period Cr | money | y |  |
| 8 | Closing Dr | money | y |  |
| 9 | Closing Cr | money | y |  |

- **Grouping, sort, totals:** By code. Total row; badge 'debits = credits in every column'; 'opening balances pending' banner; provisional marker for open periods.
- **Who may see it:** Owner, Manager, Accountant (not Cashier). **Permission:** PERM-ACC-001. **Masking:** guest names shown. Money columns need PERM-ACC-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: `accTrialBalance6`; server `GET /accounting/trial-balance?from&to&detail`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** CSV today, desktop only; no Excel or print. A ledger row opens rpt.acc.statement.

#### rpt.acc.statement - Ledger or party statement
- **Purpose:** Statement of one ledger or party with opening, running balance and closing.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-ACC-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Ledger or party | search | required |  |
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n |  |
| 2 | Voucher | text | n |  |
| 3 | Type | enum | n |  |
| 4 | Narration | text | n |  |
| 5 | Counter ledger | text | n |  |
| 6 | Debit | money | y |  |
| 7 | Credit | money | y |  |
| 8 | Balance (Dr / Cr) | money | y |  |

- **Grouping, sort, totals:** Chronological. Opening and closing rows.
- **Who may see it:** Owner, Manager, Accountant (not Cashier). **Permission:** PERM-ACC-001. **Masking:** guest names shown. Money columns need PERM-ACC-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: `accStatement`; server `GET /accounting/statement`.
- **Legacy equivalents:** A27
- **Depends on:** LF B2 (credit guests are parties under 1120)
- **Notes:** Guest ledger = statement of the party. Credit guests join through ledger 1120.

#### rpt.acc.day-book - Day book
- **Purpose:** All vouchers in a range, one line per ledger entry.
- **Status:** built. **Priority:** P2. **Money flag:** y (gate PERM-ACC-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n |  |
| 2 | Voucher | text | n |  |
| 3 | Type | enum | n |  |
| 4 | Ledger | text | n | code and name |
| 5 | Debit | money | y |  |
| 6 | Credit | money | y |  |
| 7 | Narration | text | n |  |
| 8 | Source | text | n |  |

- **Grouping, sort, totals:** By date, voucher. Total row and voucher count.
- **Who may see it:** Owner, Manager, Accountant (not Cashier). **Permission:** PERM-ACC-001. **Masking:** guest names shown. Money columns need PERM-ACC-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: `accDayBook`; server `GET /accounting/day-book`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.acc.chart-parties-opening - Chart, parties and opening balances
- **Purpose:** Chart of accounts, party list with balance, opening balances with approval.
- **Status:** built. **Priority:** P2. **Money flag:** y (gate PERM-ACC-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| View | enum | chart | chart / parties / opening |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Code | text | n |  |
| 2 | Ledger or party | text | n |  |
| 3 | Group | text | n |  |
| 4 | Nature | enum | n |  |
| 5 | Control | flag | n |  |
| 6 | Active | flag | n |  |
| 7 | Balance | money | y |  |
| 8 | Debit | money | y |  |
| 9 | Credit | money | y |  |

- **Grouping, sort, totals:** By code.
- **Who may see it:** Owner, Manager, Accountant (not Cashier). **Permission:** PERM-ACC-001. **Masking:** guest names shown. Money columns need PERM-ACC-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: server `GET /accounting/chart`, `/accounting/chart/master`, `/accounting/chart/history`, `/accounting/opening`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Edit rights stay with ACC-007 / 008 / 009; this is the read form.

#### rpt.acc.exceptions-recon-mapping - Exceptions, reconciliation and mapping
- **Purpose:** Posting exceptions of a day, checks against the reports, mapping of sources to ledgers.
- **Status:** built. **Priority:** P2. **Money flag:** y (gate PERM-ACC-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Business date | date | D-DAY (one business date; default today; max today; any earlier date with data, incl. legacy FYs) |  |
| View | enum | exceptions | exceptions / reconciliation / mapping |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Severity | enum | n |  |
| 2 | Title | text | n |  |
| 3 | Check | text | n |  |
| 4 | Reports value | money | y |  |
| 5 | Ledger value | money | y |  |
| 6 | Result | enum | n | Agrees / Differs |
| 7 | Source | text | n |  |
| 8 | Ledger | text | n |  |
| 9 | Records | int | n |  |
| 10 | Status | enum | n | Unmapped / Changed / Default |

- **Grouping, sort, totals:** By severity or check.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-ACC-001; mapping change PERM-ACC-004. Money columns need PERM-ACC-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: server `GET /accounting/days/{date}`, `/accounting/mapping`, `/accounting/mapping/history`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.acc.manual-register - Manual voucher register
- **Purpose:** Journals and guided entries with approval status.
- **Status:** built. **Priority:** P2. **Money flag:** y (gate PERM-ACC-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Status | enum | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Voucher | text | n |  |
| 2 | Date | date | n |  |
| 3 | Entry | text | n |  |
| 4 | Type | enum | n |  |
| 5 | Narration | text | n |  |
| 6 | Amount | money | y |  |
| 7 | Entered by | user | n |  |
| 8 | Status | enum | n |  |

- **Grouping, sort, totals:** By date.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-ACC-001 (enter 010, approve 011). Money columns need PERM-ACC-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: server `GET /accounting/manual`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.acc.asset-register - Fixed-asset register
- **Purpose:** Assets with cost, ITC, put-to-use, disposal and written-down value.
- **Status:** partial. **Priority:** P2. **Money flag:** y (gate PERM-ACC-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| As on | date | D-ASOF (one date; default today; max today; any earlier date with data) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Asset no | text | n |  |
| 2 | Name | text | n |  |
| 3 | Ledger | text | n |  |
| 4 | Vendor | text | n |  |
| 5 | Bought | date | n |  |
| 6 | Cost | money | y |  |
| 7 | ITC taken | money | y |  |
| 8 | Put to use | date | n |  |
| 9 | Disposal | text | n |  |
| 10 | Depreciation | money | y | ACC-3 |
| 11 | Written-down value | money | y | ACC-3 |

- **Grouping, sort, totals:** By asset no. Totals.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-EXP-004 or PERM-ACC-001. Money columns need PERM-ACC-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: `accAssetsList`; server `GET /accounting/assets`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** ACC-3 carry-overs C1, C2
- **Notes:** Put-to-use, disposal and depreciation come with ACC-3.

#### rpt.acc.lock-history - Period lock history
- **Purpose:** Who locked or unlocked which date or month, when, why.
- **Status:** built. **Priority:** P3. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date or month | text | n |  |
| 2 | Action | enum | n | lock / unlock |
| 3 | By | user | n |  |
| 4 | At | datetime | n |  |
| 5 | Reason | text | n |  |

- **Grouping, sort, totals:** Newest first.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-ACC-001 (lock 002, unlock 003). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: server `GET /accounting/locks/history`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** No money column.

#### rpt.acc.tally-xml - Tally export (masters and vouchers)
- **Purpose:** Tally Import Data XML: masters per FY and vouchers per range.
- **Status:** ACC-3. **Priority:** P1. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Masters FY | FY | D-FY (financial year; default current FY; any FY with data, incl. legacy FYs) |  |
| Vouchers From, To | date range | last 7 days | max 31 days (Tally limit, `TallyRangeError`) |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | File | text | n | masters.xml / vouchers.xml |
| 2 | Vouchers | int | n |  |
| 3 | Ledgers | int | n |  |
| 4 | Notes | text | n | provisional, re-export |

- **Grouping, sort, totals:** Two files; preview of the first 60 lines on screen.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-ACC-006. Print and export: rule V.
- **Formats:** XML download (recorded as ACC_TALLY_EXPORT), Screen preview. **Phone:** yes.
- **Data source:** Existing simulated export: `GET /accounting/export/tally`. ACC-3: `GET /accounting/tally/masters?fy`, `/accounting/tally/vouchers?from&to`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** ACC-3
- **Notes:** The ACC-3 contract writes PERM-ACC-005 for Tally; the correct code is PERM-ACC-006. Here the 31-day cap is a file rule, not a report rule.

#### rpt.acc.pl - Profit and loss
- **Purpose:** Revenue, other income and expense groups to profit before tax; second view by Tally group.
- **Status:** ACC-3. **Priority:** P2. **Money flag:** y (gate PERM-ACC-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) | Provisional if any day in the range is open. |
| Tally-group view | flag | off |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Code | text | n |  |
| 2 | Line | text | n |  |
| 3 | Group | text | n |  |
| 4 | Amount | money | y |  |

- **Grouping, sort, totals:** Revenue, other income, expenses (consumption, employee, finance, depreciation, other), profit before tax. Golden April 2026 profit is 1,40,400 rupees.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-ACC-001. Money columns need PERM-ACC-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** summary.
- **Data source:** ACC-3: `GET /accounting/pl?from&to`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** ACC-3

#### rpt.acc.balance-sheet - Balance sheet
- **Purpose:** Owner's funds, liabilities and assets at a date. Marked 'not the statutory statement'.
- **Status:** ACC-3. **Priority:** P3. **Money flag:** y (gate PERM-ACC-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| As on | date | D-ASOF (one date; default today; max today; any earlier date with data) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Block | text | n |  |
| 2 | Line | text | n |  |
| 3 | Amount | money | y |  |

- **Grouping, sort, totals:** Liabilities and assets side by side; balanced flag; suspense and opening difference.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-ACC-001. Money columns need PERM-ACC-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** summary.
- **Data source:** ACC-3: `GET /accounting/balance-sheet?asOf`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** ACC-3
- **Notes:** Golden 30 April 2026 both sides 79,27,900 rupees.

#### rpt.acc.cash-ratio - Cash ratio
- **Purpose:** Share of receipts and payments made in cash; flagged above 5 %.
- **Status:** ACC-3. **Priority:** P2. **Money flag:** y (gate PERM-ACC-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Financial year | FY | D-FY (financial year; default current FY; any FY with data, incl. legacy FYs) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Measure | text | n |  |
| 2 | Cash | money | y |  |
| 3 | Total | money | y |  |
| 4 | Share | pct | n |  |
| 5 | Flagged | flag | n |  |

- **Grouping, sort, totals:** Two rows: receipts, payments.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-ACC-001. Money columns need PERM-ACC-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** summary.
- **Data source:** ACC-3: `GET /accounting/cash-ratio?fy`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** ACC-3

#### rpt.acc.ageing - Outstanding with ageing
- **Purpose:** Money owed to the hotel or by the hotel, by party and age, bill by bill.
- **Status:** ACC-3. **Priority:** P1. **Money flag:** y (gate PERM-ACC-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| As on | date | D-ASOF (one date; default today; max today; any earlier date with data) |  |
| Side | enum | receivable | receivable / payable |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Party | text | n |  |
| 2 | Control ledger | text | n |  |
| 3 | Bill ref | text | n |  |
| 4 | Bill date | date | n |  |
| 5 | Due | date | n |  |
| 6 | Amount | money | y |  |
| 7 | Paid | money | y |  |
| 8 | Open | money | y |  |
| 9 | Days | int | n |  |
| 10 | 0-30 | money | y |  |
| 11 | 31-60 | money | y |  |
| 12 | 61-90 | money | y |  |
| 13 | 90+ | money | y |  |

- **Grouping, sort, totals:** By party, bills under it (expand). Oldest-first allocation. Totals tie to the party ledger or the row is flagged.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-ACC-001. **Masking:** guest names shown. Money columns need PERM-ACC-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** ACC-3: `GET /accounting/ageing?asOf&side`. Receivable parties under 1100 / 1110 / 1115 / 1120.
- **Legacy equivalents:** A22, A28 (appendix)
- **Depends on:** ACC-3; LF B2
- **Notes:** Covers guest outstanding, company outstanding, credit guests and Udhaar. The old report also showed advance and security deposit columns: those stay in rpt.money.receipts-deposits.

#### rpt.acc.statements-bulk - Bulk party statements (zip)
- **Purpose:** One statement file per party, zipped.
- **Status:** ACC-3. **Priority:** P3. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) |  |
| Parties | enum | all | all / receivable / payable |
| Format | enum | csv | csv / xlsx |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | File | text | n | one per party |
| 2 | Rows | int | n |  |

- **Grouping, sort, totals:** One file per party.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-ACC-001 + PERM-RPT-003. Print and export: rule V.
- **Formats:** ZIP of CSV or Excel. **Phone:** no.
- **Data source:** ACC-3: `GET /accounting/export/statements.zip`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** ACC-3

### 3.6 People (13 entries)

#### rpt.people.payroll-register - Monthly payroll register
- **Purpose:** Per-staff pay for a month: days, earnings, deductions, employer cost, net pay.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-HR-005).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Payroll month | month | last completed | last 6 months and any locked month |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Staff id | text | n |  |
| 2 | Name | text | n |  |
| 3 | Department | text | n |  |
| 4 | Designation | text | n |  |
| 5 | Pay type | enum | n |  |
| 6 | Payable days | int | n |  |
| 7 | Earned | money | y |  |
| 8 | Basic | money | y |  |
| 9 | HRA | money | y |  |
| 10 | Overtime | money | y |  |
| 11 | Gross | money | y |  |
| 12 | PF wages | money | y |  |
| 13 | PF (employee) | money | y |  |
| 14 | ESIC (employee) | money | y |  |
| 15 | PT | money | y |  |
| 16 | Advance recovery | money | y |  |
| 17 | Total deductions | money | y |  |
| 18 | PF (employer) | money | y |  |
| 19 | ESIC (employer) | money | y |  |
| 20 | Net pay | money | y |  |

- **Grouping, sort, totals:** By staff. TOTAL row. Footer 'DRAFT' or 'Locked'.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-HR-006 (run); salary PERM-HR-005. Money columns need PERM-HR-005 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** summary.
- **Data source:** Existing: `hrRegisterCsv`, `registerXlsx`; server `GET /payroll/runs/{month}`, `/register.csv`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Housekeeping, Maintenance and Supervisor never see this (DEC-061). Production gives CSV only today.

#### rpt.people.statutory - Statutory statement (PF, ESIC, PT)
- **Purpose:** PF, ESIC and professional tax per staff and in total, employee and employer.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-HR-005).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Payroll month | month | last completed |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Staff id | text | n |  |
| 2 | Name | text | n |  |
| 3 | Earned | money | y |  |
| 4 | PF wages | money | y |  |
| 5 | PF employee | money | y |  |
| 6 | PF employer | money | y |  |
| 7 | ESIC wages | money | y |  |
| 8 | ESIC employee | money | y |  |
| 9 | ESIC employer | money | y |  |
| 10 | PT | money | y |  |

- **Grouping, sort, totals:** Summary block (component, employee, employer, total), then per staff; F&F members appended.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-HR-006 (run); salary PERM-HR-005. Money columns need PERM-HR-005 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** summary.
- **Data source:** Existing: `hrStatutoryCsv`; server `/payroll/runs/{month}/statutory.csv`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Footer 'DRAFT - NOT FINAL' for unlocked runs.

#### rpt.people.bank-file - Bank transfer file
- **Purpose:** Net pay per staff with account number for the bank.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-HR-005).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Payroll run | ref | latest | issue number |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Staff | text | n |  |
| 2 | Account number | text (masked) | n | full only in the file |
| 3 | Net pay | money | y |  |

- **Grouping, sort, totals:** One row per staff. Each issue recorded; re-issue warns (double-pay guard).
- **Who may see it:** Owner, Accountant. **Permission:** PERM-HR-006 (run); salary PERM-HR-005. Money columns need PERM-HR-005 (rule M). Print and export: rule V.
- **Formats:** CSV (bank file), Screen. **Phone:** no.
- **Data source:** Existing: server `/payroll/run/{runId}/bank-files/{issueNo}`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.people.payslip - Payslip
- **Purpose:** Earnings, deductions and net pay of one staff for one month, with amount in words.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-HR-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Run and staff | ref | required | own payslip always allowed |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Earning or deduction | text | n |  |
| 2 | Amount | money | y |  |

- **Grouping, sort, totals:** Earnings, deductions, net pay.
- **Who may see it:** Owner, Accountant; staff own payslip. **Permission:** PERM-HR-006 or PERM-HR-004 (self). Money columns need PERM-HR-004 (rule M). Print and export: rule V.
- **Formats:** PDF, Screen. **Phone:** yes.
- **Data source:** Existing: `GET /payroll/payslips/{runId}/{staffId}`, `/payroll/my/payslips`. Server PDF is ETP spot 32 (open).
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.people.leave-report - Leave report
- **Purpose:** Leave taken, pending and balance per employee for a month.
- **Status:** partial. **Priority:** P2. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Month | month | D-M (month; default current month; any month with data) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Employee | text | n |  |
| 2 | Full days | int | n |  |
| 3 | Half AM | int | n |  |
| 4 | Half PM | int | n |  |
| 5 | Leave days | int | n |  |
| 6 | Pending | int | n |  |
| 7 | Categories | text | n |  |
| 8 | FY balance | text | n | used / quota |

- **Grouping, sort, totals:** By employee. TOTAL row.
- **Who may see it:** Owner, Manager. **Permission:** PERM-HR-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing prototype `hrLeaveReport`; no server report (new query over leave records).
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** No money column.

#### rpt.people.attendance - Attendance report
- **Purpose:** Day-by-day and month summary of presence, late, half-day, absent, leave.
- **Status:** partial. **Priority:** P2. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-S (range; default last 7 days; max 31 days) | Month view max 31 days. |
| Staff | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Staff | text | n |  |
| 2 | Present | int | n |  |
| 3 | Late | int | n |  |
| 4 | Half-day | int | n |  |
| 5 | Absent | int | n |  |
| 6 | Paid leave | int | n |  |
| 7 | LOP leave | int | n |  |
| 8 | Off | int | n |  |
| 9 | Holiday | int | n |  |
| 10 | Unmarked | int | n |  |
| 11 | Overtime | int | n | minutes |

- **Grouping, sort, totals:** By staff; day grid on request.
- **Who may see it:** Owner, Manager. **Permission:** PERM-HR-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: `hrCounts`; no export. New: server attendance query.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** No money column.

#### rpt.people.roster - Weekly roster
- **Purpose:** Staff against seven days.
- **Status:** built. **Priority:** P3. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Week | date | current week |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Staff | text | n |  |
| 2 | Seven day columns | text | n | shift code |
| 3 | Weekly off | text | n |  |

- **Grouping, sort, totals:** By staff.
- **Who may see it:** Owner, Manager. **Permission:** PERM-HR-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: roster grid in HR desk.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Screen only today. No money column.

#### rpt.people.staff-list - Staff list
- **Purpose:** Staff master with department, shift, weekly off and status.
- **Status:** built. **Priority:** P3. **Money flag:** y (gate PERM-HR-005).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| (none) | - | D-NOW (no date: shows the state now; no history) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Staff | text | n |  |
| 2 | Department | text | n |  |
| 3 | Default shift | text | n |  |
| 4 | Weekly off | text | n |  |
| 5 | Pay | money | y | Owner and Accountant only |
| 6 | Status | enum | n |  |

- **Grouping, sort, totals:** By department.
- **Who may see it:** Owner, Manager; pay column Owner, Accountant. **Permission:** PERM-HR-001; pay PERM-HR-005. Money columns need PERM-HR-005 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: staff master screen.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.people.advances-fnf - Salary advances and full-and-final
- **Purpose:** Advances given and recovered; settlements of leavers.
- **Status:** built. **Priority:** P2. **Money flag:** y (gate PERM-HR-005).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-F (range; default FY start to today; max 366 days; FY history allowed) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Staff | text | n |  |
| 2 | Given | date | n |  |
| 3 | Amount | money | y |  |
| 4 | Monthly recovery | money | y |  |
| 5 | Recovered | money | y |  |
| 6 | Balance | money | y |  |
| 7 | Last day | date | n |  |
| 8 | F&F net | money | y |  |
| 9 | Status | enum | n |  |

- **Grouping, sort, totals:** Two sections.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-HR-006 (run); salary PERM-HR-005. Money columns need PERM-HR-005 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: HR desk advances and F&F screens.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.people.statutory-rates - Statutory rates
- **Purpose:** PF, ESIC, PT and overtime rates by effective date.
- **Status:** built. **Priority:** P3. **Money flag:** y (gate PERM-HR-005).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| (none) | - | D-NOW (no date: shows the state now; no history) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Effective from | date | n |  |
| 2 | PF | pct | n |  |
| 3 | ESIC | pct | n |  |
| 4 | PT men | money | y |  |
| 5 | PT women | money | y |  |
| 6 | Overtime | text | n |  |
| 7 | Rounding | text | n |  |
| 8 | Locked runs | int | n |  |

- **Grouping, sort, totals:** By effective date.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-HR-006 (run); salary PERM-HR-005. Money columns need PERM-HR-005 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: HR rates screen.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.people.leave-balances - Leave balances
- **Purpose:** Entitlement, used and balance per employee per leave type.
- **Status:** planned. **Priority:** P2. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| As on | date | D-ASOF (one date; default today; max today; any earlier date with data) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Employee | text | n |  |
| 2 | Leave type | text | n |  |
| 3 | Entitlement | int | n |  |
| 4 | Used | int | n |  |
| 5 | Pending | int | n |  |
| 6 | Balance | int | n |  |

- **Grouping, sort, totals:** By employee.
- **Who may see it:** Owner, Manager. **Permission:** PERM-HR-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** New: leave records (SCC leave in full).
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** No money column.

#### rpt.people.pf-esic-files - PF ECR and ESIC upload files
- **Purpose:** Files for the PF and ESIC portals. Built only if the owner chooses.
- **Status:** planned. **Priority:** P3. **Money flag:** y (gate PERM-HR-005).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Payroll month | month | last completed |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Staff | text | n |  |
| 2 | Wages | money | y |  |
| 3 | Employee share | money | y |  |
| 4 | Employer share | money | y |  |

- **Grouping, sort, totals:** As the portal layout.
- **Who may see it:** Owner, Accountant. **Permission:** PERM-HR-006 (run); salary PERM-HR-005. Money columns need PERM-HR-005 (rule M). Print and export: rule V.
- **Formats:** Portal file (text), Screen. **Phone:** no.
- **Data source:** New.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** owner choice (Q6)

#### rpt.people.my-hr - My shifts and pay
- **Purpose:** A staff member's own roster, attendance, leave and payslips.
- **Status:** built. **Priority:** P3. **Money flag:** y (gate PERM-HR-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Month | month | D-M (month; default current month; any month with data) | own data only |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Day | date | n |  |
| 2 | Shift | text | n |  |
| 3 | Attendance | enum | n |  |
| 4 | Leave | text | n |  |
| 5 | Payslip net | money | y | own only |

- **Grouping, sort, totals:** By day.
- **Who may see it:** Every staff member, own data only. **Permission:** PERM-HR-004. Money columns need PERM-HR-004 (rule M). Print and export: rule V.
- **Formats:** Screen, PDF (payslip). **Phone:** yes.
- **Data source:** Existing: `features/staff/MyHr.tsx`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** The only money Housekeeping and Maintenance may see is this own pay (DEC-061).

### 3.7 Stock (9 entries)

#### rpt.stock.stock-sheet - Stock by location
- **Purpose:** Items by location with clean quantity against minimum and value at standard cost.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-STK-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| (none) | - | D-NOW (no date: shows the state now; no history) |  |
| Kind | enum | all | linen / amenity / supplies |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Item | text | n |  |
| 2 | Unit | text | n |  |
| 3 | One column per location | int | n |  |
| 4 | Clean | int | n |  |
| 5 | Minimum | int | n |  |
| 6 | Status | enum | n |  |
| 7 | Standard cost | money | y |  |
| 8 | Value | money | y |  |

- **Grouping, sort, totals:** By item. Total value.
- **Who may see it:** Owner, Manager, Supervisor, Housekeeper, Maintenance (quantities). **Permission:** PERM-STK-001; value PERM-STK-004. Money columns need PERM-STK-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `GET /stock/items`, `/stock/alerts`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Value and cost columns removed for roles without PERM-STK-004.

#### rpt.stock.reorder - Reorder alerts
- **Purpose:** Items below minimum with suggested order.
- **Status:** built. **Priority:** P2. **Money flag:** y (gate PERM-STK-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| (none) | - | D-NOW (no date: shows the state now; no history) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Item | text | n |  |
| 2 | Clean | int | n |  |
| 3 | Minimum | int | n |  |
| 4 | Suggested order | int | n |  |
| 5 | Usual supplier | text | n |  |
| 6 | Approx. cost | money | y |  |

- **Grouping, sort, totals:** By urgency.
- **Who may see it:** Owner, Manager, Supervisor, Housekeeper, Maintenance (quantities). **Permission:** PERM-STK-001; value PERM-STK-004. Money columns need PERM-STK-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `invAlerts`; `GET /stock/alerts`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.stock.laundry - Laundry batches
- **Purpose:** Batches sent and received with damaged and missing pieces.
- **Status:** built. **Priority:** P3. **Money flag:** y (gate PERM-STK-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Batch | text | n |  |
| 2 | Sent | date | n |  |
| 3 | Pieces | int | n |  |
| 4 | Clean back | int | n |  |
| 5 | Damaged | int | n |  |
| 6 | Missing | int | n |  |
| 7 | Charge | money | y |  |
| 8 | Status | enum | n |  |

- **Grouping, sort, totals:** By batch.
- **Who may see it:** Owner, Manager, Supervisor, Housekeeper, Maintenance (quantities). **Permission:** PERM-STK-001; value PERM-STK-004. Money columns need PERM-STK-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `GET /stock/laundry`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.stock.counts - Stock counts
- **Purpose:** Counts with variance.
- **Status:** built. **Priority:** P3. **Money flag:** y (gate PERM-STK-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Count | text | n |  |
| 2 | Date | date | n |  |
| 3 | Item | text | n |  |
| 4 | System | int | n |  |
| 5 | Counted | int | n |  |
| 6 | Variance | int | n |  |
| 7 | Variance value | money | y |  |

- **Grouping, sort, totals:** By count.
- **Who may see it:** Owner, Manager, Supervisor, Housekeeper, Maintenance (quantities). **Permission:** PERM-STK-001; value PERM-STK-004. Money columns need PERM-STK-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `invCountVariance`; `GET /stock/counts`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.stock.losses - Losses
- **Purpose:** Linen and stock written off.
- **Status:** built. **Priority:** P3. **Money flag:** y (gate PERM-STK-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n |  |
| 2 | Item | text | n |  |
| 3 | Location | text | n |  |
| 4 | Qty | int | n |  |
| 5 | Reason | text | n |  |
| 6 | Value | money | y |  |
| 7 | By | user | n |  |

- **Grouping, sort, totals:** By date.
- **Who may see it:** Owner, Manager, Supervisor, Housekeeper, Maintenance (quantities). **Permission:** PERM-STK-001; value PERM-STK-004. Money columns need PERM-STK-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `GET /stock/losses`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.stock.consumption - Consumption per occupied room-night
- **Purpose:** Quantity used per item against standard.
- **Status:** built. **Priority:** P2. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-S (range; default last 7 days; max 31 days) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Item | text | n |  |
| 2 | Used | int | n |  |
| 3 | Per room-night | int | n |  |
| 4 | Standard | int | n |  |
| 5 | Variance | int | n |  |
| 6 | Check | flag | n |  |

- **Grouping, sort, totals:** By item. Header shows occupied room-nights.
- **Who may see it:** Owner, Manager, Supervisor. **Permission:** PERM-STK-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `invConsumption`; `GET /stock/consumption`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** No money column.

#### rpt.stock.par - Par and minimums
- **Purpose:** Linen par per room type, sets, required, owned, coverage.
- **Status:** built. **Priority:** P3. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| (none) | - | D-NOW (no date: shows the state now; no history) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Linen | text | n |  |
| 2 | Par per room type | int | n |  |
| 3 | One set | int | n |  |
| 4 | Required | int | n |  |
| 5 | Owned | int | n |  |
| 6 | Coverage | pct | n |  |

- **Grouping, sort, totals:** By linen item.
- **Who may see it:** Owner, Manager, Supervisor. **Permission:** PERM-STK-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `GET /stock/items`, `/stock/settings`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.stock.movements - Stock movements
- **Purpose:** Every receipt, issue, transfer and write-off.
- **Status:** built. **Priority:** P3. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Item | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | When | datetime | n |  |
| 2 | Type | enum | n |  |
| 3 | Item | text | n |  |
| 4 | Qty | int | n |  |
| 5 | From | text | n |  |
| 6 | To | text | n |  |
| 7 | Reference | text | n |  |
| 8 | By | user | n |  |

- **Grouping, sort, totals:** Newest first.
- **Who may see it:** Owner, Manager, Supervisor. **Permission:** PERM-STK-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: `GET /stock/moves`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.stock.grn-register - Goods received (GRN) register
- **Purpose:** Purchase receipts with supplier, bill and value.
- **Status:** planned. **Priority:** P2. **Money flag:** y (gate PERM-STK-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Supplier | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | GRN no | text | n |  |
| 2 | Date | date | n |  |
| 3 | Supplier | text | n |  |
| 4 | Item | text | n |  |
| 5 | Qty | int | n |  |
| 6 | Rate | money | y |  |
| 7 | Amount | money | y |  |
| 8 | Bill no | text | n |  |

- **Grouping, sort, totals:** By GRN. Totals per supplier.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-STK-004. Money columns need PERM-STK-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** no.
- **Data source:** New: stock receipts (existing movements of type receipt).
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

### 3.8 Restaurant (5 entries)

#### rpt.rest.day-summary - Outlet day summary
- **Purpose:** Sales by outlet and settlement, top items, open checks for one day.
- **Status:** built. **Priority:** P1. **Money flag:** y (gate PERM-POS-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Business date | date | D-DAY (one business date; default today; max today; any earlier date with data, incl. legacy FYs) | Today the prototype has no date picker |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Outlet | text | n |  |
| 2 | Bills | int | n |  |
| 3 | Net | money | y |  |
| 4 | Tax | money | y |  |
| 5 | Total | money | y |  |
| 6 | Settlement | text | n |  |
| 7 | Posted to room folios | money | y |  |
| 8 | Top item | text | n |  |
| 9 | Item qty | int | n |  |
| 10 | Item value | money | y |  |
| 11 | Open check | text | n |  |

- **Grouping, sort, totals:** By outlet, by settlement, top items, open checks.
- **Who may see it:** Owner, Manager, Accountant, Cashier own shift (not Restaurant role). **Permission:** PERM-POS-004. Money columns need PERM-POS-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: `posDaySummary`; server `GET /pos/day-summary?date`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** No CSV today.

#### rpt.rest.closed-bills - Closed bills
- **Purpose:** Closed checks of the day with tender; KOT and bill reprint.
- **Status:** built. **Priority:** P2. **Money flag:** y (gate PERM-POS-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| Business date | date | D-DAY (one business date; default today; max today; any earlier date with data, incl. legacy FYs) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Check | text | n |  |
| 2 | Outlet | text | n |  |
| 3 | Table | text | n |  |
| 4 | Closed at | datetime | n |  |
| 5 | Items | int | n |  |
| 6 | Total | money | y |  |
| 7 | Tender | text | n |  |
| 8 | Server | user | n |  |

- **Grouping, sort, totals:** By closing time.
- **Who may see it:** Owner, Manager, Accountant, Cashier own shift (not Restaurant role). **Permission:** PERM-POS-004. Money columns need PERM-POS-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: server `GET /pos/checks/closed`; KOT and bill templates are ETP spot 30 (documents).
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.rest.sales-by-item - Sales by item
- **Purpose:** Quantity and value per menu item.
- **Status:** planned. **Priority:** P2. **Money flag:** y (gate PERM-POS-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Outlet | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Item | text | n |  |
| 2 | Category | text | n |  |
| 3 | Qty | int | n |  |
| 4 | Value | money | y |  |
| 5 | Share | pct | n |  |

- **Grouping, sort, totals:** By category, items by value.
- **Who may see it:** Owner, Manager, Accountant, Cashier own shift (not Restaurant role). **Permission:** PERM-POS-004. Money columns need PERM-POS-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** New: POS check lines (existing).
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.rest.sales-by-outlet-hour - Sales by outlet and hour
- **Purpose:** Sales per outlet and per hour of the day.
- **Status:** planned. **Priority:** P3. **Money flag:** y (gate PERM-POS-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Outlet | master | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Outlet | text | n |  |
| 2 | Hour | int | n |  |
| 3 | Bills | int | n |  |
| 4 | Covers | int | n |  |
| 5 | Value | money | y |  |

- **Grouping, sort, totals:** By outlet then hour.
- **Who may see it:** Owner, Manager, Accountant, Cashier own shift (not Restaurant role). **Permission:** PERM-POS-004. Money columns need PERM-POS-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** New: POS checks.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.rest.voids - POS voids
- **Purpose:** Voided and cancelled POS items and checks.
- **Status:** planned. **Priority:** P2. **Money flag:** y (gate PERM-POS-004).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Time | datetime | n |  |
| 2 | Check | text | n |  |
| 3 | Item | text | n |  |
| 4 | Qty | int | n |  |
| 5 | Value | money | y |  |
| 6 | Reason | text | n |  |
| 7 | Voided by | user | n |  |
| 8 | Approved by | user | n |  |

- **Grouping, sort, totals:** By time. Total.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-POS-004. Money columns need PERM-POS-004 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** New: POS audit rows.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Control report.

### 3.9 Admin and audit (11 entries)

#### rpt.admin.audit-log - Audit log
- **Purpose:** What happened, who did it, when, with before and after.
- **Status:** built. **Priority:** P1. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Actor | user | all |  |
| Action | master | all |  |
| Contains | text |  |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Time | datetime | n |  |
| 2 | Actor | text | n |  |
| 3 | Action | text | n |  |
| 4 | Detail (before and after) | text | n | hidden for roles without audit detail |
| 5 | Id | text | n |  |

- **Grouping, sort, totals:** Newest first. Server paging.
- **Who may see it:** Owner, Manager, Accountant; Tech admin view-only. **Permission:** PERM-ADM-004. Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: server `GET /admin/audit`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** No export today. No money column; details with money are hidden by the detail rule.

#### rpt.admin.export-log - Export and print log
- **Purpose:** Who exported or printed which report, when, with which parameters.
- **Status:** planned. **Priority:** P2. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |
| Report | master | all |  |
| User | user | all |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Time | datetime | n |  |
| 2 | User | text | n |  |
| 3 | Report | text | n |  |
| 4 | Parameters | text | n |  |
| 5 | Format | enum | n |  |
| 6 | Rows | int | n |  |

- **Grouping, sort, totals:** Newest first.
- **Who may see it:** Owner, Manager. **Permission:** PERM-ADM-004. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** New: renderer log.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Proves the permission rule: every export and print is recorded.

#### rpt.admin.scheduled-runs - Scheduled runs
- **Purpose:** Scheduled report runs, delivery result and failures.
- **Status:** planned. **Priority:** P2. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Schedule | text | n |  |
| 2 | Report | text | n |  |
| 3 | Run at | datetime | n |  |
| 4 | Recipients | text | n |  |
| 5 | Result | enum | n |  |
| 6 | Error | text | n |  |

- **Grouping, sort, totals:** Newest first.
- **Who may see it:** Owner, Manager. **Permission:** PERM-ADM-004. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** New: scheduler tables.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.admin.system-health - System health
- **Purpose:** Parked events, last backups, restore-drill status.
- **Status:** built. **Priority:** P2. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| (none) | - | D-NOW (no date: shows the state now; no history) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Item | text | n |  |
| 2 | Status | enum | n |  |
| 3 | Last run | datetime | n |  |
| 4 | Detail | text | n |  |

- **Grouping, sort, totals:** Sections.
- **Who may see it:** Owner, Tech admin. **Permission:** PERM-SYS-001. Print and export: rule V.
- **Formats:** Screen, PDF. **Phone:** yes.
- **Data source:** Existing: server `GET /admin/system-health`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Screen only today.

#### rpt.admin.backup-status - Backup status
- **Purpose:** Last backup and recovery state.
- **Status:** built. **Priority:** P3. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| (none) | - | D-NOW (no date: shows the state now; no history) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Backup | text | n |  |
| 2 | When | datetime | n |  |
| 3 | Size | int | n |  |
| 4 | Result | enum | n |  |

- **Grouping, sort, totals:** Newest first.
- **Who may see it:** Owner, Manager. **Permission:** PERM-SYS-001 or dashboard card. Print and export: rule V.
- **Formats:** Screen. **Phone:** yes.
- **Data source:** Existing: `BackupStatus.tsx`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.admin.legacy-import - Legacy import runs and reconciliation
- **Purpose:** Runs of the old-data import with per-FY match result.
- **Status:** built. **Priority:** P3. **Money flag:** y (gate PERM-LGC-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| (none) | - | D-NOW (no date: shows the state now; no history) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Run | text | n |  |
| 2 | When | datetime | n |  |
| 3 | Mode | enum | n |  |
| 4 | FY | text | n |  |
| 5 | Stays | int | n |  |
| 6 | Rooms | int | n |  |
| 7 | Bills | int | n |  |
| 8 | Receipts | money | y |  |
| 9 | Net | money | y |  |
| 10 | Result | enum | n | MATCH / DIFFERS |

- **Grouping, sort, totals:** By run, then by FY.
- **Who may see it:** Owner. **Permission:** PERM-LGC-001. Money columns need PERM-LGC-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: server `GET /legacy/runs`.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** Legacy import (LG)

#### rpt.admin.config-history - Configuration and masters history
- **Purpose:** Changes to settings and masters, with requests.
- **Status:** built. **Priority:** P2. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | When | datetime | n |  |
| 2 | Who | text | n |  |
| 3 | Area | text | n |  |
| 4 | Before | text | n |  |
| 5 | After | text | n |  |
| 6 | Reason | text | n |  |
| 7 | Request status | enum | n |  |

- **Grouping, sort, totals:** Newest first.
- **Who may see it:** Owner, Manager, Tech admin. **Permission:** PERM-CFG-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: server `GET /configuration/history`, `/configuration/requests`, `/configuration/export`; masters CSV.
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7

#### rpt.admin.rate-sheet - Rate sheet, publish log and change history
- **Purpose:** Room type by plan rates, what was published, who changed what.
- **Status:** partial. **Priority:** P2. **Money flag:** y (gate PERM-YLD-001).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| View | enum | rate sheet | rate sheet / publish log / change history |
| Night | date | D-FWD (range; default today to +7 days; max 92 days; may look 90 days ahead) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Room type | text | n |  |
| 2 | Plan | text | n |  |
| 3 | Rate | money | y |  |
| 4 | Night | date | n |  |
| 5 | Type | text | n |  |
| 6 | Before | money | y |  |
| 7 | After | money | y |  |
| 8 | When | datetime | n |  |
| 9 | Who | text | n |  |
| 10 | Reason | text | n |  |

- **Grouping, sort, totals:** By room type.
- **Who may see it:** Owner, Manager. **Permission:** PERM-YLD-001. Money columns need PERM-YLD-001 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** Existing: server `GET /yield/calendar`, `/yield/config`, `/yield/publishes`.
- **Legacy equivalents:** C7.RoomTariffCard
- **Depends on:** nothing outside W7
- **Notes:** Printed tariff card = the PDF of the rate sheet. No export today.
- **F4-T1 (2 Nov 2026, F4-CONTRACT §1.7):** extended with the tariff card — A4 letterhead print of a card version (page `tariff-card` › Print; `features/reports/rate-sheet.tsx`), "Prices include GST @ <rate> %" from the published room lines, "Valid from" line, T&C footer from settings (blank-safe), English only; CSV of the card and of the version history; print = screen (same selector `trcRateSheet`); permission PERM-TRF-001 (Owner, Manager, Accountant, Reception, Cashier); PDF hook `renderPdf?.('rate-sheet', model)` for F7. Server data: `GET /api/v1/tariff-card`.

#### rpt.admin.report-dictionary - Report dictionary
- **Purpose:** Definitions of every metric and how zero is handled.
- **Status:** built. **Priority:** P2. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| (none) | - | D-NOW (no date: shows the state now; no history) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Metric | text | n |  |
| 2 | Definition | text | n |  |
| 3 | Date basis | text | n |  |
| 4 | Zero handling | text | n |  |

- **Grouping, sort, totals:** Static table; extended to cover every catalogue figure.
- **Who may see it:** Everybody who can open any report. **Permission:** PERM-RPT-001. Print and export: rule V.
- **Formats:** Screen, PDF, CSV. **Phone:** yes.
- **Data source:** Existing: Reports dictionary (12 metrics, Financial Rulebook section 6, REQ-RPT-006).
- **Legacy equivalents:** none (HCC only)
- **Depends on:** nothing outside W7
- **Notes:** Grow from 12 to all catalogue metrics.

#### rpt.admin.cancelled-lines - Cancelled folio lines
- **Purpose:** Items and charges removed from guest bills.
- **Status:** planned. **Priority:** P2. **Money flag:** y (gate PERM-RPT-002).
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | Date | date | n |  |
| 2 | Booking | text | n |  |
| 3 | Room | text | n |  |
| 4 | Item | text | n |  |
| 5 | Qty | int | n |  |
| 6 | Amount | money | y |  |
| 7 | Cancelled by | user | n |  |
| 8 | Reason | text | n |  |

- **Grouping, sort, totals:** Newest first. Total.
- **Who may see it:** Owner, Manager, Accountant. **Permission:** PERM-RPT-002. **Masking:** guest names shown. Money columns need PERM-RPT-002 (rule M). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** New: folio reversals (audit trail).
- **Legacy equivalents:** C1.CancelItem
- **Depends on:** nothing outside W7
- **Notes:** Control report.

#### rpt.admin.desk-reminders - Desk reminders and wake-up calls
- **Purpose:** Reminders and wake-up calls set and their outcome.
- **Status:** planned. **Priority:** P3. **Money flag:** n.
- **Parameters:**

| Name | Type | Default / class | Limits |
|---|---|---|---|
| From, To | date range | D-B (range; default month start to today; max 92 days) |  |

- **Columns in order:**

| # | Column | Type | Money | Notes |
|---|---|---|---|---|
| 1 | When | datetime | n |  |
| 2 | Type | enum | n | wake-up / reminder |
| 3 | Room or guest | text | n |  |
| 4 | Note | text | n |  |
| 5 | Set by | user | n |  |
| 6 | Done | flag | n |  |

- **Grouping, sort, totals:** By time.
- **Who may see it:** Owner, Manager, Reception. **Permission:** PERM-RPT-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** New: LF B3 / B4 alert data.
- **Legacy equivalents:** C7.AlertsGuestInfo
- **Depends on:** LF B3, B4
- **Notes:** No money column.

### 3.10 Measurement (MEAS, wave F12) (54 entries)

Added 5 Nov 2026 by `claude/meas` (plan `02 Planning/HCC_Measurement_Reports_Plan_MEAS_2026-11-05.md`, contract `02 Planning/agents/MEAS-CONTRACT.md`). Two new areas: `revenue` (Revenue management, 6 entries) and `study` (Analytics and study, 22 entries). Status of every entry is `built (MEAS)`; the integrator moves it to `built` from code. Every entry is switchable in Master configuration > Measurement reports (`measOn`, contract section 1) and returns `MEAS_OFF` / is hidden everywhere when off. Column lists below fix the column keys, order and money flags; a lane may add a column but never removes one or changes a money flag without a DONE note.

#### rpt.guests.manager-flash - Manager's flash
- **Purpose:** Manager's flash: Any KPI more than 10 % below its target (meas.target.*) is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P1. **Money flag:** y. **Switch:** `meas.daily.flash (core)`. **Lane:** meas-rpt-daily.
- **Parameters:** Date (default yesterday); Compare with (budget / same day last year). Date class D-DAY (one business date; default yesterday for the flash, else today).
- **Columns in order:** Occupancy %; ADR; RevPAR; Arrivals; Departures; In-house; No-shows; Comps; OOO; Rooms on books +7 / +14 / +30; each KPI with target, last-year and variance. (Money columns: flagged y one by one in the lane; removed, not blanked, for roles without the money gate (rule M); Housekeeping, Maintenance and Stock-only roles never see them, DEC-061.)
- **Exception rule (red row):** Any KPI more than 10 % below its target (meas.target.*).
- **Who may see it:** PERM-RPT-002 (Supervisor: ops variant without money). Print and export: rule V.
- **Formats:** Screen, PDF. **Phone:** yes.
- **Data source:** reportPack, yldOcc, lgc history, closing record. **Capture needed:** none.
- **Depends on:** F7 report engine.

#### rpt.guests.regrets-denials - Regrets and denials
- **Purpose:** Regrets and denials: More than 3 price regrets on a night under 60 % occupancy is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P1. **Money flag:** n. **Switch:** `meas.daily.regrets`. **Lane:** meas-rpt-daily.
- **Parameters:** Date range; Reason; Channel; Room type. Date class D-B (range; default month start to today; max 92 days).
- **Columns in order:** Date; Reason; Channel; Lead days; Room type; Nights lost; Rooms lost; Quoted rate (hidden without money gate); Converted later. (Money columns: none.)
- **Exception rule (red row):** More than 3 price regrets on a night under 60 % occupancy.
- **Who may see it:** PERM-RPT-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes (capture).
- **Data source:** res.turnaway log. **Capture needed:** res.turnaway (meas-cap-desk).
- **Depends on:** F7 report engine; res.turnaway (meas-cap-desk).

#### rpt.guests.rate-variance - Rate variance / rate check
- **Purpose:** Rate variance / rate check: No reason, or variance above the 15 % agreed-rate threshold (AF2 CTL-01) is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P1. **Money flag:** y. **Switch:** `meas.daily.rate-variance`. **Lane:** meas-rpt-daily.
- **Parameters:** Date range; Room type; User. Date class D-B (range; default month start to today; max 92 days).
- **Columns in order:** Booking; Guest (masked per 1.6); Room; Card rate; Charged rate; Difference; Difference %; Reason code; User; Approver. (Money columns: flagged y one by one in the lane; removed, not blanked, for roles without the money gate (rule M); Housekeeping, Maintenance and Stock-only roles never see them, DEC-061.)
- **Exception rule (red row):** No reason, or variance above the 15 % agreed-rate threshold (AF2 CTL-01).
- **Who may see it:** PERM-RPT-002. Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** stay.rate_change, tariff_card_line, YLDOverride. **Capture needed:** none.
- **Depends on:** F7 report engine.

#### rpt.rooms.room-discrepancy - Room discrepancy
- **Purpose:** Room discrepancy: Any mismatch older than 2 hours is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P1. **Money flag:** n. **Switch:** `meas.daily.discrepancy`. **Lane:** meas-rpt-daily.
- **Parameters:** As-of time (default now). Date class D-NOW (no date: state now).
- **Columns in order:** Room; Front-office status; Housekeeping status; Mismatch kind (sold-but-vacant, vacant-but-occupied, OOO-with-stay, sleep-out); Since; Age hours. (Money columns: none.)
- **Exception rule (red row):** Any mismatch older than 2 hours.
- **Who may see it:** PERM-HK-001. Print and export: rule V.
- **Formats:** Screen, PDF. **Phone:** yes.
- **Data source:** rooms, bookings, hkTasks, roomoffline. **Capture needed:** none.
- **Depends on:** F7 report engine.

#### rpt.money.high-balance - High-balance and credit-limit
- **Purpose:** High-balance and credit-limit: Balance above deposit plus limit; walk-in without company above Rs 5,000 or any balance older than 2 nights is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P1. **Money flag:** y. **Switch:** `meas.daily.high-balance (core when credit is on)`. **Lane:** meas-rpt-daily.
- **Parameters:** Threshold (default meas.target.highBalance). Date class D-NOW (no date: state now).
- **Columns in order:** Room; Guest (masked); Company; Days in house; Balance; Deposit; Limit; Over by. (Money columns: flagged y one by one in the lane; removed, not blanked, for roles without the money gate (rule M); Housekeeping, Maintenance and Stock-only roles never see them, DEC-061.)
- **Exception rule (red row):** Balance above deposit plus limit; walk-in without company above Rs 5,000 or any balance older than 2 nights.
- **Who may see it:** PERM-RPT-002. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** folio lines, receipts, company credit limits. **Capture needed:** setting meas.target.highBalance.
- **Depends on:** F7 report engine; setting meas.target.highBalance.

#### rpt.money.allowances-voids - Allowances, rebates, voids and corrections
- **Purpose:** Allowances, rebates, voids and corrections: Any entry without a reason or an approver; a user above the daily cap is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P1. **Money flag:** y. **Switch:** `meas.daily.adjustments`. **Lane:** meas-rpt-daily.
- **Parameters:** Date range; Kind; User. Date class D-B (range; default month start to today; max 92 days).
- **Columns in order:** Date; Kind (discount, allowance, rebate, void, correction, refund); Bill; Amount; Reason code; User; Approver. (Money columns: flagged y one by one in the lane; removed, not blanked, for roles without the money gate (rule M); Housekeeping, Maintenance and Stock-only roles never see them, DEC-061.)
- **Exception rule (red row):** Any entry without a reason or an approver; a user above the daily cap.
- **Who may see it:** PERM-RPT-002. Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** no.
- **Data source:** discount-log, cancelled-lines, bill-corrections, refunds. **Capture needed:** allowance reason codes (meas-cap-desk).
- **Depends on:** F7 report engine; allowance reason codes (meas-cap-desk).

#### rpt.guests.no-show-cancel - No-show, cancellation and early departure
- **Purpose:** No-show, cancellation and early departure: Retention waived without an approver; no-show rate above target is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.daily.no-show`. **Lane:** meas-rpt-daily.
- **Parameters:** Date range; Channel; Kind. Date class D-B (range; default month start to today; max 92 days).
- **Columns in order:** Booking; Channel; Lead days; Kind; Retention charge applied; Charge waived; Waived by; Approver. (Money columns: flagged y one by one in the lane; removed, not blanked, for roles without the money gate (rule M); Housekeeping, Maintenance and Stock-only roles never see them, DEC-061.)
- **Exception rule (red row):** Retention waived without an approver; no-show rate above target.
- **Who may see it:** PERM-RPT-002. Print and export: rule V.
- **Formats:** Screen, Excel, PDF, CSV. **Phone:** yes.
- **Data source:** bookings, folio. **Capture needed:** none (needs F8 qw-noshow guards or this wave stubs them).
- **Depends on:** F7 report engine; none (needs F8 qw-noshow guards or this wave stubs them).

#### rpt.rooms.inventory-exceptions - Complimentary, house-use and out-of-order
- **Purpose:** Complimentary, house-use and out-of-order: Complimentary without approver; OOO more than 7 days without a work order is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P1. **Money flag:** n. **Switch:** `meas.daily.inventory (core)`. **Lane:** meas-rpt-daily.
- **Parameters:** Date range (default yesterday and today). Date class D-S (range; default last 7 days; max 31 days).
- **Columns in order:** Room; Kind (complimentary, house use, OOO, OOS); From; To / expected return; Beneficiary; Approver; Reason; Work order. (Money columns: none.)
- **Exception rule (red row):** Complimentary without approver; OOO more than 7 days without a work order.
- **Who may see it:** PERM-RPT-001. Print and export: rule V.
- **Formats:** Screen, PDF. **Phone:** yes.
- **Data source:** stays (complimentary type DEC-266), roomoffline, mnt work orders. **Capture needed:** roomoffline reason + expected return (meas-cap-hk).
- **Depends on:** F7 report engine; roomoffline reason + expected return (meas-cap-hk).

#### rpt.money.cashier-variance - Cashier and shift variance
- **Purpose:** Cashier and shift variance: Variance above Rs 100, or three variances in a month by one cashier is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P1. **Money flag:** y. **Switch:** `meas.daily.cashier`. **Lane:** meas-rpt-daily.
- **Parameters:** Date range; Cashier. Date class D-B (range; default month start to today; max 92 days).
- **Columns in order:** Date; Shift; Cashier; Expected cash; Counted cash; Paid-outs; Float; Variance; Month-to-date variance. (Money columns: flagged y one by one in the lane; removed, not blanked, for roles without the money gate (rule M); Housekeeping, Maintenance and Stock-only roles never see them, DEC-061.)
- **Exception rule (red row):** Variance above Rs 100, or three variances in a month by one cashier.
- **Who may see it:** PERM-RPT-002 (Cashier: own shift only, rule V.S). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** closing shifts, cash book. **Capture needed:** none.
- **Depends on:** F7 report engine.

#### rpt.revenue.demand-calendar - Demand calendar
- **Purpose:** Demand calendar: Forecast 85 % or more with BAR unchanged; forecast 25 % or less with no offer open is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P1. **Money flag:** y. **Switch:** `meas.rev.demand-calendar`. **Lane:** meas-rpt-revenue.
- **Parameters:** From (default today); Nights (default 90, max 92). Date class D-FWD (range; default today to +90 days (revenue) ; max 92 days).
- **Columns in order:** Date; Weekday; On the books; Forecast; Same night last year; Event; BAR; Suggested rate; Offer open. (Money columns: flagged y one by one in the lane; removed, not blanked, for roles without the money gate (rule M); Housekeeping, Maintenance and Stock-only roles never see them, DEC-061.)
- **Exception rule (red row):** Forecast 85 % or more with BAR unchanged; forecast 25 % or less with no offer open.
- **Who may see it:** PERM-RPT-001 (rate column behind PERM-RPT-002). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** yldOcc, lgc stays history, tariffs, yld.event. **Capture needed:** yld.event master (meas-forecast).
- **Depends on:** F7 report engine; yld.event master (meas-forecast).

#### rpt.revenue.pickup-pace - Pickup and pace
- **Purpose:** Pickup and pace: Pace worse than minus 10 % against last year is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.rev.pickup`. **Lane:** meas-rpt-revenue.
- **Parameters:** Months ahead (1-12); Channel. Date class D-NOW (no date: state now).
- **Columns in order:** Month; Rooms on books; Last year same snapshot; Revenue on books; Last year revenue; Pace %; Pickup last 7 days; Lead-time bucket. (Money columns: flagged y one by one in the lane; removed, not blanked, for roles without the money gate (rule M); Housekeeping, Maintenance and Stock-only roles never see them, DEC-061.)
- **Exception rule (red row):** Pace worse than minus 10 % against last year.
- **Who may see it:** PERM-RPT-002. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** bookings createdAt / cancelledAt, yld.otb_snapshot. **Capture needed:** weekly yld.otb_snapshot job (meas-forecast).
- **Depends on:** F7 report engine; weekly yld.otb_snapshot job (meas-forecast).

#### rpt.revenue.displacement - Displacement calculator
- **Purpose:** Displacement calculator: Net loss; offered rate below the counter-rate is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.rev.displacement`. **Lane:** meas-forecast (calculator) + meas-rpt-revenue (report shell).
- **Parameters:** Enquiry (group, long stay or hall); From; To; Rooms; Offered rate. Date class D-FWD (range; default today to +90 days (revenue) ; max 92 days).
- **Columns in order:** Night; Forecast occupancy; Rooms displaced; Transient revenue displaced; Offer revenue; Net gain or loss; Recommendation (accept / counter-rate / decline). (Money columns: flagged y one by one in the lane; removed, not blanked, for roles without the money gate (rule M); Housekeeping, Maintenance and Stock-only roles never see them, DEC-061.)
- **Exception rule (red row):** Net loss; offered rate below the counter-rate.
- **Who may see it:** PERM-RPT-002. Print and export: rule V.
- **Formats:** Screen, PDF. **Phone:** yes.
- **Data source:** yield forecast, groups, HB quotations. **Capture needed:** none.
- **Depends on:** F7 report engine.

#### rpt.revenue.overbooking-wash - Overbooking and wash
- **Purpose:** Overbooking and wash: Rooms oversold above the allowed number is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y (corrected at integration: cost columns behind the money gate, DEC-061). **Switch:** `meas.rev.overbooking`. **Lane:** meas-rpt-revenue.
- **Parameters:** Date range; Room type; Channel. Date class D-F (range; default FY start to today; max 366 days).
- **Columns in order:** Weekday; Channel; No-show %; Cancellation %; Expected wash rooms; Oversold allowed (yld.overbook); Walked guests; Cost of walk. (Money columns: none.)
- **Exception rule (red row):** Rooms oversold above the allowed number.
- **Who may see it:** PERM-RPT-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** bookings history, res.turnaway, walked-guest record. **Capture needed:** walked-guest record + yld.overbook (meas-cap-desk / meas-forecast).
- **Depends on:** F7 report engine; walked-guest record + yld.overbook (meas-cap-desk / meas-forecast).

#### rpt.revenue.restriction-effect - Length-of-stay and minimum-stay controls
- **Purpose:** Length-of-stay and minimum-stay controls: Restriction on a night that stayed under 40 % occupancy is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.rev.restrictions`. **Lane:** meas-rpt-revenue.
- **Parameters:** Date range; Restriction kind. Date class D-B (range; default month start to today; max 92 days).
- **Columns in order:** Night; Restriction (MIN_STAY / CTA); Bookings refused; Bookings re-shaped; Revenue on the night; Same nights unrestricted; Enforced or advisory. (Money columns: flagged y one by one in the lane; removed, not blanked, for roles without the money gate (rule M); Housekeeping, Maintenance and Stock-only roles never see them, DEC-061.)
- **Exception rule (red row):** Restriction on a night that stayed under 40 % occupancy.
- **Who may see it:** PERM-RPT-002. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** YLDRestriction, YLDViolation counter. **Capture needed:** restriction counters + yld.restrictionsEnforce (meas-forecast).
- **Depends on:** F7 report engine; restriction counters + yld.restrictionsEnforce (meas-forecast).

#### rpt.revenue.rate-parity - Rate shop and parity
- **Purpose:** Rate shop and parity: OTA rate below direct; our rate more than 15 % above the set median with occupancy under 40 % is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.rev.rate-shop`. **Lane:** meas-rpt-revenue.
- **Parameters:** From; Nights (max 92); Room type. Date class D-FWD (range; default today to +90 days (revenue) ; max 92 days).
- **Columns in order:** Stay date; Direct rate; OTA rate; Competitor 1..6 rate; Set median; Our position; Parity exception. (Money columns: flagged y one by one in the lane; removed, not blanked, for roles without the money gate (rule M); Housekeeping, Maintenance and Stock-only roles never see them, DEC-061.)
- **Exception rule (red row):** OTA rate below direct; our rate more than 15 % above the set median with occupancy under 40 %.
- **Who may see it:** PERM-RPT-002. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes (entry).
- **Data source:** channels publish log, yld.rateshop. **Capture needed:** yld.rateshop entry form (meas-forecast).
- **Depends on:** F7 report engine; yld.rateshop entry form (meas-forecast).

#### rpt.guests.satisfaction - Guest satisfaction and NPS
- **Purpose:** Guest satisfaction and NPS: NPS below target (default 30); any score of 2 or lower unanswered for 48 hours is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P1. **Money flag:** n. **Switch:** `meas.guest.survey`. **Lane:** meas-rpt-guest.
- **Parameters:** Month; Room type; Channel. Date class D-M (month; default current month; any month with data).
- **Columns in order:** Month; Responses; Response rate; Overall; Cleanliness; Staff; Value; Breakfast; NPS; Verbatim (no guest identity for Housekeeping). (Money columns: none.)
- **Exception rule (red row):** NPS below target (default 30); any score of 2 or lower unanswered for 48 hours.
- **Who may see it:** PERM-RPT-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes (form).
- **Data source:** gsv.survey. **Capture needed:** gsv.survey (meas-cap-guest).
- **Depends on:** F7 report engine; gsv.survey (meas-cap-guest).

#### rpt.guests.reputation-index - Online reputation
- **Purpose:** Online reputation: Score drop of 0.3 or more in a month; response rate under 80 % is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** n. **Switch:** `meas.guest.reputation`. **Lane:** meas-rpt-guest.
- **Parameters:** Financial year; Platform. Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs).
- **Columns in order:** Month; Platform; Reviews received; Average score; 12-month score; Response rate; Themes. (Money columns: none.)
- **Exception rule (red row):** Score drop of 0.3 or more in a month; response rate under 80 %.
- **Who may see it:** PERM-RPT-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** gsv.review_month. **Capture needed:** gsv.review_month (meas-cap-guest).
- **Depends on:** F7 report engine; gsv.review_month (meas-cap-guest).

#### rpt.guests.service-recovery - Service recovery
- **Purpose:** Service recovery: Repeat cause (same room, same category, 90 days); compensation without approval is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.guest.recovery`. **Lane:** meas-rpt-guest.
- **Parameters:** Date range; Category; Room. Date class D-B (range; default month start to today; max 92 days).
- **Columns in order:** Category; Room; Complaints; Hours to resolve; Within SLA %; Root cause; Compensation; Approver; Repeat in 90 days. (Money columns: flagged y one by one in the lane; removed, not blanked, for roles without the money gate (rule M); Housekeeping, Maintenance and Stock-only roles never see them, DEC-061.)
- **Exception rule (red row):** Repeat cause (same room, same category, 90 days); compensation without approval.
- **Who may see it:** PERM-RPT-001 (compensation column behind GSV-004). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** GSVComplaint, GSVRecovery. **Capture needed:** rootCause code list (meas-cap-guest).
- **Depends on:** F7 report engine; rootCause code list (meas-cap-guest).

#### rpt.guests.desk-timing - Arrival and departure experience
- **Purpose:** Arrival and departure experience: Median above 8 minutes at check-in; above 6 minutes at check-out is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** n. **Switch:** `meas.guest.desk-timing`. **Lane:** meas-rpt-guest.
- **Parameters:** Date range; Desk user. Date class D-B (range; default month start to today; max 92 days).
- **Columns in order:** Hour; Desk user; Arrivals; Median minutes arrival-to-key; Median minutes bill-to-checkout; Queue peak. (Money columns: none.)
- **Exception rule (red row):** Median above 8 minutes at check-in; above 6 minutes at check-out.
- **Who may see it:** PERM-RPT-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** audit log, KEYS log (DEC-212), folio settle. **Capture needed:** none.
- **Depends on:** F7 report engine.

#### rpt.guests.upsell - Upsell and upgrade
- **Purpose:** Upsell and upgrade: Free upgrade without an approver; upsell revenue per arrival below target is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.guest.upsell`. **Lane:** meas-rpt-guest.
- **Parameters:** Date range; Desk user. Date class D-B (range; default month start to today; max 92 days).
- **Columns in order:** Arrivals; Upgrades paid; Upgrades free; Approver; Late check-out charged; Day-use sold; Breakfast add-on; Extra bed; Revenue per arrival. (Money columns: flagged y one by one in the lane; removed, not blanked, for roles without the money gate (rule M); Housekeeping, Maintenance and Stock-only roles never see them, DEC-061.)
- **Exception rule (red row):** Free upgrade without an approver; upsell revenue per arrival below target.
- **Who may see it:** PERM-RPT-002. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** folio lines (UPG, LCO), stay segments, rate plans. **Capture needed:** UPG / LCO items, booked type kept (meas-cap-desk).
- **Depends on:** F7 report engine; UPG / LCO items, booked type kept (meas-cap-desk).

#### rpt.guests.repeat-loyalty - Repeat guests and loyalty base
- **Purpose:** Repeat guests and loyalty base: Repeat share falls two months running is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.guest.repeat`. **Lane:** meas-rpt-guest.
- **Parameters:** Month. Date class D-M (month; default current month; any month with data).
- **Columns in order:** Month; Customers seen before; Repeat share; Stays; Revenue share; 3+ stay guests (name, last stay, mobile: rule V); Loyalty tag; Offer sent. (Money columns: flagged y one by one in the lane; removed, not blanked, for roles without the money gate (rule M); Housekeeping, Maintenance and Stock-only roles never see them, DEC-061.)
- **Exception rule (red row):** Repeat share falls two months running.
- **Who may see it:** PERM-CRM (revenue column behind PERM-RPT-002). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** customers, stays, messaging outbox, lgc history. **Capture needed:** cust.loyaltyTag (meas-cap-guest).
- **Depends on:** F7 report engine; cust.loyaltyTag (meas-cap-guest).

#### rpt.rooms.hk-productivity - Attendant productivity and turnaround
- **Purpose:** Attendant productivity and turnaround: Pass rate under 95 %; median departure room over 45 minutes; a same-day re-let not inspected before arrival is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P1. **Money flag:** n. **Switch:** `meas.hk.productivity`. **Lane:** meas-rpt-hk.
- **Parameters:** Date range; Attendant; Shift. Date class D-S (range; default last 7 days; max 31 days).
- **Columns in order:** Attendant; Shift; Departure rooms; Stayover rooms; Minutes per room; Vacated-to-inspected minutes; Inspection pass %; Rejections by reason; Same-day re-let compliance. (Money columns: none.)
- **Exception rule (red row):** Pass rate under 95 %; median departure room over 45 minutes; a same-day re-let not inspected before arrival.
- **Who may see it:** PERM-HK-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** hk tasks (startedAt, finishedAt, inspectedAt), bookings. **Capture needed:** task timestamps + inspection result (meas-cap-hk); extends rpt.rooms.hk-turnaround.
- **Depends on:** F7 report engine; task timestamps + inspection result (meas-cap-hk); extends rpt.rooms.hk-turnaround.

#### rpt.rooms.ooo-days - Out-of-order and lost inventory
- **Purpose:** Out-of-order and lost inventory: OOO more than 7 days without a work order; idle 14+ nights is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y (corrected at integration: cost columns behind the money gate, DEC-061). **Switch:** `meas.hk.ooo`. **Lane:** meas-rpt-hk.
- **Parameters:** Date range; Reason. Date class D-B (range; default month start to today; max 92 days).
- **Columns in order:** Room; Reason; Room-days; Work order; Days without ticket; Lost revenue at ADR; Idle 14+ nights while sellable. (Money columns: none.)
- **Exception rule (red row):** OOO more than 7 days without a work order; idle 14+ nights.
- **Who may see it:** PERM-HK-001 (lost-revenue column behind PERM-RPT-002). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** roomoffline, mnt work orders, stays. **Capture needed:** roomoffline reason list + expected return + work-order link (meas-cap-hk).
- **Depends on:** F7 report engine; roomoffline reason list + expected return + work-order link (meas-cap-hk).

#### rpt.rooms.ppm-compliance - Preventive maintenance compliance
- **Purpose:** Preventive maintenance compliance: Preventive task overdue more than 7 days; P1 MTTR above 24 hours; third failure of one asset in 90 days is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** n. **Switch:** `meas.hk.ppm`. **Lane:** meas-rpt-hk.
- **Parameters:** Date range; Asset kind. Date class D-F (range; default FY start to today; max 366 days).
- **Columns in order:** Asset kind; Plans due; Done on time; Overdue; Preventive-to-corrective ratio; MTTR by priority; Repeat failures in 90 days; AMC visits due. (Money columns: none.)
- **Exception rule (red row):** Preventive task overdue more than 7 days; P1 MTTR above 24 hours; third failure of one asset in 90 days.
- **Who may see it:** PERM-MNT-001 (cost behind MNT-004). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** MNTPlan, MNTWorkOrder, MNTAmc. **Capture needed:** none.
- **Depends on:** F7 report engine.

#### rpt.rooms.utilities-per-room - Energy, water and LPG per occupied room-night
- **Purpose:** Energy, water and LPG per occupied room-night: More than 15 % above the 3-month average per occupied night is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y (corrected at integration: cost columns behind the money gate, DEC-061). **Switch:** `meas.hk.utilities`. **Lane:** meas-rpt-hk.
- **Parameters:** Month; Meter. Date class D-M (month; default current month; any month with data).
- **Columns in order:** Day; Electricity kWh; Water kL; LPG kg; Diesel litres; DG hours; Per occupied room-night; Per available room; Unit cost; Cost per occupied night; Last year same month. (Money columns: none.)
- **Exception rule (red row):** More than 15 % above the 3-month average per occupied night.
- **Who may see it:** PERM-MNT-001 (cost behind MNT-004). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes (reading).
- **Data source:** mnt.meter_reading, utility expenses, occupancy. **Capture needed:** mnt.meter + mnt.meter_reading (meas-cap-hk).
- **Depends on:** F7 report engine; mnt.meter + mnt.meter_reading (meas-cap-hk).

#### rpt.stock.linen-cost - Linen, laundry and guest-supply cost per occupied room
- **Purpose:** Linen, laundry and guest-supply cost per occupied room: Loss above 2 % of par in a month; cost per occupied night above target is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y (corrected at integration: cost columns behind the money gate, DEC-061). **Switch:** `meas.hk.linen`. **Lane:** meas-rpt-hk.
- **Parameters:** Month; Item group. Date class D-M (month; default current month; any month with data).
- **Columns in order:** Item; Par; On hand; Laundry batches; Lost; Damaged; Discarded (reason); Amenities issued; Cost per occupied room-night. (Money columns: none.)
- **Exception rule (red row):** Loss above 2 % of par in a month; cost per occupied night above target.
- **Who may see it:** STK-001 (cost behind STK-004). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** inventory.ts, laundry batches. **Capture needed:** discard reason (meas-cap-hk); extends rpt.stock.consumption.
- **Depends on:** F7 report engine; discard reason (meas-cap-hk); extends rpt.stock.consumption.

#### rpt.acc.asset-life - Asset register, FF&E reserve and renovation cycle
- **Purpose:** Asset register, FF&E reserve and renovation cycle: Asset past expected life with no plan; reserve spent above accrued is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.hk.assets`. **Lane:** meas-rpt-hk.
- **Parameters:** As-of date; Asset kind. Date class D-ASOF (one date; default today).
- **Columns in order:** Asset; Age; Expected life; Condition grade; Replacement due; WDV; Replacement cost; FF&E reserve accrued; Capex spent; Refurbishment due (category). (Money columns: flagged y one by one in the lane; removed, not blanked, for roles without the money gate (rule M); Housekeeping, Maintenance and Stock-only roles never see them, DEC-061.)
- **Exception rule (red row):** Asset past expected life with no plan; reserve spent above accrued.
- **Who may see it:** PERM-ACC-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** accounting-assets.ts, MNTAsset, P&L revenue. **Capture needed:** asset life fields + ffe.reservePct (meas-cap-hk / meas-settings).
- **Depends on:** F7 report engine; asset life fields + ffe.reservePct (meas-cap-hk / meas-settings).

#### rpt.people.staff-productivity - Staff per occupied room, overtime, absenteeism
- **Purpose:** Staff per occupied room, overtime, absenteeism: OT above 10 %; absenteeism above 5 %; rostered hours above forecast need by 20 % is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** n. **Switch:** `meas.people.productivity`. **Lane:** meas-rpt-people.
- **Parameters:** Month; Department. Date class D-M (month; default current month; any month with data).
- **Columns in order:** Department; Headcount; Staff-hours per occupied room-night; OT %; Absenteeism %; Late marks; Rostered hours on low-forecast nights. (Money columns: none.)
- **Exception rule (red row):** OT above 10 %; absenteeism above 5 %; rostered hours above forecast need by 20 %.
- **Who may see it:** HR-001 (payroll column behind HR-005). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** HRAtt, roster, forecast. **Capture needed:** none.
- **Depends on:** F7 report engine.

#### rpt.people.attrition - Attrition and tenure
- **Purpose:** Attrition and tenure: Attrition above 30 % a year in any department is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** n. **Switch:** `meas.people.attrition`. **Lane:** meas-rpt-people.
- **Parameters:** Financial year; Department. Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs).
- **Columns in order:** Department; Joiners; Leavers; Exit reasons; Tenure at exit; 12-month attrition %; Vacancy days. (Money columns: none.)
- **Exception rule (red row):** Attrition above 30 % a year in any department.
- **Who may see it:** HR-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** HRStaff. **Capture needed:** staff.exitReason list (meas-cap-people).
- **Depends on:** F7 report engine; staff.exitReason list (meas-cap-people).

#### rpt.people.agent-scorecard - Performance by desk user
- **Purpose:** Performance by desk user: A user outside the team interquartile range on discounts or variances is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.people.scorecard`. **Lane:** meas-rpt-people.
- **Parameters:** Month; Desk user. Date class D-M (month; default current month; any month with data).
- **Columns in order:** User; Check-ins; Check-outs; Rate edits; Discounts (count, Rs, % with reason); Comps requested; Upsell per arrival; Complaints attributed; Cash variances; Regrets logged; Survey score. (Money columns: flagged y one by one in the lane; removed, not blanked, for roles without the money gate (rule M); Housekeeping, Maintenance and Stock-only roles never see them, DEC-061.)
- **Exception rule (red row):** A user outside the team interquartile range on discounts or variances.
- **Who may see it:** PERM-RPT-002 (Owner and Manager only). Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** no.
- **Data source:** audit log, folio, closing, gsv, survey. **Capture needed:** none.
- **Depends on:** F7 report engine.

#### rpt.people.training - Training hours and certifications
- **Purpose:** Training hours and certifications: A mandatory certificate (fire safety, first aid, FSSAI handler, POSH) expired or missing is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** n. **Switch:** `meas.people.training`. **Lane:** meas-rpt-people.
- **Parameters:** As-of date; Department. Date class D-ASOF (one date; default today).
- **Columns in order:** Staff; Course; Date; Hours; Certificate no; Valid to; Days to expiry; Hours per head this quarter. (Money columns: none.)
- **Exception rule (red row):** A mandatory certificate (fire safety, first aid, FSSAI handler, POSH) expired or missing.
- **Who may see it:** HR-001. Print and export: rule V.
- **Formats:** Screen, Excel, PDF. **Phone:** yes.
- **Data source:** hr.training, TRAINING.replay completion. **Capture needed:** hr.training (meas-cap-people).
- **Depends on:** F7 report engine; hr.training (meas-cap-people).

#### rpt.people.staff-pulse - Staff pulse (optional)
- **Purpose:** Staff pulse (optional): eNPS below 0 is the red-row rule (see Exception rule).
- **Status:** built (MEAS). **Priority:** P3. **Money flag:** n. **Switch:** `meas.people.pulse`. **Lane:** meas-rpt-people.
- **Parameters:** Financial year; Quarter. Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs).
- **Columns in order:** Quarter; Department; Responses; Workload; Respect; Tools; Pay fairness; Would recommend; eNPS. (Money columns: none.)
- **Exception rule (red row):** eNPS below 0.
- **Who may see it:** HR-001 (Owner, Manager). Print and export: rule V.
- **Formats:** Screen, PDF. **Phone:** yes.
- **Data source:** hr.pulse (anonymous, department only). **Capture needed:** hr.pulse (meas-cap-people).
- **Depends on:** F7 report engine; hr.pulse (meas-cap-people).

#### rpt.study.revenue-monthly - Monthly revenue and cash (FY history)
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.study.revenue-monthly`. **Lane:** meas-study-a.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Monthly Revenue: Monthly, FY Summary, FY x Month grid.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Extends rpt.guests.history-summary; heads, GST, discount, cash received vs billed; heat map.

#### rpt.study.year-comparison - Year comparison
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.study.year-comparison`. **Lane:** meas-study-a.
- **Parameters:** Date class D-F (range; default FY start to today; max 366 days); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Year Comparison: Summary, Month x Year grids, YoY growth, Like-for-like Apr-Oct (date-exact window), Cumulative YTD, CAGR, Revenue head mix.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Like-for-like window = any from / to; cumulative lines per FY.

#### rpt.study.index-vs-base - Index vs a base year
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.study.covid-index`. **Lane:** meas-study-a.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Year Comparison: COVID impact and recovery, Best and worst months.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Base FY is a parameter (default 2018-19); recovery statistics.

#### rpt.study.fy-deep-dive - FY deep dive (price-volume, segments, top bills)
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.study.deep-dive`. **Lane:** meas-study-a.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Year Comparison: FY 2022-23 deep dive A-G.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Any FY against its neighbours; top-N bills under rule V.

#### rpt.study.occupancy-history - Occupancy history (three denominators)
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** n. **Switch:** `meas.study.occupancy`. **Lane:** meas-study-a.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Occupancy: Monthly, FY summary, FY x Month grid, Weekday vs weekend, Seasonality, Room-type mix.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Supersedes the P2 adr-trend for history; rooms in service from the room master first-sale / in-service dates.

#### rpt.study.operations-patterns - Operations patterns
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** n. **Switch:** `meas.study.patterns`. **Lane:** meas-study-a.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Operations Patterns: CheckIn_Hour, CheckOut_Hour, Arrivals_DOW, Departures_DOW, Turnover_EarlyLate, LengthOfStay, GroupSize, Purpose, BookingSource, AdvanceLead, Status.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** With the HCC reservation log the lead-time proxy becomes the real booking window (2.3).

#### rpt.study.rooms-and-rates - Rooms, rates and discounts
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.study.rooms-rates`. **Lane:** meas-study-a.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Rooms & Rates: Room Nights, Room Revenue, Room Avg Rate, Room Share, Room Occupancy, Top Bottom Rooms, Low Sales, Room Type FY, Tariff Trend, Rate Overrides, Discount Control, Payment Mix, Unpaid Ageing.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Room x FY heat maps; rate-override reasons become the HCC reason codes.

#### rpt.study.zero-rate-complimentary - Zero-rate and complimentary stays
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.study.zero-rate`. **Lane:** meas-study-a.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Zero-rate: all 12 sheets.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Live rows use the DEC-266 complimentary type; legacy rows keep note-parsed events; revenue-given-up estimates A / B / C.

#### rpt.study.receivables-bad-debt - Receivables and bad debt (legacy + live ageing)
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.study.receivables`. **Lane:** meas-study-a.
- **Parameters:** Date class D-ASOF (one date; default today); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Receivables: Summary, Bad Debt Schedule, Ageing by FY, Top 100 Dues, Top Debtors, Debtor Contacts, Company vs Walk-in, Follow-up List, Monthly Unpaid, Cutover Comparison.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Wires to rpt.acc.ageing for live dues; legacy block flagged bad debt per DEC-265; names and mobiles under rule V (Owner, Accountant).

#### rpt.study.revenue-kpi - Occupancy, ADR, RevPAR by month, FY, type, channel
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.study.rm-kpi`. **Lane:** meas-study-b.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Revenue Management: KPI by Month, KPI by FY, KPI by Room Type, KPI by Channel.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Unsold room-nights and their value at ADR.

#### rpt.study.channel-economics - Channel economics and net ADR
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.study.rm-channel`. **Lane:** meas-study-b.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Revenue Management: Net ADR after Commission; Sales & Marketing: Direct Share by Month, OTA Dependence.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Commission % from the channel master (replaces the 15 % scenario).

#### rpt.study.pricing-shape - Day-of-week, length-of-stay, segment mix, rate ladder, upgrades
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.study.rm-pricing`. **Lane:** meas-study-b.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Revenue Management: DOW & LOS Pricing, Segment Mix, Rate Ladder, Upgrades & Shifts, Booking Window.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Rack from the tariff card (replaces the modal-rate proxy).

#### rpt.study.guest-repeat-value - Repeat guests, frequency, lifetime value, repeat customer list
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.study.guest-repeat`. **Lane:** meas-study-b.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Guest: Repeat Guests by FY, Guest Frequency & Value, Repeat Customer List.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Customer = HCC customer id (phone key for legacy rows); overlaps 3.7 for the monthly view; list Owner and Manager (M-11).

#### rpt.study.guest-origin-plan - Guest origin, nationality, meal plan, company guests
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** n. **Switch:** `meas.study.guest-origin`. **Lane:** meas-study-b.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Guest: Guest Origin, Nationality & Form C, Meal Plan Mix, Company Guests.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** City / state from structured fields in HCC; token parse only for legacy rows.

#### rpt.study.housekeeping-operations - Turnaround, idle streaks, check-in/out vs standard, daily load
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** n. **Switch:** `meas.study.hk-ops`. **Lane:** meas-study-b.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Operations & Housekeeping: Turnaround, Idle Streaks, Check-in-out vs Standard, Daily Load.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Live rows use the task timestamps (4.1) and room-status log (4.2); standard times from settings.

#### rpt.study.fnb-halls-ancillary - F&B on bills, breakfast covers, hall utilisation, ancillary
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.study.fnb-halls`. **Lane:** meas-study-b.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: F&B, Halls & Ancillary: all analysis sheets.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Live hall rows from the HB module (F6); covers from POS once outlet sales exist.

#### rpt.study.finance-cost - USALI P&L, break-even, paid-outs, cash & bank, GST by slab
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.study.finance`. **Lane:** meas-study-b.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Finance & Cost: USALI P&L, Break-even, Paid-outs, Cash & Bank, GST Reconciliation.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Cost lines from ACC expenses by department once posted; break-even inputs = settings; GST slabs from the dated GST master.

#### rpt.study.corporate-accounts - Corporate accounts, win-back list, account base
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.study.sales`. **Lane:** meas-study-b.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Sales & Marketing: Corporate Accounts (incl. win-back).
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Company master names; contact = the company contact, then the first guest; Owner and Manager (M-11).

#### rpt.study.compliance-risk - Capture rates, audit-trail counts, risk register, licence calendar
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** n. **Switch:** `meas.study.compliance`. **Lane:** meas-study-b.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Compliance & Risk: Capture Rates, Audit Trail, Risk Register, Licence Calendar.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Risk register editable (Owner); audit counts from the HCC audit log for live rows; licence cards from the compliance module.

#### rpt.study.forecast-scenarios - Seasonality indices, forecast (three methods), scenarios, budget vs actual, comp-set index
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P2. **Money flag:** y. **Switch:** `meas.study.forecast`. **Lane:** meas-study-b.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Forecast & Benchmark: Seasonality Indices, Forecast, Scenarios, Budget vs Actual, Comp Set Index.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Inputs editable on screen (scenario rows, comp-set rows, budget); uses the 2.2 curve when on.

#### rpt.study.owner-dashboard - Owner 12-KPI monthly dashboard
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P1. **Money flag:** y. **Switch:** `meas.study.dashboard`. **Lane:** meas-study-b.
- **Parameters:** Date class D-M (month; default current month; any month with data); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: Forecast & Benchmark: Owner Dashboard.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** yes.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** The monthly page the plan section 10 names; phone yes.

#### rpt.study.management-pack - Management study pack (PDF + Excel)
- **Purpose:** Ports the workbook sheets of `E:\Projects\HCC\Existing Software\Reports` to HCC, running on legacy plus live data as one series (plan section 7A).
- **Status:** built (MEAS). **Priority:** P1. **Money flag:** y. **Switch:** `meas.study.management-pack`. **Lane:** meas-study-pack.
- **Parameters:** Date class D-FY (financial year; default current FY; any FY with data incl. legacy FYs); report-specific parameters in the lane. Legacy FYs 2017-18 to 2026-27 allowed.
- **Columns in order:** one table block per workbook sheet: The whole HCC_Management_Study_Report.pdf: cover, contents, executive summary with KPI tiles and scorecard, sections 1-18 from 6.1-6.21, appendices A (report index) and C (alphabetical index with page numbers), bookmarks.
- **Exception rule (red row):** none unless the workbook sheet defines a flag; the golden totals (plan 7A.1 rule 4) are the acceptance rule.
- **Who may see it:** PERM-RPT-002 (money-free variants PERM-RPT-001; names and mobiles rule V). Legacy rows carry the `legacy` marker (rule 1.10).
- **Formats:** Screen, Excel (native OpenXML charts), PDF (ChartBlock). **Phone:** no.
- **Data source:** analytics selectors `domain/analytics/*.ts` (prototype) and `Hcc.Reporting/Analytics/*` (server) over stays, bills, receipts, lgc history, rooms master.
- **Legacy equivalents:** the workbook named in the Notes.
- **Notes:** Scheduled monthly (meas.study.packSchedule); one Excel with one sheet per entry; ChartBlock; footer "Legacy to 30 Oct 2026 / HCC live".


## 4. Not carried over

Legacy reports with used = no, and every dormant file of Part B. One line each. The Bill Finalize and Bill Updation reports are replaced by `rpt.tax.bill-corrections` (LF A2). The HR, laundry and store reports are covered by HCC's own modules.

### 4.1 Part A menu reports (used = no)

| Id | Reason |
|---|---|
| A9 | Restaurant Payment: restaurant tables empty in every year; HCC has rpt.rest.day-summary. |
| A15 | Guest Receive/Return: laundry module unused, menu item hidden; HCC stock module covers linen. |
| A16 | Guest Laundry Balance: laundry module unused, menu item hidden, wrongly wired in the old code. |
| A17 | Owner/Bank Ledger: owner receipt and payment tables empty; replaced by rpt.money.bank-book and rpt.money.cash-out-register. |
| A21 | Calculate Tax: pre-GST recalculation, output empty; HCC reads the bill's stored GST. |
| A23 | Room-wise Laundry Stock: store module unused, unreachable; covered by HCC stock module. |
| A24 | Laundry Stock Detail: store tables empty; covered by rpt.stock.stock-sheet. |
| A25 | Lodging Material Stock: store tables empty; covered by rpt.stock.stock-sheet. |
| A28 | Bill-wise Outstanding: no run for two years; bill-wise detail is inside rpt.acc.ageing. |
| A29 | Occupancy Room: menu item hidden, unreachable; replaced by rpt.rooms.room-status. |
| A32 | Tanker Detail: water-tanker tracking never used. |
| A34 | Day End Detail: orphan menu handler, prints nothing; covered by rpt.money.day-pack. |
| A37 | Advance Booking Detail: advance-booking tables empty for two years; HCC has rpt.guests.reservations-register. |
| A46 | Finalize Sales Bill: finalized copy tables empty; replaced by rpt.tax.bill-corrections (LF A2). |
| A47 | Restaurant Purchase: unused; HCC expenses and stock modules cover purchases. |
| A48 | Restaurant Supplier Outstanding: unused; covered by vendor ageing (rpt.money.vendor-ageing, rpt.acc.ageing payable). |
| A49 | Laundry Supplier Outstanding: unused; covered by vendor ageing. |
| A51 | Salary Sheet: HR module unused in the old software; covered by rpt.people.payroll-register. |
| A52 | Employee Ledger: HR unused; covered by rpt.people.advances-fnf and payslips. |
| A53 | Employee Attendance: attendance switched off in the old software; covered by rpt.people.attendance. |
| A54 | Employee Salary Advance: HR unused; covered by rpt.people.advances-fnf. |
| A57 | Luxury Tax (finalized): finalize tables empty; replaced by rpt.tax.bill-corrections. |
| A58 | Service Tax (finalized): finalize tables empty; replaced by rpt.tax.bill-corrections. |
| A59 | Finalize Bill Summary: finalize tables empty; replaced by rpt.tax.bill-corrections. |
| A60 | Finalize Bill Detail Summary: finalize tables empty; replaced by rpt.tax.bill-corrections. |
| A61 | Day End Summary: orphan menu handler, prints nothing; covered by rpt.money.closing-report. |

### 4.2 Part B dormant .rpt files (57)

| # | File | Reason |
|---|---|---|
| B1 | AdvanceBookingDetail by Nikita.rpt | Author copy of Advance Booking Detail. |
| B2 | AdvanceBookingDetailReport.rpt | Older layout of Advance Booking Detail, only in commented code. |
| B3 | BillSummaryAccount-OLD.rpt | Old dated copy of Bill Summary Account. |
| B4 | BillSummaryAccountReport.rpt | Unopened variant of Bill Summary Account. |
| B5 | BillUpdationDifference new.rpt | Newer copy of Bill Updation layout; replaced by rpt.tax.bill-corrections. |
| B6 | CanteenCoupenDetail.rpt | Canteen coupons: dharamshala feature, never opened. |
| B7 | CheckInReport Naivedya.rpt | Author copy of the check-in registration card. |
| B8 | CompanyTariffDetailData2Report.rpt | Data2 (previous-year database) variant; HCC has one database. |
| B9 | CounterCashierDetailReport-New.rpt | Newer layout never used; the hotel ran the older one. |
| B10 | CounterCashierReportShort by nikita.rpt | Author copy of the short cash sheet. |
| B11 | CusomerDepositReport31-12-22.rpt | Dated copy of the deposit receipt. |
| B12 | CustomerDetail by nikita.rpt | Author copy of the guest ledger. |
| B13 | CustomerDetail-28-01-26.rpt | Dated copy of the guest ledger. |
| B14 | CustomerOutstanding by nikita.rpt | Author copy of Guest Outstanding. |
| B15 | CustomerTariffDetailData2Report.rpt | Data2 variant; HCC has one database. |
| B16 | DayEndReportB By mangesh.rpt | Author copy of Day End Report B. |
| B17 | DayEndReportInDetail-OLD.rpt | Old dated copy of Day End In Detail. |
| B18 | DepositDetailReportOLD.rpt | Old copy of Deposit Detail. |
| B19 | DirectBillDetailReportOLD.rpt | Old copy of Direct Bill Detail. |
| B20 | FoodBillSubReport.rpt | Sub-report file never opened. |
| B21 | FoodLaundryDetailData2Report.rpt | Data2 variant. |
| B22 | FunctionProspectusSaleBillReport By Nikita.rpt | Author copy of a hall document; hall is a new module. |
| B23 | GSTR1Report - Copy.rpt | Duplicate of GSTR-1; covered by rpt.tax.gstr1. |
| B24 | GSTR1StandardReport - Copy.rpt | Duplicate of GSTR-1 standard; covered by rpt.tax.gstr1. |
| B25 | GuestRegisterData2Report.rpt | Data2 variant of Guest Register. |
| B26 | HSNDetail.rpt | Copied from another product; reads a different database; no caller. |
| B27 | LaundryDetailReport.rpt | Never opened; laundry unused. |
| B28 | LodgingBillReceipt-ORG.rpt | Dated copy of the KOT-style slip. |
| B29 | LodgingBillReceiptCancelKot.rpt | Cancelled-KOT slip never opened. |
| B30 | LuxuryTax by Nikita.rpt | Author copy of the GST slab report. |
| B31 | MultiSalesBill.rpt | Layout selected by a setting that is off. |
| B32 | OccupiedDetail.rpt | No caller, no recovered fields. |
| B33 | OPayment.rpt | No caller, no recovered fields. |
| B34 | OtherItemDetail By Nikita.rpt | Author copy of Other Item Detail. |
| B35 | OwnerBankLedgerReport1.rpt | Variant of an unused owner ledger. |
| B36 | RateChangeDetailReportD.rpt | Dharamshala variant of Rate Change Detail. |
| B37 | RetaurantSupplierOutstanding.rpt | Misspelt copy of an unused report. |
| B38 | RoomServedItemDetail by Nikita.rpt | Author copy. |
| B39 | RoomServedItemDetailDateWise2.rpt | Second layout, never opened. |
| B40 | SaleBillDirectReport.rpt | Variant of the direct bill; covered by rpt.money.counter-bills. |
| B41 | SalesBill - 05-01-24.rpt | Dated copy of the invoice layout. |
| B42 | SalesBill - 14-7-26.rpt | Dated copy of the invoice layout. |
| B43 | SalesBill - 5-10-26-ORG.rpt | Dated copy of the invoice layout. |
| B44 | SalesBill - With IGST.rpt | IGST layout; HCC is always CGST + SGST (DEC-136). |
| B45 | SalesBill 25-11-2022.rpt | Dated copy of the invoice layout. |
| B46 | SalesBill by Nikita.rpt | Author copy of the invoice layout. |
| B47 | SalesBill-28-11-24.rpt | Dated copy of the invoice layout. |
| B48 | SalesBill-A5 Report.rpt | A5 copy of the invoice layout. |
| B49 | SalesBill-Working.rpt | Working copy of the invoice layout. |
| B50 | SalesBillMultipleBillPrint By Nikita.rpt | Author copy of multiple bill print. |
| B51 | SalesBillMultipleBillPrint org.rpt | Original copy of multiple bill print. |
| B52 | TodaysCheckOutDetail.rpt | Never opened; check-outs are in the day-end blocks. |
| B53 | TodaysCustomerDeposit by Nikita.rpt | Author copy. |
| B54 | TodaysCustomerDeposit By Sagar.rpt | Author copy. |
| B55 | TodaysExpenses By Sagar.rpt | Author copy. |
| B56 | TodaysPaymentDetail.rpt | Never opened; payments are in rpt.money.collection-detail. |
| B57 | TodaysPurchasePayment.rpt | Purchase payments, store unused. |

### 4.3 Part C documents and registers (used = no)

| Id | Reason |
|---|---|
| C1.OtherDepositNew | Other Deposit (new): table empty in every year. |
| C1.OwnerPaymentDetail | Owner Payment: table empty; owner drawings are in rpt.money.cash-out-register. |
| C1.OwnerReceiptDetail | Owner Receipt: table empty. |
| C2.SupplierPayment | Purchase Payment: store module unused; vendor payments are in rpt.money.vendor-ageing. |
| C2.TodaysOtherCollection | Today's Other Collection: table empty; covered by rpt.money.collection-detail. |
| C2.OtherDeposit | Other Deposit: table empty. |
| C2.EmployeeAttandance | Employee Attendance sheet: attendance switched off; covered by rpt.people.attendance. |
| C2.EmpAdvance | Employee Advance sheet: HR unused; covered by rpt.people.advances-fnf. |
| C4.DonationCoupon | Donation coupon printing: dharamshala feature, not used by this hotel. |
| C5.LaundryStatus | Laundry Status: laundry module unused; covered by HCC stock laundry batches. |
| C5.CustomerReceiveOrReturn | Guest receive/return sheet: laundry unused. |
| C5.CurrentStock | Current Stock: store tables empty; covered by rpt.stock.stock-sheet. |
| C5.SupplierOutstanding | Supplier Outstanding (store): unused; covered by vendor ageing. |
| C6.AccountRegisters | Account registers (receipt, payment, bank, voucher, day book, cash book, ledger): accounting module switched off in the old software; HCC accounting reports cover them. |
| C6.AccountTrialBalances | Account trial balances, balance sheet, purchase and sales registers: switched off, several .rpt files missing; HCC has rpt.acc.trial-balance and rpt.acc.balance-sheet. |
| C8.OtherDepositReceipt | Other deposit receipt: table empty. |
| C8.FunctionProspectus | Function prospectus and sale bill (hall): tables empty; hall booking is a new module (rpt.guests.hall-*). |
| C8.RetaurantBill | Restaurant purchase bill: unused. |

### 4.4 Dropped by the owner

The hotel uses no referrers or agents with commission (owner, 1 Nov 2026; gap list A9 dropped). So Part A **A8** (Reference / booking-wise) and **A14** (Commission payment detail) are not carried over; the legacy `ReferenceCode` stays readable in the imported history only.

## 5. Owner questions — all 8 accepted as recommended (owner, 1 Nov 2026: "Accept all 8"; DEC-202)

Only business choices. Each has a recommendation.

1. **Which finance reports may the Manager open?**
   - Background: Matrix v2 gives the Manager RPT-002 'A'. Choose: (a) every report; (b) everything except P&L, balance sheet, cash ratio, Tally, TDS and payroll. These are the profit and people-cost views.
   - Recommendation: Choose (b). Manager keeps collections, cash book, closing, GST working, expenses and all operations reports. Owner and Accountant see the rest.
2. **May the Accountant see guest names in reports?**
   - Background: Today the Accountant sees masked names in Reports (J•••) because the rule uses coarse permissions, though the same role reads the ledgers and the GSTR-1 working with names.
   - Recommendation: Show names to the Accountant in finance reports, ledgers, ageing and GST. Keep ID numbers masked for every role except PERM-GST-003.
3. **May the Cashier print or export?**
   - Background: The Cashier may today print the full Reports page. The matrix gives the Cashier 'own shift' view only.
   - Recommendation: Cashier views and prints own-shift collections, shift report and cash count sheet. No Excel or CSV export (needs RPT-003).
4. **Should Reception see money on the desk lists?**
   - Background: Arrivals and in-house lists carry an advance-paid and balance-due column. Reception collects these.
   - Recommendation: Yes for those two lists and rpt.guests.movement-register. No for revenue, collections and any report that totals the hotel's money.
5. **How far back may reports go?**
   - Background: The legacy import holds ten FYs of stays, bills and receipts. Accounting vouchers, payroll and expenses start in HCC only.
   - Recommendation: Allow FY history on stay, room, receipt, GST and guest reports (marked 'legacy' in the header). Not on vouchers, payroll, expenses or stock.
6. **Build PF ECR, ESIC upload files or a GSTR-1 portal JSON?**
   - Background: Plan W7 says 'if chosen'. The CA files returns today; HCC only prepares a working.
   - Recommendation: Not at go-live. Ship GSTR-1 Excel and CSV (P1). Leave ECR, ESIC and portal JSON at P3.
7. **What may leave the building by e-mail or WhatsApp on a schedule?**
   - Background: Scheduled runs go through the messaging module. A report with money or guest data sent by chat can be forwarded.
   - Recommendation: Money reports by e-mail only, to the Owner and Accountant addresses. WhatsApp for rpt.guests.daily-ops counts only, no names, no money.
8. **Who may print the guest register and Form C register, and for how long are they kept?**
   - Background: Both are statutory records with ID data. Retention follows DEC-071.
   - Recommendation: Owner, Manager and Reception view and print them for stays in the last 90 days. Older rows need Owner or Manager. No full ID number ever appears in a file.

## 6. Counts

**Entries: 130.**

| By status | Count |
|---|---|
| built | 62 |
| partial | 13 |
| ACC-3 | 7 |
| planned | 48 |

| By priority | Count |
|---|---|
| P1 | 43 |
| P2 | 62 |
| P3 | 25 |

| By area | Count |
|---|---|
| Guests and stays | 21 |
| Rooms and housekeeping | 14 |
| Money and cash | 30 |
| Tax | 12 |
| Accounting | 15 |
| People | 13 |
| Stock | 9 |
| Restaurant | 5 |
| Admin and audit | 11 |

| Legacy | Count |
|---|---|
| Legacy reports total (Part A 61, Part B 57, Part C 49) | 167 |
| Used = yes or probable, mapped to an entry: Part A | 33 of 35 (A8, A14 dropped by the owner, §4.4) |
| Used = yes or probable, mapped to an entry: Part C | 31 of 31 |
| Entries that are new because of a legacy report (status planned, with legacy ids) | 29 |
| Entries that already existed in HCC and cover legacy reports | 11 |
| New entries with no legacy report (W7 plan or HCC gap) | 19 |
| Not carried over: Part A | 28 |
| Not carried over: Part B | 57 |
| Not carried over: Part C | 18 |
| Not carried over, total | 101 |
| Entries with money flag y | 97 |
| Entries with money flag n | 33 |

**HCC inventory items that are not entries** (the inventory has 82 headings plus a planned list): `ac-exports` is a capability of the renderer (section 1.5); `fd-live-roomboard`, `fd-live-calendar`, `fd-live-reservations` and `fd-live-inhouse` are live screens, wrapped by `rpt.rooms.room-status`, `rpt.rooms.rooms-left`, `rpt.guests.reservations-register` and `rpt.guests.inhouse-list`; `ad-approvals-inbox` is a work queue; `ad-overview` is a dashboard; `ad-assistant` is a question tool; `rs-kot` and `rs-86` are documents (ETP spot 30); `ac-tally-simulated` and the ACC-3 `ac-tally-xml` are one entry (`rpt.acc.tally-xml`); `mn-pos-day-summary` and `tx-rep-gst-lines` are pointer headings.


<!-- F4-T2 -->
- rpt.money.revenue-by-category-tender - Revenue by room category and tender | Money and cash | built (F4-T2) | P1 | roles Owner, Accountant, Manager (PERM-RPT-001 + PERM-RPT-002) | money y | Blocks A (revenue by room type, ties to rpt.money.revenue-tax), B (tender columns, ties to daily-collections) and C (bridge); stay-basis toggle; CSV and Excel equal the screen.
<!-- F4-T2 end -->
<!-- F7-R6 --> rpt.acc.trial-balance, rpt.acc.statement, rpt.acc.day-book: built on the engine (prototype + server), PERM-ACC-001 (Owner, Manager, Accountant).
<!-- F7-R6 --> rpt.acc.ageing: ACC-3, wired on the engine, PERM-ACC-001. rpt.acc.pl, rpt.acc.balance-sheet, rpt.acc.cash-ratio: ACC-3, wired on the engine, PERM-ACC-013 (Owner, Accountant).
<!-- F7-R6 --> rpt.acc.tally-xml: ACC-3, summary report on the engine (formats screen + xml), PERM-ACC-006; the XML download stays the ACC-3 endpoints.

<!-- F12 counts -->
**MEAS addendum (5 Nov 2026, section 3.10):** 54 planned entries added (32 measurement + 22 study); catalogue total 184. By area: Guests and stays +10, Rooms and housekeeping +6, Money and cash +3, Accounting +1, People +5, Stock +1, Revenue management +6 (new), Analytics and study +22 (new). Money flag y: 33; n: 21. Priority: P1 13, P2 40, P3 1.
<!-- F12 counts end -->

<!-- F12-INT -->
Status 7 Nov 2026 (f12-int): all 54 section 3.10 entries are built in code on both sides (prototype `src/domain/reports/*`, server `Hcc.Reporting/Reports/Meas*` and `MeasStudy*`); ids unchanged. Money flag corrected to y: rpt.revenue.overbooking-wash, rpt.rooms.ooo-days, rpt.rooms.utilities-per-room, rpt.stock.linen-cost. Report SQL is compiled; only part of it is executed by tests (see `05 Delivery Evidence/Measurement/MEAS-DONE.md`).
<!-- F12-INT end -->


<!-- F8 (SF-CONTRACT §12): P2 report lanes write their status lines inside their own block. -->
<!-- F8-RPT-P2-A -->
<!-- F8-RPT-P2-A end -->
<!-- F8-RPT-P2-B -->
<!-- F8-RPT-P2-B end -->
<!-- F8-RPT-P2-C -->
Status 8 Nov 2026 (F8 rpt-p2-c): `rpt.acc.asset-register`, `rpt.people.leave-balances`, `rpt.stock.grn-register`, `rpt.rest.sales-by-item`, `rpt.rest.voids`, `rpt.admin.scheduled-runs`, `rpt.admin.cancelled-lines` and `rpt.admin.rate-sheet` are built on the engine, prototype and server (ids unchanged). Differences from the entries above: asset register has no per-asset Depreciation / Written-down value columns (no rate table in HCC, DEC-138 / DEC-140); rate sheet is three views (rate sheet / publish log / change history) with per-view columns; rest.voids Time is the void date, or date and time for a whole-check void; admin.cancelled-lines Qty is always 1 (a folio line has no quantity) and "Cancelled by" is the requester of the correction. Details in `05 Delivery Evidence/F8/F8-RPT-P2-C-DONE.md`.
<!-- F8-RPT-P2-C end -->
