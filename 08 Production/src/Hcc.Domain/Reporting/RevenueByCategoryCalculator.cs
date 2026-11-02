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

/// <summary>Guest-folio due and advances held at the end of a business date (see <c>FolioBalances</c> in the SQL source): due = lines + tax − (payments, deposit applied, credits to company / OTA / Udhaar − refunds).</summary>
public sealed record FolioBalances(long DuePaise, long AdvancesPaise);

/// <summary>Block C. Opening / closing figures are present when the balances were supplied; <see cref="Difference"/> is then the sum of the two identities
/// (closing due = opening due + gross − net received − advances adjusted − credits; closing advances = opening + movement), 0 when the report closes.</summary>
public sealed record RevenueBridge(long Gross, long NetReceived, long AdvancesAdjusted, long AdvanceMovement, long Difference,
    long? OpeningDue = null, long? ClosingDue = null, long? OpeningAdvances = null, long? ClosingAdvances = null, long Credits = 0);

/// <summary>One ledger-by-ledger comparison of Block C with the trial balance; <see cref="Ok"/> = the report figure equals the ledger figure.</summary>
public sealed record RevenueLedgerRow(string Account, string Name, string What, long Report, long Ledger, bool Ok);

/// <summary>A trial-balance ledger for the comparison: signed (debit − credit) opening and closing, and the period debit.</summary>
public sealed record LedgerFigures(long Opening, long Closing, long PeriodDebit);

public sealed record RevenueByCategoryReport(DateOnly From, DateOnly To, string Basis, IReadOnlyList<RevenueRowA> A, RevenueRowA ATotal, IReadOnlyList<RevenueRowB> B,
    RevenueRowB BTotal, RevenueBridge C, IReadOnlyList<RevenueLedgerRow>? Ledgers = null, bool Legacy = false)
{
    /// <summary>Header of a legacy range (LG contract §6).</summary>
    public const string LegacyHeader = "Legacy data (old Lodge software)";
}

/// <summary>One lgc.bill row for the report; <see cref="RoomType"/> is the rooms-master type of the legacy room name, null = unmapped (kept as a row <c>unassigned</c>).</summary>
public sealed record LegacyBillRow(string? RoomType, long RoomCharge, long Extra, long Fb, long Laundry, long Other, long Discount, long RoomGst, long FbGst, long LaundryGst, long OtherGst);

/// <summary>One lgc.receipt row: kind advance / payment / refund / security, method CASH / UPI / CARD / BANK / OTHER.</summary>
public sealed record LegacyReceiptRow(string? RoomType, string Kind, string Method, long Amount);

public sealed record RevenueLegacyData(IReadOnlyList<LegacyBillRow> Bills, IReadOnlyList<LegacyReceiptRow> Receipts);

public static class RevenueByCategoryCalculator
{
    public const string BusinessDate = "business-date", Stay = "stay", Unassigned = "unassigned";
    private static readonly Regex ExtraRx = new(@"extra\s+(person|bed|guest|adult)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MealRx = new(@"meal|breakfast|\b(CP|MAP|AP)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private sealed class A { public int Sellable, Sold; public long Room, Extra, Meal, Other, Disc, Corr, Net, Cgst, Sgst, Gross; }
    private sealed class B { public long Cash, Upi, Card, Bank, Co, Ud, Ota, AdvIn, AdvAdj, AdvRet, Refunds, Net; public readonly SortedDictionary<string, long> Other = new(StringComparer.Ordinal); }

    public static RevenueByCategoryReport Compute(ReportData data, RevenueCategoryData cat, DateOnly from, DateOnly to, string basis = BusinessDate, FolioBalances? opening = null, FolioBalances? closing = null)
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
        var credits = tb.Co + tb.Ud + tb.Ota;
        long diff = 0;
        if (opening is not null && closing is not null)
        {
            // The bridge is on business dates: the stay basis only regroups Block A, so its gross is the range's lines (as the prototype).
            var gross = stayMode ? data.Lines.Where(l => l.ServiceDate >= from && l.ServiceDate <= to).Sum(l => l.Amount.Value + l.Tax.Value) : total.Gross;
            diff = Math.Abs(closing.DuePaise - (opening.DuePaise + gross - tb.Net - tb.AdvAdj - credits)) + Math.Abs(closing.AdvancesPaise - opening.AdvancesPaise - movement);
        }
        var bridge = new RevenueBridge(total.Gross, tb.Net, tb.AdvAdj, movement, diff, opening?.DuePaise, closing?.DuePaise, opening?.AdvancesPaise, closing?.AdvancesPaise, credits);
        return new RevenueByCategoryReport(from, to, basis, a, ToA("total", total, "Total (folios)"), b, ToB("total", tb, "Total"), bridge);
    }

