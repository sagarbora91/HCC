using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Housekeeping;
using Hcc.Application.Maintenance;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Housekeeping;
using Hcc.Infrastructure.SqlServer.Inventory;
using Hcc.Infrastructure.SqlServer.Maintenance;
using Microsoft.Data.SqlClient;

namespace Hcc.Maintenance.Tests.Harness;

/// <summary>Controllable clock (event times come from the injected TimeProvider).</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

public static class Expect
{
    public static async Task<string> CodeAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await action();
        }
        catch (HccException e)
        {
            return e.Code.ToString();
        }

        return "NO_ERROR";
    }
}

/// <summary>A disposable <c>HccTest_*</c> database migrated to the latest version, seeded with the prototype's module-15 BASE fixture.</summary>
public sealed partial class MntWorld : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private MntWorld(string server, string name)
    {
        Server = server;
        Name = name;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
        var conns = Platform.Connections;
        Clock = new TestClock(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero));
        Policy = new SqlAccessPolicy(conns, Clock, [.. SqlHousekeepingScopeResolver.All(conns), new SqlMaintenanceScopeResolver(conns)]);
        Commands = new MaintenanceCommands(Platform.Commands, Policy, new SqlMaintenanceStore(), new SqlHousekeepingStore(), new SqlRoomBlockPort(conns), Clock);
        Queries = new MaintenanceQueries(Policy, new SqlMaintenanceReadStore(conns), Clock);
    }

    public string Server { get; }

    public string Name { get; }

    public SqlPlatform Platform { get; }

    public TestClock Clock { get; }

    public SqlAccessPolicy Policy { get; }

    public MaintenanceCommands Commands { get; }

    public MaintenanceQueries Queries { get; }

    public int PropertyId { get; private set; }

    public Dictionary<string, int> Users { get; } = [];

    public Dictionary<string, int> Rooms { get; } = [];

    public Dictionary<string, int> Vendors { get; } = [];

    public Dictionary<string, int> Assets { get; } = [];

    public Dictionary<string, int> Spares { get; } = [];

    public Dictionary<string, int> Plans { get; } = [];

    public Dictionary<string, int> Amcs { get; } = [];

    public Dictionary<string, long> Orders { get; } = [];

    public long RepairTaskId { get; private set; }

    public static string TestServer => Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";

    private static string Q(string? v) => v is null ? "NULL" : "N'" + v.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string Ts(string? v) => v is null ? "NULL" : "'" + v + "'";

    private int U(string? name) => name is null ? 0 : Users[name];

    private string UOrNull(string? name) => name is null ? "NULL" : Users[name].ToString(CultureInfo.InvariantCulture);

    public CommandContext Ctx(string user, string key) => new(PropertyId, Users[user], "test", key);

    public async Task<object?> ScalarAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = await Platform.Connections.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        return await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<int> CountAsync(string sql, CancellationToken ct = default) => Convert.ToInt32(await ScalarAsync(sql, ct), CultureInfo.InvariantCulture);

    public async Task<string?> StringAsync(string sql, CancellationToken ct = default) => await ScalarAsync(sql, ct) as string;

    public async Task ExecAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = await Platform.Connections.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Current row version of a work order, as the wire token.</summary>
    public async Task<string> VersionAsync(string orderCode, CancellationToken ct = default) =>
        Convert.ToBase64String((byte[])(await ScalarAsync($"SELECT row_ver FROM mnt.work_order WHERE wo_id = {Orders[orderCode]};", ct))!);

    public async Task<string> VersionAsync(long workOrderId, CancellationToken ct = default) =>
        Convert.ToBase64String((byte[])(await ScalarAsync($"SELECT row_ver FROM mnt.work_order WHERE wo_id = {workOrderId};", ct))!);

    public Task<string> SpareVersionAsync(string code, CancellationToken ct = default) =>
        Task.Run(async () => Convert.ToBase64String((byte[])(await ScalarAsync($"SELECT row_ver FROM mnt.spare WHERE spare_id = {Spares[code]};", ct))!), ct);

    public static async Task<MntWorld> CreateAsync(CancellationToken ct = default)
    {
        var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        if (!NamePattern().IsMatch(name)) throw new InvalidOperationException("Unexpected test database name.");
        var world = new MntWorld(TestServer, name);
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(world.Server, "master")))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"CREATE DATABASE [{name}];", conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync(ct);
        }

        try
        {
            await world.Platform.Migrations.MigrateAsync(ct);
            await world.SeedAsync(ct);
            return world;
        }
        catch
        {
            await world.DisposeAsync();
            throw;
        }
    }

    private async Task<int> Id(string sql, CancellationToken ct) => Convert.ToInt32(await ScalarAsync(sql, ct), CultureInfo.InvariantCulture);

    private async Task SeedAsync(CancellationToken ct)
    {
        PropertyId = await Id($"INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '{GoldenFixture.BusinessDate}');", ct);
        var p = PropertyId;
        await ExecAsync($"INSERT inv.room_type(property_id, code, max_occupancy) VALUES ({p}, 'Classic', 2);", ct);
        foreach (var no in new[] { "101", "103", "104", "105", "106", "107", "201", "203", "205", "303", "306", "308" })
            Rooms[no] = await Id($"INSERT inv.room(property_id, room_no, room_type_id) OUTPUT INSERTED.room_id SELECT {p}, '{no}', room_type_id FROM inv.room_type WHERE property_id = {p};", ct);

        // Users and the role templates from the RBAC seed (migration 0101): the REAL policy decides every access in these tests.
        foreach (var (login, role) in new[] { ("owner", "OWN"), ("manager", "MGR"), ("supervisor", "SUP"), ("reception", "REC"), ("cashier", "CSH"), ("accountant", "ACC"),
                     ("housekeeper", "HKP"), ("maint", "MNT"), ("maint2", "MNT"), ("supervisor2", "SUP") })
        {
            Users[login] = await Id($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({p}, '{login}', N'{login}', '2026-01-01');", ct);
            await ExecAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({Users[login]}, '{role}', '2020-01-01', '2026-01-01');", ct);
        }

        foreach (var v in GoldenFixture.Vendors)
            Vendors[v.Code] = await Id($"INSERT mnt.vendor(property_id, name, trade, phone) OUTPUT INSERTED.vendor_id VALUES ({p}, {Q(v.Name)}, {Q(v.Trade)}, '{v.Phone}');", ct);
        foreach (var a in GoldenFixture.Assets)
            Assets[a.Code] = await Id($"""
                INSERT mnt.asset(property_id, name, kind, room_id, area, make, model, installed_on, vendor_id, status) OUTPUT INSERTED.asset_id
                VALUES ({p}, {Q(a.Name)}, '{a.Kind}', {(a.Room is null ? "NULL" : Rooms[a.Room].ToString(CultureInfo.InvariantCulture))}, {Q(a.Area)}, {Q(a.Make)}, {Q(a.Model)}, '{a.Installed}',
                  {(a.Vendor is null ? "NULL" : Vendors[a.Vendor].ToString(CultureInfo.InvariantCulture))}, '{a.Status}');
                """, ct);
        foreach (var s in GoldenFixture.Spares)
            Spares[s.Code] = await Id($"INSERT mnt.spare(property_id, name, unit, stock, reorder_at, cost_paise) OUTPUT INSERTED.spare_id VALUES ({p}, {Q(s.Name)}, '{s.Unit}', {s.Stock}, {s.ReorderAt}, {s.Cost});", ct);
        foreach (var a in GoldenFixture.Amcs)
            Amcs[a.Code] = await Id($"INSERT mnt.amc(property_id, vendor_id, scope, from_date, to_date, visits, value_paise) OUTPUT INSERTED.amc_id VALUES ({p}, {Vendors[a.Vendor]}, {Q(a.Scope)}, '{a.From}', '{a.To}', {a.Visits}, {a.Value});", ct);
        foreach (var pl in GoldenFixture.Plans)
            Plans[pl.Code] = await Id($"INSERT mnt.pm_plan(property_id, asset_id, title, every_days, next_due, last_done) OUTPUT INSERTED.plan_id VALUES ({p}, {Assets[pl.Asset]}, {Q(pl.Title)}, {pl.Every}, '{pl.NextDue}', '{pl.LastDone}');", ct);

        foreach (var o in GoldenFixture.Orders)
        {
            var id = Convert.ToInt64(await ScalarAsync($"""
                INSERT mnt.work_order(property_id, kind, title, priority, status, room_id, asset_id, area, assignee_id, vendor_id, created_utc, created_by, started_utc, done_utc, done_by,
                  verified_utc, verified_by, hold_reason, awaiting_spare_id, note, plan_id, due_date, downtime_from_utc, downtime_to_utc)
                OUTPUT INSERTED.wo_id
                VALUES ({p}, '{o.Kind}', {Q(o.Title)}, '{o.Priority}', '{o.Status}', {(o.Room is null ? "NULL" : Rooms[o.Room].ToString(CultureInfo.InvariantCulture))},
                  {(o.Asset is null ? "NULL" : Assets[o.Asset].ToString(CultureInfo.InvariantCulture))}, {Q(o.Area)}, {UOrNull(o.Assignee)},
                  {(o.Vendor is null ? "NULL" : Vendors[o.Vendor].ToString(CultureInfo.InvariantCulture))}, '{o.Created}', {U(o.CreatedBy)}, {Ts(o.Started)}, {Ts(o.Done)}, {UOrNull(o.DoneBy)},
                  {Ts(o.Verified)}, {UOrNull(o.VerifiedBy)}, {Q(o.Hold)}, {(o.AwaitingSpare is null ? "NULL" : Spares[o.AwaitingSpare].ToString(CultureInfo.InvariantCulture))}, {Q(o.Note)},
                  {(o.Plan is null ? "NULL" : Plans[o.Plan].ToString(CultureInfo.InvariantCulture))}, {Ts(o.Due)}, {Ts(o.DtFrom)}, {Ts(o.DtTo)});
                """, ct), CultureInfo.InvariantCulture);
            Orders[o.Code] = id;
            foreach (var part in o.Parts ?? [])
                await ExecAsync($"INSERT mnt.work_order_part(wo_id, spare_id, unit_cost_paise, qty) VALUES ({id}, {Spares[part.Spare]}, {part.Cost}, {part.Qty});", ct);
        }

        // Housekeeping side: room 308 is blocked with an open repair task for the technician; the housekeeper has a clean in room 106;
        // room 103 has a reservation tonight (blocking it must be refused).
        RepairTaskId = Convert.ToInt64(await ScalarAsync($"""
            INSERT ops.hk_task(property_id, room_id, kind, status, assignee_id, note, business_date, created_utc)
            OUTPUT INSERTED.task_id VALUES ({p}, {Rooms["308"]}, 'repair', 'ToDo', {Users["maint"]}, N'Bathroom leak — plumber booked (fictional)', '{GoldenFixture.BusinessDate}', '2026-10-23T18:00:00');
            """, ct), CultureInfo.InvariantCulture);
        await ExecAsync($"INSERT ops.room_block(property_id, room_id, from_date, to_date, reason, source, created_by, created_utc) VALUES ({p}, {Rooms["308"]}, '2026-10-23', NULL, N'Bathroom leak', 'manual', {Users["supervisor"]}, '2026-10-23T18:00:00');", ct);
        await ExecAsync($"INSERT ops.hk_task(property_id, room_id, kind, status, assignee_id, business_date, created_utc) VALUES ({p}, {Rooms["106"]}, 'checkout', 'ToDo', {Users["housekeeper"]}, '{GoldenFixture.BusinessDate}', '2026-10-24T07:00:00');", ct);
        var res = Convert.ToInt64(await ScalarAsync($"INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status) OUTPUT INSERTED.reservation_id VALUES ({p}, 'HCC-2001', N'Ishaan Bose (fictional)', '2026-10-24', '2026-10-26', 'Confirmed');", ct), CultureInfo.InvariantCulture);
        await ExecAsync($"INSERT inv.room_night(room_id, night, reservation_id) VALUES ({Rooms["103"]}, '2026-10-24', {res}), ({Rooms["103"]}, '2026-10-25', {res});", ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (!NamePattern().IsMatch(Name)) return;
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(Server, "master"));
        await conn.OpenAsync();
        await using var cmd = new SqlCommand($"""
            IF DB_ID(N'{Name}') IS NOT NULL
            BEGIN
              ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
              DROP DATABASE [{Name}];
            END
            """, conn) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
    }
}
