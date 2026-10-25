using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Payroll;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Payroll;
using Microsoft.Data.SqlClient;

namespace Hcc.Payroll.Tests.Harness;

/// <summary>Deterministic clock (no extra package).</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// One disposable database (<c>HccTest_&lt;UTC&gt;_&lt;hex&gt;</c> on <c>.\SQLEXPRESS</c> or <c>HCC_TEST_SQL_SERVER</c>) with every migration, the REAL P1
/// access policy (<see cref="AccessModule"/>, seeded role grants incl. 0901) and the payroll services. Tests isolate themselves with
/// <see cref="NewHotelAsync"/> (own property and users). Only names matching the pattern are ever dropped.
/// </summary>
public sealed partial class PayrollDb : IAsyncLifetime
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private int _hotelNo;

    public string Server { get; } = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";

    public string Name { get; } = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];

    public SqlOptions Options => new() { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(Server, Name), BackupDirectory = "NUL" };

    public SqlPlatform Platform { get; private set; } = null!;

    /// <summary>24 Oct 2026, 08:00 IST.</summary>
    public ManualClock Clock { get; } = new(new DateTimeOffset(2026, 10, 24, 2, 30, 0, TimeSpan.Zero));

    public AccessModule Access { get; private set; } = null!;

    public PayrollService Payroll { get; private set; } = null!;

    public PayrollQueries Queries { get; private set; } = null!;

    public IAccessPolicy Policy => Access.Policy;

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

        Platform = new SqlPlatform(Options);
        await Platform.Migrations.MigrateAsync();
        Access = new AccessModule(Platform, new AccessOptions { PasswordIterations = 1000, PinIterations = 1000 }, Clock);
        var store = new SqlPayrollStore(Platform.Connections);
        Payroll = new PayrollService(Platform.Commands, store, Access.Policy, Clock);
        Queries = new PayrollQueries(store, Access.Policy);
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

    public async Task<object?> ScalarAsync(string sql, params (string Name, object? Value)[] args)
    {
        await using var conn = await Platform.Connections.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return await cmd.ExecuteScalarAsync();
    }

    public async Task<long> LongAsync(string sql, params (string Name, object? Value)[] args) => Convert.ToInt64(await ScalarAsync(sql, args), CultureInfo.InvariantCulture);

    /// <summary>A fictional hotel (own property) with one user per role; the business date 24 Oct 2026, time zone Asia/Kolkata.</summary>
    public async Task<Hotel> NewHotelAsync(string businessDate = "2026-10-24")
    {
        var no = Interlocked.Increment(ref _hotelNo).ToString("000", CultureInfo.InvariantCulture);
        var property = (int)await LongAsync($"INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('H{no}', N'Demo Hotel {no}', 'Asia/Kolkata', @d);",
            ("@d", businessDate));
        var h = new Hotel(this, property);
        foreach (var (key, role) in Hotel.Roles)
        {
            var id = (int)await LongAsync("INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES (@p, @l, @n, '2026-01-01');",
                ("@p", property), ("@l", $"{key}.{no}"), ("@n", "Demo " + key));
            await ScalarAsync("INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES (@u, @r, '2026-01-01', '2026-01-01');", ("@u", id), ("@r", role));
            h.Users[key] = id;
        }

        foreach (var (code, name) in new[] { ("MGT", "Management"), ("FO", "Front office"), ("HK", "Housekeeping"), ("MNT", "Maintenance"), ("KIT", "Kitchen"), ("SEC", "Security"), ("ACC", "Accounts") })
            await ScalarAsync("INSERT hr.department(property_id, dept_code, name) VALUES (@p, @c, @n);", ("@p", property), ("@c", code), ("@n", name));
        return h;
    }
}

/// <summary>A test hotel: its property, users by key and the golden staff ids.</summary>
public sealed class Hotel(PayrollDb db, int propertyId)
{
    /// <summary>User key → role (keys match the prototype's demo users).</summary>
    public static readonly (string Key, string Role)[] Roles =
    [
        ("owner", "OWN"), ("manager", "MGR"), ("reception", "REC"), ("cashier", "CSH"), ("accountant", "ACC"), ("supervisor", "SUP"),
        ("housekeeper", "HKP"), ("housekeeper2", "HKP"), ("maint", "MNT"), ("tech", "TAD"),
    ];

    public PayrollDb Db { get; } = db;

    public int PropertyId { get; } = propertyId;

    public Dictionary<string, int> Users { get; } = [];

    /// <summary>Golden staff code (HR-S01…) → staff_id.</summary>
    public Dictionary<string, long> Staff { get; } = [];

    public PayrollService Payroll => Db.Payroll;

    public PayrollQueries Queries => Db.Queries;

    public CommandContext As(string user, string operation, string? key = null) => new(PropertyId, Users[user], operation, key ?? Guid.NewGuid().ToString("N"));

    public int U(string user) => Users[user];

    public long S(string code) => Staff[code];

    public Task SetBusinessDateAsync(string date) => Db.ScalarAsync("UPDATE inv.property SET business_date = @d WHERE property_id = @p;", ("@d", date), ("@p", PropertyId));

