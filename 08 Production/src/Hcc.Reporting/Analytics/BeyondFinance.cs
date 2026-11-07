// F12 MEAS · meas-study-b · server twin of beyond-finance.ts: FinanceCost, CorporateAccounts, ForecastScenarios, OwnerDashboard.
using static Hcc.Reporting.Analytics.Bl;

namespace Hcc.Reporting.Analytics;

public static class BeyondFinance
{
    public static AnResult FinanceCost(AnInput inp, AnParams p)
    {
        var c = new Ctx(inp); var fy = c.FyParam(p); var ex = p.Extras; var from = FyStart(fy); var to = FyEnd(fy); var bs = c.Bills.Where(b => b.Fy == fy).ToList();
        var room = bs.Sum(b => b.Rev); var fnb = bs.Sum(b => b.B.FnbPaise); var oth = bs.Sum(b => b.B.OtherPaise); var disc = bs.Sum(b => b.B.DiscountPaise); var net = room + fnb + oth - disc;
        var exps = ex.Expenses.Where(e => Le(from, e.Date) && Le(e.Date, to)).ToList(); var cats = exps.Select(e => e.Category).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(); var cost = exps.Sum(e => e.AmountPaise);
        var pl = new List<object> { R("Rooms revenue", room), R("Food & beverage", fnb), R("Other revenue", oth), R("Less discounts", -disc), R("Net revenue (before GST)", net) };
        foreach (var k in cats) pl.Add(R("Cost: " + k, exps.Where(e => e.Category == k).Sum(e => e.AmountPaise)));
        pl.Add(R("Total cost recorded", cost)); pl.Add(R("Gross operating profit", net - cost));
        var cn = c.Bills.Where(b => b.Fy == fy).Sum(b => b.Cn); var adr = Ratio(room, cn) ?? 0; var rooms = c.RoomsOn(Lt(c.End, to) ? c.End : to).Count; var fixedC = p.Num("fixedPerMonth", 0); var vr = p.Num("variablePerRn", 0);
        var avail = c.Capacity(from, to); var sold = c.SleptIn(from, to);
        var be = new List<object> { R(fy, adr, (long)rooms, (long)fixedC, (long)vr, adr > vr ? Pct1(fixedC, (adr - vr) * rooms * 30.0) : null, Pct1(sold, avail)) };
        var paid = cats.Select(k => { var x = exps.Where(e => e.Category == k).ToList(); return (object)R(k, (long)x.Count, x.Sum(e => e.AmountPaise)); }).ToList();
        var rc = inp.Receipts.Where(r => Le(from, r.Date) && Le(r.Date, to)).ToList(); var methods = rc.Select(r => r.Method).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        var cash = new List<object>();
        foreach (var m in c.FyMonths(fy)) foreach (var k in methods) { var x = rc.Where(r => r.Date[..7] == m && r.Method == k).ToList(); if (x.Count > 0) cash.Add(R(m, k, (long)x.Count, x.Sum(r => r.AmountPaise))); }
        var gst = c.FyMonths(fy).Select(m =>
        {
            var x = c.Bills.Where(b => b.Month == m).ToList(); var taxable = x.Sum(b => b.Gross - b.B.DiscountPaise); var g = x.Sum(b => b.B.GstPaise);
            return (object)R(m, taxable, g, Pct1(g, taxable), x.Sum(b => b.B.TotalPaise), x.Sum(b => b.B.PaidPaise));
        }).ToList();
        return Finish(
        [
            Sec("pl", "USALI P&L", [T("line", "Line"), M("amount", "Amount")], pl),
            Sec("breakeven", "Break-even", [T("fy", "FY"), M("adr", "ADR"), I("rooms", "Rooms"), M("fixed", "Fixed cost per month (input)"), M("variable", "Variable cost per room-night (input)"), Pc("bePct", "Break-even occupancy %"), Pc("occ", "Actual occupancy %")], be),
            Sec("paidouts", "Paid-outs", [T("category", "Category"), I("count", "Entries"), M("amount", "Amount")], paid),
            Sec("cash", "Cash & Bank", [T("month", "Month"), T("method", "Method"), I("receipts", "Receipts"), M("amount", "Amount")], cash),
            Sec("gst", "GST Reconciliation", [T("month", "Month"), M("taxable", "Taxable value"), M("gst", "GST billed"), Pc("effective", "Effective GST %"), M("billed", "Total billed"), M("paid", "Paid")], gst),
            Series("billed-by-month", "bar", "Total billed by month", gst.Select(r => MonthLabel((string)((object?[])r)[0]!)), gst.Select(r => (double?)(long?)((object?[])r)[4]), true),
        ], c.Stays.Any(s => s.Legacy));
    }

