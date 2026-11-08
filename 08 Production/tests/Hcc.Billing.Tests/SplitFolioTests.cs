using Hcc.Contracts;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Billing.Tests;

/// <summary>[F8-SF-DOMAIN] Split folio (DEC-152, SF-CONTRACT §2.4 / §3 / §4 / §7): routing, folio rules, receipt table, D7 per company folio.</summary>
public class SplitFolioTests
{
    private static FolioRef F(long id, int seq, string kind = "guest", long? co = null, bool removed = false) => new(id, seq, kind, co, kind == "udhaar" ? "9800000000" : null, "Payer", null, removed);

    private static FolioRoutingSnapshot Single() => new([F(10, 1)], new Dictionary<FolioRouteCategory, long>());

    private static FolioRoutingSnapshot Split(params (FolioRouteCategory C, long Folio)[] routes) =>
        new([F(10, 1), F(11, 2, "company", 7)], routes.ToDictionary(r => r.C, r => r.Folio));

    private static string Reason(Action a)
    {
        var e = Assert.Throws<HccException>(a);
        return e.Details!["reason"].GetString()!;
    }

    [Fact]
    public void Single_folio_reservation_always_resolves_to_seq_1_invariance_I1()
    {
        var s = Single();
        foreach (var k in Enum.GetValues<ChargeKind>())
        {
            Assert.Equal(10, FolioRouting.Resolve(s, k, false, null, null));
            Assert.Equal(10, FolioRouting.Resolve(s, k, true, null, null));
        }
        Assert.False(s.IsSplit);
        SplitFolioRules.EnsureNoBillCorrection(s); // no refusal on a single-folio stay
        SplitFolioRules.EnsureReceiptFolio(s, "company-credit", "Company credit", 1); // today's company credit on A
    }

    [Fact]
    public void Resolve_explicit_wins_then_reversal_then_route_then_primary()
    {
        var s = Split((FolioRouteCategory.Room, 11));
        Assert.Equal(11, FolioRouting.Resolve(s, ChargeKind.Room, false, null, null));
        Assert.Equal(11, FolioRouting.Resolve(s, ChargeKind.Service, true, null, null)); // meal-plan line: room category
        Assert.Equal(11, FolioRouting.Resolve(s, ChargeKind.Discount, false, null, null));
        Assert.Equal(10, FolioRouting.Resolve(s, ChargeKind.Service, false, null, null)); // extras not routed → primary
        Assert.Equal(10, FolioRouting.Resolve(s, ChargeKind.Food, false, null, null));
        Assert.Equal(10, FolioRouting.Resolve(s, ChargeKind.Room, false, null, 10)); // explicit
        Assert.Equal(10, FolioRouting.Resolve(s, ChargeKind.Correction, false, 10, null)); // reversal follows the original (I4)
        Assert.Equal(11, FolioRouting.Resolve(s, ChargeKind.Correction, false, 11, null));
        Assert.Equal(10, FolioRouting.Resolve(s, ChargeKind.Correction, false, null, null));
        Assert.Throws<ArgumentException>(() => FolioRouting.Resolve(s, ChargeKind.Room, false, null, 99));
        var removed = new FolioRoutingSnapshot([F(10, 1), F(11, 2, "company", 7, removed: true)], new Dictionary<FolioRouteCategory, long> { [FolioRouteCategory.Room] = 11 });
        Assert.Equal(10, FolioRouting.Resolve(removed, ChargeKind.Room, false, null, null));
    }

    [Fact]
    public void Letters_and_categories()
    {
        Assert.Equal("A", FolioRouting.Letter(1));
        Assert.Equal("D", FolioRouting.Letter(4));
        Assert.Equal(3, FolioRouting.SeqOf("C"));
        Assert.Equal(FolioRouteCategory.Room, FolioRouting.CategoryOf(ChargeKind.Room, false));
        Assert.Equal(FolioRouteCategory.Extras, FolioRouting.CategoryOf(ChargeKind.Service, false));
        Assert.Equal("extras", FolioRouting.Code(FolioRouting.Parse("extras")));
    }

    [Fact]
    public void Open_rules_every_SF_reason()
    {
        var single = Single();
        Assert.Equal(2, SplitFolioRules.EnsureCanOpen(single, "InHouse", false, "company", 7, null, null, [FolioRouteCategory.Room]));
        Assert.Equal("SF_GROUP_MEMBER", Reason(() => SplitFolioRules.EnsureCanOpen(single, "InHouse", true, "guest", null, null, null, [])));
        Assert.Equal("SF_PAYER_KIND", Reason(() => SplitFolioRules.EnsureCanOpen(single, "InHouse", false, "group", null, null, null, [])));
        Assert.Equal("SF_COMPANY_MISMATCH", Reason(() => SplitFolioRules.EnsureCanOpen(single, "Confirmed", false, "company", 7, 8, null, [])));
        Assert.Equal("SF_PAYER_KIND", Reason(() => SplitFolioRules.EnsureCanOpen(single, "InHouse", false, "udhaar", null, null, " ", [])));
        Assert.Throws<HccException>(() => SplitFolioRules.EnsureCanOpen(single, "Departed", false, "guest", null, null, null, []));
        var split = Split((FolioRouteCategory.Room, 11));
        Assert.Equal("SF_COMPANY_FOLIO", Reason(() => SplitFolioRules.EnsureCanOpen(split, "InHouse", false, "company", 7, null, null, [])));
        Assert.Equal("SF_ROUTE_TAKEN", Reason(() => SplitFolioRules.EnsureCanOpen(split, "InHouse", false, "guest", null, null, null, [FolioRouteCategory.Room])));
        Assert.Equal(3, SplitFolioRules.EnsureCanOpen(split, "InHouse", false, "guest", null, null, null, [FolioRouteCategory.Extras]));
        var full = new FolioRoutingSnapshot([F(10, 1), F(11, 2), F(12, 3), F(13, 4, removed: true)], new Dictionary<FolioRouteCategory, long>());
        Assert.Equal("SF_FOLIO_LIMIT", Reason(() => SplitFolioRules.EnsureCanOpen(full, "InHouse", false, "guest", null, null, null, [])));
    }

