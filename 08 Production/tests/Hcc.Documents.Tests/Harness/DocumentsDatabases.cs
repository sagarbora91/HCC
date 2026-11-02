#pragma warning disable xUnit1051 // helper calls are short-lived and do not need per-call cancellation
using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Documents.Tests.Harness;

/// <summary>
/// LF-docdb two-database harness (LF-CONTRACT §12): a disposable <c>HccTest_*</c> pair, the main database and its derived <c>...Documents</c> twin, both migrated
/// (own journals), dropped together. One property, one user and one guest are seeded in the main database.
/// </summary>
public sealed partial class DocumentsDatabases : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private readonly string _server;
    private readonly string _main;

    private DocumentsDatabases(string server, string main)
    {
        _server = server;
        _main = main;
        Platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, main), BackupDirectory = "NUL" });
    }

    public SqlPlatform Platform { get; }

    public string MainName => _main;

    public string DocumentsName => _main + "Documents";

    public int PropertyId { get; private set; }

    public int UserId { get; private set; }

    public long GuestId { get; private set; }

    public static async Task<DocumentsDatabases> CreateAsync(CancellationToken ct, bool migrateDocuments = true)
    {
        var server = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";
        var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        if (!NamePattern().IsMatch(name)) throw new InvalidOperationException("Unexpected test database name.");
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(server, "master")))
        {
            await conn.OpenAsync(ct);
            foreach (var db in new[] { name, name + "Documents" })
            {
                await using var cmd = new SqlCommand($"CREATE DATABASE [{db}];", conn) { CommandTimeout = 120 };
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        var w = new DocumentsDatabases(server, name);
        try
        {
            await w.Platform.Migrations.MigrateAsync(ct);
            if (migrateDocuments) await w.Platform.DocumentMigrations.MigrateAsync(ct);
            w.PropertyId = Convert.ToInt32(await w.MainScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '2026-10-24');", ct), CultureInfo.InvariantCulture);
            w.UserId = Convert.ToInt32(await w.MainScalarAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({w.PropertyId}, 'owner', N'owner', '2026-01-01');", ct), CultureInfo.InvariantCulture);
            w.GuestId = Convert.ToInt64(await w.MainScalarAsync($"INSERT gst.guest(property_id, full_name, phone, origin, created_utc) OUTPUT INSERTED.guest_id VALUES ({w.PropertyId}, N'Test Guest', '9000000000', 'manual', '2026-10-24');", ct), CultureInfo.InvariantCulture);
            return w;
        }
        catch
        {
            await w.DisposeAsync();
            throw;
        }
    }

    public async Task<object?> MainScalarAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = await Platform.Connections.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        return await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<long> MainLongAsync(string sql, CancellationToken ct = default) => Convert.ToInt64(await MainScalarAsync(sql, ct), CultureInfo.InvariantCulture);

    public async Task<object?> DocsScalarAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = await Platform.Documents.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        return await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<long> DocsLongAsync(string sql, CancellationToken ct = default) => Convert.ToInt64(await DocsScalarAsync(sql, ct), CultureInfo.InvariantCulture);

    public async ValueTask DisposeAsync()
    {
        if (!NamePattern().IsMatch(_main)) return;
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(_server, "master"));
        await conn.OpenAsync();
        foreach (var db in new[] { DocumentsName, _main })
        {
            await using var cmd = new SqlCommand($"IF DB_ID(N'{db}') IS NOT NULL BEGIN ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}]; END", conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
