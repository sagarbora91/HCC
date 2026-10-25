using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Payments;
using Hcc.Application.Pos;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Pos;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Billing;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Pos;
using Microsoft.Data.SqlClient;

namespace Hcc.Pos.Tests.Harness;

/// <summary>Demo principals (fictional) mapped to the RBAC role templates.</summary>
public static class Who
{
    public const int Owner = 1;
    public const int Manager = 2;
    public const int Reception = 3;
    public const int Cashier = 4;
    public const int Housekeeper = 6;
}

public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

public static class Assertions
{
    public static async Task<HccException> Fails(HccErrorCode code, Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<HccException>(act);
        Assert.True(ex.Code == code, $"Expected {code} but got {ex.Code}: {ex.Message}");
        return ex;
    }
}

/// <summary>
/// One disposable database (<c>HccTest_&lt;UTC&gt;_&lt;hex&gt;</c>) with every migration, a fictional property, the POS service wired to the real
/// billing store, folio port and shared-drawer port (CashierService), and a restaurant + room-service outlet with the prototype's menu.
/// </summary>
public sealed partial class PosDb : IAsyncLifetime
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    public static readonly DateOnly Day = new(2026, 10, 24);

    private static int _publicNo;

    public string Server { get; } = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";

    public string Name { get; } = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];

    public SqlOptions Options => new() { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(Server, Name), BackupDirectory = "NUL" };

    public SqlAccessPolicy Policy { get; private set; } = null!;

    public ManualClock Clock { get; } = new(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero));

    public int PropertyId { get; private set; }

    public int Restaurant { get; private set; }

    public int RoomService { get; private set; }

    public PosService Pos { get; private set; } = null!;

    public PosQueries Queries { get; private set; } = null!;

    public BillingQueries FolioQueries { get; private set; } = null!;

    public CashierService Cashier { get; private set; } = null!;

    public Dictionary<string, int> Menu { get; } = [];

    public PosCaller As(int principal) => new(PropertyId, principal);

    public static string Key() => Guid.NewGuid().ToString("N");

    /// <summary>The prototype fixture menu (POS_FIXTURE_MENU): code, name, category, rupees, veg, outlets (R = restaurant, S = room service), available.</summary>
    private static readonly (string Code, string Name, string Cat, long Rupees, bool Veg, string Outlets, bool Avail)[] FixtureMenu =
    [
        ("TEA", "Masala chai", "Beverages", 60, true, "RS", true), ("COFFEE", "Filter coffee", "Beverages", 80, true, "RS", true),
        ("LIME", "Fresh lime soda", "Beverages", 120, true, "RS", true), ("LASSI", "Sweet lassi", "Beverages", 140, true, "RS", true),
        ("POHA", "Kanda poha", "Breakfast", 150, true, "RS", true), ("IDLI", "Idli sambar (3)", "Breakfast", 160, true, "RS", true),
        ("DOSA", "Masala dosa", "Breakfast", 220, true, "RS", true), ("PARATHA", "Aloo paratha with curd", "Breakfast", 240, true, "RS", true),
        ("KEBAB", "Chicken seekh kebab", "Starters", 420, false, "R", true), ("DAL", "Dal tadka", "Mains", 280, true, "RS", true),
        ("PANEER", "Paneer butter masala", "Mains", 380, true, "RS", true), ("THALI", "Maharashtrian veg thali", "Mains", 420, true, "R", true),
        ("BIRYANI", "Veg dum biryani", "Mains", 340, true, "RS", false), ("ROTI", "Tandoori roti", "Breads & rice", 40, true, "RS", true),
        ("NAAN", "Butter naan", "Breads & rice", 70, true, "RS", true),
    ];

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

        var platform = new SqlPlatform(Options);
        await platform.Migrations.MigrateAsync();
        PropertyId = Convert.ToInt32(await ScalarAsync("""
            INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '2026-10-24');
            """), CultureInfo.InvariantCulture);
        await ScalarAsync("INSERT inv.room_type(property_id, code, max_occupancy) VALUES (@p, 'Classic', 2);", ("@p", SqlDbType.Int, PropertyId));
        var connections = platform.Connections;
        Policy = new SqlAccessPolicy(connections, Clock, [new SqlShiftScopeResolver(connections)]);
        // Real P1 roles; principal ids line up with Who (fresh database: identity starts at 1; id 5 is an unused filler).
        foreach (var (login, role, expected) in new[] { ("owner", "OWN", Who.Owner), ("manager", "MGR", Who.Manager), ("reception", "REC", Who.Reception), ("cashier", "CSH", Who.Cashier), ("supervisor", "SUP", 5), ("housekeeper", "HKP", Who.Housekeeper) })
        {
            var uid = Convert.ToInt32(await ScalarAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES (@p, '{login}', N'{login}', '2026-01-01');", ("@p", SqlDbType.Int, PropertyId)), CultureInfo.InvariantCulture);
            if (uid != expected) throw new InvalidOperationException($"Seeded {login} got user id {uid}, expected {expected}.");
            await ScalarAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({uid}, '{role}', '2026-01-01', '2026-01-01');");
        }
        var billingStore = new SqlBillingStore(connections);
        // The real DEC-069 GST master (seed: restaurant 5 % without ITC, not specified premises = the prototype golden figures) and the P6 guard.
        var gstStore = new Hcc.Infrastructure.SqlServer.Tax.SqlGstMasterStore(connections);
        var tax = new GstMasterPosTaxRates(new Hcc.Application.Tax.GstRateResolver(gstStore));
        Cashier = new CashierService(platform.Commands, billingStore, Policy, Clock);
        FolioQueries = new BillingQueries(billingStore, Policy, Clock);
        var posStore = new SqlPosStore(connections);
        Pos = new PosService(platform.Commands, posStore, billingStore, Policy, tax, Cashier, new BillingPosFolioPort(billingStore, gstStore), Clock,
            new Hcc.Infrastructure.SqlServer.Closing.SqlBusinessDayGuard(), new Hcc.Application.Configuration.SettingsReader(new Hcc.Application.Configuration.SettingsRegistry([Hcc.Application.Pos.PosSettings.Provider]),
                new Hcc.Infrastructure.SqlServer.Configuration.SqlConfigurationStore(connections), new Hcc.Infrastructure.SqlServer.Configuration.SqlConfigurationStore(connections)));
        Queries = new PosQueries(posStore, Policy, tax);

        Restaurant = (await Pos.CreateOutletAsync(As(Who.Owner), Key(), new CreateOutletRequest("REST", "Restaurant", "table", Enumerable.Range(1, 40).Select(n => "T" + n.ToString(CultureInfo.InvariantCulture)).ToList()))).Value.OutletId;
        RoomService = (await Pos.CreateOutletAsync(As(Who.Owner), Key(), new CreateOutletRequest("RS", "Room service", "room", null))).Value.OutletId;
        foreach (var m in FixtureMenu)
        {
            var outlets = new List<int>();
            if (m.Outlets.Contains('R', StringComparison.Ordinal)) outlets.Add(Restaurant);
            if (m.Outlets.Contains('S', StringComparison.Ordinal)) outlets.Add(RoomService);
            var id = (await Pos.SaveMenuItemAsync(As(Who.Owner), Key(), new SaveMenuItemRequest(null, m.Code, m.Name, m.Cat, m.Rupees * 100, m.Veg, outlets, null))).Value.ItemId;
            if (!m.Avail) await Pos.SetMenuItemAvailabilityAsync(As(Who.Manager), Key(), new SetMenuItemAvailabilityRequest(id, false));
            Menu[m.Code] = id;
        }
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

    public async Task<int> CountAsync(string sql, params (string Name, SqlDbType Type, object Value)[] ps)
        => Convert.ToInt32(await ScalarAsync(sql, ps), CultureInfo.InvariantCulture);

    /// <summary>Records a dated Master-configuration change (what the Owner's SetSetting stores) for the Owner user.</summary>
    public Task SetSettingAsync(string key, string valueJson, string effectiveFrom)
        => ScalarAsync($"DECLARE @u INT = (SELECT TOP 1 user_id FROM plat.app_user WHERE property_id = @p); IF @u IS NULL BEGIN INSERT plat.app_user(property_id, login_name, display_name, created_utc) VALUES (@p, 'cfgowner', N'Owner', '2026-01-01'); SET @u = SCOPE_IDENTITY(); END; IF NOT EXISTS (SELECT 1 FROM cfg.setting WHERE property_id = @p AND setting_key = @k) INSERT cfg.setting(property_id, setting_key, created_utc, changed_utc) VALUES (@p, @k, '2026-01-01', '2026-01-01'); INSERT cfg.setting_change(property_id, setting_key, effective_from, value_json, before_json, before_text, after_text, source, changed_by, changed_utc) VALUES (@p, @k, @e, @v, 'null', 'before', 'after', 'change', @u, '2026-01-01');",
            ("@p", SqlDbType.Int, PropertyId), ("@k", SqlDbType.VarChar, key), ("@e", SqlDbType.Date, DateOnly.Parse(effectiveFrom, CultureInfo.InvariantCulture)), ("@v", SqlDbType.NVarChar, valueJson));

    public Task SetBusinessDateAsync(DateOnly date)
        => ScalarAsync("UPDATE inv.property SET business_date = @d WHERE property_id = @p;", ("@d", SqlDbType.Date, date), ("@p", SqlDbType.Int, PropertyId));

    /// <summary>A fictional in-house guest in <paramref name="roomNo"/> (reservation InHouse, folio, stay, open room segment); P3 does this at check-in.</summary>
    public async Task<(long StayId, long FolioId, long ReservationId)> NewInHouseAsync(string roomNo, bool inHouse = true, string guest = "Fictional guest")
    {
        var no = "T" + Interlocked.Increment(ref _publicNo).ToString("00000", CultureInfo.InvariantCulture);
        var roomId = await ScalarAsync("""
            DECLARE @r INT = (SELECT room_id FROM inv.room WHERE property_id = @p AND room_no = @n);
            IF @r IS NULL
            BEGIN
              INSERT inv.room(property_id, room_no, room_type_id) VALUES (@p, @n, (SELECT TOP 1 room_type_id FROM inv.room_type WHERE property_id = @p));
              SET @r = SCOPE_IDENTITY();
            END
            SELECT @r;
            """, ("@p", SqlDbType.Int, PropertyId), ("@n", SqlDbType.VarChar, roomNo));
        var rid = await LongAsync("""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status)
            OUTPUT INSERTED.reservation_id VALUES (@p, @no, @g, @a, @d, @s);
            """, ("@p", SqlDbType.Int, PropertyId), ("@no", SqlDbType.VarChar, no), ("@g", SqlDbType.NVarChar, guest), ("@a", SqlDbType.Date, Day),
            ("@d", SqlDbType.Date, Day.AddDays(2)), ("@s", SqlDbType.VarChar, inHouse ? "InHouse" : "Departed"));
        var fid = await LongAsync("INSERT bill.folio(reservation_id) OUTPUT INSERTED.folio_id VALUES (@r);", ("@r", SqlDbType.BigInt, rid));
        var gid = await LongAsync("""
            INSERT gst.guest(property_id, full_name, phone, origin, created_utc) OUTPUT INSERTED.guest_id VALUES (@p, @g, '9000000000', 'manual', SYSUTCDATETIME());
            """, ("@p", SqlDbType.Int, PropertyId), ("@g", SqlDbType.NVarChar, guest));
        var sid = await LongAsync("""
            INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by, checked_out_date, checked_out_utc, checked_out_by)
            OUTPUT INSERTED.stay_id VALUES (@p, @r, @g, @f, @st, @a, @d, @a, SYSUTCDATETIME(), 3,
              CASE WHEN @st = 'CheckedOut' THEN @a END, CASE WHEN @st = 'CheckedOut' THEN SYSUTCDATETIME() END, CASE WHEN @st = 'CheckedOut' THEN 3 END);
            """, ("@p", SqlDbType.Int, PropertyId), ("@r", SqlDbType.BigInt, rid), ("@g", SqlDbType.BigInt, gid), ("@f", SqlDbType.BigInt, fid),
            ("@st", SqlDbType.VarChar, inHouse ? "InHouse" : "CheckedOut"), ("@a", SqlDbType.Date, Day), ("@d", SqlDbType.Date, Day.AddDays(2)));
        await ScalarAsync("INSERT gst.stay_segment(stay_id, seq, room_id, from_date, to_date) VALUES (@s, 1, @r, @a, CASE WHEN @open = 1 THEN NULL ELSE @a END);",
            ("@s", SqlDbType.BigInt, sid), ("@r", SqlDbType.Int, Convert.ToInt32(roomId, CultureInfo.InvariantCulture)), ("@a", SqlDbType.Date, Day), ("@open", SqlDbType.Int, inHouse ? 1 : 0));
        return (sid, fid, rid);
    }

    /// <summary>A till with an open shift (first-shift float given) on the shared drawer.</summary>
    public async Task<(int TillId, ShiftResult Shift)> OpenTillAsync(long floatPaise, string? code = null)
    {
        var till = (await Cashier.CreateTillAsync(new Caller(PropertyId, Who.Owner), Key(), new CreateTillRequest(code ?? ("T" + Guid.NewGuid().ToString("N")[..6]), "Front desk"))).Value;
        var shift = (await Cashier.OpenShiftAsync(new Caller(PropertyId, Who.Cashier), Key(), new OpenShiftRequest(till.TillId, floatPaise))).Value;
        return (till.TillId, shift);
    }

    public async Task<long> DrawerSumAsync(long shiftId, string? kind = null)
        => Convert.ToInt64(await ScalarAsync("SELECT ISNULL(SUM(amount_paise), 0) FROM pay.cash_movement WHERE shift_id = @s AND (@k IS NULL OR kind = @k);",
            ("@s", SqlDbType.BigInt, shiftId), ("@k", SqlDbType.VarChar, (object?)kind ?? DBNull.Value)), CultureInfo.InvariantCulture);

    // ── scenario helpers ──

    public async Task<CheckResult> OpenTableAsync(string table, int who = Who.Cashier, int covers = 2)
        => (await Pos.OpenCheckAsync(As(who), Key(), new OpenCheckRequest(Restaurant, table, null, covers, null))).Value;

    public async Task<CheckResult> AddAsync(long orderId, string item, int qty = 1, int who = Who.Cashier)
        => (await Pos.AddItemAsync(As(who), Key(), new AddCheckItemRequest(orderId, Menu[item], qty, null))).Value;

    public async Task<KotResult> SendAsync(long orderId, int who = Who.Cashier)
        => (await Pos.SendKotAsync(As(who), Key(), new SendKotRequest(orderId, null))).Value;

    /// <summary>A restaurant check with items sent to the kitchen (default: dal x1, roti x2 = Rs 360 net).</summary>
    public async Task<long> SentTableAsync(string table = "T3", params (string Item, int Qty)[] items)
    {
        var o = await OpenTableAsync(table);
        foreach (var (item, qty) in items.Length == 0 ? [("DAL", 1), ("ROTI", 2)] : items) await AddAsync(o.OrderId, item, qty);
        await SendAsync(o.OrderId);
        return o.OrderId;
    }

    public async Task<string> VersionAsync(long orderId) => (await Queries.GetCheckAsync(As(Who.Owner), orderId)).Version;

    public async Task<CommandResult<SettleResult>> SettleOutletAsync(long orderId, string tender, int? till = null, string? reference = null, int who = Who.Cashier, string? key = null)
        => await Pos.SettleCheckAsync(As(who), key ?? Key(), new SettleCheckRequest(orderId, "outlet", tender, reference ?? (tender == "Cash" ? null : "DEMO-REF"), till, null, await VersionAsync(orderId)));

    public async Task<CommandResult<SettleResult>> SettleFolioAsync(long orderId, long? stayId = null, int who = Who.Cashier, string? key = null)
        => await Pos.SettleCheckAsync(As(who), key ?? Key(), new SettleCheckRequest(orderId, "folio", null, null, null, stayId, await VersionAsync(orderId)));
}
