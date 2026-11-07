// F12 MEAS · meas-study-a · server twin of legacy-occupancy.ts: OccupancyHistory, OperationsPatterns, RoomsAndRates. Same method names, same numbers.
using Hcc.Application.Reporting.Rpt;
using static Hcc.Reporting.Analytics.LegacyCore;

namespace Hcc.Reporting.Analytics;

public static class LegacyOccupancy
{
    private static string YearOf(string fy, string mm) => (string.CompareOrdinal(mm, "04") >= 0 ? FyStartYear(fy) : FyStartYear(fy) + 1).ToString();

    public static ReportResult OccupancyHistory(AnInput i, AnParams p)
    {
        var fy = FocusFy(i, p); var om = OccupancyMonths(i); var fys = FyRows(i);
        var monthly = Table("monthly", $"Monthly ({FyLabel(fy)})",
            [T("month", "Month"), I("days", "Days"), I("inService", "Room-nights in service"), I("sold", "Room-nights sold"), P("occ", "Occupancy % (rooms in service)"), P("occA", "Alt A % (rooms that sold)"),
             P("occB", "Alt B % (fixed room count)"), I("stays", "Stays arrived"), T("alos", "ALOS (arrival month)"), I("sameDay", "Same-day stays")],
            om.Where(o => FyOf(MonthStart(o.Month)) == fy).Select(o => Row(("month", MonthLabel(o.Month)), ("days", o.Days), ("inService", o.Capacity), ("sold", o.Sold), ("occ", Pct1(o.Sold, o.Capacity)),
                ("occA", Pct1(o.Sold, o.CapacityA)), ("occB", Pct1(o.Sold, o.CapacityB)), ("stays", o.Stays),
                ("alos", o.Stays > 0 ? (Math.Round(o.NightsArrived * 100.0 / o.Stays, MidpointRounding.AwayFromZero) / 100).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : ""), ("sameDay", o.SameDay))));
        var summary = Table("fy-summary", "FY summary",
            [T("fy", "FY"), I("days", "Days in data"), I("inService", "Room-nights in service"), I("sold", "Room-nights sold"), P("occ", "Occupancy %"), P("occA", "Alt A %"), P("occB", "Alt B %"), T("note", "Note")],
            fys.Select(f =>
            {
                var o = om.Where(x => FyOf(MonthStart(x.Month)) == f.Fy).ToList();
                return Row(("fy", FyLabel(f.Fy)), ("days", f.Days), ("inService", f.Capacity), ("sold", f.Sold), ("occ", Pct1(f.Sold, f.Capacity)), ("occA", Pct1(f.Sold, o.Sum(x => x.CapacityA))),
                    ("occB", Pct1(f.Sold, o.Sum(x => x.CapacityB))), ("note", f.Partial ? "Partial year" : f.Normal ? "" : "COVID window"));
            }));
        var gcols = new List<ReportColumn> { T("fy", "FY") }; gcols.AddRange(FyMonths.Select(m => P("m" + m, MonLabel[m])));
        var grid = Table("fy-month-grid", "FY x Month grid (occupancy %)", gcols, fys.Select(f =>
        {
            var c = new Dictionary<string, object?> { ["fy"] = FyLabel(f.Fy) };
            foreach (var m in FyMonths) { var o = om.FirstOrDefault(x => x.Month == $"{YearOf(f.Fy, m)}-{m}"); c["m" + m] = o is null ? null : Pct1(o.Sold, o.Capacity); }
            return RowD(c);
        }));
        var ns = Nights(i).Where(n => FyOf(n.Date) == fy).ToList();
        long CapDays(int[] set)
        {
            long c = 0; var days = new HashSet<string>();
            foreach (var o in om.Where(x => FyOf(MonthStart(x.Month)) == fy))
                for (var d = MonthStart(o.Month); Cmp(d, MonthEnd(o.Month)) <= 0 && Cmp(d, i.AsOf) <= 0; d = AddDays(d, 1))
                    if (set.Contains(Dow(d)) && days.Add(d)) c += i.Rooms.Count(r => Cmp(r.InServiceFrom, d) <= 0 && (r.InServiceTo is null || Cmp(r.InServiceTo, d) >= 0));
            return c;
        }
        var weekday = Table("weekday-weekend", $"Weekday vs weekend ({FyLabel(fy)}; weekend = Fri + Sat nights)", [T("group", "Group"), I("sold", "Room-nights sold"), I("capacity", "Room-nights in service"), P("occ", "Occupancy %")],
            new (string, int[])[] { ("Weekday nights (Sun-Thu)", [0, 1, 2, 3, 4]), ("Weekend nights (Fri-Sat)", [5, 6]) }.Select(g =>
            {
                var c = CapDays(g.Item2); long s = ns.Count(n => g.Item2.Contains(Dow(n.Date)));
                return Row(("group", g.Item1), ("sold", s), ("capacity", c), ("occ", Pct1(s, c)));
            }));
        var seasonRows = FyMonths.Select(m =>
        {
            var xs = om.Where(o => o.Month[5..] == m && !InCovid(i, o.Month) && o.Days == DateTime.DaysInMonth(int.Parse(o.Month[..4]), int.Parse(m))).ToList();
            var avg = xs.Count > 0 ? Math.Floor((xs.Sum(o => Pct1(o.Sold, o.Capacity) * 10) / xs.Count) + 0.5) / 10 : 0;
            return Row(("month", MonLabel[m]), ("obs", xs.Count), ("avg", avg));
        });
        var season = Table("seasonality", "Seasonality (simple mean of monthly %, normal years)", [T("month", "Month"), I("obs", "Months observed"), P("avg", "Average occupancy %")], seasonRows);
        var types = SortedUnique(i.Rooms.Select(r => RoomType(i, r.Id)));
        var daysFy = om.Where(o => FyOf(MonthStart(o.Month)) == fy).Sum(o => (long)o.Days);
        var mix = Table("room-type-mix", $"Room-type mix ({FyLabel(fy)})",
            [T("type", "Room type"), I("rooms", "Rooms"), I("sold", "Room-nights sold"), I("capacity", "Room-nights in service"), P("occ", "Occupancy %"), P("share", "Share of room-nights %")],
            types.Select(t =>
            {
                var rs = i.Rooms.Where(r => RoomType(i, r.Id) == t).ToList(); long s = ns.Count(n => RoomType(i, n.Room) == t);
                return Row(("type", t), ("rooms", rs.Count), ("sold", s), ("capacity", rs.Count * daysFy), ("occ", Pct1(s, rs.Count * daysFy)), ("share", Pct1(s, ns.Count)));
            }));
        var sr = SeriesPct("occ-by-fy", "bar", "Occupancy % by FY", fys.Select(f => f.Fy).ToList(), fys.Select(f => Pct1(f.Sold, f.Capacity)).ToList());
        var note = Note("caveat", "The data does not record which rooms were sellable on a given day; all three denominators are approximations and true occupancy of sellable inventory is at or above the headline.");
        return Result("rpt.study.occupancy-history", "Occupancy history (three denominators)", p with { Fy = fy }, [monthly, summary, grid, weekday, season, mix, sr, note], i);
    }

