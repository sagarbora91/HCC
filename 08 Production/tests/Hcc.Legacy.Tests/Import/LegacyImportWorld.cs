using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Commands;
using Hcc.Application.GuestIds;
using Hcc.Application.Legacy;
using Hcc.Contracts.Legacy;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Documents;
using Hcc.Infrastructure.SqlServer.GuestIds;
using Hcc.Infrastructure.SqlServer.Legacy;
using Microsoft.Data.SqlClient;

namespace Hcc.Legacy.Tests.Import;

public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// Disposable HccTest_* database (all migrations, real RBAC seed) with a fictional hotel on business date 26 Oct 2026, three fictional guests as the
/// masters importer would have created them (two share one phone, LG-R1), and the real legacy importer over the real SQL stores. Each world copies
/// <c>Fixtures/run-fictional</c> to its own temp folder (a committed guests part shreds the secret file).
/// </summary>
public sealed partial class LegacyImportWorld : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private readonly string _server;
    private readonly string _name;
    private int _key;

    private LegacyImportWorld(string server, string name)
    {
        _server = server;
        _name = name;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.FromHours(5.5)));
        var docs = new SqlDocumentStore(Platform.Connections);
        Service = new LegacyImportService(Platform.Commands, new SqlAccessPolicy(Platform.Connections, clock, []), new SqlLegacyStore(Platform.Connections),
            new SqlGuestIdStore(Platform.Connections), new GuestIdProtector(new StaticGuestIdKeyRing("k1", new Dictionary<string, byte[]> { ["k1"] = Key })), docs, docs,
            new SqlOpeningStore(Platform.Connections), clock);
        Folder = Path.Combine(Path.GetTempPath(), "hcc-lg-" + Guid.NewGuid().ToString("N")[..8], "run-fictional");
        Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "run-fictional"), Folder);
    }

    public SqlPlatform Platform { get; }

    public LegacyImportService Service { get; }

    public string Folder { get; }

    public int PropertyId { get; private set; }

    public Dictionary<string, int> Users { get; } = [];

    public Dictionary<string, long> Guests { get; } = [];

    public string SecretPath => Path.Combine(Folder, "secret", "guest_ids.jsonl");

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target);
        }
    }

    public CommandContext Ctx(string user) => new(PropertyId, Users[user], "T", "k-" + (++_key).ToString(CultureInfo.InvariantCulture));

    public LegacyImportRequest Request(bool dryRun, string? expected = null, params string[] parts) =>
        new(Folder, parts.Length == 0 ? ["guests", "history", "cutover"] : parts, dryRun, expected);

    public async Task<CommandResult<LegacyImportResult>> RunAsync(LegacyImportRequest req, string user = "owner") =>
        await Service.ImportAsync(Ctx(user), req, TestContext.Current.CancellationToken);

    public static async Task<LegacyImportWorld> CreateAsync(CancellationToken ct)
    {
        var server = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";
        var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(server, "master")))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"CREATE DATABASE [{name}];", conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var w = new LegacyImportWorld(server, name);
        try
        {
            await w.Platform.Migrations.MigrateAsync(ct);
            if (await w.ScalarAsync("SELECT SCHEMA_ID('lgc');") is DBNull or null)
                foreach (var batch in ContractSchemaStub.Batches) await w.ExecAsync(batch);
            await w.SeedAsync(ct);
            return w;
        }
        catch
        {
            await w.DisposeAsync();
            throw;
        }
    }

    public async Task<object?> ScalarAsync(string sql)
    {
        await using var conn = await Platform.Connections.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        return await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    public async Task<long> CountAsync(string sql) => Convert.ToInt64(await ScalarAsync(sql), CultureInfo.InvariantCulture);

    public Task ExecAsync(string sql) => ScalarAsync(sql);

    private async Task SeedAsync(CancellationToken ct)
    {
        PropertyId = Convert.ToInt32(await ScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '2026-10-26');"),
            CultureInfo.InvariantCulture);
        foreach (var (login, role) in new[] { ("owner", "OWN"), ("manager", "MGR") })
        {
            var id = Convert.ToInt32(await ScalarAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({PropertyId}, '{login}', N'{login}', '2026-01-01');"),
                CultureInfo.InvariantCulture);
            await ExecAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2026-01-01', '2026-01-01');");
            Users[login] = id;
        }

        // Fictional guests as the masters importer would have created them from guests.csv (origin manual). Two share one phone (LG-R1).
        foreach (var (name, phone) in new[] { ("Ravi Demo", "9876500001"), ("Rita Demo", "9876500001"), ("Sana Sample", "9876500002") })
            Guests[name] = Convert.ToInt64(await ScalarAsync($"INSERT gst.guest(property_id, full_name, phone, origin, created_utc) OUTPUT INSERTED.guest_id VALUES ({PropertyId}, N'{name}', '{phone}', 'manual', '2026-10-25');"),
                CultureInfo.InvariantCulture);
        _ = ct;
    }

    public async Task CloseFirstDayAsync()
    {
        var o = Users["owner"];
        await ExecAsync($"""
            DECLARE @r BIGINT;
            INSERT cls.close_run(property_id, business_date, run_ref, last_run_ref, status, current_step, attempts, started_by, started_utc, last_attempt_by, last_attempt_utc)
            VALUES ({PropertyId}, '2026-10-26', 'run-1', 'run-1', 'Completed', 'done', 1, {o}, '2026-10-26T18:00:00', {o}, '2026-10-26T18:00:00');
            SET @r = SCOPE_IDENTITY();
            INSERT cls.business_day(property_id, business_date, run_id, closed_by, closed_utc) VALUES ({PropertyId}, '2026-10-26', @r, {o}, '2026-10-26T18:00:00');
            """);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(Folder)!, true);
        }
        catch (IOException)
        {
        }

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
