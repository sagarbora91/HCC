using System.Globalization;
using Hcc.Domain.Invoices;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Domain.Counter;

/// <summary>A priced counter-bill line (LF-CONTRACT §6 CTRLine): qty × unit, GST snapshot and SAC.</summary>
public sealed record CounterLine(int Seq, string Label, string? ExtraId, string? MenuItemId, int Qty, Paise Unit, GstCategory Category, int Bps)
{
    public Paise Amount => new(checked(Unit.Value * Qty));

    public string Sac => SacCodes.For(Category);
}

public sealed record CounterTotals(Paise Net, Paise Tax, Paise Cgst, Paise Sgst, Paise Total);

/// <summary>
/// LF A7 rules (legacy "Direct Bill", F11) — the same as the prototype's domain/streams/counter.ts: line validation, totals (GST per line half
/// away from zero, CGST + SGST per line), a discount as a negative line at the dominant line's rate within the DEC-063 role limits (Cashier none,
/// Reception ≤ the hotel threshold, Manager ≤ PERM-FOL-003, Owner any; above = refused — a counter bill has no approval queue), the revenue
/// ledger by line class and the settlement modes.
/// </summary>
public static class CounterBillRules
{
    public const string Paid = "paid", Udhaar = "udhaar", Company = "company";

    public static readonly IReadOnlyList<string> Settlements = [Paid, Udhaar, Company];

    public const int MaxLines = 30, MaxQty = 99;

    public static string? LineRefusal(int seq, string label, int qty, Paise unit)
    {
        var at = "Line " + seq.ToString(CultureInfo.InvariantCulture) + ": ";
        if (string.IsNullOrWhiteSpace(label)) return at + "Describe the item.";
        if (label.Length > 120) return at + "Keep the description to 120 characters.";
        if (qty is < 1 or > MaxQty) return at + $"Quantity: a whole number from 1 to {MaxQty}.";
        if (unit.Value <= 0) return at + "Enter the unit price in whole paise (more than 0).";
        return null;
    }

    public static CounterLine? Dominant(IReadOnlyList<CounterLine> lines) => lines.OrderByDescending(l => l.Amount.Value).ThenBy(l => l.Seq).FirstOrDefault();

    public static CounterTotals Totals(IReadOnlyList<CounterLine> lines, Paise discount)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var all = lines.Select(l => (l.Amount, l.Bps)).ToList();
        var d = Dominant(lines);
        if (discount.Value > 0 && d is not null) all.Add((-discount, d.Bps));
        var split = GstMath.SplitLines(all);
        var net = all.Aggregate(Paise.Zero, (a, l) => a + l.Amount);
        return new CounterTotals(net, split.Tax, split.Cgst, split.Sgst, net + split.Tax);
    }

    /// <summary>Discount refusal by role (DEC-063 G1). <paramref name="limit"/> null = no limit (Owner).</summary>
    public static string? DiscountRefusal(bool mayDiscount, Paise? limit, Paise discount, Paise net)
    {
        if (discount.Value <= 0) return null;
        if (!mayDiscount) return "A discount on a counter bill is given by Reception, a Manager or the Owner — not at the cash counter (DEC-063).";
        if (discount > net) return "The discount cannot exceed the bill.";
        if (limit is { } l && discount > l)
            return $"Your discount limit is ₹{l.Value / 100m:0.00}. A counter bill has no approval queue: ask a Manager or the Owner to issue it.";
        return null;
    }

    /// <summary>Revenue ledger of a line class: food 4200, room 4000, everything else 4100 (laundry, banquet, other).</summary>
    public static string RevenueLedger(GstCategory c) => c switch { GstCategory.Food => "4200", GstCategory.Room => "4000", _ => "4100" };

    /// <summary>Udhaar kind of the bill by its dominant line class.</summary>
    public static string UdhaarKind(IReadOnlyList<CounterLine> lines) => Dominant(lines)?.Category == GstCategory.Food ? "restaurant" : "other";

    /// <summary>The Sales voucher of a counter bill (+ = debit), by ledger: Dr the settlement side (tender / 1120/&lt;customer&gt; / 1110/&lt;company&gt;) total,
    /// Cr revenue by class, Dr 4900 discount, Cr 2110 / 2120. Balanced by construction. The credit note posts the same with signs swapped.</summary>
    public static IReadOnlyList<(string Ledger, Paise Amount)> Entries(string settlementLedger, IReadOnlyList<CounterLine> lines, Paise discount, CounterTotals t)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(t);
        var e = new List<(string, Paise)> { (settlementLedger, t.Total) };
        e.AddRange(lines.GroupBy(l => RevenueLedger(l.Category)).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, -g.Aggregate(Paise.Zero, (a, l) => a + l.Amount))));
        if (discount.Value > 0) e.Add(("4900", discount));
        e.Add(("2110", -t.Cgst));
        e.Add(("2120", -t.Sgst));
        return e.Where(x => x.Item2.Value != 0).ToList();
    }
}
