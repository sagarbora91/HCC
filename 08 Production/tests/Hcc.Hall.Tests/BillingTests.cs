using Hcc.Domain.Hall;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Hall.Tests;

/// <summary>F6 H1 · HallBilling / HallRules — the same figures as the prototype tests/stream-hall-billing.mjs and stream-hall.mjs (one truth table).</summary>
public sealed class BillingTests
{
    private static readonly GstMaster Gst = GstMaster.Seed();
    private static readonly DateOnly Today = new(2026, 10, 24), EventDay = new(2026, 12, 5);

    private static Paise P(long v) => new(v);

    private static HallLine[] Golden(int pct)
    {
        long R(long x) => x - (long)Math.Round(x * pct / 100m, MidpointRounding.AwayFromZero);
        return
        [
            new("L1", "HALL", "Sagar Hall · evening", 1, "slot", P(R(1_500_000)), P(1_500_000), Paise.Zero, HallBilling.ClassHallRent),
            new("L2", "MENU", "Silver Veg per plate", 150, "plate", P(R(65_000)), P(65_000), Paise.Zero, HallBilling.ClassBanquetFood),
            new("L3", "EXTRA", "Stage decor", 1, "lot", P(R(1_000_000)), P(1_000_000), Paise.Zero, HallBilling.ClassService),
        ];
    }

