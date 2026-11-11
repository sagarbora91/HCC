// CU-RPT-B · shared test rig: real SqlAccessPolicy + SqlReportStores over the disposable HccTest_* database, seeded users, the Payment methods master.
using System.Globalization;
using Hcc.Contracts;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Reporting;
using Hcc.Reporting.Sql;
using Hcc.Reporting.Tests.Sql;

namespace Hcc.Reporting.Tests.CuB;

internal static class CuBRig
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static ReportRunner Runner(ReportDatabase db, params IReportSelector[] selectors)
    {
        var clock = new FixedClock(Rig.Now);
        return new ReportRunner(new ReportCatalogue(selectors), new SqlAccessPolicy(db.Platform.Connections, clock, []), new SqlReportStores(db.Platform.Connections), [new FixedCodeEngine()], clock);
    }

    private sealed class NoDelivery : IReportDelivery
    {
        public Task<bool> EnqueueAsync(int propertyId, long runId, IReadOnlyList<int> recipients, string fileRef, CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>The hub service (DEC-061 guard, rule V) over the real policy, stores and role reader.</summary>
    public static ReportService Service(ReportDatabase db, params IReportSelector[] selectors)
    {
        var clock = new FixedClock(Rig.Now);
        var policy = new SqlAccessPolicy(db.Platform.Connections, clock, []);
        var stores = new SqlReportStores(db.Platform.Connections);
        var cat = new ReportCatalogue(selectors);
        var runner = new ReportRunner(cat, policy, stores, [new FixedCodeEngine()], clock);
        return new ReportService(runner, cat, [new XlsxRenderer(), new PdfRenderer(), new CsvRenderer(), new PrintHtmlRenderer()], stores, stores, stores, stores, stores,
            new SqlReportPrincipalRoles(db.Platform.Connections, clock), policy, new ReferenceOnlyFileStore(), new NoDelivery(), clock, stores);
    }

    public static async Task<ReportResult> Run(ReportDatabase db, ReportRunner runner, string role, string id, params (string K, string V)[] raw)
    {
        var d = raw.ToDictionary(x => x.K, x => (string?)x.V);
        var r = await runner.RunAsync(new Caller(db.PropertyId, db.Users[role]), id, d, new RunOptions(All: true), Ct);
        RendererTests.ThreeWay(r);
        return r;
    }

    public static async Task<HccErrorCode?> Code(Func<Task> f)
    {
        try { await f(); return null; }
        catch (HccException e) { return e.Code; }
    }

    public static List<ReportRow> Data(ReportTable t) => t.Rows.Where(x => x.Kind == ReportRowKind.Row).ToList();

    public static long L(ReportRow r, string k) => Convert.ToInt64(r.Cells[k], CultureInfo.InvariantCulture);

    public static string S(ReportRow r, string k) => r.Cells[k] is DateOnly d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : Convert.ToString(r.Cells[k], CultureInfo.InvariantCulture) ?? "";

    public static async Task AddUser(ReportDatabase db, string role)
    {
        var id = Convert.ToInt32(await db.ScalarAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({db.PropertyId}, '{role.ToLowerInvariant()}', N'{role}', '2026-01-01');"), CultureInfo.InvariantCulture);
        await db.ScalarAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({id}, '{role}', '2026-01-01', '2026-01-01');");
        db.Users[role] = id;
    }

    /// <summary>Master: Cash (till), UPI, Card, Bank transfer at the desk; Cash, UPI, Card at the POS; "Old wallet" retired.</summary>
    public static Task SeedMethods(ReportDatabase db) => db.ScalarAsync($"""
        DECLARE @p INT = {db.PropertyId}, @u INT = {db.Users["OWN"]};
        INSERT mst.bank_account(property_id, code, name, sort_order, kind, updated_by, updated_utc) VALUES (@p, 'TILL', N'Front desk till', 1, 'till', @u, '2026-01-01');
        INSERT mst.bank_account(property_id, code, name, sort_order, kind, bank, account_last4, updated_by, updated_utc) VALUES (@p, 'BANK', N'Current account', 2, 'bank', N'Demo Bank', '1234', @u, '2026-01-01');
        DECLARE @t INT = (SELECT bank_account_id FROM mst.bank_account WHERE code = 'TILL'), @b INT = (SELECT bank_account_id FROM mst.bank_account WHERE code = 'BANK');
        INSERT mst.payment_method(property_id, code, name, sort_order, kind, settles_to_id, usable_frontdesk, usable_pos, usable_expenses, updated_by, updated_utc) VALUES
          (@p, 'CASH', N'Cash', 1, 'cash', @t, 1, 1, 1, @u, '2026-01-01'), (@p, 'UPI', N'UPI', 2, 'upi', @b, 1, 1, 1, @u, '2026-01-01'),
          (@p, 'CARD', N'Card', 3, 'card', @b, 1, 1, 0, @u, '2026-01-01'), (@p, 'BANKTR', N'Bank transfer', 4, 'bank', @b, 1, 0, 1, @u, '2026-01-01');
        INSERT mst.payment_method(property_id, code, name, sort_order, active, retired_on, kind, settles_to_id, usable_frontdesk, usable_pos, updated_by, updated_utc)
          VALUES (@p, 'OLDW', N'Old wallet', 5, 0, '2026-06-01', 'upi', @b, 1, 1, @u, '2026-01-01');
        SELECT 1;
        """);

    /// <summary>One floor, a Classic type and rooms 101..(100+count); returns nothing, rooms found by number afterwards.</summary>
    public static string RoomsSql(ReportDatabase db, params string[] roomNos)
    {
        var values = string.Join(", ", roomNos.Select(n => $"(@p, '{n}', @t, 'Ready', @fl)"));
        return $"""
            DECLARE @p INT = {db.PropertyId};
            INSERT mst.floor(property_id, code, name, sort_order, updated_by, updated_utc) VALUES (@p, 'F1', N'Floor 1', 1, {db.Users["OWN"]}, '2026-01-01');
            DECLARE @fl INT = SCOPE_IDENTITY();
            INSERT inv.room_type(property_id, code, max_occupancy, name) VALUES (@p, 'CLS', 3, N'Classic');
            DECLARE @t INT = (SELECT room_type_id FROM inv.room_type WHERE property_id = @p AND code = 'CLS');
            INSERT inv.room(property_id, room_no, room_type_id, hk_status, floor_id) VALUES {values};
            """;
    }
}
