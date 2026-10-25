using Hcc.Contracts;
using Hcc.Domain.Billing;
using Hcc.Domain.Closing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;
using Hcc.Domain.Reporting;

namespace Hcc.Closing.Tests.Unit;

/// <summary>
/// Daily-pack arithmetic ported from the prototype report pack (stream-f.mjs, m1.mjs; DEC-069 GST figures from main). Tax-source
/// agnostic: lines carry their stored tax snapshot, so the closing/report arithmetic is checked here with the new GST rates as data.
/// </summary>
public sealed class DailyPackTests
{
    private static readonly DateOnly D1 = new(2026, 10, 24);
    private static readonly DateOnly D2 = new(2026, 10, 25);

    private static long _id;

    private static ReportLine Line(ChargeKind kind, DateOnly date, long amount, int bps, string label = "x", long? reverses = null, ChargeKind? rk = null, string? rl = null)
        => new(Interlocked.Increment(ref _id), 1, kind, date, label, new Paise(amount), bps, MoneyMath.TaxOf(new Paise(amount), bps), reverses, rk, rl);

    private static ReportData Data(IReadOnlyList<ReportLine>? lines = null, IReadOnlyList<ReportReceipt>? receipts = null, IReadOnlyList<ReportShift>? shifts = null,
        IReadOnlyList<ReportMovement>? movements = null, IReadOnlyList<ReportStay>? stays = null, int rooms = 23, IReadOnlyList<ReportRoomBlock>? blocks = null)
        => new(D1, D2, Enumerable.Range(1, rooms).ToList(), blocks ?? [], stays ?? [], lines ?? [], receipts ?? [], shifts ?? [], movements ?? []);

    [Fact]
    public void Room_105_invoice_is_4265_with_room_5pc_laundry_18pc_and_discount_at_the_room_rate()
    {
        // Prototype run.mjs / stream-f.mjs (DEC-069): 2 nights × ₹2,000 at 5 % + laundry ₹500 at 18 % − discount ₹500 at 5 %.
        var lines = new[]
        {
            Line(ChargeKind.Room, D1, 200000, 500, "Room night · 2026-10-24"),
            Line(ChargeKind.Room, D2, 200000, 500, "Room night · 2026-10-25"),
            Line(ChargeKind.Service, D2, 50000, 1800, "Laundry service"),
            Line(ChargeKind.Discount, D2, -50000, 500, "Approved discount · Service delay goodwill"),
        };
        var p = DailyPackCalculator.Compute(Data(lines), D1, D2);
        Assert.Equal((400000L, 26500L, 426500L), (p.Revenue.NetPaise, p.Revenue.TaxPaise, p.Revenue.GrossPaise));
        Assert.Equal((400000L, 50000L, -50000L), (p.Revenue.RoomPaise, p.Revenue.ServicePaise, p.Revenue.DiscountsPaise));
        Assert.Equal(p.Revenue.TaxPaise, p.Revenue.CgstPaise + p.Revenue.SgstPaise);
        var room = Assert.Single(p.Gst, g => g.Category == GstCategories.Room);
        Assert.Equal((500, 3, 350000L, 17500L, 8750L, 8750L), (room.TaxBps, room.Lines, room.TaxablePaise, room.TaxPaise, room.CgstPaise, room.SgstPaise));
        var laundry = Assert.Single(p.Gst, g => g.Category == GstCategories.Laundry);
        Assert.Equal((1800, 50000L, 9000L, 4500L, 4500L), (laundry.TaxBps, laundry.TaxablePaise, laundry.TaxPaise, laundry.CgstPaise, laundry.SgstPaise));
        // Per-date rows add up to the range.
        Assert.Equal(p.Revenue.NetPaise, p.Days.Sum(d => d.NetRevenuePaise));
        Assert.Equal(p.Revenue.TaxPaise, p.Days.Sum(d => d.TaxPaise));
    }

