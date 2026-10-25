using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Inventory;
using Hcc.Application.Reservations;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Inventory;
using Hcc.Infrastructure.SqlServer.Migrations;
using Hcc.Infrastructure.SqlServer.Reservations;
using Microsoft.Data.SqlClient;

namespace Hcc.Inventory.Tests.Harness;

/// <summary>
/// Embedded platform migrations (0001–0002) plus the P2 block (database/modules/02xx_*.sql, copied next to the tests).
/// Bypasses EmbeddedMigrationSource.Validate's contiguity rule, which the lead is asked to relax for the per-agent blocks.
/// </summary>
public sealed class P2MigrationSource : IMigrationSource
{
    public IReadOnlyList<MigrationScript> Load()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Migrations");
        var p2 = Directory.GetFiles(dir, "0*.sql").Select(f =>
        {
            var name = Path.GetFileNameWithoutExtension(f);
            return new MigrationScript(int.Parse(name[..4], CultureInfo.InvariantCulture), name[5..], File.ReadAllText(f));
        });
        return new EmbeddedMigrationSource().Load().Concat(p2).OrderBy(s => s.Version).ToList();
    }
}

/// <summary>
/// A uniquely named <c>HccTest_*</c> database on <c>.\SQLEXPRESS</c> (env <c>HCC_TEST_SQL_SERVER</c>) with the P2 migrations
/// applied; dropped afterwards. Only names matching the pattern are dropped (never EtpReporting / EtpScratch_*).
/// </summary>
public sealed partial class InventoryDatabase : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    private InventoryDatabase(string server, string name)
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

    public static async Task<InventoryDatabase> CreateAsync(CancellationToken ct = default)
    {
        var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        if (!NamePattern().IsMatch(name)) throw new InvalidOperationException("Unexpected test database name.");
        var db = new InventoryDatabase(TestServer, name);
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(db.Server, "master")))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"CREATE DATABASE [{name}];", conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await new MigrationRunner(db.Platform.Connections, new P2MigrationSource(), db.Platform.BackupGate).MigrateAsync(ct);
        return db;
    }

    public async Task<object?> ScalarAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = await Platform.Connections.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        return await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<int> CountAsync(string sql, CancellationToken ct = default) => Convert.ToInt32(await ScalarAsync(sql, ct), CultureInfo.InvariantCulture);

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

/// <summary>A settable clock (no extra test packages).</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Fake <see cref="IAccessPolicy"/> (P1 builds the real one): principal → granted PERM ids.</summary>
public sealed class FakeAccessPolicy : IAccessPolicy
{
    public const int Owner = 1, Manager = 2, Reception = 3, Housekeeper = 4;

    private static readonly string[] Res = ["PERM-RES-001", "PERM-RES-002", "PERM-RES-003", "PERM-RES-004"];

    private readonly Dictionary<int, HashSet<string>> _grants = new()
    {
        [Owner] = [.. Res, "PERM-APR-001"],
        [Manager] = [.. Res, "PERM-APR-001"],
        [Reception] = [.. Res],
        [Housekeeper] = [],
    };

    public List<(int Principal, string Permission)> Checks { get; } = [];

    public Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default)
    {
        lock (Checks) Checks.Add((principalId, permissionId));
        if (!Has(principalId, permissionId)) throw new HccException(HccErrorCode.FORBIDDEN, "You do not have permission for this action.");
        return Task.CompletedTask;
    }

    public Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default)
        => Task.FromResult(Has(principalId, permissionId));

    private bool Has(int principalId, string permissionId) => _grants.TryGetValue(principalId, out var g) && g.Contains(permissionId);
}

/// <summary>The P2 services wired over one test database.</summary>
public sealed class P2Services
{
    public P2Services(InventoryDatabase db, TestClock clock, int taxBps = 1000)
    {
        ArgumentNullException.ThrowIfNull(db);
        Db = db;
        Clock = clock;
        Access = new FakeAccessPolicy();
        Store = new SqlInventoryStore(db.Platform.Connections);
        Reservations = new SqlReservationStore(db.Platform.Connections);
        Allocator = new SqlInventoryAllocator(db.Platform.Connections);
        RoomBlocks = new SqlRoomBlockPort(db.Platform.Connections);
        Inventory = new InventoryService(Store, db.Platform.Commands, Access, new ConfiguredTaxRateSource(taxBps), clock);
        Booking = new ReservationService(Reservations, Store, Inventory, db.Platform.Commands, Access, clock);
    }

    public InventoryDatabase Db { get; }

    public TestClock Clock { get; }

    public FakeAccessPolicy Access { get; }

    public SqlInventoryStore Store { get; }

    public SqlReservationStore Reservations { get; }

    public SqlInventoryAllocator Allocator { get; }

    public SqlRoomBlockPort RoomBlocks { get; }

    public InventoryService Inventory { get; }

    public ReservationService Booking { get; }

    public ICommandRunner Runner => Db.Platform.Commands;

    public static CommandContext Ctx(int principal, string operation, string? key = null, int property = 1)
        => new(property, principal, operation, key ?? Guid.NewGuid().ToString("N"));

    /// <summary>Rows where maintained counts differ from a recomputation (must stay 0).</summary>
    public Task<int> DriftAsync() => Db.CountAsync("SELECT COUNT(*) FROM inv.v_type_night_count_drift;");

    /// <summary>Running a raw allocator change in its own command (for count-level concurrency tests).</summary>
    public Task<CommandResult<Done>> AllocateAsync(long reservationId, StayAllocation? from, StayAllocation? to, int principal = FakeAccessPolicy.Owner)
        => Runner.RunAsync(Ctx(principal, "TestAllocate"), new { reservationId, k = Guid.NewGuid() }, async (tx, ct) =>
        {
            await Allocator.ChangeAsync(tx, 1, reservationId, from, to, true, ct);
            await tx.AuditAsync("TestAllocate", reservationId.ToString(CultureInfo.InvariantCulture), "test", ct);
            return new Done(true);
        });
}

/// <summary>Command result for test-only commands (the idempotency record must hold a JSON object, CK_idempotency_result).</summary>
public sealed record Done(bool Ok);

/// <summary>Runs an async call and returns its HCC error code name, or "OK".</summary>
public static class Outcome
{
    public static async Task<string> CodeAsync(Func<Task> call)
    {
        ArgumentNullException.ThrowIfNull(call);
        try
        {
            await call();
            return "OK";
        }
        catch (HccException e)
        {
            return e.Code.ToString();
        }
    }
}