    public async Task<string> StaffVersionAsync(long staffId) =>
        Convert.ToBase64String((byte[])(await Db.ScalarAsync("SELECT row_ver FROM hr.staff WHERE staff_id = @s;", ("@s", staffId)))!);

    public async Task<string> RowVersionAsync(string table, string key, long id) =>
        Convert.ToBase64String((byte[])(await Db.ScalarAsync($"SELECT row_ver FROM {table} WHERE {key} = @i;", ("@i", id)))!);

    /// <summary>Seeds the prototype BASE payroll fixture: 12 staff (8 linked), Aug + Sep 2026 attendance, leave, the open advance.</summary>
    public async Task SeedGoldenAsync()
    {
        var g = PayrollGolden.Data;
        foreach (var s in g.Staff)
        {
            var id = await Db.LongAsync("""
                INSERT hr.staff(property_id, name, dept_code, designation, user_id, pay_type, monthly_paise, daily_paise, default_shift, weekly_off, joined, active, phone,
                                gender, bank_name, account_no, ifsc, created_by, created_utc)
                OUTPUT INSERTED.staff_id VALUES (@p, @n, @d, @g, @u, @pt, @m, @dw, @sh, @wo, @j, 1, @ph, @ge, @bn, @ac, @if, @by, '2026-01-01');
                """, ("@p", PropertyId), ("@n", s.Name), ("@d", s.Dept), ("@g", s.Designation), ("@u", s.UserId is null ? null : Users[s.UserId]), ("@pt", s.PayType),
                ("@m", s.Monthly), ("@dw", s.Daily), ("@sh", s.DefaultShift), ("@wo", s.WeeklyOff), ("@j", s.Joined), ("@ph", s.Phone), ("@ge", s.Gender),
                ("@bn", s.BankName), ("@ac", s.AccountNo), ("@if", s.Ifsc), ("@by", U("owner")));
            Staff[s.Id] = id;
        }

        foreach (var chunk in g.Attendance.Chunk(400))
        {
            var sql = new StringBuilder("INSERT hr.attendance(staff_id, work_date, status, ot_minutes, marked_by, marked_utc) VALUES ");
            sql.AppendJoin(",", chunk.Select(a => string.Create(CultureInfo.InvariantCulture, $"({Staff[a.StaffId]}, '{a.Date}', '{a.Status}', {a.OtMin}, {U("manager")}, '2026-10-01')")));
            await Db.ScalarAsync(sql.ToString());
        }

        // The fixture holds one 0-day request (HR-LV-4: LOP on Imran's weekly off); the server refuses such a request, so it is not seeded.
        foreach (var l in g.Leaves.Where(l => l.Days > 0))
            await Db.ScalarAsync("""
                INSERT hr.leave_request(property_id, staff_id, leave_type, from_date, to_date, half, days_x2, reason, status, requested_by, requested_utc, decided_by, decided_utc)
                VALUES (@p, @s, @t, @f, @to, @h, @dx, N'Fictional', @st, @rb, '2026-09-08', @db, @du);
                """, ("@p", PropertyId), ("@s", Staff[l.StaffId]), ("@t", l.Type), ("@f", l.From), ("@to", l.To), ("@h", l.Half), ("@dx", (int)(l.Days * 2)), ("@st", l.Status),
                ("@rb", U("owner")), ("@db", l.Status == "Approved" ? U("manager") : null), ("@du", l.Status == "Approved" ? "2026-09-08" : null));
        foreach (var a in g.Advances)
            await Db.ScalarAsync("""
                INSERT hr.advance(property_id, staff_id, amount_paise, installment_paise, advance_date, from_month, reason, given_by, given_utc)
                VALUES (@p, @s, @a, @i, @d, @f, N'Festival advance (fictional)', @by, '2026-08-10');
                """, ("@p", PropertyId), ("@s", Staff[a.StaffId]), ("@a", a.Amount), ("@i", a.Installment), ("@d", a.Date), ("@f", a.Date[..7] + "-01"), ("@by", U("owner")));
    }

    /// <summary>A staff member added through the SaveStaff command by the Owner (salary set), optionally linked to a user.</summary>
    public async Task<long> AddStaffAsync(string name, long monthly = 1_800_000, string dept = "FO", string? linkUser = null, string joined = "2025-04-01", int weeklyOff = 2,
        string payType = "monthly", long daily = 0)
    {
        var r = await Payroll.SaveStaffAsync(As("owner", PayrollService.OpSaveStaff), new Contracts.Payroll.SaveStaffRequest(null, null, name, dept, "Associate", "M", weeklyOff,
            "90000 12345", linkUser is null ? null : U(linkUser), false, PayrollGolden.Date(joined), payType, monthly, daily, "M", null, null, null,
            "Demo Bank (fictional)", "000099990001", "DEMO0000101"));
        return r.Value.StaffId;
    }

    public static async Task<HccException> FailsAsync(Func<Task> act, HccErrorCode code)
    {
        var ex = await Assert.ThrowsAsync<HccException>(act);
        Assert.True(ex.Code == code, $"Expected {code}, got {ex.Code}: {ex.Message}");
        return ex;
    }
}
