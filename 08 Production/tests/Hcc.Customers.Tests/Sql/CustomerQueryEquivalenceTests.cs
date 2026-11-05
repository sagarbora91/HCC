using System.Globalization;
using System.Text;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Customers;
using Hcc.Application.Messaging;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Customers;
using Hcc.Customers.Tests.Harness;
using Hcc.Domain.Customers;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Customers;

namespace Hcc.Customers.Tests.Sql;

/// <summary>
/// PER-01: the pre-rewrite customer list and profile (whole-property load, then search / page in memory), kept verbatim as the reference for the
/// equivalence tests. Only the test project uses it.
/// </summary>
internal sealed class PreRewriteCustomerQueries(IAccessPolicy policy, ICustomerReadStore reads, IMessagingDataFactory messaging)
{
    private async Task<(bool Contact, bool Money)> ViewerAsync(int propertyId, int principalId, CancellationToken ct)
    {
        await policy.RequireAsync(propertyId, principalId, CustomerPermissions.View, null, null, ct).ConfigureAwait(false);
        return (await policy.HasAsync(propertyId, principalId, CustomerPermissions.Edit, null, ct).ConfigureAwait(false),
            await policy.HasAsync(propertyId, principalId, CustomerPermissions.Money, null, ct).ConfigureAwait(false));
    }

    public async Task<CustomerList> ListAsync(int propertyId, int principalId, string? q, string? tag, int page, int pageSize, CancellationToken ct = default)
    {
        var (contact, money) = await ViewerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, CustomerQueries.MaxPageSize);
        var src = await reads.LoadAsync(propertyId, ct).ConfigureAwait(false);
        var tags = await reads.TagsAsync(propertyId, ct).ConfigureAwait(false);
        var index = src.Index();
        var keys = await CustomerKeySpace.ForAsync(reads, propertyId, src, index, ct).ConfigureAwait(false);
        var found = CustomerRules.Search(index, q, contact);
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var t = CustomerRules.CleanTag(tag);
            found = found.Where(c => tags.TryGetValue(c.Key, out var ts) && ts.Contains(t)).ToList();
        }

        var pageRows = found.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var legacy = await LegacyRoll.ForAsync(reads, propertyId, pageRows.Select(c => c.Key).ToList(), src.Aliases, ct).ConfigureAwait(false);
        var rows = pageRows.Select(c => CustomerViews.Summary(c, contact, money, tags.GetValueOrDefault(c.Key) ?? [], keys, legacy.GetValueOrDefault(c.Key))).ToList();
        return new CustomerList(rows, found.Count, page, pageSize);
    }

    public async Task<CustomerProfile> ProfileAsync(int propertyId, int principalId, string? key, CancellationToken ct = default)
    {
        var (contact, money) = await ViewerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var src = await reads.LoadAsync(propertyId, ct).ConfigureAwait(false);
        var index = src.Index();
        var keys = await CustomerKeySpace.ForAsync(reads, propertyId, src, index, ct).ConfigureAwait(false);
        var k = CustomerRules.Resolve(keys.ResolveInbound(key, contact), src.Aliases);
        var c = index.FirstOrDefault(x => x.Key == k) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Customer not found.");
        var o = await reads.OverlayAsync(propertyId, c.Key, ct).ConfigureAwait(false);
        var prefs = o.Pref?.Preferences ?? string.Join(" | ", src.Guests.Where(g => c.GuestIds.Contains(g.GuestId) && !string.IsNullOrWhiteSpace(g.Preferences)).Select(g => g.Preferences!.Trim()).Distinct());
        var consent = await messaging.ReadAsync(async (d, t) =>
        {
            bool wa = true, sms = true, em = true, opt = false;
            foreach (var gid in c.GuestIds)
            {
                var s = (await d.GetStoredConsentAsync(propertyId, gid, t).ConfigureAwait(false))?.Consent;
                if (s is null) continue;
                wa &= s.WhatsApp;
                sms &= s.Sms;
                em &= s.Email;
                opt |= s.OptOut;
            }

            return new CustomerConsentView(wa, sms, em, opt);
        }, ct).ConfigureAwait(false);
        var legacyStays = new List<CrmLegacyStay>();
        foreach (var lk in new[] { c.Key }.Concat(src.Aliases.Where(a => CustomerRules.Resolve(a.Key, src.Aliases) == c.Key).Select(a => a.Key)).Distinct(StringComparer.Ordinal))
            legacyStays.AddRange(await reads.LegacyStaysAsync(propertyId, lk, ct).ConfigureAwait(false));
        legacyStays = legacyStays.OrderByDescending(s => s.Arrival).ThenByDescending(s => s.StayId).ToList();
        var roll = legacyStays.Count == 0 ? LegacyRoll.None
            : new LegacyRoll(legacyStays.Count, legacyStays.Sum(s => s.Nights), legacyStays.Sum(s => s.NetPaise ?? 0), legacyStays.Min(s => s.Arrival));
        var summary = CustomerViews.Summary(c, contact, money, o.Tags, keys, roll);
        return new CustomerProfile(summary.Key, summary.Name, summary.Phone, summary.Email, !contact, c.GuestIds, c.Stays.Select(s => CustomerViews.Stay(s, money)).ToList(),
            c.Completed.Count, summary.Nights, summary.LifetimePaise, money ? c.AvgRatePaise : null, summary.FirstStay, summary.LastStay,
            c.Current is null ? null : CustomerViews.Stay(c.Current, money), c.Upcoming.Select(s => CustomerViews.Stay(s, money)).ToList(), c.Cancelled, c.TopRooms,
            prefs.Length == 0 ? null : prefs, o.Pref is null ? null : VersionToken.ToWire(o.Pref.RowVer), o.Tags, o.Notes.Select(CustomerViews.Note).ToList(), consent, o.MergedFrom.Select(keys.TokenOf).ToList(),
            legacyStays.Select(s => CustomerViews.Legacy(s, money)).ToList(), money ? roll.NetPaise : null);
    }
}

