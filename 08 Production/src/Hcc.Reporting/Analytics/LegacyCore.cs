// F12 MEAS · meas-study-a · server twin of 03 Prototype/src/domain/analytics/legacy-core.ts (+ types.ts). Same method names, same integer arithmetic, same numbers.
// Dates are ISO strings ("2026-10-24") so ordinal comparison equals the prototype's. Money = long paise. Percent = double with one decimal.
using Hcc.Application.Reporting.Rpt;

namespace Hcc.Reporting.Analytics;

public sealed record AnRoom(string Id, string Type, string InServiceFrom, string? InServiceTo = null, bool Suite = false);

public sealed record AnStay(string Id, string GuestKey, string Room, string From, string To, int Nights, long RatePaise, string Source, string? Channel, string? Company, string? Purpose,
    string Status, bool Complimentary, bool Legacy);

public sealed record AnBill(string Id, string Stay, string Date, long RoomChargePaise, long FnbPaise, long OtherPaise, long DiscountPaise, long GstPaise, long TotalPaise, int ChargedNights,
    long PaidPaise, bool Legacy);

public sealed record AnReceipt(string Id, string Bill, string Date, string Method, long AmountPaise, bool Legacy);

public sealed record AnInput(IReadOnlyList<AnRoom> Rooms, IReadOnlyList<AnStay> Stays, IReadOnlyList<AnBill> Bills, IReadOnlyList<AnReceipt> Receipts, string AsOf, string LegacyUntil,
    string CovidFrom, string CovidTo);

/// <summary>Analytics parameters as bound by the engine (fy, from, to, baseFy, topN, asOf).</summary>
public sealed record AnParams(string? Fy = null, string? From = null, string? To = null, string? BaseFy = null, string? TopN = null, string? AsOf = null);

/// <summary>Where the AnInput comes from (production: SQL over stays, bills, receipts and lgc; see Integration needed in MEAS-STUDY-A-DONE).</summary>
public interface IAnalyticsInputSource
{
    Task<AnInput> LoadAsync(DateOnly asOf, CancellationToken ct = default);
}

/// <summary>Default registration until the SQL loader lands: an empty history, so the reports run and show empty tables.</summary>
public sealed class EmptyAnalyticsInputSource : IAnalyticsInputSource
{
    public Task<AnInput> LoadAsync(DateOnly asOf, CancellationToken ct = default) =>
        Task.FromResult(new AnInput([], [], [], [], asOf.ToString("yyyy-MM-dd"), "2026-10-30", "2020-03-01", "2021-03-31"));
}

public sealed record Tbl(string Key, string Title, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<ReportRow> Rows);

public sealed record Night(string Room, string Date, AnStay Stay);

public sealed record OccMonth(string Month, int Days, long Sold, long Capacity, long RoomsSold, long CapacityA, long CapacityB, int Stays, int SameDay, long NightsArrived);

public sealed class RevMonth
{
    public string Month = "";
    public int Bills;
    public long Room, Fnb, Other, Discount, Gst, Total, Received, Paid;
    public long ChargedNights;
}

public sealed class FyRow
{
    public RevMonth R = new();
    public string Fy = "";
    public long Sold, Capacity;
    public int Days;
    public bool Normal, Partial;
}

public static class LegacyCore
{
    public static ReportColumn T(string key, string label, ReportMask mask = ReportMask.None) => new(key, label, ReportColumnType.Text, false, mask);
    public static ReportColumn I(string key, string label) => new(key, label, ReportColumnType.Int, false);
    public static ReportColumn M(string key, string label) => new(key, label, ReportColumnType.Money, true);
    public static ReportColumn P(string key, string label) => new(key, label, ReportColumnType.Percent, false);

