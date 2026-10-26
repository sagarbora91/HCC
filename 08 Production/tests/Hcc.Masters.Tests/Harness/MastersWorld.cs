using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Commands;
using Hcc.Application.Masters;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Masters;
using Microsoft.Data.SqlClient;

namespace Hcc.Masters.Tests.Harness;

/// <summary>Controllable clock (event times come from the injected TimeProvider).</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Disposable HccTest_* database with the prototype BASE shape: one property (business date 24 Oct 2026), the ten demo users with their
/// roles, 24 rooms 101–108 / 201–208 / 301–308, and the floors created by the seed statement of migration 2801 (run again after the
/// rooms exist, exactly as an upgrade of an install with rooms would). Real access policy, real command runner.
/// </summary>
public sealed partial class MastersWorld : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private readonly string _server;
    private readonly string _name;

    private MastersWorld(string server, string name)
    {
        _server = server;
        _name = name;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
        Clock = new TestClock(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.FromHours(5.5)).ToUniversalTime());
        Policy = new SqlAccessPolicy(Platform.Connections, Clock, []);
        Commands = new MastersCommands(Platform.Commands, Policy, new SqlMastersStore(), [new SqlFloorUsage()], Clock);
        Queries = new MastersQueries(Policy, new SqlMastersReadStore(Platform.Connections));
    }

    public SqlPlatform Platform { get; }

    public TestClock Clock { get; }

    public SqlAccessPolicy Policy { get; }

    public MastersCommands Commands { get; }

    public MastersQueries Queries { get; }

    public int PropertyId { get; private set; }

    /// <summary>Prototype user id → principal id.</summary>
    public Dictionary<string, int> Users { get; } = [];

    /// <summary>Floor code → floor id (after seeding).</summary>
    public Dictionary<string, int> Floors { get; } = [];

    public CommandContext Ctx(string user, string key) => new(PropertyId, Users[user], "M", key);

    public static async Task<MastersWorld> CreateAsync(CancellationToken ct)
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

        var w = new MastersWorld(server, name);
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
    public static string MigrationSeedSql()
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

        var rooms = string.Join(", ", Enumerable.Range(1, 3).SelectMany(f => Enumerable.Range(1, 8).Select(i => $"('{f * 100 + i}')")));
        await ExecAsync($"INSERT inv.room_type(property_id, code, max_occupancy) VALUES ({PropertyId}, 'Classic', 2);" +
            $"INSERT inv.room(property_id, room_no, room_type_id) SELECT {PropertyId}, r, room_type_id FROM inv.room_type CROSS JOIN (VALUES {rooms}) v(r) WHERE property_id = {PropertyId};", ct);
        await ExecAsync(MigrationSeedSql(), ct);
        await using var conn = await Platform.Connections.OpenAsync(ct);
        await using var cmd = new SqlCommand($"SELECT code, floor_id FROM mst.floor WHERE property_id = {PropertyId};", conn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) Floors[rd.GetString(0)] = rd.GetInt32(1);
    }

    /// <summary>Current version token of a floor (base64 rowversion).</summary>
    public async Task<string> VersionAsync(int floorId, CancellationToken ct = default) =>
        Convert.ToBase64String((byte[])(await ScalarAsync($"SELECT row_ver FROM mst.floor WHERE floor_id = {floorId};", ct))!);

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
