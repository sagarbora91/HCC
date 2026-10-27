using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Compliance;
using Hcc.Contracts;
using Hcc.Domain.Compliance;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Compliance;

namespace Hcc.Compliance.Tests.Harness;

/// <summary>Demo principals (prototype user ids → integer principal ids).</summary>
public static class Users
{
    public const int Owner = 1, Manager = 2, Reception = 3, Cashier = 4, Accountant = 8, TechAdmin = 10;
}

/// <summary>
/// Test stand-in for P1's policy with the grants migration 1101 seeds (DEC-055): PERM-CMP-001 Owner + Accountant, PERM-CMP-002 Owner only.
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

/// <summary>
/// A fresh database with one property at business date 2026-10-24 (the prototype BASE date) and the real compliance use cases.
/// <see cref="BaseAsync"/> builds the prototype's BASE compliance fixture the way it got there: the W05-C obligations and filings, carried
/// onto SCC's cards by migration 4131, then the remaining FY 2026–27 cards due before the business date (fixtures/ext/compliance.ts).
/// </summary>
public sealed class ComplianceWorld : IAsyncDisposable
{
    public const string Today = "2026-10-24";
    public static readonly DateOnly TodayDate = new(2026, 10, 24);

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

    /// <summary>08 Production/database/migrations/&lt;file&gt;.</summary>
    public static string MigrationText(string file)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "database", "migrations", file);
            if (File.Exists(f)) return File.ReadAllText(f);
            f = Path.Combine(d.FullName, "08 Production", "database", "migrations", file);
            if (File.Exists(f)) return File.ReadAllText(f);
        }

        throw new FileNotFoundException(file + " not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Runs migration 4131 again (it already ran on the empty database): carries the W05-C rows inserted since onto SCC's cards.</summary>
    public Task RunLegacyMigrationAsync(CancellationToken ct = default) => Db.ExecAsync(MigrationText("4131_scc_tax_compliance_data.sql"), ct);

    public static async Task<ComplianceWorld> EmptyAsync(CancellationToken ct = default)
    {
        var db = await ComplianceDatabase.CreateAsync(ct);
        try
        {
            var p = Convert.ToInt32(await db.ScalarAsync($"INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '{Today}');", ct), CultureInfo.InvariantCulture);
            return new ComplianceWorld(db, p);
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    /// <summary>A W05-C obligation as it was stored (kind, latest rule); returns its id.</summary>
    public async Task<long> LegacyObligationAsync(string kind, string name, string freq, string ruleType, int? day, int? month, bool active = true, CancellationToken ct = default) =>
        Convert.ToInt64(await Db.ScalarAsync($"""
            DECLARE @id TABLE(i BIGINT);
            INSERT cmp.obligation(property_id, kind, name, form, frequency, owner_role, from_period, active, note, updated_by, updated_utc)
              OUTPUT INSERTED.obligation_id INTO @id VALUES ({PropertyId}, '{kind}', N'{name}', N'Portal', '{freq}', N'Accountant', '2026-04', {(active ? 1 : 0)}, NULL, 1, '2026-04-01T03:30:00');
            INSERT cmp.obligation_rule(obligation_id, effective_from, rule_type, due_day, due_month) SELECT i, '2000-01-01', '{ruleType}', {(day?.ToString(CultureInfo.InvariantCulture) ?? "NULL")}, {(month?.ToString(CultureInfo.InvariantCulture) ?? "NULL")} FROM @id;
            SELECT i FROM @id;
            """, ct), CultureInfo.InvariantCulture);

    /// <summary>A W05-C filing (recorded at 11:00 IST the given day).</summary>
    public Task LegacyFilingAsync(long obligationId, string period, string status, int by, DateOnly onIst, string? reference, string? reason, CancellationToken ct = default) =>
        Db.ExecAsync($"""
            INSERT cmp.filing(obligation_id, period, status, filed_by, filed_utc, reference, reason)
            VALUES ({obligationId}, '{period}', '{status}', {by}, '{onIst:yyyy-MM-dd}T05:30:00', {(reference is null ? "NULL" : "N'" + reference + "'")}, {(reason is null ? "NULL" : "N'" + reason + "'")});
            """, ct);

    /// <summary>The prototype BASE compliance (fixtures/ext/compliance.ts).</summary>
    public static async Task<ComplianceWorld> BaseAsync(CancellationToken ct = default)
    {
        var w = await EmptyAsync(ct);
        try
        {
            // W05-C: the seven obligations, Apr–Sep 2026 (GSTR-9: FY 2025-26) filed the day before the due date; GSTR-1 Sep, PT Sep and GSTR-9 open;
            // ESIC Apr and May not applicable.
            var obs = new (string Kind, string Name, string Freq, string Rule, int? Day, int? Month)[]
            {
                ("TDS", "TDS deposit", "Monthly", "day", 7, null), ("GSTR-1", "GSTR-1 · outward supplies", "Monthly", "day", 11, null),
                ("GSTR-3B", "GSTR-3B · summary return and tax", "Monthly", "day", 20, null), ("GSTR-9", "GSTR-9 · annual return", "Annual", "fixed", 31, 12),
                ("EPF", "EPF contribution", "Monthly", "day", 15, null), ("ESIC", "ESIC contribution", "Monthly", "day", 15, null),
                ("PT", "Professional Tax (Maharashtra)", "Monthly", "month-end", null, null),
            };
            var open = new HashSet<string> { "GSTR-1:2026-09", "PT:2026-09", "GSTR-9:2025-26" };
            var n = 0;
            foreach (var o in obs)
            {
                var id = await w.LegacyObligationAsync(o.Kind, o.Name, o.Freq, o.Rule, o.Day, o.Month, true, ct);
                var periods = o.Freq == "Annual" ? new[] { "2025-26" } : ["2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09"];
                foreach (var p in periods)
                {
                    if (open.Contains(o.Kind + ":" + p)) continue;
                    var (fy, item) = ComplianceRules.LegacyCard(o.Kind, p)!.Value;
                    var on = SccTaxRules.ParseDue(SccTaxCatalogue.All().Single(c => c.Card.Id == item).Card.Due, fy)!.Value.AddDays(-1);
                    if (o.Kind == "ESIC" && p is "2026-04" or "2026-05")
                        await w.LegacyFilingAsync(id, p, "Not applicable", Users.Accountant, on, null, "Staff count below the ESIC threshold until June (illustrative)", ct);
                    else
                        await w.LegacyFilingAsync(id, p, "Filed", Users.Accountant, on, $"{o.Kind}-{p.Replace("-", "", StringComparison.Ordinal)}-{(n * 7919 % 9000 + 1000).ToString(CultureInfo.InvariantCulture)}", null, ct);
                    n++;
                }
            }

            await w.RunLegacyMigrationAsync(ct);
            await w.Db.ExecAsync($"UPDATE cmp.profile SET pan = N'ABCDE1234F', gstin = N'27ABCDE1234F1Z5', entity = 'partnership', notes = N'Fictional demo firm profile (one per property).' WHERE property_id = {w.PropertyId};", ct);
            // Every other FY 2026–27 card due before the business date: filed the day before; QRMP cards NA; GSTR-1 September (oc4) open.
            var snap = await w.Store.SnapshotAsync(w.PropertyId, ct);
            var cal = snap.Calendar();
            foreach (var m in SccTaxCatalogue.Months)
                foreach (var c in cal.MonthCards(m))
                {
                    var due = SccTaxRules.ParseDue(c.Due, 2026);
                    if (due is not { } d || d >= TodayDate || c.Id == "oc4" || cal.Record(2026, c.Id) is not null) continue;
                    if (SccTaxRules.GstFilingMode(c) == "qrmp")
                    {
                        await w.Db.ExecAsync($"INSERT cmp.record(property_id, fy_start, item_id, done, na, notes, na_by, na_utc) VALUES ({w.PropertyId}, 2026, '{c.Id}', 0, 1, N'Monthly filer — the QRMP quarterly return does not apply.', {Users.Accountant}, '{d:yyyy-MM-dd}T04:30:00');", ct);
                        continue;
                    }

                    var filed = d.AddDays(-1);
                    var ev = c.Id is "jn4" or "jl11" ? string.Empty : $"ACK-{c.Id.ToUpperInvariant()}-{filed:yyyyMMdd}";
                    await w.Db.ExecAsync($"""
                        INSERT cmp.record(property_id, fy_start, item_id, done, na, filed_on, notes, evidence_ref, reviewer_note, due_date, done_by, done_utc)
                        VALUES ({w.PropertyId}, 2026, '{c.Id}', 1, 0, '{filed:yyyy-MM-dd}', N'', N'{ev}', N'Owner', '{d:yyyy-MM-dd}', {Users.Accountant}, '{filed:yyyy-MM-dd}T05:30:00');
                        """, ct);
                }

            await w.Db.ExecAsync($"""
                DECLARE @id TABLE(i BIGINT);
                INSERT cmp.evidence(property_id, fy_start, item_id, file_name, content_type, size_bytes, added_by, added_utc) OUTPUT INSERTED.evidence_id INTO @id
                VALUES ({w.PropertyId}, 2026, 'oc1', N'challan-281-sep-2026.txt', 'text/plain', 80, {Users.Accountant}, '2026-10-06T05:35:00');
                INSERT cmp.evidence_content(evidence_id, content) SELECT i, 0x4368616C6C616E20323831 FROM @id;
                """, ct);
            return w;
        }
        catch
        {
            await w.DisposeAsync();
            throw;
        }
    }

    public async Task<CmpRecord?> RecordAsync(string itemId, int fy = 2026, CancellationToken ct = default) =>
        (await Store.SnapshotAsync(PropertyId, ct)).Records.SingleOrDefault(r => r.FyStart == fy && r.ItemId == itemId);
}
