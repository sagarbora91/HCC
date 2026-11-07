// F12 MEAS · meas-study-b · server twin of beyond-revenue.ts: RevenueKpi, ChannelEconomics, PricingShape (same names, same numbers).
using static Hcc.Reporting.Analytics.Bl;

namespace Hcc.Reporting.Analytics;

public static class BeyondRevenue
{
    private static readonly string[] Types = ["Suite", "Premium", "Deluxe", "Twin"];

    internal static List<string> TypesOf(Ctx c)
    {
        var t = c.Rtype.Values.Distinct().ToList();
        return [.. Types.Where(t.Contains), .. t.Where(x => !Types.Contains(x)).OrderBy(x => x, StringComparer.Ordinal)];
    }

    public static AnResult RevenueKpi(AnInput inp, AnParams p)
    {
        var c = new Ctx(inp); var fy = c.FyParam(p);
        var month = new List<object>();
        foreach (var m in c.FyMonths(fy))
        {
            var from = Lt(m + "-01", c.DataStart) ? c.DataStart : m + "-01"; var to = Lt(c.End, MonthEnd(m)) ? c.End : MonthEnd(m);
            var ms = c.Bills.Where(b => b.Month == m).ToList(); var avail = c.Capacity(from, to); var sold = c.SleptIn(from, to); var cn = ms.Sum(b => b.Cn); var rev = ms.Sum(b => b.Rev);
            month.Add(R(m, c.SrcOf(from, to), (long)DaysBetween(from, to) + 1, (long)c.RoomsOn(to).Count, avail, sold, Pct1(sold, avail), (long)ms.Count, cn, rev, Ratio(rev, cn), Ratio(rev, avail)));
        }
        var fyRows = new List<object>(); long ta = 0, ts = 0, tr = 0, tc = 0, tm = 0;
        foreach (var f in c.Fys)
        {
            var ms = c.FyMonths(f); var from = Lt(FyStart(f), c.DataStart) ? c.DataStart : FyStart(f); var to = Lt(c.End, FyEnd(f)) ? c.End : FyEnd(f);
            var bs = c.Bills.Where(b => b.Fy == f).ToList(); var avail = c.Capacity(from, to); var sold = c.SleptIn(from, to); var cn = bs.Sum(b => b.Cn); var rev = bs.Sum(b => b.Rev); var adr = Ratio(rev, cn);
            ta += avail; ts += sold; tr += rev; tc += cn; tm += ms.Count;
            fyRows.Add(R(f, c.SrcOf(from, to), (long)ms.Count, avail, sold, Pct1(sold, avail), rev, cn, adr, Ratio(rev, avail), avail - sold, adr is null ? null : (avail - sold) * adr));
        }
        fyRows.Add(Total("Total", "", tm, ta, ts, Pct1(ts, ta), tr, tc, Ratio(tr, tc), Ratio(tr, ta), ta - ts, null));
        var f0 = Lt(FyStart(fy), c.DataStart) ? c.DataStart : FyStart(fy); var t0 = Lt(c.End, FyEnd(fy)) ? c.End : FyEnd(fy);
        var typeRows = TypesOf(c).Select(t =>
        {
            var bs = c.Bills.Where(b => b.Fy == fy && b.Type == t).ToList(); var avail = c.Capacity(f0, t0, t); var sold = c.SleptIn(f0, t0, t); var cn = bs.Sum(b => b.Cn); var rev = bs.Sum(b => b.Rev);
            return (object)R(t, fy, (long)c.RoomsOn(t0).Count(r => c.Rtype[r] == t), avail, sold, Pct1(sold, avail), rev, cn, Ratio(rev, cn), Ratio(rev, avail));
        }).ToList();
        var chans = c.Bills.Where(b => b.Fy == fy).Select(b => b.Channel).Distinct().OrderBy(x => x == OtaFree ? "" : "~" + x, StringComparer.Ordinal).ToList();
        var fyRev = c.Bills.Where(b => b.Fy == fy).Sum(b => b.Rev);
        var chanRows = chans.Select(ch =>
        {
            var bs = c.Bills.Where(b => b.Fy == fy && b.Channel == ch).ToList(); var rev = bs.Sum(b => b.Rev); var cn = bs.Sum(b => b.Cn);
            return (object)R(fy, ch, (long)bs.Count, bs.Sum(b => b.Nights), rev, Ratio(rev, cn), Pct1(rev, fyRev));
        }).ToList();
        return Finish(
        [
            Sec("month", "KPI by Month", [T("month", "Month"), T("src", "Source"), I("days", "Days"), I("rooms", "Rooms in service"), I("avail", "Available room-nights"), I("sold", "Room-nights sold"), Pc("occ", "Occupancy %"),
                I("bills", "Bills"), I("cn", "Charged room-nights"), M("rev", "Room revenue (before GST)"), M("adr", "ADR"), M("revpar", "RevPAR")], month),
            Sec("fy", "KPI by FY", [T("fy", "FY"), T("src", "Source"), I("months", "Months"), I("avail", "Available room-nights"), I("sold", "Room-nights sold"), Pc("occ", "Occupancy %"), M("rev", "Room revenue (before GST)"),
                I("cn", "Charged room-nights"), M("adr", "ADR"), M("revpar", "RevPAR"), I("unsold", "Unsold room-nights"), M("unsoldValue", "Value of unsold at ADR")], fyRows),
            Sec("type", "KPI by Room Type", [T("type", "Room type"), T("fy", "FY"), I("rooms", "Rooms"), I("avail", "Available room-nights"), I("sold", "Room-nights sold"), Pc("occ", "Occupancy %"),
                M("rev", "Room revenue"), I("cn", "Charged room-nights"), M("adr", "ADR"), M("revpar", "RevPAR")], typeRows),
            Sec("channel", "KPI by Channel", [T("fy", "FY"), T("channel", "Channel"), I("bills", "Bills"), I("rn", "Room-nights"), M("rev", "Room revenue"), M("adr", "ADR"), Pc("share", "Share of room revenue")], chanRows),
            Series("occupancy-by-month", "bar", "Occupancy % by month", month.Select(r => MonthLabel((string)((object?[])r)[0]!)), month.Select(r => (double?)((object?[])r)[6]), false, true),
        ], c.SrcOf(c.DataStart, c.End) != "HCC live");
    }

