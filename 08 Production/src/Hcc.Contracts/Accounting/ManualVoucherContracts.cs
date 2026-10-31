namespace Hcc.Contracts.Accounting;

// ACC-2 · Manual vouchers and loan party ledgers (02 Planning/agents/ACC2-POSTING-CONTRACT.md §2, §9; rulebook §1 principle 2, B5–B7, B11, A18).
// Prototype: 03 Prototype/src/domain/streams/accounting-manual.ts (ACC_MANUAL_SAVE / _DECIDE / _VOID, ACC_LOAN_ADD). Money = paise.

/// <summary>One manual line: a ledger code (<c>5080</c>) or a party ledger id (<c>1030/BANK-1</c>, <c>2400/LN-HDFC</c>); one side only.</summary>
public sealed record ManualLineRequest(string? Account, long DebitPaise, long CreditPaise, string? Note = null);

/// <summary>
/// SaveManualVoucher (PERM-ACC-010: Owner → approved at once; Accountant → pending). <see cref="VoucherId"/> set = replace a pending / rejected
/// voucher (its maker or the Owner). <see cref="VoucherType"/> = Journal | Contra | Payment | Receipt (coordinator ruling 28 Oct: never <c>type</c>).
/// </summary>
public sealed record SaveManualVoucherRequest(long? VoucherId, DateOnly? Date, string? VoucherType, string? Narration, string? Reference, string? Template,
    IReadOnlyList<ManualLineRequest>? Lines, string? ExpectedVersion = null);

/// <summary>DecideManualVoucher (Owner, PERM-ACC-011): approve (every guard re-run) or reject with a reason.</summary>
public sealed record DecideManualVoucherRequest(long VoucherId, bool Approve, string? Reason, string? ExpectedVersion = null);

/// <summary>VoidManualVoucher (Owner, PERM-ACC-011): unlocked day → void (posts nothing); locked day → a reversing voucher dated the business date.</summary>
public sealed record VoidManualVoucherRequest(long VoucherId, string? Reason, string? ExpectedVersion = null);

/// <summary>The voucher after a command; <see cref="ReversalId"/> = the reversing voucher a void on a locked day created.</summary>
public sealed record ManualVoucherResult(long VoucherId, string Status, DateOnly Date, long? ReversalId, string Version);

/// <summary>AddLoan (Owner, PERM-ACC-007): a party of control 2400, account <c>2400/&lt;CODE&gt;</c> (e.g. <c>2400/LN-HDFC</c>);
/// <see cref="Kind"/> = secured | unsecured picks the group secured-loans / unsecured-loans (coordinator ruling 28 Oct; null = secured).</summary>
public sealed record AddLoanRequest(string? Code, string? Name, string? Lender, string? AccountLast4, string? Note, string? Kind = null);

public sealed record LoanResult(string Account, string Name);

public sealed record ManualLineView(string Account, string Name, long DebitPaise, long CreditPaise, string? Note);

public sealed record ManualVoucherView(long VoucherId, DateOnly Date, string Type, string Narration, string? Reference, string? Template, string Status,
    IReadOnlyList<ManualLineView> Lines, int EnteredBy, DateTime EnteredUtc, int? DecidedBy, DateTime? DecidedUtc, string? Reason, long? VoidOf, int? VoidedBy,
    DateOnly? BookedOn, string Version);

public sealed record LoanView(string Account, string Name, string Lender, string? AccountLast4, bool Active, string? Note, string Kind);

/// <summary>GET /accounting/manual: the property's manual vouchers (newest first) and its loan party ledgers.</summary>
public sealed record ManualVouchersView(IReadOnlyList<ManualVoucherView> Vouchers, IReadOnlyList<LoanView> Loans);

/// <summary>One line of a manual voucher in an outbox payload.</summary>
public sealed record ManualLinePayload(string Account, long DebitPaise, long CreditPaise);

/// <summary>
/// <see cref="AccountingEvents.ManualVoucherApproved"/>: a manual voucher counts in the books from now (the Owner's own save, an approval,
/// or the reversing voucher of a void on a locked day — <see cref="VoidOf"/> set). Booked as the source <c>manual:&lt;id&gt;</c> on <see cref="Date"/>.
/// </summary>
public sealed record ManualVoucherApprovedEvent(long VoucherId, DateOnly Date, string Type, string Narration, string? Reference, string? Template,
    IReadOnlyList<ManualLinePayload> Lines, long? VoidOf = null);

/// <summary>
/// <see cref="AccountingEvents.ManualVoucherVoided"/>: a manual voucher was voided. <see cref="ReversalId"/> null = an unlocked day (its source
/// stops counting, nothing is booked); set = a locked day (the reversal is booked by its own ManualVoucherApproved).
/// </summary>
public sealed record ManualVoucherVoidedEvent(long VoucherId, DateOnly Date, string Reason, long? ReversalId);
