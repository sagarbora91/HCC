using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Domain.Hall;

/// <summary>A hall quotation / event line (F6-CONTRACT §2 QuoteLine). Kind HALL | MENU | EXTRA | ROOM | OTHER; GstClass HALL_RENT | BANQUET_FOOD | SERVICE | ROOM.</summary>
public sealed record HallLine(string Id, string Kind, string Desc, int Qty, string Unit, Paise Rate, Paise ListRate, Paise Discount, string GstClass, int? TaxBps = null)
{
    public Paise Taxable => new(checked(Qty * Rate.Value - Discount.Value));

    public Paise List => new(checked(Qty * ListRate.Value));
}

public sealed record HallPricedLine(HallLine Line, int TaxBps, Paise Taxable, Paise Cgst, Paise Sgst);

public sealed record HallTotals(Paise Taxable, Paise Cgst, Paise Sgst, Paise List)
{
    public Paise Tax => Cgst + Sgst;

    public Paise Total => Taxable + Cgst + Sgst;

    public Paise Discount => List - Taxable;
}

/// <summary>An advance-plan row (contract §2 AdvanceRow). Due: "ON_ACCEPT", "EVENT_DAY" or yyyy-MM-dd.</summary>
public sealed record HallAdvanceRow(string Id, string Label, int? PctBps, Paise Amount, string Due, Paise Paid);

public sealed record HallCancelSlab(int MinDays, int KeptBps);

public sealed record HallCancelQuote(int DaysBefore, int KeptBps, Paise Total, Paise Paid, Paise Kept, Paise Refund, int TaxBps);

public sealed record HallInclusiveSplit(Paise Taxable, Paise Tax, Paise Cgst, Paise Sgst);

/// <summary>
/// F6 hall money (contract §5) — ONE truth table with the prototype <c>src/domain/streams/hall-billing.ts</c>:
/// line taxable = qty × rate − discount; GST per line from the dated master by class at the invoice date (hall rent / banquet food → banquet rate,
/// ROOM → room-night rule at the line rate, else other services), CGST + SGST per line half away from zero; discount % = Σ(list − charged) / Σ list;
/// advance plan 25 % / 50 % at 30 days / balance (last row takes the rounding); event bill = MENU per-plate lines at max(guaranteed, actual);
/// cancellation kept = min(paid, total × slab %), GST-inclusive at the hall-rent rate → 4300; the rest refunded (Manager ≤ ₹50,000, Owner above).
/// </summary>
public static class HallBilling
{
    public const string ClassHallRent = "HALL_RENT", ClassBanquetFood = "BANQUET_FOOD", ClassService = "SERVICE", ClassRoom = "ROOM";

    public static readonly IReadOnlyList<HallCancelSlab> DefaultSlabs = [new(61, 0), new(30, 2500), new(7, 5000), new(0, 10000)];

    public const long RefundManagerLimitPaise = 5_000_000;

    public const int SecondDueDays = 30, FirstPctBps = 2500, SecondPctBps = 5000;

    /// <summary>a × bps / 10000, half away from zero (the GST rounding, reused for percentages).</summary>
    public static Paise Pct(Paise amount, int bps) => GstMath.LineTax(amount, bps);

    public static int TaxBps(GstMaster gst, string gstClass, DateOnly date, Paise rate)
    {
        ArgumentNullException.ThrowIfNull(gst);
        return gstClass switch
        {
            ClassRoom => gst.RoomNight(date, rate).Bps,
            ClassHallRent or ClassBanquetFood => gst.Service(date, GstCategory.Banquet).Bps,
            _ => gst.Service(date, GstCategory.Other).Bps,
        };
    }

    public static (IReadOnlyList<HallPricedLine> Lines, HallTotals Totals) Price(GstMaster gst, IReadOnlyList<HallLine> lines, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var priced = lines.Select(l =>
        {
            var bps = l.TaxBps ?? TaxBps(gst, l.GstClass, date, l.Rate);
            var s = GstMath.SplitLine(l.Taxable, bps);
            return new HallPricedLine(l, bps, l.Taxable, s.Cgst, s.Sgst);
        }).ToList();
        var t = new HallTotals(Sum(priced.Select(p => p.Taxable)), Sum(priced.Select(p => p.Cgst)), Sum(priced.Select(p => p.Sgst)), Sum(lines.Select(l => l.List)));
        return (priced, t);
    }

