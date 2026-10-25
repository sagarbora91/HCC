using Hcc.Domain.Errors;
using Hcc.Domain.Handover;

namespace Hcc.Handover.Tests.Kernel;

/// <summary>Pure rules ported from the prototype (no database).</summary>
public class HandoverRulesTests
{
    private static DateTime L(string s) => DateTime.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Shift_windows_and_night_roll_over()
    {
        Assert.Equal("Morning", HandoverRules.ShiftAt(L("2026-10-24T08:00")));
        Assert.Equal("Evening", HandoverRules.ShiftAt(L("2026-10-24T15:00")));
        Assert.Equal("Night", HandoverRules.ShiftAt(L("2026-10-24T23:10")));
        Assert.Equal("Night", HandoverRules.ShiftAt(L("2026-10-25T02:00")));
        Assert.Equal(L("2026-10-25T00:30"), HandoverRules.At(new DateOnly(2026, 10, 24), "Night", "00:30"));
        Assert.Equal(L("2026-10-24T23:30"), HandoverRules.At(new DateOnly(2026, 10, 24), "Night", "23:30"));
        Assert.Equal(L("2026-10-25T07:00"), HandoverRules.ShiftEnd(new DateOnly(2026, 10, 24), "Night"));
        Assert.Equal("Evening", HandoverRules.NextShift("Morning"));
        Assert.Equal("Morning", HandoverRules.NextShift("Night"));
    }

    [Fact]
    public void Property_clock_is_IST()
    {
        var local = HandoverRules.ToLocal(new DateTime(2026, 10, 24, 2, 30, 0, DateTimeKind.Utc));
        Assert.Equal(L("2026-10-24T08:00"), local);
    }

    [Fact]
    public void Desk_membership_and_template_use()
    {
        Assert.Equal(["Housekeeping", "Maintenance"], HandoverRules.DesksFor(["SUP"], false));
        Assert.Equal(["Housekeeping"], HandoverRules.DesksFor(["HKP"], false));
        Assert.Equal(3, HandoverRules.DesksFor(["OWN"], true).Count);
        Assert.False(HandoverRules.CanUseTemplate(["HKP"], false, ["REC"]));
        Assert.True(HandoverRules.CanUseTemplate(["REC"], false, ["CSH", "REC"]));
        Assert.True(HandoverRules.CanUseTemplate(["MGR"], true, ["REC"]));
    }

    [Fact]
    public void Template_validation_follows_R2()
    {
        (string?, bool, string?, bool, string?)[] ok = [("Skim the pool", true, "16:00", false, null)];
        var t = HandoverRules.ValidateTemplate("Pool deck check", "Maintenance", "Evening", ["mnt", "MNT"], ok);
        Assert.Equal(["MNT"], t.Roles);
        Assert.Equal("i1", t.Items[0].Key);
        Assert.Throws<HccException>(() => HandoverRules.ValidateTemplate("ab", "Maintenance", "Evening", ["MNT"], ok));
        Assert.Throws<HccException>(() => HandoverRules.ValidateTemplate("Pool deck check", "Maintenance", "Evening", ["CSH"], ok));
        Assert.Throws<HccException>(() => HandoverRules.ValidateTemplate("Pool deck check", "Maintenance", "Evening", [], ok));
        Assert.Throws<HccException>(() => HandoverRules.ValidateTemplate("Pool deck check", "Maintenance", "Evening", ["MNT"], [("Skim", true, "25:00", false, null)]));
        Assert.Throws<HccException>(() => HandoverRules.ValidateTemplate("Pool deck check", "Maintenance", "Evening", ["MNT"], []));
        Assert.Throws<HccException>(() => HandoverRules.ValidateTemplate("Pool deck check", "Maintenance", "Evening", ["MNT"], Enumerable.Range(0, 26).Select(i => ((string?)"Item text", true, (string?)null, false, (string?)null)).ToList()));
    }

    [Theory]
    [InlineData("IMG_20261024_0810.jpg", true)]
    [InlineData("lobby.exe", false)]
    [InlineData("a b (1).HEIC", true)]
    public void Photo_names_are_file_names(string name, bool valid) => Assert.Equal(valid, HandoverRules.IsValidPhotoName(name));

    [Fact]
    public void Percent_rounds_half_away_from_zero()
    {
        Assert.Equal(29, HandoverRules.RoundPct(2, 7));
        Assert.Equal(8, HandoverRules.RoundPct(2, 25));
        Assert.Equal(14, HandoverRules.RoundPct(1, 7));
        Assert.Equal(13, HandoverRules.RoundPct(1, 8)); // 12.5 → 13
    }

    [Fact]
    public void Note_reference_matches_the_prototype_id()
        => Assert.Equal("HOVN-FRONT-2026-10-24-M", HandoverRules.NoteRef("Front desk", new DateOnly(2026, 10, 24), "Morning"));
}
