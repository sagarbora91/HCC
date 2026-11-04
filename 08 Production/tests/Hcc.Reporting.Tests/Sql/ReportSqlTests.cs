// F7-R1 · migrations 4400–4405 on a disposable HccTest_* database: stores round-trip (favourites, schedules, runs, export log), PERM-RPT-004
// grants, and the runner's rule V / M through the real SqlAccessPolicy (Cashier's V·S does not reach a property-wide report).
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Reporting.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Tests.Sql;

public sealed class ReportDatabase : IAsyncLifetime
{
    private string _server = "";

    public string Name { get; } = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];

    public SqlPlatform Platform { get; private set; } = null!;

    public int PropertyId { get; private set; }

    public Dictionary<string, int> Users { get; } = new(StringComparer.Ordinal);

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
        PropertyId = Convert.ToInt32(await ScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('HPE', N'Test Hotel', 'Asia/Kolkata', '2026-10-24');"), CultureInfo.InvariantCulture);
        foreach (var role in new[] { "OWN", "MGR", "REC", "CSH", "ACC", "HKP" })
        {
            var id = Convert.ToInt32(await ScalarAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({PropertyId}, '{role.ToLowerInvariant()}', N'{role}', '2026-01-01');"), CultureInfo.InvariantCulture);
            await ScalarAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2026-01-01', '2026-01-01');");
            Users[role] = id;
        }
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
public sealed class ReportSqlTests(ReportDatabase db) : IClassFixture<ReportDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stores_round_trip_favourites_schedules_runs_and_the_export_log()
    {
        var s = new SqlReportStores(db.Platform.Connections);
        var p = db.PropertyId;
        Assert.Equal(new ReportProperty("HPE", "Test Hotel", new DateOnly(2026, 10, 24)), await s.GetAsync(p, Ct));
        Assert.Contains(p, await s.PropertyIdsAsync(Ct));
        Assert.True(await s.ToggleAsync(p, 1, "rpt.money.x", Ct));
        Assert.Equal(["rpt.money.x"], await s.FavouritesAsync(p, 1, Ct));
        Assert.False(await s.ToggleAsync(p, 1, "rpt.money.x", Ct));
        // AF-FIX2 F6: SetAsync is idempotent (a retried request after a lost answer leaves the same state).
        Assert.True(await s.SetAsync(p, 1, "rpt.money.x", true, Ct));
        Assert.True(await s.SetAsync(p, 1, "rpt.money.x", true, Ct));
        Assert.Equal(["rpt.money.x"], await s.FavouritesAsync(p, 1, Ct));
        Assert.False(await s.SetAsync(p, 1, "rpt.money.x", false, Ct));
        Assert.False(await s.SetAsync(p, 1, "rpt.money.x", false, Ct));
        Assert.Empty(await s.FavouritesAsync(p, 1, Ct));
        var id =await s.SaveAsync(p, new ReportScheduleRecord(0, "rpt.money.x", new Dictionary<string, string> { ["range"] = "yesterday" }, ReportFormat.Xlsx, "weekly", 1, new TimeOnly(7, 0), 2, [1, 5], true), Ct);
        var sc = Assert.Single(await s.ListAsync(p, Ct));
        Assert.Equal(("weekly", (int?)1, new TimeOnly(7, 0), "yesterday", 5), (sc.Cadence, sc.Day, sc.Time, sc.Params["range"], sc.Recipients[1]));
        await s.SaveAsync(p, sc with { LastRun = new DateTimeOffset(2026, 10, 24, 2, 0, 0, TimeSpan.Zero), Enabled = false }, Ct);
        Assert.False((await s.ListAsync(p, Ct))[0].Enabled);
        await Assert.ThrowsAsync<SqlException>(() => s.SaveAsync(p, sc with { Id = 0, Cadence = "monthly", Day = 31 }, Ct)); // CK_rpt_schedule_day
        var run = await s.StartAsync(p, new ReportRunRecord(0, id, "rpt.money.x", "{}", 2, DateTimeOffset.UtcNow, null, "running"), Ct);
        await s.FinishAsync(p, run, "held", 12, "rpt/1/x.xlsx", null, Ct);
        var r = Assert.Single(await s.ListAsync(p, 10, Ct));
        Assert.Equal(("held", (int?)12, "rpt/1/x.xlsx"), (r.Status, r.Rows, r.FileRef));
        await s.DeleteAsync(p, id, Ct);
        Assert.Null((await s.ListAsync(p, 10, Ct))[0].ScheduleId); // ON DELETE SET NULL keeps the run
        await s.AppendAsync(p, new ReportExportLogRecord(0, DateTimeOffset.UtcNow, 5, "rpt.money.x", "{\"from\":\"2026-10-20\"}", ReportFormat.Pdf, 7, "a.pdf", true), Ct);
        var log = Assert.Single(await s.ExportLogAsync(p, 10, Ct));
        Assert.Equal((ReportFormat.Pdf, 7, true), (log.Format, log.Rows, log.Provisional));
        Assert.Equal(1, Convert.ToInt32(await db.ScalarAsync("SELECT COUNT(*) FROM plat.role_grant WHERE permission_id = 'PERM-RPT-004' AND role_code = 'ACC';"), CultureInfo.InvariantCulture));
        Assert.Equal(0, Convert.ToInt32(await db.ScalarAsync("SELECT COUNT(*) FROM plat.role_grant WHERE permission_id LIKE 'PERM-RPT-%' AND role_code IN ('SUP','HKP','MNT');"), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Runner_rule_V_and_M_through_the_real_access_policy()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 24, 6, 0, 0, TimeSpan.Zero));
        var policy = new SqlAccessPolicy(db.Platform.Connections, clock, []);
        var runner = new ReportRunner(new ReportCatalogue([new DemoSelector()]), policy, new SqlReportStores(db.Platform.Connections), [new FixedCodeEngine()], clock);
        Caller C(string role) => new(db.PropertyId, db.Users[role]);
        foreach (var role in new[] { "OWN", "MGR", "ACC" })
            Assert.Contains((await runner.RunAsync(C(role), "rpt.admin.engine-test", Rig.NoParams, new RunOptions(Export: true), Ct)).Columns, c => c.Key == "amt");
        foreach (var role in new[] { "REC", "CSH", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => runner.RunAsync(C(role), "rpt.admin.engine-test", Rig.NoParams, null, Ct))).Code);
        Assert.False(await runner.HoldsMoneyGateAsync(C("CSH"), new DemoSelector().Definition, Ct));
        Assert.True(await runner.HoldsMoneyGateAsync(C("CSH"), new DemoSelector().Definition with { OwnShift = true }, Ct));
    }
}
