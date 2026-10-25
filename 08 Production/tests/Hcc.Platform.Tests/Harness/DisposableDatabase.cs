using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Migrations;
using Microsoft.Data.SqlClient;

namespace Hcc.Platform.Tests.Harness;

/// <summary>
/// A uniquely named database on the local test instance (default <c>.\SQLEXPRESS</c>, override with env
/// <c>HCC_TEST_SQL_SERVER</c>), created for one test class/run and dropped afterwards. Only databases whose name matches
/// <see cref="NamePattern"/> are ever dropped; no other database on the instance is touched.
/// </summary>
public sealed partial class DisposableDatabase : IAsyncDisposable
{
    public const string Prefix = "HccTest_";

    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    public static partial Regex NamePattern();

    private DisposableDatabase(string server, string name)
    {
        Server = server;
        Name = name;
        ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name);
    }

    public string Server { get; }

    public string Name { get; }

    public string ConnectionString { get; }

    public static string TestServer => Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";

    public SqlOptions Options(string? backupDirectory = "NUL") => new() { ConnectionString = ConnectionString, BackupDirectory = backupDirectory };

    public SqlPlatform Platform() => new(Options());

    public static async Task<DisposableDatabase> CreateAsync(bool migrate, CancellationToken ct = default)
    {
        var name = Prefix + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        if (!NamePattern().IsMatch(name)) throw new InvalidOperationException("Unexpected test database name.");
        var db = new DisposableDatabase(TestServer, name);
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(db.Server, "master")))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"CREATE DATABASE [{name}];", conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (migrate) await db.Platform().Migrations.MigrateAsync(ct);
        return db;
    }

    public async Task<SqlConnection> OpenAsync(CancellationToken ct = default) => await new SqlConnectionFactory(Options()).OpenAsync(ct);

    public async Task<object?> ScalarAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        return await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<int> CountAsync(string sql, CancellationToken ct = default) => Convert.ToInt32(await ScalarAsync(sql, ct), CultureInfo.InvariantCulture);

    public async Task ExecAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync(ct);
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
}

/// <summary>xUnit class fixture: one migrated disposable database shared by the tests of a class.</summary>
public sealed class MigratedDatabaseFixture : IAsyncLifetime
{
    private DisposableDatabase? _db;

    public DisposableDatabase Db => _db ?? throw new InvalidOperationException("Fixture not initialized.");

    public async ValueTask InitializeAsync() => _db = await DisposableDatabase.CreateAsync(migrate: true);

    public async ValueTask DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }
}

/// <summary>Migration source made of the embedded migrations plus extra test scripts.</summary>
public sealed class ExtraMigrationSource(params MigrationScript[] extra) : IMigrationSource
{
    public IReadOnlyList<MigrationScript> Load() => EmbeddedMigrationSource.Validate(new EmbeddedMigrationSource().Load().Concat(extra));
}