    private sealed class Co { public string Name = ""; public SortedSet<string> Fys = new(StringComparer.Ordinal); public string Last = ""; public long Stays; public long Net; }

    public static AnResult CorporateAccounts(AnInput inp, AnParams p)
    {
        var c = new Ctx(inp); var fy = c.FyParam(p);
        string Key(string? s) => Norm(s).ToUpperInvariant();
        var netOf = new Dictionary<string, long>(StringComparer.Ordinal); var dueOf = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var b in inp.Bills) { netOf[b.Stay] = netOf.GetValueOrDefault(b.Stay) + b.RoomChargePaise + b.FnbPaise + b.OtherPaise - b.DiscountPaise; dueOf[b.Stay] = dueOf.GetValueOrDefault(b.Stay) + Math.Max(0, b.TotalPaise - b.PaidPaise); }
        var by = new Dictionary<string, Co>(StringComparer.Ordinal);
        foreach (var s in c.Stays)
        {
            if (string.IsNullOrEmpty(s.Company)) continue; var k = Key(s.Company);
            if (!by.TryGetValue(k, out var o)) by[k] = o = new Co { Name = Norm(s.Company), Last = s.From };
            o.Fys.Add(FyOf(s.From)); if (Lt(o.Last, s.From)) o.Last = s.From; o.Stays++; o.Net += netOf.GetValueOrDefault(s.Id);
        }
        long N(AnStay s) => netOf.GetValueOrDefault(s.Id);
        var rows = by.Select(kv => (k: kv.Key, o: kv.Value, ss: c.Stays.Where(s => Key(s.Company) == kv.Key && FyOf(s.From) == fy).ToList())).Where(x => x.ss.Count > 0)
            .OrderByDescending(x => x.ss.Sum(N)).ThenBy(x => x.k, StringComparer.Ordinal)
            .Select(x => (object)R(x.o.Name, (long)x.ss.Count, x.ss.Sum(s => (long)s.Nights), x.ss.Sum(N), Ratio(x.ss.Sum(N), x.ss.Sum(s => (long)s.Nights)), x.o.Fys.First(), x.o.Last, (long)x.o.Fys.Count,
                c.Stays.Where(s => Key(s.Company) == x.k).Sum(s => dueOf.GetValueOrDefault(s.Id)))).ToList();
        var cutoff = AddDays(c.End, -365);
        var win = by.Values.Where(o => Lt(o.Last, cutoff)).OrderByDescending(o => o.Net).ThenBy(o => o.Name, StringComparer.Ordinal).Take((int)p.Num("topN", 50)).Select(o => (object)R(o.Name, o.Last, o.Stays, o.Net, (long)o.Fys.Count)).ToList();
        List<string> Act(string g) => by.Values.Where(o => o.Fys.Contains(g)).Select(o => Key(o.Name)).ToList();
        var bas = c.Fys.Select((f, i) =>
        {
            var a = Act(f); var prev = i > 0 ? Act(c.Fys[i - 1]) : []; var ever = c.Fys.Take(i).SelectMany(Act).ToList();
            return (object)R(f, (long)a.Count, (long)a.Count(x => !ever.Contains(x)), (long)prev.Count(x => !a.Contains(x)));
        }).ToList();
        return Finish(
        [
            Sec("accounts", "Corporate Accounts", [T("company", "Company"), I("stays", "Stays (FY)"), I("nights", "Nights (FY)"), M("net", "Net billed (FY)"), M("adr", "Net per night"), T("firstFy", "First FY"), Dt("lastStay", "Last stay"), I("years", "Years active"), M("unpaid", "Unpaid on bills (lifetime)")], rows),
            Sec("winback", "Win-back list (no stay in the last 12 months)", [T("company", "Company"), Dt("lastStay", "Last stay"), I("stays", "Stays (lifetime)"), M("net", "Net billed (lifetime)"), I("years", "Years active")], win),
            Sec("base", "Account base", [T("fy", "FY"), I("active", "Active companies"), I("new", "New companies"), I("lost", "Not back from last FY")], bas),
            Series("active-companies", "bar", "Active companies by FY", bas.Select(r => (string)((object?[])r)[0]!), bas.Select(r => (double?)(long?)((object?[])r)[1])),
        ], c.Stays.Any(s => s.Legacy));
    }

    private static List<string> NormalFys(Ctx c) => c.Fys.Where(f => c.FyMonths(f).Count == 12 && !c.Covid(FyStart(f)) && !c.Covid(FyEnd(f)) && !(Lt(FyStart(f), c.Inp.CovidWindow.From) && Lt(c.Inp.CovidWindow.To, FyEnd(f)))).ToList();

    public static AnResult ForecastScenarios(AnInput inp, AnParams p)
    {
        var c = new Ctx(inp); var fy = c.FyParam(p); var nf = NormalFys(c); var growth = p.Num("budgetGrowthPct", 10);
        long Mrev(string m) => c.Bills.Where(b => b.Month == m).Sum(b => b.Rev);
        List<string> CalM(int k) => nf.Select(f => MonthsBetween(FyStart(f), FyEnd(f))[k]).ToList();
        var seas = Enumerable.Range(0, 12).Select(k =>
        {
            var ms = CalM(k);
            return (Rev: ms.Sum(Mrev), Sold: ms.Sum(m => c.SleptIn(m + "-01", MonthEnd(m))), Avail: ms.Sum(m => c.Capacity(m + "-01", MonthEnd(m))), Cn: ms.Sum(m => c.Bills.Where(b => b.Month == m).Sum(b => b.Cn)));
        }).ToList();
        var tR = seas.Sum(x => x.Rev); var tS = seas.Sum(x => x.Sold); var tA = seas.Sum(x => x.Avail); var tC = seas.Sum(x => x.Cn);
        var si = seas.Select((x, k) => (object)R(MonFy[k], Pct1(x.Rev * 12.0, tR), Pct1((double)x.Sold * tA, (double)x.Avail * tS), x.Cn > 0 && tC > 0 ? Pct1((double)Ratio(x.Rev, x.Cn)!.Value, (double)Ratio(tR, tC)!.Value) : null, (long)nf.Count)).ToList();
        var months = MonthsBetween(FyStart(fy), FyEnd(fy)); var lastEnd = c.End[..7]; var actualMs = months.Where(m => string.CompareOrdinal(m, lastEnd) <= 0).ToList();
        var ytd = actualMs.Sum(Mrev); var seasActual = actualMs.Sum(m => seas[months.IndexOf(m)].Rev);
        static string Prev(string m) => (int.Parse(m[..4]) - 1).ToString() + m[4..];
        var fc = months.Select((m, k) =>
        {
            var act = string.CompareOrdinal(m, lastEnd) <= 0; var ly = Mrev(Prev(m)); long? sea = seasActual > 0 ? Ratio((double)ytd * seas[k].Rev, seasActual) : null;
            return (object)R(m, act ? "Actual" : "Forecast", act ? Mrev(m) : null, ly, sea, sea is null ? ly : Ratio(ly + sea.Value, 2));
        }).ToList();
        var from = FyStart(fy); var to = Lt(c.End, FyEnd(fy)) ? c.End : FyEnd(fy); var sold = c.SleptIn(from, to); var avail = c.Capacity(from, to);
        var fb = c.Bills.Where(b => b.Fy == fy).ToList(); var adr = Ratio(fb.Sum(b => b.Rev), fb.Sum(b => b.Cn)) ?? 0;
        var rooms = c.RoomsOn(to).Count; var yearAvail = rooms * 365L; var occT = avail > 0 ? (long)Math.Floor((sold * 2000.0 + avail) / (2.0 * avail)) : 0; var adrD = p.Num("adrDeltaPct", 5);
        var scen = new (double Dp, double Da)[] { (-5, 0), (0, 0), (0, adrD), (5, adrD) }.Select(s =>
        {
            var o = Math.Max(0, occT + (long)s.Dp * 10); var a = Ratio((double)adr * (100 + s.Da), 100)!.Value;
            return (object)R($"Occupancy {(s.Dp >= 0 ? "+" : "")}{Fmt(s.Dp)} pts, ADR {(s.Da >= 0 ? "+" : "")}{Fmt(s.Da)}%", o / 10.0, a, Ratio((double)yearAvail * o * a, 1000));
        }).ToList();
        var bud = months.Select(m =>
        {
            var b = Ratio(Mrev(Prev(m)) * (100 + growth), 100)!.Value; long? a = string.CompareOrdinal(m, lastEnd) <= 0 ? Mrev(m) : null;
            return (object)R(m, b, a, a is null ? null : a - b, a is null || b <= 0 ? null : a >= b ? Pct1(a.Value - b, b) : -Pct1(b - a.Value, b));
        }).ToList();
        var co = p.Num("compOccPct"); var ca = p.Num("compAdr"); var occ = Pct1(sold, avail);
        double? mpi = co > 0 && occ is not null ? Math.Floor((occ.Value * 2000 + co) / (2 * co)) / 10 : null; double? ari = ca > 0 ? Pct1(adr, ca) : null;
        double? rgi = mpi is not null && ari is not null ? Math.Floor(mpi.Value * ari.Value * 10 / 100 + 0.5) / 10 : null;
        var comp = new List<object> { R(fy, occ, co > 0 ? co : null, mpi, adr, ca > 0 ? (long)ca : null, ari, rgi) };
        return Finish(
        [
            Sec("seasonality", "Seasonality Indices", [T("month", "Month"), Pc("revIdx", "Revenue index"), Pc("occIdx", "Occupancy index"), Pc("adrIdx", "ADR index"), I("years", "Normal years used")], si),
            Sec("forecast", "Forecast", [T("month", "Month"), T("kind", "Actual / forecast"), M("actual", "Actual room revenue"), M("lastYear", "Same month last year"), M("seasonal", "Seasonal method"), M("blend", "Blend")], fc),
            Sec("scenarios", "Scenarios", [T("scenario", "Scenario"), Pc("occ", "Occupancy %"), M("adr", "ADR"), M("revenue", "Room revenue for a full year")], scen),
            Sec("budget", "Budget vs Actual", [T("month", "Month"), M("budget", "Budget (last year + growth %)"), M("actual", "Actual"), M("variance", "Variance"), Pc("variancePct", "Variance %")], bud),
            Sec("compset", "Comp Set Index", [T("fy", "FY"), Pc("occ", "Our occupancy %"), Pc("compOcc", "Comp-set occupancy % (input)"), Pc("mpi", "MPI"), M("adr", "Our ADR"), M("compAdr", "Comp-set ADR (input)"), Pc("ari", "ARI"), Pc("rgi", "RGI")], comp),
            Series("seasonality", "line", "Revenue seasonality index", si.Select(r => (string)((object?[])r)[0]!), si.Select(r => (double?)((object?[])r)[1]), false, true),
        ], c.Stays.Any(s => s.Legacy));
    }

    private static string Fmt(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static AnResult OwnerDashboard(AnInput inp, AnParams p)
    {
        var c = new Ctx(inp); var end = p.Get("month") ?? c.End[..7]; var m = end; var ms = new List<string>();
        for (var i = 0; i < 12; i++) { ms.Insert(0, m); m = PrevMonth(m); }
        var firstSeen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in c.Stays) if (!firstSeen.TryGetValue(s.GuestKey, out var o) || Lt(s.From, o)) firstSeen[s.GuestKey] = s.From;
        var rows = ms.Select(mo =>
        {
            var from = mo + "-01"; var to = MonthEnd(mo); var bs = c.Bills.Where(b => b.Month == mo).ToList(); var avail = c.Capacity(from, to); var sold = c.SleptIn(from, to); var rev = bs.Sum(b => b.Rev); var cn = bs.Sum(b => b.Cn);
            var gs = bs.Where(b => b.St is not null).ToList(); var rep = gs.Where(b => Lt(firstSeen[b.St!.GuestKey], from)).ToList(); var ota = bs.Where(b => b.Channel != OtaFree).Sum(b => b.Rev); var billed = bs.Sum(b => b.B.TotalPaise);
            return (object)R(mo, Le(to, inp.LegacyUntil) ? "Legacy" : Lt(inp.LegacyUntil, from) ? "HCC live" : "Legacy + live", Pct1(sold, avail), Ratio(rev, cn), Ratio(rev, avail), rev, billed, bs.Sum(b => b.B.FnbPaise), (long)bs.Count, Pct1(rep.Count, gs.Count), Pct1(ota, rev),
                Pct1(bs.Sum(b => b.B.DiscountPaise), bs.Sum(b => b.Gross)), inp.Receipts.Where(r => r.Date[..7] == mo).Sum(r => r.AmountPaise), bs.Sum(b => Math.Max(0, b.B.TotalPaise - b.B.PaidPaise)));
        }).ToList();
        return Finish(
        [
            Sec("dashboard", "Owner Dashboard", [T("month", "Month"), T("src", "Source"), Pc("occ", "Occupancy %"), M("adr", "ADR"), M("revpar", "RevPAR"), M("roomRev", "Room revenue"), M("billed", "Total billed (incl. GST)"), M("fnb", "F&B billed"), I("bills", "Bills"),
                Pc("repeat", "Repeat guests %"), Pc("otaShare", "OTA share of room revenue"), Pc("discPct", "Discount % of gross"), M("collected", "Collections"), M("outstanding", "Outstanding on the month's bills")], rows),
            Series("occupancy", "line", "Occupancy % (12 months)", rows.Select(r => MonthLabel((string)((object?[])r)[0]!)), rows.Select(r => (double?)((object?[])r)[2]), false, true),
        ], c.Stays.Any(s => s.Legacy));
    }
}
