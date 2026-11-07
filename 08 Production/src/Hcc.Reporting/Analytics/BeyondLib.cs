// F12 MEAS · meas-study-b · server twin of 03 Prototype/src/domain/analytics/beyond-lib.ts. Same names, same integer half-up arithmetic, same numbers
// (parity test: Goldens/study-b-an.json, produced by the prototype from a deterministic input). Pure: AnInput in, tables out.
using System.Globalization;
using Hcc.Application.Reporting.Rpt;

namespace Hcc.Reporting.Analytics;

public sealed class AnRoom { public string Id { get; init; } = ""; public string Type { get; init; } = ""; public string InServiceFrom { get; init; } = ""; public string? InServiceTo { get; init; } public bool Suite { get; init; } }

public sealed class AnStay
{
    public string Id { get; init; } = ""; public string GuestKey { get; init; } = ""; public string Room { get; init; } = ""; public string From { get; init; } = ""; public string To { get; init; } = "";
    public int Nights { get; init; } public long RatePaise { get; init; } public string Source { get; init; } = ""; public string? Channel { get; init; } public string? Company { get; init; } public string? Purpose { get; init; }
    public string Status { get; init; } = ""; public bool Complimentary { get; init; } public bool Legacy { get; init; }
    public string? BookedType { get; init; } public string? City { get; init; } public string? State { get; init; } public string? Nationality { get; init; } public string? MealPlan { get; init; }
    public string? InTime { get; init; } public string? OutTime { get; init; } public bool IdCaptured { get; init; } public bool PhoneCaptured { get; init; } public int? People { get; init; }
}

public sealed class AnBill
{
    public string Id { get; init; } = ""; public string Stay { get; init; } = ""; public string Date { get; init; } = ""; public long RoomChargePaise { get; init; } public long FnbPaise { get; init; } public long OtherPaise { get; init; }
    public long DiscountPaise { get; init; } public long GstPaise { get; init; } public long TotalPaise { get; init; } public int ChargedNights { get; init; } public long PaidPaise { get; init; } public bool Legacy { get; init; }
}

public sealed class AnReceipt { public string Id { get; init; } = ""; public string Bill { get; init; } = ""; public string Date { get; init; } = ""; public string Method { get; init; } = ""; public long AmountPaise { get; init; } public bool Legacy { get; init; } }
public sealed class AnWindow { public string From { get; init; } = ""; public string To { get; init; } = ""; }

public sealed class AnInput
{
    public List<AnRoom> Rooms { get; init; } = []; public List<AnStay> Stays { get; init; } = []; public List<AnBill> Bills { get; init; } = []; public List<AnReceipt> Receipts { get; init; } = [];
    public string AsOf { get; init; } = ""; public string LegacyUntil { get; init; } = "2026-10-30"; public AnWindow CovidWindow { get; init; } = new() { From = "2020-03-01", To = "2021-03-31" };
}

public sealed record HallEvent(string Date, int Days, long RevenuePaise);
public sealed record ExpenseFact(string Date, string Category, long AmountPaise);
public sealed record AuditFact(string Action, int Count);
public sealed record LicenceFact(string Name, string? ValidTo);

public sealed class BeyondExtras
{
    public List<HallEvent> HallEvents { get; init; } = []; public List<ExpenseFact> Expenses { get; init; } = []; public List<AuditFact> Audit { get; init; } = []; public List<LicenceFact> Licences { get; init; } = [];
}

/// <summary>Text parameters (as bound by the report engine) plus the extras the caller could supply.</summary>
public sealed class AnParams
{
    public Dictionary<string, string> Values { get; init; } = new(StringComparer.Ordinal); public BeyondExtras Extras { get; init; } = new();
    public string? Get(string key) => Values.TryGetValue(key, out var v) && v.Length > 0 ? v : null;
    public double Num(string key, double fallback = double.NaN) => Get(key) is { } s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;
}

public sealed record AnSection(string Key, string Title, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<ReportRow> Rows);
public sealed record AnResult(IReadOnlyList<AnSection> Sections, bool Legacy);

public sealed record Col(string Key, string Label, ReportColumnType Type, bool Money = false, ReportMask Mask = ReportMask.None);
public sealed class Tot { public object?[] Cells { get; } public Tot(object?[] cells) => Cells = cells; }

public static class Bl
{
    public const string OtaFree = "Direct / walk-in";
    public static readonly string[] Dow = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
    public static readonly string[] MonFy = ["Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec", "Jan", "Feb", "Mar"];

    // ── integer maths (half-up, doubles exactly as in the prototype) ──
    public static double? Pct1(double n, double d) => d > 0 ? Math.Floor((n * 2000 + d) / (2 * d)) / 10 : null;
    public static long? Ratio(double n, double d) => d > 0 ? (long)Math.Floor((2 * n + d) / (2 * d)) : null;
    public static double? Avg1(double n, double d) => d > 0 ? Math.Floor((n * 20 + d) / (2 * d)) / 10 : null;
    public static long Sum<T>(IEnumerable<T> xs, Func<T, long> f) => xs.Sum(f);

