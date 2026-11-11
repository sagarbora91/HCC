// FX-RPT-D · rpt.stock.par on a disposable HccTest_* database: linen items with a par by room type, pieces per set, required for the configured par sets, held, coverage % (half up,
// 100 when nothing is required), minimum and clean stock; amenities and linen without a par are left out; rule V through the real access policy (no money column).
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
public sealed class StockParReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
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
            // rooms: Classic 101-103, Deluxe 201; a Suite type with no rooms
            await db.ScalarAsync(RoomsSql(db, "101", "102", "103") + "SELECT 1;");
            await db.ScalarAsync($"""
                DECLARE @p INT = {db.PropertyId}, @fl INT = (SELECT MIN(floor_id) FROM mst.floor);
                INSERT inv.room_type(property_id, code, max_occupancy, name) VALUES (@p, 'DLX', 3, N'Deluxe'), (@p, 'STE', 4, N'Suite');
                INSERT inv.room(property_id, room_no, room_type_id, hk_status, floor_id) VALUES (@p, '201', (SELECT room_type_id FROM inv.room_type WHERE code = 'DLX' AND property_id = @p), 'Ready', @fl);
                INSERT stk.setting(property_id, par_sets) VALUES (@p, 2);
                SELECT 1;
                """);
            await db.ScalarAsync(ItemsSql(db) + $"""
                INSERT stk.item(property_id, code, name, kind, unit, cost_paise, min_qty, reorder_qty, created_utc) VALUES
                  (@p, 'L-MAT', N'Mattress cover', 'linen', 'pcs', 30000, 2, 4, '2026-01-01'), (@p, 'L-NAP', N'Napkin', 'linen', 'pcs', 500, 10, 20, '2026-01-01');
                INSERT stk.item_par(item_id, room_type_id, pieces) VALUES
                  ({Item("L-BED")}, (SELECT room_type_id FROM inv.room_type WHERE code = 'CLS' AND property_id = @p), 2), ({Item("L-BED")}, (SELECT room_type_id FROM inv.room_type WHERE code = 'DLX' AND property_id = @p), 3),
                  ({Item("L-TOW")}, (SELECT room_type_id FROM inv.room_type WHERE code = 'CLS' AND property_id = @p), 1),
                  ({Item("L-MAT")}, (SELECT room_type_id FROM inv.room_type WHERE code = 'STE' AND property_id = @p), 1);
                SELECT 1;
                """);
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ReportRunner Runner() => CuBRig.Runner(db, new StockParReport(Store(db)));

    // Par sets 2. Bedsheet: 3 Classic x 2 + 1 Deluxe x 3 = 9 per set, required 18, held 5+4+20+5 = 34, coverage 34 / 18 = 188.9 -> 189, min 20, clean 9.
    // Towel: 3 x 1 = 3 per set, required 6, held 19 + 3 soiled = 22, coverage 366.7 -> 367, min 30, clean 19. Mattress cover: par only for Suites (no rooms) -> 0 / 0 / held 0, coverage 100.
    // Napkin has no par and the amenities are not linen: both left out.
    [Fact]
    public async Task Linen_with_a_par_shows_pieces_per_set_requirement_coverage_and_levels()
    {
        var r = await Run(db, Runner(), "OWN", StockParReport.Id);
        var d = Data(r);
        Assert.Equal(["Bedsheet", "Mattress cover", "Towel"], d.Select(x => S(x, "item")));
        Assert.Equal((9L, 18L, 34L, 189L, 20L, 9L), (L(d[0], "perSet"), L(d[0], "required"), L(d[0], "held"), L(d[0], "coverage"), L(d[0], "min"), L(d[0], "clean")));
        Assert.Equal((0L, 0L, 0L, 100L, 2L, 0L), (L(d[1], "perSet"), L(d[1], "required"), L(d[1], "held"), L(d[1], "coverage"), L(d[1], "min"), L(d[1], "clean")));
        Assert.Equal((3L, 6L, 22L, 367L, 30L, 19L), (L(d[2], "perSet"), L(d[2], "required"), L(d[2], "held"), L(d[2], "coverage"), L(d[2], "min"), L(d[2], "clean")));
        Assert.Equal(3, r.Rows.Count);
    }

    [Fact]
    public async Task The_report_has_no_money_column_and_every_role_with_the_view_right_gets_the_same_rows()
    {
        var runner = Runner();
        foreach (var role in new[] { "OWN", "MGR", "ACC", "SUP" })
        {
            var r = await Run(db, runner, role, StockParReport.Id);
            Assert.DoesNotContain(r.Columns, c => c.Money);
            Assert.Equal(3, Data(r).Count);
            Assert.Null(r.Removed);
        }
    }

    [Fact]
    public async Task Roles_without_the_stock_view_right_are_refused()
    {
        var runner = Runner();
        foreach (var role in new[] { "REC", "CSH", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(db, runner, role, StockParReport.Id)));
    }
}
