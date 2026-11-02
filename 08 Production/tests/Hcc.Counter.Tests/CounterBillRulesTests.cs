using Hcc.Domain.Counter;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Counter.Tests;

/// <summary>LF A7 kernel rules — the prototype's tests/stream-lf-counter.mjs figures: laundry 2 × ₹500 at 18 %, chai 3 × ₹60 at 5 %, courier ₹150 at 18 %.</summary>
public class CounterBillRulesTests
{
    private static readonly CounterLine[] Lines =
    [
        new(1, "Laundry", "ext-laundry", null, 2, new Paise(50_000), GstCategory.Laundry, 1800),
        new(2, "Masala chai", null, "m-tea", 3, new Paise(6_000), GstCategory.Food, 500),
        new(3, "Courier", null, null, 1, new Paise(15_000), GstCategory.Other, 1800),
    ];

    [Fact]
    public void Totals_tax_per_line_and_sac()
    {
        var t = CounterBillRules.Totals(Lines, Paise.Zero);
        Assert.Equal(new Paise(133_000), t.Net);
        Assert.Equal(new Paise(18_000 + 900 + 2_700), t.Tax);
        Assert.Equal(t.Tax, t.Cgst + t.Sgst);
        Assert.Equal(t.Net + t.Tax, t.Total);
        Assert.Equal(["999712", "996331", "999799"], Lines.Select(l => l.Sac).ToArray());
    }

    [Fact]
    public void Discount_is_a_negative_line_at_the_dominant_rate_within_the_role_limit()
    {
        var t = CounterBillRules.Totals(Lines, new Paise(25_000));
        Assert.Equal(new Paise(108_000), t.Net);
        Assert.Equal(new Paise(21_600 - 4_500), t.Tax);
        Assert.Contains("not at the cash counter", CounterBillRules.DiscountRefusal(false, Paise.Zero, new Paise(100), new Paise(133_000)));
        Assert.Contains("limit", CounterBillRules.DiscountRefusal(true, new Paise(25_000), new Paise(25_001), new Paise(133_000)));
        Assert.Null(CounterBillRules.DiscountRefusal(true, new Paise(25_000), new Paise(25_000), new Paise(133_000)));
        Assert.Null(CounterBillRules.DiscountRefusal(true, null, new Paise(120_000), new Paise(133_000)));
        Assert.Equal("The discount cannot exceed the bill.", CounterBillRules.DiscountRefusal(true, null, new Paise(133_001), new Paise(133_000)));
    }

    [Theory]
    [InlineData("1000")]
    [InlineData("1120/UD-9000011111")]
    [InlineData("1110/CO-1")]
    public void The_sales_voucher_balances_for_every_settlement(string side)
    {
        var d = new Paise(25_000);
        var t = CounterBillRules.Totals(Lines, d);
        var e = CounterBillRules.Entries(side, Lines, d, t);
        Assert.Equal(0, e.Sum(x => x.Amount.Value));
        Assert.Equal(t.Total, e.Single(x => x.Ledger == side).Amount);
        Assert.Equal(new Paise(-18_000), e.Single(x => x.Ledger == "4200").Amount);
        Assert.Equal(new Paise(-115_000), e.Single(x => x.Ledger == "4100").Amount);
        Assert.Equal(d, e.Single(x => x.Ledger == "4900").Amount);
    }

    [Fact]
    public void Lines_and_classes()
    {
        Assert.Null(CounterBillRules.LineRefusal(1, "Tea", 1, new Paise(1)));
        Assert.StartsWith("Line 2: Quantity", CounterBillRules.LineRefusal(2, "Tea", 100, new Paise(1)));
        Assert.StartsWith("Line 1: Enter the unit price", CounterBillRules.LineRefusal(1, "Tea", 1, Paise.Zero));
        Assert.Equal("other", CounterBillRules.UdhaarKind(Lines));
        Assert.Equal("restaurant", CounterBillRules.UdhaarKind([Lines[1]]));
        Assert.Equal("4000", CounterBillRules.RevenueLedger(GstCategory.Room));
    }
}