    private static bool IsOta(BM b) => b.Channel != OtaFree;

    public static AnResult ChannelEconomics(AnInput inp, AnParams p)
    {
        var c = new Ctx(inp); var fy = c.FyParam(p); var comm = p.Num("commissionPct", 15);
        var net = c.Fys.Select(f =>
        {
            var o = c.Bills.Where(b => b.Fy == f && IsOta(b)).ToList(); var d = c.Bills.Where(b => b.Fy == f && !IsOta(b)).ToList();
            var orev = o.Sum(b => b.Rev); var ocn = o.Sum(b => b.Cn); var oadr = Ratio(orev, ocn); var dadr = Ratio(d.Sum(b => b.Rev), d.Sum(b => b.Cn));
            var netAdr = oadr is null ? null : Ratio((double)oadr * (100 - comm), 100);
            return (object)R(f, o.Sum(b => b.Nights), orev, oadr, dadr, Ratio(orev * 12.0, 100), Ratio(orev * 15.0, 100), Ratio(orev * 18.0, 100), Ratio(orev * 20.0, 100), netAdr, netAdr is not null && dadr is not null ? dadr - netAdr : null);
        }).ToList();
        var mo = c.FyMonths(fy).Select(m =>
        {
            var bs = c.Bills.Where(b => b.Month == m).ToList(); var o = bs.Where(IsOta).ToList(); var d = bs.Where(b => !IsOta(b)).ToList(); var or = o.Sum(b => b.Rev); var dr = d.Sum(b => b.Rev);
            return (object)R(m, d.Sum(b => b.Nights), o.Sum(b => b.Nights), Pct1(d.Sum(b => b.Nights), bs.Sum(b => b.Nights)), dr, or, Pct1(dr, dr + or));
        }).ToList();
        var dep = c.Fys.Select(f =>
        {
            var bs = c.Bills.Where(b => b.Fy == f).ToList(); var o = bs.Where(IsOta).ToList(); var d = bs.Where(b => !IsOta(b)).ToList(); var orev = o.Sum(b => b.Rev);
            return (object)R(f, (long)bs.Count, (long)o.Count, Pct1(o.Count, bs.Count), o.Sum(b => b.Nights), orev, Pct1(orev, bs.Sum(b => b.Rev)), Ratio(orev, o.Sum(b => b.Cn)), Avg1(o.Sum(b => b.Nights), o.Count), Avg1(d.Sum(b => b.Nights), d.Count));
        }).ToList();
        return Finish(
        [
            Sec("net", "Net ADR after Commission", [T("fy", "FY"), I("otaRn", "OTA room-nights"), M("otaRev", "OTA room revenue"), M("otaAdr", "OTA ADR"), M("directAdr", "Direct ADR"), M("c12", "Commission at 12%"), M("c15", "at 15%"),
                M("c18", "at 18%"), M("c20", "at 20%"), M("netAdr", "Net OTA ADR at the commission %"), M("gap", "Direct ADR above net OTA ADR")], net),
            Sec("direct", "Direct Share by Month", [T("month", "Month"), I("directRn", "Direct room-nights"), I("otaRn", "OTA room-nights"), Pc("directShare", "Direct share of room-nights"), M("directRev", "Direct room revenue"),
                M("otaRev", "OTA room revenue"), Pc("directRevShare", "Direct share of room revenue")], mo),
            Sec("ota", "OTA Dependence", [T("fy", "FY"), I("bills", "Bills"), I("otaBills", "OTA bills"), Pc("otaShare", "OTA share of bills"), I("otaRn", "OTA room-nights"), M("otaRev", "OTA room revenue"),
                Pc("otaRevShare", "OTA share of room revenue"), M("otaAdr", "OTA ADR"), I("otaNights", "OTA avg nights per bill"), I("directNights", "Direct avg nights per bill")], dep),
            Series("direct-share", "line", "Direct share of room-nights by month", mo.Select(r => MonthLabel((string)((object?[])r)[0]!)), mo.Select(r => (double?)((object?[])r)[3]), false, true),
        ], true);
    }

