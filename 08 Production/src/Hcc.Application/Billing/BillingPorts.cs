using Hcc.Application.Commands;
using Hcc.Domain.Billing;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;

namespace Hcc.Application.Billing;

/// <summary>PERM ids used by billing and cashier (RBAC matrix §5; contracts §3).</summary>
public static class BillingPermissions
{
    public const string FolioView = "PERM-FOL-001";
    public const string ChargePost = "PERM-FOL-002";
    public const string Discount = "PERM-FOL-003";
    public const string Correction = "PERM-FOL-005";
    public const string PaymentRecord = "PERM-PAY-001";
    public const string DepositRecord = "PERM-PAY-002";
    public const string RefundRequest = "PERM-PAY-003";
    public const string RefundExecute = "PERM-PAY-004";
    public const string CompanyCreditAuthorise = "PERM-PAY-005";
    public const string ShiftCount = "PERM-SHF-001";
    public const string ShiftDifferenceApprove = "PERM-SHF-002";
    public const string NightPost = "PERM-CLS-002";
    public const string ApprovalDecide = "PERM-APR-001";
    public const string Checkout = "PERM-STY-003";
    public const string FinanceReport = "PERM-RPT-002";
    public const string Configuration = "PERM-ADM-003";

    /// <summary>Scope kind for "own cash shift" grants (RBAC legend S).</summary>
    public const string ShiftScope = "shift";
}

/// <summary>The signed-in caller (from the server session, never the request body).</summary>
public sealed record Caller(int PropertyId, int PrincipalId);

/// <summary>
/// The night to charge for a stay: its folio and the agreed nightly price (per unit). The GST rate is resolved at posting from the
/// dated master on that price (DEC-069; <see cref="Hcc.Application.Tax.IGstRateResolver"/>), never from a booking-time estimate.
/// </summary>
public sealed record StayNight(long FolioId, Paise Rate);

/// <summary>
/// Integration port to stays (P3) and the agreed nightly rate (P2 reservation nights). Implemented by <see cref="StayNightSource"/>
/// (BillingIntegration.cs); billing unit tests use a fake.
/// </summary>
public interface IStayNightSource
{
    /// <summary>Resolves the stay's folio and the agreed rate for <paramref name="serviceDate"/> inside the command transaction; NOT_FOUND for an unknown stay.</summary>
    Task<StayNight> ResolveAsync(ICommandTransaction tx, int propertyId, long stayId, DateOnly serviceDate, CancellationToken ct);
}

// DEC-069: the synthetic ITaxRateSource / FixedTaxRateSource (DEC-039 10 %) were replaced by Hcc.Application.Tax.IGstRateResolver.

/// <summary>
/// Shared-till port (DEC-052) for other modules that move drawer cash: POS cash sales/voids, drawer-funded expenses, transfers.
/// Called inside the caller's own command transaction after it has authorized its command; requires an open shift on the till.
/// </summary>
public interface ICashDrawerPort
{
    Task<long> RecordMovementAsync(ICommandTransaction tx, int tillId, CashMovementKind kind, string sourceType, long sourceId, Paise amount,
        string label, string reference, CancellationToken ct = default);
}

// ── Store port (implemented by Hcc.Infrastructure.SqlServer.Billing.SqlBillingStore) ──

public sealed record FolioHeader(long FolioId, int PropertyId, long ReservationId, string ReservationStatus, DateOnly Arrival, DateOnly Departure,
    DateOnly BusinessDate, string Version)
{
    public bool IsOpen => ReservationStatus is "Confirmed" or "InHouse";
}

public sealed record NewCharge(long FolioId, ChargeKind Kind, DateOnly ServiceDate, string Label, Paise Amount, int TaxBps, Paise Tax,
    long? ReversesChargeId, long? ApprovalId, int PrincipalId, DateTime PostedUtc);

public sealed record NewReceipt(long FolioId, ReceiptKind Kind, string Tender, Paise Amount, long? RefundOf, DateOnly BusinessDate, int? TillId,
    long? ShiftId, string Reference, long? ApprovalId, int PrincipalId, DateTime RecordedUtc);

public sealed record ReceiptLock(long FolioId, PostedReceipt Receipt);

public sealed record ApprovalRow(long ApprovalId, int PropertyId, AdjustmentAction Action, long? FolioId, long? ShiftId, string TargetKind,
    long TargetId, Paise Amount, string? TargetVersion, Paise? Expected, Paise? Counted, string Reason, ApprovalStatus Status, int RequestedBy,
    int? DecidedBy, DateTime ExpiresUtc, long? ResultId, string Version);

