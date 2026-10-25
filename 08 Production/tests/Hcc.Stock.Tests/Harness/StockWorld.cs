using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Stock;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Stock;
using Microsoft.Data.SqlClient;

namespace Hcc.Stock.Tests.Harness;

/// <summary>Demo principals (prototype user ids → integer principal ids), as in the other module test projects.</summary>
public static class Users
{
    public const int Owner = 1, Manager = 2, Reception = 3, Cashier = 4, Supervisor = 5, Housekeeper = 6, Housekeeper2 = 7, Accountant = 8, Maint = 9, TechAdmin = 10;
}

/// <summary>
/// Test stand-in for P1's policy with the grants Linen &amp; stock needs (RBAC v1 + DEC-063): PERM-HK-003 (Housekeeper/Housekeeper2 assigned scope,
/// Supervisor/Manager/Owner property-wide), PERM-HK-004 (Supervisor/Manager/Owner), PERM-RPT-002 (Manager/Owner/Accountant; Cashier own shift only).
/// </summary>
public sealed class FakeAccessPolicy : IAccessPolicy
{
    private readonly Dictionary<int, Dictionary<string, char>> _grants = new()
    {
        [Users.Owner] = new() { ["PERM-HK-003"] = 'P', ["PERM-HK-004"] = 'P', ["PERM-RPT-002"] = 'P' },
        [Users.Manager] = new() { ["PERM-HK-003"] = 'P', ["PERM-HK-004"] = 'P', ["PERM-RPT-002"] = 'P' },
        [Users.Supervisor] = new() { ["PERM-HK-003"] = 'P', ["PERM-HK-004"] = 'P' },
        [Users.Housekeeper] = new() { ["PERM-HK-003"] = 'A' },
        [Users.Housekeeper2] = new() { ["PERM-HK-003"] = 'A' },
        [Users.Accountant] = new() { ["PERM-RPT-002"] = 'P' },
        [Users.Cashier] = new() { ["PERM-RPT-002"] = 'S' },
        [Users.Reception] = new(),
        [Users.Maint] = new(),
        [Users.TechAdmin] = new(),
    };

    public Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default) =>
        Holds(principalId, permissionId, scope, nullMeansAny: false)
            ? Task.CompletedTask
            : throw new HccException(HccErrorCode.FORBIDDEN, "Access denied.");

    public Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default) =>
        Task.FromResult(Holds(principalId, permissionId, scope, nullMeansAny: true));

    private bool Holds(int principalId, string perm, AccessScope? scope, bool nullMeansAny)
    {
        if (!_grants.TryGetValue(principalId, out var g) || !g.TryGetValue(perm, out var s)) return false;
        if (s == 'P') return true;
        return scope?.Kind == AccessScopeKinds.Any || (scope is null && nullMeansAny);
    }
}

public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

public static class Expect
{
    public static async Task<string> CodeAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await action();
        }
        catch (HccException e)
        {
            return e.Code.ToString();
        }

        return "NO_ERROR";
    }

    public static async Task<HccException> ErrorAsync(HccErrorCode code, Func<Task> action)
    {
        var e = await Assert.ThrowsAsync<HccException>(action);
        Assert.Equal(code, e.Code);
        return e;
    }
}

