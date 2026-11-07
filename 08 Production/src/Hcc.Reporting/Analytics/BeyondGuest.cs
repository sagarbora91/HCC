// F12 MEAS · meas-study-b · server twin of beyond-guest.ts: GuestRepeatValue, GuestOriginPlan.
using System.Text.RegularExpressions;
using Hcc.Application.Reporting.Rpt;
using static Hcc.Reporting.Analytics.Bl;

namespace Hcc.Reporting.Analytics;

public static class BeyondGuest
{
    private static Dictionary<string, long> NetOf(Ctx c)
    {
        var m = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var b in c.Inp.Bills) m[b.Stay] = m.GetValueOrDefault(b.Stay) + b.RoomChargePaise + b.FnbPaise + b.OtherPaise - b.DiscountPaise;
        return m;
    }

    private static Dictionary<string, string> FirstFy(Ctx c)
    {
        var m = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in c.Stays) { var f = FyOf(s.From); if (!m.TryGetValue(s.GuestKey, out var o) || Lt(f, o)) m[s.GuestKey] = f; }
        return m;
    }

    public static AnResult GuestRepeatValue(AnInput inp, AnParams p)
    {
        var c = new Ctx(inp); var net = NetOf(c); var first = FirstFy(c); var top = (int)p.Num("topN", 50);
        long N(AnStay s) => net.GetValueOrDefault(s.Id);
        var byFy = c.Fys.Select(f =>
        {
            var ss = c.Stays.Where(s => FyOf(s.From) == f).ToList(); var gs = ss.Select(s => s.GuestKey).Distinct().ToList(); var rep = gs.Where(g => Lt(first[g], f)).ToHashSet();
            var rs = ss.Where(s => rep.Contains(s.GuestKey)).ToList();
            return (object)R(f, (long)gs.Count, (long)rep.Count, Pct1(rep.Count, gs.Count), (long)ss.Count, (long)rs.Count, ss.Sum(N), rs.Sum(N), Pct1(rs.Sum(N), ss.Sum(N)));
        }).ToList();
        var per = new Dictionary<string, (long N, long Nights, long Rev, string First, string Last, SortedSet<string> Fys)>(StringComparer.Ordinal);
        foreach (var s in c.Stays)
        {
            var o = per.TryGetValue(s.GuestKey, out var e) ? e : (0L, 0L, 0L, s.From, s.From, new SortedSet<string>(StringComparer.Ordinal));
            o.N++; o.Nights += s.Nights; o.Rev += N(s); if (Lt(s.From, o.First)) o.First = s.From; if (Lt(o.Last, s.From)) o.Last = s.From; o.Fys.Add(FyOf(s.From)); per[s.GuestKey] = o;
        }
        (string N, int Lo, int Hi)[] B = [("1 stay", 1, 1), ("2 stays", 2, 2), ("3-4 stays", 3, 4), ("5-9 stays", 5, 9), ("10+ stays", 10, 1_000_000_000)];
        var freq = B.Select(b =>
        {
            var g = per.Values.Where(x => x.N >= b.Lo && x.N <= b.Hi).ToList();
            return (object)R(b.N, (long)g.Count, g.Sum(x => x.N), g.Sum(x => x.Nights), g.Sum(x => x.Rev), Ratio(g.Sum(x => x.Rev), g.Count), Avg1(g.Sum(x => x.Nights), g.Count));
        }).ToList();
        var list = per.Where(kv => kv.Value.N >= 3).OrderByDescending(kv => kv.Value.N).ThenByDescending(kv => kv.Value.Rev).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(top)
            .Select(kv => (object)R(kv.Key, kv.Value.N, kv.Value.First, kv.Value.Last, kv.Value.Nights, kv.Value.Rev, string.Join(", ", kv.Value.Fys))).ToList();
        return Finish(
        [
            Sec("repeat", "Repeat Guests by FY", [T("fy", "FY"), I("guests", "Guests"), I("repeat", "Repeat guests (seen in an earlier FY)"), Pc("repeatPct", "Repeat %"), I("stays", "Stays"), I("repeatStays", "Stays by repeat guests"),
                M("net", "Net billed"), M("repeatNet", "Net billed to repeat guests"), Pc("repeatNetPct", "Repeat share of net billed")], byFy),
            Sec("frequency", "Guest Frequency & Value", [T("bucket", "Frequency"), I("guests", "Guests"), I("stays", "Stays"), I("nights", "Nights"), M("net", "Net billed (lifetime)"), M("perGuest", "Net billed per guest"), I("avgNights", "Avg nights per guest")], freq),
            Sec("list", "Repeat Customer List", [new Col("guest", "Customer", ReportColumnType.Text, false, ReportMask.GuestName), I("stays", "Stays"), Dt("first", "First stay"), Dt("last", "Last stay"), I("nights", "Nights"), M("net", "Net billed"), T("fys", "Financial years")], list),
            Series("repeat-pct", "line", "Repeat guests % by FY", byFy.Select(r => (string)((object?[])r)[0]!), byFy.Select(r => (double?)((object?[])r)[3]), false, true),
        ], c.Stays.Any(s => s.Legacy));
    }

    private static readonly Regex India = new("^(india|indian|ind)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static List<(string K, int N)> Count(IEnumerable<string> xs) =>
        xs.GroupBy(x => x, StringComparer.Ordinal).Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();

    public static AnResult GuestOriginPlan(AnInput inp, AnParams p)
    {
        var c = new Ctx(inp); var fy = c.FyParam(p); var ss = c.Stays.Where(s => FyOf(s.From) == fy).ToList();
        var origin = Count(ss.Select(s => Norm(s.State) is { Length: > 0 } x ? x : "Unknown")).Select(x => (object)R(fy, x.K, (long)x.N, Pct1(x.N, ss.Count))).ToList();
        var nat = Count(ss.Select(s => Norm(s.Nationality) is { Length: > 0 } x ? x : "Not recorded"))
            .Select(x => (object)R(fy, x.K, India.IsMatch(x.K) || x.K == "Not recorded" ? "No" : "Yes", (long)x.N, Pct1(x.N, ss.Count))).ToList();
        string[] plans = ["EP", "CP", "MAP", "AP"];
        var plan = c.Fys.Select(f =>
        {
            var x = c.Stays.Where(s => FyOf(s.From) == f).ToList(); long Nn(string pl) => x.Count(s => (s.MealPlan ?? "").ToUpperInvariant() == pl); var known = plans.Sum(Nn);
            return (object)R(f, (long)x.Count, Nn("EP"), Nn("CP"), Nn("MAP"), Nn("AP"), x.Count - known, Pct1(Nn("CP"), x.Count));
        }).ToList();
        var co = c.Fys.Select(f =>
        {
            var x = c.Stays.Where(s => FyOf(s.From) == f && !string.IsNullOrEmpty(s.Company)).ToList(); var names = x.Select(s => Norm(s.Company).ToUpperInvariant()).ToHashSet(); var all = c.Stays.Count(s => FyOf(s.From) == f);
            return (object)R(f, (long)x.Count, (long)names.Count, x.Sum(s => (long)s.Nights), Pct1(x.Count, all));
        }).ToList();
        return Finish(
        [
            Sec("origin", "Guest Origin", [T("fy", "FY"), T("state", "State"), I("stays", "Stays"), Pc("share", "Share of stays")], origin),
            Sec("nationality", "Nationality & Form C", [T("fy", "FY"), T("nationality", "Nationality"), T("formC", "Form C needed"), I("stays", "Stays"), Pc("share", "Share of stays")], nat),
            Sec("plan", "Meal Plan Mix", [T("fy", "FY"), I("stays", "Stays"), I("ep", "EP (room only)"), I("cp", "CP (with breakfast)"), I("map", "MAP"), I("ap", "AP"), I("none", "No plan recorded"), Pc("cpPct", "CP share")], plan),
            Sec("company", "Company Guests", [T("fy", "FY"), I("stays", "Company stays"), I("companies", "Companies"), I("nights", "Nights"), Pc("share", "Share of stays")], co),
            Series("cp-share", "bar", "CP share of stays by FY", plan.Select(r => (string)((object?[])r)[0]!), plan.Select(r => (double?)((object?[])r)[7]), false, true),
        ], c.Stays.Any(s => s.Legacy));
    }
}
