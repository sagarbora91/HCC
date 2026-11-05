using System.Globalization;
using System.Text.Json;
using Hcc.Domain.Customers;

namespace Hcc.Customers.Tests.Kernel;

/// <summary>
/// PER-01: the narrow key pass (<see cref="CustomerRules.Heads"/>) must give exactly what <see cref="CustomerRules.Index"/> gives on the whole property -
/// same keys, names, mobiles, newest arrival and order - and rebuilding one customer from only its own bookings and profiles must give the same customer.
/// Random but seeded data: shared / short / missing mobiles, case and script variants of names, symbol-only names, bookings without a profile or with a
/// profile of another property, every status (expired holds left out), arrival ties, merge aliases incl. a chain and a cycle.
/// </summary>
public class CustomerHeadsEquivalenceTests
{
    private static readonly string[] Names = ["Asha Rao", "ASHA RAO", "asha  rao", "Vikram Shah", "राव शर्मा", "Meera (fictional) Patil", "!!!", "@@", "Kiran", "Rohan Rao", "Rao"];
    private static readonly string[] Phones = ["9820011111", "+91 98200 11111", "98200", "", "9000000001", "09000000001", "12345678901234", "9811122233"];
    private static readonly string[] Statuses = [CrmStay.Held, CrmStay.Confirmed, CrmStay.InHouse, CrmStay.Departed, CrmStay.Cancelled, CrmStay.NoShow, CrmStay.Expired];

    private static string J(object o) => JsonSerializer.Serialize(o);

    private static (List<CrmGuest> Guests, List<CrmStay> Stays, Dictionary<string, string> Aliases) World(int seed)
    {
        var rnd = new Random(seed);
        var guests = Enumerable.Range(1, 40 + rnd.Next(40)).Select(i => new CrmGuest(i * 3 + rnd.Next(3), Names[rnd.Next(Names.Length)], Phones[rnd.Next(Phones.Length)],
            rnd.Next(3) == 0 ? null : $"g{rnd.Next(10)}@example.test", rnd.Next(4) == 0 ? "Quiet room" : null, null)).DistinctBy(g => g.GuestId).ToList();
        var day0 = new DateOnly(2026, 9, 1);
        var stays = Enumerable.Range(1, 100 + rnd.Next(100)).Select(i =>
        {
            var arr = day0.AddDays(rnd.Next(12));
            long? gid = rnd.Next(10) switch { < 4 => null, 4 => 999_999, _ => guests[rnd.Next(guests.Count)].GuestId };
            return new CrmStay(1000 + i, "B" + rnd.Next(100_000).ToString("D5", CultureInfo.InvariantCulture) + "-" + i.ToString(CultureInfo.InvariantCulture), gid, Names[rnd.Next(Names.Length)],
                arr, arr.AddDays(1 + rnd.Next(3)), Statuses[rnd.Next(Statuses.Length)], rnd.Next(3) == 0 ? null : (101 + rnd.Next(4)).ToString(CultureInfo.InvariantCulture),
                rnd.Next(1_000_000), rnd.Next(500_000));
        }).ToList();
        var keys = CustomerRules.Index(guests, stays, new Dictionary<string, string>(), day0).Select(c => c.Key).ToList();
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < keys.Count && aliases.Count < 6; i += 3) aliases.TryAdd(keys[i], keys[i + 1]);
        if (keys.Count > 8)
        {
            aliases.TryAdd(keys[^1], keys[^2]); // chain
            aliases.TryAdd(keys[^2], keys[^3]);
            aliases.TryAdd(keys[^4], keys[^5]); // cycle
            aliases.TryAdd(keys[^5], keys[^4]);
        }

