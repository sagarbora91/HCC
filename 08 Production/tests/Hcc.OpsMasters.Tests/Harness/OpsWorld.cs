using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.GuestServices;
using Hcc.Application.Housekeeping;
using Hcc.Application.Masters;
using Hcc.Application.OpsMasters;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Billing;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.GuestServices;
using Hcc.Infrastructure.SqlServer.Housekeeping;
using Hcc.Infrastructure.SqlServer.Inventory;
using Hcc.Infrastructure.SqlServer.Masters;
using Hcc.Infrastructure.SqlServer.OpsMasters;
using Hcc.Infrastructure.SqlServer.Reservations;
using Hcc.Infrastructure.SqlServer.Stays;
using Microsoft.Data.SqlClient;

namespace Hcc.OpsMasters.Tests.Harness;

/// <summary>Controllable clock (event times come from the injected TimeProvider).</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Disposable HccTest_* database with the prototype BASE shape: one property (business date 24 Oct 2026), the demo users with their real
/// roles (two housekeepers), 24 rooms 101–108 / 201–208 / 301–308, the floors of migration 2801 and the MA-7 seed of migration 2871 run
/// for the property (as an upgrade of an install that already has a property would). Real access policy, real command runner; the
/// housekeeping and guest-services commands are built the way the composition root builds them, with the MA-7 ports.
/// </summary>
public sealed partial class OpsWorld : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private readonly string _server;
    private readonly string _name;
    private int _public = 5000;

    private OpsWorld(string server, string name)
    {
        _server = server;
        _name = name;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
        var conns = Platform.Connections;
        Clock = new TestClock(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.FromHours(5.5)).ToUniversalTime());
        Policy = new SqlAccessPolicy(conns, Clock, []);
        var eligibility = new PolicyTaskAssigneeEligibility(Policy);
        Commands = new OpsMastersCommands(Platform.Commands, Policy, new SqlOpsMastersStore(), new SqlMastersStore(), eligibility, [new SqlOpsMasterUsage()], Clock);
        Queries = new OpsMastersQueries(Policy, new SqlOpsMastersReadStore(conns), new SqlMastersReadStore(conns));
        HkReads = new SqlHousekeepingReadStore(conns);
        Housekeeping = new HousekeepingCommands(Platform.Commands, Policy, new SqlHousekeepingStore(), HkReads, eligibility, new SqlRoomBlockPort(conns), new HousekeepingOptions(), Clock, null,
            new SqlHkTaskTypeSource());
        Stayover = new StayoverCommands(Platform.Commands, Policy, new SqlStayoverStore(), Clock);
        var billingStore = new SqlBillingStore(conns);
        var billing = new BillingService(Platform.Commands, billingStore, Policy, new StayNightSource(billingStore, new SqlAgreedNightRateSource(conns)), new Hcc.Application.Tax.FixedGstRateResolver(), Clock);
        Categories = new SqlGsCategorySource(conns);
        var gsStore = new SqlGuestServicesStore(conns);
        GuestServices = new GuestServicesCommands(Platform.Commands, Policy, gsStore, billing, billingStore, Clock, null, Categories);
        GuestQueries = new GuestServicesQueries(Policy, gsStore, Clock, Categories);
    }

    public SqlPlatform Platform { get; }

    public TestClock Clock { get; }

    public SqlAccessPolicy Policy { get; }

    public OpsMastersCommands Commands { get; }

    public OpsMastersQueries Queries { get; }

    public SqlHousekeepingReadStore HkReads { get; }

    public HousekeepingCommands Housekeeping { get; }

    public StayoverCommands Stayover { get; }

    public SqlGsCategorySource Categories { get; }

    public GuestServicesCommands GuestServices { get; }

    public GuestServicesQueries GuestQueries { get; }

    public int PropertyId { get; private set; }

    /// <summary>Prototype user id → principal id.</summary>
    public Dictionary<string, int> Users { get; } = [];

    public Dictionary<string, int> Rooms { get; } = [];

    public CommandContext Ctx(string user, string key, string op = "Test") => new(PropertyId, Users[user], op, key);

    public static async Task<OpsWorld> CreateAsync(CancellationToken ct)
    {
        var server = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";
        var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        if (!NamePattern().IsMatch(name)) throw new InvalidOperationException("Unexpected test database name.");
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(server, "master")))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"CREATE DATABASE [{name}];", conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var w = new OpsWorld(server, name);
        try
        {
            await w.Platform.Migrations.MigrateAsync(ct);
            await w.SeedAsync(ct);
            return w;
        }
        catch
        {
            await w.DisposeAsync();
            throw;
        }
    }

    public async Task<object?> ScalarAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = await Platform.Connections.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        return await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<int> CountAsync(string sql, CancellationToken ct = default) => Convert.ToInt32(await ScalarAsync(sql, ct), CultureInfo.InvariantCulture);

    public Task ExecAsync(string sql, CancellationToken ct = default) => ScalarAsync(sql, ct);

    private static string MigrationText(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "database", "migrations", file))) dir = dir.Parent;
        if (dir is null) throw new FileNotFoundException("database/migrations/" + file + " not found above " + AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(dir.FullName, "database", "migrations", file));
    }

    /// <summary>The seed statement of migration 2801 (after its last GO) and the whole of 2871.</summary>
    public static string FloorSeedSql() { var t = MigrationText("2801_masters_floors.sql"); return t[(t.LastIndexOf("\nGO", StringComparison.Ordinal) + 3)..]; }

    public static string OpsSeedMigrationSql() => MigrationText("2871_ops_masters_seed.sql");

    private async Task SeedAsync(CancellationToken ct)
    {
        PropertyId = Convert.ToInt32(await ScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '2026-10-24');", ct), CultureInfo.InvariantCulture);
        foreach (var (login, role) in new[] { ("owner", "OWN"), ("manager", "MGR"), ("reception", "REC"), ("cashier", "CSH"), ("supervisor", "SUP"), ("housekeeper", "HKP"), ("housekeeper2", "HKP"),
                     ("accountant", "ACC"), ("maint", "MNT"), ("techadmin", "TAD") })
        {
            var id = Convert.ToInt32(await ScalarAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({PropertyId}, '{login}', N'{login}', '2026-01-01');", ct), CultureInfo.InvariantCulture);
            await ExecAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2026-01-01', '2026-01-01');", ct);
            Users[login] = id;
        }

        await ExecAsync($"INSERT inv.room_type(property_id, code, max_occupancy) VALUES ({PropertyId}, 'Classic', 2);", ct);
        foreach (var f in Enumerable.Range(1, 3))
            foreach (var i in Enumerable.Range(1, 8))
            {
                var no = (f * 100 + i).ToString(CultureInfo.InvariantCulture);
                Rooms[no] = Convert.ToInt32(await ScalarAsync($"INSERT inv.room(property_id, room_no, room_type_id) OUTPUT INSERTED.room_id SELECT {PropertyId}, '{no}', room_type_id FROM inv.room_type WHERE property_id = {PropertyId};", ct), CultureInfo.InvariantCulture);
            }

        await ExecAsync(FloorSeedSql(), ct);
        await ExecAsync(OpsSeedMigrationSql(), ct);
    }

    /// <summary>An in-house stay in a room (reservation, guest, folio, stay, open segment) — what stay-over generation looks for.</summary>
    public async Task<long> InHouseAsync(string room, string guest, CancellationToken ct = default)
    {
        var res = Convert.ToInt64(await ScalarAsync($"INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status) OUTPUT INSERTED.reservation_id VALUES ({PropertyId}, 'HCC-{++_public}', N'{guest}', '2026-10-23', '2026-10-26', 'InHouse');", ct), CultureInfo.InvariantCulture);
        var gid = Convert.ToInt64(await ScalarAsync($"INSERT gst.guest(property_id, full_name, phone, nationality, verified, origin, created_utc) OUTPUT INSERTED.guest_id VALUES ({PropertyId}, N'{guest}', '+91 98200 0{_public}', N'Indian', 1, 'manual', '2026-10-20T09:00:00');", ct), CultureInfo.InvariantCulture);
        var folio = Convert.ToInt64(await ScalarAsync($"INSERT bill.folio(reservation_id) OUTPUT INSERTED.folio_id VALUES ({res});", ct), CultureInfo.InvariantCulture);
        var stay = Convert.ToInt64(await ScalarAsync($"""
            INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by)
            OUTPUT INSERTED.stay_id VALUES ({PropertyId}, {res}, {gid}, {folio}, 'InHouse', '2026-10-23', '2026-10-26', '2026-10-23', '2026-10-23T09:00:00', {Users["reception"]});
            """, ct), CultureInfo.InvariantCulture);
        await ExecAsync($"INSERT gst.stay_segment(stay_id, seq, room_id, from_date) VALUES ({stay}, 1, {Rooms[room]}, '2026-10-23');", ct);
        return stay;
    }

    /// <summary>A housekeeping task row (status ToDo | InProgress | Inspection | Done; kind checkout | stayover | move | repair).</summary>
    public async Task<long> TaskAsync(string room, string kind, string status = "ToDo", string? assignee = null, CancellationToken ct = default) =>
        Convert.ToInt64(await ScalarAsync($"""
            INSERT ops.hk_task(property_id, room_id, kind, status, assignee_id, checklist_mask, business_date, created_utc)
            OUTPUT INSERTED.task_id VALUES ({PropertyId}, {Rooms[room]}, '{kind}', '{status}', {(assignee is null ? "NULL" : Users[assignee].ToString(CultureInfo.InvariantCulture))}, 0, '2026-10-24', '2026-10-24T07:00:00');
            """, ct), CultureInfo.InvariantCulture);

    public async Task<string> TaskVersionAsync(long taskId, CancellationToken ct = default) =>
        Convert.ToBase64String((byte[])(await ScalarAsync($"SELECT row_ver FROM ops.hk_task WHERE task_id = {taskId};", ct))!);

    /// <summary>Current version token of a row of one of the four master tables.</summary>
    public async Task<string> VersionAsync(string table, string idColumn, long id, CancellationToken ct = default) =>
        Convert.ToBase64String((byte[])(await ScalarAsync($"SELECT row_ver FROM {table} WHERE {idColumn} = {id};", ct))!);

    public async ValueTask DisposeAsync()
    {
        if (!NamePattern().IsMatch(_name)) return;
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(_server, "master"));
        await conn.OpenAsync();
        await using var cmd = new SqlCommand($"""
            IF DB_ID(N'{_name}') IS NOT NULL
            BEGIN
              ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
              DROP DATABASE [{_name}];
            END
            """, conn) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
    }
}
