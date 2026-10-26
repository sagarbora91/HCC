using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Inventory;
using Hcc.Application.Reservations;
using Hcc.Application.Tax;
using Hcc.Application.WebBooking;
using Hcc.Contracts;
using Hcc.Contracts.WebBooking;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Billing;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Inventory;
using Hcc.Infrastructure.SqlServer.Reservations;
using Hcc.Infrastructure.SqlServer.Tax;
using Hcc.Infrastructure.SqlServer.WebBooking;
using Microsoft.Data.SqlClient;

namespace Hcc.WebBooking.Tests.Harness;

public static class Who
{
    public const int Owner = 1, Manager = 2, Reception = 3, Housekeeper = 4;

    /// <summary>An Accountant given the web queue and reservation view by a user exception, without guest registration (F-L06 masks).</summary>
    public const int AccountantWithException = 5;
}

/// <summary>Stand-in for P1's policy (RBAC matrix v1 + the 2001 seed): Reception reviews web requests; only Owner/Manager record deposits.</summary>
public sealed class FakePolicy : IAccessPolicy
{
    private readonly Dictionary<int, HashSet<string>> _g = new()
    {
        [Who.Owner] = ["PERM-RES-001", "PERM-RES-002", "PERM-WEB-001", "PERM-PAY-002", "PERM-GST-002"],
        [Who.Manager] = ["PERM-RES-001", "PERM-RES-002", "PERM-WEB-001", "PERM-PAY-002", "PERM-GST-002"],
        [Who.Reception] = ["PERM-RES-001", "PERM-RES-002", "PERM-WEB-001", "PERM-GST-002"],
        [Who.Housekeeper] = [],
        [Who.AccountantWithException] = ["PERM-RES-001", "PERM-WEB-001"],
    };

    public Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default)
    {
        if (!_g.TryGetValue(principalId, out var g) || !g.Contains(permissionId)) throw new HccException(HccErrorCode.FORBIDDEN, "You do not have permission for this action.");
        return Task.CompletedTask;
    }

    public Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default)
        => Task.FromResult(_g.TryGetValue(principalId, out var g) && g.Contains(permissionId));
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
}

/// <summary>A disposable <c>HccTest_*</c> database migrated to the latest version.</summary>
public sealed partial class WebDatabase : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private WebDatabase(string server, string name)
    {
        Server = server;
        Name = name;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
    }

    public string Server { get; }

    public string Name { get; }

    public SqlPlatform Platform { get; }

    public static async Task<WebDatabase> CreateAsync(CancellationToken ct = default)
    {
        var server = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";
        var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        if (!NamePattern().IsMatch(name)) throw new InvalidOperationException("Unexpected test database name.");
        var db = new WebDatabase(server, name);
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(server, "master")))
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

    public async Task<long> LongAsync(string sql, CancellationToken ct = default) => Convert.ToInt64(await ScalarAsync(sql, ct), CultureInfo.InvariantCulture);

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

/// <summary>
/// A fictional property (business date 2026-10-24, Asia/Kolkata): Classic (max 2, ₹2,000) rooms 101/102 and Suite (max 4, ₹4,500 as in the
/// prototype BASE fixture) rooms 105/106, with the REAL services over it: dated GST master (DEC-069), inventory, reservations, billing store.
/// </summary>
public sealed class WebWorld : IAsyncDisposable
{
    private WebWorld(WebDatabase db, WebBookingOptions options)
    {
        Db = db;
        var conns = db.Platform.Connections;
        Options = options;
        Policy = new FakePolicy();
        Clock = new TestClock(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero));
        var master = new SqlGstMasterStore(conns);
        Gst = new GstRateResolver(master);
        InventoryStore = new SqlInventoryStore(conns);
        Inventory = new InventoryService(InventoryStore, db.Platform.Commands, Policy, Gst, Clock);
        BillingStore = new SqlBillingStore(conns);
        Reservations = new ReservationService(new SqlReservationStore(conns), InventoryStore, Inventory, db.Platform.Commands, Policy,
            new BillingReservationMoney(BillingStore), Clock);
        Store = new SqlWebBookingStore(conns);
        Web = new WebBookingService(Store, InventoryStore, Inventory, Reservations, BillingStore, Gst, db.Platform.Commands, Policy, options, Clock);
    }

    public WebDatabase Db { get; }

    public WebBookingOptions Options { get; }

    public FakePolicy Policy { get; }

    public TestClock Clock { get; }

    public GstRateResolver Gst { get; }

    public SqlInventoryStore InventoryStore { get; }

    public InventoryService Inventory { get; }

    public SqlBillingStore BillingStore { get; }

    public ReservationService Reservations { get; }

    public SqlWebBookingStore Store { get; }

    public WebBookingService Web { get; }

    public int PropertyId { get; private set; }

    public static async Task<WebWorld> CreateAsync(WebBookingOptions? options = null, CancellationToken ct = default)
    {
        var db = await WebDatabase.CreateAsync(ct);
        try
        {
            var w = new WebWorld(db, options ?? new WebBookingOptions { Enabled = true });
            await w.SeedAsync(ct);
            return w;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        PropertyId = Convert.ToInt32(await Db.ScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '2026-10-24');", ct), CultureInfo.InvariantCulture);
        await Db.ExecAsync($"""
            INSERT inv.room_type(property_id, code, max_occupancy, rack_rate_paise) VALUES ({PropertyId}, 'Classic', 2, 200000), ({PropertyId}, 'Suite', 4, 450000);
            INSERT inv.room(property_id, room_no, room_type_id) SELECT {PropertyId}, v.no, t.room_type_id FROM (VALUES ('101','Classic'),('102','Classic'),('105','Suite'),('106','Suite')) v(no, code)
              JOIN inv.room_type t ON t.code = v.code AND t.property_id = {PropertyId};
            """, ct);
    }

    public CommandContext Ctx(int principal, string key, string op = "Test") => new(PropertyId, principal, op, key);

    public static SubmitWebRequest Request(string type = "Suite", string arrival = "2026-10-25", string departure = "2026-10-27", int adults = 2, int children = 0,
        string guest = "Meher Ansari", string phone = "+91 98000 12345", string? email = "meher@example.test")
        => new(type, arrival, departure, adults, children, guest, phone, email);

    public async Task<WebRequestReceipt> SubmitAsync(SubmitWebRequest body, string? key = null)
        => (await Web.SubmitAsync(key ?? Guid.NewGuid().ToString("N"), body, TestContext.Current.CancellationToken)).Value;

    public Task<long> RequestIdAsync(string no) => Db.LongAsync($"SELECT request_id FROM web.request WHERE public_no = '{no}';");

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
