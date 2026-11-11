// FX-RPT-D · rpt.stock.counts on a disposable HccTest_* database: only the lines that differ, variance value on the first line of a count (negative allowed), "No variance" line,
// newest date first, counted-by name, location label, value removed without PERM-STK-004, rule V / M through the real access policy, screen = Excel = PDF = print.
using System.Globalization;
using Hcc.Contracts;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Reporting.Reports.FxD;
using Hcc.Reporting.Tests.CuB;
using Hcc.Reporting.Tests.Sql;
using static Hcc.Reporting.Tests.CuB.CuBRig;
using static Hcc.Reporting.Tests.FxD.FxDRig;

namespace Hcc.Reporting.Tests.FxD;

[Trait("Category", "Sql")]
public sealed class StockCountsReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;

    public async ValueTask InitializeAsync()
    {
        await Gate.WaitAsync(Ct);
        try
        {
            if (_seeded) return;
            await AddSupervisor(db);
            await db.ScalarAsync(ItemsSql(db) + $"""
                DECLARE @o INT = {db.Users["OWN"]}, @m INT = {db.Users["MGR"]}, @a INT = {db.Users["ACC"]};
                INSERT stk.count(property_id, business_date, at_utc, loc, status, actor_id, reviewer_id) VALUES (@p, '2026-10-20', '2026-10-20T10:00:00', 'store', 'Posted', @o, @m);
                INSERT stk.count(property_id, business_date, at_utc, loc, status, actor_id) VALUES (@p, '2026-10-23', '2026-10-23T10:00:00', 'floor1', 'Submitted', @m);
                INSERT stk.count(property_id, business_date, at_utc, loc, status, actor_id, reviewer_id, reason) VALUES (@p, '2026-10-23', '2026-10-23T11:00:00', 'store', 'Rejected', @a, @o, N'Recount');
                DECLARE @c1 BIGINT = (SELECT MIN(count_id) FROM stk.count), @c2 BIGINT = (SELECT MIN(count_id) + 1 FROM stk.count), @c3 BIGINT = (SELECT MAX(count_id) FROM stk.count);
                INSERT stk.count_line(count_id, item_id, system_qty, counted_qty) VALUES
                  (@c1, {Item("L-BED")}, 10, 8), (@c1, {Item("A-SOAP")}, 60, 65), (@c1, {Item("C-FLOOR")}, 10, 10), (@c2, {Item("L-TOW")}, 4, 4), (@c3, {Item("A-ZETA")}, 3, 1);
                SELECT 1;
                """);
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ReportRunner Runner() => CuBRig.Runner(db, new StockCountsReport(Store(db), Names(db)));

    private async Task<string> CountRef(string loc, string date) =>
        "CNT-" + Convert.ToInt64(await db.ScalarAsync($"SELECT count_id FROM stk.count WHERE loc = '{loc}' AND business_date = '{date}';"), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    // Count 1 (20 Oct, store, posted, by OWN): Soap +5, Bedsheet -2, Floor cleaner unchanged (left out); value 5 x 12.50 - 2 x 450.00 = -837.50 on the first line (Soap, code order).
    // Count 2 (23 Oct, floor 1, submitted, by MGR): no difference -> one "No variance" line, value 0. Count 3 (23 Oct, store, rejected, by ACC, later id -> before count 2): Zeta -2 -> -10.00.
    [Fact]
    public async Task Only_differing_lines_are_listed_with_the_variance_value_on_the_first_line_newest_first()
    {
        var r = await Run(db, Runner(), "OWN", StockCountsReport.Id);
        var d = Data(r);
        Assert.Equal(4, r.Rows.Count);
        Assert.Equal([await CountRef("store", "2026-10-23"), await CountRef("floor1", "2026-10-23"), await CountRef("store", "2026-10-20"), await CountRef("store", "2026-10-20")], d.Select(x => S(x, "count")));
        Assert.Equal(("Zeta wipes", "Rejected", "Main store", 3L, 1L, -2L, -1000L, "ACC"), (S(d[0], "item"), S(d[0], "status"), S(d[0], "location"), L(d[0], "system"), L(d[0], "counted"), L(d[0], "variance"), L(d[0], "value"), S(d[0], "by")));
        Assert.Equal(("No variance", "Floor 1 pantry", 0L, "MGR"), (S(d[1], "item"), S(d[1], "location"), L(d[1], "value"), S(d[1], "by")));
        Assert.All(new[] { "system", "counted", "variance" }, k => Assert.Null(d[1].Cells[k]));
        Assert.Equal(("2026-10-20", "Posted", "Soap bar", 60L, 65L, 5L, -83750L, "OWN"), (S(d[2], "date"), S(d[2], "status"), S(d[2], "item"), L(d[2], "system"), L(d[2], "counted"), L(d[2], "variance"), L(d[2], "value"), S(d[2], "by")));
        Assert.Equal(("Bedsheet", -2L), (S(d[3], "item"), L(d[3], "variance")));
        Assert.Null(d[3].Cells["value"]);
        Assert.DoesNotContain(d, x => S(x, "item") == "Floor cleaner");
    }

    [Fact]
    public async Task Money_gate_holders_see_the_variance_value_the_view_only_role_does_not()
    {
        var runner = Runner();
        foreach (var role in new[] { "MGR", "ACC" })
            Assert.Contains((await Run(db, runner, role, StockCountsReport.Id)).Columns, c => c.Key == "value");
        var sup = await Run(db, runner, "SUP", StockCountsReport.Id);
        Assert.DoesNotContain(sup.Columns, c => c.Key == "value");
        Assert.Equal(4, Data(sup).Count);
    }

    [Fact]
    public async Task Roles_without_the_stock_view_right_are_refused_and_the_hub_refuses_the_no_money_roles()
    {
        var runner = Runner();
        foreach (var role in new[] { "REC", "CSH", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(db, runner, role, StockCountsReport.Id)));
        var svc = Service(db, new StockCountsReport(Store(db), Names(db)));
        var none = new Dictionary<string, string?>();
        foreach (var role in new[] { "HKP", "SUP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => svc.RunAsync(new Caller(db.PropertyId, db.Users[role]), StockCountsReport.Id, none, null, 50, true, Ct)));
    }
}
