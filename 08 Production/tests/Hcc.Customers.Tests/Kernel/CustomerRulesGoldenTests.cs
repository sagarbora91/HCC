using System.Globalization;
using System.Text.Json;
using Hcc.Domain.Customers;

namespace Hcc.Customers.Tests.Kernel;

/// <summary>
/// Prototype golden figures: the customer index of the FY26 scenario (1279 bookings, 1155 customers) reproduced by the ported rules.
/// Bookings that carry their own mobile number in the prototype are bridged to a synthetic guest profile (production keeps the mobile on gst.guest).
/// </summary>
public class CustomerRulesGoldenTests
{
    private static readonly Lazy<(JsonElement Root, IReadOnlyList<Customer> Index)> Data = new(() =>
    {
        var root = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "customers-golden.json"))).RootElement;
        var profiles = root.GetProperty("guests").EnumerateArray().ToDictionary(g => g.GetProperty("id").GetString()!);
        var guests = new List<CrmGuest>();
        var stays = new List<CrmStay>();
        long n = 0;
        foreach (var b in root.GetProperty("bookings").EnumerateArray())
        {
            string? Str(string p) => b.GetProperty(p).ValueKind == JsonValueKind.Null ? null : b.GetProperty(p).GetString();
            var gid = Str("guestId");
            var has = profiles.TryGetValue(gid ?? string.Empty, out var prof);
            var phone = CustomerRules.PhoneKey(Str("phone")).Length > 0 ? Str("phone")! : (has ? prof.GetProperty("phone").GetString() : null) ?? Str("phone") ?? string.Empty;
            var name = has ? prof.GetProperty("name").GetString()! : Str("guest")!;
            var email = Str("email") ?? (has && prof.GetProperty("email").ValueKind != JsonValueKind.Null ? prof.GetProperty("email").GetString() : null);
            guests.Add(new CrmGuest(++n, name, phone, email, null, null));
            var status = Str("status") switch { "In house" => CrmStay.InHouse, "No show" => CrmStay.NoShow, var x => x! };
            stays.Add(new CrmStay(n, Str("id")!, n, Str("guest")!, DateOnly.Parse(Str("arrival")!, CultureInfo.InvariantCulture), DateOnly.Parse(Str("departure")!, CultureInfo.InvariantCulture),
                status, Str("room"), b.GetProperty("total").GetInt64(), b.GetProperty("rate").GetInt64()));
        }

        return (root, CustomerRules.Index(guests, stays, new Dictionary<string, string>(), DateOnly.Parse(root.GetProperty("date").GetString()!, CultureInfo.InvariantCulture)));
    });

    [Fact]
    public void Every_booking_belongs_to_exactly_one_customer_and_the_customer_count_matches_the_prototype()
    {
        var (root, idx) = Data.Value;
        Assert.Equal(root.GetProperty("bookingCount").GetInt32(), idx.Sum(c => c.Stays.Count));
        Assert.Equal(root.GetProperty("customers").GetArrayLength(), idx.Count);
        Assert.Equal(idx.Sum(c => c.Stays.Count), idx.SelectMany(c => c.Stays).Select(s => s.PublicNo).Distinct().Count());
    }

    [Fact]
    public void Per_customer_figures_equal_the_prototype()
    {
        var (root, idx) = Data.Value;
        var byKey = idx.ToDictionary(c => c.Key);
        foreach (var g in root.GetProperty("customers").EnumerateArray())
        {
            var c = byKey[g.GetProperty("key").GetString()!];
            var k = c.Key;
            Assert.True(g.GetProperty("stays").GetInt32() == c.Stays.Count, k + " stays");
            Assert.True(g.GetProperty("completed").GetInt32() == c.Completed.Count, k + " completed");
            Assert.True(g.GetProperty("nights").GetInt32() == c.Nights, k + " nights");
            Assert.True(g.GetProperty("lifetime").GetInt64() == c.LifetimePaise, k + " lifetime");
            Assert.True(g.GetProperty("avgRate").GetInt64() == c.AvgRatePaise, k + " avg rate");
            Assert.True(g.GetProperty("cancelled").GetInt32() == c.Cancelled, k + " cancelled");
            Assert.Equal(g.GetProperty("lastStay").GetString(), c.LastStay is { } l ? CustomerRules.Date(l) : null);
            Assert.Equal(g.GetProperty("firstStay").GetString(), c.FirstStay is { } f ? CustomerRules.Date(f) : null);
            Assert.Equal(g.GetProperty("current").GetString(), c.Current?.PublicNo);
            Assert.Equal(g.GetProperty("topRooms").EnumerateArray().Select(x => x.GetString()), c.TopRooms);
            Assert.Equal(g.GetProperty("upcoming").EnumerateArray().Select(x => x.GetString()), c.Upcoming.Select(s => s.PublicNo));
            Assert.Equal(g.GetProperty("stayIds").EnumerateArray().Select(x => x.GetString()).Order(StringComparer.Ordinal), c.Stays.Select(s => s.PublicNo).Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void A_repeat_customer_lists_stays_newest_first_with_totals_over_stays_that_happened()
    {
        var (root, idx) = Data.Value;
        var rep = idx.Single(c => c.Key == root.GetProperty("repeatKey").GetString());
        Assert.True(rep.Completed.Count >= 3);
        for (var i = 1; i < rep.Stays.Count; i++) Assert.True(rep.Stays[i - 1].Arrival >= rep.Stays[i].Arrival);
        Assert.Equal(rep.Completed.Sum(b => b.TotalPaise), rep.LifetimePaise);
        Assert.All(rep.Completed, b => Assert.True(b.Status is "Departed" or "InHouse"));
        Assert.Equal(rep.Completed[0].Arrival, rep.LastStay);
    }

    [Fact]
    public void Search_by_name_words_and_mobile_digits_matches_the_prototype_and_never_matches_booking_ids()
    {
        var (root, idx) = Data.Value;
        foreach (var s in root.GetProperty("searches").EnumerateArray())
        {
            var found = CustomerRules.Search(idx, s.GetProperty("q").GetString(), s.GetProperty("phoneOk").GetBoolean()).Select(c => c.Key).Order(StringComparer.Ordinal).ToList();
            Assert.Equal(s.GetProperty("keys").EnumerateArray().Select(x => x.GetString()), found);
        }
    }

    [Fact]
    public void Customers_are_matched_by_mobile_even_when_the_name_is_spelled_differently()
    {
        var guests = new[] { new CrmGuest(1, "Somebody Else", "+91 98200 99999", null, null, null), new CrmGuest(2, "S. Else", "098200 99999", null, null, null) };
        var d = new DateOnly(2026, 10, 1);
        var stays = new[]
        {
            new CrmStay(1, "X-1", 1, "Somebody Else", d, d.AddDays(1), "Departed", "101", 100, 100),
            new CrmStay(2, "X-2", 2, "S. Else", d.AddDays(5), d.AddDays(6), "Departed", "101", 100, 100),
        };
        var idx = CustomerRules.Index(guests, stays, new Dictionary<string, string>(), d.AddDays(10));
        Assert.Single(idx);
        Assert.Equal(2, idx[0].Stays.Count);
        Assert.Equal("9820099999", idx[0].Key);
    }

    [Theory]
    [InlineData("+91 98200 11111", "9820011111")]
    [InlineData("098200-11111", "9820011111")]
    [InlineData("12345", "")]
    [InlineData(null, "")]
    public void Phone_key_is_the_last_ten_digits(string? phone, string key) => Assert.Equal(key, CustomerRules.PhoneKey(phone));

    [Theory]
    [InlineData("Asha.Rao+stay@Gmail.com", "asharao@gmail.com")]
    [InlineData("asha.rao@googlemail.com", "asharao@gmail.com")]
    [InlineData("A.Rao@Example.test", "a.rao@example.test")]
    [InlineData("not-an-email", "")]
    [InlineData("@x.com", "")]
    public void Email_normalisation(string email, string expected) => Assert.Equal(expected, CustomerRules.NormEmail(email));

    [Fact]
    public void Aliases_resolve_transitively_and_survive_cycles()
    {
        var a = new Dictionary<string, string> { ["a"] = "b", ["b"] = "c" };
        Assert.Equal("c", CustomerRules.Resolve("a", a));
        Assert.Equal("x", CustomerRules.Resolve("x", a));
        var cyc = new Dictionary<string, string> { ["a"] = "b", ["b"] = "a" };
        Assert.Contains(CustomerRules.Resolve("a", cyc), new[] { "a", "b" });
    }

    [Fact]
    public void Validation_rules_for_tags_notes_reasons_and_maker_checker()
    {
        Assert.Equal("vip guest", CustomerRules.CleanTag("  VIP   Guest "));
        Assert.ThrowsAny<Exception>(() => CustomerRules.CleanTag("bad<tag>"));
        Assert.ThrowsAny<Exception>(() => CustomerRules.CleanTag(new string('x', 31)));
        Assert.ThrowsAny<Exception>(() => CustomerRules.CleanNote("  "));
        Assert.ThrowsAny<Exception>(() => CustomerRules.CleanReason("no"));
        Assert.ThrowsAny<Exception>(() => CustomerRules.EnsureChecker(3, 3));
        CustomerRules.EnsureChecker(3, 4);
    }

    [Fact]
    public void Masks_hide_all_but_the_last_four_digits_of_the_mobile_and_the_mailbox()
    {
        Assert.Equal("••••••1111", CustomerRules.MaskPhone("+91 98200 11111"));
        Assert.Equal("a•••@gmail.com", CustomerRules.MaskEmail("asha.rao@gmail.com"));
    }
}
