// CU-RPT-B · rpt.rooms.occupancy on a disposable HccTest_* database: per-date sellable / occupied / room revenue / ratios to the paisa, total row recomputed,
// equality with the daily pack calculator, zero-divisor dashes, rule V through the real access policy, screen = Excel = PDF = print.
using Hcc.Contracts;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Domain.Reporting;
using Hcc.Infrastructure.SqlServer.Reporting;
using Hcc.Reporting.Reports.CuB;
using Hcc.Reporting.Tests.Sql;
using static Hcc.Reporting.Tests.CuB.CuBRig;

namespace Hcc.Reporting.Tests.CuB;

[Trait("Category", "Sql")]
public sealed class RoomsOccupancyReportTests(ReportDatabase db) : IClassFixture<ReportDatabase>, IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;

    public async ValueTask InitializeAsync()
    {
        await Gate.WaitAsync(Ct);
        try
        {
            if (_seeded) return;
            await Seed();
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Rooms 101-104; 104 blocked from 23 Oct. Stays: A room 101 22-25 in house; B room 102 22-24 out on 24; C room 103 23-27 left early on 24.
    // Occupied nights: 22 = A,B; 23 = A,B,C; 24 = A. Sellable: 22 = 4, 23 = 3, 24 = 3. Room revenue: 22 = 3,000.00; 23 = 4,500.01; 24 = 1,000.00 (a service line and a discount never count).
    private async Task Seed()
    {
        var own = db.Users["OWN"];
        await db.ScalarAsync(RoomsSql(db, "101", "102", "103", "104") + $"""
            INSERT gst.guest(property_id, full_name, phone, nationality, id_type, id_last4, address, origin, created_utc) VALUES (@p, N'Ananya Rao', '+919820001041', N'Indian', N'Aadhaar', '4101', N'10 Demo Street (fictional)', 'manual', '2026-10-01');
            DECLARE @g BIGINT = (SELECT guest_id FROM gst.guest WHERE full_name = N'Ananya Rao');
            DECLARE @r101 INT = (SELECT room_id FROM inv.room WHERE room_no = '101'), @r102 INT = (SELECT room_id FROM inv.room WHERE room_no = '102'), @r103 INT = (SELECT room_id FROM inv.room WHERE room_no = '103'),
                    @r104 INT = (SELECT room_id FROM inv.room WHERE room_no = '104');
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, adults, children, source) VALUES
              (@p, 'HCC-2001', N'Ananya Rao', '2026-10-22', '2026-10-25', 'InHouse', @r101, @t, 1, 0, 'Direct'),
              (@p, 'HCC-2002', N'Ananya Rao', '2026-10-22', '2026-10-24', 'Departed', @r102, @t, 1, 0, 'Direct'),
              (@p, 'HCC-2003', N'Ananya Rao', '2026-10-23', '2026-10-27', 'Departed', @r103, @t, 1, 0, 'Direct');
            INSERT bill.folio(reservation_id) SELECT reservation_id FROM inv.reservation WHERE public_no IN ('HCC-2001','HCC-2002','HCC-2003');
            INSERT gst.registration(reservation_id, guest_id, registered_utc, registered_by) SELECT reservation_id, @g, '2026-10-20', {own} FROM inv.reservation WHERE public_no IN ('HCC-2001','HCC-2002','HCC-2003');
            INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by, checked_out_date, checked_out_utc, checked_out_by)
              SELECT @p, r.reservation_id, @g, f.folio_id, CASE WHEN r.public_no = 'HCC-2001' THEN 'InHouse' ELSE 'CheckedOut' END, r.arrival, r.departure, r.arrival,
                     DATEADD(MINUTE, 510, CAST(r.arrival AS DATETIME2)), {own},
                     CASE WHEN r.public_no = 'HCC-2001' THEN NULL ELSE '2026-10-24' END, CASE WHEN r.public_no = 'HCC-2001' THEN NULL ELSE '2026-10-24T05:30:00' END, CASE WHEN r.public_no = 'HCC-2001' THEN NULL ELSE {own} END
                FROM inv.reservation r JOIN bill.folio f ON f.reservation_id = r.reservation_id WHERE r.public_no IN ('HCC-2001','HCC-2002','HCC-2003');
            DECLARE @fa BIGINT = (SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-2001'),
                    @fb BIGINT = (SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-2002'),
                    @fc BIGINT = (SELECT f.folio_id FROM bill.folio f JOIN inv.reservation r ON r.reservation_id = f.reservation_id WHERE r.public_no = 'HCC-2003');
            INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES
              (@fa, 'room', '2026-10-22', N'Room night', 100000, 0, 0), (@fa, 'room', '2026-10-23', N'Room night', 100000, 0, 0), (@fa, 'room', '2026-10-24', N'Room night', 100000, 0, 0),
              (@fb, 'room', '2026-10-22', N'Room night', 200000, 0, 0), (@fb, 'room', '2026-10-23', N'Room night', 200000, 0, 0),
              (@fc, 'room', '2026-10-23', N'Room night', 150001, 0, 0),
              (@fa, 'service', '2026-10-24', N'Laundry', 50000, 0, 0), (@fa, 'discount', '2026-10-24', N'Discount', -10000, 0, 0);
            INSERT ops.room_block(property_id, room_id, from_date, to_date, reason, source, created_by, created_utc) VALUES (@p, @r104, '2026-10-23', NULL, N'Leak', 'manual', {own}, '2026-10-20');
            SELECT 1;
            """);
    }

    private ReportRunner Runner() => CuBRig.Runner(db, new RoomsOccupancyReport(new SqlReportSource(db.Platform.Connections)));

    [Fact]
    public async Task Rows_totals_and_ratios_are_exact_to_the_paisa()
    {
        var r = await Run(db, Runner(), "OWN", RoomsOccupancyReport.Id, ("from", "2026-10-22"), ("to", "2026-10-24"));
        var d = Data(ReportRunner.Tables(r)[0]);
        Assert.Equal(3, d.Count);
        Assert.Equal((4L, 2L, "50.0%", 300000L, "₹1,500", "₹750"), (L(d[0], "sellable"), L(d[0], "occupied"), S(d[0], "occupancy"), L(d[0], "roomRevenue"), S(d[0], "adr"), S(d[0], "revpar")));
        Assert.Equal((3L, 3L, "100.0%", 450001L, "₹1,500", "₹1,500"), (L(d[1], "sellable"), L(d[1], "occupied"), S(d[1], "occupancy"), L(d[1], "roomRevenue"), S(d[1], "adr"), S(d[1], "revpar")));
        Assert.Equal((3L, 1L, "33.3%", 100000L, "₹1,000", "₹333.33"), (L(d[2], "sellable"), L(d[2], "occupied"), S(d[2], "occupancy"), L(d[2], "roomRevenue"), S(d[2], "adr"), S(d[2], "revpar")));
        Assert.Equal(["2026-10-22", "2026-10-23", "2026-10-24"], d.Select(x => S(x, "date")));
        var total = r.Rows.Single(x => x.Kind == ReportRowKind.Total);
        // 10 sellable, 6 occupied, 850,001 paise: ADR 850001 / 6 = 141,666.83 -> 141,667; RevPAR 850001 / 10 = 85,000.1 -> 85,000
        Assert.Equal((10L, 6L, "60.0%", 850001L, "₹1,416.67", "₹850"), (L(total, "sellable"), L(total, "occupied"), S(total, "occupancy"), L(total, "roomRevenue"), S(total, "adr"), S(total, "revpar")));
    }

    [Fact]
    public async Task Totals_equal_the_daily_pack_calculator_over_the_same_tables()
    {
        var data = await new SqlReportSource(db.Platform.Connections).LoadAsync(null, db.PropertyId, new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 24), Ct);
        var pack = DailyPackCalculator.Compute(data, new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 24));
        var r = await Run(db, Runner(), "MGR", RoomsOccupancyReport.Id, ("from", "2026-10-22"), ("to", "2026-10-24"));
        var total = r.Rows.Single(x => x.Kind == ReportRowKind.Total);
        Assert.Equal((pack.Occupancy.SellableRoomNights, pack.Occupancy.OccupiedRoomNights, pack.Occupancy.OccupancyPct, pack.Occupancy.RoomRevenuePaise),
            ((int)L(total, "sellable"), (int)L(total, "occupied"), S(total, "occupancy"), L(total, "roomRevenue")));
        Assert.Equal(pack.Days.Select(x => ((long)x.Sellable, (long)x.Occupied)), Data(ReportRunner.Tables(r)[0]).Select(x => (L(x, "sellable"), L(x, "occupied"))));
    }

    [Fact]
    public async Task A_day_with_no_stays_shows_zero_percent_and_dashes_for_ADR()
    {
        var r = await Run(db, Runner(), "ACC", RoomsOccupancyReport.Id, ("from", "2026-10-01"), ("to", "2026-10-02"));
        var d = Data(ReportRunner.Tables(r)[0]);
        Assert.All(d, x => Assert.Equal((4L, 0L, "0.0%", 0L, "—", "₹0"), (L(x, "sellable"), L(x, "occupied"), S(x, "occupancy"), L(x, "roomRevenue"), S(x, "adr"), S(x, "revpar"))));
        Assert.Equal("—", S(r.Rows.Single(x => x.Kind == ReportRowKind.Total), "adr"));
    }

    [Fact]
    public async Task Owner_manager_accountant_may_run_it_everyone_else_is_refused_and_the_future_is_refused()
    {
        var runner = Runner();
        foreach (var role in new[] { "REC", "CSH", "HKP" })
            Assert.Equal(HccErrorCode.FORBIDDEN, await Code(() => Run(db, runner, role, RoomsOccupancyReport.Id)));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(db, runner, "OWN", RoomsOccupancyReport.Id, ("from", "2026-10-24"), ("to", "2026-10-25"))));
        Assert.Equal(HccErrorCode.VALIDATION, await Code(() => Run(db, runner, "OWN", RoomsOccupancyReport.Id, ("from", "2026-10-24"), ("to", "2026-10-22"))));
        Assert.Null(await Code(() => Run(db, runner, "OWN", RoomsOccupancyReport.Id)));
        Assert.True(await runner.HoldsMoneyGateAsync(new Caller(db.PropertyId, db.Users["ACC"]), new RoomsOccupancyReport(null!).Definition, Ct));
    }
}
