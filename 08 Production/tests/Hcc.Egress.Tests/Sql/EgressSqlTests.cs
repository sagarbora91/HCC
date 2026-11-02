using System.Globalization;
using Hcc.Infrastructure.Egress;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Egress;
using Microsoft.Data.SqlClient;

namespace Hcc.Egress.Tests.Sql;

public sealed class EgressDatabase : IAsyncLifetime
{
    private string _server = "";

    public string Name { get; } = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];

    public SqlPlatform Platform { get; private set; } = null!;

    public SqlEgressStore Store { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _server = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(_server, "master")))
        {
            await conn.OpenAsync();
            await using var cmd = new SqlCommand($"CREATE DATABASE [{Name}];", conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync();
        }

        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(_server, Name), BackupDirectory = "NUL" });
        await Platform.Migrations.MigrateAsync(CancellationToken.None);
        Store = new SqlEgressStore(Platform.Connections);
    }

    public async Task<object?> ScalarAsync(string sql)
    {
        await using var conn = await Platform.Connections.OpenAsync(CancellationToken.None);
        await using var cmd = new SqlCommand(sql, conn);
        return await cmd.ExecuteScalarAsync();
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(_server, "master"));
        await conn.OpenAsync();
        await using var cmd = new SqlCommand($"IF DB_ID('{Name}') IS NOT NULL BEGIN ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]; END", conn) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
    }
}

[Trait("Category", "Sql")]
public sealed class EgressSqlTests(EgressDatabase db) : IClassFixture<EgressDatabase>
{
    [Fact]
    public async Task Held_items_replay_in_original_order_and_counts_audit_and_firewall_are_read()
    {
        var ct = TestContext.Current.CancellationToken;
        var src = new FakeSource { Master = false };
        var clock = new TestClock(new DateTimeOffset(2026, 11, 2, 4, 30, 0, TimeSpan.Zero));
        var policy = new EgressPolicy(src, clock);
        var outbox = new EgressOutbox(db.Store, policy, clock);
        var ids = new List<long>();
        for (var n = 0; n < 3; n++) ids.Add(await outbox.EnqueueAsync("messaging", EgressIntegration.Messaging, "msg-" + n, "{\"n\":" + n + "}", ct));
        await outbox.EnqueueAsync("email", EgressIntegration.Email, null, "{}", ct);
        Assert.Equal(4, Convert.ToInt32(await db.ScalarAsync("SELECT COUNT(*) FROM ops.egress_outbox WHERE status = 'held' AND held_reason = N'internet off' AND attempts = 0;"), CultureInfo.InvariantCulture));
        var counts = await db.Store.CountsAsync(ct);
        Assert.Equal(["messaging", "ota-push", "email", "scheduled-report"], counts.Select(c => c.Queue));
        Assert.Equal(3, counts[0].Held);

        src.Master = true;
        src.Switches[EgressIntegration.Messaging] = true;
        policy.Invalidate();
        await outbox.TickAsync(ct);
        Assert.Equal(ids, (await db.Store.PendingAsync(EgressIntegration.Messaging, 10, ct)).Select(i => i.ItemId));
        Assert.Equal(1, (await db.Store.CountsAsync(ct))[2].Held);

        await db.Store.WriteAsync(new EgressAuditEntry(clock.Now, "ota", false, "master-off", "x.example.test"), ct);
        Assert.Equal("master-off", (await db.Store.LastRefusalsAsync(ct))["ota"].Reason);

        var fw = await db.Store.FirewallAsync(ct);
        Assert.Equal("Off", fw!.Wanted);
        Assert.Null(fw.Applied);
        Assert.True(await db.Store.SetWantedFirewallAsync("On", clock.Now, ct));
        Assert.False(await db.Store.SetWantedFirewallAsync("On", clock.Now, ct));
        Assert.Equal(2, Convert.ToInt32(await db.ScalarAsync("SELECT COUNT(*) FROM plat.role_grant WHERE permission_id = 'PERM-NET-002';"), CultureInfo.InvariantCulture));
    }
}
