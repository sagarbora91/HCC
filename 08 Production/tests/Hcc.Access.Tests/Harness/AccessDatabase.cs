using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Contracts.Access;
using Hcc.Domain.Access;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Access.Tests.Harness;

/// <summary>Settable clock (no extra package): tests move time to cross idle/absolute/lockout boundaries.</summary>
public sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public static FakeClock At2026() => new(new DateTimeOffset(2026, 10, 25, 6, 0, 0, TimeSpan.Zero));

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>
/// A disposable <c>HccTest_*</c> database on <c>.\SQLEXPRESS</c> (env <c>HCC_TEST_SQL_SERVER</c>) with platform + access
/// migrations and one property. Only databases matching the HccTest pattern are ever dropped.
/// </summary>
public sealed partial class AccessDatabase : IAsyncLifetime
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private string? _name;
    private string _server = string.Empty;

    public string ConnectionString { get; private set; } = string.Empty;

    public int PropertyId { get; private set; }

    public SqlPlatform Platform => new(new SqlOptions { ConnectionString = ConnectionString, BackupDirectory = "NUL" });

    public static AccessOptions FastOptions() => new() { PasswordIterations = 1000, PinIterations = 1000 };

    public AccessModule Module(TimeProvider clock, AccessOptions? options = null, IEnumerable<IAccessScopeResolver>? resolvers = null) =>
        new(Platform, options ?? FastOptions(), clock, resolvers);

    public async ValueTask InitializeAsync()
    {
        _server = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";
        _name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        if (!NamePattern().IsMatch(_name)) throw new InvalidOperationException("Unexpected test database name.");
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(_server, "master")))
        {
            await conn.OpenAsync();
            await using var cmd = new SqlCommand($"CREATE DATABASE [{_name}];", conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync();
        }

        ConnectionString = SqlConnectionFactory.LocalDevConnectionString(_server, _name);
        await Platform.Migrations.MigrateAsync();
        PropertyId = Convert.ToInt32(await ScalarAsync(
            "INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('TEST', N'Test Hotel', 'Asia/Kolkata', '2026-10-25');"), CultureInfo.InvariantCulture);
    }

    public async ValueTask DisposeAsync()
    {
        if (_name is null || !NamePattern().IsMatch(_name)) return;
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

    public async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        return await cmd.ExecuteScalarAsync();
    }

    public async Task<int> CountAsync(string sql, params (string Name, object Value)[] args) =>
        Convert.ToInt32(await ScalarAsync(sql, args), CultureInfo.InvariantCulture);

    public async Task<List<object[]>> RowsAsync(string sql)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await using var rd = await cmd.ExecuteReaderAsync();
        var rows = new List<object[]>();
        while (await rd.ReadAsync())
        {
            var row = new object[rd.FieldCount];
            rd.GetValues(row);
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Inserts a user directly (test setup, bypassing the admin commands) with roles and an optional credential.
    /// Returns the user id.
    /// </summary>
    public async Task<int> SeedUserAsync(string login, string[] roles, string? password = null, string? pin = null, DateTime? createdUtc = null)
    {
        var hasher = new Pbkdf2SecretHasher();
        var now = createdUtc ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var id = Convert.ToInt32(await ScalarAsync(
            "INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES (@p, @l, @n, @now);",
            ("@p", PropertyId), ("@l", login), ("@n", "User " + login), ("@now", now)), CultureInfo.InvariantCulture);
        foreach (var r in roles)
            await ScalarAsync("INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES (@u, @r, @now, @now);", ("@u", id), ("@r", r), ("@now", now));
        if (password is not null) await SeedCredentialAsync(id, CredentialKind.Password, password, hasher);
        if (pin is not null) await SeedCredentialAsync(id, CredentialKind.Pin, pin, hasher);
        return id;
    }

    public async Task SeedCredentialAsync(int userId, CredentialKind kind, string secret, ISecretHasher? hasher = null)
    {
        var h = (hasher ?? new Pbkdf2SecretHasher()).Hash(secret, 1000);
        await ScalarAsync("""
            INSERT plat.credential(user_id, kind, algorithm, iterations, salt, secret_hash, changed_utc) VALUES (@u, @k, @a, @i, @s, @h, '2026-01-01');
            """, ("@u", userId), ("@k", CredentialPolicy.ToWire(kind)), ("@a", h.Algorithm), ("@i", h.Iterations), ("@s", h.Salt), ("@h", h.Hash));
    }

    /// <summary>Registers a device directly; returns its token.</summary>
    public async Task<(int DeviceId, string Token)> SeedDeviceAsync(string label = "Front desk tablet")
    {
        var token = AccessTokens.NewToken();
        var id = Convert.ToInt32(await ScalarAsync("""
            INSERT plat.device(property_id, label, token_sha256, registered_by, registered_utc) OUTPUT INSERTED.device_id VALUES (@p, @l, @h, 1, '2026-01-01');
            """, ("@p", PropertyId), ("@l", label), ("@h", AccessTokens.Sha256(token))), CultureInfo.InvariantCulture);
        return (id, token);
    }

    public async Task<string> VersionOfAsync(int userId) =>
        Convert.ToBase64String((byte[])(await ScalarAsync("SELECT row_ver FROM plat.app_user WHERE user_id = @u;", ("@u", userId)))!);

    /// <summary>Signs a password user in and returns the session.</summary>
    public static async Task<AuthenticatedSession> SignInPasswordAsync(AccessModule m, string login, string password) =>
        (await m.Sessions.SignInAsync(new SignInRequest(login, Password: password), null)).Session;
}