    [Fact]
    public void Cgst_takes_the_odd_paisa_per_line_and_negative_tax_splits_toward_zero()
    {
        Assert.Equal((53L, 52L), GstCategories.Split(105));
        Assert.Equal((-13L, -12L), GstCategories.Split(-25)); // prototype Math.trunc
        Assert.Equal((0L, 0L), GstCategories.Split(0));
        // Split per line, then added: two 2.5 % lines of ₹2.10 tax each → CGST 2 × 11, SGST 2 × 10 (never split on the total).
        var lines = new[] { Line(ChargeKind.Service, D1, 420, 500, "Tea"), Line(ChargeKind.Service, D1, 420, 500, "Tea") };
        var p = DailyPackCalculator.Compute(Data(lines), D1, D1);
        Assert.Equal((42L, 22L, 20L), (p.Revenue.TaxPaise, p.Revenue.CgstPaise, p.Revenue.SgstPaise));
        Assert.Equal(GstCategories.Food, Assert.Single(p.Gst).Category);
    }

    [Theory]
    [InlineData("Laundry service", "laundry")]
    [InlineData("Dry cleaning", "laundry")]
    [InlineData("Banquet hall evening", "banquet")]
    [InlineData("Room service dinner", "food")]
    [InlineData("Minibar", "food")]
    [InlineData("Airport pickup", "other")]
    public void Service_labels_map_to_the_prototype_GST_categories(string label, string category) => Assert.Equal(category, GstCategories.OfLabel(label));

    [Fact]
    public void A_correction_follows_the_category_of_the_line_it_reverses()
    {
        Assert.Equal(GstCategories.Laundry, GstCategories.Of(ChargeKind.Correction, "Reversal · Laundry", ChargeKind.Service, "Laundry"));
        Assert.Equal(GstCategories.Room, GstCategories.Of(ChargeKind.Correction, "Reversal · Room night", ChargeKind.Room, "Room night"));
        Assert.Equal(GstCategories.Room, GstCategories.Of(ChargeKind.Discount, "Goodwill"));
    }

    [Fact]
    public void Occupancy_dictionary_edge_cases_and_ADR_RevPAR()
    {
        // stream-f.mjs: 8 of 23 → 34.8 %; 0 sellable → "—"; ADR/RevPAR half away from zero.
        Assert.Equal("34.8%", DailyPackCalculator.Pct(8, 23));
        Assert.Equal("0.0%", DailyPackCalculator.Pct(0, 23));
        Assert.Equal("—", DailyPackCalculator.Pct(1, 0));
        Assert.Equal("33.3%", DailyPackCalculator.Pct(1, 3));
        Assert.Equal("66.7%", DailyPackCalculator.Pct(2, 3));
        Assert.Null(DailyPackCalculator.Ratio(0, 0));
        Assert.Equal(243750L, DailyPackCalculator.Ratio(1950000, 8)); // BASE ADR ₹19,500 ÷ 8 (prototype Math.round(1950000/8))
        var stays = Enumerable.Range(1, 8).Select(i => new ReportStay(i, i, D1.AddDays(-1), D2.AddDays(3), null, false)).ToList();
        var p = DailyPackCalculator.Compute(Data(stays: stays, blocks: [new ReportRoomBlock(23, null, null)], rooms: 24), D1, D1);
        Assert.Equal((23, 8, "34.8%"), (p.Occupancy.SellableRoomNights, p.Occupancy.OccupiedRoomNights, p.Occupancy.OccupancyPct));
        var none = DailyPackCalculator.Compute(Data(rooms: 2, blocks: [new ReportRoomBlock(1, null, null), new ReportRoomBlock(2, D1, D2)]), D1, D1);
        Assert.Equal((0, "—", (long?)null, (long?)null), (none.Occupancy.SellableRoomNights, none.Occupancy.OccupancyPct, none.Occupancy.AdrPaise, none.Occupancy.RevParPaise));
    }

