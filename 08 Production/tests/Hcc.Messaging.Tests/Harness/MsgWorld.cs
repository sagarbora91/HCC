using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Messaging;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Messaging;
using Microsoft.Data.SqlClient;

namespace Hcc.Messaging.Tests.Harness;

public static class Users
{
    public const int Owner = 1, Manager = 2, Reception = 3, Cashier = 4, Housekeeper = 6, Accountant = 8;
}

/// <summary>Controllable clock (event times come from the injected TimeProvider).</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(int minutes) => Now = Now.AddMinutes(minutes);
}

/// <summary>RBAC v1 grants for the PERM ids messaging uses (RES-001/002, STY-001, APR-001); Owner/Manager/Reception per the matrix.</summary>
public sealed class FakeAccessPolicy : IAccessPolicy
{
    private static readonly string[] Front = ["PERM-RES-001", "PERM-RES-002", "PERM-STY-001"];

    private readonly Dictionary<int, HashSet<string>> _grants = new()
    {
        [Users.Owner] = [.. Front, "PERM-APR-001"],
        [Users.Manager] = [.. Front, "PERM-APR-001"],
        [Users.Reception] = [.. Front],
        [Users.Cashier] = [],
        [Users.Housekeeper] = [],
        [Users.Accountant] = [],
    };

    public Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default) =>
        _grants.TryGetValue(principalId, out var g) && g.Contains(permissionId) ? Task.CompletedTask : throw new HccException(HccErrorCode.FORBIDDEN, "Access denied.");

    public Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default) =>
        Task.FromResult(_grants.TryGetValue(principalId, out var g) && g.Contains(permissionId));
}

/// <summary>A disposable <c>HccTest_*</c> database migrated to the latest version, with a small fictional hotel and the real messaging services.</summary>
public sealed partial class MsgWorld : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private readonly string _server;
    private int _res = 3000;

    private MsgWorld(string server, string name)
    {
        _server = server;
        Name = name;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
        Clock = new TestClock(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero));
        Policy = new FakeAccessPolicy();
        Gateway = new FakeMessageGateway();
        var data = new SqlMessagingDataFactory(Platform.Connections);
        Options = new MessagingOptions();
        Engine = new TriggerEngine(Gateway, Options);
        Commands = new MessagingCommands(Platform.Commands, Policy, data, Engine, Gateway, Options, Clock);
        Queries = new MessagingQueries(Policy, data, Clock);
        Projector = new MessagingTriggerProjector(Platform.Outbox, Platform.Connections, Engine, Clock);
    }

    public string Name { get; }

    public SqlPlatform Platform { get; }

    public TestClock Clock { get; }

    public FakeAccessPolicy Policy { get; }

    public IMessageGateway Gateway { get; }

    public MessagingOptions Options { get; }

    public TriggerEngine Engine { get; }

    public MessagingCommands Commands { get; }

    public MessagingQueries Queries { get; }

    public MessagingTriggerProjector Projector { get; }

    public int PropertyId { get; private set; }

    public static string TestServer => Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";

    public static async Task<MsgWorld> CreateAsync(CancellationToken ct = default)
    {
        var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        if (!NamePattern().IsMatch(name)) throw new InvalidOperationException("Unexpected test database name.");
        var w = new MsgWorld(TestServer, name);
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(w._server, "master")))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"CREATE DATABASE [{name}];", conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync(ct);
        }

        try
        {
            await w.Platform.Migrations.MigrateAsync(ct);
            w.PropertyId = Convert.ToInt32(await w.ScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '2026-10-24');", ct), CultureInfo.InvariantCulture);
            await w.ExecAsync($"INSERT inv.room_type(property_id, code, max_occupancy) VALUES ({w.PropertyId}, 'Classic', 2);" +
                $"INSERT inv.room(property_id, room_no, room_type_id, hk_status) SELECT {w.PropertyId}, r, room_type_id, 'Ready' FROM inv.room_type CROSS JOIN (VALUES ('101'), ('102'), ('104'), ('105')) v(r) WHERE property_id = {w.PropertyId};", ct);
            return w;
        }
        catch
        {
            await w.DisposeAsync();
            throw;
        }
    }

    public CommandContext Ctx(int principal, string key, string op = "T") => new(PropertyId, principal, op, key);

    public async Task<object?> ScalarAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = await Platform.Connections.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        return await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<int> CountAsync(string sql, CancellationToken ct = default) => Convert.ToInt32(await ScalarAsync(sql, ct), CultureInfo.InvariantCulture);

    public async Task<string?> StringAsync(string sql, CancellationToken ct = default) => await ScalarAsync(sql, ct) as string;

    public async Task ExecAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = await Platform.Connections.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string S(string? v) => v is null ? "NULL" : "N'" + v.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>A registered guest profile (with an ID type + last 4 that must never reach a message) and a reservation; optionally checked in.</summary>
    public async Task<(long ResId, long GuestId, string PublicNo)> BookingAsync(string room, string arrival, string departure, string guestName, string? phone, string? email,
        string status = "Confirmed", bool inHouse = false, CancellationToken ct = default)
    {
        var publicNo = "HCC-" + (++_res).ToString(CultureInfo.InvariantCulture);
        var resId = Convert.ToInt64(await ScalarAsync($"""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status) OUTPUT INSERTED.reservation_id
            VALUES ({PropertyId}, '{publicNo}', {S(guestName)}, '{arrival}', '{departure}', '{(inHouse ? "InHouse" : status)}');
            """, ct), CultureInfo.InvariantCulture);
        await ExecAsync($"INSERT inv.room_night(room_id, night, reservation_id) SELECT room_id, '{arrival}', {resId} FROM inv.room WHERE property_id = {PropertyId} AND room_no = '{room}';", ct);
        var guestId = Convert.ToInt64(await ScalarAsync($"""
            INSERT gst.guest(property_id, full_name, phone, email, nationality, id_type, id_last4, verified, origin, created_utc)
            OUTPUT INSERTED.guest_id VALUES ({PropertyId}, {S(guestName)}, {S(phone ?? string.Empty)}, {S(email)}, N'Indian', N'Aadhaar (demo)', 'Z987', 1, 'manual', '2026-10-23T09:00:00');
            INSERT gst.registration(reservation_id, guest_id, registered_utc, registered_by) VALUES ({resId}, SCOPE_IDENTITY(), '2026-10-23T09:00:00', {Users.Reception});
            """, ct), CultureInfo.InvariantCulture);
        await ExecAsync($"INSERT bill.folio(reservation_id) VALUES ({resId});", ct);
        if (inHouse)
        {
            var folio = Convert.ToInt64(await ScalarAsync($"SELECT folio_id FROM bill.folio WHERE reservation_id = {resId};", ct), CultureInfo.InvariantCulture);
            await ExecAsync($"""
                INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by)
                VALUES ({PropertyId}, {resId}, {guestId}, {folio}, 'InHouse', '{arrival}', '{departure}', '{arrival}', '{arrival}T09:00:00', {Users.Reception});
                INSERT gst.stay_segment(stay_id, seq, room_id, from_date) SELECT SCOPE_IDENTITY(), 1, room_id, '{arrival}' FROM inv.room WHERE property_id = {PropertyId} AND room_no = '{room}';
                """, ct);
        }

        return (resId, guestId, publicNo);
    }

    public async ValueTask DisposeAsync()
    {
        if (!NamePattern().IsMatch(Name)) return;
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(_server, "master"));
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
