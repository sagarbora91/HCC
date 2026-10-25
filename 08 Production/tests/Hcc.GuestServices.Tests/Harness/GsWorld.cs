using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.GuestServices;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Billing;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.GuestServices;
using Hcc.Infrastructure.SqlServer.Reservations;
using Microsoft.Data.SqlClient;

namespace Hcc.GuestServices.Tests.Harness;

/// <summary>Demo principals (fictional) mapped to the RBAC role templates.</summary>
public static class Who
{
    public const int Owner = 1, Manager = 2, Reception = 3, Cashier = 4, Supervisor = 5, Housekeeper = 6, Housekeeper2 = 7, Accountant = 8, Maint = 9;
}

/// <summary>
/// Test stand-in for P1's policy: grants from RBAC matrix v1 for the permission ids this module reuses (P = property-wide, A = assigned only)
/// plus billing's folio / approval grants with the DEC-063 discount limits (Reception ₹250, Manager ₹1,000).
/// </summary>
public sealed class FakePolicy : IAccessPolicy
{
    private const long Unlimited = long.MaxValue;

    private readonly Dictionary<int, Dictionary<string, (char Scope, long Limit)>> _g = new()
    {
        [Who.Owner] = Wide("PERM-GST-002", "PERM-HK-002", "PERM-HK-003", "PERM-ROOM-001", "PERM-APR-001", "PERM-RPT-001", "PERM-FOL-001", "PERM-FOL-003"),
        [Who.Manager] = Wide("PERM-GST-002", "PERM-HK-002", "PERM-HK-003", "PERM-ROOM-001", "PERM-APR-001", "PERM-RPT-001", "PERM-FOL-001", "PERM-FOL-003"),
        [Who.Reception] = Wide("PERM-GST-002", "PERM-ROOM-001", "PERM-FOL-001", "PERM-FOL-003"),
        [Who.Cashier] = Wide("PERM-FOL-001"),
        [Who.Supervisor] = Wide("PERM-HK-002", "PERM-HK-003", "PERM-ROOM-001"),
        [Who.Housekeeper] = new() { ["PERM-HK-003"] = ('A', Unlimited), ["PERM-ROOM-001"] = ('A', Unlimited) },
        [Who.Housekeeper2] = new() { ["PERM-HK-003"] = ('A', Unlimited), ["PERM-ROOM-001"] = ('A', Unlimited) },
        [Who.Maint] = new() { ["PERM-ROOM-001"] = ('A', Unlimited) },
        [Who.Accountant] = Wide("PERM-RPT-001"),
    };

    public FakePolicy()
    {
        _g[Who.Manager]["PERM-FOL-003"] = ('P', 100_000);
        _g[Who.Reception]["PERM-FOL-003"] = ('P', 25_000);
    }

    private static Dictionary<string, (char, long)> Wide(params string[] perms) => perms.ToDictionary(p => p, _ => ('P', Unlimited));

    public Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default)
    {
        if (!_g.TryGetValue(principalId, out var g) || !g.TryGetValue(permissionId, out var grant)
            || (grant.Scope == 'A' && scope?.Kind != AccessScopeKinds.Any))
            throw new HccException(HccErrorCode.FORBIDDEN, $"Access denied: {permissionId} permission is required.");
        if (amountPaise is { } a && a > grant.Limit) throw new HccException(HccErrorCode.APPROVAL_REQUIRED, $"{permissionId} above your limit needs approval.");
        return Task.CompletedTask;
    }

    public Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default)
    {
        if (!_g.TryGetValue(principalId, out var g) || !g.TryGetValue(permissionId, out var grant)) return Task.FromResult(false);
        return Task.FromResult(grant.Scope == 'P' || scope is null || scope.Kind == AccessScopeKinds.Any);
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
}

