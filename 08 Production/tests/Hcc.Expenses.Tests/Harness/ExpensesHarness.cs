using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Expenses;
using Hcc.Application.Payments;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Expenses;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Billing;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Expenses;
using Microsoft.Data.SqlClient;

namespace Hcc.Expenses.Tests.Harness;

/// <summary>Demo principals (fictional; prototype owner/manager/reception/cashier/accountant).</summary>
public static class Who
{
    public const int Owner = 1;
    public const int Manager = 2;
    public const int Reception = 3;
    public const int Cashier = 4;
    public const int Accountant = 5;
    public const int Housekeeper = 6;
}

/// <summary>
/// Stand-in for P1's policy with the RBAC matrix v1 grants for the expense PERM ids (same as 0101 seed): EXP-001 Owner/Manager property-wide,
/// Reception/Cashier/Accountant own submissions; EXP-002 Owner and Accountant unlimited, Manager limit 500000 paise (APPROVAL_REQUIRED
/// above it); EXP-003 Owner/Manager/Accountant. Own-submission scope runs through the real <see cref="SqlExpenseScopeResolver"/>.
/// </summary>
public sealed class FakeAccessPolicy(IEnumerable<IAccessScopeResolver> resolvers) : IAccessPolicy
{
    private sealed record Grant(char Scope, long? Limit = null);

    private readonly Dictionary<int, Dictionary<string, Grant>> _grants = new()
    {
        [Who.Owner] = new()
        {
            ["PERM-EXP-001"] = new('P'), ["PERM-EXP-002"] = new('P'), ["PERM-EXP-003"] = new('P'), ["PERM-SHF-001"] = new('P'), ["PERM-ADM-003"] = new('P'),
        },
        [Who.Manager] = new() { ["PERM-EXP-001"] = new('P'), ["PERM-EXP-002"] = new('P', 500_000), ["PERM-EXP-003"] = new('P'), ["PERM-SHF-001"] = new('P') },
        [Who.Reception] = new() { ["PERM-EXP-001"] = new('O'), ["PERM-SHF-001"] = new('P') },
        [Who.Cashier] = new() { ["PERM-EXP-001"] = new('O'), ["PERM-SHF-001"] = new('P') },
        [Who.Accountant] = new() { ["PERM-EXP-001"] = new('O'), ["PERM-EXP-002"] = new('P'), ["PERM-EXP-003"] = new('P') },
        [Who.Housekeeper] = [],
    };

    public ConcurrentQueue<(int Principal, string Permission, AccessScope? Scope, long? Amount)> Calls { get; } = new();

    public async Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default)
    {
        Calls.Enqueue((principalId, permissionId, scope, amountPaise));
        if (!_grants.TryGetValue(principalId, out var g) || !g.TryGetValue(permissionId, out var grant) || !await ScopeOkAsync(propertyId, principalId, grant, scope, false, ct))
            throw new HccException(HccErrorCode.FORBIDDEN, $"Access denied: {permissionId} permission is required.");
        if (amountPaise is { } a && grant.Limit is { } limit && a > limit)
            throw new HccException(HccErrorCode.APPROVAL_REQUIRED, $"{permissionId} above your limit needs the Owner's approval.");
    }

    public async Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default)
        => _grants.TryGetValue(principalId, out var g) && g.TryGetValue(permissionId, out var grant) && await ScopeOkAsync(propertyId, principalId, grant, scope, true, ct);

    private async Task<bool> ScopeOkAsync(int propertyId, int principalId, Grant grant, AccessScope? scope, bool nullMeansAny, CancellationToken ct)
    {
        if (grant.Scope == 'P' || scope?.Kind == AccessScopeKinds.Any || (scope is null && nullMeansAny)) return true;
        if (scope is null || scope.Kind == AccessScopeKinds.Property) return false;
        var r = resolvers.FirstOrDefault(x => x.Kind == scope.Kind);
        return r is not null && await r.IsOwnAsync(propertyId, principalId, scope, ct);
    }
}

