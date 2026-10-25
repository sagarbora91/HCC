using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Contracts.Configuration;
using Hcc.Domain.Access;
using Hcc.Domain.Configuration;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Configuration;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Configuration.Tests.Harness;

public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>One fictional hotel (its own property, so tests sharing a database never see each other's settings) and its staff.</summary>
public sealed record Hotel(int PropertyId, int Owner, int TechAdmin, int Manager, int Reception, int Cashier, int Accountant, int Supervisor, int Housekeeper, int Maintenance)
{
    public IEnumerable<int> NonAdmins => [Manager, Reception, Cashier, Accountant, Supervisor, Housekeeper, Maintenance];
}

/// <summary>
/// A disposable <c>HccTest_*</c> database on <c>.\SQLEXPRESS</c> (env <c>HCC_TEST_SQL_SERVER</c>) with every migration, the REAL P1
/// policy (<see cref="AccessModule"/>, role templates of 0101) and the configuration services. Only HccTest names are dropped.
/// </summary>
public sealed partial class ConfigDatabase : IAsyncLifetime
{
    public static readonly DateOnly Day = new(2026, 10, 24);   // prototype BASE business date

    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private static int _hotel;

    public string Server { get; } = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";

    public string Name { get; } = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];

    public SqlOptions Options => new() { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(Server, Name), BackupDirectory = "NUL" };

    public ManualClock Clock { get; } = new(new DateTimeOffset(2026, 10, 24, 6, 0, 0, TimeSpan.Zero));

    public SqlPlatform Platform { get; private set; } = null!;

    public SqlAccessPolicy Policy { get; private set; } = null!;

    public SettingsRegistry Registry { get; private set; } = null!;

    public SqlConfigurationStore Store { get; private set; } = null!;

    public ConfigurationService Service { get; private set; } = null!;

    public ConfigurationQueries Queries { get; private set; } = null!;

    public SettingsReader Reader { get; private set; } = null!;

    public ModuleGate Gate { get; private set; } = null!;

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
        Policy = new AccessModule(Platform, new AccessOptions { PasswordIterations = 1000, PinIterations = 1000 }, Clock).Policy;
        Registry = new SettingsRegistry([]);
        Store = new SqlConfigurationStore(Platform.Connections);
        Service = new ConfigurationService(Platform.Commands, Policy, Registry, Store, Clock);
        Queries = new ConfigurationQueries(Policy, Registry, Store, Service, Clock);
        Reader = new SettingsReader(Registry, Store, Store);
        Gate = new ModuleGate(Reader);
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

    public async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] args)
    {
        await using var conn = await Platform.Connections.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        return await cmd.ExecuteScalarAsync();
    }

    public async Task<int> CountAsync(string sql, params (string Name, object Value)[] args) => Convert.ToInt32(await ScalarAsync(sql, args), CultureInfo.InvariantCulture);

    /// <summary>A new fictional property on business date 24 Oct 2026 with one user per role template (names as in the prototype BASE fixture).</summary>
    public async Task<Hotel> NewHotelAsync(string? name = null)
    {
        var n = Interlocked.Increment(ref _hotel).ToString("000", CultureInfo.InvariantCulture);
        var p = Convert.ToInt32(await ScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES (@c, @n, 'Asia/Kolkata', '2026-10-24');",
            ("@c", "H" + n), ("@n", name ?? "HCC Demo Residency " + n)), CultureInfo.InvariantCulture);
        async Task<int> User(string login, string display, string role)
        {
            var id = Convert.ToInt32(await ScalarAsync("INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES (@p, @l, @d, '2026-01-01');",
                ("@p", p), ("@l", login + n), ("@d", display)), CultureInfo.InvariantCulture);
            await ScalarAsync("INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES (@u, @r, '2026-01-01', '2026-01-01');", ("@u", id), ("@r", role));
            return id;
        }

        return new Hotel(p, await User("owner", "Sagar", RoleCodes.Owner), await User("techadmin", "Lata", RoleCodes.TechAdmin), await User("manager", "Meera", RoleCodes.Manager),
            await User("reception", "Ravi", RoleCodes.Reception), await User("cashier", "Kiran", RoleCodes.Cashier), await User("accountant", "Anil", RoleCodes.Accountant),
            await User("supervisor", "Sunita", RoleCodes.Supervisor), await User("housekeeper", "Hema", RoleCodes.Housekeeper), await User("maint", "Mohan", RoleCodes.Maintenance));
    }

    /// <summary>Signs a password user in (seeds the credential first).</summary>
    public async Task<string> PasswordAsync(int userId, string password)
    {
        var h = new Pbkdf2SecretHasher().Hash(password, 1000);
        await ScalarAsync("INSERT plat.credential(user_id, kind, algorithm, iterations, salt, secret_hash, changed_utc) VALUES (@u, @k, @a, @i, @s, @h, '2026-01-01');",
            ("@u", userId), ("@k", CredentialPolicy.ToWire(CredentialKind.Password)), ("@a", h.Algorithm), ("@i", h.Iterations), ("@s", h.Salt), ("@h", h.Hash));
        return (string)(await ScalarAsync("SELECT login_name FROM plat.app_user WHERE user_id = @u;", ("@u", userId)))!;
    }

    public Task SetBusinessDateAsync(Hotel h, DateOnly d) =>
        ScalarAsync("UPDATE inv.property SET business_date = @d WHERE property_id = @p;", ("@d", d.ToDateTime(TimeOnly.MinValue)), ("@p", h.PropertyId));

    public static string Key() => Guid.NewGuid().ToString("N");

    public static CommandContext Ctx(Hotel h, int who) => new(h.PropertyId, who, "op", Key());

    public static JsonElement J(object? v) => JsonSerializer.SerializeToElement(v, Web);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>SETTING_SET as the prototype tests call it: reads the setting's current version first.</summary>
    public async Task<SetSettingResult> SetAsync(Hotel h, int who, string key, object? value, string? effectiveFrom = null, string? reason = null)
    {
        var versions = await Store.VersionsAsync(h.PropertyId);
        var version = versions.TryGetValue(key, out var v) ? Convert.ToBase64String(v) : null;
        return (await Service.SetSettingAsync(Ctx(h, who), new SetSettingRequest(key, J(value), effectiveFrom, reason, version))).Value;
    }

    public async Task<ConfigValue> ValueAsync(Hotel h, string key, DateOnly? on = null) => await Reader.GetAsync(h.PropertyId, key, on);

    public async Task<SettingRequestView> RequestAsync(Hotel h, long id) => (await Queries.RequestsAsync(h.PropertyId, h.Owner, null)).Items.Single(r => r.RequestId == id);

    public async Task<DecideSettingRequestResult> DecideAsync(Hotel h, int who, long requestId, bool approve, string? reason = null)
    {
        var r = await RequestAsync(h, requestId);
        return (await Service.DecideAsync(Ctx(h, who), new DecideSettingRequestRequest(requestId, approve, reason, r.Version))).Value;
    }

    public async Task<IReadOnlyList<SettingHistoryEntry>> HistoryAsync(Hotel h, string? key = null) => (await Queries.HistoryAsync(h.PropertyId, h.Owner, key, 1, 100)).Items;
}

public static class Expect
{
    public static async Task<HccException> Fails(HccErrorCode code, Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<HccException>(act);
        Assert.True(ex.Code == code, $"Expected {code} but got {ex.Code}: {ex.Message}");
        return ex;
    }

    public static async Task<string> Code(Func<Task> act)
    {
        try
        {
            await act();
            return "OK";
        }
        catch (HccException ex)
        {
            return ex.Code.ToString();
        }
    }

    public static async Task<string> Message(Func<Task> act)
    {
        try
        {
            await act();
            return string.Empty;
        }
        catch (HccException ex)
        {
            return ex.Message;
        }
    }
}
