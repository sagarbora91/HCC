// CU-RPT-B · rpt.rest.day-summary on a disposable HccTest_* database: outlet block, settlement block (POS methods of the master, room-folio postings, an unlisted
// tender), top items, open checks and voids; totals to the paise; the figures equal IPosStore.DaySummaryAsync; rule V through the real access policy
// (PERM-POS-004: Owner / Manager / Accountant; the Cashier holds only the own-shift cell, Reception and Housekeeping nothing).
using Hcc.Contracts;
using Hcc.Application.Commands;
using Hcc.Application.Pos;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Domain.Tax;
using Hcc.Infrastructure.SqlServer.Pos;
using Hcc.Reporting.Reports.CuB;
using Hcc.Reporting.Tests.Sql;
using static Hcc.Reporting.Tests.CuB.CuBRig;

namespace Hcc.Reporting.Tests.CuB;

[Trait("Category", "Sql")]
public sealed class RestDaySummaryReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;

    private sealed class FiveRates : IPosTaxRates
    {
        public Task<GstRate> FoodAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct = default) => Task.FromResult(new GstRate(500, true, GstCategory.Food, "test 5 %"));
    }

    public async ValueTask InitializeAsync()
    {
        await Gate.WaitAsync(Ct);
        try
        {
            if (_seeded) return;
            await SeedMethods(db);
            await Seed();
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Business date 24 Oct; the day under test is 23 Oct. Items: Paneer 250.00, Tea 50.00. Restaurant (R1) settled: o1 UPI 2 Paneer + 2 Tea (net 600, tax 30, 630),
    // o2 Card 1 + 1 (net 300, tax 15, 315), o4 "Wallet" 4 Tea (net 200, tax 10, 210). Room service (R2) o3 posted to a room folio 2 Tea (net 100, tax 5, 105).
    // Open at R1: o5 with 1 Paneer (250 + 12.50 tax at 5 % = 262.50) and one voided Tea line (50.00, void date 23 Oct).
    private async Task Seed()
    {
        var own = db.Users["OWN"];
        await db.ScalarAsync(RoomsSql(db, "101") + $"""
            DECLARE @r101 INT = (SELECT room_id FROM inv.room WHERE room_no = '101');
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, children, source) VALUES
              (@p, 'HCC-4001', N'Ananya Rao', '2026-10-22', '2026-10-26', 'InHouse', @r101, @t, 1, 0, 'Direct');
            INSERT bill.folio(reservation_id) SELECT reservation_id FROM inv.reservation WHERE public_no = 'HCC-4001';
            DECLARE @f BIGINT = (SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-4001');
            INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES (@f, 'service', '2026-10-23', N'Restaurant bill', 10000, 500, 500);
            DECLARE @ch BIGINT = SCOPE_IDENTITY();
            INSERT pos.outlet(property_id, code, name, kind, outlet_type, created_utc) VALUES (@p, 'R1', N'Restaurant', 'table', 'restaurant', '2026-01-01'), (@p, 'R2', N'Room service', 'room', 'room-service', '2026-01-01');
            DECLARE @o1 INT = (SELECT outlet_id FROM pos.outlet WHERE code = 'R1'), @o2 INT = (SELECT outlet_id FROM pos.outlet WHERE code = 'R2');
            INSERT pos.menu_item(property_id, code, name, category, price_paise, veg) VALUES (@p, 'PAN', N'Paneer', N'Mains', 25000, 1), (@p, 'TEA', N'Tea', N'Drinks', 5000, 1);
            DECLARE @pan INT = (SELECT item_id FROM pos.menu_item WHERE code = 'PAN'), @tea INT = (SELECT item_id FROM pos.menu_item WHERE code = 'TEA');
            INSERT pos.check_order(property_id, outlet_id, table_no, room_id, guest, covers, business_date, opened_utc, opened_by, status) VALUES
              (@p, @o1, 'T1', NULL, NULL, 2, '2026-10-23', '2026-10-23T12:00:00', {own}, 'Settled'), (@p, @o1, 'T2', NULL, NULL, 3, '2026-10-23', '2026-10-23T13:00:00', {own}, 'Settled'),
              (@p, @o2, NULL, @r101, NULL, 1, '2026-10-23', '2026-10-23T14:00:00', {own}, 'Settled'), (@p, @o1, 'T4', NULL, NULL, 2, '2026-10-23', '2026-10-23T15:00:00', {own}, 'Settled'),
              (@p, @o1, 'T5', NULL, NULL, 2, '2026-10-24', '2026-10-24T09:00:00', {own}, 'Open');
            DECLARE @c1 BIGINT = (SELECT order_id FROM pos.check_order WHERE table_no = 'T1'), @c2 BIGINT = (SELECT order_id FROM pos.check_order WHERE table_no = 'T2'),
                    @c3 BIGINT = (SELECT order_id FROM pos.check_order WHERE room_id = @r101), @c4 BIGINT = (SELECT order_id FROM pos.check_order WHERE table_no = 'T4'),
                    @c5 BIGINT = (SELECT order_id FROM pos.check_order WHERE table_no = 'T5');
            INSERT pos.order_line(order_id, item_id, name, price_paise, qty) VALUES
              (@c1, @pan, N'Paneer', 25000, 2), (@c1, @tea, N'Tea', 5000, 2), (@c2, @pan, N'Paneer', 25000, 1), (@c2, @tea, N'Tea', 5000, 1),
              (@c3, @tea, N'Tea', 5000, 2), (@c4, @tea, N'Tea', 5000, 4), (@c5, @pan, N'Paneer', 25000, 1);
            INSERT pos.order_line(order_id, item_id, name, price_paise, qty, voided, void_reason, void_by, void_date) VALUES (@c5, @tea, N'Tea', 5000, 1, 1, N'Wrong item', {own}, '2026-10-23');
            INSERT pos.bill(order_id, property_id, bill_no, mode, tender, net_paise, tax_bps, tax_paise, total_paise, business_date, settled_utc, settled_by, folio_id, charge_id, status) VALUES
              (@c1, @p, 'POS/2610/0001', 'outlet', N'UPI', 60000, 500, 3000, 63000, '2026-10-23', '2026-10-23T13:00:00', {own}, NULL, NULL, 'Settled'),
              (@c2, @p, 'POS/2610/0002', 'outlet', N'Card', 30000, 500, 1500, 31500, '2026-10-23', '2026-10-23T14:00:00', {own}, NULL, NULL, 'Settled'),
              (@c3, @p, 'POS/2610/0003', 'folio', NULL, 10000, 500, 500, 10500, '2026-10-23', '2026-10-23T15:00:00', {own}, @f, @ch, 'Settled'),
              (@c4, @p, 'POS/2610/0004', 'outlet', N'Wallet', 20000, 500, 1000, 21000, '2026-10-23', '2026-10-23T16:00:00', {own}, NULL, NULL, 'Settled');
            -- a settled bill that was voided later (status Void): out of every figure
            INSERT pos.check_order(property_id, outlet_id, table_no, room_id, guest, covers, business_date, opened_utc, opened_by, status, void_reason, voided_by, voided_utc) VALUES
              (@p, @o1, 'T6', NULL, NULL, 2, '2026-10-23', '2026-10-23T17:00:00', {own}, 'Void', N'Guest left', {own}, '2026-10-23T18:00:00');
            DECLARE @c6 BIGINT = (SELECT order_id FROM pos.check_order WHERE table_no = 'T6');
            INSERT pos.order_line(order_id, item_id, name, price_paise, qty) VALUES (@c6, @pan, N'Paneer', 25000, 4);
            INSERT pos.bill(order_id, property_id, bill_no, mode, tender, net_paise, tax_bps, tax_paise, total_paise, business_date, settled_utc, settled_by, folio_id, charge_id, status, void_reason, voided_by, voided_utc) VALUES
              (@c6, @p, 'POS/2610/0005', 'outlet', N'UPI', 100000, 500, 5000, 105000, '2026-10-23', '2026-10-23T17:30:00', {own}, NULL, NULL, 'Void', N'Guest left', {own}, '2026-10-23T18:00:00');
            SELECT 1;
            """);
    }

    private ReportRunner Runner() => CuBRig.Runner(db, new RestDaySummaryReport(new SqlPosStore(db.Platform.Connections), new FiveRates(), db.Platform));

    [Fact]
    public async Task Outlets_settlement_top_items_and_open_checks_are_exact_to_the_paise()
    {
        var r = await Run(db, Runner(), "OWN", RestDaySummaryReport.Id, ("from", "2026-10-23"));
        var t = ReportRunner.Tables(r);
        Assert.Equal(["outlets", "settlement", "top", "open"], t.Select(x => x.Key));
        var o = Data(t[0]);
        Assert.Equal(("Restaurant", 3L, 110000L, 5500L, 115500L, 1L), (S(o[0], "outlet"), L(o[0], "bills"), L(o[0], "net"), L(o[0], "tax"), L(o[0], "total"), L(o[0], "open")));
        Assert.Equal(("Room service", 1L, 10000L, 500L, 10500L, 0L), (S(o[1], "outlet"), L(o[1], "bills"), L(o[1], "net"), L(o[1], "tax"), L(o[1], "total"), L(o[1], "open")));
        var all = t[0].Rows.Single(x => x.Kind == ReportRowKind.Total);
        Assert.Equal(("All outlets", 4L, 120000L, 6000L, 126000L, 1L), (S(all, "outlet"), L(all, "bills"), L(all, "net"), L(all, "tax"), L(all, "total"), L(all, "open")));
        Assert.Equal(L(all, "net") + L(all, "tax"), L(all, "total"));
        // settlement: master POS methods in order (Cash has no bill), room folio, then the unlisted tender
        Assert.Equal([("Cash", 0L), ("UPI", 63000L), ("Card", 31500L), ("Posted to room folios", 10500L), ("Wallet", 21000L)], t[1].Rows.Select(x => (S(x, "settlement"), L(x, "amount"))));
        Assert.Equal(L(all, "total"), t[1].Rows.Sum(x => L(x, "amount")));
        // top items: most pieces first
        Assert.Equal([("Tea", 9L, 45000L), ("Paneer", 3L, 75000L)], t[2].Rows.Select(x => (S(x, "item"), L(x, "qty"), L(x, "value"))));
        Assert.Equal([("Open checks now", 1L, 26250L), ("Voided lines today", 1L, 5000L)], t[3].Rows.Select(x => (S(x, "open"), L(x, "count"), L(x, "value"))));
    }

    [Fact]
    public async Task Figures_equal_the_pos_day_summary_query()
    {
        var store = new SqlPosStore(db.Platform.Connections);
        var d = await store.DaySummaryAsync(db.PropertyId, new DateOnly(2026, 10, 23), 500, Ct);
        var r = await Run(db, Runner(), "MGR", RestDaySummaryReport.Id, ("from", "2026-10-23"));
        var all = ReportRunner.Tables(r)[0].Rows.Single(x => x.Kind == ReportRowKind.Total);
        Assert.Equal((d.Checks, d.NetPaise, d.TaxPaise, d.GrossPaise, d.OpenCount), ((int)L(all, "bills"), L(all, "net"), L(all, "tax"), L(all, "total"), (int)L(all, "open")));
        Assert.Equal(d.Top.Select(x => (x.Name, (long)x.Qty, x.ValuePaise)), ReportRunner.Tables(r)[2].Rows.Select(x => (S(x, "item"), L(x, "qty"), L(x, "value"))));
    }

    [Fact]
    public async Task A_day_without_sales_shows_the_master_methods_at_zero()
    {
        var r = await Run(db, Runner(), "ACC", RestDaySummaryReport.Id, ("from", "2026-10-22"));
        var t = ReportRunner.Tables(r);
        Assert.Equal((0L, 0L), (L(t[0].Rows.Single(x => x.Kind == ReportRowKind.Total), "bills"), L(t[0].Rows.Single(x => x.Kind == ReportRowKind.Total), "total")));
        Assert.All(t[1].Rows, x => Assert.Equal(0L, L(x, "amount")));
        Assert.Empty(t[2].Rows);
    }

    [Fact]
    public async Task Owner_manager_accountant_may_run_it_everyone_else_is_refused()
    {
        var runner = Runner();
        foreach (var role in new[] { "REC", "CSH", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(db, runner, role, RestDaySummaryReport.Id, ("from", "2026-10-23"))));
        foreach (var role in new[] { "OWN", "MGR", "ACC" })
            Assert.Null(await Code(() => Run(db, runner, role, RestDaySummaryReport.Id, ("from", "2026-10-23"))));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(db, runner, "OWN", RestDaySummaryReport.Id, ("from", "2026-10-22"), ("to", "2026-10-23")))); // one business date only
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(db, runner, "OWN", RestDaySummaryReport.Id, ("from", "2026-10-25"))));
    }
}
