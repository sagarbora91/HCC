namespace Hcc.Contracts.Billing;

// Cash leaving the drawer (Wave B1; DEC-073 bank deposit / till transfer / Owner drawing, DEC-078 shared till; audit A03b-01).
// Money integer paise; dates YYYY-MM-DD; versions = base64 rowversion of the request's bill.approval row (deposits and transfers).

/// <summary>
/// POST /api/v1/commands/RecordCashBanking (K). Cashier or Manager, from their OWN open shift on <c>TillId</c> (PERM-DRW-001, own shift):
/// a bank deposit of <c>AmountPaise</c> with the bank / account label, the deposit-slip number and the slip photo taken in the app
/// (<c>SlipImageBase64</c>: JPEG or PNG under 100 KB, compressed on the device; the server refuses anything else). Expected cash drops at
/// once (the cash has left the drawer); a different Manager or Owner then confirms or rejects it (ConfirmCashBanking).
/// </summary>
public sealed record RecordCashBankingRequest(int TillId, long AmountPaise, string Bank, string SlipNo, string SlipImageBase64, string SlipContentType,
    string? Note = null);

/// <summary>
/// POST /api/v1/commands/ConfirmCashBanking (K V): a DIFFERENT Manager or Owner than the recorder (PERM-DRW-002) confirms the deposit, or
/// rejects it with a reason — the cash then goes back into that till's open shift (audited reversal; an open shift on the till is needed).
/// Also reachable through DecideApproval (action cash-banking). <c>ExpectedVersion</c> = the request's approval version.
/// </summary>
public sealed record ConfirmCashBankingRequest(long TransferId, bool Confirm, string? Reason, string ExpectedVersion);

/// <summary>
/// POST /api/v1/commands/TransferTillCash (K): the giver moves cash from their OWN open shift on <c>FromTillId</c> to another till
/// (PERM-DRW-004, own shift). Pending until the receiver accepts it on their own open shift on <c>ToTillId</c>; the giver's expected drops now.
/// </summary>
public sealed record TransferTillCashRequest(int FromTillId, int ToTillId, long AmountPaise, string? Reason = null);

/// <summary>
/// POST /api/v1/commands/AcceptTillTransfer (K V): the receiver accepts a pending transfer on their OWN open shift on the receiving till
/// (receiver's expected rises), or rejects it with a reason (the receiver, or a Manager / Owner holding PERM-DRW-002): the cash goes back
/// into the giver till's open shift. Never the giver. Also reachable through DecideApproval (action till-transfer).
/// </summary>
public sealed record AcceptTillTransferRequest(long TransferId, bool Accept, string? Reason, string ExpectedVersion);

/// <summary>POST /api/v1/commands/RecordOwnerDrawing (K): Owner only (PERM-DRW-005), from an open shift on <c>TillId</c>, reason required.</summary>
public sealed record RecordOwnerDrawingRequest(int TillId, long AmountPaise, string Reason);

/// <summary>Result of the cash-out commands. <c>Version</c> = the approval version (deposits and transfers) or the record version (drawings).</summary>
public sealed record CashTransferResult(long TransferId, string Kind, string Status, long AmountPaise, DateOnly BusinessDate, int FromTillId, int? ToTillId,
    long? ApprovalId, string Version);

/// <summary>
/// One bank deposit, till transfer or Owner drawing (GET /api/v1/cashier/transfers). <c>Kind</c> banking | till | drawing; <c>Status</c>
/// Pending | Confirmed | Accepted | Rejected | Posted. <c>DecidedDate</c> = the business date of the decision (a rejection's return and an
/// acceptance are dated it). <c>SlipStored</c> = the photo is still kept; <c>SlipNote</c> = "Slip photo purged after 600 days." once purged.
/// </summary>
public sealed record CashTransferView(long TransferId, string Kind, string Status, DateOnly BusinessDate, long AmountPaise, int FromTillId, string FromTillCode,
    int? ToTillId, string? ToTillCode, string? Bank, string? SlipNo, string? Reason, int RecordedBy, DateTime RecordedUtc, int? DecidedBy, DateTime? DecidedUtc,
    DateOnly? DecidedDate, string? DecisionReason, bool SlipStored, string? SlipNote, long? ApprovalId, string Version);

/// <summary>
/// GET /api/v1/cashier/transfers?from&amp;to (PERM-SHF-001 any shift, or PERM-DRW-003): every cash-out record dated in the range (at most 93 days)
/// plus every still-pending one, newest first.
/// </summary>
public sealed record CashTransferListView(DateOnly BusinessDate, IReadOnlyList<CashTransferView> Items);

/// <summary>GET /api/v1/cashier/transfers/{id}/slip (PERM-DRW-003: Manager, Owner, Accountant read-only): the stored slip photo.</summary>
public sealed record BankingSlipView(long TransferId, string ContentType, string ImageBase64);
