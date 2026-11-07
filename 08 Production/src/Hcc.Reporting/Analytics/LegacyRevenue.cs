// F12 MEAS · meas-study-a · server twin of legacy-revenue.ts: RevenueMonthly, YearComparison, IndexVsBase, FyDeepDive. Same method names, same numbers.
using Hcc.Application.Reporting.Rpt;
using static Hcc.Reporting.Analytics.LegacyCore;

namespace Hcc.Reporting.Analytics;

public static class LegacyRevenue
{
    private static string YearOf(string fy, string mm) => (string.CompareOrdinal(mm, "04") >= 0 ? FyStartYear(fy) : FyStartYear(fy) + 1).ToString();

    public static ReportResult RevenueMonthly(AnInput i, AnParams p)
    {
        var fy = FocusFy(i, p); var rev = RevenueMonths(i); var fys = FyRows(i);
        var monthly = Table("monthly", $"Monthly ({FyLabel(fy)})",
            [T("month", "Month"), T("data", "Data"), I("bills", "Bills"), M("room", "Room"), M("fnb", "F&B"), M("other", "Other"), M("discount", "Discount"), M("netBilled", "Net billed"), M("gst", "GST"),
             M("billed", "Total billed"), I("chargedNights", "Charged nights"), M("adr", "ADR"), M("received", "Cash received"), P("receivedPct", "Received % of billed")],
            rev.Where(r => FyOf(MonthStart(r.Month)) == fy).Select(r => Row(("month", MonthLabel(r.Month)), ("data", Marker(i, MonthStart(r.Month), r.Month + "-28")), ("bills", r.Bills), ("room", r.Room),
                ("fnb", r.Fnb), ("other", r.Other), ("discount", r.Discount), ("netBilled", Net(r)), ("gst", r.Gst), ("billed", r.Total), ("chargedNights", r.ChargedNights), ("adr", Adr(r)),
                ("received", r.Received), ("receivedPct", Pct1(r.Received, r.Total)))));
        var summary = Table("fy-summary", "FY summary",
            [T("fy", "FY"), T("data", "Data"), I("bills", "Bills"), M("netBilled", "Net billed"), M("gst", "GST"), M("billed", "Total billed"), I("chargedNights", "Charged nights"), M("adr", "ADR"),
             M("received", "Cash received"), P("receivedPct", "Received % of billed"), T("note", "Note")],
            fys.Select(f => Row(("fy", FyLabel(f.Fy)), ("data", Marker(i, f.Fy[..4] + "-04-01", (FyStartYear(f.Fy) + 1) + "-03-31")), ("bills", f.R.Bills), ("netBilled", Net(f.R)), ("gst", f.R.Gst),
                ("billed", f.R.Total), ("chargedNights", f.R.ChargedNights), ("adr", Adr(f.R)), ("received", f.R.Received), ("receivedPct", Pct1(f.R.Received, f.R.Total)),
                ("note", f.Partial ? "Partial year" : f.Normal ? "" : "COVID window"))));
        var gridCols = new List<ReportColumn> { T("fy", "FY") }; gridCols.AddRange(FyMonths.Select(m => M("m" + m, MonLabel[m]))); gridCols.Add(M("total", "Total"));
        var grid = Table("fy-month-grid", "FY x Month grid (total billed)", gridCols, fys.Select(f =>
        {
            var c = new Dictionary<string, object?> { ["fy"] = FyLabel(f.Fy), ["total"] = f.R.Total };
            foreach (var m in FyMonths) c["m" + m] = rev.FirstOrDefault(r => r.Month == $"{YearOf(f.Fy, m)}-{m}")?.Total ?? 0L;
            return RowD(c);
        }));
        var sr = Series("billed-by-fy", "bar", "Total billed by FY", fys.Select(f => f.Fy).ToList(), fys.Select(f => f.R.Total).ToList(), true);
        var sm = Series("billed-by-month", "line", $"Total billed by month {FyLabel(fy)}", monthly.Rows.Select(r => (string)r.Cells["month"]!).ToList(), monthly.Rows.Select(r => (long)r.Cells["billed"]!).ToList(), true);
        return Result("rpt.study.revenue-monthly", "Monthly revenue and cash (FY history)", p with { Fy = fy }, [monthly, summary, grid, sr, sm], i);
    }

