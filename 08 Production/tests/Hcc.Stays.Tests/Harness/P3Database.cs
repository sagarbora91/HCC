using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Application.Commands;
using Hcc.Application.Housekeeping;
using Hcc.Application.Stays;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Housekeeping;
using Hcc.Infrastructure.SqlServer.Migrations;
using Hcc.Infrastructure.SqlServer.Stays;
using Microsoft.Data.SqlClient;

namespace Hcc.Stays.Tests.Harness;

/// <summary>A disposable <c>HccTest_*</c> database migrated to the latest version (all blocks) (same naming/drop rules as the platform harness).</summary>
public sealed partial class P3Database : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private P3Database(string server, string name)
    {
        Server = server;
        Name = name;
        Options = new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" };
        Platform = new SqlPlatform(Options);
    }

    public string Server { get; }

    public string Name { get; }

    public SqlOptions Options { get; }

    public SqlPlatform Platform { get; }

    public static string TestServer => Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";

    public static async Task<P3Database> CreateAsync(CancellationToken ct = default)
    {
        var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        if (!NamePattern().IsMatch(name)) throw new InvalidOperationException("Unexpected test database name.");
        var db = new P3Database(TestServer, name);
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(db.Server, "master")))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"CREATE DATABASE [{name}];", conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await db.Platform.Migrations.MigrateAsync(ct);
        return db;
    }

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

/// <summary>Everything a P3 SQL test needs: a fresh database, the BASE seed, the fake policy and the real use cases.</summary>
public sealed class P3World : IAsyncDisposable
{
    private P3World(P3Database db, Seed seed)
    {
        Db = db;
        Seed = seed;
        var conns = db.Platform.Connections;
        Scope = new SqlAssignedScopeSource(conns);
        Policy = new FakeAccessPolicy(Scope);
        Clock = new TestClock(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero));
        HkReads = new SqlHousekeepingReadStore(conns);
        Stays = new StaysCommands(db.Platform.Commands, Policy, new SqlStaysStore(), new Sql0002StayFolioPort(), Clock);
        StayQueries = new StaysQueries(Policy, new SqlStaysReadStore(conns));
        Housekeeping = new HousekeepingCommands(db.Platform.Commands, Policy, new SqlHousekeepingStore(), HkReads, new PolicyTaskAssigneeEligibility(Policy), Options, Clock);
        Board = new HousekeepingQueries(Policy, HkReads);
        Projector = new HousekeepingTaskProjector(db.Platform.Outbox);
        Blocks = new SqlRoomBlockReader(conns);
    }

    public P3Database Db { get; }

    public Seed Seed { get; }

    public SqlAssignedScopeSource Scope { get; }

    public FakeAccessPolicy Policy { get; }

    public TestClock Clock { get; }

    public HousekeepingOptions Options { get; } = new();

    public SqlHousekeepingReadStore HkReads { get; }

    public StaysCommands Stays { get; }

    public StaysQueries StayQueries { get; }

    public HousekeepingCommands Housekeeping { get; }

    public HousekeepingQueries Board { get; }

    public HousekeepingTaskProjector Projector { get; }

    public SqlRoomBlockReader Blocks { get; }

    public int PropertyId => Seed.PropertyId;

    public static async Task<P3World> CreateAsync(CancellationToken ct = default)
    {
        var db = await P3Database.CreateAsync(ct);
        try
        {
            var seed = await Seed.BaseAsync(db, StaysGolden.Load().Base, ct);
            return new P3World(db, seed);
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    public CommandContext Ctx(int principal, string key) => new(PropertyId, principal, "P3", key);

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
