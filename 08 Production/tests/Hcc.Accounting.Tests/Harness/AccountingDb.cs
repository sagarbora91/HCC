using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Payments;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Billing;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Accounting.Tests.Harness;

public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Stand-in for the P3 stay port: stay id → (folio, nightly rate, tax bps) set by the test.</summary>
public sealed class FakeStayNightSource : IStayNightSource
{
    public ConcurrentDictionary<long, (long FolioId, long RatePaise, int TaxBps)> Stays { get; } = new();

    public Task<StayNight> ResolveAsync(ICommandTransaction tx, int propertyId, long stayId, DateOnly serviceDate, CancellationToken ct)
        => Stays.TryGetValue(stayId, out var s)
            ? Task.FromResult(new StayNight(s.FolioId, new Paise(s.RatePaise), s.TaxBps))
            : throw new HccException(HccErrorCode.NOT_FOUND, "Stay not found.");
}

/// <summary>
/// One disposable database (<c>HccTest_&lt;UTC&gt;_&lt;hex&gt;</c> on <c>.\SQLEXPRESS</c> or <c>HCC_TEST_SQL_SERVER</c>) with every migration,
/// a fictional property, one user per role template and the REAL P1 policy (SqlAccessPolicy + the shift scope resolver), so the
/// accounting PERM grants of migration 1001 are exercised. Billing and cashier services produce the outbox events.
/// </summary>
public sealed partial class AccountingDb : IAsyncLifetime
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    public static readonly DateOnly Day = new(2026, 10, 24);

    private static int _publicNo;

    public string Server { get; } = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";

    public string Name { get; } = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];

    public SqlOptions Options => new() { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(Server, Name), BackupDirectory = "NUL" };

    public ManualClock Clock { get; } = new(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero));

    public FakeStayNightSource Stays { get; } = new();

    public SqlPlatform Platform { get; private set; } = null!;

    public int PropertyId { get; private set; }

    public Dictionary<string, int> Users { get; } = new(StringComparer.Ordinal);

    public BillingService Billing { get; private set; } = null!;

    public CashierService Cashier { get; private set; } = null!;

    public BillingQueries BillingQueries { get; private set; } = null!;

    public AccountingService Accounting { get; private set; } = null!;

    public AccountingQueries Queries { get; private set; } = null!;

    public AccountingVoucherProjector Projector { get; private set; } = null!;

    public SqlAccountingStore Store { get; private set; } = null!;

    public Caller As(string role) => new(PropertyId, Users[role]);

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
        PropertyId = Convert.ToInt32(await ScalarAsync("""
            INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '2026-10-24');
            """), CultureInfo.InvariantCulture);
        foreach (var role in new[] { "OWN", "MGR", "REC", "CSH", "ACC", "SUP", "HKP", "MNT", "TAD" })
        {
            var id = Convert.ToInt32(await ScalarAsync("""
                INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES (@p, @l, @n, '2026-01-01');
                """, ("@p", SqlDbType.Int, PropertyId), ("@l", SqlDbType.VarChar, "acc." + role.ToLowerInvariant()), ("@n", SqlDbType.NVarChar, "Demo " + role)), CultureInfo.InvariantCulture);
            await ScalarAsync("INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES (@u, @r, '2026-01-01', '2026-01-01');",
                ("@u", SqlDbType.Int, id), ("@r", SqlDbType.VarChar, role));
            Users[role] = id;
        }

        var policy = new SqlAccessPolicy(Platform.Connections, Clock, [new SqlShiftScopeResolver(Platform.Connections)]);
        var billingStore = new SqlBillingStore(Platform.Connections);
        Billing = new BillingService(Platform.Commands, billingStore, policy, Stays, new FixedTaxRateSource(), Clock);
        Cashier = new CashierService(Platform.Commands, billingStore, policy, Clock);
        BillingQueries = new BillingQueries(billingStore, policy, Clock);
        Store = new SqlAccountingStore(Platform.Connections);
        Accounting = new AccountingService(Platform.Commands, Store, policy, Clock);
        Queries = new AccountingQueries(Store, policy);
        Projector = new AccountingVoucherProjector(Platform.Connections, Platform.Outbox, new StoredChargeGstReader(), Clock);
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

    public async Task<object?> ScalarAsync(string sql, params (string Name, SqlDbType Type, object Value)[] ps)
    {
        await using var conn = await Platform.Connections.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        foreach (var (n, t, v) in ps) cmd.Parameters.Add(n, t).Value = v;
        return await cmd.ExecuteScalarAsync();
    }

    public async Task<long> LongAsync(string sql, params (string Name, SqlDbType Type, object Value)[] ps)
        => Convert.ToInt64(await ScalarAsync(sql, ps), CultureInfo.InvariantCulture);

    public Task SetBusinessDateAsync(DateOnly date)
        => ScalarAsync("UPDATE inv.property SET business_date = @d WHERE property_id = @p;", ("@d", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue)), ("@p", SqlDbType.Int, PropertyId));

    /// <summary>A fictional in-house reservation with its folio; stay id = folio id for the fake stay port.</summary>
    public async Task<long> NewFolioAsync(long ratePaise = 200_000, int taxBps = 1000)
    {
        var no = "A" + Interlocked.Increment(ref _publicNo).ToString("00000", CultureInfo.InvariantCulture);
        var rid = await LongAsync("""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status)
            OUTPUT INSERTED.reservation_id VALUES (@p, @no, N'Fictional guest', '2026-10-23', '2026-10-26', 'InHouse');
            """, ("@p", SqlDbType.Int, PropertyId), ("@no", SqlDbType.VarChar, no));
        var fid = await LongAsync("INSERT bill.folio(reservation_id) OUTPUT INSERTED.folio_id VALUES (@r);", ("@r", SqlDbType.BigInt, rid));
        Stays.Stays[fid] = (fid, ratePaise, taxBps);
        return fid;
    }

    public async Task<string> FolioVersionAsync(long folioId) => (await BillingQueries.GetFolioAsync(As("OWN"), folioId)).Version;

    public static string Key() => Guid.NewGuid().ToString("N");

    /// <summary>Runs the consumer until nothing is left.</summary>
    public async Task<int> ProjectAsync()
    {
        var total = 0;
        for (var i = 0; i < 10; i++)
        {
            var n = await Projector.RunOnceAsync();
            total += n;
            if (n == 0) break;
        }

        return total;
    }

    /// <summary>Publishes an outbox event for this property through the real command runner (stands in for POS / expenses / payroll).</summary>
    public Task PublishAsync(string eventType, long aggregateId, object payload)
        => Platform.Commands.RunAsync(new CommandContext(PropertyId, Users["OWN"], "TestPublish", Key()), new { eventType, aggregateId }, async (tx, ct) =>
        {
            await tx.AuditAsync("TEST_PUBLISH", "test:" + aggregateId.ToString(CultureInfo.InvariantCulture), eventType, ct);
            await tx.PublishAsync(eventType, aggregateId, payload, ct);
            return new { ok = true };
        });
}

public static class Assertions
{
    public static async Task<HccException> Fails(HccErrorCode code, Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<HccException>(act);
        Assert.True(ex.Code == code, $"Expected {code} but got {ex.Code}: {ex.Message}");
        return ex;
    }
}
