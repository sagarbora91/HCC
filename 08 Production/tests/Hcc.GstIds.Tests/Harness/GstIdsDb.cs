using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.GuestIds;
using Hcc.Application.Payments;
using Hcc.Application.Stays;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Billing;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.GuestIds;
using Hcc.Infrastructure.SqlServer.Stays;
using Hcc.Infrastructure.SqlServer.Tax;
using Microsoft.Data.SqlClient;

namespace Hcc.GstIds.Tests.Harness;

/// <summary>Controllable clock (event times come from the injected TimeProvider).</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Stay id = reservation id; the agreed nightly price per stay is set by the test (GST is resolved by billing).</summary>
public sealed class TestStayNights : IStayNightSource
{
    public ConcurrentDictionary<long, (long FolioId, long RatePaise)> Stays { get; } = new();

    public Task<StayNight> ResolveAsync(ICommandTransaction tx, int propertyId, long stayId, DateOnly serviceDate, CancellationToken ct)
        => Stays.TryGetValue(stayId, out var s)
            ? Task.FromResult(new StayNight(s.FolioId, new Paise(s.RatePaise)))
            : throw new HccException(HccErrorCode.NOT_FOUND, "Stay not found.");
}

/// <summary>Stays folio port stand-in: registration only (no check-in in these tests).</summary>
public sealed class NoFolios : IStayFolioPort
{
    public Task<long> EnsureFolioAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task AssertCheckoutSettledAsync(ICommandTransaction tx, CheckoutFolioCheck check, CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>
/// One disposable <c>HccTest_&lt;UTC&gt;_&lt;hex&gt;</c> database on <c>.\SQLEXPRESS</c> (or <c>HCC_TEST_SQL_SERVER</c>), all migrations,
/// a fictional property, one fictional user per role template (the REAL P1 policy over the 0101 seed), and the real services.
/// Only names matching the pattern are ever dropped; EtpReporting / EtpScratch_* are never touched.
/// </summary>
public sealed partial class GstIdsDb : IAsyncLifetime
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    public static readonly DateOnly Day = new(2026, 10, 24);

    public static readonly string[] Roles = ["OWN", "MGR", "REC", "CSH", "ACC", "SUP", "HKP", "MNT", "TAD"];

    public static readonly byte[] Key1 = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    public static readonly byte[] Key2 = Enumerable.Range(101, 32).Select(i => (byte)i).ToArray();

    private static int _publicNo;

    public string Server { get; } = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";

    public string Name { get; } = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];

    public SqlOptions Options => new() { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(Server, Name), BackupDirectory = "NUL" };

