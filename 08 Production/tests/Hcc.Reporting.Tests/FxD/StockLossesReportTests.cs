// FX-RPT-D · rpt.stock.losses on a disposable HccTest_* database: range filter on the business date, oldest first, value (paise) and quantity totals, no total row on an empty range,
// value removed without PERM-STK-004, rule V / M through the real access policy, DEC-061 at the hub, screen = Excel = PDF = print.
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
public sealed class StockLossesReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
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
                INSERT stk.loss(property_id, business_date, item_id, loc, qty, reason, actor_id, value_paise, created_utc) VALUES
                  (@p, '2026-10-21', {Item("L-BED")}, 'store', 2, N'Damaged', @m, 90000, '2026-10-21T05:00:00'),
                  (@p, '2026-10-21', {Item("A-SOAP")}, 'rooms', 10, N'Missing', @o, 12500, '2026-10-21T06:00:00'),
                  (@p, '2026-10-23', {Item("L-TOW")}, 'soiled', 3, N'Stained beyond use', @a, 60000, '2026-10-23T06:00:00'),
                  (@p, '2026-10-10', {Item("L-BED")}, 'store', 1, N'Damaged', @m, 45000, '2026-10-10T06:00:00');
                SELECT 1;
                """);
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ReportRunner Runner() => CuBRig.Runner(db, new StockLossesReport(Store(db), Names(db)));

    private static (string, string)[] Range(string from, string to) => [("from", from), ("to", to)];

    // 20-24 Oct: two losses on 21 Oct (in entry order), one on 23 Oct; the loss of 10 Oct is outside. Quantity 2 + 10 + 3 = 15; value 900.00 + 125.00 + 600.00 = 1,625.00.
    [Fact]
    public async Task Losses_in_the_range_come_oldest_first_with_exact_value_and_a_total_row()
    {
        var r = await Run(db, Runner(), "OWN", StockLossesReport.Id, Range("2026-10-20", "2026-10-24"));
        var d = Data(r);
        Assert.Equal(["Bedsheet", "Soap bar", "Towel"], d.Select(x => S(x, "item")));
        Assert.Equal(("2026-10-21", "Main store", 2L, "Damaged", 90000L, "MGR"), (S(d[0], "date"), S(d[0], "location"), L(d[0], "qty"), S(d[0], "reason"), L(d[0], "value"), S(d[0], "by")));
        Assert.Equal(("In rooms", 10L, 12500L, "OWN"), (S(d[1], "location"), L(d[1], "qty"), L(d[1], "value"), S(d[1], "by")));
        Assert.Equal(("2026-10-23", "Soiled (to send)", "Stained beyond use", "ACC"), (S(d[2], "date"), S(d[2], "location"), S(d[2], "reason"), S(d[2], "by")));
        var total = r.Rows.Single(x => x.Kind == ReportRowKind.Total);
        Assert.Equal(("Total", 15L, 162500L), (S(total, "date"), L(total, "qty"), L(total, "value")));
        Assert.Equal(d.Sum(x => L(x, "value")), L(total, "value"));
    }

    [Fact]
    public async Task A_wider_range_includes_the_old_loss_and_an_empty_range_has_no_total_row()
    {
        var wide = await Run(db, Runner(), "OWN", StockLossesReport.Id, Range("2026-10-01", "2026-10-24"));
        Assert.Equal(4, Data(wide).Count);
        Assert.Equal(("2026-10-10", 207500L), (S(Data(wide)[0], "date"), L(wide.Rows.Single(x => x.Kind == ReportRowKind.Total), "value")));
        var none = await Run(db, Runner(), "OWN", StockLossesReport.Id, Range("2026-10-01", "2026-10-05"));
        Assert.Empty(none.Rows);
    }

    [Fact]
    public async Task Money_gate_holders_see_the_value_the_view_only_role_does_not()
    {
        var runner = Runner();
        foreach (var role in new[] { "MGR", "ACC" })
            Assert.Contains((await Run(db, runner, role, StockLossesReport.Id, Range("2026-10-20", "2026-10-24"))).Columns, c => c.Key == "value");
        var sup = await Run(db, runner, "SUP", StockLossesReport.Id, Range("2026-10-20", "2026-10-24"));
        Assert.DoesNotContain(sup.Columns, c => c.Key == "value");
        Assert.Equal(3, Data(sup).Count);
    }

    [Fact]
    public async Task Roles_without_the_stock_view_right_are_refused_and_the_hub_refuses_the_no_money_roles()
    {
        var runner = Runner();
        foreach (var role in new[] { "REC", "CSH", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(db, runner, role, StockLossesReport.Id, Range("2026-10-20", "2026-10-24"))));
        var svc = Service(db, new StockLossesReport(Store(db), Names(db)));
        var p = new Dictionary<string, string?> { ["from"] = "2026-10-20", ["to"] = "2026-10-24" };
        foreach (var role in new[] { "HKP", "SUP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => svc.RunAsync(new Caller(db.PropertyId, db.Users[role]), StockLossesReport.Id, p, null, 50, true, Ct)));
    }
}
