using System.Globalization;
using Hcc.Domain.Money;

namespace Hcc.Domain.Pos;

/// <summary>One active (non-void) check line for totals.</summary>
public sealed record PosLineAmount(Paise Price, int Qty)
{
    public Paise Value => new(checked(Price.Value * Qty));
}

/// <summary>Bill figures: net = Σ active lines, tax = round-half-away(net × bps / 10000), total = net + tax (prototype <c>posOrderTotals</c>).</summary>
public sealed record PosTotals(Paise Net, int TaxBps, Paise Tax, Paise Total, int Items);

/// <summary>Restaurant POS rules ported from <c>03 Prototype/src/domain/streams/pos.ts</c> and the POS handlers (module 23 spec §4).</summary>
public static class PosRules
{
    /// <summary>Void value above which a different user with approval rights must decide (₹500). // CONFIG: Master configuration (W2-1) later.</summary>
    public const long VoidLimitPaise = 50_000;

    public static readonly TimeSpan VoidRequestTtl = TimeSpan.FromHours(24);

    public const string Cash = "Cash";
    public const string Upi = "UPI";
    public const string Card = "Card";

    public static readonly IReadOnlyList<string> Tenders = [Cash, Upi, Card];

    public const string Preparing = "Preparing";
    public const string Ready = "Ready";
    public const string Served = "Served";

    private static readonly string[] KotFlow = [Preparing, Ready, Served];

    /// <summary>The next kitchen status, or null when served (final).</summary>
    public static string? NextKotStatus(string status)
    {
        var i = Array.IndexOf(KotFlow, status);
        return i >= 0 && i + 1 < KotFlow.Length ? KotFlow[i + 1] : null;
    }

    public static PosTotals Totals(IEnumerable<PosLineAmount> activeLines, int taxBps)
    {
        ArgumentNullException.ThrowIfNull(activeLines);
        var lines = activeLines.ToList();
        var net = lines.Aggregate(Paise.Zero, (a, l) => a + l.Value);
        var tax = MoneyMath.TaxOf(net, taxBps);
        return new PosTotals(net, taxBps, tax, net + tax, lines.Sum(l => l.Qty));
    }

    /// <summary><c>POS/YYMM/NNNN</c> from the business date and the property's running sequence.</summary>
    public static string BillNo(DateOnly date, int sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"POS/{date.Year % 100:00}{date.Month:00}/{sequence:0000}");

    /// <summary>Kitchen ticket label, e.g. <c>RE-KOT 3</c> (first two letters of the outlet name).</summary>
    public static string KotLabel(string outletName, int kotNo)
    {
        var name = (outletName ?? string.Empty).Trim().ToUpperInvariant();
        return string.Create(CultureInfo.InvariantCulture, $"{(name.Length > 2 ? name[..2] : name)}-KOT {kotNo}");
    }

    /// <summary>True when a void of this value needs a second person's approval.</summary>
    public static bool NeedsApproval(long valuePaise) => valuePaise > VoidLimitPaise;
}