    [Fact]
    public void HbGolden_quote_bill_invoice_and_ledgers_balance()
    {
        var q = HallBilling.Price(Gst, Golden(5), Today).Totals;
        Assert.Equal((11_637_500L, 1_047_375L, 1_047_375L, 13_732_250L), (q.Taxable.Value, q.Cgst.Value, q.Sgst.Value, q.Total.Value));
        Assert.Equal(500, HallBilling.DiscountBps(Golden(5)));
        Assert.True(HallBilling.DiscountWithin(Golden(5), HallBilling.DiscountLimitBps("Manager")));

        var bill = HallBilling.BillLines(Golden(5), 150, 160, []);
        var (lines, t) = HallBilling.Price(Gst, bill, EventDay);
        Assert.Equal(
            [("HALL", 1, 1_425_000L, 1800, 128_250L), ("MENU", 160, 9_880_000L, 1800, 889_200L), ("EXTRA", 1, 950_000L, 1800, 85_500L)],
            lines.Select(l => (l.Line.Kind, l.Line.Qty, l.Taxable.Value, l.TaxBps, l.Cgst.Value)).ToArray());
        Assert.Equal((12_255_000L, 1_102_950L, 1_102_950L, 14_460_900L), (t.Taxable.Value, t.Cgst.Value, t.Sgst.Value, t.Total.Value));

        var advance = P(2_000_000);
        var applied = advance < t.Total ? advance : t.Total;
        var balance = t.Total - applied;
        Assert.Equal(12_460_900L, balance.Value);

        // Vouchers: advance Dr 1010 Cr 2000 (no GST) · invoice · apply Dr 2000 Cr 1100 · balance Dr 1110/CO-1 Cr 1100.
        var all = new List<(string Ledger, Paise Amount)> { ("1010", advance), ("2000", -advance) };
        var inv = HallBilling.InvoiceEntries(lines, t);
        Assert.Equal(0L, inv.Sum(e => e.Amount.Value));
        all.AddRange(inv);
        all.AddRange([("2000", applied), ("1100", -applied), ("1110/CO-1", balance), ("1100", -balance)]);
        var by = all.GroupBy(e => e.Ledger).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount.Value));
        Assert.Equal(0L, all.Sum(e => e.Amount.Value));
        Assert.Equal(2_000_000L, by["1010"]);
        Assert.Equal(0L, by["2000"]);
        Assert.Equal(0L, by["1100"]);
        Assert.Equal(-2_375_000L, by["4400"]);
        Assert.Equal(-9_880_000L, by["4200"]);
        Assert.Equal(-1_102_950L, by["2110"]);
        Assert.Equal(-1_102_950L, by["2120"]);
        Assert.Equal(12_460_900L, by["1110/CO-1"]);
    }

    [Fact]
    public void FoodLedgerSwitch_puts_food_in_4400()
    {
        var (lines, t) = HallBilling.Price(Gst, HallBilling.BillLines(Golden(5), 150, 160, []), EventDay);
        var e = HallBilling.InvoiceEntries(lines, t, foodLedger: "4400");
        Assert.Equal(-(2_375_000L + 9_880_000L), e.Single(x => x.Ledger == "4400").Amount.Value);
        Assert.DoesNotContain(e, x => x.Ledger == "4200");
    }

    [Fact]
    public void AdvancePlan_25_50_balance_with_rounding_on_the_last_row()
    {
        var p = HallBilling.AdvancePlan(P(13_732_250), EventDay, Today);
        Assert.Equal([(3_433_063L, "ON_ACCEPT"), (6_866_125L, "2026-11-05"), (3_433_062L, "EVENT_DAY")], p.Select(r => (r.Amount.Value, r.Due)).ToArray());
        Assert.Equal("2026-10-24", HallBilling.AdvancePlan(P(100_000), new DateOnly(2026, 11, 1), Today)[1].Due);
        Assert.Equal([3_433_063L, 1_566_937L, 0L], HallBilling.Allocate(p, P(5_000_000)).Select(r => r.Paid.Value).ToArray());
    }

    [Fact]
    public void DiscountLimits_reception_0_manager_10_owner_any()
    {
        Assert.True(HallBilling.DiscountWithin(Golden(0), HallBilling.DiscountLimitBps("Reception")));
        Assert.False(HallBilling.DiscountWithin(Golden(1), HallBilling.DiscountLimitBps("Reception")));
        Assert.True(HallBilling.DiscountWithin(Golden(10), HallBilling.DiscountLimitBps("Manager")));
        Assert.False(HallBilling.DiscountWithin(Golden(11), HallBilling.DiscountLimitBps("Manager")));
        Assert.True(HallBilling.DiscountWithin(Golden(60), HallBilling.DiscountLimitBps("Owner")));
    }

    [Fact]
    public void Cancel_40_days_keeps_25pct_to_4300_with_gst_rest_refunded()
    {
        var c = HallBilling.Cancel(Gst, Golden(5), new DateOnly(2026, 12, 3), Today, P(5_000_000));
        Assert.Equal((40, 2500, 3_433_063L, 1_566_937L), (c.DaysBefore, c.KeptBps, c.Kept.Value, c.Refund.Value));
        Assert.Equal([("2000", 3_433_063L), ("4300", -2_909_375L), ("2110", -261_844L), ("2120", -261_844L)],
            HallBilling.CancelEntries(c.Kept, c.TaxBps).Select(e => (e.Ledger, e.Amount.Value)).ToArray());
        Assert.Equal(0, HallBilling.Slab(HallBilling.DefaultSlabs, 61).KeptBps);
        Assert.Equal(2500, HallBilling.Slab(HallBilling.DefaultSlabs, 60).KeptBps);
        Assert.Equal(5000, HallBilling.Slab(HallBilling.DefaultSlabs, 29).KeptBps);
        Assert.Equal(10000, HallBilling.Slab(HallBilling.DefaultSlabs, 6).KeptBps);
        Assert.Equal(new HallInclusiveSplit(P(100_000), P(18_000), P(9_000), P(9_000)), HallBilling.Inclusive(P(118_000), 1800));
    }

    [Fact]
    public void Refund_manager_up_to_50000_owner_above()
    {
        Assert.True(HallBilling.RefundApprover("Manager", P(5_000_000)));
        Assert.False(HallBilling.RefundApprover("Manager", P(5_000_001)));
        Assert.True(HallBilling.RefundApprover("Owner", P(50_000_000)));
        Assert.False(HallBilling.RefundApprover("Reception", P(1)));
    }

    [Fact]
    public void Gst_classes_from_the_dated_master()
    {
        Assert.Equal(1800, HallBilling.TaxBps(Gst, HallBilling.ClassHallRent, EventDay, Paise.Zero));
        Assert.Equal(1800, HallBilling.TaxBps(Gst, HallBilling.ClassBanquetFood, EventDay, P(65_000))); // banquet food 18 % (CA, DEC-239)
        Assert.Equal(500, HallBilling.TaxBps(Gst, HallBilling.ClassRoom, EventDay, P(400_000)));
        Assert.Equal(1800, HallBilling.TaxBps(Gst, HallBilling.ClassRoom, EventDay, P(800_000)));
    }

    [Fact]
    public void Clash_rules_slot_overlap_and_combined_halls()
    {
        HallVenueRef[] v = [new("V-SAGAR", null), new("V-MOTI", null), new("V-LAWN", null), new("V-SAGAR-LAWN", ["V-SAGAR", "V-LAWN"])];
        HallHold[] holds = [new("HEV-1", ["V-SAGAR-LAWN"], [EventDay], "EVENING")];
        Assert.NotNull(HallRules.Clash(v, holds, ["V-LAWN"], [EventDay], "EVENING"));
        Assert.NotNull(HallRules.Clash(v, holds, ["V-SAGAR"], [EventDay], "FULL_DAY"));
        Assert.Null(HallRules.Clash(v, holds, ["V-MOTI"], [EventDay], "EVENING"));
        Assert.Null(HallRules.Clash(v, holds, ["V-SAGAR"], [EventDay], "MORNING"));
        Assert.Null(HallRules.Clash(v, holds, ["V-SAGAR"], [EventDay.AddDays(1)], "EVENING"));
        Assert.Equal(["V-LAWN", "V-SAGAR-LAWN"], HallRules.Footprint(v, "V-LAWN").Order(StringComparer.Ordinal).ToArray());
        Assert.True(HallRules.AutoLost("OPEN", Today, Today.AddDays(30)));
        Assert.False(HallRules.AutoLost("OPEN", Today, Today.AddDays(29)));
        Assert.False(HallRules.EnquiryTransition("WON", "OPEN"));
        Assert.False(HallRules.SeesMoney("Housekeeper"));
        Assert.False(HallRules.SeesMoney("Maintenance"));
        Assert.True(HallRules.SeesMoney("Cashier"));
    }
}
