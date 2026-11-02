using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Stays;

namespace Hcc.Stays.Tests.Kernel;

/// <summary>LF lf-desk rules (A4 vehicle, A5 Form C, A6 due-out / late check-out); same cases as the prototype's tests/stream-lf-desk.mjs. No database.</summary>
public class DeskFieldTests
{
    private static readonly DateOnly Arrival = new(2026, 10, 24);
    private static HccException Fail(Action a) => Assert.Throws<HccException>(a);

    private static FormCFacts FormC(Func<FormCFacts, FormCFacts>? tweak = null)
    {
        var f = new FormCFacts("Berlin", new(2022, 3, 1), new(2032, 2, 28), "V9988776", "Tourist", "Berlin", new(2026, 9, 1), new(2027, 3, 1),
            "Frankfurt", new(2026, 10, 20), 10, "Goa", false, null, "1 Example Str, Berlin (fictional)", null, 'F');
        return tweak is null ? f : tweak(f);
    }

    [Theory]
    [InlineData(" mh24 ab  1234 ", "MH24 AB 1234")]
    [InlineData("ka01-xy9999", "KA01-XY9999")]
    public void Vehicle_numbers_are_upper_cased_with_collapsed_spaces(string raw, string expected) => Assert.Equal(expected, DeskFieldRules.NormaliseVehicle(raw));

    [Theory]
    [InlineData("AB")]
    [InlineData("")]
    [InlineData("MH24/AB1234")]
    [InlineData("AAAAAAAAAAAAAA")]
    public void Bad_vehicle_numbers_are_refused(string raw) => Assert.Equal(HccErrorCode.VALIDATION, Fail(() => DeskFieldRules.NormaliseVehicle(raw)).Code);

    [Fact]
    public void Vehicle_search_key_ignores_spaces_and_hyphens() => Assert.Equal("KA01XY9999", DeskFieldRules.VehicleKey("ka01-xy 9999"));

    [Theory]
    [InlineData("Indian", false)]
    [InlineData(" india ", false)]
    [InlineData("German", true)]
    [InlineData("", false)]
    public void Foreign_means_a_filled_in_nationality_that_is_not_Indian(string nationality, bool foreign) => Assert.Equal(foreign, DeskFieldRules.IsForeign(nationality));

    [Fact]
    public void A_complete_form_c_passes_and_each_rule_has_its_message()
    {
        DeskFieldRules.ValidateFormC(FormC(), Arrival, true, true);
        Assert.Contains("Passport", Fail(() => DeskFieldRules.ValidateFormC(FormC(), Arrival, false, true)).Message);
        Assert.Contains("date of birth", Fail(() => DeskFieldRules.ValidateFormC(FormC(), Arrival, true, false)).Message);
        Assert.Contains("passport expires on or before", Fail(() => DeskFieldRules.ValidateFormC(FormC(f => f with { PassportExpiry = Arrival }), Arrival, true, true)).Message);
        Assert.Contains("visa expires on or before", Fail(() => DeskFieldRules.ValidateFormC(FormC(f => f with { VisaExpiry = new(2026, 10, 20) }), Arrival, true, true)).Message);
        Assert.Contains("visa expiry must be after", Fail(() => DeskFieldRules.ValidateFormC(FormC(f => f with { VisaExpiry = new(2026, 8, 1) }), Arrival, true, true)).Message);
        Assert.Contains("at least 1 day", Fail(() => DeskFieldRules.ValidateFormC(FormC(f => f with { IntendedStayDays = 0 }), Arrival, true, true)).Message);
        Assert.Contains("cannot be after", Fail(() => DeskFieldRules.ValidateFormC(FormC(f => f with { ArrivalInIndia = new(2026, 11, 1) }), Arrival, true, true)).Message);
        Assert.Contains("visa number", Fail(() => DeskFieldRules.ValidateFormC(FormC(f => f with { VisaNo = "" }), Arrival, true, true)).Message);
        Assert.Contains("sex", Fail(() => DeskFieldRules.ValidateFormC(FormC(f => f with { Sex = '?' }), Arrival, true, true)).Message);
        Assert.Contains("employer or business", Fail(() => DeskFieldRules.ValidateFormC(FormC(f => f with { EmployedInIndia = true }), Arrival, true, true)).Message);
    }

