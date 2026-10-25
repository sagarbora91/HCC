using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Api;
using Hcc.Application.Billing;
using Hcc.Application.Closing;
using Hcc.Application.Commands;
using Hcc.Application.Housekeeping;
using Hcc.Application.Inventory;
using Hcc.Application.Payments;
using Hcc.Application.Reporting;
using Hcc.Application.Reservations;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Closing;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Contracts.Stays;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Closing.Tests.Harness;

/// <summary>Simulated process crash at a close step (thrown by <see cref="TestObserver"/>).</summary>
public sealed class SimulatedCrashException : Exception
{
    public SimulatedCrashException() : base("Simulated crash") { }

    public SimulatedCrashException(string message) : base(message) { }

    public SimulatedCrashException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Close-step observer the tests program (crash, pause) per point: preflight, night, cash, barrier, next-day.</summary>
public sealed class TestObserver : ICloseStepObserver
{
    public Func<string, long?, CancellationToken, Task>? OnStep { get; set; }

    public Task OnStepAsync(int propertyId, DateOnly businessDate, string point, long? stayId, CancellationToken ct) => OnStep?.Invoke(point, stayId, ct) ?? Task.CompletedTask;
}

/// <summary>
/// A fictional hotel on a disposable <c>HccTest_*</c> database with the REAL server composition (<see cref="HccComposition.AddHcc"/>: P1 policy
/// with the DEC-063 seed, every module, the business-day guarded runner) resolved from DI — no fakes. Staff are seeded as P1 users with role
/// templates. Ten Deluxe rooms 101–110 at ₹2,000. Business date 2026-10-24.
/// </summary>
public sealed partial class Hotel : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    public static readonly DateOnly Day1 = new(2026, 10, 24);

    private static int _seq;

    private ServiceProvider _sp = null!;

    private Hotel(string server, string name)
    {
        Server = server;
        Name = name;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
    }

    public string Server { get; }

    public string Name { get; }

    public SqlPlatform Platform { get; }

    public TestObserver Observer { get; } = new();

    public int PropertyId { get; private set; }

    public int Owner { get; private set; }

    public int Manager { get; private set; }

    public int Manager2 { get; private set; }

    public int Reception { get; private set; }

    public int Cashier { get; private set; }

    public int Accountant { get; private set; }

    public Dictionary<string, int> Rooms { get; } = [];

    public T Get<T>() where T : notnull => _sp.GetRequiredService<T>();

    public BillingService Billing => Get<BillingService>();

    public CashierService Till => Get<CashierService>();

    public BillingQueries BillingQueries => Get<BillingQueries>();

    public ClosingService Closing => Get<ClosingService>();

    public ReportingQueries Reports => Get<ReportingQueries>();

    public StaysCommands Stays => Get<StaysCommands>();

    public ReservationService Reservations => Get<ReservationService>();

    public InventoryService Inventory => Get<InventoryService>();

    public HousekeepingCommands Housekeeping => Get<HousekeepingCommands>();

    public Caller As(int who) => new(PropertyId, who);

    public static string Key() => "t-" + Guid.NewGuid().ToString("N");

    public CommandContext Ctx(int who, string op, string? key = null) => new(PropertyId, who, op, key ?? Key());