/// <summary>A disposable <c>HccTest_*</c> database migrated to the latest version (same naming/drop rules as the other harnesses).</summary>
public sealed partial class GsDatabase : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private GsDatabase(string server, string name)
    {
        Server = server;
        Name = name;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
    }

    public string Server { get; }

    public string Name { get; }

    public SqlPlatform Platform { get; }

    public static async Task<GsDatabase> CreateAsync(CancellationToken ct = default)
    {
        var server = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";
        var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        if (!NamePattern().IsMatch(name)) throw new InvalidOperationException("Unexpected test database name.");
        var db = new GsDatabase(server, name);
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
/// A fictional property (business date 2026-10-24, Asia/Kolkata) with rooms 101 / 102 / 104 / 201 / 202 / 206 / 301, four in-house stays
/// (each folio holds two room nights of ₹2,000 + 10 % tax = ₹4,000 net), one departed stay and one confirmed (not yet arrived) booking.
/// </summary>
public sealed class GsWorld : IAsyncDisposable
{
    private int _public = 3000;

    private GsWorld(GsDatabase db)
    {
        Db = db;
        var conns = db.Platform.Connections;
        Policy = new FakePolicy();
        Clock = new TestClock(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero));
        BillingStore = new SqlBillingStore(conns);
        Billing = new BillingService(db.Platform.Commands, BillingStore, Policy,
            new StayNightSource(BillingStore, new SqlAgreedNightRateSource(conns), new FixedTaxRateSource()), new FixedTaxRateSource(), Clock);
        Store = new SqlGuestServicesStore(conns);
        Commands = new GuestServicesCommands(db.Platform.Commands, Policy, Store, Billing, BillingStore, Clock);
        Queries = new GuestServicesQueries(Policy, Store, Clock);
    }

    public GsDatabase Db { get; }

    public FakePolicy Policy { get; }

    public TestClock Clock { get; }

    public SqlBillingStore BillingStore { get; }

    public BillingService Billing { get; }

    public SqlGuestServicesStore Store { get; }

    public GuestServicesCommands Commands { get; }

    public GuestServicesQueries Queries { get; }

    public int PropertyId { get; private set; }

    public Dictionary<string, int> Rooms { get; } = [];

    /// <summary>Prototype booking id → stay id.</summary>
    public Dictionary<string, long> Stays { get; } = [];

    public Dictionary<string, long> Folios { get; } = [];

    public static async Task<GsWorld> CreateAsync(CancellationToken ct = default)
    {
        var db = await GsDatabase.CreateAsync(ct);
        try
        {
            var w = new GsWorld(db);
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
        await Db.ExecAsync($"INSERT inv.room_type(property_id, code, max_occupancy) VALUES ({PropertyId}, 'Classic', 2);", ct);
        foreach (var no in new[] { "101", "102", "104", "201", "202", "206", "301" })
            Rooms[no] = Convert.ToInt32(await Db.ScalarAsync($"INSERT inv.room(property_id, room_no, room_type_id) OUTPUT INSERTED.room_id SELECT {PropertyId}, '{no}', room_type_id FROM inv.room_type WHERE property_id = {PropertyId};", ct), CultureInfo.InvariantCulture);
        await StayAsync("HCC-1041", "101", "Ananya Rao", "InHouse", "2026-10-23", "2026-10-25", ct);
        await StayAsync("HCC-1042", "102", "Vikram Shah", "CheckedOut", "2026-10-21", "2026-10-23", ct);
        await StayAsync("HCC-1043", "104", "Meera Nair", "InHouse", "2026-10-23", "2026-10-25", ct);
        await StayAsync("HCC-1044", "201", "Arjun Kapoor", "InHouse", "2026-10-23", "2026-10-25", ct);
        await StayAsync("HCC-1045", "202", "Nisha Desai", "InHouse", "2026-10-23", "2026-10-26", ct);
        await Db.ExecAsync($"INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status) VALUES ({PropertyId}, 'HCC-1049', N'Rohan Joshi', '2026-10-25', '2026-10-26', 'Confirmed');", ct);
    }

    private async Task StayAsync(string publicNo, string room, string guest, string status, string arrival, string departure, CancellationToken ct)
    {
        var res = Convert.ToInt64(await Db.ScalarAsync($"""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status) OUTPUT INSERTED.reservation_id
            VALUES ({PropertyId}, '{publicNo}', N'{guest}', '{arrival}', '{departure}', '{(status == "InHouse" ? "InHouse" : "Departed")}');
            """, ct), CultureInfo.InvariantCulture);
        var guestId = Convert.ToInt64(await Db.ScalarAsync($"""
            INSERT gst.guest(property_id, full_name, phone, nationality, verified, origin, created_utc)
            OUTPUT INSERTED.guest_id VALUES ({PropertyId}, N'{guest}', '+91 98200 0{++_public}', N'Indian', 1, 'manual', '2026-10-20T09:00:00');
            """, ct), CultureInfo.InvariantCulture);
        var folio = Convert.ToInt64(await Db.ScalarAsync($"INSERT bill.folio(reservation_id) OUTPUT INSERTED.folio_id VALUES ({res});", ct), CultureInfo.InvariantCulture);
        var checkedOut = status == "CheckedOut" ? $"'{departure}', '{departure}T09:00:00', {Who.Reception}" : "NULL, NULL, NULL";
        var stay = Convert.ToInt64(await Db.ScalarAsync($"""
            INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by, checked_out_date, checked_out_utc, checked_out_by)
            OUTPUT INSERTED.stay_id VALUES ({PropertyId}, {res}, {guestId}, {folio}, '{status}', '{arrival}', '{departure}', '{arrival}', '{arrival}T09:00:00', {Who.Reception}, {checkedOut});
            """, ct), CultureInfo.InvariantCulture);
        await Db.ExecAsync($"INSERT gst.stay_segment(stay_id, seq, room_id, from_date, to_date) VALUES ({stay}, 1, {Rooms[room]}, '{arrival}', {(status == "CheckedOut" ? $"'{departure}'" : "NULL")});", ct);
        if (status == "InHouse")
            await Db.ExecAsync($"""
                INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES
                ({folio}, 'room', '2026-10-23', N'Room night', 200000, 1000, 20000), ({folio}, 'room', '2026-10-24', N'Room night', 200000, 1000, 20000);
                """, ct);
        Stays[publicNo] = stay;
        Folios[publicNo] = folio;
    }

    public CommandContext Ctx(int principal, string key, string op = "Test") => new(PropertyId, principal, op, key);

    /// <summary>Net charges on a folio (sum of lines, tax excluded) in paise.</summary>
    public Task<long> FolioNetAsync(string booking, CancellationToken ct = default) => Db.LongAsync($"SELECT COALESCE(SUM(amount_paise), 0) FROM bill.charge WHERE folio_id = {Folios[booking]};", ct);

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
