namespace Hcc.Contracts.Closing;

// Cash-book view (W3-9; prototype 03 Prototype/src/domain/streams/cashbook.ts cbDay/cbRange). Read-only: every figure is derived from the
// shared cash drawer (pay.cash_movement / pay.shift, DEC-052), never stored. Money integer paise; dates YYYY-MM-DD.

/// <summary>
/// One cash-book line = one drawer movement. <see cref="Group"/> is "receipt" (cash in) or "payment" (cash out, incl. banking, refunds, expenses,
/// outlet voids); the signed movement decides the side, so a reversed expense is a receipt.
/// </summary>
public sealed record CashBookEntry(long MovementId, string Kind, string Group, string Label, string Reference, string TillCode, long ShiftId,
    int PrincipalId, long InPaise, long OutPaise, DateTime RecordedUtc);

/// <summary>
/// One business day of the cash book. Book closing = opening + receipts − payments; carried = the cash counted at the last count of the day
/// (book closing when nothing is counted yet). <see cref="DifferencePaise"/> = Σ counted − expected over the day's counts (cash over/short);
/// carried = closing + difference. <see cref="Status"/>: Closed (business day closed) · Open (current business date) · Past (before HCC closes).
/// </summary>
public sealed record CashBookDayView(DateOnly BusinessDate, string Status, bool Locked, int? LockedBy, DateTime? LockedUtc,
    IReadOnlyList<CashBookEventView> History, long OpeningPaise, IReadOnlyList<CashBookEntry> Entries, long ReceiptsPaise, long PaymentsPaise,
    long BankedPaise, long ClosingPaise, long? ExpectedPaise, long? CountedPaise, long? DifferencePaise, string? DifferenceReason, int? CountedBy,
    int? ApprovedBy, long CarriedPaise, bool Reconciles);

/// <summary>
/// Cash book for an inclusive date range (only days with drawer activity, a closed day, or the current business date).
/// Always foots: opening + receipts − payments + differences + gaps = closing. <see cref="GapsPaise"/> = Σ (a day's opening − the previous
/// listed day's carry); zero when the days join up.
/// </summary>
public sealed record CashBookRangeView(DateOnly From, DateOnly To, DateOnly BusinessDate, IReadOnlyList<CashBookDayView> Days, long OpeningPaise,
    long ReceiptsPaise, long PaymentsPaise, long DifferencesPaise, long GapsPaise, long ClosingPaise, long BookClosingPaise, string Note);
