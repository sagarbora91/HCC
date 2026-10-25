using System.Globalization;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Maintenance;

// Module 15 · Maintenance rules ported from the prototype (03 Prototype/src/domain/streams/maintenance.ts and
// src/services/mock/handlers/maintenance.ts). Pure: no I/O, times are UTC values supplied by the caller (TimeProvider),
// money is long paise. Housekeeping/maintenance users never receive money (DEC-063 G12) — see the views in Hcc.Contracts.Maintenance.

public static class MntPriority
{
    public const string P1 = "P1", P2 = "P2", P3 = "P3", P4 = "P4";

    public static readonly IReadOnlyList<string> All = [P1, P2, P3, P4];

    /// <summary>P1 Urgent 4 h · P2 High 24 h · P3 Normal 72 h · P4 Low 7 days (prototype MNT_PRIORITY).</summary>
    public static int SlaMinutes(string priority) => priority switch { P1 => 240, P2 => 1440, P3 => 4320, P4 => 10080, _ => throw HccException.Validation("Choose a priority (P1–P4).") };

    public static string Label(string priority) => priority switch { P1 => "Urgent", P2 => "High", P3 => "Normal", _ => "Low" };
}

public static class MntStatus
{
    public const string Open = "Open", InProgress = "InProgress", OnHold = "OnHold", Done = "Done", Verified = "Verified", Cancelled = "Cancelled";

    public static readonly IReadOnlyList<string> All = [Open, InProgress, OnHold, Done, Verified, Cancelled];

    public static bool IsActive(string status) => status is Open or InProgress or OnHold;
}

public static class MntKind
{
    public const string Corrective = "Corrective", Preventive = "Preventive";
}

public static class MntAssetKinds
{
    public static readonly IReadOnlyList<string> All = ["AC", "Geyser", "TV", "Lift", "Generator", "Pump", "RO plant", "Other"];

    public static readonly IReadOnlyList<string> Statuses = ["Working", "Faulty", "Retired"];
}

public sealed record MntPart(int SpareId, int Qty, long UnitCostPaise);

/// <summary>A work order as the server stores it (internal; never sent as is — see the views in Hcc.Contracts.Maintenance).</summary>
public sealed record MntOrder(
    long Id, string Kind, string Title, string Priority, string Status,
    int? RoomId, string? RoomNo, int? AssetId, string? Area, int? AssigneeId, int? VendorId,
    DateTime CreatedUtc, int CreatedBy, DateTime? StartedUtc, DateTime? DoneUtc, int? DoneBy, DateTime? VerifiedUtc, int? VerifiedBy,
    string? HoldReason, int? AwaitingSpareId, string? Note, string? ReturnReason, string? CancelReason,
    long? RepairTaskId, int? PlanId, DateOnly? DueDate, DateTime? DowntimeFromUtc, DateTime? DowntimeToUtc, long? BlockId,
    IReadOnlyList<MntPart> Parts, byte[] Version)
{
    public string No => MntRules.OrderNo(Id);

    public bool IsActive => MntStatus.IsActive(Status);

    public long PartsCostPaise => Parts.Sum(p => p.Qty * p.UnitCostPaise);
}

public sealed record MntAsset(int Id, string Name, string Kind, int? RoomId, string? RoomNo, string? Area, string Make, string Model, DateOnly? InstalledOn, int? VendorId, string Status, byte[] Version);

public sealed record MntVendor(int Id, string Name, string Trade, string Phone, byte[] Version);

public sealed record MntAmc(int Id, int VendorId, string Scope, DateOnly From, DateOnly To, int Visits, long ValuePaise, byte[] Version);

public sealed record MntSpare(int Id, string Name, string Unit, int Stock, int ReorderAt, long CostPaise, byte[] Version)
{
    public bool IsLow => Stock <= ReorderAt;
}

public sealed record MntPlan(int Id, int AssetId, string Title, int EveryDays, DateOnly NextDue, bool Active, DateOnly? LastDone, byte[] Version);

public sealed record MntDowntimeRow(int RoomId, string RoomNo, int Minutes, int Orders, bool Running);