    public static ReportResult YearComparison(AnInput i, AnParams p)
    {
        var fys = FyRows(i); var rev = RevenueMonths(i);
        var from = p.From ?? $"{FyStartYear(FyOf(i.AsOf))}-04-01"; var to = p.To ?? i.AsOf; var mdFrom = from[5..]; var mdTo = to[5..];
        var summary = Table("summary", "Summary", [T("fy", "FY"), M("billed", "Total billed"), I("nights", "Room-nights sold"), P("occ", "Occupancy %"), M("adr", "ADR"), P("yoy", "YoY % (as reported)"), T("note", "Note")],
            fys.Select((f, k) => Row(("fy", FyLabel(f.Fy)), ("billed", f.R.Total), ("nights", f.Sold), ("occ", Pct1(f.Sold, f.Capacity)), ("adr", Adr(f.R)),
                ("yoy", k > 0 && fys[k - 1].R.Total > 0 ? Pct1(f.R.Total - fys[k - 1].R.Total, fys[k - 1].R.Total) : null),
                ("note", f.Partial ? "Partial year, use like-for-like" : f.Normal ? "" : "COVID window"))));
        var gcols = new List<ReportColumn> { T("month", "Month") }; gcols.AddRange(fys.Select(f => M("y" + f.Fy, f.Fy)));
        var grid = Table("month-year-grid", "Month x Year (total billed)", gcols, FyMonths.Select(m =>
        {
            var c = new Dictionary<string, object?> { ["month"] = MonLabel[m] };
            foreach (var f in fys) c["y" + f.Fy] = rev.FirstOrDefault(r => r.Month == $"{YearOf(f.Fy, m)}-{m}")?.Total ?? 0L;
            return RowD(c);
        }));
        var yoy = Table("yoy", "YoY growth (billed, room-nights, ADR)", [T("fy", "FY"), P("billed", "Billed %"), P("nights", "Room-nights %"), P("adr", "ADR %")],
            fys.Skip(1).Select((f, k) => { var a = fys[k]; return Row(("fy", FyLabel(f.Fy)), ("billed", Pct1(f.R.Total - a.R.Total, a.R.Total)), ("nights", Pct1(f.Sold - a.Sold, a.Sold)), ("adr", Pct1(Adr(f.R) - Adr(a.R), Adr(a.R)))); }));
        long Window(string fy)
        {
            var y0 = FyStartYear(fy); var a = (string.CompareOrdinal(mdFrom, "04-01") >= 0 ? y0 : y0 + 1) + "-" + mdFrom; var b = (string.CompareOrdinal(mdTo, "04-01") >= 0 ? y0 : y0 + 1) + "-" + mdTo;
            return i.Bills.Where(x => string.CompareOrdinal(x.Date, a) >= 0 && string.CompareOrdinal(x.Date, b) <= 0).Sum(x => x.TotalPaise);
        }
        int WindowBills(string fy)
        {
            var y0 = FyStartYear(fy); var a = (string.CompareOrdinal(mdFrom, "04-01") >= 0 ? y0 : y0 + 1) + "-" + mdFrom; var b = (string.CompareOrdinal(mdTo, "04-01") >= 0 ? y0 : y0 + 1) + "-" + mdTo;
            return i.Bills.Count(x => string.CompareOrdinal(x.Date, a) >= 0 && string.CompareOrdinal(x.Date, b) <= 0);
        }
        var lfl = Table("like-for-like", $"Like-for-like {from[8..]}-{from.Substring(5, 2)} to {to[8..]}-{to.Substring(5, 2)} (date-exact)", [T("fy", "FY"), M("billed", "Total billed"), I("bills", "Bills"), P("vsPrev", "vs previous year %")],
            fys.Select((f, k) =>
            {
                var t = Window(f.Fy); var pa = k > 0 ? Window(fys[k - 1].Fy) : 0;
                return Row(("fy", FyLabel(f.Fy)), ("billed", t), ("bills", WindowBills(f.Fy)), ("vsPrev", k > 0 && pa > 0 ? Pct1(t - pa, pa) : null));
            }));
        var ccols = new List<ReportColumn> { T("fy", "FY") }; ccols.AddRange(FyMonths.Select(m => M("m" + m, MonLabel[m])));
        var cum = Table("cumulative", "Cumulative YTD (total billed)", ccols, fys.Select(f =>
        {
            long acc = 0; var c = new Dictionary<string, object?> { ["fy"] = FyLabel(f.Fy) };
            foreach (var m in FyMonths) { acc += rev.FirstOrDefault(r => r.Month == $"{YearOf(f.Fy, m)}-{m}")?.Total ?? 0L; c["m" + m] = acc; }
            return RowD(c);
        }));
        var full = fys.Where(f => f.Normal).ToList();
        var cagrRows = new List<ReportRow>();
        if (full.Count >= 2)
        {
            var a = full[0]; var b = full[^1]; var yrs = FyStartYear(b.Fy) - FyStartYear(a.Fy);
            var g = yrs > 0 && a.R.Total > 0 ? Math.Round(Math.Round((Math.Pow((double)b.R.Total / a.R.Total, 1.0 / yrs) - 1) * 1000, MidpointRounding.AwayFromZero) / 10, 1) : 0;
            cagrRows.Add(Row(("from", FyLabel(a.Fy)), ("to", FyLabel(b.Fy)), ("years", yrs), ("first", a.R.Total), ("last", b.R.Total), ("cagr", g)));
        }
        var cagr = Table("cagr", "CAGR (normal full years)", [T("from", "From FY"), T("to", "To FY"), I("years", "Years"), M("first", "First billed"), M("last", "Last billed"), P("cagr", "CAGR %")], cagrRows);
        var mix = Table("head-mix", "Revenue head mix (share of net billed)", [T("fy", "FY"), P("room", "Room %"), P("fnb", "F&B %"), P("other", "Other %"), P("discount", "Discount %")],
            fys.Select(f => { var g = f.R.Room + f.R.Fnb + f.R.Other; return Row(("fy", FyLabel(f.Fy)), ("room", Pct1(f.R.Room, g)), ("fnb", Pct1(f.R.Fnb, g)), ("other", Pct1(f.R.Other, g)), ("discount", Pct1(f.R.Discount, g))); }));
        var sr = Series("billed-by-fy", "bar", "Total billed by FY", fys.Select(f => f.Fy).ToList(), fys.Select(f => f.R.Total).ToList(), true);
        return Result("rpt.study.year-comparison", "Year comparison", p with { From = from, To = to }, [summary, grid, yoy, lfl, cum, cagr, mix, sr], i, from, to);
    }

