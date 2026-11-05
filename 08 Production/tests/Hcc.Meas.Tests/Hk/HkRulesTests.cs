using Hcc.Operations.Meas;

namespace Hcc.Meas.Tests;

/// <summary>[F12-CAP-HK] The housekeeping / engineering capture rules (twins of tests/stream-meas-hk.mjs). Needs a project reference to
/// Hcc.Operations in the project meas-settings creates (see MEAS-CAP-HK-DONE.md, Integration needed).</summary>
public sealed class HkRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);
    private static DateOnly D(int offset) => Today.AddDays(offset);
    private static HkReading R(int offset, decimal v) => new(D(offset), v);

    [Fact]
    public void F12_CAP_HK_reading_equal_or_higher_than_the_last_is_accepted()
    {
        var ex = new[] { R(-2, 100m), R(-1, 130m) };
        Assert.True(HkRules.CheckReading(ex, D(0), 130m, Today).Ok);
        Assert.True(HkRules.CheckReading(ex, D(0), 142.5m, Today).Ok);
        Assert.True(HkRules.CheckReading([], D(0), 0m, Today).Ok);
    }

    [Fact]
    public void F12_CAP_HK_reading_below_the_last_is_not_monotonic()
    {
        var r = HkRules.CheckReading([R(-1, 130m)], D(0), 129.99m, Today);
        Assert.False(r.Ok);
        Assert.Equal(HkRules.ErrNotMonotonic, r.Code);
    }

    [Fact]
    public void F12_CAP_HK_back_dated_reading_must_fit_between_its_neighbours()
    {
        var ex = new[] { R(-5, 100m), R(-1, 130m) };
        Assert.True(HkRules.CheckReading(ex, D(-3), 110m, Today).Ok);
        Assert.Equal(HkRules.ErrNotMonotonic, HkRules.CheckReading(ex, D(-3), 99m, Today).Code);
        Assert.Equal(HkRules.ErrNotMonotonic, HkRules.CheckReading(ex, D(-3), 131m, Today).Code);
    }

    [Fact]
    public void F12_CAP_HK_reading_validation_value_date_and_switched_off_meter()
    {
        Assert.Equal(HkRules.ErrValidation, HkRules.CheckReading([], D(0), -1m, Today).Code);
        Assert.Equal(HkRules.ErrValidation, HkRules.CheckReading([], D(0), 1.234m, Today).Code);
        Assert.Equal(HkRules.ErrValidation, HkRules.CheckReading([], D(1), 1m, Today).Code);
        Assert.Equal(HkRules.ErrValidation, HkRules.CheckReading([], D(0), 1m, Today, meterActive: false).Code);
    }

    [Fact]
    public void F12_CAP_HK_consumption_is_the_difference_between_consecutive_readings()
    {
        var c = HkRules.Consumption([R(-1, 130m), R(-5, 100m), R(-3, 110m)]);
        Assert.Equal([(D(-3), 10m), (D(-1), 20m)], c);
    }

    [Fact]
    public void F12_CAP_HK_offline_reason_return_date_and_note()
    {
        Assert.True(HkRules.CheckOffline("OOO", D(2), "AC", Today).Ok);
        Assert.True(HkRules.CheckOffline("house-use", null, null, Today).Ok);
        Assert.False(HkRules.CheckOffline("broken", null, null, Today).Ok);
        Assert.False(HkRules.CheckOffline("OOS", D(-1), null, Today).Ok);
        Assert.False(HkRules.CheckOffline("OOS", null, new string('x', 201), Today).Ok);
    }

    [Fact]
    public void F12_CAP_HK_asset_life_bounds_and_integer_paise()
    {
        Assert.True(HkRules.CheckAssetLife(96, "B", 4500000L).Ok);
        Assert.False(HkRules.CheckAssetLife(null, null, null).Ok);
        Assert.False(HkRules.CheckAssetLife(0, null, null).Ok);
        Assert.False(HkRules.CheckAssetLife(601, null, null).Ok);
        Assert.False(HkRules.CheckAssetLife(null, "E", null).Ok);
        Assert.False(HkRules.CheckAssetLife(null, null, -1L).Ok);
    }

    [Fact]
    public void F12_CAP_HK_discard_bounds()
    {
        Assert.True(HkRules.CheckDiscard("Bath towel", 6, "Worn out", D(0), Today).Ok);
        Assert.False(HkRules.CheckDiscard("", 6, "Worn out", D(0), Today).Ok);
        Assert.False(HkRules.CheckDiscard("Towel", 0, "Worn out", D(0), Today).Ok);
        Assert.False(HkRules.CheckDiscard("Towel", 1001, "Worn out", D(0), Today).Ok);
        Assert.False(HkRules.CheckDiscard("Towel", 1, "x", D(0), Today).Ok);
        Assert.False(HkRules.CheckDiscard("Towel", 1, "Torn", D(1), Today).Ok);
    }

    [Theory]
    [InlineData("Owner", true, true, true, true, true, true)]
    [InlineData("Manager", true, true, true, false, true, true)]
    [InlineData("Supervisor", true, false, false, false, true, false)]
    [InlineData("Maintenance", false, false, true, false, false, false)]
    [InlineData("Housekeeper", false, false, false, false, false, false)]
    [InlineData("Accountant", false, false, false, true, false, true)]
    [InlineData("Reception", false, false, false, false, false, false)]
    public void F12_CAP_HK_role_matrix_and_dec061_asset_cost_mask(string role, bool offline, bool meter, bool reading, bool assetLife, bool discard, bool seesCost)
    {
        Assert.Equal(offline, HkRules.CanSetOffline(role));
        Assert.Equal(meter, HkRules.CanSaveMeter(role));
        Assert.Equal(reading, HkRules.CanAddReading(role));
        Assert.Equal(assetLife, HkRules.CanSetAssetLife(role));
        Assert.Equal(discard, HkRules.CanAddDiscard(role));
        Assert.Equal(seesCost, HkRules.CanSeeAssetCost(role));
    }
}