/// <summary>
/// PER-01 golden equivalence: on a seeded hotel (CrmWorld + about 120 random guests and 300 bookings: shared / short / empty mobiles, case and script
/// variants, symbol-only names, bookings without a profile and one registered to another property's guest, every status, folios with room nights,
/// a reversed night and service lines, room segments and reserved nights, approved merge aliases incl. a chain, tags) the rewritten
/// <see cref="CustomerQueries"/> returns byte-for-byte the same JSON as the pre-rewrite query for every role, search text, tag, page and page size,
/// and the same profile for every customer (ck_ token, raw name key, raw mobile key, merged-away key), including the NOT_FOUND cases.
/// </summary>
[Trait("Category", "Sql")]
public class CustomerQueryEquivalenceTests : IAsyncLifetime
{
    private CrmWorld _w = null!;
    private PreRewriteCustomerQueries _old = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] Names = ["Asha Rao", "ASHA RAO", "asha  rao", "Vikram Shah", "राव शर्मा", "Meera (fictional) Patil", "!!!", "@@", "Kiran", "Rohan Rao", "Rao", "O'Brien Rao"];
    private static readonly string[] Phones = ["9820011111", "+91 98200 11111", "98200", "", "9000000001", "09000000001", "12345678901234", "9811122233", "9876500001", "9876500002"];
    private static readonly string[] Statuses = ["Held", "Confirmed", "InHouse", "Departed", "Cancelled", "NoShow", "Expired"];
    private static readonly string[] Roles = ["owner", "manager", "reception", "cashier"];

    public async ValueTask InitializeAsync()
    {
        _w = await CrmWorld.CreateAsync(Ct);
        _old = new PreRewriteCustomerQueries(_w.Policy, new SqlCustomerReadStore(_w.Platform.Connections), _w.Messaging);
        await SeedAsync();
    }

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static string S(string v) => "N'" + v.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string J(object o) => JsonSerializer.Serialize(o);

    private async Task SeedAsync()
    {
        var rnd = new Random(20261105);
        var p = _w.PropertyId;
        var rec = _w.Users["reception"];
        var sql = new StringBuilder("SET NOCOUNT ON;\n");
        sql.Append("DECLARE @p2 INT; INSERT inv.property(code, name, time_zone, business_date) VALUES ('OTHR', N'Other Hotel', 'Asia/Kolkata', '2026-10-24'); SET @p2 = SCOPE_IDENTITY();\n");
        sql.Append("INSERT gst.guest(property_id, full_name, phone, email, verified, origin, created_utc) VALUES (@p2, N'Other Guest', '9999999999', NULL, 1, 'manual', '2026-10-01');\n");
        sql.Append("DECLARE @other BIGINT = SCOPE_IDENTITY();\n");
        sql.Append("DECLARE @g TABLE(i INT PRIMARY KEY, id BIGINT);\n");
        for (var i = 0; i < 120; i++)
        {
            var email = rnd.Next(3) == 0 ? "NULL" : S($"g{rnd.Next(15)}@example.test");
            var pref = rnd.Next(4) == 0 ? S(rnd.Next(2) == 0 ? "Quiet room" : "  Extra pillow ") : "NULL";
            sql.Append(CultureInfo.InvariantCulture, $"INSERT gst.guest(property_id, full_name, phone, email, preferences, verified, origin, created_utc) VALUES ({p}, {S(Names[rnd.Next(Names.Length)])}, '{(rnd.Next(5) < 2 ? Phones[rnd.Next(Phones.Length)] : "98" + rnd.Next(100_000_000).ToString("D8", CultureInfo.InvariantCulture))}', {email}, {pref}, 1, 'manual', '2026-10-01'); INSERT @g VALUES ({i}, SCOPE_IDENTITY());\n");
        }

        sql.Append("DECLARE @r BIGINT, @f BIGINT, @c BIGINT, @s BIGINT;\n");
        for (var i = 0; i < 300; i++)
        {
            var arr = new DateOnly(2026, 9, 20).AddDays(rnd.Next(50));
            var dep = arr.AddDays(1 + rnd.Next(3));
            var status = Statuses[rnd.Next(Statuses.Length)];
            var reg = rnd.Next(10) switch { < 3 => "none", 3 => "other", _ => "g" };
            var gi = rnd.Next(120);
            sql.Append(CultureInfo.InvariantCulture, $"INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status) VALUES ({p}, 'EQ-{rnd.Next(1000):D3}-{i:D3}', {S(Names[rnd.Next(Names.Length)])}, '{arr:yyyy-MM-dd}', '{dep:yyyy-MM-dd}', '{status}'); SET @r = SCOPE_IDENTITY();\n");
            if (reg == "g") sql.Append(CultureInfo.InvariantCulture, $"INSERT gst.registration(reservation_id, guest_id, registered_utc, registered_by) SELECT @r, id, '2026-10-01', {rec} FROM @g WHERE i = {gi};\n");
            if (reg == "other") sql.Append(CultureInfo.InvariantCulture, $"INSERT gst.registration(reservation_id, guest_id, registered_utc, registered_by) VALUES (@r, @other, '2026-10-01', {rec});\n");
            if (rnd.Next(3) == 0)
                sql.Append(CultureInfo.InvariantCulture, $"INSERT inv.room_night(room_id, night, reservation_id) SELECT room_id, '{new DateOnly(2027, 1, 1).AddDays(i):yyyy-MM-dd}', @r FROM inv.room WHERE property_id = {p} AND room_no = '{(rnd.Next(2) == 0 ? "104" : "105")}';\n");
            if (status is not ("InHouse" or "Departed") && rnd.Next(4) != 0) continue;
            sql.Append("INSERT bill.folio(reservation_id) VALUES (@r); SET @f = SCOPE_IDENTITY();\n");
            var nights = rnd.Next(4);
            for (var n = 0; n < nights; n++)
            {
                var amt = 100_000 + rnd.Next(400_000);
                sql.Append(CultureInfo.InvariantCulture, $"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES (@f, 'room', '{arr.AddDays(n):yyyy-MM-dd}', N'Room night', {amt}, {(amt > 750_000 ? 1800 : 500)}, {amt / 20}); SET @c = SCOPE_IDENTITY();\n");
                if (n == 0 && rnd.Next(5) == 0)
                    sql.Append(CultureInfo.InvariantCulture, $"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise, reverses_charge_id) VALUES (@f, 'room', '{arr:yyyy-MM-dd}', N'Reversal', {-amt}, 500, {-(amt / 20)}, @c);\n");
            }

            if (rnd.Next(3) == 0)
                sql.Append(CultureInfo.InvariantCulture, $"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES (@f, 'service', '{arr:yyyy-MM-dd}', N'Laundry', {rnd.Next(50_000)}, 1800, 0);\n");
            if (reg == "g" && status is "InHouse" or "Departed" && rnd.Next(2) == 0)
            {
                sql.Append(CultureInfo.InvariantCulture, $"""
                    INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by, checked_out_date, checked_out_utc, checked_out_by)
                      SELECT {p}, @r, id, @f, 'CheckedOut', '{arr:yyyy-MM-dd}', '{dep:yyyy-MM-dd}', '{arr:yyyy-MM-dd}', '{arr:yyyy-MM-dd}', {rec}, '{dep:yyyy-MM-dd}', '{dep:yyyy-MM-dd}', {rec} FROM @g WHERE i = {gi};
                    SET @s = SCOPE_IDENTITY();
                    INSERT gst.stay_segment(stay_id, seq, room_id, from_date, to_date) SELECT @s, 1, room_id, '{arr:yyyy-MM-dd}', '{arr:yyyy-MM-dd}' FROM inv.room WHERE property_id = {p} AND room_no = '101';
                    INSERT gst.stay_segment(stay_id, seq, room_id, from_date, to_date) SELECT @s, 2, room_id, '{arr:yyyy-MM-dd}', '{dep:yyyy-MM-dd}' FROM inv.room WHERE property_id = {p} AND room_no = '{(rnd.Next(2) == 0 ? "102" : "104")}';

                    """);
            }
        }

        await _w.ExecAsync(sql.ToString(), Ct);

        // merges (approved aliases incl. a chain) and tags on raw keys taken from the full index
        var keys = (await new SqlCustomerReadStore(_w.Platform.Connections).LoadAsync(p, Ct)).Index().Select(c => c.Key).ToList();
        var owner = _w.Users["owner"];
        var mgr = _w.Users["manager"];
        var post = new StringBuilder("SET NOCOUNT ON; DECLARE @m BIGINT;\n");
        void Alias(string from, string to) => post.Append(CultureInfo.InvariantCulture, $"""
            INSERT crm.customer_merge(property_id, source_key, target_key, source_name, target_name, reason, status, proposed_by, proposed_utc, decided_by, decided_utc)
              VALUES ({p}, {S(from)}, {S(to)}, N'a', N'b', N'Same person in the test', 'Approved', {owner}, '2026-10-20', {mgr}, '2026-10-21');
            SET @m = SCOPE_IDENTITY();
            INSERT crm.customer_alias(property_id, alias_key, target_key, merge_id, created_utc) VALUES ({p}, {S(from)}, {S(to)}, @m, '2026-10-21');

            """);
        Alias(keys[3], keys[4]);
        Alias(keys[7], keys[2]);
        Alias(keys[10], keys[11]); // chain 10 -> 11 -> 12
        Alias(keys[11], keys[12]);
        foreach (var (k, t) in new[] { (keys[0], "vip"), (keys[2], "vip"), (keys[5], "corporate"), (keys[12], "vip"), (keys[20], "late checkout"), (keys[3], "vip") })
            post.Append(CultureInfo.InvariantCulture, $"INSERT crm.customer_tag(property_id, customer_key, tag, added_by, added_utc) VALUES ({p}, {S(k)}, {S(t)}, {owner}, '2026-10-21');\n");
        await _w.ExecAsync(post.ToString(), Ct);
    }

    private async Task<string> Outcome(Func<Task<object>> call)
    {
        try
        {
            return J(await call());
        }
        catch (HccException e)
        {
            return "ERR " + e.Code;
        }
    }

    [Fact]
    public async Task List_is_identical_to_the_pre_rewrite_query_for_every_role_search_tag_and_page()
    {
        var checkedRows = 0;
        foreach (var role in Roles)
        {
            var u = _w.Users[role];
            foreach (var q in new string?[] { null, "", "rao", "asha rao", "RAO asha", "राव", "98200", "11", "9", "zz", "kiran 9", "o'brien" })
                foreach (var tag in new string?[] { null, "vip", "Corporate" })
                    foreach (var (page, size) in new[] { (1, 50), (1, 100), (2, 7), (3, 7), (1, 1), (0, 0), (40, 7), (1, 500) })
                    {
                        var oldJ = await Outcome(async () => await _old.ListAsync(_w.PropertyId, u, q, tag, page, size, Ct));
                        var newJ = await Outcome(async () => await _w.Queries.ListAsync(_w.PropertyId, u, q, tag, page, size, Ct));
                        Assert.True(oldJ == newJ, $"{role} q={q} tag={tag} page={page} size={size}\nold {oldJ}\nnew {newJ}");
                        checkedRows++;
                    }
        }

        Assert.Equal(4 * 12 * 3 * 8, checkedRows);
        var all = await _w.Queries.ListAsync(_w.PropertyId, _w.Users["owner"], null, null, 1, 100, Ct);
        Assert.True(all.Total > 60, $"seed too small: {all.Total}");
        Assert.All(all.Customers, c => Assert.True(CustomerKeyToken.IsToken(c.Key)));
    }

    [Fact]
    public async Task Profile_is_identical_to_the_pre_rewrite_query_for_every_customer_and_role()
    {
        var reads = new SqlCustomerReadStore(_w.Platform.Connections);
        var src = await reads.LoadAsync(_w.PropertyId, Ct);
        var raw = src.Index().Select(c => c.Key).Concat(src.Aliases.Keys).ToList();
        var inputs = new List<string?> { null, "", "ck_AAAAAAAAAAAAAAAAAAAAAA", "n:nobody here", "9000000009" };
        foreach (var k in raw)
        {
            inputs.Add(await _w.Queries.TokenAsync(_w.PropertyId, k, Ct));
            inputs.Add(k); // raw: n: keys work for all; raw mobile keys only for contact roles
        }

        var compared = 0;
        foreach (var role in Roles)
        {
            var u = _w.Users[role];
            foreach (var key in inputs)
            {
                var oldJ = await Outcome(async () => await _old.ProfileAsync(_w.PropertyId, u, key, Ct));
                var newJ = await Outcome(async () => await _w.Queries.ProfileAsync(_w.PropertyId, u, key, Ct));
                Assert.True(oldJ == newJ, $"{role} key={key}\nold {oldJ}\nnew {newJ}");
                if (!oldJ.StartsWith("ERR", StringComparison.Ordinal)) compared++;
            }
        }

        Assert.True(compared > 200, $"too few profiles compared: {compared}");
    }

    [Fact]
    public async Task Merge_list_tokens_are_unchanged()
    {
        var m = await _w.Queries.MergesAsync(_w.PropertyId, _w.Users["owner"], null, Ct);
        Assert.Equal(4, m.Merges.Count);
        foreach (var x in m.Merges)
        {
            Assert.True(CustomerKeyToken.IsToken(x.SourceKey));
            Assert.True(CustomerKeyToken.IsToken(x.TargetKey));
        }

        var src = await new SqlCustomerReadStore(_w.Platform.Connections).LoadAsync(_w.PropertyId, Ct);
        var expected = new List<string>();
        foreach (var a in src.Aliases.OrderBy(a => a.Key, StringComparer.Ordinal)) expected.Add(await _w.Queries.TokenAsync(_w.PropertyId, a.Key, Ct));
        Assert.Equal(expected.Order(StringComparer.Ordinal), m.Merges.Select(x => x.SourceKey).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Load_some_ignores_other_properties_and_empty_sets()
    {
        var reads = new SqlCustomerReadStore(_w.Platform.Connections);
        var empty = await reads.LoadSomeAsync(_w.PropertyId, [], [], Ct);
        Assert.Empty(empty.Guests);
        Assert.Empty(empty.Stays);
        var otherGuest = Convert.ToInt64(await _w.ScalarAsync("SELECT guest_id FROM gst.guest WHERE full_name = N'Other Guest';", Ct), CultureInfo.InvariantCulture);
        var mine = _w.Res["HCC-A1"];
        var d = await reads.LoadSomeAsync(_w.PropertyId, [mine, mine, 987_654_321], [otherGuest, _w.Guest["asha1"]], Ct);
        Assert.Equal([_w.Guest["asha1"]], d.Guests.Select(g => g.GuestId));
        Assert.Equal([mine], d.Stays.Select(s => s.ReservationId));
        Assert.Equal(840_000, d.Stays[0].TotalPaise);
    }
}