/// <summary>A disposable <c>HccTest_*</c> database migrated to the latest version.</summary>
public sealed partial class StockDatabase : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private StockDatabase(string server, string name)
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

    public static async Task<StockDatabase> CreateAsync(CancellationToken ct = default)
    {
        var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        if (!NamePattern().IsMatch(name)) throw new InvalidOperationException("Unexpected test database name.");
        var db = new StockDatabase(TestServer, name);
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

/// <summary>
/// A fresh database seeded from the prototype fixture (<c>03 Prototype/src/fixtures/ext/inventory.ts</c>): property at business date 2026-10-23 with
/// 24 rooms (12 Classic, 9 Deluxe, 3 Suite), 17 items, one set of linen in rooms, seven days of consumption (17–23 Oct), laundry batch LB-S2 out,
/// count CNT-S2 awaiting review (floor 2, by the housekeeper). Fictional data only.
/// </summary>
public sealed class StockWorld : IAsyncDisposable
{
    public const string Business = "2026-10-23";

    private static readonly string[] Locs = ["store", "floor1", "floor2", "floor3", "rooms", "soiled", "laundry"];

    private sealed record Seed(string Id, string Name, string Kind, string Unit, long CostRs, int Min, int Reorder, int StdX100, int[] Stock, int[]? Par = null, long LaundryRate = 0, string? Supplier = null);

    // stock order: store, floor1, floor2, floor3, rooms, soiled, laundry; par order: Classic, Deluxe, Suite
    private static readonly Seed[] Seeds =
    [
        new("LIN-BS", "Bed sheet (king)", "linen", "pc", 650, 50, 40, 150, [40, 6, 6, 6, 48, 10, 24], [2, 2, 2], 1800),
        new("LIN-PC", "Pillow cover", "linen", "pc", 120, 90, 60, 300, [82, 12, 10, 12, 102, 18, 40], [4, 4, 6], 600),
        new("LIN-DC", "Duvet cover", "linen", "pc", 1400, 24, 12, 40, [22, 3, 3, 3, 24, 4, 8], [1, 1, 1], 4500),
        new("LIN-BT", "Bath towel", "linen", "pc", 450, 55, 60, 180, [30, 5, 4, 6, 54, 14, 30], [2, 2, 4], 1400),
        new("LIN-HT", "Hand towel", "linen", "pc", 180, 55, 48, 170, [52, 8, 7, 8, 54, 12, 26], [2, 2, 4], 700),
        new("LIN-BM", "Bath mat", "linen", "pc", 220, 28, 24, 80, [28, 4, 3, 4, 27, 6, 10], [1, 1, 2], 1000),
        new("LIN-BR", "Bathrobe", "linen", "pc", 1600, 6, 4, 20, [6, 0, 0, 2, 6, 1, 2], [0, 0, 2], 4000),
        new("AMN-SP", "Soap bar 30 g", "amenity", "pc", 14, 120, 300, 200, [260, 30, 28, 26, 0, 0, 0], null, 0, "Morning Supplies"),
        new("AMN-SH", "Shampoo 30 ml", "amenity", "pc", 12, 120, 300, 200, [70, 14, 10, 12, 0, 0, 0], null, 0, "Morning Supplies"),
        new("AMN-DK", "Dental kit", "amenity", "pc", 18, 80, 200, 130, [150, 20, 18, 16, 0, 0, 0], null, 0, "Morning Supplies"),
        new("AMN-WB", "Water bottle 500 ml", "amenity", "pc", 9, 150, 480, 200, [310, 36, 40, 30, 0, 0, 0], null, 0, "Morning Supplies"),
        new("AMN-TK", "Tea & coffee kit", "amenity", "pc", 15, 100, 240, 200, [180, 24, 20, 22, 0, 0, 0], null, 0, "Morning Supplies"),
        new("AMN-SL", "Slippers (pair)", "amenity", "pair", 35, 40, 100, 50, [64, 8, 6, 6, 0, 0, 0], null, 0, "Morning Supplies"),
        new("CLN-FC", "Floor cleaner 5 L", "cleaning", "can", 450, 4, 6, 5, [5, 1, 1, 1, 0, 0, 0], null, 0, "Deccan Hygiene Traders"),
        new("CLN-GC", "Glass cleaner 500 ml", "cleaning", "bottle", 120, 6, 12, 8, [8, 2, 1, 2, 0, 0, 0], null, 0, "Deccan Hygiene Traders"),
        new("CLN-TC", "Toilet cleaner 1 L", "cleaning", "bottle", 95, 10, 24, 15, [14, 3, 2, 3, 0, 0, 0], null, 0, "Deccan Hygiene Traders"),
        new("CLN-GB", "Garbage bags (roll of 30)", "cleaning", "roll", 60, 10, 20, 10, [4, 1, 1, 0, 0, 0, 0], null, 0, "Deccan Hygiene Traders"),
    ];

    private static readonly Dictionary<string, int> History = new()
    {
        ["2026-10-17"] = 12, ["2026-10-18"] = 14, ["2026-10-19"] = 15, ["2026-10-20"] = 11, ["2026-10-21"] = 9, ["2026-10-22"] = 10, ["2026-10-23"] = 8,
    };

    private StockWorld(StockDatabase db, int propertyId)
    {
        Db = db;
        PropertyId = propertyId;
        var conns = db.Platform.Connections;
        Reads = new SqlStockReadStore(conns);
        Commands = new StockCommands(db.Platform.Commands, Policy, new SqlStockStore(), Clock);
        Queries = new StockQueries(Policy, Reads);
    }

    public StockDatabase Db { get; }

    public int PropertyId { get; }

    public FakeAccessPolicy Policy { get; } = new();

    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero));

    public SqlStockReadStore Reads { get; }

    public StockCommands Commands { get; }

    public StockQueries Queries { get; }

    public static async Task<StockWorld> CreateAsync(CancellationToken ct = default)
    {
        var db = await StockDatabase.CreateAsync(ct);
        try
        {
            var p = Convert.ToInt32(await db.ScalarAsync($"INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '{Business}');", ct), CultureInfo.InvariantCulture);
            await db.ExecAsync($"INSERT inv.room_type(property_id, code, max_occupancy) VALUES ({p}, 'Classic', 2), ({p}, 'Deluxe', 3), ({p}, 'Suite', 4);", ct);
            var sb = new StringBuilder();
            for (var i = 1; i <= 24; i++)
            {
                var type = i <= 12 ? "Classic" : i <= 21 ? "Deluxe" : "Suite";
                sb.AppendLine(CultureInfo.InvariantCulture, $"INSERT inv.room(property_id, room_no, room_type_id) SELECT {p}, '{100 + i}', room_type_id FROM inv.room_type WHERE property_id = {p} AND code = '{type}';");
            }

            await db.ExecAsync(sb.ToString(), ct);
            sb.Clear();
            foreach (var s in Seeds)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"INSERT stk.item(property_id, code, name, kind, unit, cost_paise, min_qty, reorder_qty, std_x100, laundry_rate_paise, supplier, created_utc) VALUES ({p}, '{s.Id}', N'{s.Name}', '{s.Kind}', '{s.Unit}', {s.CostRs * 100}, {s.Min}, {s.Reorder}, {s.StdX100}, {(s.Kind == "linen" ? s.LaundryRate.ToString(CultureInfo.InvariantCulture) : "NULL")}, {(s.Supplier is null ? "NULL" : "N'" + s.Supplier + "'")}, '2026-10-01');");
                for (var l = 0; l < Locs.Length; l++)
                    sb.AppendLine(CultureInfo.InvariantCulture, $"INSERT stk.stock(item_id, loc, qty) SELECT item_id, '{Locs[l]}', {s.Stock[l]} FROM stk.item WHERE property_id = {p} AND code = '{s.Id}';");
                if (s.Par is { } par)
                    for (var t = 0; t < 3; t++)
                        sb.AppendLine(CultureInfo.InvariantCulture, $"INSERT stk.item_par(item_id, room_type_id, pieces) SELECT i.item_id, rt.room_type_id, {par[t]} FROM stk.item i JOIN inv.room_type rt ON rt.property_id = i.property_id AND rt.code = '{new[] { "Classic", "Deluxe", "Suite" }[t]}' WHERE i.property_id = {p} AND i.code = '{s.Id}';");
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"INSERT stk.setting(property_id, par_sets, vendor_name, turnaround_days) VALUES ({p}, 3, N'Fresh Linen Services', 1);");
            foreach (var (d, n) in History) sb.AppendLine(CultureInfo.InvariantCulture, $"INSERT stk.occupancy_history(property_id, night, rooms) VALUES ({p}, '{d}', {n});");

            // Seven days of consumption (17–23 Oct) exactly as the prototype generator: shampoo runs about 25% above standard.
            var dates = History.Keys.Order(StringComparer.Ordinal).ToList();
            for (var i = 0; i < dates.Count; i++)
                for (var j = 0; j < Seeds.Length; j++)
                {
                    var s = Seeds[j];
                    var factor = (1 + (((i * 7 + j * 3) % 9) - 4) / 40.0) * (s.Id == "AMN-SH" ? 1.25 : 1);
                    var q = (int)Math.Floor(History[dates[i]] * s.StdX100 / 100.0 * factor + 0.5);
                    if (q == 0) continue;
                    var kind = s.Kind == "linen" ? "room-change" : "use";
                    sb.AppendLine(CultureInfo.InvariantCulture, $"INSERT stk.move(property_id, business_date, at_utc, kind, item_id, qty, from_loc, to_loc, actor_id) SELECT {p}, '{dates[i]}', '{dates[i]}T11:30:00', '{kind}', item_id, {q}, 'floor{(i + j) % 3 + 1}', '{(s.Kind == "linen" ? "rooms" : "used")}', {(i % 2 == 1 ? Users.Housekeeper : Users.Housekeeper2)} FROM stk.item WHERE property_id = {p} AND code = '{s.Id}';");
                }

            // LB-S2: all laundry stock of every linen item, out since 21 Oct.
            sb.AppendLine(CultureInfo.InvariantCulture, $"INSERT stk.laundry_batch(property_id, vendor, sent_date, sent_utc, status, actor_id) VALUES ({p}, N'Fresh Linen Services', '2026-10-21', '2026-10-21T16:40:00', 'Out', {Users.Housekeeper2});");
            sb.AppendLine(CultureInfo.InvariantCulture, $"INSERT stk.laundry_line(batch_id, item_id, sent) SELECT (SELECT MAX(batch_id) FROM stk.laundry_batch), i.item_id, s.qty FROM stk.item i JOIN stk.stock s ON s.item_id = i.item_id AND s.loc = 'laundry' WHERE i.property_id = {p} AND i.kind = 'linen' AND s.qty > 0;");
            // CNT-S2: floor 2 count by the housekeeper: water bottles 37 (system 40), hand towels 6 (system 7).
            sb.AppendLine(CultureInfo.InvariantCulture, $"INSERT stk.count(property_id, business_date, at_utc, loc, status, actor_id, note) VALUES ({p}, '2026-10-22', '2026-10-22T07:45:00', 'floor2', 'Submitted', {Users.Housekeeper}, N'Morning count before rounds');");
            sb.AppendLine(CultureInfo.InvariantCulture, $"INSERT stk.count_line(count_id, item_id, system_qty, counted_qty) SELECT (SELECT MAX(count_id) FROM stk.count), i.item_id, s.qty, CASE i.code WHEN 'AMN-WB' THEN 37 WHEN 'LIN-HT' THEN 6 ELSE s.qty END FROM stk.item i JOIN stk.stock s ON s.item_id = i.item_id AND s.loc = 'floor2' WHERE i.property_id = {p};");
            await db.ExecAsync(sb.ToString(), ct);
            return new StockWorld(db, p);
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    public CommandContext Ctx(int principal, string key) => new(PropertyId, principal, "W2-8", key);

    public async Task<StockItemRecord> ItemAsync(string code) => (await Reads.ItemsAsync(PropertyId)).Single(i => i.Code == code);

    public async Task<int> StockAsync(string code, string loc) => (await ItemAsync(code)).Stock[loc];

    public async Task<int> TotalAsync(string code) => Hcc.Domain.Stock.StockRules.Total((await ItemAsync(code)).Stock);

    /// <summary>A count map for one location with every item at its system quantity (then adjusted by the caller).</summary>
    public async Task<Dictionary<string, int>> CountMapAsync(string loc) =>
        (await Reads.ItemsAsync(PropertyId)).ToDictionary(i => i.Code, i => i.Stock[loc], StringComparer.Ordinal);

    public async Task<string> VersionAsync(string code) => Hcc.Application.Stays.VersionToken.ToWire((await ItemAsync(code)).Version);

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
