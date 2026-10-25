using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Contracts.Reporting;
using Hcc.Domain.Billing;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;

namespace Hcc.Domain.Reporting;

// ── Source records (read from the same tables the commands write; no second source of truth, REQ-RPT-003) ──

/// <summary>A posted folio line with its stored tax snapshot; for a correction, the kind/label of the line it reverses.</summary>
public sealed record ReportLine(long ChargeId, long FolioId, ChargeKind Kind, DateOnly ServiceDate, string Label, Paise Amount, int TaxBps, Paise Tax,
    long? ReversesChargeId, ChargeKind? ReversedKind, string? ReversedLabel);

public sealed record ReportReceipt(long ReceiptId, long FolioId, ReceiptKind Kind, string Tender, Paise Amount, DateOnly BusinessDate);

public sealed record ReportMovement(long MovementId, long ShiftId, DateOnly BusinessDate, CashMovementKind Kind, Paise Amount);

/// <summary>A cashier shift; <see cref="ApprovedBy"/>/<see cref="Reason"/> come from an approved cash-difference request.</summary>
public sealed record ReportShift(long ShiftId, int TillId, string TillCode, DateOnly BusinessDate, string Status, Paise OpeningFloat, Paise? Expected,
    Paise? Counted, int? SubmittedBy, int? ApprovedBy, string? Reason);

public sealed record ReportStay(long StayId, long ReservationId, DateOnly Arrival, DateOnly Departure, DateOnly? CheckedOutDate, bool CheckedOut)
{
    /// <summary>End of the occupied interval: an early checkout ends it on the checkout date.</summary>
    public DateOnly End => CheckedOut && CheckedOutDate is { } d && d < Departure ? d : Departure;
}

public sealed record ReportRoomBlock(int RoomId, DateOnly? From, DateOnly? To)
{
    public bool Covers(DateOnly night) => (From is null || From <= night) && (To is null || night < To);
}

public sealed record ReportData(DateOnly From, DateOnly To, IReadOnlyList<int> RoomIds, IReadOnlyList<ReportRoomBlock> Blocks, IReadOnlyList<ReportStay> Stays,
    IReadOnlyList<ReportLine> Lines, IReadOnlyList<ReportReceipt> Receipts, IReadOnlyList<ReportShift> Shifts, IReadOnlyList<ReportMovement> Movements);

/// <summary>
/// GST category of a posted line (prototype <c>gst.ts</c>: room, food, laundry, banquet, other) and the intra-state CGST/SGST split.
/// Discounts and corrections reduce the supply they belong to: a discount follows the room (prototype <c>gstForFolioAdjustment</c>),
/// a correction the line it reverses. Illustrative — CA to confirm.
/// </summary>
public static partial class GstCategories
{
    public const string Room = "room";
    public const string Food = "food";
    public const string Laundry = "laundry";
    public const string Banquet = "banquet";
    public const string Other = "other";

