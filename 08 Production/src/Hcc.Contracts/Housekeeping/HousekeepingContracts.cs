namespace Hcc.Contracts.Housekeeping;

// Wire contracts for the Housekeeping module (contracts package §3). Housekeeping and maintenance never see money (DEC-061):
// no type in this file carries a rate, amount, balance or folio field — a test enforces it.

/// <summary>TransitionHousekeepingTask K V. Action: assign | start | check | complete | inspect | reject.</summary>
public sealed record TransitionHousekeepingTaskRequest(long TaskId, string ExpectedVersion, string Action, int? AssigneeId = null, int? Index = null, string? Reason = null);

public sealed record HousekeepingTaskView(
    long TaskId,
    int RoomId,
    string RoomNo,
    string Kind,
    string Status,
    int? AssigneeId,
    IReadOnlyList<bool> Checklist,
    string? Note,
    string? RejectReason,
    string BusinessDate,
    string? StartedUtc,
    string? CompletedUtc,
    string Version);

public sealed record TransitionHousekeepingTaskResult(HousekeepingTaskView Task, string RoomHk, string Detail);

/// <summary>BlockRoom K (PERM-ROOM-002): out of order over nights [From, To); From defaults to the business date; To null = open-ended.</summary>
public sealed record BlockRoomRequest(int RoomId, string Reason, string? From = null, string? To = null);

public sealed record BlockRoomResult(long BlockId, int RoomId, string RoomNo, string From, string? To, string Version);

/// <summary>UnblockRoom K V (PERM-ROOM-002). <c>ExpectedVersion</c> = the active block's version.</summary>
public sealed record UnblockRoomRequest(int RoomId, string ExpectedVersion);

public sealed record UnblockRoomResult(long BlockId, int RoomId, string RoomNo);

/// <summary>RequestRepair K (PERM-ROOM-001): a repair task for maintenance; it is a block request, not a block.</summary>
public sealed record RequestRepairRequest(int RoomId, string Reason);

public sealed record RequestRepairResult(long TaskId, int RoomId, string RoomNo);

public sealed record RoomBlockView(long BlockId, string? From, string? To, string Reason, string Source, string Version);

/// <summary>Guest on the board: name and dates only.</summary>
public sealed record BoardGuest(string Name, string Arrival, string Departure);

public sealed record RoomBoardRow(
    int RoomId,
    string RoomNo,
    string RoomType,
    string Hk,
    string Status,
    RoomBlockView? Block,
    BoardGuest? Current,
    BoardGuest? Next,
    IReadOnlyList<HousekeepingTaskView> OpenTasks,
    string RoomVersion);

public sealed record RoomBoardCounts(int Rooms, int Occupied, int Available, int Sellable, int Dirty, int Cleaning, int Inspection, int Blocked);

public sealed record RoomBoardPage(string BusinessDate, int Page, int PageSize, int Total, IReadOnlyList<RoomBoardRow> Rows, RoomBoardCounts Counts, bool AssignedOnly);

public sealed record HousekeepingTaskPage(int Page, int PageSize, int Total, IReadOnlyList<HousekeepingTaskView> Rows, bool AssignedOnly);
