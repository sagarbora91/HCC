// F12 MEAS · meas-study-a · server twin of legacy-zero.ts: ZeroRateComplimentary and ReceivablesBadDebt. DEC-266 (zero-rate flag until the complimentary type is built),
// DEC-265 (every legacy receivable is a bad debt). Same method names, same numbers.
using Hcc.Application.Reporting.Rpt;
using static Hcc.Reporting.Analytics.LegacyCore;
using static Hcc.Reporting.Analytics.LegacyOccupancy;

namespace Hcc.Reporting.Analytics;

public static class LegacyZero
{
    private sealed record Ev(AnStay Stay, AnBill? Bill, string Cat, string Date, long Expected, long Short);

    public static ReportResult ZeroRateComplimentary(AnInput i, AnParams p)
    {
        var fy = FocusFy(i, p); var st = BillStay(i); var fys = FyRows(i);
        var billOf = new Dictionary<string, AnBill>(); foreach (var b in i.Bills) billOf[b.Stay] = b;
        var evs = new List<Ev>();
        foreach (var s in i.Stays)
        {
            if (!Slept(s)) continue;
            billOf.TryGetValue(s.Id, out var b); var zeroBill = b is not null && b.RoomChargePaise == 0; var zeroRate = s.RatePaise == 0;
            if (!(s.Complimentary || zeroBill || zeroRate)) continue;
            var exp = s.RatePaise * NightsOf(s);
            evs.Add(new Ev(s, b, s.Complimentary ? "Complimentary" : zeroBill ? "Zero room charge" : "Zero rate", b?.Date ?? s.To, exp, b is not null ? Math.Max(0, exp - b.RoomChargePaise) : 0));
        }
        string[] cats = ["Complimentary", "Zero room charge", "Zero rate"];
        var inFy = evs.Where(e => FyOf(e.Date) == fy).ToList();
        var changed = i.Bills.Where(b => st.TryGetValue(b.Stay, out var s) && b.ChargedNights > 0 && s.RatePaise > 0 && RDiv(b.RoomChargePaise, b.ChargedNights) != s.RatePaise).ToList();
        var rc = Table("rate-changes-by-fy", "Rate changes by FY (billed rate differs from the stay rate)", [T("fy", "FY"), I("stays", "Stays billed"), I("changed", "Stays with a rate change"), P("share", "Share %")],
            fys.Select(f => { var n = i.Bills.Count(b => FyOf(b.Date) == f.Fy); var c = changed.Count(b => FyOf(b.Date) == f.Fy); return Row(("fy", FyLabel(f.Fy)), ("stays", n), ("changed", c), ("share", Pct1(c, n))); }));
        var events = Table("zero-events", $"Zero-rate events ({FyLabel(fy)})",
            [T("stay", "Stay"), T("date", "Date"), T("room", "Room"), T("category", "Category"), I("nights", "Nights"), M("expected", "Expected room charge"), M("billed", "Room charge billed"), M("received", "Received")],
            inFy.Select(e => Row(("stay", e.Stay.Id), ("date", e.Date), ("room", e.Stay.Room), ("category", e.Cat), ("nights", NightsOf(e.Stay)), ("expected", e.Expected), ("billed", e.Bill?.RoomChargePaise ?? 0L), ("received", e.Bill?.PaidPaise ?? 0L))));
        var zb = Table("zero-bills", "Zero room-charge bills by FY", [T("fy", "FY"), I("bills", "Bills"), I("zero", "Zero room-charge bills"), P("share", "Share %")],
            fys.Select(f => { var bs = i.Bills.Where(b => FyOf(b.Date) == f.Fy).ToList(); var z = bs.Count(b => b.RoomChargePaise == 0); return Row(("fy", FyLabel(f.Fy)), ("bills", bs.Count), ("zero", z), ("share", Pct1(z, bs.Count))); }));
        var ccols = new List<ReportColumn> { T("fy", "FY") }; ccols.AddRange(cats.Select(c => I("c_" + c, c))); ccols.Add(I("total", "Total"));
        var byCat = Table("zero-by-fy-category", "Zero by FY and category", ccols, fys.Select(f =>
        {
            var es = evs.Where(e => FyOf(e.Date) == f.Fy).ToList(); var c = new Dictionary<string, object?> { ["fy"] = FyLabel(f.Fy) };
            foreach (var k in cats) c["c_" + k] = es.Count(e => e.Cat == k);
            c["total"] = es.Count; return RowD(c);
        }));
        var months = SortedUnique(evs.Select(e => MonthOf(e.Date)));
        var byMonth = Table("zero-by-month", "Zero by month", [T("month", "Month"), I("events", "Events"), M("expected", "Expected room charge")],
            months.Select(m => { var es = evs.Where(e => MonthOf(e.Date) == m).ToList(); return Row(("month", MonthLabel(m)), ("events", es.Count), ("expected", es.Sum(e => e.Expected))); }));
        var byRoom = Table("zero-by-room", $"Zero by room ({FyLabel(fy)})", [T("room", "Room"), T("type", "Type"), I("events", "Events")],
            SortedUnique(inFy.Select(e => e.Stay.Room)).Select(r => Row(("room", r), ("type", RoomType(i, r)), ("events", inFy.Count(e => e.Stay.Room == r)))));
        var big = changed.Where(b => FyOf(b.Date) == fy).Select(b =>
        {
            var s = st[b.Stay]; var nightly = RDiv(b.RoomChargePaise, b.ChargedNights); var cut = s.RatePaise - nightly;
            return (B: b, S: s, Cut: cut, Total: cut * b.ChargedNights, Nightly: nightly);
        }).Where(x => x.Nightly > 0 && (x.Cut * 10 > x.S.RatePaise * 3 || x.Cut > 100000)).OrderByDescending(x => x.Total).ThenBy(x => x.B.Id, StringComparer.Ordinal).Take(50).ToList();
        var down = Table("large-down-adjustments", "Large down-adjustments (cut over 30 % or over Rs 1,000 a night; top 50)",
            [T("bill", "Bill"), T("room", "Room"), M("from", "Stay rate"), M("to", "Billed rate"), M("cut", "Cut per night"), M("total", "Cut x nights")],
            big.Select(x => Row(("bill", x.B.Id), ("room", x.S.Room), ("from", x.S.RatePaise), ("to", x.Nightly), ("cut", x.Cut), ("total", x.Total))));
        string Why(Ev e) => string.IsNullOrEmpty(e.Stay.Purpose) ? "not recorded" : e.Stay.Purpose!;
        var reasons = Table("reasons-ranking", "Reasons ranking (purpose of the stay)", [T("reason", "Purpose"), I("events", "Events")],
            SortedUnique(inFy.Select(Why)).Select(r => (R: r, C: inFy.Count(e => Why(e) == r))).OrderByDescending(x => x.C).ThenBy(x => x.R, StringComparer.Ordinal).Select(x => Row(("reason", x.R), ("events", x.C))));
        var vs = Table("zero-vs-receipts", "Zero vs receipts", [T("item", "Item"), I("stays", "Stays")],
            [Row(("item", "Zero-event stays"), ("stays", inFy.Count)), Row(("item", "With a zero room-charge bill"), ("stays", inFy.Count(e => e.Cat == "Zero room charge" || e.Bill?.RoomChargePaise == 0))),
             Row(("item", "Zero room-charge bill and money received"), ("stays", inFy.Count(e => e.Bill?.RoomChargePaise == 0 && (e.Bill?.PaidPaise ?? 0) > 0)))]);
        var A = inFy.Sum(e => e.Expected); var B = inFy.Where(e => e.Bill is not null).Sum(e => e.Short); var C = inFy.Where(e => e.Bill?.RoomChargePaise == 0).Sum(e => e.Expected);
        var given = Table("revenue-given-up", "Revenue given up (estimates, room tariff before GST)", [T("est", "Estimate"), M("amount", "Amount")],
            [Row(("est", "A: expected charge of every zero-event stay"), ("amount", A)), Row(("est", "B: shortfall against the bill (stays with a bill)"), ("amount", B)), Row(("est", "C: expected charge of stays whose bill is zero"), ("amount", C))]);
        var sr = Series("events-by-fy", "bar", "Zero-rate events by FY", fys.Select(f => f.Fy).ToList(), fys.Select(f => (long)evs.Count(e => FyOf(e.Date) == f.Fy)).ToList(), false);
        var note = Note("method", "Evidence of a zero rate, not proof of a free stay. Legacy rows keep note-parsed events; live rows read the complimentary flag (DEC-266: the explicit complimentary stay type is pending). Revenue-given-up figures are estimates, not losses.");
        return Result("rpt.study.zero-rate-complimentary", "Zero-rate and complimentary stays", p with { Fy = fy }, [rc, events, zb, byCat, byMonth, byRoom, down, reasons, vs, given, sr, note], i);
    }