    public static ReportResult IndexVsBase(AnInput i, AnParams p)
    {
        var bs = p.BaseFy ?? "2018-19"; var fys = FyRows(i); var rev = RevenueMonths(i); var b = fys.FirstOrDefault(f => f.Fy == bs);
        var index = Table("index", $"Index vs {FyLabel(bs)} (base = 100)", [T("fy", "FY"), P("billed", "Billed index"), P("nights", "Room-night index"), P("adr", "ADR index"), T("note", "Note")],
            b is null ? [] : fys.Select(f => Row(("fy", FyLabel(f.Fy)), ("billed", Idx1(f.R.Total, b.R.Total)), ("nights", Idx1(f.Sold, b.Sold)), ("adr", Idx1(Adr(f.R), Adr(b.R))), ("note", f.Partial ? "Partial year" : f.Normal ? "" : "COVID window"))));
        var covid = fys.Where(f => !f.Normal && !f.Partial).ToList();
        var impact = Table("covid-impact", "COVID impact and recovery", [T("fy", "FY"), M("billed", "Total billed"), M("vsBase", "Change vs base"), P("vsBasePct", "Change %"), T("status", "Status")],
            b is null ? [] : fys.Where(f => f.Fy != bs).Select(f =>
            {
                var back = f.Normal && f.R.Total >= b.R.Total;
                return Row(("fy", FyLabel(f.Fy)), ("billed", f.R.Total), ("vsBase", f.R.Total - b.R.Total), ("vsBasePct", Pct1(f.R.Total - b.R.Total, b.R.Total)),
                    ("status", f.Partial ? "Partial year" : covid.Contains(f) ? "COVID window" : back ? "Recovered" : "Below base"));
            }));
        var normal = fys.Where(f => f.Normal).ToList();
        var months = rev.Where(r => normal.Any(f => f.Fy == FyOf(MonthStart(r.Month)))).OrderByDescending(r => r.Total).ThenBy(r => r.Month, StringComparer.Ordinal).ToList();
        var bwRows = months.Take(5).Select((r, k) => Row(("rank", "Best " + (k + 1)), ("month", MonthLabel(r.Month)), ("billed", r.Total), ("kind", "best")))
            .Concat(Enumerable.Reverse(months.Skip(Math.Max(0, months.Count - 5))).Select((r, k) => Row(("rank", "Worst " + (k + 1)), ("month", MonthLabel(r.Month)), ("billed", r.Total), ("kind", "worst"))));
        var bw = Table("best-worst", "Best and worst months (normal years, total billed)", [T("rank", "Rank"), T("month", "Month"), M("billed", "Total billed"), T("kind", "Kind")], bwRows);
        var sr = SeriesPct("index-billed", "line", "Billed index vs base", fys.Select(f => f.Fy).ToList(), b is null ? [] : fys.Select(f => Idx1(f.R.Total, b.R.Total)).ToList());
        return Result("rpt.study.index-vs-base", "Index vs a base year", p with { Fy = FocusFy(i, p), BaseFy = bs }, [index, impact, bw, sr], i);
    }