    private static Paise Sum(IEnumerable<Paise> xs) => xs.Aggregate(Paise.Zero, (a, x) => a + x);

    /// <summary>Discount limit in bps by role: Owner null (any), Manager 1000, everyone else 0 (A26).</summary>
    public static int? DiscountLimitBps(string role) => role switch { "Owner" => null, "Manager" => 1000, _ => 0 };

    /// <summary>Exact: (Σ list − Σ charged) × 10000 ≤ limit × Σ list.</summary>
    public static bool DiscountWithin(IReadOnlyList<HallLine> lines, int? limitBps)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (limitBps is null) return true;
        long list = lines.Sum(l => l.List.Value), charged = lines.Sum(l => l.Taxable.Value);
        return checked((list - charged) * 10000) <= checked(limitBps.Value * list);
    }

    public static int DiscountBps(IReadOnlyList<HallLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        long list = lines.Sum(l => l.List.Value), charged = lines.Sum(l => l.Taxable.Value);
        return list > 0 ? (int)((list - charged) * 10000 / list) : 0;
    }

    /// <summary>Default plan: 25 % on acceptance, 50 % 30 days before the event (on the acceptance date when nearer), balance on the event day.</summary>
    public static IReadOnlyList<HallAdvanceRow> AdvancePlan(Paise total, DateOnly eventDate, DateOnly acceptDate)
    {
        var second = eventDate.AddDays(-SecondDueDays);
        var due2 = second < acceptDate ? acceptDate : second;
        return PlanAmounts(total, [
            new HallAdvanceRow("ADV-1", "Advance on booking", FirstPctBps, Paise.Zero, "ON_ACCEPT", Paise.Zero),
            new HallAdvanceRow("ADV-2", "Second advance", SecondPctBps, Paise.Zero, due2.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), Paise.Zero),
            new HallAdvanceRow("ADV-3", "Balance", null, Paise.Zero, "EVENT_DAY", Paise.Zero)]);
    }

    /// <summary>% rows half away from zero, fixed rows as given, the LAST row = total − the others.</summary>
    public static IReadOnlyList<HallAdvanceRow> PlanAmounts(Paise total, IReadOnlyList<HallAdvanceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var used = Paise.Zero;
        var out_ = new List<HallAdvanceRow>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (i == rows.Count - 1) { out_.Add(r with { Amount = total - used }); continue; }
            var a = r.PctBps is { } p ? Pct(total, p) : r.Amount;
            used += a;
            out_.Add(r with { Amount = a });
        }

        return out_;
    }

    /// <summary>A receipt filled into the plan in row order (an overpayment stays on the last row).</summary>
    public static IReadOnlyList<HallAdvanceRow> Allocate(IReadOnlyList<HallAdvanceRow> rows, Paise amount)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var left = amount.Value;
        var out_ = new List<HallAdvanceRow>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (left <= 0) { out_.Add(r); continue; }
            var room = i == rows.Count - 1 ? left : Math.Max(0, r.Amount.Value - r.Paid.Value);
            var take = Math.Min(room, left);
            left -= take;
            out_.Add(take == 0 ? r : r with { Paid = r.Paid + new Paise(take) });
        }

        return out_;
    }

    public static int BilledPax(int guaranteed, int actual) => Math.Max(guaranteed, actual);

    /// <summary>Event-day lines: MENU per-plate lines at the billed plates (a line discount scales with the plates), then the extras.</summary>
    public static IReadOnlyList<HallLine> BillLines(IReadOnlyList<HallLine> accepted, int guaranteed, int actual, IReadOnlyList<HallLine> extras)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        ArgumentNullException.ThrowIfNull(extras);
        var pax = BilledPax(guaranteed, actual);
        return
        [
            .. accepted.Select(l => l.Kind == "MENU" && l.Unit == "plate" && pax > 0
                ? l with { Qty = pax, Discount = l.Discount.Value != 0 && l.Qty != 0 ? new Paise((long)Math.Round(l.Discount.Value * (decimal)pax / l.Qty, MidpointRounding.AwayFromZero)) : l.Discount }
                : l),
            .. extras,
        ];
    }

    public static HallCancelSlab Slab(IReadOnlyList<HallCancelSlab> slabs, int daysBefore)
    {
        ArgumentNullException.ThrowIfNull(slabs);
        var sorted = slabs.OrderByDescending(x => x.MinDays).ToList();
        return sorted.FirstOrDefault(x => daysBefore >= x.MinDays) ?? sorted[^1];
    }

    public static HallCancelQuote Cancel(GstMaster gst, IReadOnlyList<HallLine> accepted, DateOnly eventDate, DateOnly date, Paise paid, IReadOnlyList<HallCancelSlab>? slabs = null)
    {
        var days = eventDate.DayNumber - date.DayNumber;
        var slab = Slab(slabs ?? DefaultSlabs, days);
        var total = Price(gst, accepted, date).Totals.Total;
        var keptCap = Pct(total, slab.KeptBps);
        var kept = paid < keptCap ? paid : keptCap;
        return new HallCancelQuote(days, slab.KeptBps, total, paid, kept, paid - kept, TaxBps(gst, ClassHallRent, date, Paise.Zero));
    }

    /// <summary>A GST-inclusive amount split: taxable = gross × 10000 / (10000 + bps) half away from zero; tax = the rest, split per GstMath.Split.</summary>
    public static HallInclusiveSplit Inclusive(Paise gross, int bps)
    {
        long p = checked(gross.Value * 10000), d = 10000 + bps, q = p / d, r = p - q * d;
        var taxable = q + (r * 2 >= d ? 1 : 0);
        var tax = new Paise(gross.Value - taxable);
        var s = GstMath.Split(tax);
        return new HallInclusiveSplit(new Paise(taxable), tax, s.Cgst, s.Sgst);
    }

    public static bool RefundApprover(string role, Paise refund) => role == "Owner" || (role == "Manager" && refund.Value <= RefundManagerLimitPaise);

    /// <summary>Revenue ledger of a line (contract §5): MENU → food ledger (4200, or 4400 with the Q6 switch), ROOM → 4000, else 4400.</summary>
    public static string RevenueLedger(string kind, string foodLedger = "4200") => kind switch { "MENU" => foodLedger, "ROOM" => "4000", _ => "4400" };

    /// <summary>The invoice Sales voucher (+ = debit): Dr 1100 total · Cr revenue by ledger · Cr 2110 · Cr 2120. Balanced by construction.</summary>
    public static IReadOnlyList<(string Ledger, Paise Amount)> InvoiceEntries(IReadOnlyList<HallPricedLine> lines, HallTotals t, string foodLedger = "4200")
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(t);
        var e = new List<(string, Paise)> { ("1100", t.Total) };
        e.AddRange(lines.GroupBy(l => RevenueLedger(l.Line.Kind, foodLedger)).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => (g.Key, -Sum(g.Select(x => x.Taxable)))));
        e.Add(("2110", -t.Cgst));
        e.Add(("2120", -t.Sgst));
        return e.Where(x => x.Item2.Value != 0).ToList();
    }

    /// <summary>The cancellation journal: Dr 2000 kept · Cr 4300 taxable · Cr 2110 · Cr 2120.</summary>
    public static IReadOnlyList<(string Ledger, Paise Amount)> CancelEntries(Paise kept, int bps)
    {
        var s = Inclusive(kept, bps);
        return new List<(string, Paise)> { ("2000", kept), ("4300", -s.Taxable), ("2110", -s.Cgst), ("2120", -s.Sgst) }.Where(x => x.Item2.Value != 0).ToList();
    }
}
