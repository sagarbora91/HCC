using System.Globalization;
using System.Text.RegularExpressions;

namespace Hcc.Domain.GuestServices;

/// <summary>Request, complaint and lost-item vocabularies (prototype <c>GSV_*</c> constants, services.ts).</summary>
public static class GuestServiceVocab
{
    public static readonly IReadOnlyList<string> RequestTypes = ["Extra towels", "Drinking water", "Iron & board", "Wake-up call", "Taxi", "Toiletries & amenities", "Minor repair", "Other"];
    public const string Housekeeping = "Housekeeping";
    public const string Maintenance = "Maintenance";
    public const string FrontDesk = "Front desk";
    public static readonly IReadOnlyList<string> Depts = [Housekeeping, Maintenance, FrontDesk];
    public const string WakeUp = "Wake-up call";
    public const string Other = "Other";
    public const string MinorRepair = "Minor repair";

    public const string Open = "Open";
    public const string Assigned = "Assigned";
    public const string InProgress = "InProgress";
    public const string Done = "Done";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlyList<string> ComplaintCategories = ["Room cleanliness", "Maintenance", "Noise", "Staff behaviour", "Food & beverage", "Billing", "Amenities", "Other"];
    public static readonly IReadOnlyList<string> Severities = ["Low", "Medium", "High", "Critical"];
    public const string Investigating = "Investigating";
    public const string Resolved = "Resolved";
    public const string Closed = "Closed";

    public static readonly IReadOnlyList<string> ItemCategories = ["Jewellery", "Electronics", "Cash / wallet", "Documents", "Clothing", "Personal care", "Other"];
    public static readonly IReadOnlySet<string> ValuableCategories = new HashSet<string>(["Jewellery", "Electronics", "Cash / wallet", "Documents"], StringComparer.Ordinal);
    public static readonly IReadOnlyList<string> Storage = ["Front office safe", "Housekeeping store", "Back office cupboard"];
    public const string Safe = "Front office safe";
    public static readonly IReadOnlyList<string> IdTypes = ["Aadhaar (fictional)", "Passport (fictional)", "Driving licence (fictional)", "Voter ID (fictional)"];
    public static readonly IReadOnlyList<string> Disposal = ["Donated", "Destroyed", "Handed to police"];
    public const string Stored = "Stored";
    public const string Claimed = "Claimed";
    public const string HandedOver = "HandedOver";
    public const string Disposed = "Disposed";
}

/// <summary>Per-property settings (prototype <c>GSVSettings</c> + defaults).</summary>
public sealed record GuestServiceSettings(IReadOnlyDictionary<string, int> Sla, int RetentionDays, int ValuableRetentionDays)
{
    public static GuestServiceSettings Default { get; } = new(new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["Extra towels"] = 20, ["Drinking water"] = 10, ["Iron & board"] = 15, ["Wake-up call"] = 5, ["Taxi"] = 30,
        ["Toiletries & amenities"] = 20, ["Minor repair"] = 45, ["Other"] = 30,
    }, 90, 180);

    /// <summary>Stored values over the defaults (a stored SLA map may omit types).</summary>
    public static GuestServiceSettings Merge(IReadOnlyDictionary<string, int>? sla, int? retention, int? valuable)
    {
        var map = new Dictionary<string, int>(Default.Sla, StringComparer.Ordinal);
        if (sla is not null) foreach (var (k, v) in sla) map[k] = v;
        return new GuestServiceSettings(map, retention ?? Default.RetentionDays, valuable ?? Default.ValuableRetentionDays);
    }
}

public sealed record SlaLabel(string Tone, string Label, int? MinutesLeft);

/// <summary>A request reduced to what the SLA and report rules need.</summary>
public sealed record RequestFact(string Type, string Dept, string Status, DateTime CreatedUtc, DateTime DueUtc, DateTime? ScheduledUtc, DateTime? DoneUtc);

