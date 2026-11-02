using Hcc.Domain.Errors;
using Hcc.Domain.Keys;

namespace Hcc.Keys.Tests.Kernel;

public class KeyRulesTests
{
    [Fact]
    public void Bis_doors_are_35_and_only_106_is_a_manual_key_room()
    {
        var rooms = Enumerable.Range(101, 7).Concat(Enumerable.Range(201, 14)).Concat(Enumerable.Range(301, 14)).Select(r => r.ToString()).ToList();
        Assert.Equal(35, rooms.Count(KeyRules.BisDoorExists));
        Assert.Equal(34, rooms.Count(KeyRules.DefaultHasLock));
        Assert.False(KeyRules.DefaultHasLock("106"));
        Assert.True(KeyRules.BisDoorExists("106"));
        Assert.False(KeyRules.DefaultHasLock("X99"));   // an HCC room without a BIS door has no lock until the Manager maps it
    }

    [Theory]
    [InlineData("issued", null, true)]
    [InlineData("cancelled", null, true)]
    [InlineData("read", null, true)]
    [InlineData("reissued", null, false)]
    [InlineData("reissued", "lost", true)]
    [InlineData("reissued", "damaged", true)]
    [InlineData("extended", "extension", true)]
    [InlineData("extended", null, false)]
    [InlineData("reactivated", "other", true)]
    [InlineData("reactivated", "", false)]
    [InlineData("reactivated", "because", false)]
    [InlineData("bogus", null, false)]
    public void Reason_is_required_for_reissue_extend_and_reactivate(string ev, string? reason, bool valid)
    {
        if (valid) KeyRules.ValidateEvent(ev, string.IsNullOrEmpty(reason) ? null : reason);
        else Assert.Throws<HccException>(() => KeyRules.ValidateEvent(ev, string.IsNullOrEmpty(reason) ? null : reason));
    }

    [Fact]
    public void Staying_days_round_up_and_never_fall_below_one()
    {
        Assert.Equal(2, KeyRules.StayingDays(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 4)));
        Assert.Equal(1, KeyRules.StayingDays(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2)));
        Assert.Equal(new DateTime(2026, 11, 4, 12, 0, 0), KeyRules.CardExpiry(new DateOnly(2026, 11, 4)));
    }

    [Fact]
    public void Mismatch_warning_appears_unless_the_folio_checks_out_at_12_00()
    {
        Assert.Null(KeyRules.MismatchWarning(new DateOnly(2026, 11, 4), new TimeOnly(12, 0)));
        var early = KeyRules.MismatchWarning(new DateOnly(2026, 11, 4), new TimeOnly(11, 0));
        Assert.NotNull(early);
        Assert.Contains("12:00", early, StringComparison.Ordinal);
        Assert.Contains("11:00", early, StringComparison.Ordinal);
        Assert.NotNull(KeyRules.MismatchWarning(new DateOnly(2026, 11, 4), new TimeOnly(14, 0)));
    }

    [Fact]
    public void Job_states_only_move_forward_and_terminal_states_never_move()
    {
        Assert.True(KeyRules.CanMove("queued", "picked"));
        Assert.True(KeyRules.CanMove("picked", "waiting_card"));
        Assert.True(KeyRules.CanMove("waiting_card", "done"));
        Assert.True(KeyRules.CanMove("queued", "expired"));
        Assert.False(KeyRules.CanMove("queued", "done"));
        Assert.False(KeyRules.CanMove("waiting_card", "picked"));
        foreach (var t in new[] { "done", "failed", "expired" }) Assert.All(new[] { "queued", "picked", "waiting_card", "done" }, to => Assert.False(KeyRules.CanMove(t, to)));
        Assert.Equal(TimeSpan.FromMinutes(2), KeyRules.QueueTtl);
    }

    [Fact]
    public void Every_event_maps_to_a_job_kind()
    {
        Assert.All(KeyEvents.All, e => Assert.False(string.IsNullOrEmpty(KeyRules.KindFor(e))));
        Assert.Equal("extend", KeyRules.KindFor("extended"));
        Assert.Equal("cancel", KeyRules.KindFor("cancelled"));
    }
}
