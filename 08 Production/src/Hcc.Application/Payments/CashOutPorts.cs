using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts.Billing;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;

namespace Hcc.Application.Payments;

/// <summary>
/// PERM ids of cash leaving the drawer (migration 3101; DEC-073). Own-shift grants (scope S) are checked against the shift through the
/// shift scope resolver: "own shift" = the person who opened it (DEC-078).
/// </summary>
public static class CashOutPermissions
{
    /// <summary>Record a bank deposit from your own open shift: Cashier, Manager (S).</summary>
    public const string BankingRecord = "PERM-DRW-001";

    /// <summary>Confirm or reject a bank deposit someone else recorded: Owner, Manager (P).</summary>
    public const string BankingConfirm = "PERM-DRW-002";

    /// <summary>View the deposit-slip photo: Owner, Manager (A), Accountant (V, read-only).</summary>
    public const string SlipView = "PERM-DRW-003";

    /// <summary>Give a till transfer from, or accept one on, your own open shift: Owner, Manager, Reception, Cashier (S).</summary>
    public const string TillTransfer = "PERM-DRW-004";

    /// <summary>Owner drawing from an open shift: Owner only (P).</summary>
    public const string OwnerDrawing = "PERM-DRW-005";
}

/// <summary>One row of <c>pay.cash_transfer</c> (migration 3100).</summary>
public sealed record CashTransferRow(long TransferId, int PropertyId, CashOutKind Kind, string Status, DateOnly BusinessDate, Paise Amount, int FromTillId,
    long FromShiftId, int? ToTillId, long? ToShiftId, string? Bank, string? SlipNo, string? Reason, long? ApprovalId, int RecordedBy, DateTime RecordedUtc,
    int? DecidedBy, DateTime? DecidedUtc, DateOnly? DecidedDate, string? DecisionReason, DateTime? SlipPurgedUtc, bool SlipStored, string Version);

public sealed record NewCashTransfer(int PropertyId, CashOutKind Kind, string Status, DateOnly BusinessDate, Paise Amount, int FromTillId, long FromShiftId,
    int? ToTillId, string? Bank, string? SlipNo, string? Reason, int RecordedBy, DateTime RecordedUtc);

/// <summary>The decision on a pending deposit or transfer (<paramref name="ToShiftId"/> = the receiving shift of an accepted transfer).</summary>
public sealed record CashTransferDecision(string Status, int DecidedBy, DateTime DecidedUtc, DateOnly DecidedDate, string? Reason, long? ToShiftId);

/// <summary>A listed record with its till codes and the version of its approval request (null for drawings).</summary>
public sealed record CashTransferListRow(CashTransferRow Row, string FromTillCode, string? ToTillCode, string? ApprovalVersion);

/// <summary>Persistence of <c>pay.cash_transfer</c> and <c>pay.banking_slip</c> (SQL adapter: SqlCashOutStore). Writes run in the command transaction.</summary>
public interface ICashOutStore
{
    Task<(long TransferId, string Version)> InsertAsync(ICommandTransaction tx, NewCashTransfer transfer, CancellationToken ct);

    Task SetApprovalAsync(ICommandTransaction tx, long transferId, long approvalId, CancellationToken ct);

    Task InsertSlipAsync(ICommandTransaction tx, long transferId, string contentType, byte[] image, CancellationToken ct);

    /// <summary>Read without a lock (to find the tills to lock first: lock order till → shift → transfer → approval).</summary>
    Task<CashTransferRow?> PeekAsync(ICommandTransaction tx, long transferId, CancellationToken ct);

    /// <summary>UPDLOCK on the record.</summary>
    Task<CashTransferRow?> LockAsync(ICommandTransaction tx, long transferId, CancellationToken ct);

    Task DecideAsync(ICommandTransaction tx, long transferId, CashTransferDecision decision, CancellationToken ct);

    /// <summary>Records of the property dated in [from, to] plus every pending one (own read-committed connection).</summary>
    Task<IReadOnlyList<CashTransferListRow>> ListAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>The stored slip photo, or null when there is none (purged after 600 days, or not a deposit).</summary>
    Task<(string ContentType, byte[] Image)?> SlipAsync(int propertyId, long transferId, CancellationToken ct);
}

/// <summary>
/// The decision side of a deposit or transfer request, reachable from DecideApproval (bill.approval actions cash-banking / till-transfer)
/// inside that command's transaction. Implemented by <see cref="CashOutService"/>.
/// </summary>
public interface ICashOutDecisions
{
    Task<ApprovalResult> DecideAsync(ICommandTransaction tx, Caller caller, ApprovalRow request, bool approve, string reason, string expectedVersion, CancellationToken ct);
}
