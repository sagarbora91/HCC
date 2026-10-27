namespace Hcc.Contracts.Accounting;

// ACC-1 · Opening balances entered by hand (02 Planning/agents/ACC1-POSTING-CONTRACT.md §5; rulebook §G2; DEC-139 / DEC-148).
// Prototype: 03 Prototype/src/domain/streams/accounting-opening.ts (ACC_OPENING_SAVE / _APPROVE / _REOPEN). Money = paise.

/// <summary>One opening line: a ledger code (<c>1500</c>) or a party ledger id (<c>1110/CMP-0007</c>); one side only.</summary>
public sealed record OpeningLineRequest(string? Account, long DebitPaise, long CreditPaise);

/// <summary>SaveOpeningBalances: replaces the draft. <see cref="AsOf"/> = the start of the business financial year (omitted = that date);
/// a 1400 line sent is ignored (the difference is recomputed).</summary>
public sealed record SaveOpeningBalancesRequest(DateOnly? AsOf, IReadOnlyList<OpeningLineRequest>? Lines, string? Note);

/// <summary>ApproveOpeningBalances (Owner, PERM-ACC-009). <see cref="ExpectedVersion"/> null = no stale check.</summary>
public sealed record ApproveOpeningBalancesRequest(string? ExpectedVersion);

/// <summary>ReopenOpeningBalances (Owner, PERM-ACC-009), only while no period lock exists on or after the entry's date.</summary>
public sealed record ReopenOpeningBalancesRequest(string? Reason, string? ExpectedVersion);

public sealed record OpeningBalancesResult(DateOnly AsOf, string Status, int Lines, long DebitPaise, long CreditPaise, long DifferencePaise, string Version);

public sealed record OpeningLineView(string Account, string Name, long DebitPaise, long CreditPaise);

/// <summary>The opening entry. <see cref="DifferencePaise"/> = the signed 1400 balance (debit positive).</summary>
public sealed record OpeningEntryView(DateOnly AsOf, string Status, IReadOnlyList<OpeningLineView> Lines, long DebitPaise, long CreditPaise, long DifferencePaise,
    int EnteredBy, DateTime EnteredUtc, int? ApprovedBy, DateTime? ApprovedUtc, string? Note, string Version);

/// <summary>
/// GET /accounting/opening: the business FY start, whether opening balances are still pending (no approved entry with 1400 = 0;
/// DEC-148 "opening balances pending"), the entry if any, and the first locked date on or after its date (reopen refused while set).
/// </summary>
public sealed record OpeningBalancesStateView(DateOnly FyStart, bool Pending, OpeningEntryView? Entry, DateOnly? LockedFrom);
