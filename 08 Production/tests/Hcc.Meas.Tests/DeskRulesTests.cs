using Hcc.Operations.Meas;

namespace Hcc.Meas.Tests;

/// <summary>[F12-CAP-DESK] server rules of the desk captures; the twin of 03 Prototype/tests/stream-meas-desk.mjs.</summary>
public class DeskRulesTests
{
    private static readonly string[] Own = ["OWN"];
    private static readonly string[] Mgr = ["MGR"];
    private static readonly string[] Rec = ["REC"];
    private static readonly string[] Csh = ["CSH"];

    [Fact]
    public void F12_CAP_DESK_Late_checkout_bands_and_prices()
    {
        Assert.Null(DeskRules.LateCheckoutItem(new TimeOnly(13, 59)));
        Assert.Equal("LCO1", DeskRules.LateCheckoutItem(new TimeOnly(14, 0)));
        Assert.Equal("LCO1", DeskRules.LateCheckoutItem(new TimeOnly(17, 59)));
        Assert.Equal("LCO2", DeskRules.LateCheckoutItem(new TimeOnly(18, 0)));
        Assert.Equal(30000, DeskRules.LateCheckoutPaise("LCO1"));
        Assert.Equal(60000, DeskRules.LateCheckoutPaise("LCO2"));
        Assert.Null(DeskRules.LateCheckoutPaise("UPG"));
    }

    [Fact]
    public void F12_CAP_DESK_Turnaway_validation()
    {
        var ok = new DeskRules.TurnawayInput("full", new DateOnly(2026, 10, 31), 2, "Classic", 1, 200000, "Walk-in", null);
        Assert.Null(DeskRules.ValidateTurnaway(ok));
        Assert.NotNull(DeskRules.ValidateTurnaway(ok with { Reason = "nope" }));
        Assert.NotNull(DeskRules.ValidateTurnaway(ok with { Nights = 0 }));
        Assert.NotNull(DeskRules.ValidateTurnaway(ok with { Rooms = 21 }));
        Assert.NotNull(DeskRules.ValidateTurnaway(ok with { QuotedRatePaise = -1 }));
        Assert.NotNull(DeskRules.ValidateTurnaway(ok with { Channel = " " }));
        Assert.NotNull(DeskRules.ValidateTurnaway(ok with { Reason = "other" }));
        Assert.Null(DeskRules.ValidateTurnaway(ok with { Reason = "other", Note = "Wanted a pool" }));
    }

    [Fact]
    public void F12_CAP_DESK_Roles_per_capture()
    {
        Assert.True(DeskRules.MayLogTurnaway(Rec)); Assert.True(DeskRules.MayLogTurnaway(Mgr)); Assert.False(DeskRules.MayLogTurnaway(Csh));
        Assert.False(DeskRules.MayLogWalked(Rec)); Assert.True(DeskRules.MayLogWalked(Mgr));
        Assert.False(DeskRules.MaySaveAllowance(Mgr)); Assert.True(DeskRules.MaySaveAllowance(Own));
        Assert.True(DeskRules.MaySetExtra(Rec)); Assert.False(DeskRules.MaySetExtra(Csh));
        Assert.False(DeskRules.MayBlacklist(Rec)); Assert.True(DeskRules.MayBlacklist(Mgr));
    }

    [Fact]
    public void F12_CAP_DESK_Waiver_and_free_upgrade_need_a_manager()
    {
        Assert.NotNull(DeskRules.ValidateWaiver(Rec, "VIP guest"));
        Assert.NotNull(DeskRules.ValidateWaiver(Mgr, ""));
        Assert.Null(DeskRules.ValidateWaiver(Mgr, "VIP guest"));
        Assert.True(DeskRules.FreeUpgradeApproved(Mgr, 2, null, null));
        Assert.False(DeskRules.FreeUpgradeApproved(Rec, 3, null, null));
        Assert.False(DeskRules.FreeUpgradeApproved(Rec, 3, 3, Mgr));
        Assert.False(DeskRules.FreeUpgradeApproved(Rec, 3, 4, Csh));
        Assert.True(DeskRules.FreeUpgradeApproved(Rec, 3, 2, Mgr));
    }

    [Fact]
    public void F12_CAP_DESK_Blacklist_needs_reason_and_owner_approval()
    {
        Assert.NotNull(DeskRules.ValidateBlacklist(Rec, 3, "Damaged room", 1, Own));
        Assert.NotNull(DeskRules.ValidateBlacklist(Mgr, 2, "x", 1, Own));
        Assert.NotNull(DeskRules.ValidateBlacklist(Mgr, 2, "Damaged room", null, null));
        Assert.NotNull(DeskRules.ValidateBlacklist(Mgr, 2, "Damaged room", 2, Mgr));
        Assert.Null(DeskRules.ValidateBlacklist(Mgr, 2, "Damaged room", 1, Own));
        Assert.Null(DeskRules.ValidateBlacklist(Own, 1, "Damaged room", null, null));
    }

    [Fact]
    public void F12_CAP_DESK_Codes_lead_days_and_audit()
    {
        Assert.Equal("VIP-GUEST", DeskRules.NormaliseCode("vip guest"));
        Assert.Null(DeskRules.NormaliseCode("a"));
        Assert.Equal(7, DeskRules.LeadDays(new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 31)));
        Assert.Equal(0, DeskRules.LeadDays(new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 20)));
        Assert.Equal("MEAS_OFF", DeskRules.MeasOff);
        Assert.Equal("Classic × 1 · 2 night(s) from 2026-10-31 · full · quoted 2000", DeskRules.TurnawayAudit("Classic", 1, 2, new DateOnly(2026, 10, 31), "full", 200000));
    }
}
