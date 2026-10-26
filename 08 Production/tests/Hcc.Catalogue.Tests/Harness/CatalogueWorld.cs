using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Catalogue;
using Hcc.Application.Commands;
using Hcc.Application.Tax;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Billing;
using Hcc.Infrastructure.SqlServer.Catalogue;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Masters;
using Hcc.Infrastructure.SqlServer.Tax;
using Microsoft.Data.SqlClient;

namespace Hcc.Catalogue.Tests.Harness;

/// <summary>Controllable clock (event times come from the injected TimeProvider).</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Disposable HccTest_* database with the prototype BASE shape for the catalogue: one property (business date 24 Oct 2026), the demo users
/// with their roles, room types Classic (₹2,000) and Suite (₹4,500), rooms 105 / 106, the catalogue seed batch of migration 2820 (run again
/// after the property exists, as an upgrade would) and helpers that create an in-house stay with a folio and posted room nights.
/// Real access policy, real command runner, real billing store and GST master (seed 5 % / 18 % rates).
/// </summary>
public sealed partial class CatalogueWorld : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private readonly string _server;
    private readonly string _name;
    private int _resNo = 1100;

    private CatalogueWorld(string server, string name)
    {
        _server = server;
        _name = name;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
        Clock = new TestClock(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.FromHours(5.5)).ToUniversalTime());
        Policy = new SqlAccessPolicy(Platform.Connections, Clock, []);
        var gstStore = new SqlGstMasterStore(Platform.Connections);
        Commands = new CatalogueCommands(Platform.Commands, Policy, new SqlCatalogueStore(), new SqlMastersStore(), new SqlBillingStore(Platform.Connections),
            new GstRateResolver(gstStore), Clock, gstStore);
        Queries = new CatalogueQueries(Policy, new SqlCatalogueReadStore(Platform.Connections), new SqlMastersReadStore(Platform.Connections));
    }

    public SqlPlatform Platform { get; }

    public TestClock Clock { get; }

    public SqlAccessPolicy Policy { get; }

    public CatalogueCommands Commands { get; }

    public CatalogueQueries Queries { get; }

    public int PropertyId { get; private set; }

    public Dictionary<string, int> Users { get; } = [];

    public CommandContext Ctx(string user, string key) => new(PropertyId, Users[user], "C", key);

    public static async Task<CatalogueWorld> CreateAsync(CancellationToken ct)
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

        var w = new CatalogueWorld(server, name);
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

    public async Task<long> LongAsync(string sql, CancellationToken ct = default) => Convert.ToInt64(await ScalarAsync(sql, ct), CultureInfo.InvariantCulture);

    public Task ExecAsync(string sql, CancellationToken ct = default) => ScalarAsync(sql, ct);

    /// <summary>The seed batch of migration 2820 (after its last GO).</summary>
    public static string MigrationSeedSql()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "database", "migrations", "2820_catalogue.sql"))) dir = dir.Parent;
        if (dir is null) throw new FileNotFoundException("database/migrations/2820_catalogue.sql not found above " + AppContext.BaseDirectory);
        var text = File.ReadAllText(Path.Combine(dir.FullName, "database", "migrations", "2820_catalogue.sql"));
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

        await ExecAsync($"INSERT inv.room_type(property_id, code, max_occupancy) VALUES ({PropertyId}, 'Classic', 3), ({PropertyId}, 'Suite', 4);" +
            $"INSERT inv.room(property_id, room_no, room_type_id) SELECT {PropertyId}, '105', room_type_id FROM inv.room_type WHERE property_id = {PropertyId} AND code = 'Classic';" +
            $"INSERT inv.room(property_id, room_no, room_type_id) SELECT {PropertyId}, '106', room_type_id FROM inv.room_type WHERE property_id = {PropertyId} AND code = 'Suite';", ct);
        await ExecAsync(MigrationSeedSql(), ct);
    }

    /// <summary>A reservation in room 105 / 106 (24 → 26 Oct) with a folio; in house with the room night of 24 Oct posted (5 %) unless told otherwise.</summary>
    public async Task<(long ReservationId, long FolioId)> StayAsync(string room = "105", string status = "InHouse", int adults = 2, int children = 0, bool postNight = true, CancellationToken ct = default)
    {
        var rate = room == "106" ? 450000 : 200000;
        var no = "HCC-" + (++_resNo).ToString(CultureInfo.InvariantCulture);
        var rid = await LongAsync($"""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, children, nightly_rate_paise)
            OUTPUT INSERTED.reservation_id
            SELECT {PropertyId}, '{no}', N'Catalogue Test', '2026-10-24', '2026-10-26', '{status}', r.room_id, r.room_type_id, {adults}, {children}, {rate}
              FROM inv.room r WHERE r.property_id = {PropertyId} AND r.room_no = '{room}';
            """, ct);
        var fid = await LongAsync($"INSERT bill.folio(reservation_id) OUTPUT INSERTED.folio_id VALUES ({rid});", ct);
        if (postNight) await PostRoomNightAsync(fid, rate, ct);
        return (rid, fid);
    }

    /// <summary>A room-night charge of 24 Oct at the room slab (posted "now" on the test clock).</summary>
    public Task PostRoomNightAsync(long folioId, long rate, CancellationToken ct = default) => ExecAsync(
        $"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise, posted_utc) VALUES ({folioId}, 'room', '2026-10-24', N'Room night · 2026-10-24', {rate}, 500, {rate * 5 / 100}, '{Clock.GetUtcNow().UtcDateTime:yyyy-MM-ddTHH:mm:ss.fff}');", ct);

    public async Task<string> FolioVersionAsync(long folioId, CancellationToken ct = default) =>
        Convert.ToBase64String((byte[])(await ScalarAsync($"SELECT row_ver FROM bill.folio WHERE folio_id = {folioId};", ct))!);

    public async Task<int> IdAsync(string table, string idColumn, string code, CancellationToken ct = default) =>
        await CountAsync($"SELECT {idColumn} FROM {table} WHERE property_id = {PropertyId} AND code = '{code}';", ct);

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
