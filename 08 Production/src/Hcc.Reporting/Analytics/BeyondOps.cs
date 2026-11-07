// F12 MEAS · meas-study-b · server twin of beyond-ops.ts: HousekeepingOperations, FnbHallsAncillary, ComplianceRisk.
using System.Text.RegularExpressions;
using static Hcc.Reporting.Analytics.Bl;

namespace Hcc.Reporting.Analytics;

public static class BeyondOps
{
    public static AnResult HousekeepingOperations(AnInput inp, AnParams p)
    {
        var c = new Ctx(inp); var fy = c.FyParam(p); var from = Lt(FyStart(fy), c.DataStart) ? c.DataStart : FyStart(fy); var to = Lt(c.End, FyEnd(fy)) ? c.End : FyEnd(fy);
        var gaps = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var g in c.Stays.GroupBy(s => s.Room))
        {
            var xs = g.ToList(); xs.Sort((a, b) => string.CompareOrdinal(a.From, b.From));   // stable, as the prototype's sort
            for (var i = 1; i < xs.Count; i++)
            {
                var f = FyOf(xs[i].From); var gap = DaysBetween(xs[i - 1].To, xs[i].From); if (gap < 0) continue;
                if (!gaps.TryGetValue(f, out var l)) gaps[f] = l = []; l.Add(gap);
            }
        }
        var turn = c.Fys.Select(f =>
        {
            var g = gaps.GetValueOrDefault(f) ?? []; var same = g.Count(x => x == 0);
            return (object)R(f, (long)g.Count, (long)same, Pct1(same, g.Count), (long)g.Count(x => x == 1), Avg1(g.Sum(), g.Count));
        }).ToList();
        var idle = new List<object>();
        foreach (var r in inp.Rooms)
        {
            var sold = c.Slept.Where(s => s.Room == r.Id && Le(from, s.Date) && Le(s.Date, to)).Select(s => s.Date).ToHashSet(); long run = 0, best = 0, unsold = 0, inSvc = 0;
            for (var d = from; Le(d, to); d = AddDays(d, 1))
            {
                if (Lt(d, r.InServiceFrom) || (r.InServiceTo is not null && Lt(r.InServiceTo, d))) continue;
                inSvc++; if (sold.Contains(d)) run = 0; else { run++; unsold++; if (run > best) best = run; }
            }
            if (inSvc > 0) idle.Add(R(r.Id, c.Rtype.GetValueOrDefault(r.Id) ?? "", fy, inSvc, inSvc - unsold, unsold, best));
        }
        var stdIn = p.Get("stdIn") ?? "12:00"; var stdOut = p.Get("stdOut") ?? "11:00"; var ss = c.Stays.Where(s => FyOf(s.From) == fy).ToList();
        var withIn = ss.Where(s => !string.IsNullOrEmpty(s.InTime)).ToList(); var withOut = ss.Where(s => !string.IsNullOrEmpty(s.OutTime)).ToList();
        var early = withIn.Count(s => string.CompareOrdinal(s.InTime, stdIn) < 0); var late = withOut.Count(s => string.CompareOrdinal(s.OutTime, stdOut) > 0);
        var inout = new List<object> { R(fy, (long)ss.Count, (long)withIn.Count, (long)early, Pct1(early, withIn.Count), (long)withOut.Count, (long)late, Pct1(late, withOut.Count)) };
        var arr = new Dictionary<string, long>(StringComparer.Ordinal); var dep = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var s in ss) { arr[s.From] = arr.GetValueOrDefault(s.From) + 1; dep[s.To] = dep.GetValueOrDefault(s.To) + 1; }
        var load = Dow.Select((n, i) =>
        {
            long days = 0, a = 0, d2 = 0, ma = 0, md = 0;
            for (var d = from; Le(d, to); d = AddDays(d, 1)) if (DowOf(d) == i) { days++; var x = arr.GetValueOrDefault(d); var y = dep.GetValueOrDefault(d); a += x; d2 += y; if (x > ma) ma = x; if (y > md) md = y; }
            return (object)R(n, days, a, Avg1(a, days), ma, d2, Avg1(d2, days), md);
        }).ToList();
        return Finish(
        [
            Sec("turnaround", "Turnaround", [T("fy", "FY"), I("turnovers", "Room turnovers"), I("sameDay", "Same-day turnovers"), Pc("sameDayPct", "Same-day %"), I("nextDay", "Next-day turnovers"), I("avgGap", "Average gap (days)")], turn),
            Sec("idle", "Idle Streaks", [T("room", "Room"), T("type", "Type"), T("fy", "FY"), I("inService", "Days in service"), I("soldDays", "Nights sold"), I("unsold", "Unsold nights"), I("longest", "Longest unsold run (days)")], idle),
            Sec("inout", "Check-in-out vs Standard", [T("fy", "FY"), I("stays", "Stays"), I("withIn", "With check-in time"), I("early", "Before standard check-in"), Pc("earlyPct", "Early %"), I("withOut", "With check-out time"), I("late", "After standard check-out"), Pc("latePct", "Late %")], inout),
            Sec("load", "Daily Load", [T("day", "Night of week"), I("days", "Days"), I("arrivals", "Arrivals"), I("avgArr", "Avg arrivals / day"), I("maxArr", "Busiest arrival day"), I("departures", "Departures"), I("avgDep", "Avg departures / day"), I("maxDep", "Busiest departure day")], load),
            Series("avg-arrivals", "bar", "Average arrivals per day by night of week", Dow, load.Select(r => (double?)((object?[])r)[3])),
        ], c.Stays.Any(s => s.Legacy));
    }

    public static AnResult FnbHallsAncillary(AnInput inp, AnParams p)
    {
        var c = new Ctx(inp); var fy = c.FyParam(p); var ex = p.Extras;
        var mo = c.FyMonths(fy).Select(m => { var bs = c.Bills.Where(b => b.Month == m).ToList(); var fb = bs.Sum(b => b.B.FnbPaise); var rev = bs.Sum(b => b.Rev); return (object)R(m, (long)bs.Count, fb, Ratio(fb, bs.Count), Pct1(fb, rev)); }).ToList();
        var byFy = c.Fys.Select(f => { var bs = c.Bills.Where(b => b.Fy == f).ToList(); var fb = bs.Sum(b => b.B.FnbPaise); var rev = bs.Sum(b => b.Rev); return (object)R(f, (long)bs.Count, (long)bs.Count(b => b.B.FnbPaise > 0), fb, Ratio(fb, bs.Count), Pct1(fb, rev)); }).ToList();
        var cov = c.Fys.Select(f => { var cp = c.Stays.Where(s => FyOf(s.From) == f && (s.MealPlan ?? "").ToUpperInvariant() == "CP").ToList(); return (object)R(f, (long)cp.Count, cp.Sum(s => (long)s.Nights * (s.People ?? 1)), cp.Sum(s => (long)s.Nights)); }).ToList();
        var hall = c.Fys.Select(f => { var h = ex.HallEvents.Where(x => FyOf(x.Date) == f).ToList(); return (object)R(f, (long)h.Count, h.Sum(x => (long)x.Days), h.Sum(x => x.RevenuePaise), Ratio(h.Sum(x => x.RevenuePaise), h.Count)); }).ToList();
        var anc = c.Fys.Select(f => { var bs = c.Bills.Where(b => b.Fy == f).ToList(); var o = bs.Sum(b => b.B.OtherPaise); return (object)R(f, (long)bs.Count, o, Ratio(o, bs.Count), Pct1(o, bs.Sum(b => b.Rev))); }).ToList();
        return Finish(
        [
            Sec("fnb-month", "F&B on Bills by Month", [T("month", "Month"), I("bills", "Bills"), M("fnb", "F&B billed"), M("perBill", "F&B per bill"), Pc("pctRoom", "F&B % of room revenue")], mo),
            Sec("fnb-fy", "F&B by FY", [T("fy", "FY"), I("bills", "Bills"), I("withFnb", "Bills with F&B"), M("fnb", "F&B billed"), M("perBill", "F&B per bill"), Pc("pctRoom", "F&B % of room revenue")], byFy),
            Sec("covers", "Breakfast Covers", [T("fy", "FY"), I("stays", "CP stays"), I("covers", "Covers (people x nights)"), I("nights", "CP room-nights")], cov),
            Sec("halls", "Hall Utilisation", [T("fy", "FY"), I("events", "Events"), I("days", "Event days"), M("revenue", "Hall revenue"), M("perEvent", "Revenue per event")], hall),
            Sec("ancillary", "Ancillary per Stay", [T("fy", "FY"), I("bills", "Bills"), M("other", "Other charges (extra bed, laundry, other)"), M("perBill", "Other per bill"), Pc("pctRoom", "Other % of room revenue")], anc),
            Series("fnb-per-bill", "bar", "F&B per bill by FY", byFy.Select(r => (string)((object?[])r)[0]!), byFy.Select(r => (double?)(long?)((object?[])r)[4]), true),
        ], c.Stays.Any(s => s.Legacy));
    }

    private static readonly (string Risk, int L, int I, string Control, string Status)[] Risks =
    [
        ("Revenue leakage through free-text rate edits and zero edits", 4, 3, "Rate card, reason codes, approver for cuts > 10% and for complimentary stays, audit log", "Open"),
        ("Walk-in credit becoming bad debt", 4, 4, "No check-out with balance; credit only against company master with limit; monthly ageing", "Open"),
        ("Cash handling and unreferenced receipts", 3, 3, "Receipt always linked to a bill; daily cash count vs HCC; UPI / bank import", "Open"),
        ("GST slab or 0% bills mis-stated in returns", 3, 4, "Tax code per line; monthly GST reconciliation; CA review of 0% bills", "Open"),
        ("Guest ID not captured", 3, 4, "Mandatory ID at check-in; police register export", "Open"),
        ("Rooms out of order unrecorded", 4, 2, "Daily room-status log; maintenance tickets", "Open"),
        ("OTA dependence and commission cost", 3, 3, "Channel master with commission; direct-booking offer; enquiry log", "Open"),
        ("Single-point data loss", 3, 5, "HCC server with backups; legacy files archived read-only", "Mitigated by HCC"),
        ("Licence lapse (fire NOC, FSSAI, lodging licence)", 2, 5, "Licence calendar with 60 / 30-day alerts", "Open"),
        ("Foreign guest without Form C", 1, 4, "Nationality list + Form C task", "Open"),
        ("Key / access control (physical keys)", 3, 3, "KEYS module (key-card) with issue / return log", "In progress"),
        ("Staff advances unreconciled (paid-outs)", 3, 2, "Payroll module; advances ledger per employee", "Open"),
    ];

    private static readonly Regex India = new("^(india|indian|ind)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static AnResult ComplianceRisk(AnInput inp, AnParams p)
    {
        var c = new Ctx(inp); var fy = c.FyParam(p); var ex = p.Extras; var ss = c.Stays.Where(s => FyOf(s.From) == fy).ToList();
        var cap = c.Fys.Select(f =>
        {
            var x = c.Stays.Where(s => FyOf(s.From) == f).ToList(); var id = x.Count(s => s.IdCaptured); var ph = x.Count(s => s.PhoneCaptured); var pu = x.Count(s => !string.IsNullOrEmpty(s.Purpose));
            return (object)R(f, (long)x.Count, (long)id, Pct1(id, x.Count), (long)ph, Pct1(ph, x.Count), (long)pu, Pct1(pu, x.Count));
        }).ToList();
        var foreign = ss.Count(s => !string.IsNullOrEmpty(s.Nationality) && !India.IsMatch(s.Nationality));
        var audit = ex.Audit.Take((int)p.Num("topN", 20)).Select(a => (object)R(a.Action, (long)a.Count)).ToList();
        var disc = c.Bills.Count(b => b.Fy == fy && b.B.DiscountPaise > 0); var zero = c.Bills.Count(b => b.Fy == fy && b.B.TotalPaise == 0);
        audit.Add(R("Discounted bills (selected FY)", (long)disc)); audit.Add(R("Zero-value bills (selected FY)", (long)zero)); audit.Add(R("Foreign-national stays (selected FY, Form C)", (long)foreign));
        var risk = Risks.Select(r => (object)R(r.Risk, (long)r.L, (long)r.I, (long)(r.L * r.I), r.Control, r.Status)).ToList();
        var lic = ex.Licences.Select(l => (object)R(l.Name, l.ValidTo, l.ValidTo is null ? null : (long?)Math.Round((P(l.ValidTo) - P(c.End)).TotalDays))).ToList();
        return Finish(
        [
            Sec("capture", "Capture Rates", [T("fy", "FY"), I("stays", "Stays"), I("id", "With ID record"), Pc("idPct", "ID %"), I("phone", "With phone"), Pc("phonePct", "Phone %"), I("purpose", "With purpose of visit"), Pc("purposePct", "Purpose %")], cap),
            Sec("audit", "Audit Trail", [T("action", "Action recorded"), I("count", "Count")], audit),
            Sec("risk", "Risk Register", [T("risk", "Risk"), I("likelihood", "Likelihood (1-5)"), I("impact", "Impact (1-5)"), I("rating", "Rating"), T("control", "Control in HCC"), T("status", "Status")], risk),
            Sec("licences", "Licence Calendar", [T("name", "Licence"), Dt("validTo", "Valid to"), I("days", "Days left")], lic),
            Series("id-capture", "line", "ID capture % by FY", cap.Select(r => (string)((object?[])r)[0]!), cap.Select(r => (double?)((object?[])r)[3]), false, true),
        ], c.Stays.Any(s => s.Legacy));
    }
}