public sealed record MntPmOccurrence(MntPlan Plan, DateOnly Due);

public sealed record MntPmCompliance(int Due, int OnTime, int? Pct);

public sealed record MntSummary(int Open, int InProgress, int OnHold, int Done, int Verified, int Breached);

public static class MntRules
{
    /// <summary>Plan occurrences are generated up to the business date plus this many days (prototype MNT_PM_HORIZON_DAYS).</summary>
    public const int PmHorizonDays = 3;

    public static string OrderNo(long id) => "WO-" + id.ToString("D4", CultureInfo.InvariantCulture);

    public static int Minutes(DateTime from, DateTime to) => (int)Math.Round((to - from).TotalMinutes, MidpointRounding.AwayFromZero);

    public static string Duration(int mins)
    {
        if (mins < 60) return $"{Math.Max(0, mins).ToString(CultureInfo.InvariantCulture)} min";
        if (mins < 1440) return $"{mins / 60} h {(mins % 60 > 0 ? (mins % 60).ToString(CultureInfo.InvariantCulture) + " min" : string.Empty)}".Trim();
        var d = mins / 1440;
        var h = mins % 1440 / 60;
        return $"{d} d{(h > 0 ? " " + h.ToString(CultureInfo.InvariantCulture) + " h" : string.Empty)}";
    }

    // ── SLA ──
    public static DateTime SlaDue(MntOrder w) => w.CreatedUtc.AddMinutes(MntPriority.SlaMinutes(w.Priority));

    /// <summary>Met / Breached (done late or still open past due) / DueSoon (≤ 25 % of the SLA left) / OnTrack; Cancelled = NA.</summary>
    public static string Sla(MntOrder w, DateTime now)
    {
        if (w.Status == MntStatus.Cancelled) return "NA";
        var due = SlaDue(w);
        if (w.DoneUtc is { } done) return done <= due ? "Met" : "Breached";
        if (now > due) return "Breached";
        return Minutes(now, due) <= MntPriority.SlaMinutes(w.Priority) / 4.0 ? "DueSoon" : "OnTrack";
    }

    public static int Age(MntOrder w, DateTime now) => Minutes(w.CreatedUtc, now);

    // ── AMC, spares ──
    /// <summary>Active / Expiring (ends within 30 days) / Expired / NotStarted.</summary>
    public static string AmcStatus(DateOnly from, DateOnly to, DateOnly date)
    {
        if (date > to) return "Expired";
        if (date < from) return "NotStarted";
        return date.AddDays(30) >= to ? "Expiring" : "Active";
    }

    public static IReadOnlyList<MntSpare> LowSpares(IEnumerable<MntSpare> spares) => spares.Where(s => s.IsLow).ToList();

    // ── Preventive maintenance ──
    /// <summary>Plan occurrences due on or before date + horizon that have no work order yet (what GeneratePmWorkOrders creates).</summary>
    public static IReadOnlyList<MntPmOccurrence> PmDue(IEnumerable<MntPlan> plans, IEnumerable<MntOrder> orders, DateOnly businessDate)
    {
        var list = orders.Where(o => o.PlanId is not null).ToList();
        return PmDue(plans, (plan, due) => list.Any(o => o.PlanId == plan && o.DueDate == due), businessDate);
    }

    public static IReadOnlyList<MntPmOccurrence> PmDue(IEnumerable<MntPlan> plans, Func<int, DateOnly, bool> occurrenceExists, DateOnly businessDate)
    {
        ArgumentNullException.ThrowIfNull(occurrenceExists);
        var horizon = businessDate.AddDays(PmHorizonDays);
        var result = new List<MntPmOccurrence>();
        foreach (var p in plans.Where(p => p.Active))
        {
            var d = p.NextDue;
            for (var n = 0; d <= horizon && n < 12; n++)
            {
                if (!occurrenceExists(p.Id, d)) result.Add(new MntPmOccurrence(p, d));
                d = d.AddDays(p.EveryDays);
            }
        }

        return result;
    }

