using System.Globalization;
using Hcc.Contracts;
using Hcc.Contracts.Closing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Domain.Closing;

/// <summary>Persisted steps of a business-day close, in order (WF-DAY-001).</summary>
public enum CloseStep
{
    Preflight,
    Nights,
    Cash,
    Close,
    Snapshot,
    NextDay,
}

public static class CloseSteps
{
    public static string Code(CloseStep s) => s switch
    {
        CloseStep.Preflight => "preflight",
        CloseStep.Nights => "nights",
        CloseStep.Cash => "cash",
        CloseStep.Close => "close",
        CloseStep.Snapshot => "snapshot",
        CloseStep.NextDay => "next-day",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "Unknown step."),
    };

    public const string Done = "done";
}

/// <summary>Run status: Running · Blocked (a check failed) · Closed (barrier up, next day pending) · Completed.</summary>
public static class CloseRunStatus
{
    public const string Running = "Running";
    public const string Blocked = "Blocked";
    public const string Closed = "Closed";
    public const string Completed = "Completed";
}

// ── Preflight inputs (read inside the close transaction or for the checklist view) ──

public sealed record PendingArrival(long ReservationId, string PublicNo, string Guest, string? RoomNo, DateOnly Arrival);

public sealed record OverdueDeparture(long StayId, string PublicNo, string Guest, string? RoomNo, DateOnly Departure);

public sealed record ExpiredHold(long ReservationId, string PublicNo, string Guest, DateTime HoldUntilUtc);

/// <summary>An in-house stay that needs the night of the business date; <see cref="HasNight"/> = its room charge is posted.</summary>
public sealed record NightTarget(long StayId, long FolioId, string PublicNo, string Guest, string? RoomNo, bool HasNight);

public sealed record PendingApproval(long ApprovalId, string Action, Paise Amount, string Reason);

public sealed record ActiveShift(long ShiftId, string TillCode, string Status, DateOnly BusinessDate, Paise Expected);

public sealed record PreflightData(DateOnly BusinessDate, IReadOnlyList<PendingArrival> Arrivals, IReadOnlyList<OverdueDeparture> Departures,
    IReadOnlyList<ExpiredHold> Holds, IReadOnlyList<NightTarget> Nights, IReadOnlyList<PendingApproval> Approvals, IReadOnlyList<ActiveShift> Shifts,
    IReadOnlyList<PreflightItem>? PosChecks = null, IReadOnlyList<PreflightItem>? PosVoids = null);

/// <summary>
/// Closing checks ported from the prototype (<c>closingPreflight</c>, <c>DAY_CLOSE</c> in <c>03 Prototype/src/domain/streams/closing.ts</c>
/// and <c>services/mock/handlers/closing.ts</c>). Restaurant checks (POS) and pending expenses join when those modules reach the server.
/// </summary>
public static class CloseRules
{
    public const string Arrivals = "arrivals";
    public const string Departures = "departures";
    public const string Holds = "holds";
    public const string Nights = "nights";
    public const string Approvals = "approvals";
    public const string Pos = "pos";
    public const string PosVoidRequests = "posVoids";
    public const string Shift = "shift";

    /// <summary>Checks the preflight step blocks on (before the night posting).</summary>
    public static readonly IReadOnlySet<string> PreflightBlocking = new HashSet<string>(StringComparer.Ordinal) { Arrivals, Departures, Holds, Approvals, Pos, PosVoidRequests };

    /// <summary>Checks the cash step blocks on.</summary>
    public static readonly IReadOnlySet<string> CashBlocking = new HashSet<string>(StringComparer.Ordinal) { Shift };

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>All checks in close order; a check with items is blocking (REQ-CLOSE-002).</summary>
    public static IReadOnlyList<PreflightCheck> Evaluate(PreflightData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        static string Room(string? r) => r is null ? "" : " · room " + r;
        PreflightCheck Check(string id, string title, string hint, IEnumerable<PreflightItem> items)
        {
            var list = items.ToList();
            return new PreflightCheck(id, title, hint, list.Count > 0, list);
        }

        return
        [
            Check(Arrivals, "Arrivals resolved", "Check in, or cancel after refunding advances.",
                data.Arrivals.Select(a => new PreflightItem($"{a.Guest}{Room(a.RoomNo)} · {a.PublicNo}", "reservation:" + a.ReservationId))),
            Check(Departures, "Overdue departures resolved", "Settle and check out, or extend the stay.",
                data.Departures.Select(d => new PreflightItem($"{d.Guest}{Room(d.RoomNo)} · due {Iso(d.Departure)}", "stay:" + d.StayId))),
            Check(Holds, "Expired holds released", "Release or confirm holds whose expiry has passed.",
                data.Holds.Select(h => new PreflightItem($"{h.Guest} · {h.PublicNo} · hold expired {h.HoldUntilUtc:yyyy-MM-dd HH:mm} UTC", "reservation:" + h.ReservationId))),
            Check(Nights, "Room-night charges posted", "The close posts tonight's room line for every in-house stay (step 2).",
                data.Nights.Where(n => !n.HasNight).Select(n => new PreflightItem($"{n.Guest}{Room(n.RoomNo)} · {n.PublicNo}", "stay:" + n.StayId))),
            Check(Approvals, "Approvals resolved", "Approve or reject pending requests.",
                data.Approvals.Select(a => new PreflightItem($"{Label(a.Action)} · {a.Amount} · approval {a.ApprovalId}", "approval:" + a.ApprovalId))),
            Check(Pos, "Restaurant checks settled", "Settle each open check, charge it to the room, or void it (with approval above the limit).", data.PosChecks ?? []),
            Check(PosVoidRequests, "POS void requests decided", "Approve or reject each pending void request.", data.PosVoids ?? []),
            Check(Shift, "Cash shifts reconciled", "Count every drawer and submit the shift; a difference must be decided.",
                data.Shifts.Select(s => new PreflightItem(s.Status == "Submitted"
                    ? $"Till {s.TillCode} · count submitted · difference awaiting approval"
                    : $"Till {s.TillCode} · shift open ({Iso(s.BusinessDate)}) · {s.Expected} expected", "shift:" + s.ShiftId))),
        ];
    }

