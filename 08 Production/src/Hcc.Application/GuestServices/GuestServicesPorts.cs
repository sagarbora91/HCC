using Hcc.Application.Commands;
using Hcc.Domain.GuestServices;

namespace Hcc.Application.GuestServices;

/// <summary>
/// PERM ids of guest services: the module's own set from role matrix v2 (DEC-079, migration 3700) instead of the borrowed guest-register,
/// housekeeping, room and approval ids. <see cref="Manage"/> held property-wide = the desk that logs and works every request (Owner, Manager,
/// Reception, Housekeeping supervisor); held at assigned scope = housekeeping / maintenance staff working their own requests.
/// <see cref="HousekeepingStaff"/> and <see cref="MaintenanceStaff"/> are core ids used only to tell the two departments apart when a request
/// is assigned (not access grants). Recovery amounts are money (<see cref="Money"/>): Supervisor, Housekeeper and Maintenance never see them.
/// Reports: the module view property-wide or the operations-report permission (PERM-RPT-001, its own meaning).
/// </summary>
public static class GuestServicePermissions
{
    public const string View = "PERM-GSV-001";
    public const string Manage = "PERM-GSV-002";
    public const string Approve = "PERM-GSV-003";
    public const string Money = "PERM-GSV-004";
    public const string Supervise = "PERM-GSV-005";
    public const string HousekeepingStaff = "PERM-HK-003";
    public const string MaintenanceStaff = "PERM-ROOM-001";
    public const string Report = "PERM-RPT-001";
}

public static class GuestServiceEvents
{
    public const string RequestLogged = "ServiceRequestLogged";
    public const string RequestDone = "ServiceRequestDone";
    public const string ComplaintLogged = "GuestComplaintLogged";
    public const string ComplaintResolved = "GuestComplaintResolved";
    public const string RecoveryOffered = "ServiceRecoveryOffered";
    public const string LostItemLogged = "LostItemLogged";
}

public sealed record PropertyClock(DateOnly BusinessDate, string TimeZone);

public sealed record StayInfo(long StayId, int RoomId, string RoomNo, string GuestName, string Status, long FolioId);

public sealed record RequestRecord(long RequestId, int PropertyId, long StayId, int RoomId, string RoomNo, string GuestName, string Type, string Detail, string Dept,
    int? AssigneeId, string Priority, string Status, DateTime CreatedUtc, DateTime DueUtc, DateTime? ScheduledUtc, DateTime? StartedUtc, DateTime? DoneUtc,
    string? GuestNote, string? CancelReason, int CreatedBy, byte[] Version)
{
    public RequestFact Fact => new(Type, Dept, Status, CreatedUtc, DueUtc, ScheduledUtc, DoneUtc);
}

public sealed record NewRequest(int PropertyId, StayInfo Stay, string Type, string Detail, string Dept, int? AssigneeId, string Priority, string Status,
    DateTime CreatedUtc, DateTime DueUtc, DateTime? ScheduledUtc, int CreatedBy);

public sealed record ComplaintRecord(long ComplaintId, int PropertyId, long? StayId, int? RoomId, string? RoomNo, string GuestName, string Category, string Severity,
    string Description, int OwnerId, string Status, string? RootCause, string? RecoveryAction, DateOnly? FollowUpOn, string? FollowUpOutcome, bool? FollowUpSatisfied,
    DateTime? FollowUpUtc, int? FollowUpBy, DateTime CreatedUtc, DateTime? ResolvedUtc, DateTime? ClosedUtc, int CreatedBy, string? RecoveryState, long? RecoveryAmountPaise,
    string? RecoveryReason, long? RecoveryFolioId, long? RecoveryApprovalId, long? RecoveryChargeId, DateTime? RecoveryUtc, int? RecoveryBy,
    string? ApprovalStatus, byte[] Version, string? CategoryCode = null)
{
    public string RecoveryStatus => GuestRecoveryRules.StatusOf(RecoveryState, RecoveryChargeId is not null, RecoveryApprovalId is not null, ApprovalStatus);
}

public sealed record NewComplaint(int PropertyId, long? StayId, int? RoomId, string GuestName, string Category, string Severity, string Description, int OwnerId,
    DateTime CreatedUtc, int CreatedBy, string? CategoryCode = null);

public sealed record ItemRecord(long ItemId, int PropertyId, string Description, string Category, bool Valuable, string FoundWhere, int? RoomId, string? RoomNo, long? StayId,
    DateTime FoundUtc, DateOnly FoundDate, int FoundBy, string Storage, string Status, string? ClaimName, string? ClaimPhone, string? ClaimIdType, string? ClaimIdNote,
    string? ClaimMatchNote, DateTime? ClaimUtc, int? ClaimBy, string? HandoverTo, string? HandoverNote, DateTime? HandoverUtc, int? HandoverBy, string? DisposalMethod,
    string? DisposalNote, DateTime? DisposalUtc, int? DisposalBy, byte[] Version, string? CategoryCode = null);

public sealed record NewItem(int PropertyId, string Description, string Category, bool Valuable, string FoundWhere, int? RoomId, long? StayId, DateTime FoundUtc,
    DateOnly FoundDate, int FoundBy, string Storage, string? CategoryCode = null);

public sealed record LogEntry(DateTime AtUtc, int By, string What);

public sealed record RequestFilter(int? AssigneeId, string? DeptOnly, bool OpenOnly);

