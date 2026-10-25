using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Stays;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Housekeeping;
using Hcc.Stays.Tests.Harness;

namespace Hcc.Stays.Tests.Sql;

/// <summary>Concurrent checkout (one winner) and outbox → housekeeping exactly-once (spike T09 dedup + causal key).</summary>
[Trait("Category", "Sql")]
public class ConcurrencySqlTests : IAsyncLifetime
{
    private P3World _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await P3World.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    /// <summary>Settles an in-house BASE stay (P4 stand-in) and returns (stayId, version).</summary>
    private async Task<(long StayId, string Version)> SettledStayAsync(string booking)
    {
        var stay = _w.Seed.Stays[booking];
        var folio = await _w.Seed.FolioOfAsync(_w.Seed.Reservations[booking], Ct);
        var rate = Convert.ToInt64(await _w.Db.ScalarAsync($"SELECT CASE t.code WHEN 'Suite' THEN 450000 WHEN 'Deluxe' THEN 300000 ELSE 200000 END FROM gst.stay_segment s JOIN inv.room r ON r.room_id = s.room_id JOIN inv.room_type t ON t.room_type_id = r.room_type_id WHERE s.stay_id = {stay};", Ct), System.Globalization.CultureInfo.InvariantCulture);
        await _w.Seed.PostLinesAsync(folio, new[] { "2026-10-23", "2026-10-24", "2026-10-25" }.Select(d => new GoldenLine("room", d, rate, 1000)), Ct);
        await _w.Seed.ReceiptAsync(folio, "payment", "UPI", rate * 3 * 11 / 10, Ct);
        return (stay, await _w.Seed.StayVersionAsync(stay, Ct));
    }

    [Fact]
    public async Task Eight_parallel_checkouts_of_one_stay_with_different_keys_have_exactly_one_winner()
    {
        var (stay, ver) = await SettledStayAsync("HCC-1041");
        using var go = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
        {
            go.Wait(Ct);
            try
            {
                await _w.Stays.CheckoutStayAsync(_w.Ctx(Users.Reception, "race-" + i), new CheckoutStayRequest(stay, ver), Ct);
                return "OK";
            }
            catch (HccException e)
            {
                return e.Code.ToString();
            }
        }, Ct)).ToList();
        go.Set();
        var results = await Task.WhenAll(tasks);
        Assert.Equal(1, results.Count(r => r == "OK"));
        Assert.All(results.Where(r => r != "OK"), r => Assert.Contains(r, new[] { "INVALID_TRANSITION", "STALE_VERSION" }));
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'StayDeparted' AND aggregate_id = {stay};", Ct));
        Assert.Equal(1, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'CHECKOUT';", Ct));
        Assert.Equal("Dirty", await _w.Db.StringAsync($"SELECT hk_status FROM inv.room WHERE room_id = {_w.Seed.Rooms["101"]};", Ct));
        Assert.Equal(1, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.idempotency WHERE operation = 'CheckoutStay';", Ct));
    }

    [Fact]
    public async Task Parallel_projectors_and_redelivery_create_exactly_one_task_per_departed_stay()
    {
        var stays = new List<long>();
        foreach (var b in new[] { "HCC-1041", "HCC-1042", "HCC-1043", "HCC-1044", "HCC-1045" })
        {
            var (stay, ver) = await SettledStayAsync(b);
            await _w.Stays.CheckoutStayAsync(_w.Ctx(Users.Reception, "co-" + b), new CheckoutStayRequest(stay, ver), Ct);
            stays.Add(stay);
        }

        var applied = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => _w.Projector.RunOnceAsync(ct: Ct), Ct)));
        Assert.Equal(5, applied.Sum());
        foreach (var s in stays)
            Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM ops.hk_task WHERE stay_id = {s} AND kind = 'checkout' AND status = 'ToDo';", Ct));

        // Redelivery of an already-processed event through the frozen consumer is skipped.
        var evt = await LoadEventAsync(stays[0]);
        Assert.False(await _w.Db.Platform.Outbox.DeliverAsync(HousekeepingTaskProjector.ConsumerName, evt, HousekeepingTaskProjector.ApplyAsync, Ct));
        // A second consumer name (e.g. a rebuilt projector) still cannot duplicate the task: the causal key holds.
        Assert.True(await _w.Db.Platform.Outbox.DeliverAsync("housekeeping-rebuild", evt, HousekeepingTaskProjector.ApplyAsync, Ct));
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM ops.hk_task WHERE stay_id = {stays[0]};", Ct));
        Assert.Equal(5, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.consumer_dedup WHERE consumer = 'housekeeping-tasks';", Ct));
    }

    private async Task<OutboxEvent> LoadEventAsync(long stayId)
    {
        await using var conn = await _w.Db.Platform.Connections.OpenAsync(Ct);
        await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"SELECT outbox_id, event_type, aggregate_id, payload, created_utc FROM plat.outbox WHERE event_type = 'StayDeparted' AND aggregate_id = {stayId};", conn);
        await using var rd = await cmd.ExecuteReaderAsync(Ct);
        await rd.ReadAsync(Ct);
        return new OutboxEvent(rd.GetInt64(0), rd.GetString(1), rd.GetInt64(2), rd.GetString(3), rd.GetDateTime(4));
    }

    [Fact]
    public async Task Failed_checkout_leaves_everything_unchanged_and_the_same_key_can_retry()
    {
        var stay = _w.Seed.Stays["HCC-1046"];
        var ver = await _w.Seed.StayVersionAsync(stay, Ct);
        await Expect.ErrorAsync(HccErrorCode.INVALID_TRANSITION, () => _w.Stays.CheckoutStayAsync(_w.Ctx(Users.Reception, "retry"), new CheckoutStayRequest(stay, ver), Ct));
        Assert.Equal("InHouse", await _w.Db.StringAsync($"SELECT status FROM gst.stay WHERE stay_id = {stay};", Ct));
        Assert.Equal(0, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.outbox;", Ct));
        var (_, v2) = await SettledStayAsync("HCC-1046");
        var ok = await _w.Stays.CheckoutStayAsync(_w.Ctx(Users.Reception, "retry"), new CheckoutStayRequest(stay, v2), Ct);
        Assert.False(ok.Replayed);
        Assert.Equal(ver, v2); // settling the folio does not touch the stay row
    }
}
