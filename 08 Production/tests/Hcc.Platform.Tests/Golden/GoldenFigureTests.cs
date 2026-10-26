using Hcc.Domain.Dates;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Platform.Tests.Golden;

/// <summary>
/// Golden figures from the prototype BASE fixture, recomputed from raw records with the C# kernel. The constants are the
/// prototype tests' own assertions (03 Prototype/tests/run.mjs, m1.mjs, stream-a2-till.mjs, stream-accounting.mjs), so a
/// regenerated golden file that drifted from them fails here too. Since A08-04 the golden carries the DEC-069 GST master figures
/// (it was a frozen DEC-039 10 % snapshot: drawer ₹10,750, Room 105 ₹4,400); 03 Prototype `npm test` fails when it drifts again.
/// </summary>
public class GoldenFigureTests
{
    // ₹10,725 by hand: opening float ₹5,000 + cash payments (HCC-1041 ₹2,000 + HCC-1044 ₹2,000 + HCC-1047 ₹2,000 + HCC-1050 ₹1,000 = ₹7,000)
    // − posted cash expense EXP-101 ₹1,800 + restaurant cash bill ₹525 (₹500 food + 5 % GST ₹25) = ₹10,725. UPI receipts, the UPI
    // outlet bill and the pending UPI expense EXP-102 do not touch the drawer. (DEC-039 had ₹550 = ₹500 + 10 %, so ₹10,750.)
    private const long DrawerPaise = 1_072_500;
    private const long CollectionsPaise = 2_150_000;   // ₹21,500 = ₹7,000 cash + ₹14,500 UPI folio payments (no tax involved)
    private const long OutletCashPaise = 52_500;       // ₹525 = ₹500 + 5 % restaurant GST (not specified premises)

    // ₹4,265 by hand: 2 nights × ₹2,000 = ₹4,000 at 5 % (≤ ₹7,500 per unit per night) = ₹200; laundry ₹500 at 18 % = ₹90;
    // discount −₹500 follows the room-night rate 5 % = −₹25. Net ₹4,000 + tax ₹265 = ₹4,265. Before settlement: ₹4,265 − ₹1,000
    // advance = ₹3,265 (the ₹500 deposit does not reduce it). (DEC-039: everything at 10 % → tax ₹400, total ₹4,400, balance ₹3,400.)
    private const long Room105InvoicePaise = 426_500;
    private const long Room105TaxPaise = 26_500;
    private const long Room105BalanceBeforeSettlementPaise = 326_500;

    private static readonly GoldenFixture G = GoldenFixture.Load();

    [Fact]
    public void Golden_file_matches_the_prototype_test_constants()
    {
        Assert.True(G.FictionalDataOnly);
        Assert.Equal(DrawerPaise, G.Base.Expected.DrawerPaise);
        Assert.Equal(CollectionsPaise, G.Base.Expected.CollectionsPaise);
        Assert.Equal(OutletCashPaise, G.Base.Expected.OutletCashPaise);
        Assert.Equal(Room105InvoicePaise, G.Journey.Expected.TotalPaise);
        Assert.Equal(Room105TaxPaise, G.Journey.Expected.TaxPaise);
        Assert.Equal(Room105BalanceBeforeSettlementPaise, G.Journey.Expected.BalanceBeforeSettlementPaise);
        Assert.Equal("105", G.Journey.Room);
    }

    [Fact]
    public void BASE_collections_are_21500_rupees()
    {
        var collections = CashFigures.Collections(G.Base.FolioReceipts.Select(r => r.ToDated()), G.Base.Date);
        Assert.Equal(CollectionsPaise, collections.Value);
        Assert.Equal("₹21,500", collections.ToString());
    }

    [Fact]
    public void BASE_expected_drawer_is_10725_rupees()
    {
        var drawer = CashFigures.ExpectedDrawer(new Paise(G.Base.OpeningCashPaise), G.Base.Date,
            G.Base.FolioReceipts.Select(r => r.ToDated()), G.Base.Outlet, G.Base.CashExpenses);
        Assert.Equal(DrawerPaise, drawer.Value);
        Assert.Equal("₹10,725", drawer.ToString());

        // Pending and UPI expenses and UPI outlet sales do not touch the drawer.
        var withoutPendingUpi = CashFigures.ExpectedDrawer(new Paise(G.Base.OpeningCashPaise), G.Base.Date,
            G.Base.FolioReceipts.Select(r => r.ToDated()), G.Base.Outlet.Where(o => o.Method == CashFigures.Cash),
            G.Base.CashExpenses.Where(e => e.Status == ExpenseStatus.Posted));
        Assert.Equal(drawer, withoutPendingUpi);
    }