    private static string Label(string action) => action switch
    {
        "cash-difference" => "Cash difference",
        "" => "Request",
        _ => char.ToUpperInvariant(action[0]) + action[1..],
    };

    /// <summary>The blocking checks among <paramref name="checks"/> limited to <paramref name="ids"/> (all when null).</summary>
    public static IReadOnlyList<PreflightCheck> Blockers(IReadOnlyList<PreflightCheck> checks, IReadOnlySet<string>? ids = null)
        => checks.Where(c => c.Blocking && (ids is null || ids.Contains(c.Id))).ToList();

    /// <summary>INVALID_TRANSITION naming the first blocker; <c>details.blockers</c> lists every blocking check with its items.</summary>
    public static HccException Blocked(DateOnly date, IReadOnlyList<PreflightCheck> blockers)
    {
        ArgumentNullException.ThrowIfNull(blockers);
        var details = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
        {
            ["businessDate"] = System.Text.Json.JsonSerializer.SerializeToElement(Iso(date)),
            ["blockers"] = System.Text.Json.JsonSerializer.SerializeToElement(blockers.Select(b => new { id = b.Id, title = b.Title, hint = b.Hint, items = b.Items.Select(i => i.Label).ToArray() }).ToArray()),
        };
        var first = blockers[0];
        return new HccException(HccErrorCode.INVALID_TRANSITION, $"Day close {Iso(date)} is blocked: {first.Title.ToLowerInvariant()} — {first.Hint}", details);
    }

    /// <summary>Validates a client run id (1–40 characters: letters, digits, '-', '_', '.', ':').</summary>
    public static string ValidateRunRef(string? runId)
    {
        var v = runId?.Trim() ?? string.Empty;
        if (v.Length is 0 or > 40 || !v.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':'))
            throw HccException.Validation("runId must be 1–40 characters (letters, digits, '-', '_', '.', ':').");
        return v;
    }

    public static HccException DayClosed(DateOnly date) => new(HccErrorCode.DAY_CLOSED, $"Business day {Iso(date)} is closed. Corrections post in the open day.");
}

/// <summary>
/// DEC-065 cash-book day lock (prototype <c>cbCanLock</c> / <c>cbCanReopen</c> in <c>03 Prototype/src/domain/streams/cashbook.ts</c>):
/// a checker locks a closed day; the counter, the cash-difference approver and the closer never lock it; while locked nothing may add,
/// change or reverse a cash entry or correction touching that day until the Owner reopens it with a reason.
/// </summary>
public static class CashBookRules
{
    public static string LockedMessage(DateOnly date, string what)
    {
        var d = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $"Cash book {d} is locked, so {what} is blocked. The Owner must reopen {d} (with a reason) first.";
    }

    /// <summary>Throws INVALID_TRANSITION / FORBIDDEN when <paramref name="principalId"/> may not lock the day now.</summary>
    public static void EnsureCanLock(int principalId, bool dayClosed, bool alreadyLocked, IReadOnlyCollection<int> counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        if (!dayClosed) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Close the business day first.");
        if (alreadyLocked) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This cash-book day is already locked.");
        if (counters.Contains(principalId))
            throw new HccException(HccErrorCode.FORBIDDEN, "A different person from the one who counted, approved the difference or closed the day must lock it (DEC-065).");
    }

    public static string EnsureReopenReason(string? reason)
    {
        var r = reason?.Trim() ?? string.Empty;
        if (r.Length == 0) throw HccException.Validation("A reason is required to reopen a locked cash-book day.");
        if (r.Length > 200) throw HccException.Validation("Keep the reason to 200 characters.");
        return r;
    }
}
