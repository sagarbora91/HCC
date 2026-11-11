// CU-RPT-A · rpt.money.cash-book on the server: shifts, drawer movements, closed and locked days seeded on a disposable HccTest_* database; day rows, range total and the entry
// block with its running balance to the paise, refusals (Cashier own-shift grant, Reception, Housekeeping), screen = Excel = PDF = print.
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Reporting.Reports.CuA;
using Hcc.Reporting.Tests.Sql;

namespace Hcc.Reporting.Tests.CuA;

[Trait("Category", "Sql")]
public sealed class CashBookReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private readonly FakePolicy _policy = new();

    public ValueTask InitializeAsync() => new(CuARig.SeedCash(db));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Day_rows_and_range_total_foot_to_the_paise()
    {
        var r = await CuARig.Run(db, _policy, db.Users["ACC"], CashBookReport.Id, ("from", "2026-10-22"), ("to", "2026-10-24"));
        var d = CuARig.Data(r);
        Assert.Equal(["2026-10-22", "2026-10-23", "2026-10-24"], d.Select(x => CuARig.S(x, "date")));
        Assert.Equal(["Closed · locked", "Closed", "Open"], d.Select(x => CuARig.S(x, "status")));
        Assert.Equal([1000000L, 1395000, 1245000], d.Select(x => CuARig.L(x, "opening")));
        Assert.Equal([500000L, 200000, 0], d.Select(x => CuARig.L(x, "receipts")));
        Assert.Equal([100000L, 350000, 0], d.Select(x => CuARig.L(x, "payments")));      // 23 Oct: refund 500 + banking 3,000
        Assert.Equal([1400000L, 1245000, 1245000], d.Select(x => CuARig.L(x, "closing")));
        Assert.Equal(1395000L, CuARig.L(d[0], "counted"));
        Assert.Equal(1245000L, CuARig.L(d[1], "counted"));
        Assert.Null(d[2].Cells["counted"]);
        Assert.Equal([-5000L, 0], d.Take(2).Select(x => CuARig.L(x, "overShort")));
        Assert.Null(d[2].Cells["overShort"]);
        var t = Assert.Single(r.Rows, x => x.Kind == ReportRowKind.Total);
        Assert.Equal(("Range total", 1000000L, 700000L, 450000L, 1245000L, -5000L),
            (CuARig.S(t, "date"), CuARig.L(t, "opening"), CuARig.L(t, "receipts"), CuARig.L(t, "payments"), CuARig.L(t, "closing"), CuARig.L(t, "overShort")));
        // opening + receipts - payments + differences = closing (carried forward)
        Assert.Equal(CuARig.L(t, "closing"), CuARig.L(t, "opening") + CuARig.L(t, "receipts") - CuARig.L(t, "payments") + CuARig.L(t, "overShort"));
    }

    [Fact]
    public async Task Entry_block_has_an_opening_line_per_day_and_a_running_balance_that_ends_at_the_book_closing()
    {
        var r = await CuARig.Run(db, _policy, db.Users["OWN"], CashBookReport.Id, ("from", "2026-10-22"), ("to", "2026-10-23"));
        var entries = Assert.Single(Hcc.Reporting.ReportRunner.Tables(r), t => t.Key == "entries").Rows;
        Assert.Equal(["Opening balance", "Payment / advance", "Expense", "Opening balance", "Payment / advance", "Payment refund", "Banked after count"], entries.Select(x => CuARig.S(x, "entry")));
        Assert.Equal([1000000L, 1500000, 1400000, 1395000, 1595000, 1545000, 1245000], entries.Select(x => CuARig.L(x, "balance")));
        Assert.Equal(["RC-000001", "RC-000002"], entries.Where(x => CuARig.S(x, "detail") is "RC-000001" or "RC-000002").Select(x => CuARig.S(x, "detail")));
        Assert.Equal("MGR", CuARig.S(entries[^1], "by"));
        Assert.Equal(300000L, CuARig.L(entries[^1], "out"));
        Assert.Null(entries[^1].Cells["in"]);
    }

    [Fact]
    public async Task A_range_with_no_cash_activity_has_no_total_row()
    {
        var r = await CuARig.Run(db, _policy, db.Users["OWN"], CashBookReport.Id, ("from", "2026-10-01"), ("to", "2026-10-10"));
        Assert.Empty(r.Rows);
    }

    [Fact]
    public async Task Ranges_over_62_days_cashier_reception_and_housekeeping_are_refused()
    {
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => CuARig.Run(db, _policy, db.Users["OWN"], CashBookReport.Id, ("from", "2026-07-01"), ("to", "2026-10-24")))).Code);
        foreach (var who in new[] { db.Users["CSH"], db.Users["REC"], 7 })
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => CuARig.Run(db, _policy, who, CashBookReport.Id, ("from", "2026-10-22"), ("to", "2026-10-22")))).Code);
    }

    [Fact]
    public async Task Money_columns_never_reach_a_role_without_the_money_gate()
    {
        _policy.Denied.Add((db.Users["MGR"], "PERM-RPT-002"));
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => CuARig.Run(db, _policy, db.Users["MGR"], CashBookReport.Id, ("from", "2026-10-22"), ("to", "2026-10-22")))).Code);
        foreach (var perm in new[] { "PERM-RPT-001", "PERM-RPT-002", "PERM-RPT-003" }) _policy.Grants[perm].Add("HKP");
        var s = CuARig.Service(db, _policy);
        Assert.DoesNotContain(await s.CatalogueAsync(Rig.C(7), TestContext.Current.CancellationToken), e => e.Definition.Id == CashBookReport.Id);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => s.RunAsync(Rig.C(7), CashBookReport.Id, Rig.NoParams, null, 100, TestContext.Current.CancellationToken))).Code);
    }
}