public sealed record NewApproval(int PropertyId, AdjustmentAction Action, long? FolioId, long? ShiftId, string TargetKind, long TargetId, Paise Amount,
    string? TargetVersion, Paise? Expected, Paise? Counted, string Reason, int RequestedBy, DateTime RequestedUtc, DateTime ExpiresUtc);

public sealed record ApprovalChange(ApprovalStatus Status, int? DecidedBy, DateTime? DecidedUtc, string? DecisionReason, int? ExecutedBy,
    DateTime? ExecutedUtc, long? ResultId);

public sealed record TillRow(int TillId, int PropertyId, string Code, string Name, string Version);

public sealed record ShiftRow(long ShiftId, int TillId, int PropertyId, DateOnly BusinessDate, string Status, Paise OpeningFloat, Paise? Expected,
    Paise? Counted, int? SubmittedBy, long? ApprovalId, string Version);

public sealed record ShiftUpdate(string Status, Paise? Expected, Paise? Counted, int? SubmittedBy, DateTime? SubmittedUtc, string? Reason,
    long? ApprovalId, int? ClosedBy, DateTime? ClosedUtc);

public sealed record NewMovement(long ShiftId, int TillId, DateOnly BusinessDate, CashMovementKind Kind, string SourceType, long SourceId, Paise Amount,
    string Label, string Reference, int PrincipalId, DateTime RecordedUtc);

public sealed record MovementRow(long MovementId, CashMovementKind Kind, string SourceType, long SourceId, Paise Amount, string Label, string Reference,
    DateOnly BusinessDate);

public sealed record FolioSnapshot(FolioHeader Header, IReadOnlyList<PostedLine> Lines, IReadOnlyList<PostedReceipt> Receipts, IReadOnlyList<ApprovalRow> Approvals);

/// <summary>
/// Persistence for billing and cashier. Methods taking an <see cref="ICommandTransaction"/> run inside the command transaction;
/// <c>Lock*</c> methods take UPDLOCK (+ HOLDLOCK where a range matters). Lock order everywhere: folio → approval → receipt → till → shift.
/// </summary>
public interface IBillingStore
{
    Task<FolioHeader?> LockFolioAsync(ICommandTransaction tx, long folioId, CancellationToken ct);

    /// <summary>The reservation's folio id, creating the folio (UPDLOCK + HOLDLOCK on the business key) when none exists yet.</summary>
    Task<long> EnsureFolioAsync(ICommandTransaction tx, long reservationId, CancellationToken ct);

    /// <summary>The reservation's folio id, or null when it has none.</summary>
    Task<long?> FolioIdForReservationAsync(ICommandTransaction tx, long reservationId, CancellationToken ct);

    /// <summary>The stay's reservation and folio (P3 gst.stay) inside the command transaction; null when the stay is unknown at the property.</summary>
    Task<(long ReservationId, long FolioId)?> StayFolioAsync(ICommandTransaction tx, int propertyId, long stayId, CancellationToken ct);

    /// <summary>The reservation's booked nightly rate (P2 <c>inv.reservation.nightly_rate_paise</c>), the fallback when no agreed night snapshot exists.</summary>
    Task<Paise?> ReservationNightlyRateAsync(ICommandTransaction tx, long reservationId, CancellationToken ct);

    /// <summary>Bumps the folio's version (any line or receipt change invalidates approvals bound to the old one, DEC-041).</summary>
    Task<string> TouchFolioAsync(ICommandTransaction tx, long folioId, CancellationToken ct);

    Task<IReadOnlyList<PostedLine>> LinesAsync(ICommandTransaction tx, long folioId, CancellationToken ct);

    Task<IReadOnlyList<PostedReceipt>> ReceiptsAsync(ICommandTransaction tx, long folioId, CancellationToken ct);

    Task<IReadOnlyList<ApprovalRow>> ApprovalsForFolioAsync(ICommandTransaction tx, long folioId, CancellationToken ct);

    /// <summary>The canonical room-night charge for a folio and date, read under UPDLOCK + HOLDLOCK (spike T07).</summary>
    Task<PostedLine?> FindRoomNightAsync(ICommandTransaction tx, long folioId, DateOnly serviceDate, CancellationToken ct);

    Task<long> InsertChargeAsync(ICommandTransaction tx, NewCharge charge, CancellationToken ct);