    [Fact]
    public void Base_drawer_10725_and_collections_21500_from_receipts_restaurant_cash_and_expense()
    {
        // m1.mjs / stream-f.mjs (DEC-069): ₹5,000 float + ₹7,000 cash payments + ₹525 restaurant cash − ₹1,800 cash expense = ₹10,725.
        var receipts = new List<ReportReceipt>();
        var movements = new List<ReportMovement>();
        long rid = 0;
        void Pay(string tender, long amount)
        {
            receipts.Add(new ReportReceipt(++rid, rid, ReceiptKind.Payment, tender, new Paise(amount), D1));
            if (tender == Tenders.Cash) movements.Add(new ReportMovement(rid, 1, D1, CashMovementKind.Payment, new Paise(amount)));
        }

        foreach (var a in new long[] { 200000, 200000, 200000, 100000 }) Pay(Tenders.Cash, a);
        foreach (var a in new long[] { 200000, 300000, 200000, 450000, 200000, 100000 }) Pay(Tenders.Upi, a);
        movements.Add(new ReportMovement(100, 1, D1, CashMovementKind.PosSale, new Paise(52500)));
        movements.Add(new ReportMovement(101, 1, D1, CashMovementKind.Expense, new Paise(-180000)));
        var shift = new ReportShift(1, 1, "FD-1", D1, "Closed", new Paise(500000), new Paise(1072500), new Paise(1072500), 4, null, null);
        var p = DailyPackCalculator.Compute(Data(receipts: receipts, movements: movements, shifts: [shift]), D1, D1);
        Assert.Equal(1072500L, p.Drawer.ExpectedPaise);
        Assert.Equal((500000L, 752500L, 180000L, 1072500L, 0L), (p.Drawer.OpeningPaise, p.Drawer.CashInPaise, p.Drawer.CashOutPaise, p.Drawer.CountedPaise!.Value, p.Drawer.DifferencePaise));
        Assert.Equal(2150000L, p.Receipts.NetPaise);
        Assert.Equal((700000L, 1450000L), (p.Receipts.ByTender.Single(t => t.Tender == "Cash").NetPaise, p.Receipts.ByTender.Single(t => t.Tender == "UPI").NetPaise));
        Assert.Equal((180000L, 180000L), (p.Expenses.CashPaise, p.Expenses.TotalPaise));
        Assert.Equal(52500L, Assert.Single(p.Drawer.Tills).RestaurantPaise);
    }

    [Fact]
    public void Two_shifts_on_one_till_carry_the_count_and_the_difference_is_reported_once()
    {
        // DEC-035 multi-shift: shift 2 opens with shift 1's counted cash (₹200 short, approved); day expected = shift 2's expected.
        var s1 = new ReportShift(1, 1, "FD-1", D1, "Closed", new Paise(100000), new Paise(150000), new Paise(130000), 4, 2, "Short ₹200");
        var s2 = new ReportShift(2, 1, "FD-1", D1, "Closed", new Paise(130000), new Paise(160000), new Paise(160000), 5, null, null);
        var mv = new[] { new ReportMovement(1, 1, D1, CashMovementKind.Payment, new Paise(50000)), new ReportMovement(2, 2, D1, CashMovementKind.Payment, new Paise(30000)) };
        var row = Assert.Single(DailyPackCalculator.Compute(Data(shifts: [s1, s2], movements: mv), D1, D1).Drawer.Tills);
        Assert.Equal((2, 100000L, 80000L, 160000L, 160000L, -20000L, 2, "Short ₹200"),
            (row.Shifts, row.OpeningPaise, row.CashInPaise, row.ExpectedPaise, row.CountedPaise!.Value, row.DifferencePaise, row.ApprovedBy!.Value, row.Reason));
    }

