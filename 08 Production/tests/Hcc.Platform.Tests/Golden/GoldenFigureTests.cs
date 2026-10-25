using Hcc.Domain.Money;

namespace Hcc.Platform.Tests.Golden;

/// <summary>
/// Golden figures from the prototype BASE fixture, recomputed from raw records with the C# kernel. The constants are the
/// prototype tests' own assertions (03 Prototype/tests/run.mjs, m1.mjs, stream-a2-till.mjs, stream-accounting.mjs), so a
/// regenerated golden file that drifted from them fails here too.
/// </summary>
public class GoldenFigureTests
{
    private const long DrawerPaise = 1_075_000;        // ₹10,750 = ₹10,200 folio/expense cash + ₹550 restaurant cash
    private const long CollectionsPaise = 2_150_000;   // ₹21,500
    private const long Room105InvoicePaise = 440_000;  // ₹4,400

    private static readonly GoldenFixture G = GoldenFixture.Load();

    [Fact]
    public void Golden_file_matches_the_prototype_test_constants()
    {
        Assert.True(G.FictionalDataOnly);
        Assert.Equal(DrawerPaise, G.Base.Expected.DrawerPaise);
        Assert.Equal(CollectionsPaise, G.Base.Expected.CollectionsPaise);
        Assert.Equal(55_000, G.Base.Expected.OutletCashPaise);
        Assert.Equal(Room105InvoicePaise, G.Journey.Expected.TotalPaise);
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
    public void BASE_expected_drawer_is_10750_rupees()
    {
        var drawer = CashFigures.ExpectedDrawer(new Paise(G.Base.OpeningCashPaise), G.Base.Date,
            G.Base.FolioReceipts.Select(r => r.ToDated()), G.Base.Outlet, G.Base.CashExpenses);
        Assert.Equal(DrawerPaise, drawer.Value);
        Assert.Equal("₹10,750", drawer.ToString());

        // Pending and UPI expenses and UPI outlet sales do not touch the drawer.
        var withoutPendingUpi = CashFigures.ExpectedDrawer(new Paise(G.Base.OpeningCashPaise), G.Base.Date,
            G.Base.FolioReceipts.Select(r => r.ToDated()), G.Base.Outlet.Where(o => o.Method == CashFigures.Cash),
            G.Base.CashExpenses.Where(e => e.Status == ExpenseStatus.Posted));
        Assert.Equal(drawer, withoutPendingUpi);
    }

    [Fact]
    public void Room_105_journey_invoice_is_4400_rupees_with_deposit_kept_separate()
    {
        var lines = G.Journey.Lines.Select(l => new FolioLine(new Paise(l.AmountPaise), l.TaxBps)).ToList();
        var receipts = G.Journey.Receipts.Select(r => new FolioReceipt(FolioTotals.ParseKind(r.Kind), new Paise(r.AmountPaise))).ToList();

        var beforeSettlement = FolioTotals.Compute(lines, receipts.Take(2));
        Assert.Equal(G.Journey.Expected.BalanceBeforeSettlementPaise, beforeSettlement.Balance.Value); // ₹3,400; ₹500 deposit does not reduce it
        Assert.Equal(50_000, beforeSettlement.Deposit.Value);

        var t = FolioTotals.Compute(lines, receipts);
        var e = G.Journey.Expected;
        Assert.Equal((e.NetPaise, e.TaxPaise, e.TotalPaise, e.PaidPaise, e.DepositPaise, e.BalancePaise),
            (t.Net.Value, t.Tax.Value, t.Total.Value, t.Paid.Value, t.Deposit.Value, t.Balance.Value));
        Assert.Equal(Room105InvoicePaise, t.Total.Value);
        Assert.Equal("₹4,400", t.Total.ToString());
    }
}
