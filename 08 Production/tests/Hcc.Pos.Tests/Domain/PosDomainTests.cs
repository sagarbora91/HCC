using Hcc.Domain.Money;
using Hcc.Domain.Pos;

namespace Hcc.Pos.Tests.Domain;

/// <summary>Pure POS rules with the prototype's golden figures (03 Prototype/tests/stream-pos.mjs, DEC-069 restaurant GST 5 %).</summary>
public class PosDomainTests
{
    private static PosLineAmount L(long rupees, int qty) => new(Paise.FromRupees(rupees), qty);

    [Fact]
    public void Fixture_check_F01_is_500_plus_25_equals_525()
    {
        var t = PosRules.Totals([L(150, 2), L(60, 2), L(80, 1)], 500);
        Assert.Equal((50_000, 2_500, 52_500, 5), (t.Net.Value, t.Tax.Value, t.Total.Value, t.Items));
    }

    [Fact]
    public void Fixture_check_F02_is_680_plus_34_equals_714()
    {
        var t = PosRules.Totals([L(220, 2), L(160, 1), L(80, 1)], 500);
        Assert.Equal((68_000, 3_400, 71_400), (t.Net.Value, t.Tax.Value, t.Total.Value));
    }

    [Fact]
    public void Dal_plus_two_roti_check_is_360_plus_18_equals_378()
    {
        var t = PosRules.Totals([L(280, 1), L(40, 2)], 500);
        Assert.Equal((36_000, 1_800, 37_800), (t.Net.Value, t.Tax.Value, t.Total.Value));
    }

    [Fact]
    public void Room_posting_golden_lassi_paratha_tea_is_440_plus_22_equals_462()
    {
        var t = PosRules.Totals([L(240, 1), L(60, 1), L(140, 1)], 500);
        Assert.Equal((44_000, 2_200, 46_200), (t.Net.Value, t.Tax.Value, t.Total.Value));
    }

    [Fact]
    public void Tax_rounds_half_away_from_zero()
    {
        // net 90 paise at 5 % = 4.5 paise -> 5
        var t = PosRules.Totals([new PosLineAmount(new Paise(90), 1)], 500);
        Assert.Equal(5, t.Tax.Value);
        Assert.Equal(95, t.Total.Value);
    }

    [Fact]
    public void Same_figures_with_the_18_percent_rate_for_specified_premises()
    {
        var t = PosRules.Totals([L(280, 1), L(40, 2)], 1800);
        Assert.Equal((36_000, 6_480, 42_480), (t.Net.Value, t.Tax.Value, t.Total.Value));
    }

    [Theory]
    [InlineData(50_000, false)]
    [InlineData(50_001, true)]
    [InlineData(1, false)]
    public void Void_limit_is_500_rupees_inclusive(long paise, bool needsApproval) => Assert.Equal(needsApproval, PosRules.NeedsApproval(paise));

    [Fact]
    public void Bill_numbers_follow_year_month_and_property_sequence()
    {
        Assert.Equal("POS/2610/0003", PosRules.BillNo(new DateOnly(2026, 10, 24), 3));
        Assert.Equal("POS/2701/0120", PosRules.BillNo(new DateOnly(2027, 1, 2), 120));
    }

    [Fact]
    public void Kot_flow_is_forward_only_and_served_is_final()
    {
        Assert.Equal("Ready", PosRules.NextKotStatus("Preparing"));
        Assert.Equal("Served", PosRules.NextKotStatus("Ready"));
        Assert.Null(PosRules.NextKotStatus("Served"));
    }

    [Fact]
    public void Kot_label_uses_the_first_two_letters_of_the_outlet()
    {
        Assert.Equal("RE-KOT 3", PosRules.KotLabel("Restaurant", 3));
        Assert.Equal("RO-KOT 1", PosRules.KotLabel("Room service", 1));
    }
}
