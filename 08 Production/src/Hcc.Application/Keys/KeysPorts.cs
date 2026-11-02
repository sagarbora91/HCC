using Hcc.Application.Commands;
using Hcc.Contracts.Keys;

namespace Hcc.Application.Keys;

/// <summary>Key permissions (migration 4721). The Manager holds all four; there is no separate key operator login.</summary>
public static class KeyPermissions
{
    public const string Read = "PERM-KEY-001";
    public const string Issue = "PERM-KEY-002";
    public const string Cancel = "PERM-KEY-003";
    public const string Configure = "PERM-KEY-004";
}

public sealed record KeyReservation(long ReservationId, string PublicNo, string Status, DateOnly Arrival, DateOnly Departure, int? RoomId, string? RoomNo, long? StayId);

public sealed record KeyLockRec(int RoomId, string RoomNo, bool HasLock, int? LockNo, string? DoorId, string? DoorName, string? Note, byte[] Version);

public sealed record KeyEncoderRec(string EncoderId, int PropertyId, string PcName, string? Adapter, bool Online, string? Detail, DateTime? LastSeenUtc, bool Enabled);

public sealed record KeyJobRec(Guid JobId, int PropertyId, string Kind, string Event, string? Reason, long? ReservationId, int? RoomId, string? RoomNo, string PayloadJson,
    int RequestedBy, string RequestedByName, DateTime RequestedUtc, string? EncoderId, string State, string? ErrorCode, string? ResultJson, DateTime TimeoutUtc, DateTime? FinishedUtc, string? Warning);

public sealed record KeyCardRec(long CardId, string? CardNo, int RoomId, string RoomNo, string Event, string? Reason, DateTime BeginAt, DateTime EndAt, string Status, int IssuedBy, DateTime IssuedUtc);

public sealed record KeyLogFilter(DateOnly? From, DateOnly? To, string? Room, string? Booking, int? User, string? Event, int Limit = 500);

/// <summary>Data-access port of the key module: transactional members belong to a staff command; the rest open their own connection (agent calls, reads).</summary>
public interface IKeyStore
{
    // ── inside a staff command ──
    Task<KeyReservation?> LockReservationAsync(ICommandTransaction tx, int propertyId, long reservationId, CancellationToken ct = default);

    Task<TimeOnly> FolioCheckoutAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>The lock row of a room (created from the BIS default map when missing).</summary>
    Task<KeyLockRec?> LockOfRoomAsync(ICommandTransaction tx, int propertyId, int roomId, CancellationToken ct = default);

    Task<KeyEncoderRec?> OnlineEncoderAsync(ICommandTransaction tx, int propertyId, DateTime utcNow, CancellationToken ct = default);

    Task InsertJobAsync(ICommandTransaction tx, KeyJobRec job, string? idemKey, CancellationToken ct = default);

    Task InsertManualCardAsync(ICommandTransaction tx, int propertyId, Guid jobId, string ev, string? reason, long reservationId, int roomId, DateTime beginAt, DateTime endAt, int by, DateTime utc, CancellationToken ct = default);

    Task<bool> HasActiveCardAsync(ICommandTransaction tx, int propertyId, int roomId, CancellationToken ct = default);

    Task<KeyLockRec?> SaveLockAsync(ICommandTransaction tx, int propertyId, SaveKeyLockRequest req, byte[] expected, int by, DateTime utc, CancellationToken ct = default);

    Task InsertEncoderAsync(ICommandTransaction tx, int propertyId, string encoderId, string pcName, string tokenHash, CancellationToken ct = default);

    // ── agent side ──
    Task<KeyEncoderRec?> AuthenticateAsync(string encoderId, string tokenHash, CancellationToken ct = default);

    Task HeartbeatAsync(string encoderId, string? adapter, bool online, string? detail, bool? x86, DateTime utc, CancellationToken ct = default);

    /// <summary>Queued jobs past their time-out become expired; picked / waiting jobs the agent abandoned fail with UiTimeout.</summary>
    Task<int> ExpireAsync(int propertyId, DateTime utcNow, CancellationToken ct = default);

    /// <summary>Atomically takes the oldest queued job (one job at a time: nothing while another is picked / waiting_card for that encoder).</summary>
    Task<KeyJobRec?> ClaimNextAsync(string encoderId, int propertyId, DateTime utcNow, CancellationToken ct = default);

    Task<KeyJobRec?> GetJobAsync(Guid jobId, CancellationToken ct = default);

    Task<bool> MoveStateAsync(Guid jobId, string encoderId, string from, string to, DateTime utc, CancellationToken ct = default);

    /// <summary>Finishes a job in one transaction with its card effects. Returns the job as stored (a repeated call changes nothing).</summary>
    Task<KeyJobRec> CompleteAsync(KeyJobRec job, string encoderId, string code, string? message, string? cardNo, string? guestSn, string? agentVersion, DateTime utcNow, CancellationToken ct = default);

    // ── reads ──
    Task<KeyReservation?> ReservationAsync(int propertyId, long reservationId, CancellationToken ct = default);

    Task<TimeOnly> FolioCheckoutAsync(int propertyId, CancellationToken ct = default);

    Task<KeyLockRec?> LockOfRoomAsync(int propertyId, int roomId, CancellationToken ct = default);

    Task<IReadOnlyList<KeyLockRec>> LocksAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<KeyEncoderRec>> EncodersAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<KeyJobRec>> JobsOfReservationAsync(int propertyId, long reservationId, int? roomId, int limit, CancellationToken ct = default);

    Task<IReadOnlyList<KeyCardRec>> ActiveCardsAsync(int propertyId, long reservationId, CancellationToken ct = default);

    Task<KeyCountsView> CountsAsync(int propertyId, long? reservationId, int? roomId, CancellationToken ct = default);

    Task<(IReadOnlyList<KeyLogRow> Rows, KeyCountsView Totals, int Total)> LogAsync(int propertyId, KeyLogFilter filter, CancellationToken ct = default);
}