    [GeneratedRegex(@"laund|dry.?clean|ironing|press(ing)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LaundryWords();

    [GeneratedRegex(@"banquet|\bhall\b|conference|meeting room", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BanquetWords();

    [GeneratedRegex(@"food|restaurant|room service|breakfast|lunch|dinner|meal|snack|minibar|mini-bar|\btea\b|coffee|chai|beverage|juice|water bottle",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FoodWords();

    /// <summary>Category of a free-text service charge (prototype <c>gstCategoryOfLabel</c>).</summary>
    // GST-C: switch to the category stored on the charge once agent C's GST master lands server-side (bill.charge category).
    public static string OfLabel(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (LaundryWords().IsMatch(label)) return Laundry;
        if (BanquetWords().IsMatch(label)) return Banquet;
        if (FoodWords().IsMatch(label)) return Food;
        return Other;
    }

    public static string Of(ChargeKind kind, string label, ChargeKind? reversedKind = null, string? reversedLabel = null) => kind switch
    {
        ChargeKind.Room or ChargeKind.Discount => Room,
        ChargeKind.Service => OfLabel(label),
        _ => reversedKind is { } k && k != ChargeKind.Correction ? Of(k, reversedLabel ?? string.Empty) : Room,
    };

    /// <summary>SGST = half the line tax rounded toward zero; CGST = the rest (takes the odd paisa) — always sums to the line tax.</summary>
    public static (long Cgst, long Sgst) Split(long tax)
    {
        var sgst = tax / 2; // C# integer division truncates toward zero (prototype Math.trunc)
        return (tax - sgst, sgst);
    }
}

/// <summary>
/// The owner's daily pack (prototype <c>reportPack</c> / <c>closingSummary</c>, <c>03 Prototype/src/domain/streams/closing.ts</c>):
/// lines by service date, receipts by receipt date, cash by shift business date, occupancy from stay intervals. Pure; the closing
/// snapshot and GET /reports/daily both call <see cref="Compute"/> on data read from the same tables.
/// </summary>
public static class DailyPackCalculator
{
    public const string ExpenseNote = "Wave 1: drawer-cash expenses (shared till); non-cash expenses arrive with the expense module (Wave 2).";

    /// <summary>Occupancy % to 1 dp, integer arithmetic half-up; "—" when the denominator is zero (report dictionary §6).</summary>
    public static string Pct(long num, long den)
    {
        if (den <= 0) return "—";
        var tenths = (num * 1000 + den / 2) / den;
        return (tenths / 10).ToString(CultureInfo.InvariantCulture) + "." + (tenths % 10).ToString(CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>Money ratio (ADR, RevPAR) rounded half away from zero to the paisa; null when the denominator is zero.</summary>
    public static long? Ratio(long num, long den) => den <= 0 ? null : MoneyMath.DivideRoundHalfAwayFromZero(num, den);

    public static IEnumerable<DateOnly> Dates(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1)) yield return d;
    }

    /// <summary>The pack for [from, to] (inclusive) computed from <paramref name="data"/>, which must cover that range.</summary>
    public static DailyReport Compute(ReportData data, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (from > to || from < data.From || to > data.To) throw new ArgumentOutOfRangeException(nameof(from), "The range is outside the loaded data.");
        bool In(DateOnly d) => d >= from && d <= to;
        var dates = Dates(from, to).ToList();
        var lines = data.Lines.Where(l => In(l.ServiceDate)).ToList();
        var receipts = data.Receipts.Where(r => In(r.BusinessDate)).ToList();
        var shifts = data.Shifts.Where(s => In(s.BusinessDate)).ToList();
        var movements = data.Movements.Where(m => In(m.BusinessDate)).ToList();

        int Sellable(DateOnly d) => data.RoomIds.Count(r => !data.Blocks.Any(b => b.RoomId == r && b.Covers(d)));
        int Occupied(DateOnly d) => data.Stays.Count(s => s.Arrival <= d && d < s.End);
        long Sum(IEnumerable<ReportLine> ls) => ls.Sum(l => l.Amount.Value);

        // Revenue and GST
        var room = Sum(lines.Where(l => l.Kind == ChargeKind.Room));
        var service = Sum(lines.Where(l => l.Kind == ChargeKind.Service));
        var discounts = Sum(lines.Where(l => l.Kind == ChargeKind.Discount));
        var corrections = Sum(lines.Where(l => l.Kind == ChargeKind.Correction));
        var net = room + service + discounts + corrections;
        var gst = lines
            .GroupBy(l => (Category: GstCategories.Of(l.Kind, l.Label, l.ReversedKind, l.ReversedLabel), l.TaxBps))
            .Select(g =>
            {
                var split = g.Select(l => GstCategories.Split(l.Tax.Value)).ToList();
                return new GstCategoryRow(g.Key.Category, g.Key.TaxBps, g.Count(), Sum(g), split.Sum(x => x.Cgst), split.Sum(x => x.Sgst), g.Sum(l => l.Tax.Value));
            })
            .OrderBy(r => CategoryOrder(r.Category)).ThenBy(r => r.TaxBps)
            .ToList();
        var tax = gst.Sum(r => r.TaxPaise);
        var revenue = new RevenueFigures(room, service, discounts, corrections, net, tax, gst.Sum(r => r.CgstPaise), gst.Sum(r => r.SgstPaise), net + tax);

        // Occupancy
        var sellable = dates.Sum(Sellable);
        var occupied = dates.Sum(Occupied);
        var occupancy = new OccupancyFigures(sellable, occupied, Pct(occupied, sellable), room, Ratio(room, occupied), Ratio(room, sellable));

        // Receipts
        var found = receipts.Where(r => r.Kind is ReceiptKind.Payment or ReceiptKind.Refund).Select(r => r.Tender).Distinct().Where(t => !Tenders.All.Contains(t)).Order(StringComparer.Ordinal);
        var tenders = Tenders.All.Concat(found).Select(t =>
        {
            var pay = receipts.Where(r => r.Tender == t && r.Kind == ReceiptKind.Payment).Sum(r => r.Amount.Value);
            var refund = receipts.Where(r => r.Tender == t && r.Kind == ReceiptKind.Refund).Sum(r => r.Amount.Value);
            return new TenderRow(t, pay, refund, pay - refund);
        }).ToList();
        long Kind(ReceiptKind k) => receipts.Where(r => r.Kind == k).Sum(r => r.Amount.Value);
        var receiptFigures = new ReceiptFigures(tenders, tenders.Sum(t => t.NetPaise), Kind(ReceiptKind.Deposit), Kind(ReceiptKind.DepositReturn), Kind(ReceiptKind.CompanyCredit));

        // Expenses (drawer cash): a reversal is a positive movement on its own date and offsets.
        var expenseOut = -movements.Where(m => m.Kind == CashMovementKind.Expense).Sum(m => m.Amount.Value);
        var reversals = movements.Where(m => m.Kind == CashMovementKind.ExpenseReversal).Sum(m => m.Amount.Value);
        var expenses = new ExpenseFigures(expenseOut - reversals, reversals, expenseOut - reversals, ExpenseNote);

        var drawer = Drawer(shifts, movements);

        var days = dates.Select(d =>
        {
            var dl = lines.Where(l => l.ServiceDate == d).ToList();
            var dr = receipts.Where(r => r.BusinessDate == d && r.Kind is ReceiptKind.Payment or ReceiptKind.Refund);
            var dm = movements.Where(m => m.BusinessDate == d && m.Kind is CashMovementKind.Expense or CashMovementKind.ExpenseReversal);
            return new ReportDayRow(d,
                data.Stays.Count(s => s.Arrival == d),
                data.Stays.Count(s => s.CheckedOut && s.CheckedOutDate == d),
                Sellable(d), Occupied(d), Sum(dl), dl.Sum(l => l.Tax.Value),
                dr.Sum(r => r.Kind == ReceiptKind.Payment ? r.Amount.Value : -r.Amount.Value),
                -dm.Sum(m => m.Amount.Value));
        }).ToList();

        return new DailyReport(from, to, occupancy, revenue, gst, receiptFigures, expenses, drawer, days);
    }

    private static int CategoryOrder(string c) => c switch
    {
        GstCategories.Room => 0,
        GstCategories.Food => 1,
        GstCategories.Laundry => 2,
        GstCategories.Banquet => 3,
        _ => 4,
    };

    /// <summary>Per till and business date: first shift's float, Σ cash in/out over the shifts, last shift's expected/counted (DEC-035 multi-shift).</summary>
    private static DrawerFigures Drawer(IReadOnlyList<ReportShift> shifts, IReadOnlyList<ReportMovement> movements)
    {
        var rows = new List<DrawerDayRow>();
        foreach (var g in shifts.GroupBy(s => (s.TillCode, s.BusinessDate)).OrderBy(g => g.Key.BusinessDate).ThenBy(g => g.Key.TillCode, StringComparer.Ordinal))
        {
            var ordered = g.OrderBy(s => s.ShiftId).ToList();
            long cashIn = 0, cashOut = 0, expense = 0, restaurant = 0, difference = 0;
            Paise lastExpected = Paise.Zero;
            foreach (var s in ordered)
            {
                var mv = movements.Where(m => m.ShiftId == s.ShiftId).Select(m => new CashMovement(m.Kind, m.Amount)).ToList();
                var e = CashDrawer.Expected(s.OpeningFloat, mv);
                cashIn += mv.Where(m => m.Amount > Paise.Zero).Sum(m => m.Amount.Value);
                cashOut += -mv.Where(m => m.Amount < Paise.Zero).Sum(m => m.Amount.Value);
                expense += e.Expenses.Value;
                restaurant += e.Restaurant.Value;
                lastExpected = e.Expected;
                if (s.Counted is { } c && s.Status == "Closed") difference += c.Value - e.Expected.Value;
            }

            var last = ordered[^1];
            var status = ordered.All(s => s.Status == "Closed") ? "Closed" : last.Status;
            var reasons = ordered.Where(s => s.Reason is not null).Select(s => s.Reason!).ToList();
            rows.Add(new DrawerDayRow(g.Key.TillCode, g.Key.BusinessDate, ordered.Count, status, ordered[0].OpeningFloat.Value, cashIn, cashOut, expense, restaurant,
                lastExpected.Value, last.Status == "Closed" ? last.Counted?.Value : null, difference, last.SubmittedBy,
                ordered.LastOrDefault(s => s.ApprovedBy is not null)?.ApprovedBy, reasons.Count > 0 ? string.Join(" · ", reasons) : null));
        }

        var latest = rows.GroupBy(r => r.TillCode).Select(g => g.OrderBy(r => r.BusinessDate).Last()).ToList();
        var earliest = rows.GroupBy(r => r.TillCode).Select(g => g.OrderBy(r => r.BusinessDate).First()).ToList();
        long? counted = latest.Count > 0 && latest.All(r => r.CountedPaise is not null) ? latest.Sum(r => r.CountedPaise!.Value) : null;
        return new DrawerFigures(rows, earliest.Sum(r => r.OpeningPaise), rows.Sum(r => r.CashInPaise), rows.Sum(r => r.CashOutPaise),
            latest.Sum(r => r.ExpectedPaise), counted, rows.Sum(r => r.DifferencePaise));
    }
}
