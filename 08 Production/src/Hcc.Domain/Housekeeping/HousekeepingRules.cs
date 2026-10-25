using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Housekeeping;

/// <summary>Room readiness in <c>inv.room.hk_status</c> (0002 CHECK constraint).</summary>
public static class RoomHk
{
    public const string Ready = "Ready";
    public const string Dirty = "Dirty";
    public const string Cleaning = "Cleaning";
    public const string Inspection = "Inspection";
}

/// <summary>Housekeeping task status (prototype "To do" / "In progress" / "Inspection" / "Done").</summary>
public static class HkTaskStatus
{
    public const string ToDo = "ToDo";
    public const string InProgress = "InProgress";
    public const string Inspection = "Inspection";
    public const string Done = "Done";
}

/// <summary>Task kinds: only checkout/move cleans drive room readiness; stayover and repair leave it unchanged.</summary>
public static class HkTaskKind
{
    public const string Checkout = "checkout";
    public const string Stayover = "stayover";
    public const string Repair = "repair";
    public const string Move = "move";

    public static bool DrivesReadiness(string kind) => kind is Checkout or Move;
}

public static class HkAction
{
    public const string Assign = "assign";
    public const string Start = "start";
    public const string Check = "check";
    public const string Complete = "complete";
    public const string Inspect = "inspect";
    public const string Reject = "reject";

    public static readonly IReadOnlyList<string> All = [Assign, Start, Check, Complete, Inspect, Reject];
}

/// <summary>Task state as stored; <see cref="ChecklistMask"/> = three cleaning items, bit i = item i ticked.</summary>
public sealed record HkTaskState(string Kind, string Status, int ChecklistMask, int? AssigneeId, string? RejectReason);

/// <summary>Result of a transition: the new task state, the new room readiness (null = unchanged), and timestamps to set.</summary>
public sealed record HkTransition(HkTaskState Task, string? RoomHk, bool SetStarted, bool SetCompleted, bool ClearCompleted, bool SetInspected, string Detail);

/// <summary>
/// The housekeeping task machine (WF-HK-001) ported from the prototype's TASK handler (handlers/housekeeping.ts):
/// To do → start → In progress (room Cleaning) → 3 checklist ticks → complete → Inspection (or Ready with inspection off)
/// → inspect → Ready (a block, if any, stays — AC-010) | reject (reason) → In progress, checklist cleared.
/// Authorization and the assignee eligibility check are the caller's job.
/// </summary>
public static class HkTaskMachine
{
    public const int ChecklistItems = 3;
    public const int FullChecklist = (1 << ChecklistItems) - 1;

    public static HkTransition Apply(HkTaskState t, string action, bool inspectionRequired, int? assigneeId = null, int? index = null, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(t);
        var readiness = HkTaskKind.DrivesReadiness(t.Kind);
        static HccException Bad(string m) => new(HccErrorCode.INVALID_TRANSITION, m);
        switch (action)
        {
            case HkAction.Assign:
                if (assigneeId is null or <= 0) throw HccException.Validation(t.Kind == HkTaskKind.Repair ? "Select an enabled maintenance user." : "Select an enabled housekeeper.");
                if (t.Status == HkTaskStatus.Done) throw Bad("This task is already done.");
                return new(t with { AssigneeId = assigneeId }, null, false, false, false, false, $"assigned → {assigneeId}");
            case HkAction.Start:
                if (t.Status != HkTaskStatus.ToDo) throw Bad("Task has already started.");
                return new(t with { Status = HkTaskStatus.InProgress }, readiness ? RoomHk.Cleaning : null, true, false, false, false, "start");
            case HkAction.Check:
                if (t.Kind == HkTaskKind.Repair) throw HccException.Validation("Repair tasks have no cleaning checklist.");
                if (t.Status != HkTaskStatus.InProgress) throw Bad("Start the task before checking items.");
                if (index is null or < 0 or >= ChecklistItems) throw HccException.Validation("Invalid checklist item.");
                return new(t with { ChecklistMask = t.ChecklistMask ^ (1 << index.Value) }, null, false, false, false, false, $"check {index}");
            case HkAction.Complete:
                if (t.Kind == HkTaskKind.Repair)
                {
                    // Completing a repair never unblocks the room: the supervisor reviews and unblocks (PERM-ROOM-002).
                    if (t.Status != HkTaskStatus.InProgress) throw Bad("Start the repair before completing it.");
                    return new(t with { Status = HkTaskStatus.Done }, null, false, true, false, false, "complete");
                }

                if (t.Status != HkTaskStatus.InProgress || t.ChecklistMask != FullChecklist) throw Bad("Finish all checklist items first.");
                if (t.Kind == HkTaskKind.Stayover) return new(t with { Status = HkTaskStatus.Done }, null, false, true, false, false, "complete");
                return inspectionRequired
                    ? new(t with { Status = HkTaskStatus.Inspection }, RoomHk.Inspection, false, true, false, false, "complete")
                    : new(t with { Status = HkTaskStatus.Done }, RoomHk.Ready, false, true, false, false, "complete");
            case HkAction.Inspect:
                if (t.Status != HkTaskStatus.Inspection) throw Bad("Task is not awaiting inspection.");
                return new(t with { Status = HkTaskStatus.Done }, RoomHk.Ready, false, false, false, true, "inspect");
            case HkAction.Reject:
                if (t.Status != HkTaskStatus.Inspection) throw Bad("Task is not awaiting inspection.");
                var r = (reason ?? string.Empty).Trim();
                if (r.Length < 3) throw HccException.Validation("Enter a reason for returning the room for cleaning.");
                if (r.Length > 500) throw HccException.Validation("The reason is at most 500 characters.");
                return new(t with { Status = HkTaskStatus.InProgress, ChecklistMask = 0, RejectReason = r }, RoomHk.Cleaning, false, false, true, false, "reject · " + r);
            default:
                throw HccException.Validation("Unknown task action.");
        }
    }
}

/// <summary>A room block over nights [From, To); null From = no lower bound, null To = open-ended (prototype <c>blockedDuring</c>).</summary>
public sealed record RoomBlockSpan(DateOnly? From, DateOnly? To);

/// <summary>Room board status ported from the prototype's <c>roomStatus()</c> / <c>blockedDuring()</c> (domain/selectors.ts).</summary>
public static class RoomBoardRules
{
    public const string Blocked = "Blocked";
    public const string Occupied = "Occupied";

    /// <summary>Whether the block covers any night in [arrival, departure).</summary>
    public static bool BlockedDuring(RoomBlockSpan? block, DateOnly arrival, DateOnly departure)
    {
        if (block is null) return false;
        return (block.To is null || arrival < block.To) && (block.From is null || departure > block.From);
    }

    /// <summary>Blocked tonight → Blocked; else occupied → Occupied; else the readiness status.</summary>
    public static string RoomStatus(RoomBlockSpan? block, DateOnly businessDate, bool occupied, string hk)
        => BlockedDuring(block, businessDate, businessDate.AddDays(1)) ? Blocked : occupied ? Occupied : hk;
}