    [Fact]
    public void Route_and_remove_rules()
    {
        var split = Split((FolioRouteCategory.Room, 11));
        Assert.Equal(11, SplitFolioRules.EnsureCanRoute(split, 2, [FolioRouteCategory.Room, FolioRouteCategory.Extras]).FolioId); // its own route is not "taken"
        Assert.Equal("SF_FOLIO_UNKNOWN", Reason(() => SplitFolioRules.EnsureCanRoute(split, 3, [])));
        Assert.Equal("SF_FOLIO_NOT_EMPTY", Reason(() => SplitFolioRules.EnsureCanRemove(split, 1, 0, 0, false)));
        Assert.Equal("SF_FOLIO_NOT_EMPTY", Reason(() => SplitFolioRules.EnsureCanRemove(split, 2, 1, 0, false)));
        Assert.Equal("SF_FOLIO_INVOICED", Reason(() => SplitFolioRules.EnsureCanRemove(split, 2, 0, 0, true)));
        Assert.Equal(11, SplitFolioRules.EnsureCanRemove(split, 2, 0, 0, false).FolioId);
        Assert.Equal("SF_SPLIT_BILL_CORRECTION", Reason(() => SplitFolioRules.EnsureNoBillCorrection(split)));
    }

    [Fact]
    public void Receipt_table_deposits_on_A_company_credit_on_the_company_folio_no_cash_on_it()
    {
        var split = Split();
        Assert.Equal("SF_DEPOSIT_FOLIO", Reason(() => SplitFolioRules.EnsureReceiptFolio(split, "deposit", "UPI", 2)));
        Assert.Equal("SF_DEPOSIT_FOLIO", Reason(() => SplitFolioRules.EnsureReceiptFolio(split, "ota-credit", null, 2)));
        Assert.Equal("SF_COMPANY_FOLIO", Reason(() => SplitFolioRules.EnsureReceiptFolio(split, "company-credit", null, 1)));
        Assert.Equal("SF_COMPANY_FOLIO", Reason(() => SplitFolioRules.EnsureReceiptFolio(split, "payment", "Cash", 2)));
        Assert.Equal("SF_PAYER_KIND", Reason(() => SplitFolioRules.EnsureReceiptFolio(split, "guest-credit", null, 2)));
        SplitFolioRules.EnsureReceiptFolio(split, "company-credit", "Company credit", 2);
        SplitFolioRules.EnsureReceiptFolio(split, "payment", "UPI", 2);
        SplitFolioRules.EnsureReceiptFolio(split, "deposit", "Cash", 1);
    }

    [Fact]
    public void D7_folio_exposure_counts_only_that_folio_and_nights_only_when_room_is_routed()
    {
        // golden stay §5.7: folio B = 3 nights ₹3,000 + 5 % = ₹9,450; nothing paid on B
        var posted = Paise.FromRupees(9450);
        Assert.Equal(posted, SplitFolioRules.FolioExposure(posted, Paise.Zero, true, Paise.Zero));
        Assert.Equal(Paise.FromRupees(6300), SplitFolioRules.FolioExposure(Paise.FromRupees(3150), Paise.FromRupees(6300), true, Paise.FromRupees(3150)));
        Assert.Equal(Paise.FromRupees(3150), SplitFolioRules.FolioExposure(Paise.FromRupees(3150), Paise.FromRupees(6300), false, Paise.Zero));
        Assert.Equal(Paise.Zero, SplitFolioRules.FolioExposure(Paise.FromRupees(100), Paise.Zero, false, Paise.FromRupees(500)));
        // within ₹50,000: passes; limit ₹9,000 (variant b): refused with the D7 text, "split folio"
        SplitFolioRules.EnsureCompanyCredit(7, "Deccan Agro Ltd (fictional)", Paise.FromRupees(50000), Paise.Zero, posted);
        var e = Assert.Throws<HccException>(() => SplitFolioRules.EnsureCompanyCredit(7, "Deccan Agro Ltd (fictional)", Paise.FromRupees(9000), Paise.Zero, posted));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, e.Code);
        Assert.Equal("CREDIT_LIMIT", e.Details!["reason"].GetString());
        Assert.Contains("this split folio needs", e.Message, StringComparison.Ordinal);
        // night posting is never refused by D7; any other line is checked
        Assert.True(SplitFolioRules.IsNightPosting(ChargeKind.Room, true));
        Assert.False(SplitFolioRules.IsNightPosting(ChargeKind.Service, true));
        Assert.False(SplitFolioRules.IsNightPosting(ChargeKind.Room, false));
    }
}
