// FX-RPT-D · rpt.stock.movements on a disposable HccTest_* database: moves in the business-date range newest first, hotel-local (IST) time, location labels, supplier / "used" kept,
// item filter (trimmed, case-insensitive), actor names, rule V through the real access policy (no money column), supervisor reaches it at the hub.
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
public sealed class StockMovementsReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
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
                DECLARE @o INT = {db.Users["OWN"]}, @m INT = {db.Users["MGR"]}, @h INT = {db.Users["HKP"]};
                INSERT stk.move(property_id, business_date, at_utc, kind, item_id, qty, from_loc, to_loc, ref, actor_id) VALUES
                  (@p, '2026-10-20', '2026-10-20T04:00:00', 'grn', {Item("A-SOAP")}, 100, N'Morning Supplies', 'store', 'GRN-1', @o),
                  (@p, '2026-10-21', '2026-10-21T05:00:00', 'issue', {Item("L-BED")}, 10, 'store', 'floor1', NULL, @m),
                  (@p, '2026-10-21', '2026-10-21T05:00:00', 'use', {Item("A-SOAP")}, 3, 'floor2', 'used', '203', @h),
                  (@p, '2026-10-10', '2026-10-10T05:00:00', 'issue', {Item("L-BED")}, 7, 'store', 'floor1', NULL, @m);
                SELECT 1;
                """);
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ReportRunner Runner() => CuBRig.Runner(db, new StockMovementsReport(Store(db), Names(db)));

    private static (string, string)[] Range(params (string, string)[] extra) => [("from", "2026-10-20"), ("to", "2026-10-24"), .. extra];

    // 20-24 Oct: use (21 Oct 10:30 IST, later id) before issue (same instant) before goods receipt (20 Oct 09:30 IST); the 10 Oct move is outside.
    [Fact]
    public async Task Moves_come_newest_first_with_local_time_labels_and_names()
    {
        var r = await Run(db, Runner(), "OWN", StockMovementsReport.Id, Range());
        var d = Data(r);
        Assert.Equal(3, r.Rows.Count);
        Assert.Equal(("2026-10-21T10:30", "use", "Soap bar", 3L, "Floor 2 pantry", "used", "203", "HKP"), (S(d[0], "when"), S(d[0], "type"), S(d[0], "item"), L(d[0], "qty"), S(d[0], "from"), S(d[0], "to"), S(d[0], "ref"), S(d[0], "by")));
        Assert.Equal(("issue", "Bedsheet", 10L, "Main store", "Floor 1 pantry", "MGR"), (S(d[1], "type"), S(d[1], "item"), L(d[1], "qty"), S(d[1], "from"), S(d[1], "to"), S(d[1], "by")));
        Assert.Null(d[1].Cells["ref"]);
        Assert.Equal(("2026-10-20T09:30", "grn", 100L, "Morning Supplies", "Main store", "GRN-1", "OWN"), (S(d[2], "when"), S(d[2], "type"), L(d[2], "qty"), S(d[2], "from"), S(d[2], "to"), S(d[2], "ref"), S(d[2], "by")));
    }

    [Fact]
    public async Task The_item_filter_is_trimmed_and_ignores_case_and_the_range_limits_the_rows()
    {
        var soap = Data(await Run(db, Runner(), "OWN", StockMovementsReport.Id, Range(("item", "  SOAP "))));
        Assert.Equal(["use", "grn"], soap.Select(x => S(x, "type")));
        Assert.Empty(Data(await Run(db, Runner(), "OWN", StockMovementsReport.Id, Range(("item", "towel")))));
        var wide = Data(await Run(db, Runner(), "OWN", StockMovementsReport.Id, [("from", "2026-10-01"), ("to", "2026-10-24")]));
        Assert.Equal(4, wide.Count);
        Assert.Equal("2026-10-10T10:30", S(wide[3], "when"));
    }

    [Fact]
    public async Task No_money_column_and_roles_without_the_view_right_are_refused_but_the_supervisor_reaches_it_at_the_hub()
    {
        var runner = Runner();
        Assert.DoesNotContain((await Run(db, runner, "MGR", StockMovementsReport.Id, Range())).Columns, c => c.Money);
        foreach (var role in new[] { "REC", "CSH", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(db, runner, role, StockMovementsReport.Id, Range())));
        var svc = Service(db, new StockMovementsReport(Store(db), Names(db)));
        var p = new Dictionary<string, string?> { ["from"] = "2026-10-20", ["to"] = "2026-10-24" };
        var ok = await svc.RunAsync(new Caller(db.PropertyId, db.Users["SUP"]), StockMovementsReport.Id, p, null, 50, true, Ct);
        Assert.Equal(3, ok.Rows.Count);
    }
}