    private sealed record Due(AnBill B, long Amount, long Gst, string Party, bool Company, bool Legacy, long Age, string Fy);

    public static ReportResult ReceivablesBadDebt(AnInput i, AnParams p)
    {
        var asOf = p.AsOf ?? i.AsOf; var top = Math.Max(1, Math.Min(int.TryParse(p.TopN, out var tn) ? tn : 100, 100)); var st = BillStay(i);
        var dues = i.Bills.Where(b => string.CompareOrdinal(b.Date, asOf) <= 0 && b.TotalPaise > b.PaidPaise).Select(b =>
        {
            st.TryGetValue(b.Stay, out var s); var due = b.TotalPaise - b.PaidPaise;
            return new Due(b, due, RDiv(due * b.GstPaise, b.TotalPaise), !string.IsNullOrEmpty(s?.Company) ? s!.Company! : s?.GuestKey is { Length: > 0 } g ? g : "unknown", !string.IsNullOrEmpty(s?.Company), b.Legacy, AgeDays(asOf, b.Date), FyOf(b.Date));
        }).ToList();
        var legacy = dues.Where(d => d.Legacy).ToList(); var live = dues.Where(d => !d.Legacy).ToList();
        var fys = SortedUnique(dues.Select(d => d.Fy)); var totalDue = dues.Sum(d => d.Amount);
        var sumRows = fys.Select(f => { var ds = dues.Where(d => d.Fy == f).ToList(); return Row(("fy", FyLabel(f)), ("kind", ds.Any(d => d.Legacy) ? "Bad debt (legacy)" : "Live receivable"), ("bills", ds.Count), ("due", ds.Sum(d => d.Amount)), ("share", Pct1(ds.Sum(d => d.Amount), totalDue))); }).ToList();
        sumRows.Add(Row(ReportRowKind.Total, ("fy", "Total"), ("kind", ""), ("bills", dues.Count), ("due", totalDue), ("share", 100.0)));
        var summary = Table("summary", "Summary by FY (legacy = bad debt, DEC-265)", [T("fy", "FY"), T("kind", "Kind"), I("bills", "Bills"), M("due", "Due"), P("share", "Share of total due %")], sumRows);
        var badRows = SortedUnique(legacy.Select(d => d.Fy)).Select(f => { var ds = legacy.Where(d => d.Fy == f).ToList(); return Row(("fy", FyLabel(f)), ("bills", ds.Count), ("due", ds.Sum(d => d.Amount)), ("gst", ds.Sum(d => d.Gst)), ("net", ds.Sum(d => d.Amount - d.Gst))); }).ToList();
        badRows.Add(Row(ReportRowKind.Total, ("fy", "Total"), ("bills", legacy.Count), ("due", legacy.Sum(d => d.Amount)), ("gst", legacy.Sum(d => d.Gst)), ("net", legacy.Sum(d => d.Amount - d.Gst))));
        var bad = Table("bad-debt-schedule", "Bad Debt Schedule (legacy, write-off provision 100 %; GST is an estimate, CA to confirm)", [T("fy", "FY"), I("bills", "Bills"), M("due", "Due"), M("gst", "GST included (estimate)"), M("net", "Net of GST")], badRows);
        var acols = new List<ReportColumn> { T("fy", "FY") }; acols.AddRange(BucketNames.Select(b => M("b_" + b, b + " days"))); acols.Add(M("total", "Total"));
        var ageing = Table("ageing-by-fy", "Ageing by FY", acols, fys.Select(f =>
        {
            var ds = dues.Where(d => d.Fy == f).ToList(); var c = new Dictionary<string, object?> { ["fy"] = FyLabel(f) };
            foreach (var b in BucketNames) c["b_" + b] = ds.Where(d => Bucket(d.Age) == b).Sum(d => d.Amount);
            c["total"] = ds.Sum(d => d.Amount); return RowD(c);
        }));
        var sorted = dues.OrderByDescending(d => d.Amount).ThenBy(d => d.B.Id, StringComparer.Ordinal).ToList();
        var topDues = Table("top-dues", $"Top {top} dues", [T("bill", "Bill"), T("party", "Party", ReportMask.GuestName), T("date", "Bill date"), I("age", "Age (days)"), M("due", "Due"), T("kind", "Kind")],
            sorted.Take(top).Select(d => Row(("bill", d.B.Id), ("party", d.Party), ("date", d.B.Date), ("age", d.Age), ("due", d.Amount), ("kind", d.Legacy ? "Bad debt (legacy)" : "Live"))));
        var byParty = new Dictionary<string, (long N, long Due, string Old)>();
        foreach (var d in dues) { var r = byParty.TryGetValue(d.Party, out var x) ? x : (0L, 0L, d.B.Date); byParty[d.Party] = (r.Item1 + 1, r.Item2 + d.Amount, string.CompareOrdinal(d.B.Date, r.Item3) < 0 ? d.B.Date : r.Item3); }
        var debtors = Table("top-debtors", "Top debtors", [T("party", "Debtor", ReportMask.GuestName), I("bills", "Bills"), M("due", "Due"), T("oldest", "Oldest bill")],
            byParty.OrderByDescending(kv => kv.Value.Due).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(top).Select(kv => Row(("party", kv.Key), ("bills", kv.Value.N), ("due", kv.Value.Due), ("oldest", kv.Value.Old))));
        var cw = Table("company-vs-walkin", "Company vs walk-in", [T("kind", "Kind"), I("bills", "Bills"), M("due", "Due"), P("share", "Share %")],
            new (string, bool)[] { ("Company / B2B", true), ("Walk-in", false) }.Select(x => { var ds = dues.Where(d => d.Company == x.Item2).ToList(); return Row(("kind", x.Item1), ("bills", ds.Count), ("due", ds.Sum(d => d.Amount)), ("share", Pct1(ds.Sum(d => d.Amount), totalDue))); }));
        var follow = Table("follow-up-list", "Follow-up list (dues older than 2 years are verify-first)", [T("bill", "Bill"), T("party", "Party", ReportMask.GuestName), M("due", "Due"), I("age", "Age (days)"), T("action", "Action")],
            sorted.Take(top).Select(d => Row(("bill", d.B.Id), ("party", d.Party), ("due", d.Amount), ("age", d.Age), ("action", d.Age > 730 ? "Verify first" : "Call"))));
        var monthly = Table("monthly-unpaid", "Monthly unpaid", [T("month", "Month"), I("bills", "Bills"), M("due", "Due")],
            SortedUnique(dues.Select(d => MonthOf(d.B.Date))).Select(m => { var ds = dues.Where(d => MonthOf(d.B.Date) == m).ToList(); return Row(("month", MonthLabel(m)), ("bills", ds.Count), ("due", ds.Sum(d => d.Amount))); }));
        var cut = Table("cutover-comparison", $"Cutover comparison (legacy to {i.LegacyUntil} / HCC live)", [T("side", "Side"), I("bills", "Bills owing"), M("due", "Due"), T("treatment", "Treatment")],
            [Row(("side", "Legacy"), ("bills", legacy.Count), ("due", legacy.Sum(d => d.Amount)), ("treatment", "Bad debt, not carried as opening receivable (DEC-265)")),
             Row(("side", "HCC live"), ("bills", live.Count), ("due", live.Sum(d => d.Amount)), ("treatment", "Receivable; see rpt.acc.ageing"))]);
        var notes = Note("contacts", "Debtor Contacts (phone, GSTIN) are pending: the analytics input carries no contact data. Names stay under rule V.");
        var sr = Series("due-by-fy", "bar", "Due by FY", fys, fys.Select(f => dues.Where(d => d.Fy == f).Sum(d => d.Amount)).ToList(), true);
        return Result("rpt.study.receivables-bad-debt", "Receivables and bad debt (legacy + live ageing)", p with { AsOf = asOf, TopN = top.ToString() }, [summary, bad, ageing, topDues, debtors, cw, follow, monthly, cut, sr, notes], i, null, asOf);
    }
}
