using System.Text.RegularExpressions;
using Hcc.Domain.Billing;
using Hcc.Domain.Money;

namespace Hcc.Domain.Reporting;

// F4-T2 · rpt.money.revenue-by-category-tender (F4-CONTRACT §2): a view over the daily-pack source records, grouped by the room type of the stay
// on the night. Mirrors 03 Prototype/src/domain/streams/rpt-revcat.ts (rctReport); no new money logic.

/// <summary>The room a stay occupied: one row per stay segment (gst.stay_segment) with its room type code; <see cref="To"/> null = still there.</summary>
public sealed record CategorySegment(long StayId, long FolioId, int RoomId, string RoomType, DateOnly From, DateOnly? To)
{
    public bool Covers(DateOnly d) => From <= d && (To is null || d < To);
}

/// <summary>Rooms with their type code (for the sellable room-nights per category).</summary>
public sealed record CategoryRoom(int RoomId, string RoomType);

public sealed record RevenueCategoryData(IReadOnlyList<CategoryRoom> Rooms, IReadOnlyList<CategorySegment> Segments);

public sealed record RevenueRowA(string Key, string Label, int RoomNightsSellable, int NightsSold, decimal OccPct, long RoomTariff, long ExtraPerson, long MealPlans,
    long OtherCharges, long Discounts, long Corrections, long Net, long Cgst, long Sgst, long Gross, long Adr);

public sealed record RevenueRowB(string Key, string Label, long Cash, long Upi, long Card, long Bank, long CompanyCredit, long Udhaar, long Ota,
    IReadOnlyDictionary<string, long> OtherTenders, long AdvancesReceived, long AdvancesAdjusted, long AdvancesRefunded, long Refunds, long NetReceived);

public sealed record RevenueBridge(long Gross, long NetReceived, long AdvancesAdjusted, long AdvanceMovement, long Difference);

public sealed record RevenueByCategoryReport(DateOnly From, DateOnly To, string Basis, IReadOnlyList<RevenueRowA> A, RevenueRowA ATotal, IReadOnlyList<RevenueRowB> B,
    RevenueRowB BTotal, RevenueBridge C);