    public ManualClock Clock { get; } = new(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero));

    public Dictionary<string, int> User { get; } = new(StringComparer.Ordinal);

    public int PropertyId { get; private set; }

    public int DeluxeTypeId { get; private set; }

    public int SuiteTypeId { get; private set; }

    public SqlConnectionFactory Connections { get; private set; } = null!;

    public ICommandRunner Runner { get; private set; } = null!;

    public IAccessPolicy Policy { get; private set; } = null!;

    public SqlGstMasterStore GstStore { get; private set; } = null!;

    public GstRateResolver Gst { get; private set; } = null!;

    public GstMasterService GstMaster { get; private set; } = null!;

    public TestStayNights Stays { get; } = new();

    public BillingService Billing { get; private set; } = null!;

    public CashierService Cashier { get; private set; } = null!;

    public BillingQueries Queries { get; private set; } = null!;

    public GuestIdProtector Protector { get; private set; } = null!;

    public GuestIdService GuestIds { get; private set; } = null!;

    public StaysCommands StaysCmd { get; private set; } = null!;

    public StaysQueries StayQueries { get; private set; } = null!;

    public SqlGuestIdStore GuestIdStore { get; private set; } = null!;

    public Caller As(string role) => new(PropertyId, User[role]);

    public CommandContext Ctx(string role, string? key = null) => new(PropertyId, User[role], "Test", key ?? Key());

    public static string Key() => Guid.NewGuid().ToString("N");

    public async ValueTask InitializeAsync()
    {
        if (!NamePattern().IsMatch(Name)) throw new InvalidOperationException("Unexpected test database name.");
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(Server, "master")))
        {
            await conn.OpenAsync();
            await using var cmd = new SqlCommand($"CREATE DATABASE [{Name}];", conn) { CommandTimeout = 120 };
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (ex.Number == 262)
            {
                throw new InvalidOperationException($"The test login cannot create databases on {Server}; grant dbcreator (see 08 Production/README.md).", ex);
            }
        }

        var options = Options;
        var platform = new SqlPlatform(options);
        await platform.Migrations.MigrateAsync();
        Connections = platform.Connections;
        Runner = platform.Commands;
        PropertyId = Convert.ToInt32(await ScalarAsync("""
            INSERT inv.property(code, name, time_zone, business_date) VALUES ('DEMO', N'HCC Demo Residency (fictional)', 'Asia/Kolkata', '2026-10-24');
            DECLARE @p INT = SCOPE_IDENTITY();
            INSERT inv.room_type(property_id, code, max_occupancy, name, rack_rate_paise, sort_order) VALUES (@p, 'Deluxe', 3, N'Deluxe', 200000, 1);
            DECLARE @d INT = SCOPE_IDENTITY();
            INSERT inv.room_type(property_id, code, max_occupancy, name, rack_rate_paise, sort_order) VALUES (@p, 'Suite', 3, N'Suite', 450000, 2);
            DECLARE @s INT = SCOPE_IDENTITY();
            INSERT inv.room(property_id, room_no, room_type_id) VALUES (@p, '105', @d), (@p, '106', @s);
            SELECT @p;
            """), CultureInfo.InvariantCulture);
        DeluxeTypeId = Convert.ToInt32(await ScalarAsync("SELECT room_type_id FROM inv.room_type WHERE code = 'Deluxe';"), CultureInfo.InvariantCulture);
        SuiteTypeId = Convert.ToInt32(await ScalarAsync("SELECT room_type_id FROM inv.room_type WHERE code = 'Suite';"), CultureInfo.InvariantCulture);
        foreach (var role in Roles)
        {
            var id = Convert.ToInt32(await ScalarAsync("""
                INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES (@p, @l, @n, '2026-10-01');
                """, ("@p", SqlDbType.Int, PropertyId), ("@l", SqlDbType.VarChar, "test." + role.ToLowerInvariant()), ("@n", SqlDbType.NVarChar, "Test " + role + " (fictional)")),
                CultureInfo.InvariantCulture);
            await ScalarAsync("INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES (@u, @r, '2026-10-01', '2026-10-01');",
                ("@u", SqlDbType.Int, id), ("@r", SqlDbType.VarChar, role));
            User[role] = id;
        }

        Policy = new SqlAccessPolicy(Connections, Clock, [new SqlShiftScopeResolver(Connections)]);
        GstStore = new SqlGstMasterStore(Connections);
        Gst = new GstRateResolver(GstStore);
        GstMaster = new GstMasterService(Runner, Policy, GstStore, Gst, Clock);
        var billingStore = new SqlBillingStore(Connections);
        Billing = new BillingService(Runner, billingStore, Policy, Stays, Gst, Clock, GstStore);
        Cashier = new CashierService(Runner, billingStore, Policy, Clock);
        Queries = new BillingQueries(billingStore, Policy, Clock);

        Protector = new GuestIdProtector(new StaticGuestIdKeyRing("k1", new Dictionary<string, byte[]> { ["k1"] = Key1 }));
        GuestIdStore = new SqlGuestIdStore(Connections);
        GuestIds = new GuestIdService(Runner, Policy, GuestIdStore, Protector, Clock);
        StaysCmd = new StaysCommands(Runner, Policy, new SqlStaysStore(), new NoFolios(), Clock, GuestIds);
        StayQueries = new StaysQueries(Policy, new SqlStaysReadStore(Connections));
    }

    /// <summary>Services over the same database with a different key ring (rotation / wrong-key tests).</summary>
    public (GuestIdProtector Protector, GuestIdService Service, StaysCommands Stays) WithRing(IGuestIdKeyRing ring)
    {
        var p = new GuestIdProtector(ring);
        var s = new GuestIdService(Runner, Policy, GuestIdStore, p, Clock);
        return (p, s, new StaysCommands(Runner, Policy, new SqlStaysStore(), new NoFolios(), Clock, s));
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

    public async Task<object?> ScalarAsync(string sql, params (string Name, SqlDbType Type, object Value)[] ps)
    {
        await using var conn = await new SqlConnectionFactory(Options).OpenAsync();
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        foreach (var (n, t, v) in ps) cmd.Parameters.Add(n, t).Value = v;
        return await cmd.ExecuteScalarAsync();
    }

    public async Task<long> LongAsync(string sql, params (string Name, SqlDbType Type, object Value)[] ps)
        => Convert.ToInt64(await ScalarAsync(sql, ps), CultureInfo.InvariantCulture);

    /// <summary>Every row of a query as one text blob (for no-leak scans).</summary>
    public async Task<string> DumpAsync(string sql)
    {
        await using var conn = await new SqlConnectionFactory(Options).OpenAsync();
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        await using var rd = await cmd.ExecuteReaderAsync();
        var sb = new System.Text.StringBuilder();
        do
        {
            while (await rd.ReadAsync())
                for (var i = 0; i < rd.FieldCount; i++)
                    sb.Append(rd.IsDBNull(i) ? "" : rd.GetValue(i) is byte[] b ? Convert.ToHexString(b) : Convert.ToString(rd.GetValue(i), CultureInfo.InvariantCulture)).Append('|');
        }
        while (await rd.NextResultAsync());
        return sb.ToString();
    }

    public Task SetBusinessDateAsync(DateOnly date)
        => ScalarAsync("UPDATE inv.property SET business_date = @d WHERE property_id = @p;", ("@d", SqlDbType.Date, date), ("@p", SqlDbType.Int, PropertyId));

    /// <summary>A fictional in-house reservation with its folio; registers the stay's agreed nightly price for PostRoomNight.</summary>
    public async Task<(long StayId, long FolioId)> InHouseAsync(long nightlyPaise, DateOnly arrival, DateOnly departure, string status = "InHouse")
    {
        var no = "G" + Interlocked.Increment(ref _publicNo).ToString("00000", CultureInfo.InvariantCulture);
        var rid = await LongAsync("""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, nightly_rate_paise)
            OUTPUT INSERTED.reservation_id VALUES (@p, @no, N'Fictional guest', @a, @d, @s, @r);
            """, ("@p", SqlDbType.Int, PropertyId), ("@no", SqlDbType.VarChar, no), ("@a", SqlDbType.Date, arrival), ("@d", SqlDbType.Date, departure),
            ("@s", SqlDbType.VarChar, status), ("@r", SqlDbType.BigInt, nightlyPaise));
        var fid = await LongAsync("INSERT bill.folio(reservation_id) OUTPUT INSERTED.folio_id VALUES (@r);", ("@r", SqlDbType.BigInt, rid));
        Stays.Stays[rid] = (fid, nightlyPaise);
        return (rid, fid);
    }

    /// <summary>Posts one room night as the night audit would: with the business date at that night, then back to the previous date.</summary>
    public async Task<ChargeResult> NightAsync(long stayId, DateOnly date)
    {
        var previous = DateOnly.FromDateTime((DateTime)(await ScalarAsync("SELECT business_date FROM inv.property WHERE property_id = @p;", ("@p", SqlDbType.Int, PropertyId)))!);
        await SetBusinessDateAsync(date);
        try
        {
            return (await Billing.PostRoomNightAsync(As("OWN"), Key(), new PostRoomNightRequest(stayId, date))).Value;
        }
        finally
        {
            await SetBusinessDateAsync(previous);
        }
    }

    public async Task<ChargeResult> ChargeAsync(long folioId, string label, long amount, string? category = null)
        => (await Billing.PostChargeAsync(As("OWN"), Key(), new PostChargeRequest(folioId, label, amount, category))).Value;

    public async Task<string> FolioVersionAsync(long folioId) => (await Queries.GetFolioAsync(As("OWN"), folioId)).Version;

    public async Task<Hcc.Contracts.Tax.GstSettingChangeResult> SetGstAsync(string key, long value, string? from = null, string role = "OWN")
        => (await GstMaster.SetAsync(Ctx(role), new Hcc.Contracts.Tax.SetGstSettingRequest(key, value, from, "Test change (fictional)"))).Value;
}

public static class Expect
{
    public static async Task<HccException> Fails(HccErrorCode code, Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<HccException>(act);
        Assert.True(ex.Code == code, $"Expected {code} but got {ex.Code}: {ex.Message}");
        return ex;
    }
}