    // ── dates ──
    public static DateTime P(string d) => DateTime.ParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string D(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string AddDays(string d, int n) => D(P(d).AddDays(n));
    public static int DaysBetween(string a, string b) => (int)Math.Round((P(b) - P(a)).TotalDays);
    public static string FyOf(string d) { var y = int.Parse(d[..4], CultureInfo.InvariantCulture); var m = int.Parse(d.Substring(5, 2), CultureInfo.InvariantCulture); var s = m >= 4 ? y : y - 1; return $"{s}-{(s + 1) % 100:D2}"; }
    public static string FyStart(string fy) => fy[..4] + "-04-01";
    public static string FyEnd(string fy) => (int.Parse(fy[..4], CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture) + "-03-31";
    public static string MonthEnd(string m) => D(P(m + "-01").AddMonths(1).AddDays(-1));
    public static string NextMonth(string m) => D(P(m + "-01").AddMonths(1))[..7];
    public static string PrevMonth(string m) => D(P(m + "-01").AddMonths(-1))[..7];
    public static int DowOf(string d) => ((int)P(d).DayOfWeek + 6) % 7;
    public static List<string> MonthsBetween(string from, string to) { var o = new List<string>(); for (var m = from[..7]; string.CompareOrdinal(m, to[..7]) <= 0; m = NextMonth(m)) o.Add(m); return o; }
    public static string MonthLabel(string m) => MonFy[(int.Parse(m.Substring(5, 2), CultureInfo.InvariantCulture) + 8) % 12] + " " + m.Substring(2, 2);
    public static bool Le(string a, string b) => string.CompareOrdinal(a, b) <= 0;
    public static bool Lt(string a, string b) => string.CompareOrdinal(a, b) < 0;
    public static string Norm(string? s) => (s ?? "").Trim();

    // ── tables ──
    public static ReportColumn C(Col c) => new(c.Key, c.Label, c.Type, c.Money, c.Mask);
    public static Col T(string k, string l) => new(k, l, ReportColumnType.Text);
    public static Col I(string k, string l) => new(k, l, ReportColumnType.Int);
    public static Col M(string k, string l) => new(k, l, ReportColumnType.Money, true);
    public static Col Pc(string k, string l) => new(k, l, ReportColumnType.Percent);
    public static Col Dt(string k, string l) => new(k, l, ReportColumnType.Date);
    public static object?[] R(params object?[] cells) => cells;
    public static Tot Total(params object?[] cells) => new(cells);

    public static AnSection Sec(string key, string title, Col[] spec, IEnumerable<object> rows)
    {
        var cols = spec.Select(C).ToList();
        var outRows = new List<ReportRow>();
        foreach (var r in rows)
        {
            var isTot = r is Tot; var cells = isTot ? ((Tot)r).Cells : (object?[])r;
            var d = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < cols.Count; i++) d[cols[i].Key] = i < cells.Length ? cells[i] : null;
            outRows.Add(new ReportRow(isTot ? ReportRowKind.Total : ReportRowKind.Row, d));
        }
        return new AnSection(key, title, cols, outRows);
    }

    public static AnSection Series(string name, string kind, string title, IEnumerable<string> labels, IEnumerable<double?> values, bool money = false, bool pct = false)
    {
        var ls = labels.ToList(); var vs = values.ToList();
        return Sec("series:" + name, $"[{kind}] {title}", [T("label", "Label"), new Col("value", title, pct ? ReportColumnType.Percent : money ? ReportColumnType.Money : ReportColumnType.Int, money)], ls.Select((l, i) => (object)R(l, i < vs.Count ? vs[i] : null)));
    }

    public static AnResult Finish(List<AnSection> secs, bool legacy) => new(secs, legacy);
}

public sealed record BM(AnBill B, AnStay? St, string Date, string Month, string Fy, string Type, string Channel, long Rev, long Cn, long Nights, long Gross, string Seg);

public sealed class Ctx
{
    private static readonly HashSet<string> NotSlept = new(StringComparer.Ordinal) { "cancelled", "canceled", "no-show", "noshow", "no show", "duplicate", "expired", "held", "confirmed" };
    public AnInput Inp { get; }
    public Dictionary<string, AnStay> StayBy { get; }
    public Dictionary<string, string> Rtype { get; }
    public List<(string Room, string Date, string Type, string Stay)> Slept { get; } = [];
    public string DataStart { get; }
    public string End { get; }
    public List<string> Fys { get; } = [];
    public List<BM> Bills { get; }
    private readonly Dictionary<string, List<string>> _on = new(StringComparer.Ordinal);