    // ── dates ──
    private static DateTime Parse(string d) => DateTime.ParseExact(d, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    public static string AddDays(string d, int n) => Parse(d).AddDays(n).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    public static string FyOf(string d)
    {
        var y = int.Parse(d[..4]); var m = int.Parse(d.Substring(5, 2)); var s = m >= 4 ? y : y - 1;
        return $"{s}-{(s + 1) % 100:00}";
    }
    public static int FyStartYear(string fy) => int.Parse(fy[..4]);
    public static string FyPrev(string fy) { var y = FyStartYear(fy) - 1; return $"{y}-{(y % 100) + 1:00}"; }
    public static string MonthOf(string d) => d[..7];
    public static int DaysInMonth(string m) => DateTime.DaysInMonth(int.Parse(m[..4]), int.Parse(m.Substring(5, 2)));
    public static string MonthStart(string m) => m + "-01";
    public static string MonthEnd(string m) => m + "-" + DaysInMonth(m).ToString("00");
    public static string MonthNext(string m) { var y = int.Parse(m[..4]); var mo = int.Parse(m.Substring(5, 2)); return mo == 12 ? $"{y + 1}-01" : $"{y}-{mo + 1:00}"; }
    public static int Dow(string d) => (int)Parse(d).DayOfWeek;
    public static readonly string[] DowLabel = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    public static readonly string[] FyMonths = ["04", "05", "06", "07", "08", "09", "10", "11", "12", "01", "02", "03"];
    public static readonly Dictionary<string, string> MonLabel = new()
    {
        ["01"] = "Jan", ["02"] = "Feb", ["03"] = "Mar", ["04"] = "Apr", ["05"] = "May", ["06"] = "Jun", ["07"] = "Jul", ["08"] = "Aug", ["09"] = "Sep", ["10"] = "Oct", ["11"] = "Nov", ["12"] = "Dec",
    };
    public static string MonthLabel(string m) => MonLabel[m[5..]] + " " + m[..4];
    public static string FyLabel(string fy) => "FY " + fy;
    public static List<string> SortedUnique(IEnumerable<string> xs) { var l = xs.Distinct(StringComparer.Ordinal).ToList(); l.Sort(StringComparer.Ordinal); return l; }
    public static int Cmp(string a, string b) => string.CompareOrdinal(a, b);

    // ── integer arithmetic ──
    public static long RDiv(long a, long b) => b == 0 ? 0 : (long)Math.Floor((2.0 * a + b) / (2.0 * b));
    public static double Pct1(long n, long d) => d == 0 ? 0 : Math.Floor((n * 2000.0 + d) / (2.0 * d)) / 10;
    public static double Idx1(long v, long b) => Pct1(v, b);

    // ── classification ──
    private static readonly string[] NotSlept = ["Cancelled", "No show", "Held", "Expired", "Duplicate", "Confirmed"];
    public static bool Slept(AnStay s) => !NotSlept.Contains(s.Status);
    public static int NightsOf(AnStay s) => Math.Max(1, s.Nights);
    public static bool InCovid(AnInput i, string m) => Cmp(MonthEnd(m), i.CovidFrom) >= 0 && Cmp(MonthStart(m), i.CovidTo) <= 0;
    public static string Marker(AnInput i, string from, string to)
    {
        var a = Cmp(from, i.LegacyUntil) <= 0; var b = Cmp(to, i.LegacyUntil) > 0;
        return a && b ? "legacy + live" : a ? "legacy" : "live";
    }
    public static string BillMarker(IEnumerable<AnBill> bs)
    {
        var l = false; var v = false; foreach (var b in bs) { if (b.Legacy) l = true; else v = true; }
        return l && v ? "legacy + live" : l ? "legacy" : "live";
    }
    public static string RoomType(AnInput i, string room)
    {
        var r = i.Rooms.FirstOrDefault(x => x.Id == room);
        return r is null ? "unassigned" : (r.Suite || room == "211" ? "Suite" : r.Type);
    }

    public static List<Night> Nights(AnInput i)
    {
        var o = new List<Night>();
        foreach (var s in i.Stays)
            if (Slept(s))
                for (var k = 0; k < NightsOf(s); k++) { var d = AddDays(s.From, k); if (Cmp(d, i.AsOf) <= 0) o.Add(new Night(s.Room, d, s)); }
        return o;
    }

    public static string DataStart(AnInput i)
    {
        var m = "9999-12-31";
        foreach (var s in i.Stays) if (Slept(s) && Cmp(s.From, m) < 0) m = s.From;
        foreach (var b in i.Bills) if (Cmp(b.Date, m) < 0) m = b.Date;
        return m;
    }

    public static int RoomsInService(AnInput i, string d) => i.Rooms.Count(r => Cmp(r.InServiceFrom, d) <= 0 && (r.InServiceTo is null || Cmp(r.InServiceTo, d) >= 0));

    public static List<OccMonth> OccupancyMonths(AnInput i)
    {
        var ds = DataStart(i); var o = new List<OccMonth>();
        if (Cmp(ds, i.AsOf) > 0) return o;
        var by = Nights(i).GroupBy(n => MonthOf(n.Date)).ToDictionary(g => g.Key, g => g.ToList());
        for (var m = MonthOf(ds); Cmp(m, MonthOf(i.AsOf)) <= 0; m = MonthNext(m))
        {
            var from = m == MonthOf(ds) ? ds : MonthStart(m); var to = Cmp(MonthEnd(m), i.AsOf) > 0 ? i.AsOf : MonthEnd(m);
            var days = 0; long cap = 0;
            for (var d = from; Cmp(d, to) <= 0; d = AddDays(d, 1)) { days++; cap += RoomsInService(i, d); }
            var list = by.GetValueOrDefault(m) ?? [];
            var sold = list.Select(n => n.Room + "|" + n.Date).Distinct().Count(); var roomsSold = list.Select(n => n.Room).Distinct().Count();
            var arrived = i.Stays.Where(s => Slept(s) && MonthOf(s.From) == m).ToList();
            o.Add(new OccMonth(m, days, sold, cap, roomsSold, (long)roomsSold * days, (long)i.Rooms.Count * days, arrived.Count, arrived.Count(s => s.Nights <= 0 || s.From == s.To), arrived.Sum(NightsOf)));
        }
        return o;
    }

    public static List<RevMonth> RevenueMonths(AnInput i)
    {
        var m = new Dictionary<string, RevMonth>();
        RevMonth Get(string k) { if (!m.TryGetValue(k, out var r)) m[k] = r = new RevMonth { Month = k }; return r; }
        foreach (var b in i.Bills)
        {
            var r = Get(MonthOf(b.Date)); r.Bills++; r.Room += b.RoomChargePaise; r.Fnb += b.FnbPaise; r.Other += b.OtherPaise; r.Discount += b.DiscountPaise; r.Gst += b.GstPaise;
            r.Total += b.TotalPaise; r.ChargedNights += b.ChargedNights; r.Paid += b.PaidPaise;
        }
        foreach (var x in i.Receipts) Get(MonthOf(x.Date)).Received += x.AmountPaise;
        return [.. m.Values.OrderBy(r => r.Month, StringComparer.Ordinal)];
    }

    public static long Net(RevMonth r) => r.Room + r.Fnb + r.Other - r.Discount;
    public static long Adr(RevMonth r) => RDiv(r.Room, r.ChargedNights);

    public static List<FyRow> FyRows(AnInput i)
    {
        var rev = RevenueMonths(i); var occ = OccupancyMonths(i);
        var fys = SortedUnique(rev.Select(r => FyOf(MonthStart(r.Month))).Concat(occ.Select(o => FyOf(MonthStart(o.Month)))));
        var res = new List<FyRow>();
        foreach (var fy in fys)
        {
            var rs = rev.Where(r => FyOf(MonthStart(r.Month)) == fy).ToList(); var os = occ.Where(o => FyOf(MonthStart(o.Month)) == fy).ToList();
            var t = new RevMonth { Month = fy };
            foreach (var r in rs) { t.Bills += r.Bills; t.Room += r.Room; t.Fnb += r.Fnb; t.Other += r.Other; t.Discount += r.Discount; t.Gst += r.Gst; t.Total += r.Total; t.ChargedNights += r.ChargedNights; t.Received += r.Received; t.Paid += r.Paid; }
            var months = os.Select(o => o.Month).Distinct().Count(); var full = months == 12 && os.All(o => o.Days == DaysInMonth(o.Month));
            res.Add(new FyRow { R = t, Fy = fy, Sold = os.Sum(o => o.Sold), Capacity = os.Sum(o => o.Capacity), Days = os.Sum(o => o.Days), Normal = !os.Any(o => InCovid(i, o.Month)) && full, Partial = !full });
        }
        return res;
    }

    // ── tables ──
    public static ReportRow Row(params (string Key, object? Value)[] cells) => Row(ReportRowKind.Row, cells);
    public static ReportRow Row(ReportRowKind kind, params (string Key, object? Value)[] cells)
    {
        var d = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in cells) d[k] = v switch { int n => (long)n, _ => v };
        return new ReportRow(kind, d);
    }
    public static ReportRow RowD(Dictionary<string, object?> cells, ReportRowKind kind = ReportRowKind.Row) =>
        new(kind, cells.ToDictionary(kv => kv.Key, kv => kv.Value is int n ? (object?)(long)n : kv.Value, StringComparer.Ordinal));
    public static Tbl Table(string key, string title, IReadOnlyList<ReportColumn> cols, IEnumerable<ReportRow> rows) => new(key, title, cols, [.. rows]);
    public static Tbl Series(string name, string kind, string title, IReadOnlyList<string> labels, IReadOnlyList<long> values, bool money, bool pct = false) =>
        Table("series." + name, kind + "|" + title,
            [T("label", "Label"), new("value", "Value", pct ? ReportColumnType.Percent : money ? ReportColumnType.Money : ReportColumnType.Int, money)],
            labels.Select((l, k) => Row(("label", l), ("value", pct ? (object)(values[k] / 10.0) : values[k]))));
    public static Tbl SeriesPct(string name, string kind, string title, IReadOnlyList<string> labels, IReadOnlyList<double> values) =>
        Table("series." + name, kind + "|" + title, [T("label", "Label"), new("value", "Value", ReportColumnType.Percent, false)],
            labels.Select((l, k) => Row(("label", l), ("value", values[k]))));
    public static Tbl Note(string key, string text) => Table("note." + key, "Note", [T("note", "Note")], [Row(("note", text))]);

    public static ReportResult Result(string id, string title, AnParams p, IReadOnlyList<Tbl> tables, AnInput i, string? from = null, string? to = null)
    {
        var main = tables[0];
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (p.Fy is not null) values["fy"] = p.Fy;
        var about = new ReportAbout(DateTimeOffset.MinValue, "", $"Legacy history to {i.LegacyUntil} / HCC live. Legacy to 30 Oct 2026 / HCC live", false, true);
        return new ReportResult(id, title, new BoundParams(values), main.Columns, main.Rows, about, Sections: [.. tables.Select(t => new ReportTable(t.Key, t.Title, t.Columns, t.Rows))]);
    }

    public static string FocusFy(AnInput i, AnParams p) => p.Fy ?? FyOf(i.AsOf);
    public static Dictionary<string, AnStay> BillStay(AnInput i) => i.Stays.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
}