    public static ReportResult FyDeepDive(AnInput i, AnParams p)
    {
        var fy = FocusFy(i, p); var prev = FyPrev(fy); var fys = FyRows(i); var cur = fys.FirstOrDefault(f => f.Fy == fy); var old = fys.FirstOrDefault(f => f.Fy == prev);
        var top = Math.Max(1, Math.Min(int.TryParse(p.TopN, out var tn) ? tn : 10, 100));
        var bills = i.Bills.Where(b => FyOf(b.Date) == fy).ToList(); var st = BillStay(i);
        var pvRows = new List<ReportRow>();
        if (cur is not null && old is not null)
        {
            var volume = (cur.R.ChargedNights - old.R.ChargedNights) * Adr(old.R); var price = (Adr(cur.R) - Adr(old.R)) * cur.R.ChargedNights;
            var nonRoom = (Net(cur.R) - cur.R.Room) - (Net(old.R) - old.R.Room);
            pvRows = [Row(("item", "Total billed, previous FY"), ("amount", old.R.Total)), Row(("item", "Volume effect (nights x old ADR)"), ("amount", volume)), Row(("item", "Price effect (ADR change x new nights)"), ("amount", price)),
                Row(("item", "Non-room net change (F&B, other, discount)"), ("amount", nonRoom)), Row(("item", "GST change and rounding"), ("amount", cur.R.Total - old.R.Total - volume - price - nonRoom)),
                Row(ReportRowKind.Total, ("item", "Total billed, this FY"), ("amount", cur.R.Total))];
        }
        var pv = Table("a-price-volume", $"A Price-volume {FyLabel(prev)} to {FyLabel(fy)}", [T("item", "Item"), M("amount", "Amount")], pvRows);
        var nb = Table("b-neighbours", "B Against neighbouring years", [T("fy", "FY"), M("billed", "Total billed"), I("nights", "Room-nights"), M("adr", "ADR"), P("occ", "Occupancy %")],
            fys.Where(f => Math.Abs(FyStartYear(f.Fy) - FyStartYear(fy)) <= 1).Select(f => Row(("fy", FyLabel(f.Fy)), ("billed", f.R.Total), ("nights", f.Sold), ("adr", Adr(f.R)), ("occ", Pct1(f.Sold, f.Capacity)))));
        var seg = new Dictionary<string, (long N, long Rev)>();
        foreach (var b in bills) { var t = st.TryGetValue(b.Stay, out var s) ? RoomType(i, s.Room) : "unassigned"; var r = seg.GetValueOrDefault(t); seg[t] = (r.N + b.ChargedNights, r.Rev + b.RoomChargePaise); }
        var segTotal = seg.Values.Sum(x => x.Rev);
        var c = Table("c-room-type", "C Segments by room type", [T("type", "Room type"), I("nights", "Charged nights"), M("room", "Room revenue"), M("adr", "ADR"), P("share", "Share of room revenue %")],
            seg.OrderByDescending(kv => kv.Value.Rev).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => Row(("type", kv.Key), ("nights", kv.Value.N), ("room", kv.Value.Rev), ("adr", RDiv(kv.Value.Rev, kv.Value.N)), ("share", Pct1(kv.Value.Rev, segTotal)))));
        var src = new Dictionary<string, (long N, long T)>();
        foreach (var b in bills) { var k = st.TryGetValue(b.Stay, out var s) && s.Source is not null ? s.Source : "unknown"; var r = src.GetValueOrDefault(k); src[k] = (r.N + 1, r.T + b.TotalPaise); }
        var billsTotal = bills.Sum(x => x.TotalPaise);
        var d = Table("d-source", "D Booking source", [T("source", "Source"), I("bills", "Bills"), M("billed", "Total billed"), P("share", "Share %")],
            src.OrderByDescending(kv => kv.Value.T).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => Row(("source", kv.Key), ("bills", kv.Value.N), ("billed", kv.Value.T), ("share", Pct1(kv.Value.T, billsTotal)))));
        var pay = new Dictionary<string, long>();
        foreach (var r in i.Receipts) if (FyOf(r.Date) == fy) pay[r.Method] = pay.GetValueOrDefault(r.Method) + r.AmountPaise;
        var payTotal = pay.Values.Sum();
        var e = Table("e-payment-mix", "E Payment mix", [T("method", "Method"), M("amount", "Received"), P("share", "Share %")],
            pay.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => Row(("method", kv.Key), ("amount", kv.Value), ("share", Pct1(kv.Value, payTotal)))));
        var f = Table("f-top-bills", $"F Top {top} bills", [T("bill", "Bill"), T("guest", "Guest", ReportMask.GuestName), T("date", "Date"), M("billed", "Total billed"), I("nights", "Charged nights")],
            bills.OrderByDescending(b => b.TotalPaise).ThenBy(b => b.Id, StringComparer.Ordinal).Take(top).Select(b => Row(("bill", b.Id), ("guest", st.TryGetValue(b.Stay, out var s) ? s.GuestKey : ""), ("date", b.Date), ("billed", b.TotalPaise), ("nights", b.ChargedNights))));
        var los = new Dictionary<int, long>();
        foreach (var s in i.Stays) if (FyOf(s.From) == fy && Slept(s)) { var k = Math.Min(7, Math.Max(1, s.Nights)); los[k] = los.GetValueOrDefault(k) + 1; }
        var losTotal = los.Values.Sum();
        var g = Table("g-length-of-stay", "G Length of stay (stays by arrival)", [T("los", "Nights"), I("stays", "Stays"), P("share", "Share %")],
            Enumerable.Range(1, 7).Select(k => Row(("los", k == 7 ? "7+" : k.ToString()), ("stays", los.GetValueOrDefault(k)), ("share", Pct1(los.GetValueOrDefault(k), losTotal)))));
        var revm = RevenueMonths(i);
        var sm = Series("months", "line", $"Total billed by month {FyLabel(fy)}", FyMonths.Select(m => MonLabel[m]).ToList(),
            FyMonths.Select(m => revm.FirstOrDefault(r => r.Month == $"{YearOf(fy, m)}-{m}")?.Total ?? 0L).ToList(), true);
        return Result("rpt.study.fy-deep-dive", "FY deep dive (price-volume, segments, top bills)", p with { Fy = fy, TopN = top.ToString() }, [pv, nb, c, d, e, f, g, sm], i);
    }
}