    [Fact]
    public void The_visa_number_is_masked_to_its_last_four_without_the_id_details_right()
    {
        Assert.Equal("V9988776", DeskFieldRules.VisaForViewer("V9988776", true));
        Assert.Equal("••••8776", DeskFieldRules.VisaForViewer("V9988776", false));
        Assert.Equal("••••", DeskFieldRules.VisaForViewer("V99", false));
    }

    [Fact]
    public void Due_out_is_the_checkout_time_for_fixed_and_24_hours_per_night_from_check_in_for_24h()
    {
        var co = new TimeOnly(11, 0); var ci = new TimeOnly(14, 0);
        Assert.Equal(new DateTime(2026, 10, 26, 11, 0, 0), DeskFieldRules.DueOut("fixed", new(2026, 10, 24), new(2026, 10, 26), co, ci, new DateTime(2026, 10, 24, 9, 30, 0)));
        Assert.Equal(new DateTime(2026, 10, 26, 9, 30, 0), DeskFieldRules.DueOut("24h", new(2026, 10, 24), new(2026, 10, 26), co, ci, new DateTime(2026, 10, 24, 9, 30, 0)));
        Assert.Equal(new DateTime(2026, 10, 27, 9, 30, 0), DeskFieldRules.DueOut("24h", new(2026, 10, 24), new(2026, 10, 27), co, ci, new DateTime(2026, 10, 24, 9, 30, 0)));
        Assert.Equal(new DateTime(2026, 10, 26, 14, 0, 0), DeskFieldRules.DueOut("24h", new(2026, 10, 24), new(2026, 10, 26), co, ci, null)); // old stay: arrival + check-in time
    }

    [Fact]
    public void Late_check_out_starts_after_the_grace_and_the_half_day_rounds_half_away_from_zero()
    {
        var due = new DateTime(2026, 10, 25, 11, 0, 0);
        Assert.False(DeskFieldRules.LateCheckout(due, new(2026, 10, 25, 11, 30, 0), 1, 200000, 50).Late);
        Assert.False(DeskFieldRules.LateCheckout(due, new(2026, 10, 25, 12, 0, 0), 1, 200000, 50).Late);
        var l = DeskFieldRules.LateCheckout(due, new(2026, 10, 25, 13, 15, 0), 1, 200000, 50);
        Assert.True(l.Late);
        Assert.Equal(135, l.MinutesPast);
        Assert.Equal("Guest is past the due-out time by 2:15 — post the late check-out charge (half day) or extend the stay first.", l.Message);
        Assert.Equal(100000, l.HalfDayPaise);
        Assert.Equal(2, DeskFieldRules.LateCheckout(due, due, 1, 3, 50).HalfDayPaise);
        Assert.False(DeskFieldRules.LateCheckout(due, new(2026, 10, 25, 13, 15, 0), 3, 200000, 50).Late);
    }

    [Fact]
    public void Early_check_in_warns_only_on_fixed_stays_before_the_check_in_time_minus_the_grace()
    {
        var ci = new TimeOnly(14, 0);
        Assert.True(DeskFieldRules.IsEarlyCheckin("fixed", new(2026, 10, 24, 9, 0, 0), ci, 1));
        Assert.False(DeskFieldRules.IsEarlyCheckin("fixed", new(2026, 10, 24, 13, 30, 0), ci, 1));
        Assert.False(DeskFieldRules.IsEarlyCheckin("24h", new(2026, 10, 24, 9, 0, 0), ci, 1));
    }
}
