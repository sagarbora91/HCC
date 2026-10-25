using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Handover;

/// <summary>Checklist item as defined on a template (also the snapshot inside a run).</summary>
public sealed record HovItemDef(string Key, string Text, bool Mandatory, string? Due, bool Photo, string? Link);

/// <summary>Checklist item of a run: the snapshot plus its tick state.</summary>
public sealed record HovItemState(HovItemDef Def, bool Done, int? DoneBy, DateTime? DoneUtc, string? Note, string? PhotoName)
{
    public string Key => Def.Key;
}

/// <summary>A run (saved) or an active template not yet ticked today (<see cref="Started"/> false), with every figure the rules need.</summary>
public sealed record HovRun(long? RunId, long TemplateId, int TemplateVersion, string Name, string Desk, string Shift, IReadOnlyList<string> Roles, DateOnly Date,
    bool Open, bool Started, int? StartedBy, int? SubmittedBy, DateTime? SubmittedUtc, bool? Late, string? ReopenReason, IReadOnlyList<HovItemState> Items);

public sealed record HovProgress(int Total, int Done, int Pct, int Mandatory, int MandatoryDone, int Overdue, bool Late);

public sealed record HovDashRow(string Key, int Runs, int Submitted, int Items, int Done, int Pct, int Overdue, int Late, int NotStarted);

/// <summary>
/// Shift handover rules, ported from the prototype (<c>domain/streams/handover.ts</c> + <c>handlers/handover.ts</c>): shift windows on the
/// property clock (Asia/Kolkata, UTC+05:30, no DST), desks, template validation, overdue/late/progress, dashboard. No I/O.
/// </summary>
public static partial class HandoverRules
{
    public const string Morning = "Morning", Evening = "Evening", Night = "Night";
    public const string FrontDesk = "Front desk", Housekeeping = "Housekeeping", Maintenance = "Maintenance";
    public const int MaxItems = 25, MaxCarry = 8, NoteMax = 200, TextMax = 400, ItemTextMax = 120;
    public const long MaxCashPaise = 100_000_000;

    public static readonly IReadOnlyList<string> Shifts = [Morning, Evening, Night];
    public static readonly IReadOnlyList<string> Desks = [FrontDesk, Housekeeping, Maintenance];

    /// <summary>Roles of each desk by role code (Owner/Manager see every desk; Manager is a Front desk role).</summary>
    public static IReadOnlyList<string> DeskRoles(string desk) => desk switch
    {
        FrontDesk => ["REC", "CSH", "MGR"],
        Housekeeping => ["SUP", "HKP"],
        Maintenance => ["MNT", "SUP"],
        _ => throw HccException.Validation("Choose a desk."),
    };

    public static readonly IReadOnlySet<string> AssignableRoles = new HashSet<string>(["OWN", "MGR", "REC", "CSH", "SUP", "HKP", "MNT"], StringComparer.Ordinal);

    // ── Clock & shifts ──
    private static readonly TimeSpan IstOffset = new(5, 30, 0);