    [Fact]
    public void BASE_restaurant_bills_carry_the_food_rate_of_the_server_GST_master()
    {
        // The prototype taxed each outlet bill with its GST master; the server's statutory seed gives the same rate and the same paise.
        var master = GstMaster.Seed();
        Assert.Equal(2, G.Base.OutletReceipts.Count);
        foreach (var o in G.Base.OutletReceipts)
        {
            var food = master.Food(StayDates.Parse(o.Date), master.SpecifiedPremises(StayDates.Parse(o.Date), 0)); // BASE: no unit above ₹7,500 last FY
            Assert.Equal(food.Bps, o.TaxBps);
            Assert.Equal(o.TaxPaise, GstMath.LineTax(new Paise(o.NetPaise!.Value), food.Bps).Value);
            Assert.Equal(o.AmountPaise, o.NetPaise.Value + o.TaxPaise!.Value);
        }

        Assert.Equal((50_000L, 2_500L, 500), (G.Base.OutletReceipts[0].NetPaise!.Value, G.Base.OutletReceipts[0].TaxPaise!.Value, G.Base.OutletReceipts[0].TaxBps!.Value));
    }

    [Fact]
    public void Room_105_journey_invoice_is_4265_rupees_with_deposit_kept_separate()
    {
        var lines = G.Journey.Lines.Select(l => new FolioLine(new Paise(l.AmountPaise), l.TaxBps)).ToList();
        var receipts = G.Journey.Receipts.Select(r => new FolioReceipt(FolioTotals.ParseKind(r.Kind), new Paise(r.AmountPaise))).ToList();

        var beforeSettlement = FolioTotals.Compute(lines, receipts.Take(2));
        Assert.Equal(G.Journey.Expected.BalanceBeforeSettlementPaise, beforeSettlement.Balance.Value); // ₹3,265; ₹500 deposit does not reduce it
        Assert.Equal(Room105BalanceBeforeSettlementPaise, beforeSettlement.Balance.Value);
        Assert.Equal(50_000, beforeSettlement.Deposit.Value);

        var t = FolioTotals.Compute(lines, receipts);
        var e = G.Journey.Expected;
        Assert.Equal((e.NetPaise, e.TaxPaise, e.TotalPaise, e.PaidPaise, e.DepositPaise, e.BalancePaise),
            (t.Net.Value, t.Tax.Value, t.Total.Value, t.Paid.Value, t.Deposit.Value, t.Balance.Value));
        Assert.Equal(Room105InvoicePaise, t.Total.Value);
        Assert.Equal(Room105TaxPaise, t.Tax.Value);
        Assert.Equal("₹4,265", t.Total.ToString());
    }

    [Fact]
    public void Room_105_journey_lines_carry_the_rates_of_the_server_GST_master()
    {
        // Server = prototype on the rates themselves: room nights by the per-unit-per-night slab, laundry by its service rate, the
        // discount by the room-night rate it reduces (prototype gstForFolioAdjustment / server AdjustmentRateAsync).
        var master = GstMaster.Seed();
        foreach (var l in G.Journey.Lines)
        {
            var date = StayDates.Parse(l.Date);
            var expected = l.Kind switch
            {
                "room" => master.RoomNight(date, new Paise(l.AmountPaise)).Bps,
                "service" => master.Service(date, GstCategories.ForCharge(null, l.Label)).Bps,
                "discount" => G.Journey.Lines.First(x => x.Kind == "room" && x.Date == l.Date).TaxBps,
                _ => throw new InvalidOperationException("Unexpected line kind " + l.Kind),
            };
            Assert.True(expected == l.TaxBps, $"{l.Kind} {l.Label}: server master {expected} bps, prototype {l.TaxBps} bps");
        }

        Assert.Equal(["room|500", "room|500", "service|1800", "discount|500"], G.Journey.Lines.Select(l => $"{l.Kind}|{l.TaxBps}"));

        // CGST / SGST per line (A03b probe §4): ₹50 + ₹50 per night, ₹45 + ₹45 laundry, −₹12.50 − ₹12.50 discount = ₹132.50 each.
        var split = GstMath.SplitLines(G.Journey.Lines.Select(l => (new Paise(l.AmountPaise), l.TaxBps)));
        Assert.Equal((Room105TaxPaise, 13_250L, 13_250L), (split.Tax.Value, split.Cgst.Value, split.Sgst.Value));
    }
}