    public Ctx(AnInput inp)
    {
        Inp = inp;
        StayBy = inp.Stays.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.Last());
        Rtype = inp.Rooms.ToDictionary(r => r.Id, r => r.Suite ? "Suite" : r.Type);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var st in inp.Stays)
        {
            if (NotSlept.Contains(st.Status.ToLowerInvariant()) || !Rtype.ContainsKey(st.Room)) continue;
            var n = Math.Max(st.Nights, 1);
            for (var i = 0; i < n; i++) { var date = Bl.AddDays(st.From, i); var k = st.Room + "|" + date; if (string.CompareOrdinal(date, inp.AsOf) > 0 || !seen.Add(k)) continue; Slept.Add((st.Room, date, Rtype[st.Room], st.Id)); }
        }
        var ds = inp.AsOf; foreach (var s in Slept) if (Bl.Lt(s.Date, ds)) ds = s.Date;
        foreach (var b in inp.Bills) if (Bl.Lt(b.Date, ds) && b.RoomChargePaise > 0) ds = b.Date;
        DataStart = ds; End = inp.AsOf;
        for (var d = DataStart; Bl.Le(d, End); d = Bl.AddDays(d, 28)) { var f = Bl.FyOf(d); if (!Fys.Contains(f)) Fys.Add(f); }
        if (!Fys.Contains(Bl.FyOf(End))) Fys.Add(Bl.FyOf(End));
        var groupSize = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in inp.Stays) { var k = s.GuestKey + "|" + s.From + "|" + s.To; groupSize[k] = groupSize.GetValueOrDefault(k) + 1; }
        Bills = inp.Bills.Select(b =>
        {
            StayBy.TryGetValue(b.Stay, out var st);
            var channel = !string.IsNullOrEmpty(st?.Channel) ? st!.Channel! : Bl.OtaFree; var gross = b.RoomChargePaise + b.FnbPaise + b.OtherPaise;
            string seg;
            if (st is null) seg = Bl.OtaFree;
            else if (b.ChargedNights >= 7) seg = "Long stay (7+ nights)";
            else if (groupSize.GetValueOrDefault(st.GuestKey + "|" + st.From + "|" + st.To, 1) >= 3) seg = "Group (3+ rooms)";
            else if (!string.IsNullOrEmpty(st.Company)) seg = "Corporate (company on bill)";
            else if ((st.Purpose ?? "").ToLowerInvariant() == "official") seg = "Official (work purpose)";
            else if (channel != Bl.OtaFree) seg = "OTA (" + channel + ")";
            else seg = Bl.OtaFree;
            return new BM(b, st, b.Date, b.Date[..7], Bl.FyOf(b.Date), st is not null && Rtype.TryGetValue(st.Room, out var t) ? t : "", channel, b.RoomChargePaise, b.RoomChargePaise > 0 ? b.ChargedNights : 0, b.ChargedNights, gross, seg);
        }).ToList();
    }

    public bool Covid(string d) => Bl.Le(Inp.CovidWindow.From, d) && Bl.Le(d, Inp.CovidWindow.To);

    public List<string> RoomsOn(string d)
    {
        if (!_on.TryGetValue(d, out var r)) { r = Inp.Rooms.Where(x => Bl.Le(x.InServiceFrom, d) && (x.InServiceTo is null || Bl.Le(d, x.InServiceTo))).Select(x => x.Id).ToList(); _on[d] = r; }
        return r;
    }

    public long Capacity(string from, string to, string? type = null, bool normalOnly = false)
    {
        long n = 0; var e = Bl.Lt(End, to) ? End : to;
        for (var d = Bl.Lt(from, DataStart) ? DataStart : from; Bl.Le(d, e); d = Bl.AddDays(d, 1))
        {
            if (normalOnly && Covid(d)) continue;
            var rs = RoomsOn(d); n += type is null ? rs.Count : rs.Count(r => Rtype[r] == type);
        }
        return n;
    }

    public long SleptIn(string from, string to, string? type = null, bool normalOnly = false) =>
        Slept.Count(s => Bl.Le(from, s.Date) && Bl.Le(s.Date, to) && (type is null || s.Type == type) && !(normalOnly && Covid(s.Date)));

    public string SrcOf(string from, string to) => Bl.Le(to, Inp.LegacyUntil) ? "Legacy" : Bl.Lt(Inp.LegacyUntil, from) ? "HCC live" : "Legacy + live";
    public List<string> FyMonths(string fy) => Bl.MonthsBetween(Bl.FyStart(fy), Bl.FyEnd(fy)).Where(m => Bl.Le(DataStart, Bl.MonthEnd(m)) && Bl.Le(m + "-01", End)).ToList();
    public string FyParam(AnParams p) => p.Get("fy") ?? Bl.FyOf(End);
    public IReadOnlyList<AnStay> Stays => Inp.Stays;
}
