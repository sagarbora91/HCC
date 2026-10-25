using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Payments;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Billing;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Migrations;
using Microsoft.Data.SqlClient;

namespace Hcc.Billing.Tests.Harness;

/// <summary>Demo principals (fictional) mapped to the RBAC role templates.</summary>
public static class Who
{
    public const int Owner = 1;
    public const int Manager = 2;
    public const int Reception = 3;
    public const int Cashier = 4;
    public const int Cashier2 = 5;
    public const int Housekeeper = 6;
}

/// <summary>
/// Fake <see cref="IAccessPolicy"/> following RBAC matrix §5/§6 for the billing PERM ids (P1 provides the real one).
/// Limits: discount without approval Reception ₹250 / Manager ₹1,000; company credit Manager ₹10,000; cash difference Manager ₹200.
/// </summary>
public sealed class FakeAccessPolicy : IAccessPolicy
{
    private const long Unlimited = long.MaxValue;
    private readonly Dictionary<int, Dictionary<string, long>> _grants = new()
    {
        [Who.Owner] = All(Unlimited),
        [Who.Manager] = new()
        {
            ["PERM-FOL-001"] = Unlimited, ["PERM-FOL-002"] = Unlimited, ["PERM-FOL-003"] = 100_000, ["PERM-FOL-005"] = Unlimited,
            ["PERM-PAY-001"] = Unlimited, ["PERM-PAY-002"] = Unlimited, ["PERM-PAY-003"] = Unlimited, ["PERM-PAY-004"] = Unlimited,
            ["PERM-PAY-005"] = 1_000_000, ["PERM-SHF-001"] = Unlimited, ["PERM-SHF-002"] = 20_000, ["PERM-CLS-002"] = Unlimited,
            ["PERM-APR-001"] = Unlimited, ["PERM-STY-003"] = Unlimited, ["PERM-RPT-002"] = Unlimited,
        },
        [Who.Reception] = new()
        {
            ["PERM-FOL-001"] = Unlimited, ["PERM-FOL-002"] = Unlimited, ["PERM-FOL-003"] = 25_000, ["PERM-FOL-005"] = Unlimited,
            ["PERM-PAY-001"] = Unlimited, ["PERM-PAY-002"] = Unlimited, ["PERM-PAY-003"] = Unlimited, ["PERM-SHF-001"] = Unlimited,
            ["PERM-STY-003"] = Unlimited,
        },
        [Who.Cashier] = Cashier(),
        [Who.Cashier2] = Cashier(),
        [Who.Housekeeper] = [],
    };

    public ConcurrentQueue<(int Principal, string Permission, AccessScope? Scope, long? Amount)> Calls { get; } = new();

    private static Dictionary<string, long> All(long limit) => new[]
    {
        "PERM-FOL-001", "PERM-FOL-002", "PERM-FOL-003", "PERM-FOL-005", "PERM-PAY-001", "PERM-PAY-002", "PERM-PAY-003", "PERM-PAY-004", "PERM-PAY-005",
        "PERM-SHF-001", "PERM-SHF-002", "PERM-CLS-002", "PERM-APR-001", "PERM-STY-003", "PERM-RPT-002", "PERM-ADM-003",
    }.ToDictionary(p => p, _ => limit);

    private static Dictionary<string, long> Cashier() => new()
    {
        ["PERM-FOL-001"] = Unlimited, ["PERM-PAY-001"] = Unlimited, ["PERM-PAY-002"] = Unlimited, ["PERM-PAY-003"] = Unlimited,
        ["PERM-PAY-004"] = Unlimited, ["PERM-SHF-001"] = Unlimited,
    };

    public Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default)
    {
        Calls.Enqueue((principalId, permissionId, scope, amountPaise));
        if (!_grants.TryGetValue(principalId, out var g) || !g.TryGetValue(permissionId, out var limit))
            throw new HccException(HccErrorCode.FORBIDDEN, $"Access denied: {permissionId} permission is required.");
        if (amountPaise is { } a && a > limit) throw new HccException(HccErrorCode.APPROVAL_REQUIRED, $"{permissionId} above your limit needs approval.");
        return Task.CompletedTask;
    }

    public Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default)
        => Task.FromResult(_grants.TryGetValue(principalId, out var g) && g.ContainsKey(permissionId));
}

/// <summary>Deterministic clock for approval expiry.</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Stand-in for the P2/P3 stay port: stay id = reservation id; rate and tax per reservation set by the test.</summary>
public sealed class FakeStayNightSource : IStayNightSource
{
    public ConcurrentDictionary<long, (long FolioId, long RatePaise, int TaxBps)> Stays { get; } = new();

    public (long FolioId, long RatePaise, int TaxBps) this[long stayId] { get => Stays[stayId]; set => Stays[stayId] = value; }

    public Task<StayNight> ResolveAsync(ICommandTransaction tx, int propertyId, long stayId, DateOnly serviceDate, CancellationToken ct)
        => Stays.TryGetValue(stayId, out var s)
            ? Task.FromResult(new StayNight(s.FolioId, new Paise(s.RatePaise), s.TaxBps))
            : throw new HccException(HccErrorCode.NOT_FOUND, "Stay not found.");
}

