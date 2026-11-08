using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Application.Ota;
using Hcc.Domain.Configuration;
using Hcc.Infrastructure.SqlServer.Ota;

namespace Hcc.Ota.Tests.Sql;

/// <summary>
/// [F8D-TESTS-B] F8-QW-NS (QWQ-2) overbooking cap on the channel push, with a seeded setting: <c>yld.overbook.&lt;room type code&gt;</c> is added to the
/// availability of the staff push preview AND of the automatic push the projector queues, for that room type only; cap 0 / unset changes nothing;
/// the cap is clamped to 0..10. Domain figures of <c>OtaRules.PushAvail</c> are in NoShowOverbookTests. Test files only.
/// </summary>
[Trait("Category", "Sql")]
public sealed class OtaOverbookCapSqlTests : OtaSqlTest
{
    private sealed class Caps(Dictionary<string, long> values) : ISettingsReader
    {
        public Task<ConfigValue> GetAsync(int propertyId, string key, DateOnly? on = null, CancellationToken ct = default) =>
            Task.FromResult(values.TryGetValue(key, out var v) ? ConfigValue.Of(v) : ConfigValue.Of(0L));

        public Task<ConfigValue> GetAsync(ICommandTransaction tx, int propertyId, string key, DateOnly? on = null, CancellationToken ct = default) => GetAsync(propertyId, key, on, ct);
    }

    private OtaQueries Capped(Dictionary<string, long> caps) =>
        new(F.Store, new Hcc.Infrastructure.SqlServer.Inventory.SqlInventoryStore(F.Db.Platform.Connections), F.Inventory, F.Booking, F.Policy, F.Clock, new Caps(caps));

    private static string Key(string type) => MeasurementCatalogue.OverbookKey(type);

    [Fact]
    public async Task Cap_adds_rooms_to_the_staff_push_preview_of_that_type_only_zero_or_unset_changes_nothing()
    {
        var channel = F.ChannelIds["BOOKNEST"];
        var plain = (await F.Queries.PushPreviewAsync(1, F.Reception, channel, Ct)).Rows;

        var unset = (await Capped([]).PushPreviewAsync(1, F.Reception, channel, Ct)).Rows;
        Assert.Equal(plain, unset);                               // unset = 0 = identical output

        var capped = (await Capped(new() { [Key("Deluxe")] = 2 }).PushPreviewAsync(1, F.Reception, channel, Ct)).Rows;
        Assert.Equal(plain.Count, capped.Count);
        for (var i = 0; i < plain.Count; i++)
        {
            var (a, b) = (plain[i], capped[i]);
            Assert.Equal((a.RoomType, a.Night, a.RatePaise), (b.RoomType, b.Night, b.RatePaise));
            if (a.RoomType == "Deluxe")
            {
                Assert.Equal(a.Avail + 2, b.Avail);
                // Closed = stop-sold or nothing left: a sold-out Deluxe night reopens with the cap.
                Assert.Equal(b.Avail == 0, b.Closed);
            }
            else Assert.Equal(a, b);                              // other room types untouched
        }

        Assert.Contains(plain, r => r.RoomType == "Deluxe");
    }

    [Fact]
    public async Task Cap_is_clamped_between_zero_and_ten()
    {
        var channel = F.ChannelIds["BOOKNEST"];
        var plain = (await F.Queries.PushPreviewAsync(1, F.Reception, channel, Ct)).Rows;
        var huge = (await Capped(new() { [Key("Deluxe")] = 99 }).PushPreviewAsync(1, F.Reception, channel, Ct)).Rows;
        var negative = (await Capped(new() { [Key("Deluxe")] = -4 }).PushPreviewAsync(1, F.Reception, channel, Ct)).Rows;
        for (var i = 0; i < plain.Count; i++)
        {
            if (plain[i].RoomType != "Deluxe") continue;
            Assert.Equal(plain[i].Avail + MeasurementCatalogue.OverbookMax, huge[i].Avail);
            Assert.Equal(plain[i].Avail, negative[i].Avail);
        }
    }

    [Fact]
    public async Task Cap_is_added_to_the_automatic_push_the_projector_queues_and_matches_the_capped_preview()
    {
        while (await F.Projector.RunOnceAsync(ct: Ct) > 0) { }
        var caps = new Dictionary<string, long> { [Key("Deluxe")] = 3 };
        var projector = new OtaChannelManagerProjector(F.Db.Platform.Outbox, F.Db.Platform.Connections, F.Clock, new Caps(caps));

        await F.AcceptAsync("F1");                                 // BookNest Deluxe 27 → 29 Oct
        await projector.RunOnceAsync(ct: Ct);

        var push = Convert.ToInt64(await F.Db.ScalarAsync($"SELECT MAX(push_id) FROM ota.push WHERE origin = 'Inventory' AND channel_id = {F.ChannelIds["BOOKNEST"]};", Ct), System.Globalization.CultureInfo.InvariantCulture);
        var plain = (await F.Queries.PushPreviewAsync(1, F.Reception, F.ChannelIds["BOOKNEST"], Ct)).Rows.Where(r => r.RoomType == "Deluxe" && r.Night is "2026-10-27" or "2026-10-28").ToList();
        Assert.Equal(2, plain.Count);
        foreach (var r in plain)
        {
            var stored = await CountAsync($"SELECT avail FROM ota.push_row WHERE push_id = {push} AND channel_code = 'BN-DLX' AND night = '{r.Night}';");
            Assert.Equal(r.Avail + 3, stored);                     // uncapped availability now, plus the cap
        }
    }
}
