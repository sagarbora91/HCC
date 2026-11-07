using Hcc.Contracts.Reporting;
using Hcc.Domain.Money;

namespace Hcc.Domain.Reporting;

/// <summary>Counts and totals of a closed day that the owner digest prints (F4-CONTRACT §3); the money comes from the daily pack and the revenue-by-category report.</summary>
public sealed record DigestFacts(DateOnly Date, DailyReport Pack, RevenueByCategoryReport Revenue, int PendingApprovals, int TomorrowArrivals, int ReorderAlerts = 0, int OverdueComplaints = 0,
    IReadOnlyList<RateReductionLine>? RateReductions = null, MeasDigestBlock? Measurement = null);

/// <summary>CTL-01: one rate change of the day that went more than the Owner's threshold below the stay's agreed rate (gst.stay_rate_change.over_threshold):
/// what (room, booking, agreed → new, tariff), how much (basis points below the agreed rate, threshold in force), who (changed by, approved by), why.</summary>
public sealed record RateReductionLine(string RoomNo, string Booking, long TariffPaise, long AgreedPaise, long NewPaise, int ReductionBps, int ThresholdBps,
    string ChangedBy, string? ApprovedBy, string Reason);

/// <summary>The two held drafts' text. The e-mail may carry money; the WhatsApp text is counts only (DEC-202 #7): no rupee amount, no name. Mirrors the prototype digest.ts.</summary>
public static class DigestBodies
{
    private static string M(long paise) => new Paise(paise).ToString();

    public static string Email(DigestFacts f)
    {
        ArgumentNullException.ThrowIfNull(f);
        var o = f.Pack.Occupancy;
        var lines = new List<string>
        {
            $"Owner digest · {f.Date:yyyy-MM-dd}", "",
            $"Occupancy {o.OccupancyPct} ({o.OccupiedRoomNights} of {o.SellableRoomNights} rooms) · ADR {(o.AdrPaise is { } a ? M(a) : "—")} · RevPAR {(o.RevParPaise is { } v ? M(v) : "—")}",
            "Revenue by category (net / incl. GST):",
        };
        lines.AddRange(f.Revenue.A.Where(x => x.Net != 0 || x.Gross != 0).Select(x => $"  {x.Label}: {M(x.Net)} / {M(x.Gross)}"));
        lines.Add($"  Total: {M(f.Revenue.ATotal.Net)} / {M(f.Revenue.ATotal.Gross)}");
        lines.Add("Collections by tender:");
        foreach (var t in f.Pack.Receipts.ByTender.Where(t => t.NetPaise != 0)) lines.Add($"  {t.Tender}: {M(t.NetPaise)}");
        lines.Add($"  Total: {M(f.Pack.Receipts.NetPaise)}");
        lines.Add($"Advances received {M(f.Revenue.BTotal.AdvancesReceived)} · adjusted {M(f.Revenue.BTotal.AdvancesAdjusted)}");
        lines.Add($"Expenses {M(f.Pack.Expenses.TotalPaise)}");
        lines.Add($"Cash in drawer at close: {(f.Pack.Drawer.CountedPaise is { } c ? M(c) : "not counted")}");
        lines.Add($"Pending approvals: {f.PendingApprovals}");
        var cuts = f.RateReductions ?? [];
        lines.Add($"Rate reductions beyond the agreed-rate threshold: {cuts.Count}");
        lines.AddRange(cuts.Select(RateLine));
        lines.Add($"Reorder alerts: {f.ReorderAlerts} · complaints / lost and found over SLA: {f.OverdueComplaints}");
        lines.Add($"Tomorrow: {f.TomorrowArrivals} arrival{(f.TomorrowArrivals == 1 ? "" : "s")}, 0 events");
        lines.AddRange(MeasurementDigest.EmailLines(f.Measurement)); // F12 MEAS: absent when there is no block
        return string.Join('\n', lines);
    }

    public static string WhatsApp(DigestFacts f)
    {
        ArgumentNullException.ThrowIfNull(f);
        var o = f.Pack.Occupancy;
        return string.Join('\n', (string[])[$"Digest {f.Date:yyyy-MM-dd}", $"Rooms sold {o.OccupiedRoomNights} of {o.SellableRoomNights}", $"Arrivals tomorrow {f.TomorrowArrivals}",
            $"Pending approvals {f.PendingApprovals}", $"Rate cuts beyond threshold {(f.RateReductions ?? []).Count}", $"Reorder alerts {f.ReorderAlerts}", $"Complaints over SLA {f.OverdueComplaints}",
            .. MeasurementDigest.WhatsAppLines(f.Measurement)]); // F12 MEAS: a count only (DEC-202 #7)
    }

    /// <summary>"Room 101 · HCC-S0001 · agreed ₹2,500 → ₹1,900 (24% below; threshold 15%) · tariff ₹2,000 · by Meera · approved by Sagar · reason".
    /// Mirrors the prototype digest.ts <c>dgsRateLine</c>.</summary>
    public static string RateLine(RateReductionLine r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return $"  Room {r.RoomNo} · {r.Booking} · agreed {M(r.AgreedPaise)} → {M(r.NewPaise)} ({Pct(r.ReductionBps)} below; threshold {Pct(r.ThresholdBps)}) · tariff {M(r.TariffPaise)}"
            + $" · by {r.ChangedBy} · {(r.ApprovedBy is { } a ? "approved by " + a : "applied directly")} · {r.Reason}";
    }

    private static string Pct(int bps) => (bps / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";
}
