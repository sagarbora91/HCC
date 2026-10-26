using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Commands;
using Hcc.Application.FrontDeskMasters;
using Hcc.Application.Masters;
using Hcc.Contracts.FrontDeskMasters;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.FrontDeskMasters;
using Hcc.Infrastructure.SqlServer.Masters;
using Microsoft.Data.SqlClient;

namespace Hcc.FrontDeskMasters.Tests.Harness;

/// <summary>Controllable clock (event times come from the injected TimeProvider).</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Disposable HccTest_* database with one property (business date 24 Oct 2026) and the nine demo users with their roles. Real access
/// policy (0101 + module RBAC seeds), real command runner, the MA-0 history store. The four lists start empty (server defaults).
/// </summary>
public sealed partial class FdmWorld : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private readonly string _server;
    private readonly string _name;

    private FdmWorld(string server, string name)
    {
        _server = server;
        _name = name;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
        Clock = new TestClock(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.FromHours(5.5)).ToUniversalTime());
        Policy = new SqlAccessPolicy(Platform.Connections, Clock, []);
        Store = new SqlFrontDeskMastersStore();
        Commands = new FrontDeskMastersCommands(Platform.Commands, Policy, Store, new SqlMastersStore(), Clock);
        Queries = new FrontDeskMastersQueries(Policy, new SqlFrontDeskMastersReadStore(Platform.Connections), new SqlMastersReadStore(Platform.Connections));
    }

    public SqlPlatform Platform { get; }

    public TestClock Clock { get; }

    public SqlAccessPolicy Policy { get; }

    public SqlFrontDeskMastersStore Store { get; }

    public FrontDeskMastersCommands Commands { get; }

    public FrontDeskMastersQueries Queries { get; }

    public int PropertyId { get; private set; }

    /// <summary>Prototype user id → principal id.</summary>
    public Dictionary<string, int> Users { get; } = [];

    public CommandContext Ctx(string user, string key) => new(PropertyId, Users[user], "M", key);

    public static string Key() => "k-" + Guid.NewGuid().ToString("N");

    public static async Task<FdmWorld> CreateAsync(CancellationToken ct)
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

        var w = new FdmWorld(server, name);
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

    private async Task SeedAsync(CancellationToken ct)
    {
        PropertyId = Convert.ToInt32(await ScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '2026-10-24');", ct), CultureInfo.InvariantCulture);
        foreach (var (login, role) in new[] { ("owner", "OWN"), ("manager", "MGR"), ("reception", "REC"), ("cashier", "CSH"), ("supervisor", "SUP"), ("housekeeper", "HKP"), ("accountant", "ACC"), ("maint", "MNT"), ("techadmin", "TAD") })
        {
            var id = Convert.ToInt32(await ScalarAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({PropertyId}, '{login}', N'{login}', '2026-01-01');", ct), CultureInfo.InvariantCulture);
            await ExecAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2026-01-01', '2026-01-01');", ct);
            Users[login] = id;
        }
    }

    /// <summary>Stored row id and version of a code (after the list was stored).</summary>
    public async Task<(int Id, string Version)> RowAsync(string table, string code, CancellationToken ct = default)
    {
        await using var conn = await Platform.Connections.OpenAsync(ct);
        await using var cmd = new SqlCommand($"SELECT row_id, row_ver FROM {table} WHERE property_id = {PropertyId} AND code = @c;", conn);
        cmd.Parameters.AddWithValue("@c", code);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct)) throw new InvalidOperationException($"{code} not in {table}");
        return (rd.GetInt32(0), Convert.ToBase64String((byte[])rd[1]));
    }

    public static string Table(string master) => master switch
    {
        FrontDeskMasterIds.BookingSources => "mst.booking_source",
        FrontDeskMasterIds.IdTypes => "mst.id_type",
        FrontDeskMasterIds.NumberSeries => "bill.number_series",
        _ => "inv.policy",
    };

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