public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>One disposable database (<c>HccTest_&lt;UTC&gt;_&lt;hex&gt;</c>) with all migrations, a fictional property and the expense + cashier services.</summary>
public sealed partial class ExpensesDb : IAsyncLifetime
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    public static readonly DateOnly Day = new(2026, 10, 24);

    public string Server { get; } = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";

    public string Name { get; } = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];

    public SqlOptions Options => new() { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(Server, Name), BackupDirectory = "NUL" };

    public SqlPlatform Platform { get; private set; } = null!;

    public FakeAccessPolicy Policy { get; private set; } = null!;

    public ManualClock Clock { get; } = new(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero));

    public int PropertyId { get; private set; }

    public int OtherPropertyId { get; private set; }

    public ExpenseCommands Commands { get; private set; } = null!;

    public ExpenseQueries Queries { get; private set; } = null!;

    public CashierService Cashier { get; private set; } = null!;

    public BillingQueries Billing { get; private set; } = null!;

    public Caller As(int principal) => new(PropertyId, principal);

    public CommandContext Ctx(int principal, string? key = null, int? property = null) => new(property ?? PropertyId, principal, "x", key ?? Key());

    public static string Key() => Guid.NewGuid().ToString("N");

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
        Platform = new SqlPlatform(options);
        await Platform.Migrations.MigrateAsync();
        PropertyId = Convert.ToInt32(await ScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '2026-10-24');"), CultureInfo.InvariantCulture);
        OtherPropertyId = Convert.ToInt32(await ScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('OTHER', N'Other Demo', 'Asia/Kolkata', '2026-10-24');"), CultureInfo.InvariantCulture);
        var store = new SqlBillingStore(Platform.Connections);
        Policy = new FakeAccessPolicy([new SqlExpenseScopeResolver(Platform.Connections)]);
        Cashier = new CashierService(Platform.Commands, store, Policy, Clock);
        Billing = new BillingQueries(store, Policy, Clock);
        var expenses = new SqlExpenseStore(Platform.Connections);
        Commands = new ExpenseCommands(Platform.Commands, Policy, expenses, Cashier, Clock);
        Queries = new ExpenseQueries(Policy, expenses);
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

    public Task ExecAsync(string sql, params (string Name, SqlDbType Type, object Value)[] ps) => ScalarAsync(sql, ps);

    /// <summary>A shared till with an open shift (DEC-052) and the given opening float.</summary>
    public async Task<(int TillId, ShiftResult Shift)> OpenTillAsync(long floatPaise)
    {
        var till = (await Cashier.CreateTillAsync(As(Who.Owner), Key(), new CreateTillRequest("T" + Guid.NewGuid().ToString("N")[..6], "Front desk"))).Value;
        var shift = (await Cashier.OpenShiftAsync(As(Who.Cashier), Key(), new OpenShiftRequest(till.TillId, floatPaise))).Value;
        return (till.TillId, shift);
    }

    public async Task<long> ExpectedCashAsync(long shiftId) => (await Billing.GetShiftAsync(As(Who.Owner), shiftId)).Expected.ExpectedPaise;

    public async Task<ExpenseView> SubmitAsync(int who, string vendor, long amount, string method = "Cash", string category = "Supplies", bool draft = false)
        => (await Commands.SubmitAsync(Ctx(who), new SubmitExpenseRequest(vendor, category, amount, method, draft))).Value.Expense;

    public async Task<ExpenseView> ApproveAsync(int who, ExpenseView e, int? till = null)
        => (await Commands.DecideAsync(Ctx(who), new DecideExpenseRequest(e.ExpenseId, e.Version, true, null, till))).Value.Expense;

    public async Task<long> CountAsync(string sql, params (string Name, SqlDbType Type, object Value)[] ps) => await LongAsync(sql, ps);
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
