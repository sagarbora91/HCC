using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Commands;
using Hcc.Application.Customers;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Customers;
using Hcc.Infrastructure.SqlServer.Messaging;
using Microsoft.Data.SqlClient;

namespace Hcc.Customers.Tests.Harness;

public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Disposable HccTest_* database with a small fictional hotel (24 Oct 2026), the REAL access policy (RBAC seed incl. 2301) and the real customer services.
/// Asha Rao: two guest profiles with the same mobile, 3 stays (2 departed, 1 in house) = 6 nights, Rs 17,325.00; a third profile with an e-mail that
/// normalises to hers; a name-only booking. Vikram Shah: a booking without mobile + a profile with one (name-only duplicate).
/// </summary>
public sealed partial class CrmWorld : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private readonly string _server;
    private readonly string _name;

    private CrmWorld(string server, string name)
    {
        _server = server;
        _name = name;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
        Clock = new TestClock(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.FromHours(5.5)).ToUniversalTime());
        Policy = new SqlAccessPolicy(Platform.Connections, Clock, []);
        Messaging = new SqlMessagingDataFactory(Platform.Connections);
        var reads = new SqlCustomerReadStore(Platform.Connections);
        Commands = new CustomerCommands(Platform.Commands, Policy, new SqlCustomerStore(), reads, Messaging, Clock);
        Queries = new CustomerQueries(Policy, reads, Messaging);
    }

    public SqlPlatform Platform { get; }

    public TestClock Clock { get; }

    public SqlAccessPolicy Policy { get; }

    public SqlMessagingDataFactory Messaging { get; }

    public CustomerCommands Commands { get; }

    public CustomerQueries Queries { get; }

    public int PropertyId { get; private set; }

    public Dictionary<string, int> Users { get; } = [];

    public Dictionary<string, long> Guest { get; } = [];

    public Dictionary<string, long> Res { get; } = [];

    public const string Asha = "9820011111", Asha2 = "9000000001", VikramPhone = "9811122233", VikramName = "n:vikram shah";

    public CommandContext Ctx(string user, string key) => new(PropertyId, Users[user], "T", key);

    public static async Task<CrmWorld> CreateAsync(CancellationToken ct)
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

        var w = new CrmWorld(server, name);
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

    public Task ExecAsync(string sql, CancellationToken ct = default) => ScalarAsync(sql, ct);

    private static string S(string? v) => v is null ? "NULL" : "N'" + v.Replace("'", "''", StringComparison.Ordinal) + "'";

    private async Task<long> GuestAsync(string code, string name, string phone, string? email, string? prefs, string? idLast4, CancellationToken ct)
    {
        var id = Convert.ToInt64(await ScalarAsync($"""
            INSERT gst.guest(property_id, full_name, phone, email, preferences, id_type, id_last4, verified, origin, created_utc)
            OUTPUT INSERTED.guest_id VALUES ({PropertyId}, {S(name)}, '{phone}', {S(email)}, {S(prefs)}, {(idLast4 is null ? "NULL" : "N'Aadhaar'")}, {(idLast4 is null ? "NULL" : "'" + idLast4 + "'")}, 1, 'manual', '2026-10-01');
            """, ct), CultureInfo.InvariantCulture);
        Guest[code] = id;
        return id;
    }

    /// <summary>A reservation with optional registration, folio, nightly room charges (net paise at 5 % tax) and stay.</summary>
    private async Task BookAsync(string code, string guestName, string? guest, string arrival, string departure, string status, string? room, long nightly, int nightsPosted, CancellationToken ct)
    {
        var rid = Convert.ToInt64(await ScalarAsync($"INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status) OUTPUT INSERTED.reservation_id VALUES ({PropertyId}, '{code}', {S(guestName)}, '{arrival}', '{departure}', '{status}');", ct), CultureInfo.InvariantCulture);
        Res[code] = rid;
        if (room is not null)
            for (var d = DateOnly.Parse(arrival, CultureInfo.InvariantCulture); d < DateOnly.Parse(departure, CultureInfo.InvariantCulture); d = d.AddDays(1))
                await ExecAsync($"INSERT inv.room_night(room_id, night, reservation_id) SELECT room_id, '{d:yyyy-MM-dd}', {rid} FROM inv.room WHERE property_id = {PropertyId} AND room_no = '{room}';", ct);
        if (guest is not null) await ExecAsync($"INSERT gst.registration(reservation_id, guest_id, registered_utc, registered_by) VALUES ({rid}, {Guest[guest]}, '2026-10-01', {Users["reception"]});", ct);
        if (nightsPosted == 0 && status is not ("InHouse" or "Departed")) return;
        var folio = Convert.ToInt64(await ScalarAsync($"INSERT bill.folio(reservation_id) OUTPUT INSERTED.folio_id VALUES ({rid});", ct), CultureInfo.InvariantCulture);
        for (var i = 0; i < nightsPosted; i++)
            await ExecAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES ({folio}, 'room', DATEADD(DAY, {i}, '{arrival}'), N'Room night', {nightly}, 500, {nightly / 20});", ct);
        if (status is "InHouse" or "Departed" && guest is not null)
        {
            var stay = Convert.ToInt64(await ScalarAsync($"""
                INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by, checked_out_date, checked_out_utc, checked_out_by)
                OUTPUT INSERTED.stay_id VALUES ({PropertyId}, {rid}, {Guest[guest]}, {folio}, '{(status == "InHouse" ? "InHouse" : "CheckedOut")}', '{arrival}', '{departure}', '{arrival}', '{arrival}', {Users["reception"]},
                  {(status == "InHouse" ? "NULL, NULL, NULL" : $"'{departure}', '{departure}', {Users["reception"]}")});
                """, ct), CultureInfo.InvariantCulture);
            await ExecAsync($"INSERT gst.stay_segment(stay_id, seq, room_id, from_date, to_date) SELECT {stay}, 1, room_id, '{arrival}', {(status == "InHouse" ? "NULL" : $"'{departure}'")} FROM inv.room WHERE property_id = {PropertyId} AND room_no = '{room}';", ct);
        }
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        PropertyId = Convert.ToInt32(await ScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '2026-10-24');", ct), CultureInfo.InvariantCulture);
        await ExecAsync($"INSERT inv.room_type(property_id, code, max_occupancy) VALUES ({PropertyId}, 'Classic', 2);" +
            $"INSERT inv.room(property_id, room_no, room_type_id, hk_status) SELECT {PropertyId}, r, room_type_id, 'Ready' FROM inv.room_type CROSS JOIN (VALUES ('101'), ('102'), ('104'), ('105')) v(r) WHERE property_id = {PropertyId};", ct);
        foreach (var (login, role) in new[] { ("owner", "OWN"), ("manager", "MGR"), ("reception", "REC"), ("cashier", "CSH"), ("accountant", "ACC"), ("housekeeper", "HKP") })
        {
            var id = Convert.ToInt32(await ScalarAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({PropertyId}, '{login}', N'{login}', '2026-01-01');", ct), CultureInfo.InvariantCulture);
            await ExecAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2026-01-01', '2026-01-01');", ct);
            Users[login] = id;
        }

        await GuestAsync("asha1", "Asha Rao", "+91 98200 11111", "asha.rao@gmail.com", "Quiet room, extra pillow", "AB12", ct);
        await GuestAsync("asha2", "Asha Rao", "098200 11111", null, null, null, ct);
        await GuestAsync("ashar", "Asha R.", Asha2, "Asha.Rao+stay@gmail.com", null, null, ct);
        await GuestAsync("vik1", "Vikram Shah", string.Empty, null, null, null, ct);
        await GuestAsync("vik2", "Vikram Shah", "+91 98111 22233", "vikram@example.test", null, null, ct);
        // Asha: 2 departed + 1 in house; a future booking and a no-profile booking by name only
        await BookAsync("HCC-A1", "Asha Rao", "asha1", "2026-10-10", "2026-10-12", "Departed", "101", 400000, 2, ct);
        await BookAsync("HCC-A2", "Asha Rao", "asha2", "2026-10-15", "2026-10-16", "Departed", "101", 400000, 1, ct);
        await BookAsync("HCC-A3", "Asha Rao", "asha1", "2026-10-23", "2026-10-26", "InHouse", "102", 450000, 1, ct);
        await BookAsync("HCC-A4", "Asha Rao", null, "2026-10-30", "2026-11-01", "Confirmed", null, 0, 0, ct);
        await BookAsync("HCC-R1", "Asha R.", "ashar", "2026-09-01", "2026-09-02", "Departed", "104", 300000, 1, ct);
        // Vikram
        await BookAsync("HCC-V1", "Vikram Shah", "vik1", "2026-10-05", "2026-10-06", "Departed", "104", 300000, 1, ct);
        await BookAsync("HCC-V2", "Vikram Shah", "vik2", "2026-11-05", "2026-11-06", "Confirmed", null, 0, 0, ct);
        await BookAsync("HCC-V3", "Vikram Shah", "vik2", "2026-10-01", "2026-10-02", "Cancelled", null, 0, 0, ct);
        await BookAsync("HCC-X1", "Hold Person", null, "2026-10-28", "2026-10-29", "Expired", null, 0, 0, ct);
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
