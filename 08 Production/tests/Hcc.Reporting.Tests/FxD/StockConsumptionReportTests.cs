// FX-RPT-D · rpt.stock.consumption on a disposable HccTest_* database: use moves (amenities, cleaning) and room-change moves (linen) in the range, occupied room-nights from the imported
// history, per room-night text, standard, variance % and the flag beyond 15 %, the room-night total row, the 93-day limit, rule V through the real access policy (no money column).
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
public sealed class StockConsumptionReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
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
                DECLARE @o INT = {db.Users["OWN"]};
                INSERT stk.occupancy_history(property_id, night, rooms) VALUES
                  (@p, '2026-10-20', 4), (@p, '2026-10-21', 4), (@p, '2026-10-22', 6), (@p, '2026-10-23', 5), (@p, '2026-10-24', 5), (@p, '2026-10-10', 9);
                INSERT stk.move(property_id, business_date, at_utc, kind, item_id, qty, from_loc, to_loc, actor_id) VALUES
                  (@p, '2026-10-21', '2026-10-21T05:00:00', 'use', {Item("A-SOAP")}, 20, 'floor2', 'used', @o),
                  (@p, '2026-10-23', '2026-10-23T05:00:00', 'use', {Item("A-SOAP")}, 16, 'floor2', 'used', @o),
                  (@p, '2026-10-10', '2026-10-10T05:00:00', 'use', {Item("A-SOAP")}, 99, 'floor2', 'used', @o),
                  (@p, '2026-10-22', '2026-10-22T05:00:00', 'use', {Item("C-FLOOR")}, 12, 'store', 'used', @o),
                  (@p, '2026-10-22', '2026-10-22T05:00:00', 'room-change', {Item("L-BED")}, 24, 'floor1', 'rooms', @o),
                  (@p, '2026-10-22', '2026-10-22T06:00:00', 'use', {Item("L-BED")}, 5, 'floor1', 'used', @o);
                SELECT 1;
                """);
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ReportRunner Runner() => CuBRig.Runner(db, new StockConsumptionReport(Store(db)));

    private static (string, string)[] Range(string from, string to) => [("from", from), ("to", to)];

    // Room-nights 20-24 Oct: 4 + 4 + 6 + 5 + 5 = 24 (the 10 Oct history is outside). Soap bar: 20 + 16 = 36 -> 1.50 per night, standard 1.50, variance 0, no flag.
    // Floor cleaner: 12 -> 0.50, standard 1, variance (0.5 - 1) / 1 = -50 %, flagged. Bedsheet (linen): the room-change move 24 -> 1, no standard; its "use" move does not count.
    [Fact]
    public async Task Use_per_room_night_standard_variance_and_flag_follow_the_moves_and_the_history()
    {
        var r = await Run(db, Runner(), "OWN", StockConsumptionReport.Id, Range("2026-10-20", "2026-10-24"));
        var d = Data(r);
        Assert.Equal(["Soap bar", "Zeta wipes", "Floor cleaner", "Bedsheet", "Towel"], d.Select(x => S(x, "item")));
        Assert.Equal(("Amenities", 36L, "1.50", "1.50", 0L), (S(d[0], "kind"), L(d[0], "used"), S(d[0], "perRN"), S(d[0], "std"), L(d[0], "variance")));
        Assert.Null(d[0].Cells["flag"]);
        Assert.Equal((0L, "0"), (L(d[1], "used"), S(d[1], "perRN")));
        Assert.All(new[] { "std", "variance", "flag" }, k => Assert.Null(d[1].Cells[k]));
        Assert.Equal(("Cleaning supplies", 12L, "0.50", "1", -50L, "Outside ±15%"), (S(d[2], "kind"), L(d[2], "used"), S(d[2], "perRN"), S(d[2], "std"), L(d[2], "variance"), S(d[2], "flag")));
        Assert.Equal(("Linen", 24L, "1"), (S(d[3], "kind"), L(d[3], "used"), S(d[3], "perRN")));
        Assert.Null(d[3].Cells["std"]);
        var total = r.Rows.Single(x => x.Kind == ReportRowKind.Total);
        Assert.Equal("Occupied room-nights 24", S(total, "item"));
    }

    [Fact]
    public async Task A_range_beyond_93_days_is_refused()
    {
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(db, Runner(), "OWN", StockConsumptionReport.Id, Range("2026-07-01", "2026-10-24"))));
    }

    [Fact]
    public async Task A_day_with_no_history_counts_no_room_nights_and_the_view_role_sees_the_report()
    {
        // 15-16 Oct: no history, no stays -> 0 room-nights: per room-night "0", variance blank (no nights), total row says 0
        var sup = await Run(db, Runner(), "SUP", StockConsumptionReport.Id, Range("2026-10-15", "2026-10-16"));
        Assert.Equal("Occupied room-nights 0", S(sup.Rows.Single(x => x.Kind == ReportRowKind.Total), "item"));
        Assert.All(Data(sup), x => Assert.Null(x.Cells["variance"]));
        Assert.DoesNotContain(sup.Columns, c => c.Money);
    }

    [Fact]
    public async Task Roles_without_the_stock_view_right_are_refused_and_the_supervisor_reaches_it_at_the_hub()
    {
        var runner = Runner();
        foreach (var role in new[] { "REC", "CSH", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(db, runner, role, StockConsumptionReport.Id, Range("2026-10-20", "2026-10-24"))));
        var svc = Service(db, new StockConsumptionReport(Store(db)));
        var p = new Dictionary<string, string?> { ["from"] = "2026-10-20", ["to"] = "2026-10-24" };
        var ok = await svc.RunAsync(new Caller(db.PropertyId, db.Users["SUP"]), StockConsumptionReport.Id, p, null, 50, true, Ct);
        Assert.Equal(6, ok.Rows.Count);
    }
}