    [Fact]
    public void Expense_reversal_offsets_on_its_own_date_and_deposits_are_not_receipts()
    {
        var mv = new[]
        {
            new ReportMovement(1, 1, D1, CashMovementKind.Expense, new Paise(-30000)),
            new ReportMovement(2, 1, D2, CashMovementKind.ExpenseReversal, new Paise(30000)),
        };
        var receipts = new[]
        {
            new ReportReceipt(1, 1, ReceiptKind.Deposit, "Cash", new Paise(50000), D1),
            new ReportReceipt(2, 1, ReceiptKind.DepositReturn, "Cash", new Paise(50000), D2),
            new ReportReceipt(3, 1, ReceiptKind.Payment, "Card", new Paise(10000), D2),
            new ReportReceipt(4, 1, ReceiptKind.Refund, "Card", new Paise(4000), D2),
        };
        var p = DailyPackCalculator.Compute(Data(movements: mv, receipts: receipts), D1, D2);
        Assert.Equal((0L, 30000L), (p.Expenses.TotalPaise, p.Expenses.ReversalsPaise));
        Assert.Equal((30000L, -30000L), (p.Days[0].CashExpensesPaise, p.Days[1].CashExpensesPaise));
        Assert.Equal((6000L, 50000L, 50000L), (p.Receipts.NetPaise, p.Receipts.DepositsInPaise, p.Receipts.DepositsOutPaise));
        Assert.Equal(6000L, p.Receipts.ByTender.Single(t => t.Tender == "Card").NetPaise);
    }

    [Fact]
    public void Preflight_lists_blockers_in_close_order_and_the_error_names_them()
    {
        var data = new PreflightData(D1,
            [new PendingArrival(1, "HCC-1049", "Guest A (fictional)", "110", D1)], [],
            [new ExpiredHold(2, "HCC-9001", "Hold Test (fictional)", new DateTime(2026, 10, 24, 7, 30, 0, DateTimeKind.Utc))],
            [new NightTarget(3, 30, "HCC-1041", "Guest B (fictional)", "101", false), new NightTarget(4, 40, "HCC-1042", "Guest C (fictional)", "102", true)],
            [new PendingApproval(5, "cash-difference", new Paise(30000), "Short")], [new ActiveShift(6, "FD-1", "Open", D1, new Paise(1072500))]);
        var checks = CloseRules.Evaluate(data);
        Assert.Equal(["arrivals", "departures", "holds", "nights", "approvals", "shift"], checks.Select(c => c.Id));
        Assert.Equal([true, false, true, true, true, true], checks.Select(c => c.Blocking));
        Assert.Single(checks.Single(c => c.Id == "nights").Items); // only the stay without tonight's line
        Assert.Contains("₹10,725", checks.Single(c => c.Id == "shift").Items[0].Label, StringComparison.Ordinal);
        Assert.Equal(["arrivals", "holds", "approvals"], CloseRules.Blockers(checks, CloseRules.PreflightBlocking).Select(b => b.Id));
        var ex = CloseRules.Blocked(D1, CloseRules.Blockers(checks));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, ex.Code);
        Assert.Equal(5, ex.Details!["blockers"].GetArrayLength());
    }

    [Fact]
    public void Cash_book_lock_rules_follow_DEC_065()
    {
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Assert.Throws<HccException>(() => CashBookRules.EnsureCanLock(7, false, false, [])).Code);
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Assert.Throws<HccException>(() => CashBookRules.EnsureCanLock(7, true, true, [])).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, Assert.Throws<HccException>(() => CashBookRules.EnsureCanLock(7, true, false, [3, 7])).Code);
        CashBookRules.EnsureCanLock(7, true, false, [3, 4]);
        Assert.Throws<HccException>(() => CashBookRules.EnsureReopenReason("  "));
        Assert.Equal("Cash book 2026-10-24 is locked, so a correction is blocked. The Owner must reopen 2026-10-24 (with a reason) first.", CashBookRules.LockedMessage(D1, "a correction"));
        Assert.Throws<HccException>(() => CloseRules.ValidateRunRef("bad id with spaces"));
        Assert.Equal("run-1", CloseRules.ValidateRunRef(" run-1 "));
    }
}
