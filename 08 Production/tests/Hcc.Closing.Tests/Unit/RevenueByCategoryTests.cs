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

    [Fact]
    public void Block_C_closes_with_opening_and_closing_due_and_ties_to_the_trial_balance_ledger_by_ledger()
    {
        var (data, cat) = Sample();
        // Folio due after D2 = lines + tax − (payments, deposit applied, credits, − refunds); the sample leaves the opening at zero.
        var gross = data.Lines.Sum(l => l.Amount.Value + l.Tax.Value);
        var received = 100000 - 10000 + 500000 + 5000;      // payments less refund
        var credits = 400000;                                 // company credit
        var adjusted = 20000;                                 // deposit applied
        var opening = new FolioBalances(0, 0);
        var closing = new FolioBalances(gross - received - adjusted - credits, 50000 - 20000);
        var r = RevenueByCategoryCalculator.Compute(data, cat, D1, D2, RevenueByCategoryCalculator.BusinessDate, opening, closing);
        Assert.Equal(0, r.C.Difference);
        Assert.Equal(closing.DuePaise, r.C.ClosingDue);
        var broken = RevenueByCategoryCalculator.Compute(data, cat, D1, D2, RevenueByCategoryCalculator.BusinessDate, opening, closing with { DuePaise = closing.DuePaise + 100 });
        Assert.Equal(100, broken.C.Difference);
        // The ACC-RULEBOOK April 2026 golden month's trial balance (figures as the prototype test asserts them): guest ledger 0 / 0, advances 10,000 -> 31,500,
        // company credit 63,000 raised on 1110, OTA 21,000 on 1115, Udhaar none.
        var golden = new RevenueByCategoryReport(D1, D2, "business-date", [], r.ATotal, [], new RevenueRowB("total", "Total", 0, 0, 0, 0, 6300000, 0, 2100000, new Dictionary<string, long>(), 3150000, 1000000, 0, 0, 0),
            new RevenueBridge(5460000, 4520000, 1000000, 2150000, 0, 0, 0, 1000000, 3150000, 8400000));
        var tb = new Dictionary<string, LedgerFigures>
        {
            ["1100"] = new(0, 0, 55650000), ["2000"] = new(-1000000, -3150000, 1000000), ["1110"] = new(4000000, 0, 6300000), ["1115"] = new(0, 0, 2100000),
        };
        var rows = RevenueByCategoryCalculator.LedgerRows(golden, code => tb.GetValueOrDefault(code, new LedgerFigures(0, 0, 0)));
        Assert.Equal(7, rows.Count);
        Assert.All(rows, x => Assert.True(x.Ok, $"{x.Account} {x.What}: report {x.Report} vs ledger {x.Ledger}"));
        var off = RevenueByCategoryCalculator.LedgerRows(golden, code => code == "1115" ? new LedgerFigures(0, 0, 1) : tb.GetValueOrDefault(code, new LedgerFigures(0, 0, 0)));
        Assert.False(off.Single(x => x.Account == "1115").Ok);
    }

    [Fact]
    public void Legacy_range_reproduces_the_bill_and_receipt_sums_under_the_legacy_header_and_keeps_an_unmapped_room()
    {
        var bills = new List<LegacyBillRow>
        {
            new("Deluxe", 200000, 10000, 5000, 600, 300, 15000, 24000, 250, 108, 54),
            new(null, 100000, 0, 0, 0, 0, 0, 12000, 0, 0, 0),
        };
        var receipts = new List<LegacyReceiptRow>
        {
            new("Deluxe", "advance", "UPI", 50000), new("Deluxe", "payment", "CASH", 200000), new(null, "security", "CASH", 1000), new("Deluxe", "refund", "CASH", 3000),
        };
        var r = RevenueByCategoryCalculator.ComputeLegacy(new RevenueLegacyData(bills, receipts), D1, D2);
        Assert.True(r.Legacy);
        Assert.Equal("Legacy data (old Lodge software)", RevenueByCategoryReport.LegacyHeader);
        Assert.Equal(300000, r.ATotal.RoomTariff);
        Assert.Equal(10000, r.ATotal.ExtraPerson);
        Assert.Equal(5000 + 600 + 300, r.ATotal.OtherCharges);
        Assert.Equal(-15000, r.ATotal.Discounts);
        Assert.Equal(bills.Sum(b => b.RoomGst + b.FbGst + b.LaundryGst + b.OtherGst), r.ATotal.Cgst + r.ATotal.Sgst);
        Assert.Equal(r.ATotal.Net + r.ATotal.Cgst + r.ATotal.Sgst, r.ATotal.Gross);
        Assert.Equal(r.ATotal.Net, r.A.Sum(x => x.Net));
        Assert.Equal(100000, r.A.Single(x => x.Key == RevenueByCategoryCalculator.Unassigned).RoomTariff);
        Assert.Equal((50000L, 197000L, 1000L, 3000L, 247000L), (r.BTotal.Upi, r.BTotal.Cash, r.BTotal.AdvancesReceived, r.BTotal.Refunds, r.BTotal.NetReceived));
        Assert.Equal(1000, r.B.Single(x => x.Key == RevenueByCategoryCalculator.Unassigned).AdvancesReceived);
    }

    [Fact]
    public void Digest_whatsapp_text_is_counts_only_and_the_email_carries_money()
    {
        var (data, cat) = Sample();
        var facts = new DigestFacts(D2, DailyPackCalculator.Compute(data, D2, D2), RevenueByCategoryCalculator.Compute(data, cat, D2, D2), 3, 2);
        var wa = DigestBodies.WhatsApp(facts);
        Assert.DoesNotContain("₹", wa, StringComparison.Ordinal);
        Assert.Contains("Pending approvals 3", wa, StringComparison.Ordinal);
        Assert.Contains("Arrivals tomorrow 2", wa, StringComparison.Ordinal);
        Assert.Contains("₹", DigestBodies.Email(facts), StringComparison.Ordinal);
    }
}