    /// <summary>The legacy range (LG contract §6): the same two blocks over the old Lodge bills and receipts. Net = Σ components − discount; GST = Σ the four stored GST amounts,
    /// split per bill (SGST half toward zero, CGST the odd paisa); advance and payment rows are collections by tender, a security deposit is an advance, a refund goes out.</summary>
    public static RevenueByCategoryReport ComputeLegacy(RevenueLegacyData d, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(d);
        var ra = new Dictionary<string, A>(StringComparer.Ordinal); var rb = new Dictionary<string, B>(StringComparer.Ordinal);
        A RA(string? k) { k ??= Unassigned; return ra.TryGetValue(k, out var r) ? r : ra[k] = new A(); }
        B RB(string? k) { k ??= Unassigned; return rb.TryGetValue(k, out var r) ? r : rb[k] = new B(); }
        var ta = new A(); var tb = new B();
        foreach (var x in d.Bills)
        {
            var net = x.RoomCharge + x.Extra + x.Fb + x.Laundry + x.Other - x.Discount;
            var tax = x.RoomGst + x.FbGst + x.LaundryGst + x.OtherGst;
            var (cg, sg) = GstCategories.Split(tax);
            foreach (var r in new[] { RA(x.RoomType), ta })
            { r.Room += x.RoomCharge; r.Extra += x.Extra; r.Other += x.Fb + x.Laundry + x.Other; r.Disc -= x.Discount; r.Net += net; r.Cgst += cg; r.Sgst += sg; r.Gross += net + tax; }
        }
        foreach (var x in d.Receipts)
            foreach (var r in new[] { RB(x.RoomType), tb })
            {
                if (x.Kind == "security") { r.AdvIn += x.Amount; continue; }
                var v = x.Kind == "refund" ? -x.Amount : x.Amount;
                if (x.Kind == "refund") r.Refunds += x.Amount;
                r.Net += v;
                switch (x.Method) { case "CASH": r.Cash += v; break; case "UPI": r.Upi += v; break; case "CARD": r.Card += v; break; case "BANK": r.Bank += v; break; default: r.Other["Other"] = r.Other.GetValueOrDefault("Other") + v; break; }
            }
        var a = ra.OrderBy(kv => kv.Key == Unassigned ? 1 : 0).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => ToA(kv.Key, kv.Value)).ToList();
        var b = rb.OrderBy(kv => kv.Key == Unassigned ? 1 : 0).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => ToB(kv.Key, kv.Value)).ToList();
        var bridge = new RevenueBridge(ta.Gross, tb.Net, 0, tb.AdvIn, 0);
        return new RevenueByCategoryReport(from, to, BusinessDate, a, ToA("total", ta, "Total (legacy bills)"), b, ToB("total", tb, "Total"), bridge, null, true);
    }

    /// <summary>Block C against the trial balance, ledger by ledger (same seven rows as the prototype): guest ledger 1100 opening / closing due, advances 2000 start / end,
    /// and the credits raised against 1110 company, 1115 OTA and 1120 Udhaar receivables (period debit). <paramref name="ledger"/> returns the ledger's figures (zeros when it has none).</summary>
    public static IReadOnlyList<RevenueLedgerRow> LedgerRows(RevenueByCategoryReport r, Func<string, LedgerFigures> ledger)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(ledger);
        var c = r.C;
        RevenueLedgerRow Row(string acc, string name, string what, long report, long led) => new(acc, name, what, report, led, report == led);
        var g = ledger("1100"); var adv = ledger("2000");
        return
        [
            Row("1100", "Guest ledger (in-house folios)", "Opening due", c.OpeningDue ?? 0, g.Opening),
            Row("1100", "Guest ledger (in-house folios)", "Closing due", c.ClosingDue ?? 0, g.Closing),
            Row("2000", "Advance deposits", "Advances held at start", c.OpeningAdvances ?? 0, -adv.Opening),
            Row("2000", "Advance deposits", "Advances held at end", c.ClosingAdvances ?? 0, -adv.Closing),
            Row("1110", "Company receivables", "Company credit raised", r.BTotal.CompanyCredit, ledger("1110").PeriodDebit),
            Row("1115", "OTA receivables", "OTA credit raised", r.BTotal.Ota, ledger("1115").PeriodDebit),
            Row("1120", "Udhaar receivables", "Guest (Udhaar) credit raised", r.BTotal.Udhaar, ledger("1120").PeriodDebit),
        ];
    }

    private static RevenueRowA ToA(string key, A r, string? label = null) => new(key, label ?? (key == Unassigned ? "Unassigned room" : key), r.Sellable, r.Sold,
        r.Sellable == 0 ? 0 : Math.Floor((r.Sold * 1000m + r.Sellable / 2) / r.Sellable) / 10, r.Room, r.Extra, r.Meal, r.Other, r.Disc, r.Corr, r.Net, r.Cgst, r.Sgst, r.Gross,
        r.Sold == 0 ? 0 : MoneyMath.DivideRoundHalfAwayFromZero(r.Room, r.Sold));
    private static RevenueRowB ToB(string key, B r, string? label = null) => new(key, label ?? (key == Unassigned ? "Unassigned room" : key), r.Cash, r.Upi, r.Card, r.Bank, r.Co, r.Ud, r.Ota,
        new Dictionary<string, long>(r.Other), r.AdvIn, r.AdvAdj, r.AdvRet, r.Refunds, r.Net);
}
