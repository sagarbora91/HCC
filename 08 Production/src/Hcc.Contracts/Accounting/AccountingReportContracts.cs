namespace Hcc.Contracts.Accounting;

// ACC-1 lane acc1-statements: the report views of 02 Planning/agents/ACC1-POSTING-CONTRACT.md §6 (prototype ACCStatement, ACCTB6,
// ACCDayBook in 03 Prototype/src/domain/streams/accounting-reports.ts). Same fields; money fields carry the server's "Paise" suffix
// (prototype `opening` = OpeningPaise, `debit` = DebitPaise, `running` = RunningPaise …). Signed balances: Dr positive / Cr negative.

/// <summary>One voucher of a ledger / party statement (rulebook §7.2). <see cref="Counter"/> = the other side's ledger name(s).</summary>
public sealed record StatementRowView(DateOnly Date, string No, string Type, string Narration, string Counter, long DebitPaise, long CreditPaise, long RunningPaise);

/// <summary>Ledger / party statement for [<see cref="From"/>, <see cref="To"/>]: opening = every voucher before <see cref="From"/>
/// (the opening entry included), closing = opening + period; both signed as <see cref="StatementRowView.RunningPaise"/>.</summary>
public sealed record StatementView(string Account, string Name, DateOnly From, DateOnly To, long OpeningPaise, long DebitPaise, long CreditPaise, long ClosingPaise,
    IReadOnlyList<StatementRowView> Rows);

/// <summary>One row of the six-column trial balance (rulebook §7.3). <see cref="Control"/> is set on a party row (detail).</summary>
public sealed record TrialBalance6RowView(string Account, string Name, string Group, string? Control, long OpeningDebitPaise, long OpeningCreditPaise,
    long DebitPaise, long CreditPaise, long ClosingDebitPaise, long ClosingCreditPaise);

public sealed record TrialBalance6TotalsView(long OpeningDebitPaise, long OpeningCreditPaise, long DebitPaise, long CreditPaise, long ClosingDebitPaise, long ClosingCreditPaise);

/// <summary>Six-column trial balance. <see cref="Detail"/> false = parties summed into their control; true = a row per party.
/// <see cref="OpeningPending"/> = no approved opening entry, or its 1400 Opening-balance difference is not zero (rulebook §G2).</summary>
public sealed record TrialBalance6View(DateOnly From, DateOnly To, bool Detail, IReadOnlyList<TrialBalance6RowView> Rows, TrialBalance6TotalsView Totals,
    bool Balanced, bool OpeningPending);

/// <summary>One voucher entry of the day book (rulebook §7.1).</summary>
public sealed record DayBookRowView(DateOnly Date, string No, string Type, string Narration, string Account, string Ledger, long DebitPaise, long CreditPaise, string Source);

/// <summary>Day book: every voucher in the range in date, number order, one row per entry.</summary>
public sealed record DayBookView(DateOnly From, DateOnly To, IReadOnlyList<DayBookRowView> Rows, int Vouchers, long DebitPaise, long CreditPaise, bool Balanced);

// ACC-3 lane acc3-pl: 02 Planning/agents/ACC3-REPORTS-CONTRACT.md §2 (prototype ACCPl, ACCBalanceSheet, cash ratio in
// 03 Prototype/src/domain/streams/accounting-pl.ts). Same fields, PascalCase; money in paise ("Paise" suffix on scalar money fields).

/// <summary>One P&L line. <see cref="Paise"/> signed as the ledger's balance movement (income positive, expense positive as a cost).</summary>
public sealed record PlLineView(string Code, string Name, long Paise, string Group);

public sealed record PlSectionView(IReadOnlyList<PlLineView> Lines, long Total);

public sealed record PlGroupView(string Name, IReadOnlyList<PlLineView> Lines, long Total);

public sealed record PlExpensesView(PlGroupView Consumption, PlGroupView Employee, PlGroupView Finance, PlGroupView Depreciation, PlGroupView Other, long Total);

public sealed record PlTallyGroupView(string Group, string Name, long Paise);

