using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Housekeeping;
using Hcc.Application.Stays;
using Hcc.Application.Closing;
using Hcc.Application.Commands;
using Hcc.Domain.Inventory;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Closing;
using Hcc.Infrastructure.SqlServer.Housekeeping;
using Hcc.Infrastructure.SqlServer.Inventory;
using Hcc.Infrastructure.SqlServer.Stays;

namespace Hcc.Stays.Tests.Harness;

/// <summary>Flat 5 % GST per night (the real master is exercised elsewhere; here only the snapshot rows matter).</summary>
public sealed class FlatPricer : IStayNightPricer
{
    public Task<IReadOnlyList<PricedNight>> PriceAsync(int propertyId, DateOnly from, DateOnly to, long ratePaise, CancellationToken ct = default)
        => Task.FromResult(StayPricing.Nights(from, to, ratePaise, _ => 500));
}

/// <summary>
/// W3-7 world: the P3 BASE seed upgraded to full P2 reservations (room, type, ₹4,000 agreed rate, night snapshots; first night of every
/// in-house stay already posted) with the REAL access policy (matrix v1 grants, one user per role) and the real allocator.
/// </summary>
public sealed class StayChangeWorld : IAsyncDisposable
{
    public const long Rate = 400_000;

    private StayChangeWorld(P3World w) => W = w;

    public P3World W { get; }

    public P3Database Db => W.Db;

    public Seed Seed => W.Seed;

    public TestClock Clock => W.Clock;

    public SqlAccessPolicy Policy { get; private set; } = null!;

    public StayChangeCommands Changes { get; private set; } = null!;

    public StayoverCommands Stayover { get; private set; } = null!;

    public HousekeepingCommands Hk { get; private set; } = null!;

    public int Owner, Manager, Reception, Supervisor, Housekeeper;

    public static async Task<StayChangeWorld> CreateAsync(bool guard = true, CancellationToken ct = default)
    {
        var w = await P3World.CreateAsync(ct);
        var world = new StayChangeWorld(w);
        try
        {
            await world.InitAsync(guard, ct);
            return world;
        }
        catch
        {
            await w.DisposeAsync();
            throw;
        }
    }

    private async Task InitAsync(bool guard, CancellationToken ct)
    {
        await Db.ExecAsync($"""
            UPDATE r SET room_id = x.room_id, room_type_id = rm.room_type_id, nightly_rate_paise = {Rate}
            FROM inv.reservation r CROSS APPLY (SELECT TOP (1) room_id FROM inv.room_night WHERE reservation_id = r.reservation_id) x JOIN inv.room rm ON rm.room_id = x.room_id;
            INSERT inv.reservation_night(reservation_id, night, rate_paise, tax_bps, tax_paise) SELECT reservation_id, night, {Rate}, 500, {Rate / 20} FROM inv.room_night;
            """, ct);
        foreach (var stay in Seed.Stays.Values)
        {
            var folio = Convert.ToInt64(await Db.ScalarAsync($"SELECT folio_id FROM gst.stay WHERE stay_id = {stay};", ct), CultureInfo.InvariantCulture);
            await Seed.PostLinesAsync(folio, [new GoldenLine("room", "2026-10-23", Rate, 500)], ct);
        }

        var conns = Db.Platform.Connections;
        Owner = await UserAsync("owner", "OWN", ct);
        Manager = await UserAsync("manager", "MGR", ct);
        Reception = await UserAsync("reception", "REC", ct);
        Supervisor = await UserAsync("supervisor", "SUP", ct);
        Housekeeper = await UserAsync("housekeeper", "HKP", ct);
        Policy = new SqlAccessPolicy(conns, Clock, W.Scope);
        IBusinessDayGuard? g = guard ? new SqlBusinessDayGuard() : null;
        Changes = new StayChangeCommands(Db.Platform.Commands, Policy, new SqlStayChangeStore(), new SqlInventoryAllocator(conns), new FlatPricer(), Clock, g);
        Hk = new HousekeepingCommands(Db.Platform.Commands, Policy, new SqlHousekeepingStore(), W.HkReads, new PolicyTaskAssigneeEligibility(Policy), new SqlRoomBlockPort(conns), new HousekeepingOptions(), Clock);
        Stayover = new StayoverCommands(Db.Platform.Commands, Policy, new SqlStayoverStore(), Clock, g);
    }

    private async Task<int> UserAsync(string login, string role, CancellationToken ct)
    {
        var id = Convert.ToInt32(await Db.ScalarAsync(
            $"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({W.PropertyId}, '{login}', N'User {login}', '2026-01-01');", ct), CultureInfo.InvariantCulture);
        await Db.ExecAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2026-01-01', '2026-01-01');", ct);
        return id;
    }

    public CommandContext Ctx(int principal, string key) => W.Ctx(principal, key);

    public long Stay(string booking) => Seed.Stays[booking];

    public int Room(string no) => Seed.Rooms[no];

    public async Task<long> FolioAsync(string booking, CancellationToken ct = default) =>
        Convert.ToInt64(await Db.ScalarAsync($"SELECT folio_id FROM gst.stay WHERE stay_id = {Stay(booking)};", ct), CultureInfo.InvariantCulture);

    /// <summary>Empty = every maintained type-night count equals its recomputation.</summary>
    public Task<int> CountDriftAsync(CancellationToken ct = default) => Db.CountAsync("SELECT COUNT(*) FROM inv.v_type_night_count_drift;", ct);

    public Task<string> NightsOfAsync(string booking, CancellationToken ct = default) =>
        Db.StringAsync($"""
            SELECT STRING_AGG(CONCAT(rm.room_no, '@', CONVERT(CHAR(10), n.night, 23)), ' ') WITHIN GROUP (ORDER BY n.night)
            FROM inv.room_night n JOIN inv.room rm ON rm.room_id = n.room_id WHERE n.reservation_id = {Seed.Reservations[booking]};
            """, ct)!;

    public ValueTask DisposeAsync() => W.DisposeAsync();
}
