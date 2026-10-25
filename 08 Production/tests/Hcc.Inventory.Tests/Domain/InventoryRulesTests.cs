using Hcc.Contracts;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Inventory;
using Hcc.Domain.Reservations;
using Hcc.Inventory.Tests.Harness;

namespace Hcc.Inventory.Tests.Domain;

/// <summary>Pure P2 rules (no database): restriction codes/messages against the prototype, pricing, blocks, status map.</summary>
public sealed class InventoryRulesTests
{
    private static readonly GoldenInventory G = GoldenInventory.Load();

    [Fact]
    public void Stay_restriction_violations_match_the_prototype_codes_and_messages()
    {
        var typeIds = G.Raw.RoomTypes.Select((t, i) => (t.Code, Id: i + 1)).ToDictionary(x => x.Code, x => x.Id);
        var restrictions = G.Raw.Restrictions.Select(r => new StayRestriction(r.Id, r.Type == "*" ? null : typeIds[r.Type], StayDates.Parse(r.From),
            StayDates.Parse(r.To), r.MinStay, r.Cta, r.Note)).ToList();
        Assert.Equal(6, G.Expected.ViolationCases.Count);
        foreach (var c in G.Expected.ViolationCases)
        {
            var v = StayRestrictions.Violations(restrictions, typeIds[c.Type], c.Type, StayDates.Parse(c.Arrival), StayDates.Parse(c.Departure));
            Assert.Equal(c.Violations.Select(x => (x.Code, x.Restriction, x.Message)), v.Select(x => (x.Code, x.Restriction, x.Message)));
        }

        var note = StayRestrictions.OverrideNote([new StayViolation("MIN_STAY", "YLDR-1", "m")], "Regular corporate guest");
        Assert.Equal("restriction overridden (MIN_STAY YLDR-1): Regular corporate guest", note);
    }

    [Fact]
    public void Nightly_pricing_uses_the_arrival_tariff_and_rounds_tax_half_away_from_zero()
    {
        var a = new DateOnly(2026, 10, 24);
        var nights = StayPricing.Nights(a, a.AddDays(2), 12345, d => d == a ? 1000 : 1800);
        Assert.Equal([(12345L, 1000, 1235L), (12345L, 1800, 2222L)], nights.Select(n => (n.RatePaise, n.TaxBps, n.TaxPaise)));
        var tariffs = new[] { (new DateOnly(2026, 11, 1), 210000L), (new DateOnly(2026, 12, 1), 250000L) };
        Assert.Equal(200000, StayPricing.RateOn(tariffs, 200000, new DateOnly(2026, 10, 31)));
        Assert.Equal(210000, StayPricing.RateOn(tariffs, 200000, new DateOnly(2026, 11, 30)));
        Assert.Equal(250000, StayPricing.RateOn(tariffs, 200000, new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void Blocks_cover_half_open_windows_and_undated_blocks_cover_everything()
    {
        var d = new DateOnly(2026, 11, 10);
        RoomBlockWindow[] blocks = [new(1, d, d.AddDays(2), "x"), new(2, null, null, "x"), new(3, d, null, "x")];
        Assert.True(AvailabilityRules.BlockedDuring(blocks, 1, d.AddDays(1), d.AddDays(5)));
        Assert.False(AvailabilityRules.BlockedDuring(blocks, 1, d.AddDays(2), d.AddDays(3)));   // departs-on-block-end is free
        Assert.False(AvailabilityRules.BlockedDuring(blocks, 1, d.AddDays(-2), d));             // adjacent before
        Assert.True(AvailabilityRules.BlockedDuring(blocks, 2, d.AddDays(100), d.AddDays(101)));
        Assert.False(AvailabilityRules.BlockedDuring(blocks, 3, d.AddDays(-1), d));
        Assert.True(AvailabilityRules.BlockedDuring(blocks, 3, d.AddDays(400), d.AddDays(401)));
        Assert.True(RoomNumberComparer.CompareNumbers("1001", "999") > 0);
    }

    [Fact]
    public void Stay_and_party_rules_keep_the_prototype_messages()
    {
        var bd = new DateOnly(2026, 10, 24);
        Assert.Equal("Arrival cannot be before the current business date.", Assert.Throws<HccException>(() => StayRules.Stay("2026-10-23", "2026-10-25", bd)).Message);
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => StayRules.Stay("2026-11-12", "2026-12-13", bd)).Code);
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => StayRules.Stay("2026-10-25", "2026-10-25", bd)).Code);
        Assert.Equal("Choose 1–4 guests.", Assert.Throws<HccException>(() => StayRules.Party(5, 0)).Message);
        Assert.Equal("Room 105 (Classic) accommodates at most 2 guests including children.",
            Assert.Throws<HccException>(() => StayRules.Capacity(new RoomInfo(1, "105", 1, "Classic", 2), 3)).Message);
        Assert.Equal("Give a reason for overriding the stay restriction.", Assert.Throws<HccException>(() => StayRules.OverrideReason(" x ")).Message);
        Assert.Null(StayRules.PhoneNumber("  "));
        Assert.Throws<HccException>(() => StayRules.PhoneNumber("abc"));
    }

    [Fact]
    public void Status_map_follows_the_prototype_cancel_no_show_and_confirm_rules()
    {
        var now = new DateTime(2026, 10, 24, 3, 0, 0, DateTimeKind.Utc);
        var bd = new DateOnly(2026, 10, 24);
        var running = now.AddMinutes(30);
        var lapsed = now.AddMinutes(-1);
        Assert.Equal(ReservationStatuses.Expired, ReservationStatusMap.Effective(ReservationStatuses.Held, lapsed, now));
        Assert.Equal(ReservationStatuses.Held, ReservationStatusMap.Effective(ReservationStatuses.Held, null, now));  // no holdUntil never lapses
        ReservationStatusMap.EnsureCancellable(ReservationStatuses.Held, running, now, false, bd, bd);
        Assert.Throws<HccException>(() => ReservationStatusMap.EnsureCancellable(ReservationStatuses.Held, lapsed, now, false, bd, bd));
        Assert.Throws<HccException>(() => ReservationStatusMap.EnsureCancellable(ReservationStatuses.Held, running, now, true, bd, bd));
        Assert.Throws<HccException>(() => ReservationStatusMap.EnsureCancellable(ReservationStatuses.Confirmed, null, now, true, bd.AddDays(1), bd));
        ReservationStatusMap.EnsureCancellable(ReservationStatuses.Confirmed, null, now, true, bd, bd);
        foreach (var s in new[] { ReservationStatuses.InHouse, ReservationStatuses.Departed, ReservationStatuses.Cancelled, ReservationStatuses.NoShow })
            Assert.Equal(HccErrorCode.INVALID_TRANSITION, Assert.Throws<HccException>(() => ReservationStatusMap.EnsureCancellable(s, null, now, false, bd, bd)).Code);
        Assert.Contains("expired at 10:00", Assert.Throws<HccException>(() => ReservationStatusMap.EnsureConfirmable(ReservationStatuses.Expired, lapsed, now, " at 10:00")).Message, StringComparison.Ordinal);
        Assert.Throws<HccException>(() => ReservationStatusMap.EnsureAmendable(ReservationStatuses.InHouse, null, now));
        Assert.Equal("RESTRICTED", AvailabilityReasons.Restricted);
    }
}