/// <summary>Profit &amp; loss, ICAI vertical (rulebook §7.4) plus the Tally-group view. <see cref="Warnings"/> names ledgers with no bucket.</summary>
public sealed record PlView(DateOnly From, DateOnly To, bool Provisional, PlSectionView Revenue, PlSectionView OtherIncome, PlExpensesView Expenses,
    long ProfitBeforeTax, IReadOnlyList<PlTallyGroupView> ByTallyGroup, IReadOnlyList<string>? Warnings = null);

public sealed record BsLineView(string Code, string Name, long Paise);

public sealed record BsBlockView(string Name, IReadOnlyList<BsLineView> Lines, long Total);

public sealed record BsLiabilitiesView(BsBlockView OwnersFunds, BsBlockView NonCurrent, BsBlockView Current, long Total);

public sealed record BsAssetsView(BsBlockView NonCurrent, BsBlockView Current, long Total);

/// <summary>Balance sheet (rulebook §7.5) — an HCC view, not the statutory statement.</summary>
public sealed record BalanceSheetView(DateOnly AsOf, bool Provisional, BsLiabilitiesView Liabilities, BsAssetsView Assets, bool Balanced, long Suspense, long OpeningDifference);

/// <summary>Cash-ratio report (rulebook §7.9). Pct fields in basis points; <see cref="Flagged"/> when either &gt; 500.</summary>
public sealed record CashRatioView(string Fy, long CashReceipts, long TotalReceipts, long CashPayments, long TotalPayments, int ReceiptsPct, int PaymentsPct, bool Flagged);

// ACC-3 lane acc3-ageing: 02 Planning/agents/ACC3-REPORTS-CONTRACT.md §3 (prototype ACCAgeing, ACCGstSummary in
// 03 Prototype/src/domain/streams/accounting-ageing.ts, accounting-gst-summary.ts). Same fields, PascalCase; money in paise.

/// <summary>One open bill of a party (ref = the voucher's source key; <c>opening</c> = the opening balance, DEC-172). Due = the bill date (no terms master).</summary>
public sealed record AgeingBillView(string Ref, DateOnly Date, DateOnly Due, long Amount, long Paid, long Open, int Days);

/// <summary>One party row: open = the party's ledger balance on asOf (signed to the side); buckets from the open bills. <see cref="Flagged"/> when Σ buckets ≠ open.</summary>
public sealed record AgeingRowView(string Account, string Party, string Name, string Control, IReadOnlyList<AgeingBillView> Bills, long Open,
    long B0_30, long B31_60, long B61_90, long B90, bool Flagged = false, string? Note = null, long Advance = 0);

public sealed record AgeingTotalsView(long Open, long B0_30, long B31_60, long B61_90, long B90);

/// <summary>GET /accounting/ageing?asOf&amp;side=receivable|payable (AccountingPermissions.View, as the trial balance) — outstanding with ageing (rulebook §7.6).</summary>
public sealed record AgeingView(DateOnly AsOf, string Side, IReadOnlyList<AgeingRowView> Rows, AgeingTotalsView Totals);

/// <summary>Output GST (tax paise; <see cref="GstSummaryView.OutputTaxable"/> = the taxable value on the same keys) by rate and GSTR-1 section.</summary>
public sealed record GstSummaryOutputView(long Rate5, long Rate18, long B2b, long B2cs, long Advances11A, long Advances11B, long CreditNotes);

public sealed record GstSummarySetoffView(GstHeadsView Output, GstHeadsView Credit, GstHeadsView Cash, GstHeadsView CreditLeft, long Payable, long CashAvailable, long Challan);

public sealed record GstSummaryTiesView(bool Gstr1, bool Gstr3b, IReadOnlyList<string> Differences);

/// <summary>GET /accounting/gst/summary?month=YYYY-MM (AccountingPermissions.View, as the trial balance) — GST summary (rulebook §7.7) tied to GSTR-1 and the GST month.</summary>
public sealed record GstSummaryView(string Month, GstSummaryOutputView Output, GstSummaryOutputView OutputTaxable, GstHeadsView Itc, long Rcm, GstSummarySetoffView Setoff,
    long CashPaid, long TcsCredit, long CashLedgerBalance, GstHeadsView BookOutput, GstSummaryTiesView Ties);