        return (guests, stays, aliases);
    }

    private static List<CrmGuestHead> HeadsOf(IEnumerable<CrmGuest> gs) => gs.Select(g => new CrmGuestHead(g.GuestId, g.Name, g.Phone)).ToList();

    // As SqlCustomerReadStore.HeadsAsync gives them: no expired holds; a guest id only when it is a profile of the property, and then no booking name.
    private static List<CrmStayHead> HeadsOf(IEnumerable<CrmStay> ss, IReadOnlyCollection<CrmGuest> gs) => ss.Where(s => s.Status != CrmStay.Expired).Select(s =>
    {
        var own = s.GuestId is { } id && gs.Any(g => g.GuestId == id);
        return new CrmStayHead(s.ReservationId, s.PublicNo, own ? s.GuestId : null, own ? null : s.GuestName, s.Arrival);
    }).ToList();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(11)]
    [InlineData(42)]
    [InlineData(99)]
    [InlineData(2026)]
    public void Key_pass_matches_the_full_index_and_one_customer_rebuilds_from_its_own_rows(int seed)
    {
        var (guests, stays, aliases) = World(seed);
        var today = new DateOnly(2026, 9, 6);
        var index = CustomerRules.Index(guests, stays, aliases, today);
        var hx = CustomerRules.Heads(HeadsOf(guests), HeadsOf(stays, guests), aliases);
        var heads = hx.Heads;
        Assert.Equal(index.Select(c => (c.Key, c.Name, c.Phone, c.Stays[0].Arrival)), heads.Select(h => (h.Key, h.Name, h.Phone, h.LatestArrival)));
        foreach (var (c, h) in index.Zip(heads))
        {
            var (res, gids) = hx.RowsOf([h]);
            Assert.Equal(c.Stays.Select(s => s.ReservationId).Order(), res.Order());
            var own = CustomerRules.Index(guests.Where(g => gids.Contains(g.GuestId)).ToList(), stays.Where(s => res.Contains(s.ReservationId)).ToList(), aliases, today);
            Assert.Equal(J(c), J(Assert.Single(own)));
        }

        // a page of several customers rebuilds them all at once, each the same
        var page = heads.Skip(2).Take(7).ToList();
        var (pres, pgids) = hx.RowsOf(page);
        var some = CustomerRules.Index(guests.Where(g => pgids.Contains(g.GuestId)).ToList(), stays.Where(s => pres.Contains(s.ReservationId)).ToList(), aliases, today)
            .ToDictionary(c => c.Key);
        Assert.Equal(page.Select(h => J(index.Single(c => c.Key == h.Key))), page.Select(h => J(some[h.Key])));
    }

    [Theory]
    [InlineData(5, "rao", true)]
    [InlineData(5, "RAO asha", true)]
    [InlineData(6, "राव", true)]
    [InlineData(8, "98200", true)]
    [InlineData(8, "98200", false)]
    [InlineData(9, "11", true)]
    [InlineData(9, "  ", true)]
    [InlineData(10, "kiran 9", true)]
    [InlineData(10, "zzz", true)]
    public void Search_on_heads_gives_the_same_customers_in_the_same_order(int seed, string q, bool phoneOk)
    {
        var (guests, stays, aliases) = World(seed);
        var index = CustomerRules.Index(guests, stays, aliases, new DateOnly(2026, 9, 6));
        var heads = CustomerRules.Heads(HeadsOf(guests), HeadsOf(stays, guests), aliases).Heads;
        Assert.Equal(CustomerRules.Search(index, q, phoneOk).Select(c => c.Key), CustomerRules.SearchHeads(heads, q, phoneOk).Select(h => h.Key));
    }

    // The pre-PER-01 NormName / Digits, verbatim, as the reference for the allocation-free fast paths.
    private static string OldNormName(string? name)
    {
        var s = (name ?? string.Empty).Replace("(fictional)", " ", StringComparison.OrdinalIgnoreCase).Trim().Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant();
        var sb = new System.Text.StringBuilder();
        var space = true;
        foreach (var r in s.EnumerateRunes())
        {
            var cat = System.Text.Rune.GetUnicodeCategory(r);
            if (System.Text.Rune.IsLetterOrDigit(r) || cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark) { sb.Append(r.ToString()); space = false; }
            else if (!space) { sb.Append(' '); space = true; }
        }

        return sb.ToString().Trim();
    }

    private static string OldDigits(string? s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in s ?? string.Empty) if (char.IsAsciiDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    [Fact]
    public void Fast_name_and_digit_normalisation_matches_the_old_rule()
    {
        var rnd = new Random(5);
        const string alphabet = "aAzZ09 \t\r\n.-_'()!@#~\u0001\u007fRaoASHA(fictional)(FICTIONAL)";
        var samples = new List<string?> { null, "", " ", "Asha Rao", "  ASHA   RAO  ", "a", "A", "1", "!!!", "Meera (fictional) Patil", "राव शर्मा", "Ｒａｏ", "x\u0301y", new string('a', 300), new string('-', 300) + "B" };
        for (var i = 0; i < 4000; i++) samples.Add(new string(Enumerable.Range(0, rnd.Next(0, 40)).Select(_ => alphabet[rnd.Next(alphabet.Length)]).ToArray()));
        foreach (var s in samples)
        {
            Assert.Equal(OldNormName(s), CustomerRules.NormName(s));
            Assert.Equal(OldDigits(s), CustomerRules.Digits(s));
        }
    }}
