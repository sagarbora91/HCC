using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Commands;
using Hcc.Application.Masters;
using Hcc.Application.RoomMasters;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Inventory;
using Hcc.Infrastructure.SqlServer.Masters;
using Hcc.Infrastructure.SqlServer.RoomMasters;
using Microsoft.Data.SqlClient;

namespace Hcc.RoomMasters.Tests.Harness;

/// <summary>Controllable clock (event times come from the injected TimeProvider).</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Disposable HccTest_* database in the prototype BASE shape: one property (business date 24 Oct 2026), the nine demo roles, room types
/// Classic / Deluxe / Suite (rack ₹2,000 / ₹3,000 / ₹4,500, mixed-case codes as the import writes them), 24 rooms 101–108 / 201–208 /
/// 301–308 inserted after the migrations, then the floors seed of 2801 and the floor_id backfill of 2810 run again (an upgrade of an install
/// with rooms); one confirmed reservation HCC-1101 on room 103 for the nights of 24 and 25 Oct; type-night counts materialised for Oct–Nov.
/// </summary>
public sealed partial class RoomMastersWorld : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private readonly string _server;
    private readonly string _name;

    private RoomMastersWorld(string server, string name)
    {
        _server = server;
        _name = name;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
        Clock = new TestClock(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.FromHours(5.5)).ToUniversalTime());
        Policy = new SqlAccessPolicy(Platform.Connections, Clock, []);
        Floors = new MastersCommands(Platform.Commands, Policy, new SqlMastersStore(), [new SqlFloorUsage()], Clock);
        FloorQueries = new MastersQueries(Policy, new SqlMastersReadStore(Platform.Connections));
        Commands = new RoomMastersCommands(Platform.Commands, Policy, new SqlRoomMastersStore(Platform.Connections), new SqlMastersStore(),
            new SqlRoomBlockPort(Platform.Connections, Clock), Clock);
        Queries = new RoomMastersQueries(Policy, new SqlRoomMastersReadStore(Platform.Connections), new SqlMastersReadStore(Platform.Connections));
    }

    public SqlPlatform Platform { get; }

    public TestClock Clock { get; }

    public SqlAccessPolicy Policy { get; }

    public MastersCommands Floors { get; }

    public MastersQueries FloorQueries { get; }

    public RoomMastersCommands Commands { get; }

    public RoomMastersQueries Queries { get; }

    public int PropertyId { get; private set; }

    public Dictionary<string, int> Users { get; } = [];

    /// <summary>Room type code → id.</summary>
    public Dictionary<string, int> Types { get; } = [];

    /// <summary>Floor code → id.</summary>
    public Dictionary<string, int> FloorIds { get; } = [];

    public CommandContext Ctx(string user, string key) => new(PropertyId, Users[user], "M", key);

    /// <summary>The floor_id backfill statement of migration 2810.</summary>
    public const string FloorBackfill = """
        UPDATE r SET floor_id = f.floor_id
          FROM inv.room r JOIN mst.floor f ON f.property_id = r.property_id
           AND f.code = CONCAT('F', IIF(r.room_no NOT LIKE '%[^0-9]%' AND LEN(r.room_no) >= 3, IIF(CAST(r.room_no AS BIGINT) / 100 < 1, 1, CAST(r.room_no AS BIGINT) / 100), 1));
        """;

    public static async Task<RoomMastersWorld> CreateAsync(CancellationToken ct)
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

        var w = new RoomMastersWorld(server, name);
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

    /// <summary>The seed statement of migration 2801 (the batch after its last GO).</summary>
    private static string FloorsSeedSql()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "database", "migrations", "2801_masters_floors.sql"))) dir = dir.Parent;
        if (dir is null) throw new FileNotFoundException("database/migrations/2801_masters_floors.sql not found above " + AppContext.BaseDirectory);
        var text = File.ReadAllText(Path.Combine(dir.FullName, "database", "migrations", "2801_masters_floors.sql"));
        return text[(text.LastIndexOf("\nGO", StringComparison.Ordinal) + 3)..];
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        PropertyId = Convert.ToInt32(await ScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '2026-10-24');", ct), CultureInfo.InvariantCulture);
        foreach (var (login, role) in new[] { ("owner", "OWN"), ("manager", "MGR"), ("reception", "REC"), ("cashier", "CSH"), ("supervisor", "SUP"), ("housekeeper", "HKP"), ("accountant", "ACC"), ("maint", "MNT"), ("techadmin", "TAD") })
        {
            var id = Convert.ToInt32(await ScalarAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({PropertyId}, '{login}', N'{login}', '2026-01-01');", ct), CultureInfo.InvariantCulture);
            await ExecAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2026-01-01', '2026-01-01');", ct);
            Users[login] = id;
        }

        var p = PropertyId;
        await ExecAsync($"""
            INSERT inv.room_type(property_id, code, max_occupancy, name, rack_rate_paise, sort_order) VALUES
              ({p}, 'Classic', 2, N'Classic', 200000, 10), ({p}, 'Deluxe', 3, N'Deluxe', 300000, 20), ({p}, 'Suite', 4, N'Suite', 450000, 30);
            DECLARE @t TABLE(i INT, code VARCHAR(20));
            INSERT @t VALUES (1, 'Classic'), (2, 'Classic'), (3, 'Deluxe'), (4, 'Deluxe'), (5, 'Classic'), (6, 'Suite'), (7, 'Deluxe'), (8, 'Classic');
            INSERT inv.room(property_id, room_no, room_type_id)
            SELECT {p}, CAST(f.n * 100 + x.i AS VARCHAR(10)), rt.room_type_id FROM (VALUES (1), (2), (3)) f(n) CROSS JOIN @t x JOIN inv.room_type rt ON rt.code = x.code AND rt.property_id = {p};
            """, ct);
        await ExecAsync(FloorsSeedSql(), ct);
        await ExecAsync(FloorBackfill, ct);
        await ExecAsync($"""
            DECLARE @room INT = (SELECT room_id FROM inv.room WHERE room_no = '103' AND property_id = {p});
            DECLARE @type INT = (SELECT room_type_id FROM inv.room WHERE room_id = @room);
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id) VALUES ({p}, 'HCC-1101', N'Test Guest', '2026-10-24', '2026-10-26', 'Confirmed', @room, @type);
            DECLARE @res BIGINT = SCOPE_IDENTITY();
            INSERT inv.room_night(room_id, night, reservation_id) VALUES (@room, '2026-10-24', @res), (@room, '2026-10-25', @res);
            EXEC inv.usp_rebuild_type_night_count {p}, '2026-10-01', '2026-12-01';
            """, ct);
        await using var conn = await Platform.Connections.OpenAsync(ct);
        await using (var cmd = new SqlCommand($"SELECT code, room_type_id FROM inv.room_type WHERE property_id = {p};", conn))
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
            while (await rd.ReadAsync(ct)) Types[rd.GetString(0)] = rd.GetInt32(1);
        await using (var cmd = new SqlCommand($"SELECT code, floor_id FROM mst.floor WHERE property_id = {p};", conn))
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
            while (await rd.ReadAsync(ct)) FloorIds[rd.GetString(0)] = rd.GetInt32(1);
    }

    public async Task<int> RoomIdAsync(string roomNo, CancellationToken ct = default) =>
        await CountAsync($"SELECT room_id FROM inv.room WHERE property_id = {PropertyId} AND room_no = '{roomNo}';", ct);

    public async Task<string> VersionAsync(string table, string idColumn, long id, CancellationToken ct = default) =>
        Convert.ToBase64String((byte[])(await ScalarAsync($"SELECT row_ver FROM {table} WHERE {idColumn} = {id};", ct))!);

    public Task<string> RoomVersionAsync(int roomId, CancellationToken ct = default) => VersionAsync("inv.room", "room_id", roomId, ct);

    public Task<string> TypeVersionAsync(int typeId, CancellationToken ct = default) => VersionAsync("inv.room_type", "room_type_id", typeId, ct);

    /// <summary>Rows of inv.v_type_night_count_drift (maintained counts that differ from a recomputation) — 0 = consistent.</summary>
    public Task<int> DriftAsync(CancellationToken ct = default) => CountAsync("SELECT COUNT(*) FROM inv.v_type_night_count_drift;", ct);

    public Task<int> SellableAsync(string typeCode, string night, CancellationToken ct = default) =>
        CountAsync($"SELECT sellable FROM inv.type_night_count WHERE room_type_id = {Types[typeCode]} AND night = '{night}';", ct);

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
