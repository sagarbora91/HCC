// FX-RPT-D · rpt.stock.reorder on a disposable HccTest_* database: items below the minimum (clean = store + floor pantries), suggested quantity, order (most short first, then name),
// unit cost to the paise and removed without PERM-STK-004, rule V / M through the real access policy, DEC-061 at the hub, screen = Excel = PDF = print.
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
public sealed class StockReorderReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
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
            await db.ScalarAsync(ItemsSql(db) + "SELECT 1;");
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ReportRunner Runner() => CuBRig.Runner(db, new StockReorderReport(Store(db)));

    // Clean: Soap 60+10 = 70 (min 100, short 30, suggest max(200, 200-70) = 200); Bedsheet 5+4 = 9 (rooms and laundry do not count; min 20, short 11, suggest max(40, 31) = 40);
    // Towel 19 (soiled does not count; min 30, short 11, suggest max(25, 41) = 41); Zeta 0 (min 10, short 10, suggest max(5, 20) = 20). Floor cleaner 10 >= 5 is not listed.
    [Fact]
    public async Task Items_below_the_minimum_are_listed_most_short_first_then_by_name_with_the_suggested_order()
    {
        var r = await Run(db, Runner(), "OWN", StockReorderReport.Id);
        var d = Data(r);
        Assert.Equal(["Soap bar", "Bedsheet", "Towel", "Zeta wipes"], d.Select(x => S(x, "item")));
        Assert.Equal(("Amenities", 70L, 100L, 200L, 1250L), (S(d[0], "kind"), L(d[0], "clean"), L(d[0], "min"), L(d[0], "suggest"), L(d[0], "cost")));
        Assert.Equal(("Linen", 9L, 20L, 40L, 45000L), (S(d[1], "kind"), L(d[1], "clean"), L(d[1], "min"), L(d[1], "suggest"), L(d[1], "cost")));
        Assert.Equal((19L, 30L, 41L, 20000L), (L(d[2], "clean"), L(d[2], "min"), L(d[2], "suggest"), L(d[2], "cost")));
        Assert.Equal((0L, 10L, 20L, 500L), (L(d[3], "clean"), L(d[3], "min"), L(d[3], "suggest"), L(d[3], "cost")));
        Assert.DoesNotContain(r.Rows, x => S(x, "item") == "Floor cleaner");
        Assert.DoesNotContain(r.Rows, x => x.Kind == ReportRowKind.Total);
    }

    [Fact]
    public async Task Money_gate_holders_see_the_unit_cost_the_view_only_role_does_not()
    {
        var runner = Runner();
        foreach (var role in new[] { "MGR", "ACC" })
            Assert.Contains((await Run(db, runner, role, StockReorderReport.Id)).Columns, c => c.Key == "cost");
        var sup = await Run(db, runner, "SUP", StockReorderReport.Id);
        Assert.DoesNotContain(sup.Columns, c => c.Key == "cost");
        Assert.Equal(4, Data(sup).Count);
        Assert.Contains("cost", sup.Removed!);
    }

    [Fact]
    public async Task Roles_without_the_stock_view_right_are_refused_and_the_hub_refuses_the_no_money_roles()
    {
        var runner = Runner();
        foreach (var role in new[] { "REC", "CSH", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(db, runner, role, StockReorderReport.Id)));
        var svc = Service(db, new StockReorderReport(Store(db)));
        var none = new Dictionary<string, string?>();
        foreach (var role in new[] { "HKP", "SUP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => svc.RunAsync(new Caller(db.PropertyId, db.Users[role]), StockReorderReport.Id, none, null, 50, true, Ct)));
    }

    [Fact]
    public async Task A_property_with_no_items_gives_an_empty_report()
    {
        var r = await new StockReorderReport(Store(db)).SelectAsync(new Caller(db.PropertyId + 1000, 1), new BoundParams(new Dictionary<string, string>()), Ct);
        Assert.Empty(r.Rows);
    }
}
