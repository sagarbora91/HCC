// CU-RPT-B · rpt.stock.stock-sheet on a disposable HccTest_* database: pieces per location, totals, unit cost and value to the paise, rule V / M through the real access
// policy (PERM-STK-001 views quantities; cost and value need PERM-STK-004 and are removed without it), screen = Excel = PDF = print.
using Hcc.Contracts;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Stock;
using Hcc.Reporting.Reports.CuB;
using Hcc.Reporting.Tests.Sql;
using static Hcc.Reporting.Tests.CuB.CuBRig;

namespace Hcc.Reporting.Tests.CuB;

[Trait("Category", "Sql")]
public sealed class StockSheetReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;

    public async ValueTask InitializeAsync()
    {
        await Gate.WaitAsync(Ct);
        try
        {
            if (_seeded) return;
            await AddUser(db, "SUP");
            await Seed();
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // A-SOAP amenity 12.50: store 200, floor 2 30 = 230 -> 2,875.00. C-FLOOR cleaning 80.00: store 10 -> 800.00. L-BED linen 450.00: store 40, floor 1 10, rooms 20, laundry 5 = 75 -> 33,750.00.
    // Total value 37,425.00 = 3,742,500 paise.
    private async Task Seed() => await db.ScalarAsync($"""
        DECLARE @p INT = {db.PropertyId};
        INSERT stk.item(property_id, code, name, kind, unit, cost_paise, min_qty, reorder_qty, created_utc) VALUES
          (@p, 'L-BED', N'Bedsheet', 'linen', 'pcs', 45000, 20, 40, '2026-01-01'), (@p, 'A-SOAP', N'Soap bar', 'amenity', 'pcs', 1250, 100, 200, '2026-01-01'),
          (@p, 'C-FLOOR', N'Floor cleaner', 'cleaning', 'litre', 8000, 5, 10, '2026-01-01');
        INSERT stk.stock(item_id, loc, qty) SELECT item_id, x.loc, x.qty FROM stk.item i JOIN (VALUES
          ('L-BED', 'store', 40), ('L-BED', 'floor1', 10), ('L-BED', 'rooms', 20), ('L-BED', 'laundry', 5), ('A-SOAP', 'store', 200), ('A-SOAP', 'floor2', 30), ('C-FLOOR', 'store', 10)) x(code, loc, qty) ON x.code = i.code;
        SELECT 1;
        """);

    private ReportRunner Runner() => CuBRig.Runner(db, new StockSheetReport(new SqlStockReadStore(db.Platform.Connections)));

    [Fact]
    public async Task Pieces_totals_cost_and_value_are_exact_to_the_paise()
    {
        var r = await Run(db, Runner(), "OWN", StockSheetReport.Id);
        Assert.Equal(["item", "kind", "unit", "store", "floor1", "floor2", "floor3", "rooms", "soiled", "laundry", "total", "cost", "value"], r.Columns.Select(c => c.Key));
        var d = r.Rows.Where(x => x.Kind == ReportRowKind.Row).ToList();
        Assert.Equal(["Soap bar", "Floor cleaner", "Bedsheet"], d.Select(x => S(x, "item"))); // code order A-SOAP, C-FLOOR, L-BED
        Assert.Equal(("Amenities", "pcs", 200L, 0L, 30L, 0L, 230L, 1250L, 287500L), (S(d[0], "kind"), S(d[0], "unit"), L(d[0], "store"), L(d[0], "floor1"), L(d[0], "floor2"), L(d[0], "rooms"), L(d[0], "total"), L(d[0], "cost"), L(d[0], "value")));
        Assert.Equal(("Cleaning supplies", "litre", 10L, 10L, 8000L, 80000L), (S(d[1], "kind"), S(d[1], "unit"), L(d[1], "store"), L(d[1], "total"), L(d[1], "cost"), L(d[1], "value")));
        Assert.Equal(("Linen", 40L, 10L, 20L, 5L, 0L, 75L, 3375000L), (S(d[2], "kind"), L(d[2], "store"), L(d[2], "floor1"), L(d[2], "rooms"), L(d[2], "laundry"), L(d[2], "soiled"), L(d[2], "total"), L(d[2], "value")));
        var total = r.Rows.Single(x => x.Kind == ReportRowKind.Total);
        Assert.Equal(("Total value", 3742500L), (S(total, "item"), L(total, "value")));
        Assert.Equal(d.Sum(x => L(x, "value")), L(total, "value"));
    }

    [Fact]
    public async Task Money_gate_holders_see_cost_and_value_the_view_only_role_sees_pieces_only()
    {
        var runner = Runner();
        foreach (var role in new[] { "MGR", "ACC" })
            Assert.Contains((await Run(db, runner, role, StockSheetReport.Id)).Columns, c => c.Key == "value");
        // SUP holds PERM-STK-001 but not PERM-STK-004 (and is a no-money role, DEC-061): the engine removes cost and value (rule M)
        var sup = await Run(db, runner, "SUP", StockSheetReport.Id);
        Assert.DoesNotContain(sup.Columns, c => c.Key is "cost" or "value");
        Assert.Contains(sup.Columns, c => c.Key == "total");
        Assert.Equal(["Soap bar", "Floor cleaner", "Bedsheet"], sup.Rows.Where(x => x.Kind == ReportRowKind.Row).Select(x => S(x, "item")));
        Assert.Contains("cost", sup.Removed!);
        Assert.False(await runner.HoldsMoneyGateAsync(new Caller(db.PropertyId, db.Users["SUP"]), new StockSheetReport(null!).Definition, Ct));
    }

    [Fact]
    public async Task Roles_without_the_stock_view_right_or_only_with_assigned_scope_are_refused()
    {
        var runner = Runner();
        foreach (var role in new[] { "REC", "CSH", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(db, runner, role, StockSheetReport.Id)));
    }
}
