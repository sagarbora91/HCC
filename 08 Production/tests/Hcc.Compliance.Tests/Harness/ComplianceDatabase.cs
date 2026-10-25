using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Migrations;
using Microsoft.Data.SqlClient;

namespace Hcc.Compliance.Tests.Harness;

/// <summary>A disposable <c>HccTest_*</c> database migrated to the latest version (all blocks) .</summary>
public sealed partial class ComplianceDatabase : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private ComplianceDatabase(string server, string name)
    {
        Server = server;
        Name = name;
        Options = new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" };
        Platform = new SqlPlatform(Options);
    }

    public string Server { get; }

    public string Name { get; }

    public SqlOptions Options { get; }

    public SqlPlatform Platform { get; }

    public static string TestServer => Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";

    public static async Task<ComplianceDatabase> CreateAsync(CancellationToken ct = default)
    {
        var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        if (!NamePattern().IsMatch(name)) throw new InvalidOperationException("Unexpected test database name.");
        var db = new ComplianceDatabase(TestServer, name);
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(db.Server, "master")))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"CREATE DATABASE [{name}];", conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await db.Platform.Migrations.MigrateAsync(ct);
        return db;
    }

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
