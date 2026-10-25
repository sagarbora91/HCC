using Hcc.Contracts;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Groups;
using Hcc.Domain.Money;

namespace Hcc.Groups.Tests.Kernel;

/// <summary>
/// Golden figures from the prototype group stream (03 Prototype/tests/stream-groups.mjs on the BASE fixture, business date
/// 24 Oct 2026, DEC-069 GST 5 % for rooms ≤ Rs 7,500) and the server routing / attrition rules. No database.
/// </summary>
public sealed class GroupRulesGoldenTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);

    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static IReadOnlyList<(DateOnly, Paise, int)> Nights(DateOnly a, DateOnly d, long rate, int bps = 500)
    {
        var list = new List<(DateOnly, Paise, int)>();
        for (var n = a; n < d; n = n.AddDays(1)) list.Add((n, new Paise(rate), bps));
        return list;
    }

    // ── Prototype golden figures ──

    [Fact]
    public void Pickup_lines_GB1_released_block_reads_4_2_2_0()
    {
        // GB-1 Deccan Pharma: Released at the cut-off (21 Oct); allot {Classic:2}, released {Classic:2}, 2 members picked up.
        var l = GroupPickup.Line(new BlockTypeCounts("Classic", 2, 2, 2, new Paise(175000)), "Released", D(10, 21), Today);
        var t = GroupPickup.Totals([l]);
        Assert.Equal((4, 2, 2, 0), (t.Allotted, t.Picked, t.Released, t.Remaining));
        Assert.Equal(50, t.PickedPercent);
    }

    [Fact]
    public void Pickup_lines_GB2_open_block_reads_9_0_9()
    {
        var lines = new[] { new BlockTypeCounts("Classic", 6, 0, 0, new Paise(180000)), new BlockTypeCounts("Deluxe", 3, 0, 0, new Paise(270000)) }
            .Select(c => GroupPickup.Line(c, "Open", D(10, 27), Today)).ToList();
        var t = GroupPickup.Totals(lines);
        Assert.Equal((9, 0, 9), (t.Allotted, t.Picked, t.Remaining));
    }

    [Fact]
    public void Open_block_past_its_cut_off_shows_released_before_the_sweep_GB3()
    {
        // NEXT_DAY to 25 Oct: GB-3 (cut-off 24 Oct, Classic 5 + Suite 1, nothing picked) is derived as Released, 6 rooms released.
        var day = D(10, 25);
        Assert.Equal("Released", GroupPickup.EffectiveStatus("Open", D(10, 24), day));
        var lines = new[] { new BlockTypeCounts("Classic", 5, 0, 0, new Paise(190000)), new BlockTypeCounts("Suite", 1, 0, 0, new Paise(420000)) }
            .Select(c => GroupPickup.Line(c, "Open", D(10, 24), day)).ToList();
        Assert.Equal(6, GroupPickup.Totals(lines).Released);
        Assert.Equal(0, GroupPickup.Totals(lines).Remaining);
        Assert.Equal("Open", GroupPickup.EffectiveStatus("Open", D(10, 27), day));
    }

    private static readonly PostedLine Night23 = new(1, ChargeKind.Room, D(10, 23), "Room night · 2026-10-23", new Paise(200000), 500, new Paise(10000), null);

    [Fact]
    public void Exposure_HCC1044_is_430000()
    {
        // 3 nights × Rs 2,000 at 5 % = Rs 6,300 − Rs 2,000 paid (one night posted, two agreed nights unposted).
        Assert.Equal(new Paise(430000), GroupEstimate.Exposure("InHouse", [Night23], Nights(D(10, 23), D(10, 26), 200000), new Paise(200000)));
    }

    [Fact]
    public void Master_folio_GB1_totals_paid_400000_estimate_860000_and_company_exposure_7060000()
    {
        var rows = new[] { "HCC-1044", "HCC-1048" }.Select(_ =>
        {
            var f = FolioStatement.Compute([Night23], [new PostedReceipt(1, ReceiptKind.Payment, "UPI", new Paise(200000), Today, null, "DEMO")]);
            return (f, Estimate: GroupEstimate.Exposure("InHouse", [Night23], Nights(D(10, 23), D(10, 26), 200000), f.Paid));
        }).ToList();
        Assert.All(rows, r => Assert.Equal((200000L, 10000L, 210000L, 10000L), (r.f.Net.Value, r.f.Tax.Value, r.f.Total.Value, r.f.Balance.Value)));
        Assert.Equal(400000, rows.Sum(r => r.f.Paid.Value));
        Assert.Equal(860000, rows.Sum(r => r.Estimate.Value));
        Assert.Equal(7060000, 6200000 + rows.Sum(r => r.Estimate.Value)); // CO-1 opening outstanding Rs 62,000 + both stays
    }

    [Fact]
    public void Stay_value_at_block_rate_GB2_rooming_list_takes_CO2_to_9745000()
    {
        var classic = GroupEstimate.StayValue(Nights(D(10, 30), D(11, 1), 180000));
        var deluxe = GroupEstimate.StayValue(Nights(D(10, 30), D(11, 1), 270000));
        Assert.Equal((378000L, 567000L), (classic.Value, deluxe.Value));
        Assert.Equal(9745000, 8800000 + classic.Value + deluxe.Value);
    }

    [Fact]
    public void Inactive_stays_count_nothing_and_departed_stays_add_no_unposted_nights()
    {
        Assert.Equal(Paise.Zero, GroupEstimate.Exposure("Cancelled", [Night23], Nights(D(10, 23), D(10, 26), 200000), Paise.Zero));
        Assert.Equal(new Paise(210000), GroupEstimate.Exposure("Departed", [Night23], Nights(D(10, 23), D(10, 26), 200000), Paise.Zero));
        Assert.Equal(Paise.Zero, GroupEstimate.Exposure("InHouse", [Night23], [], new Paise(500000)));
    }

    [Fact]
    public void Gstin_shape_as_the_prototype()
    {
        Assert.Equal("27ZZFIC0009F1Z5", GroupText.Gstin("27zzfic0009f1z5"));
        Assert.Null(GroupText.Gstin("  "));
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => GroupText.Gstin("12345")).Code);
    }

    // ── Server routing rules ──

    [Fact]
    public void Bill_to_company_routes_room_and_its_tax_to_the_master_extras_to_the_guest()
    {
        var rules = GroupRouting.ForBillTo(company: true);
        Assert.Equal(new LineRoute(RouteTarget.Master, RouteTarget.Master), GroupRouting.ForLine(rules, 7, ChargeKind.Room));
        Assert.Equal(new LineRoute(RouteTarget.Master, RouteTarget.Master), GroupRouting.ForLine(rules, 7, ChargeKind.Discount));
        Assert.Equal(new LineRoute(RouteTarget.Guest, RouteTarget.Guest), GroupRouting.ForLine(rules, 7, ChargeKind.Service));
        var guest = GroupRouting.ForBillTo(company: false);
        Assert.Equal(new LineRoute(RouteTarget.Guest, RouteTarget.Guest), GroupRouting.ForLine(guest, 7, ChargeKind.Room));
    }

    [Fact]
    public void Member_override_beats_group_default_beats_built_in_default()
    {
        IReadOnlyList<RoutingRule> rules =
        [
            new(null, RouteCategory.Extras, RouteTarget.Master),
            new(null, RouteCategory.Tax, RouteTarget.Guest),
            new(9, RouteCategory.Room, RouteTarget.Guest),
            new(9, RouteCategory.Tax, RouteTarget.Follow),
        ];
        Assert.Equal(new LineRoute(RouteTarget.Master, RouteTarget.Guest), GroupRouting.ForLine(rules, 7, ChargeKind.Room)); // room: built-in master; tax: group guest
        Assert.Equal(new LineRoute(RouteTarget.Master, RouteTarget.Guest), GroupRouting.ForLine(rules, 7, ChargeKind.Service));
        Assert.Equal(new LineRoute(RouteTarget.Guest, RouteTarget.Guest), GroupRouting.ForLine(rules, 9, ChargeKind.Room)); // override + tax follows
        Assert.Equal(new LineRoute(RouteTarget.Master, RouteTarget.Master), GroupRouting.ForLine(rules, 9, ChargeKind.Service));
        Assert.Throws<HccException>(() => GroupRouting.Validate(new RoutingRule(null, RouteCategory.Room, RouteTarget.Follow)));
    }

    [Fact]
    public void Shares_split_net_and_tax_per_line()
    {
        var net = new Paise(200000);
        var tax = new Paise(10000);
        Assert.Equal(new LineShares(new Paise(210000), Paise.Zero), GroupRouting.Shares(net, tax, new LineRoute(RouteTarget.Master, RouteTarget.Master)));
        Assert.Equal(new LineShares(new Paise(200000), new Paise(10000)), GroupRouting.Shares(net, tax, new LineRoute(RouteTarget.Master, RouteTarget.Guest)));
        Assert.Equal(new LineShares(Paise.Zero, new Paise(210000)), GroupRouting.Shares(net, tax, new LineRoute(RouteTarget.Guest, RouteTarget.Guest)));
    }

    [Fact]
    public void Reroute_never_goes_below_the_transferred_share_and_a_settled_folio_only_gives_back()
    {
        GroupRouting.EnsureRerouteAllowed(true, new Paise(210000), new Paise(420000), new Paise(210000));
        GroupRouting.EnsureRerouteAllowed(false, new Paise(420000), new Paise(210000), new Paise(210000));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Assert.Throws<HccException>(() => GroupRouting.EnsureRerouteAllowed(true, new Paise(210000), Paise.Zero, new Paise(210000))).Code);
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Assert.Throws<HccException>(() => GroupRouting.EnsureRerouteAllowed(false, new Paise(210000), new Paise(420000), new Paise(210000))).Code);
    }

    // ── Server attrition rule (the prototype has none: it only releases unpicked rooms) ──

    [Fact]
    public void Attrition_GB1_two_unpicked_classic_rooms_at_50_percent_for_three_nights()
    {
        // Contracted 4 (2 allotted after release + 2 released), 2 picked, no free allowance, 50 % of Rs 1,750 = Rs 875 per room-night.
        var lines = GroupAttrition.Lines([new AttritionInput("Classic", 4, 2, new Paise(175000))], D(10, 23), D(10, 26), 0, 5000);
        Assert.Equal(3, lines.Count);
        Assert.All(lines, l => Assert.Equal((2, 87500L, 175000L), (l.Rooms, l.PerRoomNight.Value, l.Amount.Value)));
        Assert.Equal(525000, lines.Sum(l => l.Amount.Value));
    }

    [Fact]
    public void Attrition_allowance_is_free_and_rounds_down_per_type()
    {
        // 10 contracted, 3 picked, 20 % free = 2 rooms → 5 chargeable; 25 % of Rs 1,999.99 = Rs 500.00 (49999.75 → 50000 half away from zero).
        var lines = GroupAttrition.Lines([new AttritionInput("Deluxe", 10, 3, new Paise(199999)), new AttritionInput("Suite", 2, 2, new Paise(420000))],
            D(11, 6), D(11, 7), 2000, 2500);
        var l = Assert.Single(lines);
        Assert.Equal(("Deluxe", 5, 50000L, 250000L), (l.RoomType, l.Rooms, l.PerRoomNight.Value, l.Amount.Value));
        Assert.Equal(1, GroupAttrition.FreeRooms(9, 2000));
        Assert.Empty(GroupAttrition.Lines([new AttritionInput("Classic", 5, 4, new Paise(190000))], D(11, 6), D(11, 8), 2000, 10000));
    }

    [Fact]
    public void Master_balance_counts_routed_shares_and_attrition_less_receipts()
    {
        var f = GroupMaster.Figures(new Paise(400000), new Paise(20000), new Paise(525000), new Paise(26250), new Paise(300000), new Paise(200000), new Paise(50000));
        Assert.Equal((925000L, 46250L, 971250L, 450000L, 521250L), (f.Net.Value, f.Tax.Value, f.Charges.Value, f.Received.Value, f.Balance.Value));
    }
}
