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
