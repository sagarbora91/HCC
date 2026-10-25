using Hcc.Application.GuestServices;
using Hcc.Domain.GuestServices;

namespace Hcc.GuestServices.Tests.Kernel;

/// <summary>
/// The prototype's guest-services rules and fixture figures (03 Prototype/src/fixtures/ext/services.ts, tests/stream-services.mjs) with the
/// simulated clock 2026-10-24 08:00 read as UTC. No database.
/// </summary>
public class RulesGoldenTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);

    private static DateTime At(string hhmm, string day = "2026-10-24") => DateTime.SpecifyKind(DateTime.Parse($"{day}T{hhmm}:00", System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);

    /// <summary>The six requests of the prototype BASE fixture (SR-901…SR-906).</summary>
    private static readonly RequestFact[] Fixture =
    [
        new("Taxi", "Front desk", "Done", At("06:40"), At("07:10"), null, At("06:58")),
        new("Iron & board", "Housekeeping", "Done", At("07:00"), At("07:15"), null, At("07:28")),
        new("Extra towels", "Housekeeping", "Open", At("07:30"), At("07:50"), null, null),
        new("Drinking water", "Housekeeping", "Assigned", At("07:55"), At("08:05"), null, null),
        new("Wake-up call", "Front desk", "Open", At("07:45"), At("06:35", "2026-10-25"), At("06:30", "2026-10-25"), null),
        new("Minor repair", "Maintenance", "Assigned", At("07:40"), At("08:25"), null, null),
    ];

    [Fact]
    public void Sla_minutes_follow_the_defaults_and_urgent_halves_with_a_floor_of_five()
    {
        var s = GuestServiceSettings.Default;
        Assert.Equal(20, GuestServiceRules.SlaMinutes(s, "Extra towels", false));
        Assert.Equal(10, GuestServiceRules.SlaMinutes(s, "Extra towels", true));
        Assert.Equal(15, GuestServiceRules.SlaMinutes(s, "Taxi", true));   // prototype: Urgent taxi due after 15 min
        Assert.Equal(5, GuestServiceRules.SlaMinutes(s, "Wake-up call", true));
        Assert.Equal(5, GuestServiceRules.SlaMinutes(s, "Drinking water", true));
        Assert.Equal("Maintenance", GuestServiceRules.DefaultDept("Minor repair"));
        Assert.Equal("Front desk", GuestServiceRules.DefaultDept("Taxi"));
        Assert.Equal("Housekeeping", GuestServiceRules.DefaultDept("Extra towels"));
    }

    [Fact]
    public void Sla_labels_match_the_prototype()
    {
        Assert.Equal("Overdue · 10 min", GuestServiceRules.Label(Fixture[2], Now).Label);
        Assert.Equal("red", GuestServiceRules.Label(Fixture[2], Now).Tone);
        Assert.Equal("Due in 5 min", GuestServiceRules.Label(Fixture[3], Now).Label);
        Assert.Equal("Done late · 28 min", GuestServiceRules.Label(Fixture[1], Now).Label);
        Assert.Equal("Within SLA · 18 min", GuestServiceRules.Label(Fixture[0], Now).Label);
        Assert.Equal("Scheduled 06:30", GuestServiceRules.Label(Fixture[4], Now).Label);
        Assert.True(GuestServiceRules.IsOverdue(Fixture[2], Now));
        Assert.False(GuestServiceRules.IsOverdue(Fixture[4], Now));
        Assert.True(GuestServiceRules.IsLate(Fixture[1]));
        Assert.False(GuestServiceRules.IsLate(Fixture[0]));
    }

    [Fact]
    public void Response_report_reproduces_the_prototype_figures()
    {
        var t = GuestServiceRules.ResponseTotals(Fixture, Now);
        Assert.Equal((2, 1, 50, 23, 4, 1), (t.Done, t.Within, t.Pct, t.Avg, t.Open, t.Overdue));
        var fd = GuestServiceRules.ResponseReport(Fixture, Now, byDept: true).Single(r => r.Key == "Front desk");
        Assert.Equal((1, 18, 100), (fd.Done, fd.AvgMinutes, fd.Pct));
        Assert.Equal(3, GuestServiceRules.ResponseReport(Fixture, Now, byDept: true).Count);
    }

    [Fact]
    public void Cancelled_requests_are_left_out_of_the_report()
    {
        var withCancelled = Fixture.Append(new RequestFact("Taxi", "Front desk", "Cancelled", At("07:00"), At("07:30"), null, null)).ToArray();
        Assert.Equal(GuestServiceRules.ResponseTotals(Fixture, Now), GuestServiceRules.ResponseTotals(withCancelled, Now));
    }

    [Fact]
    public void Retention_disposal_dates_and_valuables()
    {
        var s = GuestServiceSettings.Default;
        Assert.Equal(new DateOnly(2026, 9, 28), GuestServiceRules.DisposeOn(new DateOnly(2026, 6, 30), false, s));
        Assert.True(GuestServiceRules.IsDisposable("Stored", new DateOnly(2026, 10, 24), new DateOnly(2026, 6, 30), false, s));   // LF-902 scarf
        Assert.False(GuestServiceRules.IsDisposable("Stored", new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 22), true, s));  // LF-901 charger
        Assert.False(GuestServiceRules.IsDisposable("Claimed", new DateOnly(2026, 10, 24), new DateOnly(2026, 6, 30), false, s));
        var shorter = GuestServiceSettings.Merge(null, 30, 60);
        Assert.Equal(new DateOnly(2026, 7, 30), GuestServiceRules.DisposeOn(new DateOnly(2026, 6, 30), false, shorter));   // prototype settings test
        Assert.True(GuestServiceRules.IsValuable("Jewellery"));
        Assert.False(GuestServiceRules.IsValuable("Clothing"));
    }

    [Theory]
    [InlineData("Photo matched · ends 1234", true)]
    [InlineData("ends 4417", true)]
    [InlineData("123456789012", false)]
    [InlineData("1234 5678 9012", false)]
    [InlineData("12-34-56-78", false)]
    [InlineData("ab", false)]
    [InlineData("", false)]
    public void Id_note_never_holds_a_full_id_number(string note, bool ok) => Assert.Equal(ok, GuestServiceRules.IdNoteOk(note));

    [Theory]
    [InlineData("98000 00111", "9800000111")]
    [InlineData("+91-98000-00111", "+919800000111")]
    [InlineData("12345", null)]
    [InlineData("phone", null)]
    public void Phone_normalisation(string input, string? expected) => Assert.Equal(expected, GuestServiceRules.NormalizePhone(input));

    [Fact]
    public void Recovery_status_follows_the_billing_approval()
    {
        Assert.Equal("None", GuestRecoveryRules.StatusOf(null, false, false, null));
        Assert.Equal("Requesting", GuestRecoveryRules.StatusOf("Requesting", false, false, null));
        Assert.Equal("Posted", GuestRecoveryRules.StatusOf("Recorded", true, false, null));
        Assert.Equal("Pending approval", GuestRecoveryRules.StatusOf("Recorded", false, true, "Pending"));
        Assert.Equal("Approved", GuestRecoveryRules.StatusOf("Recorded", false, true, "Approved"));
        Assert.Equal("Rejected", GuestRecoveryRules.StatusOf("Recorded", false, true, "Rejected"));
        Assert.Equal("Rejected", GuestRecoveryRules.StatusOf("Recorded", false, true, "Expired"));
        Assert.True(GuestRecoveryRules.CanOffer("None"));
        Assert.True(GuestRecoveryRules.CanOffer("Rejected"));
        Assert.False(GuestRecoveryRules.CanOffer("Pending approval"));
        Assert.False(GuestRecoveryRules.CanOffer("Posted"));
    }

    [Fact]
    public void Complaint_report_counts_by_category_and_severity()
    {
        var rows = GuestServiceRules.ComplaintReport(
        [
            new ComplaintFact("Noise", "Medium", "Open", false), new ComplaintFact("Noise", "High", "Closed", true),
            new ComplaintFact("Room cleanliness", "Low", "Resolved", false),
        ]);
        Assert.Equal(["Room cleanliness", "Noise"], rows.Select(r => r.Category).ToArray());
        var noise = rows.Single(r => r.Category == "Noise");
        Assert.Equal((2, 1, 1), (noise.Total, noise.Open, noise.Recovery));
        Assert.Equal(1, noise.BySeverity["High"]);
    }
}
