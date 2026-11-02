using Hcc.Domain.Billing;
using Hcc.Domain.Money;
using Hcc.Domain.Reporting;

namespace Hcc.Closing.Tests.Unit;

/// <summary>F4-T2: revenue by room category and tender ties to the daily pack (Revenue and Receipts tabs) to the paisa. Fictional data.</summary>
public sealed class RevenueByCategoryTests
{
    private static readonly DateOnly D1 = new(2026, 10, 24);
    private static readonly DateOnly D2 = new(2026, 10, 25);
    private static long _id;

    private static ReportLine Line(long folio, ChargeKind kind, DateOnly date, long amount, int bps, string label = "x")
        => new(Interlocked.Increment(ref _id), folio, kind, date, label, new Paise(amount), bps, MoneyMath.TaxOf(new Paise(amount), bps), null, null, null);

    private static ReportReceipt Rcpt(long folio, ReceiptKind kind, string tender, long amount, DateOnly d) => new(Interlocked.Increment(ref _id), folio, kind, tender, new Paise(amount), d);

    // Room 1 = Deluxe, room 2 = Suite. Stay 10 (folio 100) in room 1; stay 20 (folio 200) in room 2 on D1 then room 1 on D2; folio 300 has no stay (unassigned).
    private static (ReportData, RevenueCategoryData) Sample()
    {
        var lines = new List<ReportLine>
        {
            Line(100, ChargeKind.Room, D1, 200000, 500, "Room night"), Line(100, ChargeKind.Service, D1, 50000, 1800, "Laundry"), Line(100, ChargeKind.Discount, D1, -20000, 500, "Discount"),
            Line(200, ChargeKind.Room, D1, 900000, 1800, "Room night"), Line(200, ChargeKind.Room, D2, 900000, 1800, "Room night"), Line(200, ChargeKind.Service, D2, 100000, 1800, "Extra person"),
            Line(300, ChargeKind.Service, D2, 10000, 1800, "Misc"),
        };
        var receipts = new List<ReportReceipt>
        {
            Rcpt(100, ReceiptKind.Payment, "Cash", 100000, D1), Rcpt(100, ReceiptKind.Deposit, "Cash", 50000, D1), Rcpt(100, ReceiptKind.Refund, "Cash", 10000, D2),
            Rcpt(200, ReceiptKind.Payment, "UPI", 500000, D2), Rcpt(200, ReceiptKind.CompanyCredit, "Company credit", 400000, D2), Rcpt(200, ReceiptKind.DepositApply, "Cash", 20000, D2),
            Rcpt(300, ReceiptKind.Payment, "Wallet", 5000, D2),
        };
        var stays = new List<ReportStay> { new(10, 1, D1, D2, null, false), new(20, 2, D1, D2.AddDays(1), null, false) };
        var data = new ReportData(D1, D2, [1, 2], [], stays, lines, receipts, [], []);
        var cat = new RevenueCategoryData([new(1, "Deluxe"), new(2, "Suite")],
            [new(10, 100, 1, "Deluxe", D1, null), new(20, 200, 2, "Suite", D1, D2), new(20, 200, 1, "Deluxe", D2, null)]);
        return (data, cat);
    }

    [Fact]
    public void Block_A_total_ties_to_the_daily_pack_revenue()
    {
        var (data, cat) = Sample();
        var r = RevenueByCategoryCalculator.Compute(data, cat, D1, D2);
        var p = DailyPackCalculator.Compute(data, D1, D2);
        Assert.Equal(p.Revenue.NetPaise, r.ATotal.Net);
        Assert.Equal(p.Revenue.CgstPaise, r.ATotal.Cgst);
        Assert.Equal(p.Revenue.SgstPaise, r.ATotal.Sgst);
        Assert.Equal(p.Revenue.GrossPaise, r.ATotal.Gross);
        Assert.Equal(p.Revenue.RoomPaise, r.ATotal.RoomTariff);
        Assert.Equal(r.ATotal.Net, r.A.Sum(x => x.Net));
    }

    [Fact]
    public void A_line_belongs_to_the_room_type_of_the_stay_on_that_night_and_an_unmapped_folio_is_kept()
    {
        var (data, cat) = Sample();
        var r = RevenueByCategoryCalculator.Compute(data, cat, D1, D2);
        var suite = r.A.Single(x => x.Key == "Suite");
        Assert.Equal(900000, suite.RoomTariff);   // the D1 night in the suite
        var deluxe = r.A.Single(x => x.Key == "Deluxe");
        Assert.Equal(200000 + 900000, deluxe.RoomTariff); // folio 100's night + folio 200's D2 night after the room move
        Assert.Equal(100000, deluxe.ExtraPerson);
        Assert.Equal(50000, deluxe.OtherCharges - 0);
        Assert.Equal(-20000, deluxe.Discounts);
        Assert.Equal(10000, r.A.Single(x => x.Key == RevenueByCategoryCalculator.Unassigned).Net);
    }

    [Fact]
    public void Block_B_total_ties_to_the_daily_pack_tenders_and_deposits()
    {
        var (data, cat) = Sample();
        var r = RevenueByCategoryCalculator.Compute(data, cat, D1, D2);
        var p = DailyPackCalculator.Compute(data, D1, D2);
        Assert.Equal(p.Receipts.ByTender.Single(t => t.Tender == "Cash").NetPaise, r.BTotal.Cash);
        Assert.Equal(p.Receipts.ByTender.Single(t => t.Tender == "UPI").NetPaise, r.BTotal.Upi);
        Assert.Equal(p.Receipts.NetPaise, r.BTotal.NetReceived);
        Assert.Equal(p.Receipts.DepositsInPaise, r.BTotal.AdvancesReceived);
        Assert.Equal(p.Receipts.CompanyCreditPaise, r.BTotal.CompanyCredit);
        Assert.Equal(5000, r.BTotal.OtherTenders["Wallet"]);
        Assert.Equal(10000, r.BTotal.Refunds);
        Assert.Equal(r.BTotal.NetReceived, r.B.Sum(x => x.NetReceived));
    }

    [Fact]
    public void Stay_basis_counts_the_whole_folio_of_stays_that_arrive_in_the_range_and_the_bridge_ties()
    {
        var (data, cat) = Sample();
        var stay = RevenueByCategoryCalculator.Compute(data, cat, D2, D2, RevenueByCategoryCalculator.Stay);
        Assert.Equal(0, stay.ATotal.Net); // both stays arrived on D1
        var all = RevenueByCategoryCalculator.Compute(data, cat, D1, D1, RevenueByCategoryCalculator.Stay);
        Assert.Equal(data.Lines.Where(l => l.FolioId is 100 or 200).Sum(l => l.Amount.Value), all.ATotal.Net);
        var r = RevenueByCategoryCalculator.Compute(data, cat, D1, D2);
        Assert.Equal(0, r.C.Difference);
        Assert.Equal(r.BTotal.AdvancesReceived - r.BTotal.AdvancesAdjusted - r.BTotal.AdvancesRefunded, r.C.AdvanceMovement);
    }
}