public static class RevenueByCategoryCalculator
{
    public const string BusinessDate = "business-date", Stay = "stay", Unassigned = "unassigned";
    private static readonly Regex ExtraRx = new(@"extra\s+(person|bed|guest|adult)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MealRx = new(@"meal|breakfast|\b(CP|MAP|AP)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private sealed class A { public int Sellable, Sold; public long Room, Extra, Meal, Other, Disc, Corr, Net, Cgst, Sgst, Gross; }
    private sealed class B { public long Cash, Upi, Card, Bank, Co, Ud, Ota, AdvIn, AdvAdj, AdvRet, Refunds, Net; public readonly SortedDictionary<string, long> Other = new(StringComparer.Ordinal); }

    public static RevenueByCategoryReport Compute(ReportData data, RevenueCategoryData cat, DateOnly from, DateOnly to, string basis = BusinessDate)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(cat);
        if (from > to || from < data.From || to > data.To) throw new ArgumentOutOfRangeException(nameof(from), "The range is outside the loaded data.");
        var stayMode = basis == Stay;
        var stayArrivals = data.Stays.Where(s => s.Arrival >= from && s.Arrival <= to).Select(s => s.StayId).ToHashSet();
        var folioOfStay = cat.Segments.GroupBy(s => s.StayId).ToDictionary(g => g.Key, g => g.First().FolioId);
        var arrivalFolios = stayArrivals.Where(folioOfStay.ContainsKey).Select(id => folioOfStay[id]).ToHashSet();

        string TypeOf(long folio, DateOnly d)
        {
            var segs = cat.Segments.Where(s => s.FolioId == folio).OrderBy(s => s.From).ToList();
            return (segs.FirstOrDefault(s => s.Covers(d)) ?? segs.FirstOrDefault())?.RoomType ?? Unassigned;
        }
        var rowsA = new Dictionary<string, A>(StringComparer.Ordinal);
        A RA(string k) => rowsA.TryGetValue(k, out var r) ? r : rowsA[k] = new A();
        foreach (var t in cat.Rooms.Select(r => r.RoomType).Distinct()) RA(t);

        var lines = stayMode ? data.Lines.Where(l => arrivalFolios.Contains(l.FolioId)) : data.Lines.Where(l => l.ServiceDate >= from && l.ServiceDate <= to);
        foreach (var l in lines)
        {
            var r = RA(TypeOf(l.FolioId, l.ServiceDate));
            var (cg, sg) = l.Split;
            switch (l.Kind)
            {
                case ChargeKind.Room: r.Room += l.Amount.Value; break;
                case ChargeKind.Discount: r.Disc += l.Amount.Value; break;
                case ChargeKind.Correction: r.Corr += l.Amount.Value; break;
                default: if (ExtraRx.IsMatch(l.Label)) r.Extra += l.Amount.Value; else if (MealRx.IsMatch(l.Label)) r.Meal += l.Amount.Value; else r.Other += l.Amount.Value; break;
            }
            r.Net += l.Amount.Value; r.Cgst += cg; r.Sgst += sg; r.Gross += l.Amount.Value + l.Tax.Value;
        }
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            foreach (var room in cat.Rooms)
                if (!data.Blocks.Any(b => b.RoomId == room.RoomId && b.Covers(d))) RA(room.RoomType).Sellable++;
            foreach (var s in data.Stays.Where(s => s.Arrival <= d && d < s.End))
            {
                if (stayMode && !stayArrivals.Contains(s.StayId)) continue;
                var folio = folioOfStay.TryGetValue(s.StayId, out var f) ? f : -1;
                RA(folio < 0 ? Unassigned : TypeOf(folio, d)).Sold++;
            }
        }
        var a = rowsA.Where(kv => kv.Key != Unassigned || kv.Value.Net != 0 || kv.Value.Sold != 0).OrderBy(kv => kv.Key == Unassigned ? 1 : 0).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => ToA(kv.Key, kv.Value)).ToList();
        var total = new A();
        foreach (var r in rowsA.Values)
        { total.Sellable += r.Sellable; total.Sold += r.Sold; total.Room += r.Room; total.Extra += r.Extra; total.Meal += r.Meal; total.Other += r.Other; total.Disc += r.Disc; total.Corr += r.Corr; total.Net += r.Net; total.Cgst += r.Cgst; total.Sgst += r.Sgst; total.Gross += r.Gross; }

        var rowsB = new Dictionary<string, B>(StringComparer.Ordinal);
        B RB(string k) => rowsB.TryGetValue(k, out var r) ? r : rowsB[k] = new B();
        var tb = new B();
        foreach (var x in data.Receipts.Where(x => x.BusinessDate >= from && x.BusinessDate <= to))
        {
            var key = TypeOf(x.FolioId, x.BusinessDate);
            foreach (var r in new[] { RB(key), tb })
                switch (x.Kind)
                {
                    case ReceiptKind.Payment or ReceiptKind.Refund:
                        var v = x.Kind == ReceiptKind.Refund ? -x.Amount.Value : x.Amount.Value;
                        if (x.Kind == ReceiptKind.Refund) r.Refunds += x.Amount.Value;
                        r.Net += v;
                        switch (x.Tender) { case "Cash": r.Cash += v; break; case "UPI": r.Upi += v; break; case "Card": r.Card += v; break; case "Bank transfer": r.Bank += v; break; default: r.Other[x.Tender] = r.Other.GetValueOrDefault(x.Tender) + v; break; }
                        break;
                    case ReceiptKind.Deposit: r.AdvIn += x.Amount.Value; break;
                    case ReceiptKind.DepositApply: r.AdvAdj += x.Amount.Value; break;
                    case ReceiptKind.DepositReturn: r.AdvRet += x.Amount.Value; break;
                    case ReceiptKind.CompanyCredit: r.Co += x.Amount.Value; break;
                    case ReceiptKind.GuestCredit: r.Ud += x.Amount.Value; break;
                    case ReceiptKind.OtaCredit: r.Ota += x.Amount.Value; break;
                }
        }
        var b = rowsB.Where(kv => kv.Key != Unassigned || kv.Value.Net != 0 || kv.Value.AdvIn != 0 || kv.Value.Co != 0).OrderBy(kv => kv.Key == Unassigned ? 1 : 0).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => ToB(kv.Key, kv.Value)).ToList();
        var movement = tb.AdvIn - tb.AdvAdj - tb.AdvRet;
        // The server bridge ties the three blocks: charges in A, receipts in B, advance movement = received − adjusted − returned (opening / closing due need the full history; the prototype shows them).
        var bridge = new RevenueBridge(total.Gross, tb.Net, tb.AdvAdj, movement, Math.Abs(movement - (tb.AdvIn - tb.AdvAdj - tb.AdvRet)));
        return new RevenueByCategoryReport(from, to, basis, a, ToA("total", total, "Total (folios)"), b, ToB("total", tb, "Total"), bridge);
    }

    private static RevenueRowA ToA(string key, A r, string? label = null) => new(key, label ?? (key == Unassigned ? "Unassigned room" : key), r.Sellable, r.Sold,
        r.Sellable == 0 ? 0 : Math.Floor((r.Sold * 1000m + r.Sellable / 2) / r.Sellable) / 10, r.Room, r.Extra, r.Meal, r.Other, r.Disc, r.Corr, r.Net, r.Cgst, r.Sgst, r.Gross,
        r.Sold == 0 ? 0 : MoneyMath.DivideRoundHalfAwayFromZero(r.Room, r.Sold));
    private static RevenueRowB ToB(string key, B r, string? label = null) => new(key, label ?? (key == Unassigned ? "Unassigned room" : key), r.Cash, r.Upi, r.Card, r.Bank, r.Co, r.Ud, r.Ota,
        new Dictionary<string, long>(r.Other), r.AdvIn, r.AdvAdj, r.AdvRet, r.Refunds, r.Net);
}
