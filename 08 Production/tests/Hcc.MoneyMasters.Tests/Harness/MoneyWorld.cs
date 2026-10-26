using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Masters;
using Hcc.Application.MoneyMasters;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Masters;
using Hcc.Infrastructure.SqlServer.MoneyMasters;
using Microsoft.Data.SqlClient;

namespace Hcc.MoneyMasters.Tests.Harness;

/// <summary>Controllable clock (event times come from the injected TimeProvider).</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Disposable HccTest_* database: one property created AFTER the migrations (so its money masters are seeded lazily by the first read or
/// command, as for a property set up after the upgrade), the nine demo users with their roles, and four expenses carrying the old free-text
/// values (two vendors, Cash / UPI / Vendor credit, Supplies / Laundry). Real access policy, real command runner.
/// </summary>
public sealed partial class MoneyWorld : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private readonly string _server;
    private readonly string _name;

    private MoneyWorld(string server, string name)
    {
        _server = server;
        _name = name;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
        Clock = new TestClock(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.FromHours(5.5)).ToUniversalTime());
        Policy = new SqlAccessPolicy(Platform.Connections, Clock, []);
        var store = new SqlMoneyMastersStore();
        Commands = new MoneyMastersCommands(Platform.Commands, Policy, store, new SqlMastersStore(), [new MoneyMasterUsage(store), new SqlFloorUsage()], Clock);
        Queries = new MoneyMastersQueries(Policy, new SqlMoneyMastersReadStore(Platform.Connections), new SqlMastersReadStore(Platform.Connections));
    }

    public SqlPlatform Platform { get; }

    public TestClock Clock { get; }

    public SqlAccessPolicy Policy { get; }

    public MoneyMastersCommands Commands { get; }

    public MoneyMastersQueries Queries { get; }

    public int PropertyId { get; private set; }

    /// <summary>Prototype user id → principal id.</summary>
    public Dictionary<string, int> Users { get; } = [];

    public CommandContext Ctx(string user, string key) => new(PropertyId, Users[user], "M", key);

    public static async Task<MoneyWorld> CreateAsync(CancellationToken ct)
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

        var w = new MoneyWorld(server, name);
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

        await ExecAsync($"""
            INSERT ops.expense(property_id, vendor, category, amount_paise, method, business_date, status, submitted_by, created_utc) VALUES
              ({PropertyId}, N'Morning Supplies', N'Supplies', 50000, 'Cash', '2026-10-24', 'Pending', {Users["reception"]}, '2026-10-24'),
              ({PropertyId}, N'Morning Supplies', N'Supplies', 20000, 'UPI', '2026-10-24', 'Pending', {Users["reception"]}, '2026-10-24'),
              ({PropertyId}, N'Fresh Linen Services', N'Laundry', 80000, 'Vendor credit', '2026-10-24', 'Pending', {Users["reception"]}, '2026-10-24'),
              ({PropertyId}, N'  morning supplies ', N'Supplies', 10000, 'Cash', '2026-10-24', 'Pending', {Users["reception"]}, '2026-10-24');
            """, ct);
    }

    /// <summary>Id of a row by code (after the money masters were seeded).</summary>
    public async Task<int> IdAsync(string table, string idColumn, string code, CancellationToken ct = default) =>
        await CountAsync($"SELECT {idColumn} FROM {table} WHERE property_id = {PropertyId} AND code = '{code}';", ct);

    /// <summary>Current version token of a row (base64 rowversion).</summary>
    public async Task<string> VersionAsync(string table, string idColumn, int id, CancellationToken ct = default) =>
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

/// <summary>Bearer token <c>test-&lt;principal&gt;</c> is a valid session of that principal behind the real RequireHccSession filter.</summary>
public sealed class TestSessions(int propertyId) : ISessionService
{
    public static string Token(int principalId) => "test-" + principalId.ToString(CultureInfo.InvariantCulture);

    public Task<(AuthenticatedSession Session, string Token)> SignInAsync(SignInRequest request, string? deviceToken, CancellationToken ct = default)
        => throw new NotSupportedException("Tests sign in with a bearer token.");

    public Task<AuthenticatedSession> ValidateAsync(string? token, CancellationToken ct = default)
    {
        if (token is { } t && t.StartsWith("test-", StringComparison.Ordinal)
            && int.TryParse(t.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var principal) && principal > 0)
            return Task.FromResult(new AuthenticatedSession(principal, principal, propertyId, "Test user " + t[5..], CredentialKind.Pin, null,
                DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(12)));
        throw new HccException(HccErrorCode.UNAUTHENTICATED, "Your session has ended. Sign in again.");
    }

    public Task SignOutAsync(string? token, CancellationToken ct = default) => Task.CompletedTask;
}