    /// <summary>Where a plan's next-due lands after generation (advanced past the horizon, at most 12 steps).</summary>
    public static DateOnly AdvanceNextDue(MntPlan p, DateOnly businessDate)
    {
        ArgumentNullException.ThrowIfNull(p);
        var horizon = businessDate.AddDays(PmHorizonDays);
        var d = p.NextDue;
        for (var n = 0; d <= horizon && n < 12; n++) d = d.AddDays(p.EveryDays);
        return d;
    }

    public static string PlanState(MntPlan p, IEnumerable<MntOrder> orders, DateOnly businessDate)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (!p.Active) return "Paused";
        var open = orders.Where(o => o.PlanId == p.Id && o.IsActive).ToList();
        if (open.Any(o => o.DueDate < businessDate) || p.NextDue < businessDate) return "Overdue";
        if (open.Count > 0 || p.NextDue <= businessDate.AddDays(PmHorizonDays)) return "Due";
        return "Scheduled";
    }

    // ── Reports ──
    private static readonly (string Label, double Max)[] AgeBucketDefs = [("Under 1 day", 1440), ("1–3 days", 4320), ("3–7 days", 10080), ("Over 7 days", double.PositiveInfinity)];

    public static IReadOnlyList<(string Label, int Count)> AgeBuckets(IEnumerable<MntOrder> orders, DateTime now)
    {
        var open = orders.Where(o => o.IsActive).ToList();
        return AgeBucketDefs.Select((b, i) => (b.Label, open.Count(w =>
        {
            var a = Age(w, now);
            return a < b.Max && (i == 0 || a >= AgeBucketDefs[i - 1].Max);
        }))).ToList();
    }

    /// <summary>Mean time to repair (minutes), corrective work orders created → done; null when none are done.</summary>
    public static int? Mttr(IEnumerable<MntOrder> orders)
    {
        var done = orders.Where(w => w.Kind == MntKind.Corrective && w.DoneUtc is not null).ToList();
        if (done.Count == 0) return null;
        return (int)Math.Round(done.Sum(w => (double)Minutes(w.CreatedUtc, w.DoneUtc!.Value)) / done.Count, MidpointRounding.AwayFromZero);
    }

    /// <summary>Preventive work orders due on/before the business date, done on/before their due date.</summary>
    public static MntPmCompliance PmCompliance(IEnumerable<MntOrder> orders, DateOnly businessDate)
    {
        var due = orders.Where(w => w.Kind == MntKind.Preventive && w.DueDate is { } d && d <= businessDate && w.Status != MntStatus.Cancelled).ToList();
        var onTime = due.Count(w => w.DoneUtc is { } done && DateOnly.FromDateTime(done) <= w.DueDate!.Value);
        return new MntPmCompliance(due.Count, onTime, due.Count == 0 ? null : (int)Math.Round(onTime * 100.0 / due.Count, MidpointRounding.AwayFromZero));
    }

    public static int SlaBreaches(IEnumerable<MntOrder> orders, DateTime now) => orders.Count(w => Sla(w, now) == "Breached");

    /// <summary>End of a downtime: explicit end; else still running while the room has an active block; else when the work finished.</summary>
    public static DateTime? DowntimeEnd(MntOrder w, bool roomBlockedNow, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (w.DowntimeFromUtc is null) return null;
        if (w.DowntimeToUtc is { } to) return to;
        if (roomBlockedNow) return now;
        return w.VerifiedUtc ?? w.DoneUtc ?? now;
    }

    public static IReadOnlyList<MntDowntimeRow> DowntimeByRoom(IEnumerable<MntOrder> orders, ISet<int> blockedRoomIds, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(blockedRoomIds);
        var rows = new Dictionary<int, (string RoomNo, int Minutes, int Orders, bool Running)>();
        foreach (var w in orders.Where(w => w.RoomId is not null && w.DowntimeFromUtc is not null))
        {
            var blocked = blockedRoomIds.Contains(w.RoomId!.Value);
            var end = DowntimeEnd(w, blocked, now)!.Value;
            rows.TryGetValue(w.RoomId.Value, out var row);
            row = (w.RoomNo ?? string.Empty, row.Minutes + Math.Max(0, Minutes(w.DowntimeFromUtc!.Value, end)), row.Orders + 1, row.Running || (w.DowntimeToUtc is null && blocked));
            rows[w.RoomId.Value] = row;
        }

        return rows.Select(r => new MntDowntimeRow(r.Key, r.Value.RoomNo, r.Value.Minutes, r.Value.Orders, r.Value.Running))
            .OrderByDescending(r => r.Minutes).ThenBy(r => r.RoomNo, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<(string Kind, int Open, int Total)> ByKind(IEnumerable<MntOrder> orders, IReadOnlyDictionary<int, MntAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        var map = new Dictionary<string, (int Open, int Total)>(StringComparer.Ordinal);
        foreach (var w in orders)
        {
            var k = w.AssetId is { } a && assets.TryGetValue(a, out var asset) ? asset.Kind : "Other";
            map.TryGetValue(k, out var r);
            map[k] = (r.Open + (w.IsActive ? 1 : 0), r.Total + 1);
        }

        return map.Select(kv => (kv.Key, kv.Value.Open, kv.Value.Total)).OrderByDescending(r => r.Total).ThenBy(r => r.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>Parts cost (paise) of work orders completed in the business month.</summary>
    public static long MonthPartsCost(IEnumerable<MntOrder> orders, DateOnly businessDate) =>
        orders.Where(w => w.DoneUtc is { } d && d.Year == businessDate.Year && d.Month == businessDate.Month).Sum(w => w.PartsCostPaise);

    public static MntSummary Summary(IEnumerable<MntOrder> orders, DateTime now)
    {
        var list = orders.ToList();
        int C(string s) => list.Count(w => w.Status == s);
        return new MntSummary(C(MntStatus.Open), C(MntStatus.InProgress), C(MntStatus.OnHold), C(MntStatus.Done), C(MntStatus.Verified), SlaBreaches(list, now));
    }

    /// <summary>Active first (P1 first, oldest first), then Done (awaiting verification), then closed (newest first).</summary>
    public static IReadOnlyList<MntOrder> Sort(IEnumerable<MntOrder> orders)
    {
        static int Rank(MntOrder w) => w.Status switch { MntStatus.InProgress => 0, MntStatus.Open => 1, MntStatus.OnHold => 2, MntStatus.Done => 3, _ => 4 };
        return orders.OrderBy(Rank)
            .ThenBy(w => Rank(w) < 4 ? w.Priority : string.Empty, StringComparer.Ordinal)
            .ThenBy(w => Rank(w) < 4 ? w.CreatedUtc : DateTime.MinValue)
            .ThenByDescending(w => Rank(w) < 4 ? DateTime.MinValue : w.CreatedUtc)
            .ThenBy(w => w.Id)
            .ToList();
    }
}

/// <summary>Work-order state machine (prototype handlers). Every method returns the new order or throws a typed HccException.</summary>
public static class MntMachine
{
    private static HccException Invalid(string message) => new(HccErrorCode.INVALID_TRANSITION, message);

    private static string Text(string? v, int min, int max, string message)
    {
        var t = (v ?? string.Empty).Trim();
        return t.Length < min || t.Length > max ? throw HccException.Validation(message) : t;
    }

    public static string Title(string? v) => Text(v, 3, 200, "Describe the fault (3–200 characters).");

    public static string Priority(string? p) => p is not null && MntPriority.All.Contains(p) ? p : throw HccException.Validation("Choose a priority (P1–P4).");

    public static MntOrder Assign(MntOrder w, int? technicianId, int? vendorId)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (!w.IsActive && w.Status != MntStatus.Done) throw Invalid("This work order is closed.");
        if ((technicianId is null) == (vendorId is null)) throw HccException.Validation("Choose a technician or a vendor.");
        return w with { AssigneeId = technicianId, VendorId = vendorId };
    }

    public static (MntOrder Order, bool Resumed) Start(MntOrder w, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (w.Status is not (MntStatus.Open or MntStatus.OnHold)) throw Invalid("Only an open or on-hold work order can start.");
        if (w.AssigneeId is null && w.VendorId is null) throw Invalid("Assign a technician or vendor first.");
        return (w with { Status = MntStatus.InProgress, StartedUtc = w.StartedUtc ?? now, HoldReason = null, AwaitingSpareId = null }, w.Status == MntStatus.OnHold);
    }

    public static MntOrder Hold(MntOrder w, string? reason, int? awaitingSpareId)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (w.Status != MntStatus.InProgress) throw Invalid("Only work in progress can be put on hold.");
        var r = Text(reason, 3, 200, "Enter why the work is on hold (3–200 characters).");
        return w with { Status = MntStatus.OnHold, HoldReason = r, AwaitingSpareId = awaitingSpareId };
    }

    public static int PartQty(int qty) => qty is >= 1 and <= 99 ? qty : throw HccException.Validation("Enter a quantity between 1 and 99.");

    public static MntOrder AddPart(MntOrder w, MntSpare spare, int qty)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(spare);
        if (w.Status is not (MntStatus.InProgress or MntStatus.OnHold)) throw Invalid("Parts can be booked only while work is open.");
        PartQty(qty);
        if (qty > spare.Stock) throw new HccException(HccErrorCode.NOT_AVAILABLE, $"Only {spare.Stock.ToString(CultureInfo.InvariantCulture)} {spare.Unit} of {spare.Name} in stock.");
        var parts = w.Parts.ToList();
        var i = parts.FindIndex(p => p.SpareId == spare.Id && p.UnitCostPaise == spare.CostPaise);
        if (i >= 0) parts[i] = parts[i] with { Qty = parts[i].Qty + qty };
        else parts.Add(new MntPart(spare.Id, qty, spare.CostPaise));
        return w with { Parts = parts, AwaitingSpareId = w.AwaitingSpareId == spare.Id ? null : w.AwaitingSpareId };
    }

    public static MntOrder Complete(MntOrder w, string? note, int userId, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (w.Status != MntStatus.InProgress) throw Invalid("Start the work before completing it.");
        var n = Text(note, 3, 500, "Describe the work done (3–500 characters).");
        return w with { Status = MntStatus.Done, DoneUtc = now, DoneBy = userId, Note = n, ReturnReason = null };
    }

    /// <summary>Four-eyes: the completer or the assignee cannot verify (FORBIDDEN). Return needs a reason.</summary>
    public static MntOrder Verify(MntOrder w, bool approve, string? reason, int userId, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (w.Status != MntStatus.Done) throw Invalid("Only completed work can be verified.");
        if (w.DoneBy == userId || w.AssigneeId == userId) throw new HccException(HccErrorCode.FORBIDDEN, "You cannot verify your own work. Ask another supervisor.");
        if (!approve)
        {
            var r = Text(reason, 3, 500, "Enter why the work is returned (3–500 characters).");
            return w with { Status = MntStatus.InProgress, ReturnReason = r, DoneUtc = null, DoneBy = null };
        }

        return w with { Status = MntStatus.Verified, VerifiedUtc = now, VerifiedBy = userId };
    }

    public static MntOrder Cancel(MntOrder w, string? reason)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (w.Status is not (MntStatus.Open or MntStatus.OnHold)) throw Invalid("Only open or on-hold work orders can be cancelled.");
        return w with { Status = MntStatus.Cancelled, CancelReason = Text(reason, 3, 200, "Enter a cancellation reason (3–200 characters).") };
    }

    public static void EnsureCanBlock(MntOrder w)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (w.RoomId is null) throw HccException.Validation("This work order is not for a guest room.");
        if (!w.IsActive && w.Status != MntStatus.Done) throw Invalid("This work order is closed.");
    }

    public static string BlockReason(MntOrder w)
    {
        ArgumentNullException.ThrowIfNull(w);
        var r = $"Maintenance · {w.No} · {w.Title}";
        return r.Length <= 200 ? r : r[..200];
    }
}