    public static ReportResult OperationsPatterns(AnInput i, AnParams p)
    {
        var fy = FocusFy(i, p); var stays = i.Stays.Where(s => Slept(s) && FyOf(s.From) == fy).ToList(); var n = stays.Count;
        Tbl Dist(string key, string label, IEnumerable<string> keys, Func<AnStay, string> f) =>
            Table(key, label, [T("item", label.Split(" (")[0]), I("stays", "Stays"), P("share", "Share %")], keys.Select(k => { var c = stays.Count(s => f(s) == k); return Row(("item", k), ("stays", c), ("share", Pct1(c, n))); }));
        var arr = Dist("arrivals-dow", "Arrivals_DOW", DowLabel, s => DowLabel[Dow(s.From)]);
        var dep = Dist("departures-dow", "Departures_DOW", DowLabel, s => DowLabel[Dow(AddDays(s.From, NightsOf(s)))]);
        var los = Dist("length-of-stay", "LengthOfStay", ["1", "2", "3", "4", "5", "6", "7+"], s => NightsOf(s) >= 7 ? "7+" : NightsOf(s).ToString());
        var grp = new Dictionary<string, int>();
        foreach (var s in stays) { var k = s.GuestKey + "|" + s.From; grp[k] = grp.GetValueOrDefault(k) + 1; }
        string[] gsKeys = ["1 room", "2 rooms", "3-4 rooms", "5+ rooms"];
        string GsOf(int c) => c == 1 ? gsKeys[0] : c == 2 ? gsKeys[1] : c <= 4 ? gsKeys[2] : gsKeys[3];
        var gs = Table("group-size", "GroupSize (rooms booked together: same guest, same arrival)", [T("item", "Rooms"), I("parties", "Parties"), P("share", "Share %")],
            gsKeys.Select(k => { var c = grp.Values.Count(v => GsOf(v) == k); return Row(("item", k), ("parties", c), ("share", Pct1(c, grp.Count))); }));
        IEnumerable<KeyValuePair<string, int>> Cnt(Func<AnStay, string> f)
        {
            var m = new Dictionary<string, int>();
            foreach (var s in stays) { var k = f(s); m[k] = m.GetValueOrDefault(k) + 1; }
            return m.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal);
        }
        var pur = Table("purpose", "Purpose", [T("item", "Purpose"), I("stays", "Stays"), P("share", "Share %")], Cnt(s => string.IsNullOrEmpty(s.Purpose) ? "not recorded" : s.Purpose).Select(kv => Row(("item", kv.Key), ("stays", kv.Value), ("share", Pct1(kv.Value, n)))));
        var src = Table("booking-source", "BookingSource", [T("item", "Source"), I("stays", "Stays"), P("share", "Share %")],
            Cnt(s => !string.IsNullOrEmpty(s.Channel) ? s.Channel : !string.IsNullOrEmpty(s.Source) ? s.Source : "unknown").Select(kv => Row(("item", kv.Key), ("stays", kv.Value), ("share", Pct1(kv.Value, n)))));
        var all = i.Stays.Where(s => FyOf(s.From) == fy).ToList();
        var status = Table("status", "Status (all stays arriving in the FY)", [T("item", "Status"), I("stays", "Stays"), P("share", "Share %")],
            SortedUnique(all.Select(s => s.Status)).Select(k => { var c = all.Count(s => s.Status == k); return Row(("item", k), ("stays", c), ("share", Pct1(c, all.Count))); }));
        var need = Table("needs-times", "CheckIn_Hour, CheckOut_Hour, Turnover_EarlyLate, AdvanceLead", [T("item", "Sheet"), T("state", "State")],
            new[] { "CheckIn_Hour", "CheckOut_Hour", "Turnover_EarlyLate", "AdvanceLead" }.Select(k => Row(("item", k), ("state", "Pending: the analytics input carries dates only (needs check-in / out times and booking date)"))));
        var sr = Series("arrivals-dow", "bar", "Arrivals by weekday", DowLabel, arr.Rows.Select(r => (long)r.Cells["stays"]!).ToList(), false);
        return Result("rpt.study.operations-patterns", "Operations patterns", p with { Fy = fy }, [arr, dep, los, gs, pur, src, status, need, sr], i);
    }

    private static readonly string[] Buckets = ["0-30", "31-90", "91-180", "181-365", "366-730", "731+"];
    public static string Bucket(long age) => age <= 30 ? Buckets[0] : age <= 90 ? Buckets[1] : age <= 180 ? Buckets[2] : age <= 365 ? Buckets[3] : age <= 730 ? Buckets[4] : Buckets[5];
    public static string[] BucketNames => Buckets;
    public static long AgeDays(string asOf, string d) => ParseDay(asOf).DayNumber - ParseDay(d).DayNumber; // F12-D L5: invariant yyyy-MM-dd, whole days
    private static DateOnly ParseDay(string x) => DateOnly.ParseExact(x.Length > 10 ? x[..10] : x, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static ReportResult RoomsAndRates(AnInput i, AnParams p)
    {
        var fy = FocusFy(i, p); var fys = FyRows(i); var st = BillStay(i); var om = OccupancyMonths(i);
        var ids = i.Rooms.Select(r => r.Id).OrderBy(x => x, StringComparer.Ordinal).ToList(); var ns = Nights(i);
        long days = om.Where(o => FyOf(MonthStart(o.Month)) == fy).Sum(o => (long)o.Days);
        var billsFy = i.Bills.Where(b => FyOf(b.Date) == fy).ToList();
        var per = ids.Select(id =>
        {
            var bs = billsFy.Where(b => st.TryGetValue(b.Stay, out var s) && s.Room == id).ToList();
            return (Id: id, Type: RoomType(i, id), Nights: (long)ns.Count(n => n.Room == id && FyOf(n.Date) == fy), Rev: bs.Sum(b => b.RoomChargePaise), Cn: bs.Sum(b => (long)b.ChargedNights));
        }).ToList();
        var totNights = per.Sum(r => r.Nights); var totRev = per.Sum(r => r.Rev);
        var rn = Table("room-nights-revenue-rate", $"Room Nights / Revenue / Avg Rate / Share / Occupancy ({FyLabel(fy)})",
            [T("room", "Room"), T("type", "Type"), I("nights", "Room-nights"), M("revenue", "Room revenue"), M("avg", "Avg rate"), P("share", "Revenue share %"), P("occ", "Occupancy %")],
            per.Select(r => Row(("room", r.Id), ("type", r.Type), ("nights", r.Nights), ("revenue", r.Rev), ("avg", RDiv(r.Rev, r.Cn)), ("share", Pct1(r.Rev, totRev)), ("occ", Pct1(r.Nights, days)))));
        var ranked = per.OrderByDescending(r => r.Rev).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        var tb = Table("top-bottom", "Top / Bottom rooms (by room revenue)", [T("rank", "Rank"), T("room", "Room"), M("revenue", "Room revenue"), I("nights", "Room-nights")],
            ranked.Take(5).Select((r, k) => Row(("rank", "Top " + (k + 1)), ("room", r.Id), ("revenue", r.Rev), ("nights", r.Nights)))
                .Concat(Enumerable.Reverse(ranked.Skip(Math.Max(0, ranked.Count - 5))).Select((r, k) => Row(("rank", "Bottom " + (k + 1)), ("room", r.Id), ("revenue", r.Rev), ("nights", r.Nights)))));
        double avgN = per.Count > 0 ? (double)totNights / per.Count : 0;
        var low = Table("low-sales", "Low sales (below half the room average)", [T("room", "Room"), I("nights", "Room-nights"), I("avg", "Average per room")],
            per.Where(r => r.Nights * 2 < avgN).Select(r => Row(("room", r.Id), ("nights", r.Nights), ("avg", (long)Math.Floor(avgN + 0.5)))));
        var types = SortedUnique(per.Select(r => r.Type));
        var tcols = new List<ReportColumn> { T("type", "Room type") }; tcols.AddRange(fys.Select(f => M("y" + f.Fy, f.Fy)));
        var tf = Table("room-type-fy", "Room type by FY (room revenue)", tcols, types.Select(t =>
        {
            var c = new Dictionary<string, object?> { ["type"] = t };
            foreach (var f in fys) c["y" + f.Fy] = i.Bills.Where(b => FyOf(b.Date) == f.Fy && RoomType(i, st.TryGetValue(b.Stay, out var s) ? s.Room : "") == t).Sum(b => b.RoomChargePaise);
            return RowD(c);
        }));
        var tt = Table("tariff-trend", "Tariff trend (ADR by FY)", [T("fy", "FY"), M("adr", "ADR"), P("change", "Change %")], fys.Select((f, k) => Row(("fy", FyLabel(f.Fy)), ("adr", RDiv(f.R.Room, f.R.ChargedNights)),
            ("change", k > 0 ? Pct1(RDiv(f.R.Room, f.R.ChargedNights) - RDiv(fys[k - 1].R.Room, fys[k - 1].R.ChargedNights), RDiv(fys[k - 1].R.Room, fys[k - 1].R.ChargedNights)) : null))));
        var ov = billsFy.Where(b => st.TryGetValue(b.Stay, out var s) && b.ChargedNights > 0 && s.RatePaise > 0 && RDiv(b.RoomChargePaise, b.ChargedNights) != s.RatePaise).ToList();
        long Delta(IEnumerable<AnBill> bs) => bs.Sum(b => b.RoomChargePaise - st[b.Stay].RatePaise * b.ChargedNights);
        var below = ov.Where(b => RDiv(b.RoomChargePaise, b.ChargedNights) < st[b.Stay].RatePaise).ToList(); var above = ov.Where(b => RDiv(b.RoomChargePaise, b.ChargedNights) > st[b.Stay].RatePaise).ToList();
        var ro = Table("rate-overrides", $"Rate overrides (billed rate differs from the stay rate, {FyLabel(fy)})", [T("kind", "Kind"), I("bills", "Bills"), M("delta", "Net rate effect (billed minus rate)")],
            [Row(("kind", "Below stay rate"), ("bills", below.Count), ("delta", Delta(below))), Row(("kind", "Above stay rate"), ("bills", above.Count), ("delta", Delta(above)))]);
        var dc = Table("discount-control", "Discount control", [T("fy", "FY"), I("bills", "Bills with discount"), M("discount", "Discount"), P("pct", "Discount % of gross")],
            fys.Select(f => Row(("fy", FyLabel(f.Fy)), ("bills", i.Bills.Count(b => FyOf(b.Date) == f.Fy && b.DiscountPaise > 0)), ("discount", f.R.Discount), ("pct", Pct1(f.R.Discount, f.R.Room + f.R.Fnb + f.R.Other)))));
        var methods = SortedUnique(i.Receipts.Select(r => r.Method));
        var pcols = new List<ReportColumn> { T("fy", "FY") }; pcols.AddRange(methods.Select(m => M("p_" + m, m)));
        var pm = Table("payment-mix", "Payment mix by FY (received)", pcols, fys.Select(f =>
        {
            var c = new Dictionary<string, object?> { ["fy"] = FyLabel(f.Fy) };
            foreach (var m in methods) c["p_" + m] = i.Receipts.Where(r => FyOf(r.Date) == f.Fy && r.Method == m).Sum(r => r.AmountPaise);
            return RowD(c);
        }));
        var unpaid = i.Bills.Where(b => b.TotalPaise > b.PaidPaise).ToList();
        var ua = Table("unpaid-ageing", "Unpaid ageing (as of the business date)", [T("bucket", "Age (days)"), I("bills", "Bills"), M("due", "Due")], Buckets.Select(k =>
        {
            var bs = unpaid.Where(b => Bucket(AgeDays(i.AsOf, b.Date)) == k).ToList();
            return Row(("bucket", k), ("bills", bs.Count), ("due", bs.Sum(b => b.TotalPaise - b.PaidPaise)));
        }));
        var sr = Series("adr-by-fy", "line", "ADR by FY", fys.Select(f => f.Fy).ToList(), fys.Select(f => RDiv(f.R.Room, f.R.ChargedNights)).ToList(), true);
        return Result("rpt.study.rooms-and-rates", "Rooms, rates and discounts", p with { Fy = fy }, [rn, tb, low, tf, tt, ro, dc, pm, ua, sr], i);
    }
}