/// <summary>
/// One disposable database (<c>HccTest_&lt;UTC&gt;_&lt;hex&gt;</c> on <c>.\SQLEXPRESS</c> or <c>HCC_TEST_SQL_SERVER</c>) with platform + module
/// migrations, a fictional property and the billing services wired to fakes. Only names matching the pattern are ever dropped.
/// </summary>
public sealed partial class BillingDb : IAsyncLifetime
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    public static readonly DateOnly Day = new(2026, 10, 24);

    private static int _publicNo;

    public string Server { get; } = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";

    public string Name { get; } = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];

    public SqlOptions Options => new() { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(Server, Name), BackupDirectory = "NUL" };

    public FakeAccessPolicy Policy { get; } = new();

    public ManualClock Clock { get; } = new(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero));

    public FakeStayNightSource Stays { get; } = new();

    public int PropertyId { get; private set; }

    public int OtherPropertyId { get; private set; }

    public BillingService Billing { get; private set; } = null!;

    public CashierService Cashier { get; private set; } = null!;

    public BillingQueries Queries { get; private set; } = null!;

    public ICommandRunner Runner { get; private set; } = null!;

    public Caller As(int principal) => new(PropertyId, principal);

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

        var options = Options;
        var connections = new SqlConnectionFactory(options);
        await new MigrationRunner(connections, new ModuleMigrationSource(), new SqlBackupGate(options)).MigrateAsync();
        PropertyId = Convert.ToInt32(await ScalarAsync("""
            INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '2026-10-24');
            INSERT inv.room_type(property_id, code, max_occupancy) VALUES (SCOPE_IDENTITY(), 'Classic', 2);
            """), CultureInfo.InvariantCulture);
        OtherPropertyId = Convert.ToInt32(await ScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('OTHER', N'Other Demo', 'Asia/Kolkata', '2026-10-24');"), CultureInfo.InvariantCulture);
        Runner = new SqlPlatform(options).Commands;
        var store = new SqlBillingStore(connections);
        Billing = new BillingService(Runner, store, Policy, Stays, new FixedTaxRateSource(), Clock);
        Cashier = new CashierService(Runner, store, Policy, Clock);
        Queries = new BillingQueries(store, Policy, Clock);
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
        await using var conn = await new SqlConnectionFactory(Options).OpenAsync();
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        foreach (var (n, t, v) in ps) cmd.Parameters.Add(n, t).Value = v;
        return await cmd.ExecuteScalarAsync();
    }

    public async Task<long> LongAsync(string sql, params (string Name, SqlDbType Type, object Value)[] ps)
        => Convert.ToInt64(await ScalarAsync(sql, ps), CultureInfo.InvariantCulture);

    public Task SetBusinessDateAsync(DateOnly date)
        => ScalarAsync("UPDATE inv.property SET business_date = @d WHERE property_id = @p;", ("@d", SqlDbType.Date, date), ("@p", SqlDbType.Int, PropertyId));

    /// <summary>A fictional reservation with its folio (folios are opened by P3 check-in in production).</summary>
    public async Task<(long ReservationId, long FolioId)> NewFolioAsync(string status = "InHouse", DateOnly? arrival = null, DateOnly? departure = null, int? property = null)
    {
        var no = "T" + Interlocked.Increment(ref _publicNo).ToString("00000", CultureInfo.InvariantCulture);
        var a = arrival ?? Day;
        var rid = await LongAsync("""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status)
            OUTPUT INSERTED.reservation_id VALUES (@p, @no, N'Fictional guest', @a, @d, @s);
            """, ("@p", SqlDbType.Int, property ?? PropertyId), ("@no", SqlDbType.VarChar, no), ("@a", SqlDbType.Date, a),
            ("@d", SqlDbType.Date, departure ?? a.AddDays(2)), ("@s", SqlDbType.VarChar, status));
        var fid = await LongAsync("INSERT bill.folio(reservation_id) OUTPUT INSERTED.folio_id VALUES (@r);", ("@r", SqlDbType.BigInt, rid));
        return (rid, fid);
    }

    public async Task<string> FolioVersionAsync(long folioId) => (await Queries.GetFolioAsync(As(Who.Owner), folioId)).Version;

    public static string Key() => Guid.NewGuid().ToString("N");

    /// <summary>A till with an open shift (first shift float given).</summary>
    public async Task<(int TillId, ShiftResult Shift)> OpenTillAsync(long floatPaise, string? code = null)
    {
        var till = (await Cashier.CreateTillAsync(As(Who.Owner), Key(), new CreateTillRequest(code ?? ("T" + Guid.NewGuid().ToString("N")[..6]), "Front desk"))).Value;
        var shift = (await Cashier.OpenShiftAsync(As(Who.Cashier), Key(), new OpenShiftRequest(till.TillId, floatPaise))).Value;
        return (till.TillId, shift);
    }

    public Task<CommandResult<ReceiptResult>> PayAsync(long folioId, long amount, string tender = "UPI", string kind = "payment", int? till = null, int who = Who.Cashier, string? key = null)
        => Billing.RecordReceiptAsync(As(who), key ?? Key(), new RecordReceiptRequest(folioId, kind, tender, amount, tender == "Cash" ? null : "DEMO-REF", till));
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