    public static async Task<Hotel> CreateAsync(CancellationToken ct = default)
    {
        var server = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";
        var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        if (!NamePattern().IsMatch(name)) throw new InvalidOperationException("Unexpected test database name.");
        var h = new Hotel(server, name);
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(server, "master")))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"CREATE DATABASE [{name}];", conn) { CommandTimeout = 120 };
            try
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (SqlException ex) when (ex.Number == 262)
            {
                throw new InvalidOperationException($"The test login cannot create databases on {server}; grant dbcreator (see 08 Production/README.md).", ex);
            }
        }

        try
        {
            await h.Platform.Migrations.MigrateAsync(ct);
            await h.SeedAsync(ct);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hcc:Access:PasswordIterations"] = "1000",
                ["Hcc:Access:PinIterations"] = "1000",
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ICloseStepObserver>(h.Observer);
            services.AddHcc(config, h.Platform);
            h._sp = services.BuildServiceProvider();
            return h;
        }
        catch
        {
            await h.DisposeAsync();
            throw;
        }
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        PropertyId = Convert.ToInt32(await ScalarAsync("""
            INSERT inv.property(code, name, time_zone, business_date) VALUES ('DEMO', N'HCC Demo Residency (fictional)', 'Asia/Kolkata', '2026-10-24');
            DECLARE @p INT = SCOPE_IDENTITY();
            INSERT inv.room_type(property_id, code, max_occupancy, name, rack_rate_paise, sort_order) VALUES (@p, 'Deluxe', 3, N'Deluxe', 200000, 1);
            DECLARE @t INT = SCOPE_IDENTITY();
            INSERT inv.room(property_id, room_no, room_type_id) VALUES (@p,'101',@t),(@p,'102',@t),(@p,'103',@t),(@p,'104',@t),(@p,'105',@t),
              (@p,'106',@t),(@p,'107',@t),(@p,'108',@t),(@p,'109',@t),(@p,'110',@t);
            SELECT @p;
            """, ct), CultureInfo.InvariantCulture);
        foreach (var (no, id) in await RowsAsync<(string, int)>($"SELECT room_no, room_id FROM inv.room WHERE property_id = {PropertyId}", r => (r.GetString(0), r.GetInt32(1)), ct))
            Rooms[no] = id;
        async Task<int> UserAsync(string login, string name, string role) => Convert.ToInt32(await ScalarAsync($"""
            INSERT plat.app_user(property_id, login_name, display_name, created_utc) VALUES ({PropertyId}, '{login}', N'{name}', SYSUTCDATETIME());
            DECLARE @u INT = SCOPE_IDENTITY();
            INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES (@u, '{role}', DATEADD(day, -30, SYSUTCDATETIME()), SYSUTCDATETIME());
            SELECT @u;
            """, ct), CultureInfo.InvariantCulture);
        Owner = await UserAsync("own.sagar", "Sagar (Owner)", "OWN");
        Manager = await UserAsync("mgr.meera", "Meera (Manager)", "MGR");
        Manager2 = await UserAsync("mgr.ravi", "Ravi (Manager)", "MGR");
        Reception = await UserAsync("rec.asha", "Asha (Reception)", "REC");
        Cashier = await UserAsync("csh.kiran", "Kiran (Cashier)", "CSH");
        Accountant = await UserAsync("acc.neha", "Neha (Accountant)", "ACC");
    }

    // ── fixtures ──

    /// <summary>An in-house stay loaded directly (P2/P3 stand-in, like the P3 seed): reservation + room nights + guest + folio + stay.</summary>
    public async Task<(long ReservationId, long StayId, long FolioId)> SeedInHouseAsync(string roomNo, string arrival, string departure, long ratePaise = 200000, CancellationToken ct = default)
    {
        var n = Interlocked.Increment(ref _seq);
        var room = Rooms[roomNo];
        var a = DateOnly.Parse(arrival, CultureInfo.InvariantCulture);
        var d = DateOnly.Parse(departure, CultureInfo.InvariantCulture);
        var nights = string.Join("\n", Enumerable.Range(0, d.DayNumber - a.DayNumber).Select(i => $"INSERT inv.room_night(room_id, night, reservation_id) VALUES ({room}, '{a.AddDays(i):yyyy-MM-dd}', @r);"));
        var rows = await RowsAsync<(long, long, long)>($"""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, nightly_rate_paise)
            SELECT {PropertyId}, 'HCC-S{n:0000}', N'Seed guest {n} (fictional)', '{arrival}', '{departure}', 'InHouse', room_id, room_type_id, 2, {ratePaise} FROM inv.room WHERE room_id = {room};
            DECLARE @r BIGINT = SCOPE_IDENTITY();
            {nights}
            INSERT gst.guest(property_id, full_name, phone, nationality, verified, origin, created_utc) VALUES ({PropertyId}, N'Seed guest {n} (fictional)', '98000{n:00000}', N'Indian', 1, 'manual', SYSUTCDATETIME());
            DECLARE @g BIGINT = SCOPE_IDENTITY();
            INSERT gst.registration(reservation_id, guest_id, registered_utc, registered_by) VALUES (@r, @g, SYSUTCDATETIME(), {Reception});
            INSERT bill.folio(reservation_id) VALUES (@r);
            DECLARE @f BIGINT = SCOPE_IDENTITY();
            INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by)
            VALUES ({PropertyId}, @r, @g, @f, 'InHouse', '{arrival}', '{departure}', '{arrival}', SYSUTCDATETIME(), {Reception});
            DECLARE @s BIGINT = SCOPE_IDENTITY();
            INSERT gst.stay_segment(stay_id, seq, room_id, from_date) VALUES (@s, 1, {room}, '{arrival}');
            EXEC inv.usp_rebuild_type_night_count {PropertyId}, '2026-10-01', '2027-01-01';
            SELECT @r, @s, @f;
            """, r => (r.GetInt64(0), r.GetInt64(1), r.GetInt64(2)), ct);
        return rows[0];
    }

    /// <summary>Book → register → check in through the P2/P3 commands as Reception.</summary>
    public async Task<(long ReservationId, long StayId, long FolioId)> BookAndCheckInAsync(string roomNo, string arrival, string departure, string guest, CancellationToken ct = default)
    {
        var room = Rooms[roomNo];
        var quote = (await Inventory.QuoteStayAsync(Ctx(Reception, "QuoteStay"), new QuoteStayRequest(arrival, departure, 2, 0, room), ct)).Value;
        var res = (await Reservations.CreateAsync(Ctx(Reception, "CreateReservation"), new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput(guest, "9800000105"), room), ct)).Value;
        var det = await Reservations.GetAsync(PropertyId, Reception, res.ReservationId, ct);
        await Stays.RegisterGuestAsync(Ctx(Reception, "RegisterGuest"), new RegisterGuestRequest(res.ReservationId, det.Version,
            new GuestDraftDto(guest, "9800000105", Address: "12 MG Road, Latur (fictional)", Nationality: "Indian", IdType: "Aadhaar", IdLast4: "4321")), ct);
        det = await Reservations.GetAsync(PropertyId, Reception, res.ReservationId, ct);
        var stay = (await Stays.CheckInAsync(Ctx(Reception, "CheckIn"), new CheckInRequest(res.ReservationId, det.Version), ct)).Value;
        return (res.ReservationId, stay.StayId, stay.FolioId);
    }

    public async Task<(int TillId, ShiftResult Shift)> OpenTillAsync(long floatPaise, CancellationToken ct = default)
    {
        var till = (await Till.CreateTillAsync(As(Owner), Key(), new CreateTillRequest("FD-1", "Front desk"), ct)).Value;
        return (till.TillId, (await Till.OpenShiftAsync(As(Cashier), Key(), new OpenShiftRequest(till.TillId, floatPaise), ct)).Value);
    }

    public async Task<long> ExpectedAsync(long shiftId, CancellationToken ct = default) => (await BillingQueries.GetShiftAsync(As(Owner), shiftId, ct)).Expected.ExpectedPaise;

    /// <summary>The cashier counts the drawer exactly (closes the shift).</summary>
    public async Task<ShiftResult> CountExactAsync(long shiftId, CancellationToken ct = default)
    {
        var view = await BillingQueries.GetShiftAsync(As(Owner), shiftId, ct);
        return (await Till.SubmitShiftCountAsync(As(Cashier), Key(), new SubmitShiftCountRequest(shiftId, view.Expected.ExpectedPaise, null, null, null, view.Shift.Version), ct)).Value;
    }

    public Task<CommandResult<ReceiptResult>> PayAsync(long folioId, long amount, string tender, int? tillId = null, string kind = "payment", int? who = null, CancellationToken ct = default)
        => Billing.RecordReceiptAsync(As(who ?? Cashier), Key(), new RecordReceiptRequest(folioId, kind, tender, amount, tender == "Cash" ? null : "DEMO-" + tender + "-" + Interlocked.Increment(ref _seq), tillId), ct);

    public sealed record DrawerResult(long MovementId);

    /// <summary>A shared-till movement from another module (restaurant cash, drawer expense) through P4's port, in a guarded command.</summary>
    public async Task<long> DrawerAsync(int tillId, CashMovementKind kind, long amountPaise, string label, int? who = null, CancellationToken ct = default)
    {
        var source = Interlocked.Increment(ref _seq);
        var r = await Get<ICommandRunner>().RunAsync(Ctx(who ?? Cashier, "TestDrawerMovement"), new { tillId, kind = kind.ToString(), amountPaise, label, source }, async (tx, c) =>
        {
            var id = await Get<ICashDrawerPort>().RecordMovementAsync(tx, tillId, kind, "test", source, new Paise(amountPaise), label, "fixture", c);
            await tx.AuditAsync("TEST_DRAWER_MOVEMENT", "till:" + tillId, label, c);
            return new DrawerResult(id);
        }, ct);
        return r.Value.MovementId;
    }

    public Task<CommandResult<CloseRunResult>> CloseAsync(int? who = null, DateOnly? date = null, string? runId = null, CancellationToken ct = default)
        => Closing.RunBusinessDayCloseAsync(As(who ?? Manager), Key(), new RunBusinessDayCloseRequest(date ?? Day1, runId ?? "run-" + Guid.NewGuid().ToString("N")[..12]), ct);

    public async Task<DateOnly> BusinessDateAsync(CancellationToken ct = default)
        => DateOnly.FromDateTime((DateTime)(await ScalarAsync($"SELECT business_date FROM inv.property WHERE property_id = {PropertyId}", ct))!);

    public async Task<string> VersionAsync(string sql, CancellationToken ct = default) => Convert.ToBase64String((byte[])(await ScalarAsync(sql, ct))!);

    // ── SQL helpers ──

    public async Task<object?> ScalarAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = await Platform.Connections.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        return await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<long> LongAsync(string sql, CancellationToken ct = default) => Convert.ToInt64(await ScalarAsync(sql, ct), CultureInfo.InvariantCulture);

    public async Task<List<T>> RowsAsync<T>(string sql, Func<SqlDataReader, T> read, CancellationToken ct = default)
    {
        await using var conn = await Platform.Connections.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        var list = new List<T>();
        while (await rd.ReadAsync(ct)) list.Add(read(rd));
        return list;
    }

    public async ValueTask DisposeAsync()
    {
        if (_sp is not null) await _sp.DisposeAsync();
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

public static class Expect
{
    public static async Task<HccException> Fails(HccErrorCode code, Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<HccException>(act);
        Assert.True(ex.Code == code, $"Expected {code} but got {ex.Code}: {ex.Message}");
        return ex;
    }
}