    Task<long> InsertReceiptAsync(ICommandTransaction tx, NewReceipt receipt, CancellationToken ct);

    /// <summary>The parent receipt under UPDLOCK + HOLDLOCK, so competing refunds serialize on it (spike T06/C3).</summary>
    Task<ReceiptLock?> LockReceiptAsync(ICommandTransaction tx, long receiptId, CancellationToken ct);

    Task<Paise> RefundedAsync(ICommandTransaction tx, long receiptId, CancellationToken ct);

    Task<(long ApprovalId, string Version)> InsertApprovalAsync(ICommandTransaction tx, NewApproval approval, CancellationToken ct);

    Task<ApprovalRow?> PeekApprovalAsync(ICommandTransaction tx, long approvalId, CancellationToken ct);

    Task<ApprovalRow?> LockApprovalAsync(ICommandTransaction tx, long approvalId, CancellationToken ct);

    /// <summary>Versioned update; STALE_VERSION when <paramref name="expectedVersion"/> no longer matches.</summary>
    Task<string> UpdateApprovalAsync(ICommandTransaction tx, long approvalId, string expectedVersion, ApprovalChange change, CancellationToken ct);

    Task<DateOnly?> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);

    Task<bool> TillCodeExistsAsync(ICommandTransaction tx, int propertyId, string code, CancellationToken ct);

    Task<TillRow> InsertTillAsync(ICommandTransaction tx, int propertyId, string code, string name, int principalId, DateTime createdUtc, CancellationToken ct);

    Task<TillRow?> LockTillAsync(ICommandTransaction tx, int tillId, CancellationToken ct);

    Task<ShiftRow?> ActiveShiftAsync(ICommandTransaction tx, int tillId, CancellationToken ct);

    Task<ShiftRow?> LastClosedShiftAsync(ICommandTransaction tx, int tillId, CancellationToken ct);

    Task<ShiftRow?> PeekShiftAsync(ICommandTransaction tx, long shiftId, CancellationToken ct);

    Task<ShiftRow?> LockShiftAsync(ICommandTransaction tx, long shiftId, CancellationToken ct);

    Task<ShiftRow> InsertShiftAsync(ICommandTransaction tx, int tillId, DateOnly businessDate, Paise openingFloat, long? previousShiftId, int principalId,
        DateTime openedUtc, CancellationToken ct);

    Task<ShiftRow> UpdateShiftAsync(ICommandTransaction tx, long shiftId, string expectedVersion, ShiftUpdate update, CancellationToken ct);

    Task<long> InsertMovementAsync(ICommandTransaction tx, NewMovement movement, CancellationToken ct);

    Task<IReadOnlyList<MovementRow>> MovementsAsync(ICommandTransaction tx, long shiftId, CancellationToken ct);

    Task InsertShiftCountAsync(ICommandTransaction tx, long shiftId, Paise expected, Paise counted, string? denominationsJson, string? reason,
        long? approvalId, int principalId, DateTime submittedUtc, CancellationToken ct);

    // Queries (own read-committed connection).
    Task<FolioSnapshot?> ReadFolioAsync(long folioId, CancellationToken ct);

    Task<(ShiftRow Shift, IReadOnlyList<MovementRow> Movements)?> ReadShiftAsync(long shiftId, CancellationToken ct);

    Task<IReadOnlyList<PostedReceipt>> ReceiptsOnAsync(int propertyId, DateOnly businessDate, CancellationToken ct);

    /// <summary>W4-3: the reservation's folio id and stay (id, version), or null when the reservation is not on the property.</summary>
    Task<ReservationFolioRef?> FindReservationFolioAsync(int propertyId, long reservationId, CancellationToken ct);

    /// <summary>W4-3: every till of the property with its active shift and last closed shift (cash desk read).</summary>
    Task<CashDeskRead> ReadCashDeskAsync(int propertyId, CancellationToken ct);
}

public sealed record ReservationFolioRef(long ReservationId, long? FolioId, long? StayId, string? StayVersion);

public sealed record CashDeskShiftRead(ShiftRow Shift, string? Reason, int? ClosedBy, ApprovalRow? Approval);

public sealed record CashDeskTillRead(TillRow Till, CashDeskShiftRead? Active, CashDeskShiftRead? LastClosed);

public sealed record CashDeskRead(DateOnly? BusinessDate, IReadOnlyList<CashDeskTillRead> Tills);
