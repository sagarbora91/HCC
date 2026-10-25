using Hcc.Application.Commands;
using Hcc.Domain.Stays;

namespace Hcc.Application.Stays;

/// <summary>PERM ids (RBAC matrix v1 §5; contracts §3) used by the Stays module.</summary>
public static class StaysPermissions
{
    public const string GuestView = "PERM-GST-001";
    public const string GuestRegister = "PERM-GST-002";
    /// <summary>Field permission: ID type / last 4 visible; others see "Restricted".</summary>
    public const string GuestIdDetails = "PERM-GST-003";
    public const string CheckIn = "PERM-STY-001";
    public const string CheckOut = "PERM-STY-003";
}

/// <summary>
/// Port toward P4 (Billing), implemented by <c>Hcc.Application.Billing.BillingStayFolioPort</c> over the billing folio
/// service. Both methods run inside the caller's command transaction (the actor is <c>tx.Context</c>).
/// </summary>
public interface IStayFolioPort
{
    /// <summary>Returns the reservation's folio, creating it if none exists yet (check-in: "folio active").</summary>
    Task<long> EnsureFolioAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default);

    /// <summary>
    /// Throws INVALID_TRANSITION unless the folio may close: every agreed night [arrival, departure) posted, balance 0
    /// (or settled by an approved company credit — P4), deposit returned, no pending approvals (P4). Locks the folio row.
    /// </summary>
    Task AssertCheckoutSettledAsync(ICommandTransaction tx, CheckoutFolioCheck check, CancellationToken ct = default);
}

public sealed record CheckoutFolioCheck(long FolioId, long ReservationId, DateOnly Arrival, DateOnly Departure, long? CompanyCreditApprovalId);

public sealed record ReservationRecord(
    long ReservationId,
    int PropertyId,
    string Status,
    DateOnly Arrival,
    DateOnly Departure,
    string GuestName,
    byte[] Version,
    int? RoomId,
    bool AllocationIntact);

public sealed record GuestRecord(long GuestId, int PropertyId, GuestDraft Draft, bool Verified, string Origin, byte[] Version);

public sealed record RoomRecord(int RoomId, int PropertyId, string RoomNo, string Hk, long? OccupyingStayId, byte[] Version);

public sealed record StayRecord(long StayId, int PropertyId, long ReservationId, long FolioId, string Status, DateOnly Arrival, DateOnly Departure, int RoomId, byte[] Version);

/// <summary>Persistence port of the Stays commands (SQL adapter in Infrastructure/Stays). Lock* methods take UPDLOCK row locks.</summary>
public interface IStaysStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<ReservationRecord?> LockReservationAsync(ICommandTransaction tx, int propertyId, long reservationId, CancellationToken ct = default);

    Task<long?> RegisteredGuestAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default);

    Task<GuestRecord?> LockGuestAsync(ICommandTransaction tx, int propertyId, long guestId, CancellationToken ct = default);

    Task<long> InsertGuestAsync(ICommandTransaction tx, int propertyId, GuestDraft clean, bool verified, string origin, DateTime utc, CancellationToken ct = default);

    /// <summary>Versioned update (STALE_VERSION when <paramref name="expectedVersion"/> no longer matches).</summary>
    Task UpdateGuestAsync(ICommandTransaction tx, long guestId, byte[] expectedVersion, GuestDraft clean, bool verified, CancellationToken ct = default);

    Task<byte[]> GuestVersionAsync(ICommandTransaction tx, long guestId, CancellationToken ct = default);

    Task UpsertRegistrationAsync(ICommandTransaction tx, long reservationId, long guestId, int principalId, DateTime utc, CancellationToken ct = default);

    Task<RoomRecord?> LockRoomAsync(ICommandTransaction tx, int propertyId, int roomId, CancellationToken ct = default);

    Task<bool> RoomBlockedDuringAsync(ICommandTransaction tx, int roomId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task SetReservationStatusAsync(ICommandTransaction tx, long reservationId, byte[] expectedVersion, string status, CancellationToken ct = default);

    Task<(long StayId, byte[] Version)> InsertStayAsync(ICommandTransaction tx, ReservationRecord r, long guestId, long folioId, int roomId, DateOnly businessDate, int principalId, DateTime utc, CancellationToken ct = default);

    Task<StayRecord?> LockStayAsync(ICommandTransaction tx, int propertyId, long stayId, CancellationToken ct = default);

    /// <summary>Stay CheckedOut + open segment closed at the business date (versioned; STALE_VERSION on mismatch). Returns the new version.</summary>
    Task<byte[]> CheckOutStayAsync(ICommandTransaction tx, long stayId, byte[] expectedVersion, DateOnly businessDate, int principalId, DateTime utc, CancellationToken ct = default);

    Task SetRoomHkAsync(ICommandTransaction tx, int roomId, string hk, CancellationToken ct = default);
}

/// <summary>Read side of the Stays module (own connection, no transaction).</summary>
public interface IStaysReadStore
{
    /// <summary>The profile including its party; null when absent or outside the property.</summary>
    Task<GuestRecord?> GetGuestAsync(int propertyId, long guestId, CancellationToken ct = default);
}
