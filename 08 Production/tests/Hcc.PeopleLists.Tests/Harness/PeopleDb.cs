using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Masters;
using Hcc.Application.Payroll;
using Hcc.Application.PeopleLists;
using Hcc.Contracts;
using Hcc.Contracts.Payroll;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Masters;
using Hcc.Infrastructure.SqlServer.Payroll;
using Hcc.Infrastructure.SqlServer.PeopleLists;
using Microsoft.Data.SqlClient;

namespace Hcc.PeopleLists.Tests.Harness;

/// <summary>Deterministic clock.</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// One disposable database (<c>HccTest_&lt;UTC&gt;_&lt;hex&gt;</c>) with every migration, the REAL access policy (seeded role grants incl. PERM-MST-001/002
/// and the payroll PERM-HR ids), the People lists commands / queries and the payroll services. Tests isolate themselves with <see cref="NewHotelAsync"/>
/// (own property, users and departments).
/// </summary>
public sealed partial class PeopleDb : IAsyncLifetime
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private int _hotelNo;

    public string Server { get; } = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";

    public string Name { get; } = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];

    public SqlOptions Options => new() { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(Server, Name), BackupDirectory = "NUL" };

    public SqlPlatform Platform { get; private set; } = null!;

    /// <summary>24 Oct 2026, 08:00 IST (a Saturday).</summary>
    public ManualClock Clock { get; } = new(new DateTimeOffset(2026, 10, 24, 2, 30, 0, TimeSpan.Zero));

    public AccessModule Access { get; private set; } = null!;

    public PeopleListsCommands Lists { get; private set; } = null!;

    public PeopleListsQueries ListQueries { get; private set; } = null!;

    public PayrollService Payroll { get; private set; } = null!;

    public PayrollQueries PayrollQueries { get; private set; } = null!;

    public SqlPayrollStore PayrollStore { get; private set; } = null!;

    public IAccessPolicy Policy => Access.Policy;

    public async ValueTask InitializeAsync()
    {
        if (!NamePattern().IsMatch(Name)) throw new InvalidOperationException("Unexpected test database name.");
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(Server, "master")))
        {
            await conn.OpenAsync();
            await using var cmd = new SqlCommand($"CREATE DATABASE [{Name}];", conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync();
        }

        Platform = new SqlPlatform(Options);
        await Platform.Migrations.MigrateAsync();
        Access = new AccessModule(Platform, new AccessOptions { PasswordIterations = 1000, PinIterations = 1000 }, Clock);
        Lists = new PeopleListsCommands(Platform.Commands, Access.Policy, new SqlPeopleListsStore(), new SqlMastersStore(), [new SqlPeopleListsUsage()], Clock);
        ListQueries = new PeopleListsQueries(Access.Policy, new SqlPeopleListsReadStore(Platform.Connections), new SqlMastersReadStore(Platform.Connections));
        PayrollStore = new SqlPayrollStore(Platform.Connections);
        Payroll = new PayrollService(Platform.Commands, PayrollStore, Access.Policy, Clock);
        PayrollQueries = new PayrollQueries(PayrollStore, Access.Policy);
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

    /// <summary>A fictional hotel (own property) with one user per role, three departments and the business date 24 Oct 2026.</summary>
    public async Task<Hotel> NewHotelAsync(string businessDate = "2026-10-24")
    {
        var no = Interlocked.Increment(ref _hotelNo).ToString("000", CultureInfo.InvariantCulture);
        var property = (int)await LongAsync($"INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('P{no}', N'Demo Hotel {no}', 'Asia/Kolkata', @d);", ("@d", businessDate));
        var h = new Hotel(this, property);
        foreach (var (key, role) in Hotel.Roles)
        {
            var id = (int)await LongAsync("INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES (@p, @l, @n, '2026-01-01');",
                ("@p", property), ("@l", $"{key}.{no}"), ("@n", "Demo " + key));
            await ScalarAsync("INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES (@u, @r, '2026-01-01', '2026-01-01');", ("@u", id), ("@r", role));
            h.Users[key] = id;
        }

        foreach (var (code, name) in new[] { ("FO", "Front office"), ("HK", "Housekeeping"), ("KIT", "Kitchen") })
            await ScalarAsync("INSERT hr.department(property_id, dept_code, name) VALUES (@p, @c, @n);", ("@p", property), ("@c", code), ("@n", name));
        return h;
    }
}

/// <summary>A test hotel: its property and users by key.</summary>
public sealed class Hotel(PeopleDb db, int propertyId)
{
    public static readonly (string Key, string Role)[] Roles =
    [
        ("owner", "OWN"), ("manager", "MGR"), ("reception", "REC"), ("cashier", "CSH"), ("accountant", "ACC"), ("supervisor", "SUP"),
        ("housekeeper", "HKP"), ("maint", "MNT"), ("techadmin", "TAD"),
    ];

    public PeopleDb Db { get; } = db;

    public int PropertyId { get; } = propertyId;

    public Dictionary<string, int> Users { get; } = [];

    public PeopleListsCommands Lists => Db.Lists;

    public PeopleListsQueries Queries => Db.ListQueries;

    public CommandContext As(string user, string operation, string? key = null) => new(PropertyId, Users[user], operation, key ?? Guid.NewGuid().ToString("N"));

    public int U(string user) => Users[user];

    public Task<string> VersionAsync(string table, string key, long id) =>
        Db.ScalarAsync($"SELECT row_ver FROM {table} WHERE {key} = @i;", ("@i", id)).ContinueWith(t => Convert.ToBase64String((byte[])t.Result!), TaskScheduler.Default);

    public Task<string> StaffVersionAsync(long staffId) => VersionAsync("hr.staff", "staff_id", staffId);

    /// <summary>A staff member added through SaveStaff by the Owner (monthly salary set), optionally linked to a user and a designation code.</summary>
    public async Task<long> AddStaffAsync(string name, string shift = "M", int weeklyOff = 2, string dept = "FO", string? linkUser = null, string payType = "monthly", long monthly = 1_800_000,
        long daily = 0, string joined = "2025-04-01", string designation = "Associate", string? designationCode = null)
    {
        var r = await Db.Payroll.SaveStaffAsync(As("owner", PayrollService.OpSaveStaff), new SaveStaffRequest(null, null, name, dept, designation, shift, weeklyOff, "90000 12345",
            linkUser is null ? null : U(linkUser), false, DateOnly.ParseExact(joined, "yyyy-MM-dd", CultureInfo.InvariantCulture), payType, monthly, daily, "M", null, null, null,
            "Demo Bank (fictional)", "000099990001", "DEMO0000101", designationCode));
        return r.Value.StaffId;
    }

    public static async Task<HccException> FailsAsync(Func<Task> act, HccErrorCode code, string? reason = null)
    {
        var ex = await Assert.ThrowsAsync<HccException>(act);
        Assert.True(ex.Code == code, $"Expected {code}, got {ex.Code}: {ex.Message}");
        if (reason is not null) Assert.Equal(reason, ex.Details!["reason"].GetString());
        return ex;
    }
}