/// <summary>Complaint recovery status from the stored link and the Billing approval it points to (prototype <c>gsvRecoveryStatus</c>).</summary>
public static class GuestRecoveryRules
{
    public const string None = "None";
    public const string Requesting = "Requesting";
    public const string Posted = "Posted";
    public const string PendingApproval = "Pending approval";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";

    public static string StatusOf(string? state, bool hasCharge, bool hasApproval, string? approvalStatus)
    {
        if (state is null) return None;
        if (state == "Requesting") return Requesting;
        if (hasCharge) return Posted;
        if (!hasApproval) return None;
        return approvalStatus switch
        {
            "Pending" => PendingApproval,
            "Approved" or "Executed" => Approved,
            _ => Rejected,
        };
    }

    /// <summary>A new recovery may be offered when none exists or the earlier request was rejected / expired.</summary>
    public static bool CanOffer(string status) => status is None or Rejected;
}

/// <summary>
/// Persistence for guest services. Methods taking an <see cref="ICommandTransaction"/> run inside the command transaction and the
/// <c>Lock*</c> ones take UPDLOCK; every <c>Update*</c> is versioned (STALE_VERSION when <c>expected</c> no longer matches) and returns the new version.
/// </summary>
public interface IGuestServicesStore
{
    Task<PropertyClock> PropertyAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<(GuestServiceSettings Settings, byte[]? Version)> LockSettingsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<byte[]> SaveSettingsAsync(ICommandTransaction tx, int propertyId, GuestServiceSettings settings, byte[]? expected, int principalId, DateTime utc, CancellationToken ct = default);

    Task<StayInfo?> StayAsync(ICommandTransaction tx, int propertyId, long stayId, CancellationToken ct = default);

    /// <summary>The most recent stay (in house or departed, arrived on or before <paramref name="today"/>) that used the room; null when none.</summary>
    Task<long?> LastStayInRoomAsync(ICommandTransaction tx, int roomId, DateOnly today, CancellationToken ct = default);

    Task<(int RoomId, string RoomNo)?> RoomByNoAsync(ICommandTransaction tx, int propertyId, string roomNo, CancellationToken ct = default);

    Task<long> InsertRequestAsync(ICommandTransaction tx, NewRequest request, CancellationToken ct = default);

    Task<RequestRecord?> LockRequestAsync(ICommandTransaction tx, int propertyId, long requestId, CancellationToken ct = default);

    Task<byte[]> UpdateRequestAsync(ICommandTransaction tx, RequestRecord updated, byte[] expected, CancellationToken ct = default);

    /// <summary>LF-ALERTS: moves a wake-up call's wake_at_utc, scheduled_utc and due_utc together (snooze).</summary>
    Task<byte[]> SnoozeWakeAsync(ICommandTransaction tx, long requestId, DateTime wakeUtc, DateTime dueUtc, byte[] expected, CancellationToken ct = default);

    Task<long> InsertComplaintAsync(ICommandTransaction tx, NewComplaint complaint, CancellationToken ct = default);

    Task<ComplaintRecord?> LockComplaintAsync(ICommandTransaction tx, int propertyId, long complaintId, CancellationToken ct = default);

    Task<byte[]> UpdateComplaintAsync(ICommandTransaction tx, ComplaintRecord updated, byte[] expected, CancellationToken ct = default);

    Task<long> InsertItemAsync(ICommandTransaction tx, NewItem item, CancellationToken ct = default);

    Task<ItemRecord?> LockItemAsync(ICommandTransaction tx, int propertyId, long itemId, CancellationToken ct = default);

    Task<byte[]> UpdateItemAsync(ICommandTransaction tx, ItemRecord updated, byte[] expected, CancellationToken ct = default);

    Task AppendLogAsync(ICommandTransaction tx, string kind, long recordId, DateTime utc, int principalId, string what, CancellationToken ct = default);
}

/// <summary>Reads on their own connection (no locks).</summary>
public interface IGuestServicesReadStore
{
    Task<PropertyClock> PropertyAsync(int propertyId, CancellationToken ct = default);

    Task<(GuestServiceSettings Settings, byte[]? Version)> SettingsAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<RequestRecord>> RequestsAsync(int propertyId, RequestFilter filter, CancellationToken ct = default);

    Task<RequestRecord?> RequestAsync(int propertyId, long requestId, CancellationToken ct = default);

    Task<IReadOnlyList<ComplaintRecord>> ComplaintsAsync(int propertyId, bool openOnly, CancellationToken ct = default);

    Task<ComplaintRecord?> ComplaintAsync(int propertyId, long complaintId, CancellationToken ct = default);

    /// <summary><paramref name="foundBy"/> restricts to items that principal found (loggers without front-office rights).</summary>
    Task<IReadOnlyList<ItemRecord>> ItemsAsync(int propertyId, int? foundBy, CancellationToken ct = default);

    Task<IReadOnlyList<LogEntry>> HistoryAsync(int propertyId, string kind, long recordId, CancellationToken ct = default);

    /// <summary>Every history entry of the register <paramref name="kind"/> (request | complaint | item) of the property, oldest first, with its record id.</summary>
    Task<IReadOnlyList<(long RecordId, LogEntry Entry)>> HistoriesAsync(int propertyId, string kind, CancellationToken ct = default);

    /// <summary>In-house stays plus the stays referenced by a request, complaint or lost item (reservation + guest ids for the screens).</summary>
    Task<IReadOnlyList<StayLookupRow>> StaysAsync(int propertyId, CancellationToken ct = default);
}

public sealed record StayLookupRow(long StayId, long ReservationId, long? GuestId, string RoomNo, string GuestName, string Status);
