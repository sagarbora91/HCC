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
