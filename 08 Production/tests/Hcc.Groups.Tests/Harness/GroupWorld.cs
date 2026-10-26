using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Groups;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Contracts.Groups;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Billing;
using Hcc.Infrastructure.SqlServer.Closing;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Groups;
using Hcc.Infrastructure.SqlServer.Tax;
using Microsoft.Data.SqlClient;

namespace Hcc.Groups.Tests.Harness;

public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Billing's stay-night source is not used by these tests (no room-night posting through Billing).</summary>
internal sealed class NoStayNights : IStayNightSource
{
    public Task<StayNight> ResolveAsync(ICommandTransaction tx, int propertyId, long stayId, DateOnly serviceDate, CancellationToken ct)
        => throw new NotSupportedException();
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

/// <summary>
/// Disposable HccTest_* database with the prototype BASE group picture on 24 Oct 2026: block GB-1 "Deccan Pharma regional review"
/// (Released at the 21 Oct cut-off; Classic contracted 4, picked 2, released 2, block rate Rs 1,750) with its two in-house members
/// HCC-1044 Arjun Kapoor (room 201) and HCC-1048 Dev Malhotra (room 302): 23–26 Oct at Rs 2,000, the 23 Oct night posted at 5 %
/// (Rs 100 GST = CGST 50 + SGST 50) and Rs 2,000 paid each. Block GB-2 (open, cut-off 27 Oct) and a stand-alone reservation HCC-2001.
/// The REAL access policy (RBAC seed incl. 2201), the real DEC-069 GST master, the P6 business-day guard and Billing's store.
/// </summary>
public sealed partial class GroupWorld : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private readonly string _server;
    private readonly string _name;

