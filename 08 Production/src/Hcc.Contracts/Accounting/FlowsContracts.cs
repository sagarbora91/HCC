namespace Hcc.Contracts.Accounting;

// ACC-2 flows (02 Planning/agents/ACC2-POSTING-CONTRACT.md §4, §7, §8): requests, results and events of the commands
// ApplyDeposit, SettleFolioToOta, RecordOtaPayout, ReverseUdhaarPayment and SetCompanyActive (Hcc.Api OtaPayoutEndpoints).

/// <summary>A6a: apply the deposit held to the folio at check-out (PERM-PAY-001 on the till's open shift). <c>ExpectedVersion</c> = the folio version seen.</summary>
public sealed record ApplyDepositRequest(long FolioId, long AmountPaise, int TillId, string? ExpectedVersion = null);

/// <summary>A13: move the in-house folio's whole balance to the OTA's receivable (receipt kind ota-credit) before CheckoutStay; the stay's
/// reservation must come from a connected channel (ota.message New → reservation).</summary>
public sealed record SettleFolioToOtaRequest(long FolioId, string? ExpectedVersion = null);

public sealed record FolioFlowResult(long ReceiptId, long FolioId, string Kind, long AmountPaise, long DepositHeldPaise, long BalancePaise, string FolioVersion, int? ChannelId = null);

/// <summary>A13 / DEC-150: a payout from the channel's statement (PERM-OTA-004). <c>BankAccount</c> = the bank account code (mst.bank_account, kind bank).</summary>
public sealed record RecordOtaPayoutRequest(int ChannelId, string StatementRef, long GrossPaise, long CommissionPaise, long CommissionGstPaise, long TcsPaise, long NetPaise,
    string BankAccount, IReadOnlyList<long>? ReservationIds = null);

public sealed record OtaPayoutResult(long PayoutId, int ChannelId, long GrossPaise, long OpenBeforePaise, long OpenAfterPaise);

/// <summary>GET /ota/payouts (PERM-OTA-004).</summary>
public sealed record OtaPayoutView(long PayoutId, int ChannelId, DateOnly BusinessDate, string StatementRef, long GrossPaise, long CommissionPaise, long CommissionGstPaise,
    long TcsPaise, long NetPaise, string BankAccount, IReadOnlyList<long> ReservationIds, int RecordedBy, DateTime RecordedUtc);

/// <summary>A17b: the Owner reverses a mistaken Udhaar desk payment; cash leaves the drawer of <c>TillId</c> (open shift).</summary>
public sealed record ReverseUdhaarPaymentRequest(long ReceivableId, long SettlementId, string Reason, int? TillId = null);

public sealed record UdhaarReversalResult(long ReversalId, long SettlementId, long ReceivableId, long AmountPaise, long OutstandingPaise, string Message);

/// <summary>Contract §8 / DEC-157: retire (false) or reactivate (true) a company account (PERM-GRP-002).</summary>
public sealed record SetCompanyActiveRequest(long CompanyId, bool Active, string? Reason = null);

public sealed record CompanyActiveResult(long CompanyId, bool Active, string Message);

/// <summary>Outbox: an OTA payout recorded (Accounting books SourceRules.OtaPayout; key <c>ota-payout:&lt;payoutId&gt;</c>). <c>ChannelCode</c> = the 1115 party
/// (ota.channel.code), <c>BankAccount</c> = the 1030 party (mst.bank_account.code).</summary>
public sealed record OtaPayoutRecordedEvent(long PayoutId, int ChannelId, string ChannelCode, string Channel, DateOnly BusinessDate, string StatementRef, long GrossPaise, long CommissionPaise,
    long CommissionGstPaise, long TcsPaise, long NetPaise, string BankAccount, bool ItcAllowed, int Bookings);

/// <summary>Outbox: a mistaken Udhaar payment reversed (A17b; Accounting books the negative Receipt, key <c>udhaar-payment:&lt;reversal id&gt;</c>).
/// <c>AmountPaise</c> is the positive amount reversed.</summary>
public sealed record UdhaarPaymentReversedEvent(long ReceivableId, long SettlementId, long ReversalOf, DateOnly BusinessDate, string Tender, long AmountPaise, string Customer,
    string Reason, long? CashMovementId = null);