    /// <summary>Property-local clock value (IST) of a UTC instant, kind Unspecified.</summary>
    public static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc + IstOffset, DateTimeKind.Unspecified);

    public static string ShiftAt(DateTime local)
    {
        var t = local.TimeOfDay;
        return t >= new TimeSpan(7, 0, 0) && t < new TimeSpan(15, 0, 0) ? Morning : t >= new TimeSpan(15, 0, 0) && t < new TimeSpan(23, 0, 0) ? Evening : Night;
    }

    public static string NextShift(string shift) => shift switch { Morning => Evening, Evening => Night, _ => Morning };

    private static (string Start, string End) Window(string shift) => shift switch
    {
        Morning => ("07:00", "15:00"),
        Evening => ("15:00", "23:00"),
        Night => ("23:00", "07:00"),
        _ => throw HccException.Validation("Choose a shift."),
    };

    /// <summary>Absolute local clock value of an HH:mm inside a shift of a business date (Night times before 23:00 roll to the next day).</summary>
    public static DateTime At(DateOnly date, string shift, string hhmm)
    {
        var t = TimeOnly.ParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture);
        var d = shift == Night && string.CompareOrdinal(hhmm, "23:00") < 0 ? date.AddDays(1) : date;
        return d.ToDateTime(t, DateTimeKind.Unspecified);
    }

    public static DateTime ShiftEnd(DateOnly date, string shift) => At(date, shift, Window(shift).End);

    public static bool IsValidHhmm(string? v) => v is not null && Hhmm().IsMatch(v);

    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex Hhmm();

    [GeneratedRegex(@"^[\w .()-]+\.(jpe?g|png|heic)$", RegexOptions.IgnoreCase)]
    private static partial Regex PhotoFile();

    public static bool IsValidPhotoName(string name) => name is not null && name.Length <= 80 && PhotoFile().IsMatch(name);

    // ── Desks / access ──
    public static bool InDesk(IReadOnlyCollection<string> roleCodes, bool manager, string desk) => manager || roleCodes.Intersect(DeskRoles(desk), StringComparer.Ordinal).Any();

    public static IReadOnlyList<string> DesksFor(IReadOnlyCollection<string> roleCodes, bool manager) => Desks.Where(d => InDesk(roleCodes, manager, d)).ToList();

    /// <summary>Owner/Manager use any checklist; others only when one of their role codes is on it (R1).</summary>
    public static bool CanUseTemplate(IReadOnlyCollection<string> roleCodes, bool manager, IReadOnlyCollection<string> templateRoles)
        => manager || roleCodes.Intersect(templateRoles, StringComparer.Ordinal).Any();

    // ── Template validation (R2) ──
    public sealed record CleanTemplate(string Name, string Desk, string Shift, IReadOnlyList<string> Roles, IReadOnlyList<HovItemDef> Items);

    public static CleanTemplate ValidateTemplate(string? name, string? desk, string? shift, IReadOnlyList<string>? roles, IReadOnlyList<(string? Text, bool Mandatory, string? Due, bool Photo, string? Link)>? items)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length is < 3 or > 60) throw HccException.Validation("Template name must be 3–60 characters.");
        if (desk is null || !Desks.Contains(desk)) throw HccException.Validation("Choose a desk.");
        if (shift is null || !Shifts.Contains(shift)) throw HccException.Validation("Choose a shift.");
        var rs = (roles ?? []).Select(r => (r ?? string.Empty).Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToList();
        if (rs.Count == 0) throw HccException.Validation("Choose at least one role for this checklist.");
        var deskRoles = DeskRoles(desk);
        if (!rs.All(r => AssignableRoles.Contains(r) && (deskRoles.Contains(r) || r == "OWN")))
            throw HccException.Validation($"Roles must belong to the {desk} desk ({string.Join(", ", deskRoles)}).");
        var list = items ?? [];
        if (list.Count is < 1 or > MaxItems) throw HccException.Validation($"A checklist needs 1–{MaxItems} items.");
        var clean = new List<HovItemDef>();
        for (var i = 0; i < list.Count; i++)
        {
            var text = (list[i].Text ?? string.Empty).Trim();
            if (text.Length is < 3 or > ItemTextMax) throw HccException.Validation($"Item {i + 1}: text must be 3–{ItemTextMax} characters.");
            var due = (list[i].Due ?? string.Empty).Trim();
            if (due.Length > 0 && !IsValidHhmm(due)) throw HccException.Validation($"Item {i + 1}: due time must be HH:mm (24-hour).");
            var link = (list[i].Link ?? string.Empty).Trim();
            if (link.Length > 40) throw HccException.Validation($"Item {i + 1}: link is too long.");
            clean.Add(new HovItemDef("i" + (i + 1).ToString(CultureInfo.InvariantCulture), text, list[i].Mandatory, due.Length > 0 ? due : null, list[i].Photo, link.Length > 0 ? link : null));
        }

        return new CleanTemplate(n, desk, shift, rs, clean);
    }

    // ── Runs ──
    public static bool ItemOverdue(HovRun r, HovItemState i, DateTime nowLocal)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(i);
        return !i.Done && i.Def.Due is not null && r.Open && At(r.Date, r.Shift, i.Def.Due) < nowLocal;
    }

    public static HovProgress Progress(HovRun r, DateTime nowLocal)
    {
        ArgumentNullException.ThrowIfNull(r);
        var total = r.Items.Count;
        var done = r.Items.Count(i => i.Done);
        var m = r.Items.Where(i => i.Def.Mandatory).ToList();
        var overdue = r.Items.Count(i => ItemOverdue(r, i, nowLocal));
        var late = r.Open ? ShiftEnd(r.Date, r.Shift) < nowLocal : r.Late == true;
        return new HovProgress(total, done, total == 0 ? 0 : RoundPct(done, total), m.Count, m.Count(i => i.Done), overdue, late);
    }

    /// <summary>Whole percent, half away from zero (JS Math.round on non-negative values).</summary>
    public static int RoundPct(int part, int whole) => (int)Math.Round(part * 100m / whole, MidpointRounding.AwayFromZero);

    /// <summary>Current shift first, then shift order, then name (prototype <c>hovRunsFor</c> ordering).</summary>
    public static IReadOnlyList<HovRun> Order(IEnumerable<HovRun> runs, string currentShift)
    {
        ArgumentNullException.ThrowIfNull(runs);
        return runs.OrderBy(r => ((r.Shift == currentShift ? 0 : 1) * 10) + ((IList<string>)Shifts.ToList()).IndexOf(r.Shift)).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static string NoteRef(string desk, DateOnly date, string shift)
    {
        ArgumentNullException.ThrowIfNull(desk);
        ArgumentNullException.ThrowIfNull(shift);
        return $"HOVN-{desk.Split(' ')[0].ToUpperInvariant()}-{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}-{shift[0]}";
    }

    // ── Dashboard (completion by role or desk) ──
    public static IReadOnlyList<HovDashRow> Dashboard(IEnumerable<HovRun> runs, string by, DateTime nowLocal)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var rows = new Dictionary<string, (int Runs, int Submitted, int Items, int Done, int Overdue, int Late, int NotStarted)>(StringComparer.Ordinal);
        foreach (var r in runs)
        {
            var p = Progress(r, nowLocal);
            foreach (var k in by == "role" ? r.Roles : [r.Desk])
            {
                rows.TryGetValue(k, out var x);
                rows[k] = (x.Runs + 1, x.Submitted + (r.Started && !r.Open ? 1 : 0), x.Items + p.Total, x.Done + p.Done, x.Overdue + p.Overdue, x.Late + (p.Late ? 1 : 0), x.NotStarted + (r.Started ? 0 : 1));
            }
        }

        return rows.Select(kv => new HovDashRow(kv.Key, kv.Value.Runs, kv.Value.Submitted, kv.Value.Items, kv.Value.Done,
            kv.Value.Items == 0 ? 0 : RoundPct(kv.Value.Done, kv.Value.Items), kv.Value.Overdue, kv.Value.Late, kv.Value.NotStarted)).ToList();
    }
}
