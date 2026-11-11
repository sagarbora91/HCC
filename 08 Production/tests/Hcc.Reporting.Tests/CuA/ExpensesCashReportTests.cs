// CU-RPT-A · rpt.money.expenses-cash on the server: drawer figures per business date (cash book arithmetic) and expenses split by what they settle to (payment-method master),
// the seven range tiles to the paise, capital and unposted expenses left out, refusals, DEC-061 guard of the hub. Disposable HccTest_* database. Fictional data.
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Contracts.Closing;
using Hcc.Domain.Errors;
using Hcc.Domain.Expenses;
using Hcc.Reporting.Reports.CuA;
using Hcc.Reporting.Tests.Sql;

namespace Hcc.Reporting.Tests.CuA;

[Trait("Category", "Sql")]
public sealed class ExpensesCashReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private readonly FakePolicy _policy = new();

    public async ValueTask InitializeAsync()
    {
        await CuARig.SeedCash(db);
        await CuARig.SeedExpenses(db);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Day_rows_total_and_tiles_to_the_paise()
    {
        var r = await CuARig.Run(db, _policy, db.Users["OWN"], ExpensesCashReport.Id, ("from", "2026-10-21"), ("to", "2026-10-24"));
        var d = CuARig.Data(r);
        Assert.Equal(["2026-10-21", "2026-10-22", "2026-10-23", "2026-10-24"], d.Select(x => CuARig.S(x, "date")));
        Assert.Equal(["—", "Closed", "Closed", "Open"], d.Select(x => CuARig.S(x, "day")));
        Assert.Null(d[0].Cells["opening"]);
        Assert.Equal([1000000L, 1395000, 1245000], d.Skip(1).Select(x => CuARig.L(x, "opening")));
        Assert.Equal([1400000L, 1245000, 1245000], d.Skip(1).Select(x => CuARig.L(x, "expected")));
        Assert.Equal([1395000L, 1245000], d.Skip(1).Take(2).Select(x => CuARig.L(x, "counted")));
        Assert.Null(d[3].Cells["counted"]);
        Assert.Equal([-5000L, 0], d.Skip(1).Take(2).Select(x => CuARig.L(x, "difference")));
        Assert.Equal([0L, 130000, -30000, 0], d.Select(x => CuARig.L(x, "cashExpenses")));
        var t = Assert.Single(r.Rows, x => x.Kind == ReportRowKind.Total);
        Assert.Equal(("Total", 100000L), (CuARig.S(t, "date"), CuARig.L(t, "cashExpenses")));
        var tiles = Assert.Single(Hcc.Reporting.ReportRunner.Tables(r), x => x.Key == "tiles").Rows;
        Assert.Equal(
            [("Drawer-cash expenses", 100000L), ("UPI / bank expenses", 450000), ("Vendor-credit expenses (no drawer effect)", 1010000), ("Reversals included above", -30000),
             ("Posted expenses (net of reversals)", 1560000), ("Cash into drawer (payments + deposits + POS sales)", 700000), ("Cash out of drawer (refunds + returns + POS voids)", -50000)],
            tiles.Select(x => (CuARig.S(x, "tile"), CuARig.L(x, "amount"))));
    }

    [Fact]
    public void Capital_and_unposted_expenses_stay_out_and_an_unknown_method_counts_as_bank()
    {
        static LedgerRow L(long id, string method, long amt, string status, bool capital) =>
            new(id, new DateOnly(2026, 10, 22), "C", method, "V", null, amt, null, null, status, null, "x", null, null, false, DateTime.UtcNow, null, false, capital);
        var view = new CashBookRangeView(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 22), [], 0, 0, 0, 0, 0, 0, 0, "");
        var (days, tiles) = ExpensesCashReport.Blocks(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 22), view,
            [L(1, "Cash", 1000, "Posted", false), L(2, "Cash", 5000, "Posted", true), L(3, "Cash", 700, "Rejected", false), L(4, "Mystery", 200, "Posted", false), L(5, "UPI", 300, "Posted", false)], null, []);
        Assert.Equal(1000L, CuARig.L(days[0], "cashExpenses"));
        Assert.Equal("—", CuARig.S(days[0], "day"));
        Assert.Equal([1000L, 500, 0, 0, 1500, 0, 0], tiles.Select(x => CuARig.L(x, "amount")));
    }

    [Fact]
    public async Task Cashier_reception_and_housekeeping_are_refused_and_the_hub_enforces_DEC_061()
    {
        foreach (var who in new[] { db.Users["CSH"], db.Users["REC"], 7 })
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => CuARig.Run(db, _policy, who, ExpensesCashReport.Id, ("from", "2026-10-22"), ("to", "2026-10-22")))).Code);
        foreach (var perm in new[] { "PERM-RPT-001", "PERM-RPT-002", "PERM-RPT-003" }) _policy.Grants[perm].Add("HKP");
        var s = CuARig.Service(db, _policy);
        Assert.DoesNotContain(await s.CatalogueAsync(Rig.C(7), TestContext.Current.CancellationToken), e => e.Definition.Id == ExpensesCashReport.Id);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => s.ExportAsync(Rig.C(7), ExpensesCashReport.Id, "csv", Rig.NoParams, TestContext.Current.CancellationToken))).Code);
        Assert.Contains(await s.CatalogueAsync(Rig.C(2), TestContext.Current.CancellationToken), e => e.Definition.Id == ExpensesCashReport.Id && e.Money);
    }
}
