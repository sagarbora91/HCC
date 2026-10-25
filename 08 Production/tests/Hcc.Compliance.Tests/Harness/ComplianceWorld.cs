using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Compliance;
using Hcc.Contracts;
using Hcc.Contracts.Compliance;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Compliance;

namespace Hcc.Compliance.Tests.Harness;

/// <summary>Demo principals (prototype user ids → integer principal ids).</summary>
public static class Users
{
    public const int Owner = 1, Manager = 2, Reception = 3, Cashier = 4, Accountant = 8, TechAdmin = 10;
}

/// <summary>
/// Test stand-in for P1's policy with the grants migration 1100 seeds (DEC-055): PERM-CMP-001 Owner + Accountant, PERM-CMP-002 Owner only.
/// (A seed test checks the migration's role grants equal these.)
/// </summary>
public sealed class FakeAccessPolicy : IAccessPolicy
{
    private static readonly Dictionary<int, string[]> Grants = new()
    {
        [Users.Owner] = [CompliancePermissions.Use, CompliancePermissions.Manage],
        [Users.Accountant] = [CompliancePermissions.Use],
    };

    public Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default) =>
        Holds(principalId, permissionId) ? Task.CompletedTask : throw new HccException(HccErrorCode.FORBIDDEN, "Access denied.");

    public Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default) => Task.FromResult(Holds(principalId, permissionId));

    private static bool Holds(int principal, string perm) => Grants.TryGetValue(principal, out var g) && g.Contains(perm);
}

public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>A fresh database with one property at business date 2026-10-24 (the prototype BASE date) and the real compliance use cases.</summary>
public sealed class ComplianceWorld : IAsyncDisposable
{
    public const string Today = "2026-10-24";

    private ComplianceWorld(ComplianceDatabase db, int propertyId)
    {
        Db = db;
        PropertyId = propertyId;
        Policy = new FakeAccessPolicy();
        Clock = new TestClock(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero));
        Store = new SqlComplianceStore(db.Platform.Connections);
        Commands = new ComplianceCommands(db.Platform.Commands, Policy, Store, Clock);
        Queries = new ComplianceQueries(Policy, Store);
    }

    public ComplianceDatabase Db { get; }

    public int PropertyId { get; }

    public FakeAccessPolicy Policy { get; }

    public TestClock Clock { get; }

    public SqlComplianceStore Store { get; }

    public ComplianceCommands Commands { get; }

    public ComplianceQueries Queries { get; }

    public CommandContext Ctx(int principal, string key) => new(PropertyId, principal, "T", key);

    public ValueTask DisposeAsync() => Db.DisposeAsync();

    /// <summary>Prototype BASE: the 7 default obligations tracked from Apr 2026 (GSTR-9 from FY 2025-26) with the fixture filings, leaving
    /// GSTR-1 Sep, PT Sep and GSTR-9 FY 2025-26 open; ESIC Apr and May are "Not applicable".</summary>
    public static async Task<ComplianceWorld> BaseAsync(CancellationToken ct = default)
    {
        var db = await ComplianceDatabase.CreateAsync(ct);
        try
        {
            var p = Convert.ToInt32(await db.ScalarAsync($"INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '{Today}');", ct), CultureInfo.InvariantCulture);
            var w = new ComplianceWorld(db, p);
            await w.Commands.SeedDefaultsAsync(w.Ctx(Users.Owner, "seed"), new SeedDefaultsRequest("2026-04", "2025-26"), ct);
            var snap = await w.Store.SnapshotAsync(p, ct);
            var open = new HashSet<string> { "GSTR-1:2026-09", "PT:2026-09", "GSTR-9:2025-26" };
            var n = 0;
            foreach (var e in ComplianceCalendar.Build(snap, DateOnly.Parse(Today, CultureInfo.InvariantCulture)))
            {
                if (open.Contains(e.Obligation.Kind + ":" + e.Row.Period)) continue;
                var at = e.Row.Due.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T11:00:00";
                var na = e.Obligation.Kind == "ESIC" && e.Row.Period is "2026-04" or "2026-05";
                n++;
                await db.ExecAsync(na
                    ? $"INSERT cmp.filing(obligation_id, period, status, filed_by, filed_utc, reason) VALUES ({e.Obligation.ObligationId}, '{e.Row.Period}', 'Not applicable', {Users.Accountant}, '{at}', N'Staff count below the ESIC threshold until June (illustrative)');"
                    : $"INSERT cmp.filing(obligation_id, period, status, filed_by, filed_utc, reference) VALUES ({e.Obligation.ObligationId}, '{e.Row.Period}', 'Filed', {Users.Accountant}, '{at}', N'{e.Obligation.Kind}-{e.Row.Period.Replace("-", "", StringComparison.Ordinal)}-{n.ToString(CultureInfo.InvariantCulture)}');", ct);
            }

            return w;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    /// <summary>Obligation id by kind (BASE has one per kind).</summary>
    public async Task<long> IdOfAsync(string kind, CancellationToken ct = default) =>
        Convert.ToInt64(await Db.ScalarAsync($"SELECT obligation_id FROM cmp.obligation WHERE property_id = {PropertyId} AND kind = '{kind}' AND name NOT LIKE N'%(fictional)%';", ct), CultureInfo.InvariantCulture);

    public async Task<ObligationDto> ObligationAsync(long id, CancellationToken ct = default) =>
        (await Queries.GetObligationsAsync(PropertyId, Users.Owner, ct)).Single(o => o.ObligationId == id);

    public async Task<CalendarItemDto> ItemAsync(string kind, string period, CancellationToken ct = default) =>
        (await Queries.GetCalendarAsync(PropertyId, Users.Owner, true, ct)).Items.Single(i => i.Kind == kind && i.Period == period && !i.Name.Contains("(fictional)", StringComparison.Ordinal));
}