    private GroupWorld(string server, string name)
    {
        _server = server;
        _name = name;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
        Clock = new TestClock(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.FromHours(5.5)).ToUniversalTime());
        var c = Platform.Connections;
        Policy = new SqlAccessPolicy(c, Clock, [new SqlShiftScopeResolver(c)]);
        var gstStore = new SqlGstMasterStore(c);
        var gst = new GstRateResolver(gstStore);
        BillingStore = new SqlBillingStore(c);
        Store = new SqlGroupStore(c);
        Commands = new GroupCommands(Platform.Commands, Store, Policy, new BillingGroupFolioPort(BillingStore), gst, Clock, new SqlBusinessDayGuard());
        Queries = new GroupQueries(Store, Policy, gst);
        CompanyStore = new SqlCompanyStore(c);
        Companies = new CompanyCommands(Platform.Commands, CompanyStore, Policy, Clock);
        CompanyBook = new CompanyQueries(CompanyStore, Policy);
        Invoices = new GroupInvoiceSource(Store);
        Billing = new BillingService(Platform.Commands, BillingStore, Policy, new NoStayNights(), gst, Clock, gstStore, new SqlBusinessDayGuard());
        Folios = new BillingQueries(BillingStore, Policy, Clock);
    }

    public SqlPlatform Platform { get; }

    public TestClock Clock { get; }

    public SqlAccessPolicy Policy { get; }

    public SqlBillingStore BillingStore { get; }

    public SqlGroupStore Store { get; }

    public GroupCommands Commands { get; }

    public GroupQueries Queries { get; }

    public SqlCompanyStore CompanyStore { get; }

    public CompanyCommands Companies { get; }

    public CompanyQueries CompanyBook { get; }

    public GroupInvoiceSource Invoices { get; }

    public BillingService Billing { get; }

    public BillingQueries Folios { get; }

    public int PropertyId { get; private set; }

    public Dictionary<string, int> Users { get; } = [];

    public Dictionary<string, long> Res { get; } = [];

    public Dictionary<string, long> Folio { get; } = [];

    public Dictionary<string, long> Night23 { get; } = [];

    public long Gb1 { get; private set; }

    public long Gb2 { get; private set; }

    public CommandContext Ctx(string user, string? key = null) => new(PropertyId, Users[user], "T", key ?? Guid.NewGuid().ToString("N"));

    public Caller Caller(string user) => new(PropertyId, Users[user]);

    public static async Task<GroupWorld> CreateAsync(CancellationToken ct = default)
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

        var w = new GroupWorld(server, name);
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

    public async Task<long> LongAsync(string sql) => Convert.ToInt64(await ScalarAsync(sql), CultureInfo.InvariantCulture);

    public Task ExecAsync(string sql) => ScalarAsync(sql);

    private async Task<long> IdAsync(string sql) => Convert.ToInt64(await ScalarAsync(sql), CultureInfo.InvariantCulture);

    private async Task MemberAsync(string code, string guest, string room, long? block, string status, bool posted, CancellationToken ct)
    {
        var rid = await IdAsync($"""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, nightly_rate_paise, group_block_id, source)
            OUTPUT INSERTED.reservation_id
            SELECT {PropertyId}, '{code}', N'{guest}', '2026-10-23', '2026-10-26', '{status}', room_id, room_type_id, 1, 200000, {(block is { } b ? b.ToString(CultureInfo.InvariantCulture) : "NULL")}, 'Corporate'
              FROM inv.room WHERE property_id = {PropertyId} AND room_no = '{room}';
            """);
        Res[code] = rid;
        for (var d = 23; d < 26; d++)
            await ExecAsync($"INSERT inv.reservation_night(reservation_id, night, rate_paise, tax_bps, tax_paise) VALUES ({rid}, '2026-10-{d}', 200000, 500, 10000);" +
                $"INSERT inv.room_night(room_id, night, reservation_id) SELECT room_id, '2026-10-{d}', {rid} FROM inv.room WHERE property_id = {PropertyId} AND room_no = '{room}';");
        if (!posted) return;
        var folio = await IdAsync($"INSERT bill.folio(reservation_id) OUTPUT INSERTED.folio_id VALUES ({rid});");
        Folio[code] = folio;
        Night23[code] = await IdAsync($"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise, principal_id, posted_utc) OUTPUT INSERTED.charge_id VALUES ({folio}, 'room', '2026-10-23', N'Room night · 2026-10-23', 200000, 500, 10000, {Users["owner"]}, '2026-10-23T18:00:00');");
        await ExecAsync($"INSERT tax.charge_gst(charge_id, category, tax_bps, itc, tax_paise, cgst_paise, sgst_paise, rule_text) VALUES ({Night23[code]}, 'room', 500, 0, 10000, 5000, 5000, N'Room night ≤ Rs 7,500');");
        await ExecAsync($"INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date, reference, principal_id, recorded_utc) VALUES ({folio}, 'payment', 'UPI', 200000, '2026-10-24', N'DEMO-{code}', {Users["cashier"]}, '2026-10-24T08:00:00');");
        var guestId = await IdAsync($"INSERT gst.guest(property_id, full_name, phone, origin, created_utc) OUTPUT INSERTED.guest_id VALUES ({PropertyId}, N'{guest}', '9820001044', 'manual', '2026-10-23');");
        var stay = await IdAsync($"""
            INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by)
            OUTPUT INSERTED.stay_id VALUES ({PropertyId}, {rid}, {guestId}, {folio}, 'InHouse', '2026-10-23', '2026-10-26', '2026-10-23', '2026-10-23T14:00:00', {Users["reception"]});
            """);
        await ExecAsync($"INSERT gst.stay_segment(stay_id, seq, room_id, from_date) SELECT {stay}, 1, room_id, '2026-10-23' FROM inv.room WHERE property_id = {PropertyId} AND room_no = '{room}';");
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        PropertyId = Convert.ToInt32(await ScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '2026-10-24');", ct), CultureInfo.InvariantCulture);
        await ExecAsync($"INSERT inv.room_type(property_id, code, max_occupancy) VALUES ({PropertyId}, 'Classic', 2), ({PropertyId}, 'Deluxe', 3);" +
            $"INSERT inv.room(property_id, room_no, room_type_id) SELECT {PropertyId}, v.r, t.room_type_id FROM (VALUES ('101','Classic'), ('201','Classic'), ('302','Classic'), ('303','Deluxe'), ('104','Deluxe')) v(r, t) JOIN inv.room_type t ON t.code = v.t AND t.property_id = {PropertyId};");
        foreach (var (login, role) in new[] { ("owner", "OWN"), ("manager", "MGR"), ("reception", "REC"), ("cashier", "CSH"), ("accountant", "ACC"), ("housekeeper", "HKP") })
        {
            var id = Convert.ToInt32(await ScalarAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({PropertyId}, '{login}', N'{login}', '2026-01-01');", ct), CultureInfo.InvariantCulture);
            await ExecAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2026-01-01', '2026-01-01');");
            Users[login] = id;
        }

        Gb1 = await IdAsync($"INSERT inv.group_block(property_id, code, name, arrival, departure, cutoff, status, released_utc) OUTPUT INSERTED.group_block_id VALUES ({PropertyId}, 'GB-1', N'Deccan Pharma regional review', '2026-10-23', '2026-10-26', '2026-10-21', 'Released', '2026-10-22T02:30:00');");
        await ExecAsync($"INSERT inv.group_block_type(group_block_id, room_type_id, allotted, picked, released, rate_paise) SELECT {Gb1}, room_type_id, 2, 2, 2, 175000 FROM inv.room_type WHERE property_id = {PropertyId} AND code = 'Classic';");
        Gb2 = await IdAsync($"INSERT inv.group_block(property_id, code, name, arrival, departure, cutoff, status) OUTPUT INSERTED.group_block_id VALUES ({PropertyId}, 'GB-2', N'Godavari Agro dealers meet', '2026-10-30', '2026-11-01', '2026-10-27', 'Open');");
        await ExecAsync($"INSERT inv.group_block_type(group_block_id, room_type_id, allotted, picked, released, rate_paise) SELECT {Gb2}, room_type_id, CASE code WHEN 'Classic' THEN 6 ELSE 3 END, 0, 0, CASE code WHEN 'Classic' THEN 180000 ELSE 270000 END FROM inv.room_type WHERE property_id = {PropertyId};");
        await MemberAsync("HCC-1044", "Arjun Kapoor", "201", Gb1, "InHouse", true, ct);
        await MemberAsync("HCC-1048", "Dev Malhotra", "302", Gb1, "InHouse", true, ct);
        var rid = await IdAsync($"INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, nightly_rate_paise) OUTPUT INSERTED.reservation_id VALUES ({PropertyId}, 'HCC-2001', N'Walk-in Guest', '2026-10-30', '2026-11-01', 'Confirmed', 180000);");
        Res["HCC-2001"] = rid;
    }

    /// <summary>A company-billed group master on block GB-1 (prototype billTo company: room + its tax to the master, extras to the guest).</summary>
    public async Task<GroupResult> CreateGb1Async(string user = "reception", int allowanceBps = 0, int attritionBps = 0)
        => (await Commands.CreateAsync(Ctx(user), new CreateGroupRequest("Deccan Pharma regional review", Gb1, "Rahul Joshi", "Deccan Pharma Distributors (fictional)",
            "Rahul Joshi", "+91 90000 00011", "travel@deccanpharma.example.test", "Pune", "27zzfic0001f1z5", allowanceBps, attritionBps))).Value;

    public async Task<string> VersionAsync(long groupId) => Convert.ToBase64String((byte[])(await ScalarAsync($"SELECT row_ver FROM grp.group_account WHERE group_id = {groupId};"))!);

    public Task<GroupStatementView> StatementAsync(long groupId, string user = "owner") => Queries.StatementAsync(PropertyId, Users[user], groupId);

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
