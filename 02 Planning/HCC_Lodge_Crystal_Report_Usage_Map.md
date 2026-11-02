# Lodge Crystal Reports: usage map

**Date:** 2 Nov 2026
**Purpose:** Decide which of the 715 `.rpt` files in the old VB6 Lodge software are really used, which are orphans or copies, how each used report is opened (menu, form, parameters), what data and features the reports need, and how that compares with HCC's W7 report catalogue (130 entries) and the planned ETP Reporting Engine (OpenXML Excel + PDFsharp PDF).
**Status:** read-only analysis. Nothing in `Existing Software` was changed. The per-file table is in `E:\Projects\HCC\02 Planning\lodge-reports-map.csv` (203 rows for the 203 distinct file names, plus 13 rows for report names the code asks for that exist in no folder).

**Sources:** `Existing Software\Forms\Forms` (`*.frm`, `*.bas`, `Hotel.vbp`, `Reports\`, `DataModi-ReportRelated.txt`), `Updated Version\Reports` and `Reports--19-9-26`, `Lodge 22-6-26-org\Lodge 22-6-26-org` (live-install copy, 157 `.rpt`) and its `Panchavati Report` folder, `pakage for 64 bit` (installer), `DB-PROFILE` (row counts), screenshots `Menu 1/4/5/7/7_1/7_2/8.jpeg`. HCC side: `Hotel Control Centre\02 Planning\HCC_Report_Catalogue_v1.md` (the W7 catalogue; it lives in the app repo, not in `E:\Projects\HCC\02 Planning`) and `HCC_Decision_Register.md`.

**Legend.** **[M] Measured** = counted or read directly from code or files. **[G] Guessed** = inference, with the reason. Untagged text is a design or decision statement.

---

## 1. Bottom line

* **[M] 715 files are only 203 distinct report names.** The same set is copied into 7 folders (Forms, Updated Version x2, the live-install copy, Panchavati and two root folders). 139 names are in all four main folders (Forms, both Updated Version folders, live-install copy); 42 are in the three development folders but not in the live-install copy; 17 exist only in the live-install copy (old copies).
* **[M] 129 of the 203 files are opened by code that the compiled program can reach** (`Hotel.vbp` forms reachable from the menu, plus modules). **55 are copies or variants** (dated, "by <person>", OLD, "- Copy", identical bytes) and **19 are orphans** (no caller at all). That is 74 unused files, 36 %.
* **[M] The code also asks for 13 report names that exist in no folder** (e.g. `profitloss.rpt`, `balancesheet.rpt`, `voucher.rpt`, `salesregister.rpt`). 8 are called from compiled forms (the accounting menu, which the live install does not show), 5 only from forms that are not in the project.
* **[M] "Used in code" is not "used by the hotel".** Of the 129, **49 feed modules the hotel does not use or the owner has dropped** (laundry, store, restaurant purchase, HR, owner ledger, finalize bill, function prospectus, accounting registers, calculate tax, commission and reference reports); the feeder tables are empty in all ten years or the catalogue records the owner's ruling. The W7 catalogue (written 1 Nov 2026 from a menu-by-menu review) already records that. **80 of the 129 map to a W7 catalogue entry; 49 are "not carried" there.**
* **[M] Live signal:** the report change log `DataModi-ReportRelated.txt` shows 2022-2023 edits to SalesBill, the CounterCashier set, CustomerDetail, CustomerOutstanding, DayEndReportB, RoomServedItemDetail, UserWiseCheckInCheckOut, CheckInReport, GRCFormat and others: the team kept these alive. The table `BillUpdationOLDDetail` has 1.16 million rows, so the Bill Updation reports are heavily used. `GSTR1` has 14,652 rows, `GSTTaxReport` 11,618 (the GST reports are run monthly).
* **[M] The live screenshots show 25 top-level Report items plus 9 sub-items.** The code has more (Taxation Report submenu, Tanker, Restaurant Payment, Room Wise Transaction Summary, Room Wise Allocation, Luxury Tax Report). They are hidden in the running install **[G: by user rights or setting; the screenshots do not show which]**.
* **There is no report-run log.** Neither Lodge nor the profile has a table that records which report was run when (searched all 282 table names and the code). Usage evidence is therefore indirect: caller in code, menu visibility, change log, and row counts in feeder tables. Section 8 states how to get real evidence cheaply.

---

## 2. Method (so it can be repeated)

1. **Inventory** [M]: hash and date every `.rpt` in the seven folders.
2. **Callers** [M]: every string ending `.rpt` in `*.frm/*.bas/*.cls` (258 references). Lines starting with `'` are comments. A reference counts as **used** only if the form is in `Hotel.vbp` **and** can be reached from a menu handler or from another reachable form (`Show`, `Load`, `Visible = True`, `Tag =` chains), or sits in a module.
3. **Menu link** [M]: `mdimain.frm` has 246 menu items; 159 open a form. Most reports go through one generic form, `frmreport`, which a menu handler selects with `frmreport.Tag = "<Key>"`; the report file is chosen inside `frmreport` by `If/Case Tag = ...`. The map follows that Tag.
4. **Classification:** *used*; *duplicate* (not called, and name matches a used report after removing words such as OLD, "by <name>", dates, "- Copy", ORG, or bytes identical to another file); *orphan* (not called, no sibling).
5. **HCC match** [M for catalogue ids that name the report; G where marked "inferred" in the CSV]: through the legacy ids in the catalogue summary table (A = menu report, B = dormant file, C = document opened by another form) and its section 4.

Reachability result: **139 forms on disk, 124 in the project, 113 reachable from the menu**, 26 not reachable (14 of those are not even in the project; `frmImageCapture`, one reachable form, is also outside the project).

---

## 3. Counts

| Item | Count | Basis |
|---|---:|---|
| `.rpt` files in all folders | 715 | [M] |
| Distinct names | 203 | [M] |
| Forms / modules | 139 forms (124 compiled), 8 `.bas`, 1 `.cls` | [M] |
| `.rpt` string references in code | 258 (207 active, 51 in comments; the active ones are `ReportFileName` assignments and 16 `OpenReport` calls) | [M] |
| Used in compiled, reachable code | 129 files | [M] |
| Used but business-dead (empty feeder tables) | 49 | [M] with catalogue section 4 |
| Duplicate / variant | 55 | [M] rule-based |
| Orphan | 19 | [M] |
| Names the code wants that exist nowhere | 13 | [M] |
| Used files missing from the live-install copy | 9 (BillSummaryAccountDetail, BillWisePaymentModeReport, DuplicateCustomerDetail, GRCFormatReport, GSTR1ReportIGST, GSTR1StandardReport, OtherDepositReceipt, TariffSalesDetail, UserWiseCheckInCheckOutReport) | [M] |
| Used files edited since 1 Jul 2025 (file date) | 9 (BalanceRoom, BillSummary, CustomerDetail, DayEndReportB, LuxuryTax, RoomServedItemDetail, SalesBill, SalesBillMultipleBillPrint, TrialSalesBill) | [M] dates; **[G]** many 2022 dates are copy dates, not edits |
| Used reports containing sub-report or embedded objects | 12 (DayEndDetail, DayEndReportA/AD/B/BD/CD, GSTR1Report/IGST/Standard, SalesBill, SalesBillFinalize, SalesBillMultipleBillPrint); 24 in all 182 | [M] counted OLE sub-document streams |

**Cross-check with the W7 catalogue** (written independently): it lists **57 dormant `.rpt` files** (Part B). My Forms-folder duplicates plus orphans are 53; the other 4 are `HSNDetail`, `MultiSalesBill` (in the Forms root) and two dated SalesBill copies in the Updated Version folders. So the two analyses agree.

---

## 4. How reports are opened (menu to file)

Generated from the code. "Opens" = the form and, in square brackets, the `Tag` that selects the report. Report-menu items are checked against the screenshots (`Menu 7*.jpeg`).

| Menu path (from code) | Opens | Report files | Shown in live screenshot |
|---|---|---|---|
| Master Information > Guest Information | frmCustomerMaster | DuplicateCustomerDetail.rpt |  |
| Store > Room Wise Laundry Stock Detail Report | frmreport [RoomWiseLaundryStockDetail] | RoomWiseLaundryStockDetail.rpt |  |
| Store > Lodging Material Stock Detail Report | frmreport [LodgingMaterialStockDetail] | LodgingMaterialStockDetailReport.rpt |  |
| Store > Laundry Stock Detail Report | frmreport [LaundryStockDetail] | LaundryStockDetailReport.rpt |  |
| Store > Current Stock Report | frmPurchaseReport [CurrentStock] | CurrentStockReport.rpt |  |
| Store > Supplier Outstanding | frmPurchaseReport [SupplierOutstanding] | SupplierOutstanding.rpt |  |
| Laundry Transaction > Laundry Supplier Outstanding | frmreport [LaundrySupplierOutstanding] | SupplierOutstanding.rpt |  |
| Restaurant Transaction > Restaurant Supplier Outstanding Report | frmreport [RetaurantSupplierOutstanding] | SupplierOutstanding.rpt |  |
| Laundry Transaction > Guest Receive/Return | frmreport [CustomerReceiveOrReturn] | CustomerReceiveOrReturn.rpt |  |
| Laundry Transaction > Laundry Receive/Return Report | frmLaundryReport [ReceiveOrReturn] | CustomerReceiveOrReturn.rpt |  |
| Transaction > Lodging Bill | frmSalesBill | CustomerReceiveOrReturn.rpt, GRCFormatReport.rpt, LodgingBillReceipt.rpt, RoomServedItemDetail.rpt, RoomStatusReport.rpt, SalesBill.rpt, SalesBillD.rpt, TrialSalesBill.rpt |  |
| Laundry Transaction > Guest Laundry Balance | frmreport [CustomerLaundryBalance] | LaundryReport.rpt |  |
| Report > Room Service Detail > Laundry Statement | frmreport [LaundryReport] | LaundryReport.rpt | yes |
| Laundry Transaction > Laundry Status | frmLaundryReport [LaundryStatus] | LaundryStatus.rpt |  |
| Restaurant Transaction > Restaurant Purchase Bill | frmRetaurantPurchaseBill | RetaurantBill.rpt |  |
| Restaurant Transaction > Restaurant Purchase Report | frmreport [RetaurantPurchaseReport] | RetaurantPurchaseReport.rpt |  |
| Transaction > Sales Bill Report | frmSalesBillReport | CheckInReport.rpt, CheckInReportD.rpt, DirectBill.rpt, DirectBillSummary.rpt, DonationCouponPrinting.rpt, PhotoAddressProof.rpt, RoomServedItemDetail.rpt, SalesBill.rpt, SalesBillD.rpt |  |
| Report > Occupancy Room | frmreport [OccupancyRoom] | RoomStatusReport.rpt | hidden in code |
| Report > Todays Room Status | frmreport [TodaysRoomStatus] | RoomStatusReport.rpt | yes |
| Transaction > Function Prospectus Detail > Function Prospectus | frmFunctionProspectus | FunctionProspectusReport.rpt |  |
| Transaction > Function Prospectus Detail > Function Prospectus Sales Bill | frmFunctionProspectusSalesBill | FunctionProspectusSaleBillReport.rpt |  |
| Transaction > Other Depositer | frmOtherDeposit | OtherDepositReceipt.rpt |  |
| Transaction > Counter Cashier's Report | frmCouCaseesReportI | CounterCashierDetailReport.rpt, CounterCashierReport.rpt, CounterCashierReportShort.rpt, CounterCashierReportShortD.rpt, DepositDetailReport.rpt, DirectBillSummary.rpt, DirectBillSummaryReport.rpt, EmpAdvanceReport.rpt, EmployeeAttandance.rpt, OtherDeposit.rpt, PreviousAdvancePaymentReport.rpt, SupplierPayment.rpt, TodaysCreditDetail.rpt, TodaysCustomerDeposit.rpt, TodaysCustomerOtherDiscount.rpt, TodaysExpenses.rpt, TodaysOtherCollection.rpt |  |
| Transaction > Day Close | frmDayclose | CounterCashierReport.rpt, CounterCashierReportShort.rpt, DayEndReportA.rpt, DayEndReportB.rpt, DayEndReportInDetail.rpt |  |
| Transaction > Counter Cash Hand Over Report | frmreport [CounterCashHandOverReport] | CounterCashHandOverReport.rpt |  |
| Transaction > Multiple Sales Bill Print | frmreport [MultipaleSalesBillPrint] | SalesBillMultipleBillPrint.rpt |  |
| Report > Day End Report | frmreport [DayEndReport] | DayEndReportA.rpt, DayEndReportAD.rpt, DayEndReportB.rpt, DayEndReportBD.rpt, DayEndReportCD.rpt | yes |
| HR Department > Salary Sheet | frmreport [SalarySheet] | SalarySheetDos.rpt |  |
| HR Department > Employee Ledger | frmreport [EmployeeLedger] | EmployeeLedgerReport.rpt |  |
| HR Department > Employee Attendance Report | frmreport [EmployeeAttendenceReport] | EmployeeAttendenceReport.rpt |  |
| HR Department > Employee Salary Advance Report | frmreport [EmployeeSalaryAdvanceReport] | EmployeeSalaryadvance.rpt |  |
| Administrator > Bill Finalize Report > Finalize Sales Bill Report | frmreport [FinalizeSalesBillPrint] | SalesBillFinalize.rpt |  |
| Administrator > Bill Finalize Report > Luxury Tax Report | frmreport [LuxuryTaxReportFinalize] | LuxuryTaxFinalize.rpt |  |
| Administrator > Bill Finalize Report > Service Tax Report | frmreport [ServiceTaxReportFinalize] | ServiceTaxReportFinalize.rpt |  |
| Administrator > Bill Finalize Report > Finalize Bill Summary Report | frmreport [BillSummaryFinalize] | BillSummerFinalizeShort.rpt |  |
| Administrator > Bill Finalize Report > Finalize Bill Detail Summary Report | frmreport [BillDetailSummaryFinalize] | BillSummaryFinalize.rpt |  |
| Report > Lodging Report | frmDaycloseReport | BillSummary.rpt, BillSummaryAccount.rpt, BillSummaryAccountDetail.rpt, BillSummerShort.rpt, BillSummerShortD.rpt, BillWisePaymentModeReport.rpt, CancelItemReport.rpt, DayEndDetail.rpt, GuestRegister.rpt, GuestRegisterD.rpt, OtherDepositNew.rpt, OwnerPaymentDetail.rpt, OwnerReceiptDetail.rpt, PaidOutDetail.rpt, PaidOutSummary.rpt, TariffSalesDetail.rpt, departmentwisesales.rpt, exciseliquer.rpt, todayssalesreport.rpt | yes |
| Report > Paid Out Detail | frmreport [ExpensesDetail] | PaidOutDetail.rpt, PaidOutSummary.rpt | yes |
| Report > Guest Detail Report > Guest Outstanding | frmreport [CustomerOutstanding] | CustomerOutstanding.rpt | yes |
| Report > Guest Detail Report > Bill Wise Outstanding Detail | frmreport [BillWiseOustandingDetail] | BillWiseOustandingDetailReport.rpt | yes |
| Report > Guest Detail Report > Guest Ledger Detail | frmreport [CustOutstandingDetail] | CustomerDetail.rpt | yes |
| Report > Guest Detail Report > Guest Tariff Detail | frmreport [CustomerTariffDetail] | CompanyTariffDetail.rpt, CustomerTariffDetail.rpt | yes |
| Report > Guest Detail Report > Guest Refund Detail | frmreport [CustomerRefundDetail] | CustomerRefundReport.rpt | yes |
| Report > Guest Detail Report > Guest Information Report | frmreport [CustomerDetailsReport] | GuestInformationReport.rpt | yes |
| Report > Lodge GST Report | frmreport [LuxuryTaxReport] | LuxuryTax.rpt | yes |
| Report > Luxury Tax Report | frmreport [LuxuryTaxReport] | LuxuryTax.rpt | not in screenshot |
| Report > GST R1 Report | frmreport [GSTR1Report] | GSTR1Report.rpt, GSTR1ReportIGST.rpt, GSTR1StandardReport.rpt | yes |
| Report > Userwise Check-In  Check-Out Report | frmreport [UserWiseCheckInCheckOut] | UserWiseCheckInCheckOutReport.rpt | yes |
| Report > Commission Payment Detail | frmreport [CommissionPaymentDetail] | CommissionPaymentDetailReport.rpt | yes |
| Report > Taxation Report > Service Tax Detail Report | frmreport [ServiceTaxDetailReport] | ServiceTaxDetailReport.rpt | not in screenshot |
| Report > Taxation Report > Service Tax Report | frmreport [ServiceTaxReport] | ServiceTaxReport.rpt | not in screenshot |
| Report > Taxation Report > Calculate Tax Report | frmreport [CalculateTaxReport] | CombineLuxuryAndServiceTaxCalculateReport.rpt, LuxuryTaxCalculateReport.rpt, ServiceTaxCalculateReport.rpt | not in screenshot |
| Report > Room Service Detail > Food Laundry Detail | frmreport [FoodLaundryDetail] | FoodLaundryDetail.rpt, RoomServedItemDetailDateWise.rpt | yes |
| Report > Room Service Detail > Other Item Detail | frmreport [OtherItemDetail] | OtherItemDetail.rpt | yes |
| Report > Owner/ Bank Ledger Report  | frmreport [OwnerBankLedger] | OwnerBankLedgerReport.rpt | yes |
| Report > Rate Change Detail | frmreport [RateChangeDetail] | RateChangeDetailReport.rpt | yes |
| Report > Room Wise House Keeping Period | frmreport [RoomWiseHouseKeepingPeriod] | RoomWiseHouseKeepingPeriodReport.rpt | yes |
| Report > Tanker detail report | frmreport [TankerDetailReport] | TankerDetailReport.rpt | not in screenshot |
| Report > Reference / Booking Wise Report | frmreport [ReferenceWiseCollection] | ReferenceWiseCollectionReport.rpt | yes |
| Report > Collection Detail | frmreport [CollectionDetail] | CollectionDetail.rpt | yes |
| Report > Restaurant Payment | frmreport [RestaurantPayment] | RestaurantPayment.rpt | not in screenshot |
| Report > Occupancy Average | frmreport [OccupancyAverage] | OccupancyAverage.rpt | yes |
| Report > Room Transaction Detail | frmreport [RoomTransactionDetail] | RoomTransactionDetailReport.rpt | yes |
| Report > Room Wise Transaction Summary Report | frmreport [RoomWiseTransactionSummaryReport] | RoomWiseTransactionSummaryReport.rpt | not in screenshot |
| Report > Room Wise Allocation Detail | frmreport [RoomWiseAllocationDetail] | RoomWiseAllocationDetail.rpt | not in screenshot |
| Report > Advance Booking Detail | frmreport [AdvanceBookingDetail] | AdvanceBookingDetail.rpt | yes |
| Report > Direct Bill Detail Report | frmreport [DirectBillDetailReport] | DirectBillDetailReport.rpt | yes |
| Report > Business From Company | frmreport [BisnessFromCompany] | BusinessDetailReport.rpt | yes |
| Report > NRI(foreigner) Report | frmreport [NRIReport] | NRIBusinessDetailReport.rpt | yes |
| Report > Bill Updation Report | frmreport [BillUpdationReport] | BillUpdationDifference.rpt | hidden in code |
| Account Report  > Receipt Register  | frmvoucherRegister [ReceiptRegister] | ReceiptRegister.rpt |  |
| Account Report  > Payment Register | frmvoucherRegister [PaymentRegister] | PaymentRegister.rpt |  |
| Account Report  > Contra Register | frmvoucherRegister [ContraRegister] | BankRegister.rpt |  |
| Account Report  > Journal Register | frmvoucherRegister [JournalRegister] | VoucherRegister.rpt |  |
| Account Report  > Register > Day Book | frmvoucherRegister [DayBook] | DayBookRegister.rpt |  |
| Account Report  > Register > Ledger | frmvoucherRegister [Ledger] | Ledger.rpt |  |
| Account Report  > Register > Cash Book | frmvoucherRegister [CashBook] | CashBook.rpt |  |

Other openers that are not menu items: `frmSalesBill` (bill, trial bill, registration card, KOT-style slip), `frmSalesBillReport` (sales bill range print, registration card, PhotoAddressProof), `frmDayclose` and `Module1` (day-end sets A/B/C and counter cashier, exported to PDF for e-mail), `frmCouCaseesReportI` (counter cashier and its 16 sub-sheets, 17 reports in all), `frmCustomerDeposit` (deposit receipt), `frmDirectBill`, `frmRoomBooking` (Form C), `frmCustomerMaster` (duplicate guests), `mdimain` (room tariff card, wrong payment entry). Two Report-menu items have **no `.rpt`**: SAC Summary and Summary of Document Issue are coded output (`SACSummary`, `SummaryOfDocument` in `frmreport`).

---

## 5. Which Lodge reports HCC covers

### 5.1 Used reports that the W7 catalogue carries (80 files -> 37 entries)

| HCC catalogue entry | Status / priority | Lodge .rpt files that feed it |
|---|---|---|
| rpt.acc.ageing | ACC-3 P1 | CustomerOutstanding |
| rpt.acc.statement | built P1 | CustomerDetail |
| rpt.admin.cancelled-lines | planned P2 | CancelItemReport |
| rpt.admin.desk-reminders | planned P3 | AlertUserDefineReport |
| rpt.admin.rate-sheet | partial P2 | RoomTraiffCard |
| rpt.guests.company-business | planned P2 | BusinessDetailReport |
| rpt.guests.customer-history | partial P2 | DuplicateCustomerDetail |
| rpt.guests.form-c-register | planned P1 | CFormReport, NRIBusinessDetailReport |
| rpt.guests.guest-directory | planned P3 | GuestInformationReport |
| rpt.guests.guest-register | planned P1 | CheckInReport, CheckInReportD, GuestRegister, GuestRegisterD, PhotoAddressProof |
| rpt.guests.guest-tariff | planned P2 | CompanyTariffDetail, CustomerTariffDetail |
| rpt.guests.rate-changes | planned P1 | RateChangeDetailReport |
| rpt.guests.service-lines | planned P2 | FoodLaundryDetail, LaundryReport, OtherItemDetail, RoomServedItemDetail, RoomServedItemDetailDateWise |
| rpt.guests.userwise-activity | planned P2 | UserWiseCheckInCheckOutReport |
| rpt.money.closing-report | built P1 | CounterCashierReport, CounterCashierReportShort, CounterCashierReportShortD, DayEndReportA, DayEndReportAD, DayEndReportB, DayEndReportBD, DayEndReportCD |
| rpt.money.collection-detail | planned P1 | BillSummaryAccount, BillSummaryAccountDetail, BillWisePaymentModeReport, CollectionDetail, CusomerDepositReport, DepositDetailReport, TodaysCustomerDeposit |
| rpt.money.counter-bills | planned P2 | DirectBill, DirectBillDetailReport, DirectBillSummary, DirectBillSummaryReport |
| rpt.money.credit-bills | planned P1 | TodaysCreditDetail |
| rpt.money.day-pack | built P1 | CounterCashierDetailReport, DayEndDetail, DayEndReportInDetail |
| rpt.money.discount-log | planned P2 | TodaysCustomerOtherDiscount |
| rpt.money.expense-register; rpt.money.expenses-by-head | partial P1, planned P1 | PaidOutDetail, PaidOutSummary, TodaysExpenses |
| rpt.money.payment-corrections | planned P2 | WrongPaymentEntry |
| rpt.money.receipts-deposits | built P1 | PreviousAdvancePaymentReport |
| rpt.money.refunds | planned P2 | CustomerRefundReport |
| rpt.money.shift-report | planned P1 | CounterCashHandOverReport |
| rpt.rooms.allocation-grid | planned P2 | RoomWiseAllocationDetail |
| rpt.rooms.hk-turnaround | planned P2 | RoomWiseHouseKeepingPeriodReport |
| rpt.rooms.occupancy | built P1 | OccupancyAverage |
| rpt.rooms.room-status | planned P1 | RoomStatusDetailReport, RoomStatusReport |
| rpt.rooms.room-wise-revenue | planned P2 | RoomTransactionDetailReport, RoomWiseTransactionSummaryReport, TariffSalesDetail |
| rpt.rooms.rooms-left | planned P2 | BalanceRoom |
| rpt.tax.bill-corrections | planned P1 | BillUpdationDifference |
| rpt.tax.gst-by-bill | planned P1 | LuxuryTax, ServiceTaxDetailReport, ServiceTaxReport |
| rpt.tax.gstr1 | built P1 | GSTR1Report, GSTR1ReportIGST, GSTR1StandardReport |
| rpt.tax.invoice-register | planned P1 | BillSummary, BillSummerShort, BillSummerShortD, LodgingBillReceipt, SalesBill, SalesBillD, SalesBillMultipleBillPrint |
| rpt.tax.invoice-register / rpt.guests.guest-register | planned P1, planned P1 | GRCFormatReport, TrialSalesBill |

**Status mix of the 80 carried files [M]:** built 17, partial 5, ACC-3 1, planned 57. The 57 "planned" ones are the main build work for the W7 report module (catalogue section 6: 48 planned + 13 partial + 7 ACC-3 entries of 130).

### 5.2 Used in code but not carried (49 files)

| Why HCC does not carry it (catalogue section) | Lodge .rpt files |
|---|---|
| cat 4.1 A37; HCC rpt.guests.reservations-register | AdvanceBookingDetail |
| cat 4.3 C6: accounting module switched off in Lodge; HCC ACC reports | BankRegister, CashBook, ComposedTrialBalance, DayBookRegister, DetailTrialBalance, Ledger, PaymentRegister, ReceiptRegister, VoucherRegister |
| cat 4.1 A46,A57-A60: finalize tables empty; replaced by rpt.tax.bill-corrections | BillSummaryFinalize, BillSummerFinalizeShort, LuxuryTaxFinalize, SalesBillFinalize, ServiceTaxReportFinalize |
| cat 4.1 A28 (inside rpt.acc.ageing) | BillWiseOustandingDetailReport |
| cat 4.1 A21 Calculate Tax | CombineLuxuryAndServiceTaxCalculateReport, LuxuryTaxCalculateReport, ServiceTaxCalculateReport |
| cat 4.4 dropped by owner (A14, A8) | CommissionPaymentDetailReport, ReferenceWiseCollectionReport |
| cat 4.1/4.3 laundry and store modules unused; covered by HCC stock | CurrentStockReport, CustomerReceiveOrReturn, LaundryStatus, LaundryStockDetailReport, LodgingMaterialStockDetailReport, RoomWiseLaundryStockDetail, SupplierOutstanding, SupplierPayment |
| cat 4.3 C4.DonationCoupon | DonationCouponPrinting |
| cat 4.1 A51-A54 / C2: HR unused in Lodge; HCC people reports | EmpAdvanceReport, EmployeeAttandance, EmployeeAttendenceReport, EmployeeLedgerReport, EmployeeSalaryadvance, SalarySheetDos |
| cat 4.3 C8.FunctionProspectus; hall booking is a new HCC module | FunctionProspectusReport, FunctionProspectusSaleBillReport |
| cat 4.1 A17 / 4.3 owner and other-deposit tables empty | OtherDeposit, OtherDepositNew, OtherDepositReceipt, OwnerBankLedgerReport, OwnerPaymentDetail, OwnerReceiptDetail, TodaysOtherCollection |
| cat 4.1 A9,A47 / C8.RetaurantBill | RestaurantPayment, RetaurantBill, RetaurantPurchaseReport |
| cat 4.1 A32 | TankerDetailReport |

### 5.3 Needs a decision (not simply covered)

| # | Item | Why it needs a decision | Suggested ruling |
|---|---|---|---|
| D1 | **Registration card layouts**: `CheckInReport`, `CheckInReportD`, `GRCFormatReport`, `PhotoAddressProof`, `GuestRegister`, `GuestRegisterD` | The catalogue folds them all into `rpt.guests.guest-register` (planned P1). Lodge has six physical layouts, some with ID pictures printed on the sheet. `GRCFormatReport` was added 8 Jul 2023 (change log), i.e. someone asked for it. | Owner picks the one card layout and whether the ID picture is printed (ties to the ID patch) |
| D2 | **GSTR-1 IGST layout** (`GSTR1ReportIGST`) | HCC is CGST+SGST only (DEC-136). A guest from another state staying in Maharashtra is still intra-state, but a company billed in another state is not. | Confirm with the CA that no IGST invoice is ever raised |
| D3 | **Multiple Sales Bill Print** and **Trial Sales Bill** (`SalesBillMultipleBillPrint`, `TrialSalesBill`) | Catalogue maps them only loosely to the invoice register; both were edited in 2025-26. | Confirm the HCC invoice print covers "one bill per room group" and "pro-forma before check-out" |
| D4 | **Day-end sets** A, B, C and their D (detail) versions, plus `DayEndReportInDetail` | HCC has closing report and day pack; Lodge's A/B/C split is a hotel habit and goes to the CA by e-mail as PDF. | Show the owner HCC's day pack next to Lodge A/B/C and agree |
| D5 | **Bill Updation** (`BillUpdationDifference`) | 1.16 M rows in `BillUpdationOLDDetail`; HCC plans `rpt.tax.bill-corrections` (planned P1) | Keep P1; data comes from the LF wave |
| D6 | **Balance Room** (`BalanceRoom`) and **Room Status Detail** | Catalogue guesses `rooms-left` / `room-status` | Confirm what the front desk uses daily |
| D7 | **Accounting reports the code asks for but no file exists** (profit and loss, balance sheet, trial balance, voucher, sales and purchase registers) | Lodge's accounting menu is off in the live install. ACC-3 delivers P&L, balance sheet and GST summary in HCC | None needed; note for the migration plan |

---

## 6. Unused files: orphans and duplicates (74)

All are safe to leave behind in a rebuild or an HCC port; none is opened by reachable code. "Variant of X" means the base name matches a used report. The CSV gives the full path set, size and date for each.

| File | Status | Why | Found in |
|---|---|---|---|
| AdvanceBookingDetail by Nikita.rpt | duplicate | variant of advancebookingdetail.rpt | F,UV,UV926 |
| AdvanceBookingDetailReport OLD.rpt | duplicate | dated/author/old copy (base not matched exactly) | LIVE |
| BillSummaryAccount-OLD.rpt | duplicate | variant of billsummaryaccount.rpt | F,UV,UV926 |
| BillSummaryOLD.rpt | duplicate | variant of billsummary.rpt | LIVE |
| BillSummerShortD OLD.rpt | duplicate | variant of billsummershortd.rpt | LIVE |
| BillSummerShortDOLD.rpt | duplicate | variant of billsummershortd.rpt | LIVE |
| BillUpdationDifference new.rpt | duplicate | variant of billupdationdifference.rpt | F,UV,UV926 |
| CheckInReport Naivedya.rpt | duplicate | variant of checkinreport.rpt | F,LIVE,UV,UV926 |
| CompanyTariffDetailData2Report.rpt | duplicate | dated/author/old copy (base not matched exactly) | F,LIVE,UV,UV926 |
| Copy of RoomStatusDetailReport.rpt | duplicate | variant of roomstatusdetailreport.rpt | LIVE |
| CounterCashierDetailReport-New.rpt | duplicate | variant of countercashierdetailreport.rpt | F,UV,UV926 |
| CounterCashierReportShort by nikita.rpt | duplicate | variant of countercashierreportshort.rpt | F,UV,UV926 |
| CounterCashierReportShortDOLD.rpt | duplicate | variant of countercashierreportshortd.rpt | LIVE |
| CusomerDepositReport31-12-22.rpt | duplicate | variant of cusomerdepositreport.rpt | F,UV,UV926 |
| CustomerDetail by nikita.rpt | duplicate | variant of customerdetail.rpt | F,UV,UV926 |
| CustomerDetail-28-01-26.rpt | duplicate | variant of customerdetail.rpt | F,UV,UV926 |
| CustomerOutstanding by nikita.rpt | duplicate | variant of customeroutstanding.rpt | F,UV,UV926 |
| CustomerTariffDetailData2Report.rpt | duplicate | dated/author/old copy (base not matched exactly) | F,LIVE,UV,UV926 |
| DayEndReportB By mangesh.rpt | duplicate | variant of dayendreportb.rpt | F,UV,UV926 |
| DayEndReportBDOLD.rpt | duplicate | variant of dayendreportbd.rpt | LIVE |
| DayEndReportInDetail-OLD.rpt | duplicate | variant of dayendreportindetail.rpt | F,UV,UV926 |
| DepositDetailReportOLD.rpt | duplicate | variant of depositdetailreport.rpt | F,LIVE,UV,UV926 |
| DirectBillDetailReportOLD.rpt | duplicate | variant of directbilldetailreport.rpt | F,LIVE,UV,UV926 |
| FoodLaundryDetailData2Report.rpt | duplicate | dated/author/old copy (base not matched exactly) | F,LIVE,UV,UV926 |
| FunctionProspectusSaleBillReport By Nikita.rpt | duplicate | variant of functionprospectussalebillreport.rpt | F,UV,UV926 |
| GSTR1Report - Copy.rpt | duplicate | variant of gstr1report.rpt | F,UV,UV926 |
| GSTR1StandardReport - Copy.rpt | duplicate | variant of gstr1standardreport.rpt | F,UV,UV926 |
| GuestRegisterData2Report.rpt | duplicate | dated/author/old copy (base not matched exactly) | F,LIVE,UV,UV926 |
| LodgingBillReceipt-ORG.rpt | duplicate | variant of lodgingbillreceipt.rpt | F,UV,UV926 |
| LuxuryTax 03-10.rpt | duplicate | variant of luxurytax.rpt | LIVE |
| LuxuryTax by Nikita.rpt | duplicate | variant of luxurytax.rpt | F,UV,UV926 |
| OtherItemDetail By Nikita.rpt | duplicate | variant of otheritemdetail.rpt | F,UV,UV926 |
| ReferenceWiseCollectionReport 03-10.rpt | duplicate | variant of referencewisecollectionreport.rpt | LIVE |
| RoomServedItemDetail by Nikita.rpt | duplicate | variant of roomserveditemdetail.rpt | F,UV,UV926 |
| SalesBill - 05-01-24.rpt | duplicate | variant of salesbill.rpt | F,UV,UV926 |
| SalesBill - 14-7-26.rpt | duplicate | variant of salesbill.rpt | UV926 |
| SalesBill - 5-10-26-ORG.rpt | duplicate | variant of salesbill.rpt | UV |
| SalesBill - Copy.rpt | duplicate | variant of salesbill.rpt | LIVE |
| SalesBill - With IGST.rpt | duplicate | variant of salesbill.rpt | F,UV,UV926 |
| SalesBill 03-10.rpt | duplicate | variant of salesbill.rpt | LIVE |
| SalesBill 16-03-2020.rpt | duplicate | variant of salesbill.rpt | LIVE |
| SalesBill 25-11-2022.rpt | duplicate | variant of salesbill.rpt | F,UV,UV926 |
| SalesBill by Nikita.rpt | duplicate | variant of salesbill.rpt | F,UV,UV926 |
| SalesBill-28-11-24.rpt | duplicate | variant of salesbill.rpt | F,UV,UV926 |
| SalesBill-A5 Report.rpt | duplicate | variant of salesbill.rpt | F,UV,UV926 |
| SalesBill-Amerpreet.rpt | duplicate | variant of salesbill.rpt | LIVE |
| SalesBill-Working.rpt | duplicate | variant of salesbill.rpt | F,UV,UV926 |
| SalesBill_Old17022013.rpt | duplicate | variant of salesbill.rpt | LIVE |
| SalesBillMultipleBillPrint By Nikita.rpt | duplicate | variant of salesbillmultiplebillprint.rpt | F,UV,UV926 |
| SalesBillMultipleBillPrint org.rpt | duplicate | variant of salesbillmultiplebillprint.rpt | F,UV,UV926 |
| TankerDetailReportOLD.rpt | duplicate | variant of tankerdetailreport.rpt | LIVE |
| TodaysCustomerDeposit by Nikita.rpt | duplicate | variant of todayscustomerdeposit.rpt | F,UV,UV926 |
| TodaysCustomerDeposit By Sagar.rpt | duplicate | variant of todayscustomerdeposit.rpt | F,UV,UV926 |
| TodaysCustomerDeposit ORG13062014.rpt | duplicate | variant of todayscustomerdeposit.rpt | LIVE |
| TodaysExpenses By Sagar.rpt | duplicate | variant of todaysexpenses.rpt | F,UV,UV926 |
| AdvanceBookingDetailReport.rpt | orphan |  | F,LIVE,UV,UV926 |
| BillSummaryAccountReport.rpt | orphan |  | F,UV,UV926 |
| BillSummaryOLD (2).rpt | orphan |  | LIVE |
| CanteenCoupenDetail.rpt | orphan |  | F,LIVE,UV,UV926 |
| FoodBillSubReport.rpt | orphan |  | F,LIVE,UV,UV926 |
| HSNDetail.rpt | orphan |  | F0,UV0 |
| LaundryDetailReport.rpt | orphan |  | F,LIVE,UV,UV926 |
| LodgingBillReceiptCancelKot.rpt | orphan |  | F,LIVE,UV,UV926 |
| MultiSalesBill.rpt | orphan |  | F0,LIVE,UV0 |
| OccupiedDetail.rpt | orphan |  | F,LIVE,UV,UV926 |
| OPayment.rpt | orphan |  | F,UV,UV926 |
| OwnerBankLedgerReport1.rpt | orphan |  | F,UV,UV926 |
| RateChangeDetailReportD.rpt | orphan |  | F,LIVE,UV,UV926 |
| RetaurantSupplierOutstanding.rpt | orphan |  | F,LIVE,UV,UV926 |
| RoomServedItemDetailDateWise2.rpt | orphan |  | F,UV,UV926 |
| SaleBillDirectReport.rpt | orphan |  | F,LIVE,UV,UV926 |
| TodaysCheckOutDetail.rpt | orphan |  | F,LIVE,UV,UV926 |
| TodaysPaymentDetail.rpt | orphan |  | F,LIVE,UV,UV926 |
| TodaysPurchasePayment.rpt | orphan |  | F,LIVE,UV,UV926 |

---

## 7. Crystal usage style (summary level) and what a replacement engine must do

### 7.1 What Lodge does today

| Aspect | Finding | Basis |
|---|---|---|
| Crystal version | Crystal Reports **8.5** Report Designer Component (`craxdrt.dll` 8.5) plus the `Crystl32.OCX` control; the 64-bit packaging folder ships Crystal **9** runtime (`craxdrt9`, `crviewer9`), and one dead form (`frmSubFinancer`) uses the 9 viewer. 8.5 is unsupported by SAP and 32-bit only | [M] `Hotel.vbp`, `SETUP.LST`, support DLL list |
| How a report is run | **Mostly the OCX**: `mdimain.crpt.ReportFileName = ...`, `.Formulas(n) = ...`, `.SelectionFormula = ...`, `.Action = 1`, `.Destination = crptToWindow/crptToPrinter`. 191 active `ReportFileName` lines, 488 `.Formulas` lines, 163 `.SelectionFormula` lines, 48 `Destination` lines | [M] |
| RDC style (object model) | `CRAXDRT.Application.OpenReport(...)` in `frmDayclose` and `Module1` (16 calls incl. one in the dead form `frmSubFinancer`: day-end A/B/C, counter cashier, deposit detail) | [M] |
| Data source | Reports are designed against the **ODBC DSN `Lodge`** (readable in the few reports whose connection text is uncompressed: `DSN=Lodge;DBQ=...Lodge13-14.mdb;DriverId=25` (Access driver) and `DSN=Lodge;Trusted_Connection=Yes;DATABASE=Lodge2526` (SQL Server driver)). At start-up `Module1.CreateUserDSN` re-registers the DSN `Lodge` to the current year's Access file (`DBEngine.RegisterDatabase`) or SQL database (`SQLConfigDataSource`). So one `.rpt` runs on either engine. Most `.rpt` files compress their connection text, so I could not read it for all; **[G]** the same DSN applies | [M] 6 files read, [G] rest |
| Query text | **Table-based, not command text:** the code first fills **scratch/work tables** (`TempCounterCashierReport`, `TempFoodBillSubReport`, `TempTariffSalesDetail`, `TempDuplicateCustomerDetail`, `PrintBill`, `CheckInReport`, `GSTTaxReport`, `DayEndReportInDetail`, ...) and the report reads them. Some also read saved Access queries (the change log says "modify query RoomServedItemDetail"); no Crystal "SQL command" text was found | [M] code and DataModi.txt; [G] saved queries not seen because the supplied `.mdb` files were not opened for this task |
| Parameters | Not Crystal parameter fields. Values go as **formulas** (`OrgName`, dates, titles, flags) and a **record-selection formula** string built in VB (`{Table.Field} = ...`) | [M] |
| Formulas inside reports | Present (e.g. NETAmountNew, ContactNo, MailId added to SalesBill 22 Mar 2024) | [M] change log |
| Sub-reports | 24 of 182 files; 12 used (day-end A/B/C, DayEndDetail, GSTR-1 x3, SalesBill, SalesBillMultipleBillPrint, SalesBillFinalize) | [M] |
| Images in reports | Guest photo and ID proof are pushed into BLOB columns of scratch tables at print time (`CheckInReport.CustomerPhoto/IDProff/IDProof2ndPart`, `PhotoAddresProof.PhotoAndID`); `LogoAndQR.QRImage/LogoImage` for bill logo and UPI/QR | [M] |
| Export | `Module1.ExportReportToPDF` (RDC, `crEFTPortableDocFormat`, to `MailSending\`) used by day close to e-mail the day-end PDFs (CDO); **Excel** built through Office automation in 4 forms (`Excel.Application`: frmAccountReport, frmCouCaseesReportI, frmPurchaseReport, frmreport); the 64-bit packaging list also holds the Crystal Excel/Word/RTF export DLLs | [M] |
| Printing | Windows default printer through Crystal; separate `Printer.` object drawing (210 lines) for pay slips/salary advance only (not Crystal); `Printingsetting.txt` stores a page setting | [M] |
| Grouping, totals, running totals, page headers, section formatting | Standard Crystal features; not individually inspected (the files are binary OLE) | [G] |
| Layouts and paper | A4 and A5 and slip layouts (`SalesBill-A5 Report` is dormant; `LodgingBillReceipt` is a KOT-style slip). Hindi/Devanagari fields | [M] file names; [G] Hindi |

### 7.2 What the replacement (ETP Reporting Engine: OpenXML Excel + PDFsharp PDF) must support

Minimum for the 80 carried reports, driven by the findings above:

1. **One data contract per report** (query to rows) instead of "fill a scratch table, then read it": replace each `Temp*` table with a server query/view or a view model. This is where most of the work is, not the layout.
2. **Parameters** as typed values (date range, room, guest, bill range, user, flags) with the HCC range classes (D-DAY, D-S, D-B, D-F) from the catalogue, passed to the data query, not as a string selection formula.
3. **Page header and footer, repeating column heads, page X of Y**, group headers/footers with subtotals, grand totals, running totals, conditional line (zero rows suppress, bold totals).
4. **Sub-reports / master-detail** (day-end blocks, GSTR-1, bill with its tax and payment tables): nested tables in PDF; separate sheets or blocks in Excel.
5. **Numbers**: money in integer paise, shown `#,##0.00`; numbers in Excel as numbers, not text; amount in words on the bill (Lodge uses Crystal `ToWords`).
6. **Images**: logo, UPI QR code, guest photo and ID picture (permission gated, logged), signature line.
7. **Formats**: PDF (PDFsharp) and `.xlsx` (OpenXML) from one definition (catalogue rule 1.5); CSV for GSTR-1; Tally XML separately (ACC-3).
8. **Layout fidelity** for the legal documents (tax invoice, registration card, Form C, deposit receipt, GST tables): fixed-position templates matching the current paper size; A4 and A5 and 80 mm slip variants.
9. **Unicode text** (Devanagari names and address lines) with an embedded font in PDFsharp.
10. **Output control**: print to a named printer, e-mail attach, save to folder, scheduled run (catalogue 1.9), every print and export logged (catalogue rule V).
11. **Permissions and masking** (catalogue 1.2, 1.3, 1.6): money columns removed for roles without the money gate; ID numbers masked.
12. **Report designer is not needed**: the catalogue fixes the column lists, so a code-defined template per report is enough. Crystal Designer, `.rpt` files and the 8.5 runtime are retired.

---

## 8. Getting real usage evidence in a day (recommended before cutting anything)

1. Ask the Hotel Parth desk for a **screenshot of the Report menu per user level** (the screenshots show only one).
2. **Add a logging line** next to the 89 `mdimain.crpt.Action = 1` lines (or, less invasive, in one shared procedure if the report opening is first refactored) that appends `ReportName, user, date` to a text file; release it and read the file after 30 days. 3-6 h of VB6 work **[G]** because the 89 sites are spread over many forms.
3. Until then use the indirect signals in section 1; the catalogue's own review found 35 of 61 menu reports in use **[M, catalogue section 6]**.

---

## 9. Questions for the owner

1. Which day-end set does the CA actually receive (A, B or C), and in which layout?
2. Is IGST ever billed (company in another state)? If not, `GSTR1ReportIGST` is dropped.
3. Is the Report-menu variant shown on screen the full list the front desk uses, or are there other users with more items?
4. Which registration card layout should HCC print, with or without the ID picture?
5. May all 74 unused files be archived and ignored (they stay in the backup folders)?