    private static readonly (string N, int Lo, int Hi)[] Los = [("1 night", 1, 1), ("2 nights", 2, 2), ("3 nights", 3, 3), ("4-7 nights", 4, 7), ("8-14 nights", 8, 14), ("15+ nights", 15, 1_000_000_000)];

    public static AnResult PricingShape(AnInput inp, AnParams p)
    {
        var c = new Ctx(inp); var fy = c.FyParam(p); var rec = c.Fys.Skip(Math.Max(0, c.Fys.Count - 3)).ToList();
        var dowRows = Dow.Select((n, i) =>
        {
            long sold = c.Slept.Count(s => !c.Covid(s.Date) && DowOf(s.Date) == i); long cap = 0;
            for (var d = c.DataStart; Le(d, c.End); d = AddDays(d, 1)) if (DowOf(d) == i && !c.Covid(d)) cap += c.RoomsOn(d).Count;
            var arr = c.Bills.Where(b => b.St is not null && b.Cn > 0 && DowOf(b.St.From) == i).ToList(); var ar = arr.Where(b => rec.Contains(b.Fy)).ToList();
            return (object)R(n, sold, cap, Pct1(sold, cap), Ratio(arr.Sum(b => b.Rev), arr.Sum(b => b.Cn)), Ratio(ar.Sum(b => b.Rev), ar.Sum(b => b.Cn)));
        }).ToList();
        var losRows = Los.Select(l =>
        {
            var ms = c.Bills.Where(b => b.Nights >= l.Lo && b.Nights <= l.Hi && b.Rev > 0).ToList(); var r = ms.Where(b => rec.Contains(b.Fy)).ToList();
            return (object)R(l.N, (long)ms.Count, ms.Sum(b => b.Nights), Ratio(ms.Sum(b => b.Rev), ms.Sum(b => b.Cn)), (long)r.Count, Ratio(r.Sum(b => b.Rev), r.Sum(b => b.Cn)), Pct1(r.Sum(b => b.B.DiscountPaise), r.Sum(b => b.Gross)));
        }).ToList();
        var fb = c.Bills.Where(b => b.Fy == fy).ToList(); var segs = fb.Select(b => b.Seg).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(); var fyRev = fb.Sum(b => b.Rev);
        var segRows = segs.Select(s => { var x = fb.Where(b => b.Seg == s).ToList(); return (object)R(fy, s, (long)x.Count, x.Sum(b => b.Nights), x.Sum(b => b.Rev), Ratio(x.Sum(b => b.Rev), x.Sum(b => b.Cn)), Pct1(x.Sum(b => b.Rev), fyRev)); }).ToList();
        var ladder = new List<object>();
        foreach (var t in BeyondRevenue.TypesOf(c))
        {
            var rates = fb.Where(b => b.Type == t && b.Rev > 0 && b.Nights > 0).Select(b => (long)Math.Floor((Ratio(b.Rev, b.Nights)!.Value + 50) / 100.0) * 100).OrderBy(x => x).ToList();
            if (rates.Count == 0) continue;
            var cnt = rates.GroupBy(x => x).OrderBy(g => g.Key).ToList(); long modal = rates[0]; var best = 0;
            foreach (var g in cnt) if (g.Count() > best) { best = g.Count(); modal = g.Key; }
            var below = rates.Where(x => x * 100 < modal * 95).ToList(); var n = rates.Count;
            ladder.Add(R(t, fy, (long)n, modal, rates[n / 10], rates[n / 2], rates[9 * n / 10], Pct1(below.Count, n), below.Count > 0 ? Ratio(below.Sum(x => modal - x), below.Count) : null));
        }
        static int Rank(string t) => Array.IndexOf(Types, t) < 0 ? 0 : 4 - Array.IndexOf(Types, t);
        var up = c.Fys.Select(f =>
        {
            var ss = c.Stays.Where(s => FyOf(s.From) == f).ToList(); var typed = ss.Where(s => !string.IsNullOrEmpty(s.BookedType)).ToList();
            string RT(AnStay s) => c.Rtype.GetValueOrDefault(s.Room) ?? "";
            var ups = typed.Count(s => Rank(RT(s)) > Rank(s.BookedType!) && Rank(s.BookedType!) > 0); var downs = typed.Count(s => Rank(RT(s)) < Rank(s.BookedType!) && Rank(RT(s)) > 0);
            return (object)R(f, (long)ss.Count, (long)typed.Count, (long)ups, Pct1(ups, typed.Count), (long)downs);
        }).ToList();
        var firstRcp = new Dictionary<string, string>(StringComparer.Ordinal); var billById = inp.Bills.GroupBy(b => b.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var r in inp.Receipts) { if (!billById.TryGetValue(r.Bill, out var b)) continue; if (!firstRcp.TryGetValue(b.Stay, out var o) || Lt(r.Date, o)) firstRcp[b.Stay] = r.Date; }
        string[] B = ["No receipt linked", "Paid on / after arrival", "1-3 days before", "4-7 days before", "8-30 days before", "31+ days before"]; var bucket = new Dictionary<string, long>();
        foreach (var s in c.Stays.Where(s => FyOf(s.From) == fy))
        {
            var k = B[0]; if (firstRcp.TryGetValue(s.Id, out var f)) { var g = DaysBetween(f, s.From); k = g <= 0 ? B[1] : g <= 3 ? B[2] : g <= 7 ? B[3] : g <= 30 ? B[4] : B[5]; }
            bucket[k] = bucket.GetValueOrDefault(k) + 1;
        }
        var nStays = bucket.Values.Sum(); var win = B.Where(bucket.ContainsKey).Select(k => (object)R(k, bucket[k], Pct1(bucket[k], nStays))).ToList();
        return Finish(
        [
            Sec("dow", "DOW & LOS Pricing: night of week", [T("night", "Night"), I("sold", "Room-nights sold (normal years)"), I("cap", "Capacity (normal years)"), Pc("occ", "Occupancy %"), M("adr", "ADR by arrival day, all years"), M("adrRecent", "ADR by arrival day, last 3 FYs")], dowRows),
            Sec("los", "DOW & LOS Pricing: length of stay", [T("bucket", "Length of stay"), I("bills", "Bills"), I("rn", "Room-nights"), M("adr", "ADR"), I("billsRecent", "Bills, last 3 FYs"), M("adrRecent", "ADR, last 3 FYs"), Pc("discPct", "Discount % of gross, last 3 FYs")], losRows),
            Sec("segment", "Segment Mix", [T("fy", "FY"), T("segment", "Segment"), I("bills", "Bills"), I("rn", "Room-nights"), M("rev", "Room revenue"), M("adr", "ADR"), Pc("share", "Share of room revenue")], segRows),
            Sec("ladder", "Rate Ladder", [T("type", "Room type"), T("fy", "FY"), I("bills", "Bills"), M("modal", "Modal rate (rack proxy)"), M("p10", "P10"), M("p50", "Median"), M("p90", "P90"), Pc("below", "Bills more than 5% below modal"), M("cut", "Avg cut below modal")], ladder),
            Sec("upgrades", "Upgrades & Shifts", [T("fy", "FY (arrival)"), I("stays", "Stays"), I("typed", "Stays with a booked type"), I("ups", "Upgrades"), Pc("upPct", "Upgrade %"), I("downs", "Downgrades")], up),
            Sec("window", "Booking Window (first receipt vs arrival)", [T("bucket", "First receipt vs arrival"), I("stays", "Stays"), Pc("share", "Share")], win),
            Series("dow-occupancy", "bar", "Occupancy % by night of week", Dow, dowRows.Select(r => (double?)((object?[])r)[3]), false, true),
        ], true);
    }
}
