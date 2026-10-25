namespace Hcc.Contracts.Housekeeping;

/// <summary>
/// GenerateStayoverTasks K (PERM-HK-002): one stay-over clean per occupied room for the current business date, idempotent per room
/// and date (also run automatically when a new business day opens). No money field (DEC-061).
/// </summary>
public sealed record GenerateStayoverTasksRequest;

public sealed record GenerateStayoverTasksResult(string BusinessDate, int Created, int AlreadyExisted, int OccupiedRooms);