public sealed record ResponseFacts(string Key, int Count, int Done, int AvgMinutes, int WithinSla, int Pct, int Open, int Overdue);

public sealed record ComplaintFact(string Category, string Severity, string Status, bool HasRecovery);

/// <summary>
/// Pure rules ported from the prototype streams (<c>03 Prototype/src/domain/streams/services.ts</c>): SLA minutes and labels,
/// retention and disposal dates, the ID-note rule and the response / complaint / lost-and-found figures.
/// </summary>
public static partial class GuestServiceRules
{
    public static string DefaultDept(string type) => type == GuestServiceVocab.MinorRepair ? GuestServiceVocab.Maintenance
        : type is GuestServiceVocab.WakeUp or "Taxi" or GuestServiceVocab.Other ? GuestServiceVocab.FrontDesk : GuestServiceVocab.Housekeeping;

    /// <summary>SLA minutes for a type; Urgent halves it (minimum 5).</summary>
    public static int SlaMinutes(GuestServiceSettings settings, string type, bool urgent)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var m = settings.Sla.TryGetValue(type, out var v) ? v : 30;
        return urgent ? Math.Max(5, (int)Math.Round(m / 2.0, MidpointRounding.AwayFromZero)) : m;
    }

    public static bool IsOpen(string status) => status is not (GuestServiceVocab.Done or GuestServiceVocab.Cancelled);

    public static bool IsValuable(string category) => GuestServiceVocab.ValuableCategories.Contains(category);

    public static bool IsOverdue(RequestFact r, DateTime nowUtc) => IsOpen(r.Status) && nowUtc > r.DueUtc;

    public static bool IsLate(RequestFact r) => r.Status == GuestServiceVocab.Done && r.DoneUtc is { } d && d > r.DueUtc;

    public static int MinutesBetween(DateTime from, DateTime to) => (int)Math.Round((to - from).TotalMinutes, MidpointRounding.AwayFromZero);

    /// <summary>Prototype <c>gsvSlaLabel</c>: green within SLA, orange done late / due in ≤ 5 min, red overdue, blue scheduled or running.</summary>
    public static SlaLabel Label(RequestFact r, DateTime nowUtc, string propertyTimeZone = "UTC")
    {
        ArgumentNullException.ThrowIfNull(r);
        if (r.Status == GuestServiceVocab.Cancelled) return new("neutral", "Cancelled", null);
        if (r.Status == GuestServiceVocab.Done)
        {
            var m = MinutesBetween(r.CreatedUtc, r.DoneUtc!.Value);
            return IsLate(r) ? new("orange", $"Done late · {m} min", null) : new("green", $"Within SLA · {m} min", null);
        }

        if (r.ScheduledUtc is { } s && nowUtc < s && MinutesBetween(nowUtc, s) > 60)
            return new("blue", "Scheduled " + TimeZoneInfo.ConvertTimeFromUtc(s, TimeZoneInfo.FindSystemTimeZoneById(propertyTimeZone)).ToString("HH:mm", CultureInfo.InvariantCulture), null);
        var left = MinutesBetween(nowUtc, r.DueUtc);
        if (left < 0) return new("red", $"Overdue · {-left} min", left);
        if (left <= 5) return new("orange", $"Due in {left} min", left);
        return new("blue", $"Due in {left} min", left);
    }

    /// <summary>First calendar date on which a stored item may be disposed: found date + retention (valuables longer).</summary>
    public static DateOnly DisposeOn(DateOnly foundDate, bool valuable, GuestServiceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return foundDate.AddDays(valuable ? settings.ValuableRetentionDays : settings.RetentionDays);
    }

    public static bool IsDisposable(string status, DateOnly today, DateOnly foundDate, bool valuable, GuestServiceSettings settings)
        => status == GuestServiceVocab.Stored && today >= DisposeOn(foundDate, valuable, settings);

    [GeneratedRegex(@"\d{6,}")]
    private static partial Regex LongDigits();

    /// <summary>The ID-check note keeps a short reference only: at least 3 characters, never a run of 6+ digits (spaces and dashes ignored).</summary>
    public static bool IdNoteOk(string? note)
    {
        var n = (note ?? string.Empty).Trim();
        return n.Length >= 3 && !LongDigits().IsMatch(n.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal));
    }

    /// <summary>Digits-only phone (spaces and dashes removed), 10–13 digits with an optional leading +; null when invalid.</summary>
    public static string? NormalizePhone(string? phone)
    {
        var p = (phone ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        return PhonePattern().IsMatch(p) ? p : null;
    }

    [GeneratedRegex(@"^\+?\d{10,13}$")]
    private static partial Regex PhonePattern();

    /// <summary>Response figures per request type or department (cancelled requests excluded; groups without requests dropped).</summary>
    public static IReadOnlyList<ResponseFacts> ResponseReport(IEnumerable<RequestFact> requests, DateTime nowUtc, bool byDept)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var all = requests.Where(r => r.Status != GuestServiceVocab.Cancelled).ToList();
        var keys = byDept ? GuestServiceVocab.Depts : GuestServiceVocab.RequestTypes;
        return keys.Select(k =>
        {
            var rs = all.Where(r => (byDept ? r.Dept : r.Type) == k).ToList();
            var done = rs.Where(r => r.Status == GuestServiceVocab.Done).ToList();
            var mins = done.Select(r => MinutesBetween(r.CreatedUtc, r.DoneUtc!.Value)).ToList();
            var within = done.Count(r => !IsLate(r));
            return new ResponseFacts(k, rs.Count, done.Count, mins.Count == 0 ? 0 : (int)Math.Round(mins.Average(), MidpointRounding.AwayFromZero), within,
                done.Count == 0 ? 0 : (int)Math.Round(within * 100.0 / done.Count, MidpointRounding.AwayFromZero), rs.Count(r => IsOpen(r.Status)), rs.Count(r => IsOverdue(r, nowUtc)));
        }).Where(r => r.Count > 0).ToList();
    }

    /// <summary>Totals over all non-cancelled requests (prototype <c>gsvResponseTotals</c>).</summary>
    public static (int Done, int Within, int Pct, int Avg, int Open, int Overdue) ResponseTotals(IEnumerable<RequestFact> requests, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var list = requests.ToList();
        var rows = ResponseReport(list, nowUtc, byDept: true);
        var done = rows.Sum(r => r.Done);
        var within = rows.Sum(r => r.WithinSla);
        var mins = list.Where(r => r.Status == GuestServiceVocab.Done).Select(r => MinutesBetween(r.CreatedUtc, r.DoneUtc!.Value)).ToList();
        return (done, within, done == 0 ? 0 : (int)Math.Round(within * 100.0 / done, MidpointRounding.AwayFromZero),
            mins.Count == 0 ? 0 : (int)Math.Round(mins.Average(), MidpointRounding.AwayFromZero), rows.Sum(r => r.Open), rows.Sum(r => r.Overdue));
    }

    /// <summary>Complaint counts per category with severity split and number carrying a recovery.</summary>
    public static IReadOnlyList<(string Category, int Total, int Open, IReadOnlyDictionary<string, int> BySeverity, int Recovery)> ComplaintReport(IEnumerable<ComplaintFact> complaints)
    {
        ArgumentNullException.ThrowIfNull(complaints);
        var cs = complaints.ToList();
        return GuestServiceVocab.ComplaintCategories.Select(cat =>
        {
            var xs = cs.Where(c => c.Category == cat).ToList();
            return (cat, xs.Count, xs.Count(c => c.Status != GuestServiceVocab.Closed),
                (IReadOnlyDictionary<string, int>)GuestServiceVocab.Severities.ToDictionary(v => v, v => xs.Count(c => c.Severity == v), StringComparer.Ordinal),
                xs.Count(c => c.HasRecovery));
        }).Where(r => r.Item2 > 0).ToList();
    }
}
