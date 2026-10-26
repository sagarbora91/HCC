using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Commands;
using Hcc.Application.Import;
using Hcc.Contracts.Import;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Billing;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Import;
using Hcc.Infrastructure.SqlServer.Stays;
using Hcc.Infrastructure.SqlServer.Yield;
using Microsoft.Data.SqlClient;

namespace Hcc.Import.Tests.Harness;

public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Paths of the shipped templates and fictional samples (deployment/import-templates).</summary>
public static class Templates
{
    public static DirectoryInfo ProductionRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Hcc.slnx"))) return d;
        throw new InvalidOperationException("Hcc.slnx not found above the test output folder.");
    }

    public static string Folder => Path.Combine(ProductionRoot().FullName, "deployment", "import-templates");

    public static string Sample(string master) => File.ReadAllText(Path.Combine(Folder, "samples", master + ".csv"));

    public static string Template(string master) => File.ReadAllText(Path.Combine(Folder, "templates", master + ".csv"));
}

/// <summary>
/// Disposable HccTest_* database with an empty fictional hotel in setup (business date 26 Oct 2026, no day closed), the REAL access policy
/// (RBAC seed incl. 2701) and the real import service over the real module stores (inventory, yield, access, billing, stays, POS, payroll, groups,
/// maintenance, stock).
/// </summary>
public sealed partial class ImportWorld : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private readonly string _server;
    private readonly string _name;
    private int _key;

    private ImportWorld(string server, string name)
    {
        _server = server;
        _name = name;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
        Clock = new TestClock(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.FromHours(5.5)).ToUniversalTime());
        Policy = new SqlAccessPolicy(Platform.Connections, Clock, []);
        Billing = new SqlBillingStore(Platform.Connections);
        Yield = new SqlYieldStore(Platform.Connections);
        Rates = new SqlRatePublicationPort(Platform.Connections);
        Store = new SqlImportStore(Platform.Connections);
        Service = new ImportService(Platform.Commands, Policy, Store, new SqlImportInventoryWriter(), new SqlImportAccessWriter(), Rates, Yield, Billing, new SqlStaysStore(), new Hcc.Infrastructure.SqlServer.Pos.SqlPosStore(Platform.Connections),
            new Hcc.Infrastructure.SqlServer.Payroll.SqlPayrollStore(Platform.Connections), new Hcc.Infrastructure.SqlServer.Groups.SqlCompanyStore(Platform.Connections),
            new Hcc.Infrastructure.SqlServer.Maintenance.SqlMaintenanceStore(), new Hcc.Infrastructure.SqlServer.Stock.SqlStockStore(), Clock);
    }

    public SqlPlatform Platform { get; }

    public TestClock Clock { get; }

    public SqlAccessPolicy Policy { get; }

    public SqlBillingStore Billing { get; }

    public SqlYieldStore Yield { get; }

    public SqlRatePublicationPort Rates { get; }

    public SqlImportStore Store { get; }

    public ImportService Service { get; }

    public int PropertyId { get; private set; }

    public Dictionary<string, int> Users { get; } = [];

    public CommandContext Ctx(string user, string? key = null) => new(PropertyId, Users[user], "T", key ?? "k-" + (++_key).ToString(CultureInfo.InvariantCulture));

    public async Task<ImportBatchReport> UploadAsync(string user, string master, string csv, string? asOf = null, string? fileName = null) =>
        (await Service.UploadAsync(Ctx(user), new UploadImportBatchRequest(master, fileName ?? master + ".csv", csv, asOf), TestContext.Current.CancellationToken)).Value;

    public async Task<ImportCommitResult> CommitAsync(string user, long batchId, bool dryRun = false, string? key = null) =>
        (await Service.CommitAsync(Ctx(user, key), new CommitImportBatchRequest(batchId, dryRun), TestContext.Current.CancellationToken)).Value;

    /// <summary>Uploads a shipped sample, asserts it is valid and commits it as the Owner.</summary>
    public async Task<ImportCommitResult> ImportSampleAsync(string master, string? asOf = null)
    {
        var r = await UploadAsync("owner", master, Templates.Sample(master), asOf);
        Assert.True(r.ErrorCount == 0, master + ": " + string.Join(" | ", r.Errors.Select(e => $"{e.Row}/{e.Column}: {e.Message}")));
        return await CommitAsync("owner", r.BatchId);
    }

    public static async Task<ImportWorld> CreateAsync(CancellationToken ct)
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

        var w = new ImportWorld(server, name);
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

    public Task<object?> ScalarAsync(string sql) => QueryAsync(sql, TestContext.Current.CancellationToken);

    private async Task<object?> QueryAsync(string sql, CancellationToken ct)
    {
        await using var conn = await Platform.Connections.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        return await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<long> CountAsync(string sql) => Convert.ToInt64(await ScalarAsync(sql), CultureInfo.InvariantCulture);

    public Task ExecAsync(string sql) => ScalarAsync(sql);

    /// <summary>Closes the business date like P6 does at its barrier (close run + cls.business_day) and advances the date.</summary>
    public async Task CloseFirstDayAsync()
    {
        await ExecAsync($"""
            DECLARE @r BIGINT;
            INSERT cls.close_run(property_id, business_date, run_ref, last_run_ref, status, current_step, attempts, started_by, started_utc, last_attempt_by, last_attempt_utc)
            VALUES ({PropertyId}, '2026-10-26', 'run-1', 'run-1', 'Completed', 'done', 1, {Users["owner"]}, '2026-10-26T18:00:00', {Users["owner"]}, '2026-10-26T18:00:00');
            SET @r = SCOPE_IDENTITY();
            INSERT cls.business_day(property_id, business_date, run_id, closed_by, closed_utc) VALUES ({PropertyId}, '2026-10-26', @r, {Users["owner"]}, '2026-10-26T18:00:00');
            UPDATE inv.property SET business_date = '2026-10-27' WHERE property_id = {PropertyId};
            """);
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        PropertyId = Convert.ToInt32(await QueryAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '2026-10-26');", ct), CultureInfo.InvariantCulture);
        foreach (var (login, role) in new[] { ("owner", "OWN"), ("techadmin", "TAD"), ("manager", "MGR"), ("reception", "REC"), ("accountant", "ACC") })
        {
            var id = Convert.ToInt32(await QueryAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({PropertyId}, '{login}', N'{login}', '2026-01-01');", ct), CultureInfo.InvariantCulture);
            await ExecAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2026-01-01', '2026-01-01');");
            Users[login] = id;
        }
    }

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
