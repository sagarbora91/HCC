using Hcc.Accounting.Tests.Golden;
using Hcc.Application.Accounting;
using Hcc.Domain.Accounting;

namespace Hcc.Accounting.Tests.Pl;

/// <summary>ACC-3 lane acc3-pl: P&amp;L, balance sheet and cash ratio over the rulebook golden month (rulebook §8.4, §8.5; contract §2).</summary>
public sealed class PlReportsTests
{
    private static readonly DateOnly From = new(2026, 4, 1), To = new(2026, 4, 30);

    [Fact]
    [Trait("Lane", "acc3-pl")]
    public void April_2026_profit_and_loss_is_the_rulebook_84()
    {
        var (_, period) = AccountingReports.Split(RulebookMonthTests.FullMonthVouchers(), From, To);
        var p = PlReports.Pl(From, To, period);
        Assert.Equal(58_000_000, p.Revenue.Total);
        Assert.Equal(10_100_000, p.Expenses.Consumption.Total);
        Assert.Equal(26_450_000, p.Expenses.Employee.Total);
        Assert.Equal(1_736_000, p.Expenses.Finance.Total);
        Assert.Equal(5_674_000, p.Expenses.Other.Total);
        Assert.Equal(0, p.Expenses.Depreciation.Total);
        Assert.Equal(14_040_000, p.ProfitBeforeTax);
        Assert.Equal(-1_000_000, p.Revenue.Lines.Single(l => l.Code == "4900").Paise);
        Assert.Equal(["5030", "5040", "5070", "5900"], p.Expenses.Other.Lines.Select(l => l.Code));
        Assert.Equal(58_000_000, p.ByTallyGroup.Single(g => g.Group == "sales").Paise);
        Assert.Null(p.Warnings);
    }

    [Fact]
    [Trait("Lane", "acc3-pl")]
    public void Balance_sheet_30_April_2026_is_79_27_900_on_both_sides()
    {
        var b = PlReports.BalanceSheet(To, RulebookMonthTests.FullMonthVouchers());
        Assert.True(b.Balanced);
        Assert.Equal(792_790_000, b.Liabilities.Total);
        Assert.Equal(792_790_000, b.Assets.Total);
        Assert.Equal(579_540_000, b.Liabilities.OwnersFunds.Total);
        Assert.Equal(196_000_000, b.Liabilities.NonCurrent.Total);
        Assert.Equal(17_250_000, b.Liabilities.Current.Total);
        Assert.Equal(738_000_000, b.Assets.NonCurrent.Total);
        Assert.Equal(10_000, b.Assets.Current.Lines.Single(l => l.Code == "1340").Paise);
        Assert.Equal(0, b.Suspense);
        Assert.Equal(0, b.OpeningDifference);
        Assert.False(b.Provisional);
    }

    [Fact]
    [Trait("Lane", "acc3-pl")]
    public void Balance_sheet_balances_on_every_day_of_April()
    {
        var vs = RulebookMonthTests.FullMonthVouchers();
        for (var d = From; d <= To; d = d.AddDays(1)) Assert.True(PlReports.BalanceSheet(d, vs).Balanced, d.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait("Lane", "acc3-pl")]
    public void Cash_ratio_in_basis_points_flagged_above_five_percent()
    {
        var (_, period) = AccountingReports.Split(RulebookMonthTests.FullMonthVouchers(), From, new DateOnly(2027, 3, 31));
        var c = PlReports.CashRatio("2026-27", period);
        Assert.True(c.TotalReceipts > 0);
        Assert.Equal((int)Math.Round(c.CashReceipts * 10000m / c.TotalReceipts, MidpointRounding.AwayFromZero), c.ReceiptsPct);
        Assert.Equal(c.ReceiptsPct > 500 || c.PaymentsPct > 500, c.Flagged);
        var z = PlReports.CashRatio("2019-20", []);
        Assert.Equal((0, 0, false), (z.TotalReceipts, z.ReceiptsPct, z.Flagged));
        var cash = new Voucher("R1", From, VoucherType.Receipt, "x", "k", "receipt", "s", [new("1000", 600, 0), new("1100", 0, 600)], []);
        var bank = new Voucher("R2", From, VoucherType.Receipt, "x", "k", "receipt", "s", [new("1030/BA-HDFC", 9400, 0), new("1100", 0, 9400)], []);
        var r = PlReports.CashRatio("2026-27", [cash, bank]);
        Assert.Equal((600L, 10_000L, 600, true), (r.CashReceipts, r.TotalReceipts, r.ReceiptsPct, r.Flagged));
    }

    [Fact]
    [Trait("Lane", "acc3-pl")]
    public void Owner_added_ledger_without_a_bucket_goes_to_other_with_a_warning_and_fy_helpers()
    {
        var v = new Voucher("J1", From, VoucherType.Journal, "x", "k", "manual", "s", [new("9999", 500, 0), new("1000", 0, 500)], []);
        var p = PlReports.Pl(From, To, [v]);
        Assert.Equal(0, p.ProfitBeforeTax); // suspense is a balance-sheet ledger, not P&L
        Assert.Equal(new DateOnly(2025, 4, 1), PlReports.FyStart(new DateOnly(2026, 3, 31)));
        Assert.Equal("2026-27", PlReports.FyLabel(new DateOnly(2026, 4, 1)));
        Assert.Throws<Hcc.Domain.Errors.HccException>(() => PlReports.FyRange("abc"));
    }
}
